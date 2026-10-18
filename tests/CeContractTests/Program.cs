using System;
using System.Linq;
using Mono.Cecil;
class Program
{
    static int checks;
    static void Check(bool condition,string label) { if(!condition)throw new Exception(label);checks++; }
    static void Main(string[] args)
    {
        if(args.Length!=1)throw new ArgumentException("Pass the installed CombatExtended.dll path. Reads metadata only; never executes CE or Harmony.");
        using var assembly=AssemblyDefinition.ReadAssembly(args[0]);
        var types=assembly.MainModule.Types;
        var bipod=types.Single(t=>t.FullName=="CombatExtended.BipodComp");
        var decision=bipod.Methods.Single(m=>m.Name=="get_ShouldSetUp");
        Check(!decision.IsStatic&&decision.IsPublic&&decision.ReturnType.FullName=="System.Boolean"&&decision.Parameters.Count==0,"CE setup getter signature");
        var close=bipod.Methods.Single(m=>m.Name=="CloseBipod");
        Check(!close.IsStatic&&close.IsPublic&&close.ReturnType.FullName=="System.Void"&&close.Parameters.Count==0,"CE native close signature");
        Check(close.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetUpInvert"),"native close retains CE stat-reset operation");
        var verb=types.Single(t=>t.FullName=="CombatExtended.Verb_LaunchProjectileCE");
        var shift=verb.Methods.Single(m=>m.Name=="ShiftTarget"&&m.Parameters.Count==5);
        var rotation=verb.Methods.Single(m=>m.Name=="ShotRotation"&&m.Parameters.Count==1);
        foreach(var method in new[]{shift,rotation})
            Check(method.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="TrueCenter"&&m.DeclaringType.FullName=="RimWorld.GenThing"
                && m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="Verse.Thing")==1,"unique TrueCenter source read in "+method.Name);
        Check(verb.Methods.Single(m=>m.Name=="get_ShotHeight").ReturnType.FullName=="System.Single","native launch height getter");
        Check(verb.Fields.Single(f=>f.Name=="sourceLoc").FieldType.FullName=="UnityEngine.Vector2","native source coordinates");
        var cast=verb.Methods.Single(m=>m.Name=="TryCastShot"&&m.Parameters.Count==0);
        Check(cast.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Launch"&&m.DeclaringType.FullName=="CombatExtended.ProjectileCE"),"native CE launch retained");
        Console.WriteLine($"PASS: {checks} installed CE metadata contracts ({assembly.Name.Version}); no runtime compatibility claim");
    }
}
