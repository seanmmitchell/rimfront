using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Borrow the game's dressed, directional atlas frames. Atlas meshes/textures
    // remain game-owned; only our bounded material cache is destroyed on exit.
    internal static class NativePawnSprites
    {
        static readonly MethodInfo UpdateFrame = AccessTools.Method(typeof(PawnRenderer), "GetBlitMeshUpdatedFrame",
            new[] { typeof(PawnTextureAtlasFrameSet), typeof(Rot4), typeof(PawnDrawMode) });
        static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        internal const int MaximumRefreshesPerFrame = 2;
        static int refreshFrame = -1, refreshes;
        static bool warned;
        sealed class Entry
        {
            internal Material Material;
            internal int LastFrame;
            internal Func<PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh> Frame;
        }

        internal static bool TryDraw(Pawn pawn, Vector3 position, bool corpse)
        {
            try
            {
                if (UpdateFrame == null || pawn.Drawer?.renderer == null) return Unavailable();
                if (!GlobalTextureAtlasManager.TryGetPawnFrameSet(pawn, out PawnTextureAtlasFrameSet frames, out bool created)
                    || frames?.atlas == null) return false;
                Entry entry = GetEntry(pawn);
                if (entry == null) return false;
                NativePawnAnimation.Snapshot animation = NativePawnAnimation.Sample(pawn);
                Vector3 towards = NativeView.RenderPosition - position; towards.y = 0;
                float viewerYaw = towards.sqrMagnitude > .000001f
                    ? Mathf.Atan2(towards.x, towards.z) * Mathf.Rad2Deg : NativeView.Yaw * Mathf.Rad2Deg + 180f;
                Rot4 facing = new Rot4(PawnSpriteView.Facing(animation.FacingYaw, viewerYaw));
                int index = frames.GetIndex(facing, PawnDrawMode.BodyAndHead);
                if (!AllowRefresh(frames.isDirty[index], Time.frameCount)) return false;
                // This native call refreshes dirty frames (apparel/health changes)
                // before returning their mesh and UVs. Never modify the borrowed mesh.
                Mesh mesh = entry.Frame(frames, facing, PawnDrawMode.BodyAndHead);
                if (mesh == null) return false;
                Bounds bounds = mesh.bounds;
                if (bounds.size.x <= .001f || bounds.size.z <= .001f) return false;
                entry.Material.mainTexture = frames.atlas;
                NativePawnRig.Form form = NativePawnModels.ClassifyPawn(pawn);
                float scale = NativeScene.PawnScale(pawn);
                float height = NativePawnModels.BaseHeight(form) * scale;
                bool lying = corpse || animation.Lying;
                if (lying) position = NativePawns.TargetPosition(pawn);
                float centerHeight = lying
                    ? NativePawns.LyingRootHeight(NativePawnModels.BaseWidth(form), scale, !corpse && pawn.CurrentBed() != null)
                    : .04f + height * .5f;
                position.y = centerHeight;
                Matrix4x4 root = CardRoot(bounds, position, height, viewerYaw, lying);
                NativeLighting.Draw(mesh, root, entry.Material, position);
                return true;
            }
            catch (Exception error) { return Unavailable(error.GetBaseException().Message); }
        }

        internal static bool AllowRefresh(bool dirty, int frame)
        {
            if (refreshFrame != frame) { refreshFrame = frame; refreshes = 0; }
            if (!dirty) return true;
            if (refreshes >= MaximumRefreshesPerFrame) return false;
            refreshes++;
            return true;
        }

        internal static Matrix4x4 CardRoot(Bounds bounds, Vector3 center, float height, float viewerYaw, bool lying)
        {
            float extent = height / bounds.size.z;
            Quaternion upright = Quaternion.Euler(0, viewerYaw + 180f, 0);
            // Rotate native XZ art into an upright card. Lying characters roll
            // within that card, centered on their bed/body.
            Quaternion roll = Quaternion.Euler(0, 0, lying ? 90f : 0f);
            return Matrix4x4.TRS(center, upright * roll * Quaternion.Euler(-90, 0, 0),
                new Vector3(extent, extent, extent)) * Matrix4x4.Translate(-bounds.center);
        }

        static bool Unavailable(string reason = "native pawn atlas API unavailable")
        {
            if (!warned) { warned = true; Log.Warning("[RimFront] 2D character art fell back to 3D: " + reason); }
            return false;
        }
        static Entry GetEntry(Pawn pawn)
        {
            if (entries.TryGetValue(pawn.thingIDNumber, out Entry entry))
            { entry.LastFrame = Time.frameCount; return entry; }
            if (entries.Count >= NativePawns.MaximumModels)
            {
                int oldestId = -1, oldestFrame = Time.frameCount;
                foreach (var pair in entries)
                    if (pair.Value.LastFrame < oldestFrame) { oldestId = pair.Key; oldestFrame = pair.Value.LastFrame; }
                if (oldestId == -1) return null;
                UnityEngine.Object.Destroy(entries[oldestId].Material); entries.Remove(oldestId);
            }
            var frame = (Func<PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh>)Delegate.CreateDelegate(
                typeof(Func<PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh>), pawn.Drawer.renderer, UpdateFrame);
            entry = new Entry { LastFrame = Time.frameCount, Frame = frame,
                Material = new Material(ShaderDatabase.Cutout) { name = "RimFront borrowed pawn art", color = Color.white } };
            entries.Add(pawn.thingIDNumber, entry);
            return entry;
        }
        internal static void Dispose()
        {
            foreach (Entry entry in entries.Values) UnityEngine.Object.Destroy(entry.Material);
            entries.Clear(); warned = false; refreshFrame = -1; refreshes = 0;
        }
    }
}
