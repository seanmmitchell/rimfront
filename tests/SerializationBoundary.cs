// Compile-time engine boundary for source-linked portable suites. Native Scribe is
// exercised separately; these no-op calls do not constitute save/load coverage.
// MultiplayerTests supplies its own phase-aware in-memory serializer instead.
namespace Verse
{
    public interface IExposable { void ExposeData(); }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars, ResolvingCrossRefs, PostLoadInit }
    public enum LookMode { Value, Deep }
    public static class Scribe { public static LoadSaveMode mode; }
#if !MULTIPLAYER_FIXTURE
    public static class Scribe_Values
    { public static void Look<T>(ref T value, string label, T defaultValue = default) { } }
    public static class Scribe_References
    { public static void Look<T>(ref T value, string label) where T : class { } }
    public static class Scribe_Deep
    { public static void Look<T>(ref T value, string label, params object[] args) { } }
    public static class Scribe_Collections
    { public static void Look<T>(ref System.Collections.Generic.List<T> value, string label, LookMode mode) { } }
#endif
}
