using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Definition-family silhouettes. Unknown objects retain NativeScene's size-aware fallback.
    internal static partial class NativeObjectModels
    {
        internal enum Kind { None, Solar, Wind, Generator, Battery, Cooler, Heater, Research, Stove, Smelter, Machining,
            Shelf, Storage, Comms, Pod, Medical, Fence, Trap, Sandbags, Barricade, Embrasure, Monument, Mushroom, Cactus, Broadleaf,
            Log, Metal, Stone, Chunk, Component, Medicine, Meal, RawFood, Book, Clothing, Headwear }
        static readonly Color Steel = new Color(.36f,.42f,.43f), Dark = new Color(.13f,.18f,.20f), Glass = new Color(.25f,.55f,.63f);
        internal static Kind Classify(string definition, bool building, bool plant, bool tree, bool apparel, bool ingestible)
        {
            string n=(definition??"").ToLowerInvariant();
            if (plant)
            {
                if(n.Contains("mushroom")||n.Contains("fungus"))return Kind.Mushroom;
                if(n.Contains("cactus"))return Kind.Cactus;
                if(tree && !(n.Contains("pine")||n.Contains("spruce")||n.Contains("conifer")))return Kind.Broadleaf;
                return Kind.None;
            }
            if (apparel) return n.Contains("helmet")||n.Contains("hat")||n.Contains("hood")||n.Contains("mask")||n.EndsWith("cap",StringComparison.Ordinal)?Kind.Headwear:Kind.Clothing;
            if (building)
            {
                // Cover names must win over generic storage/machining/fence families.
                if(n.Contains("embrasure"))return Kind.Embrasure;
                if(n.Contains("barricade"))return Kind.Barricade;
                if(n.Contains("sandbag"))return Kind.Sandbags;
                if(n.Contains("solar"))return Kind.Solar;
                if(n.Contains("windturbine"))return Kind.Wind;
                if(n.Contains("generator")||n.Contains("geothermal")||n.Contains("reactor"))return Kind.Generator;
                if(n.Contains("battery"))return Kind.Battery;
                if(n.Contains("cooler")||n.Contains("aircondition"))return Kind.Cooler;
                if(n.Contains("heater"))return Kind.Heater;
                if(n.Contains("research"))return Kind.Research;
                if(n.Contains("stove")||n.Contains("campfire"))return Kind.Stove;
                if(n.Contains("smelter")||n.Contains("crematorium"))return Kind.Smelter;
                if(n.Contains("machin")||n.Contains("fabrication")||n.Contains("smithy")||n.Contains("tailor"))return Kind.Machining;
                if(n.Contains("shelf"))return Kind.Shelf;
                if(n.Contains("storage")||n.Contains("hopper")||n.Contains("barrel"))return Kind.Storage;
                if(n.Contains("comms")||n.Contains("console"))return Kind.Comms;
                if(n.Contains("casket")||n.Contains("biosculp")||n.Contains("pod"))return Kind.Pod;
                if(n.Contains("vitals")||n.Contains("medical")||n.Contains("hospital"))return n.Contains("bed")?Kind.None:Kind.Medical;
                if(n.Contains("fence"))return Kind.Fence;
                if(n.Contains("trap"))return Kind.Trap;
                if(n.Contains("sculpture")||n.Contains("statue")||n.Contains("altar"))return Kind.Monument;
                return Kind.None;
            }
            if(n.Contains("woodlog")||n=="wood")return Kind.Log;
            if(n.Contains("steel")||n.Contains("plasteel")||n.Contains("silver")||n.Contains("gold"))return Kind.Metal;
            if(n.Contains("chunk"))return Kind.Chunk;
            if(n.Contains("blocks"))return Kind.Stone;
            if(n.Contains("component")||n.Contains("chip"))return Kind.Component;
            if(n.Contains("medicine")||n.Contains("medkit"))return Kind.Medicine;
            if(n.Contains("meal"))return Kind.Meal;
            if(n.Contains("book")||n.Contains("tome"))return Kind.Book;
            return ingestible?Kind.RawFood:Kind.None;
        }

        internal static bool TryAdd(NativeScene.Builder b, Thing thing)
        {
            // Doors need the scene's static frame and separately animated leaves, even
            // when their definition name also matches a family such as FenceGate.
            if (thing is Building_Door) return false;
            bool building=thing is Building, plant=thing is Plant;
            if(!building&&!plant&&thing.def.category!=ThingCategory.Item)return false;
            if(thing.def.IsWeapon)return false;
            Kind kind=Classify(thing.def.defName,building,plant,thing.def.plant?.IsTree==true,thing.def.apparel!=null,thing.def.ingestible!=null);
            if(kind==Kind.None)return false;
            float growth=thing is Plant p?NativeScene.PlantGrowth(p.Growth):1f;
            Color tint=thing.DrawColor;
            if(plant && tint==Color.white)tint=new Color(.30f,.47f,.25f);
            else if(building && tint==Color.white)tint=Steel;
            float w=building?Math.Max(.5f,thing.def.size.x):1f, d=building?Math.Max(.5f,thing.def.size.z):1f;
            Build(b,kind,w,d,growth,tint,thing.stackCount);
            return true;
        }

        // One visible piece per five units, capped for dense/modded stockpiles.
        internal static int ResourceStackCount(int count) => count <= 0 ? 0 : Math.Min(18, 1 + (count - 1) / 5);
        // Include both the generic pile height and specialized resource piece count.
        // A dirty notification alone cannot rebuild a mesh with an unchanged signature.
        internal static int ItemStackSignature(int count) => (Math.Min(4, Math.Max(0, count) / 20) << 5) | ResourceStackCount(count);

        internal static void Build(NativeScene.Builder b, Kind kind, float w, float d, float growth, Color tint, int count)
        {
            w=Mathf.Clamp(w,.3f,20);d=Mathf.Clamp(d,.3f,20);growth=Mathf.Clamp(growth,.15f,1);
            Color wood=new Color(.43f,.29f,.17f), green=new Color(.27f,.42f,.24f);
            switch(kind)
            {
                case Kind.Solar:
                    Box(b,0,.33f,0,w*.74f,.66f,d*.65f,Steel);
                    Angle(b,new Vector3(0,.72f,0),new Vector3(w*.93f,.09f,d*.90f),new Vector3(12,0,0),Dark);
                    for(int i=0;i<4;i++)Angle(b,new Vector3((i-1.5f)*w*.22f,.78f,0),new Vector3(w*.20f,.02f,d*.83f),new Vector3(12,0,0),new Color(.18f,.32f,.49f));
                    break;
                case Kind.Wind:
                    Box(b,0,1.35f,0,.30f,2.7f,.30f,tint);Box(b,0,2.7f,0,.5f,.45f,.55f,Steel);
                    for(int i=0;i<3;i++)Angle(b,new Vector3(0,2.7f,.32f)+Quaternion.Euler(0,0,i*120)*new Vector3(0,.65f,0),new Vector3(.16f,1.3f,.08f),new Vector3(0,0,i*120),new Color(.72f,.75f,.70f));
                    break;
                case Kind.Generator:
                    Box(b,0,.42f,0,w*.85f,.84f,d*.85f,tint);Box(b,-w*.25f,1.15f,-d*.26f,.28f,1.55f,.30f,Dark);
                    for(int i=0;i<4;i++)Box(b,w*.22f,.90f,(i-1.5f)*d*.16f,w*.29f,.09f,d*.11f,Steel);
                    break;
                case Kind.Battery:
                    Box(b,0,.47f,0,w*.76f,.94f,d*.84f,tint);Box(b,0,.98f,0,w*.82f,.10f,d*.9f,Dark);
                    Box(b,-w*.21f,1.08f,0,.13f,.12f,.13f,new Color(.64f,.18f,.13f));Box(b,w*.21f,1.08f,0,.13f,.12f,.13f,Dark);break;
                case Kind.Cooler:
                    Box(b,0,1.05f,0,w*.95f,1.3f,d*.88f,tint);
                    for(int i=0;i<6;i++)Box(b,0,.59f+i*.18f,d*.45f,w*.78f,.06f,.04f,Dark);break;
                case Kind.Heater:
                    Box(b,0,.36f,0,w*.67f,.72f,d*.60f,tint);
                    for(int i=0;i<5;i++)Box(b,(i-2)*w*.105f,.36f,d*.31f,.04f,.52f,.04f,new Color(.61f,.29f,.13f));break;
                case Kind.Research:case Kind.Machining:case Kind.Stove:case Kind.Comms:case Kind.Medical:
                    Box(b,0,.4f,0,w*.84f,.80f,d*.79f,tint);Box(b,0,.85f,0,w*.96f,.12f,d*.90f,Steel);
                    if(kind==Kind.Research||kind==Kind.Comms||kind==Kind.Medical)
                    {
                        Box(b,0,1.13f,-d*.20f,w*.36f,.49f,.15f,Dark);Box(b,0,1.13f,-d*.11f,w*.29f,.35f,.025f,Glass);
                        Box(b,0,.94f,d*.20f,w*.46f,.035f,d*.22f,Dark);
                        if(kind==Kind.Medical)Box(b,w*.38f,1.24f,0,.055f,1.1f,.055f,Steel);
                    }
                    else if(kind==Kind.Stove)
                        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box(b,x*w*.22f,.93f,z*d*.22f,w*.23f,.035f,d*.24f,Dark);
                    else {Box(b,-w*.25f,1.18f,-d*.14f,.22f,.58f,.22f,Dark);Box(b,0,1.45f,-d*.14f,w*.60f,.17f,.25f,Steel);}
                    break;
                case Kind.Smelter:
                    Box(b,0,.63f,0,w*.87f,1.26f,d*.86f,tint);Box(b,0,.50f,d*.44f,w*.49f,.55f,.04f,Dark);
                    Box(b,0,1.57f,-d*.23f,w*.34f,.70f,d*.32f,Dark);break;
                case Kind.Shelf:
                    Box(b,-w*.43f,.75f,0,.10f,1.5f,d*.85f,tint);Box(b,w*.43f,.75f,0,.10f,1.5f,d*.85f,tint);
                    for(int i=0;i<3;i++)Box(b,0,.12f+i*.65f,0,w*.93f,.10f,d*.89f,tint);break;
                case Kind.Storage:
                    Box(b,0,.48f,0,w*.85f,.96f,d*.85f,tint);Box(b,0,.99f,0,w*.90f,.08f,d*.90f,Dark);
                    Box(b,0,.60f,d*.44f,w*.25f,.14f,.04f,Steel);break;
                case Kind.Pod:
                    Box(b,0,.40f,0,w*.82f,.70f,d*.91f,tint);Box(b,0,.83f,0,w*.69f,.32f,d*.70f,Glass);
                    Box(b,0,.85f,d*.37f,w*.77f,.43f,.12f,Steel);break;
                case Kind.Fence:
                    for(int side=-1;side<=1;side+=2)Box(b,side*w*.43f,.55f,0,.12f,1.1f,.12f,tint);
                    Box(b,0,.35f,0,w,.09f,.08f,tint);Box(b,0,.83f,0,w,.09f,.08f,tint);break;
                case Kind.Trap:
                    Box(b,0,.06f,0,w*.78f,.12f,d*.78f,wood);
                    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)b.Pyramid(new Vector3(x*w*.22f,.12f,z*d*.22f),.12f,.45f,Steel);break;
                case Kind.Sandbags:
                    for(int row=0;row<3;row++)for(int i=0;i<3;i++)Box(b,(i-1)*w*.29f+(row%2)*w*.045f,.13f+row*.20f,0,w*.31f,.21f,d*.73f,new Color(.54f,.48f,.31f));break;
                case Kind.Barricade:
                    AddBarricade(b,w,d,tint);break;
                case Kind.Embrasure:
                    AddEmbrasure(b,w,d,tint);break;
                case Kind.Monument:
                    Box(b,0,.18f,0,w*.82f,.36f,d*.82f,tint);b.Pyramid(new Vector3(0,.36f,0),Math.Min(w,d)*.58f,1.35f,tint);
                    Box(b,0,1.74f,0,.30f,.30f,.30f,tint);break;
                case Kind.Mushroom:
                    Box(b,0,.25f*growth,0,.12f*growth,.5f*growth,.12f*growth,new Color(.72f,.66f,.48f));
                    b.Pyramid(new Vector3(0,.42f*growth,0),.75f*growth,.25f*growth,tint);break;
                case Kind.Cactus:
                    Box(b,0,1f*growth,0,.28f*growth,2f*growth,.28f*growth,green);
                    Box(b,-.25f*growth,.8f*growth,0,.55f*growth,.16f*growth,.18f*growth,green);
                    Box(b,-.46f*growth,1.1f*growth,0,.17f*growth,.65f*growth,.17f*growth,green);break;
                case Kind.Broadleaf:
                    Box(b,0,1.15f*growth,0,.23f*growth,2.3f*growth,.23f*growth,wood);
                    Box(b,0,2.05f*growth,0,1.25f*growth,.88f*growth,1.25f*growth,tint);
                    b.Pyramid(new Vector3(0,2.49f*growth,0),1.35f*growth,.60f*growth,tint);break;
                case Kind.Log:
                    AddLogs(b,ResourceStackCount(count));break;
                case Kind.Metal:
                    AddIngots(b,ResourceStackCount(count),tint);break;
                case Kind.Stone:
                    AddBlocks(b,ResourceStackCount(count),tint);break;
                case Kind.Chunk:
                    AddChunk(b,tint);break;
                case Kind.Component:
                    Box(b,0,.07f,0,.40f,.14f,.32f,Dark);Box(b,0,.15f,0,.27f,.025f,.22f,green);
                    for(int i=0;i<3;i++)Box(b,(i-1)*.08f,.18f,0,.04f,.05f,.12f,Steel);break;
                case Kind.Medicine:
                    Box(b,0,.15f,0,.37f,.30f,.26f,new Color(.80f,.80f,.71f));
                    Box(b,0,.31f,0,.07f,.02f,.19f,new Color(.63f,.17f,.15f));Box(b,0,.31f,0,.24f,.02f,.06f,new Color(.63f,.17f,.15f));break;
                case Kind.Meal:
                    Box(b,0,.035f,0,.43f,.07f,.34f,new Color(.71f,.70f,.61f));Box(b,0,.10f,0,.33f,.09f,.24f,new Color(.54f,.44f,.24f));break;
                case Kind.RawFood:
                    for(int i=0;i<3;i++)b.Pyramid(new Vector3((i-1)*.11f,.02f,(i%2)*.10f),.20f,.20f,tint);break;
                case Kind.Book:
                    Box(b,0,.012f,0,.33f,.024f,.43f,tint);Box(b,0,.128f,0,.33f,.024f,.43f,tint);
                    Box(b,-.155f,.07f,0,.02f,.116f,.43f,tint);Box(b,.008f,.07f,0,.30f,.09f,.40f,new Color(.79f,.76f,.62f));break;
                case Kind.Headwear:
                    Box(b,0,.14f,0,.34f,.28f,.33f,tint);Box(b,0,.04f,.05f,.44f,.05f,.43f,tint);break;
                case Kind.Clothing:
                    Box(b,0,.08f,0,.37f,.14f,.52f,tint);Box(b,-.25f,.07f,.11f,.18f,.12f,.24f,tint);Box(b,.25f,.07f,.11f,.18f,.12f,.24f,tint);break;
            }
        }
        static void Box(NativeScene.Builder b,float x,float y,float z,float w,float h,float d,Color color)=>b.Box(new Vector3(x,y,z),new Vector3(w,h,d),color);
        static void Angle(NativeScene.Builder b,Vector3 position,Vector3 size,Vector3 euler,Color color)
        {
            Vector3 origin=b.Origin;Quaternion rotation=b.Rotation;
            b.Origin=origin+rotation*position;b.Rotation=rotation*Quaternion.Euler(euler);
            try{b.Box(Vector3.zero,size,color);}finally{b.Origin=origin;b.Rotation=rotation;}
        }
    }
}
