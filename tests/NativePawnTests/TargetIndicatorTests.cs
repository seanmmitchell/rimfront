using System;
using System.Reflection;

internal static partial class Program
{
    static void TestTargetIndicators(Assembly assembly)
    {
        Type type = assembly.GetType("RimFront.NativeTargetIndicators", true);
        MethodInfo classify = type.GetMethod("Classify", Flags), tint = type.GetMethod("Tint", Flags);
        MethodInfo distinctAlly = type.GetMethod("IsDistinctAlly", Flags).MakeGenericMethod(typeof(object));
        object playerFaction = new object(), visitorFaction = new object();
        int relationCalls = 0;
        Func<object, object, bool> relationLookup = (left, right) => { relationCalls++; return true; };
        Check(!(bool)distinctAlly.Invoke(null, new object[] { playerFaction, playerFaction, relationLookup }) && relationCalls == 0,
            "own faction never calls native relation lookup (prevents repeated same-faction log errors)");
        Check(!(bool)distinctAlly.Invoke(null, new object[] { null, playerFaction, relationLookup }) && relationCalls == 0,
            "factionless pawn never calls native relation lookup");
        Check((bool)distinctAlly.Invoke(null, new object[] { visitorFaction, playerFaction, relationLookup }) && relationCalls == 1,
            "different faction performs exactly one actual relation lookup");
        string State(bool prisoner, bool revolt, bool hostile, bool player, bool ally, bool guest)
            => classify.Invoke(null, new object[] { prisoner, revolt, hostile, player, ally, guest }).ToString();
        Check(State(true, false, true, false, false, false) == "Prisoner", "ordinary hostile-faction prisoners keep prisoner marker");
        Check(State(true, true, true, false, false, false) == "Revolting", "native prison break has distinct marker");
        Check(State(false, false, true, false, false, true) == "Hostile", "hostile visitor is not marked as a safe guest");
        Check(State(false, false, false, true, false, true) == "Colonist", "player pawn takes precedence over visitor category");
        Check(State(false, false, false, false, true, true) == "Friend", "allied visitor distinguished from neutral guest");
        Check(State(false, false, false, false, false, true) == "Guest", "actual hosted guest marker");
        Check(State(false, false, false, false, false, false) == "Neutral", "wildlife has neutral marker");
        Check(State(false, false, false, false, false, false) == "Neutral", "unhosted neutral human is not automatically a guest");
        Check(State(false, false, false, false, true, false) == "Friend", "player-owned noncolonist has friendly marker rather than colonist label");
        MethodInfo batch = type.GetMethod("ScanBatchCount", Flags), due = type.GetMethod("ScanDue", Flags);
        int scanned = 0, frames = 0;
        while (scanned < 1300)
        {
            Check((bool)due.Invoke(null, new object[] { true, 100, 106 }), "paused scan continues pending bounded batches before next simulation deadline");
            int size = (int)batch.Invoke(null, new object[] { 1300, scanned, 512 });
            Check(size > 0 && size <= 512, "large-map indicator scan respects per-frame budget");
            scanned += size; frames++;
        }
        Check(scanned == 1300 && frames == 3, "all later pawn batches visited while game remains paused");
        Check(!(bool)due.Invoke(null, new object[] { false, 100, 106 }), "completed paused scan does not restart every frame");
        Check((int)batch.Invoke(null, new object[] { 0, 100, 512 }) == 0, "removed pawn list completes scan safely");
        Check((int)batch.Invoke(null, new object[] { 10, 30, 512 }) == 0, "shrinking pawn list does not overrun indexed scan");
        Type status = type.GetNestedType("Status", Flags);
        object revolt = Enum.Parse(status, "Revolting");
        object orange = tint.Invoke(null, new[] { revolt, (object)0 }), red = tint.Invoke(null, new[] { revolt, (object)12 });
        Check((float)Get(orange, "g") > (float)Get(red, "g"), "prison break alternates orange and red by simulation tick");
        object paused = tint.Invoke(null, new[] { revolt, (object)12 });
        Check((float)Get(paused, "g") == (float)Get(red, "g"), "paused prison-break marker does not advance its flash");
    }
}
