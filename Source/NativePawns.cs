using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Appearance changes rebuild bounded cached meshes. Animation changes only transforms.
    internal static class NativePawns
    {
        internal const int MaximumModels = 64, MaximumDetailedPawns = 16, MaximumRebuildsPerFrame = 2;
        static readonly Dictionary<int, Model> models = new Dictionary<int, Model>();
        static readonly Dictionary<NativePawnRig.Form, Model> fallbacks = new Dictionary<NativePawnRig.Form, Model>();
        static Material material;
        static int rebuilds, detailed, partBuilds;
        static bool warned;
        sealed class AimBounds { internal Geometry Geometry; internal NativePawnModels.Appearance Appearance; internal float RefreshAt; }
        static readonly Dictionary<int, AimBounds> aimBounds = new Dictionary<int, AimBounds>();
        static readonly Dictionary<int, int> articulatedLods = new Dictionary<int, int>();
        internal static bool RenderedArticulated(int pawnId, int frame)
            => articulatedLods.TryGetValue(pawnId, out int drawnFrame) && frame >= drawnFrame && frame - drawnFrame <= 1;
        internal static void RecordLod(int pawnId, bool articulated, int frame)
        {
            if (!articulated) { articulatedLods.Remove(pawnId); return; }
            if (!articulatedLods.ContainsKey(pawnId) && articulatedLods.Count >= MaximumModels)
            {
                int oldestId = 0, oldestFrame = int.MaxValue;
                foreach (var pair in articulatedLods)
                    if (pair.Value < oldestFrame) { oldestId = pair.Key; oldestFrame = pair.Value; }
                articulatedLods.Remove(oldestId);
            }
            articulatedLods[pawnId] = frame;
        }
        internal sealed class Geometry
        {
            internal Vector3[] Vertices;
            internal int[] Triangles;
            internal Bounds Bounds;
        }
        internal static Geometry CaptureGeometry(NativeScene.Builder builder)
        {
            if (builder.Vertices.Count == 0) return null;
            Bounds bounds = new Bounds(builder.Vertices[0], Vector3.zero);
            foreach (Vector3 vertex in builder.Vertices) bounds.Encapsulate(vertex);
            return new Geometry { Vertices = builder.Vertices.ToArray(), Triangles = builder.Triangles.ToArray(), Bounds = bounds };
        }
        internal static int CachedModels => models.Count;
        internal static int DetailedPawns => detailed;
        internal static float LyingRootHeight(float bodyWidth, float scale, bool inBed) => (inBed ? .75f : .04f) + bodyWidth * .5f * scale;
        internal static Matrix4x4 BedRoot(Vector3 headSlot, Vector3 footSlot, float bedYaw, float bodyWidth, float scale, float bodyCenter)
        {
            Vector3 center = (headSlot + footSlot) * .5f;
            center.y = LyingRootHeight(bodyWidth, scale, true);
            // Native bed rotation points from the head slot toward the feet. The rig's
            // +Y axis points toward its head and becomes -X after the lying roll.
            return Matrix4x4.TRS(center, Quaternion.Euler(0, bedYaw - 90f, 0) * Quaternion.Euler(0, 0, 90),
                new Vector3(scale, scale, scale)) * Matrix4x4.Translate(new Vector3(0, -bodyCenter, 0));
        }
        internal static float TargetHeight(Pawn pawn)
        {
            NativePawnRig.Form form = NativePawnModels.ClassifyPawn(pawn);
            float scale = NativeScene.PawnScale(pawn);
            bool lying = pawn.Dead || pawn.Downed || pawn.GetPosture().Laying();
            if (!lying) return .04f + NativePawnModels.BaseHeight(form) * scale;
            float width = NativePawnModels.BaseWidth(form);
            return LyingRootHeight(width, scale, pawn.CurrentBed() != null) + width * .5f * scale;
        }
        internal static Vector3 TargetPosition(Pawn pawn)
        {
            Building_Bed bed;
            Vector3 head, feet;
            if (!pawn.Dead && pawn.GetPosture().Laying() && BedSlots(pawn, out bed, out head, out feet))
            {
                Vector3 position = (head + feet) * .5f;
                position.y = pawn.DrawPos.y;
                return position;
            }
            return pawn.DrawPos;
        }
        static bool BedSlots(Pawn pawn, out Building_Bed bed, out Vector3 head, out Vector3 feet)
        {
            bed = pawn.CurrentBed();
            head = feet = Vector3.zero;
            if (bed == null) return false;
            int slot = bed.GetCurOccupantSlotIndex(pawn);
            head = bed.GetSleepingSlotPos(slot).ToVector3Shifted();
            feet = bed.GetFootSlotPos(slot).ToVector3Shifted();
            return true;
        }

        internal static bool TryAimLying(Pawn pawn, Vector3 eye, float yaw, float pitch, out double ahead)
            => TryAimPawn(pawn, pawn.DrawPos, false, eye, yaw, pitch, out ahead);

        internal static bool TryAimPawn(Pawn pawn, Vector3 position, bool corpse, Vector3 eye, float yaw, float pitch, out double ahead)
        {
            ahead = 0;
            if (pawn == null) return false;
            Geometry standing;
            Geometry[] parts = null;
            NativePawnModels.Appearance appearance;
            bool articulate = false;
            if (NativeView.Active)
            {
                Model model = GetModel(pawn);
                standing = model.StandingGeometry; appearance = model.Appearance;
                articulate = !corpse && model.HasParts && RenderedArticulated(pawn.thingIDNumber, Time.frameCount);
                if (articulate) parts = model.PartGeometry;
            }
            else
            {
                // In-flight native shots can outlive the camera. Never recreate GPU
                // resources after leaving the view merely to answer a collision query.
                if (!aimBounds.TryGetValue(pawn.thingIDNumber, out AimBounds entry) || Time.realtimeSinceStartup >= entry.RefreshAt)
                {
                    NativePawnModels.Appearance capturedAppearance = NativePawnModels.Capture(pawn);
                    NativeScene.Builder builder = new NativeScene.Builder();
                    NativePawnModels.BuildStanding(builder, capturedAppearance);
                    Geometry captured = CaptureGeometry(builder);
                    if (captured == null) return false;
                    if (aimBounds.Count >= MaximumModels) aimBounds.Clear();
                    entry = new AimBounds { Geometry = captured, Appearance = capturedAppearance, RefreshAt = Time.realtimeSinceStartup + .5f };
                    aimBounds[pawn.thingIDNumber] = entry;
                }
                standing = entry.Geometry; appearance = entry.Appearance;
            }
            NativePawnAnimation.Snapshot animation = NativePawnAnimation.Sample(pawn);
            position.y = .04f;
            float scale = NativeScene.PawnScale(pawn);
            Matrix4x4 root = corpse || animation.Lying
                ? LyingRoot(pawn, appearance.Form, animation.FacingYaw, corpse, ref position)
                : Matrix4x4.TRS(position, Quaternion.Euler(0, animation.FacingYaw, 0), new Vector3(scale, scale, scale));
            return TryAimGeometry(appearance, animation, standing, parts, articulate, root, eye, yaw, pitch, out ahead);
        }

        internal static Matrix4x4 PartRoot(NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot animation,
            NativePawnRig.Part part, Matrix4x4 root)
        {
            NativePawnRig.Pose pose = NativePawnAnimation.Evaluate(appearance.Form, animation, part);
            return root * Matrix4x4.TRS(NativePawnModels.RestPivot(appearance, part) + pose.Offset, Quaternion.Euler(pose.Euler), Vector3.one);
        }
        internal static bool TryAimGeometry(NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot animation,
            Geometry standing, Geometry[] parts, bool articulated, Matrix4x4 root, Vector3 eye, float yaw, float pitch, out double ahead)
        {
            ahead = 0;
            if (!NativeAimTargeting.Ray(yaw, pitch, NativeScene.ViewDistance, out Vector3 direction, out float unused)) return false;
            float nearest = float.PositiveInfinity;
            if (articulated && parts != null)
            {
                for (int i = 0; i < Math.Min(parts.Length, (int)NativePawnRig.Part.Count); i++)
                    RayMesh(parts[i], PartRoot(appearance, animation, (NativePawnRig.Part)i, root), eye, direction, ref nearest);
            }
            else RayMesh(standing, root, eye, direction, ref nearest);
            ahead = nearest;
            return !float.IsInfinity(nearest);
        }
        static void RayMesh(Geometry geometry, Matrix4x4 transform, Vector3 eye, Vector3 direction, ref float nearest)
        {
            if (geometry == null) return;
            Matrix4x4 inverse = transform.inverse;
            Vector3 localEye = inverse.MultiplyPoint3x4(eye), localDirection = inverse.MultiplyVector(direction);
            if (!NativeAimTargeting.RayBox(localEye, localDirection, geometry.Bounds.min, geometry.Bounds.max, out float broad)
                || broad >= nearest) return;
            // Bounds are rejection only: gaps between limbs and around non-box faces
            // remain empty. Arrays are captured once with the exact rendered mesh.
            for (int i = 0; i + 2 < geometry.Triangles.Length; i += 3)
                if (NativeAimTargeting.RayTriangle(localEye, localDirection, geometry.Vertices[geometry.Triangles[i]],
                    geometry.Vertices[geometry.Triangles[i + 1]], geometry.Vertices[geometry.Triangles[i + 2]], out float hit)
                    && NativeAimTargeting.Nearer(hit, nearest)) nearest = hit;
        }

        internal static bool TryAimBounds(Bounds bounds, Matrix4x4 root, Vector3 eye, float yaw, float pitch, out double ahead)
        {
            ahead = 0;
            if (float.IsNaN(yaw) || float.IsInfinity(yaw) || float.IsNaN(pitch) || float.IsInfinity(pitch)
                || Math.Abs(pitch) >= Math.PI / 2) return false;
            Matrix4x4 inverse = root.inverse;
            Vector3 origin = inverse.MultiplyPoint3x4(eye);
            // A unit horizontal component retains the distance convention of standing aim.
            Vector3 direction = inverse.MultiplyVector(new Vector3(Mathf.Sin(yaw), -(float)Math.Tan(pitch), Mathf.Cos(yaw)));
            Vector3 min = bounds.min, max = bounds.max;
            return AimGeometry.RayIntersectsBox(origin.x, origin.y, origin.z, direction.x, direction.y, direction.z,
                min.x, min.y, min.z, max.x, max.y, max.z, out ahead);
        }

        static Matrix4x4 LyingRoot(Pawn pawn, NativePawnRig.Form form, float yaw, bool corpse, ref Vector3 position)
        {
            bool tall = form == NativePawnRig.Form.Human || form == NativePawnRig.Form.MechBiped;
            float scale = NativeScene.PawnScale(pawn), width = NativePawnModels.BaseWidth(form), center = tall ? .85f : .48f;
            Building_Bed bed;
            Vector3 head, feet;
            if (BedSlots(pawn, out bed, out head, out feet) && !corpse)
            {
                position = (head + feet) * .5f;
                position.y = LyingRootHeight(width, scale, true);
                return BedRoot(head, feet, bed.Rotation.AsAngle, width, scale, center);
            }
            position.y = LyingRootHeight(width, scale, false);
            return Matrix4x4.TRS(position, Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(0, 0, 90),
                new Vector3(scale, scale, scale)) * Matrix4x4.Translate(new Vector3(0, -center, 0));
        }

        sealed class Model
        {
            internal NativePawnModels.Appearance Appearance;
            internal Mesh Standing;
            internal Geometry StandingGeometry;
            internal readonly Mesh[] Parts = new Mesh[(int)NativePawnRig.Part.Count];
            internal readonly Geometry[] PartGeometry = new Geometry[(int)NativePawnRig.Part.Count];
            internal bool HasParts, PartsUnavailable;
            internal float RefreshAt;
            internal int LastFrame;
            internal void Dispose()
            {
                Destroy(Standing); Standing = null; StandingGeometry = null;
                for (int i = 0; i < Parts.Length; i++) { Destroy(Parts[i]); Parts[i] = null; PartGeometry[i] = null; }
                HasParts = PartsUnavailable = false;
            }
        }

        internal static void BeginFrame(Pawn controlled)
        {
            rebuilds = partBuilds = detailed = 0;
            if (material == null) material = new Material(ShaderDatabase.Cutout)
                { name = "RimFront pawn wardrobe", color = Color.white, mainTexture = Texture2D.whiteTexture };
            // First-person skin/clothes get priority over distant model refreshes.
            if (controlled != null) GetModel(controlled);
        }

        internal static NativePawnModels.Appearance Appearance(Pawn pawn) => GetModel(pawn).Appearance;

        static Model GetModel(Pawn pawn)
        {
            Model model;
            float now = Time.realtimeSinceStartup;
            bool exists = models.TryGetValue(pawn.thingIDNumber, out model);
            if (exists) model.LastFrame = Time.frameCount;
            if ((!exists || now >= model.RefreshAt) && rebuilds < MaximumRebuildsPerFrame)
            {
                rebuilds++;
                try
                {
                    NativePawnModels.Appearance appearance = NativePawnModels.Capture(pawn);
                    if (!exists || model.Appearance.StableHash != appearance.StableHash)
                    {
                        NativeScene.Builder builder = new NativeScene.Builder();
                        NativePawnModels.BuildStanding(builder, appearance);
                        Mesh replacement = builder.Create("RimFront dressed pawn " + pawn.thingIDNumber);
                        if (!exists)
                        {
                            if (models.Count >= MaximumModels) EvictOldest();
                            model = new Model(); models.Add(pawn.thingIDNumber, model);
                        }
                        else model.Dispose();
                        model.Appearance = appearance; model.Standing = replacement; model.StandingGeometry = CaptureGeometry(builder);
                    }
                    model.RefreshAt = now + .5f;
                    model.LastFrame = Time.frameCount;
                    return model;
                }
                catch (Exception error)
                {
                    if (!warned) { warned = true; Log.Warning("[RimFront] Pawn appearance used a fallback: " + error.Message); }
                    if (exists) { model.RefreshAt = now + 2f; return model; }
                }
            }
            if (exists) return model;
            NativePawnRig.Form form = NativePawnModels.ClassifyPawn(pawn);
            if (!fallbacks.TryGetValue(form, out model))
            {
                model = new Model { Appearance = NativePawnModels.Fallback(form) };
                NativeScene.Builder builder = new NativeScene.Builder();
                NativePawnModels.BuildStanding(builder, model.Appearance);
                model.Standing = builder.Create("RimFront fallback " + form);
                model.StandingGeometry = CaptureGeometry(builder);
                fallbacks.Add(form, model);
            }
            return model;
        }

        static void EvictOldest()
        {
            int key = 0, oldest = int.MaxValue;
            foreach (KeyValuePair<int, Model> pair in models)
                if (pair.Key != NativeView.ControlledPawn?.thingIDNumber && pair.Value.LastFrame < oldest)
                { oldest = pair.Value.LastFrame; key = pair.Key; }
            models[key].Dispose(); models.Remove(key);
        }

        internal static void Draw(Pawn pawn, Vector3 position, float distanceSquared, bool corpse = false)
        {
            Model model = GetModel(pawn);
            NativePawnAnimation.Snapshot animation = NativePawnAnimation.Sample(pawn);
            bool lying = corpse || animation.Lying;
            float scale = NativeScene.PawnScale(pawn);
            Quaternion rotation = Quaternion.Euler(0, animation.FacingYaw, 0);
            position.y = .04f;
            Matrix4x4 root;
            if (lying)
                root = LyingRoot(pawn, model.Appearance.Form, animation.FacingYaw, corpse, ref position);
            else root = Matrix4x4.TRS(position, rotation, new Vector3(scale, scale, scale));

            bool articulate = (RimFrontMod.Settings?.animatePawns ?? true) && !corpse && !animation.Dead
                && distanceSquared < 14f * 14f && detailed < MaximumDetailedPawns;
            if (articulate && !model.HasParts && !model.PartsUnavailable && partBuilds < 1)
            {
                partBuilds++;
                try
                {
                    for (int i = 0; i < model.Parts.Length; i++)
                    {
                        NativeScene.Builder builder = new NativeScene.Builder();
                        NativePawnModels.BuildPart(builder, model.Appearance, (NativePawnRig.Part)i);
                        model.Parts[i] = builder.Create("RimFront rig " + i);
                        model.PartGeometry[i] = CaptureGeometry(builder);
                    }
                    model.HasParts = true;
                }
                catch (Exception error)
                {
                    for (int i = 0; i < model.Parts.Length; i++) { Destroy(model.Parts[i]); model.Parts[i] = null; model.PartGeometry[i] = null; }
                    model.PartsUnavailable = true;
                    if (!warned) { warned = true; Log.Warning("[RimFront] Pawn articulation used its standing model: " + error.Message); }
                }
            }
            bool drawnArticulated = articulate && model.HasParts;
            RecordLod(pawn.thingIDNumber, drawnArticulated, Time.frameCount);
            if (drawnArticulated)
            {
                detailed++;
                for (int i = 0; i < model.Parts.Length; i++)
                {
                    if (model.Parts[i] == null) continue;
                    NativePawnRig.Part part = (NativePawnRig.Part)i;
                    NativeLighting.Draw(model.Parts[i], PartRoot(model.Appearance, animation, part, root), material, position);
                }
            }
            else NativeLighting.Draw(model.Standing, root, material, position);
            if (!lying && !corpse) NativeWeapons.DrawHeld(pawn, position, animation.FacingYaw, model.Appearance, animation, articulate && model.HasParts);
            if (!lying && !corpse) NativePawnEquipment.Draw(pawn, model.Appearance, animation, root, articulate && model.HasParts);
        }

        static void Destroy(UnityEngine.Object value) { if (value != null) UnityEngine.Object.Destroy(value); }
        internal static void ClearAimCache() { aimBounds.Clear(); articulatedLods.Clear(); }
        internal static void Dispose()
        {
            foreach (Model model in models.Values) model.Dispose();
            foreach (Model model in fallbacks.Values) model.Dispose();
            models.Clear(); fallbacks.Clear(); Destroy(material); material = null;
            ClearAimCache();
            rebuilds = partBuilds = detailed = 0; warned = false;
            NativePawnAnimation.Dispose();
            NativePawnEquipment.Dispose();
        }
    }
}
