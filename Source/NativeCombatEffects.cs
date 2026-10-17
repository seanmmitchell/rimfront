using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    internal static class NativeEffectMath
    {
        internal static bool IsFinite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
            || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
        internal static float VisualVanillaHeight(float startHeight, float endHeight, float progress, float arcHeight)
        {
            float t = Mathf.Clamp01(progress);
            return Mathf.Lerp(startHeight, endHeight, t) + Math.Max(0f, arcHeight) * 4f * t * (1f - t);
        }

        // Cosmetic fragments only. This never changes a projectile's trajectory.
        internal static Vector3 ReflectVisualDirection(Vector3 incoming, Vector3 normal)
        {
            if (incoming.sqrMagnitude < 0.000001f) incoming = Vector3.down;
            if (normal.sqrMagnitude < 0.000001f) normal = Vector3.up;
            return Vector3.Reflect(incoming.normalized, normal.normalized).normalized;
        }

        internal static Matrix4x4 BeamTransform(Vector3 from, Vector3 to, float width)
        {
            Vector3 delta = to - from;
            Quaternion rotation = delta.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(delta) : Quaternion.identity;
            return Matrix4x4.TRS((from + to) * 0.5f, rotation,
                new Vector3(Math.Max(0.001f, width), Math.Max(0.001f, width), Math.Max(0.001f, delta.magnitude)));
        }
    }

    /// <summary>Bounded visual observers of real projectile launches, positions and impacts.
    /// No calls to damage, launch, ammo, velocity or simulation RNG APIs.</summary>
    internal static class NativeCombatEffects
    {
        internal const int MaximumProjectiles = 96;
        internal const int MaximumTracers = 128;
        internal const int MaximumSparks = 192;
        internal const int MaximumMarks = 64;
        internal enum ImpactSurface { Flesh, Metal, Stone, Wood, Soil, Water, Plant, Armor, Shield }
        internal enum ParticleKind { Spark, Chip, Dust, Splash }
        internal struct ImpactParticle
        {
            internal Vector3 Position, Velocity;
            internal int BornTick, Lifetime;
            internal float Size;
            internal ParticleKind Kind;
            internal ImpactSurface Surface;
        }
        internal static int DrawCalls { get; private set; }
        static readonly Type CEProjectile = AccessTools.TypeByName("CombatExtended.ProjectileCE");
        static readonly PropertyInfo CEPosition = CEProjectile?.GetProperty("ExactPosition");
        static readonly FieldInfo CELauncher = CEProjectile == null ? null : AccessTools.Field(CEProjectile, "launcher");
        static readonly FieldInfo CELastPosition = CEProjectile == null ? null : AccessTools.Field(CEProjectile, "LastPos");
        static readonly FieldInfo CEVelocity = CEProjectile == null ? null : AccessTools.Field(CEProjectile, "velocity");
        static readonly FieldInfo VanillaOrigin = AccessTools.Field(typeof(Projectile), "origin");
        static readonly FieldInfo VanillaDestination = AccessTools.Field(typeof(Projectile), "destination");
        static readonly FieldInfo VanillaTarget = AccessTools.Field(typeof(Projectile), "usedTarget");
        static readonly Dictionary<int, Flight> flights = new Dictionary<int, Flight>();
        static readonly List<int> remove = new List<int>();
        static readonly List<Streak> streaks = new List<Streak>();
        static readonly List<ImpactParticle> sparks = new List<ImpactParticle>();
        static readonly List<ContactMark> marks = new List<ContactMark>();
        static readonly Dictionary<int, int> shooterTicks = new Dictionary<int, int>();
        static readonly HashSet<int> impactsInProgress = new HashSet<int>();
        static ImpactObservation currentImpact;
        static Map currentMap;
        static Mesh cube;
        static Material tracerMaterial, sparkMaterial, markMaterial;
        static readonly Material[] impactMaterials = new Material[9];
        static float Clock => (Find.TickManager?.TicksGame ?? 0) / 60f;
        static int scanCursor;
        static bool warned;

        sealed class Flight
        {
            internal Thing Thing;
            internal bool IsCE, LaunchNotified;
            internal Vector3 Origin, Destination, Previous;
            internal float StartHeight, EndHeight, Arc;
        }
        struct Streak { internal Vector3 From, To; internal float Born, Duration, Width; internal bool Flash; }
        struct ContactMark { internal Vector3 Position; internal int BornTick; internal float Size; internal ImpactSurface Surface; }
        internal sealed class ImpactObservation
        {
            internal int Id;
            internal Thing Hit;
            internal Map Map;
            internal Vector3 Position, Incoming, Normal;
            internal bool Deflected, Shielded;
            internal ImpactSurface Surface;
            internal ImpactObservation Previous;
        }

        internal static void Initialize()
        {
            if (cube != null) return;
            NativeScene.Builder builder = new NativeScene.Builder();
            builder.Box(Vector3.zero, Vector3.one, Color.white);
            cube = builder.Create("RimFront projectile and impact geometry");
            tracerMaterial = Material("RimFront tracer", new Color(1f, .88f, .42f));
            sparkMaterial = Material("RimFront spark", new Color(1f, .69f, .22f));
            markMaterial = Material("RimFront ground contact", new Color(.19f, .18f, .15f));
            for (int i = 0; i < impactMaterials.Length; i++)
                impactMaterials[i] = Material("RimFront impact " + (ImpactSurface)i, SurfaceColor((ImpactSurface)i));
        }

        static Material Material(string name, Color color) => new Material(ShaderDatabase.Cutout)
            { name = name, color = color, mainTexture = Texture2D.whiteTexture };

        internal static void Draw(Map map, Pawn controlled)
        {
            DrawCalls = 0;
            if (!NativeView.Active || controlled == null || !controlled.Spawned || map == null) return;
            EnsureMap(map);
            Initialize();
            // The game's projectile group includes def.projectile != null, including CE's
            // ThingWithComps projectiles. Scan at most 512 entries, with a rotating cursor.
            List<Thing> projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
            int count = Math.Min(projectiles.Count, 512);
            for (int i = 0; i < count; i++)
            {
                if (projectiles.Count == 0) break;
                if (scanCursor >= projectiles.Count) scanCursor = 0;
                Thing projectile = projectiles[scanCursor++];
                if (!flights.ContainsKey(projectile.thingIDNumber))
                    try { Track(projectile); } catch (Exception e) { Warn(e); }
            }
            remove.Clear();
            foreach (KeyValuePair<int, Flight> entry in flights)
            {
                Flight flight = entry.Value;
                Thing projectile = flight.Thing;
                if (projectile.Destroyed || !projectile.Spawned || projectile.Map != map)
                { remove.Add(entry.Key); continue; }
                Vector3 position;
                try { position = Position(flight); }
                catch (Exception e) { Warn(e); remove.Add(entry.Key); continue; }
                if (!Visible(map, position)) { remove.Add(entry.Key); continue; }
                Vector3 delta = position - flight.Previous;
                if (delta.sqrMagnitude > .0001f)
                {
                    AddStreak(flight.Previous, position, .075f, .026f, false);
                    flight.Previous = position;
                }
                Vector3 direction = delta;
                if (direction.sqrMagnitude < .0001f && flight.IsCE)
                {
                    try
                    {
                        if (CELastPosition != null) direction = position - (Vector3)CELastPosition.GetValue(projectile);
                        if (direction.sqrMagnitude < .0001f && CEVelocity != null) direction = (Vector3)CEVelocity.GetValue(projectile);
                    }
                    catch (Exception e) { Warn(e); }
                }
                if (direction.sqrMagnitude < .0001f) direction = flight.Destination - flight.Origin;
                direction = direction.normalized;
                if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
                // A small luminous projectile body, always at the game's actual X/Z.
                DrawBeam(position - direction * .12f, position + direction * .05f, .038f, tracerMaterial, true);
            }
            foreach (int id in remove) flights.Remove(id);
            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                Streak streak = streaks[i];
                float age = Clock - streak.Born;
                if (age >= streak.Duration) { streaks.RemoveAt(i); continue; }
                if (!Visible(map, streak.To) && !Visible(map, streak.From)) continue;
                float fade = Math.Max(.15f, 1f - age / streak.Duration);
                DrawBeam(streak.From, streak.To, streak.Width * fade, streak.Flash ? sparkMaterial : tracerMaterial, true);
            }
            int tick = Find.TickManager.TicksGame;
            bool showImpacts = RimFrontMod.Settings?.impactEffects ?? true;
            if (!showImpacts) { sparks.Clear(); marks.Clear(); }
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                ImpactParticle spark = sparks[i];
                float life = Remaining(spark.BornTick, tick, spark.Lifetime);
                if (life <= 0) { sparks.RemoveAt(i); continue; }
                Vector3 position = ParticlePosition(spark, tick);
                if (!Visible(map, position)) continue;
                Material material = impactMaterials[(int)spark.Surface];
                if (spark.Kind == ParticleKind.Spark)
                    DrawBeam(position, position + spark.Velocity.normalized * (.16f * life), spark.Size * life, material, true);
                else
                {
                    float size = spark.Size * life * (spark.Kind == ParticleKind.Dust ? 1f + (1f - life) * 3f : 1f);
                    Vector3 dimensions = spark.Kind == ParticleKind.Splash ? new Vector3(size * .5f, size * 1.8f, size * .5f)
                        : spark.Kind == ParticleKind.Chip ? new Vector3(size, size * .45f, size * .7f) : Vector3.one * size;
                    NativeLighting.Draw(cube, Matrix4x4.TRS(position, Quaternion.Euler(17 * i, tick - spark.BornTick, 31 * i), dimensions), material, position);
                    DrawCalls++;
                }
            }
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                ContactMark mark = marks[i]; float life = Remaining(mark.BornTick, tick, 180);
                if (life <= 0) { marks.RemoveAt(i); continue; }
                if (!Visible(map, mark.Position)) continue;
                NativeLighting.Draw(cube, Matrix4x4.TRS(mark.Position, Quaternion.identity,
                    new Vector3(mark.Size * life, .008f, mark.Size * .7f * life)), markMaterial, mark.Position);
                DrawCalls++;
            }
        }

        static void EnsureMap(Map map)
        {
            if (currentMap == map) return;
            Dispose(); currentMap = map;
        }

        static bool IsCE(Thing thing) => CEProjectile != null && CEPosition?.PropertyType == typeof(Vector3)
            && CEProjectile.IsInstanceOfType(thing);

        static Flight Track(Thing projectile, bool transientIfFull = false)
        {
            if (projectile == null || !projectile.Spawned || projectile.def.projectile == null || !NativeView.Active) return null;
            if (flights.TryGetValue(projectile.thingIDNumber, out Flight existing)) return existing;
            return ObserveBounded(flights, projectile.thingIDNumber, MaximumProjectiles, transientIfFull, () => CreateFlight(projectile));
        }
        // Impact observation must remain possible when the persistent flight pool is full.
        // The transient value is returned to this event but never grows the retained cache.
        internal static T ObserveBounded<T>(IDictionary<int, T> cache, int id, int capacity, bool transientIfFull, Func<T> create) where T : class
        {
            if (cache.TryGetValue(id, out T found)) return found;
            if (cache.Count >= capacity && !transientIfFull) return null;
            T value = create();
            if (value != null && cache.Count < capacity) cache.Add(id, value);
            return value;
        }
        static Flight CreateFlight(Thing projectile)
        {
            bool ce = IsCE(projectile);
            Projectile vanilla = projectile as Projectile;
            if (!ce && vanilla == null) return null;
            Vector3 raw = ce ? (Vector3)CEPosition.GetValue(projectile, null) : vanilla.ExactPosition;
            if (!Visible(projectile.Map, raw)) return null;
            Flight flight = new Flight { Thing = projectile, IsCE = ce, Origin = raw, Destination = raw + Vector3.forward, StartHeight = 1.15f, EndHeight = .04f };
            if (!ce)
            {
                if (VanillaOrigin != null) flight.Origin = (Vector3)VanillaOrigin.GetValue(projectile);
                if (VanillaDestination != null) flight.Destination = (Vector3)VanillaDestination.GetValue(projectile);
                if (VanillaTarget != null)
                {
                    LocalTargetInfo target = (LocalTargetInfo)VanillaTarget.GetValue(projectile);
                    if (target.Thing is Pawn targetPawn) flight.EndHeight = NativeScene.PawnTargetHeight(targetPawn) * .58f;
                    else if (target.Thing is Building) flight.EndHeight = 1f;
                }
                Pawn shooter = vanilla.Launcher as Pawn;
                if (shooter != null && NativeWeapons.TryGetMuzzle(shooter, out Vector3 muzzle, out Vector3 ignored))
                    flight.StartHeight = muzzle.y;
                Vector3 path = flight.Destination - flight.Origin; path.y = 0f;
                flight.Arc = Math.Min(projectile.def.projectile.arcHeightFactor, path.magnitude * .2f);
            }
            else if (CELastPosition != null) flight.Origin = (Vector3)CELastPosition.GetValue(projectile);
            flight.Previous = Position(flight);
            return flight;
        }

        static Vector3 Position(Flight flight)
        {
            if (flight.IsCE) return (Vector3)CEPosition.GetValue(flight.Thing, null);
            if (NativeVanillaAim.TryPosition((Projectile)flight.Thing, out Vector3 manualPosition)) return manualPosition;
            Vector3 position = ((Projectile)flight.Thing).ExactPosition;
            Vector3 path = flight.Destination - flight.Origin; path.y = 0f;
            Vector3 traveled = position - flight.Origin; traveled.y = 0f;
            float fraction = path.sqrMagnitude > .0001f ? Vector3.Dot(traveled, path) / path.sqrMagnitude : 1f;
            position.y = NativeEffectMath.VisualVanillaHeight(flight.StartHeight, flight.EndHeight, fraction, flight.Arc);
            return position;
        }

        internal static void ObservedLaunch(Thing projectile)
        {
            try
            {
                if (!NativeView.Active || projectile?.Map == null || projectile.Map != NativeView.ControlledPawn?.Map) return;
                EnsureMap(projectile.Map);
                Flight flight = Track(projectile, true);
                if (flight == null || flight.LaunchNotified) return;
                flight.LaunchNotified = true;
                Pawn shooter = (flight.IsCE ? CELauncher?.GetValue(projectile) as Thing : ((Projectile)projectile).Launcher) as Pawn;
                NotifyActualShot(shooter, flight.Previous);
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void ObservedBeam(Thing projectile, Vector3 from, Vector3 to)
        {
            try
            {
                if (!NativeView.Active || projectile?.Map == null || projectile.Map != NativeView.ControlledPawn?.Map) return;
                if (!Visible(projectile.Map, from) && !Visible(projectile.Map, to)) return;
                EnsureMap(projectile.Map);
                // Includes only reflection segments that CE itself actually generated.
                AddStreak(from, to, .11f, .045f, false);
                NotifyActualShot(CELauncher?.GetValue(projectile) as Pawn, from);
            }
            catch (Exception e) { Warn(e); }
        }

        static void NotifyActualShot(Pawn shooter, Vector3 fallback)
        {
            if (shooter == null) return;
            int tick = Find.TickManager.TicksGame;
            // Pellets, forwarding Launch overloads and CE beam reflections share one flash.
            if (shooterTicks.TryGetValue(shooter.thingIDNumber, out int previous) && previous == tick) return;
            if (shooterTicks.Count >= 256) shooterTicks.Clear();
            shooterTicks[shooter.thingIDNumber] = tick;
            NativeWeapons.NotifyShot(shooter);
            var kind = NativeWeapons.ClassifyName(shooter.equipment?.Primary?.def.defName, true);
            if (kind == NativeWeapons.WeaponKind.Bow || kind == NativeWeapons.WeaponKind.Grenade) return;
            Vector3 position = fallback, direction = Vector3.forward;
            if (NativeWeapons.TryGetMuzzle(shooter, out Vector3 muzzle, out Vector3 muzzleDirection))
            { position = muzzle; direction = muzzleDirection; }
            AddStreak(position, position + direction.normalized * .34f, .055f, .11f, true);
            NativeLighting.Flash(position, 1f);
        }

        internal static ImpactObservation BeginImpact(Thing projectile, Thing hit, bool blockedByShield = false)
        {
            try
            {
                if (!NativeView.Active || projectile?.Map == null || projectile.Destroyed || projectile.Map != NativeView.ControlledPawn?.Map
                    || impactsInProgress.Contains(projectile.thingIDNumber)) return null;
                EnsureMap(projectile.Map);
                Flight flight = Track(projectile, true);
                if (flight == null) return null;
                Vector3 position = Position(flight);
                bool manualRay = !flight.IsCE && NativeVanillaAim.Tracks((Projectile)flight.Thing);
                if (!flight.IsCE && !manualRay && hit != null)
                {
                    // Vanilla only identifies a hit thing/cell, not a 3D contact point.
                    position.x = hit.DrawPos.x; position.z = hit.DrawPos.z;
                    position.y = hit is Pawn p ? NativeScene.PawnTargetHeight(p) * .58f : Math.Max(.04f, position.y);
                }
                if (!Visible(projectile.Map, position)) return null;
                Vector3 incoming = position - flight.Previous;
                if (incoming.sqrMagnitude < .0001f) incoming = position - flight.Origin;
                if (incoming.sqrMagnitude < .0001f) incoming = Vector3.down;
                Vector3 normal = hit == null && position.y <= .15f ? Vector3.up : -incoming.normalized;
                if (!flight.IsCE && !manualRay && hit is Building && hit.def.fillPercent >= .95f)
                {
                    float width = hit.Rotation.IsHorizontal ? hit.def.size.z : hit.def.size.x;
                    float depth = hit.Rotation.IsHorizontal ? hit.def.size.x : hit.def.size.z;
                    position = BoxContact(position, width * .5f, depth * .5f, incoming, out normal);
                }
                AddStreak(flight.Previous, position, .09f, .026f, false);
                ImpactObservation observation = new ImpactObservation
                {
                    Id = projectile.thingIDNumber, Hit = hit, Map = projectile.Map,
                    Position = position, Incoming = incoming.normalized, Normal = normal, Previous = currentImpact,
                    Surface = SurfaceFor(hit, projectile.Map, position), Shielded = blockedByShield
                };
                impactsInProgress.Add(observation.Id);
                currentImpact = observation;
                return observation;
            }
            catch (Exception e) { Warn(e); return null; }
        }

        internal static void ConfirmDeflection(Pawn target, bool deflected)
        {
            if (deflected && currentImpact != null && currentImpact.Hit == target) currentImpact.Deflected = true;
        }
        internal static void ConfirmShield(Pawn target, bool absorbed)
        {
            try
            {
                if (!absorbed || target == null || currentImpact == null) return;
                // An explosion can synchronously damage nearby shielded pawns inside
                // Impact(null). Their absorption does not turn the terrain contact into
                // a shield hit; only an explicit direct hit identity is attributable here.
                if (currentImpact.Hit == target) currentImpact.Shielded = true;
            }
            catch (Exception error) { Warn(error); }
        }

        internal static ImpactSurface ClassifyImpact(string material, bool pawn, bool flesh, bool plant,
            bool building, bool water, bool shield, bool deflected)
        {
            if (shield) return ImpactSurface.Shield;
            if (deflected) return ImpactSurface.Armor;
            if (pawn) return flesh ? ImpactSurface.Flesh : ImpactSurface.Metal;
            if (plant) return ImpactSurface.Plant;
            if (water && !building) return ImpactSurface.Water;
            string name = (material ?? "").ToLowerInvariant();
            if (name.Contains("wood") || name.Contains("timber")) return ImpactSurface.Wood;
            if (name.Contains("metal") || name.Contains("steel") || name.Contains("silver") || name.Contains("gold") || name.Contains("uranium")) return ImpactSurface.Metal;
            if (building || name.Contains("stone") || name.Contains("stony") || name.Contains("rock") || name.Contains("concrete") || name.Contains("granite") || name.Contains("marble")) return ImpactSurface.Stone;
            return ImpactSurface.Soil;
        }
        static ImpactSurface SurfaceFor(Thing hit, Map map, Vector3 position)
        {
            string name = hit?.def?.defName ?? "";
            ThingDef stuff = hit?.Stuff;
            if (stuff?.stuffProps != null)
            {
                name = stuff.defName;
                if (stuff.stuffProps.categories != null) foreach (StuffCategoryDef category in stuff.stuffProps.categories) name += " " + category.defName;
            }
            if (hit == null)
            {
                IntVec3 cell = position.ToIntVec3();
                if (cell.InBounds(map)) name = cell.GetTerrain(map)?.defName ?? "";
            }
            Pawn pawn = hit as Pawn;
            return ClassifyImpact(name, pawn != null, pawn?.RaceProps?.IsFlesh ?? false, hit is Plant,
                hit is Building, hit == null && name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0, false, false);
        }
        internal static Color SurfaceColor(ImpactSurface surface)
        {
            switch (surface)
            {
                case ImpactSurface.Metal: case ImpactSurface.Armor: return new Color(1f, .68f, .23f);
                case ImpactSurface.Shield: return new Color(.30f, .72f, 1f);
                case ImpactSurface.Stone: return new Color(.55f, .56f, .52f);
                case ImpactSurface.Wood: return new Color(.48f, .31f, .16f);
                case ImpactSurface.Water: return new Color(.39f, .62f, .72f);
                case ImpactSurface.Plant: return new Color(.29f, .44f, .22f);
                default: return new Color(.46f, .39f, .29f);
            }
        }
        internal static int ParticleCount(ImpactSurface surface) => surface == ImpactSurface.Flesh ? 0
            : surface == ImpactSurface.Shield || surface == ImpactSurface.Armor || surface == ImpactSurface.Metal ? 9
            : surface == ImpactSurface.Stone || surface == ImpactSurface.Wood ? 8 : 6;
        internal static float Remaining(int born, int tick, int lifetime)
        {
            long age = (long)tick - born;
            return age < 0 || lifetime <= 0 || age >= lifetime ? 0 : 1f - (float)age / lifetime;
        }
        internal static ImpactParticle MakeParticle(ImpactSurface surface, int seed, int index, Vector3 point,
            Vector3 normal, Vector3 incoming, int tick)
        {
            if (!NativeEffectMath.IsFinite(point)) point = Vector3.zero;
            if (!NativeEffectMath.IsFinite(normal) || normal.sqrMagnitude < .0001f) normal = Vector3.up;
            normal.Normalize();
            if (!NativeEffectMath.IsFinite(incoming) || incoming.sqrMagnitude < .0001f) incoming = -normal;
            bool metal = surface == ImpactSurface.Metal || surface == ImpactSurface.Armor || surface == ImpactSurface.Shield;
            ParticleKind kind = surface == ImpactSurface.Water ? ParticleKind.Splash
                : metal && index % 3 != 2 ? ParticleKind.Spark
                : surface == ImpactSurface.Stone || surface == ImpactSurface.Wood || surface == ImpactSurface.Plant ? (index % 3 == 2 ? ParticleKind.Dust : ParticleKind.Chip)
                : ParticleKind.Dust;
            float angle = (seed & 1023) * .71f + index * 2.39996f;
            Vector3 fan = new Vector3(Mathf.Sin(angle), .35f + (index % 3) * .22f, Mathf.Cos(angle));
            Vector3 direction = metal ? NativeEffectMath.ReflectVisualDirection(incoming, normal) + fan * .65f : normal * .65f + fan;
            // Keep debris on the visible side of the contact plane, including wall hits.
            float inward = Vector3.Dot(direction, normal);
            if (inward < .15f) direction += normal * (.15f - inward);
            return new ImpactParticle { Surface = surface, Kind = kind, Position = point + normal * .025f,
                Velocity = direction.normalized * (kind == ParticleKind.Spark ? 3.8f : kind == ParticleKind.Chip ? 1.8f : 1.1f),
                BornTick = tick, Lifetime = kind == ParticleKind.Spark ? 15 + index % 6 : 24 + index % 12,
                Size = kind == ParticleKind.Spark ? .024f : kind == ParticleKind.Chip ? .065f : .085f };
        }
        internal static Vector3 ParticlePosition(ImpactParticle particle, int tick)
        {
            float seconds = Mathf.Clamp((float)((long)tick - particle.BornTick), 0, Math.Max(0, particle.Lifetime)) / 60f;
            float gravity = particle.Kind == ParticleKind.Dust ? .35f : 2.8f;
            Vector3 point = particle.Position + particle.Velocity * seconds + Vector3.down * (gravity * seconds * seconds);
            point.y = Math.Max(.035f, point.y); return point;
        }
        internal static bool TryGroundMark(Vector3 point, ImpactSurface surface, out Vector3 mark)
        {
            mark = point;
            if (!NativeEffectMath.IsFinite(point) || point.y < -.05f || point.y > .15f
                || surface == ImpactSurface.Water || surface == ImpactSurface.Flesh || surface == ImpactSurface.Shield || surface == ImpactSurface.Armor) return false;
            mark.y = .035f; return true;
        }
        internal static Vector3 BoxContact(Vector3 center, float halfX, float halfZ, Vector3 incoming, out Vector3 normal)
        {
            normal = Vector3.up;
            Vector3 travel = incoming; travel.y = 0;
            if (!NativeEffectMath.IsFinite(travel) || travel.sqrMagnitude < .0001f) return center;
            float tx = Math.Abs(travel.x) > .0001f ? Math.Max(.01f, halfX) / Math.Abs(travel.x) : float.MaxValue;
            float tz = Math.Abs(travel.z) > .0001f ? Math.Max(.01f, halfZ) / Math.Abs(travel.z) : float.MaxValue;
            normal = tx <= tz ? new Vector3(-Math.Sign(travel.x), 0, 0) : new Vector3(0, 0, -Math.Sign(travel.z));
            return center - travel * Math.Min(tx, tz);
        }

        internal static void EndImpact(ImpactObservation observation, Exception error)
        {
            if (observation == null) return;
            impactsInProgress.Remove(observation.Id);
            currentImpact = observation.Previous;
            try
            {
                if (error != null || !NativeView.Active || observation.Map != currentMap || !(RimFrontMod.Settings?.impactEffects ?? true)) return;
                ImpactSurface surface = observation.Shielded ? ImpactSurface.Shield : observation.Deflected ? ImpactSurface.Armor : observation.Surface;
                int tick = Find.TickManager.TicksGame;
                for (int i = 0; i < ParticleCount(surface); i++) AddSpark(MakeParticle(surface, observation.Id, i,
                    observation.Position, observation.Normal, observation.Incoming, tick));
                if (surface == ImpactSurface.Armor || surface == ImpactSurface.Metal || surface == ImpactSurface.Shield)
                    NativeLighting.Flash(observation.Position, .3f);
                if (observation.Hit == null && TryGroundMark(observation.Position, surface, out Vector3 mark))
                {
                    if (marks.Count >= MaximumMarks) marks.RemoveAt(0);
                    marks.Add(new ContactMark { Position = mark, BornTick = tick, Size = .14f, Surface = surface });
                }
            }
            catch (Exception e) { Warn(e); }
            finally { flights.Remove(observation.Id); }
        }

        static bool Visible(Map map, Vector3 position)
        {
            if (!NativeEffectMath.IsFinite(position)) return false;
            Pawn controlled = NativeView.ControlledPawn;
            if (controlled == null || !controlled.Spawned || map != controlled.Map) return false;
            IntVec3 cell = position.ToIntVec3();
            if (!cell.InBounds(map) || cell.Fogged(map)) return false;
            Vector3 delta = position - controlled.DrawPos; delta.y = 0f;
            return delta.sqrMagnitude <= 900f;
        }

        static void AddStreak(Vector3 from, Vector3 to, float duration, float width, bool flash)
        {
            if (!ClipForDisplay(ref from, ref to)) return;
            if ((to - from).sqrMagnitude < .00001f) return;
            if (streaks.Count >= MaximumTracers) streaks.RemoveAt(0);
            streaks.Add(new Streak { From = from, To = to, Born = Clock, Duration = duration, Width = width, Flash = flash });
        }

        // Long CE beams are clipped once when observed, never extended into undiscovered fog
        // or outside the 30-cell view. Short moving-projectile segments cost only a few samples.
        static bool ClipForDisplay(ref Vector3 from, ref Vector3 to)
        {
            if (currentMap == null || !NativeEffectMath.IsFinite(from) || !NativeEffectMath.IsFinite(to)) return false;
            if (!Visible(currentMap, from))
            {
                if (!Visible(currentMap, to)) return false;
                Vector3 swap = from; from = to; to = swap;
            }
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length > 60f) { to = from + delta / length * 60f; delta = to - from; length = 60f; }
            int steps = Math.Max(1, Mathf.CeilToInt(length * 5f));
            Vector3 last = from;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 point = from + delta * ((float)i / steps);
                if (!Visible(currentMap, point)) { to = last; return (to - from).sqrMagnitude > .00001f; }
                last = point;
            }
            return true;
        }
        static void AddSpark(ImpactParticle spark)
        {
            if (sparks.Count >= MaximumSparks) sparks.RemoveAt(0);
            sparks.Add(spark);
        }
        static void DrawBeam(Vector3 from, Vector3 to, float width, Material material, bool emissive)
        {
            if (!NativeEffectMath.IsFinite(from) || !NativeEffectMath.IsFinite(to)) return;
            NativeLighting.Draw(cube, NativeEffectMath.BeamTransform(from, to, width), material, (from + to) * .5f, emissive);
            DrawCalls++;
        }
        static void Warn(Exception e)
        {
            if (warned) return;
            warned = true;
            Log.Warning("[RimFront] Optional projectile visual adapter skipped an unsupported event: " + e.GetBaseException().Message);
        }
        internal static void Dispose()
        {
            flights.Clear(); remove.Clear(); streaks.Clear(); sparks.Clear(); marks.Clear(); shooterTicks.Clear(); impactsInProgress.Clear();
            currentImpact = null; currentMap = null; scanCursor = 0; DrawCalls = 0;
            if (cube != null) UnityEngine.Object.Destroy(cube);
            if (tracerMaterial != null) UnityEngine.Object.Destroy(tracerMaterial);
            if (sparkMaterial != null) UnityEngine.Object.Destroy(sparkMaterial);
            if (markMaterial != null) UnityEngine.Object.Destroy(markMaterial);
            for (int i = 0; i < impactMaterials.Length; i++)
            { if (impactMaterials[i] != null) UnityEngine.Object.Destroy(impactMaterials[i]); impactMaterials[i] = null; }
            cube = null; tracerMaterial = sparkMaterial = markMaterial = null;
        }

        internal static IEnumerable<MethodBase> LaunchMethods()
        {
            foreach (MethodInfo method in typeof(Projectile).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                ParameterInfo[] p = method.GetParameters();
                if (method.Name == "Launch" && p.Length > 1 && p[0].ParameterType == typeof(Thing) && p[1].ParameterType == typeof(Vector3)) yield return method;
            }
            if (CEProjectile != null)
                foreach (MethodInfo method in CEProjectile.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    ParameterInfo[] p = method.GetParameters();
                    if ((method.Name == "Launch" || method.Name == "Throw") && p.Length > 1 && p[0].ParameterType == typeof(Thing)) yield return method;
                }
        }
        internal static IEnumerable<MethodBase> ImpactMethods()
        {
            var assemblies = new HashSet<Assembly> { typeof(Projectile).Assembly };
            if (CEProjectile != null) assemblies.Add(CEProjectile.Assembly);
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type type in types)
                {
                    if (type == null || (!typeof(Projectile).IsAssignableFrom(type) && (CEProjectile == null || !CEProjectile.IsAssignableFrom(type)))) continue;
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        ParameterInfo[] p = method.GetParameters();
                        if (method.Name == "Impact" && !method.IsAbstract && p.Length >= 1 && p[0].ParameterType == typeof(Thing)) yield return method;
                    }
                }
            }
        }
        internal static bool HasShieldImpactFlag(MethodBase method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            // Vanilla exposes this explicit outcome; current CE Impact(Thing) does
            // not. Never infer shield absorption from some other optional bool.
            return parameters.Length >= 2 && parameters[1].ParameterType == typeof(bool)
                && parameters[1].Name == "blockedByShield";
        }
    }

    [HarmonyPatch]
    internal static class NativeObserveProjectileLaunch
    {
        static IEnumerable<MethodBase> TargetMethods() => NativeCombatEffects.LaunchMethods();
        static void Postfix(Thing __instance) => NativeCombatEffects.ObservedLaunch(__instance);
    }

    [HarmonyPatch]
    internal static class NativeObserveProjectileImpact
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodBase method in NativeCombatEffects.ImpactMethods())
                if (!NativeCombatEffects.HasShieldImpactFlag(method)) yield return method;
        }
        static void Prefix(Thing __instance, Thing __0, out NativeCombatEffects.ImpactObservation __state)
            => __state = NativeCombatEffects.BeginImpact(__instance, __0);
        // Finalizer removes the nested-impact guard even when another mod throws; never swallows exceptions.
        static Exception Finalizer(Exception __exception, NativeCombatEffects.ImpactObservation __state)
        { NativeCombatEffects.EndImpact(__state, __exception); return __exception; }
    }

    [HarmonyPatch]
    internal static class NativeObserveShieldProjectileImpact
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodBase method in NativeCombatEffects.ImpactMethods())
                if (NativeCombatEffects.HasShieldImpactFlag(method)) yield return method;
        }
        static void Prefix(Thing __instance, Thing __0, bool __1, out NativeCombatEffects.ImpactObservation __state)
            => __state = NativeCombatEffects.BeginImpact(__instance, __0, __1);
        static Exception Finalizer(Exception __exception, NativeCombatEffects.ImpactObservation __state)
        { NativeCombatEffects.EndImpact(__state, __exception); return __exception; }
    }

    [HarmonyPatch(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))]
    internal static class NativeObserveVanillaDeflection
    {
        static void Postfix(Pawn __0, bool __5) => NativeCombatEffects.ConfirmDeflection(__0, __5);
    }

    [HarmonyPatch]
    internal static class NativeObserveCEDeflection
    {
        static MethodInfo Method
        {
            get
            {
                Type type = AccessTools.TypeByName("CombatExtended.ArmorUtilityCE");
                return type == null ? null : AccessTools.Method(type, "GetAfterArmorDamage",
                    new[] { typeof(DamageInfo), typeof(Pawn), typeof(BodyPartRecord), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() });
            }
        }
        static bool Prepare() => Method != null;
        static MethodBase TargetMethod() => Method;
        static void Postfix(Pawn __1, bool __3, bool __5)
        { NativeCombatEffects.ConfirmDeflection(__1, __3); NativeCombatEffects.ConfirmShield(__1, __5); }
    }

    [HarmonyPatch(typeof(CompShield), nameof(CompShield.PostPreApplyDamage))]
    internal static class NativeObserveImpactShield
    {
        static void Postfix(CompShield __instance, bool __1)
        {
            Pawn owner = __instance.parent is Apparel apparel ? apparel.Wearer : __instance.parent as Pawn;
            NativeCombatEffects.ConfirmShield(owner, __1);
        }
    }

    [HarmonyPatch]
    internal static class NativeObserveCEBeam
    {
        static MethodInfo Method
        {
            get
            {
                Type type = AccessTools.TypeByName("CombatExtended.Lasers.LaserBeamCE");
                return type == null ? null : AccessTools.Method(type, "SpawnBeam", new[] { typeof(Vector3), typeof(Vector3) });
            }
        }
        static bool Prepare() => Method != null;
        static MethodBase TargetMethod() => Method;
        static void Postfix(Thing __instance, Vector3 __0, Vector3 __1) => NativeCombatEffects.ObservedBeam(__instance, __0, __1);
    }
}
