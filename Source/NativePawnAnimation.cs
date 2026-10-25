using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFront
{
    // Presentation only. Phase, recoil and swings advance on simulation ticks, never wall time.
    internal static class NativePawnAnimation
    {
        internal const int MaximumTrackedPawns = 256;
        static readonly Dictionary<int, Observation> observations = new Dictionary<int, Observation>();
        static readonly PropertyInfo CurrentToil = typeof(JobDriver).GetProperty("CurToil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static Map map;
        static bool warnedObserver;

        internal struct Snapshot
        {
            internal bool Moving, Running, Aiming, Reloading, Downed, Sleeping, Dead, Lying, Working, Ingesting, Carrying;
            internal float Speed, Phase, IdlePhase, FacingYaw, AimPitch, Recoil, Melee, MeleeProgress, Hurt;
            internal NativeReloadAnimation.Snapshot Reload;
        }

        sealed class Observation
        {
            internal Pawn Pawn;
            internal int SampleTick = -1, PositionTick = -1, LastSeen, ShotTick = int.MinValue, MeleeTick = int.MinValue, HurtTick = int.MinValue;
            internal Vector3 Position, MeleeTarget;
            internal float Phase, HurtStrength;
            internal Snapshot Value;
        }

        static Observation Observe(Pawn pawn, int tick)
        {
            Map activeMap = NativeView.ControlledPawn?.Map;
            if (map != activeMap) { observations.Clear(); map = activeMap; }
            if (observations.TryGetValue(pawn.thingIDNumber, out Observation result) && result.Pawn == pawn)
            { result.LastSeen = tick; return result; }
            if (observations.Count >= MaximumTrackedPawns)
            {
                int oldestId = -1, oldestTick = int.MaxValue;
                foreach (var entry in observations)
                    if (entry.Value.LastSeen < oldestTick) { oldestId = entry.Key; oldestTick = entry.Value.LastSeen; }
                if (oldestId != -1) observations.Remove(oldestId);
            }
            result = new Observation { Pawn = pawn, LastSeen = tick, Phase = (pawn.thingIDNumber % 31) * .2f };
            observations[pawn.thingIDNumber] = result;
            return result;
        }

        internal static Snapshot Sample(Pawn pawn)
        {
            if (pawn == null || Find.TickManager == null) return default;
            int tick = Find.TickManager.TicksGame;
            Observation observation = Observe(pawn, tick);
            if (observation.SampleTick == tick) return observation.Value;
            Snapshot state = new Snapshot
            {
                Dead = pawn.Dead, Downed = pawn.Downed, Sleeping = pawn.jobs?.curDriver?.asleep == true,
                FacingYaw = pawn.Rotation.AsAngle, IdlePhase = (tick % 36000) * .055f + (pawn.thingIDNumber % 31) * .2f,
                Carrying = pawn.carryTracker?.CarriedThing != null
            };
            state.Lying = state.Dead || state.Downed || pawn.GetPosture().Laying();
            Vector3 position = pawn.DrawPos;
            bool moving = !state.Lying && pawn.pather?.MovingNow == true;
            int elapsed = observation.PositionTick < 0 ? 0 : tick - observation.PositionTick;
            Vector3 displacement = position - observation.Position; displacement.y = 0;
            float speed = 0;
            if (moving)
            {
                if (AnalogMovement.Controls(pawn)) speed = AnalogMovement.VelocityFor(pawn).magnitude;
                else if (elapsed > 0 && elapsed <= 30) speed = displacement.magnitude * 60f / elapsed;
                else speed = pawn.GetStatValue(StatDefOf.MoveSpeed);
            }
            state.Speed = SafeSpeed(speed);
            state.Moving = moving && state.Speed > .05f;
            state.Running = state.Moving && state.Speed > 4.5f;
            // Displacement may include a teleport or offscreen interval; never feed that into gait.
            if (elapsed > 0 && elapsed <= 30 && state.Moving)
                observation.Phase = AdvancePhase(observation.Phase, state.Speed, elapsed);
            state.Phase = observation.Phase;
            observation.Position = position; observation.PositionTick = tick;
            if (state.Moving && displacement.sqrMagnitude > .00001f)
                state.FacingYaw = Mathf.Atan2(displacement.x, displacement.z) * Mathf.Rad2Deg;

            Job job = pawn.CurJob;
            string name = job?.def?.defName;
            state.Reload = NativeReloadAnimation.Sample(pawn);
            state.Reloading = state.Reload.Active;
            state.Recoil = RecoilAt(Age(tick, observation.ShotTick));
            state.MeleeProgress = Mathf.Clamp01(Age(tick, observation.MeleeTick) / 18f);
            state.Melee = SwingAt(Age(tick, observation.MeleeTick));
            state.Hurt = HurtAt(Age(tick, observation.HurtTick), observation.HurtStrength);

            Verb verb = job?.verbToUse ?? pawn.equipment?.PrimaryEq?.PrimaryVerb;
            LocalTargetInfo target = verb?.CurrentTarget ?? LocalTargetInfo.Invalid;
            if (pawn.stances?.curStance is Stance_Busy busy && busy.focusTarg.IsValid) target = busy.focusTarg;
            state.Aiming = !state.Lying && !state.Reloading && verb?.verbProps?.IsMeleeAttack == false
                && target.IsValid && (pawn.stances?.curStance is Stance_Busy || name == "AttackStatic");
            if (state.Aiming)
            {
                Vector3 aim = target.Thing is Pawn targetPawn ? NativePawns.TargetPosition(targetPawn)
                    : target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();
                aim.y = target.Thing is Pawn other ? NativeScene.GetPawnHeight(other) * .68f : .8f;
                SetAim(ref state, position, aim, NativeScene.GetPawnHeight(pawn) * .68f);
            }
            else if (state.Melee > 0)
                SetAim(ref state, position, observation.MeleeTarget, NativeScene.GetPawnHeight(pawn) * .65f);
            if (RimFrontMultiplayer.InSession && RimFrontGame.For(pawn) is RimFrontGame controller)
            {
                state.FacingYaw = controller.Yaw * Mathf.Rad2Deg;
                if (state.Aiming) state.AimPitch = Mathf.Clamp(controller.Pitch * Mathf.Rad2Deg, -70, 70);
            }
            else if (NativeView.ControlledPawn == pawn)
            {
                state.FacingYaw = NativeView.Yaw * Mathf.Rad2Deg;
                if (state.Aiming) state.AimPitch = Mathf.Clamp(NativeView.PitchDegrees, -70, 70);
            }
            bool atWork = !state.Moving && !state.Lying && !state.Reloading && pawn.jobs?.curDriver != null
                && job != null && job.targetA.IsValid && pawn.Position.DistanceToSquared(job.targetA.Cell) <= 9
                && (name == "Ingest" || name == "IngestForNutrition" || IsWorkJob(name)) && ActiveWorkToil(pawn.jobs.curDriver);
            state.Ingesting = atWork && (name == "Ingest" || name == "IngestForNutrition");
            state.Working = atWork && IsWorkJob(name);
            if (state.Dead || state.Sleeping || state.Downed)
            { state.Recoil = state.Melee = state.Hurt = 0; state.Aiming = state.Working = state.Ingesting = state.Reloading = false; }
            observation.SampleTick = tick; observation.Value = state;
            return state;
        }

        static bool IsWorkJob(string name)
        {
            switch (name)
            {
                case "Mine": case "Repair": case "ConstructFinishFrame": case "Deconstruct":
                case "SmoothFloor": case "SmoothWall": case "CutPlant": case "Harvest": case "Sow":
                case "DoBill": case "Research": case "TendPatient": case "Clean": case "Refuel": return true;
                default: return false;
            }
        }

        static bool ActiveWorkToil(JobDriver driver)
        {
            try
            {
                // A work job may first walk to ingredients or pick them up. Do not mime labor
                // during those path/instant toils just because the overall job has a work name.
                Toil toil = CurrentToil?.GetValue(driver, null) as Toil;
                return toil != null && toil.defaultCompleteMode != ToilCompleteMode.PatherArrival
                    && toil.defaultCompleteMode != ToilCompleteMode.Instant;
            }
            catch { return false; }
        }

        static void SetAim(ref Snapshot state, Vector3 source, Vector3 target, float sourceHeight)
        {
            Vector3 delta = target - new Vector3(source.x, sourceHeight, source.z);
            float horizontal = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            if (horizontal <= .001f) return;
            state.FacingYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            state.AimPitch = Mathf.Clamp(-Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg, -70, 70);
        }

        static int Age(int tick, int start) => start == int.MinValue || start > tick ? int.MaxValue : tick - start;
        internal static float SafeSpeed(float speed) => float.IsNaN(speed) || float.IsInfinity(speed) ? 0 : Mathf.Clamp(speed, 0, 12);
        internal static float AdvancePhase(float phase, float speed, int ticks)
        {
            if (float.IsNaN(phase) || float.IsInfinity(phase)) phase = 0;
            return (phase + SafeSpeed(speed) * Mathf.Clamp(ticks, 0, 30) / 60f * 7f) % (Mathf.PI * 2f);
        }
        internal static float RecoilAt(int age) { if (age < 0) return 0; float amount = Mathf.Clamp01(1f - age / 10f); return amount * amount; }
        internal static float SwingAt(int age) => age < 0 || age >= 18 ? 0 : Mathf.Sin(Mathf.PI * age / 18f);
        static float FiniteOrZero(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        internal static float HurtAt(int age, float strength) => age < 0 ? 0
            : Mathf.Clamp01(FiniteOrZero(strength)) * Mathf.Clamp01(1f - age / 12f);

        internal static void NotifyShot(Pawn pawn)
        {
            if (!VisibleSessionPawn(pawn)) return;
            Observation observation = Observe(pawn, Find.TickManager.TicksGame);
            observation.ShotTick = Find.TickManager.TicksGame;
            observation.SampleTick = -1;
        }
        internal static void NotifyMelee(Pawn pawn, Thing target)
        {
            if (!VisibleSessionPawn(pawn) || target == null) return;
            Observation observation = Observe(pawn, Find.TickManager.TicksGame);
            observation.MeleeTick = Find.TickManager.TicksGame;
            observation.MeleeTarget = target.DrawPos;
            observation.MeleeTarget.y = target is Pawn other ? NativeScene.GetPawnHeight(other) * .65f : .8f;
            observation.SampleTick = -1;
        }
        internal static void NotifyHurt(Pawn pawn, DamageInfo damage, float amount)
        {
            if (!VisibleSessionPawn(pawn) || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)
                || damage.Def == null || !damage.Def.ExternalViolenceFor(pawn)) return;
            Observation observation = Observe(pawn, Find.TickManager.TicksGame);
            observation.HurtTick = Find.TickManager.TicksGame;
            observation.HurtStrength = Mathf.Clamp(amount / 15f, .15f, 1f);
            observation.SampleTick = -1;
        }
        internal static void ObserverError(Exception error)
        {
            if (warnedObserver) return;
            warnedObserver = true;
            Log.Warning("[RimFront] A visual pawn animation observation was skipped: " + error.Message);
        }
        static bool VisibleSessionPawn(Pawn pawn) => NativeView.Active && pawn?.Spawned == true && !pawn.Dead
            && pawn.Map == NativeView.ControlledPawn?.Map && Find.TickManager != null
            && pawn.Position.DistanceToSquared(NativeView.ControlledPawn.Position) <= 32 * 32;

        internal static NativePawnRig.Pose Evaluate(NativePawnRig.Form form, Snapshot state, NativePawnRig.Part part)
        {
            state.Phase = FiniteOrZero(state.Phase); state.IdlePhase = FiniteOrZero(state.IdlePhase);
            state.AimPitch = Mathf.Clamp(FiniteOrZero(state.AimPitch), -70, 70);
            state.Recoil = Mathf.Clamp01(FiniteOrZero(state.Recoil));
            state.Melee = Mathf.Clamp01(FiniteOrZero(state.Melee)); state.MeleeProgress = Mathf.Clamp01(FiniteOrZero(state.MeleeProgress));
            state.Hurt = Mathf.Clamp01(FiniteOrZero(state.Hurt));
            var pose = new NativePawnRig.Pose();
            if (state.Dead) return pose;
            if (state.Lying || state.Downed || state.Sleeping)
            {
                if (part == NativePawnRig.Part.Body) pose.Offset.y = Mathf.Sin(state.IdlePhase * .65f) * (state.Sleeping ? .006f : .003f);
                if (part == NativePawnRig.Part.LeftArm) pose.Euler = new Vector3(-12, 0, -18);
                if (part == NativePawnRig.Part.RightArm) pose.Euler = new Vector3(-12, 0, 18);
                return pose;
            }
            float stride = Mathf.Sin(state.Phase), opposing = -stride;
            float intensity = state.Moving ? Mathf.Clamp01(SafeSpeed(state.Speed) / 4.5f) : 0;
            float amplitude = intensity * (state.Running ? 34 : 25);
            float breath = Mathf.Sin(state.IdlePhase);
            bool quadruped = form == NativePawnRig.Form.Quadruped || form == NativePawnRig.Form.Insect
                || form == NativePawnRig.Form.MechCrawler || form == NativePawnRig.Form.MechCentipede;
            bool biped = !quadruped && form != NativePawnRig.Form.Serpent;
            switch (part)
            {
                case NativePawnRig.Part.Body:
                    pose.Offset.y = state.Moving ? Mathf.Abs(stride) * .018f * intensity : breath * .005f;
                    pose.Euler.x = state.Moving ? (state.Running ? 7 : 3) * intensity : 0;
                    pose.Euler.y = state.Melee * -12;
                    if (form == NativePawnRig.Form.Serpent || form == NativePawnRig.Form.Entity)
                        pose.Euler.z += Mathf.Sin(state.Phase) * intensity * 7;
                    if (quadruped || form == NativePawnRig.Form.Serpent)
                    { pose.Offset.z += state.Melee * .09f; pose.Euler.x -= state.Melee * 8; }
                    pose.Offset.z -= state.Recoil * .025f;
                    pose.Offset.z -= state.Hurt * .035f;
                    pose.Euler.x -= state.Hurt * 9;
                    break;
                case NativePawnRig.Part.Head:
                    pose.Euler.x = state.Aiming ? state.AimPitch * .45f : breath * 1.2f;
                    if (state.Ingesting) pose.Euler.x = 13 + Mathf.Sin(state.IdlePhase * 2) * 5;
                    if (quadruped || form == NativePawnRig.Form.Serpent || form == NativePawnRig.Form.Bird)
                        pose.Euler.x -= state.Melee * 20;
                    pose.Euler.x -= state.Hurt * 8;
                    break;
                case NativePawnRig.Part.LeftLeg: case NativePawnRig.Part.RightLeg:
                    float leg = part == NativePawnRig.Part.LeftLeg ? stride : opposing;
                    pose.Euler.x = leg * amplitude;
                    // Positive lift only: the swing foot clears the ground, planted foot stays put.
                    pose.Offset.y = Mathf.Max(0, leg) * intensity * (quadruped ? .025f : .035f);
                    if (form == NativePawnRig.Form.Bird) pose.Euler.x *= 1.3f;
                    if (form == NativePawnRig.Form.Serpent) { pose.Euler = Vector3.zero; pose.Offset = Vector3.zero; }
                    break;
                case NativePawnRig.Part.LeftArm: case NativePawnRig.Part.RightArm:
                    bool left = part == NativePawnRig.Part.LeftArm;
                    float arm = left ? opposing : stride;
                    pose.Euler.x = arm * amplitude * (quadruped ? 1 : .65f);
                    if (quadruped) pose.Offset.y = Mathf.Max(0, arm) * intensity * .025f;
                    if (biped && state.Aiming)
                        pose.Euler = new Vector3(-68 + state.AimPitch - state.Recoil * 9, left ? 16 : -8, left ? -9 : 8);
                    if (biped && state.Carrying) pose.Euler = new Vector3(-68, left ? 16 : -16, 0);
                    if (biped && state.Ingesting) pose.Euler = new Vector3(left ? -38 : -105 + breath * 8, 0, left ? -15 : 15);
                    if (biped && state.Working) pose.Euler = new Vector3(-55 + Mathf.Sin(state.IdlePhase * 2.5f) * (left ? 8 : 24), 0, left ? -8 : 8);
                    if (biped && state.Reloading)
                    {
                        float progress = state.Reload.HasTiming ? Mathf.Clamp01(FiniteOrZero(state.Reload.Progress)) : .3f;
                        pose.Euler = new Vector3(left ? -55 - Mathf.Sin(progress * Mathf.PI) * 25 : -48, left ? 20 : -12, left ? -18 : 12);
                    }
                    if (biped && state.Melee > 0 && !state.Reloading)
                        pose.Euler = left ? new Vector3(-35, 0, -15) : new Vector3(-40 - state.Melee * 75, -35 + state.MeleeProgress * 90, 25);
                    if (form == NativePawnRig.Form.Bird) pose.Euler.z = (left ? -1 : 1) * (10 + intensity * 12 * Mathf.Sin(state.Phase));
                    break;
                case NativePawnRig.Part.Tail:
                    pose.Euler.y = Mathf.Sin(state.Moving ? state.Phase : state.IdlePhase * .5f) * (state.Moving ? 18 : 4);
                    break;
            }
            return pose;
        }

        internal static NativePawnRig.Pose WeaponPose(Snapshot state, bool firstPerson)
        {
            if (state.Lying || state.Dead || state.Downed || state.Sleeping || state.Reloading || state.Melee <= 0) return default;
            float amount = Mathf.Clamp01(FiniteOrZero(state.Melee));
            float progress = Mathf.Clamp01(FiniteOrZero(state.MeleeProgress));
            return new NativePawnRig.Pose(new Vector3(-.12f * amount, .06f * amount, .13f * amount) * (firstPerson ? 1 : .7f),
                new Vector3(-45 * amount, (-25 + progress * 85) * amount, 35 * amount));
        }

        internal static void Dispose() { observations.Clear(); map = null; }
    }

    // Both the vanilla melee verb and CE's override issue this notification after resolving
    // an actual attack (including misses/dodges/parries). Aborted busy casts never reach it.
    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.Notify_MeleeAttackOn))]
    internal static class ObserveNativeMeleeAnimation
    {
        static void Postfix(Pawn ___pawn, Thing __0)
        {
            try { NativePawnAnimation.NotifyMelee(___pawn, __0); }
            catch (Exception error) { NativePawnAnimation.ObserverError(error); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage), new[] { typeof(DamageInfo), typeof(float) })]
    internal static class ObserveNativeHurtAnimation
    {
        static void Postfix(Pawn __instance, DamageInfo __0, float __1)
        {
            try { NativePawnAnimation.NotifyHurt(__instance, __0, __1); }
            catch (Exception error) { NativePawnAnimation.ObserverError(error); }
        }
    }
}
