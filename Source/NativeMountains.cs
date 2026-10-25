using UnityEngine;
using Verse;

namespace RimFront
{
    internal static class NativeMountains
    {
        static Map map;
        static bool[] rocks;
        static float[] heights;
        static bool dirty;
        static readonly MountainSurfaceCache surfaces = new MountainSurfaceCache();
        internal static int Revision { get; private set; }

        internal static bool IsRock(Thing thing) => thing is Building && thing.def.building != null
            && (thing.def.building.isNaturalRock || thing.def.building.isResourceRock);
        static bool RockAt(Map world, int x, int z)
        {
            var cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(world)) return false;
            foreach (Thing thing in world.thingGrid.ThingsListAtFast(cell))
                if (IsRock(thing)) return true;
            return false;
        }
        internal static void Invalidate(Map world, IntVec3 cell)
        {
            if (map != world || rocks == null || dirty || !cell.InBounds(world)) return;
            if (rocks[cell.z * world.Size.x + cell.x] != RockAt(world, cell.x, cell.z)) dirty = true;
        }
        internal static bool Update(Map world)
        {
            if (map == world && !dirty) return false;
            if (map != world) surfaces.Clear();
            map = world;
            int width = world.Size.x, depth = world.Size.z;
            rocks = new bool[width * depth];
            for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++) rocks[z * width + x] = RockAt(world, x, z);
            heights = MountainProfile.Build(rocks, width, depth);
            dirty = false; Revision++;
            return true;
        }
        internal static MountainSurfaceCache.Snapshot Capture(int x, int z)
            => new MountainSurfaceCache.Snapshot(heights, map.Size.x, map.Size.z, x, z, Revision);
        internal static void Publish(MountainSurfaceCache.Snapshot snapshot) => surfaces.Publish(snapshot);
        internal static void Remove(int x, int z) => surfaces.Remove(x, z);
        internal static int GeometryRevision(Thing thing)
            => map == thing.Map ? surfaces.AtCell(thing.Position.x, thing.Position.z)?.Revision ?? -1 : -1;
        internal static void Add(NativeScene.Builder b, Thing thing, bool fogged = false, MountainSurfaceCache.Snapshot snapshot = null)
        {
            if (RimFrontMultiplayer.InSession) { SimulationGeometry.AddMountain(b, thing, fogged); return; }
            if (map != thing.Map) return;
            int x = thing.Position.x, z = thing.Position.z;
            snapshot = snapshot ?? surfaces.AtCell(x, z);
            if (snapshot == null || !snapshot.Contains(x, z)) return; // A missing chunk has no displayed mountain.
            float a = snapshot.Corner(x, z), c = snapshot.Corner(x + 1, z + 1);
            float d = snapshot.Corner(x, z + 1), e = snapshot.Corner(x + 1, z);
            // Unknown ore and rock types share one exterior tint.
            AddCell(b, x, z, a, e, c, d, fogged ? new Color(.58f, .60f, .53f) : thing.DrawColor);
        }
        internal static void AddCell(NativeScene.Builder b, int x, int z, float h00, float h10, float h11, float h01, Color color)
        {
            Vector3 a = new Vector3(x, h00, z), e = new Vector3(x + 1, h10, z);
            Vector3 c = new Vector3(x + 1, h11, z + 1), d = new Vector3(x, h01, z + 1);
            Vector3 ba = new Vector3(x, 0, z), be = new Vector3(x + 1, 0, z);
            Vector3 bc = new Vector3(x + 1, 0, z + 1), bd = new Vector3(x, 0, z + 1);
            b.Triangle(a, d, c, color); b.Triangle(a, c, e, color * .96f);
            b.Triangle(ba, a, e, color * .78f); b.Triangle(ba, e, be, color * .78f);
            b.Triangle(be, e, c, color * .83f); b.Triangle(be, c, bc, color * .83f);
            b.Triangle(bc, c, d, color * .90f); b.Triangle(bc, d, bd, color * .90f);
            b.Triangle(bd, d, a, color * .68f); b.Triangle(bd, a, ba, color * .68f);
        }
        internal static void Dispose() { map = null; rocks = null; heights = null; dirty = false; Revision = 0; surfaces.Clear(); }
    }
}
