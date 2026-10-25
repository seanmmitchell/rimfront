using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static Type MultiplayerType;
        public static Type TypeByName(string name) => name == "Multiplayer.Client.Multiplayer" ? MultiplayerType : null;
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        public static PropertyInfo Property(Type type, string name) => type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        public static MethodInfo Method(Type type, string name, Type[] args = null) => type?.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, args ?? Type.EmptyTypes, null);
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public sealed class Harmony
    {
        public static int Patches;
        public Harmony(string id) { }
        public void Patch(MethodInfo method, HarmonyMethod prefix, HarmonyMethod finalizer) { Patches++; }
    }
}
namespace Verse
{
    public sealed class StaticConstructorOnStartupAttribute : Attribute { }
    public sealed class AssemblyHandler { public readonly List<Assembly> loadedAssemblies = new List<Assembly>(); }
    public sealed class ModContentPack { public string RootDir; public readonly AssemblyHandler assemblies = new AssemblyHandler(); }
    public static class LoadedModManager { public static readonly List<ModContentPack> RunningMods = new List<ModContentPack>(); }
    public static class Log { public static void Warning(string message) { } }
}
namespace RimFront
{
    internal static class RimFrontMultiplayer
    {
        internal static void Claim() { } internal static void Input() { } internal static void End() { }
        internal static void SetRules() { } internal static void ResetSession() { } internal static void Disconnect() { }
    }
}
