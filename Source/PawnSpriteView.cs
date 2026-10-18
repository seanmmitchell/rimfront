using System;

namespace RimFront
{
    // Pure viewpoint policy shared by the renderer and portable regression tests.
    internal static class PawnSpriteView
    {
        internal static int Facing(float pawnYaw, float towardsViewerYaw)
        {
            if (float.IsNaN(pawnYaw) || float.IsInfinity(pawnYaw)
                || float.IsNaN(towardsViewerYaw) || float.IsInfinity(towardsViewerYaw)) return 2;
            // Native art: north is the back, south the front. A pawn facing its
            // viewer therefore uses south, independent of the map's north.
            double angle = ((double)pawnYaw - towardsViewerYaw + 180) % 360;
            if (angle < 0) angle += 360;
            return (int)Math.Floor((angle + 45) / 90) % 4;
        }
    }
}
