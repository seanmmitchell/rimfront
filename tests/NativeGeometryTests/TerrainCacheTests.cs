using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestChunkRebuildOrder(Assembly assembly)
    {
        Type scene=assembly.GetType("RimFront.NativeScene",true), chunk=scene.GetNestedType("Chunk",Flags);
        var chunks=(IDictionary)scene.GetField("chunks",Flags).GetValue(null);
        Check(chunks.Count==0,"scheduler test starts without a live render session");
        Assembly game=Assembly.Load("Assembly-CSharp");
        Type mapType=game.GetType("Verse.Map",true), cellType=game.GetType("Verse.IntVec3",true);
        object map=RuntimeHelpers.GetUninitializedObject(mapType);
        FieldInfo infoField=mapType.GetField("info",Flags);
        object info=RuntimeHelpers.GetUninitializedObject(infoField.FieldType);
        infoField.FieldType.GetField("sizeInt",Flags).SetValue(info,Activator.CreateInstance(cellType,240,1,240));
        infoField.SetValue(map,info);
        MethodInfo choose=scene.GetMethod("SelectChunk",Flags);
        object Add(int x,int z,bool direct)
        {
            object value=Activator.CreateInstance(chunk,Flags,null,new object[]{x,z},null);
            chunk.GetField("Dirty",Flags).SetValue(value,true); chunk.GetField("DirectlyDirty",Flags).SetValue(value,direct);
            chunks.Add(x|(z<<16),value); return value;
        }
        try
        {
            object farDirect=Add(8,8,true), nearProfile=Add(6,5,false), farProfile=Add(9,9,false);
            Check(ReferenceEquals(choose.Invoke(null,new object[]{map,5,5,0f}),farDirect),"direct edits outrank profile updates and missing chunks");
            chunk.GetField("Dirty",Flags).SetValue(farDirect,false);
            Check(ReferenceEquals(choose.Invoke(null,new object[]{map,5,5,0f}),nearProfile),"nearby dirty profile outranks far dirty and cache expansion");
            chunk.GetField("Dirty",Flags).SetValue(nearProfile,false); chunk.GetField("Dirty",Flags).SetValue(farProfile,false);
            object missing=choose.Invoke(null,new object[]{map,5,5,0f});
            Check((int)chunk.GetField("X",Flags).GetValue(missing)==5 && (int)chunk.GetField("Z",Flags).GetValue(missing)==5,"missing chunks still fill nearest first after edits settle");
        }
        finally { chunks.Clear(); }
    }
}
