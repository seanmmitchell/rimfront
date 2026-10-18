using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RimFront
{
    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherTick")]
    internal static class NativePathFire
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.FullBodyBusy));
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(getter)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MovingFireStance), nameof(MovingFireStance.PathBusy)));
                count++;
            }
            if (count != 1) throw new NotSupportedException("Native pather busy-check contract changed.");
        }
    }
}
