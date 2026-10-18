using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static partial class Program
{
    static void TestMachineGuns(Assembly assembly)
    {
        Type guns = assembly.GetType("RimFront.NativeMachineGuns", true), profiles = guns.GetNestedType("Profile", Flags),
            weapons = assembly.GetType("RimFront.NativeWeapons", true), parts = weapons.GetNestedType("WeaponPart", Flags),
            bipods = assembly.GetType("RimFront.NativeWeaponBrace", true);
        MethodInfo build = guns.GetMethod("BuildGeometry", Flags), classify = guns.GetMethod("Classify", Flags),
            supports = bipods.GetMethod("CanBrace", Flags), supportMesh = guns.GetMethod("BuildBipod", Flags);
        var shapes = new HashSet<string>();
        foreach (string name in new[] { "M60", "M249", "PKM", "MG42", "RPK", "DP28", "Lewis", "M240", "M1919" })
        {
            object profile = classify.Invoke(null, new object[] { "CE_Gun_" + name });
            Check(profile.ToString() == name, "CE profile recognized: " + name);
            Check(weapons.GetMethod("ClassifyName", Flags).Invoke(null, new object[] { "CE_Gun_" + name, true }).ToString() == "Heavy", "named CE gun uses heavy grip/muzzle/reload: " + name);
            object mesh = Activator.CreateInstance(builder, true);
            foreach (object part in Enum.GetValues(parts)) build.Invoke(null, new[] { mesh, profile, part });
            ValidateMesh(mesh, name);
            List<float[]> points = Vectors(mesh, "Vertices");
            Check(points.Count < 1400, "machine gun fits mesh budget: " + name);
            Check(Math.Abs(points.Max(p => p[2]) - 1.16f) < .0001, "profile muzzle shares heavy weapon socket: " + name);
            Check(shapes.Add(string.Join(";", points.Select(p => string.Join(",", p)))), "profile has distinct geometry: " + name);
            foreach (bool deployed in new[] { false, true })
            {
                object support = Activator.CreateInstance(builder, true);
                supportMesh.Invoke(null, new object[] { support, deployed });
                ValidateMesh(support, "bipod " + deployed);
                Check(Vectors(support, "Vertices").Count <= 120, "bounded shared bipod geometry");
                if (deployed) Check(Vectors(support, "Vertices").Min(p => p[1]) < -.45f, "deployed bipod has feet below receiver");
                object sample = Activator.CreateInstance(builder, true);
                foreach (object part in Enum.GetValues(parts)) build.Invoke(null, new[] { sample, profile, part });
                supportMesh.Invoke(null, new object[] { sample, deployed });
                AddCatalog(name + (deployed ? " deployed" : " folded"), sample, weapon: true);
            }
        }
        foreach (var alias in new[] { ("CE_Gun_MSixty", "M60"), ("CE_Gun_MTwoFourNine", "M249"), ("RNGun_M60LMG", "M60"), ("Gun_M240B", "M240") })
            Check(classify.Invoke(null, new object[] { alias.Item1 }).ToString() == alias.Item2, "actual CE/other mod definition alias: " + alias.Item1);
        foreach (string name in new[] { "Gun_AssaultRifle", "Gun_SniperRifle", "Gun_LMG", "Gun_PumpShotgun", "CE_Gun_M60" })
            Check((bool)supports.Invoke(null, new object[] { name, true }), "long gun can mount: " + name);
        foreach (var sample in new[] { ("Gun_Autopistol", true), ("Bow", true), ("FragGrenade", true), ("Knife", false) })
            Check(!(bool)supports.Invoke(null, new object[] { sample.Item1, sample.Item2 }), "unsupported weapon cannot mount: " + sample.Item1);
        Check(classify.Invoke(null, new object[] { "UnknownFutureGun" }).ToString() == "Generic", "unknown weapons retain generic model");
        Check((int)guns.GetField("MaximumMeshes", Flags).GetRawConstantValue() == (Enum.GetValues(profiles).Length - 1) * 4, "profile cache budget includes whole model and reload parts");
    }
}
