using System;

namespace RimFront
{
    // Shared by camera, scene, effects and selection. Native weapon ranges stay native.
    internal static class ViewRange
    {
        internal const float BaseDistance = 60f;
        internal const int MaximumChunkRadius = 10, MaximumChunks = (MaximumChunkRadius * 2 + 1) * (MaximumChunkRadius * 2 + 1);
        internal static int Step(int value) => Math.Max(0, Math.Min(2, value));
        internal static float DistanceFor(int step) => BaseDistance * (1f + Step(step) * .5f);
        internal static int ChunkRadiusFor(int step) => (int)Math.Ceiling(DistanceFor(step) / 12f);
    }
}
