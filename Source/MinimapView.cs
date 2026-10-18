using System;

namespace RimFront
{
    // Engine-independent north-up framing. Never clamp to map edges: doing so
    // would discard the guaranteed neighbourhood on one side of the pawn.
    internal struct MinimapView
    {
        internal const float LocalRadius = 25f, AimPadding = 4f;
        internal float MinX, MinZ, Size;
        internal float X(float worldX) => (worldX - MinX) / Size;
        internal float Y(float worldZ) => 1f - (worldZ - MinZ) / Size;
        internal bool Contains(float x, float z) => X(x) >= 0 && X(x) <= 1 && Y(z) >= 0 && Y(z) <= 1;
        internal bool CoversLocalArea(float pawnX, float pawnZ) => Size > 0
            && Contains(pawnX - 20f, pawnZ - 20f) && Contains(pawnX + 20f, pawnZ + 20f);
        internal static MinimapView Frame(float pawnX, float pawnZ, bool aiming, float aimX, float aimZ)
        {
            float minX = pawnX - LocalRadius, maxX = pawnX + LocalRadius;
            float minZ = pawnZ - LocalRadius, maxZ = pawnZ + LocalRadius;
            if (aiming && Finite(aimX) && Finite(aimZ))
            {
                minX = Math.Min(minX, aimX - AimPadding); maxX = Math.Max(maxX, aimX + AimPadding);
                minZ = Math.Min(minZ, aimZ - AimPadding); maxZ = Math.Max(maxZ, aimZ + AimPadding);
            }
            float size = Math.Max(maxX - minX, maxZ - minZ);
            return new MinimapView { MinX = (minX + maxX - size) * .5f, MinZ = (minZ + maxZ - size) * .5f, Size = size };
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
