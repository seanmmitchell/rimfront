namespace RimFront
{
    internal static class RimFrontMultiplayer { internal static bool InSession => false; }
    internal static class SimulationGeometry
    {
        internal static UnityEngine.Vector3 Position(Verse.Thing thing) => throw new System.Exception("SP terrain suite entered MP position code");
        internal static UnityEngine.Vector3 Eye(Verse.Pawn pawn) => throw new System.Exception("SP terrain suite entered MP aim code");
        internal static void AddMountain(NativeScene.Builder builder, Verse.Thing thing, bool fogged) => throw new System.Exception("SP terrain suite entered MP mountain code");
    }
}
