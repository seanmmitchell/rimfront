using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

internal static partial class Program
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static Type builder, vector, color;
    static void Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3) throw new ArgumentException("Pass the local RimWorld 1.7 directory, compiled RimFront 1.7 DLL path, and optional Harmony 1.7 assembly directory.");
        string game = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(game, name.Name + ".dll");
            if (!File.Exists(path) && args.Length == 3) path = Path.Combine(Path.GetFullPath(args[2]), name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
        var unity = Assembly.Load("UnityStubs");
        vector = unity.GetType("UnityEngine.Vector3", true);
        color = unity.GetType("UnityEngine.Color", true);
        builder = assembly.GetType("RimFront.NativeScene+Builder", true);
        object box = NewBuilder();
        Invoke(box, "Box", Vec(2, 3, 4), Vec(2, 4, 6), Tint());
        List<float[]> points = Points(box);
        Check(points.Count == 24, "box has one independent normal per face");
        Check(Indices(box).Count == 36, "box has 12 triangles");
        CheckBounds(points, new[] { 1f, 1f, 1f }, new[] { 3f, 5f, 7f });
        CheckTriangles(box, new[] { 2f, 3f, 4f });
        List<float[]> boxLights = LightingPoints(box);
        Check(boxLights.Count == points.Count, "each geometry vertex has a lighting sample");
        for (int face = 0; face < 6; face++)
        {
            float[] light = boxLights[face * 4];
            Check(light[0] < 1 || light[0] > 3 || light[1] < 1 || light[1] > 5 || light[2] < 1 || light[2] > 7, "wall samples light outside its solid face");
            for (int vertex = 1; vertex < 4; vertex++) Check(Distance(light, boxLights[face * 4 + vertex]) < .00001, "one coherent face-lighting cell");
        }
        object floor = NewBuilder();
        Invoke(floor, "Floor", Vec(10, 0, 20), Tint());
        Check(Points(floor).Count == 4 && Indices(floor).Count == 6, "floor topology");
        CheckTriangles(floor, new[] { 10f, -1f, 20f });
        foreach (float[] light in LightingPoints(floor)) Check(Math.Abs(light[0] - 10) < .00001 && Math.Abs(light[2] - 20) < .00001, "floor samples center instead of neighbor-cell corners");
        object pyramid = NewBuilder();
        Invoke(pyramid, "Pyramid", Vec(0, 0, 0), 2f, 3f, Tint());
        Check(Points(pyramid).Count == 12 && Indices(pyramid).Count == 12, "pyramid sides topology");
        CheckBounds(Points(pyramid), new[] { -1f, 0f, -1f }, new[] { 1f, 3f, 1f });
        CheckTriangles(pyramid, new[] { 0f, 1f, 0f });
        object transformed = NewBuilder();
        builder.GetField("Origin", Flags).SetValue(transformed, Vec(12, 4, -5));
        Type quaternion = unity.GetType("UnityEngine.Quaternion", true);
        object rotation = quaternion.GetMethod("Euler", new[] { typeof(float), typeof(float), typeof(float) }).Invoke(null, new object[] { 0f, 90f, 0f });
        builder.GetField("Rotation", Flags).SetValue(transformed, rotation);
        Invoke(transformed, "Box", Vec(0, 0, 0), Vec(2, 4, 6), Tint());
        CheckBounds(Points(transformed), new[] { 9f, 2f, -6f }, new[] { 15f, 6f, -4f });
        CheckTriangles(transformed, new[] { 12f, 4f, -5f });
        Type scene = assembly.GetType("RimFront.NativeScene", true);
        int radius = (int)scene.GetProperty("ChunkRadius", Flags).GetValue(null);
        int maximum = (int)scene.GetField("MaximumChunks", Flags).GetRawConstantValue();
        Check((2 * radius + 1) * (2 * radius + 1) <= maximum, "cache neighborhood fits declared budget");
        Check((int)scene.GetField("ChunkSize", Flags).GetRawConstantValue() > 0, "chunk size positive");
        TestMountainsAndRange(assembly);
        TestChunkRebuildOrder(assembly);
        TestLighting(assembly);
        TestWeather(assembly);
        TestWeapons(assembly);
        TestEffectMath(assembly);
        TestReloadAnimation(assembly);
        TestAnalogCombat(assembly, unity);
        Console.WriteLine($"PASS: {checks} native geometry, lighting, weather, weapons, reload, CE coordinates, effect math and budget checks using compiled implementations and local 1.7 math. No GPU or game session was started.");
    }
    static void TestMountainsAndRange(Assembly assembly)
    {
        Type mountains = assembly.GetType("RimFront.NativeMountains", true);
        object cell = NewBuilder();
        mountains.GetMethod("AddCell", Flags).Invoke(null, new object[] { cell, 11, 23, 6f, 8f, 9f, 7f, Tint() });
        CheckBounds(Points(cell), new[] { 11f, 0f, 23f }, new[] { 12f, 9f, 24f });
        CheckTriangles(cell, new[] { 11.5f, 2f, 23.5f });
        Check(Points(cell).Count == 30 && Indices(cell).Count == 30, "mountain has a faceted top and four solid sides");
        Check(LightingPoints(cell).Count == Points(cell).Count, "mountain faces have native lighting samples");
        object next = NewBuilder();
        mountains.GetMethod("AddCell", Flags).Invoke(null, new object[] { next, 12, 23, 8f, 10f, 11f, 9f, Tint() });
        var a = Points(cell); var b = Points(next);
        foreach (float[] seam in new[] { new[] { 12f, 8f, 23f }, new[] { 12f, 9f, 24f } })
            Check(a.Exists(p => Distance(p, seam) < .00001) && b.Exists(p => Distance(p, seam) < .00001), "adjacent rock meshes share exact vertices at a chunk boundary");
        Type aim = assembly.GetType("RimFront.NativeAimTargeting", true);
        MethodInfo triangle = aim.GetMethod("RayTriangle", Flags);
        bool hit = false;
        var indices = Indices(cell);
        for (int i = 0; i < indices.Count; i += 3)
        {
            object[] args = { Vec(11.5f, 6, 20), Vec(0, 0, 1),
                Vec(a[indices[i]][0], a[indices[i]][1], a[indices[i]][2]),
                Vec(a[indices[i+1]][0], a[indices[i+1]][1], a[indices[i+1]][2]),
                Vec(a[indices[i+2]][0], a[indices[i+2]][1], a[indices[i+2]][2]), 0f };
            hit |= (bool)triangle.Invoke(null, args) && Math.Abs((float)args[5] - 3f) < .00001;
        }
        Check(hit, "aim ray contacts a mountain above the old wall and coarse-box height");
        Type mod = assembly.GetType("RimFront.RimFrontMod", true), settingsType = assembly.GetType("RimFront.RimFrontSettings", true);
        FieldInfo settings = mod.GetField("Settings", Flags);
        object saved = settings.GetValue(null), testSettings = Activator.CreateInstance(settingsType);
        try
        {
            settings.SetValue(null, testSettings);
            Type scene = assembly.GetType("RimFront.NativeScene", true);
            MethodInfo ray = aim.GetMethod("Ray", Flags);
            foreach (int step in new[] { 0, 1, 2 })
            {
                settingsType.GetField("extraViewRange").SetValue(testSettings, step);
                float distance = 60f + step * 30;
                Check((float)scene.GetProperty("ViewDistance", Flags).GetValue(null) == distance, "live settings drive the shared rendering range");
                Check((float)scene.GetProperty("ViewDistanceSquared", Flags).GetValue(null) == distance * distance, "live radial culling matches range");
                Check((float)assembly.GetType("RimFront.NativeVegetation", true).GetProperty("RenderRadius", Flags).GetValue(null) == distance + 2, "vegetation follows every live range setting");
                foreach (float pitch in new[] { -.8f, 0f, .8f })
                {
                    object[] args = { 0f, pitch, 200f, Vec(0, 0, 0), 0f };
                    Check((bool)ray.Invoke(null, args), "extended view produces an aim ray");
                    float[] direction = Coordinates(args[3]);
                    double length = Math.Sqrt(1 + direction[1] * direction[1]) * (float)args[4];
                    Check(Math.Abs(length - distance) < .001, "pitched aim reaches each configured view distance");
                    args[2] = 20f;
                    ray.Invoke(null, args);
                    Check((float)args[4] <= 20f, "extended visibility retains native weapon range");
                }
            }
        }
        finally { settings.SetValue(null, saved); }
    }
    static object NewBuilder() => Activator.CreateInstance(builder, true);
    static void TestReloadAnimation(Assembly assembly)
    {
        Type reload = assembly.GetType("RimFront.NativeReloadAnimation", true);
        Type kind = assembly.GetType("RimFront.NativeWeapons+WeaponKind", true);
        MethodInfo progress = reload.GetMethod("ComputeProgress", Flags), evaluate = reload.GetMethod("Evaluate", Flags);
        float Progress(int remaining,int duration)=>(float)progress.Invoke(null,new object[]{remaining,duration});
        Check(Progress(100,100)==0 && Progress(0,100)==1,"reload progress uses actual remaining toil ticks");
        Check(Progress(-1,100)==1 && Progress(200,100)==0,"reload progress clamps delayed/end states");
        Check(Progress(10,0)==0,"unknown reload duration has no fabricated completion");
        Check(Progress(50,100)==.5f,"half the real toil duration remains halfway through reload");
        float last=-1;
        for(int remaining=100;remaining>=0;remaining--){float p=Progress(remaining,100);Check(p>=last && p>=0 && p<=1,"reload progresses monotonically with toil");last=p;}
        foreach(object weapon in Enum.GetValues(kind))
        foreach(float phase in new[]{float.NaN,float.PositiveInfinity,-1f,0f,.2f,.5f,.9f,1f,2f})
        {
            object pose=evaluate.Invoke(null,new[]{weapon,(object)phase,false});
            foreach(FieldInfo field in pose.GetType().GetFields(Flags))
                if(field.FieldType==vector)Check(Array.TrueForAll(Coordinates(field.GetValue(pose)),float.IsFinite),"reload pose remains finite");
        }
        object rifle=Enum.Parse(kind,"Rifle");
        object start=evaluate.Invoke(null,new[]{rifle,(object)0f,false}), end=evaluate.Invoke(null,new[]{rifle,(object)1f,false});
        foreach(string name in new[]{"WeaponOffset","WeaponEuler","FeedOffset","BoltOffset","LeftHandOffset"})
        {
            FieldInfo field=start.GetType().GetField(name,Flags);
            Check(Distance(Coordinates(field.GetValue(start)),Coordinates(field.GetValue(end)))<.00001,"magazine reload returns to rest: "+name);
        }
        object mid=evaluate.Invoke(null,new[]{rifle,(object).5f,false});
        Check(Coordinates(mid.GetType().GetField("FeedOffset",Flags).GetValue(mid))[1]<-.1f,"magazine visibly leaves weapon during exchange");
        object single=evaluate.Invoke(null,new[]{rifle,(object).88f,true});
        Check(Distance(Coordinates(single.GetType().GetField("BoltOffset",Flags).GetValue(single)),new float[3])<.00001,"single-round insertion does not cycle magazine bolt every shell");
        Type snapshotType=reload.GetNestedType("Snapshot",Flags);
        object snapshot=Activator.CreateInstance(snapshotType);
        MethodInfo fromSnapshot=reload.GetMethod("ForSnapshot",Flags);
        void Set(string name,object value)=>snapshotType.GetField(name,Flags).SetValue(snapshot,value);
        object Pose(object weapon)=>fromSnapshot.Invoke(null,new[]{weapon,snapshot});
        object idle=Pose(rifle);
        foreach(FieldInfo field in idle.GetType().GetFields(Flags))
            if(field.FieldType==vector)Check(Distance(Coordinates(field.GetValue(idle)),new float[3])<.00001,"no real reload means identity pose");
        Set("Active",true);
        object unknown=Pose(rifle);
        Check(Coordinates(unknown.GetType().GetField("WeaponOffset",Flags).GetValue(unknown))[1]<0,"unknown real reload timing lowers weapon");
        Set("HasTiming",true);Set("SingleRound",true);Set("ElapsedTicks",30);Set("Progress",.5f);Set("AmmoIncreased",true);
        object shotgun=Pose(Enum.Parse(kind,"Shotgun"));
        Check(!(bool)shotgun.GetType().GetField("ShowFeed",Flags).GetValue(shotgun),"observed insertion hides loose shell");
        object revolver=Pose(Enum.Parse(kind,"Revolver"));
        Check((bool)revolver.GetType().GetField("ShowFeed",Flags).GetValue(revolver),"observed insertion never hides revolver cylinder");
        Set("Progress",1f);
        object between=Pose(Enum.Parse(kind,"Revolver"));
        Check(Coordinates(between.GetType().GetField("FeedOffset",Flags).GetValue(between))[0]<-.1f,"cylinder remains open between real per-round toils");
    }
    static void TestAnalogCombat(Assembly assembly, Assembly unity)
    {
        Type adapter = assembly.GetType("RimFront.AnalogCombatCompatibility",true);
        object recentered=adapter.GetMethod("Recenter",Flags).Invoke(null,new[]{Vec(10,7,20),Vec(3,99,4)});
        Check(Distance(Coordinates(recentered),new[]{3f,7f,4f})<.00001,"CE recenter preserves native projectile/body height");
        MethodInfo predict=adapter.GetMethod("PredictPosition",Flags);
        foreach(float seconds in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            Check(Distance(Coordinates(predict.Invoke(null,new[]{Vec(3,7,4),Vec(2,9,-3),(object)seconds})),new[]{3f,0f,4f})<.00001,"invalid prediction time has no extrapolation");
        Check(Distance(Coordinates(predict.Invoke(null,new[]{Vec(3,7,4),Vec(2,9,-3),(object)2f})),new[]{7f,0f,-2f})<.00001,"CE lead follows actual analog XZ velocity");
        Type bounds=unity.GetType("UnityEngine.Bounds",true),cell=Assembly.Load("Assembly-CSharp").GetType("Verse.IntVec3",true);
        MethodInfo overlap=adapter.GetMethod("OverlapsCell",Flags);
        object box=Activator.CreateInstance(bounds,Vec(.9f,100,.9f),Vec(.4f,1,.4f));
        bool Hits(int x,int z)=>(bool)overlap.Invoke(null,new[]{box,Activator.CreateInstance(cell,x,0,z)});
        Check(Hits(0,0)&&Hits(1,0)&&Hits(0,1)&&Hits(1,1),"CE broadphase includes footprint crossing four cell corners regardless of height");
        Check(!Hits(2,0)&&!Hits(-1,0),"CE broadphase excludes disjoint cells");
        box=Activator.CreateInstance(bounds,Vec(.5f,0,.5f),Vec(1,1,1));
        Check(Hits(1,0),"CE broadphase includes exact cell boundary for native narrowphase");
    }
    static void TestWeather(Assembly assembly)
    {
        Type weather = assembly.GetType("RimFront.NativeWeather", true);
        MethodInfo count = weather.GetMethod("ParticleCount", Flags), sun = weather.GetMethod("SunDirection", Flags);
        foreach (string name in new[] { "MaximumRain", "MaximumSnow" })
        {
            int maximum = (int)weather.GetField(name, Flags).GetRawConstantValue();
            foreach (float rate in new[] { float.NaN, float.PositiveInfinity, -.1f, 0f })
                Check((int)count.Invoke(null, new object[] { rate, maximum }) == 0, "invalid/dry rate has no precipitation");
            Check((int)count.Invoke(null, new object[] { .5f, maximum }) > 0, "positive precipitation visible");
            Check((int)count.Invoke(null, new object[] { 100f, maximum }) == maximum, "weather particle cap respected");
        }
        float[] noon = Coordinates(sun.Invoke(null, new object[] { .5f })), midnight = Coordinates(sun.Invoke(null, new object[] { 0f }));
        Check(noon[1] > .9f && midnight[1] < -.9f, "sun cue follows local day/night");
        float[] dawn = Coordinates(sun.Invoke(null, new object[] { .25f })), dusk = Coordinates(sun.Invoke(null, new object[] { .75f }));
        Check(dawn[0] > 0 && dusk[0] < 0, "sun travels east to west");
        float[] nextDay = Coordinates(sun.Invoke(null, new object[] { 1f }));
        Check(Distance(midnight, nextDay) < .0001, "sun cue wraps continuously at midnight");
    }
    static void TestWeapons(Assembly assembly)
    {
        Type weapons = assembly.GetType("RimFront.NativeWeapons", true), kind = weapons.GetNestedType("WeaponKind", Flags);
        MethodInfo classify = weapons.GetMethod("ClassifyName", Flags), geometry = weapons.GetMethod("BuildGeometry", Flags);
        foreach (var sample in new[] { ("AutoPistol", "Pistol"), ("Revolver", "Revolver"), ("AssaultRifle", "Rifle"), ("PumpShotgun", "Shotgun"), ("SubmachineGun", "Smg"), ("SniperRifle", "Sniper"), ("MachineGun", "Heavy"), ("GrenadeLauncher", "Launcher"), ("LongBow", "Bow"), ("FragGrenade", "Grenade") })
            Check(classify.Invoke(null, new object[] { sample.Item1, true }).ToString() == sample.Item2, "weapon classification: " + sample.Item1);
        Check(classify.Invoke(null, new object[] { "SteelKnife", false }).ToString() == "Blade", "melee blade classification");
        Check(classify.Invoke(null, new object[] { "Club", false }).ToString() == "Blunt", "melee blunt classification");
        var lengths = new Dictionary<string, float>();
        foreach (object value in Enum.GetValues(kind))
        {
            object model = NewBuilder(); geometry.Invoke(null, new[] { model, value });
            List<float[]> points = Points(model); List<int> indices = Indices(model);
            Check(points.Count > 0 && points.Count < 2000, "bounded nonempty weapon model: " + value);
            Check(points.TrueForAll(point => Array.TrueForAll(point, float.IsFinite)), "finite weapon vertices");
            Check(indices.Count % 3 == 0 && indices.TrueForAll(index => index >= 0 && index < points.Count), "valid weapon triangle topology");
            for (int i = 0; i < indices.Count; i += 3)
            {
                float[] a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
                float ux = b[0]-a[0], uy = b[1]-a[1], uz = b[2]-a[2], vx = c[0]-a[0], vy = c[1]-a[1], vz = c[2]-a[2];
                Check(Math.Pow(uy*vz-uz*vy,2)+Math.Pow(uz*vx-ux*vz,2)+Math.Pow(ux*vy-uy*vx,2) > 1e-12, "weapon triangle has area");
            }
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (float[] point in points) { min = Math.Min(min,point[2]); max = Math.Max(max,point[2]); }
            lengths[value.ToString()] = max-min;
            Type part = weapons.GetNestedType("WeaponPart", Flags);
            MethodInfo partGeometry=weapons.GetMethod("BuildPartGeometry",Flags);
            int assembledVertices=0,assembledIndices=0;
            foreach(object piece in Enum.GetValues(part))
            {
                object partBuilder=NewBuilder();partGeometry.Invoke(null,new[]{partBuilder,value,piece});
                var partPoints=Points(partBuilder);var partIndices=Indices(partBuilder);
                Check(partPoints.Count<2000&&partPoints.TrueForAll(p=>Array.TrueForAll(p,float.IsFinite)),"reload part has bounded finite vertices");
                Check(partIndices.Count%3==0&&partIndices.TrueForAll(i=>i>=0&&i<partPoints.Count),"reload part topology valid");
                if(value.ToString()=="Shotgun"&&piece.ToString()=="Feed")continue;
                assembledVertices+=partPoints.Count;assembledIndices+=partIndices.Count;
            }
            Check(assembledVertices==points.Count&&assembledIndices==indices.Count,"assembled model matches reload parts: "+value);
        }
        Check(lengths["Rifle"] > lengths["Pistol"] && lengths["Sniper"] > lengths["Rifle"], "weapon silhouettes have distinct barrel lengths");
    }
    static void TestEffectMath(Assembly assembly)
    {
        Type math = assembly.GetType("RimFront.NativeEffectMath", true);
        MethodInfo finite = math.GetMethod("IsFinite", Flags);
        Check((bool)finite.Invoke(null, new[] { Vec(0,0,0) }), "zero projectile position is finite");
        Check(!(bool)finite.Invoke(null, new[] { Vec(float.NaN,1,1) }), "NaN projectile position rejected");
        Check(!(bool)finite.Invoke(null, new[] { Vec(1,float.PositiveInfinity,1) }), "infinite projectile position rejected");
        MethodInfo height = math.GetMethod("VisualVanillaHeight", Flags), reflect = math.GetMethod("ReflectVisualDirection", Flags), beam = math.GetMethod("BeamTransform", Flags);
        float Height(float t) => (float)height.Invoke(null, new object[] { 1f, 2f, t, 3f });
        Check(Height(-1) == 1 && Height(0) == 1 && Height(1) == 2 && Height(2) == 2, "visual trajectory endpoints clamp");
        Check(Math.Abs(Height(.5f) - 4.5f) < .0001f, "visual trajectory arc midpoint");
        float[] reflected = Coordinates(reflect.Invoke(null, new[] { Vec(1,-1,0), Vec(0,1,0) }));
        Check(reflected[0] > 0 && reflected[1] > 0 && Math.Abs(Distance(reflected,new float[3])-1) < .0001, "cosmetic reflection leaves surface normalized");
        Check(Array.TrueForAll(Coordinates(reflect.Invoke(null,new[] { Vec(0,0,0),Vec(0,0,0) })),float.IsFinite), "zero reflection vectors have finite fallback");
        object matrix = beam.Invoke(null, new[] { Vec(1,2,3), Vec(4,6,3), (object).02f });
        MethodInfo point = matrix.GetType().GetMethod("MultiplyPoint3x4", new[] { vector });
        Check(Distance(Coordinates(point.Invoke(matrix,new[] { Vec(0,0,-.5f) })),new[] {1f,2f,3f}) < .001, "beam begins at observed projectile segment");
        Check(Distance(Coordinates(point.Invoke(matrix,new[] { Vec(0,0,.5f) })),new[] {4f,6f,3f}) < .001, "beam ends at observed projectile segment");
        object zero = beam.Invoke(null,new[] { Vec(1,1,1),Vec(1,1,1),(object)0f });
        Check(Array.TrueForAll(Coordinates(point.Invoke(zero,new[] {Vec(0,0,0)})),float.IsFinite), "zero-length beam remains finite");
    }
    static float[] Coordinates(object value) => new[] {(float)vector.GetField("x").GetValue(value),(float)vector.GetField("y").GetValue(value),(float)vector.GetField("z").GetValue(value)};
    static double Distance(float[] a,float[] b) => Math.Sqrt(Math.Pow(a[0]-b[0],2)+Math.Pow(a[1]-b[1],2)+Math.Pow(a[2]-b[2],2));
    static void TestLighting(Assembly assembly)
    {
        Type lighting = assembly.GetType("RimFront.NativeLighting", true);
        MethodInfo compose = lighting.GetMethod("Compose", Flags);
        object darkLamp = Activator.CreateInstance(color, 0f, 0f, 0f, 1f);
        float[] Evaluate(float sky, bool roofed, object lamp) => Channels(compose.Invoke(null, new[] { (object)sky, roofed, lamp }));
        float[] night = Evaluate(0, false, darkLamp), day = Evaluate(1, false, darkLamp);
        float[] roofDay = Evaluate(1, true, darkLamp), roofNight = Evaluate(0, true, darkLamp);
        for (int channel = 0; channel < 3; channel++)
        {
            Check(day[channel] > night[channel], "day is brighter than night");
            Check(Math.Abs(roofDay[channel] - roofNight[channel]) < 0.00001f, "roof blocks daylight contribution");
            Check(night[channel] > 0, "minimum ambient preserves visible silhouettes");
        }
        object redLamp = Activator.CreateInstance(color, .1f, 0f, 0f, 1f);
        float[] red = Evaluate(0, true, redLamp);
        Check(red[0] > roofNight[0], "colored lamp illuminates its channel");
        Check(red[1] == roofNight[1] && red[2] == roofNight[2], "colored lamp does not add unrelated channels");
        foreach (float sky in new[] { -10f, 0f, .5f, 1f, 10f })
        foreach (float lampValue in new[] { -2f, 0f, .2f, 2f })
        {
            float[] value = Evaluate(sky, false, Activator.CreateInstance(color, lampValue, lampValue, lampValue, 1f));
            foreach (float channel in value) Check(float.IsFinite(channel) && channel >= 0 && channel <= 1.20001f, "lighting output finite and bounded");
            Check(value[3] == 1f, "lighting stays opaque");
        }
    }
    static float[] Channels(object value) => new[] { (float)color.GetField("r").GetValue(value), (float)color.GetField("g").GetValue(value), (float)color.GetField("b").GetValue(value), (float)color.GetField("a").GetValue(value) };
    static object Vec(float x, float y, float z) => Activator.CreateInstance(vector, x, y, z);
    static object Tint() => Activator.CreateInstance(color, 0.7f, 0.5f, 0.2f, 1f);
    static void Invoke(object target, string method, params object[] args) => builder.GetMethod(method, Flags).Invoke(target, args);
    static List<float[]> Points(object target)
    {
        var result = new List<float[]>();
        foreach (object point in (IEnumerable)builder.GetField("Vertices", Flags).GetValue(target))
            result.Add(new[] { (float)vector.GetField("x").GetValue(point), (float)vector.GetField("y").GetValue(point), (float)vector.GetField("z").GetValue(point) });
        return result;
    }
    static List<int> Indices(object target) => (List<int>)builder.GetField("Triangles", Flags).GetValue(target);
    static List<float[]> LightingPoints(object target)
    {
        var result = new List<float[]>();
        foreach (object point in (IEnumerable)builder.GetField("LightingPositions", Flags).GetValue(target)) result.Add(Coordinates(point));
        return result;
    }
    static void CheckBounds(List<float[]> points, float[] min, float[] max)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach (float[] point in points) { lo = Math.Min(lo, point[axis]); hi = Math.Max(hi, point[axis]); }
            Check(Math.Abs(lo - min[axis]) < 0.001f && Math.Abs(hi - max[axis]) < 0.001f, "geometry bounds axis " + axis);
        }
    }
    static void CheckTriangles(object target, float[] center)
    {
        var points = Points(target); var indices = Indices(target);
        foreach (int index in indices) Check(index >= 0 && index < points.Count, "triangle index in bounds");
        for (int i = 0; i < indices.Count; i += 3)
        {
            float[] a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
            float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
            float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            Check(nx * nx + ny * ny + nz * nz > 0.00001f, "non-degenerate triangle");
            float dot = nx * ((a[0] + b[0] + c[0]) / 3 - center[0]) + ny * ((a[1] + b[1] + c[1]) / 3 - center[1]) + nz * ((a[2] + b[2] + c[2]) / 3 - center[2]);
            Check(dot > 0, "outward triangle winding");
        }
    }
    static void Check(bool result, string reason) { checks++; if (!result) throw new Exception("FAIL: " + reason); }
}
