using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

internal static partial class Program
{
    static void TestShotSource(Assembly assembly)
    {
        Type manual = assembly.GetType("RimFront.NativeManualAim", true);
        Assembly harmony = Assembly.Load("0Harmony");
        Type instruction = harmony.GetType("HarmonyLib.CodeInstruction", true);
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(instruction);
        Type thing = Assembly.Load("Assembly-CSharp").GetType("Verse.Thing", true);
        MethodInfo center = thing.Assembly.GetType("RimWorld.GenThing", true).GetMethod("TrueCenter", Flags, null, new[] { thing }, null);
        MethodInfo transform = manual.GetMethod("CeSourceCenters", Flags), source = manual.GetMethod("CeSource", Flags);
        object Op(OpCode code, object operand = null) => Activator.CreateInstance(instruction, new[] { (object)code, operand });
        IList Instructions(int centers)
        {
            IList list = (IList)Activator.CreateInstance(listType);
            list.Add(Op(OpCodes.Ldarg_0));
            for (int i = 0; i < centers; i++) list.Add(Op(OpCodes.Call, center));
            list.Add(Op(OpCodes.Ret)); return list;
        }
        IList original = Instructions(1);
        var patched = ((IEnumerable)transform.Invoke(null, new object[] { original })).Cast<object>().ToArray();
        Check(patched.Length == original.Count + 2 && ReferenceEquals(patched[0], original[0])
            && ReferenceEquals(patched[1], original[1]) && ReferenceEquals(patched[4], original[2]),
            "CE source transpiler retains native instructions and inserts only the scoped source adapter");
        Check((OpCode)instruction.GetField("opcode").GetValue(patched[2]) == OpCodes.Ldarg_0
            && Equals(instruction.GetField("operand").GetValue(patched[3]), source),
            "CE source transpiler supplies this exact verb after native TrueCenter for ownership checks");
        foreach (int count in new[] { 0, 2 })
        {
            bool rejected = false;
            try { transform.Invoke(null, new object[] { Instructions(count) }); }
            catch (TargetInvocationException error) { rejected = error.InnerException is NotSupportedException; }
            Check(rejected, "changed/multiple CE source reads fail closed instead of applying partial geometry");
        }
        MethodInfo resolve = assembly.GetType("RimFront.NativeShotSource", true).GetMethod("Resolve", Flags);
        object eye = V(10, 1.6f, 20), muzzle = V(10.2f, 1.3f, 20.6f), target = V(10, 1.6f, 40);
        Check(Close(Coordinates(resolve.Invoke(null, new[] { eye, muzzle, target, (object)10f })), Coordinates(muzzle)),
            "compiled shot source uses actual installed Unity vector math at unobstructed LMG muzzle");
        Check(Close(Coordinates(resolve.Invoke(null, new[] { eye, muzzle, target, (object)0f })), Coordinates(eye)),
            "compiled shot source cannot advance through an immediate cover contact");
        object[] heightArgs = { null, 123f };
        Check((bool)manual.GetMethod("CeSourceHeight", Flags).Invoke(null, heightArgs) && (float)heightArgs[1] == 123,
            "unowned/native CE height is untouched");
        Check(Close(Coordinates(source.Invoke(null, new[] { muzzle, (object)null })), Coordinates(muzzle)),
            "unowned/native source is untouched");
    }
}
