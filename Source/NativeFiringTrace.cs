using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;
using Verse.AI;

namespace RimFront
{
    // Diagnostics only: no job/verb/input mutations, and no diagnostic exception may escape.
    internal sealed class NativeFiringTrace
    {
        readonly InputTracker inputs = new InputTracker();
        readonly Gate gate = new Gate();
        static int nextSession;
        bool active, observedNative;
        int session, lastShot;
        Job lastJob;
        Type lastStance;
        Verb lastVerb;
        bool lastReload;
        internal static bool Enabled => RimFrontMod.Settings?.traceFiring ?? false;

        internal void Reset()
        {
            active = observedNative = false;
            inputs.Reset(); gate.Reset();
            lastJob = null; lastStance = null; lastVerb = null;
        }

        bool Ready(Pawn pawn)
        {
            if (!Enabled || pawn == null)
            {
                if (active) Reset();
                return false;
            }
            if (!active)
            {
                active = true; session = ++nextSession;
                Emit(pawn, "trace-enabled", "input baseline follows; press 0 means no observed mouse-down yet");
            }
            return true;
        }

        internal void Input(Pawn pawn, bool physical, bool raw, bool accepted, bool pending, bool gated,
            bool paused, bool reload, bool mode, bool suppressEdge)
        {
            try
            {
                if (!Ready(pawn)) return;
                int change = inputs.Observe(physical, raw, accepted, pending, gated, paused, reload, mode, suppressEdge);
                if (change == 0) return;
                string edge = (change & 8) != 0 ? "baseline" : (change & 2) != 0 ? "down"
                    : (change & 4) != 0 ? "up" : "state";
                Emit(pawn, "input", "edge=" + edge + " physical=" + physical + " raw=" + raw + " accepted=" + accepted
                    + " pending=" + pending + " releaseGate=" + gated + " paused=" + paused
                    + " reloadInput=" + reload + " modeInput=" + mode + " suppressPressEdge=" + suppressEdge);
            }
            catch { }
        }

        internal void Observe(Pawn pawn, Job owned)
        {
            try
            {
                if (!Ready(pawn)) return;
                Job job = pawn.CurJob;
                Type stance = pawn.stances?.curStance?.GetType();
                bool reload = CombatAdapter.IsReloading(pawn);
                Verb verb = owned?.verbToUse ?? pawn.equipment?.PrimaryEq?.PrimaryVerb;
                int shot = verb?.LastShotTick ?? -1;
                if (observedNative && verb == lastVerb && shot > lastShot)
                    Emit(pawn, "shot-observed", "LastShotTick=" + shot + " previous=" + lastShot
                        + " (native evidence; intermediate shots between observations may be coalesced)");
                if (!observedNative || job != lastJob || stance != lastStance || reload != lastReload)
                    Emit(pawn, "native-state", "job=" + (job?.def?.defName ?? "none")
                        + " owned=" + (job != null && job == owned) + " stance=" + (stance?.Name ?? "none")
                        + " reloading=" + reload + " LastShotTick=" + shot);
                observedNative = true; lastJob = job; lastStance = stance; lastReload = reload;
                lastVerb = verb; lastShot = shot;
            }
            catch { }
        }

        internal void Event(Pawn pawn, string kind, string detail)
        {
            try { if (Ready(pawn)) Emit(pawn, kind, detail); }
            catch { }
        }

        void Emit(Pawn pawn, string kind, string detail)
        {
            double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (!gate.Accept(kind, detail, now, out bool notify))
            {
                if (notify) Log.Message("[RimFront:Fire] utc=" + DateTime.UtcNow.ToString("O")
                    + " tick=" + (Find.TickManager?.TicksGame ?? -1) + " session=" + session + " press=" + inputs.PressId
                    + " rate limit reached; further events suppressed until the next one-second window.");
                return;
            }
            string weapon = pawn.equipment?.Primary?.def?.defName ?? "Unarmed";
            string state = "unavailable";
            try
            {
                WeaponState value = CombatAdapter.Describe(pawn);
                state = "mode=" + value.fireMode + " ammo=" + value.ammo + "/" + value.capacity + " status=" + value.status;
            }
            catch { }
            Log.Message("[RimFront:Fire] utc=" + DateTime.UtcNow.ToString("O")
                + " tick=" + (Find.TickManager?.TicksGame ?? -1) + " session=" + session + " press=" + inputs.PressId
                + " pawn=" + pawn.thingIDNumber + " weapon=" + weapon + " " + state + " event=" + kind + " " + detail);
        }

        // Pure observed-input history. A baseline held button is not an observed click.
        internal sealed class InputTracker
        {
            int previous = -1;
            internal int PressId { get; private set; }
            internal void Reset() { previous = -1; PressId = 0; }
            internal int Observe(bool physical, bool raw, bool accepted, bool pending, bool gated,
                bool paused, bool reload, bool mode, bool suppressEdge)
            {
                int bits = (physical ? 1 : 0) | (accepted ? 2 : 0) | (pending ? 4 : 0) | (gated ? 8 : 0)
                    | (paused ? 16 : 0) | (reload ? 32 : 0) | (mode ? 64 : 0) | (suppressEdge ? 128 : 0) | (raw ? 256 : 0);
                if (previous == bits) return 0;
                int result = 1;
                if (previous < 0) result |= 8;
                else if ((previous & 1) != (bits & 1))
                {
                    result |= physical ? 2 : 4;
                    if (physical) PressId++;
                }
                previous = bits;
                return result;
            }
        }

        // Bound both repeated failure messages and total diagnostics, independently of game ticks/pause.
        internal sealed class Gate
        {
            internal const int MaximumPerSecond = 48, MaximumKeys = 32;
            readonly Dictionary<string, Entry> recent = new Dictionary<string, Entry>();
            struct Entry { internal string Detail; internal double Time; }
            double window = double.NaN;
            int count;
            bool notified;
            internal void Reset() { recent.Clear(); window = double.NaN; count = 0; notified = false; }
            internal bool Accept(string kind, string detail, double now, out bool notify)
            {
                notify = false;
                if (double.IsNaN(now) || double.IsInfinity(now) || kind == null) return false;
                if (double.IsNaN(window) || now < window || now - window >= 1)
                { window = now; count = 0; notified = false; }
                if (recent.TryGetValue(kind, out Entry previous) && previous.Detail == detail
                    && now >= previous.Time && now - previous.Time < 1) return false;
                if (count >= MaximumPerSecond)
                { notify = !notified; notified = true; return false; }
                if (!recent.ContainsKey(kind) && recent.Count >= MaximumKeys) recent.Clear();
                recent[kind] = new Entry { Detail = detail, Time = now };
                count++;
                return true;
            }
        }
    }
}
