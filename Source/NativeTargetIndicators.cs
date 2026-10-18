using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    internal static class NativeTargetIndicators
    {
        internal enum Status { Colonist, Friend, Guest, Prisoner, Revolting, Hostile, Neutral }
        internal const int MaximumIndicators = 24, MaximumScanned = 512;
        static readonly List<Pawn> nearby = new List<Pawn>(MaximumScanned);
        static readonly HashSet<Pawn> retained = new HashSet<Pawn>();
        static readonly Comparison<Pawn> byDistance = CompareDistance;
        static readonly IComparer<Pawn> distanceComparer = Comparer<Pawn>.Create(byDistance);
        static readonly Func<Faction, Faction, bool> alliance = (left, right) => left.RelationKindWith(right) == FactionRelationKind.Ally;
        static Pawn observer;
        static Map map;
        static int nextScan, cursor, lastScanFrame = -1;
        static bool warned, scanning;
        static int CompareDistance(Pawn left, Pawn right) =>
            (left.DrawPos - observer.DrawPos).sqrMagnitude.CompareTo((right.DrawPos - observer.DrawPos).sqrMagnitude);

        internal static Status Classify(bool prisoner, bool revolt, bool hostile, bool player, bool ally, bool guest)
        {
            if (revolt) return Status.Revolting;
            if (prisoner) return Status.Prisoner;
            if (hostile) return Status.Hostile;
            if (player) return Status.Colonist;
            if (ally) return Status.Friend;
            return guest ? Status.Guest : Status.Neutral;
        }
        internal static Color Tint(Status status, int tick)
        {
            switch (status)
            {
                case Status.Revolting: return (tick / 12) % 2 == 0 ? new Color(1f, .50f, .08f) : new Color(1f, .12f, .10f);
                case Status.Prisoner: return new Color(1f, .65f, .18f);
                case Status.Hostile: return new Color(1f, .27f, .22f);
                case Status.Colonist: return new Color(.35f, 1f, .55f);
                case Status.Friend: return new Color(.35f, .80f, 1f);
                case Status.Guest: return new Color(.9f, .85f, .55f);
                default: return new Color(.82f, .84f, .86f);
            }
        }
        internal static bool IsDistinctAlly<T>(T faction, T player, Func<T, T, bool> lookup) where T : class
            => faction != null && player != null && !ReferenceEquals(faction, player) && lookup(faction, player);
        internal static Status State(Pawn pawn, Pawn controlled)
        {
            bool ownFaction = pawn.Faction == Faction.OfPlayer;
            return Classify(pawn.IsPrisoner, PrisonBreakUtility.IsPrisonBreaking(pawn), pawn.HostileTo(controlled), pawn.IsColonist,
                ownFaction || IsDistinctAlly(pawn.Faction, Faction.OfPlayer, alliance),
                pawn.HostFaction == Faction.OfPlayer && pawn.guest?.GuestStatus == GuestStatus.Guest);
        }
        internal static int ScanBatchCount(int total, int position, int budget)
            => Math.Max(0, Math.Min(Math.Max(0, budget), Math.Max(0, total) - Math.Max(0, position)));
        internal static bool ScanDue(bool inProgress, int tick, int due) => inProgress || tick >= due;
        static void RefreshCandidates(int tick)
        {
            if (!ScanDue(scanning, tick, nextScan)) return;
            if (!scanning)
            { scanning = true; cursor = 0; nextScan = tick + 6; }
            if (!scanning || lastScanFrame == Time.frameCount) return;
            lastScanFrame = Time.frameCount;
            // Keep earlier batches while examining later ones. Paused large maps finish
            // their first bounded scan over successive frames instead of waiting on ticks.
            for (int i = nearby.Count - 1; i >= 0; i--)
            {
                Pawn pawn = nearby[i];
                if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Map != map || pawn.Position.Fogged(map)
                    || (pawn.DrawPos - observer.DrawPos).sqrMagnitude > 900f)
                { retained.Remove(pawn); nearby.RemoveAt(i); }
            }
            nearby.Sort(byDistance);
            var pawns = map.mapPawns.AllPawnsSpawned;
            int count = ScanBatchCount(pawns.Count, cursor, MaximumScanned);
            for (int i = 0; i < count; i++)
            {
                Pawn candidate = pawns[cursor++];
                if (candidate == observer || candidate.Dead || !candidate.Spawned || candidate.Position.Fogged(map)
                    || retained.Contains(candidate) || (candidate.DrawPos - observer.DrawPos).sqrMagnitude > 900f) continue;
                int index = nearby.BinarySearch(candidate, distanceComparer);
                if (index < 0) index = ~index;
                if (index >= MaximumScanned) continue;
                if (nearby.Count == MaximumScanned) { retained.Remove(nearby[nearby.Count - 1]); nearby.RemoveAt(nearby.Count - 1); }
                nearby.Insert(index, candidate); retained.Add(candidate);
            }
            scanning = cursor < pawns.Count;
        }

        internal static void OnGUI(Pawn controlled)
        {
            if (!(RimFrontMod.Settings?.pawnIndicators ?? true) || controlled?.Spawned != true || !NativeView.Active
                || Find.Camera == null || Find.TickManager == null || Event.current.type != EventType.Repaint) return;
            GameFont oldFont = Text.Font; TextAnchor oldAnchor = Text.Anchor; Color oldColor = GUI.color;
            try
            {
                int tick = Find.TickManager.TicksGame;
                if (observer != controlled || map != controlled.Map)
                { Dispose(); observer = controlled; map = controlled.Map; }
                RefreshCandidates(tick);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                float sx = UI.screenWidth / (float)Math.Max(1, Screen.width), sy = UI.screenHeight / (float)Math.Max(1, Screen.height);
                int shown = 0;
                foreach (Pawn pawn in nearby)
                {
                    if (!pawn.Spawned || pawn.Dead || pawn.Map != map || pawn.Position.Fogged(map)
                        || (pawn.DrawPos - controlled.DrawPos).sqrMagnitude > 900f) continue;
                    Vector3 point = NativePawns.TargetPosition(pawn); point.y = NativePawns.TargetHeight(pawn) + .18f;
                    Vector3 screen = Find.Camera.WorldToScreenPoint(point);
                    if (!NativeEffectMath.IsFinite(screen) || screen.z <= 0) continue;
                    float x = screen.x * sx, y = UI.screenHeight - screen.y * sy;
                    if (x < 0 || x > UI.screenWidth || y < 80 || y > UI.screenHeight - 90) continue;
                    if (!GenSight.LineOfSight(controlled.Position, pawn.Position, map, true)) continue;
                    Status state = State(pawn, controlled);
                    Color color = Tint(state, tick);
                    GUI.color = Color.white;
                    Widgets.DrawBoxSolid(new Rect(x - 75, y - 12, 150, 21), new Color(.025f, .03f, .035f, .80f));
                    Widgets.DrawBoxSolid(new Rect(x - 75, y - 12, 3, 21), color);
                    GUI.color = color;
                    string name = pawn.LabelShortCap.ToString();
                    if (name.Length > 18) name = name.Substring(0, 17) + "…";
                    Widgets.Label(new Rect(x - 70, y - 12, 140, 21), name + " · " + state);
                    if (++shown >= MaximumIndicators) break;
                }
            }
            catch (Exception error)
            {
                if (!warned) { warned = true; Log.Warning("[RimFront] Pawn indicator skipped: " + error.Message); }
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }
        internal static void Dispose() { nearby.Clear(); retained.Clear(); observer = null; map = null; nextScan = cursor = 0; lastScanFrame = -1; scanning = false; }
    }
}
