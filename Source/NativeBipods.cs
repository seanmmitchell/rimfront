using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RimFront
{
    // Session-local bracing. CE owns setup time and every gameplay modifier.
    internal static class NativeBipods
    {
        static readonly Type Bipod = AccessTools.TypeByName("CombatExtended.BipodComp");
        static readonly Type Driver = AccessTools.TypeByName("CombatExtended.JobDriver_SetUpBipod");
        static readonly FieldInfo Deployed = Bipod?.GetField("IsSetUpRn");
        static readonly MethodInfo Deploy = Bipod?.GetMethod("DeployUpBipod", Type.EmptyTypes);
        static readonly MethodInfo Close = Bipod?.GetMethod("CloseBipod", Type.EmptyTypes);
        static Pawn owner;
        static ThingWithComps weapon;
        static object comp;
        static Job setup;
        static bool requested, manual, patched;

        internal static bool CanBrace(string name, bool ranged)
        {
            if (!ranged) return false;
            var kind = NativeWeapons.ClassifyName(name, true);
            return kind == NativeWeapons.WeaponKind.Rifle || kind == NativeWeapons.WeaponKind.Shotgun
                || kind == NativeWeapons.WeaponKind.Smg || kind == NativeWeapons.WeaponKind.Sniper
                || kind == NativeWeapons.WeaponKind.Heavy || kind == NativeWeapons.WeaponKind.Launcher;
        }
        internal static bool IsMounted(Pawn pawn) => requested && pawn == owner && weapon == pawn?.equipment?.Primary
            && (comp == null || (Deployed?.FieldType == typeof(bool) && (bool)Deployed.GetValue(comp)));
        internal static string Status(Pawn pawn) => pawn == owner && requested
            ? (IsMounted(pawn) ? "Mounted / V fold" : "Bipod setup / V cancel") : "";

        internal static bool HasBipod(Pawn pawn) => IsMounted(pawn) || FindComp(pawn?.equipment?.Primary) != null;
        internal static bool VisualDeployed(Pawn pawn)
        {
            object native = FindComp(pawn?.equipment?.Primary);
            return IsMounted(pawn) || (native != null && Deployed?.FieldType == typeof(bool) && (bool)Deployed.GetValue(native));
        }
        static object FindComp(ThingWithComps equipped)
        {
            if (equipped == null || Bipod == null) return null;
            foreach (ThingComp candidate in equipped.AllComps)
                if (Bipod.IsInstanceOfType(candidate)) return candidate;
            return null;
        }
        static void Initialize()
        {
            if (patched || Bipod == null) return;
            MethodInfo getter = Bipod.GetProperty("ShouldSetUp")?.GetGetMethod();
            if (getter == null || getter.ReturnType != typeof(bool)) throw new NotSupportedException("CE bipod setup contract");
            new Harmony("rimfront.bipods").Patch(getter, prefix: new HarmonyMethod(typeof(NativeBipods), nameof(ShouldSetUp)));
            patched = true;
        }
        // CE auto-setup must not immediately undo an explicit V fold. Scope to the exact
        // equipped component and possession session; other pawns keep their native rules.
        static bool ShouldSetUp(object __instance, ref bool __result)
        {
            if (!manual || !ReferenceEquals(comp, __instance) || owner != NativeView.ControlledPawn
                || !NativeView.Active || owner?.equipment?.Primary != weapon) return true;
            __result = requested && !IsMounted(owner);
            return false;
        }
        internal static bool Toggle(Pawn pawn, out string message)
        {
            message = "This weapon cannot be braced.";
            if (CombatAdapter.IsReloading(pawn))
            { message = "Finish reloading before toggling the bipod."; return false; }
            ThingWithComps equipped = pawn?.equipment?.Primary;
            if (equipped == null || !CanBrace(equipped.def.defName, equipped.def.IsRangedWeapon)) return false;
            if (owner != null && (owner != pawn || weapon != equipped)) Clear();
            object bipod = FindComp(equipped);
            try
            {
                if (bipod != null)
                {
                    Initialize();
                    if (Deploy == null || Close == null || Deployed?.FieldType != typeof(bool) || Driver == null)
                        throw new NotSupportedException("CE bipod methods");
                }
                bool folding = requested || (bipod != null && (bool)Deployed.GetValue(bipod))
                    || CombatAdapter.IsSettingUpBipod(pawn);
                if (!folding && pawn.stances.curStance is Stance_Busy)
                { message = "Wait for the weapon to finish before mounting."; return false; }
                if (bipod != null)
                {
                    Type controller = AccessTools.TypeByName("CombatExtended.Controller");
                    FieldInfo settingsField = controller?.GetField("settings");
                    object settings = settingsField?.GetValue(null);
                    PropertyInfo enabled = settingsField?.FieldType.GetProperty("BipodMechanics");
                    if (controller != null && (settings == null || enabled?.PropertyType != typeof(bool) || !(bool)enabled.GetValue(settings)))
                    { message = "Enable CE bipod mechanics to deploy this bipod."; return false; }
                }
                owner = pawn; weapon = equipped; comp = bipod; manual = true;
                if (CombatAdapter.IsSettingUpBipod(pawn)) setup = pawn.CurJob;
                if (folding)
                { Fold(); message = "Bipod folded."; return true; }
                if (comp != null)
                {
                    JobDef definition = DefDatabase<JobDef>.GetNamedSilentFail("JobDef_SetUpBipod");
                    if (definition?.driverClass != Driver) throw new NotSupportedException("CE bipod job driver");
                    Deploy.Invoke(comp, null);
                    setup = JobMaker.MakeJob(definition, equipped);
                    requested = true;
                    pawn.pather?.StopDead();
                    pawn.jobs.StartJob(setup, JobCondition.InterruptForced);
                    message = "Setting up bipod / V cancel.";
                }
                else { requested = true; message = "Weapon braced / V fold; movement releases the brace."; }
                return true;
            }
            catch (Exception e)
            {
                Clear();
                Log.Warning("[RimFront] Bipod adapter: " + e.GetBaseException().Message);
                message = "Unsupported CE bipod API; use the native controls.";
                return false;
            }
        }
        internal static void Update(Pawn pawn, bool moving)
        {
            if (owner == null) return;
            if (owner != pawn || weapon != pawn?.equipment?.Primary || !pawn.Spawned || pawn.Dead || pawn.Downed)
            { Clear(); return; }
            if (moving) Fold();
            if (requested && comp != null && setup != null && pawn.CurJob != setup)
            {
                setup = null;
                if (!IsMounted(pawn)) Fold(); // interrupted setup never becomes a completed mount
            }
        }
        static void Fold()
        {
            bool wasRequested = requested;
            requested = false;
            Job stopping = setup; setup = null;
            if (stopping != null && owner?.CurJob == stopping)
                owner.jobs.EndCurrentJob(JobCondition.InterruptForced);
            if (comp != null && (stopping != null || wasRequested || (Deployed?.FieldType == typeof(bool) && (bool)Deployed.GetValue(comp))))
                Close?.Invoke(comp, null);
        }
        internal static void Clear()
        {
            try { Fold(); }
            catch (Exception e) { Log.Warning("[RimFront] Bipod cleanup: " + e.GetBaseException().Message); }
            finally { owner = null; weapon = null; comp = null; setup = null; requested = manual = false; }
        }
    }
}
