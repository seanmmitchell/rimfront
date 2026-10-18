using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestNativeMinimap(Assembly assembly)
    {
        Type renderer = assembly.GetType("RimFront.NativeMapRenderer", true);
        object state = renderer.GetField("State", Flags).GetValue(null);
        Type viewType = assembly.GetType("RimFront.MinimapView", true);
        object view = viewType.GetMethod("Frame", Flags).Invoke(null, new object[] { 100f, 100f, true, 160f, 120f });
        MethodInfo run = state.GetType().GetMethod("Run", Flags);
        Type cameraType = vector.Assembly.GetType("UnityEngine.Camera", true);
        object camera = RuntimeHelpers.GetUninitializedObject(cameraType);
        FieldInfo cameraField = renderer.GetField("renderCamera", Flags);
        object previous = cameraField.GetValue(null);
        Type game = Assembly.Load("Assembly-CSharp").GetType("Verse.CameraDriver", true);
        object driver = RuntimeHelpers.GetUninitializedObject(game);
        Type harmonyType = Assembly.Load("0Harmony").GetType("HarmonyLib.Harmony", true);
        object harmony = Activator.CreateInstance(harmonyType, new object[] { "rimfront.tests.native-minimap" });
        string[] hooks = { "NativeMinimapCamera", "NativeMinimapDriverCamera", "NativeMinimapRootSize", "NativeCameraViewRect" };
        try
        {
            cameraField.SetValue(null, camera);
            foreach (string hook in hooks)
            {
                object processor = harmonyType.GetMethod("CreateClassProcessor").Invoke(harmony, new[] { (object)assembly.GetType("RimFront." + hook, true) });
                processor.GetType().GetMethod("Patch").Invoke(processor, null);
            }
            Action draw = () =>
            {
                Type find = game.Assembly.GetType("Verse.Find", true);
                Check(ReferenceEquals(find.GetProperty("Camera", Flags).GetValue(null), camera), "patched native Find.Camera resolves to the offscreen camera");
                Check(ReferenceEquals(game.GetProperty("MyCamera", Flags).GetValue(driver), camera), "patched CameraDriver camera agrees with Find.Camera");
                float size = (float)viewType.GetField("Size", Flags).GetValue(view);
                Check((float)game.GetProperty("RootSize", Flags).GetValue(driver) == size * .5f,
                    "native zoom uses minimap coverage instead of the FPS camera's saved colony zoom");
                object rect = game.GetProperty("CurrentViewRect", Flags).GetValue(driver);
                int minimum = (int)rect.GetType().GetField("minX").GetValue(rect);
                Check(minimum <= 75, "native culling includes the pawn neighbourhood while framing distant aim");
            };
            run.Invoke(state, new[] { view, (object)draw });
            Check(!(bool)state.GetType().GetProperty("Active", Flags).GetValue(state), "successful native pass releases all patched camera getters");
            bool failed = false;
            try { run.Invoke(state, new[] { view, (object)(Action)(() => { draw(); throw new InvalidOperationException("injected native draw failure"); }) }); }
            catch (TargetInvocationException e) { failed = e.InnerException is InvalidOperationException; }
            Check(failed && !(bool)state.GetType().GetProperty("Active", Flags).GetValue(state), "native camera getters restore after an actual patched-pass exception");
            foreach (string hook in new[] { "NativeMinimapCamera", "NativeMinimapDriverCamera", "NativeMinimapRootSize" })
            {
                object[] args = hook == "NativeMinimapRootSize" ? new object[] { 123f } : new[] { camera };
                Check((bool)assembly.GetType("RimFront." + hook, true).GetMethod("Prefix", Flags).Invoke(null, args),
                    "inactive native minimap returns control to original getter: " + hook);
            }
            Type graphics = vector.Assembly.GetType("UnityEngine.Graphics", true);
            MethodInfo wrapper = null;
            foreach (MethodInfo candidate in graphics.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (candidate.Name != "DrawMesh") continue;
                bool hasCamera = false, hasLayer = false;
                foreach (ParameterInfo parameter in candidate.GetParameters())
                { hasCamera |= parameter.ParameterType == cameraType; hasLayer |= parameter.Name == "layer" && parameter.ParameterType == typeof(int); }
                if (hasCamera && hasLayer) { wrapper = candidate; break; }
            }
            Check(wrapper != null, "native draw wrapper exposes camera and layer destination");
            ParameterInfo[] parameters = wrapper.GetParameters();
            int cameraIndex = Array.FindIndex(parameters, p => p.ParameterType == cameraType);
            int layerIndex = Array.FindIndex(parameters, p => p.Name == "layer" && p.ParameterType == typeof(int));
            MethodInfo route = assembly.GetType("RimFront.NativeMinimapDrawDestination", true).GetMethod("Route", Flags);
            object source = RuntimeHelpers.GetUninitializedObject(cameraType), foreign = RuntimeHelpers.GetUninitializedObject(cameraType);
            foreach (object existing in new[] { null, source, camera, foreign })
            {
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < arguments.Length; i++) arguments[i] = new object();
                arguments[cameraIndex] = existing; arguments[layerIndex] = 9;
                object[] saved = (object[])arguments.Clone();
                route.Invoke(null, new object[] { wrapper, arguments, camera, source, 31 });
                bool unrelated = ReferenceEquals(existing, foreign);
                Check(ReferenceEquals(arguments[cameraIndex], unrelated ? foreign : camera) && (int)arguments[layerIndex] == (unrelated ? 9 : 31),
                    "native destination routing isolates map draws while preserving an explicit foreign atlas camera");
                for (int i = 0; i < arguments.Length; i++)
                    if (i != cameraIndex && i != layerIndex)
                        Check(ReferenceEquals(arguments[i], saved[i]), "native destination routing preserves material, geometry and draw properties");
            }
            object[] noMap = { null, view, null };
            Check(!(bool)renderer.GetMethod("TryRender", Flags).Invoke(null, noMap) && noMap[2] == null,
                "absent map cannot start a GPU pass or reuse a stale minimap texture");
        }
        finally
        {
            cameraField.SetValue(null, previous);
            harmonyType.GetMethod("UnpatchAll").Invoke(harmony, new object[] { "rimfront.tests.native-minimap" });
        }
    }
}