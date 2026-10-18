using System;

namespace RimFront
{
    // Pure, deterministic presentation data: no game RNG, terrain or pathing changes.
    internal static class MountainProfile
    {
        internal const float BaseHeight = 2.4f, MaximumHeight = 24f;
        internal const int DensityRadius = 6;

        internal static float Height(float radius, float density)
            => BaseHeight + Math.Min(MaximumHeight - BaseHeight, Math.Max(0, radius) * .8f
                * (float)Math.Sqrt(Math.Max(0, Math.Min(1, density))));

        internal static float[] Build(bool[] rock, int width, int depth)
        {
            if (width <= 0 || depth <= 0 || rock == null || rock.Length != checked(width * depth))
                throw new ArgumentException("Rock grid dimensions do not match.");
            var radius = new float[rock.Length];
            var sums = new int[checked((width + 1) * (depth + 1))];
            int stride = width + 1;
            // Two-pass chamfer distance measures the local radius to exposed rock edges.
            for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
            {
                int i = z * width + x, s = (z + 1) * stride + x + 1;
                sums[s] = (rock[i] ? 1 : 0) + sums[s - 1] + sums[s - stride] - sums[s - stride - 1];
                if (!rock[i]) continue;
                float r = Math.Min(Math.Min(x + 1, width - x), Math.Min(z + 1, depth - z));
                if (x > 0) r = Math.Min(r, radius[i - 1] + 1);
                if (z > 0)
                {
                    r = Math.Min(r, radius[i - width] + 1);
                    if (x > 0) r = Math.Min(r, radius[i - width - 1] + 1.414214f);
                    if (x + 1 < width) r = Math.Min(r, radius[i - width + 1] + 1.414214f);
                }
                radius[i] = r;
            }
            for (int z = depth - 1; z >= 0; z--) for (int x = width - 1; x >= 0; x--)
            {
                int i = z * width + x;
                if (!rock[i]) continue;
                float r = radius[i];
                if (x + 1 < width) r = Math.Min(r, radius[i + 1] + 1);
                if (z + 1 < depth)
                {
                    r = Math.Min(r, radius[i + width] + 1);
                    if (x > 0) r = Math.Min(r, radius[i + width - 1] + 1.414214f);
                    if (x + 1 < width) r = Math.Min(r, radius[i + width + 1] + 1.414214f);
                }
                radius[i] = r;
            }
            var heights = new float[rock.Length];
            int diameter = DensityRadius * 2 + 1;
            for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                if (!rock[i]) continue;
                int x0 = Math.Max(0, x - DensityRadius), x1 = Math.Min(width, x + DensityRadius + 1);
                int z0 = Math.Max(0, z - DensityRadius), z1 = Math.Min(depth, z + DensityRadius + 1);
                int count = sums[z1 * stride + x1] - sums[z0 * stride + x1]
                    - sums[z1 * stride + x0] + sums[z0 * stride + x0];
                heights[i] = Height(radius[i], count / (float)(diameter * diameter));
            }
            return heights;
        }

        // A world-grid corner is identical for both neighboring chunks and materials.
        internal static float Corner(float[] heights, int width, int depth, int x, int z)
        {
            float sum = 0;
            for (int dz = -1; dz <= 0; dz++) for (int dx = -1; dx <= 0; dx++)
            {
                int cx = x + dx, cz = z + dz;
                float value = cx < 0 || cz < 0 || cx >= width || cz >= depth ? 0 : heights[cz * width + cx];
                sum += Math.Max(BaseHeight, value);
            }
            return sum * .25f;
        }
    }
}
