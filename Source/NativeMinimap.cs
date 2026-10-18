using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Read-only tactical map: one small texture, no second camera or map renderer.
    internal static class NativeMinimap
    {
        internal const int Resolution = 96, MaximumScannedPawns = 512, MaximumMarkers = 128;
        static readonly Color Fog = new Color(.045f, .055f, .065f);
        static readonly Color Outside = new Color(.015f, .02f, .025f);
        static readonly Color AimColor = new Color(1f, .82f, .25f);
        static readonly Color[] pixels = new Color[Resolution * Resolution];
        static readonly List<Pawn> nearby = new List<Pawn>(MaximumMarkers);
        static readonly Dictionary<TerrainDef, Color> terrainColors = new Dictionary<TerrainDef, Color>();
        static Texture2D texture;
        static Map map;
        static Pawn observer;
        static MinimapView view;
        static Vector3 aimPoint;
        static bool hasAim, hitAim, warned;
        static int scanCursor;
        static float nextRefresh;

        internal static void OnGUI(Pawn controlled)
        {
            if (controlled == null || !controlled.Spawned || Event.current.type != EventType.Repaint) return;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                if (map != controlled.Map || observer != controlled)
                { Dispose(); map = controlled.Map; observer = controlled; }
                Vector3 position = NativeView.RenderPosition;
                // Allow ordinary movement between refreshes, but never lose the
                // minimum 20-tile neighbourhood after fast travel or a teleport.
                bool movedOutside = !view.CoversLocalArea(position.x, position.z);
                if (Time.realtimeSinceStartup >= nextRefresh || texture == null || movedOutside)
                {
                    nextRefresh = Time.realtimeSinceStartup + .2f;
                    Refresh(controlled);
                }
                // Keep clear of the top HUD and bottom weapon bar, including UI scaling.
                float size = Mathf.Min(260f, UI.screenWidth * .30f, UI.screenHeight - 240f);
                if (size < 96f || texture == null) return;
                Rect panel = new Rect(UI.screenWidth - size - 16f, 82f, size, size + 50f);
                Rect area = new Rect(panel.x + 8f, panel.y + 25f, size - 16f, size - 16f);
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(panel, new Color(.035f, .045f, .05f, .95f));
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(panel.x + 8f, panel.y, size - 16f, 24f), "N ↑   MINIMAP  /  " + Mathf.RoundToInt(view.Size) + " tiles across");
                GUI.DrawTexture(area, texture);
                DrawPawns(area, controlled);
                Vector2 player = Project(area, position);
                if (hasAim)
                {
                    Vector2 target = Project(area, aimPoint);
                    Widgets.DrawLine(player, target, AimColor, 1f);
                    Widgets.DrawBoxSolid(new Rect(target.x - 4, target.y - 4, 9, 9), Color.black);
                    Widgets.DrawBoxSolid(new Rect(target.x - 3, target.y - 3, 7, 7), AimColor);
                    if (!hitAim) Widgets.DrawBoxSolid(new Rect(target.x - 1, target.y - 1, 3, 3), Color.black);
                }
                float yaw = NativeView.Yaw;
                Vector2 forward = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
                Vector2 right = new Vector2(-forward.y, forward.x);
                Widgets.DrawLine(player, player + forward * 14f, Color.white, 2f);
                Widgets.DrawLine(player + forward * 14f, player + forward * 7f + right * 4f, Color.white, 2f);
                Widgets.DrawLine(player + forward * 14f, player + forward * 7f - right * 4f, Color.white, 2f);
                Widgets.DrawBoxSolid(new Rect(player.x - 4, player.y - 4, 9, 9), Color.black);
                Widgets.DrawBoxSolid(new Rect(player.x - 3, player.y - 3, 7, 7), Color.white);
                Widgets.Label(new Rect(panel.x + 8f, area.yMax + 2f, size - 16f, 22f), "White: you   ·   Gold: aim");
            }
            catch (Exception error)
            {
                if (!warned) { warned = true; Log.Warning("[RimFront] Minimap skipped: " + error.Message); }
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }

        static void Refresh(Pawn controlled)
        {
            Verb verb = controlled.equipment?.PrimaryEq?.PrimaryVerb;
            float range = controlled.equipment?.Primary?.def.IsRangedWeapon == true && verb != null
                ? NativeManualAim.SelectionRange(verb) : 2f;
            hasAim = NativeAimTargeting.TrySelect(controlled, NativeView.Yaw, NativeView.PitchDegrees * Mathf.Deg2Rad,
                range, out NativeAimTargeting.TargetInfo aim);
            aimPoint = aim.Point; hitAim = aim.HitThing || aim.HitSurface;
            Vector3 position = NativeView.RenderPosition;
            view = MinimapView.Frame(position.x, position.z, hasAim, aimPoint.x, aimPoint.z);
            if (texture == null)
                texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
                { name = "RimFront minimap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            // Texture rows increase northwards; GUI projection flips Z for markers.
            for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                {
                    IntVec3 cell = new IntVec3(Mathf.FloorToInt(view.MinX + (x + .5f) * view.Size / Resolution), 0,
                        Mathf.FloorToInt(view.MinZ + (z + .5f) * view.Size / Resolution));
                    pixels[z * Resolution + x] = CellColor(cell);
                }
            texture.SetPixels(pixels); texture.Apply(false, false);
            RefreshPawns();
        }

        static Color CellColor(IntVec3 cell)
        {
            if (!cell.InBounds(map)) return Outside;
            if (cell.Fogged(map)) return Fog;
            Building building = cell.GetEdifice(map);
            if (building is Building_Door door)
                return door.Open ? new Color(.55f, .65f, .52f) : new Color(.73f, .53f, .30f);
            if (building != null) return building.def.passability == Traversability.Impassable
                ? new Color(.70f, .72f, .68f) : new Color(.47f, .53f, .55f);
            TerrainDef terrain = cell.GetTerrain(map);
            if (!terrainColors.TryGetValue(terrain, out Color ground))
            {
                string name = terrain.defName.ToLowerInvariant();
                ground = name.Contains("water") ? new Color(.18f, .36f, .48f)
                    : name.Contains("sand") ? new Color(.60f, .53f, .34f)
                    : name.Contains("soil") || name.Contains("mud") ? new Color(.26f, .33f, .20f)
                    : name.Contains("ice") || name.Contains("snow") ? new Color(.72f, .80f, .82f)
                    : new Color(.39f, .42f, .38f);
                terrainColors[terrain] = ground;
            }
            return Color.Lerp(ground, new Color(.85f, .89f, .94f), Mathf.Clamp01(map.snowGrid.GetDepth(cell) * 3f));
        }

        static bool Visible(Pawn candidate)
        {
            if (candidate == null || candidate == observer || !candidate.Spawned || candidate.Dead || candidate.Map != map
                || candidate.Position.Fogged(map)) return false;
            Vector3 point = candidate.DrawPos;
            return view.Contains(point.x, point.z) && GenSight.LineOfSight(observer.Position, candidate.Position, map, true);
        }
        static void RefreshPawns()
        {
            for (int i = nearby.Count - 1; i >= 0; i--) if (!Visible(nearby[i])) nearby.RemoveAt(i);
            var pawns = map.mapPawns.AllPawnsSpawned;
            int count = Math.Min(MaximumScannedPawns, pawns.Count);
            for (int i = 0; i < count; i++)
            {
                if (scanCursor >= pawns.Count) scanCursor = 0;
                Pawn candidate = pawns[scanCursor++];
                if (nearby.Count < MaximumMarkers && !nearby.Contains(candidate) && Visible(candidate)) nearby.Add(candidate);
            }
        }
        static void DrawPawns(Rect area, Pawn controlled)
        {
            foreach (Pawn candidate in nearby)
            {
                // Recheck visibility so a pawn crossing behind cover/fog is hidden immediately.
                if (!Visible(candidate)) continue;
                Vector2 point = Project(area, candidate.DrawPos);
                if (point.x < area.x + 3 || point.x > area.xMax - 3 || point.y < area.y + 3 || point.y > area.yMax - 3) continue;
                Color color = NativeTargetIndicators.Tint(NativeTargetIndicators.State(candidate, controlled), Find.TickManager.TicksGame);
                Widgets.DrawBoxSolid(new Rect(point.x - 3, point.y - 3, 6, 6), Color.black);
                Widgets.DrawBoxSolid(new Rect(point.x - 2, point.y - 2, 4, 4), color);
            }
        }
        static Vector2 Project(Rect area, Vector3 point)
            => new Vector2(area.x + view.X(point.x) * area.width, area.y + view.Y(point.z) * area.height);

        internal static void Dispose()
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null; map = null; observer = null; nearby.Clear(); terrainColors.Clear();
            scanCursor = 0; nextRefresh = 0; hasAim = hitAim = false;
        }
    }
}
