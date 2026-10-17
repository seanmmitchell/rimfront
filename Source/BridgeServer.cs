using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RimFront
{
    // Network threads only exchange immutable JSON and commands. They never touch Verse objects.
    public static class BridgeServer
    {
        private static readonly ConcurrentQueue<InputCommand> Commands = new ConcurrentQueue<InputCommand>();
        private static readonly SemaphoreSlim Slots = new SemaphoreSlim(4, 4);
        private static readonly object Lifecycle = new object();
        private static TcpListener listener;
        private static Thread worker;
        private static string token;
        private static string authority;
        private static int generation;
        private static long lastInputTicks;
        private static byte[] snapshot = WireJson.Encode(new WorldSnapshot());
        private static Dictionary<string, Asset> assets;
        public static string Url { get; private set; }
        public static DateTime LastInputUtc { get { return new DateTime(Interlocked.Read(ref lastInputTicks), DateTimeKind.Utc); } }
        public static bool Running { get { return listener != null; } }

        public static void Start(string clientDirectory)
        {
            lock (Lifecycle)
            {
                if (listener != null) return;
                var loaded = new Dictionary<string, Asset>(StringComparer.Ordinal);
                string root = Path.GetFullPath(clientDirectory);
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string mime = ext == ".html" ? "text/html; charset=utf-8" : ext == ".js" || ext == ".mjs" ? "text/javascript; charset=utf-8" : ext == ".css" ? "text/css; charset=utf-8" : ext == ".svg" ? "image/svg+xml" : null;
                    if (mime == null || new FileInfo(file).Length > 2097152) continue;
                    string key = "/" + file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                    loaded[key] = new Asset { Bytes = File.ReadAllBytes(file), Mime = mime };
                }
                if (!loaded.ContainsKey("/index.html")) throw new FileNotFoundException("RimFront Client/index.html is missing. Rebuild the mod package.");
                loaded["/"] = loaded["/index.html"];
                TcpListener created = null;
                for (int port = 18741; port <= 18750; port++)
                {
                    var candidate = new TcpListener(IPAddress.Loopback, port);
                    candidate.Server.ExclusiveAddressUse = true;
                    try { candidate.Start(8); created = candidate; authority = "127.0.0.1:" + port; break; }
                    catch (SocketException) { candidate.Stop(); }
                }
                if (created == null) throw new IOException("RimFront loopback ports 18741-18750 are occupied.");
                var secret = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
                token = BitConverter.ToString(secret).Replace("-", "").ToLowerInvariant();
                assets = loaded;
                Url = "http://" + authority + "/#token=" + token;
                Interlocked.Exchange(ref lastInputTicks, DateTime.UtcNow.Ticks);
                listener = created;
                int currentGeneration = ++generation;
                worker = new Thread(() => Accept(created, currentGeneration)) { IsBackground = true, Name = "RimFront loopback" };
                worker.Start();
            }
        }

        public static void Stop()
        {
            lock (Lifecycle)
            {
                ++generation;
                var old = listener;
                listener = null;
                if (old != null) old.Stop();
                InputCommand ignored;
                while (Commands.TryDequeue(out ignored)) { }
                snapshot = WireJson.Encode(new WorldSnapshot());
                Url = null;
            }
        }

        public static bool TryDequeue(out InputCommand command) { return Commands.TryDequeue(out command); }
        public static void Publish(WorldSnapshot state) { Interlocked.Exchange(ref snapshot, WireJson.Encode(state)); }

        private static void Accept(TcpListener server, int epoch)
        {
            while (epoch == Volatile.Read(ref generation))
            {
                TcpClient client;
                try { client = server.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                if (!Slots.Wait(0)) { client.Close(); continue; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(client, epoch); }
                    catch (Exception) { /* Malformed or disconnected peers cannot affect the game thread. */ }
                    finally { CloseGracefully(client); Slots.Release(); }
                });
            }
        }

        private static void Handle(TcpClient client, int epoch)
        {
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 2000;
            NetworkStream stream = client.GetStream();
            {
                var header = new List<byte>(512);
                int end = 0;
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                while (header.Count < 8192 && end != 4)
                {
                    if (DateTime.UtcNow > deadline) return;
                    int value = stream.ReadByte();
                    if (value < 0) return;
                    header.Add((byte)value);
                    end = (end == 0 || end == 2) && value == 13 ? end + 1 : (end == 1 || end == 3) && value == 10 ? end + 1 : 0;
                }
                if (end != 4) { Reply(stream, 431, "Headers too large"); return; }
                string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                if (request.Length != 3 || request[2] != "HTTP/1.1") { Reply(stream, 400, "Invalid request"); return; }
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length && lines[i].Length > 0; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon < 1) { Reply(stream, 400, "Invalid header"); return; }
                    string key = lines[i].Substring(0, colon).Trim();
                    if (fields.ContainsKey(key)) { Reply(stream, 400, "Duplicate header"); return; }
                    fields[key] = lines[i].Substring(colon + 1).Trim();
                }
                string host, origin, site;
                if (!fields.TryGetValue("Host", out host) || host != authority ||
                    (fields.TryGetValue("Origin", out origin) && origin != "http://" + authority) ||
                    (fields.TryGetValue("Sec-Fetch-Site", out site) && site == "cross-site"))
                { Reply(stream, 403, "Local same-origin requests only"); return; }
                if (epoch != Volatile.Read(ref generation)) return;
                string path = request[1].Split('?')[0];
                bool api = path.StartsWith("/api/", StringComparison.Ordinal);
                if (api)
                {
                    string supplied;
                    if (!fields.TryGetValue("X-RimFront-Token", out supplied) || !SecretEquals(supplied, token))
                    { Reply(stream, 401, "Open the client using the RimWorld control button"); return; }
                }
                if (path == "/api/state" && request[0] == "GET")
                { Send(stream, 200, "application/json", Volatile.Read(ref snapshot)); return; }
                if (path == "/api/input" && request[0] == "POST")
                {
                    string lengthText, contentType;
                    int length;
                    if (fields.ContainsKey("Transfer-Encoding") || !fields.TryGetValue("Content-Length", out lengthText) ||
                        !int.TryParse(lengthText, out length) || length < 2 || length > 4096 ||
                        !fields.TryGetValue("Content-Type", out contentType) || contentType.Split(';')[0].Trim() != "application/json")
                    { Reply(stream, 400, "Expected bounded JSON body"); return; }
                    byte[] body = new byte[length];
                    int offset = 0;
                    while (offset < body.Length)
                    {
                        if (DateTime.UtcNow > deadline) return;
                        int read = stream.Read(body, offset, body.Length - offset);
                        if (read == 0) return;
                        offset += read;
                    }
                    InputCommand command;
                    try { command = WireJson.Decode<InputCommand>(body); }
                    catch (Exception) { Reply(stream, 400, "Invalid JSON"); return; }
                    if (command == null || !command.IsValid()) { Reply(stream, 400, "Invalid controls"); return; }
                    lock (Lifecycle)
                    {
                        if (epoch != generation) return;
                        if (Commands.Count >= 64) { Reply(stream, 429, "Input queue full"); return; }
                        Commands.Enqueue(command);
                        Interlocked.Exchange(ref lastInputTicks, DateTime.UtcNow.Ticks);
                    }
                    Send(stream, 200, "application/json", Encoding.UTF8.GetBytes("{\"accepted\":true}"));
                    return;
                }
                Asset asset;
                if (request[0] == "GET" && !api && assets.TryGetValue(path, out asset))
                { Send(stream, 200, asset.Mime, asset.Bytes); return; }
                Reply(stream, 404, "Not found");
            }
        }

        private static bool SecretEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
        private static void CloseGracefully(TcpClient client)
        {
            try
            {
                // Deliver error responses even when rejecting a request before consuming its body.
                client.Client.Shutdown(SocketShutdown.Send);
                client.ReceiveTimeout = 100;
                byte[] discard = new byte[1024];
                int budget = 8192;
                while (budget > 0)
                {
                    int read = client.GetStream().Read(discard, 0, Math.Min(budget, discard.Length));
                    if (read == 0) break;
                    budget -= read;
                }
            }
            catch (Exception) { }
            finally { client.Close(); }
        }
        private static void Reply(Stream stream, int code, string message) { Send(stream, code, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(message)); }
        private static void Send(Stream stream, int code, string mime, byte[] bytes)
        {
            string header = "HTTP/1.1 " + code + (code == 200 ? " OK" : " Error") + "\r\nContent-Type: " + mime +
                "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'\r\n\r\n";
            byte[] encoded = Encoding.ASCII.GetBytes(header);
            stream.Write(encoded, 0, encoded.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        private sealed class Asset { public byte[] Bytes; public string Mime; }
    }
}
