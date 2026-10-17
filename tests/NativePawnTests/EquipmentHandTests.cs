using System;
using System.Linq;
using System.Reflection;

internal static partial class Program
{
    static void TestHandAllocation(Type weapons,Type equipment,Type handType,Type appearanceType,Type snapshotType)
    {
        MethodInfo resolve=weapons.GetMethod("ResolveHands",Flags),view=weapons.GetMethod("ViewHand",Flags),draw=weapons.GetMethod("DrawViewHand",Flags);
        int leftBit=1<<(int)Enum.Parse(part,"LeftArm"),rightBit=1<<(int)Enum.Parse(part,"RightArm");
        foreach(int missing in new[]{0,leftBit,rightBit,leftBit|rightBit})foreach(bool weapon in new[]{false,true})foreach(bool shield in new[]{false,true})foreach(bool drafted in new[]{false,true})
        {
            object[] args={missing,weapon,shield,drafted,null,null};resolve.Invoke(null,args);
            string w=args[4].ToString(),s=args[5].ToString();
            bool left=(missing&leftBit)==0,right=(missing&rightBit)==0;
            string expectedWeapon=!drafted||!weapon?"None":right?"Right":left?"Left":"None";
            string expectedShield=!drafted||!shield?"None":left&&expectedWeapon!="Left"?"Left":right&&expectedWeapon!="Right"?"Right":"None";
            Check(w==expectedWeapon&&s==expectedShield,"physical hands are allocated consistently across draft/equipment/anatomy combinations");
            Check(w=="None"||s=="None"||w!=s,"one physical hand cannot hold weapon and shield simultaneously");
            object a=Activator.CreateInstance(appearanceType,true);Set(a,"MissingParts",missing);
            foreach(bool supporting in new[]{false,true})
            {
                object hand=view.Invoke(null,new[]{a,(object)supporting});
                bool shown=(bool)draw.Invoke(null,new[]{a,(object)supporting,args[5]});
                Check(!shown||(hand.ToString()!="None"&&hand.ToString()!=s),"first-person hand meshes never duplicate a shield grip or absent limb");
                if(missing==(leftBit|rightBit))Check(!shown,"bare fists are hidden when both arms are missing");
            }
            if(missing==rightBit)
            {
                Check(view.Invoke(null,new[]{a,(object)false}).ToString()=="Left","remaining left hand takes first-person weapon grip or bare fist");
                Check(view.Invoke(null,new[]{a,(object)true}).ToString()=="None","missing right arm cannot appear as supporting hand");
            }
        }
        MethodInfo offset=weapons.GetMethod("FirstPersonOffset",Flags),shieldOffset=equipment.GetMethod("FirstPersonShieldOffset",Flags);
        float[] rightOffset=Coordinates(offset.Invoke(null,new[]{Enum.Parse(handType,"Right"),(object)0f})),leftOffset=Coordinates(offset.Invoke(null,new[]{Enum.Parse(handType,"Left"),(object)0f}));
        Check(leftOffset[0]<0&&rightOffset[0]>0&&Math.Abs(leftOffset[0]+rightOffset[0])<.00001f&&leftOffset[1]==rightOffset[1]&&leftOffset[2]==rightOffset[2],"first-person weapon moves to remaining hand's side without changing reach");
        Check(Coordinates(shieldOffset.Invoke(null,new[]{Enum.Parse(handType,"Left")}))[0]<0&&Coordinates(shieldOffset.Invoke(null,new[]{Enum.Parse(handType,"Right")}))[0]>0,"first-person shield follows whichever free hand owns it");

        Assembly unity=vector.Assembly;Type quaternion=unity.GetType("UnityEngine.Quaternion",true);
        MethodInfo rotate=quaternion.GetMethod("op_Multiply",new[]{quaternion,vector}),attach=weapons.GetMethod("AttachmentPose",Flags);
        Type kind=weapons.GetNestedType("WeaponKind",Flags),placement=weapons.GetNestedType("CarryPlacement",Flags);
        foreach(int missing in new[]{0,rightBit})foreach(bool articulated in new[]{false,true})foreach(float pitch in new[]{-70f,-30f,0f,30f,70f})
            foreach(string family in new[]{"Pistol","Revolver","Rifle","Shotgun","Smg","Sniper","Heavy","Launcher","Bow"})
        {
            object a=Activator.CreateInstance(appearanceType,true),state=Activator.CreateInstance(snapshotType);Set(a,"MissingParts",missing);Set(state,"Aiming",true);Set(state,"AimPitch",pitch);
            object[] args={a,state,articulated,Enum.Parse(kind,family),Enum.Parse(placement,"Held"),null,null};
            Check((bool)attach.Invoke(null,args),"ranged weapon remains attached for animated/static and left fallback aim");
            float[] direction=Coordinates(rotate.Invoke(null,new[]{args[6],V(0,0,1)}));
            float radians=pitch*(float)Math.PI/180;
            Check(Close(direction,new[]{0f,-(float)Math.Sin(radians),(float)Math.Cos(radians)}),"barrel matches aim pitch independently of wrist rotation: "+family+" pitch "+pitch+" articulated "+articulated);
            object[] socketArgs={a,state,articulated,missing==rightBit,null,null};weapons.GetMethod("HandSocket",Flags).Invoke(null,socketArgs);
            float[] grip=Coordinates(weapons.GetMethod("GripLocal",Flags).Invoke(null,new[]{Enum.Parse(kind,family)}));
            float[] moved=Coordinates(rotate.Invoke(null,new[]{args[6],V(grip[0]*.78f,grip[1]*.78f,grip[2]*.78f)})),origin=Coordinates(args[5]);
            Check(Close(Enumerable.Range(0,3).Select(i=>origin[i]+moved[i]).ToArray(),Coordinates(socketArgs[4])),"correcting barrel orientation does not detach modeled grip");
        }
        foreach(string action in new[]{"Melee","Reload"})
        {
            object a=Activator.CreateInstance(appearanceType,true),state=Activator.CreateInstance(snapshotType);Set(state,"Aiming",true);
            if(action=="Melee"){Set(state,"Melee",1f);Set(state,"MeleeProgress",.5f);}else Set(state,"Reloading",true);
            object[] args={a,state,true,Enum.Parse(kind,"Rifle"),Enum.Parse(placement,"Held"),null,null};attach.Invoke(null,args);
            float[] direction=Coordinates(rotate.Invoke(null,new[]{args[6],V(0,0,1)}));
            Check(!Close(direction,new[]{0f,0f,1f}),"ranged aim correction preserves active "+action+" pose");
        }
    }
}
