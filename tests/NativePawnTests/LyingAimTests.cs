using System;
using System.Reflection;

internal static partial class Program
{
    static void TestLyingAim(Assembly assembly)
    {
        Type pawns = assembly.GetType("RimFront.NativePawns", true);
        Type boundsType = vector.Assembly.GetType("UnityEngine.Bounds", true);
        object bounds = Activator.CreateInstance(boundsType, V(0, .8625f, .01f), V(.71f, 1.725f, .38f));
        MethodInfo bed = pawns.GetMethod("BedRoot", Flags), aim = pawns.GetMethod("TryAimBounds", Flags);
        foreach (float scale in new[] { .45f, 1f, 2.5f })
        foreach (float degrees in new[] { 0f, 90f, 180f, 270f })
        foreach (float lateral in new[] { 0f, 1f })
        {
            double angle = degrees * Math.PI / 180;
            float forwardX = (float)Math.Sin(angle), forwardZ = (float)Math.Cos(angle);
            float sideX = forwardZ, sideZ = -forwardX;
            object head = V(120.5f + sideX * lateral, 0, 120.5f + sideZ * lateral);
            float[] h = Coordinates(head);
            object feet = V(h[0] + forwardX, 0, h[2] + forwardZ);
            object root = bed.Invoke(null, new object[] { head, feet, degrees, .71f, scale, .85f });
            MethodInfo point = root.GetType().GetMethod("MultiplyPoint3x4", new[] { vector });
            float[] center = Coordinates(point.Invoke(root, new[] { V(0, .85f, 0) }));
            object eye = V(center[0] + sideX * 5, 1.55f, center[2] + sideZ * 5);
            float[] e = Coordinates(eye);
            foreach (float bodyY in new[] { .1f, .95f, 1.55f })
            {
                float[] target = Coordinates(point.Invoke(root, new[] { V(0, bodyY, 0) }));
                double dx = target[0] - e[0], dz = target[2] - e[2], horizontal = Math.Sqrt(dx * dx + dz * dz);
                float yaw = (float)Math.Atan2(dx, dz), pitch = (float)Math.Atan2(e[1] - target[1], horizontal);
                object[] args = { bounds, root, eye, yaw, pitch, 0d };
                Check((bool)aim.Invoke(null, args), "lying head/torso/feet selectable from side across bed rotation/slot/scale");
                double distance = (double)args[5];
                Check(distance >= 0 && distance <= horizontal + .001 && distance >= horizontal - scale,
                    "lying hit distance uses horizontal ray units");
                args[3] = yaw + (float)Math.PI; args[4] = -pitch;
                Check(!(bool)aim.Invoke(null, args), "lying target behind ray rejected");
            }
            foreach (float height in new[] { .4f, .75f + .71f * scale + .1f })
            {
                object outsideEye = V(center[0] + sideX * 5, height, center[2] + sideZ * 5);
                float yaw = (float)Math.Atan2(-sideX, -sideZ);
                object[] args = { bounds, root, outsideEye, yaw, 0f, 0d };
                Check(!(bool)aim.Invoke(null, args), "horizontal ray below mattress or above lying body rejected");
            }
        }
        object identity = assembly.GetType("RimFront.NativePawns", true).GetMethod("BedRoot", Flags)
            .Invoke(null, new object[] { V(0, 0, 0), V(0, 0, 1), 0f, .71f, 1f, .85f });
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            object[] args = { bounds, identity, V(5, 1.55f, 0), invalid, 0f, 0d };
            Check(!(bool)aim.Invoke(null, args), "invalid lying ray yaw rejected");
            args[3] = 0f; args[4] = invalid;
            Check(!(bool)aim.Invoke(null, args), "invalid lying ray pitch rejected");
        }
    }
}
