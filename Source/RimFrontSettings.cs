using UnityEngine;
using Verse;

namespace RimFront
{
    public sealed class RimFrontSettings : ModSettings
    {
        public int extraViewRange = 0;
        public bool analogMovement = true;
        public bool movingFire = true;
        public bool animatePawns = true;
        public bool spritePawns = false;
        public bool shieldHaze = true;
        public bool shieldImpacts = true;
        public bool shieldImpactsInColony = false;
        public bool bloodEffects = true;
        public bool goreEffects = true;
        public bool bloodTrails = true;
        public bool impactEffects = true;
        public bool pawnIndicators = true;
        public bool traceFiring = false;
        Vector2 scrollPosition;
        float contentHeight = 600f;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref extraViewRange, "extraViewRange", 0);
            extraViewRange = ViewRange.Step(extraViewRange);
            Scribe_Values.Look(ref analogMovement, "analogMovement", true);
            Scribe_Values.Look(ref movingFire, "movingFire", true);
            Scribe_Values.Look(ref animatePawns, "animatePawns", true);
            Scribe_Values.Look(ref spritePawns, "spritePawns", false);
            Scribe_Values.Look(ref shieldHaze, "shieldHaze", true);
            Scribe_Values.Look(ref shieldImpacts, "shieldImpacts", true);
            Scribe_Values.Look(ref shieldImpactsInColony, "shieldImpactsInColony", false);
            Scribe_Values.Look(ref bloodEffects, "bloodEffects", true);
            Scribe_Values.Look(ref goreEffects, "goreEffects", true);
            Scribe_Values.Look(ref bloodTrails, "bloodTrails", true);
            Scribe_Values.Look(ref impactEffects, "impactEffects", true);
            Scribe_Values.Look(ref pawnIndicators, "pawnIndicators", true);
            Scribe_Values.Look(ref traceFiring, "traceFiring", false);
            base.ExposeData();
        }
        internal void Draw(Rect rect)
        {
            Rect content = new Rect(0, 0, Mathf.Max(1f, rect.width - 20f), contentHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, content);
            Listing_Standard listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(content);
            listing.Label("View range");
            extraViewRange = ViewRange.Step(extraViewRange);
            listing.Label("Additional view range: " + (extraViewRange * 50) + "% (" + ViewRange.DistanceFor(extraViewRange) + " tiles)");
            extraViewRange = Mathf.RoundToInt(listing.Slider(extraViewRange, 0f, 2f));
            listing.Label("0% = 60 tiles (twice the original range); 50% = 90; 100% = 120. Applies immediately. Longer ranges increase rendering work and cache warmup.");
            listing.Gap();
            listing.Label("Movement mode");
            listing.CheckboxLabeled("True analog movement (default)", ref analogMovement,
                "Continuous movement with swept collisions and CE position integration. Disable to use native path movement.");
            listing.Label("Changes apply the next time you enter RimFront. An unsupported CE adapter automatically uses native movement.");
            listing.Gap();
            listing.CheckboxLabeled("Move while firing (default)", ref movingFire,
                "Fire ranged weapons while walking at half speed. Applies immediately. Disable to make movement cancel firing. Reloads and bipod setup remain stationary.");
            listing.Label("Hold Left Ctrl to crouch behind cover. Crouching also halves walking speed.");
            listing.Gap();
            listing.Label("Character rendering");
            listing.CheckboxLabeled("Use viewpoint-based 2D characters", ref spritePawns,
                "Replace nearby 3D characters with upright native sprites. Front, side and back artwork follows your viewpoint. Applies immediately; disable to restore 3D models. First-person hands stay 3D. Unavailable artwork falls back to 3D.");
            listing.CheckboxLabeled("Animate nearby 3D pawns", ref animatePawns,
                "Animate limbs and clothing on nearby pawns. Disable to reduce rendering work while keeping their dressed models visible.");
            listing.CheckboxLabeled("Pawn status indicators", ref pawnIndicators,
                "Label visible colonists, friends, guests, prisoners and hostiles. Prison breaks flash orange/red. You can deliberately fire at any faction.");
            listing.Gap();
            listing.Label("Shield effects");
            listing.CheckboxLabeled("Faint shield-belt haze", ref shieldHaze,
                "Show a barely visible protective sphere around active shield belts in RimFront.");
            listing.CheckboxLabeled("Blue/red shield impact ghosting", ref shieldImpacts,
                "Show brief force-scaled echoes for shield blocks, shield parries and melee contact with active energy belts. Separation is capped at 10 cm horizontally and 2 cm vertically (one tile treated as one metre). Damage and protection remain native.");
            listing.CheckboxLabeled("Shield impacts in colony view", ref shieldImpactsInColony,
                "Also show the blue/red impact effect on shield belts and supported physical shields in normal 2D colony view.");
            listing.Gap();
            listing.Label("Blood and impacts");
            listing.CheckboxLabeled("Blood splashes", ref bloodEffects,
                "Show blood droplets and splashes from actual injuries in RimFront. Does not change damage or bleeding.");
            listing.CheckboxLabeled("Gore fragments", ref goreEffects,
                "Show brief tissue fragments on substantial flesh injuries when blood splashes are enabled. Does not remove body parts.");
            listing.CheckboxLabeled("Bleeding and blood trails", ref bloodTrails,
                "Show bleeding effects and native blood stains in RimFront. Native wound treatment and cleaning remain authoritative.");
            listing.CheckboxLabeled("Surface impact effects", ref impactEffects,
                "Show sparks, chips, dust and contact marks from observed projectile impacts in RimFront.");
            listing.Gap();
            listing.Label("Diagnostics");
            listing.CheckboxLabeled("Trace firing input and actions", ref traceFiring,
                "Write bounded firing diagnostics to Player.log: mouse transitions, trigger gates, attacks, shots, reloads and interruptions. Applies immediately. Enable while reproducing a firing issue, then disable.");
            contentHeight = Mathf.Max(rect.height, listing.CurHeight + 12f);
            listing.End();
            Widgets.EndScrollView();
        }
    }
}
