using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    /// <summary>Original low-poly scenery submitted directly to the game's native renderer.
    /// Reads game state only. No GameObjects, physics, damage, or simulated pawn positions.</summary>
    internal static class NativeScene
    {
        internal const int ChunkSize = 12;
        internal const float FogHeight = 5f;
        internal static int ChunkRadius => ViewRange.ChunkRadiusFor(RimFrontMod.Settings?.extraViewRange ?? 0);
        internal const int MaximumChunks = ViewRange.MaximumChunks;
        internal static int DrawCalls { get; private set; }
        internal static void CountDraw() { DrawCalls++; }
        internal static int CachedChunks { get { return chunks.Count; } }
        internal static float ViewDistance => ViewRange.DistanceFor(RimFrontMod.Settings?.extraViewRange ?? 0);
        internal static float ViewDistanceSquared => ViewDistance * ViewDistance;
        static readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        static readonly List<int> expired = new List<int>();
        static readonly HashSet<int> drawn = new HashSet<int>();
        static readonly Dictionary<ThingDef, bool> bloodDefinitions = new Dictionary<ThingDef, bool>();
        internal const int MaximumBloodInstances = 5;
        internal const int MaximumBloodDefinitions = 512, MaximumBloodSplatsPerChunk = 96, MaximumBloodThingsPerCell = 2;
        static bool bloodTrailsVisible;
        static readonly PropertyInfo DoorOpenPct = typeof(Building_Door).GetProperty("OpenPct", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static Map currentMap;
        static Material material;
        static Mesh doorMesh;
        static float nextRefresh;
        static readonly Color Stone = new Color(.58f, .60f, .53f);
        static readonly Color Wood = new Color(.46f, .32f, .19f);
        static readonly Color Metal = new Color(.39f, .44f, .44f);
        static readonly Color Fog = new Color(.075f, .105f, .12f);

        internal static void Initialize()
        {
            if (material != null) return;
            // Cutout writes depth on both engines. VertexColor/SolidColor do NOT on 1.7.
            // Native 1.7 Cutout multiplies its white texture by the mesh vertex colors.
            material = new Material(ShaderDatabase.Cutout) { name = "RimFront native opaque", color = Color.white, mainTexture = Texture2D.whiteTexture };
            // Race metadata identifies modded blood and smears without matching names.
            // Mechanoid bloodDef can be machine debris, which is not a fluid stain.
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.race != null && def.race.IsFlesh)
                {
                    RegisterBloodDefinition(def.race.BloodDef, false);
                    RegisterBloodDefinition(def.race.BloodSmearDef, true);
                }
            foreach (MutantDef mutant in DefDatabase<MutantDef>.AllDefsListForReading)
                if (mutant.canBleed)
                {
                    RegisterBloodDefinition(mutant.bloodDef, false);
                    RegisterBloodDefinition(mutant.bloodSmearDef, true);
                }
            bloodTrailsVisible = RimFrontMod.Settings?.bloodTrails ?? true;
            Builder door = new Builder();
            AddDoorLeaf(door);
            doorMesh = door.Create("RimFront sliding door leaf");
        }

        internal static void Draw(Map map, Pawn controlled, float yaw)
        {
            DrawCalls = 0;
            if (map == null || controlled == null || !controlled.Spawned) return;
            if (currentMap != map) { Dispose(); currentMap = map; }
            Initialize();
            bool showBlood = RimFrontMod.Settings?.bloodTrails ?? true;
            if (showBlood != bloodTrailsVisible)
            {
                bloodTrailsVisible = showBlood;
                foreach (Chunk chunk in chunks.Values) chunk.Dirty = true;
            }
            if (NativeMountains.Update(map))
                foreach (Chunk chunk in chunks.Values) chunk.Dirty = true;
            NativeLighting.Begin(map);
            NativePawns.BeginFrame(controlled);
            NativeVegetation.Begin(NativeView.RenderPosition, yaw);
            int centerX = controlled.Position.x / ChunkSize, centerZ = controlled.Position.z / ChunkSize;
            expired.Clear();
            foreach (KeyValuePair<int, Chunk> pair in chunks)
                if (Math.Abs(pair.Value.X - centerX) > ChunkRadius || Math.Abs(pair.Value.Z - centerZ) > ChunkRadius) expired.Add(pair.Key);
            foreach (int key in expired) { chunks[key].Dispose(); chunks.Remove(key); }

            // One rebuild per frame. Direct map changes come first, then nearby
            // profile changes, then missing chunks; cache expansion cannot starve edits.
            float now = Time.realtimeSinceStartup;
            Chunk selected = SelectChunk(map, centerX, centerZ, now);
            if (selected != null)
            {
                selected.Refresh(map, now);
                chunks[Key(selected.X, selected.Z)] = selected;
            }

            // Recolor at most one cached mesh per frame. Unchanged lighting needs no
            // upload; Waylith may re-upload the complete mesh when its colors change.
            Chunk lightChunk = null;
            int lightPriority = -1;
            foreach (Chunk chunk in chunks.Values)
            {
                bool flash = NativeLighting.HasFlashNear(new Vector3((chunk.X + .5f) * ChunkSize, 1f, (chunk.Z + .5f) * ChunkSize), ChunkSize);
                int priority = chunk.WasFlashed && !flash ? 2 : flash ? 1 : 0;
                if (chunk.LightCheckedAt + (priority > 0 ? .035f : .3f) > now) continue;
                if (lightChunk == null || priority > lightPriority || (priority == lightPriority && chunk.LightCheckedAt < lightChunk.LightCheckedAt))
                { lightChunk = chunk; lightPriority = priority; }
            }
            if (lightChunk != null) lightChunk.Relight(now);
            drawn.Clear();
            Vector3 observer = controlled.DrawPos;
            foreach (Chunk chunk in chunks.Values)
            {
                if (chunk.Mesh != null) { Graphics.DrawMesh(chunk.Mesh, Matrix4x4.identity, material, 0); DrawCalls++; }
                foreach (Thing thing in chunk.Dynamic)
                {
                    if (thing == null || !thing.Spawned || thing.Map != map || thing == controlled || !drawn.Add(thing.thingIDNumber)) continue;
                    Vector3 pos = thing.DrawPos;
                    float dx = pos.x - observer.x, dz = pos.z - observer.z;
                    if (thing is Plant plant)
                    {
                        if (!thing.Position.Fogged(map)) NativeVegetation.Queue(plant, dx * dx + dz * dz);
                        continue;
                    }
                    if (dx * dx + dz * dz > ViewDistanceSquared || thing.Position.Fogged(map)) continue;
                    Building_Door door = thing as Building_Door;
                    if (door != null) { DrawDoor(door, pos); continue; }
                    if (thing is Corpse corpse && corpse.InnerPawn != null)
                        NativePawns.Draw(corpse.InnerPawn, pos, dx * dx + dz * dz, true);
                }
            }
            NativeVegetation.Draw(map);
            // Pawn positions/spawns are current every frame; chunk refresh is for static cells only.
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == controlled || pawn.Dead || !pawn.Spawned || pawn.Position.Fogged(map)) continue;
                Vector3 pos = pawn.DrawPos;
                float dx = pos.x - observer.x, dz = pos.z - observer.z;
                if (dx * dx + dz * dz > ViewDistanceSquared) continue;
                NativePawns.Draw(pawn, pos, dx * dx + dz * dz);
            }
            NativeCombatEffects.Draw(map, controlled);
            NativeBloodEffects.Draw(map, controlled);
            NativeShieldEffects.Draw3D(map, controlled);
            NativeWeather.Draw(map, controlled);
            NativeWeapons.DrawFirstPerson(controlled);
            NativePawnEquipment.DrawFirstPerson(controlled);
        }

        internal static float PawnScale(Pawn pawn) { return Mathf.Clamp((float)Math.Sqrt(Math.Max(.15f, pawn.BodySize)), .45f, 2.5f); }
        internal static float GetPawnHeight(Pawn pawn) => NativePawns.TargetHeight(pawn);
        internal static float PawnTargetHeight(Pawn pawn) { return GetPawnHeight(pawn); }
        internal static float GetPawnWidth(Pawn pawn) { return NativePawnModels.BaseWidth(NativePawnModels.ClassifyPawn(pawn)) * PawnScale(pawn); }

        static Chunk SelectChunk(Map map, int centerX, int centerZ, float now)
        {
            Chunk selected = null;
            int bestDistance = int.MaxValue;
            foreach (Chunk chunk in chunks.Values)
            {
                if (!chunk.Dirty) continue;
                int distance = (chunk.X - centerX) * (chunk.X - centerX) + (chunk.Z - centerZ) * (chunk.Z - centerZ);
                if (selected == null || (chunk.DirectlyDirty && !selected.DirectlyDirty)
                    || (chunk.DirectlyDirty == selected.DirectlyDirty && (distance < bestDistance
                        || (distance == bestDistance && chunk.CheckedAt < selected.CheckedAt))))
                { selected = chunk; bestDistance = distance; }
            }
            if (selected != null) return selected;
            for (int z = centerZ - ChunkRadius; z <= centerZ + ChunkRadius; z++)
                for (int x = centerX - ChunkRadius; x <= centerX + ChunkRadius; x++)
                {
                    if (x < 0 || z < 0 || x * ChunkSize >= map.Size.x || z * ChunkSize >= map.Size.z || chunks.ContainsKey(Key(x, z))) continue;
                    int distance = (x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ);
                    if (distance < bestDistance) { selected = new Chunk(x, z); bestDistance = distance; }
                }
            if (selected != null || now < nextRefresh) return selected;
            foreach (Chunk chunk in chunks.Values)
                if (selected == null || chunk.CheckedAt < selected.CheckedAt) selected = chunk;
            nextRefresh = now + .1f;
            return selected;
        }

        static int Key(int x, int z) { return x | (z << 16); }

        internal static void Invalidate(MapDrawer drawer, IntVec3 cell)
        {
            if (currentMap == null || drawer != currentMap.mapDrawer) return;
            NativeMountains.Invalidate(currentMap, cell);
            // Adjacent cells cover geometry touching a chunk boundary. The existing game
            // invalidation notification is observed only; its original behavior is unchanged.
            for (int z = cell.z - 1; z <= cell.z + 1; z++) for (int x = cell.x - 1; x <= cell.x + 1; x++)
            {
                if (x < 0 || z < 0) continue;
                Chunk chunk;
                if (chunks.TryGetValue(Key(x / ChunkSize, z / ChunkSize), out chunk)) chunk.Dirty = chunk.DirectlyDirty = true;
            }
        }

        static void DrawDoor(Building_Door door, Vector3 position)
        {
            float open = door.Open ? 1f : 0f;
            if (DoorOpenPct != null) open = Mathf.Clamp01((float)DoorOpenPct.GetValue(door, null));
            Quaternion rotation = Quaternion.Euler(0, door.Rotation.AsAngle, 0);
            position.y = .04f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 offset = rotation * new Vector3(side * (.235f + open * .46f), 0, 0);
                NativeLighting.Draw(doorMesh, Matrix4x4.TRS(position + offset, rotation, Vector3.one), material, position);
            }
        }

        static void AddDoorLeaf(Builder builder)
        {
            builder.Box(new Vector3(0, 1.1f, 0), new Vector3(.45f, 2.2f, .15f), Wood);
            builder.Box(new Vector3(0, 1.05f, -.085f), new Vector3(.10f, .48f, .04f), Metal);
        }

        // CPU geometry only; the targeting cache owns these arrays and never creates a mesh.
        internal static void AppendAimGeometry(Builder builder, Thing thing)
        {
            if (thing is Plant plant) { NativeVegetation.AppendAimGeometry(builder, plant); return; }
            Vector3 savedOrigin = builder.Origin;
            Quaternion savedRotation = builder.Rotation;
            try
            {
                AddThing(builder, thing);
                if (!(thing is Building_Door door)) return;
                float open = door.Open ? 1f : 0f;
                if (DoorOpenPct != null) open = Mathf.Clamp01((float)DoorOpenPct.GetValue(door, null));
                Quaternion rotation = Quaternion.Euler(0, door.Rotation.AsAngle, 0);
                Vector3 position = door.DrawPos; position.y = .04f;
                builder.Rotation = rotation;
                for (int side = -1; side <= 1; side += 2)
                {
                    builder.Origin = position + rotation * new Vector3(side * (.235f + open * .46f), 0, 0);
                    AddDoorLeaf(builder);
                }
            }
            finally { builder.Origin = savedOrigin; builder.Rotation = savedRotation; }
        }

        internal static void Dispose()
        {
            foreach (Chunk chunk in chunks.Values) chunk.Dispose();
            chunks.Clear(); expired.Clear(); drawn.Clear(); bloodDefinitions.Clear();
            Destroy(doorMesh); Destroy(material);
            doorMesh = null;
            material = null; currentMap = null; nextRefresh = 0;
            NativeMountains.Dispose();
            NativeAimTargeting.Dispose();
            NativePawns.Dispose(); NativeWeapons.Dispose(); NativeVegetation.Dispose(); NativeCombatEffects.Dispose(); NativeBloodEffects.Dispose(); NativeWeather.Dispose(); NativeLighting.Dispose();
        }
        static void Destroy(UnityEngine.Object value) { if (value != null) UnityEngine.Object.Destroy(value); }

        sealed class Chunk
        {
            internal readonly int X, Z;
            internal Mesh Mesh;
            internal readonly List<Thing> Dynamic = new List<Thing>();
            internal float CheckedAt;
            internal bool Dirty, DirectlyDirty;
            internal float LightCheckedAt = -1f;
            internal bool WasFlashed;
            Color[] baseColors, litColors;
            Vector3[] lightPositions;
            int signature;
            bool built;
            internal Chunk(int x, int z) { X = x; Z = z; }
            internal void Dispose() { NativeMountains.Remove(X, Z); Destroy(Mesh); Mesh = null; Dynamic.Clear(); baseColors = litColors = null; lightPositions = null; }

            internal void Relight(float now)
            {
                LightCheckedAt = now;
                WasFlashed = NativeLighting.HasFlashNear(new Vector3((X + .5f) * ChunkSize, 1f, (Z + .5f) * ChunkSize), ChunkSize);
                if (Mesh == null || baseColors == null) return;
                bool changed = false;
                for (int i = 0; i < baseColors.Length; i++)
                {
                    Color value = baseColors[i] * NativeLighting.Sample(lightPositions[i]); value.a = 1f;
                    Color old = litColors[i];
                    if (Math.Abs(old.r - value.r) + Math.Abs(old.g - value.g) + Math.Abs(old.b - value.b) > .006f)
                    { changed = true; litColors[i] = value; }
                }
                if (changed) Mesh.colors = litColors;
            }

            internal void Refresh(Map map, float now)
            {
                CheckedAt = now;
                Dirty = DirectlyDirty = false;
                Dynamic.Clear();
                int hash = (bloodTrailsVisible ? 17 : 19) ^ NativeMountains.Revision;
                for (int z = Z * ChunkSize; z < Math.Min(map.Size.z, (Z + 1) * ChunkSize); z++)
                    for (int x = X * ChunkSize; x < Math.Min(map.Size.x, (X + 1) * ChunkSize); x++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        NativeMountains.Invalidate(map, cell);
                        bool fog = cell.Fogged(map);
                        unchecked { hash = hash * 31 + (fog ? 1 : cell.GetTerrain(map).shortHash); }
                        if (fog) continue;
                        unchecked
                        {
                            hash = hash * 31 + (map.roofGrid.Roofed(cell) ? 1 : 0);
                            hash = hash * 31 + (int)(map.snowGrid.GetDepth(cell) * 8f);
                        }
                        List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                        foreach (Thing thing in things)
                        {
                            if (thing.Position != cell) continue;
                            if (thing is Building_Door || thing is Corpse || thing is Plant) Dynamic.Add(thing);
                            if (thing is Pawn || !SupportedStatic(thing)) continue;
                            unchecked
                            {
                                hash = hash * 31 + thing.thingIDNumber;
                                hash = hash * 31 + thing.Rotation.AsInt;
                                hash = hash * 31 + thing.DrawColor.GetHashCode();
                                if (thing is Filth filth) hash = hash * 31 + BloodFilthHash(filth);
                                if (thing.def.category == ThingCategory.Item) hash = hash * 31 + NativeObjectModels.ItemStackSignature(thing.stackCount);
                            }
                        }
                    }
                if (built && hash == signature) return;
                Builder builder = new Builder();
                MountainSurfaceCache.Snapshot mountain = NativeMountains.Capture(X, Z);
                int bloodBudget = MaximumBloodSplatsPerChunk;
                for (int z = Z * ChunkSize; z < Math.Min(map.Size.z, (Z + 1) * ChunkSize); z++)
                    for (int x = X * ChunkSize; x < Math.Min(map.Size.x, (X + 1) * ChunkSize); x++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        Vector3 center = new Vector3(x + .5f, 0, z + .5f);
                        if (cell.Fogged(map))
                        {
                            builder.Box(center + new Vector3(0, FogHeight * .5f, 0), new Vector3(1, FogHeight, 1), Fog);
                            foreach (Thing rock in map.thingGrid.ThingsListAtFast(cell))
                                if (rock.Position == cell && NativeMountains.IsRock(rock)) NativeMountains.Add(builder, rock, true, mountain);
                            continue;
                        }
                        string terrain = cell.GetTerrain(map).defName.ToLowerInvariant();
                        Color ground = terrain.Contains("water") ? new Color(.24f, .39f, .42f) : terrain.Contains("sand") ? new Color(.63f, .57f, .40f) : terrain.Contains("soil") || terrain.Contains("mud") ? new Color(.36f, .38f, .24f) : terrain.Contains("ice") || terrain.Contains("snow") ? new Color(.74f, .79f, .79f) : new Color(.46f, .47f, .40f);
                        float variation = .94f + ((x * 17 + z * 31) & 3) * .025f;
                        ground = Color.Lerp(ground, new Color(.85f, .89f, .94f), Mathf.Clamp01(map.snowGrid.GetDepth(cell) * 3f));
                        ground *= variation;
                        builder.Floor(center, ground);
                        if (map.roofGrid.Roofed(cell))
                            builder.Box(center + new Vector3(0, 2.49f, 0), new Vector3(1, .12f, 1), Stone * .8f);
                        int bloodThings = 0;
                        foreach (Thing thing in map.thingGrid.ThingsListAtFast(cell))
                            if (thing.Position == cell && SupportedStatic(thing))
                            {
                                if (thing is Filth blood)
                                {
                                    if (bloodThings++ < MaximumBloodThingsPerCell && bloodBudget > 0)
                                        AddBloodFilth(builder, blood, bloodDefinitions[thing.def], ground, ref bloodBudget);
                                }
                                else AddThing(builder, thing, mountain);
                            }
                    }
                Mesh replacement = builder.Create("RimFront chunk " + X + "," + Z);
                Destroy(Mesh); Mesh = replacement; signature = hash; built = true;
                NativeMountains.Publish(mountain);
                baseColors = builder.Colors.ToArray(); litColors = new Color[baseColors.Length];
                lightPositions = builder.LightingPositions.ToArray();
                Relight(now);
            }
        }

        static bool SupportedStatic(Thing thing) { return !(thing is Corpse) && !(thing is Plant) && (thing is Building || thing.def.category == ThingCategory.Item
            || (bloodTrailsVisible && thing is Filth && bloodDefinitions.ContainsKey(thing.def))); }

        // Native plant cards and stable CPU collision proxies share this growth step.
        internal static float PlantGrowth(float growth)
        {
            if (float.IsNaN(growth) || float.IsInfinity(growth)) return .15f;
            return Mathf.Clamp(Mathf.Floor(Mathf.Clamp01(growth) * 8f) / 8f, .15f, 1f);
        }

        internal static void AddThing(Builder b, Thing thing, MountainSurfaceCache.Snapshot mountain = null)
        {
            if (thing is Plant plant) { NativeVegetation.AppendAimGeometry(b, plant); return; }
            if (NativeMountains.IsRock(thing)) { NativeMountains.Add(b, thing, false, mountain); return; }
            Vector3 pos = thing.DrawPos; pos.y = .04f;
            Quaternion rotation = Quaternion.Euler(0, thing.Rotation.AsAngle, 0);
            b.Origin = pos; b.Rotation = rotation;
            if (NativeObjectModels.TryAdd(b, thing))
            { b.Origin = Vector3.zero; b.Rotation = Quaternion.identity; return; }
            Building_Door door = thing as Building_Door;
            if (door != null)
            {
                b.Box(new Vector3(-.49f, 1.2f, 0), new Vector3(.12f, 2.4f, .34f), Stone);
                b.Box(new Vector3(.49f, 1.2f, 0), new Vector3(.12f, 2.4f, .34f), Stone);
                b.Box(new Vector3(0, 2.35f, 0), new Vector3(1.1f, .16f, .34f), Stone);
            }
            else if (thing is Building)
            {
                string name = thing.def.defName.ToLowerInvariant();
                float w = Math.Max(.5f, thing.def.size.x), d = Math.Max(.5f, thing.def.size.z);
                if (thing.def.fillPercent >= .95f)
                {
                    b.Box(new Vector3(0, 1.2f, 0), new Vector3(w, 2.4f, d), Stone);
                    b.Box(new Vector3(0, 2.42f, 0), new Vector3(w + .025f, .08f, d + .025f), Stone * .83f);
                }
                else if (name.Contains("bed"))
                {
                    b.Box(new Vector3(0, .32f, 0), new Vector3(w * .85f, .4f, d * .9f), Wood);
                    b.Box(new Vector3(0, .57f, .10f), new Vector3(w * .81f, .15f, d * .69f), new Color(.43f, .54f, .59f));
                    b.Box(new Vector3(0, .60f, -d * .31f), new Vector3(w * .68f, .20f, d * .22f), new Color(.79f, .77f, .64f));
                }
                else if (name.Contains("chair") || name.Contains("stool"))
                {
                    b.Box(new Vector3(0, .46f, 0), new Vector3(.64f, .13f, .64f), Wood);
                    b.Box(new Vector3(0, .22f, 0), new Vector3(.35f, .44f, .35f), Wood);
                    if (name.Contains("chair")) b.Box(new Vector3(0, .84f, -.27f), new Vector3(.64f, .68f, .12f), Wood);
                }
                else if (name.Contains("table") || name.Contains("bench"))
                {
                    b.Box(new Vector3(0, .83f, 0), new Vector3(w * .94f, .16f, d * .88f), Wood);
                    for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                        b.Box(new Vector3(x * w * .32f, .40f, z * d * .29f), new Vector3(.12f, .8f, .12f), Wood);
                }
                else if (name.Contains("turret"))
                {
                    b.Box(new Vector3(0, .43f, 0), new Vector3(w * .70f, .86f, d * .70f), Metal);
                    b.Box(new Vector3(0, 1.03f, .2f), new Vector3(.42f, .35f, .9f), Metal * .8f);
                    b.Box(new Vector3(0, 1.07f, .77f), new Vector3(.12f, .12f, .45f), Metal * .55f);
                }
                else if (name.Contains("conduit") || name.Contains("cable"))
                    b.Box(new Vector3(0, .045f, 0), new Vector3(w, .06f, .06f), Metal);
                else if (name.Contains("lamp") || name.Contains("light"))
                {
                    b.Box(new Vector3(0, .6f, 0), new Vector3(.12f, 1.2f, .12f), Metal);
                    b.Box(new Vector3(0, 1.25f, 0), new Vector3(.32f, .25f, .32f), new Color(.86f, .78f, .48f));
                }
                else
                {
                    float height = name.Contains("sandbag") || name.Contains("barricade") ? .65f : thing.def.fillPercent < .1f ? .18f : 1.15f;
                    b.Box(new Vector3(0, height / 2, 0), new Vector3(w * .86f, height, d * .86f), name.Contains("sandbag") ? new Color(.57f, .51f, .34f) : Metal);
                    b.Box(new Vector3(0, height + .03f, 0), new Vector3(w * .72f, .06f, d * .72f), Metal * 1.15f);
                }
            }
            else if (thing.def.category == ThingCategory.Item)
            {
                string name = thing.def.defName.ToLowerInvariant();
                Color color = name.Contains("wood") ? Wood : name.Contains("steel") || name.Contains("component") ? Metal : new Color(.64f, .53f, .31f);
                float height = .13f + Math.Min(4, thing.stackCount / 20) * .055f;
                if (thing.def.IsWeapon) NativeWeapons.AddDropped(b, thing, pos, rotation);
                else b.Box(new Vector3(0, height / 2, 0), new Vector3(.4f, height, .4f), color);
            }
            b.Origin = Vector3.zero; b.Rotation = Quaternion.identity;
        }

        internal static int BloodFilthHash(Filth filth)
        {
            unchecked
            {
                int hash = Mathf.Clamp(filth.thickness, 1, 5);
                if (filth.drawInstances == null) return hash;
                int count = Math.Min(MaximumBloodInstances, filth.drawInstances.Count);
                hash = hash * 31 + count;
                for (int i = 0; i < count; i++)
                {
                    var instance = filth.drawInstances[i];
                    hash = hash * 31 + instance.drawPos.GetHashCode();
                    hash = hash * 31 + instance.rotation.GetHashCode();
                }
                return hash;
            }
        }

        static void RegisterBloodDefinition(ThingDef def, bool smear)
        {
            if (def?.filth == null) return;
            bool previous;
            if (bloodDefinitions.TryGetValue(def, out previous)) bloodDefinitions[def] = previous || smear;
            else if (bloodDefinitions.Count < MaximumBloodDefinitions) bloodDefinitions.Add(def, smear);
        }

        static void AddBloodFilth(Builder builder, Filth filth, bool smear, Color ground, ref int budget)
        {
            int count = Math.Min(Math.Min(MaximumBloodInstances, filth.drawInstances?.Count ?? 0), budget);
            float size = Mathf.Clamp(filth.def.graphicData?.drawSize.x ?? 1f, .25f, 1.5f);
            if (count == 0)
            {
                BuildBloodSplat(builder, filth.DrawPos, filth.Rotation.AsAngle, filth.thingIDNumber, filth.thickness, smear, size, filth.DrawColor, ground);
                budget--;
            }
            else for (int i = 0; i < count; i++)
            {
                var instance = filth.drawInstances[i];
                BuildBloodSplat(builder, instance.drawPos, instance.rotation, filth.thingIDNumber + i * 137,
                    filth.thickness, smear, size, filth.DrawColor, ground);
                budget--;
            }
        }

        internal static void BuildBloodSplat(Builder builder, Vector3 position, float rotation, int seed, int thickness,
            bool smear, float size, Color blood, Color ground)
        {
            int layers = Mathf.Clamp(thickness, 1, 5);
            size = Mathf.Clamp(size, .25f, 1.5f);
            // Blend into this cell's actual ground palette before the usual chunk lighting.
            // Flat polygons avoid opaque box clutter and do not need another render pass.
            Color stain = Color.Lerp(ground, blood, Mathf.Clamp01(blood.a) * (.70f + .06f * layers));
            stain.a = 1;
            Vector3 savedOrigin = builder.Origin; Quaternion savedRotation = builder.Rotation;
            position.y = .032f;
            builder.Origin = position; builder.Rotation = Quaternion.Euler(0, rotation, 0);
            uint random = unchecked((uint)seed * 747796405u + 2891336453u);
            for (int drop = 0; drop < layers + 2; drop++)
            {
                float x = drop == 0 ? 0 : (BloodRandom(ref random) - .5f) * .64f * size;
                float z = drop == 0 ? 0 : (BloodRandom(ref random) - .5f) * .64f * size;
                float radius = (drop == 0 ? .18f + layers * .013f : .025f + BloodRandom(ref random) * .05f) * size;
                Vector3 center = new Vector3(x, drop * .0005f, z);
                for (int side = 0; side < 8; side++)
                {
                    float a = side * Mathf.PI / 4f, next = (side + 1) * Mathf.PI / 4f;
                    float rx = radius * (smear ? .55f : 1f), rz = radius * (smear ? 1.65f : .83f);
                    builder.Triangle(center, center + new Vector3(Mathf.Sin(a) * rx, 0, Mathf.Cos(a) * rz),
                        center + new Vector3(Mathf.Sin(next) * rx, 0, Mathf.Cos(next) * rz), stain);
                }
            }
            builder.Origin = savedOrigin; builder.Rotation = savedRotation;
        }

        static float BloodRandom(ref uint state)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (state >> 8) * (1f / 16777216f);
        }

        // Flat faces carry baked directional shading, avoiding native light/shader dependence.
        // All geometry is built in managed buffers, then uploaded only when its chunk changes.
        internal sealed class Builder
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector3> LightingPositions = new List<Vector3>();
            internal readonly List<int> Triangles = new List<int>();
            internal readonly List<Color> Colors = new List<Color>();
            readonly List<Vector2> uv = new List<Vector2>();
            internal Vector3 Origin;
            internal Quaternion Rotation = Quaternion.identity;
            internal void Floor(Vector3 p, Color color)
            {
                Quad(p + new Vector3(-.5f, .025f, -.5f), p + new Vector3(-.5f, .025f, .5f), p + new Vector3(.5f, .025f, .5f), p + new Vector3(.5f, .025f, -.5f), color);
            }
            internal void Box(Vector3 c, Vector3 size, Color color)
            {
                Vector3 h = size * .5f;
                Vector3 a = c + new Vector3(-h.x, -h.y, -h.z), b = c + new Vector3(h.x, -h.y, -h.z), d = c + new Vector3(-h.x, -h.y, h.z), e = c + new Vector3(h.x, -h.y, h.z);
                Vector3 up = new Vector3(0, size.y, 0);
                Quad(a, a + up, b + up, b, color * .78f);
                Quad(e, e + up, d + up, d, color * .90f);
                Quad(d, d + up, a + up, a, color * .68f);
                Quad(b, b + up, e + up, e, color * .83f);
                Quad(a + up, d + up, e + up, b + up, color);
                Quad(d, a, b, e, color * .55f);
            }
            internal void Pyramid(Vector3 baseCenter, float width, float height, Color color)
            {
                float h = width / 2;
                Vector3 a = baseCenter + new Vector3(-h, 0, -h), b = baseCenter + new Vector3(h, 0, -h), c = baseCenter + new Vector3(h, 0, h), d = baseCenter + new Vector3(-h, 0, h), tip = baseCenter + new Vector3(0, height, 0);
                Triangle(a, tip, b, color * .78f); Triangle(b, tip, c, color * .9f); Triangle(c, tip, d, color); Triangle(d, tip, a, color * .68f);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                Vector3 sample = Origin + Rotation * ((a + b + c + d) * .25f + Vector3.Cross(b - a, c - a).normalized * .08f);
                int i = Vertices.Count; Add(a, color, sample); Add(b, color, sample); Add(c, color, sample); Add(d, color, sample);
                Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2); Triangles.Add(i); Triangles.Add(i + 2); Triangles.Add(i + 3);
            }
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                Vector3 sample = Origin + Rotation * ((a + b + c) / 3f + Vector3.Cross(b - a, c - a).normalized * .08f);
                int i = Vertices.Count; Add(a, color, sample); Add(b, color, sample); Add(c, color, sample); Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
            }
            void Add(Vector3 vertex, Color color, Vector3 lightPosition) { color.a = 1; Vertices.Add(Origin + Rotation * vertex); LightingPositions.Add(lightPosition); Colors.Add(color); uv.Add(Vector2.zero); }
            internal Mesh Create(string name)
            {
                if (Vertices.Count == 0) return null;
                Mesh mesh = new Mesh { name = name };
                // Dense modded cells can exceed Unity's default 16-bit index range.
                if (Vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = Vertices.ToArray(); mesh.triangles = Triangles.ToArray(); mesh.colors = Colors.ToArray(); mesh.uv = uv.ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }

    // Rendering only: ticking, construction, pathfinding and mesh invalidation remain game-owned.
    // Prevent orthographic roof/terrain planes from covering the native perspective scene.
    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh))]
    internal static class NativeSceneSuppressFlatMap
    {
        static bool Prefix() { return !NativeView.Active; }
    }

    [HarmonyPatch]
    internal static class NativeSceneObserveMapChanges
    {
        // Both the 1.6 reference API and installed 1.7 expose two MapMeshDirty
        // overloads with an IntVec3 first argument. Observe both verified signatures.
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(MapDrawer).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != nameof(MapDrawer.MapMeshDirty)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length > 0 && parameters[0].ParameterType == typeof(IntVec3)) yield return method;
            }
        }
        static void Postfix(MapDrawer __instance, IntVec3 __0) { NativeScene.Invalidate(__instance, __0); }
    }
}
