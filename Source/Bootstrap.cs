using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimFront
{
    public sealed class RimFrontMod : Mod
    {
        internal static RimFrontSettings Settings;
        public RimFrontMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimFrontSettings>();
            new Harmony("rimfront.nativefps").PatchAll(typeof(RimFrontMod).Assembly);
            NativeManualAim.Initialize();
            Log.Message("[RimFront] Native 3D renderer loaded; Harmony hooks installed. Version "
                + typeof(RimFrontMod).Assembly.GetName().Version.ToString(3) + ".");
        }
        public override string SettingsCategory() => "RimFront";
        public override void DoSettingsWindowContents(Rect inRect) => Settings.Draw(inRect);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class PawnControlGizmo
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (!RimFrontGame.CanControl(__instance)) yield break;
            RimFrontGame session = Current.Game?.GetComponent<RimFrontGame>();
            if (session == null) yield break;
            bool controlled = session.Controls(__instance);
            yield return new Command_Action
            {
                defaultLabel = controlled ? "Return to colony view" : "Play in RimFront",
                defaultDesc = "Enter native first-person 3D inside RimWorld. WASD moves; mouse looks; left mouse fires; R reloads. Escape returns to colony view. Combat and movement use the game's own rules.",
                icon = TexCommand.Attack,
                action = () =>
                {
                    if (controlled) session.Release("Control released in RimWorld.");
                    else session.Control(__instance);
                }
            };
        }
    }
}
