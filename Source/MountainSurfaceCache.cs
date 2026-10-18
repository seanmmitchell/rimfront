using System.Collections.Generic;

namespace RimFront
{
    // Only published snapshots are eligible for targeting. Capturing a newer field
    // never changes collision until the corresponding chunk mesh has been replaced.
    internal sealed class MountainSurfaceCache
    {
        internal sealed class Snapshot
        {
            internal readonly int X, Z, Revision;
            readonly float[] corners;
            readonly bool[] occupied = new bool[144];
            internal Snapshot(float[] heights, int width, int depth, int x, int z, int revision)
            {
                X = x; Z = z; Revision = revision;
                corners = new float[169];
                for (int dz = 0; dz < 12; dz++) for (int dx = 0; dx < 12; dx++)
                {
                    int cx = x * 12 + dx, cz = z * 12 + dz;
                    occupied[dz * 12 + dx] = cx < width && cz < depth && heights[cz * width + cx] > 0;
                }
                for (int dz = 0; dz <= 12; dz++) for (int dx = 0; dx <= 12; dx++)
                    corners[dz * 13 + dx] = MountainProfile.Corner(heights, width, depth, x * 12 + dx, z * 12 + dz);
            }
            internal bool Contains(int x, int z) => occupied[(z - Z * 12) * 12 + x - X * 12];
            internal float Corner(int x, int z) => corners[(z - Z * 12) * 13 + x - X * 12];
        }
        readonly Dictionary<int, Snapshot> published = new Dictionary<int, Snapshot>();
        static int Key(int x, int z) => x | (z << 16);
        internal Snapshot AtCell(int x, int z)
        { published.TryGetValue(Key(x / 12, z / 12), out Snapshot value); return value; }
        internal void Publish(Snapshot value) => published[Key(value.X, value.Z)] = value;
        internal void Remove(int x, int z) => published.Remove(Key(x, z));
        internal void Clear() => published.Clear();
    }
}
