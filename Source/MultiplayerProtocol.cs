using System;
using System.Collections.Generic;

namespace RimFront
{
    // Transport-independent possession ordering. No engine, wall clock, or presentation reads.
    internal sealed class ControlLease
    {
        internal const int LifetimeTicks = 120;
        internal int Owner, PawnId, MapId;
        internal long Generation, Sequence;
        internal int AcceptedTick;

        internal bool Accept(int sender, long generation, long sequence, int tick, InputCommand command)
        {
            if (sender != Owner || generation != Generation || sequence <= Sequence || sequence <= 0
                || command == null || command.action != "input" || !command.IsValid()) return false;
            Sequence = sequence;
            AcceptedTick = tick;
            return true;
        }

        internal bool Expired(int tick) => (long)tick - AcceptedTick >= LifetimeTicks;
    }

    internal sealed class PossessionIndex
    {
        readonly Dictionary<int, ControlLease> pawns = new Dictionary<int, ControlLease>();
        readonly Dictionary<int, ControlLease> owners = new Dictionary<int, ControlLease>();
        internal long NextGeneration = 1;
        internal ControlLease Pawn(int pawn) => pawns.TryGetValue(pawn, out var lease) ? lease : null;
        internal ControlLease Owner(int owner) => owners.TryGetValue(owner, out var lease) ? lease : null;
        internal IEnumerable<ControlLease> All => pawns.Values;

        internal ControlLease Claim(int owner, int pawn, int map, int tick, bool eligible)
        {
            if (owner < 0 || pawn < 0 || map < 0 || !eligible || pawns.ContainsKey(pawn)
                || NextGeneration <= 0 || NextGeneration == long.MaxValue) return null;
            // Validate the destination before releasing the owner's previous pawn.
            var lease = new ControlLease { Owner = owner, PawnId = pawn, MapId = map,
                Generation = NextGeneration++, AcceptedTick = tick };
            if (owners.TryGetValue(owner, out var previous)) pawns.Remove(previous.PawnId);
            pawns.Add(pawn, lease);
            owners[owner] = lease;
            return lease;
        }

        internal bool Release(int owner, int pawn, long generation)
        {
            var lease = Pawn(pawn);
            if (lease == null || lease.Owner != owner || lease.Generation != generation) return false;
            pawns.Remove(pawn);
            owners.Remove(owner);
            return true;
        }

        internal void Restore(IEnumerable<ControlLease> saved, long nextGeneration)
        {
            pawns.Clear(); owners.Clear(); NextGeneration = Math.Max(1, nextGeneration);
            foreach (var lease in saved)
            {
                if (lease == null || lease.Owner < 0 || lease.PawnId < 0 || lease.MapId < 0
                    || lease.Generation <= 0 || lease.Generation == long.MaxValue
                    || pawns.ContainsKey(lease.PawnId) || owners.ContainsKey(lease.Owner))
                    throw new InvalidOperationException("Invalid RimFront possession snapshot.");
                pawns.Add(lease.PawnId, lease); owners.Add(lease.Owner, lease);
                NextGeneration = Math.Max(NextGeneration, lease.Generation + 1);
            }
        }

        internal List<ControlLease> OnMap(int map)
        {
            var result = new List<ControlLease>();
            foreach (var lease in pawns.Values) if (lease.MapId == map) result.Add(lease);
            result.Sort((a, b) => a.PawnId.CompareTo(b.PawnId));
            return result;
        }
    }

    internal sealed class MultiplayerInputPump
    {
        internal const double IntervalSeconds = .05;
        double nextSend, lastSample;
        bool sampled, lastFire, lastCrouch, lastPaused;
        float lastForward, lastStrafe;
        internal long Sequence;
        internal bool ShouldSend(InputCommand command, double now, bool paused)
        {
            if (command == null || !command.IsValid() || double.IsNaN(now) || double.IsInfinity(now)) return false;
            bool edge = !sampled || command.action == "release" || command.fire != lastFire || command.crouch != lastCrouch
                || command.reload || command.cycleFireMode || command.toggleBrace || command.toggleBipod || paused != lastPaused
                || (command.forward == 0 && lastForward != 0) || (command.strafe == 0 && lastStrafe != 0);
            lastSample = now; sampled = true;
            lastFire = command.fire; lastCrouch = command.crouch; lastPaused = paused;
            lastForward = command.forward; lastStrafe = command.strafe;
            if (!edge && now < nextSend) return false;
            nextSend = now + IntervalSeconds;
            return true;
        }
        internal bool Stale(double now) => sampled && (now - lastSample) * 1000 > 350;
        internal void Reset() { sampled = lastFire = lastCrouch = lastPaused = false; lastForward = lastStrafe = 0; nextSend = lastSample = 0; Sequence = 0; }
    }
}
