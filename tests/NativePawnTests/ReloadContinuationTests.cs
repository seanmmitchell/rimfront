using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestReloadContinuation(Assembly assembly)
    {
        Type jobs = assembly.GetType("RimFront.NativeJobPolicy", true);
        Type attacks = assembly.GetType("RimFront.NativeAttackLifecycle", true);
        MethodInfo compatible = jobs.GetMethod("CompatibleReload", Flags);
        MethodInfo suppress = jobs.GetMethod("SuppressAttackResume", Flags);
        MethodInfo retry = attacks.GetMethod("RetryAfterReload", Flags);
        bool IsReload(string name = "ReloadWeapon", bool sleep = false, bool driver = true,
            bool self = true, bool primary = true, bool comp = true)
            => (bool)compatible.Invoke(null, new object[] { name, sleep, driver, self, primary, comp });
        bool Suppress(bool owned, string name, bool resume, bool nativeReload)
            => (bool)suppress.Invoke(null, new object[] { owned, name, resume, nativeReload });
        bool Retry(bool fresh, bool fired, int initial, int last)
            => (bool)retry.Invoke(null, new object[] { fresh, fired, initial, last });

        Check(IsReload(), "actual CE reload of the controlled pawn's equipped weapon is recognized");
        Check(!IsReload("AttackStatic") && !IsReload(null) && !IsReload(sleep: true),
            "unrelated job definitions and forced sleep do not qualify as compatible reloads");
        Check(!IsReload(driver: false) && !IsReload(self: false) && !IsReload(primary: false) && !IsReload(comp: false),
            "reload continuation requires native CE driver, matching holder/primary, and real ammo comp");
        foreach (bool owned in new[] { false, true })
        foreach (bool resume in new[] { false, true })
        foreach (bool nativeReload in new[] { false, true })
        foreach (string name in new[] { "AttackStatic", "Goto", "ReloadWeapon", "Wait", null })
            Check(Suppress(owned, name, resume, nativeReload) == (owned && resume && nativeReload && name == "AttackStatic"),
                "only the exact owned attack suspension is suppressed; foreign orders/other jobs retain native queue semantics");
        MethodInfo requiresNative = jobs.GetMethod("RequiresNativeControl", Flags);
        Check((bool)requiresNative.Invoke(null, new object[] { "AttackStatic", false, true, false, 0, false }),
            "unowned AttackStatic still hands control back rather than being broadly allowed after reload");

        // CE's base shot assigns LastShotTick before OnCastSuccessful starts the
        // empty-mag reload. This can precede the next GameComponentTick observation.
        Check(Retry(true, false, 400, 400), "accepted short click survives a pre-first-shot automatic reload");
        Check(!Retry(true, false, 400, 401), "shot fired inside current cast prevents phantom Semi/Burst retry before game component sees it");
        Check(!Retry(true, true, 400, 400), "previous observed shot also consumes the original trigger");
        Check(!Retry(false, false, 400, 400), "stale, paused, moving, reload-requested or mode-change-blocked input cannot become deferred fire");
        Check(Retry(true, false, -999999, -999999), "never-fired native verb retains pre-shot intent");
        Type triggers = assembly.GetType("RimFront.NativeTriggerPolicy", true);
        MethodInfo start = triggers.GetMethod("ShouldStart", Flags);
        bool Start(bool held, bool pressed, bool perPress)
            => (bool)start.Invoke(null, new object[] { held, pressed, perPress });
        bool consumed = Retry(true, false, 400, 401);
        Check(Start(true, consumed, false) && !Start(false, consumed, false),
            "after a fired round and reload, Auto requires current held input");
        Check(!Start(true, consumed, true) && Start(false, true, true),
            "after reload Semi/Burst requires a new click, preserving an independently queued second click");
        Check(Start(false, Retry(true, false, 400, 400), true),
            "a short Semi/Burst click interrupted before its first shot remains exactly one accepted trigger");

        // Actual hook early-outs use managed native objects without starting jobs,
        // loading a map, or executing native reload/gameplay code.
        Type game = assembly.GetType("RimFront.RimFrontGame", true);
        MethodInfo before = game.GetMethod("BeforeNativeJobStart", Flags);
        Type trackerType = before.GetParameters()[0].ParameterType, jobType = before.GetParameters()[1].ParameterType;
        Type pawnType = game.GetField("pawn", Flags).FieldType;
        object session = RuntimeHelpers.GetUninitializedObject(game);
        object pawn = RuntimeHelpers.GetUninitializedObject(pawnType);
        object tracker = RuntimeHelpers.GetUninitializedObject(trackerType);
        object foreignTracker = RuntimeHelpers.GetUninitializedObject(trackerType);
        object attack = RuntimeHelpers.GetUninitializedObject(jobType);
        object other = RuntimeHelpers.GetUninitializedObject(jobType);
        Set(session, "pawn", pawn); Set(session, "ownedJob", attack); Set(session, "pendingFire", true);
        Set(pawn, "jobs", tracker); Set(tracker, "curJob", attack);
        object[] args = { foreignTracker, other, true };
        before.Invoke(session, args);
        Check((bool)args[2] && (bool)Get(session, "pendingFire") && ReferenceEquals(Get(session, "ownedJob"), attack),
            "another pawn's native StartJob cannot alter queue flags, owned attack or pending fire");
        Set(tracker, "curJob", other);
        args = new object[] { tracker, other, true };
        before.Invoke(session, args);
        Check((bool)args[2] && ReferenceEquals(Get(session, "ownedJob"), attack),
            "stale ownership cannot alter a replacement external job's suspension");
        Set(session, "ownedJob", null);
        before.Invoke(session, args);
        Check((bool)args[2], "ordinary native jobs retain their resume flag when no owned attack exists");

        Type hook = assembly.GetType("RimFront.PreventStaleOwnedAttackAfterReload", true);
        ParameterInfo[] hookArgs = hook.GetMethod("Prefix", Flags).GetParameters();
        Check(hookArgs[2].Name == "__3" && hookArgs[2].ParameterType == typeof(bool).MakeByRefType(),
            "reload hook changes native StartJob resume argument, not stance cancellation or job condition");
        MethodInfo nativeStart = trackerType.GetMethod("StartJob", Flags);
        Check(nativeStart.GetParameters()[3].Name == "resumeCurJobAfterwards" && nativeStart.GetParameters()[3].ParameterType == typeof(bool),
            "installed native StartJob fourth argument is the verified resumption flag");
    }
}
