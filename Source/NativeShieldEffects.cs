using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Read-only shield presentation. Native blocks and confirmed melee contacts create impacts.
    // Kept outside NativeScene's lifecycle so opt-in colony-view impacts survive FPS exit.
    internal static class NativeShieldEffects
    {
        internal const int MaximumImpacts = 48, MaximumHazePawns = 24, ImpactLifetime = 18;
        // One world tile is treated as one metre for this cosmetic scale. These are
        // total blue-to-red separations; each echo moves half the stated distance.
        internal const float MaximumHorizontalSeparation = .10f, MaximumVerticalSeparation = .02f;
        const int LatitudeSteps = 10, LongitudeSteps = 20;
        struct Impact
        {
            internal Pawn Pawn;
            internal Map Map;
            internal int Tick;
            internal Vector3 Direction;
            internal float Strength;
        }
        static readonly Impact[] impacts = new Impact[MaximumImpacts];
        static readonly List<Pawn> hazePawns = new List<Pawn>(MaximumHazePawns);
        static readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        static Map map;
        static Pawn hazeController;
        static int nextImpact, nextHazeTick;
        static Mesh sphere, ring;
        static Material material;
        static bool warned;

        internal static bool ShouldRecord(bool absorbed, bool nativeView, bool impactSetting, bool colonySetting)
            => absorbed && impactSetting && (nativeView || colonySetting);
        internal static float ImpactOpacity(int age) => age < 0 || age >= ImpactLifetime ? 0
            : (1f - age / (float)ImpactLifetime) * (1f - age / (float)ImpactLifetime);
        internal static float ImpactStrength(float damage) => !Finite(damage) ? .5f : Mathf.Clamp(.35f + damage / 45f, .35f, 1f);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        internal static Vector3 ImpactDirection(Vector3 target, Vector3 source, float incomingAngle)
        {
            Vector3 direction = source - target; direction.y = 0;
            if (Finite(direction) && direction.sqrMagnitude > .0001f) return direction.normalized;
            if (!Finite(incomingAngle) || incomingAngle < 0) return Vector3.forward;
            float angle = incomingAngle * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(angle), 0, -Mathf.Cos(angle));
        }

        internal static Vector3 GhostOffset(Vector3 direction, int age, bool red, float strength)
        {
            if (age < 0 || age >= ImpactLifetime) return Vector3.zero;
            direction.y = 0;
            if (!Finite(direction) || direction.sqrMagnitude < .0001f) direction = Vector3.forward;
            direction = direction.normalized;
            float force = Finite(strength) ? Mathf.Clamp01(strength) : 0;
            // Immediate impulse, then settle back toward the shield as it fades.
            float displacement = force * (1f - age / (float)ImpactLifetime) * (red ? 1f : -1f) * .5f;
            return (new Vector3(direction.z, 0, -direction.x) * MaximumHorizontalSeparation
                + Vector3.up * MaximumVerticalSeparation) * displacement;
        }

        static void EnsureMap(Map current)
        {
            if (map == current) return;
            Array.Clear(impacts, 0, impacts.Length);
            map = current; nextImpact = 0;
            BeginViewSession();
        }

        internal static void BeginViewSession()
        {
            // A takeover can happen without advancing a tick, even for the same pawn.
            // Invalidate only the spatial scan: colony impacts and GPU resources survive.
            hazePawns.Clear(); hazeController = null; nextHazeTick = 0;
        }

        internal static void EnsureHazeController(Pawn controlled)
        {
            if (ReferenceEquals(hazeController, controlled)) return;
            BeginViewSession();
            hazeController = controlled;
        }

        internal static void NotifyAbsorbed(Pawn pawn, DamageInfo info, bool absorbed)
        {
            if (!absorbed) return;
            Vector3 target = pawn?.DrawPos ?? Vector3.zero;
            Vector3 source = info.Instigator?.DrawPos ?? target;
            Record(pawn, ImpactDirection(target, source, info.Angle), info.Amount);
        }

        internal static void NotifyParry(Pawn pawn, Thing shield, Pawn attacker, float damage)
        {
            if (shield == null || !NativePawnEquipment.IsPhysicalShield(shield)) return;
            Vector3 target = pawn?.DrawPos ?? Vector3.zero;
            Record(pawn, ImpactDirection(target, attacker?.DrawPos ?? target, -1), damage);
        }

        internal static bool ShouldShowMeleeContact(bool activeShield, bool external, bool ranged, bool explosive, bool pawnSource, float damage)
            => activeShield && external && !ranged && !explosive && pawnSource && Finite(damage) && damage > 0;

        internal static void NotifyMeleeContact(Pawn pawn, DamageInfo info, float damage, bool hadActiveShield)
        {
            if (pawn == null || info.Def == null || !ShouldShowMeleeContact(hadActiveShield,
                info.Def.ExternalViolenceFor(pawn), info.Def.isRanged, info.Def.isExplosive, info.Instigator is Pawn, damage)) return;
            Vector3 target = pawn.DrawPos;
            Record(pawn, ImpactDirection(target, info.Instigator.DrawPos, info.Angle), info.Amount);
        }

        static void Record(Pawn pawn, Vector3 direction, float damage)
        {
            if (!ShouldRecord(true, NativeView.Active, RimFrontMod.Settings?.shieldImpacts ?? true,
                RimFrontMod.Settings?.shieldImpactsInColony ?? false)
                || Current.ProgramState != ProgramState.Playing || Find.TickManager == null
                || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Map != Find.CurrentMap
                || pawn.Position.Fogged(pawn.Map)) return;
            EnsureMap(pawn.Map);
            int tick = Find.TickManager.TicksGame;
            float strength = ImpactStrength(damage);
            // Pellets/secondary damage may report the same shield in one tick; merge them.
            for (int i = 0; i < impacts.Length; i++)
                if (impacts[i].Pawn == pawn && impacts[i].Tick == tick)
                { impacts[i].Strength = Mathf.Max(impacts[i].Strength, strength); return; }
            impacts[nextImpact] = new Impact { Pawn = pawn, Map = pawn.Map, Tick = tick, Direction = direction, Strength = strength };
            nextImpact = (nextImpact + 1) % MaximumImpacts;
        }

        static bool ActiveShield(ThingWithComps thing)
        {
            if (thing == null) return false;
            foreach (ThingComp comp in thing.AllComps)
                if (comp is CompShield shield && shield.ShieldState == ShieldState.Active) return true;
            return false;
        }
        internal static bool HasActiveShield(Pawn pawn)
        {
            if (pawn == null) return false;
            if (ActiveShield(pawn)) return true;
            var apparel = pawn.apparel?.WornApparel;
            if (apparel != null) for (int i = 0; i < apparel.Count; i++) if (ActiveShield(apparel[i])) return true;
            return false;
        }

        static bool Visible(Pawn pawn, Map current) => pawn != null && pawn.Spawned && !pawn.Dead
            && pawn.Map == current && !pawn.Position.Fogged(current);

        internal static void Draw3D(Map current, Pawn controlled)
        {
            if (current == null || controlled == null || !NativeView.Active || Find.TickManager == null) return;
            try
            {
                EnsureMap(current);
                EnsureHazeController(controlled);
                int tick = Find.TickManager.TicksGame;
                if (RimFrontMod.Settings?.shieldHaze ?? true)
                {
                    if (tick >= nextHazeTick)
                    {
                        hazePawns.Clear(); nextHazeTick = tick + 15;
                        if (Visible(controlled, current) && HasActiveShield(controlled)) hazePawns.Add(controlled);
                        foreach (Pawn pawn in current.mapPawns.AllPawnsSpawned)
                        {
                            if (hazePawns.Count >= MaximumHazePawns) break;
                            if (pawn != controlled && Visible(pawn, current) && pawn.Position.DistanceToSquared(controlled.Position) < NativeScene.ViewDistanceSquared
                                && HasActiveShield(pawn)) hazePawns.Add(pawn);
                        }
                    }
                    foreach (Pawn pawn in hazePawns)
                    {
                        if (!Visible(pawn, current) || !HasActiveShield(pawn)
                            || pawn.Position.DistanceToSquared(controlled.Position) >= NativeScene.ViewDistanceSquared) continue;
                        BubblePose(pawn, out Vector3 center, out Vector3 size);
                        Draw(sphere, center, size, new Color(.40f, .68f, 1f, .014f), true);
                    }
                }
                DrawImpacts(current, controlled, tick, false);
            }
            catch (Exception error) { Warn(error); }
        }

        internal static void Draw2D()
        {
            try
            {
                if (NativeView.Active || Current.ProgramState != ProgramState.Playing || Find.TickManager == null
                    || !WorldRendererUtility.DrawingMap || Find.CurrentMap == null) return;
                EnsureMap(Find.CurrentMap);
                if (!(RimFrontMod.Settings?.shieldImpactsInColony ?? false)) return;
                DrawImpacts(map, null, Find.TickManager.TicksGame, true);
            }
            catch (Exception error) { Warn(error); }
        }

        static void DrawImpacts(Map current, Pawn controlled, int tick, bool colony)
        {
            bool enabled = RimFrontMod.Settings?.shieldImpacts ?? true;
            for (int i = 0; i < impacts.Length; i++)
            {
                Impact impact = impacts[i];
                if (impact.Pawn == null) continue;
                int age = tick - impact.Tick;
                float opacity = ImpactOpacity(age);
                if (opacity <= 0 || !Visible(impact.Pawn, current) || impact.Map != current)
                { impacts[i] = default; continue; }
                if (!enabled) continue;
                if (colony)
                {
                    if (Find.CameraDriver == null || !Find.CameraDriver.CurrentViewRect.Contains(impact.Pawn.Position)) continue;
                }
                else if (impact.Pawn.Position.DistanceToSquared(controlled.Position) >= NativeScene.ViewDistanceSquared) continue;
                BubblePose(impact.Pawn, out Vector3 center, out Vector3 size);
                if (colony)
                {
                    center = impact.Pawn.DrawPos; center.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                    float scale = Mathf.Max(.7f, NativeScene.PawnScale(impact.Pawn));
                    size = new Vector3(scale, 1, scale);
                }
                float alpha = opacity * impact.Strength * (colony ? .55f : .24f);
                Mesh mesh = colony ? ring : sphere;
                Vector3 blue = GhostOffset(impact.Direction, age, false, impact.Strength);
                Vector3 red = GhostOffset(impact.Direction, age, true, impact.Strength);
                // Colony rings stay on their overhead plane; vertical ghost movement
                // is only meaningful for the perspective shield surface.
                if (colony) blue.y = red.y = 0;
                Draw(mesh, center + blue, size,
                    new Color(.18f, .55f, 1f, alpha), !colony);
                Draw(mesh, center + red, size,
                    new Color(1f, .16f, .21f, alpha * .70f), !colony);
            }
        }

        static void BubblePose(Pawn pawn, out Vector3 center, out Vector3 size)
        {
            float height = NativePawns.TargetHeight(pawn), width = Mathf.Max(.56f, NativeScene.GetPawnWidth(pawn) * .8f);
            center = NativePawns.TargetPosition(pawn); center.y = height * .5f;
            size = new Vector3(width, height * .55f + .10f, width);
        }

        static void Draw(Mesh requested, Vector3 center, Vector3 scale, Color color, bool native)
        {
            if (material == null) Initialize();
            // Callers can pass a null pre-initialization mesh; infer mode after initialization.
            Mesh mesh = requested ?? (native ? sphere : ring);
            properties.Clear(); properties.SetColor("_Color", color);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(center, Quaternion.identity, scale), material, 0, null, 0, properties);
            if (native) NativeScene.CountDraw();
        }

        static void Initialize()
        {
            Mesh newSphere = null, newRing = null;
            Material newMaterial = null;
            try
            {
                BuildSphereData(out Vector3[] vertices, out int[] triangles);
                newSphere = CreateMesh("RimFront shield sphere", vertices, triangles);
                BuildRingData(out vertices, out triangles);
                newRing = CreateMesh("RimFront shield ghost ring", vertices, triangles);
                newMaterial = new Material(ShaderDatabase.Transparent);
                newMaterial.name = "RimFront shield haze"; newMaterial.color = Color.white;
                newMaterial.mainTexture = Texture2D.whiteTexture; newMaterial.renderQueue = 3100;
                sphere = newSphere; ring = newRing; material = newMaterial;
            }
            catch
            {
                if (newSphere != null) UnityEngine.Object.Destroy(newSphere);
                if (newRing != null) UnityEngine.Object.Destroy(newRing);
                if (newMaterial != null) UnityEngine.Object.Destroy(newMaterial);
                throw;
            }
        }

        static Mesh CreateMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var mesh = new Mesh();
            try
            {
                mesh.name = name; mesh.vertices = vertices; mesh.triangles = triangles; mesh.colors = colors;
                mesh.RecalculateBounds(); return mesh;
            }
            catch { UnityEngine.Object.Destroy(mesh); throw; }
        }

        internal static void BuildSphereData(out Vector3[] vertices, out int[] triangles)
        {
            vertices = new Vector3[(LatitudeSteps + 1) * (LongitudeSteps + 1)];
            var indices = new List<int>(LatitudeSteps * LongitudeSteps * 12);
            for (int y = 0; y <= LatitudeSteps; y++)
            {
                float latitude = Mathf.PI * y / LatitudeSteps;
                for (int x = 0; x <= LongitudeSteps; x++)
                {
                    float angle = 2 * Mathf.PI * x / LongitudeSteps;
                    vertices[y * (LongitudeSteps + 1) + x] = new Vector3(Mathf.Sin(latitude) * Mathf.Sin(angle),
                        Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Cos(angle));
                    if (y == LatitudeSteps || x == LongitudeSteps) continue;
                    int a = y * (LongitudeSteps + 1) + x, b = a + LongitudeSteps + 1;
                    if (y == 0) AddTriangle(indices, a, b + 1, b);
                    else if (y == LatitudeSteps - 1) AddTriangle(indices, a, a + 1, b);
                    else AddQuad(indices, a, a + 1, b, b + 1);
                }
            }
            triangles = indices.ToArray();
        }

        internal static void BuildRingData(out Vector3[] vertices, out int[] triangles)
        {
            const int segments = 40;
            vertices = new Vector3[(segments + 1) * 2];
            var indices = new List<int>(segments * 12);
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2 * Mathf.PI * i / segments;
                Vector3 point = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                vertices[i * 2] = point * .73f; vertices[i * 2 + 1] = point * .81f;
                if (i < segments) AddQuad(indices, i * 2, i * 2 + 1, i * 2 + 2, i * 2 + 3);
            }
            triangles = indices.ToArray();
        }

        static void AddQuad(List<int> indices, int a, int b, int c, int d)
        {
            AddTriangle(indices, a, b, c); AddTriangle(indices, b, d, c);
        }
        static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            indices.Add(a); indices.Add(b); indices.Add(c);
            // Visible from inside an energy bubble too; no opaque fallback shader or cube.
            indices.Add(c); indices.Add(b); indices.Add(a);
        }

        internal static void Warn(Exception error)
        {
            if (warned) return; warned = true;
            Log.Warning("[RimFront] Shield presentation skipped safely: " + error.Message);
        }
        internal static void Dispose()
        {
            Array.Clear(impacts, 0, impacts.Length); map = null;
            nextImpact = 0; BeginViewSession();
            if (sphere != null) UnityEngine.Object.Destroy(sphere);
            if (ring != null) UnityEngine.Object.Destroy(ring);
            if (material != null) UnityEngine.Object.Destroy(material);
            sphere = ring = null; material = null;
        }
    }

    [HarmonyPatch(typeof(CompShield), nameof(CompShield.PostPreApplyDamage))]
    internal static class ObserveNativeEnergyShieldImpact
    {
        static void Postfix(CompShield __instance, DamageInfo __0, bool __1)
        {
            try
            {
                Pawn owner = __instance.parent is Apparel apparel ? apparel.Wearer : __instance.parent as Pawn;
                NativeShieldEffects.NotifyAbsorbed(owner, __0, __1);
            }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
    }

    [HarmonyPatch]
    internal static class ObserveNativePhysicalShieldImpact
    {
        static MethodInfo Method => AccessTools.TypeByName("CombatExtended.ArmorUtilityCE") is Type type
            ? AccessTools.Method(type, "GetAfterArmorDamage", new[] { typeof(DamageInfo), typeof(Pawn), typeof(BodyPartRecord),
                typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() }) : null;
        static bool Prepare() => Method != null;
        static MethodBase TargetMethod() => Method;
        static void Postfix(DamageInfo __0, Pawn __1, bool __5)
        {
            try { NativeShieldEffects.NotifyAbsorbed(__1, __0, __5); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
    }

}
