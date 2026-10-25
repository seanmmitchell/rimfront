using System.Collections.Generic;
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }
namespace Verse
{
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> value, string label, LookMode mode)
        {
            if (mode == LookMode.Value)
            {
                if (Scribe.mode == LoadSaveMode.Saving) TestScribe.Node[label] = new List<T>(value);
                else if (Scribe.mode == LoadSaveMode.LoadingVars) value = new List<T>((List<T>)TestScribe.Node[label]);
                return;
            }
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var children = new List<Dictionary<string, object>>();
                foreach (IExposable item in value) children.Add(TestScribe.Capture(item));
                TestScribe.Node[label] = children;
            }
            else if (TestScribe.Node.TryGetValue(label, out object data))
            {
                var children = (List<Dictionary<string, object>>)data;
                if (Scribe.mode == LoadSaveMode.LoadingVars) value = new List<T>();
                for (int i = 0; i < children.Count; i++)
                {
                    if (Scribe.mode == LoadSaveMode.LoadingVars) value.Add((T)System.Activator.CreateInstance(typeof(T), true));
                    TestScribe.Visit((IExposable)value[i], children[i]);
                }
            }
        }
    }
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default)
        {
            if (Scribe.mode == LoadSaveMode.Saving) TestScribe.Node[label] = value;
            else if (Scribe.mode == LoadSaveMode.LoadingVars) value = TestScribe.Node.TryGetValue(label, out var saved) ? (T)saved : defaultValue;
        }
    }
    public static class Scribe_References
    {
        // Native engine references are retained; this boundary tests RimFront's
        // snapshot fields, not RimWorld's object resolver or save XML format.
        public static void Look<T>(ref T value, string label) where T : class => Scribe_Values.Look(ref value, label);
    }
    public static class Scribe_Deep
    {
        public static void Look<T>(ref T value, string label, params object[] args)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            { TestScribe.Node[label] = value is IExposable item ? TestScribe.Capture(item) : null; }
            else if (TestScribe.Node.TryGetValue(label, out var child) && child is Dictionary<string, object> node)
            {
                if (Scribe.mode == LoadSaveMode.LoadingVars) value = (T)System.Activator.CreateInstance(typeof(T), true);
                TestScribe.Visit((IExposable)value, node);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars) value = default;
        }
    }
    public static class TestScribe
    {
        internal static Dictionary<string, object> Node;
        internal static Dictionary<string, object> Capture(IExposable item)
        { var node = new Dictionary<string, object>(); Visit(item, node); return node; }
        internal static void Visit(IExposable item, Dictionary<string, object> node)
        { var previous = Node; Node = node; try { item.ExposeData(); } finally { Node = previous; } }
        internal static Dictionary<string, object> Save(IExposable item)
        { Scribe.mode = LoadSaveMode.Saving; try { return Capture(item); } finally { Scribe.mode = LoadSaveMode.Inactive; } }
        internal static void Load(IExposable item, Dictionary<string, object> node)
        {
            try { Scribe.mode = LoadSaveMode.LoadingVars; Visit(item, node); Scribe.mode = LoadSaveMode.PostLoadInit; Visit(item, node); }
            finally { Scribe.mode = LoadSaveMode.Inactive; }
        }
    }
}
namespace RimFront
{
    // Only transport context is simulated. Production registry, dispatcher, input
    // lease, and firing controller are linked unchanged into this executable.
    internal static class MultiplayerBridge
    {
        internal static bool InSession = true, Available = true, SharedColony = true, Interface = true, Hosting = true;
        internal static int LocalPlayer;
        internal static int? Sender;
        internal static object SessionToken = new object();
        internal static string Failure;
        internal static bool Paused => Verse.Find.TickManager.Paused;
        internal static HashSet<int> ConnectedPlayers() => new HashSet<int> { 0, 1, 2 };
    }
    internal static class ViewRange { internal static int Step(int value) => System.Math.Clamp(value, 0, 2); }
}
