using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.IO;
using HarmonyLib;
using Verse;

namespace RimFront
{
    // Optional late-bound integration. No Multiplayer assembly is linked or redistributed.
    internal static class MultiplayerBridge
    {
        const string Verified16 = "28d2cda41d78497970565748be0e4b7e23f6ece03310a304359a840ab38faad1";
        const string Verified17 = "2c3403fee45cb142aee6b212e04a8c204dc4af5aee99a632d787b51ada0018ce";
        const string Common16 = "aeb57fae464bdf324e67974a7c418abf796c36d2249f3d8b548eb330efd050e4";
        const string Common17 = "0c3c5718d2c1cd16e716b5df63590179fb4d33d99eb3da2c88272388f184ccb4";
        const string Api16 = "863766df5fd24a31eda1e59947f026abd5b8dc99cb0d7bca6a8a3d356b65ad8d";
        const string Api17 = "815e5a370c6606ded09df2337f932303da75ce439431ff4d48c02a5a12a1cf2a";
        static Type multiplayer, api;
        static FieldInfo session, localId, commandPlayer, multifaction;
        static PropertyInfo isHosting, isReplay, inInterface, gameComp, simulating;
        static MethodInfo getPlayers;
        static PropertyInfo timeVote;
        static PropertyInfo gameTimeVote;
        static MethodInfo togglePause, sendTimeVote;
        internal static bool Available { get; private set; }
        internal static string Failure { get; private set; } = "Multiplayer adapter has not initialized.";
        [ThreadStatic] internal static int? Sender;
        internal static bool InSession => session == null ? multiplayer != null : session.GetValue(null) != null;
        internal static object SessionToken => session?.GetValue(null);
        internal static int LocalPlayer => InSession ? (int)localId.GetValue(session.GetValue(null)) : -1;
        internal static bool Hosting => Available && (bool)isHosting.GetValue(null);
        internal static bool Interface => Available && InSession && (bool)inInterface.GetValue(null)
            && !(bool)isReplay.GetValue(null) && !(bool)simulating.GetValue(null);
        internal static bool SharedColony => Available && !(bool)multifaction.GetValue(gameComp.GetValue(null));

        [StaticConstructorOnStartup]
        static class Startup
        {
            static Startup() { Initialize(); }
        }

        internal static void Initialize()
        {
            multiplayer = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            if (multiplayer == null) { Failure = "Multiplayer is not loaded."; return; }
            session = AccessTools.Field(multiplayer, "session");
            try
            {
                if (session == null) throw new NotSupportedException("Multiplayer session detection contract");
#if RW17
                const string expected = Verified17;
                const string expectedModule = "1359f60a-cc52-4130-bc1b-cf1e8adf26d8";
#else
                const string expected = Verified16;
                const string expectedModule = "34169ac4-d99e-463c-b48c-0af3be9783f8";
#endif
                VerifyHash(multiplayer.Assembly, expected, expectedModule);
                api = AccessTools.TypeByName("Multiplayer.API.MP") ?? throw new NotSupportedException("Multiplayer API 0.6");
                localId = RequireField(session.FieldType, "playerId", typeof(int));
                gameComp = RequireProperty(multiplayer, "GameComp");
                multifaction = RequireField(gameComp.PropertyType, "multifaction", typeof(bool));
                isReplay = RequireProperty(multiplayer, "IsReplay", typeof(bool));
                inInterface = RequireProperty(api, "InInterface", typeof(bool));
                isHosting = RequireProperty(api, "IsHosting", typeof(bool));
                simulating = RequireProperty(AccessTools.TypeByName("Multiplayer.Client.TickPatch"), "Simulating", typeof(bool));
                getPlayers = AccessTools.Method(api, "GetPlayers", Type.EmptyTypes);
                Type timeControl = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.TimeControlPatch");
                timeVote = RequireProperty(timeControl, "CurTimeSpeedUI");
                gameTimeVote = RequireProperty(timeControl, "CurTimeSpeedGame");
                togglePause = AccessTools.Method(timeControl, "TogglePaused", new[] { timeVote.PropertyType });
                sendTimeVote = togglePause == null ? null : AccessTools.Method(timeControl, "SendTimeVote", new[] { togglePause.ReturnType });
                if (sendTimeVote?.ReturnType != typeof(void)) throw new NotSupportedException("Multiplayer pause vote contract");
                Type command = AccessTools.TypeByName("Multiplayer.Common.ScheduledCommand");
#if RW17
                VerifyHash(command?.Assembly, Common17, "e4cb3309-7ac6-41bb-a662-57c1fc376360");
                VerifyHash(api.Assembly, Api17, "3e93eaaa-c92a-4421-a5b3-cd683b85da7b");
#else
                VerifyHash(command?.Assembly, Common16, "1c9e28b4-489a-4d53-b564-86e4c0598dde");
                VerifyHash(api.Assembly, Api16, "c74c9bf7-f68c-4e12-ac6e-e2a0c2bd9227");
#endif
                commandPlayer = RequireField(command, "playerId", typeof(int));
                var harmony = new Harmony("rimfront.multiplayer");
                foreach (string typeName in new[] { "Multiplayer.Client.AsyncTimeComp", "Multiplayer.Client.AsyncTime.AsyncWorldTimeComp" })
                {
                    MethodInfo execute = AccessTools.Method(AccessTools.TypeByName(typeName), "ExecuteCmd", new[] { command });
                    if (execute?.ReturnType != typeof(void)) throw new NotSupportedException(typeName + ".ExecuteCmd");
                    harmony.Patch(execute, prefix: new HarmonyMethod(typeof(MultiplayerBridge), nameof(BeginCommand)),
                        finalizer: new HarmonyMethod(typeof(MultiplayerBridge), nameof(EndCommand)));
                }
                Register(nameof(RimFrontMultiplayer.Claim), true);
                Register(nameof(RimFrontMultiplayer.Input), true);
                Register(nameof(RimFrontMultiplayer.End), true);
                Register(nameof(RimFrontMultiplayer.SetRules), false, true);
                Register(nameof(RimFrontMultiplayer.ResetSession), false, true);
                Register(nameof(RimFrontMultiplayer.Disconnect), false, true);
                Available = true; Failure = null;
            }
            catch (Exception error)
            {
                Available = false; Failure = error.GetBaseException().Message;
                Log.Warning("[RimFront] Multiplayer possession unavailable: " + Failure);
            }
        }

