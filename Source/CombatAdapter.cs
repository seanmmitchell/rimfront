using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimFront
{
    // No CE assembly reference: every shot still uses the equipped native verb and native job driver.
    internal static class CombatAdapter
    {
        static readonly Type AmmoType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
        static readonly Type BipodType = AccessTools.TypeByName("CombatExtended.BipodComp");
        static readonly Type BipodDriver = AccessTools.TypeByName("CombatExtended.JobDriver_SetUpBipod");
        static readonly Type ReloadDriver = AccessTools.TypeByName("CombatExtended.JobDriver_Reload");
        static readonly MethodInfo ReloadMethod = AmmoType?.GetMethod("TryStartReload", Type.EmptyTypes);
        static readonly PropertyInfo AmmoCount = AmmoType?.GetProperty("CurMagCount");
        static readonly PropertyInfo MagazineSize = AmmoType?.GetProperty("MagSize");
        static readonly PropertyInfo HasMagazine = AmmoType?.GetProperty("HasMagazine");
        static readonly PropertyInfo HasAmmo = AmmoType?.GetProperty("HasAmmo");
        static readonly PropertyInfo UsesAmmo = AmmoType?.GetProperty("UseAmmo");
        static readonly PropertyInfo CanFire = AmmoType?.GetProperty("CanBeFiredNow");
        static bool warned;
        // Resolve stateful native verbs only in nearest-first order after geometric filtering.
        internal static int SelectNearestUsable(double[] distances, Func<int, bool> usable)
        {
            var order = new List<int>(distances.Length);
            for (int i = 0; i < distances.Length; i++)
                if (!double.IsNaN(distances[i]) && !double.IsInfinity(distances[i]) && distances[i] >= 0) order.Add(i);
            order.Sort((left, right) => distances[left].CompareTo(distances[right]));
            foreach (int index in order) if (usable(index)) return index;
            return -1;
        }
        internal static string Mode => AmmoType == null ? "Vanilla native combat" : "Combat Extended native combat";
        static bool Supported => ReloadMethod != null && ReloadMethod.ReturnType == typeof(void)
            && AmmoCount?.PropertyType == typeof(int) && MagazineSize?.PropertyType == typeof(int)
            && HasMagazine?.PropertyType == typeof(bool) && HasAmmo?.PropertyType == typeof(bool)
            && UsesAmmo?.PropertyType == typeof(bool) && CanFire?.PropertyType == typeof(bool);

        static object AmmoComp(Pawn pawn)
        {
            if (AmmoType == null || pawn.equipment?.Primary == null) return null;
            foreach (ThingComp comp in pawn.equipment.Primary.AllComps)
                if (AmmoType.IsInstanceOfType(comp)) return comp;
            return null;
        }

        internal static WeaponState Describe(Pawn pawn)
        {
            WeaponState state = new WeaponState { label = pawn.equipment?.Primary?.LabelCap.ToString() ?? "Unarmed", status = "Ready", ammo = -1, capacity = -1 };
            state.fireMode = NativeFireModes.Describe(pawn);
            object comp = AmmoComp(pawn);
            if (comp == null) { state.status = AmmoType == null ? "Native weapon" : "CE weapon without magazine"; return state; }
            if (!Supported) { state.status = "Unsupported CE API: control fire disabled"; return state; }
            try
            {
                state.ammo = (int)AmmoCount.GetValue(comp, null);
                state.capacity = (int)MagazineSize.GetValue(comp, null);
                state.canReload = (bool)HasMagazine.GetValue(comp, null) && state.ammo < state.capacity
                    && (!(bool)UsesAmmo.GetValue(comp, null) || (bool)HasAmmo.GetValue(comp, null));
                state.status = (bool)CanFire.GetValue(comp, null) ? "Ready" : "Empty / reload required";
                if (IsReloading(pawn)) state.status = "Reloading";
            }
            catch (Exception e) { Report(e); state.status = "CE API unavailable"; state.canReload = false; }
            return state;
        }

        internal static bool IsReloading(Pawn pawn) => pawn.CurJob?.def?.defName == "ReloadWeapon";
        internal static bool IsEquippedReload(Pawn pawn, Job job)
        {
            ThingWithComps weapon = pawn?.equipment?.Primary;
            if (pawn == null || job == null || weapon == null || ReloadDriver == null) return false;
            return NativeJobPolicy.CompatibleReload(job.def?.defName, job.forceSleep,
                job.def?.driverClass == ReloadDriver, job.targetA.Thing == pawn,
                job.targetB.Thing == weapon, AmmoComp(pawn) != null);
        }
        internal static bool IsSettingUpBipod(Pawn pawn)
        {
            Job job = pawn?.CurJob;
            ThingWithComps weapon = pawn?.equipment?.Primary;
            if (job == null || weapon == null || BipodType == null || BipodDriver == null) return false;
            bool hasBipod = false;
            if (weapon.AllComps != null)
                foreach (ThingComp comp in weapon.AllComps) if (BipodType.IsInstanceOfType(comp)) { hasBipod = true; break; }
            return NativeJobPolicy.CompatibleBipod(job.def?.defName, job.playerForced, job.forceSleep,
                job.def?.driverClass == BipodDriver, job.targetA.Thing == weapon, hasBipod);
        }

        internal static bool TryReload(Pawn pawn, out string message)
        {
            message = "This weapon has no supported manual reload.";
            object comp = AmmoComp(pawn);
            if (comp == null || !Supported) return false;
            try
            {
                if (IsReloading(pawn)) { message = "Reloading."; return true; }
                if (!Describe(pawn).canReload) { message = "Magazine full or no compatible ammunition in inventory."; return false; }
                ReloadMethod.Invoke(comp, null);
                message = IsReloading(pawn) ? "Reloading." : "Combat Extended declined reload.";
                return IsReloading(pawn);
            }
            catch (Exception e) { Report(e); message = "Unsupported CE reload API; reload in RimWorld."; return false; }
        }

        internal static Job CreateAttack(Pawn pawn, float yaw, float pitch, out string message)
        {
            message = "Aim anywhere on the map to fire.";
            Thing primary = pawn.equipment?.Primary;
            bool melee = primary == null || !primary.def.IsRangedWeapon;
            if (!melee && AmmoType != null && !Supported) { message = "Unsupported CE API; fire disabled."; return null; }
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) { message = "This pawn is incapable of violence."; return null; }
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (!melee && (verb == null || !verb.Available())) { message = "Weapon is unavailable."; return null; }
            object comp = melee ? null : AmmoComp(pawn);
            if (comp != null)
            {
                try { if (!(bool)CanFire.GetValue(comp, null)) { message = "Reload required."; return null; } }
                catch (Exception e) { Report(e); message = "Unsupported CE API; fire disabled."; return null; }
            }
            float range = melee ? 2f : NativeManualAim.SelectionRange(verb);
            if (!NativeAimTargeting.TrySelect(pawn, yaw, pitch, range, out NativeAimTargeting.TargetInfo aim))
            { message = "No valid point on the map in that direction."; return null; }
            Thing target = aim.Target.Thing;
            if (melee)
            {
                if (target == null || !pawn.Position.AdjacentTo8WayOrInside(target.Position))
                { message = "Aim at an adjacent object or pawn for melee."; return null; }
                verb = pawn.TryGetAttackVerb(target, false);
                if (verb == null || !verb.IsMeleeAttack || !verb.Available() || !verb.CanHitTarget(target))
                { message = "Native melee cannot reach that target."; return null; }
            }
            // Cells let native AttackStatic fire freely without hostile-only targeting,
            // chasing, target auto-selection, or a moving Thing overriding mouse aim.
            LocalTargetInfo nativeTarget = melee ? aim.Target : new LocalTargetInfo(aim.Point.ToIntVec3());
            Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, nativeTarget);
            attack.verbToUse = verb;
            attack.maxNumStaticAttacks = 1;
            attack.endIfCantShootTargetFromCurPos = true;
            attack.playerForced = true;
            attack.playerInterruptedForced = true; // suppress native automatic follow-up target queues
            if (!melee && !NativeManualAim.Prepare(pawn, verb, attack, aim, yaw, pitch, out message)) return null;
            string trajectory = !melee && !NativeManualAim.IsCE(verb) && !NativeVanillaAim.Supports(verb) ? " (native 2D trajectory)" : "";
            message = (melee ? "Attacking " : "Firing at ") + aim.Label + trajectory + ".";
            return attack;
        }

        static void Report(Exception exception)
        {
            if (warned) return;
            warned = true;
            Log.Warning("[RimFront] CE adapter failed closed: " + exception.GetBaseException().Message);
        }
    }
}

