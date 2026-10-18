using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestAttackLifecycle(Assembly assembly)
    {
        Type policy = assembly.GetType("RimFront.NativeAttackLifecycle", true);
        MethodInfo cancel = policy.GetMethod("ShouldCancel", Flags);
        MethodInfo idle = policy.GetMethod("NextIdleTicks", Flags);
        MethodInfo cooldown = policy.GetMethod("CancellationCooldown", Flags);
        MethodInfo progressing = policy.GetMethod("Progressing", Flags);
        bool Cancel(bool held, bool fired, bool moving = false, bool reload = false, bool invalid = false, int ticks = 0)
            => (bool)cancel.Invoke(null, new object[] { held, fired, moving, reload, invalid, ticks });
        int Next(int ticks, bool progress) => (int)idle.Invoke(null, new object[] { ticks, progress });
        int Cool(bool burst, int remaining, int native) => (int)cooldown.Invoke(null, new object[] { burst, remaining, native });

        Check(!Cancel(false, false), "a queued short click retains its first native warmup and shot after mouse release");
        Check(Cancel(false, true), "trigger release stops remaining burst rounds after the first shot");
        Check(!Cancel(true, true), "holding the trigger preserves an otherwise valid native burst");
        foreach (bool held in new[] { false, true })
        foreach (bool fired in new[] { false, true })
        {
            Check(Cancel(held, fired, moving: true), "movement can interrupt a pending warmup or active burst even while fire is held");
            Check(Cancel(held, fired, reload: true), "reload intent can cancel the owned attack before busy-stance early return");
            Check(Cancel(held, fired, invalid: true), "invalid geometry never leaves an owned attack waiting indefinitely");
        }
        int waiting = 0;
        for (int i = 0; i < 59; i++) waiting = Next(waiting, false);
        Check(!Cancel(true, false, ticks: waiting), "ordinary short native startup delays are not treated as stalled attacks");
        waiting = Next(waiting, false);
        Check(Cancel(true, false, ticks: waiting), "sixty native ticks without warmup, burst, or cooldown progress release a failed attack");
        Check(Next(waiting, true) == 0, "legitimate long warmups and native cooldowns reset the idle watchdog");
        Check(Next(int.MaxValue, false) == 60 && Next(-20, false) == 1, "idle accounting remains bounded for invalid history");
        bool Progress(bool busy, bool changed, int remaining, int previous, int shot, int oldShot)
            => (bool)progressing.Invoke(null, new object[] { busy, changed, remaining, previous, shot, oldShot });
        Check(Progress(true, false, 900, 901, 10, 10), "a long warmup progresses while its native ticks count down");
        Check(!Progress(true, false, 900, 900, 10, 10), "a permanently busy stance with frozen native ticks cannot defeat the stuck-attack watchdog");
        Check(!Progress(true, false, -7, -6, 10, 10), "negative warmup countdown is not progress");
        Check(!Progress(true, true, 0, 1, 10, 10), "an already expired replacement stance is not progress");
        Check(Progress(true, true, 900, 0, 10, 10), "a newly entered native stance starts a fresh progress window");
        Check(Progress(false, false, 0, 0, 11, 10), "a real native shot advances progress independently of stance visibility");
        Check(Cool(true, 6, 90) == 90, "canceling between burst rounds cannot exploit a short inter-round cooldown");
        Check(Cool(true, 120, 90) == 120, "burst cancellation never shortens a longer existing native cooldown");
        Check(Cool(false, 75, 90) == 75, "canceling an already completed shot preserves remaining cooldown without restarting it");
        Check(Cool(false, 0, 90) == 0, "canceling a pre-shot warmup invents no shot cooldown or ammunition consumption");
        MethodInfo resetPolicy = policy.GetMethod("ShouldResetOnCleanup", Flags);
        foreach (bool success in new[] { false, true }) foreach (bool burst in new[] { false, true })
            Check((bool)resetPolicy.Invoke(null, new object[] { success, burst }) == (!success || burst),
                "only successful non-bursting completion preserves CE repeating state; interruption or live burst always resets");
        TestOwnedCleanupOrdering(assembly);
        TestNativeMovementHook(assembly);
    }

    static void TestNativeMovementHook(Assembly assembly)
    {
        // Compile/install the actual transpiler against this machine's native
        // PatherTick IL; the old fixture did not exercise Harmony at all.
        Type harmonyType = Assembly.Load("0Harmony").GetType("HarmonyLib.Harmony", true);
        object harmony = Activator.CreateInstance(harmonyType, new object[] { "rimfront.tests.path-fire" });
        try
        {
            object processor = harmonyType.GetMethod("CreateClassProcessor").Invoke(harmony,
                new[] { (object)assembly.GetType("RimFront.NativePathFire", true) });
            processor.GetType().GetMethod("Patch").Invoke(processor, null);
            Type stance = Assembly.Load("Assembly-CSharp").GetType("Verse.Stance_Warmup", true);
            object busy = RuntimeHelpers.GetUninitializedObject(stance);
            Check((bool)stance.GetProperty("StanceBusy", Flags).GetValue(busy),
                "real native busy getter stays true while the pathfinding-only exception is installed");
            Check(assembly.GetType("RimFront.MovingFireStance", true).GetCustomAttributes(false).Length == 0,
                "moving fire does not install a global stance-busy hook");
        }
        finally { harmonyType.GetMethod("UnpatchAll").Invoke(harmony, new object[] { "rimfront.tests.path-fire" }); }
    }

    static void TestOwnedCleanupOrdering(Assembly assembly)
    {
        Type gameType = assembly.GetType("RimFront.RimFrontGame", true);
        Type pawnType = gameType.GetField("pawn", Flags).FieldType;
        Assembly native = pawnType.Assembly;
        Type verbType = native.GetType("Verse.Verb", true);
        // A managed observer around the real native Reset verifies ordering without a
        // running map, ammunition, graphics, timers, or invocation of native firing.
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RimFrontCleanupObserver"), AssemblyBuilderAccess.Run);
        var type = dynamicAssembly.DefineDynamicModule("observer").DefineType("ObservedVerb", TypeAttributes.Public, verbType);
        FieldBuilder callback = type.DefineField("BeforeReset", typeof(Action), FieldAttributes.Public | FieldAttributes.Static);
        FieldBuilder repeating = type.DefineField("Repeating", typeof(bool), FieldAttributes.Public);
        MethodInfo nativeReset = verbType.GetMethod("Reset", Flags);
        MethodBuilder reset = type.DefineMethod("Reset", MethodAttributes.Public | MethodAttributes.Virtual, typeof(void), Type.EmptyTypes);
        ILGenerator il = reset.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, callback); il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, repeating);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, nativeReset); il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(reset, nativeReset);
        MethodInfo nativeCast = verbType.GetMethod("TryCastShot", Flags);
        MethodBuilder cast = type.DefineMethod("TryCastShot", MethodAttributes.Family | MethodAttributes.Virtual, typeof(bool), Type.EmptyTypes);
        cast.GetILGenerator().Emit(OpCodes.Ldc_I4_0); cast.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethodOverride(cast, nativeCast);
        Type observer = type.CreateType();
        object verb = RuntimeHelpers.GetUninitializedObject(observer);
        object pawn = RuntimeHelpers.GetUninitializedObject(pawnType);
        Type trackerType = native.GetType("Verse.AI.Pawn_JobTracker", true);
        object tracker = RuntimeHelpers.GetUninitializedObject(trackerType);
        object foreignTracker = RuntimeHelpers.GetUninitializedObject(trackerType);
        object game = Activator.CreateInstance(gameType, new object[] { null });
        Type jobType = native.GetType("Verse.AI.Job", true);
        object job = RuntimeHelpers.GetUninitializedObject(jobType);
        FieldInfo attackDef = native.GetType("RimWorld.JobDefOf", true).GetField("AttackStatic", Flags);
        object savedDef = attackDef.GetValue(null);
        Type manual = assembly.GetType("RimFront.NativeManualAim", true);
        FieldInfo aimField = manual.GetField("current", Flags); object savedAim = aimField.GetValue(null);
        object aim = Activator.CreateInstance(aimField.FieldType, true);
        try
        {
            object definition = savedDef ?? RuntimeHelpers.GetUninitializedObject(attackDef.FieldType);
            attackDef.SetValue(null, definition);
            Set(job, "def", definition); Set(job, "verbToUse", verb);
            Set(tracker, "curJob", job); Set(pawn, "jobs", tracker);
            Set(game, "pawn", pawn); Set(game, "ownedJob", job);
            Set(aim, "Pawn", pawn); aimField.SetValue(null, aim);
            bool resetObserved = false;
            observer.GetField("BeforeReset").SetValue(null, (Action)(() =>
            {
                resetObserved = true;
                Check(ReferenceEquals(aimField.GetValue(null), aim), "native verb resets while manual geometry is still owned");
                Check(ReferenceEquals(gameType.GetField("ownedJob", Flags).GetValue(game), job), "native verb resets before ownership is discarded");
            }));
            MethodInfo cleanup = gameType.GetMethod("BeforeNativeJobCleanup", Flags);
            Type conditionType = cleanup.GetParameters()[1].ParameterType;
            object interrupted = Enum.Parse(conditionType, "InterruptForced"), succeeded = Enum.Parse(conditionType, "Succeeded");
            cleanup.Invoke(game, new[] { foreignTracker, interrupted });
            Check(!resetObserved && ReferenceEquals(aimField.GetValue(null), aim), "another pawn's native cleanup cannot cancel the controlled attack");
            cleanup.Invoke(game, new[] { tracker, interrupted });
            Check(resetObserved && aimField.GetValue(null) == null, "native job replacement resets the owned verb and then clears its shot geometry");
            Check(gameType.GetField("ownedJob", Flags).GetValue(game) == null, "native job cleanup releases the reference before pooled Jobs can be reused");
            resetObserved = false;
            cleanup.Invoke(game, new[] { tracker, interrupted });
            Check(!resetObserved, "repeated cleanup cannot reset a subsequent external use of the same weapon");
            Set(game, "ownedJob", job); aimField.SetValue(null, aim);
            cleanup.Invoke(game, new[] { tracker, succeeded });
            Check(!resetObserved && aimField.GetValue(null) == null && gameType.GetField("ownedJob", Flags).GetValue(game) == null,
                "normal completed burst releases job/aim ownership without erasing native repeat-shot warmup state");
            gameType.GetMethod("StopOwnedJob", Flags).Invoke(game, new object[] { "test next attack", true });
            Check(!resetObserved, "preparing the next held-fire attack cannot reset the previously completed unowned verb");
            FieldInfo repeatState = observer.GetField("Repeating");
            repeatState.SetValue(verb, true);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Set(game, "ownedJob", job); aimField.SetValue(null, aim);
                cleanup.Invoke(game, new[] { tracker, succeeded });
                gameType.GetMethod("StopOwnedJob", Flags).Invoke(game, new object[] { "test next attack", true });
                Check((bool)repeatState.GetValue(verb) && !resetObserved,
                    "repeated successful native cleanup and next-attack preparation preserve simulated CE repeating state");
            }
            Set(game, "ownedJob", job); aimField.SetValue(null, aim);
            cleanup.Invoke(game, new[] { tracker, interrupted });
            Check(!(bool)repeatState.GetValue(verb) && resetObserved,
                "an interruption after successful cycles still resets the exact owned verb and its repeat-shot state");
        }
        finally { attackDef.SetValue(null, savedDef); aimField.SetValue(null, savedAim); }
    }
}
