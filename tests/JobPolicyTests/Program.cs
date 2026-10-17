using System;
using RimFront;

internal static class Program
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static bool Releases(string name,bool owned=false,bool forced=false,bool sleep=false,int expiry=-1)
        =>NativeJobPolicy.RequiresNativeControl(name,owned,forced,sleep,expiry);
    static void Main()
    {
        Check(!Releases("Wait_MaintainPosture",expiry:1),"automatic one-tick transition after ending a job must retain first-person control");
        Check(Releases("Wait_MaintainPosture",forced:true,expiry:1),"explicit forced posture order must keep native ownership");
        Check(Releases("Wait_MaintainPosture",sleep:true,expiry:1),"forced-sleep posture must keep native ownership");
        foreach(int expiry in new[]{-1,0,2,60,600})
            Check(Releases("Wait_MaintainPosture",expiry:expiry),"indefinite or longer posture job must not be mistaken for one-tick transition");
        foreach(string name in new[]{"Goto","HaulToCell","LayDown","Wait_Downed","FleeAndCower","ModdedRecoveryJob"})
            Check(Releases(name),"external native job must release: "+name);
        Check(!Releases("AttackStatic",owned:true,forced:true),"RimFront's own attack must retain control");
        Check(!Releases("Goto",owned:true,forced:true),"RimFront's own path order must retain control");
        Check(Releases("AttackStatic",forced:true),"another issuer's attack order keeps native ownership");
        foreach(string name in new[]{"Wait","Wait_Combat","ReloadWeapon"})
        {
            Check(!Releases(name),"existing stationary/reload flow remains compatible: "+name);
            Check(Releases(name,sleep:true),"forced sleep takes precedence over ordinary idle/reload exception: "+name);
        }
        Check(!Releases(null),"temporary absence of a job does not release control");
        Check(Releases("Wait_MaintainPosture_Custom",expiry:1),"similarly named mod job is not silently allowlisted");
        for (int mask=0;mask<32;mask++)
        {
            bool forced=(mask&1)!=0,sleep=(mask&2)!=0,driver=(mask&4)!=0,equipped=(mask&8)!=0,bipod=(mask&16)!=0;
            bool compatible=NativeJobPolicy.CompatibleBipod("JobDef_SetUpBipod",forced,sleep,driver,equipped,bipod);
            Check(compatible==(!forced&&!sleep&&driver&&equipped&&bipod),"only automatic native setup of the equipped bipod qualifies for retained control");
            Check(NativeJobPolicy.RequiresNativeControl("JobDef_SetUpBipod",false,forced,sleep,-1,compatible)==!compatible,
                "invalid or externally forced bipod jobs retain native ownership rather than being silently preempted");
        }
        foreach(string name in new[]{"JobDef_SetUpBipod_Custom","ReloadWeapon","Goto",null})
            Check(!NativeJobPolicy.CompatibleBipod(name,false,false,true,true,true),"similar or unrelated jobs cannot borrow the bipod exception");
        Check(Releases("JobDef_SetUpBipod"),"a job name alone is insufficient to authorize retained control");
        Check(NativeJobPolicy.RequiresNativeControl("HaulToCell",false,false,false,-1,true),"bipod context cannot allowlist an unrelated external job");
        Console.WriteLine($"PASS: {checks} native job ownership regressions using the actual policy helper. No game session was started.");
    }
}
