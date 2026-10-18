using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Presentation-space ray selection only. Native verbs still own range validation,
    // accuracy, interception, damage, ammo and warmup. No faction or hostility filter.
    internal static class NativeAimTargeting
    {
        internal const int MaximumCells = 4096, MaximumThings = 3072, MaximumCached = MaximumThings, MaximumTriangles = 65536;
        internal struct TargetInfo
        {
            internal LocalTargetInfo Target;
            internal Vector3 Point;
            internal float Distance;
            internal bool HitThing, HitSurface;
            internal string Label;
        }
        sealed class Geometry
        {
            internal Thing Thing;
            internal Vector3 Position, Min, Max;
            internal Rot4 Rotation;
            internal int Signature, Used;
            internal Vector3[] Vertices;
            internal int[] Triangles;
        }
        static readonly Dictionary<int, Geometry> cache = new Dictionary<int, Geometry>();
        static readonly HashSet<IntVec3> visited = new HashSet<IntVec3>();
        static readonly HashSet<int> seen = new HashSet<int>();
        static readonly PropertyInfo DoorOpenPct = typeof(Building_Door).GetProperty("OpenPct", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static Map cachedMap;
        static int query;
        static bool warned;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        // Parameter distance uses horizontal tiles, matching native weapon range.
        // The unnormalised direction deliberately preserves that convention.
        internal static bool Ray(float yaw, float pitch, float range, out Vector3 direction, out float limit)
        {
            direction = Vector3.zero; limit = 0;
            if (!Finite(yaw) || !Finite(pitch) || !Finite(range) || range <= 0 || Math.Abs(pitch) >= Math.PI / 2 - .001) return false;
            direction = new Vector3(Mathf.Sin(yaw), -(float)Math.Tan(pitch), Mathf.Cos(yaw));
            // Selection follows the camera range while retaining the native weapon limit.
            limit = Math.Min(range, NativeScene.ViewDistance * Mathf.Cos(pitch));
            return Finite(direction) && limit > .001f;
        }
        internal static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0;
            if (!Finite(origin) || !Finite(direction) || !Finite(a) || !Finite(b) || !Finite(c)) return false;
            Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(direction, ac);
            double determinant = Vector3.Dot(ab, p);
            if (Math.Abs(determinant) < .0000001) return false;
            double inverse = 1 / determinant;
            Vector3 offset = origin - a;
            double u = Vector3.Dot(offset, p) * inverse;
            if (u < -.000001 || u > 1.000001) return false;
            Vector3 q = Vector3.Cross(offset, ab);
            double v = Vector3.Dot(direction, q) * inverse;
            if (v < -.000001 || u + v > 1.000001) return false;
            double t = Vector3.Dot(ac, q) * inverse;
            if (t < .00001 || double.IsNaN(t) || double.IsInfinity(t) || t > float.MaxValue) return false;
            distance = (float)t;
            return true;
        }
        internal static bool Nearer(float candidate, float nearest)
            => Finite(candidate) && candidate >= .00001f && candidate < nearest - .00001f;
        internal static bool RayBox(Vector3 eye, Vector3 direction, Vector3 min, Vector3 max, out float distance)
        {
            bool hit = AimGeometry.RayIntersectsBox(eye.x, eye.y, eye.z, direction.x, direction.y, direction.z,
                min.x, min.y, min.z, max.x, max.y, max.z, out double value);
            distance = (float)value;
            return hit;
        }

        static int Signature(Thing thing)
        {
            unchecked
            {
                int hash = thing.stackCount * 397 ^ thing.def.GetHashCode();
                if (NativeMountains.IsRock(thing)) hash = hash * 31 + NativeMountains.Revision;
                if (thing is Plant plant) hash = hash * 31 + NativeScene.PlantGrowth(plant.Growth).GetHashCode();
                // Stable open/closed leaves retain their geometry across render frames.
                if (thing is Building_Door door) hash = hash * 31 + DoorSignature(DoorOpenPct == null ? (door.Open ? 1f : 0f) : (float)DoorOpenPct.GetValue(door, null));
                return hash;
            }
        }
        internal static int DoorSignature(float openness) => (Finite(openness) ? Mathf.Clamp01(openness) : 0f).GetHashCode();

        // Conservative envelopes of every scenery family currently built by NativeScene
        // and NativeObjectModels, including rotated solar panels, door leaves and turrets.
        // Reject outside this cheap box before allocating CPU geometry for a cache miss.
        internal static Bounds CoarseBounds(Vector3 position, float width, float depth, float yaw, int category)
        {
            width = Finite(width) ? Math.Max(.5f, width) : 1f;
            depth = Finite(depth) ? Math.Max(.5f, depth) : 1f;
            float x = category == 0 ? width * .5f + 1.35f : 1.5f;
            float z = category == 0 ? depth * .5f + 1.35f : 1.5f;
            float sin = Math.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)), cos = Math.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad));
            float halfX = cos * x + sin * z, halfZ = sin * x + cos * z;
            float top = category == 2 ? 1.5f : 4.2f;
            // Large solar silhouettes tilt a depth-scaled panel around a low pivot;
            // their lower corners can extend below the ground plane.
            float bottom = category == 0 ? -.5f - Math.Min(20f, Math.Max(width, depth)) * .12f : -.5f;
            return new Bounds(new Vector3(position.x, (top + bottom) * .5f, position.z), new Vector3(halfX * 2, top - bottom, halfZ * 2));
        }
        internal static Bounds CoarseThingBounds(Thing thing)
        {
            Bounds bounds = CoarseBounds(thing.DrawPos, thing.def.size.x, thing.def.size.z, thing.Rotation.AsAngle,
                thing is Building ? 0 : thing is Plant ? 1 : 2);
            if (NativeMountains.IsRock(thing))
            {
                Vector3 min = bounds.min, max = bounds.max;
                max.y = MountainProfile.MaximumHeight;
                bounds = new Bounds((min + max) * .5f, max - min);
            }
            return bounds;
        }
        static bool CoarseHit(Thing thing, Vector3 eye, Vector3 direction, float nearest)
        {
            Bounds bounds = CoarseThingBounds(thing);
            return RayBox(eye, direction, bounds.min, bounds.max, out float entry) && entry < nearest;
        }
        static Geometry GetGeometry(Thing thing)
        {
            int id = thing.thingIDNumber, signature = Signature(thing);
            Vector3 position = thing.DrawPos;
            if (cache.TryGetValue(id, out Geometry found) && found.Thing == thing && found.Signature == signature
                && found.Position == position && found.Rotation == thing.Rotation)
            { found.Used = query; return found; }
            var builder = new NativeScene.Builder();
            NativeScene.AppendAimGeometry(builder, thing);
            if (builder.Vertices.Count == 0) return null;
            Vector3 min = builder.Vertices[0], max = min;
            foreach (Vector3 vertex in builder.Vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
            var geometry = new Geometry { Thing = thing, Position = position, Rotation = thing.Rotation, Signature = signature, Used = query,
                Vertices = builder.Vertices.ToArray(), Triangles = builder.Triangles.ToArray(), Min = min, Max = max };
            if (!cache.ContainsKey(id) && cache.Count >= MaximumCached)
            {
                int oldestId = 0, oldestUse = int.MaxValue;
                foreach (KeyValuePair<int, Geometry> pair in cache)
                    if (pair.Value.Used < oldestUse) { oldestUse = pair.Value.Used; oldestId = pair.Key; }
                cache.Remove(oldestId);
            }
            cache[id] = geometry;
            return geometry;
        }

        // Shared by the narrowly scoped native projectile compatibility filter. This
        // checks presentation geometry only and does not decide damage eligibility.
        internal static bool TryBounds(Thing thing, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (thing == null || !thing.Spawned || thing is Pawn || thing is Corpse
                || (!(thing is Building) && !(thing is Plant) && thing.def.category != ThingCategory.Item)) return false;
            if (!ReferenceEquals(cachedMap, thing.Map)) { Dispose(); cachedMap = thing.Map; }
            Geometry geometry = GetGeometry(thing);
            if (geometry == null || !Finite(geometry.Min) || !Finite(geometry.Max)) return false;
            bounds = new Bounds((geometry.Min + geometry.Max) * .5f, geometry.Max - geometry.Min);
            return true;
        }

        internal static bool TryHitThing(Thing thing, Vector3 eye, float yaw, float pitch, out double ahead)
        {
            ahead = 0;
            if (thing == null || !thing.Spawned || !Finite(eye) || !Ray(yaw, pitch, NativeScene.ViewDistance, out Vector3 direction, out float unused)) return false;
            if (!ReferenceEquals(cachedMap, thing.Map)) { Dispose(); cachedMap = thing.Map; }
            if (thing is Pawn pawn) return NativePawns.TryAimPawn(pawn, thing.DrawPos, false, eye, yaw, pitch, out ahead);
            if (thing is Corpse corpse && corpse.InnerPawn != null) return NativePawns.TryAimPawn(corpse.InnerPawn, thing.DrawPos, true, eye, yaw, pitch, out ahead);
            if (!(thing is Building) && !(thing is Plant) && thing.def.category != ThingCategory.Item) return false;
            if (!CoarseHit(thing, eye, direction, float.PositiveInfinity)) return false;
            Geometry geometry = GetGeometry(thing);
            if (geometry == null || !RayBox(eye, direction, geometry.Min, geometry.Max, out float broad)) return false;
            float nearest = float.PositiveInfinity;
            int limit = Math.Min(geometry.Triangles.Length, MaximumTriangles * 3);
            for (int i = 0; i + 2 < limit; i += 3)
                if (RayTriangle(eye, direction, geometry.Vertices[geometry.Triangles[i]], geometry.Vertices[geometry.Triangles[i + 1]],
                    geometry.Vertices[geometry.Triangles[i + 2]], out float hit) && Nearer(hit, nearest)) nearest = hit;
            ahead = nearest;
            return Finite(nearest);
        }

        internal static bool TrySelect(Pawn shooter, float yaw, float pitch, float maxRange, out TargetInfo result)
        {
            result = default(TargetInfo);
            if (shooter == null || !shooter.Spawned || !Ray(yaw, pitch, maxRange, out Vector3 direction, out float limit)) return false;
            try
            {
                Map map = shooter.Map;
                if (!ReferenceEquals(cachedMap, map)) { Dispose(); cachedMap = map; }
                query++; visited.Clear(); seen.Clear();
                Vector3 eye = shooter.DrawPos; eye.y = NativeView.EyeHeight;
                if (NativeView.Active && NativeView.ControlledPawn == shooter) eye = NativeView.RenderPosition;
                if (!Finite(eye)) return false;
                IntVec3 first = eye.ToIntVec3();
                if (!first.InBounds(map) || first.Fogged(map)) return false;
                float nearest = limit + .0001f, boundary = limit;
                Thing selected = null;
                bool surface = false;
                int triangleBudget = MaximumTriangles;
                int x = first.x, z = first.z, stepX = direction.x >= 0 ? 1 : -1, stepZ = direction.z >= 0 ? 1 : -1;
                float deltaX = Math.Abs(direction.x) < .0000001 ? float.PositiveInfinity : Math.Abs(1 / direction.x);
                float deltaZ = Math.Abs(direction.z) < .0000001 ? float.PositiveInfinity : Math.Abs(1 / direction.z);
                float nextX = float.IsInfinity(deltaX) ? deltaX : ((stepX > 0 ? x + 1 : x) - eye.x) / direction.x;
                float nextZ = float.IsInfinity(deltaZ) ? deltaZ : ((stepZ > 0 ? z + 1 : z) - eye.z) / direction.z;
                float entry = 0;
                for (int segment = 0; segment < 256 && entry <= limit && entry <= nearest; segment++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map) || cell.Fogged(map)) { boundary = Math.Max(0, entry - .002f); break; }
                    float exit = Math.Min(limit, Math.Min(nextX, nextZ));
                    if (direction.y < -.000001f)
                    {
                        float floor = (.025f - eye.y) / direction.y;
                        if (floor >= entry - .00001f && floor <= exit + .00001f && Nearer(floor, nearest))
                        { nearest = floor; selected = null; surface = true; }
                    }
                    if (map.roofGrid.Roofed(cell) && RayBox(eye, direction, new Vector3(x, 2.43f, z), new Vector3(x + 1, 2.55f, z + 1), out float roof)
                        && Nearer(roof, nearest)) { nearest = roof; selected = null; surface = true; }

                    bool complete = true;
                    // A four-cell margin also catches long lying/corpse silhouettes and
                    // meshes extending beyond their native occupancy cell.
                    for (int dz = -4; dz <= 4 && complete; dz++) for (int dx = -4; dx <= 4 && complete; dx++)
                    {
                        IntVec3 near = new IntVec3(x + dx, 0, z + dz);
                        if (!near.InBounds(map) || near.Fogged(map) || !visited.Add(near)) continue;
                        if (visited.Count > MaximumCells) { complete = false; break; }
                        foreach (Thing thing in map.thingGrid.ThingsListAtFast(near))
                        {
                            if (thing == shooter || !thing.Spawned || thing.Map != map || !seen.Add(thing.thingIDNumber)) continue;
                            if (seen.Count > MaximumThings) { complete = false; break; }
                            if (thing.Position.Fogged(map)) continue;
                            float hit;
                            Pawn pawn = thing as Pawn;
                            Corpse corpse = thing as Corpse;
                            if (pawn != null || corpse?.InnerPawn != null)
                            {
                                // Dynamic bodies use a centre-radius render budget. Static
                                // scenery is chunked: a large building's visible near face
                                // may be within range even when its centre is farther away.
                                Vector3 radial = thing.DrawPos - shooter.DrawPos; radial.y = 0;
                                if (radial.sqrMagnitude > NativeScene.ViewDistanceSquared) continue;
                                Pawn body = pawn ?? corpse.InnerPawn;
                                if (!NativePawns.TryAimPawn(body, thing.DrawPos, corpse != null, eye, yaw, pitch, out double pawnHit)) continue;
                                hit = Math.Max(.001f, (float)pawnHit);
                            }
                            else
                            {
                                if (!(thing is Building) && !(thing is Plant) && thing.def.category != ThingCategory.Item) continue;
                                if (!CoarseHit(thing, eye, direction, nearest)) continue;
                                Geometry geometry = GetGeometry(thing);
                                if (geometry == null || !RayBox(eye, direction, geometry.Min, geometry.Max, out float broad) || broad >= nearest) continue;
                                hit = nearest;
                                for (int index = 0; index + 2 < geometry.Triangles.Length; index += 3)
                                {
                                    if (--triangleBudget < 0) { complete = false; break; }
                                    if (RayTriangle(eye, direction, geometry.Vertices[geometry.Triangles[index]], geometry.Vertices[geometry.Triangles[index + 1]],
                                        geometry.Vertices[geometry.Triangles[index + 2]], out float contact) && Nearer(contact, hit)) hit = contact;
                                }
                            }
                            if (Nearer(hit, nearest)) { nearest = hit; selected = thing; surface = false; }
                            if (!complete) break;
                        }
                    }
                    // Fail closed on unusually dense scenes: never choose through geometry
                    // that the bounded query could not examine.
                    if (!complete) { boundary = Math.Min(boundary, Math.Max(0, entry - .002f)); break; }
                    if (exit >= limit || exit >= nearest) break;
                    if (nextX <= nextZ) { x += stepX; entry = nextX; nextX += deltaX; if (Math.Abs(entry - nextZ) < .000001f) { z += stepZ; nextZ += deltaZ; } }
                    else { z += stepZ; entry = nextZ; nextZ += deltaZ; }
                }
                if (nearest > boundary) { nearest = boundary; selected = null; surface = false; }
                if (!Finite(nearest) || nearest < .0001f) return false;
                Vector3 point = eye + direction * nearest;
                IntVec3 targetCell = point.ToIntVec3();
                if (!targetCell.InBounds(map) || targetCell.Fogged(map)) return false;
                // Cell-based native verbs measure range to cell centres. Bring an empty
                // endpoint inward if rounding alone would put its cell out of range.
                if (selected == null)
                {
                    for (int adjust = 0; adjust < 16 && !CellWithinRange(shooter.Position.x, shooter.Position.z, targetCell.x, targetCell.z, maxRange); adjust++)
                    {
                        nearest -= .1f; if (nearest <= 0) return false;
                        point = eye + direction * nearest; targetCell = point.ToIntVec3();
                    }
                    if (!CellWithinRange(shooter.Position.x, shooter.Position.z, targetCell.x, targetCell.z, maxRange)) return false;
                }
                result = new TargetInfo { Target = selected == null ? new LocalTargetInfo(targetCell) : new LocalTargetInfo(selected), Point = point,
                    Distance = nearest, HitThing = selected != null, HitSurface = surface, Label = selected?.LabelShortCap.ToString() ?? (surface ? "surface" : "aim point") };
                return true;
            }
            catch (Exception error)
            {
                if (!warned) { warned = true; Log.Warning("[RimFront] Free aim selection failed safely: " + error.Message); }
                return false;
            }
        }
        internal static bool CellWithinRange(int originX, int originZ, int targetX, int targetZ, float range)
        {
            double x = (double)targetX - originX, z = (double)targetZ - originZ;
            return Finite(range) && range >= 0 && x * x + z * z <= (double)range * range;
        }
        internal static void Dispose()
        { cache.Clear(); visited.Clear(); seen.Clear(); cachedMap = null; query = 0; }
    }
}