        static FieldInfo RequireField(Type type, string name, Type value)
        {
            var result = type == null ? null : AccessTools.Field(type, name);
            if (result?.FieldType != value) throw new NotSupportedException((type?.FullName ?? "missing type") + "." + name);
            return result;
        }
        static void VerifyHash(Assembly assembly, string expected, string moduleId)
        {
            if (assembly == null) throw new NotSupportedException("Missing Multiplayer dependency assembly.");
            if (assembly.ManifestModule.ModuleVersionId != new Guid(moduleId))
                throw new NotSupportedException("Unverified loaded Multiplayer module: " + assembly.GetName().Name);
            using (var stream = File.OpenRead(ArtifactPath(assembly)))
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != expected)
                    throw new NotSupportedException("Unverified Multiplayer dependency: " + assembly.GetName().Name);
        }
        internal static string ArtifactPath(Assembly assembly)
        {
            if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location)) return assembly.Location;
            // Both the game and the port can load assemblies from byte arrays. In
            // that case Location is empty; bind the artifact to its owning mod.
            foreach (var mod in LoadedModManager.RunningMods)
            {
                if (!mod.assemblies.loadedAssemblies.Contains(assembly)) continue;
#if RW17
                string root = mod.RootDir;
#else
                string root = Path.Combine(mod.RootDir, "1.6");
#endif
                string folder = assembly.GetName().Name == "0MultiplayerAPI" ? "Assemblies" : "AssembliesCustom";
                string path = Path.Combine(root, folder, assembly.GetName().Name + ".dll");
                if (File.Exists(path)) return path;
            }
            throw new NotSupportedException("Cannot locate the owning mod artifact for " + assembly.GetName().Name);
        }
        static PropertyInfo RequireProperty(Type type, string name, Type value = null)
        {
            var result = type == null ? null : AccessTools.Property(type, name);
            if (result?.GetGetMethod(true) == null || (value != null && result.PropertyType != value))
                throw new NotSupportedException((type?.FullName ?? "missing type") + "." + name);
            return result;
        }
        static void Register(string name, bool map, bool host = false)
        {
            var target = AccessTools.Method(typeof(RimFrontMultiplayer), name);
            var register = AccessTools.Method(api, "RegisterSyncMethod", new[] { typeof(MethodInfo), AccessTools.TypeByName("Multiplayer.API.SyncType").MakeArrayType() });
            object sync = register?.Invoke(null, new object[] { target, null }) ?? throw new NotSupportedException("RegisterSyncMethod");
            var syncInterface = AccessTools.TypeByName("Multiplayer.API.ISyncMethod");
            Type context = AccessTools.TypeByName("Multiplayer.API.SyncContext");
            syncInterface.GetMethod("SetContext").Invoke(sync, new[] { Enum.Parse(context, map ? "CurrentMap" : "None") });
            if (host) syncInterface.GetMethod("SetHostOnly").Invoke(sync, null);
        }
        static void BeginCommand(object __0, out int? __state)
        { __state = Sender; Sender = (int)commandPlayer.GetValue(__0); }
        static Exception EndCommand(Exception __exception, int? __state)
        { Sender = __state; return __exception; }

        // Roster reads are local observations only. Their result must travel through a
        // host command before it can release deterministic possession state.
        internal static HashSet<int> ConnectedPlayers()
        {
            var result = new HashSet<int>();
            if (getPlayers?.Invoke(null, null) is IEnumerable players)
                foreach (object player in players) result.Add((int)player.GetType().GetProperty("Id").GetValue(player));
            return result;
        }
        internal static void TogglePause()
        {
            if (Interface) sendTimeVote.Invoke(null, new[] { togglePause.Invoke(null, new[] { timeVote.GetValue(null) }) });
        }
        internal static bool Paused => Available && InSession && gameTimeVote.GetValue(null)?.ToString() == "Paused";
    }
}
