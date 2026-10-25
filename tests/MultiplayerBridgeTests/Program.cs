using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using RimFront;
using Verse;
using HarmonyLib;

class FakeMultiplayer { public static object session = new object(); }
static class Program
{
    static int checks;
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
    static void Reject(Action action, string reason)
    {
        try { action(); } catch (NotSupportedException) { checks++; return; }
        catch (TargetInvocationException error) when (error.InnerException is NotSupportedException) { checks++; return; }
        throw new Exception(reason);
    }
    static void Main()
    {
        var own = Assembly.GetExecutingAssembly();
        var bytes = File.ReadAllBytes(own.Location);
        string root = Path.Combine(Path.GetTempPath(), "rimfront-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "AssembliesCustom"));
        try
        {
            MultiplayerBridge.Initialize();
            Check(!MultiplayerBridge.InSession && !MultiplayerBridge.Available, "missing MP changed single-player detection");
            Check(MultiplayerBridge.ArtifactPath(own) == own.Location, "file-loaded assembly lost its real location");
            var raw = Assembly.Load(bytes);
            Check(string.IsNullOrEmpty(raw.Location), "test failed to reproduce byte-array loading");
            Reject(() => MultiplayerBridge.ArtifactPath(raw), "unowned in-memory assembly was accepted");
            string path = Path.Combine(root, "AssembliesCustom", raw.GetName().Name + ".dll");
            File.WriteAllBytes(path, bytes);
            var owner = new ModContentPack { RootDir = root }; owner.assemblies.loadedAssemblies.Add(raw);
            LoadedModManager.RunningMods.Add(owner);
            Check(MultiplayerBridge.ArtifactPath(raw) == path, "byte-array assembly could not resolve through its owning mod");
            var verify = typeof(MultiplayerBridge).GetMethod("VerifyHash", BindingFlags.Static | BindingFlags.NonPublic);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string mvid = raw.ManifestModule.ModuleVersionId.ToString();
            verify.Invoke(null, new object[] { raw, hash, mvid }); checks++;
            Reject(() => verify.Invoke(null, new object[] { raw, hash, Guid.NewGuid().ToString() }), "wrong loaded module ID accepted");
            bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(path, bytes);
            Reject(() => verify.Invoke(null, new object[] { raw, hash, mvid }), "changed artifact hash accepted");
            AccessTools.MultiplayerType = typeof(FakeMultiplayer); MultiplayerBridge.Initialize();
            Check(MultiplayerBridge.InSession && !MultiplayerBridge.Available && Harmony.Patches == 0,
                "unknown active MP build fell through to unsynchronized control");
            FakeMultiplayer.session = null;
            Check(!MultiplayerBridge.InSession, "inactive MP was mistaken for an active session");
            Console.WriteLine("PASS: " + checks + " production Multiplayer bridge artifact and fail-closed checks. No game or network session started.");
        }
        finally
        {
            LoadedModManager.RunningMods.Clear();
            string resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("rimfront-bridge-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }
}
