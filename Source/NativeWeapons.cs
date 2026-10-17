using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFront
{
    /// <summary>Original weapon geometry, rendered without changing equipment, ammo, or aim.</summary>
    internal static class NativeWeapons
    {
        internal enum WeaponKind { Pistol, Revolver, Rifle, Shotgun, Smg, Sniper, Heavy, Launcher, Bow, Blade, Blunt, Grenade }
        internal enum WeaponPart { Body, Feed, Bolt }
        internal enum CarryPlacement { Held, Back, Hip }
        internal enum EquipmentHand { None, Right, Left }
        internal const int MaximumWeaponMeshes = 12;
        internal const int MaximumPartMeshes = MaximumWeaponMeshes * 3;
        internal const int MaximumHandMeshes = 4;
        internal const int MaximumClassificationEntries = 256;
        static readonly Dictionary<WeaponKind, Mesh> models = new Dictionary<WeaponKind, Mesh>();
        static readonly Dictionary<int, Mesh> parts = new Dictionary<int, Mesh>();
        static readonly Dictionary<ThingDef, WeaponKind> kinds = new Dictionary<ThingDef, WeaponKind>();
        static readonly bool CEHandedness = AccessTools.TypeByName("CombatExtended.Apparel_Shield") != null;
        static readonly Dictionary<int, HeldObservation> held = new Dictionary<int, HeldObservation>();
        struct HeldObservation
        {
            internal Pawn Pawn;
            internal NativePawnModels.Appearance Appearance;
            internal bool Articulated;
            internal int Frame;
        }
        static Material material;
        static readonly Mesh[] hands = new Mesh[MaximumHandMeshes];
        static Color handSkin, handSleeve;
        static bool handPaletteReady;
        static NativePawnModels.Appearance handAppearance;
        static readonly Color Steel = new Color(.22f, .26f, .25f);
        static readonly Color Edge = new Color(.37f, .42f, .40f);
        static readonly Color Black = new Color(.10f, .13f, .12f);
        static readonly Color Wood = new Color(.49f, .31f, .16f);
        static readonly Color Green = new Color(.38f, .45f, .27f);

        internal static WeaponKind ClassifyName(string name, bool ranged)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (!ranged) return n.Contains("knife") || n.Contains("sword") || n.Contains("blade") || n.Contains("spear") ? WeaponKind.Blade : WeaponKind.Blunt;
            if (n.Contains("launcher") || n.Contains("rocket") || n.Contains("rpg")) return WeaponKind.Launcher;
            if (n.Contains("grenade")) return WeaponKind.Grenade;
            if (n.Contains("bow")) return WeaponKind.Bow;
            if (n.Contains("revolver")) return WeaponKind.Revolver;
            if (n.Contains("shotgun") || n.Contains("buckshot") || n.Contains("mossberg")) return WeaponKind.Shotgun;
            if (n.Contains("sniper") || n.Contains("marksman") || n.Contains("boltaction") || n.Contains("bolt_action")) return WeaponKind.Sniper;
            if (n.Contains("smg") || n.Contains("submachine") || n.Contains("uzi") || n.Contains("mp5")) return WeaponKind.Smg;
            if (n.Contains("minigun") || n.Contains("machinegun") || n.Contains("lmg") || n.Contains("hmg") || n.Contains("autocannon")) return WeaponKind.Heavy;
            if (n.Contains("pistol") || n.Contains("handgun") || n.Contains("glock") || n.Contains("deserteagle")) return WeaponKind.Pistol;
            // Modded or future ranged definitions remain visibly equipped without guessing their mechanics.
            return WeaponKind.Rifle;
        }

        static WeaponKind Kind(Thing weapon)
        {
            WeaponKind kind;
            if (kinds.TryGetValue(weapon.def, out kind)) return kind;
            kind = ClassifyName(weapon.def.defName, weapon.def.IsRangedWeapon);
            if (kinds.Count >= MaximumClassificationEntries) kinds.Clear();
            kinds.Add(weapon.def, kind);
            return kind;
        }

        static void Initialize()
        {
            if (material != null) return;
            material = new Material(ShaderDatabase.Cutout) { name = "RimFront native weapons", color = Color.white, mainTexture = Texture2D.whiteTexture };
        }

        static Mesh Model(WeaponKind kind)
        {
            Mesh mesh;
            if (models.TryGetValue(kind, out mesh)) return mesh;
            NativeScene.Builder builder = new NativeScene.Builder();
            BuildGeometry(builder, kind);
            mesh = builder.Create("RimFront " + kind);
            models.Add(kind, mesh);
            return mesh;
        }

        static Mesh Part(WeaponKind kind, WeaponPart part)
        {
            int key = (int)kind * 3 + (int)part;
            Mesh mesh;
            if (parts.TryGetValue(key, out mesh)) return mesh;
            NativeScene.Builder builder = new NativeScene.Builder();
            BuildPartGeometry(builder, kind, part);
            mesh = builder.Create("RimFront " + kind + " " + part);
            parts.Add(key, mesh);
            return mesh;
        }

        static Mesh Hand(bool compact, bool left)
        {
            EquipmentHand gripHand = WeaponHand(handAppearance);
            if (gripHand == EquipmentHand.None) return null;
            // 'left' is the supporting role in this viewmodel. With no right arm,
            // the left arm takes the grip and the absent right support stays hidden.
            EquipmentHand physicalHand = ViewHand(handAppearance, left);
            if (physicalHand == EquipmentHand.None) return null;
            bool physicalLeft = physicalHand == EquipmentHand.Left;
            int limb = 1 << (int)(physicalLeft ? NativePawnRig.Part.LeftArm : NativePawnRig.Part.RightArm);
            if (handAppearance != null && (handAppearance.MissingParts & limb) != 0) return null;
            int key = (compact ? 0 : 2) + (left ? 1 : 0);
            Mesh mesh = hands[key];
            if (mesh != null) return mesh;
            NativeScene.Builder b = new NativeScene.Builder();
            Color skin = handSkin, sleeve = handSleeve;
            float side = gripHand == EquipmentHand.Left ? -1f : 1f;
            bool plated = false;
            if (handAppearance != null)
            {
                if ((handAppearance.ProstheticParts & limb) != 0) skin = Steel;
                foreach (NativePawnModels.ApparelPiece piece in handAppearance.Apparel)
                {
                    if ((piece.Coverage & limb) == 0) continue;
                    if (piece.Kind == NativePawnModels.ApparelKind.Gloves) skin = piece.Color;
                    if (piece.Kind == NativePawnModels.ApparelKind.Plate) plated = true;
                }
            }
            if (!left)
            {
                b.Box(new Vector3(side * .035f, -.20f, -.02f), new Vector3(.15f, .17f, .16f), skin);
                RotatedBox(b, new Vector3(side * .10f, -.36f, -.17f), new Vector3(.18f, .20f, .36f), Quaternion.Euler(-30, side * -15, 0), sleeve);
                if (plated) RotatedBox(b, new Vector3(side * .10f, -.29f, -.19f), new Vector3(.20f, .10f, .31f), Quaternion.Euler(-30, side * -15, 0), sleeve * .8f);
            }
            else
            {
                b.Box(new Vector3(side * -.045f, -.10f, compact ? .08f : .43f), new Vector3(.15f, .14f, .16f), skin);
                RotatedBox(b, new Vector3(side * -.16f, -.26f, compact ? -.04f : .30f), new Vector3(.17f, .22f, .35f), Quaternion.Euler(-35, side * 20, 0), sleeve);
                if (plated) RotatedBox(b, new Vector3(side * -.16f, -.19f, compact ? -.06f : .28f), new Vector3(.20f, .10f, .31f), Quaternion.Euler(-35, side * 20, 0), sleeve * .8f);
            }
            mesh = b.Create("RimFront " + (compact ? "compact" : "long") + (left ? " left" : " right") + " hand");
            hands[key] = mesh;
            return mesh;
        }

        /// <summary>Called only after the engine actually launches a projectile. Pellet launches
        /// set the same timestamp instead of multiplying recoil. This never changes camera aim.</summary>
        internal static void NotifyShot(Pawn shooter)
        {
            if (!NativeView.Active || shooter == null || shooter.Map != NativeView.ControlledPawn.Map) return;
            NativePawnAnimation.NotifyShot(shooter);
        }

        static float Recoil(Pawn pawn)
        {
            return NativePawnAnimation.Sample(pawn).Recoil;
        }

        static void UpdateHands(Pawn pawn)
        {
            NativePawnModels.Appearance appearance = NativePawns.Appearance(pawn);
            if (handPaletteReady && handAppearance.StableHash == appearance.StableHash) return;
            handAppearance = appearance;
            handSkin = appearance.Skin; handSleeve = appearance.SleeveColor; handPaletteReady = true;
            for (int i = 0; i < hands.Length; i++) { if (hands[i] != null) UnityEngine.Object.Destroy(hands[i]); hands[i] = null; }
        }

        internal static void DrawFirstPerson(Pawn pawn)
        {
            Thing weapon = pawn?.equipment?.Primary;
            if (!NativeView.Active || pawn != NativeView.ControlledPawn) return;
            if (weapon != null && NativePawnEquipment.IsPhysicalShield(weapon)) return;
            Initialize();
            UpdateHands(pawn);
            EquipmentHand gripHand = WeaponHand(handAppearance);
            if (gripHand == EquipmentHand.None) return;
            EquipmentHand shieldHand = NativePawnEquipment.HeldHand(pawn, handAppearance);
            if (weapon == null)
            {
                Quaternion facing = Quaternion.Euler(NativeView.PitchDegrees, NativeView.Yaw * Mathf.Rad2Deg, 0);
                Vector3 origin = NativeView.RenderPosition + facing * new Vector3(gripHand == EquipmentHand.Left ? -.18f : .18f, -.10f, .43f);
                NativePawnRig.Pose strike = NativePawnAnimation.WeaponPose(NativePawnAnimation.Sample(pawn), true);
                Matrix4x4 guard = Matrix4x4.TRS(origin, facing, Vector3.one * .58f);
                if (DrawViewHand(handAppearance, false, shieldHand)) NativeLighting.Draw(Hand(true, false), guard * Matrix4x4.TRS(strike.Offset, Quaternion.Euler(strike.Euler), Vector3.one), material, pawn.DrawPos);
                if (DrawViewHand(handAppearance, true, shieldHand)) NativeLighting.Draw(Hand(true, true), guard * Matrix4x4.Translate(new Vector3(gripHand == EquipmentHand.Left ? .28f : -.28f, 0, .12f)), material, pawn.DrawPos);
                return;
            }
            WeaponKind kind = Kind(weapon);
            Vector3 position; Quaternion rotation; float scale;
            FirstPersonPose(pawn, kind, out position, out rotation, out scale);
            NativeReloadAnimation.Snapshot reload = NativeReloadAnimation.Sample(pawn);
            NativeReloadAnimation.Pose animation = NativeReloadAnimation.ForSnapshot(kind, reload);
            ApplyReload(ref position, ref rotation, scale, animation);
            Matrix4x4 transform = Matrix4x4.TRS(position, rotation, new Vector3(scale, scale, scale));
            DrawModel(kind, transform, pawn.DrawPos, reload, animation);
            NativeLighting.Draw(Hand(Compact(kind), false), transform, material, pawn.DrawPos);
            if (DrawViewHand(handAppearance, true, shieldHand)) NativeLighting.Draw(Hand(Compact(kind), true), transform * Matrix4x4.TRS(animation.LeftHandOffset, Quaternion.Euler(animation.LeftHandEuler), Vector3.one), material, pawn.DrawPos);
        }

        internal static void DrawHeld(Pawn pawn, Vector3 worldPosition, float yawDegrees,
            NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot state, bool articulated)
        {
            Thing weapon = pawn?.equipment?.Primary;
            if (weapon == null || pawn.Dead || pawn.Downed || !pawn.RaceProps.Humanlike) return;
            if (NativePawnEquipment.IsPhysicalShield(weapon)) return;
            Initialize();
            WeaponKind kind = Kind(weapon);
            if (held.Count >= MaximumClassificationEntries && !held.ContainsKey(pawn.thingIDNumber)) held.Clear();
            held[pawn.thingIDNumber] = new HeldObservation { Pawn = pawn, Appearance = appearance,
                Articulated = articulated, Frame = Time.frameCount };
            CarryPlacement placement = Placement(pawn.Drafted, TwoHanded(weapon, kind));
            if (!AttachmentPose(appearance, state, articulated, kind, placement, out Vector3 local, out Quaternion localRotation)) return;
            float pawnScale = NativeScene.PawnScale(pawn), scale = pawnScale * .78f;
            Quaternion facing = Quaternion.Euler(0, yawDegrees, 0);
            Vector3 position = worldPosition + facing * (local * pawnScale);
            Quaternion rotation = facing * localRotation;
            // A stowed weapon remains stowed even when its owner has a native combat job.
            NativeReloadAnimation.Snapshot reload = placement == CarryPlacement.Held ? state.Reload : default;
            NativeReloadAnimation.Pose animation = NativeReloadAnimation.ForSnapshot(kind, reload);
            DrawModel(kind, Matrix4x4.TRS(position, rotation, new Vector3(scale, scale, scale)), worldPosition, reload, animation);
        }

        static bool TwoHanded(Thing weapon, WeaponKind kind) => IsTwoHanded(kind, weapon.def.defName,
            CEHandedness, weapon.def.weaponTags?.Contains("CE_OneHandedWeapon") == true);

        internal static bool IsTwoHanded(WeaponKind kind, string defName, bool ceRules, bool oneHandedTag)
        {
            // This is CE's own shield-compatibility contract, including patched mod weapons.
            if (oneHandedTag) return false;
            if (ceRules) return true;
            string name = (defName ?? "").ToLowerInvariant();
            if (name.Contains("twohand") || name.Contains("two_hand") || name.Contains("greatsword")
                || name.Contains("halberd") || name.Contains("spear") || name.Contains("pike")
                || name.Contains("greatclub") || name.Contains("warhammer")) return true;
            return kind == WeaponKind.Rifle || kind == WeaponKind.Shotgun || kind == WeaponKind.Smg
                || kind == WeaponKind.Sniper || kind == WeaponKind.Heavy || kind == WeaponKind.Launcher || kind == WeaponKind.Bow;
        }

        internal static CarryPlacement Placement(bool drafted, bool twoHanded) => drafted
            ? CarryPlacement.Held : twoHanded ? CarryPlacement.Back : CarryPlacement.Hip;

        internal static EquipmentHand OtherHand(EquipmentHand hand) => hand == EquipmentHand.Left
            ? EquipmentHand.Right : hand == EquipmentHand.Right ? EquipmentHand.Left : EquipmentHand.None;
        internal static EquipmentHand WeaponHand(NativePawnModels.Appearance appearance)
        {
            if (appearance == null) return EquipmentHand.None;
            ResolveHands(appearance.MissingParts, true, false, true, out EquipmentHand weapon, out EquipmentHand shield);
            return weapon;
        }
        internal static EquipmentHand ViewHand(NativePawnModels.Appearance appearance, bool supporting)
        {
            EquipmentHand hand = WeaponHand(appearance);
            if (supporting) hand = OtherHand(hand);
            if (hand == EquipmentHand.None) return hand;
            int part = (int)(hand == EquipmentHand.Left ? NativePawnRig.Part.LeftArm : NativePawnRig.Part.RightArm);
            return (appearance.MissingParts & (1 << part)) == 0 ? hand : EquipmentHand.None;
        }
        internal static bool DrawViewHand(NativePawnModels.Appearance appearance, bool supporting, EquipmentHand shield)
        {
            EquipmentHand hand = ViewHand(appearance, supporting);
            return hand != EquipmentHand.None && hand != shield;
        }
        internal static void ResolveHands(int missingParts, bool weaponPresent, bool shieldPresent, bool drafted,
            out EquipmentHand weapon, out EquipmentHand shield)
        {
            weapon = shield = EquipmentHand.None;
            if (!drafted) return;
            bool right = (missingParts & (1 << (int)NativePawnRig.Part.RightArm)) == 0;
            bool left = (missingParts & (1 << (int)NativePawnRig.Part.LeftArm)) == 0;
            if (weaponPresent) weapon = right ? EquipmentHand.Right : left ? EquipmentHand.Left : EquipmentHand.None;
            if (shieldPresent) shield = left && weapon != EquipmentHand.Left ? EquipmentHand.Left
                : right && weapon != EquipmentHand.Right ? EquipmentHand.Right : EquipmentHand.None;
        }
        internal static Vector3 FirstPersonOffset(EquipmentHand hand, float recoil) => new Vector3(
            hand == EquipmentHand.Left ? -.22f : .22f, -.28f, .43f - recoil * .07f);

        internal static Vector3 GripLocal(WeaponKind kind)
        {
            if (kind == WeaponKind.Blade || kind == WeaponKind.Blunt) return new Vector3(0, 0, -.12f);
            if (kind == WeaponKind.Bow) return new Vector3(0, 0, .13f);
            if (kind == WeaponKind.Grenade) return Vector3.zero;
            return new Vector3(0, -.16f, -.025f);
        }

        // The palm center and shoulder pivot are the same geometry used by NativePawnModels.
        // No independent weapon translation may move this grip away from the drawn hand.
        internal static bool HandSocket(NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot state,
            bool articulated, bool left, out Vector3 position, out Quaternion rotation)
        {
            NativePawnRig.Part part = left ? NativePawnRig.Part.LeftArm : NativePawnRig.Part.RightArm;
            position = Vector3.zero; rotation = Quaternion.identity;
            if (appearance == null || (appearance.MissingParts & (1 << (int)part)) != 0) return false;
            Vector3 pivot = NativePawnModels.RestPivot(appearance, part);
            NativePawnRig.Pose pose = articulated ? NativePawnAnimation.Evaluate(appearance.Form, state, part) : default;
            rotation = Quaternion.Euler(pose.Euler);
            Vector3 palm = new Vector3(left ? -.275f : .275f, .78f, .025f);
            position = pivot + pose.Offset + rotation * (palm - pivot);
            return true;
        }

        internal static bool AttachmentPose(NativePawnModels.Appearance appearance, NativePawnAnimation.Snapshot state,
            bool articulated, WeaponKind kind, CarryPlacement placement, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero; rotation = Quaternion.identity;
            if (appearance == null) return false;
            Vector3 grip;
            if (placement == CarryPlacement.Held)
            {
                EquipmentHand hand = WeaponHand(appearance);
                if (hand == EquipmentHand.None || !HandSocket(appearance, state, articulated,
                    hand == EquipmentHand.Left, out grip, out rotation)) return false;
                rotation *= Quaternion.Euler(68, 0, 0);
                // Aim direction is independent of the arm pose (and of whether the
                // body is articulated at this distance). Re-anchor the grip below.
                if (state.Aiming && !state.Reloading && state.Melee <= 0
                    && kind != WeaponKind.Blade && kind != WeaponKind.Blunt && kind != WeaponKind.Grenade)
                {
                    float pitch = float.IsNaN(state.AimPitch) || float.IsInfinity(state.AimPitch) ? 0 : Mathf.Clamp(state.AimPitch, -70, 70);
                    rotation = Quaternion.Euler(pitch, 0, 0);
                }
                // Rotate the model about the attached grip; never apply the first-person
                // independent action/reload translation to a third-person hand socket.
                if (state.Reloading) rotation *= Quaternion.Euler(NativeReloadAnimation.ForSnapshot(kind, state.Reload).WeaponEuler);
            }
            else
            {
                bool back = placement == CarryPlacement.Back;
                grip = back ? new Vector3(.12f, 1.02f, -.25f)
                    : new Vector3(kind == WeaponKind.Blade ? -.32f : .32f, kind == WeaponKind.Blade || kind == WeaponKind.Blunt ? .88f : .72f, -.035f);
                rotation = back ? Quaternion.Euler(-75, 0, -15) : Quaternion.Euler(90, 0, 0);
                if (articulated)
                {
                    Vector3 pivot = NativePawnModels.RestPivot(appearance, NativePawnRig.Part.Body);
                    NativePawnRig.Pose body = NativePawnAnimation.Evaluate(appearance.Form, state, NativePawnRig.Part.Body);
                    Quaternion bodyRotation = Quaternion.Euler(body.Euler);
                    grip = pivot + body.Offset + bodyRotation * (grip - pivot);
                    rotation = bodyRotation * rotation;
                }
            }
            position = grip - rotation * (GripLocal(kind) * .78f);
            return true;
        }

        internal static bool TryGetMuzzle(Pawn pawn, out Vector3 position, out Vector3 direction)
        {
            position = Vector3.zero; direction = Vector3.forward;
            Thing weapon = pawn?.equipment?.Primary;
            if (weapon == null || !weapon.def.IsRangedWeapon || !pawn.Spawned) return false;
            WeaponKind kind = Kind(weapon);
            if (kind == WeaponKind.Grenade) return false;
            // Do not emit a false flash from a holster/back mount during native AI attacks.
            if (!pawn.Drafted) return false;
            Vector3 origin; Quaternion rotation; float scale;
            bool firstPerson = NativeView.Active && pawn == NativeView.ControlledPawn;
            if (firstPerson)
            {
                if (WeaponHand(NativePawns.Appearance(pawn)) == EquipmentHand.None) return false;
                FirstPersonPose(pawn, kind, out origin, out rotation, out scale);
                ApplyReload(ref origin, ref rotation, scale, NativeReloadAnimation.ForSnapshot(kind, NativeReloadAnimation.Sample(pawn)));
            }
            else
            {
                NativePawnAnimation.Snapshot state = NativePawnAnimation.Sample(pawn);
                bool observed = held.TryGetValue(pawn.thingIDNumber, out HeldObservation recent)
                    && recent.Pawn == pawn && Time.frameCount - recent.Frame <= 2;
                NativePawnModels.Appearance appearance = observed ? recent.Appearance : NativePawns.Appearance(pawn);
                if (!AttachmentPose(appearance, state, observed && recent.Articulated, kind, CarryPlacement.Held,
                    out Vector3 local, out Quaternion localRotation)) return false;
                float pawnScale = NativeScene.PawnScale(pawn);
                Quaternion facing = Quaternion.Euler(0, state.FacingYaw, 0);
                Vector3 root = pawn.DrawPos; root.y = .04f;
                origin = root + facing * (local * pawnScale);
                rotation = facing * localRotation; scale = pawnScale * .78f;
            }
            position = origin + rotation * (MuzzleLocal(kind) * scale);
            direction = rotation * Vector3.forward;
            return true;
        }

        static void FirstPersonPose(Pawn pawn, WeaponKind kind, out Vector3 position, out Quaternion rotation, out float scale)
        {
            float recoil = Recoil(pawn);
            Quaternion camera = Quaternion.Euler(NativeView.PitchDegrees, NativeView.Yaw * Mathf.Rad2Deg, 0);
            // Use the actual eye transform, including camera interpolation.
            position = NativeView.RenderPosition;
            // Kept in world depth: walls can occlude the viewmodel instead of drawing through them.
            position += camera * FirstPersonOffset(WeaponHand(NativePawns.Appearance(pawn)), recoil);
            rotation = camera * Quaternion.Euler(-recoil * (Compact(kind) ? 8f : 4.5f), -3f, recoil * 1.5f);
            scale = .58f;
            ApplyAction(pawn, true, ref position, ref rotation, scale);
        }

        static void ApplyAction(Pawn pawn, bool firstPerson, ref Vector3 position, ref Quaternion rotation, float scale)
        {
            NativePawnRig.Pose pose = NativePawnAnimation.WeaponPose(NativePawnAnimation.Sample(pawn), firstPerson);
            position += rotation * (pose.Offset * scale);
            rotation *= Quaternion.Euler(pose.Euler);
        }

        static bool Compact(WeaponKind kind) { return kind == WeaponKind.Pistol || kind == WeaponKind.Revolver || kind == WeaponKind.Grenade; }

        static void ApplyReload(ref Vector3 position, ref Quaternion rotation, float scale, NativeReloadAnimation.Pose pose)
        {
            position += rotation * (pose.WeaponOffset * scale);
            rotation *= Quaternion.Euler(pose.WeaponEuler);
        }

        static void DrawModel(WeaponKind kind, Matrix4x4 transform, Vector3 sample, NativeReloadAnimation.Snapshot reload, NativeReloadAnimation.Pose animation)
        {
            if (!reload.Active || !reload.HasTiming)
            { NativeLighting.Draw(Model(kind), transform, material, sample); return; }
            NativeLighting.Draw(Part(kind, WeaponPart.Body), transform, material, sample);
            bool looseRound = reload.SingleRound && kind != WeaponKind.Revolver;
            if (looseRound && kind != WeaponKind.Shotgun)
                NativeLighting.Draw(Part(kind, WeaponPart.Feed), transform, material, sample);
            if (animation.ShowFeed)
                NativeLighting.Draw(Part(looseRound ? WeaponKind.Shotgun : kind, WeaponPart.Feed), transform * Matrix4x4.TRS(animation.FeedOffset, Quaternion.Euler(animation.FeedEuler), Vector3.one), material, sample);
            NativeLighting.Draw(Part(kind, WeaponPart.Bolt), transform * Matrix4x4.TRS(animation.BoltOffset, Quaternion.identity, Vector3.one), material, sample);
        }

        internal static Vector3 MuzzleLocal(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Pistol: return new Vector3(0, .07f, .46f);
                case WeaponKind.Revolver: return new Vector3(0, .09f, .63f);
                case WeaponKind.Shotgun: return new Vector3(0, .08f, 1.02f);
                case WeaponKind.Smg: return new Vector3(0, .07f, .67f);
                case WeaponKind.Sniper: return new Vector3(0, .08f, 1.27f);
                case WeaponKind.Heavy: return new Vector3(0, .06f, 1.16f);
                case WeaponKind.Launcher: return new Vector3(0, .16f, .98f);
                case WeaponKind.Bow: return new Vector3(0, .02f, .62f);
                default: return new Vector3(0, .07f, .96f);
            }
        }

        internal static void AddDropped(NativeScene.Builder builder, Thing weapon, Vector3 worldPosition, Quaternion rotation)
        {
            if (weapon == null || !weapon.def.IsWeapon) return;
            Vector3 oldOrigin = builder.Origin; Quaternion oldRotation = builder.Rotation;
            WeaponKind kind = Kind(weapon);
            worldPosition.y += kind == WeaponKind.Bow ? .56f : .17f;
            builder.Origin = worldPosition;
            builder.Rotation = rotation * Quaternion.Euler(0, 0, 90);
            try { BuildGeometry(builder, kind); }
            finally { builder.Origin = oldOrigin; builder.Rotation = oldRotation; }
        }

        internal static void BuildGeometry(NativeScene.Builder b, WeaponKind kind)
        {
            BuildPartGeometry(b, kind, WeaponPart.Body);
            // A loose shotgun round is only drawn while the real reload cycle is inserting it.
            if (kind != WeaponKind.Shotgun) BuildPartGeometry(b, kind, WeaponPart.Feed);
            BuildPartGeometry(b, kind, WeaponPart.Bolt);
        }

        internal static void BuildPartGeometry(NativeScene.Builder b, WeaponKind kind, WeaponPart part)
        {
            if (part == WeaponPart.Body) { BuildBodyGeometry(b, kind); return; }
            if (part == WeaponPart.Feed)
            {
                switch (kind)
                {
                    case WeaponKind.Pistol:
                        b.Box(new Vector3(0, -.16f, -.025f), new Vector3(.07f, .25f, .10f), Edge);
                        b.Box(new Vector3(0, -.30f, -.025f), new Vector3(.11f, .025f, .14f), Black); break;
                    case WeaponKind.Revolver:
                        OctagonalBarrel(b, new Vector3(0, .055f, .14f), .105f, .20f, Edge); break;
                    case WeaponKind.Rifle: case WeaponKind.Sniper: case WeaponKind.Smg:
                        b.Box(new Vector3(0, -.17f, .26f), new Vector3(.10f, kind == WeaponKind.Smg ? .31f : .27f, .12f), Black); break;
                    case WeaponKind.Heavy:
                        b.Box(new Vector3(-.17f, -.13f, .17f), new Vector3(.20f, .27f, .27f), Green); break;
                    case WeaponKind.Shotgun:
                        b.Box(new Vector3(0, -.07f, .22f), new Vector3(.045f, .045f, .13f), new Color(.64f, .20f, .13f));
                        b.Box(new Vector3(0, -.07f, .15f), new Vector3(.047f, .047f, .025f), new Color(.77f, .58f, .24f)); break;
                    case WeaponKind.Launcher:
                        b.Box(new Vector3(0, .16f, .28f), new Vector3(.13f, .13f, .57f), Green * 1.2f); break;
                }
                return;
            }
            if (kind == WeaponKind.Pistol)
            {
                b.Box(new Vector3(0, .055f, .18f), new Vector3(.12f, .13f, .49f), Edge);
                Sights(b, .025f, .39f, .15f);
            }
            else if (kind == WeaponKind.Shotgun)
                b.Box(new Vector3(0, .045f, .49f), new Vector3(.145f, .13f, .34f), Wood);
            else if (kind == WeaponKind.Rifle || kind == WeaponKind.Sniper || kind == WeaponKind.Smg || kind == WeaponKind.Heavy)
                b.Box(new Vector3(.095f, .08f, .10f), new Vector3(.085f, .04f, .065f), Edge);
        }

        static void BuildBodyGeometry(NativeScene.Builder b, WeaponKind kind)
        {
            if (kind == WeaponKind.Bow)
            {
                b.Box(new Vector3(0, 0, .13f), new Vector3(.055f, .32f, .07f), Wood);
                for (int side = -1; side <= 1; side += 2)
                {
                    RotatedBox(b, new Vector3(side * .27f, 0, .09f), new Vector3(.48f, .06f, .06f), Quaternion.Euler(0, side * 20, 0), Wood);
                    RotatedBox(b, new Vector3(side * .48f, 0, -.02f), new Vector3(.16f, .05f, .05f), Quaternion.Euler(0, side * 45, 0), Wood);
                }
                b.Box(new Vector3(0, 0, -.075f), new Vector3(1.05f, .009f, .009f), new Color(.76f, .72f, .58f));
                b.Box(new Vector3(0, .02f, .24f), new Vector3(.016f, .016f, .76f), Wood * 1.3f);
                return;
            }
            if (kind == WeaponKind.Blade || kind == WeaponKind.Blunt)
            {
                b.Box(new Vector3(0, 0, -.12f), new Vector3(.09f, .09f, .34f), Wood);
                b.Box(new Vector3(0, 0, .08f), new Vector3(.30f, .055f, .055f), Edge);
                if (kind == WeaponKind.Blade)
                {
                    b.Box(new Vector3(0, 0, .46f), new Vector3(.10f, .025f, .72f), new Color(.63f, .69f, .66f));
                    b.Box(new Vector3(0, 0, .84f), new Vector3(.055f, .022f, .08f), Edge);
                }
                else b.Box(new Vector3(0, 0, .48f), new Vector3(.26f, .23f, .63f), Wood);
                return;
            }
            if (kind == WeaponKind.Grenade)
            {
                b.Box(Vector3.zero, new Vector3(.17f, .24f, .17f), Green);
                b.Box(new Vector3(0, .15f, 0), new Vector3(.07f, .07f, .07f), Edge);
                b.Box(new Vector3(.065f, .08f, 0), new Vector3(.035f, .24f, .05f), Steel);
                return;
            }
            // Every firearm has a readable trigger grip and front/rear sights.
            RotatedBox(b, new Vector3(0, -.16f, -.025f), new Vector3(.095f, .28f, .13f), Quaternion.Euler(-12, 0, 0), kind == WeaponKind.Revolver ? Wood : Black);
            b.Box(new Vector3(0, -.095f, .13f), new Vector3(.105f, .025f, .14f), Steel);
            b.Box(new Vector3(0, -.04f, .20f), new Vector3(.10f, .11f, .025f), Steel);
            if (kind == WeaponKind.Pistol)
            {
                b.Box(new Vector3(0, -.005f, .18f), new Vector3(.105f, .06f, .42f), Black);
                b.Box(new Vector3(0, .07f, .445f), new Vector3(.065f, .065f, .03f), Black);
                return;
            }
            if (kind == WeaponKind.Revolver)
            {
                b.Box(new Vector3(0, .09f, .43f), new Vector3(.075f, .09f, .40f), Steel);
                b.Box(new Vector3(0, .035f, .40f), new Vector3(.065f, .07f, .34f), Edge);
                b.Box(new Vector3(0, .15f, -.015f), new Vector3(.055f, .11f, .07f), Steel);
                Sights(b, .02f, .57f, .17f);
                return;
            }
            if (kind == WeaponKind.Launcher)
            {
                OctagonalBarrel(b, new Vector3(0, .16f, .25f), .16f, 1.44f, Green);
                OctagonalBarrel(b, new Vector3(0, .16f, .96f), .17f, .035f, Black);
                b.Box(new Vector3(.18f, .23f, .25f), new Vector3(.09f, .16f, .23f), Black);
                b.Box(new Vector3(0, -.045f, .45f), new Vector3(.10f, .25f, .13f), Black);
                return;
            }
            bool smg = kind == WeaponKind.Smg, shotgun = kind == WeaponKind.Shotgun, sniper = kind == WeaponKind.Sniper, heavy = kind == WeaponKind.Heavy;
            float length = smg ? .65f : sniper ? 1.25f : shotgun ? 1f : heavy ? 1.14f : .94f;
            b.Box(new Vector3(0, .055f, .19f), new Vector3(heavy ? .22f : .14f, heavy ? .21f : .16f, smg ? .32f : .43f), Steel);
            b.Box(new Vector3(0, .055f, smg ? -.19f : -.27f), new Vector3(.11f, .16f, smg ? .24f : .40f), shotgun || sniper ? Wood : Black);
            b.Box(new Vector3(0, .035f, smg ? -.33f : -.48f), new Vector3(.14f, .24f, .055f), Black);
            float foreCenter = smg ? .39f : .49f;
            if (!shotgun) b.Box(new Vector3(0, .045f, foreCenter), new Vector3(.145f, .13f, smg ? .20f : .34f), sniper ? Wood : Green);
            float barrelStart = foreCenter + (smg ? .09f : .15f);
            b.Box(new Vector3(0, .075f, (barrelStart + length) * .5f), new Vector3(shotgun ? .075f : .05f, shotgun ? .075f : .05f, length - barrelStart), Steel);
            if (shotgun)
                b.Box(new Vector3(0, -.005f, .64f), new Vector3(.065f, .065f, .58f), Edge);
            else if (heavy)
            {
                for (int side = -1; side <= 1; side += 2)
                    RotatedBox(b, new Vector3(side * .13f, -.13f, .72f), new Vector3(.035f, .38f, .035f), Quaternion.Euler(0, 0, side * 28), Black);
            }
            if (sniper)
            {
                b.Box(new Vector3(0, .20f, .20f), new Vector3(.06f, .13f, .22f), Black);
                OctagonalBarrel(b, new Vector3(0, .29f, .22f), .065f, .44f, Edge);
                b.Box(new Vector3(0, .29f, .447f), new Vector3(.09f, .09f, .012f), new Color(.27f, .49f, .46f));
            }
            else Sights(b, .08f, length - .10f, .18f);
            b.Box(new Vector3(0, .075f, length), new Vector3(.075f, .075f, .045f), Black);
        }

        static void Sights(NativeScene.Builder b, float rear, float front, float height)
        {
            b.Box(new Vector3(0, height, rear), new Vector3(.09f, .055f, .045f), Black);
            b.Box(new Vector3(0, height, front), new Vector3(.025f, .055f, .035f), Edge);
            b.Box(new Vector3(0, height + .03f, front), new Vector3(.018f, .008f, .015f), new Color(.68f, .79f, .41f));
        }

        static void OctagonalBarrel(NativeScene.Builder b, Vector3 center, float radius, float length, Color color)
        {
            // Two overlapping square prisms form an original eight-sided silhouette with no texture dependency.
            b.Box(center, new Vector3(radius * 1.5f, radius * 1.5f, length), color);
            RotatedBox(b, center, new Vector3(radius * 1.5f, radius * 1.5f, length), Quaternion.Euler(0, 0, 45), color);
        }

        static void RotatedBox(NativeScene.Builder b, Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            Vector3 oldOrigin = b.Origin; Quaternion oldRotation = b.Rotation;
            b.Origin = oldOrigin + oldRotation * center; b.Rotation = oldRotation * rotation;
            try { b.Box(Vector3.zero, size, color); }
            finally { b.Origin = oldOrigin; b.Rotation = oldRotation; }
        }

        internal static void Dispose()
        {
            foreach (Mesh mesh in models.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (Mesh mesh in parts.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            parts.Clear();
            models.Clear(); kinds.Clear();
            held.Clear();
            for (int i = 0; i < hands.Length; i++) { if (hands[i] != null) UnityEngine.Object.Destroy(hands[i]); hands[i] = null; }
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            handPaletteReady = false;
            handAppearance = null;
            NativeReloadAnimation.Dispose();
        }
    }
}
