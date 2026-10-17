using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Cosmetic observers only: no hediff, damage, filth, body-part or game RNG writes.
    // Persistent bleeding trails are the native filth rendered by NativeScene.
    internal static class NativeBloodEffects
    {
        internal const int Capacity = 256, Lifetime = 54, EventCapacity = 128;
        internal struct Particle
        {
            internal Vector3 Origin, Velocity;
            internal Color Color;
            internal int Tick, Owner, StopAge;
            internal float Size;
            internal bool Active, Tissue, Trail;
        }
        struct ObservedEvent { internal int Owner, Tick; internal bool Active, Damage; }
        static readonly Particle[] particles = new Particle[Capacity];
        static readonly ObservedEvent[] events = new ObservedEvent[EventCapacity];
        static readonly List<Vector3> vertices = new List<Vector3>(Capacity * 6);
        static readonly List<Color> colors = new List<Color>(Capacity * 6);
        static readonly List<Vector2> uv = new List<Vector2>(Capacity * 6);
        static readonly List<int> triangles = new List<int>(Capacity * 24);
        static readonly int[] octahedron = { 0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5, 1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2 };
        static Map map;
        static Mesh mesh;
        static Material material;
        static int nextParticle, nextEvent, revision, drawnRevision = -1, drawnTick = -1;
        static Pawn drawnPawn;
        static bool warned, drawnBlood, drawnGore, drawnTrails, disabledCleared;

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool ShouldEmit(bool external, bool flesh, bool canBleed, bool makesBlood, float damage)
            => external && flesh && canBleed && makesBlood && Finite(damage) && damage > 0;
        internal static bool ShouldRender(bool trail, bool tissue, bool blood, bool gore, bool trails)
            => trail ? trails : blood && (!tissue || gore);
        internal static bool Alive(int age) => age >= 0 && age < Lifetime;
        internal static float RemainingScale(int age) => !Alive(age) ? 0f : Mathf.Clamp01((Lifetime - age) / 12f);
        internal static int DropletCount(float damage) => !Finite(damage) || damage <= 0 ? 0 : Mathf.Clamp(4 + (int)Math.Min(damage / 4f, 12f), 4, 16);
        internal static int TissueCount(float damage, bool enabled) => !enabled || !Finite(damage) || damage < 18 ? 0 : Math.Min(3, 1 + (int)Math.Min(damage / 30f, 2f));

        // Deterministic variation is local arithmetic, never Verse.Rand or Unity Random.
        internal static Vector3 SprayVelocity(Vector3 incoming, int index, int seed, bool tissue)
        {
            incoming.y = 0;
            if (!Finite(incoming) || incoming.sqrMagnitude < .00001f) incoming = Vector3.forward;
            incoming = incoming.normalized;
            uint hash = unchecked((uint)(seed * 747796405 + index * 2891336453L));
            hash ^= hash >> 16;
            float angle = (hash & 65535) * (Mathf.PI * 2f / 65536f);
            float speed = .7f + ((hash >> 16) & 255) / 255f * 1.8f;
            Vector3 scatter = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            Vector3 velocity = incoming * (tissue ? .55f : 1.2f) + scatter * speed;
            velocity.y = 1.0f + ((hash >> 24) & 255) / 255f * 2.0f;
            return velocity;
        }
        internal static Vector3 PositionAt(Vector3 origin, Vector3 velocity, int age)
        {
            if (!Finite(origin)) origin = new Vector3(0, .04f, 0);
            if (!Finite(velocity)) velocity = Vector3.zero;
            float time = Mathf.Clamp(age, 0, Lifetime) / 60f;
            Vector3 result = origin + Vector3.ClampMagnitude(velocity, 5f) * time + Vector3.down * (4.9f * time * time);
            result.y = Math.Max(.04f, result.y);
            return result;
        }
        internal static void Push(Particle particle)
        {
            if (!Finite(particle.Origin) || !Finite(particle.Velocity) || !Finite(particle.Size) || particle.Size <= 0) return;
            particle.Active = true;
            particles[nextParticle] = particle;
            nextParticle = (nextParticle + 1) % Capacity;
            revision++;
        }

        static void EnsureMap(Map current)
        {
            if (ReferenceEquals(current, map)) return;
            Array.Clear(particles, 0, particles.Length); Array.Clear(events, 0, events.Length);
            nextParticle = nextEvent = 0; map = current; revision++; drawnTick = -1;
        }
        static bool Location(Pawn pawn, out Map current, out Vector3 position)
        {
            current = null; position = Vector3.zero;
            if (!NativeView.Active || pawn == null || Find.TickManager == null) return false;
            if (pawn.Spawned)
            {
                current = pawn.Map; position = NativePawns.TargetPosition(pawn);
                bool lying = pawn.Downed || pawn.GetPosture().Laying();
                position.y = lying ? NativePawns.LyingRootHeight(NativePawnModels.BaseWidth(NativePawnModels.ClassifyPawn(pawn)),
                    NativeScene.PawnScale(pawn), pawn.CurrentBed() != null) : Math.Max(.25f, NativePawns.TargetHeight(pawn) * .60f);
            }
            else if (pawn.Corpse?.Spawned == true)
            {
                current = pawn.Corpse.Map; position = pawn.Corpse.DrawPos;
                position.y = NativePawns.LyingRootHeight(NativePawnModels.BaseWidth(NativePawnModels.ClassifyPawn(pawn)), NativeScene.PawnScale(pawn), false);
            }
            Pawn controlled = NativeView.ControlledPawn;
            if (current == null || current != controlled?.Map || !Finite(position)) return false;
            IntVec3 cell = position.ToIntVec3();
            Vector3 delta = position - controlled.DrawPos; delta.y = 0;
            return cell.InBounds(current) && !cell.Fogged(current) && delta.sqrMagnitude <= 900f;
        }
        static Color BloodColor(Pawn pawn)
        {
            ThingDef blood = pawn.IsMutant ? pawn.mutant.Def.bloodDef ?? pawn.RaceProps.BloodDef : pawn.RaceProps.BloodDef;
            Color color = blood?.graphicData?.color ?? new Color(.52f, .035f, .035f);
            if (!Finite(color.r) || !Finite(color.g) || !Finite(color.b)) color = new Color(.52f, .035f, .035f);
            color.r = Mathf.Clamp01(color.r); color.g = Mathf.Clamp01(color.g); color.b = Mathf.Clamp01(color.b); color.a = 1;
            return color;
        }
        static bool Observe(int owner, int tick, bool damage)
        {
            for (int i = 0; i < events.Length; i++)
            {
                if (!events[i].Active || events[i].Owner != owner || events[i].Tick != tick) continue;
                if (!damage || events[i].Damage) return false;
                events[i].Damage = true;
                // Native DropBloodFilth can execute inside PostApplyDamage first.
                for (int p = 0; p < particles.Length; p++)
                    if (particles[p].Active && particles[p].Trail && particles[p].Owner == owner && particles[p].Tick == tick) particles[p].Active = false;
                return true;
            }
            events[nextEvent] = new ObservedEvent { Owner = owner, Tick = tick, Active = true, Damage = damage };
            nextEvent = (nextEvent + 1) % EventCapacity;
            return true;
        }
        static int StopAge(Vector3 position, Vector3 velocity)
        {
            // Resolve a short cosmetic path once. No colliders or gameplay projectiles are created.
            IntVec3 previous = IntVec3.Invalid;
            for (int age = 1; age < Lifetime; age++)
            {
                Vector3 sample = PositionAt(position, velocity, age);
                IntVec3 cell = sample.ToIntVec3();
                if (cell != previous)
                {
                    if (!cell.InBounds(map) || cell.Fogged(map)) return age - 1;
                    Building edifice = cell.GetEdifice(map);
                    if (edifice != null && edifice.def.passability == Traversability.Impassable
                        && (!(edifice is Building_Door door) || !door.Open)) return age - 1;
                    previous = cell;
                }
                if (sample.y <= .0401f) return age;
            }
            return Lifetime - 1;
        }
        static void Spawn(Pawn pawn, Vector3 position, Vector3 incoming, float damage, bool trail)
        {
            int tick = Find.TickManager.TicksGame;
            if (!Observe(pawn.thingIDNumber, tick, !trail)) return;
            Color color = BloodColor(pawn);
            int droplets = trail ? 3 : DropletCount(damage);
            int tissue = trail ? 0 : TissueCount(damage, RimFrontMod.Settings?.goreEffects ?? true);
            for (int i = 0; i < droplets + tissue; i++)
            {
                bool fragment = i >= droplets;
                Vector3 velocity = SprayVelocity(incoming, i, unchecked(pawn.thingIDNumber * 397 ^ tick), fragment);
                if (trail) { velocity *= .16f; velocity.y = -.25f; }
                Push(new Particle { Origin = position, Velocity = velocity, Color = fragment ? Color.Lerp(color, new Color(.55f, .20f, .16f), .25f) : color,
                    Tick = tick, Owner = pawn.thingIDNumber, Size = fragment ? .075f : .025f + (i % 3) * .009f,
                    Tissue = fragment, Trail = trail, StopAge = StopAge(position, velocity) });
            }
        }
        internal static void NotifyDamage(Pawn pawn, DamageInfo info, float damage)
        {
            if (!(RimFrontMod.Settings?.bloodEffects ?? true) || pawn?.RaceProps == null || info.Def == null
                || !ShouldEmit(info.Def.ExternalViolenceFor(pawn), pawn.RaceProps.IsFlesh, pawn.health?.CanBleed ?? false, info.Def.makesBlood, damage)
                || info.InstantPermanentInjury || !Location(pawn, out Map current, out Vector3 position)) return;
            EnsureMap(current);
            Vector3 incoming = position - (info.Instigator?.DrawPos ?? position);
            if (incoming.sqrMagnitude < .0001f && Finite(info.Angle) && info.Angle >= 0)
                incoming = new Vector3(Mathf.Sin(info.Angle * Mathf.Deg2Rad), 0, Mathf.Cos(info.Angle * Mathf.Deg2Rad));
            Spawn(pawn, position, incoming, damage, false);
        }
        internal static void NotifyBleed(Pawn pawn)
        {
            if (!(RimFrontMod.Settings?.bloodTrails ?? true)
                || pawn?.health == null || !pawn.health.CanBleed || pawn.health.hediffSet.BleedRateTotal <= 0
                || !Location(pawn, out Map current, out Vector3 position)) return;
            EnsureMap(current); Spawn(pawn, position, Vector3.zero, 0, true);
        }

        static void Initialize()
        {
            if (mesh != null && material != null) return;
            Mesh created = null; Material paint = null;
            try
            {
                created = new Mesh { name = "RimFront blood particles" }; created.MarkDynamic();
                paint = new Material(ShaderDatabase.Cutout) { name = "RimFront blood", mainTexture = Texture2D.whiteTexture, color = Color.white };
                mesh = created; material = paint;
            }
            catch { if (created != null) UnityEngine.Object.Destroy(created); if (paint != null) UnityEngine.Object.Destroy(paint); throw; }
        }
        static void AddOctahedron(Vector3 position, float size, bool flat, Color color)
        {
            int first = vertices.Count;
            float height = flat ? .003f : size;
            vertices.Add(position + new Vector3(0, height, 0)); vertices.Add(position - new Vector3(0, height, 0));
            vertices.Add(position + new Vector3(size, 0, 0)); vertices.Add(position - new Vector3(size, 0, 0));
            vertices.Add(position + new Vector3(0, 0, size)); vertices.Add(position - new Vector3(0, 0, size));
            for (int i = 0; i < 6; i++) { colors.Add(color * (i == 0 ? 1f : i == 1 ? .55f : .82f)); uv.Add(Vector2.zero); }
            for (int i = 0; i < octahedron.Length; i++) triangles.Add(first + octahedron[i]);
        }
        internal static void Draw(Map current, Pawn controlled)
        {
            if (current == null || controlled == null || !NativeView.Active) return;
            bool blood = RimFrontMod.Settings?.bloodEffects ?? true, trails = RimFrontMod.Settings?.bloodTrails ?? true;
            if (!blood && !trails)
            {
                if (!disabledCleared)
                {
                    Array.Clear(particles, 0, particles.Length); Array.Clear(events, 0, events.Length);
                    vertices.Clear(); nextParticle = nextEvent = 0; revision++; drawnTick = -1; disabledCleared = true;
                }
                return;
            }
            try
            {
                disabledCleared = false;
                EnsureMap(current); int tick = Find.TickManager.TicksGame;
                bool gore = RimFrontMod.Settings?.goreEffects ?? true;
                if (tick != drawnTick || revision != drawnRevision || controlled != drawnPawn || blood != drawnBlood || gore != drawnGore || trails != drawnTrails)
                {
                    vertices.Clear(); triangles.Clear(); colors.Clear(); uv.Clear();
                    for (int i = 0; i < particles.Length; i++)
                    {
                        Particle particle = particles[i];
                        if (!particle.Active) continue;
                        int age = tick - particle.Tick;
                        if (!Alive(age)) { particles[i].Active = false; continue; }
                        if (!ShouldRender(particle.Trail, particle.Tissue, blood, gore, trails)) { particles[i].Active = false; continue; }
                        Vector3 position = PositionAt(particle.Origin, particle.Velocity, Math.Min(age, particle.StopAge));
                        Vector3 delta = position - controlled.DrawPos; delta.y = 0;
                        IntVec3 cell = position.ToIntVec3();
                        if (delta.sqrMagnitude > 900f || !cell.InBounds(current) || cell.Fogged(current)) continue;
                        bool flat = position.y <= .0401f;
                        AddOctahedron(position, particle.Size * RemainingScale(age) * (flat ? 1.8f : 1f), flat, particle.Color);
                    }
                    if (vertices.Count > 0)
                    {
                        Initialize(); mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv);
                        mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                    }
                    drawnTick = tick; drawnRevision = revision; drawnPawn = controlled; drawnBlood = blood; drawnGore = gore; drawnTrails = trails;
                }
                if (vertices.Count > 0) NativeLighting.Draw(mesh, Matrix4x4.identity, material, controlled.DrawPos);
            }
            catch (Exception error) { Warn(error); }
        }
        internal static void Warn(Exception error)
        {
            if (warned) return; warned = true;
            Log.Warning("[RimFront] Blood presentation disabled for this draw: " + error.GetType().Name + ": " + error.Message);
        }
        internal static void Dispose()
        {
            Array.Clear(particles, 0, particles.Length); Array.Clear(events, 0, events.Length);
            vertices.Clear(); colors.Clear(); triangles.Clear(); uv.Clear(); map = null; drawnPawn = null;
            nextParticle = nextEvent = revision = 0; drawnTick = drawnRevision = -1; disabledCleared = false;
            if (mesh != null) UnityEngine.Object.Destroy(mesh); if (material != null) UnityEngine.Object.Destroy(material);
            mesh = null; material = null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage), new[] { typeof(DamageInfo), typeof(float) })]
    internal static class ObserveNativeBloodDamage
    {
        static void Postfix(Pawn __instance, DamageInfo __0, float __1)
        { try { NativeBloodEffects.NotifyDamage(__instance, __0, __1); } catch (Exception error) { NativeBloodEffects.Warn(error); } }
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodFilth))]
    internal static class ObserveNativeBleedingDrop
    {
        static void Postfix(Pawn ___pawn)
        { try { NativeBloodEffects.NotifyBleed(___pawn); } catch (Exception error) { NativeBloodEffects.Warn(error); } }
    }
}
