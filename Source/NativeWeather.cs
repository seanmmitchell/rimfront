using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Presentation only: weather rates, roofs and local time remain owned by RimWorld.
    internal static class NativeWeather
    {
        internal const int MaximumRain = 96;
        internal const int MaximumSnow = 64;
        const int MaximumParticles = MaximumRain + MaximumSnow;
        static readonly Vector3[] vertices = new Vector3[MaximumParticles * 4];
        static readonly Color[] colors = new Color[MaximumParticles * 4];
        static Mesh precipitation, disc;
        static Material material;
        static Map currentMap;
        static float nextRefresh;
        static int visibleParticles;

        internal static int ParticleCount(float rate, int maximum)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0 || maximum <= 0) return 0;
            return (int)Math.Ceiling(Math.Min(1f, rate) * maximum);
        }

        // A simple celestial cue tied to local day, not a second weather/astronomy simulation.
        internal static Vector3 SunDirection(float dayPercent)
        {
            double angle = (dayPercent - .25) * Math.PI * 2;
            return new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), .18f).normalized;
        }

        internal static void Draw(Map map, Pawn controlled)
        {
            if (map == null || controlled == null || Find.Camera == null) return;
            if (currentMap != map) { Dispose(); currentMap = map; }
            Initialize();
            float rain = map.weatherManager.RainRate, snow = map.weatherManager.SnowRate;
            Camera camera = Find.Camera;
            if (Time.realtimeSinceStartup >= nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + .05f;
                RefreshParticles(map, controlled.DrawPos, camera.transform.right, camera.transform.up, rain, snow);
            }
            if (visibleParticles > 0)
                NativeLighting.Draw(precipitation, Matrix4x4.identity, material, controlled.DrawPos);

            Vector3 direction = SunDirection(GenLocalDate.DayPercent(map));
            bool sun = direction.y > 0;
            if (!sun) direction = -direction;
            // Dense precipitation hides the celestial cue; roofs/buildings occlude via depth.
            if (direction.y > .04f && rain + snow < .25f && (!sun || map.skyManager.CurSkyGlow > .15f))
            {
                float scale = sun ? .70f : .50f;
                Vector3 center = camera.transform.position + direction * 25f;
                skyProperties.Clear();
                skyProperties.SetColor("_Color", sun ? new Color(1f, .88f, .48f) : new Color(.68f, .76f, .9f));
                Graphics.DrawMesh(disc, Matrix4x4.TRS(center, camera.transform.rotation, Vector3.one * scale), material, 0, null, 0, skyProperties);
                NativeScene.CountDraw();
            }
        }

        static readonly MaterialPropertyBlock skyProperties = new MaterialPropertyBlock();

        static void Initialize()
        {
            if (material != null) return;
            material = new Material(ShaderDatabase.Cutout) { name = "RimFront native weather", color = Color.white, mainTexture = Texture2D.whiteTexture };
            int[] indices = new int[MaximumParticles * 6];
            for (int i = 0; i < MaximumParticles; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            precipitation = new Mesh { name = "RimFront bounded precipitation" };
            precipitation.vertices = vertices; precipitation.triangles = indices;
            precipitation.uv = new Vector2[vertices.Length];
            precipitation.colors = colors;
            const int segments = 16;
            var discVertices = new Vector3[segments + 1];
            var discColors = new Color[segments + 1];
            var discIndices = new int[segments * 3];
            discColors[0] = Color.white;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                discVertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                discColors[i + 1] = Color.white;
                discIndices[i * 3] = 0; discIndices[i * 3 + 1] = (i + 1) % segments + 1; discIndices[i * 3 + 2] = i + 1;
            }
            disc = new Mesh { name = "RimFront sun and moon cue" };
            disc.vertices = discVertices; disc.triangles = discIndices; disc.colors = discColors;
            disc.uv = new Vector2[discVertices.Length]; disc.RecalculateBounds();
        }

        static void RefreshParticles(Map map, Vector3 observer, Vector3 cameraRight, Vector3 cameraUp, float rain, float snow)
        {
            Array.Clear(vertices, 0, vertices.Length);
            Array.Clear(colors, 0, colors.Length);
            visibleParticles = 0;
            int rainCount = ParticleCount(rain, MaximumRain), snowCount = ParticleCount(snow, MaximumSnow);
            float time = (Find.TickManager.TicksGame % 360000) / 60f;
            for (int i = 0; i < rainCount + snowCount; i++)
            {
                bool flake = i >= rainCount;
                int seed = flake ? i - rainCount + MaximumRain : i;
                float x = observer.x + Mathf.Repeat(Hash(seed, 17) * 24 - observer.x + 12, 24) - 12;
                float z = observer.z + Mathf.Repeat(Hash(seed, 43) * 24 - observer.z + 12, 24) - 12;
                if (flake) x += Mathf.Sin(time * .7f + seed) * .3f;
                IntVec3 cell = new IntVec3(Mathf.FloorToInt(x), 0, Mathf.FloorToInt(z));
                if (!cell.InBounds(map) || cell.Fogged(map) || map.roofGrid.Roofed(cell)) continue;
                float y = .08f + Mathf.Repeat(Hash(seed, 79) * 9f - time * (flake ? 1.2f : 10f), 9f);
                Vector3 center = new Vector3(x, y, z);
                Vector3 side = cameraRight * (flake ? .035f : .012f);
                Vector3 up = flake ? cameraUp * .035f : new Vector3(-.06f, .29f, 0);
                int offset = visibleParticles++ * 4;
                vertices[offset] = center - side - up; vertices[offset + 1] = center - side + up;
                vertices[offset + 2] = center + side + up; vertices[offset + 3] = center + side - up;
                Color tint = flake ? new Color(.88f, .93f, 1f) : new Color(.44f, .57f, .69f);
                for (int j = 0; j < 4; j++) colors[offset + j] = tint;
            }
            precipitation.vertices = vertices; precipitation.colors = colors; precipitation.RecalculateBounds();
        }

        static float Hash(int seed, uint salt)
        {
            uint value = unchecked((uint)(seed + 1) * 747796405u + salt * 2891336453u);
            value = (value ^ (value >> 16)) * 2246822519u;
            return (value & 65535u) / 65536f;
        }

        internal static void Dispose()
        {
            if (precipitation != null) UnityEngine.Object.Destroy(precipitation);
            if (disc != null) UnityEngine.Object.Destroy(disc);
            if (material != null) UnityEngine.Object.Destroy(material);
            precipitation = null; disc = null; material = null; currentMap = null;
            visibleParticles = 0; nextRefresh = 0;
        }
    }
}
