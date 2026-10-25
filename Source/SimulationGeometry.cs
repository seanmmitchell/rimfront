using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimFront
{
    // Multiplayer collision has one CPU pose, independent of cameras, GPU meshes,
    // animation observation history, atlas availability, and render detail budgets.
    internal static class SimulationGeometry
    {
        internal static Vector3 Position(Thing thing)
        {
            if (thing == null) return Vector3.zero;
            if (thing is Pawn pawn)
            {
                if (AnalogMovement.TryGetPosition(pawn, out var continuous)) return continuous;
                return pawn.pather?.MovingNow == true
                    ? Vector3.Lerp(pawn.Position.ToVector3Shifted(), pawn.pather.nextCell.ToVector3Shifted(), pawn.pather.MovePercentage)
                    : pawn.Position.ToVector3Shifted();
            }
            return GenThing.TrueCenter(thing);
        }
        internal static Vector3 Eye(Pawn pawn)
        {
            Vector3 eye = Position(pawn);
            eye.y = NativeView.IsCrouching(pawn) ? NativeStance.CrouchEyeHeight : NativeView.EyeHeight;
            return eye;
        }
        internal static Vector3 Muzzle(Pawn pawn, Vector3 target)
        {
            Vector3 eye = Eye(pawn);
            var controller = RimFrontGame.For(pawn);
            var weapon = pawn?.equipment?.Primary;
            if (controller == null || weapon == null) return eye;
            var appearance = NativePawnModels.Capture(pawn);
            var hand = NativeWeapons.WeaponHand(appearance);
            if (hand == NativeWeapons.EquipmentHand.None) return eye;
            var kind = NativeWeapons.ClassifyName(weapon.def.defName, weapon.def.IsRangedWeapon);
            Quaternion rotation = Quaternion.Euler(controller.Pitch * Mathf.Rad2Deg, controller.Yaw * Mathf.Rad2Deg, 0);
            Vector3 offset = NativeWeaponBrace.IsMounted(pawn) ? new Vector3(0, -.22f, .52f) : NativeWeapons.FirstPersonOffset(hand, 0);
            Vector3 muzzle = eye + rotation * (offset + Quaternion.Euler(0, -3f, 0) * (NativeWeapons.MuzzleLocal(kind) * .58f));
            Vector3 delta = muzzle - eye;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            if (horizontal < .001f) return eye;
            if (!NativeAimTargeting.TrySelectFrom(pawn, eye, Mathf.Atan2(delta.x, delta.z),
                -Mathf.Atan2(delta.y, horizontal), horizontal, false, out var clearance)) return eye;
            return NativeShotSource.Resolve(eye, muzzle, target, (clearance.Point - eye).magnitude);
        }
        internal static bool TryAimPawn(Pawn pawn, bool corpse, Vector3 eye, float yaw, float pitch, out double ahead)
        {
            var appearance = NativePawnModels.Capture(pawn);
            var builder = new NativeScene.Builder();
            NativePawnModels.BuildStanding(builder, appearance);
            var geometry = NativePawns.CaptureGeometry(builder);
            ahead = 0;
            if (geometry == null) return false;
            float scale = NativeScene.PawnScale(pawn);
            var controller = RimFrontGame.For(pawn);
            float facing = controller == null ? pawn.Rotation.AsAngle : controller.Yaw * Mathf.Rad2Deg;
            Vector3 position = Position(pawn); position.y = .04f;
            var animation = new NativePawnAnimation.Snapshot { FacingYaw = facing,
                Lying = corpse || pawn.Dead || pawn.Downed || pawn.GetPosture().Laying() };
            Matrix4x4 root = animation.Lying ? NativePawns.LyingRoot(pawn, appearance.Form, facing, corpse, ref position)
                : Matrix4x4.TRS(position, Quaternion.Euler(0, facing, 0), new Vector3(scale,
                    NativeStance.VerticalScale(NativePawnModels.BaseHeight(appearance.Form), scale, NativeView.IsCrouching(pawn)), scale));
            return NativePawns.TryAimGeometry(appearance, animation, geometry, null, false, root, eye, yaw, pitch, out ahead);
        }

        sealed class Mountains { internal float[] Heights; }
        static readonly Dictionary<Map, Mountains> mountains = new Dictionary<Map, Mountains>();
        internal static void Clear() => mountains.Clear();
        internal static void Invalidate(Thing thing)
        {
            if (thing?.Map != null && NativeMountains.IsRock(thing)) mountains.Remove(thing.Map);
        }
        internal static void AddMountain(NativeScene.Builder builder, Thing thing, bool fogged)
        {
            Map map = thing.Map;
            if (!mountains.TryGetValue(map, out var data))
            {
                var rocks = new bool[map.Size.x * map.Size.z];
                foreach (var building in map.listerThings.AllThings)
                    if (NativeMountains.IsRock(building)) rocks[building.Position.z * map.Size.x + building.Position.x] = true;
                data = new Mountains { Heights = MountainProfile.Build(rocks, map.Size.x, map.Size.z) };
                mountains[map] = data;
            }
            var snapshot = new MountainSurfaceCache.Snapshot(data.Heights, map.Size.x, map.Size.z,
                thing.Position.x / NativeScene.ChunkSize, thing.Position.z / NativeScene.ChunkSize, 0);
            int x = thing.Position.x, z = thing.Position.z;
            NativeMountains.AddCell(builder, x, z, snapshot.Corner(x, z), snapshot.Corner(x + 1, z),
                snapshot.Corner(x + 1, z + 1), snapshot.Corner(x, z + 1), fogged ? new Color(.58f, .60f, .53f) : thing.DrawColor);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class MultiplayerRockSpawn { static void Postfix(Thing __instance) => SimulationGeometry.Invalidate(__instance); }
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    internal static class MultiplayerRockDespawn { static void Prefix(Thing __instance) => SimulationGeometry.Invalidate(__instance); }
}
