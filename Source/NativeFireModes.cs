using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimFront
{
    // CE retains its own mode order, multiplayer hook, persistence and weapon rules.
    // No shared VerbProperties or native enum fields are written by this adapter.
    internal static class NativeFireModes
    {
        static readonly Type CompType = AccessTools.TypeByName("CombatExtended.CompFireModes");
        static readonly Contract Api = new Contract(CompType);

        static bool Melee(Pawn pawn) => pawn?.equipment?.Primary == null || !pawn.equipment.Primary.def.IsRangedWeapon;
        static object Comp(Pawn pawn)
        {
            if (CompType == null || Melee(pawn)) return null;
            var comps = pawn.equipment.Primary.AllComps;
            if (comps != null) foreach (ThingComp comp in comps) if (CompType.IsInstanceOfType(comp)) return comp;
            return null;
        }
        internal static string Describe(Pawn pawn)
        {
            if (Melee(pawn)) return "Melee";
            object comp = Comp(pawn);
            return comp == null ? "Fixed" : Api.TryRead(comp, out string mode, out _) ? Label(mode) : "Unavailable";
        }
        internal static bool CanCycle(Pawn pawn)
            => Api.TryRead(Comp(pawn), out _, out int count) && count > 1;
        internal static bool TryCycle(Pawn pawn, out string message)
        {
            object comp = Comp(pawn);
            if (comp == null) { message = Melee(pawn) ? "Melee weapons have no fire mode." : "This weapon has a fixed fire mode."; return false; }
            return Api.TryCycle(comp, out message);
        }
        internal static bool RequiresTriggerPress(Pawn pawn)
            => Api.TryRead(Comp(pawn), out string mode, out _) && RequiresPress(mode);
        internal static bool RequiresPress(string mode) => mode == "SingleFire" || mode == "BurstFire";
        internal static string Label(string mode) => mode == "SingleFire" ? "Semi-auto"
            : mode == "BurstFire" ? "Burst" : mode == "AutoFire" ? "Auto" : "Unavailable";

        // A small injectable reflection boundary permits testing the actual read/cycle
        // path with managed fixtures, without loading CE or running its simulation.
        internal sealed class Contract
        {
            readonly Type type, modeType;
            readonly PropertyInfo current, available;
            readonly MethodInfo toggle;
            readonly bool supported;
            internal Contract(Type type)
            {
                this.type = type;
                if (type == null) return;
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                    current = type.GetProperty("CurrentFireMode", flags);
                    available = type.GetProperty("AvailableFireModes", flags);
                    toggle = type.GetMethod("ToggleFireMode", flags, null, Type.EmptyTypes, null);
                    modeType = current?.PropertyType;
                    supported = modeType != null && modeType.IsEnum && current.GetGetMethod() != null
                        && current.GetIndexParameters().Length == 0 && available?.GetGetMethod() != null
                        && available.GetIndexParameters().Length == 0
                        && available.PropertyType == typeof(List<>).MakeGenericType(modeType)
                        && toggle?.ReturnType == typeof(void)
                        && Enum.IsDefined(modeType, "AutoFire") && Enum.IsDefined(modeType, "BurstFire")
                        && Enum.IsDefined(modeType, "SingleFire");
                }
                catch { supported = false; }
            }
            internal bool TryRead(object comp, out string mode, out int count)
            {
                mode = null; count = 0;
                if (!supported || comp == null || !type.IsInstanceOfType(comp)) return false;
                try
                {
                    object value = current.GetValue(comp, null);
                    string name = value == null ? null : Enum.GetName(modeType, value);
                    IList modes = available.GetValue(comp, null) as IList;
                    if (Label(name) == "Unavailable" || modes == null || modes.Count == 0 || modes.Count > 16) return false;
                    bool found = false;
                    var distinct = new HashSet<string>();
                    foreach (object entry in modes)
                    {
                        string item = entry == null ? null : Enum.GetName(modeType, entry);
                        if (Label(item) == "Unavailable" || !distinct.Add(item)) return false;
                        found |= Equals(value, entry);
                    }
                    if (!found) return false;
                    mode = name; count = modes.Count; return true;
                }
                catch { return false; }
            }
            internal bool TryCycle(object comp, out string message)
            {
                message = "Fire mode is unavailable for this weapon.";
                if (!TryRead(comp, out _, out int count)) return false;
                if (count <= 1) { message = "This weapon has a fixed fire mode."; return false; }
                try
                {
                    toggle.Invoke(comp, null);
                    if (!TryRead(comp, out string mode, out _)) return false;
                    message = "Fire mode: " + Label(mode) + ".";
                    return true;
                }
                catch { message = "Combat Extended could not change this weapon's fire mode."; return false; }
            }
        }
    }
}
