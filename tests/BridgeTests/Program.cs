using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RimFront;

class Program
{
    static int checks;
    static readonly HttpClient Client = new HttpClient(new HttpClientHandler { UseProxy = false });
    static string address;
    static string token;
    static async Task Main()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "rimfront-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "index.html"), "<!doctype html><title>RimFront test fixture</title>");
        try
        {
            Check(!new InputCommand { action = "input", yaw = float.NaN }.IsValid(), "NaN rejected");
            foreach (float pitch in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, (float)(Math.PI / 2), (float)(-Math.PI / 2) })
                Check(!new InputCommand { action = "input", pitch = pitch }.IsValid(), "invalid pitch rejected: " + pitch);
            Check(new InputCommand { action = "input", pitch = .7f }.IsValid(), "positive pitch accepted");
            var legacyMode = WireJson.Decode<InputCommand>(Encoding.UTF8.GetBytes("{\"action\":\"input\",\"fire\":true}"));
            Check(legacyMode.fire && !legacyMode.cycleFireMode, "legacy input without optional mode field remains valid");
            var modeRequest = WireJson.Decode<InputCommand>(WireJson.Encode(new InputCommand { action = "input", cycleFireMode = true }));
            Check(modeRequest.IsValid() && modeRequest.cycleFireMode, "fire mode edge survives protocol round trip");
            Check(new InputCommand { action = "input", pitch = -.7f }.IsValid(), "negative pitch accepted");
            Check(AimGeometry.Intersects(0, 0, 0, 10, 1.55, 1.765, .35, out var ahead) && ahead == 10, "level ray hits human ahead");
            Check(!AimGeometry.Intersects(0, -.2, 0, 10, 1.55, 1.765, .35, out _), "looking above human rejects target");
            Check(!AimGeometry.Intersects(0, 0, 0, -10, 1.55, 1.765, .35, out _), "target behind rejected");
            Check(!AimGeometry.Intersects(0, 0, .36, 10, 1.55, 1.765, .35, out _), "ray outside body width rejected");
            Check(AimGeometry.Intersects(0, 0, .35, 10, 1.55, 1.765, .35, out _), "body width boundary accepted");
            Check(!AimGeometry.Intersects(0, 0, 0, 10, 1.55, .98, .35, out _), "level ray passes above animal");
            Check(AimGeometry.Intersects(0, .08, 0, 10, 1.55, .98, .35, out _), "downward ray hits animal");
            Check(!AimGeometry.Intersects(0, .2, 0, 10, 1.55, .98, .35, out _), "ray below ground rejects animal");
            Check(AimGeometry.Intersects(Math.PI / 2, 0, 10, 0, 1.55, 1.765, .35, out _), "east cardinal aim");
            Check(AimGeometry.Intersects(-Math.PI / 2, 0, -10, 0, 1.55, 1.765, .35, out _), "west cardinal aim");
            Check(AimGeometry.Intersects(Math.PI, 0, 0, -10, 1.55, 1.765, .35, out _), "south cardinal aim");
            Check(AimGeometry.RayIntersectsBox(-5, .8, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out var boxDistance)
                && Math.Abs(boxDistance - 4.65) < .000001, "box slab returns entry distance");
            Check(AimGeometry.RayIntersectsBox(-5, .8, 0, .5, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out boxDistance)
                && Math.Abs(boxDistance - 9.3) < .000001, "inverse model scale preserves ray parameter");
            Check(!AimGeometry.RayIntersectsBox(-5, -.01, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "parallel ray below box rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, .21, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "parallel ray outside depth rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, 0, -1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "box behind ray rejected");
            Check(AimGeometry.RayIntersectsBox(0, .8, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out boxDistance) && boxDistance == 0, "inside box accepted at zero distance");
            Check(!AimGeometry.RayIntersectsBox(0, .8, 0, 0, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "zero direction rejected");
            Check(!AimGeometry.RayIntersectsBox(double.NaN, .8, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "nonfinite ray rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, 0, 1, 0, 0, -.35, 0, -.2, double.PositiveInfinity, 1.725, .2, out _), "nonfinite box rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, 0, 1, 0, 0, .35, 0, -.2, -.35, 1.725, .2, out _), "inverted box rejected");
            Check(!new InputCommand { action = "input", forward = 2 }.IsValid(), "out of range movement rejected");
            Check(!new InputCommand { action = "teleport" }.IsValid(), "unknown action rejected");
            BridgeServer.Start(fixture);
            Connect();
            Check((await Send("GET", "/", auth:false)).StatusCode == HttpStatusCode.OK, "client assets accessible");
            Check((await Send("GET", "/api/state", auth:false)).StatusCode == HttpStatusCode.Unauthorized, "state requires token");
            Check((await Send("GET", "/api/state", origin:"https://evil.example")).StatusCode == HttpStatusCode.Forbidden, "cross origin rejected");
            Check((await Send("GET", "/api/state", host:"evil.example")).StatusCode == HttpStatusCode.Forbidden, "DNS rebinding host rejected");
            Check((await Send("GET", "/Source/Protocol.cs")).StatusCode == HttpStatusCode.NotFound, "source not served");
            BridgeServer.Publish(new WorldSnapshot { active = true, width = 2, height = 1, cells = new[] { 0, 1 }, controlledPawnId = 42, pawns = new[] { new PawnState { id = 42, name = "A \"quoted\" pawn", health = .75f } } });
            var response = await Send("GET", "/api/state");
            var state = WireJson.Decode<WorldSnapshot>(await response.Content.ReadAsByteArrayAsync());
            Check(state.active && state.cells[1] == 1 && state.pawns[0].name == "A \"quoted\" pawn", "snapshot roundtrip");
            Check((await Send("POST", "/api/input", "not-json")).StatusCode == HttpStatusCode.BadRequest, "malformed JSON rejected");
            Check((await Send("POST", "/api/input", "{\"action\":\"input\",\"forward\":4}")).StatusCode == HttpStatusCode.BadRequest, "invalid controls rejected");
            Check((await Send("POST", "/api/input", new string('x', 5000))).StatusCode == HttpStatusCode.BadRequest, "oversized input rejected");
            string input = "{\"seq\":1,\"action\":\"input\",\"forward\":1,\"strafe\":0,\"yaw\":0.5,\"fire\":true}";
            Check((await Send("POST", "/api/input", input)).StatusCode == HttpStatusCode.OK, "valid control accepted");
            Check(BridgeServer.TryDequeue(out var command) && command.forward == 1 && command.fire, "controls queued for main thread");
            Check((DateTime.UtcNow - BridgeServer.LastInputUtc).TotalSeconds < 2, "heartbeat updated");
            for (int i = 0; i < 64; i++) Check((await Send("POST", "/api/input", input)).StatusCode == HttpStatusCode.OK, "bounded queue enqueue " + i);
            Check((await Send("POST", "/api/input", input)).StatusCode == HttpStatusCode.TooManyRequests, "queue overflow rejected");
            string oldToken = token;
            BridgeServer.Stop();
            Check(!BridgeServer.TryDequeue(out command), "stop clears pending controls");
            BridgeServer.Start(fixture);
            Connect();
            Check(token != oldToken, "restart rotates secret");
            string newToken = token;
            token = oldToken;
            Check((await Send("GET", "/api/state")).StatusCode == HttpStatusCode.Unauthorized, "old token rejected");
            token = newToken;
            Console.WriteLine("PASS: " + checks + " bridge/protocol checks");
        }
        finally
        {
            BridgeServer.Stop();
            Client.Dispose();
            // This directory was created uniquely by this process and contains only its own fixture.
            File.Delete(Path.Combine(fixture, "index.html"));
            Directory.Delete(fixture);
        }
    }
    static void Connect()
    {
        string[] parts = BridgeServer.Url.Split('#');
        address = parts[0].TrimEnd('/');
        token = parts[1].Substring("token=".Length);
    }
    static async Task<HttpResponseMessage> Send(string method, string path, string json = null, bool auth = true, string origin = null, string host = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), address + path);
        if (auth) request.Headers.Add("X-RimFront-Token", token);
        if (origin != null) request.Headers.Add("Origin", origin);
        if (host != null) request.Headers.Host = host;
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.SendAsync(request);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        checks++;
    }
}
