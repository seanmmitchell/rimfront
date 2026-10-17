using UnityEngine;

namespace RimFront
{
    internal static class NativePawnRig
    {
        internal enum Form { Human, Quadruped, Bird, Insect, Serpent, MechBiped, MechCrawler, MechCentipede, Entity }
        internal enum BodyShape { Average, Female, Thin, Fat, Hulk, Child }
        internal enum Part { Body, Head, LeftArm, RightArm, LeftLeg, RightLeg, Tail, Count }
        internal struct Pose
        {
            internal Vector3 Offset, Euler;
            internal Pose(Vector3 offset, Vector3 euler) { Offset = offset; Euler = euler; }
        }
    }
}
