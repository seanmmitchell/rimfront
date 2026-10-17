using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static partial class Program
{
    static void TestCeTraversal(Assembly assembly)
    {
        Type aim = assembly.GetType("RimFront.NativeManualAim", true);
        MethodInfo traverse = aim.GetMethod("SegmentCells", Flags);
        List<(int x, int z)> Cells(float ax, float az, float bx, float bz)
        {
            object result = traverse.Invoke(null, new[] { V(ax, 1.55f, az), V(bx, .2f, bz) });
            return result == null ? null : ((IEnumerable)result).Cast<object>()
                .Select(c => ((int)Get(c, "x"), (int)Get(c, "z"))).ToList();
        }
        void Verify(float ax, float az, float bx, float bz)
        {
            List<(int x, int z)> actual = Cells(ax, az, bx, bz);
            Check(actual != null, "finite bounded CE segment produces traversal");
            // Independent reference: split the continuous line at every grid-plane
            // crossing, then sample each open interval. This does not reproduce DDA.
            var times = new List<double> { 0, 1 };
            void Planes(double a, double b)
            {
                if (a == b) return;
                for (int plane = (int)Math.Ceiling(Math.Min(a, b)); plane <= Math.Floor(Math.Max(a, b)); plane++)
                {
                    double t = (plane - a) / (b - a);
                    if (t > 0 && t < 1) times.Add(t);
                }
            }
            Planes(ax, bx); Planes(az, bz); times.Sort();
            var expected = new List<(int x, int z)>();
            void Add(double t)
            {
                var cell = ((int)Math.Floor(ax + ((double)bx - ax) * t),
                    (int)Math.Floor(az + ((double)bz - az) * t));
                if (expected.Count == 0 || expected[expected.Count - 1] != cell) expected.Add(cell);
            }
            Add(0);
            for (int i = 1; i < times.Count; i++)
                if (times[i] - times[i - 1] > 1e-12) Add((times[i] + times[i - 1]) / 2);
            Add(1);
            Check(actual.SequenceEqual(expected), $"CE checks fractional segment cells in physical entry order: ({ax},{az})->({bx},{bz})");
            Check(actual.Count == actual.Distinct().Count(), "CE traversal contains no duplicate cells");
            Check(actual.Count <= 2048, "CE traversal respects cell budget");
            Check(actual[0] == ((int)Math.Floor(ax), (int)Math.Floor(az))
                && actual[actual.Count - 1] == ((int)Math.Floor(bx), (int)Math.Floor(bz)),
                "CE traversal includes origin and endpoint cells");
            // Slab entry parameters independently prove physical order, including
            // zero-length boundary contact cells and either axis remaining constant.
            double previous = -1;
            foreach (var cell in actual)
            {
                double enter = 0, exit = 1;
                void Slab(double a, double b, int low)
                {
                    if (a == b) { Check(a >= low && a <= low + 1, "zero-axis traversal stays in its slab"); return; }
                    double first = (low - a) / (b - a), last = (low + 1 - a) / (b - a);
                    enter = Math.Max(enter, Math.Min(first, last));
                    exit = Math.Min(exit, Math.Max(first, last));
                }
                Slab(ax, bx, cell.x); Slab(az, bz, cell.z);
                Check(enter <= exit + 1e-10 && enter >= previous - 1e-10,
                    "CE traversal never visits an uncrossed or farther cell before a nearer cell");
                previous = enter;
            }
        }
        Verify(.1f, .9f, 1.1f, 1.9f);
        Verify(1.1f, 1.9f, .1f, .9f);
        Check(Cells(.1f, .9f, 1.1f, 1.9f).Contains((0, 1)),
            "regression: offset diagonal includes collider cell omitted by native integer-center line");
        foreach (var segment in new[] {
            (1f, 1f, 0f, 0f), (0f, 0f, 1f, 1f), (.5f, .5f, 8.5f, 8.5f),
            (8.5f, 8.5f, .5f, .5f), (2f, 1f, 2f, 6f), (2f, 6f, 2f, 1f),
            (1f, 2f, 6f, 2f), (6f, 2f, 1f, 2f), (.1f, .1f, .1f, .1f),
            (-2.7f, -3.1f, 4.3f, 6.2f), (10.99f, 10.01f, 31.12f, 49.9f),
            (.25f, .75f, 2047.25f, .75f), (2047.25f, .75f, .25f, .75f) })
            Verify(segment.Item1, segment.Item2, segment.Item3, segment.Item4);
        var random = new Random(56031);
        for (int i = 0; i < 96; i++)
        {
            float ax = (float)(random.NextDouble() * 30 - 15), az = (float)(random.NextDouble() * 30 - 15);
            float bx = (float)(random.NextDouble() * 30 - 15), bz = (float)(random.NextDouble() * 30 - 15);
            Verify(ax, az, bx, bz); Verify(bx, bz, ax, az);
        }
        Check(Cells(0, 0, 2048, 0) == null, "over-budget segment does not return a misleading partial traversal");
        Check(Cells(float.NaN, 0, 1, 1) == null && Cells(0, 0, float.PositiveInfinity, 1) == null,
            "nonfinite segments are rejected without invalid cells");
    }
}
