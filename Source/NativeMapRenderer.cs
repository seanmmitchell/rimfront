using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
#if RW17
using UnityStubs.Rendering;
using MeshDrawCommand = Waylith.Rendering.MeshDrawCommand;
using MeshDrawSubmission = Waylith.Rendering.MeshDrawSubmission;
#endif

namespace RimFront
{
    // Draw the game's own colony scene into an owned target. Do not call MapUpdate:
    // simulation, weather updates and mesh invalidation still run once in the game loop.
    internal static class NativeMapRenderer
    {
        internal const int Resolution = 256;
        internal const int MaximumDynamicThings = 256;
        static readonly List<Thing> dynamicThings = new List<Thing>(MaximumDynamicThings);
#if RW17
        static readonly List<MeshDrawCommand> commands = new List<MeshDrawCommand>(2048);
#endif
        internal static readonly MinimapRenderState State = new MinimapRenderState();
        internal static bool Rendering => State.Active;
        internal static Camera RenderCamera => renderCamera;
        static Camera renderCamera;
        static RenderTexture target;
#if !RW17
        internal const int RenderLayer = 31;
        internal static Camera SourceCamera { get; private set; }
#endif
        internal static bool TryRender(Map map, MinimapView view, out Texture result)
        {
            result = null;
            if (map == null || map.Disposed || Rendering || Find.CameraDriver == null) return false;
#if RW17
            // BeginCapture is not nestable. Never steal another renderer's capture.
            if (CameraTextureRenderer.Current == null || MeshDrawSubmission.IsCapturing) return false;
#endif
#if !RW17
            if (!NativeMinimapDrawDestination.Initialize()) return false;
#endif
            RenderTexture previous = RenderTexture.active;
            try
            {
                EnsureTarget();
                renderCamera.transform.position = new Vector3(view.MinX + view.Size * .5f, 40f, view.MinZ + view.Size * .5f);
                renderCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                renderCamera.orthographicSize = view.Size * .5f;
#if !RW17
                SourceCamera = Find.Camera;
#endif
                State.Run(view, () =>
                {
#if RW17
                    // Camera.Render would also copy pending first-person draws in 1.7.
                    // Capture only this native pass, including its property blocks and
                    // instanced/batched submissions, then use the game's target renderer.
                    commands.Clear();
                    using (MeshDrawSubmission.BeginCapture(commands)) DrawMap(map);
                    CameraTextureRenderer.Current.Render(target, commands, CameraViewFactory.GetView(renderCamera),
                        new Rect(0, 0, 1, 1), renderCamera.backgroundColor);
#else
                    // Every stock draw is bound to this camera and an isolated layer.
                    // Existing first-person submissions use layer 0 and cannot enter it.
                    DrawMap(map);
                    renderCamera.Render();
#endif
                });
                result = target;
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
#if !RW17
                SourceCamera = null;
#endif
            }
        }

        static void EnsureTarget()
        {
            if (renderCamera == null)
            {
                var owner = new GameObject("RimFront native minimap camera");
#if !RW17
                owner.hideFlags = HideFlags.HideAndDontSave;
#endif
                renderCamera = owner.AddComponent<Camera>();
                renderCamera.enabled = false;
                renderCamera.orthographic = true;
                renderCamera.aspect = 1f;
                renderCamera.nearClipPlane = .1f;
                renderCamera.farClipPlane = 100f;
                renderCamera.clearFlags = CameraClearFlags.SolidColor;
                renderCamera.backgroundColor = new Color(.015f, .02f, .025f, 1f);
#if !RW17
                renderCamera.cullingMask = 1 << RenderLayer;
#endif
            }
            if (target == null)
            {
                target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
                { name = "RimFront native minimap", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                renderCamera.targetTexture = target;
            }
            if (!target.IsCreated() && !target.Create()) throw new InvalidOperationException("Cannot create the native minimap target.");
        }

        static void DrawMap(Map map)
        {
            map.mapDrawer.DrawMapMesh();
            // Never start a second whole-colony cull, pawn-atlas preparation or
            // parallel drawing job from OnGUI. Keep native art for local doors,
            // items and buildings; small pawn dots are drawn by the HUD instead.
            dynamicThings.Clear();
            CellRect rect = ViewRect();
            for (int z = rect.minZ; z <= rect.maxZ && dynamicThings.Count < MaximumDynamicThings; z++)
            for (int x = rect.minX; x <= rect.maxX && dynamicThings.Count < MaximumDynamicThings; x++)
            {
                var cell = new IntVec3(x, 0, z);
                if (cell.Fogged(map)) continue;
                foreach (Thing thing in map.thingGrid.ThingsListAtFast(cell))
                {
                    if (dynamicThings.Count >= MaximumDynamicThings) break;
                    if (thing.Position != cell || thing is Pawn || thing is Corpse || !thing.Spawned
                        || thing.def.drawerType == DrawerType.MapMeshOnly || thing.def.drawerType == DrawerType.None) continue;
                    dynamicThings.Add(thing);
                }
            }
            // Preserve the native phase order, using one bounded local list.
            foreach (Thing thing in dynamicThings) thing.DynamicDrawPhase(DrawPhase.EnsureInitialized);
            foreach (Thing thing in dynamicThings) thing.DynamicDrawPhase(DrawPhase.ParallelPreDraw);
            foreach (Thing thing in dynamicThings) thing.DynamicDrawPhase(DrawPhase.Draw);
            MapEdgeClipDrawer.DrawClippers(map);
        }

        internal static CellRect ViewRect()
        {
            MinimapView view = State.View;
            var rect = CellRect.FromLimits(Mathf.FloorToInt(view.MinX), Mathf.FloorToInt(view.MinZ),
                Mathf.CeilToInt(view.MinX + view.Size), Mathf.CeilToInt(view.MinZ + view.Size));
            if (Find.CurrentMap != null) rect.ClipInsideMap(Find.CurrentMap);
            return rect;
        }

        internal static void Dispose()
        {
            if (renderCamera != null)
            {
                renderCamera.targetTexture = null;
                UnityEngine.Object.Destroy(renderCamera.gameObject);
            }
            renderCamera = null;
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            target = null;
            dynamicThings.Clear();
#if RW17
            commands.Clear();
#endif
        }
    }

