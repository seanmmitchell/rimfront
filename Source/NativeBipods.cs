using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RimFront
{
    // A possession-only veto of CE's setup decision. CE still owns deployment,
    // stats and aim-mode rules; no global CE setting or shared definition is edited.
    internal static class NativeBipods
    {
        static readonly Type CompType = AccessTools.TypeByName("CombatExtended.BipodComp");
        static readonly Contract Api = new Contract(CompType);
        static readonly Dictionary<Pawn, bool> preferences = new Dictionary<Pawn, bool>();
        static bool patched, initialized;
        internal static void Start(Pawn pawn) { preferences[pawn] = true; }
        internal static void Stop() { Pawn pawn = RimFrontGame.Active?.ControlledPawn; if (pawn != null) preferences.Remove(pawn); }
        internal static bool Automatic(Pawn pawn) => pawn == null || !preferences.TryGetValue(pawn, out bool value) || value;
        internal static void Restore(Pawn pawn, bool automatic) { if (pawn != null) preferences[pawn] = automatic; }
        internal static void ClearAll() => preferences.Clear();
        static object Comp(Pawn pawn)
        {
            if (CompType == null || pawn?.equipment?.Primary?.AllComps == null) return null;
            foreach (ThingComp comp in pawn.equipment.Primary.AllComps)
                if (CompType.IsInstanceOfType(comp)) return comp;
            return null;
        }
        internal static bool Suppress(bool active, bool samePawn, bool equipped, bool enabled)
            => active && samePawn && equipped && !enabled;
        static bool ShouldSetUp(ThingComp __instance, ref bool __result)
        {
            foreach (var pair in preferences)
                if (RimFrontGame.For(pair.Key) != null && !pair.Value && __instance.parent == pair.Key.equipment?.Primary)
                { __result = false; return false; }
            return true;
        }
        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (!Api.Supported) return;
            try
            {
                new Harmony("rimfront.bipod").Patch(Api.Decision,
                    prefix: new HarmonyMethod(typeof(NativeBipods), nameof(ShouldSetUp)));
                patched = true;
            }
            catch (Exception error) { Log.Warning("[RimFront] Bipod toggle unavailable: " + error.GetBaseException().Message); }
        }
        internal static string Describe(Pawn pawn)
            => pawn != null && preferences.ContainsKey(pawn) && Comp(pawn) != null ? (!patched ? "Bipod unavailable"
                : Automatic(pawn) ? "Bipod auto ON (G)" : "Bipod auto OFF (G)") : "";
        internal static bool TryToggle(Pawn pawn, out string message)
        {
            message = "This weapon has no supported bipod toggle.";
            object comp = Comp(pawn);
            if (!patched || pawn == null || !preferences.ContainsKey(pawn) || comp == null) return false;
            bool next = !Automatic(pawn);
            // Veto setup before cleanup can synchronously ask CE for a new job.
            bool previous = Automatic(pawn);
            preferences[pawn] = next;
            if (!next && !Api.TryClose(comp))
            { preferences[pawn] = previous; message = "Combat Extended could not close this bipod."; return false; }
            if (!next && CombatAdapter.IsSettingUpBipod(pawn))
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            message = next ? "Automatic bipod setup ON (CE aim-mode rules)." : "Automatic bipod setup OFF.";
            return true;
        }
        internal sealed class Contract
        {
            readonly Type type;
            readonly MethodInfo close;
            internal readonly MethodInfo Decision;
            internal readonly bool Supported;
            internal Contract(Type type)
            {
                this.type = type;
                if (type == null) return;
                try
                {
                    Decision = type.GetProperty("ShouldSetUp", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
                    close = type.GetMethod("CloseBipod", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                    Supported = Decision?.ReturnType == typeof(bool) && Decision.GetParameters().Length == 0
                        && close?.ReturnType == typeof(void) && !close.ContainsGenericParameters;
                }
                catch { Supported = false; }
            }
            internal bool TryClose(object comp)
            {
                if (!Supported || comp == null || !type.IsInstanceOfType(comp)) return false;
                try { close.Invoke(comp, null); return true; }
                catch { return false; }
            }
        }
    }
}
