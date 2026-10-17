using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Upright cards borrow native plant art. Only our meshes/materials are owned here;
    // no texture readback, game-material mutation, or simulation random calls.
    internal static class NativeVegetation
    {
        internal const int MaximumCards = 4096, MaximumCandidates = 8192, MaximumBatches = 128;
        internal const float MaximumWidth = 3f, MaximumHeight = 4.1f, RenderRadius = 32f;
        static readonly List<Candidate> candidates = new List<Candidate>();
        static readonly Dictionary<Texture, Batch> batches = new Dictionary<Texture, Batch>();
        static readonly Vector2[] fallbackShape = { new Vector2(-.5f, .25f), new Vector2(-.32f, .75f), new Vector2(0, 1),
            new Vector2(.32f, .75f), new Vector2(.5f, .25f), new Vector2(0, 0) };
        static Vector3 observer;
        static float yaw;
        static int frame;
        static bool warned;
        struct Candidate { internal Plant Plant; internal float Distance; }

        internal sealed class Buffers
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector2> Uv = new List<Vector2>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<int> Triangles = new List<int>();
            internal void Clear() { Vertices.Clear(); Uv.Clear(); Colors.Clear(); Triangles.Clear(); }
        }
        sealed class Batch
        {
            internal readonly Buffers Data = new Buffers();
            internal Mesh Mesh;
            internal Material Material;
            internal int Used;
            internal void Dispose()
            {
                if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
                if (Material != null) UnityEngine.Object.Destroy(Material);
                // Material.mainTexture belongs to the game and is never destroyed.
                Mesh = null; Material = null; Data.Clear();
            }
        }

        internal static void Begin(Vector3 position, float facing)
        {
            observer = position; yaw = facing; frame++;
            candidates.Clear();
            foreach (Batch batch in batches.Values) batch.Data.Clear();
        }
        internal static void Queue(Plant plant, float distanceSquared)
        {
            if (plant == null || !plant.Spawned || !Finite(distanceSquared) || distanceSquared < 0
                || distanceSquared > RenderRadius * RenderRadius || candidates.Count >= MaximumCandidates) return;
            candidates.Add(new Candidate { Plant = plant, Distance = distanceSquared });
        }
        static int Compare(Candidate a, Candidate b)
        {
            int distance = a.Distance.CompareTo(b.Distance);
            return distance != 0 ? distance : a.Plant.thingIDNumber.CompareTo(b.Plant.thingIDNumber);
        }
        internal static void Draw(Map map)
        {
            candidates.Sort(Compare);
            int remaining = MaximumCards;
            foreach (Candidate candidate in candidates)
            {
                if (remaining == 0) break;
                Plant plant = candidate.Plant;
                if (!plant.Spawned || plant.Map != map || plant.Position.Fogged(map)) continue;
                try
                {
                    Vector3 ground = plant.DrawPos; ground.y = .04f;
                    Vector2 size = PlantSize(plant);
                    Material source = null;
                    Texture texture = null;
                    try { source = plant.Graphic?.MatSingleFor(plant); texture = source?.mainTexture; }
                    catch (Exception error)
                    {
                        if (!warned) { warned = true; Log.Warning("[RimFront] Plant art unavailable; using a flat silhouette: " + error.Message); }
                    }
                    bool art = UseNativeArt(source != null, texture != null,
                        texture != null && texture == BaseContent.BadTex);
                    Batch batch = GetBatch(art ? texture : Texture2D.whiteTexture);
                    if (batch == null) continue;
                    Color light = NativeLighting.Sample(ground);
                    if (art)
                    {
                        int start = batch.Data.Uv.Count;
                        AddCard(batch.Data, ground, observer, yaw, size, SafeTint(source.color) * light,
                            (plant.thingIDNumber & 1) != 0);
                        Vector2 scale = source.mainTextureScale, offset = source.mainTextureOffset;
                        if (!Finite(scale.x) || !Finite(scale.y)) scale = Vector2.one;
                        if (!Finite(offset.x) || !Finite(offset.y)) offset = Vector2.zero;
                        for (int i = start; i < batch.Data.Uv.Count; i++)
                        {
                            Vector2 uv = batch.Data.Uv[i];
                            batch.Data.Uv[i] = new Vector2(uv.x * scale.x + offset.x, uv.y * scale.y + offset.y);
                        }
                    }
                    else AddFallback(batch.Data, ground, observer, yaw, size, plant.def.plant.IsTree, light);
                    remaining--;
                }
                catch (Exception error)
                {
                    if (!warned) { warned = true; Log.Warning("[RimFront] Plant art skipped: " + error.Message); }
                }
            }
            foreach (Batch batch in batches.Values)
            {
                Buffers data = batch.Data;
                if (data.Vertices.Count == 0) continue;
                if (batch.Mesh == null) batch.Mesh = new Mesh { name = "RimFront native plant cards" };
                batch.Mesh.Clear();
                batch.Mesh.SetVertices(data.Vertices); batch.Mesh.SetUVs(0, data.Uv);
                batch.Mesh.SetColors(data.Colors); batch.Mesh.SetTriangles(data.Triangles, 0);
                batch.Mesh.RecalculateBounds();
                // Lighting is sampled per plant into vertex colors, not once per batch.
                Graphics.DrawMesh(batch.Mesh, Matrix4x4.identity, batch.Material, 0);
                NativeScene.CountDraw();
            }
            candidates.Clear();
        }

        static Batch GetBatch(Texture texture)
        {
            if (batches.TryGetValue(texture, out Batch found)) { found.Used = frame; return found; }
            if (batches.Count >= MaximumBatches)
            {
                Texture oldest = null; int oldestFrame = frame;
                foreach (var entry in batches)
                    if (entry.Value.Used < oldestFrame) { oldest = entry.Key; oldestFrame = entry.Value.Used; }
                // Never evict geometry already collected this frame.
                if (oldest == null) return null;
                batches[oldest].Dispose(); batches.Remove(oldest);
            }
            var batch = new Batch
            {
                Used = frame,
                Material = new Material(ShaderDatabase.Cutout)
                { name = "RimFront borrowed plant art", mainTexture = texture, color = Color.white }
            };
            batches.Add(texture, batch); return batch;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static Color SafeTint(Color tint) => Finite(tint.r) && Finite(tint.g) && Finite(tint.b) && Finite(tint.a) ? tint : Color.white;
        internal static bool UseNativeArt(bool material, bool texture, bool missingTexture) => material && texture && !missingTexture;

        internal static Vector2 Size(bool tree, float growth, Vector2 drawSize, float visualMin, float visualMax)
        {
            // Native Plant.Print uses drawSize.x and a square sprite. The shared
            // growth step also keys CPU collision geometry; texture state may vary.
            float definitionSize = Finite(drawSize.x) && drawSize.x > 0 ? drawSize.x : tree ? 3.6f : .9f;
            if (!Finite(visualMin) || !Finite(visualMax) || visualMin < 0 || visualMax <= 0 || visualMin > visualMax)
            { visualMin = .3f; visualMax = 1f; }
            float extent = definitionSize * Mathf.Lerp(visualMin, visualMax, NativeScene.PlantGrowth(growth));
            if (!Finite(extent)) extent = tree ? 3.6f : .9f;
            return new Vector2(Mathf.Clamp(extent, .12f, MaximumWidth), Mathf.Clamp(extent, .12f, MaximumHeight));
        }
        internal static Vector2 PlantSize(Plant plant) => Size(plant.def.plant.IsTree, plant.Growth,
            plant.def.graphicData?.drawSize ?? Vector2.one, plant.def.plant.visualSizeRange.min, plant.def.plant.visualSizeRange.max);

        static Vector3 Right(Vector3 ground, Vector3 camera, float fallbackYaw)
        {
            Vector3 towards = camera - ground; towards.y = 0;
            if (!NativeEffectMath.IsFinite(towards) || towards.sqrMagnitude < .000001f)
                towards = new Vector3(-Mathf.Sin(Finite(fallbackYaw) ? fallbackYaw : 0), 0,
                    -Mathf.Cos(Finite(fallbackYaw) ? fallbackYaw : 0));
            towards.Normalize();
            return new Vector3(-towards.z, 0, towards.x);
        }
        internal static void AddCard(Buffers data, Vector3 ground, Vector3 camera, float fallbackYaw,
            Vector2 size, Color tint, bool flip)
        {
            Vector3 right = Right(ground, camera, fallbackYaw) * (size.x * .5f), up = Vector3.up * size.y;
            int first = data.Vertices.Count;
            data.Vertices.Add(ground - right); data.Vertices.Add(ground - right + up);
            data.Vertices.Add(ground + right + up); data.Vertices.Add(ground + right);
            float left = flip ? 1 : 0, opposite = 1 - left;
            data.Uv.Add(new Vector2(left, 0)); data.Uv.Add(new Vector2(left, 1));
            data.Uv.Add(new Vector2(opposite, 1)); data.Uv.Add(new Vector2(opposite, 0));
            for (int i = 0; i < 4; i++) data.Colors.Add(tint);
            DoubleTriangle(data, first, first + 1, first + 2); DoubleTriangle(data, first, first + 2, first + 3);
        }
        static void DoubleTriangle(Buffers data, int a, int b, int c)
        {
            data.Triangles.Add(a); data.Triangles.Add(b); data.Triangles.Add(c);
            data.Triangles.Add(c); data.Triangles.Add(b); data.Triangles.Add(a);
        }
        internal static void AddFallback(Buffers data, Vector3 ground, Vector3 camera, float fallbackYaw,
            Vector2 size, bool tree, Color light)
        {
            // Missing native art gets a small flat silhouette, never an opaque error
            // rectangle or a return to the retired three-dimensional plant models.
            if (tree) AddCard(data, ground, camera, fallbackYaw, new Vector2(size.x * .10f, size.y * .56f),
                new Color(.42f, .29f, .17f) * light, false);
            Vector3 right = Right(ground, camera, fallbackYaw);
            float bottom = tree ? size.y * .28f : 0;
            int first = data.Vertices.Count;
            foreach (Vector2 point in fallbackShape)
            {
                data.Vertices.Add(ground + right * (point.x * size.x) + Vector3.up * (bottom + point.y * (size.y - bottom)));
                data.Uv.Add(Vector2.zero); data.Colors.Add(new Color(.30f, .46f, .24f) * light);
            }
            for (int i = 1; i < fallbackShape.Length - 1; i++) DoubleTriangle(data, first, first + i, first + i + 1);
        }

        internal static void AppendProxy(NativeScene.Builder builder, bool tree, Vector2 size)
        {
            // Stable world-axis volume, never billboard yaw. Alpha holes are not
            // ballistic holes; native plant interception probability still applies.
            if (tree)
            {
                builder.Box(new Vector3(0, size.y * .28f, 0), new Vector3(size.x * .12f, size.y * .56f, size.x * .12f), Color.white);
                builder.Box(new Vector3(0, size.y * .66f, 0), new Vector3(size.x, size.y * .68f, size.x), Color.white);
            }
            else builder.Box(new Vector3(0, size.y * .5f, 0), new Vector3(size.x, size.y, size.x), Color.white);
        }
        internal static void AppendAimGeometry(NativeScene.Builder builder, Plant plant)
        {
            Vector3 origin = builder.Origin; Quaternion rotation = builder.Rotation;
            try
            {
                Vector3 position = plant.DrawPos; position.y = .04f;
                builder.Origin = position; builder.Rotation = Quaternion.identity;
                AppendProxy(builder, plant.def.plant.IsTree, PlantSize(plant));
            }
            finally { builder.Origin = origin; builder.Rotation = rotation; }
        }
        internal static void Dispose()
        {
            foreach (Batch batch in batches.Values) batch.Dispose();
            batches.Clear(); candidates.Clear(); frame = 0; observer = Vector3.zero; yaw = 0;
        }
    }
}
