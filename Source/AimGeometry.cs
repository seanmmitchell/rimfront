using System;

namespace RimFront
{
    // Target selection only; native verbs still decide LOS, spread, ballistics and damage.
    internal static class AimGeometry
    {
        // Transform the ray into model space first. Keeping its direction unnormalised
        // preserves the caller's distance units through the inverse model transform.
        internal static bool RayIntersectsBox(double x, double y, double z, double dx, double dy, double dz,
            double minX, double minY, double minZ, double maxX, double maxY, double maxZ, out double distance)
        {
            distance = 0;
            double near = 0, far = double.PositiveInfinity;
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(dx) || !Finite(dy) || !Finite(dz)
                || !Finite(minX) || !Finite(minY) || !Finite(minZ) || !Finite(maxX) || !Finite(maxY) || !Finite(maxZ)
                || minX >= maxX || minY >= maxY || minZ >= maxZ || (dx == 0 && dy == 0 && dz == 0)) return false;
            if (!Slab(x, dx, minX, maxX, ref near, ref far) || !Slab(y, dy, minY, maxY, ref near, ref far)
                || !Slab(z, dz, minZ, maxZ, ref near, ref far) || far <= 0) return false;
            distance = near;
            return true;
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool Slab(double origin, double direction, double min, double max, ref double near, ref double far)
        {
            if (direction == 0) return origin >= min && origin <= max;
            double a = (min - origin) / direction, b = (max - origin) / direction;
            if (a > b) { double swap = a; a = b; b = swap; }
            near = Math.Max(near, a); far = Math.Min(far, b);
            return near <= far;
        }

    }
}
