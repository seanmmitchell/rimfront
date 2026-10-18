using System;

namespace RimFront
{
    // Budget the optional map pass separately from input and the aim/player overlay.
    internal sealed class MinimapRefreshPolicy
    {
        internal double Next { get; private set; }
        double interval = .25;
        internal bool Due(double now) => now >= Next;
        internal void Complete(double now, double seconds, bool success, bool paused)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            // At most ~2% of one CPU thread in steady state; back off immediately,
            // recover gradually. Scheduling from completion prevents catch-up bursts.
            double desired = Math.Max(.25, Math.Min(2, seconds / .02));
            interval = Math.Max(desired, interval * .8 + desired * .2);
            Next = now + (success ? Math.Max(interval, paused ? 1 : .25) : 2);
        }
        internal void Reset() { Next = 0; interval = .25; }
    }
}
