using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static partial class Program
{
    static void TestCoverAndResourceModels(Assembly assembly)
    {
        Type objects = assembly.GetType("RimFront.NativeObjectModels",true), kinds = objects.GetNestedType("Kind",Flags);
        MethodInfo classify = objects.GetMethod("Classify",Flags), build = objects.GetMethod("Build",Flags);
        foreach (var sample in new[] { ("Barricade","Barricade"),("SteelBarricade","Barricade"),
            ("CE_Embrasure","Embrasure"),("CE_ReinforcedEmbrasure","Embrasure"),("Sandbags","Sandbags"),
            ("WoodLog","Log"),("Steel","Metal"),("BlocksGranite","Stone"),("BlocksSandstone","Stone"),("ChunkGranite","Chunk") })
        {
            bool building = sample.Item2 == "Barricade" || sample.Item2 == "Embrasure" || sample.Item2 == "Sandbags";
            Check(classify.Invoke(null,new object[] { sample.Item1,building,false,false,false,false }).ToString() == sample.Item2,
                "cover and material definitions have distinct silhouettes: "+sample.Item1);
        }
        object Mesh(string name,int count,object tint = null)
        {
            object mesh = Activator.CreateInstance(builder,true);
            build.Invoke(null,new object[] { mesh,Enum.Parse(kinds,name),1f,1f,1f,
                tint ?? (name == "Metal" ? Tint(.36f,.42f,.43f) : Tint(.55f,.53f,.47f)),count });
            return mesh;
        }
        MethodInfo signature = objects.GetMethod("ItemStackSignature",Flags), pieceCount = objects.GetMethod("ResourceStackCount",Flags);
        foreach (string name in new[] { "Log","Metal","Stone" })
        {
            List<float[]> previous = null;
            int previousStamp = -1, previousPieces = -1;
            for (int count = 0; count <= 120; count++)
            {
                object mesh = Mesh(name,count);
                List<float[]> points = Vectors(mesh,"Vertices");
                int stamp = (int)signature.Invoke(null,new object[] { count });
                int pieces = (int)pieceCount.Invoke(null,new object[] { count });
                Check(pieces == (count == 0 ? 0 : Math.Min(18,1+(count-1)/5)),"pile tracks five-unit quantity steps: "+name);
                if (count == 0) Check(points.Count == 0,"empty stack has no resource pieces: "+name);
                else
                {
                    ValidateMesh(mesh,name+" quantity "+count);
                    Check(points.All(p=>Math.Abs(p[0])<.5f && Math.Abs(p[2])<.5f && p[1]>=0 && p[1]<.6f),
                        "stockpile stays on the ground and inside one tile: "+name);
                    if (previous != null && count > 1)
                    {
                        Check(points.Count>=previous.Count,"adding resources never removes pieces: "+name);
                        bool same = points.Count == previous.Count && points.Zip(previous).All(p=>Close(p.First,p.Second));
                        Check(same || stamp != previousStamp,"every actual vertex change invalidates the scene cache: "+name);
                        Check(pieces == previousPieces || points.Count>previous.Count,"quantity step adds visible geometry: "+name);
                    }
                }
                previous = points; previousStamp = stamp; previousPieces = pieces;
            }
            Check(Vectors(Mesh(name,int.MaxValue),"Vertices").Count == previous.Count,"oversized modded stacks stay bounded: "+name);
            Check(Vectors(Mesh(name,-1),"Vertices").Count == 0,"invalid negative stacks have no pieces: "+name);
            Check(Vectors(Mesh(name,75),"Vertices").Max(p=>p[1])>Vectors(Mesh(name,1),"Vertices").Max(p=>p[1]),
                "full native stack is visibly taller than a single unit: "+name);
            string label = name == "Log" ? "Wood" : name == "Metal" ? "Steel" : "Stone blocks";
            foreach (int count in new[] { 1,25,50,75 })
                AddCatalog(label+" / "+count+(count == 1 ? " unit" : " units"),Mesh(name,count),true);
        }
        foreach (string name in new[] { "Barricade","Embrasure" })
        {
            object mesh = Mesh(name,1);
            ValidateMesh(mesh,name);
            Check(Vectors(mesh,"Vertices").Min(p=>p[1])>=-.00001f,"cover is grounded: "+name);
            var firstColors = ((IEnumerable)Get(mesh,"Colors")).Cast<object>().Select(c=>(float)Get(c,"r")).ToArray();
            var secondColors = ((IEnumerable)Get(Mesh(name,1,Tint(.2f,.7f,.9f)),"Colors")).Cast<object>().Select(c=>(float)Get(c,"r")).ToArray();
            Check(!firstColors.SequenceEqual(secondColors),"cover retains stuff/paint tint: "+name);
        }
        object embrasure = Mesh("Embrasure",1), barricade = Mesh("Barricade",1);
        AddCatalog("Barricade / wood",Mesh("Barricade",1,Tint(.43f,.29f,.17f)),true);
        AddCatalog("Barricade / steel",Mesh("Barricade",1,Tint(.36f,.42f,.43f)),true);
        AddCatalog("Embrasure / stone",embrasure,true);
        AddCatalog("Embrasure / steel",Mesh("Embrasure",1,Tint(.36f,.42f,.43f)),true);
        foreach (float[] direction in new[] { new[] { 0f,0f,1f },new[] { 1f,0f,0f },new[] { 0f,0f,-1f },new[] { -1f,0f,0f } })
        {
            Check(!ObjectRayHit(assembly,embrasure,direction,1.55f),"embrasure port is open at eye height on every cardinal face");
            Check(ObjectRayHit(assembly,embrasure,direction,.8f),"embrasure solid base stops low rays");
            Check(ObjectRayHit(assembly,embrasure,direction,2.1f),"embrasure lintel stops high rays");
        }
        Check(!ObjectRayHit(assembly,barricade,new[] { 0f,0f,1f },1.55f),"standing ray clears waist-high barricade");
        Check(ObjectRayHit(assembly,barricade,new[] { 0f,0f,1f },.45f),"barricade rails intercept low rays");
    }

    static bool ObjectRayHit(Assembly assembly,object mesh,float[] direction,float height)
    {
        MethodInfo ray = assembly.GetType("RimFront.NativeAimTargeting",true).GetMethod("RayTriangle",Flags);
        List<float[]> points = Vectors(mesh,"Vertices");
        var indices = (List<int>)Get(mesh,"Triangles");
        object Vec(float[] p) => V(p[0],p[1],p[2]);
        for (int i = 0; i < indices.Count; i += 3)
            if ((bool)ray.Invoke(null,new object[] { V(-direction[0]*2,height,-direction[2]*2),Vec(direction),
                Vec(points[indices[i]]),Vec(points[indices[i+1]]),Vec(points[indices[i+2]]),0f })) return true;
        return false;
    }
}
