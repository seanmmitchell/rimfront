using System;

namespace RimFront
{
    // Continuous swept square footprint against solid grid cells. No Unity/game dependencies.
    internal static class AnalogCollision
    {
        internal struct Point
        {
            internal double X, Z;
            internal Point(double x, double z) { X = x; Z = z; }
        }
        const double Epsilon = 1e-10;
        internal static Point Move(double x, double z, double dx, double dz, double radius, Func<int, int, bool> blocked)
        {
            if (!Finite(x) || !Finite(z) || !Finite(dx) || !Finite(dz) || !Finite(radius) || radius <= 0 || radius >= .5 || blocked == null)
                return new Point(x, z);
            // Avoid unbounded scans from malformed input; real movement advances <.5 cells/tick.
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (!Finite(distance) || distance > 64) return new Point(x, z);
            // Game positions are stored as floats after this double-precision sweep. A fraction
            // of the movement time is not a safe separation: small steps round back inside a
            // wall. Reserve at least two float ULPs at these world coordinates, in world units.
            double worldScale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(z)), Math.Max(Math.Abs(x + dx), Math.Abs(z + dz)));
            double skin = Math.Max(.00001, worldScale * 2.384185791015625e-7);
            double originalX = x, originalZ = z;
            if (!RepairRoundingOverlap(ref x, ref z, radius, skin, blocked)) return new Point(originalX, originalZ);
            for (int pass = 0; pass < 4 && Math.Abs(dx) + Math.Abs(dz) > Epsilon; pass++)
            {
                double first = 1, nx = 0, nz = 0;
                int minX = (int)Math.Floor(Math.Min(x, x + dx) - radius), maxX = (int)Math.Floor(Math.Max(x, x + dx) + radius);
                int minZ = (int)Math.Floor(Math.Min(z, z + dz) - radius), maxZ = (int)Math.Floor(Math.Max(z, z + dz) + radius);
                for (int cz = minZ; cz <= maxZ; cz++) for (int cx = minX; cx <= maxX; cx++)
                {
                    if (!blocked(cx, cz)) continue;
                    double left = cx - radius, right = cx + 1 + radius, bottom = cz - radius, top = cz + 1 + radius;
                    // Deep/new geometry overlaps never become permission to move through a wall.
                    if (x > left && x < right && z > bottom && z < top) return new Point(x, z);
                    double tx0, tx1, tz0, tz1;
                    if (!Slab(x, dx, left, right, out tx0, out tx1) || !Slab(z, dz, bottom, top, out tz0, out tz1)) continue;
                    double enter = Math.Max(tx0, tz0), leave = Math.Min(tx1, tz1);
                    if (enter < -Epsilon || leave < Math.Max(enter, 0) || enter > first + Epsilon) continue;
                    double hitX = tx0 >= tz0 - Epsilon ? -Math.Sign(dx) : 0;
                    double hitZ = tz0 >= tx0 - Epsilon ? -Math.Sign(dz) : 0;
                    if (enter < first - Epsilon) { first = Math.Max(0, enter); nx = hitX; nz = hitZ; }
                    else { if (hitX != 0) nx = hitX; if (hitZ != 0) nz = hitZ; }
                }
                if (nx == 0 && nz == 0) { x += dx; z += dz; break; }
                double backoff = Math.Max(nx != 0 ? skin / Math.Abs(dx) : 0, nz != 0 ? skin / Math.Abs(dz) : 0);
                double safe = Math.Max(0, first - backoff);
                x += dx * safe; z += dz * safe;
                dx *= 1 - first; dz *= 1 - first;
                if (nx != 0) dx = 0;
                if (nz != 0) dz = 0;
            }
            return new Point(x, z);
        }
        static bool RepairRoundingOverlap(ref double x, ref double z, double radius, double skin, Func<int,int,bool> blocked)
        {
            // Only small rounding-scale overlaps can be repaired. New walls/pawns overlapping the
            // body by a real distance still block; no teleporting across their occupied cell.
            for (int pass=0; pass<4; pass++)
            {
                bool repaired=false;
                int minX=(int)Math.Floor(x-radius), maxX=(int)Math.Floor(x+radius);
                int minZ=(int)Math.Floor(z-radius), maxZ=(int)Math.Floor(z+radius);
                for (int cz=minZ; cz<=maxZ; cz++) for (int cx=minX; cx<=maxX; cx++)
                {
                    if (!blocked(cx,cz)) continue;
                    double left=cx-radius, right=cx+1+radius, bottom=cz-radius, top=cz+1+radius;
                    if (x<=left || x>=right || z<=bottom || z>=top) continue;
                    double west=x-left, east=right-x, south=z-bottom, north=top-z;
                    double depth=Math.Min(Math.Min(west,east),Math.Min(south,north));
                    if (depth>skin*2) return false;
                    if (depth==west) x=left-skin;
                    else if (depth==east) x=right+skin;
                    else if (depth==south) z=bottom-skin;
                    else z=top+skin;
                    repaired=true;
                }
                if (!repaired) return true;
            }
            return false;
        }
        static bool Slab(double p, double d, double min, double max, out double near, out double far)
        {
            near = double.NegativeInfinity; far = double.PositiveInfinity;
            if (Math.Abs(d) < 1e-12) return p > min && p < max;
            double a = (min - p) / d, b = (max - p) / d;
            near = Math.Min(a, b); far = Math.Max(a, b); return true;
        }
        internal static Point Direction(double yaw, double forward, double strafe)
        {
            if (!Finite(yaw) || !Finite(forward) || !Finite(strafe)) return new Point(0, 0);
            double x = Math.Sin(yaw) * forward + Math.Cos(yaw) * strafe;
            double z = Math.Cos(yaw) * forward - Math.Sin(yaw) * strafe;
            double length = Math.Sqrt(x * x + z * z);
            if (length > 1) { x /= length; z /= length; }
            return new Point(x, z);
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
