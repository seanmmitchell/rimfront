using System;
using System.Reflection;
using RimFront;
using UnityEngine;
using Verse;
class Program
{
    static int checks;
    static void Check(bool result,string label) { if(!result)throw new Exception(label);checks++; }
    static bool Close(Vector3 a,Vector3 b)=>(a-b).magnitude<.0001f;
    public class Wrong { public int ShouldSetUp=>1;public void CloseBipod()=>throw new Exception(); }
    public class WrongClose { public bool ShouldSetUp=>true;public bool CloseBipod()=>true; }
    static bool Decision(ThingComp comp,bool native=true)
    {
        object[] args={comp,native};
        bool run=(bool)typeof(NativeBipods).GetMethod("ShouldSetUp",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        return run ? native : (bool)args[1];
    }
    static void Main()
    {
        for(int mask=0;mask<16;mask++)
            Check(NativeBipods.Suppress((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==(mask==7),"exact controlled primary only");
        foreach(Type t in new[]{null,typeof(Wrong),typeof(WrongClose)})
        { var api=new NativeBipods.Contract(t);Check(!api.Supported&&!api.TryClose(new Wrong()),"unsupported CE contract never mutates"); }
        var contract=new NativeBipods.Contract(typeof(CombatExtended.BipodComp));
        var comp=new CombatExtended.BipodComp();
        Check(contract.Supported&&!contract.TryClose(null)&&!contract.TryClose(new Wrong()),"native contract type checked");
        Check(contract.TryClose(comp)&&comp.Closes==1,"native close invoked once");
        comp.Throw=true;Check(!contract.TryClose(comp)&&comp.Closes==2,"native exception handled");comp.Throw=false;
        var pawn=new Pawn();comp.parent=pawn.equipment.Primary;pawn.equipment.Primary.AllComps.Add(comp);
        NativeView.Active=true;NativeView.ControlledPawn=pawn;NativeBipods.Initialize();NativeBipods.Start(pawn);
        Check(Decision(comp)&&!Decision(comp,false),"enabled gate preserves both native decisions");
        Check(NativeBipods.Describe(pawn).Contains("ON"),"HUD reports enabled");
        pawn.SettingUp=true;
        pawn.jobs.OnEnd=()=>Check(!Decision(comp),"setup veto precedes synchronous cleanup");
        int before=comp.Closes;
        Check(NativeBipods.TryToggle(pawn,out _)&&comp.Closes==before+1&&pawn.jobs.Ends==1&&!pawn.jobs.LastStartNewJob,"disable folds native bipod and cancels exact setup without restart");
        Check(!Decision(comp)&&NativeBipods.Describe(pawn).Contains("OFF"),"disabled gate and HUD agree");
        var other=new CombatExtended.BipodComp{parent=new ThingWithComps()};
        Check(Decision(other),"other weapons/pawns unaffected");
        NativeView.Active=false;Check(Decision(comp),"inactive view restores native decision");NativeView.Active=true;
        Check(!NativeBipods.TryToggle(new Pawn(),out _),"foreign pawn cannot toggle");
        pawn.SettingUp=false;
        Check(NativeBipods.TryToggle(pawn,out _)&&Decision(comp)&&comp.Closes==before+1,"enable delegates to CE without redeploy/close mutation");
        comp.Throw=true;Check(!NativeBipods.TryToggle(pawn,out _)&&Decision(comp),"failed close restores prior preference");comp.Throw=false;
        NativeBipods.TryToggle(pawn,out _);NativeBipods.Stop();
        Check(Decision(comp)&&NativeBipods.Describe(pawn)=="","release restores native behavior and clears HUD");
        NativeBipods.Start(pawn);Check(Decision(comp),"new possession resets preference");
        pawn.equipment.Primary=new ThingWithComps();Check(!NativeBipods.TryToggle(pawn,out _)&&NativeBipods.Describe(pawn)=="","non-bipod weapon is a safe no-op");
        Check(Decision(comp),"unequipped previous weapon no longer gated");NativeBipods.Stop();

        Vector3 eye=new(10,1.6f,20),muzzle=new(10.2f,1.3f,20.6f),target=new(10,1.6f,40);
        float length=(muzzle-eye).magnitude;
        Check(Close(NativeShotSource.Resolve(eye,muzzle,target,length+.01f),muzzle),"clear barrel uses rendered muzzle");
        Check(Close(NativeShotSource.Resolve(eye,muzzle,target,0),eye),"immediate blocker keeps eye origin");
        Check(Close(NativeShotSource.Resolve(eye,muzzle,target,.21f),eye+(muzzle-eye)*(.2f/length)),"cover clips before contact on full three-dimensional barrel segment");
        Check(Close(NativeShotSource.Resolve(eye,muzzle,eye+(muzzle-eye)*.5f,10),eye),"near crosshair cannot place origin behind selected surface");
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f})
            Check(Close(NativeShotSource.Resolve(eye,muzzle,target,value),eye),"invalid clearance falls back");
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        foreach(int axis in new[]{0,1,2})
        {
            Vector3 bad=muzzle;if(axis==0)bad.x=value;else if(axis==1)bad.y=value;else bad.z=value;
            Check(Close(NativeShotSource.Resolve(eye,bad,target,10),eye),"invalid muzzle rejected");
            Check(Close(NativeShotSource.Resolve(eye,muzzle,bad,10),eye),"invalid target rejected");
        }
        Check(Close(NativeShotSource.Resolve(eye,eye,target,10),eye),"zero offset safe");
        Check(Close(NativeShotSource.Resolve(eye,eye+new Vector3(0,0,3),target,10),eye),"unbounded barrel rejected");
        foreach(float yaw in new[]{0f,.7f,1.57f,3.14f,-2f})
        foreach(float pitch in new[]{-1.1f,0,.8f})
        {
            Vector3 offset=new(.7f*MathF.Sin(yaw)*MathF.Cos(pitch),-.7f*MathF.Sin(pitch),.7f*MathF.Cos(yaw)*MathF.Cos(pitch));
            Vector3 actual=NativeShotSource.Resolve(eye,eye+offset,eye+offset*30,.36f);
            Check(Close(actual,eye+offset*.5f),"clipping follows yaw/pitch without grid snapping");
        }
        NativeView.RenderPosition=eye;NativeWeapons.Muzzle=muzzle;NativeAimTargeting.Contact=muzzle;
        Check((NativeShotSource.Sample(pawn,target)-muzzle).magnitude<.011f,"sample uses rendered muzzle and bounded clearance query");
        NativeAimTargeting.Complete=false;Check(Close(NativeShotSource.Sample(pawn,target),eye),"failed/budget-limited query cannot skip cover");
        NativeAimTargeting.Complete=true;NativeAimTargeting.Contact=eye+(muzzle-eye)*.3f;
        Check((NativeShotSource.Sample(pawn,target)-eye).magnitude<length*.3f,"sample clips obstruction before muzzle");
        int queries=NativeAimTargeting.Queries;NativeWeapons.Available=false;
        Check(Close(NativeShotSource.Sample(pawn,target),eye)&&NativeAimTargeting.Queries==queries,"unavailable muzzle uses eye without query");
        Console.WriteLine($"PASS: {checks} production bipod/shot-source checks (managed engine boundary)");
    }
}
