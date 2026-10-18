using System;
using System.Reflection;
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
        static Pawn controlled;
        static bool automatic = true, patched, initialized;
        internal static void Start(Pawn pawn) { controlled = pawn; automatic = true; }
        internal static void Stop() { controlled = null; automatic = true; }
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
            if (!Suppress(NativeView.Active, NativeView.ControlledPawn == controlled,
                controlled != null && __instance.parent == controlled.equipment?.Primary, automatic)) return true;
            __result = false;
            return false;
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
            => pawn == controlled && Comp(pawn) != null ? (!patched ? "Bipod unavailable"
                : automatic ? "Bipod auto ON (G)" : "Bipod auto OFF (G)") : "";
        internal static bool TryToggle(Pawn pawn, out string message)
        {
            message = "This weapon has no supported bipod toggle.";
            object comp = Comp(pawn);
            if (!patched || pawn == null || pawn != controlled || comp == null) return false;
            bool next = !automatic;
            // Veto setup before cleanup can synchronously ask CE for a new job.
            bool previous = automatic;
            automatic = next;
            if (!next && !Api.TryClose(comp))
            { automatic = previous; message = "Combat Extended could not close this bipod."; return false; }
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
