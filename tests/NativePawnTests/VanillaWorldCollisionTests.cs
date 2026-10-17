using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static partial class Program
{
    static void TestVanillaWorldCollision(Assembly assembly)
    {
        Type aim = assembly.GetType("RimFront.NativeVanillaAim", true);
        Type targeting = assembly.GetType("RimFront.NativeAimTargeting", true);
        MethodInfo earlier = aim.GetMethod("EarlierContact", Flags), triangle = targeting.GetMethod("RayTriangle", Flags);
        bool Earlier(double contact, double nearest, double limit) => (bool)earlier.Invoke(null, new object[] { contact, nearest, limit });
        float Surface(float z, float eyeY = 1.55f, float left = 19f, float right = 22f)
        {
            object[] arguments = { V(20.5f, eyeY, 20.5f), V(0, 0, 1), V(left, .04f, z), V(right, .04f, z), V((left + right) / 2, 2.44f, z), 0f };
            return (bool)triangle.Invoke(null, arguments) ? (float)arguments[5] : float.PositiveInfinity;
        }
        foreach(float wallZ in new[] { 20.55f, 21f, 23.5f })
        {
            float distance = Surface(wallZ);
            Check(Earlier(distance, double.PositiveInfinity, 3), "near-shooter and endpoint world faces are eligible on the actual segment");
        }
        Check(!Earlier(Surface(24), double.PositiveInfinity, 3), "world face beyond the traveled segment cannot receive an early hit");
        Check(!Earlier(Surface(21, 3), double.PositiveInfinity, 3), "shot above rendered wall height is not blocked by its occupied cell");
        Check(!Earlier(Surface(21, 1.55f, 19, 19.8f), double.PositiveInfinity, 3), "ray through an open door gap does not hit a displaced leaf");
        foreach(float[] order in new[] { new[] { 23.5f, 21f, 22.5f }, new[] { 22.5f, 23.5f, 21f } })
        {
            double nearest = double.PositiveInfinity;
            foreach(float wall in order) { float candidate = Surface(wall); if(Earlier(candidate, nearest, 3)) nearest = candidate; }
            Check(Math.Abs(nearest - .5) < .00001, "world selection takes nearest surface independent of native grid/list order");
        }
        foreach(double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -.1 })
            Check(!Earlier(invalid, double.PositiveInfinity, 3), "invalid or behind-segment contact is rejected");

        MethodInfo clip = aim.GetMethod("ClipNativeInterval", Flags), commit = aim.GetMethod("ShouldCommitWorld", Flags);
        MethodInfo deterministic = aim.GetMethod("DeterministicWorld", Flags, null, new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null);
        Check((bool)deterministic.Invoke(null,new object[]{true,true,false,false}),"full solid world obstruction uses deterministic segment collision");
        Check(!(bool)deterministic.Invoke(null,new object[]{false,false,false,false}),"incidental foliage retains native stochastic interception");
        Check(!(bool)deterministic.Invoke(null,new object[]{true,false,false,false}),"partial furniture and cover retain native stochastic interception");
        Check(!(bool)deterministic.Invoke(null,new object[]{true,true,true,false}),"open-door incidental frame contacts retain native interception probability");
        Check((bool)deterministic.Invoke(null,new object[]{false,false,false,true}),"explicit intended item/plant hit retains successful native target eligibility");
        float[] clipped = Coordinates(clip.Invoke(null, new[] { V(20.5f, .123f, 30.5f), V(20.5f, 1.55f, 23.5f) }));
        Check(Math.Abs(clipped[1] - .123f) < .000001 && Math.Abs(clipped[2] - 23.5f) < .000001,
            "native interceptor interval ends at world contact while preserving native altitude conventions");
        Check(21f < clipped[2] && 25f > clipped[2], "shield/pawn checks before the blocker remain in the native interval and checks beyond it are excluded");
        for(int bits = 0; bits < 32; bits++)
        {
            bool intercepted=(bits&1)!=0, destroyed=(bits&2)!=0, spawned=(bits&4)!=0, blocker=(bits&8)!=0, same=(bits&16)!=0;
            bool result=(bool)commit.Invoke(null,new object[]{intercepted,destroyed,spawned,blocker,same});
            Check(result == (!intercepted && !destroyed && spawned && blocker && same),
                "world impact commits only once for a live projectile after native shield/pawn interception declines");
        }

        Assembly game = Assembly.Load("Assembly-CSharp");
        Type cell = game.GetType("Verse.IntVec3", true);
        Type setType = typeof(HashSet<>).MakeGenericType(cell);
        object cells = Activator.CreateInstance(setType);
        MethodInfo collect = aim.GetMethod("CollectSegmentCells", Flags);
        foreach(var segment in new[] { (start:V(20.5f,1.55f,20.5f),end:V(20.5f,1.55f,20.6f)),
            (start:V(20.5f,1.55f,20.5f),end:V(20.5f,1.55f,50.5f)),
            (start:V(20.5f,1.55f,20.5f),end:V(41.5f,.04f,41.5f)) })
        {
            Check((bool)collect.Invoke(null,new[]{segment.start,segment.end,cells}), "ordinary same-cell and long tick intervals fit bounded broad phase");
            int count = (int)setType.GetProperty("Count").GetValue(cells);
            Check(count>0 && count<=(int)aim.GetField("MaximumSegmentCells",Flags).GetRawConstantValue(), "segment lookup stays within its cell budget");
            float[] start=Coordinates(segment.start),end=Coordinates(segment.end);
            bool Contains(int x,int z)=>((IEnumerable)cells).Cast<object>().Any(c=>(int)Get(c,"x")==x&&(int)Get(c,"z")==z);
            Check(Contains((int)Math.Floor(start[0]),(int)Math.Floor(start[2]))&&Contains((int)Math.Floor(end[0]),(int)Math.Floor(end[2])),
                "broad phase includes both origin and endpoint cells that native free interception skips");
            Check(Contains((int)Math.Floor(end[0])+2,(int)Math.Floor(end[2])), "broad phase includes displaced door leaves and geometry overhangs");
        }
        Check(!(bool)collect.Invoke(null,new[]{V(float.NaN,0,0),V(1,0,1),cells})
            &&(int)setType.GetProperty("Count").GetValue(cells)==0,"invalid interval clears scratch cells without retaining stale candidates");
    }
}
