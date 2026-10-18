using System;
using UnityEngine;

namespace RimFront
{
    internal static partial class NativeObjectModels
    {
        static void AddBarricade(NativeScene.Builder b, float w, float d, Color tint)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Box(b,side*w*.36f,.43f,0,w*.13f,.86f,d*.22f,tint*.78f);
                Box(b,side*w*.36f,.055f,0,w*.20f,.11f,d*.82f,tint*.70f);
                Angle(b,new Vector3(side*w*.36f,.35f,-d*.17f),new Vector3(w*.10f,.69f,d*.12f),new Vector3(-25,0,0),tint*.85f);
            }
            for (int row = 0; row < 3; row++)
            {
                float y = .23f + row*.22f;
                Box(b,0,y,d*.07f,w*.96f,.185f,d*.22f,tint*(.92f+row*.04f));
                for (int side = -1; side <= 1; side += 2)
                    Box(b,side*w*.36f,y,d*.185f,w*.034f,.034f,d*.018f,Dark);
            }
            Box(b,0,.875f,0,w,.09f,d*.36f,tint*1.07f);
        }

        static void AddEmbrasure(NativeScene.Builder b, float w, float d, Color tint)
        {
            // A real opening at standing eye height; the same triangles are used
            // by crosshair selection and supported presentation-space projectiles.
            Box(b,0,.63f,0,w,1.26f,d,tint);
            Box(b,0,2.12f,0,w,.56f,d,tint);
            // Linked wall definitions are often non-rotatable. Corner piers leave
            // ports on all four faces so north/south runs can also be fired through.
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Box(b,x*w*.37f,1.55f,z*d*.37f,w*.26f,.58f,d*.26f,tint*.91f);
            Box(b,0,1.275f,0,w*1.025f,.03f,d*1.025f,tint*1.12f);
            Box(b,0,1.825f,0,w*1.025f,.03f,d*1.025f,tint*.82f);
            Box(b,0,2.425f,0,w*1.025f,.05f,d*1.025f,tint*1.06f);
        }

        static void AddLogs(NativeScene.Builder b, int pieces)
        {
            int remaining = pieces;
            for (int row = 0; row < 4 && remaining > 0; row++)
            {
                int slots = 6-row, used = Math.Min(slots,remaining);
                for (int i = 0; i < used; i++)
                {
                    // Start at the center, adding symmetric pairs as the pile grows.
                    float x = CenterSlot(i,slots)*.132f;
                    float z = ((i+row)%3-1)*.016f;
                    Log(b,new Vector3(x,.065f+row*.1144f,z),.064f,.68f+(i%3)*.025f,
                        new Color(.34f,.22f,.12f)*(1f+(i%3)*.065f));
                }
                remaining -= used;
            }
        }

        static float CenterSlot(int index, int slots)
        {
            if (slots%2 == 0) return (index%2 == 0 ? -1 : 1)*(index/2+.5f);
            return index == 0 ? 0 : (index%2 == 1 ? -1 : 1)*((index+1)/2);
        }

        static void Log(NativeScene.Builder b, Vector3 center, float radius, float length, Color bark)
        {
            Color end = new Color(.72f,.53f,.31f);
            for (int i = 0; i < 8; i++)
            {
                float a = i*Mathf.PI*.25f, next = (i+1)*Mathf.PI*.25f;
                Vector3 p = new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0);
                Vector3 q = new Vector3(Mathf.Cos(next)*radius,Mathf.Sin(next)*radius,0);
                Vector3 front = center+new Vector3(0,0,-length*.5f), back = center+new Vector3(0,0,length*.5f);
                Color face = bark*(.86f+(i%3)*.07f);
                b.Triangle(front+p,front+q,back+q,face);
                b.Triangle(front+p,back+q,back+p,face);
                // A dark bark rim surrounds the inset end grain on both cut ends.
                b.Triangle(front,front+q,front+p,bark);
                b.Triangle(back,back+p,back+q,bark);
                Vector3 f = front+new Vector3(0,0,-.001f), r = back+new Vector3(0,0,.001f);
                b.Triangle(f,f+q*.81f,f+p*.81f,end);
                b.Triangle(r,r+p*.81f,r+q*.81f,end);
                b.Triangle(f+new Vector3(0,0,-.001f),f+q*.42f+new Vector3(0,0,-.001f),f+p*.42f+new Vector3(0,0,-.001f),end*.88f);
                b.Triangle(r+new Vector3(0,0,.001f),r+p*.42f+new Vector3(0,0,.001f),r+q*.42f+new Vector3(0,0,.001f),end*.88f);
            }
        }

        static void AddIngots(NativeScene.Builder b, int pieces, Color tint)
        {
            // Uncolored vanilla steel uses a cool metal tone; other resources retain
            // their native gold/silver/plasteel color.
            Color metal = tint == Color.white ? Steel : tint;
            for (int i = 0; i < pieces; i++)
            {
                int layer = i/6, slot = i%6;
                Vector3 p = new Vector3(CenterSlot(slot%3,3)*.25f,layer*.105f,slot/3*.32f-.16f);
                BeveledBlock(b,p,new Vector3(.235f,.10f,.30f),.027f,metal*(.94f+(slot%3)*.035f));
            }
        }

        static void AddBlocks(NativeScene.Builder b, int pieces, Color tint)
        {
            Color stone = tint == Color.white ? new Color(.57f,.56f,.52f) : tint;
            for (int i = 0; i < pieces; i++)
            {
                int layer = i/6, slot = i%6;
                // Alternate courses across the pile, like stacked dressed masonry.
                Vector3 p = layer%2 == 0
                    ? new Vector3(CenterSlot(slot%3,3)*.253f,layer*.158f,slot/3*.34f-.17f)
                    : new Vector3(slot/3*.34f-.17f,layer*.158f,CenterSlot(slot%3,3)*.253f);
                if (layer%2 == 0) BeveledBlock(b,p,new Vector3(.24f,.15f,.325f),.014f,stone*(.90f+(slot%4)*.035f));
                else
                {
                    Vector3 origin = b.Origin; Quaternion rotation = b.Rotation;
                    b.Origin = origin+rotation*p; b.Rotation = rotation*Quaternion.Euler(0,90,0);
                    try { BeveledBlock(b,Vector3.zero,new Vector3(.24f,.15f,.325f),.014f,stone*(.90f+(slot%4)*.035f)); }
                    finally { b.Origin = origin; b.Rotation = rotation; }
                }
            }
        }

        static void BeveledBlock(NativeScene.Builder b, Vector3 baseCenter, Vector3 size, float bevel, Color tint)
        {
            float x = size.x*.5f, z = size.z*.5f;
            var bottom = new[] { new Vector3(-x,0,-z),new Vector3(x,0,-z),new Vector3(x,0,z),new Vector3(-x,0,z) };
            var top = new[] { new Vector3(-x+bevel,size.y,-z+bevel),new Vector3(x-bevel,size.y,-z+bevel),
                new Vector3(x-bevel,size.y,z-bevel),new Vector3(-x+bevel,size.y,z-bevel) };
            for (int i = 0; i < 4; i++)
            {
                int next = (i+1)%4;
                Vector3 a = baseCenter+bottom[i], d = baseCenter+bottom[next];
                Vector3 u = a+new Vector3(0,size.y-bevel,0), v = d+new Vector3(0,size.y-bevel,0);
                Vector3 t = baseCenter+top[i], s = baseCenter+top[next];
                Color side = tint*(.70f+i*.065f);
                b.Triangle(a,u,v,side); b.Triangle(a,v,d,side);
                b.Triangle(u,t,s,tint*1.07f); b.Triangle(u,s,v,tint*1.07f);
            }
            b.Triangle(baseCenter+top[0],baseCenter+top[3],baseCenter+top[2],tint);
            b.Triangle(baseCenter+top[0],baseCenter+top[2],baseCenter+top[1],tint);
            b.Triangle(baseCenter+bottom[0],baseCenter+bottom[1],baseCenter+bottom[2],tint*.55f);
            b.Triangle(baseCenter+bottom[0],baseCenter+bottom[2],baseCenter+bottom[3],tint*.55f);
        }

        static void AddChunk(NativeScene.Builder b, Color tint)
        {
            // Rough chunks remain distinct from the manufactured block stockpiles.
            BeveledBlock(b,Vector3.zero,new Vector3(.62f,.42f,.57f),.13f,tint);
        }
    }
}