    // Scope camera queries and zoom/culling to the minimap without moving or
    // rewriting the first-person camera or CameraDriver's cached colony state.
    [HarmonyPatch(typeof(Find), nameof(Find.Camera), MethodType.Getter)]
    internal static class NativeMinimapCamera
    {
        static bool Prefix(ref Camera __result)
        {
            if (!NativeMapRenderer.Rendering) return true;
            __result = NativeMapRenderer.RenderCamera; return false;
        }
    }
    [HarmonyPatch(typeof(CameraDriver), "MyCamera", MethodType.Getter)]
    internal static class NativeMinimapDriverCamera
    {
        static bool Prefix(ref Camera __result)
        {
            if (!NativeMapRenderer.Rendering) return true;
            __result = NativeMapRenderer.RenderCamera; return false;
        }
    }
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.RootSize), MethodType.Getter)]
    internal static class NativeMinimapRootSize
    {
        static bool Prefix(ref float __result)
        {
            if (!NativeMapRenderer.Rendering) return true;
            __result = NativeMapRenderer.State.View.Size * .5f; return false;
        }
    }
    // Unity 1.6's managed wrappers retain materials/property blocks, submeshes,
    // instancing and native shaders. Change only their destination during this pass.
    internal static class NativeMinimapDrawDestination
    {
        static readonly Dictionary<MethodBase, int[]> destinations = new Dictionary<MethodBase, int[]>();
#if !RW17
        static bool initialized, supported;
        internal static bool Initialize()
        {
            if (initialized) return supported;
            initialized = true;
            var harmony = new Harmony("rimfront.native-minimap-destination");
            try
            {
                int count = 0;
                foreach (MethodBase method in TargetMethods())
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeMinimapDrawDestination), nameof(Prefix)));
                    count++;
                }
                if (count == 0) throw new NotSupportedException("No native camera-bound draw wrappers.");
                supported = true;
            }
            catch (Exception error)
            {
                harmony.UnpatchAll("rimfront.native-minimap-destination");
                Log.Warning("[RimFront] Native minimap unavailable: " + error.GetBaseException().Message);
            }
            return supported;
        }
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(Graphics).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "DrawMesh" && method.Name != "DrawMeshInstanced" && method.Name != "DrawMeshInstancedIndirect") continue;
                int[] indices = Indices(method);
                if (indices[0] >= 0 && indices[1] >= 0 && method.GetMethodBody() != null) yield return method;
            }
        }
        static void Prefix(MethodBase __originalMethod, object[] __args)
        {
            if (NativeMapRenderer.Rendering)
                Route(__originalMethod, __args, NativeMapRenderer.RenderCamera, NativeMapRenderer.SourceCamera, NativeMapRenderer.RenderLayer);
        }
#endif
        static int[] Indices(MethodBase method)
        {
            if (destinations.TryGetValue(method, out int[] found)) return found;
            var result = new[] { -1, -1 };
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(Camera)) result[0] = i;
                else if (parameters[i].Name == "layer" && parameters[i].ParameterType == typeof(int)) result[1] = i;
            }
            destinations.Add(method, result);
            return result;
        }
        internal static void Route(MethodBase method, object[] args, Camera destination, Camera source, int layer)
        {
            int[] indices = Indices(method);
            if (indices[0] < 0 || indices[1] < 0) return;
            // Pawn-atlas and other offscreen cameras may run inside native drawing.
            // Keep their explicit destinations and layers intact.
            if (args[indices[0]] is Camera camera && !ReferenceEquals(camera, destination) && !ReferenceEquals(camera, source)) return;
            args[indices[0]] = destination;
            args[indices[1]] = layer;
        }
    }
}
