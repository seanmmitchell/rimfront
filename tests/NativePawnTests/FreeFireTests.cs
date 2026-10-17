using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestFreeFire(Assembly assembly)
    {
        TestCEContactGeometry(assembly);
        Type manual=assembly.GetType("RimFront.NativeManualAim",true);
        MethodInfo owns=manual.GetMethod("Owns",Flags),range=manual.GetMethod("InRange",Flags),angle=manual.GetMethod("AirAngle",Flags);
        object pawn=new object(),verb=new object(),job=new object(),other=new object();
        foreach(bool active in new[]{false,true})foreach(bool validating in new[]{false,true})
            foreach(bool samePawn in new[]{false,true})foreach(bool sameVerb in new[]{false,true})foreach(bool sameJob in new[]{false,true})
        {
            bool result=(bool)owns.Invoke(null,new object[]{pawn,verb,job,samePawn?pawn:other,sameVerb?verb:other,sameJob?job:other,active,validating});
            Check(result==(active&&samePawn&&sameVerb&&(validating||sameJob)),"manual geometry applies only to the active controlled pawn and exact owned verb/job; synchronous validation cannot affect another caster");
        }
        foreach(int missing in new[]{0,1,2})
        {
            object[] args={pawn,verb,job,pawn,verb,job,true,true};args[missing]=null;
            Check(!(bool)owns.Invoke(null,args),"incomplete manual ownership never changes native combat");
        }
        MethodInfo allow=manual.GetMethod("AllowCast",Flags),rebase=manual.GetMethod("RebaseRangeEstimate",Flags),distance=manual.GetMethod("HorizontalDistance",Flags);
        for(int mask=0;mask<16;mask++)
        {
            bool owned=(mask&1)!=0,valid=(mask&2)!=0,inBounds=(mask&4)!=0,inRange=(mask&8)!=0;
            bool accepted=(bool)allow.Invoke(null,new object[]{owned,valid,inBounds,inRange});
            Check(accepted==(!owned||(valid&&inBounds&&inRange)),"invalid or out-of-range manual rays cannot reuse CE suppressive-fire history; unowned casts retain native behavior");
        }
        float Scale(float estimate,float previous,float next)=>(float)rebase.Invoke(null,new object[]{estimate,previous,next});
        Check(Math.Abs(Scale(12,10,30)-36)<.0001f,"burst range estimate follows near-to-far aim while retaining native twenty-percent estimation error");
        Check(Math.Abs(Scale(8,10,2)-1.6f)<.0001f,"burst range estimate follows far-to-near aim while retaining native underestimation");
        float estimate=12,prior=10;
        foreach(float next in new[]{4f,22f,1f,30f,10f}){estimate=Scale(estimate,prior,next);prior=next;}
        Check(Math.Abs(estimate-12)<.0001f,"repeated aim-distance changes do not accumulate additional artificial accuracy loss");
        Check(Scale(12,0,5)==5&&Scale(float.NaN,10,5)==5&&Scale(12,10,float.NaN)==0,"absent or invalid range history cannot create a nonfinite native shot estimate");
        float adjacent=(float)distance.Invoke(null,new[]{V(128.5f,1.1f,256.5f),V(128.9f,1.6f,256.5f)});
        Check(adjacent>.39f&&adjacent<.41f,"an adjacent face in the shooter's grid cell still has real positive horizontal aim distance");
        Check((float)distance.Invoke(null,new[]{V(0,1,0),V(3,20,4)})==5,"CE range-error input uses horizontal point distance, independent of target height");
        MethodInfo candidate=manual.GetMethod("NeedsExplicitCandidate",Flags);
        for(int mask=0;mask<64;mask++)
        {
            bool owned=(mask&1)!=0,hit=(mask&2)!=0,spawned=(mask&4)!=0,sameMap=(mask&8)!=0,zeroFill=(mask&16)!=0,isPawn=(mask&32)!=0;
            bool accepted=(bool)candidate.Invoke(null,new object[]{owned,hit,spawned,sameMap,zeroFill,isPawn});
            Check(accepted==(owned&&!hit&&spawned&&sameMap&&zeroFill&&!isPawn),"explicit item collision only supplements owned shots with a live same-map zero-fill item, never native hits or pawn collision");
        }
        bool Range(object origin,object point,float min,float max)=>(bool)range.Invoke(null,new[]{origin,point,(object)min,max});
        Check(Range(V(128.5f,1.1f,256.5f),V(131.5f,10,260.5f),0,5),"native range uses horizontal distance even while aiming upward");
        Check(!Range(V(128.5f,1.1f,256.5f),V(131.51f,1,260.5f),0,5),"manual aim cannot extend a native weapon's maximum range");
        Check(!Range(V(0,0,0),V(1,0,1),2,30)&&Range(V(0,0,0),V(2,0,0),2,30),"native minimum range is preserved at its boundary");
        Check(Range(V(0,0,0),V(0,30,0),0,30),"vertical point does not invent horizontal range");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            Check(!Range(V(invalid,0,0),V(0,0,1),0,30),"invalid aim origin cannot authorize a shot");
            Check(!Range(V(0,0,0),V(0,invalid,1),0,30),"invalid point height cannot authorize a shot");
            Check(!Range(V(0,0,0),V(0,0,1),0,invalid),"invalid weapon range cannot authorize a shot");
            Check(float.IsFinite((float)angle.Invoke(null,new object[]{invalid})),"invalid optional pitch does not create nonfinite launch geometry");
        }
        Check(!Range(V(0,0,0),V(0,0,1),-1,30)&&!Range(V(0,0,0),V(0,0,1),0,0),"invalid range intervals fail closed");
        foreach(float pitch in new[]{-1.4f,-.7f,0f,.7f,1.4f})
        {
            float launch=(float)angle.Invoke(null,new object[]{pitch});
            Check(Math.Abs(launch+pitch)<.00001f,"CE free-air elevation follows the actual camera pitch without flattening or target-center snapping");
            Check(Math.Sign(Math.Sin(launch))==-Math.Sign(pitch),"downward camera pitch produces downward elevation and upward pitch produces upward elevation");
        }

        FieldInfo state=manual.GetField("current",Flags);object saved=state.GetValue(null);
        Type aimType=state.FieldType,pawnType=aimType.GetField("Pawn",Flags).FieldType;
        object controlled=RuntimeHelpers.GetUninitializedObject(pawnType),otherPawn=RuntimeHelpers.GetUninitializedObject(pawnType);
        MethodInfo clear=manual.GetMethod("Clear",Flags);
        try
        {
            object aim=Activator.CreateInstance(aimType,true);Set(aim,"Pawn",controlled);state.SetValue(null,aim);
            clear.Invoke(null,new[]{otherPawn});
            Check(ReferenceEquals(state.GetValue(null),aim),"another pawn's cleanup cannot release the owned aim state");
            clear.Invoke(null,new[]{controlled});
            Check(state.GetValue(null)==null,"owned job cleanup clears manual geometry before normal combat resumes");
            state.SetValue(null,aim);clear.Invoke(null,new object[]{null});
            Check(state.GetValue(null)==null,"unconditional release clears manual state");
            MethodInfo getter=manual.GetMethod("TryGet",Flags,null,new[]{pawnType,vector.MakeByRefType(),typeof(float).MakeByRefType(),typeof(float).MakeByRefType()},null);
            object[] args={null,null,99f,99f};
            Check(!(bool)getter.Invoke(null,args)&&Close(Coordinates(args[1]),new[]{0f,0f,0f})&&(float)args[2]==0&&(float)args[3]==0,"launches without an owned pawn safely decline manual context");
        }
        finally{state.SetValue(null,saved);}

        // Exercise the actual nested target scopes used by WarmupComplete/TryCastShot.
        // The outer selected Thing must survive the inner return for native XP/logging.
        Assembly native = pawnType.Assembly;
        object scopeVerb = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.Verb_Shoot", true));
        FieldInfo verbTarget = native.GetType("Verse.Verb", true).GetField("currentTarget", Flags);
        Type targetInfo = verbTarget.FieldType;
        object cellTarget = Activator.CreateInstance(targetInfo);
        ConstructorInfo thingTarget = targetInfo.GetConstructor(new[] { native.GetType("Verse.Thing", true) });
        object selected = thingTarget.Invoke(new[] { controlled }), nested = thingTarget.Invoke(new[] { otherPawn });
        MethodInfo pushTarget = manual.GetMethod("PushTarget", Flags), popTarget = manual.GetMethod("CeEndCast", Flags);
        saved = state.GetValue(null);
        try
        {
            object aim = Activator.CreateInstance(aimType, true); state.SetValue(null, aim);
            verbTarget.SetValue(scopeVerb, cellTarget);
            object outer = pushTarget.Invoke(null, new[] { scopeVerb, selected });
            object inner = pushTarget.Invoke(null, new[] { scopeVerb, nested });
            popTarget.Invoke(null, new[] { scopeVerb, inner, null });
            Check(Equals(verbTarget.GetValue(scopeVerb), selected), "returning from nested CE shot preserves the selected Thing for native warmup XP and battle logging");
            var failure = new InvalidOperationException("native warmup sentinel");
            object returned = popTarget.Invoke(null, new object[] { scopeVerb, outer, failure });
            Check(Equals(verbTarget.GetValue(scopeVerb), cellTarget) && ReferenceEquals(returned, failure), "warmup completion restores the original cell target and propagates native exceptions");
            verbTarget.SetValue(scopeVerb, selected);
            outer = pushTarget.Invoke(null, new[] { scopeVerb, nested });
            clear.Invoke(null, new object[] { null });
            verbTarget.SetValue(scopeVerb, cellTarget);
            popTarget.Invoke(null, new[] { scopeVerb, outer, null });
            Check(Equals(verbTarget.GetValue(scopeVerb), cellTarget), "job cancellation inside a shot scope cannot resurrect the old target after native Reset");
        }
        finally { state.SetValue(null, saved); }

        // Bounds overrides used by CE rays must unwind even when CE or another mod
        // throws, preserving that exact native exception rather than swallowing it.
        FieldInfo scopedTarget=manual.GetField("boundsTarget",Flags),scopedBounds=manual.GetField("targetBounds",Flags);
        object savedTarget=scopedTarget.GetValue(null),savedBounds=scopedBounds.GetValue(null);
        try
        {
            Type scopeType=manual.GetNestedType("BoundsState",Flags);object scope=Activator.CreateInstance(scopeType,true);
            Set(scope,"Target",controlled);Set(scope,"Bounds",savedBounds);scopedTarget.SetValue(null,otherPawn);
            var error=new InvalidOperationException("native collision sentinel");
            object result=manual.GetMethod("CeRayEnd",Flags).Invoke(null,new object[]{scope,error});
            Check(ReferenceEquals(result,error)&&ReferenceEquals(scopedTarget.GetValue(null),controlled),"CE ray scope restores prior collision context and preserves native exceptions");
            result=manual.GetMethod("CeRayEnd",Flags).Invoke(null,new object[]{null,error});
            Check(ReferenceEquals(result,error)&&ReferenceEquals(scopedTarget.GetValue(null),controlled),"unowned ray finalizer leaves another scope untouched");
        }
        finally{scopedTarget.SetValue(null,savedTarget);scopedBounds.SetValue(null,savedBounds);}
    }

    static void TestCEContactGeometry(Assembly assembly)
    {
        Type manual = assembly.GetType("RimFront.NativeManualAim", true);
        MethodInfo segment = manual.GetMethod("SegmentDirection", Flags), convert = manual.GetMethod("SegmentDistance", Flags);
        MethodInfo inCell = manual.GetMethod("ContactInCell", Flags), use = manual.GetMethod("UseRenderedContact", Flags);
        for (int mask = 0; mask < 16; mask++)
            Check((bool)use.Invoke(null, new object[] { (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0 }) == (mask == 15),
                "rendered collision changes only owned shots against supported spawned same-map objects");
        object from = V(.5f, 2.2f, .5f), to = V(.5f, 2.2f, 9.5f);
        object[] rayArgs = { from, to, 0f, 0f, 0f, 0f };
        Check((bool)segment.Invoke(null, rayArgs) && Math.Abs((float)rayArgs[4] - 9) < .0001f, "high horizontal projectile segment retains its full actual travel distance");
        Type geometry = assembly.GetType("RimFront.NativeAimTargeting", true);
        object[] hitArgs = { from, V(0, 0, 1), V(0, 2, 3), V(1, 2, 3), V(.5f, 2.4f, 3), 0f };
        Check((bool)geometry.GetMethod("RayTriangle", Flags).Invoke(null, hitArgs), "a shot through the visible upper wall above CE's old two-tile height intersects rendered geometry");
        object[] distanceArgs = { (double)(float)hitArgs[5], (float)rayArgs[4], (float)rayArgs[5], 0f };
        Check((bool)convert.Invoke(null, distanceArgs) && Math.Abs((float)distanceArgs[3] - 2.5f) < .0001f, "CE receives the real near-surface segment distance rather than the intended target centre");
        Type cellType = inCell.GetParameters()[3].ParameterType;
        object nearCell = Activator.CreateInstance(cellType, new object[] { 0, 0, 2 });
        object farCell = Activator.CreateInstance(cellType, new object[] { 0, 0, 8 });
        Check((bool)inCell.Invoke(null, new[] { from, to, distanceArgs[3], nearCell }), "wall face on a cell boundary remains eligible in the first traversed contact cell");
        Check(!(bool)inCell.Invoke(null, new[] { from, to, distanceArgs[3], farCell }), "a contact from a different segment cell cannot be accepted ahead of intervening blockers");
        distanceArgs = new object[] { 10d, 9f, 9f, 0f };
        Check(!(bool)convert.Invoke(null, distanceArgs), "rendered collision cannot hit beyond this tick's actual projectile segment");
        distanceArgs = new object[] { 2.5d, 3f, 5f, 0f };
        Check((bool)convert.Invoke(null, distanceArgs) && Math.Abs((float)distanceArgs[3] - 25f / 6f) < .0001f,
            "pitched contact converts horizontal aim units to native three-dimensional ray distance");
        foreach (object end in new[] { from, V(.5f, 10, .5f), V(float.NaN, 0, 0) })
        {
            rayArgs = new object[] { from, end, 0f, 0f, 0f, 0f };
            Check(!(bool)segment.Invoke(null, rayArgs), "zero-length, vertical, or invalid segments retain native collision instead of inventing geometry");
        }
    }
}
