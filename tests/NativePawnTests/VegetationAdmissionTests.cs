using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestVegetationAdmission(Assembly assembly)
    {
        Assembly game=Assembly.Load("Assembly-CSharp");
        Type vegetation=assembly.GetType("RimFront.NativeVegetation",true), gameType=game.GetType("Verse.Game",true);
        FieldInfo currentGame=game.GetType("Verse.Current",true).GetField("gameInt",Flags);
        FieldInfo settings=assembly.GetType("RimFront.RimFrontMod",true).GetField("Settings",Flags);
        object savedGame=currentGame.GetValue(null), savedSettings=settings.GetValue(null);
        MethodInfo begin=vegetation.GetMethod("Begin",Flags), queue=vegetation.GetMethod("Queue",Flags);
        try
        {
            object testSettings=Activator.CreateInstance(assembly.GetType("RimFront.RimFrontSettings",true));
            testSettings.GetType().GetField("extraViewRange").SetValue(testSettings,2); settings.SetValue(null,testSettings);
            object boundary=RuntimeHelpers.GetUninitializedObject(gameType);
            FieldInfo mapsField=gameType.GetField("maps",Flags);
            var maps=(IList)Activator.CreateInstance(mapsField.FieldType);
            Type mapType=game.GetType("Verse.Map",true);
            maps.Add(RuntimeHelpers.GetUninitializedObject(mapType)); maps.Add(RuntimeHelpers.GetUninitializedObject(mapType));
            mapsField.SetValue(boundary,maps); currentGame.SetValue(null,boundary);
            Type plantType=game.GetType("RimWorld.Plant",true), thingType=game.GetType("Verse.Thing",true);
            FieldInfo id=thingType.GetField("thingIDNumber",Flags), state=thingType.GetField("mapIndexOrState",Flags);
            object Plant(int number)
            { object plant=RuntimeHelpers.GetUninitializedObject(plantType);id.SetValue(plant,number);state.SetValue(plant,(sbyte)1);return plant; }
            begin.Invoke(null,new[]{V(0,0,0),(object)0f});
            int capacity=(int)vegetation.GetField("MaximumCandidates",Flags).GetRawConstantValue();
            for(int i=0;i<capacity;i++) queue.Invoke(null,new[]{Plant(i),(object)10000f});
            object near=Plant(99999); queue.Invoke(null,new[]{near,(object)1f});
            object candidates=vegetation.GetField("candidates",Flags).GetValue(null);
            var ordered=(IList)candidates.GetType().GetMethod("Ordered",Flags).Invoke(candidates,null);
            Check(ordered.Count==capacity,"production vegetation queue stays bounded after replacing a far plant");
            Check(ReferenceEquals(ordered[0].GetType().GetField("Value",Flags).GetValue(ordered[0]),near),"production Queue retains a late plant at one tile after 8192 plants at 100 tiles");
            queue.Invoke(null,new[]{Plant(100000),(object)float.NaN});
            Check((int)candidates.GetType().GetProperty("Count",Flags).GetValue(candidates)==capacity,"invalid production vegetation candidates do not alter capacity");
            begin.Invoke(null,new[]{V(0,0,0),(object)0f});
            Check((int)candidates.GetType().GetProperty("Count",Flags).GetValue(candidates)==0,"new render frame releases all prior plant candidates");
        }
        finally { begin.Invoke(null,new[]{V(0,0,0),(object)0f});currentGame.SetValue(null,savedGame);settings.SetValue(null,savedSettings); }
    }
}
