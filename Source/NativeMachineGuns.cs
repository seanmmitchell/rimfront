using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimFront
{
    // Original geometry. Profiles affect presentation only, never weapon mechanics.
    internal static class NativeMachineGuns
    {
        internal enum Profile { Generic, M60, M249, PKM, MG42, RPK, DP28, Lewis, M240, M1919 }
        internal const int MaximumMeshes = 9 * 4;
        static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        static readonly Color Steel = new Color(.23f, .27f, .26f), Edge = new Color(.40f, .44f, .41f),
            Black = new Color(.10f, .12f, .11f), Wood = new Color(.47f, .29f, .14f), Green = new Color(.34f, .39f, .24f),
            Brass = new Color(.67f, .51f, .24f);
        internal static Profile Classify(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("m249") || n.Contains("mtwofournine") || n.Contains("minimi")) return Profile.M249;
            if (n.Contains("m240") || n.Contains("fnmag")) return Profile.M240;
            if (n.Contains("m1919")) return Profile.M1919;
            if (n.Contains("m60") || n.Contains("msixty")) return Profile.M60;
            if (n.Contains("pkm") || n.Contains("pkp")) return Profile.PKM;
            if (n.Contains("mg42") || n.Contains("mg34") || n.Contains("mg3")) return Profile.MG42;
            if (n.Contains("rpk")) return Profile.RPK;
            if (n.Contains("dp28") || n.Contains("dp27")) return Profile.DP28;
            if (n.Contains("lewis")) return Profile.Lewis;
            return Profile.Generic;
        }
        internal static Mesh Model(Profile profile, int part = -1)
        {
            int key = (int)profile * 4 + part + 1;
            if (meshes.TryGetValue(key, out Mesh mesh)) return mesh;
            var b = new NativeScene.Builder();
            if (part < 0)
                for (int i = 0; i < 3; i++) BuildGeometry(b, profile, (NativeWeapons.WeaponPart)i);
            else BuildGeometry(b, profile, (NativeWeapons.WeaponPart)part);
            mesh = b.Create("RimFront " + profile + " " + part);
            meshes.Add(key, mesh);
            return mesh;
        }
        internal static void BuildGeometry(NativeScene.Builder b, Profile profile, NativeWeapons.WeaponPart part)
        {
            if (profile == Profile.Generic) { NativeWeapons.BuildPartGeometry(b, NativeWeapons.WeaponKind.Heavy, part); return; }
            bool pan = profile == Profile.DP28 || profile == Profile.Lewis;
            bool rpk = profile == Profile.RPK, mg = profile == Profile.MG42, lewis = profile == Profile.Lewis;
            bool wood = profile == Profile.PKM || profile == Profile.DP28 || lewis || rpk;
            if (part == NativeWeapons.WeaponPart.Feed)
            {
                if (pan) Disc(b, new Vector3(0, .22f, .23f), lewis ? .23f : .31f, .065f, Black);
                else if (rpk)
                {
                    Box(b, 0, -.17f, .26f, .10f, .29f, .14f, Black);
                    RotateBox(b, new Vector3(0, -.37f, .31f), new Vector3(.10f, .18f, .14f), Quaternion.Euler(-24, 0, 0), Black);
                }
                else
                {
                    Box(b, -.19f, -.12f, .20f, .24f, profile == Profile.M249 ? .25f : .30f, .28f, Green);
                    for (int i = 0; i < 6; i++)
                        Box(b, -.11f - .031f * i, .035f - .012f * i, .16f, .022f, .025f, .17f, Brass);
                }
                return;
            }
            if (part == NativeWeapons.WeaponPart.Bolt)
            { Box(b, .13f, .06f, .11f, .09f, .04f, .07f, Edge); return; }
            Box(b, 0, .04f, .19f, mg ? .17f : .21f, .18f, .46f, Steel);
            Box(b, 0, .145f, .18f, .17f, .035f, .38f, Black); // feed cover
            RotateBox(b, new Vector3(0, -.16f, -.025f), new Vector3(.10f, .28f, .13f), Quaternion.Euler(-12, 0, 0), Black);
            Box(b, 0, -.10f, .11f, .11f, .025f, .16f, Steel); // trigger guard
            float stockWidth = profile == Profile.M249 ? .08f : .12f;
            Box(b, 0, .035f, -.28f, stockWidth, .15f, .42f, wood ? Wood : Black);
            Box(b, 0, .02f, -.49f, .15f, .23f, .045f, Black);
            float jacket = lewis ? .18f : mg || profile == Profile.M1919 ? .115f : .055f;
            Box(b, 0, .06f, .76f, jacket, jacket, .75f, Steel);
            if (mg || profile == Profile.M1919 || lewis)
                for (int i = 0; i < 7; i++)
                {
                    Box(b, jacket * .51f, .06f, .48f + i * .085f, .008f, .045f, .035f, Black);
                    Box(b, -jacket * .51f, .06f, .48f + i * .085f, .008f, .045f, .035f, Black);
                    if (lewis) Box(b, 0, .16f, .48f + i * .085f, .19f, .02f, .025f, Edge);
                }
            else
            {
                Box(b, 0, -.015f, .60f, .045f, .045f, .47f, Edge); // gas tube
                Box(b, 0, .035f, .49f, .145f, .13f, profile == Profile.M60 ? .34f : .25f, wood ? Wood : Green);
            }
            if (profile == Profile.M60 || profile == Profile.M249 || profile == Profile.M240)
            {
                Box(b, .08f, .25f, .36f, .045f, .20f, .045f, Black);
                Box(b, 0, .34f, .36f, .20f, .045f, .045f, Black); // carrying handle
            }
            Box(b, 0, .20f, .01f, .09f, .065f, .045f, Black);
            Box(b, 0, .18f, 1.02f, .028f, .16f, .035f, Edge);
            Box(b, 0, .06f, 1.14f, mg ? .10f : .075f, mg ? .10f : .075f, .04f, Black);
        }
        internal static void BuildBipod(NativeScene.Builder b, bool deployed)
        {
            Box(b, 0, -.04f, .76f, .18f, .07f, .10f, Steel);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 center = deployed ? new Vector3(side * .15f, -.26f, .76f) : new Vector3(side * .10f, -.075f, .53f);
                Quaternion rotation = deployed ? Quaternion.Euler(0, 0, side * 24) : Quaternion.Euler(90, 0, 0);
                RotateBox(b, center, new Vector3(.035f, .44f, .035f), rotation, Black);
                if (deployed) Box(b, side * .24f, -.46f, .76f, .13f, .035f, .11f, Steel);
            }
        }
        static void Disc(NativeScene.Builder b, Vector3 center, float radius, float height, Color color)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6, c = (i + 1) * Mathf.PI / 6;
                Vector3 left = center + new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius),
                    right = center + new Vector3(Mathf.Sin(c) * radius, 0, Mathf.Cos(c) * radius), up = new Vector3(0, height, 0);
                b.Triangle(center + up, left + up, right + up, color);
                b.Triangle(center, right, left, color * .6f);
                b.Triangle(left, right, right + up, color * .8f);
                b.Triangle(left, right + up, left + up, color * .8f);
            }
        }
        static void Box(NativeScene.Builder b, float x, float y, float z, float w, float h, float d, Color color)
            => b.Box(new Vector3(x, y, z), new Vector3(w, h, d), color);
        static void RotateBox(NativeScene.Builder b, Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            Vector3 origin = b.Origin; Quaternion facing = b.Rotation;
            b.Origin = origin + facing * center; b.Rotation = facing * rotation;
            try { b.Box(Vector3.zero, size, color); }
            finally { b.Origin = origin; b.Rotation = facing; }
        }
        internal static void Dispose()
        {
            foreach (Mesh mesh in meshes.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            meshes.Clear();
        }
    }
}
