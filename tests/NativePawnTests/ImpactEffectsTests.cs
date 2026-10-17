using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestImpactEffects(Assembly assembly)
    {
        Type effects=assembly.GetType("RimFront.NativeCombatEffects",true);
        Type surface=effects.GetNestedType("ImpactSurface",Flags);
        MethodInfo classify=effects.GetMethod("ClassifyImpact",Flags),count=effects.GetMethod("ParticleCount",Flags);
        object Surface(string name)=>Enum.Parse(surface,name);
        string Classify(string name,bool pawn=false,bool flesh=false,bool plant=false,bool building=false,bool water=false,bool shield=false,bool deflected=false)
            =>classify.Invoke(null,new object[]{name,pawn,flesh,plant,building,water,shield,deflected}).ToString();
        Check(Classify("WoodLog Woody",building:true)=="Wood","stuff overrides building default for wooden walls");
        Check(Classify("Steel Metallic",building:true)=="Metal","steel walls emit metal contact effects");
        Check(Classify("GraniteBlocks Stony",building:true)=="Stone","stone furniture emits mineral chips");
        Check(Classify("Soil")=="Soil"&&Classify("WaterShallow",water:true)=="Water","soil and water impacts stay distinct");
        Check(Classify("WatermillGenerator",building:true,water:true)=="Stone","water-named buildings do not generate terrain splashes");
        Check(Classify("WoodTree",plant:true)=="Plant","living plants take precedence over wood name");
        Check(Classify("Human",pawn:true,flesh:true)=="Flesh","pawn contact alone does not infer armor or blood");
        Check(Classify("Mech",pawn:true)=="Metal","mechanical pawns use solid contact effects");
        Check(Classify("Human",pawn:true,flesh:true,shield:true,deflected:true)=="Shield","confirmed shield absorption takes precedence over armor and flesh");
        Check(Classify("Human",pawn:true,flesh:true,deflected:true)=="Armor","confirmed armor deflection takes precedence over flesh");
        Check((int)count.Invoke(null,new[]{Surface("Flesh")})==0,"generic flesh contact produces no inferred blood particles");

        MethodInfo hasShieldFlag=effects.GetMethod("HasShieldImpactFlag",Flags);
        foreach(var signature in new[]{(name:nameof(ImpactWithShieldFlag),expected:true),
            (name:nameof(ImpactWithoutFlag),expected:false),(name:nameof(ImpactWithDifferentBool),expected:false),
            (name:nameof(ImpactWithWrongFlagType),expected:false)})
            Check((bool)hasShieldFlag.Invoke(null,new object[]{typeof(Program).GetMethod(signature.name,Flags)})==signature.expected,
                "only an explicit native blockedByShield bool selects the shield-aware observer");
        Type regularHook=assembly.GetType("RimFront.NativeObserveProjectileImpact",true);
        Type shieldHook=assembly.GetType("RimFront.NativeObserveShieldProjectileImpact",true);
        var regular=((IEnumerable)regularHook.GetMethod("TargetMethods",Flags).Invoke(null,null)).Cast<MethodBase>().ToHashSet();
        var blocked=((IEnumerable)shieldHook.GetMethod("TargetMethods",Flags).Invoke(null,null)).Cast<MethodBase>().ToHashSet();
        var impacts=((IEnumerable)effects.GetMethod("ImpactMethods",Flags).Invoke(null,null)).Cast<MethodBase>().ToHashSet();
        Check(blocked.Count>0,"installed game exposes explicit-shield Impact contracts");
        Check(!regular.Overlaps(blocked)&&impacts.SetEquals(regular.Concat(blocked)),
            "each native impact is observed exactly once across signature-specific hooks");
        foreach(MethodBase method in blocked)
            Check((bool)hasShieldFlag.Invoke(null,new object[]{method}),"shield hook never binds a missing or unrelated bool argument");
        Check(shieldHook.GetMethod("Prefix",Flags).GetParameters().Any(p=>p.Name=="__1"&&p.ParameterType==typeof(bool)),
            "shield-aware Harmony observer receives the native outcome argument");
        Check(!regularHook.GetMethod("Prefix",Flags).GetParameters().Any(p=>p.Name=="__1"),
            "ordinary and CE-style Impact(Thing) hooks do not require a nonexistent shield argument");
        MethodInfo begin=effects.GetMethod("BeginImpact",Flags);
        Check(begin.GetParameters().Length==3&&begin.GetParameters()[2].ParameterType==typeof(bool)
            &&Equals(begin.GetParameters()[2].DefaultValue,false),"ordinary contacts default to no confirmed shield block");
        foreach(string underlying in new[]{"Soil","Water","Stone"})
            Check(Classify(underlying,water:underlying=="Water",shield:true)=="Shield",
                "explicit interceptor absorption takes precedence over underlying terrain");

        // A terrain explosion can call armor/shield observers for several nearby
        // pawns while its own Impact(null) observation is still active.
        Type observationType=effects.GetNestedType("ImpactObservation",Flags);
        FieldInfo active=effects.GetField("currentImpact",Flags);object savedActive=active.GetValue(null);
        MethodInfo shield=effects.GetMethod("ConfirmShield",Flags);
        Type pawnType=shield.GetParameters()[0].ParameterType;
        object first=RuntimeHelpers.GetUninitializedObject(pawnType),second=RuntimeHelpers.GetUninitializedObject(pawnType);
        try
        {
            foreach(object target in new[]{null,second,first})
            {
                object observation=Activator.CreateInstance(observationType,true);Set(observation,"Hit",target);active.SetValue(null,observation);
                shield.Invoke(null,new[]{first,(object)false});
                Check(!(bool)Get(observation,"Shielded"),"unabsorbed damage never marks a shield contact");
                shield.Invoke(null,new[]{first,(object)true});
                Check((bool)Get(observation,"Shielded")==ReferenceEquals(target,first),"shield absorption is attributed only to the explicitly hit pawn, never terrain or a different explosion victim");
                Check(ReferenceEquals(Get(observation,"Hit"),target),"outcome observation never rewrites native contact identity");
            }
        }
        finally{active.SetValue(null,savedActive);}

        MethodInfo make=effects.GetMethod("MakeParticle",Flags),position=effects.GetMethod("ParticlePosition",Flags),remaining=effects.GetMethod("Remaining",Flags);
        object sample=null;
        foreach(string material in Enum.GetNames(surface))
        {
            int particles=(int)count.Invoke(null,new[]{Surface(material)});
            Check(particles>=0&&particles<=9,"per-contact particle work is bounded");
            foreach(int seed in new[]{int.MinValue,-17,0,647,int.MaxValue})
            foreach(float[] normal in new[]{new[]{0f,1f,0f},new[]{1f,0f,0f},new[]{0f,0f,-1f}})
            for(int i=0;i<particles;i++)
            {
                object[] args={Surface(material),seed,i,V(128,1,256),V(normal[0],normal[1],normal[2]),V(-normal[0],-normal[1],-normal[2]),100};
                object particle=make.Invoke(null,args),repeat=make.Invoke(null,args);sample=particle;
                float[] origin=Coordinates(Get(particle,"Position")),velocity=Coordinates(Get(particle,"Velocity"));
                Check(origin.All(float.IsFinite)&&velocity.All(float.IsFinite),"contact fragments have finite position and velocity");
                Check(Close(origin,Coordinates(Get(repeat,"Position")))&&Close(velocity,Coordinates(Get(repeat,"Velocity"))),"cosmetic dispersion is deterministic without simulation RNG");
                Check(Enumerable.Range(0,3).Sum(axis=>velocity[axis]*normal[axis])>=-.00001f,"initial debris travels out of the contact plane");
                Check(velocity.Sum(x=>x*x)<=16f,"fragment velocity remains bounded");
                int duration=(int)Get(particle,"Lifetime");
                Check(duration>0&&duration<=36,"contact lifetimes are short and bounded in game ticks");
                float[] now=Coordinates(position.Invoke(null,new[]{particle,(object)105})),paused=Coordinates(position.Invoke(null,new[]{particle,(object)105}));
                Check(Close(now,paused),"drawing multiple paused frames does not move a fragment");
                Check(now.All(float.IsFinite)&&now[1]>=.035f,"fragment trajectories stay finite above the ground");
                Check((float)remaining.Invoke(null,new object[]{100,100,duration})==1f&&(float)remaining.Invoke(null,new object[]{100,100+duration,duration})==0f,"particle survives at birth and expires at its exact tick deadline");
                string kind=Get(particle,"Kind").ToString();
                if(material=="Water")Check(kind=="Splash","water contacts never generate sparks");
                if(material=="Wood"||material=="Stone"||material=="Plant")Check(kind=="Chip"||kind=="Dust","nonmetal solids generate chips or dust rather than sparks");
            }
        }
        foreach(object invalid in new[]{V(float.NaN,0,0),V(float.PositiveInfinity,0,0),V(0,0,0)})
        {
            object particle=make.Invoke(null,new object[]{Surface("Metal"),1,0,invalid,invalid,invalid,10});
            Check(Coordinates(Get(particle,"Position")).All(float.IsFinite)&&Coordinates(Get(particle,"Velocity")).All(float.IsFinite),"invalid optional geometry inputs produce finite fallback fragments");
        }
        Check((float)remaining.Invoke(null,new object[]{100,99,30})==0f,"future observations cannot draw after clock rollback");
        Check((float)remaining.Invoke(null,new object[]{int.MinValue,int.MaxValue,30})==0f,"tick age subtraction cannot overflow into a live effect");
        Check((float)remaining.Invoke(null,new object[]{0,0,0})==0f,"zero-duration effects are inactive");
        float previous=1f;
        for(int tick=100;tick<=130;tick++)
        {
            float life=(float)remaining.Invoke(null,new object[]{100,tick,30});
            Check(life>=0&&life<=previous,"effect lifetime fades monotonically with simulation ticks");previous=life;
        }

        MethodInfo ground=effects.GetMethod("TryGroundMark",Flags);
        foreach(string material in Enum.GetNames(surface))
        {
            object[] args={V(128,.04f,256),Surface(material),null};bool accepted=(bool)ground.Invoke(null,args);
            bool expected=material!="Flesh"&&material!="Water"&&material!="Shield"&&material!="Armor";
            Check(accepted==expected,"ground contact eligibility excludes blood, splashes and shield/armor contact");
            if(accepted)Check(Close(Coordinates(args[2]),new[]{128f,.035f,256f}),"ground marks preserve actual contact X/Z");
        }
        foreach(object point in new[]{V(1,1.5f,2),V(1,-1,2),V(float.NaN,0,2)})
        {
            object[] args={point,Surface("Stone"),null};
            Check(!(bool)ground.Invoke(null,args),"wall-height and invalid impacts cannot produce unrelated floor marks");
        }
        MethodInfo box=effects.GetMethod("BoxContact",Flags);
        foreach(var test in new[]{(incoming:V(1,0,0),expected:new[]{9f,1.2f,20f},normal:new[]{-1f,0f,0f}),
            (incoming:V(0,0,-1),expected:new[]{10f,1.2f,22f},normal:new[]{0f,0f,1f}),
            (incoming:V(1,-.5f,1),expected:new[]{9f,1.2f,19f},normal:new[]{-1f,0f,0f})})
        {
            object[] args={V(10,1.2f,20),1f,2f,test.incoming,null};
            Check(Close(Coordinates(box.Invoke(null,args)),test.expected)&&Close(Coordinates(args[4]),test.normal),"vanilla full-building contact lies on the incoming near face and preserves height");
        }

        // Exercise the exact retained/transient observation policy used by projectile
        // launch and impact observers, without a spawned map or native renderer.
        MethodInfo observe=effects.GetMethod("ObserveBounded",Flags).MakeGenericMethod(typeof(object));
        var cache=new Dictionary<int,object>();for(int i=0;i<96;i++)cache.Add(i,new object());
        int created=0;Func<object> create=()=>{created++;return new object();};
        object Read(int id,int cap,bool transient)=>observe.Invoke(null,new object[]{cache,id,cap,transient,create});
        Check(Read(96,96,false)==null&&created==0&&cache.Count==96,"periodic tracking skips work when persistent capacity is full");
        object contact=Read(96,96,true);
        Check(contact!=null&&created==1&&cache.Count==96&&!cache.ContainsKey(96),"full tracking pool still observes an impact without retaining another flight");
        Check(ReferenceEquals(Read(1,96,true),cache[1])&&created==1,"existing flight observation is reused without allocation");
        Check(Read(96,97,false)!=null&&created==2&&cache.Count==97,"free persistent capacity retains the observed flight");
        Func<object> missing=()=>null;
        Check(observe.Invoke(null,new object[]{cache,100,98,true,missing})==null&&cache.Count==97,"unobservable projectile is not cached");

        // Validate the actual shared particle list eviction policy; restore it afterward.
        IList pool=(IList)effects.GetField("sparks",Flags).GetValue(null);
        object[] saved=new object[pool.Count];pool.CopyTo(saved,0);
        try
        {
            pool.Clear();MethodInfo add=effects.GetMethod("AddSpark",Flags);
            for(int i=0;i<400;i++)add.Invoke(null,new[]{sample});
            Check(pool.Count==(int)effects.GetField("MaximumSparks",Flags).GetRawConstantValue(),"sustained impacts cannot exceed the shared 192-particle limit");
        }
        finally{pool.Clear();foreach(object item in saved)pool.Add(item);}
    }

    static void ImpactWithShieldFlag(object hitThing,bool blockedByShield){}
    static void ImpactWithoutFlag(object hitThing){}
    static void ImpactWithDifferentBool(object hitThing,bool destroyed){}
    static void ImpactWithWrongFlagType(object hitThing,int blockedByShield){}
}
