using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Form = RimFront.NativePawnRig.Form;
using Part = RimFront.NativePawnRig.Part;
using BodyShape = RimFront.NativePawnRig.BodyShape;

namespace RimFront
{
    // Original low-poly models. Appearance snapshots contain values only: cached meshes never
    // retain a Pawn, apparel Thing, hediff, or other mutable game object.
    internal static class NativePawnModels
    {
        internal enum ApparelKind { Shirt, Pants, Coat, Robe, Vest, Plate, Helmet, Hat, Hood, Mask, Backpack, Boots, Gloves, Belt }
        internal struct ApparelPiece
        {
            internal ApparelKind Kind;
            internal Color Color;
            internal int Coverage, Layer;
        }
        internal sealed class Appearance
        {
            internal Form Form;
            internal BodyShape Shape;
            internal Color Skin = new Color(.65f,.46f,.32f), Hair = new Color(.16f,.12f,.09f), SleeveColor = new Color(.65f,.46f,.32f);
            internal string RaceName = "", HairName = "";
            internal ApparelPiece[] Apparel = new ApparelPiece[0];
            internal int MissingParts, ProstheticParts, StableHash;
        }
        internal const int MaximumApparel = 24;
        static readonly Color Metal = new Color(.42f,.48f,.49f), DarkMetal = new Color(.18f,.22f,.24f), Eye = new Color(.055f,.065f,.06f);
        static readonly Dictionary<ThingDef,Form> forms=new Dictionary<ThingDef,Form>();
        static readonly FieldInfo skinColorBase=typeof(Pawn_StoryTracker).GetField("skinColorBase",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        static int Bit(Part part) => 1 << (int)part;
        static bool Has(string value, string fragment) => value.IndexOf(fragment, StringComparison.Ordinal) >= 0;

        internal static Appearance Fallback(Form form)
        {
            Appearance a=new Appearance{Form=form};
            if(form!=Form.Human)a.Skin=DefaultPigment(form,"");
            a.SleeveColor=a.Skin;a.StableHash=ComputeHash(a);return a;
        }
        internal static Form ClassifyPawn(Pawn pawn)
        {
            if(pawn==null||pawn.RaceProps?.Humanlike==true)return Form.Human;
            if(pawn.def!=null && forms.TryGetValue(pawn.def,out Form cached))return cached;
            Form result=ClassifyForm(pawn.def?.defName,pawn.RaceProps?.body?.defName,false,pawn.RaceProps?.IsMechanoid??false,pawn.RaceProps?.Insect??false);
            if(pawn.def!=null){if(forms.Count>=256)forms.Clear();forms[pawn.def]=result;}
            return result;
        }
        internal static float BaseHeight(Form form)=>form==Form.Human||form==Form.MechBiped?1.725f:form==Form.Serpent?.38f:.94f;
        internal static float BaseWidth(Form form)=>form==Form.Human||form==Form.MechBiped?.71f:form==Form.Insect||form==Form.MechCrawler||form==Form.MechCentipede?.64f:form==Form.Serpent?.33f:.55f;

        internal static Appearance Capture(Pawn pawn)
        {
            Appearance a = new Appearance();
            if (pawn == null) { a.StableHash=ComputeHash(a); return a; }
            string race=pawn.def?.defName ?? "";
            a.RaceName=race.ToLowerInvariant();
            a.Form=ClassifyPawn(pawn);
            if (a.Form==Form.Human && pawn.story!=null)
            {
                a.Shape=ClassifyBody(pawn.story.bodyType?.defName);
                // SkinColorBase lazily generates a missing gene in the real game getter.
                // A renderer must not initialize gameplay state on an incomplete modded pawn.
                if(pawn.story.skinColorOverride.HasValue || skinColorBase?.GetValue(pawn.story) is Color) a.Skin=pawn.story.SkinColor;
                a.Hair=pawn.story.HairColor;
                a.HairName=(pawn.story.hairDef?.defName ?? "").ToLowerInvariant();
                if (pawn.DevelopmentalStage!=DevelopmentalStage.Adult) a.Shape=BodyShape.Child;
            }
            else
            {
                a.Skin=DefaultPigment(a.Form,a.RaceName);
                // Animal graphic colors are real tint data; white is the neutral texture tint.
                var stage=(pawn.RaceProps?.Humanlike??false)?null:pawn.ageTracker?.CurKindLifeStage;
                Color tint=stage?.bodyGraphicData?.color ?? Color.white;
                a.Skin*=tint;
            }
            a.SleeveColor=a.Skin;
            List<ApparelPiece> apparel=new List<ApparelPiece>();
            var worn=pawn.apparel?.WornApparel;
            if(worn!=null) for(int i=0;i<worn.Count && apparel.Count<MaximumApparel;i++)
            {
                var thing=worn[i]; var props=thing?.def?.apparel;
                if(props==null || NativePawnEquipment.IsPhysicalShield(thing))continue;
                string groups="",layers=""; int order=0;
                foreach(var group in props.bodyPartGroups) groups+=" "+group.defName.ToLowerInvariant();
                foreach(var layer in props.layers) { layers+=" "+layer.defName.ToLowerInvariant(); order=Math.Max(order,layer.drawOrder); }
                ApparelKind kind=ClassifyApparel(thing.def.defName,groups,layers);
                apparel.Add(new ApparelPiece{Kind=kind,Color=thing.DrawColor,Coverage=Coverage(groups,kind),Layer=order});
            }
            apparel.Sort((x,y)=>{int result=x.Layer.CompareTo(y.Layer);return result!=0?result:((int)x.Kind).CompareTo((int)y.Kind);});
            a.Apparel=apparel.ToArray();
            foreach(var piece in a.Apparel)
                if((piece.Coverage&(Bit(Part.LeftArm)|Bit(Part.RightArm)))!=0 && piece.Kind!=ApparelKind.Gloves) a.SleeveColor=piece.Color;
            var hediffs=pawn.health?.hediffSet?.hediffs;
            if(hediffs!=null) foreach(var h in hediffs)
            {
                if(h.Part==null || (!(h is Hediff_MissingPart) && !(h is Hediff_AddedPart)))continue;
                Part? part=MapLimb(h.Part);
                if(!part.HasValue)continue;
                if(h is Hediff_AddedPart) a.ProstheticParts|=Bit(part.Value);
                else a.MissingParts|=Bit(part.Value);
            }
            a.MissingParts&=~a.ProstheticParts;
            a.StableHash=ComputeHash(a);
            return a;
        }
        // Match structural roots only: losing a finger does not remove an entire arm.
        static Part? MapLimb(BodyPartRecord record)
        {
            string name=(record.def?.defName ?? "").ToLowerInvariant();
            if(Has(name,"head"))return Part.Head;
            if(Has(name,"tail"))return Part.Tail;
            bool arm=Has(name,"arm")||Has(name,"shoulder")||Has(name,"wing");
            bool leg=Has(name,"leg");
            if(!arm&&!leg)return null;
            string side=(record.untranslatedCustomLabel ?? record.customLabel ?? "").ToLowerInvariant();
            bool left=Has(side,"left"),right=Has(side,"right");
            if(!left&&!right)return null; // Ambiguous mod anatomy is kept intact.
            if(leg&&Has(side,"front"))arm=true;
            if(leg&&Has(side,"middle"))return null; // Seven-part rigs cannot identify every insect limb.
            return arm ? (left?Part.LeftArm:Part.RightArm) : (left?Part.LeftLeg:Part.RightLeg);
        }
        internal static BodyShape ClassifyBody(string name)
        {
            name=(name??"").ToLowerInvariant();
            if(Has(name,"child")||Has(name,"baby"))return BodyShape.Child;
            if(Has(name,"female"))return BodyShape.Female;
            if(Has(name,"thin"))return BodyShape.Thin;
            if(Has(name,"fat"))return BodyShape.Fat;
            if(Has(name,"hulk"))return BodyShape.Hulk;
            return BodyShape.Average;
        }
        internal static Form ClassifyForm(string raceName,string bodyName,bool human,bool mech,bool insect)
        {
            string n=((raceName??"")+" "+(bodyName??"")).ToLowerInvariant();
            if(human)return Form.Human;
            if(mech)
            {
                if(Has(n,"centipede")||Has(n,"diabolus")||Has(n,"apocriton"))return Form.MechCentipede;
                if(Has(n,"crawler")||Has(n,"scyther")||Has(n,"tesseron")||Has(n,"lancer")||Has(n,"militor"))return Has(n,"crawler")?Form.MechCrawler:Form.MechBiped;
                return Has(n,"quadruped")||Has(n,"spider")?Form.MechCrawler:Form.MechBiped;
            }
            if(insect||Has(n,"insect")||Has(n,"spider")||Has(n,"scarab")||Has(n,"spelopede"))return Form.Insect;
            if(Has(n,"snake")||Has(n,"serpent")||Has(n,"cobra")||Has(n,"worm"))return Form.Serpent;
            if(Has(n,"bird")||Has(n,"chicken")||Has(n,"duck")||Has(n,"turkey")||Has(n,"ostrich")||Has(n,"emu")||Has(n,"cassowary"))return Form.Bird;
            if(Has(n,"entity")||Has(n,"fleshbeast")||Has(n,"sightstealer")||Has(n,"devourer")||Has(n,"revenant"))return Form.Entity;
            return Form.Quadruped;
        }
        internal static ApparelKind ClassifyApparel(string name,string groups,string layers)
        {
            string n=(name??"").ToLowerInvariant(),g=(groups??"").ToLowerInvariant(),l=(layers??"").ToLowerInvariant();
            if(Has(n,"backpack")||Has(n,"jump")||Has(n,"pack"))return ApparelKind.Backpack;
            if(Has(n,"hood"))return ApparelKind.Hood;
            if(Has(n,"mask")||Has(n,"veil")||Has(n,"goggle")||Has(g,"mouth")||Has(g,"eyes"))return ApparelKind.Mask;
            if(Has(n,"helmet")||Has(n,"cataphracthelmet"))return ApparelKind.Helmet;
            if(Has(g,"head")||Has(l,"overhead")||Has(n,"hat")||n.EndsWith("cap",StringComparison.Ordinal)||Has(n,"_cap"))return ApparelKind.Hat;
            if(Has(n,"boot")||Has(g,"feet")&&!Has(g,"legs"))return ApparelKind.Boots;
            if(Has(n,"glove")||Has(g,"hands")&&!Has(g,"arms"))return ApparelKind.Gloves;
            if(Has(g,"waist")&&!Has(g,"torso")||Has(n,"belt"))return ApparelKind.Belt;
            if(Has(n,"robe")||Has(n,"dress"))return ApparelKind.Robe;
            if(Has(n,"pants")||Has(n,"trouser")||Has(g,"legs")&&!Has(g,"torso"))return ApparelKind.Pants;
            if(Has(n,"vest"))return ApparelKind.Vest;
            if(Has(n,"plate")||Has(n,"armor")||Has(n,"marine")||Has(n,"cataphract"))return ApparelKind.Plate;
            if(Has(n,"parka")||Has(n,"duster")||Has(n,"coat")||Has(n,"jacket")||Has(n,"cape")||Has(n,"cloak")||Has(l,"shell"))return ApparelKind.Coat;
            if(Has(n,"flak"))return ApparelKind.Vest;
            return ApparelKind.Shirt;
        }
        static int Coverage(string groups,ApparelKind kind)
        {
            int result=0;
            if(Has(groups,"torso")||Has(groups,"neck")||Has(groups,"waist"))result|=Bit(Part.Body);
            if(Has(groups,"head")||Has(groups,"eyes")||Has(groups,"mouth"))result|=Bit(Part.Head);
            if(Has(groups,"arms")||Has(groups,"shoulders")||Has(groups,"hands"))result|=Bit(Part.LeftArm)|Bit(Part.RightArm);
            if(Has(groups,"lefthand"))result|=Bit(Part.LeftArm);
            if(Has(groups,"righthand"))result|=Bit(Part.RightArm);
            if(Has(groups,"legs")||Has(groups,"feet"))result|=Bit(Part.LeftLeg)|Bit(Part.RightLeg);
            if(result!=0)return result;
            switch(kind)
            {
                case ApparelKind.Helmet:case ApparelKind.Hat:case ApparelKind.Hood:case ApparelKind.Mask:return Bit(Part.Head);
                case ApparelKind.Pants:case ApparelKind.Boots:return Bit(Part.LeftLeg)|Bit(Part.RightLeg);
                case ApparelKind.Gloves:return Bit(Part.LeftArm)|Bit(Part.RightArm);
                case ApparelKind.Coat:case ApparelKind.Robe:return Bit(Part.Body)|Bit(Part.LeftArm)|Bit(Part.RightArm)|Bit(Part.LeftLeg)|Bit(Part.RightLeg);
                default:return Bit(Part.Body);
            }
        }
        static Color DefaultPigment(Form form,string race)
        {
            if(form==Form.MechBiped||form==Form.MechCrawler||form==Form.MechCentipede)return new Color(.49f,.55f,.53f);
            if(form==Form.Insect)return new Color(.36f,.29f,.19f);
            if(form==Form.Serpent)return new Color(.43f,.48f,.26f);
            if(form==Form.Entity)return new Color(.47f,.36f,.43f);
            if(Has(race,"muffalo")||Has(race,"polar")||Has(race,"sheep"))return new Color(.80f,.81f,.75f);
            if(Has(race,"thrumbo"))return new Color(.75f,.80f,.75f);
            if(Has(race,"panther")||Has(race,"raven"))return new Color(.18f,.20f,.21f);
            return new Color(.57f,.45f,.32f);
        }
        internal static int ComputeHash(Appearance a)
        {
            unchecked
            {
                int h=216613626; h=Mix(h,(int)a.Form);h=Mix(h,(int)a.Shape);h=Mix(h,a.MissingParts);h=Mix(h,a.ProstheticParts);
                h=HashColor(h,a.Skin);h=HashColor(h,a.Hair);
                foreach(char c in a.RaceName??"")h=Mix(h,c);
                foreach(char c in a.HairName??"")h=Mix(h,c);
                foreach(var p in a.Apparel??new ApparelPiece[0]) {h=Mix(h,(int)p.Kind);h=Mix(h,p.Coverage);h=Mix(h,p.Layer);h=HashColor(h,p.Color);}
                return h;
            }
        }
        static int Mix(int hash,int value) { unchecked{return (hash^value)*16777619;} }
        static int HashColor(int h,Color c) {h=Mix(h,Mathf.RoundToInt(c.r*255));h=Mix(h,Mathf.RoundToInt(c.g*255));return Mix(h,Mathf.RoundToInt(c.b*255));}

        internal static Vector3 RestPivot(Appearance a,Part part)
        {
            if(a.Form==Form.Serpent)
                return part==Part.Head?new Vector3(0,.20f,.33f):part==Part.Tail?new Vector3(0,.13f,-.38f):new Vector3(0,.16f,0);
            if(a.Form==Form.Bird)
            {
                switch(part)
                {
                    case Part.Body:return new Vector3(0,.30f,0);
                    case Part.Head:return new Vector3(0,.70f,.18f);
                    case Part.LeftArm:return new Vector3(-.18f,.60f,0);
                    case Part.RightArm:return new Vector3(.18f,.60f,0);
                    case Part.LeftLeg:return new Vector3(-.117f,.29f,0);
                    case Part.RightLeg:return new Vector3(.117f,.29f,0);
                    default:return new Vector3(0,.43f,-.20f);
                }
            }
            bool humanoid=a.Form==Form.Human||a.Form==Form.MechBiped;
            if(humanoid)
            {
                switch(part)
                {
                    case Part.Body:return new Vector3(0,.72f,0);
                    case Part.Head:return new Vector3(0,1.32f,0);
                    case Part.LeftArm:return new Vector3(-.275f,1.27f,0);
                    case Part.RightArm:return new Vector3(.275f,1.27f,0);
                    case Part.LeftLeg:return new Vector3(-.105f,.69f,0);
                    case Part.RightLeg:return new Vector3(.105f,.69f,0);
                    default:return new Vector3(0,.75f,-.15f);
                }
            }
            switch(part)
            {
                case Part.Body:return new Vector3(0,.48f,0);
                case Part.Head:return new Vector3(0,.67f,.3f);
                case Part.LeftArm:return new Vector3(-.18f,.48f,.25f);
                case Part.RightArm:return new Vector3(.18f,.48f,.25f);
                case Part.LeftLeg:return new Vector3(-.18f,.48f,-.25f);
                case Part.RightLeg:return new Vector3(.18f,.48f,-.25f);
                default:return new Vector3(0,.5f,-.35f);
            }
        }
        internal static void BuildStanding(NativeScene.Builder builder,Appearance appearance)
        {
            Vector3 origin=builder.Origin;
            try
            {
                for(int i=0;i<(int)Part.Count;i++)
                {
                    Part part=(Part)i;
                    builder.Origin=origin+builder.Rotation*RestPivot(appearance,part);
                    BuildPart(builder,appearance,part);
                }
            }
            finally {builder.Origin=origin;}
        }
        internal static void BuildPart(NativeScene.Builder builder,Appearance appearance,Part part)
        {
            if(part<Part.Body||part>=Part.Count||(appearance.MissingParts&Bit(part))!=0)return;
            Writer w=new Writer(builder,RestPivot(appearance,part));
            if(appearance.Form==Form.Human) Human(w,appearance,part);
            else if(appearance.Form==Form.MechBiped) MechBiped(w,appearance,part);
            else Creature(w,appearance,part);
        }
        struct Writer
        {
            readonly NativeScene.Builder builder; readonly Vector3 pivot;
            internal Writer(NativeScene.Builder builder,Vector3 pivot){this.builder=builder;this.pivot=pivot;}
            internal void Box(float x,float y,float z,float width,float height,float depth,Color color){builder.Box(new Vector3(x,y,z)-pivot,new Vector3(width,height,depth),color);}
            internal void Spike(float x,float y,float z,float width,float height,Color color){builder.Pyramid(new Vector3(x,y,z)-pivot,width,height,color);}
        }
        static float BodyWidth(BodyShape s)=>s==BodyShape.Fat?.49f:s==BodyShape.Hulk?.47f:s==BodyShape.Thin?.32f:s==BodyShape.Child?.33f:s==BodyShape.Female?.37f:.41f;
        static void Human(Writer w,Appearance a,Part part)
        {
            Color skin=(a.ProstheticParts&Bit(part))!=0?Metal:a.Skin;
            float bw=BodyWidth(a.Shape),depth=a.Shape==BodyShape.Fat?.29f:.23f;
            if(part==Part.Body)
            {
                w.Box(0,1.0f,0,bw,.61f,depth,skin);w.Box(0,.69f,0,bw*.83f,.16f,.24f,skin);
                // Modest neutral undergarment geometry is original and independent of apparel.
                w.Box(0,.66f,0,bw*.85f,.13f,.242f,new Color(.40f,.36f,.31f));
                w.Box(0,1.32f,0,.15f,.15f,.16f,skin);
            }
            else if(part==Part.Head)
            {
                float headWidth=a.Shape==BodyShape.Child?.37f:.33f;
                w.Box(0,1.49f,.005f,headWidth,.37f,.31f,skin);
                w.Box(-.073f,1.52f,.166f,.036f,.027f,.012f,Eye);w.Box(.073f,1.52f,.166f,.036f,.027f,.012f,Eye);
                w.Box(0,1.445f,.18f,.045f,.06f,.045f,skin*.88f);
                w.Box(0,1.39f,.166f,.075f,.014f,.01f,skin*.60f);
                w.Box(-headWidth*.5f-.012f,1.49f,0,.035f,.10f,.07f,skin);w.Box(headWidth*.5f+.012f,1.49f,0,.035f,.10f,.07f,skin);
                bool covered=false,hat=false;
                foreach(var item in a.Apparel){if(item.Kind==ApparelKind.Helmet||item.Kind==ApparelKind.Hood)covered=true;if(item.Kind==ApparelKind.Hat)hat=true;}
                if(!covered&&!Has(a.HairName,"bald")&&!Has(a.HairName,"shaved"))
                {
                    if(!hat)w.Box(0,1.695f,-.015f,headWidth+.025f,.06f,.33f,a.Hair);
                    if(Has(a.HairName,"long")||Has(a.HairName,"braid")||Has(a.HairName,"ponytail"))w.Box(0,1.46f,-.16f,.29f,.43f,.09f,a.Hair);
                    else if(Has(a.HairName,"mohawk")){if(!hat)w.Box(0,1.70f,0,.085f,.05f,.31f,a.Hair);}
                    else w.Box(0,1.59f,-.155f,.32f,.17f,.065f,a.Hair);
                }
            }
            else if(part==Part.LeftArm||part==Part.RightArm)
            {
                float x=part==Part.LeftArm?-.275f:.275f;
                w.Box(x,1.055f,0,.145f,.43f,.16f,skin);w.Box(x,.78f,.025f,.13f,.14f,.16f,skin);
                if((a.ProstheticParts&Bit(part))!=0)w.Box(x,.96f,.09f,.08f,.09f,.025f,DarkMetal);
            }
            else if(part==Part.LeftLeg||part==Part.RightLeg)
            {
                float x=part==Part.LeftLeg?-.105f:.105f;
                w.Box(x,.375f,0,.165f,.59f,.18f,skin);w.Box(x,.045f,.055f,.17f,.09f,.29f,skin*.85f);
                if((a.ProstheticParts&Bit(part))!=0)w.Box(x,.31f,.10f,.10f,.08f,.025f,DarkMetal);
            }
            int layer=0;
            foreach(var item in a.Apparel) Apparel(w,a,part,item,Math.Min(layer++,MaximumApparel-1)*.001f);
        }
        static void Apparel(Writer w,Appearance a,Part part,ApparelPiece item,float padding)
        {
            if((item.Coverage&Bit(part))==0 && item.Kind!=ApparelKind.Backpack && !(item.Kind==ApparelKind.Pants&&part==Part.Body))return;
            Color c=item.Color;float bw=BodyWidth(a.Shape),d=a.Shape==BodyShape.Fat?.29f:.23f;
            bool arm=part==Part.LeftArm||part==Part.RightArm,leg=part==Part.LeftLeg||part==Part.RightLeg;
            float x=part==Part.LeftArm?-.275f:part==Part.RightArm?.275f:part==Part.LeftLeg?-.105f:.105f;
            if(item.Kind==ApparelKind.Backpack){if(part==Part.Body)w.Box(0,1.02f,-.23f,.31f,.43f,.20f,c);return;}
            if(item.Kind==ApparelKind.Belt){if(part==Part.Body){w.Box(0,.78f,0,bw+.04f,.085f,d+.05f,c);w.Box(0,.78f,d*.5f+.045f,.065f,.055f,.025f,Metal);}return;}
            if(item.Kind==ApparelKind.Pants&&part==Part.Body){w.Box(0,.67f,0,bw*.87f,.16f,d+.025f,c);return;}
            if(part==Part.Head)
            {
                switch(item.Kind)
                {
                    case ApparelKind.Helmet:w.Box(0,1.68f,0,.40f,.09f,.38f,c);w.Box(-.18f,1.57f,0,.045f,.2f,.34f,c);w.Box(.18f,1.57f,0,.045f,.2f,.34f,c);w.Box(0,1.59f,-.17f,.36f,.22f,.055f,c);break;
                    case ApparelKind.Hood:w.Box(0,1.67f,-.015f,.40f,.10f,.37f,c);w.Box(-.18f,1.49f,-.025f,.05f,.32f,.32f,c);w.Box(.18f,1.49f,-.025f,.05f,.32f,.32f,c);w.Box(0,1.49f,-.18f,.36f,.34f,.05f,c);break;
                    case ApparelKind.Mask:w.Box(0,1.445f,.20f,.29f,.13f,.07f,c);w.Box(0,1.44f,.25f,.10f,.09f,.06f,c*.75f);break;
                    case ApparelKind.Hat:w.Box(0,1.675f,0,.56f,.035f,.47f,c);w.Box(0,1.705f,-.015f,.34f,.04f,.32f,c);break;
                }
                return;
            }
            if(item.Kind==ApparelKind.Boots){if(leg){w.Box(x,.17f,0,.19f,.29f,.22f,c);w.Box(x,.055f,.065f,.20f,.11f,.32f,c);}return;}
            if(item.Kind==ApparelKind.Gloves){if(arm)w.Box(x,.79f,.03f,.15f,.18f,.19f,c);return;}
            bool longCoat=item.Kind==ApparelKind.Coat||item.Kind==ApparelKind.Robe;
            bool armor=item.Kind==ApparelKind.Plate||item.Kind==ApparelKind.Vest;
            if(part==Part.Body && item.Kind!=ApparelKind.Pants)
            {
                w.Box(0,1.0f,0,bw+.026f+padding,.62f+padding,d+.03f+padding,c);
                if(armor)
                {
                    w.Box(0,1.08f,d*.5f+.035f,bw*.79f,.38f,.055f,c*.84f);w.Box(0,.83f,d*.5f+.03f,bw*.8f,.09f,.05f,c*.65f);
                    if(item.Kind==ApparelKind.Plate){w.Box(0,1.27f,.01f,bw*.82f,.08f,d+.08f,c*.72f);w.Box(0,1.05f,d*.5f+.073f,.07f,.32f,.025f,c);}
                    else{w.Box(-bw*.23f,1.0f,d*.5f+.08f,.10f,.15f,.06f,c*.70f);w.Box(bw*.23f,1.0f,d*.5f+.08f,.10f,.15f,.06f,c*.70f);}
                }
                else if(longCoat)
                {
                    if(item.Kind==ApparelKind.Coat)w.Box(0,1.18f,d*.5f+.025f,.035f,.24f,.025f,c*.63f);
                    else w.Box(-.065f,1.05f,d*.5f+.025f,.085f,.49f,.025f,c*.75f);
                    w.Box(0,.77f,d*.5f+.025f,bw,.035f,.03f,c*.65f);
                }
            }
            if(arm && item.Kind!=ApparelKind.Vest && item.Kind!=ApparelKind.Pants)
            {
                // The naked shoulder ends at 1.27. Keep the garment cap above it, and
                // separate successive worn layers vertically as well as around the arm.
                w.Box(x,1.105f+padding*.5f,0,.158f+padding*.3f,.35f+padding,.18f+padding,c);
                if(longCoat||item.Kind==ApparelKind.Plate)w.Box(x,.91f,0,.16f+padding*.3f,.22f+padding,.18f+padding,c);
            }
            if(leg && (item.Kind==ApparelKind.Pants||longCoat||item.Kind==ApparelKind.Plate))
            {
                if(longCoat)w.Box(x,item.Kind==ApparelKind.Robe?.465f:.60f,-.015f,.205f+padding,item.Kind==ApparelKind.Robe?.64f+padding:.38f+padding,.28f+padding,c);
                else w.Box(x,.38f,0,.182f+padding*.3f,.59f+padding,.20f+padding,c);
            }
        }
        static void MechBiped(Writer w,Appearance a,Part part)
        {
            Color c=a.Skin;
            if(part==Part.Body){w.Box(0,1.01f,0,.43f,.57f,.31f,c);w.Box(0,1.07f,.19f,.28f,.25f,.08f,DarkMetal);w.Box(0,.71f,0,.29f,.15f,.23f,DarkMetal);}
            else if(part==Part.Head){w.Box(0,1.48f,0,.31f,.37f,.30f,c);w.Box(0,1.51f,.16f,.22f,.055f,.025f,new Color(.78f,.26f,.15f));w.Box(0,1.70f,0,.06f,.05f,.06f,DarkMetal);}
            else if(part==Part.LeftArm||part==Part.RightArm){float x=part==Part.LeftArm?-.275f:.275f;w.Box(x,1.09f,0,.16f,.42f,.23f,c);w.Box(x,.81f,.1f,.12f,.20f,.32f,DarkMetal);}
            else if(part==Part.LeftLeg||part==Part.RightLeg){float x=part==Part.LeftLeg?-.105f:.105f;w.Box(x,.39f,0,.16f,.57f,.20f,c);w.Box(x,.055f,.075f,.20f,.11f,.36f,DarkMetal);}
        }
        static void Creature(Writer w,Appearance a,Part part)
        {
            Color c=a.Skin;
            bool left=part==Part.LeftArm||part==Part.LeftLeg,front=part==Part.LeftArm||part==Part.RightArm;
            float x=left?-.18f:.18f,z=front?.25f:-.25f;
            bool limb=front||part==Part.LeftLeg||part==Part.RightLeg;
            if(a.Form==Form.Serpent)
            {
                if(part==Part.Body){for(int i=0;i<4;i++)w.Box((i%2==0?.025f:-.025f),.16f,.22f-i*.17f,.27f,.24f,.21f,c*(1f-i*.05f));}
                if(part==Part.Head){w.Box(0,.26f,.48f,.29f,.22f,.31f,c);Eyes(w,.11f,.30f,.64f);}
                if(part==Part.Tail){w.Box(.04f,.12f,-.51f,.18f,.15f,.28f,c*.83f);w.Box(.08f,.10f,-.71f,.10f,.10f,.18f,c*.76f);}return;
            }
            if(a.Form==Form.Bird)
            {
                if(part==Part.Body){w.Box(0,.49f,0,.36f,.40f,.43f,c);w.Box(0,.64f,.18f,.18f,.26f,.19f,c);}
                if(part==Part.Head){w.Box(0,.81f,.23f,.25f,.25f,.27f,c);w.Box(0,.79f,.41f,.12f,.08f,.16f,new Color(.73f,.52f,.20f));Eyes(w,.115f,.85f,.36f);}
                if(front){w.Box(x,.51f,-.03f,.14f,.32f,.39f,c*.76f);}
                if(part==Part.LeftLeg||part==Part.RightLeg){w.Box(x*.65f,.17f,0,.055f,.28f,.06f,new Color(.56f,.39f,.18f));w.Box(x*.65f,.035f,.075f,.11f,.04f,.22f,new Color(.56f,.39f,.18f));}
                if(part==Part.Tail)w.Box(0,.43f,-.35f,.24f,.09f,.31f,c*.72f);return;
            }
            bool insect=a.Form==Form.Insect,centipede=a.Form==Form.MechCentipede,crawler=a.Form==Form.MechCrawler,entity=a.Form==Form.Entity;
            if(part==Part.Body)
            {
                w.Box(0,.58f,0,.43f,.36f,.68f,c);
                if(insect||centipede){for(int i=0;i<4;i++)w.Box(0,.66f,.24f-i*.17f,.48f,.26f,.145f,c*(1f-i*.06f));}
                if(crawler){w.Box(0,.62f,0,.50f,.37f,.62f,c);w.Box(0,.80f,0,.26f,.09f,.31f,DarkMetal);}
                if(entity){w.Spike(-.13f,.75f,-.1f,.15f,.18f,c*.65f);w.Spike(.13f,.75f,-.15f,.13f,.18f,c*.7f);}
                if(Has(a.RaceName,"muffalo")||Has(a.RaceName,"bison"))w.Box(0,.78f,-.03f,.46f,.15f,.48f,c*.90f);
            }
            if(part==Part.Head)
            {
                w.Box(0,.74f,.40f,.29f,.30f,.31f,c);w.Box(0,.65f,.60f,.23f,.17f,.20f,c*.87f);
                Eyes(w,.145f,.80f,.55f);
                if(crawler||centipede){w.Box(0,.76f,.62f,.14f,.06f,.04f,new Color(.72f,.24f,.16f));}
                else if(insect){w.Box(-.12f,.58f,.69f,.07f,.10f,.18f,c*.52f);w.Box(.12f,.58f,.69f,.07f,.10f,.18f,c*.52f);}
                else {w.Box(-.14f,.91f,.35f,.09f,.06f,.11f,c);w.Box(.14f,.91f,.35f,.09f,.06f,.11f,c);}
                if(Has(a.RaceName,"thrumbo")||Has(a.RaceName,"rhinoceros"))w.Spike(0,.81f,.51f,.10f,.13f,new Color(.79f,.77f,.64f));
            }
            if(limb)
            {
                if(insect||centipede||crawler)
                {
                    w.Box(x*1.30f,.37f,z,.15f,.12f,.12f,c*.75f);w.Box(x*1.35f,.20f,z,.06f,.30f,.09f,c*.66f);
                    if(centipede||insect&&front){w.Box(x*1.30f,.33f,z*.36f,.15f,.09f,.07f,c*.7f);w.Box(x*1.35f,.18f,z*.36f,.045f,.28f,.07f,c*.6f);}
                }
                else {w.Box(x,.27f,z,.12f,.49f,.13f,c*.87f);w.Box(x,.045f,z+.035f,.14f,.09f,.20f,c*.57f);}
            }
            if(part==Part.Tail)
            {
                if(centipede)w.Box(0,.54f,-.48f,.35f,.24f,.30f,c*.75f);
                else if(!crawler)w.Box(0,.53f,-.50f,.09f,.12f,.36f,c*.8f);
            }
        }
        static void Eyes(Writer w,float x,float y,float z){w.Box(-x,y,z,.034f,.032f,.025f,Eye);w.Box(x,y,z,.034f,.032f,.025f,Eye);}
    }
}
