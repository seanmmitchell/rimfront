using RimFront;
using Verse;
using UnityEngine;

static class Program
{
    static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    static Map Mountain(bool fogged=false)
    {
        NativeMountains.Dispose(); NativeAimTargeting.Dispose();
        var map=new Map(); int id=0;
        for(int z=0;z<128;z++) for(int x=0;x<128;x++)
        {
            if((x-64)*(x-64)+(z-64)*(z-64)>24*24) continue;
            var rock=new Building { Position=new(x,0,z), Map=map, thingIDNumber=++id,
                def=new(){building=new(){isNaturalRock=true}} };
            map.thingGrid.ThingsListAtFast(rock.Position).Add(rock);
            if(fogged) map.Fog.Add(rock.Position);
        }
        NativeMountains.Update(map);
        for(int z=0;z<11;z++) for(int x=0;x<11;x++) NativeMountains.Publish(NativeMountains.Capture(x,z));
        return map;
    }
    static NativeScene.Builder Geometry(Thing thing,MountainSurfaceCache.Snapshot snapshot=null)
    { var b=new NativeScene.Builder(); NativeMountains.Add(b,thing,false,snapshot); return b; }
    static float Top(NativeScene.Builder b)=>b.Vertices.Max(v=>v.y);
    static void PublishedGeometry()
    {
        var map=Mountain(); var neighbor=map.thingGrid.ThingsListAtFast(new(64,0,63))[0];
        var original=Geometry(neighbor);
        int revision=NativeMountains.GeometryRevision(neighbor);
        var eye=new Vector3(64.5f,10,50.5f);
        Require(NativeAimTargeting.TryHitThing(neighbor,eye,0,0,out double before),"Old displayed slope must intercept the ray");
        map.thingGrid.ThingsListAtFast(new(64,0,64)).Clear();
        NativeMountains.Invalidate(map,new(64,0,64)); NativeMountains.Update(map);
        var staged=NativeMountains.Capture(5,5);
        Require(Top(Geometry(neighbor,staged))<5 && Top(original)>20,"Mining must materially change the staged slope");
        Require(Top(Geometry(neighbor))==Top(original),"Unpublished heights leaked into displayed/collision geometry");
        Require(NativeMountains.GeometryRevision(neighbor)==revision,"Aim cache revision changed before mesh publication");
        Require(NativeAimTargeting.TryHitThing(neighbor,eye,0,0,out double held) && held==before,"Aim contact diverged from the old displayed mesh");
        NativeMountains.Publish(staged);
        Require(Top(Geometry(neighbor))==Top(Geometry(neighbor,staged)),"Publication did not switch render and collision together");
        Require(NativeMountains.GeometryRevision(neighbor)!=revision,"Published chunk did not invalidate old aim cache");
        Require(!NativeAimTargeting.TryHitThing(neighbor,eye,0,0,out _),"Old high contact survived publication of the lower mesh");
        var added=new Building { Position=new(64,0,39), Map=map, thingIDNumber=88888, def=new(){building=new(){isNaturalRock=true}} };
        map.thingGrid.ThingsListAtFast(added.Position).Add(added);
        NativeMountains.Invalidate(map,added.Position); NativeMountains.Update(map);
        Require(Geometry(added).Vertices.Count==0,"New rock acquired invisible collision before its mesh was published");
        NativeMountains.Publish(NativeMountains.Capture(5,3));
        Require(Geometry(added).Vertices.Count>0,"New rock remained absent after its mesh was published");
        NativeMountains.Remove(5,5);
        Require(Geometry(neighbor).Vertices.Count==0 && !NativeAimTargeting.TryHitThing(neighbor,eye,0,0,out _),"Evicted chunk retained invisible mountain collisions");
    }
    static void ChunkIsolation()
    {
        var map=Mountain(); var left=map.thingGrid.ThingsListAtFast(new(63,0,64))[0];
        var right=map.thingGrid.ThingsListAtFast(new(72,0,64))[0];
        float oldRight=Top(Geometry(right)); int oldRightRevision=NativeMountains.GeometryRevision(right);
        map.thingGrid.ThingsListAtFast(new(64,0,64)).Clear(); NativeMountains.Invalidate(map,new(64,0,64)); NativeMountains.Update(map);
        var newRight=NativeMountains.Capture(6,5);
        Require(Top(Geometry(right,newRight))<oldRight,"Cross-chunk neighbor must be affected by mining");
        NativeMountains.Publish(NativeMountains.Capture(5,5));
        Require(Top(Geometry(right))==oldRight && NativeMountains.GeometryRevision(right)==oldRightRevision,"Publishing one chunk changed another chunk's collision");
        NativeMountains.Publish(newRight);
        Require(Top(Geometry(right))<oldRight,"Second chunk did not adopt its published heights");
        NativeMountains.Update(new Map());
        Require(Geometry(left).Vertices.Count==0,"Map change retained old published surfaces");
    }
    static void FoggedExterior()
    {
        var map=Mountain(true); var pawn=new Pawn { Position=new(64,0,20),Map=map,thingIDNumber=99999 };
        var eye=pawn.DrawPos; eye.y=1.55f;
        NativeAimTargeting.Ray(0,-.25f,60,out var dir,out _);
        float actual=float.PositiveInfinity;
        foreach(var list in map.thingGrid.Cells.Values) foreach(var rock in list)
        {
            var b=Geometry(rock);
            for(int i=0;i<b.Triangles.Count;i+=3)
                if(NativeAimTargeting.RayTriangle(eye,dir,b.Vertices[b.Triangles[i]],b.Vertices[b.Triangles[i+1]],b.Vertices[b.Triangles[i+2]],out float t))actual=Math.Min(actual,t);
        }
        Require(float.IsFinite(actual) && (eye+dir*actual).y>5,"Fixture must contain a visible above-fog slope");
        Require(NativeAimTargeting.TrySelect(pawn,0,-.25f,60,out var hit),"Visible fogged exterior could not be aimed at");
        Require(Math.Abs(hit.Distance-actual)<.001 && hit.HitSurface,"Selection stopped in empty air instead of on the visible slope");
        Require(!hit.HitThing && hit.Target.Thing==null && hit.Label=="mountain","Unrevealed rock identity leaked from exterior selection");
        Require(hit.Point.y>NativeScene.FogHeight,"Exterior contact entered underground fog");
    }
    static void FogPrivacy()
    {
        var map=Mountain(true); var pawn=new Pawn { Position=new(64,0,20),Map=map,thingIDNumber=99999 };
        var hiddenPawn=new Pawn { Position=new(64,0,45),Map=map,thingIDNumber=99998,LabelShortCap="hidden enemy" };
        map.thingGrid.ThingsListAtFast(hiddenPawn.Position).Add(hiddenPawn);
        Require(NativeAimTargeting.TrySelect(pawn,0,0,60,out var low),"Low fog-boundary aim should retain the safe visible endpoint");
        Require(!low.HitThing && !low.HitSurface && !low.Point.ToIntVec3().Fogged(map) && low.Point.z<40,"Ground ray exposed hidden rock or occupants");
        Require(NativeAimTargeting.TrySelect(pawn,0,-.25f,60,out var high) && high.Label=="mountain","Hidden occupant changed exterior selection");
        var queries=typeof(NativeAimTargeting).GetField("query",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        int before=(int)queries.GetValue(null);
        Require(NativeAimTargeting.TrySelect(pawn,0,-.25f,60,out _) && (int)queries.GetValue(null)==before,
            "Identical frame/tick input repeated the expensive geometry query");
        Time.frameCount++;
        Require(NativeAimTargeting.TrySelect(pawn,0,-.25f,60,out _) && (int)queries.GetValue(null)>before,
            "Next render frame reused stale aim geometry");
        Require(!NativeAimTargeting.TrySelect(pawn,0,-.25f,10,out var shortHit) || shortHit.Distance<=10,"Exterior aiming bypassed native weapon range");
        map.Fog.Clear();
        Find.TickManager.TicksGame++; // Fog changed on the next simulated game tick.
        Require(NativeAimTargeting.TrySelect(pawn,0,-.25f,60,out var known) && known.HitThing && known.Target.Thing!=null,"Revealed rock lost ordinary Thing selection");
        var emptyMap=new Map(); pawn.Map=emptyMap; pawn.Position=new(100,0,100);
        var continuousEye=new Vector3(100.99f,1.55f,100.99f);
        Require(NativeAimTargeting.TrySelectFrom(pawn,continuousEye,(float)Math.PI/2,0,.6f,false,out var clearance)
            && Math.Abs(clearance.Distance-.6f)<.0001f,"Continuous muzzle clearance was shortened by cell-centre range rounding");
        Require(!NativeAimTargeting.TrySelectFrom(pawn,continuousEye,(float)Math.PI/2,0,.6f,true,out var constrained)
            || NativeAimTargeting.CellWithinRange(100,100,constrained.Point.ToIntVec3().x,constrained.Point.ToIntVec3().z,.6f),
            "Cell-constrained callers lost native cell range validation");
    }
    static void NearestAdmission()
    {
        var candidates=new NearestCandidates<int>(8192);
        for(int i=0;i<8192;i++) candidates.Offer(i,10000+i,i);
        Require(candidates.Offer(99999,1,99999),"Near plant was discarded after 8192 far candidates");
        Require(candidates.Count==8192 && candidates.Ordered()[0].Value==99999,"Near plant was not prioritized within the bounded queue");
        var random=new Random(17); var input=Enumerable.Range(0,20000).Select(id=>(id,distance:(float)random.Next(300))).ToArray();
        int[] expected=input.OrderBy(p=>p.distance).ThenBy(p=>p.id).Take(8192).Select(p=>p.id).ToArray();
        foreach(var sequence in new[]{input,input.Reverse().ToArray()})
        {
            candidates.Clear(); foreach(var p in sequence)candidates.Offer(p.id,p.distance,p.id);
            Require(candidates.Ordered().Select(p=>p.Id).SequenceEqual(expected),"Nearest admission depends on chunk enumeration order or mishandles ties");
            Require(!candidates.Offer(-1,float.NaN,-1) && !candidates.Offer(-1,float.PositiveInfinity,-1),"Invalid candidate corrupted the queue");
        }
    }
    static int Main(string[] args)
    {
        var scenarios=new (string name,Action run)[]{("published-mountain-geometry",PublishedGeometry),("independent-chunk-publication",ChunkIsolation),
            ("fogged-mountain-exterior",FoggedExterior),("underground-fog-privacy",FogPrivacy),("nearest-plant-admission",NearestAdmission)};
        foreach(var scenario in scenarios)
        {
            if(args.Length>0 && args[0]!=scenario.name)continue;
            try{scenario.run();Console.WriteLine("PASS: "+scenario.name);}
            catch(Exception error){Console.Error.WriteLine("FAIL: "+scenario.name+": "+error.Message);return 1;}
        }
        Console.WriteLine("Production terrain/aim scenarios passed with simulated map boundaries. No GPU/game session started.");return 0;
    }
}
