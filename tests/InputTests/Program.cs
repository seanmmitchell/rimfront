using System;
using RimFront;

class Program
{
    static int checks;
    static int Main()
    {
        try
        {
            Check(!new InputCommand { action = "input", yaw = float.NaN }.IsValid(), "NaN rejected");
            foreach (float pitch in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, (float)(Math.PI / 2), (float)(-Math.PI / 2) })
                Check(!new InputCommand { action = "input", pitch = pitch }.IsValid(), "invalid pitch rejected: " + pitch);
            Check(new InputCommand { action = "input", pitch = .7f }.IsValid(), "positive pitch accepted");
            Check(new InputCommand { action = "input", pitch = -.7f }.IsValid(), "negative pitch accepted");
            Check(AimGeometry.RayIntersectsBox(-5, .8, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out var boxDistance)
                && Math.Abs(boxDistance - 4.65) < .000001, "box slab returns entry distance");
            Check(AimGeometry.RayIntersectsBox(-5, .8, 0, .5, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out boxDistance)
                && Math.Abs(boxDistance - 9.3) < .000001, "inverse model scale preserves ray parameter");
            Check(!AimGeometry.RayIntersectsBox(-5, -.01, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "parallel ray below box rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, .21, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "parallel ray outside depth rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, 0, -1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "box behind ray rejected");
            Check(AimGeometry.RayIntersectsBox(0, .8, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out boxDistance) && boxDistance == 0, "inside box accepted at zero distance");
            Check(!AimGeometry.RayIntersectsBox(0, .8, 0, 0, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "zero direction rejected");
            Check(!AimGeometry.RayIntersectsBox(double.NaN, .8, 0, 1, 0, 0, -.35, 0, -.2, .35, 1.725, .2, out _), "nonfinite ray rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, 0, 1, 0, 0, -.35, 0, -.2, double.PositiveInfinity, 1.725, .2, out _), "nonfinite box rejected");
            Check(!AimGeometry.RayIntersectsBox(-5, .8, 0, 1, 0, 0, .35, 0, -.2, -.35, 1.725, .2, out _), "inverted box rejected");
            Check(!new InputCommand { action = "input", forward = 2 }.IsValid(), "out of range movement rejected");
            Check(!new InputCommand { action = "teleport" }.IsValid(), "unknown action rejected");

            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Check(!new InputCommand { action = "input", forward = value }.IsValid(), "nonfinite forward rejected");
                Check(!new InputCommand { action = "input", strafe = value }.IsValid(), "nonfinite strafe rejected");
                Check(!new InputCommand { action = "input", yaw = value }.IsValid(), "nonfinite yaw rejected");
            }
            Check(new InputCommand { action = "release" }.IsValid(), "release remains a valid native control command");
            Check(!new InputCommand().IsValid(), "missing action rejected");
            foreach (float edge in new[] { -1f, 1f })
                Check(new InputCommand { action = "input", forward = edge, strafe = edge }.IsValid(), "signed movement boundary accepted");
            foreach (float outside in new[] { -1.001f, 1.001f })
            {
                Check(!new InputCommand { action = "input", forward = outside }.IsValid(), "forward beyond boundary rejected");
                Check(!new InputCommand { action = "input", strafe = outside }.IsValid(), "strafe beyond boundary rejected");
            }
            Check(new InputCommand { action = "input", yaw = 100000 }.IsValid(), "large accumulated yaw boundary accepted");
            Check(!new InputCommand { action = "input", yaw = -100001 }.IsValid(), "unbounded accumulated yaw rejected");
            Check(new InputCommand { action = "input", fire = true, reload = true, cycleFireMode = true, toggleBipod = true }.IsValid(), "native button combinations remain valid for tick policy arbitration");
            Console.WriteLine("PASS: " + checks + " native input/aim geometry checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        checks++;
    }
}
