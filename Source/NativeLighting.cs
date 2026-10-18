using System;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Both renderers support vertex colors and per-draw property blocks. Waylith's
    // Unity Light shim does not evaluate lights, so use the game's real glow grid.
    internal static class NativeLighting
    {
        const int FlashCapacity = 16;
        const int CacheSize = 128;
        static readonly FlashState[] flashes = new FlashState[FlashCapacity];
        static readonly CellLight[] cells = new CellLight[CacheSize * CacheSize];
        static readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        static Map map;
        static float now;
        static int nextFlash;

        struct FlashState { internal Vector3 Position; internal float Until, Strength; }
        struct CellLight { internal int X, Z, Frame; internal float Until; internal Color Color, Final; }

        internal static void Begin(Map current)
        {
            if (map != current) { Dispose(); map = current; }
            now = Time.realtimeSinceStartup;
            if (Find.Camera != null && map != null)
            {
                float sky = Mathf.Clamp01(map.skyManager.CurSkyGlow);
                Find.Camera.backgroundColor = Color.Lerp(new Color(.018f, .025f, .045f), new Color(.37f, .46f, .48f), sky);
            }
        }

        internal static Color Compose(float sky, bool roofed, Color lamp)
        {
            float daylight = roofed ? 0f : Mathf.Clamp01(sky);
            // Low ambient keeps silhouettes readable; roof and power state come from RimWorld.
            return new Color(Mathf.Clamp(.11f + daylight * .89f + lamp.r * 3.6f, .11f, 1.2f),
                Mathf.Clamp(.13f + daylight * .87f + lamp.g * 3.6f, .13f, 1.2f),
                Mathf.Clamp(.18f + daylight * .82f + lamp.b * 3.6f, .18f, 1.2f), 1f);
        }

        internal static Color Sample(Vector3 position)
        {
            if (map == null) return Color.white;
            // Mountain exteriors and other surfaces above the native ceiling see sky.
            // Cell fog and roof darkness still apply to the underground scene below.
            if (position.y > 2.55f) return Compose(map.skyManager.CurSkyGlow, false, Color.black);
            int x = Mathf.Clamp(Mathf.FloorToInt(position.x), 0, map.Size.x - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(position.z), 0, map.Size.z - 1);
            int index = (x & (CacheSize - 1)) + (z & (CacheSize - 1)) * CacheSize;
            CellLight cached = cells[index];
            if (cached.Until <= now || cached.X != x || cached.Z != z)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                cached.X = x; cached.Z = z; cached.Until = now + .1f; cached.Frame = -1;
                cached.Color = cell.Fogged(map) ? new Color(.18f, .18f, .18f) :
                    Compose(map.skyManager.CurSkyGlow, map.roofGrid.Roofed(cell), map.glowGrid.VisualGlowAt(cell));
            }
            if (cached.Frame == Time.frameCount) return cached.Final;
            Color color = cached.Color;
            for (int i = 0; i < flashes.Length; i++)
            {
                FlashState flash = flashes[i];
                if (flash.Until <= now) continue;
                float distance = (new Vector3(x + .5f, 1f, z + .5f) - flash.Position).sqrMagnitude;
                if (distance >= 16f) continue;
                // A short LOS check prevents flashes lighting the other side of a wall.
                IntVec3 source = flash.Position.ToIntVec3(), target = new IntVec3(x, 0, z);
                if (!source.InBounds(map) || !GenSight.LineOfSight(source, target, map, true)) continue;
                float strength = flash.Strength * (1f - distance / 16f) * Mathf.Clamp01((flash.Until - now) / .14f);
                color.r += strength; color.g += strength * .72f; color.b += strength * .32f;
            }
            color.a = 1f;
            cached.Frame = Time.frameCount; cached.Final = color; cells[index] = cached;
            return color;
        }

        internal static void Flash(Vector3 position, float strength = 1f)
        {
            if (!NativeView.Active) return;
            flashes[nextFlash] = new FlashState { Position = position, Strength = Mathf.Clamp(strength, 0f, 2f), Until = Time.realtimeSinceStartup + .14f };
            nextFlash = (nextFlash + 1) % flashes.Length;
        }

        internal static bool HasFlashNear(Vector3 center, float radius)
        {
            for (int i = 0; i < flashes.Length; i++)
                if (flashes[i].Until > now && (flashes[i].Position - center).sqrMagnitude < radius * radius) return true;
            return false;
        }

        internal static void Draw(Mesh mesh, Matrix4x4 transform, Material material, Vector3 samplePosition, bool emissive = false)
        {
            if (mesh == null || material == null) return;
            properties.Clear();
            properties.SetColor("_Color", material.color * (emissive ? Color.white : Sample(samplePosition)));
            Graphics.DrawMesh(mesh, transform, material, 0, null, 0, properties);
            NativeScene.CountDraw();
        }

        internal static void Dispose()
        {
            map = null; now = 0; nextFlash = 0;
            Array.Clear(flashes, 0, flashes.Length);
            Array.Clear(cells, 0, cells.Length);
        }
    }
}
