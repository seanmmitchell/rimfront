// Managed math boundary for portable CI. NativeGeometryTests separately uses installed 1.7 math.
using N = System.Numerics.Vector3;
namespace UnityEngine {
public static class Time { public static int frameCount; }
public record struct Vector3(float x,float y,float z) {
 public static Vector3 zero=>new(0,0,0);
 public float sqrMagnitude=>x*x+y*y+z*z;
 static N Native(Vector3 a)=>new(a.x,a.y,a.z);
 static Vector3 From(N a)=>new(a.X,a.Y,a.Z);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>From(Native(a)+Native(b));
 public static Vector3 operator-(Vector3 a,Vector3 b)=>From(Native(a)-Native(b));
 public static Vector3 operator*(Vector3 a,float n)=>From(Native(a)*n);
 public static Vector3 operator/(Vector3 a,float n)=>From(Native(a)/n);
 public static Vector3 Cross(Vector3 a,Vector3 b)=>From(N.Cross(Native(a),Native(b)));
 public static float Dot(Vector3 a,Vector3 b)=>N.Dot(Native(a),Native(b));
 public static Vector3 Min(Vector3 a,Vector3 b)=>From(N.Min(Native(a),Native(b)));
 public static Vector3 Max(Vector3 a,Vector3 b)=>From(N.Max(Native(a),Native(b)));
}
public record struct Color(float r,float g,float b,float a=1) {
 public static Color white=>new(1,1,1);
 public static Color operator*(Color c,float n)=>new(c.r*n,c.g*n,c.b*n,c.a);
}
public struct Bounds {
 public Vector3 center,size; public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;}
 public Vector3 min=>center-size*.5f; public Vector3 max=>center+size*.5f;
}
public static class Mathf {
 public const float Deg2Rad=(float)Math.PI/180;
 public static float Sin(float n)=>MathF.Sin(n); public static float Cos(float n)=>MathF.Cos(n);
 public static float Clamp01(float n)=>Math.Clamp(n,0,1);
}
}
