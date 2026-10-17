using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFront
{
    /// <summary>Read-only presentation of the real reload job. Never starts/completes a job,
    /// loads ammunition, or infers a successful reload from the input key.</summary>
    internal static class NativeReloadAnimation
    {
        internal const int MaximumTrackedPawns = 128;
        static readonly Dictionary<int, Observation> observations = new Dictionary<int, Observation>();
        static readonly PropertyInfo CurrentToil = typeof(JobDriver).GetProperty("CurToil", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly Type AmmoType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
        static readonly PropertyInfo AmmoCount = AmmoType?.GetProperty("CurMagCount");
        static readonly PropertyInfo MagazineSize = AmmoType?.GetProperty("MagSize");
        static readonly PropertyInfo AmmoProperties = AmmoType?.GetProperty("Props");
        static readonly FieldInfo SingleRound = AmmoProperties?.PropertyType.GetField("reloadOneAtATime");

        internal struct Snapshot
        {
            internal bool Active, HasTiming, SingleRound, AmmoIncreased;
            internal float Progress;
            internal int Ammo, Capacity, ElapsedTicks;
        }
        internal struct Pose
        {
            internal Vector3 WeaponOffset, WeaponEuler, FeedOffset, FeedEuler, BoltOffset, LeftHandOffset, LeftHandEuler;
            internal bool ShowFeed;
        }
        sealed class Observation
        {
            internal Job Job;
            internal Thing Weapon;
            internal int Tick = -1, StartTick, PreviousAmmo = -1;
            internal Snapshot Value;
        }

        internal static Snapshot Sample(Pawn pawn)
        {
            Thing weapon = pawn?.equipment?.Primary;
            Job job = pawn?.CurJob;
            JobDriver driver = pawn?.jobs?.curDriver;
            if (pawn == null || weapon == null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
                job?.def?.defName != "ReloadWeapon" || job.targetB.Thing != weapon || driver == null)
            {
                if (pawn != null) observations.Remove(pawn.thingIDNumber);
                return default(Snapshot);
            }
            Observation observation;
            if (!observations.TryGetValue(pawn.thingIDNumber, out observation))
            {
                if (observations.Count >= MaximumTrackedPawns) observations.Clear();
                observation = new Observation(); observations.Add(pawn.thingIDNumber, observation);
            }
            int tick = Find.TickManager.TicksGame;
            if (observation.Job == job && observation.Weapon == weapon && observation.Tick == tick) return observation.Value;
            bool continued = observation.Job == job && observation.Weapon == weapon;
            if (!continued) observation.StartTick = tick;
            Snapshot value = new Snapshot { Active = true, Ammo = -1, Capacity = -1, ElapsedTicks = Math.Max(0, tick - observation.StartTick) };
            try
            {
                // CE sets this duration after applying reload-time, weapon-factor and pawn-speed stats.
                // Reading each actual wait toil naturally restarts the animation for shell-by-shell loads.
                Toil toil = CurrentToil?.GetValue(driver, null) as Toil;
                if (toil != null && toil.defaultCompleteMode == ToilCompleteMode.Delay && toil.defaultDuration > 0)
                {
                    value.HasTiming = true;
                    value.Progress = ComputeProgress(driver.ticksLeftThisToil, toil.defaultDuration);
                }
                else if (continued && observation.Value.HasTiming)
                {
                    // Instant load/jump toils can occupy a render frame: hold the completed phase,
                    // rather than snapping between a reload and the ordinary firing pose.
                    value.HasTiming = true; value.Progress = 1f;
                }
                ThingWithComps equipped = weapon as ThingWithComps;
                if (equipped != null && AmmoType != null)
                    foreach (ThingComp comp in equipped.AllComps)
                    {
                        if (!AmmoType.IsInstanceOfType(comp)) continue;
                        if (AmmoCount?.PropertyType == typeof(int)) value.Ammo = (int)AmmoCount.GetValue(comp, null);
                        if (MagazineSize?.PropertyType == typeof(int)) value.Capacity = (int)MagazineSize.GetValue(comp, null);
                        if (SingleRound?.FieldType == typeof(bool)) value.SingleRound = (bool)SingleRound.GetValue(AmmoProperties.GetValue(comp, null));
                        break;
                    }
            }
            catch
            {
                // Unknown CE/driver internals only affect the presentation: retain a lowered-gun pose.
                value.HasTiming = false; value.Progress = 0;
            }
            value.AmmoIncreased = continued && observation.PreviousAmmo >= 0 && value.Ammo > observation.PreviousAmmo;
            observation.Job = job; observation.Weapon = weapon; observation.Tick = tick;
            observation.PreviousAmmo = value.Ammo; observation.Value = value;
            return value;
        }

        internal static float ComputeProgress(int remaining, int duration)
        {
            if (duration <= 0) return 0;
            return Mathf.Clamp01(1f - (float)Math.Max(0, remaining) / duration);
        }

        static float Smooth(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
        internal static Pose Evaluate(NativeWeapons.WeaponKind kind, float progress, bool singleRound)
        {
            float p = float.IsNaN(progress) || float.IsInfinity(progress) ? 0 : Mathf.Clamp01(progress);
            float handling = Smooth(0, .15f, p) * (1f - Smooth(.86f, 1f, p));
            float exchange = Smooth(.13f, .32f, p) * (1f - Smooth(.62f, .82f, p));
            float bolt = Smooth(.81f, .89f, p) * (1f - Smooth(.92f, 1f, p));
            Pose pose = new Pose
            {
                WeaponOffset = new Vector3(-.045f, -.07f, -.015f) * handling,
                WeaponEuler = new Vector3(10, -12, -28) * handling,
                FeedOffset = new Vector3(-.04f, -.43f, -.06f) * exchange,
                LeftHandOffset = new Vector3(-.03f, -.34f, -.17f) * exchange,
                LeftHandEuler = new Vector3(0, 0, -35) * handling,
                BoltOffset = new Vector3(0, 0, -.13f) * (singleRound ? 0 : bolt),
                ShowFeed = true
            };
            if (kind == NativeWeapons.WeaponKind.Revolver)
            {
                pose.FeedOffset = new Vector3(-.19f, .015f, 0) * handling;
                pose.FeedEuler = new Vector3(0, 0, 65f * handling);
                pose.LeftHandOffset = new Vector3(-.14f, .10f, .07f) * handling;
                pose.LeftHandEuler = new Vector3(0, 20, 35) * exchange;
            }
            else if (kind == NativeWeapons.WeaponKind.Shotgun || singleRound)
            {
                // The cycle is the real current CE delay toil; never loop using wall-clock time.
                pose.FeedOffset = new Vector3(-.14f, -.19f, -.12f) * (1f - Smooth(.20f, .74f, p));
                pose.FeedEuler = new Vector3(0, 0, 25f * handling);
                pose.LeftHandOffset = new Vector3(-.14f, -.17f, -.28f) * handling;
                pose.ShowFeed = p > .10f && p < .79f;
            }
            else if (kind == NativeWeapons.WeaponKind.Launcher)
            {
                pose.FeedOffset = new Vector3(-.15f, -.08f, -.73f) * exchange;
                pose.LeftHandOffset = new Vector3(-.14f, -.05f, -.48f) * exchange;
                pose.WeaponEuler = new Vector3(7, -18, -40) * handling;
            }
            return pose;
        }

        internal static Pose ForSnapshot(NativeWeapons.WeaponKind kind, Snapshot state)
        {
            if (!state.Active) return new Pose { ShowFeed = true };
            if (!state.HasTiming)
                return new Pose { WeaponOffset = new Vector3(0, -.04f, 0), WeaponEuler = new Vector3(8, 0, -18), ShowFeed = true };
            Pose pose = Evaluate(kind, state.Progress, state.SingleRound);
            if (state.SingleRound)
            {
                // Keep the gun open/lowered between individual rounds; only the insertion hand loops.
                float hold = Smooth(0, 8f, state.ElapsedTicks);
                pose.WeaponOffset = new Vector3(-.045f, -.07f, -.015f) * hold;
                pose.WeaponEuler = new Vector3(10, -12, -28) * hold;
                if (kind == NativeWeapons.WeaponKind.Revolver)
                {
                    pose.FeedOffset = new Vector3(-.19f, .015f, 0) * hold;
                    pose.FeedEuler = new Vector3(0, 0, 65f * hold);
                }
                // Cock/pump once on the final known insertion, rather than after every shell.
                if (state.Capacity > 0 && state.Ammo >= state.Capacity - 1)
                    pose.BoltOffset = Evaluate(kind, state.Progress, false).BoltOffset;
            }
            // Once CE has actually inserted ammunition, stop displaying that loose round.
            if (state.SingleRound && state.AmmoIncreased && kind != NativeWeapons.WeaponKind.Revolver) pose.ShowFeed = false;
            return pose;
        }

        internal static void Dispose() { observations.Clear(); }
    }
}
