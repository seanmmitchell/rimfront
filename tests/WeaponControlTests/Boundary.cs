// Minimal managed boundary: the linked production adapters make all decisions.
// This does not execute Harmony or CE. Native installed-assembly checks complement it.
using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib
{
    public static class AccessTools { public static Type TypeByName(string name) => typeof(CombatExtended.BipodComp).Assembly.GetType(name); }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public sealed class Harmony { public Harmony(string id) { } public void Patch(MethodInfo method, HarmonyMethod prefix) { } }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public float magnitude => MathF.Sqrt(x*x+y*y+z*z);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b) => new(a.x*b,a.y*b,a.z*b);
    }
    public static class Mathf
    {
        public static float Sqrt(float v)=>MathF.Sqrt(v);
        public static float Atan2(float y,float x)=>MathF.Atan2(y,x);
        public static float Clamp01(float v)=>Math.Clamp(v,0,1);
    }
}
namespace Verse
{
    public class ThingComp { public ThingWithComps parent; }
    public class ThingWithComps { public List<ThingComp> AllComps=new(); }
    public class Pawn { public Equipment equipment=new(); public Verse.AI.Jobs jobs=new(); public bool SettingUp; }
    public class Equipment { public ThingWithComps Primary=new(); }
    public static class Log { public static void Warning(string text) => throw new Exception(text); }
}
namespace Verse.AI
{
    public enum JobCondition { InterruptForced }
    public class Jobs
    {
        public int Ends; public bool LastStartNewJob; public Action OnEnd;
        public void EndCurrentJob(JobCondition condition,bool startNewJob)
        { Ends++;LastStartNewJob=startNewJob;OnEnd?.Invoke(); }
    }
}
namespace CombatExtended
{
    public class BipodComp : Verse.ThingComp
    {
        public bool ShouldSetUp => true;
        public int Closes; public bool Throw;
        public void CloseBipod() { Closes++;if(Throw) throw new Exception("native close sentinel"); }
    }
}
namespace RimFront
{
    internal static class NativeView
    { internal static bool Active; internal static Verse.Pawn ControlledPawn; internal static UnityEngine.Vector3 RenderPosition; }
    internal static class CombatAdapter { internal static bool IsSettingUpBipod(Verse.Pawn pawn)=>pawn.SettingUp; }
    internal static class NativeEffectMath
    { internal static bool IsFinite(UnityEngine.Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z); }
    internal static class NativeWeapons
    {
        internal static UnityEngine.Vector3 Muzzle; internal static bool Available=true;
        internal static bool TryGetMuzzle(Verse.Pawn pawn,out UnityEngine.Vector3 muzzle,out UnityEngine.Vector3 direction)
        { muzzle=Muzzle;direction=default;return Available; }
    }
    internal static class NativeAimTargeting
    {
        internal struct TargetInfo { internal UnityEngine.Vector3 Point; }
        internal static bool Complete=true; internal static UnityEngine.Vector3 Contact; internal static int Queries;
        internal static bool TrySelectFrom(Verse.Pawn pawn,UnityEngine.Vector3 eye,float yaw,float pitch,float range,
            bool constrainCellRange,out TargetInfo result)
        { Queries++;if(constrainCellRange)throw new Exception("barrel clearance must not use weapon cell range");result=new(){Point=Contact};return Complete; }
    }
}
