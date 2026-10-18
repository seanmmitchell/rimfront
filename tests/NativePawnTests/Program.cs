using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

internal static partial class Program
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Type models,appearance,builder,vector,color,form,shape,part;
    static int checks;
    static readonly List<object> catalog=new List<object>();
    static readonly List<object> weaponCatalog=new List<object>();
    static readonly List<object> objectCatalog=new List<object>();
    static readonly List<object> actionCatalog=new List<object>();
    static int Main(string[] args)
    {
        try
        {
            if(args.Length<3||args.Length>4)throw new ArgumentException("Pass game directory, RimFront DLL, Harmony directory, and optional catalog JSON path.");
            string game=Path.GetFullPath(args[0]),harmony=Path.GetFullPath(args[2]);
            AssemblyLoadContext.Default.Resolving+=(_,name)=>
            {
                string path=Path.Combine(game,name.Name+".dll");
                if(!File.Exists(path))path=Path.Combine(harmony,name.Name+".dll");
                return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
            };
            Assembly assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
            Assembly unity=Assembly.Load("UnityStubs");
            vector=unity.GetType("UnityEngine.Vector3",true);color=unity.GetType("UnityEngine.Color",true);
            models=assembly.GetType("RimFront.NativePawnModels",true);appearance=models.GetNestedType("Appearance",Flags);
            builder=assembly.GetType("RimFront.NativeScene+Builder",true);
            Type rig=assembly.GetType("RimFront.NativePawnRig",true);
            form=rig.GetNestedType("Form",Flags);shape=rig.GetNestedType("BodyShape",Flags);part=rig.GetNestedType("Part",Flags);
            TestMachineGuns(assembly);
            TestModels();
            TestAppearance();
            TestAnimation(assembly);
            TestObjects(assembly);
            TestCoverAndResourceModels(assembly);
            TestOutfitsAndPoses(assembly,unity);
            TestEquipmentAttachments(assembly,unity);
            TestShieldEffects(assembly);
            TestShieldGhost056(assembly);
            TestShieldContact(assembly);
            TestShieldSession(assembly);
            TestLyingAim(assembly);
            TestPawnAimGeometry(assembly);
            TestCrouchCover(assembly);
            TestBloodEffects(assembly);
            TestImpactEffects(assembly);
            TestBloodFilth(assembly);
            TestTargetIndicators(assembly);
            TestFreeAim(assembly);
            TestFreeFire(assembly);
            TestShotSource(assembly);
            TestCeTraversal(assembly);
            TestVanillaAim(assembly);
            TestVanillaWorldCollision(assembly);
            TestAimLifecycle(assembly);
            TestAttackLifecycle(assembly);
            TestFireModes(assembly);
            TestFireDebug(assembly);
            TestShotCadence(assembly);
            TestReloadContinuation(assembly);
            TestVegetation(assembly);
            TestVegetationAdmission(assembly);
            TestPawnSprites(assembly);
            TestNativeMinimap(assembly);
            if(args.Length==4){File.WriteAllText(args[3],JsonSerializer.Serialize(catalog));File.WriteAllText(Path.ChangeExtension(args[3],"weapons.json"),JsonSerializer.Serialize(weaponCatalog));File.WriteAllText(Path.ChangeExtension(args[3],"objects.json"),JsonSerializer.Serialize(objectCatalog));File.WriteAllText(Path.ChangeExtension(args[3],"actions.json"),JsonSerializer.Serialize(actionCatalog));}
            Console.WriteLine($"PASS: {checks} native pawn/object model, appearance and animation checks against compiled helpers. No GPU or game session was started.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error.GetBaseException());return 1;}
    }
    static object NewAppearance(string bodyForm="Human",string bodyShape="Average")
    {
        object result=Activator.CreateInstance(appearance,true);
        Set(result,"Form",Enum.Parse(form,bodyForm));Set(result,"Shape",Enum.Parse(shape,bodyShape));
        Set(result,"Skin",Tint(.72f,.5f,.36f));Set(result,"Hair",Tint(.13f,.08f,.05f));
        Set(result,"SleeveColor",Tint(.72f,.5f,.36f));Set(result,"HairName","Short");
        Set(result,"Apparel",Array.CreateInstance(appearance.GetField("Apparel",Flags).FieldType.GetElementType(),0));
        return result;
    }
    static object Build(object state)
    {
        object result=Activator.CreateInstance(builder,true);
        models.GetMethod("BuildStanding",Flags).Invoke(null,new[]{result,state});return result;
    }
    static void TestModels()
    {
        foreach(object value in Enum.GetValues(form))
        {
            object state=models.GetMethod("Fallback",Flags).Invoke(null,new[]{value}),mesh=Build(state);
            ValidateMesh(mesh,"form "+value);
            List<float[]> points=Vectors(mesh,"Vertices");
            float declaredHeight=(float)models.GetMethod("BaseHeight",Flags).Invoke(null,new[]{value});
            float declaredWidth=(float)models.GetMethod("BaseWidth",Flags).Invoke(null,new[]{value});
            Check(points.Max(p=>p[1])<=declaredHeight+.005f&&points.Min(p=>p[1])>=-.005f,"body height envelope matches family aiming height: "+value);
            Check(points.Max(p=>p[0])-points.Min(p=>p[0])<=declaredWidth+.005f,"body width envelope fits family footprint: "+value);
            int assembledIndex=0;
            foreach(object bodyPart in Enum.GetValues(part))
            {
                if(bodyPart.ToString()=="Count")continue;
                object limb=Activator.CreateInstance(builder,true);models.GetMethod("BuildPart",Flags).Invoke(null,new[]{limb,state,bodyPart});
                float[] pivot=Coordinates(models.GetMethod("RestPivot",Flags).Invoke(null,new[]{state,bodyPart}));
                foreach(float[] point in Vectors(limb,"Vertices"))
                {
                    Check(assembledIndex<points.Count,"rig parts fit standing vertex buffer");
                    for(int axis=0;axis<3;axis++)Check(Math.Abs(point[axis]+pivot[axis]-points[assembledIndex][axis])<.00001,"rig rest pivot reconstructs standing model");
                    assembledIndex++;
                }
            }
            Check(assembledIndex==points.Count,"all standing geometry belongs to a rig part: "+value);
            AddCatalog(value.ToString(),mesh);
        }
        foreach(object value in Enum.GetValues(shape))
        {
            object state=NewAppearance("Human",value.ToString()),mesh=Build(state);
            ValidateMesh(mesh,"human "+value);
            if(value.ToString()!="Average")AddCatalog("Human / "+value,mesh);
        }
    }
    static void TestAppearance()
    {
        MethodInfo classify=models.GetMethod("ClassifyForm",Flags),hash=models.GetMethod("ComputeHash",Flags);
        foreach(var sample in new[]{("Human","Human",true,false,false,"Human"),("Chicken","Bird",false,false,false,"Bird"),("Cobra","Snake",false,false,false,"Serpent"),("Megaspider","Insect",false,false,true,"Insect"),("Centipede","Mech",false,true,false,"MechCentipede"),("Lancer","Mech",false,true,false,"MechBiped"),("ModCrawler","Mech",false,true,false,"MechCrawler"),("Fleshbeast","Entity",false,false,false,"Entity"),("UnknownModAnimal","Unknown",false,false,false,"Quadruped")})
            Check(classify.Invoke(null,new object[]{sample.Item1,sample.Item2,sample.Item3,sample.Item4,sample.Item5}).ToString()==sample.Item6,"family classification/fallback: "+sample.Item1);
        MethodInfo apparelClassify=models.GetMethod("ClassifyApparel",Flags);
        foreach(var sample in new[]{("Parka","Torso Arms","Shell","Coat"),("MarineArmor","Torso Arms Legs","Shell","Plate"),("UnknownModGear","Head","Overhead","Hat"),("UnknownModGear","Feet","Skin","Boots"),("UnknownModGear","Hands","Skin","Gloves"),("UnknownModGear","Torso","Skin","Shirt"),("ReinforcedPants","Legs","Skin","Pants"),("FlakVest","Torso","Middle","Vest"),("ClothHood","Head","Overhead","Hood"),("FlakPants","Legs","Middle","Pants"),("FlakJacket","Torso Arms","Shell","Coat"),("ArmorRecon","Torso Arms Legs","Shell","Plate"),("ArmorVest","Torso","Middle","Vest"),("RoyalCape","Torso","Shell","Coat")})
            Check(apparelClassify.Invoke(null,new object[]{sample.Item1,sample.Item2,sample.Item3}).ToString()==sample.Item4,"apparel classification/fallback: "+sample.Item1+" "+sample.Item2);
        object baseline=NewAppearance();int baselineHash=(int)hash.Invoke(null,new[]{baseline});
        Check((int)hash.Invoke(null,new[]{NewAppearance()})==baselineHash,"equal appearance values share stable cache key");
        foreach(var change in new (string,object)[]{("Skin",Tint(.2f,.4f,.8f)),("Hair",Tint(.8f,.1f,.1f)),("HairName","Long"),("RaceName","modded"),("Shape",Enum.Parse(shape,"Hulk")),("MissingParts",4),("ProstheticParts",4)})
        {object altered=NewAppearance();Set(altered,change.Item1,change.Item2);Check((int)hash.Invoke(null,new[]{altered})!=baselineHash,"appearance cache invalidates: "+change.Item1);}
        int leftArm=Convert.ToInt32(Enum.Parse(part,"LeftArm"));
        object missing=NewAppearance();Set(missing,"MissingParts",1<<leftArm);
        Check(Vectors(Build(missing),"Vertices").Count<Vectors(Build(baseline),"Vertices").Count,"missing arm removes assembled geometry");
        object limb=Activator.CreateInstance(builder,true);
        models.GetMethod("BuildPart",Flags).Invoke(null,new[]{limb,missing,Enum.Parse(part,"LeftArm")});
        Check(Vectors(limb,"Vertices").Count==0,"missing limb has no animated part mesh");
        AddCatalog("Human / missing arm",Build(missing));
        object prosthetic=NewAppearance();Set(prosthetic,"ProstheticParts",1<<leftArm);
        ValidateMesh(Build(prosthetic),"prosthetic arm");AddCatalog("Human / prosthetic",Build(prosthetic));
        Type pieceType=models.GetNestedType("ApparelPiece",Flags),kind=models.GetNestedType("ApparelKind",Flags);
        foreach(object garment in Enum.GetValues(kind))
        {
            object state=NewAppearance(),piece=Activator.CreateInstance(pieceType);
            Set(piece,"Kind",garment);Set(piece,"Color",Tint(.17f,.45f,.65f));Set(piece,"Layer",10);
            Set(piece,"Coverage",models.GetMethod("Coverage",Flags).Invoke(null,new[]{(object)"",garment}));
            Array worn=Array.CreateInstance(pieceType,1);worn.SetValue(piece,0);Set(state,"Apparel",worn);
            Check((int)hash.Invoke(null,new[]{state})!=baselineHash,"apparel invalidates appearance cache: "+garment);
            object mesh=Build(state);ValidateMesh(mesh,"apparel "+garment);AddCatalog("Human / "+garment,mesh);
            int before=(int)hash.Invoke(null,new[]{state});Set(piece,"Color",Tint(.8f,.15f,.2f));worn.SetValue(piece,0);
            Check((int)hash.Invoke(null,new[]{state})!=before,"dye change invalidates cached mesh: "+garment);
        }
    }
    static void TestAnimation(Assembly assembly)
    {
        Type animation=assembly.GetType("RimFront.NativePawnAnimation",true),snapshot=animation.GetNestedType("Snapshot",Flags);
        MethodInfo safe=animation.GetMethod("SafeSpeed",Flags),advance=animation.GetMethod("AdvancePhase",Flags),evaluate=animation.GetMethod("Evaluate",Flags);
        float Safe(float speed)=>(float)safe.Invoke(null,new object[]{speed});
        foreach(float speed in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-10f})Check(Safe(speed)==0,"invalid speed cannot drive gait");
        Check(Safe(100)==12&&Safe(3)==3,"gait speed clamped while ordinary speed retained");
        float Advance(float phase,float speed,int ticks)=>(float)advance.Invoke(null,new object[]{phase,speed,ticks});
        Check(Math.Abs(Advance(1.3f,3,0)-1.3f)<.00001,"paused simulation does not advance gait");
        Check(Math.Abs(Advance(1.3f,0,10)-1.3f)<.00001,"stationary pawn does not advance gait");
        Check(Advance(0,3,30)==Advance(0,3,3000),"offscreen gap cannot create unbounded animation step");
        Check(float.IsFinite(Advance(float.NaN,float.PositiveInfinity,int.MaxValue)),"invalid animation clock input stays finite");
        MethodInfo recoil=animation.GetMethod("RecoilAt",Flags),swing=animation.GetMethod("SwingAt",Flags);
        float last=1;
        for(int tick=0;tick<30;tick++){float current=(float)recoil.Invoke(null,new object[]{tick});Check(current>=0&&current<=last,"recoil decays on simulation ticks");last=current;}
        Check((float)recoil.Invoke(null,new object[]{10})==0,"shot recoil expires");
        Check((float)recoil.Invoke(null,new object[]{-1})==0,"future shot event does not recoil early");
        Check((float)swing.Invoke(null,new object[]{0})==0&&(float)swing.Invoke(null,new object[]{18})==0&&(float)swing.Invoke(null,new object[]{9})>.99f,"melee swing has bounded start/peak/end");
        MethodInfo hurt=animation.GetMethod("HurtAt",Flags);
        Check((float)hurt.Invoke(null,new object[]{0,1f})==1&&(float)hurt.Invoke(null,new object[]{12,1f})==0,"observed injury reaction expires on game ticks");
        Check((float)hurt.Invoke(null,new object[]{-1,1f})==0&&(float)hurt.Invoke(null,new object[]{0,float.NaN})==0,"invalid injury time or strength cannot create a flinch");
        foreach(object family in Enum.GetValues(form))
        foreach(float phase in new[]{0f,.5f,1.57f,3.14f,5f,float.NaN,float.PositiveInfinity})
        {
            object state=Activator.CreateInstance(snapshot);Set(state,"Moving",true);Set(state,"Running",true);Set(state,"Speed",6f);Set(state,"Phase",phase);Set(state,"AimPitch",float.NaN);
            foreach(object bodyPart in Enum.GetValues(part))
            {
                if(bodyPart.ToString()=="Count")continue;
                object pose=evaluate.Invoke(null,new[]{family,state,bodyPart});
                Check(Coordinates(Get(pose,"Offset")).All(float.IsFinite)&&Coordinates(Get(pose,"Euler")).All(float.IsFinite),"finite articulated pose "+family+" "+bodyPart);
                if(bodyPart.ToString().EndsWith("Leg"))Check(Coordinates(Get(pose,"Offset"))[1]>=0,"swing translation never lowers a foot through ground");
            }
        }
        object walking=Activator.CreateInstance(snapshot);Set(walking,"Moving",true);Set(walking,"Speed",4f);Set(walking,"Phase",1.2f);
        object human=Enum.Parse(form,"Human"),left=Enum.Parse(part,"LeftLeg"),right=Enum.Parse(part,"RightLeg");
        object lp=evaluate.Invoke(null,new[]{human,walking,left}),rp=evaluate.Invoke(null,new[]{human,walking,right});
        Check(Math.Abs(Coordinates(Get(lp,"Euler"))[0]+Coordinates(Get(rp,"Euler"))[0])<.00001,"opposite legs have mirrored gait");
        Set(walking,"Dead",true);Set(walking,"Lying",true);
        foreach(object bodyPart in Enum.GetValues(part))
        {object pose=evaluate.Invoke(null,new[]{human,walking,bodyPart});Check(Coordinates(Get(pose,"Offset")).All(v=>v==0)&&Coordinates(Get(pose,"Euler")).All(v=>v==0),"dead body does not continue locomotion");}
        object resting=Activator.CreateInstance(snapshot),melee=Activator.CreateInstance(snapshot);Set(melee,"Melee",1f);Set(melee,"MeleeProgress",.5f);
        MethodInfo weapon=animation.GetMethod("WeaponPose",Flags);
        Check(Coordinates(Get(weapon.Invoke(null,new[]{resting,(object)true}),"Offset")).All(v=>v==0),"idle first-person weapon pose is unchanged");
        Check(Coordinates(Get(weapon.Invoke(null,new[]{melee,(object)true}),"Offset")).Any(v=>Math.Abs(v)>.01f),"observed melee moves the first-person weapon");
        Set(melee,"Lying",true);Check(Coordinates(Get(weapon.Invoke(null,new[]{melee,(object)true}),"Offset")).All(v=>v==0),"lying pawn has no weapon swing");
        Set(melee,"Lying",false);Set(melee,"Reloading",true);Check(Coordinates(Get(weapon.Invoke(null,new[]{melee,(object)true}),"Offset")).All(v=>v==0),"reload pose takes priority over lingering melee event");
    }
    static void ValidateMesh(object mesh,string label)
    {
        List<float[]> vertices=Vectors(mesh,"Vertices");List<int> indices=(List<int>)Get(mesh,"Triangles");
        Check(vertices.Count>0&&vertices.Count<30000,"bounded nonempty mesh: "+label);
        Check(vertices.All(v=>v.All(float.IsFinite)),"finite vertices: "+label);
        Check(vertices.All(v=>v.All(n=>Math.Abs(n)<10)),"local mesh envelope: "+label);
        Check(((ICollection)Get(mesh,"Colors")).Count==vertices.Count,"color buffer matches geometry: "+label);
        Check(indices.Count%3==0&&indices.All(i=>i>=0&&i<vertices.Count),"triangle topology: "+label);
        for(int i=0;i<indices.Count;i+=3)
        {
            float[] a=vertices[indices[i]],b=vertices[indices[i+1]],c=vertices[indices[i+2]];
            double ux=b[0]-a[0],uy=b[1]-a[1],uz=b[2]-a[2],vx=c[0]-a[0],vy=c[1]-a[1],vz=c[2]-a[2];
            Check(Math.Pow(uy*vz-uz*vy,2)+Math.Pow(uz*vx-ux*vz,2)+Math.Pow(ux*vy-uy*vx,2)>1e-14,"nondegenerate pawn triangle: "+label);
        }
    }
    static void TestObjects(Assembly assembly)
    {
        Type objects=assembly.GetType("RimFront.NativeObjectModels",true),kind=objects.GetNestedType("Kind",Flags);
        MethodInfo classify=objects.GetMethod("Classify",Flags),build=objects.GetMethod("Build",Flags);
        foreach(var sample in new[]{("SolarGenerator",true,false,false,false,false,"Solar"),("WindTurbine",true,false,false,false,false,"Wind"),("HospitalBed",true,false,false,false,false,"None"),("UnknownModMachine",true,false,false,false,false,"Machining"),("UnknownModObject",true,false,false,false,false,"None"),("Plant_PineTree",false,true,true,false,false,"None"),("Plant_OakTree",false,true,true,false,false,"Broadleaf"),("Plant_Mushroom",false,true,false,false,true,"Mushroom"),("Apparel_RoyalCape",false,false,false,true,false,"Clothing"),("Apparel_CowboyHat",false,false,false,true,false,"Headwear"),("MedicineIndustrial",false,false,false,false,false,"Medicine"),("RawPotatoes",false,false,false,false,true,"RawFood")})
            Check(classify.Invoke(null,new object[]{sample.Item1,sample.Item2,sample.Item3,sample.Item4,sample.Item5,sample.Item6}).ToString()==sample.Item7,"object classifier precedence/fallback: "+sample.Item1);
        foreach(object family in Enum.GetValues(kind))
        {
            object mesh=Activator.CreateInstance(builder,true);
            string familyName=family.ToString();
            object tint=familyName=="Mushroom"||familyName=="Cactus"||familyName=="Broadleaf"?Tint(.30f,.47f,.25f):Tint(.38f,.48f,.53f);
            build.Invoke(null,new[]{mesh,family,(object)1.8f,1.4f,1f,tint,6});
            if(family.ToString()=="None"){Check(Vectors(mesh,"Vertices").Count==0,"unknown object defers to existing scene fallback");continue;}
            ValidateMesh(mesh,"object "+family);AddCatalog("Object / "+family,mesh,true);
        }
        // Exercise the real dispatch guard using a door without initialized map/game state.
        // No definition-name match may bake a static fence into an animated doorway.
        Type door=Assembly.Load("Assembly-CSharp").GetType("RimWorld.Building_Door",true);
        object gate=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(door);
        object doorMesh=Activator.CreateInstance(builder,true);
        Check(!(bool)objects.GetMethod("TryAdd",Flags).Invoke(null,new[]{doorMesh,gate}),"actual doors bypass generic family meshes before reading their definition");
        Check(Vectors(doorMesh,"Vertices").Count==0,"door family dispatch leaves no permanent crossbars");
        MethodInfo signature=objects.GetMethod("ItemStackSignature",Flags);
        foreach(string resource in new[]{"Log","Metal","Stone"})
        {
            int previousVertices=-1,previousSignature=-1;
            for(int count=1;count<=100;count++)
            {
                object mesh=Activator.CreateInstance(builder,true);
                build.Invoke(null,new[]{mesh,Enum.Parse(kind,resource),(object)1f,1f,1f,Tint(.5f,.5f,.5f),count});
                int vertices=Vectors(mesh,"Vertices").Count,stamp=(int)signature.Invoke(null,new object[]{count});
                if(count>1)Check(vertices==previousVertices||stamp!=previousSignature,"resource geometry changes invalidate cached mesh: "+resource+" count "+count);
                previousVertices=vertices;previousSignature=stamp;
            }
        }
        for(int count=2;count<=100;count++)
            Check(Math.Min(4,count/20)==Math.Min(4,(count-1)/20)||(int)signature.Invoke(null,new object[]{count})!=(int)signature.Invoke(null,new object[]{count-1}),"generic pile height changes invalidate cached mesh");
    }
    static object Outfit(params string[] garments)
    {
        object state=NewAppearance();Type pieceType=models.GetNestedType("ApparelPiece",Flags),kind=models.GetNestedType("ApparelKind",Flags);
        Array pieces=Array.CreateInstance(pieceType,garments.Length);
        for(int i=0;i<garments.Length;i++)
        {
            object garment=Enum.Parse(kind,garments[i]),piece=Activator.CreateInstance(pieceType);
            Set(piece,"Kind",garment);Set(piece,"Color",i%2==0?Tint(.24f,.42f,.58f):Tint(.23f,.29f,.25f));Set(piece,"Layer",i*10);
            Set(piece,"Coverage",models.GetMethod("Coverage",Flags).Invoke(null,new[]{(object)"",garment}));pieces.SetValue(piece,i);
        }
        Set(state,"Apparel",pieces);return state;
    }
    static void TestOutfitsAndPoses(Assembly assembly,Assembly unity)
    {
        foreach(var outfit in new[]{("Civilian layers",new[]{"Shirt","Pants","Coat","Boots"}),("Plate and helmet",new[]{"Shirt","Pants","Plate","Helmet","Gloves","Boots"}),("Robe and hat",new[]{"Robe","Hat"})})
        {object mesh=Build(Outfit(outfit.Item2));ValidateMesh(mesh,"combined outfit "+outfit.Item1);AddCatalog(outfit.Item1,mesh,action:true);}
        Type animation=assembly.GetType("RimFront.NativePawnAnimation",true),snapshot=animation.GetNestedType("Snapshot",Flags);
        Type quaternion=unity.GetType("UnityEngine.Quaternion",true);
        MethodInfo euler=quaternion.GetMethod("Euler",new[]{typeof(float),typeof(float),typeof(float)}),evaluate=animation.GetMethod("Evaluate",Flags);
        foreach(string action in new[]{"Walking","Aiming","Melee","Reloading","Hurt","Working"})
        {
            object state=Activator.CreateInstance(snapshot);
            Set(state,"IdlePhase",1.1f);
            if(action=="Walking"){Set(state,"Moving",true);Set(state,"Speed",4.5f);Set(state,"Phase",1.57f);}
            if(action=="Aiming"){Set(state,"Aiming",true);Set(state,"AimPitch",-15f);}
            if(action=="Melee"){Set(state,"Melee",1f);Set(state,"MeleeProgress",.5f);}
            if(action=="Reloading")
            {
                Set(state,"Reloading",true);object reload=Get(state,"Reload");Set(reload,"Active",true);Set(reload,"HasTiming",true);Set(reload,"Progress",.5f);Set(state,"Reload",reload);
            }
            if(action=="Hurt")Set(state,"Hurt",1f);
            if(action=="Working")Set(state,"Working",true);
            object dressed=Outfit("Shirt","Pants","Vest","Boots"),mesh=Activator.CreateInstance(builder,true);
            foreach(object bodyPart in Enum.GetValues(part))
            {
                if(bodyPart.ToString()=="Count")continue;
                object pose=evaluate.Invoke(null,new[]{Enum.Parse(form,"Human"),state,bodyPart});
                float[] offset=Coordinates(Get(pose,"Offset")),rotation=Coordinates(Get(pose,"Euler")),pivot=Coordinates(models.GetMethod("RestPivot",Flags).Invoke(null,new[]{dressed,bodyPart}));
                Set(mesh,"Origin",Activator.CreateInstance(vector,pivot[0]+offset[0],pivot[1]+offset[1],pivot[2]+offset[2]));
                Set(mesh,"Rotation",euler.Invoke(null,new object[]{rotation[0],rotation[1],rotation[2]}));
                models.GetMethod("BuildPart",Flags).Invoke(null,new[]{mesh,dressed,bodyPart});
            }
            ValidateMesh(mesh,"posed body "+action);AddCatalog(action+" / body rig",mesh,action:true);
        }
        Type pawns=assembly.GetType("RimFront.NativePawns",true);MethodInfo lying=pawns.GetMethod("LyingRootHeight",Flags);
        foreach(float width in new[]{.33f,.55f,.71f})foreach(float scale in new[]{.45f,1f,2.5f})foreach(bool inBed in new[]{false,true})
        {
            float root=(float)lying.Invoke(null,new object[]{width,scale,inBed});
            Check(Math.Abs(root-width*.5f*scale-(inBed?.75f:.04f))<.00001,"lying body bottom clears fixed bed/floor height across scales");
        }
        MethodInfo bed=pawns.GetMethod("BedRoot",Flags);
        foreach(float yaw in new[]{0f,90f,180f,270f})foreach(int slot in new[]{0,1})foreach(float scale in new[]{.45f,1f,2.5f})
        {
            float radians=yaw*(float)Math.PI/180,fx=(float)Math.Sin(radians),fz=(float)Math.Cos(radians);
            float cx=128.5f+slot*fz,cz=94.5f-slot*fx,width=.71f,center=.85f;
            object head=V(cx-fx*.5f,0,cz-fz*.5f),foot=V(cx+fx*.5f,0,cz+fz*.5f);
            object root=bed.Invoke(null,new[]{head,foot,(object)yaw,width,scale,center});
            MethodInfo point=root.GetType().GetMethod("MultiplyPoint3x4",new[]{vector});
            float[] middle=Coordinates(point.Invoke(root,new[]{V(0,center,0)}));
            Check(Math.Abs(middle[0]-cx)<.00002&&Math.Abs(middle[2]-cz)<.00002,"bed root centers on the actual asymmetric sleeping slot, yaw "+yaw+" slot "+slot);
            float[] crown=Coordinates(point.Invoke(root,new[]{V(0,center+.3f,0)}));
            Check((crown[0]-middle[0])*(-fx)+(crown[2]-middle[2])*(-fz)>.29f*scale,"bed head points to native head slot, yaw "+yaw);
            float[] sideA=Coordinates(point.Invoke(root,new[]{V(-width*.5f,center,0)})),sideB=Coordinates(point.Invoke(root,new[]{V(width*.5f,center,0)}));
            Check(Math.Abs(Math.Min(sideA[1],sideB[1])-.75f)<.00002,"bed model lower edge clears mattress at every rotation and scale");
        }
    }
    static object V(float x,float y,float z)=>Activator.CreateInstance(vector,x,y,z);
    static void TestEquipmentAttachments(Assembly assembly,Assembly unity)
    {
        Type weapons=assembly.GetType("RimFront.NativeWeapons",true),kind=weapons.GetNestedType("WeaponKind",Flags),carry=weapons.GetNestedType("CarryPlacement",Flags);
        Type animation=assembly.GetType("RimFront.NativePawnAnimation",true),snapshot=animation.GetNestedType("Snapshot",Flags),quaternion=unity.GetType("UnityEngine.Quaternion",true);
        MethodInfo attach=weapons.GetMethod("AttachmentPose",Flags),socket=weapons.GetMethod("HandSocket",Flags),evaluate=animation.GetMethod("Evaluate",Flags);
        MethodInfo euler=quaternion.GetMethod("Euler",new[]{vector}),rotate=quaternion.GetMethod("op_Multiply",new[]{quaternion,vector});
        object zero=V(0,0,0),identity=quaternion.GetProperty("identity",Flags).GetValue(null);
        foreach(string poseName in new[]{"Idle","AimUp","AimDown","Melee","Reload"})foreach(bool articulated in new[]{false,true})foreach(bool left in new[]{false,true})
        {
            object a=NewAppearance(),state=Activator.CreateInstance(snapshot),arm=Enum.Parse(part,left?"LeftArm":"RightArm");
            if(left)Set(a,"MissingParts",1<<(int)Enum.Parse(part,"RightArm"));
            if(poseName.StartsWith("Aim")){Set(state,"Aiming",true);Set(state,"AimPitch",poseName=="AimUp"?-35f:35f);}
            if(poseName=="Melee"){Set(state,"Melee",1f);Set(state,"MeleeProgress",.5f);}
            if(poseName=="Reload"){Set(state,"Reloading",true);object r=Get(state,"Reload");Set(r,"HasTiming",true);Set(r,"Progress",.5f);Set(state,"Reload",r);}
            object mesh=Activator.CreateInstance(builder,true);models.GetMethod("BuildPart",Flags).Invoke(null,new[]{mesh,a,arm});
            // The final naked-arm primitive is its actual palm; derive its center from vertices.
            var hand=Vectors(mesh,"Vertices").TakeLast(24).ToArray();
            object palm=V(hand.Average(v=>v[0]),hand.Average(v=>v[1]),hand.Average(v=>v[2]));
            float[] pivot=Coordinates(models.GetMethod("RestPivot",Flags).Invoke(null,new[]{a,arm}));
            object pose=evaluate.Invoke(null,new[]{Enum.Parse(form,"Human"),state,arm});
            float[] offset=articulated?Coordinates(Get(pose,"Offset")):new float[3];
            object armRotation=articulated?euler.Invoke(null,new[]{Get(pose,"Euler")}):identity;
            float[] moved=Coordinates(rotate.Invoke(null,new[]{armRotation,palm}));
            float[] expected=Enumerable.Range(0,3).Select(i=>pivot[i]+offset[i]+moved[i]).ToArray();
            object[] socketArgs={a,state,articulated,left,zero,identity};
            Check((bool)socket.Invoke(null,socketArgs),"existing palm has an equipment socket");
            Check(Close(Coordinates(socketArgs[4]),expected),"socket follows actual palm geometry: "+poseName+" articulated "+articulated+" left "+left);
            foreach(object weapon in Enum.GetValues(kind))
            {
                object[] args={a,state,articulated,weapon,Enum.Parse(carry,"Held"),zero,identity};
                Check((bool)attach.Invoke(null,args),"held equipment attaches with available hand");
                float[] grip=Coordinates(weapons.GetMethod("GripLocal",Flags).Invoke(null,new[]{weapon}));
                float[] rotated=Coordinates(rotate.Invoke(null,new[]{args[6],V(grip[0]*.78f,grip[1]*.78f,grip[2]*.78f)})),origin=Coordinates(args[5]);
                Check(Close(Enumerable.Range(0,3).Select(i=>origin[i]+rotated[i]).ToArray(),expected),"modeled grip stays at animated or static palm: "+weapon+" "+poseName);
            }
        }
        MethodInfo handed=weapons.GetMethod("IsTwoHanded",Flags),placement=weapons.GetMethod("Placement",Flags);
        foreach(var sample in new[]{("Pistol","Gun_Pistol",false,false,false),("Blade","MeleeWeapon_Longsword",false,false,false),("Blade","Mod_Greatsword",false,false,true),("Rifle","Gun_Rifle",false,false,true),("Rifle","Mod_Carbine",true,true,false),("Pistol","UntaggedCEGun",true,false,true)})
        {
            bool two=(bool)handed.Invoke(null,new object[]{Enum.Parse(kind,sample.Item1),sample.Item2,sample.Item3,sample.Item4});
            Check(two==sample.Item5,"handedness metadata and family fallback: "+sample.Item2);
            Check(placement.Invoke(null,new object[]{false,two}).ToString()==(two?"Back":"Hip"),"undrafted weapon holster follows handedness");
            Check(placement.Invoke(null,new object[]{true,two}).ToString()=="Held","drafted weapons are held");
        }
        object noHands=NewAppearance();Set(noHands,"MissingParts",(1<<(int)Enum.Parse(part,"LeftArm"))|(1<<(int)Enum.Parse(part,"RightArm")));
        foreach(string position in new[]{"Held","Back","Hip"})
        {object[] args={noHands,Activator.CreateInstance(snapshot),false,Enum.Parse(kind,"Rifle"),Enum.Parse(carry,position),zero,identity};Check((bool)attach.Invoke(null,args)==(position!="Held"),"missing hands prevent holding but retain owned stowed equipment");}
        MethodInfo select=assembly.GetType("RimFront.CombatAdapter",true).GetMethod("SelectNearestUsable",Flags);
        var visited=new List<int>();Func<int,bool> accepts=index=>{visited.Add(index);return index==2;};
        int chosen=(int)select.Invoke(null,new object[]{new[]{8d,double.NaN,3d,1d,double.PositiveInfinity,-1d},accepts});
        Check(chosen==2&&visited.SequenceEqual(new[]{3,2}),"native verb checks reject nearest then stop at first usable without visiting invalid distances");
        visited.Clear();Check((int)select.Invoke(null,new object[]{Array.Empty<double>(),accepts})==-1&&visited.Count==0,"no geometric candidates means no stateful native verb lookup");
        visited.Clear();Func<int,bool> rejects=index=>{visited.Add(index);return false;};
        Check((int)select.Invoke(null,new object[]{new[]{4d,1d,2d},rejects})==-1&&visited.SequenceEqual(new[]{1,2,0}),"unusable targets exhaust nearest-first candidates without an attack");
        Type equipment=assembly.GetType("RimFront.NativePawnEquipment",true),handType=weapons.GetNestedType("EquipmentHand",Flags);
        MethodInfo descriptor=equipment.GetMethod("PhysicalDescriptor",Flags),shieldPose=equipment.GetMethod("ShieldPose",Flags),buildShield=equipment.GetMethod("BuildShield",Flags);
        foreach(var sample in new[]{("CombatExtended.Apparel_Shield","","ModShield",false,true),("","CombatExtended.ShieldDefExtension","Custom",false,true),("","","MedievalBuckler",true,true),("","","ShieldBelt",false,false),("","","ShieldBelt",true,false),("","","EnergyShield",true,false),("","","ShieldPack",true,false)})
            Check((bool)descriptor.Invoke(null,new object[]{sample.Item1,sample.Item2,sample.Item3,sample.Item4})==sample.Item5,"only physical owned shields get board geometry: "+sample.Item3);
        TestHandAllocation(weapons,equipment,handType,appearance,snapshot);
        foreach(bool tall in new[]{false,true})
        {object mesh=Activator.CreateInstance(builder,true);buildShield.Invoke(null,new[]{mesh,(object)tall,Tint(.2f,.45f,.6f)});ValidateMesh(mesh,"physical shield "+tall);}
        foreach(var sample in new[]{("Undrafted sword and shield","Blade",false,false),("Undrafted rifle and shield","Rifle",false,true),("Drafted pistol and shield","Pistol",true,false)})
        {
            object a=Outfit("Shirt","Pants","Vest","Boots"),state=Activator.CreateInstance(snapshot),combined=Activator.CreateInstance(builder,true);
            Set(state,"Aiming",sample.Item3);
            foreach(object bodyPart in Enum.GetValues(part))
            {
                if(bodyPart.ToString()=="Count")continue;
                object piece=Activator.CreateInstance(builder,true);models.GetMethod("BuildPart",Flags).Invoke(null,new[]{piece,a,bodyPart});
                object pose=evaluate.Invoke(null,new[]{Enum.Parse(form,"Human"),state,bodyPart});
                float[] pivot=Coordinates(models.GetMethod("RestPivot",Flags).Invoke(null,new[]{a,bodyPart})),offset=Coordinates(Get(pose,"Offset"));
                AppendTransformed(combined,piece,V(pivot[0]+offset[0],pivot[1]+offset[1],pivot[2]+offset[2]),euler.Invoke(null,new[]{Get(pose,"Euler")}),1,rotate);
            }
            object weaponKind=Enum.Parse(kind,sample.Item2),weaponMesh=Activator.CreateInstance(builder,true);
            weapons.GetMethod("BuildGeometry",Flags).Invoke(null,new[]{weaponMesh,weaponKind});
            object[] weaponArgs={a,state,true,weaponKind,placement.Invoke(null,new object[]{sample.Item3,sample.Item4}),zero,identity};attach.Invoke(null,weaponArgs);
            AppendTransformed(combined,weaponMesh,weaponArgs[5],weaponArgs[6],.78f,rotate);
            object shield=Activator.CreateInstance(builder,true);buildShield.Invoke(null,new[]{shield,(object)false,Tint(.2f,.45f,.6f)});
            object[] shieldArgs={a,state,true,Enum.Parse(handType,sample.Item3?"Left":"None"),zero,identity};shieldPose.Invoke(null,shieldArgs);
            if(!sample.Item3)Check(Coordinates(shieldArgs[4])[2]<-.4f,"undrafted shield sits behind torso and weapon mount");
            AppendTransformed(combined,shield,shieldArgs[4],shieldArgs[5],1,rotate);
            ValidateMesh(combined,sample.Item1);AddCatalog(sample.Item1,combined,action:true);
        }
    }
    static bool Close(float[] a,float[] b)=>Enumerable.Range(0,3).All(i=>Math.Abs(a[i]-b[i])<.00002f);
    static void TestShieldEffects(Assembly assembly)
    {
        Type effects=assembly.GetType("RimFront.NativeShieldEffects",true);
        MethodInfo gate=effects.GetMethod("ShouldRecord",Flags),opacity=effects.GetMethod("ImpactOpacity",Flags),strength=effects.GetMethod("ImpactStrength",Flags);
        foreach(bool absorbed in new[]{false,true})foreach(bool native in new[]{false,true})foreach(bool enabled in new[]{false,true})foreach(bool colony in new[]{false,true})
            Check((bool)gate.Invoke(null,new object[]{absorbed,native,enabled,colony})==(absorbed&&enabled&&(native||colony)),"shield impact requires an actual block and enabled current-view effect");
        float last=1;
        for(int age=0;age<=18;age++)
        {float a=(float)opacity.Invoke(null,new object[]{age});Check(float.IsFinite(a)&&a>=0&&a<=last,"shield ghost fades monotonically on game ticks");last=a;}
        Check(last==0&&(float)opacity.Invoke(null,new object[]{-1})==0,"expired and future shield hits are invisible");
        foreach(float damage in new[]{-100f,0,1,1000,float.NaN,float.PositiveInfinity})
        {float value=(float)strength.Invoke(null,new object[]{damage});Check(float.IsFinite(value)&&value>=0&&value<=1,"damage cannot make shield effect unbounded");}
        MethodInfo direction=effects.GetMethod("ImpactDirection",Flags),ghost=effects.GetMethod("GhostOffset",Flags);
        float[] verticalBlue=Coordinates(ghost.Invoke(null,new object[]{V(0,1,0),9,false,1f})),verticalRed=Coordinates(ghost.Invoke(null,new object[]{V(0,1,0),9,true,1f}));
        Check(verticalBlue.All(float.IsFinite)&&verticalBlue.Sum(v=>v*v)>.0001f&&Close(verticalBlue,verticalRed.Select(v=>-v).ToArray()),"vertical-only hit direction still produces finite opposing shield echoes");
        foreach(object source in new[]{V(5,20,2),V(0,0,0),V(float.NaN,0,0)})
        {
            float[] d=Coordinates(direction.Invoke(null,new[]{V(0,0,0),source,(object)float.NaN}));
            Check(d.All(float.IsFinite)&&Math.Abs(d[1])<.00001&&Math.Abs(d.Sum(x=>x*x)-1)<.00002,"impact fallback direction stays finite, horizontal and normalized");
            foreach(int age in new[]{0,9,18})
            {
                float[] blue=Coordinates(ghost.Invoke(null,new[]{V(d[0],d[1],d[2]),(object)age,false,1f})),red=Coordinates(ghost.Invoke(null,new[]{V(d[0],d[1],d[2]),(object)age,true,1f}));
                Check(Close(blue,red.Select(v=>-v).ToArray())&&blue.All(float.IsFinite)&&blue.Sum(v=>v*v)<=.04f,"red and blue ghosts split oppositely within bounded distance");
            }
        }
        foreach(string method in new[]{"BuildSphereData","BuildRingData"})
        {
            object[] args={null,null};effects.GetMethod(method,Flags).Invoke(null,args);
            float[][] vertices=((IEnumerable)args[0]).Cast<object>().Select(Coordinates).ToArray();int[] indices=(int[])args[1];
            Check(vertices.Length<1000&&indices.Length<5000&&indices.Length%3==0,"shield effect geometry stays small: "+method);
            foreach(float[] p in vertices)Check(p.All(float.IsFinite)&&p.Sum(v=>v*v)<=1.00001f,"shield effect has finite unit bounds");
            for(int i=0;i<indices.Length;i+=3)
            {
                Check(indices[i]>=0&&indices[i]<vertices.Length&&indices[i+1]>=0&&indices[i+1]<vertices.Length&&indices[i+2]>=0&&indices[i+2]<vertices.Length,"shield triangle indices valid");
                float[] a=vertices[indices[i]],b=vertices[indices[i+1]],c=vertices[indices[i+2]];
                float ux=b[0]-a[0],uy=b[1]-a[1],uz=b[2]-a[2],vx=c[0]-a[0],vy=c[1]-a[1],vz=c[2]-a[2];
                Check(Math.Abs(uy*vz-uz*vy)+Math.Abs(uz*vx-ux*vz)+Math.Abs(ux*vy-uy*vx)>.000001f,"shield triangles have nonzero area");
            }
        }
    }
    static void AppendTransformed(object destination,object source,object translation,object rotation,float scale,MethodInfo rotate)
    {
        IList vertices=(IList)Get(destination,"Vertices"),colors=(IList)Get(destination,"Colors"),indices=(IList)Get(destination,"Triangles");
        int start=vertices.Count;float[] origin=Coordinates(translation);
        foreach(float[] p in Vectors(source,"Vertices"))
        {float[] moved=Coordinates(rotate.Invoke(null,new[]{rotation,V(p[0]*scale,p[1]*scale,p[2]*scale)}));vertices.Add(V(origin[0]+moved[0],origin[1]+moved[1],origin[2]+moved[2]));}
        foreach(object c in (IEnumerable)Get(source,"Colors"))colors.Add(c);
        foreach(int i in (IEnumerable)Get(source,"Triangles"))indices.Add(start+i);
    }
    static void AddCatalog(string name,object mesh,bool worldObject=false,bool action=false,bool weapon=false)
    {
        var colors=new List<float[]>();
        foreach(object value in (IEnumerable)Get(mesh,"Colors"))colors.Add(new[]{(float)Get(value,"r"),(float)Get(value,"g"),(float)Get(value,"b")});
        (weapon?weaponCatalog:action?actionCatalog:worldObject?objectCatalog:catalog).Add(new{Name=name,Vertices=Vectors(mesh,"Vertices"),Triangles=(List<int>)Get(mesh,"Triangles"),Colors=colors});
    }
    static List<float[]> Vectors(object owner,string field)
    {
        var result=new List<float[]>();foreach(object value in (IEnumerable)Get(owner,field))result.Add(Coordinates(value));return result;
    }
    static float[] Coordinates(object value)=>new[]{(float)Get(value,"x"),(float)Get(value,"y"),(float)Get(value,"z")};
    static object Tint(float r,float g,float b)=>Activator.CreateInstance(color,r,g,b,1f);
    static object Get(object owner,string name)=>owner.GetType().GetField(name,Flags).GetValue(owner);
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Flags).SetValue(owner,value);
    static void Check(bool valid,string reason){checks++;if(!valid)throw new Exception(reason);}
}

