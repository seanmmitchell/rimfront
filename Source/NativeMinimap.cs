using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Native colony rendering with a north-up pawn/aim overlay.
    internal static class NativeMinimap
    {
        static readonly Color AimColor = new Color(1f, .82f, .25f);
        static Texture texture;
        static Map map;
        static Pawn observer;
        static MinimapView view;
        static Vector3 aimPoint;
        static bool hasAim, hitAim, warned;
        static readonly MinimapRefreshPolicy refresh = new MinimapRefreshPolicy();
        static readonly List<Pawn> pawns = new List<Pawn>(64);
        static readonly List<Color> pawnColors = new List<Color>(64);
        static float nextOverlay;
        static int pawnCursor;

        internal static void OnGUI(Pawn controlled)
        {
            if (controlled == null || !controlled.Spawned || Event.current.type != EventType.Repaint) return;
            if (!(RimFrontMod.Settings?.minimap ?? true)) { if (map != null) Dispose(); return; }
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                if (map != controlled.Map || observer != controlled)
                { Dispose(); map = controlled.Map; observer = controlled; }
                float size = Mathf.Min(260f, UI.screenWidth * .30f, UI.screenHeight - 246f);
                if (size < 96f) return;
                Vector3 position = NativeView.RenderPosition;
                // Allow ordinary movement between refreshes, but never lose the
                // minimum 20-tile neighbourhood after fast travel or a teleport.
                if (Time.realtimeSinceStartup >= nextOverlay)
                {
                    nextOverlay = Time.realtimeSinceStartup + .2f;
                    RefreshOverlay(controlled);
                }
                if (refresh.Due(Time.realtimeSinceStartup))
                {
                    Refresh(controlled);
                }
                // Keep clear of the top HUD and bottom weapon bar, including UI scaling.
                if (texture == null || !view.CoversLocalArea(position.x, position.z)) return;
                Rect panel = new Rect(UI.screenWidth - size - 16f, 82f, size, size + 50f);
                Rect area = new Rect(panel.x + 8f, panel.y + 25f, size - 16f, size - 16f);
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(panel, new Color(.035f, .045f, .05f, .95f));
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(panel.x + 8f, panel.y, size - 16f, 24f), "N ↑   MINIMAP  /  " + Mathf.RoundToInt(view.Size) + " tiles across");
                GUI.DrawTexture(area, texture);
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn other = pawns[i];
                    if (!other.Spawned || other.Map != map || other.Position.Fogged(map)) continue;
                    Vector3 point = other.DrawPos;
                    if (!view.Contains(point.x, point.z)) continue;
                    Vector2 dot = Project(area, point);
                    Widgets.DrawBoxSolid(new Rect(dot.x - 2, dot.y - 2, 4, 4), pawnColors[i]);
                }
                Vector2 player = Project(area, position);
                if (hasAim && view.Contains(aimPoint.x, aimPoint.z))
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

        static void RefreshOverlay(Pawn controlled)
        {
            Verb verb = controlled.equipment?.PrimaryEq?.PrimaryVerb;
            float range = controlled.equipment?.Primary?.def.IsRangedWeapon == true && verb != null
                ? NativeManualAim.SelectionRange(verb) : 2f;
            hasAim = NativeAimTargeting.TrySelect(controlled, NativeView.Yaw, NativeView.PitchDegrees * Mathf.Deg2Rad,
                range, out NativeAimTargeting.TargetInfo aim);
            aimPoint = aim.Point; hitAim = aim.HitThing || aim.HitSurface;
            for (int i = pawns.Count - 1; i >= 0; i--)
                if (!Visible(pawns[i], controlled)) { pawns.RemoveAt(i); pawnColors.RemoveAt(i); }
                else pawnColors[i] = PawnColor(pawns[i], controlled);
            var all = controlled.Map.mapPawns.AllPawnsSpawned;
            // Bounded rotating scan even on colonies with thousands of pawns.
            int count = Math.Min(128, all.Count);
            for (int i = 0; i < count; i++)
            {
                if (pawnCursor >= all.Count) pawnCursor = 0;
                Pawn other = all[pawnCursor++];
                if (pawns.Count < 64 && !pawns.Contains(other) && Visible(other, controlled))
                { pawns.Add(other); pawnColors.Add(PawnColor(other, controlled)); }
            }
        }

        static Color PawnColor(Pawn other, Pawn controlled)
            => NativeTargetIndicators.Tint(NativeTargetIndicators.State(other, controlled), Find.TickManager.TicksGame);

        static bool Visible(Pawn other, Pawn controlled) => other != null && other != controlled && other.Spawned
            && !other.Dead && other.Map == controlled.Map && !other.Position.Fogged(controlled.Map)
            && (other.DrawPos - controlled.DrawPos).sqrMagnitude <= NativeScene.ViewDistanceSquared
            && GenSight.LineOfSight(controlled.Position, other.Position, controlled.Map);

        static void Refresh(Pawn controlled)
        {
            Vector3 position = NativeView.RenderPosition;
            MinimapView requested = MinimapView.Frame(position.x, position.z, hasAim, aimPoint.x, aimPoint.z);
            long started = Stopwatch.GetTimestamp();
            bool success = false;
            try
            {
                success = NativeMapRenderer.TryRender(controlled.Map, requested, out Texture rendered);
                if (success) { texture = rendered; view = requested; }
            }
            finally
            {
                refresh.Complete(Time.realtimeSinceStartup, (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency,
                    success, Find.TickManager.Paused);
            }
        }

        static Vector2 Project(Rect area, Vector3 point)
            => new Vector2(area.x + view.X(point.x) * area.width, area.y + view.Y(point.z) * area.height);

        internal static void Dispose()
        {
            NativeMapRenderer.Dispose();
            texture = null; map = null; observer = null; warned = false;
            refresh.Reset(); nextOverlay = 0; pawnCursor = 0; pawns.Clear(); pawnColors.Clear(); view = default; hasAim = hitAim = false;
        }
    }
}
