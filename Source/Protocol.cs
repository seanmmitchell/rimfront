using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RimFront
{
    [DataContract]
    public sealed class InputCommand
    {
        [DataMember] public int seq;
        [DataMember] public string action;
        [DataMember] public float forward;
        [DataMember] public float strafe;
        [DataMember] public float yaw;
        [DataMember] public float pitch;
        [DataMember] public bool fire;
        [DataMember] public bool reload;
        [DataMember] public bool cycleFireMode;
        [DataMember] public int pawnId;

        public bool IsValid()
        {
            return seq >= 0 && (action == "input" || action == "release") &&
                Finite(forward) && Finite(strafe) && Finite(yaw) && Finite(pitch) && Math.Abs(pitch) < Math.PI / 2 &&
                Math.Abs(forward) <= 1 && Math.Abs(strafe) <= 1 && Math.Abs(yaw) <= 100000;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    [DataContract]
    public sealed class WorldSnapshot
    {
        [DataMember] public int protocol = 1;
        [DataMember] public string gameVersion = "";
        [DataMember] public string status = "idle";
        [DataMember] public string combatMode = "vanilla";
        [DataMember] public bool active;
        [DataMember] public bool paused;
        [DataMember] public int tick;
        [DataMember] public int mapId;
        [DataMember] public int width;
        [DataMember] public int height;
        [DataMember] public int controlledPawnId;
        [DataMember] public int[] cells = new int[0];
        [DataMember] public PawnState[] pawns = new PawnState[0];
        [DataMember] public string message = "Select a drafted colonist in RimWorld and choose Enter RimFront.";
        [DataMember] public WeaponState weapon = new WeaponState();
    }

    [DataContract]
    public sealed class PawnState
    {
        [DataMember] public int id;
        [DataMember] public string name;
        [DataMember] public float x;
        [DataMember] public float z;
        [DataMember] public float health;
        [DataMember] public bool hostile;
        [DataMember] public bool downed;
    }

    [DataContract]
    public sealed class WeaponState
    {
        [DataMember] public string label = "Unarmed";
        [DataMember] public string status = "";
        [DataMember] public int ammo = -1;
        [DataMember] public int capacity = -1;
        [DataMember] public bool canReload;
        [DataMember] public string fireMode = "Fixed";
    }

    public static class WireJson
    {
        public static byte[] Encode<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }
        public static T Decode<T>(byte[] value)
        {
            using (var stream = new MemoryStream(value))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
