// Simulated map/object boundary. Production targeting, snapshots and mountain triangles are linked unchanged.
using UnityEngine;
namespace Verse {
public static class Find { public static TickManager TickManager = new TickManager(); }
public sealed class TickManager { public int TicksGame; }
public struct IntVec3 : IEquatable<IntVec3> {
 public int x,y,z; public IntVec3(int x,int y,int z){this.x=x;this.y=y;this.z=z;}
 public bool Equals(IntVec3 b)=>x==b.x&&z==b.z;
 public override bool Equals(object b)=>b is IntVec3 v&&Equals(v);
 public override int GetHashCode()=>HashCode.Combine(x,z);
 public static bool operator==(IntVec3 a,IntVec3 b)=>a.Equals(b);
 public static bool operator!=(IntVec3 a,IntVec3 b)=>!a.Equals(b);
}
public static class Ext {
 public static bool InBounds(this IntVec3 c,Map m)=>c.x>=0&&c.z>=0&&c.x<m.Size.x&&c.z<m.Size.z;
 public static bool Fogged(this IntVec3 c,Map m)=>m.Fog.Contains(c);
 public static IntVec3 ToIntVec3(this Vector3 v)=>new((int)Math.Floor(v.x),0,(int)Math.Floor(v.z));
}
public enum ThingCategory { Undefined, Item }
public class BuildingProperties { public bool isNaturalRock,isResourceRock; }
public class Size { public int x=1,z=1; }
public class ThingDef { public Size size=new(); public BuildingProperties building; public ThingCategory category; }
public record struct Rot4 { public float AsAngle=>0; }
public class Thing {
 public ThingDef def=new(); public int thingIDNumber,stackCount=1; public Map Map; public bool Spawned=true;
 public IntVec3 Position; public Vector3 DrawPos=>new(Position.x+.5f,0,Position.z+.5f); public Rot4 Rotation;
 public Color DrawColor=>Color.white; public string LabelShortCap="secret rock type";
}
public class Building:Thing {}
public class Pawn:Thing {}
public class Plant:Thing { public float Growth; }
public class Corpse:Thing { public Pawn InnerPawn; }
public class Grid { public Dictionary<IntVec3,List<Thing>> Cells=new(); public List<Thing> ThingsListAtFast(IntVec3 c) { if (!Cells.TryGetValue(c,out var list)) Cells[c]=list=new(); return list; } }
public class RoofGrid { public bool Roofed(IntVec3 c)=>false; }
public class Map { public IntVec3 Size=new(128,0,128); public HashSet<IntVec3> Fog=new(); public Grid thingGrid=new(); public RoofGrid roofGrid=new(); }
public struct LocalTargetInfo { public Thing Thing; public IntVec3 Cell; public LocalTargetInfo(Thing t){Thing=t;Cell=t.Position;} public LocalTargetInfo(IntVec3 c){Thing=null;Cell=c;} }
public static class Log { public static void Warning(string msg)=>Console.WriteLine(msg); }
}
namespace RimWorld { public class Building_Door:Verse.Building { public bool Open; public float OpenPct=>0; } }
namespace RimFront {
internal static class NativeView { internal static bool Active=>false; internal static Verse.Pawn ControlledPawn=>null; internal static Vector3 RenderPosition=>Vector3.zero; internal const float EyeHeight=1.55f; }
internal static class NativePawns { internal static bool TryAimPawn(Verse.Pawn p,Vector3 pos,bool corpse,Vector3 eye,float yaw,float pitch,out double hit){if(Verse.Ext.Fogged(p.Position,p.Map))throw new InvalidOperationException("Hidden pawn was inspected");hit=0;return false;} }
}

namespace RimFront {
internal static class NativeScene {
 internal const float FogHeight=5f;
 internal static float ViewDistance=60f;
 internal static float ViewDistanceSquared=>ViewDistance*ViewDistance;
 internal static float PlantGrowth(float growth)=>growth;
 internal static void AppendAimGeometry(Builder b,Verse.Thing thing) { if(NativeMountains.IsRock(thing))NativeMountains.Add(b,thing); }
 internal sealed class Builder {
  internal readonly List<Vector3> Vertices=new(); internal readonly List<int> Triangles=new();
  internal void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color) { int i=Vertices.Count;Vertices.Add(a);Vertices.Add(b);Vertices.Add(c);Triangles.Add(i);Triangles.Add(i+1);Triangles.Add(i+2); }
 }
}
}
