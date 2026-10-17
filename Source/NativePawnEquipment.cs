using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Cosmetic equipment only: ownership and protection remain entirely game-owned.
    internal static class NativePawnEquipment
    {
        internal const int MaximumMeshes = 32;
        static readonly Dictionary<ThingDef, int> definitions = new Dictionary<ThingDef, int>();
        static readonly Dictionary<int, ShieldMesh> meshes = new Dictionary<int, ShieldMesh>();
        static Material material;
        sealed class ShieldMesh { internal Mesh Mesh; internal int Frame; }

        internal static bool PhysicalDescriptor(string className, string extensionName, string defName, bool melee)
        {
            if (className == "CombatExtended.Apparel_Shield" || extensionName == "CombatExtended.ShieldDefExtension") return true;
            string name = (defName ?? "").ToLowerInvariant();
            return melee && !name.Contains("belt") && !name.Contains("energy") && !name.Contains("shieldpack")
                && (name.Contains("shield") || name.Contains("buckler"));
        }

        internal static bool IsPhysicalShield(Thing thing) => ShieldKind(thing) != 0;

        static int ShieldKind(Thing thing)
        {
            if (thing?.def == null) return 0;
            int kind;
            if (definitions.TryGetValue(thing.def, out kind)) return kind;
            bool physical = false, tall = false;
            for (Type type = thing.GetType(); type != null; type = type.BaseType)
                if (PhysicalDescriptor(type.FullName, null, null, false)) { physical = true; break; }
            if (thing.def.modExtensions != null)
                foreach (DefModExtension extension in thing.def.modExtensions)
                {
                    if (extension == null) continue;
                    Type type = extension.GetType();
                    for (Type current = type; current != null; current = current.BaseType)
                        if (PhysicalDescriptor(null, current.FullName, null, false))
                        {
                            physical = true;
                            FieldInfo field = type.GetField("drawAsTall", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (field?.FieldType == typeof(bool)) tall = (bool)field.GetValue(extension);
                            break;
                        }
                }
            physical |= PhysicalDescriptor(null, null, thing.def.defName, thing.def.IsMeleeWeapon);
            kind = physical ? (tall ? 2 : 1) : 0;
            if (definitions.Count >= 256) definitions.Clear();
            definitions[thing.def] = kind;
            return kind;
        }

        static Thing OwnedShield(Pawn pawn)
        {
            var worn = pawn?.apparel?.WornApparel;
            if (worn != null) for (int i = 0; i < worn.Count; i++) if (IsPhysicalShield(worn[i])) return worn[i];
            var equipped = pawn?.equipment?.AllEquipmentListForReading;
            if (equipped != null) for (int i = 0; i < equipped.Count; i++) if (IsPhysicalShield(equipped[i])) return equipped[i];
            return null;
        }

        internal static NativeWeapons.EquipmentHand HeldHand(Pawn pawn, NativePawnModels.Appearance appearance)
        {
            if (pawn == null || appearance == null || OwnedShield(pawn) == null) return NativeWeapons.EquipmentHand.None;
            Thing primary = pawn.equipment?.Primary;
            NativeWeapons.ResolveHands(appearance.MissingParts, primary != null && !IsPhysicalShield(primary), true,
                pawn.Drafted, out NativeWeapons.EquipmentHand weapon, out NativeWeapons.EquipmentHand shield);
            return shield;
        }
        internal static Vector3 FirstPersonShieldOffset(NativeWeapons.EquipmentHand hand)
            => new Vector3(hand == NativeWeapons.EquipmentHand.Right ? .32f : -.32f, -.35f, .48f);

        internal static void BuildShield(NativeScene.Builder builder, bool tall, Color color)
        {
            Color rim = new Color(.26f, .28f, .29f), grip = new Color(.27f, .20f, .13f);
            float height = tall ? 1.02f : .64f, width = tall ? .58f : .56f;
            builder.Box(Vector3.zero, new Vector3(width, height, .065f), rim);
            builder.Box(new Vector3(0, 0, .043f), new Vector3(width - .055f, height - .055f, .033f), color);
            builder.Box(new Vector3(0, 0, .069f), new Vector3(.12f, .14f, .035f), rim * 1.2f);
            builder.Box(new Vector3(-.12f, 0, -.06f), new Vector3(.045f, .22f, .055f), grip);
            builder.Box(new Vector3(.12f, 0, -.06f), new Vector3(.045f, .22f, .055f), grip);
            builder.Box(new Vector3(0, 0, -.094f), new Vector3(.28f, .045f, .035f), grip);
            if (tall) builder.Box(new Vector3(0, .28f, .064f), new Vector3(.29f, .055f, .012f), rim);
        }

        static Mesh GetMesh(Thing shield)
        {
            Color color = shield.DrawColor;
            bool tall = ShieldKind(shield) == 2;
            int key = (tall ? 1 << 24 : 0) | (Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255) << 16)
                | (Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255) << 8) | Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255);
            ShieldMesh entry;
            if (!meshes.TryGetValue(key, out entry))
            {
                if (meshes.Count >= MaximumMeshes)
                {
                    int oldest = int.MaxValue, remove = 0;
                    foreach (var pair in meshes) if (pair.Value.Frame < oldest) { oldest = pair.Value.Frame; remove = pair.Key; }
                    UnityEngine.Object.Destroy(meshes[remove].Mesh); meshes.Remove(remove);
                }
                NativeScene.Builder builder = new NativeScene.Builder(); BuildShield(builder, tall, color);
                entry = new ShieldMesh { Mesh = builder.Create("RimFront physical shield") }; meshes.Add(key, entry);
            }
            entry.Frame = Time.frameCount;
            if (material == null) material = new Material(ShaderDatabase.Cutout)
                { name = "RimFront shield", color = Color.white, mainTexture = Texture2D.whiteTexture };
            return entry.Mesh;
        }

        internal static void Draw(Pawn pawn, NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot state,
            Matrix4x4 root, bool articulated)
        {
            if (pawn == null || pawn.Dead || state.Lying || !pawn.RaceProps.Humanlike) return;
            Thing shield = OwnedShield(pawn); if (shield == null) return;
            Vector3 position; Quaternion rotation;
            ShieldPose(appearance, state, articulated, HeldHand(pawn, appearance), out position, out rotation);
            NativeLighting.Draw(GetMesh(shield), root * Matrix4x4.TRS(position, rotation, Vector3.one), material, pawn.DrawPos);
        }

        internal static void ShieldPose(NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot state,
            bool articulated, NativeWeapons.EquipmentHand hand, out Vector3 position, out Quaternion rotation)
        {
            if (hand != NativeWeapons.EquipmentHand.None
                && NativeWeapons.HandSocket(appearance, state, articulated, hand == NativeWeapons.EquipmentHand.Left, out position, out rotation))
            {
                // The wrist compensates for elbow pitch: the plate continues to face
                // forward while its attachment follows the animated hand.
                position += new Vector3(0, .08f, .10f);
                rotation = Quaternion.identity;
            }
            else
            {
                NativePawnRig.Pose pose = articulated ? NativePawnAnimation.Evaluate(appearance.Form, state, NativePawnRig.Part.Body) : default(NativePawnRig.Pose);
                rotation = Quaternion.Euler(pose.Euler);
                Vector3 pivot = NativePawnModels.RestPivot(appearance, NativePawnRig.Part.Body);
                position = pivot + pose.Offset + rotation * (new Vector3(0, 1.03f, -.62f) - pivot);
                rotation *= Quaternion.Euler(0, 180, 0);
            }
        }

        internal static void DrawFirstPerson(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.RaceProps.Humanlike) return;
            Thing shield = OwnedShield(pawn); if (shield == null) return;
            NativePawnModels.Appearance appearance = NativePawns.Appearance(pawn);
            NativeWeapons.EquipmentHand hand = HeldHand(pawn, appearance);
            if (hand == NativeWeapons.EquipmentHand.None) return;
            Quaternion camera = Quaternion.Euler(NativeView.PitchDegrees, NativeView.Yaw * Mathf.Rad2Deg, 0);
            Vector3 position = NativeView.RenderPosition + camera * FirstPersonShieldOffset(hand);
            NativeLighting.Draw(GetMesh(shield), Matrix4x4.TRS(position, camera, Vector3.one * .72f), material, pawn.DrawPos);
        }

        internal static void Dispose()
        {
            foreach (ShieldMesh entry in meshes.Values) if (entry.Mesh != null) UnityEngine.Object.Destroy(entry.Mesh);
            meshes.Clear(); definitions.Clear();
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
        }
    }
}
