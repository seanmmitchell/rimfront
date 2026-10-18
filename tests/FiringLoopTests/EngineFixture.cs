// Deliberately small deterministic engine boundary, NOT a substitute for native/CE runtime tests.
// Gameplay.cs, its trigger/lifecycle logic and NativeJobPolicy are linked production sources.
// This fixture supplies job callbacks, finite verb timing, ammo and view input, without rendering,
// Harmony execution, native targeting, armor, ballistics, reservations, or CE reflection.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimFront;
using RimWorld;
using Verse;
using Verse.AI;

public sealed class FiringWorld : IDisposable
{
    static readonly FieldInfo AcceptedAt = typeof(RimFrontGame).GetField("acceptedAt", BindingFlags.Instance | BindingFlags.NonPublic);
    public readonly RimFrontGame Game;
    public readonly Pawn Pawn;
    public Verb Verb => Pawn.Verb;
    public int Ammo = 6, Reserve = 60, MagazineSize = 6;
    public string Mode = "Auto";
    public int WarmupTicks = 3, BurstRounds = 3, BurstIntervalTicks = 2, CooldownTicks = 3, ReloadTicks = 5;
    public bool InvalidAim, StallAttack;
    public bool ExpiredWarmup;
    public bool HasBipod = true, AutoBipod = true;
    public int BipodToggles;
    public bool DeferNextOrderedJob, HoldQueuedJobs;
    public bool RecycleNextOrderedJob, QueueRecycledJob;
    public Job RecycledJob;
    public int Shots, Reloads, ResetCount, ResumedForeignAttacks, AttackOrders;
    public int WarmupPresentOnReset;
    public readonly List<int> ShotTicks = new List<int>();
    public readonly List<string> Events = new List<string>();
    public List<string> Errors => Log.Errors;
    public List<string> Messages => Log.Entries;
    public bool Active => RimFrontGame.Active == Game && NativeView.Active;
    public int PendingResumptions => Pawn.jobs.Pending.Count;
    public bool Paused { get => Find.TickManager.Paused; set => Find.TickManager.Paused = value; }
    public int TickCount => Find.TickManager.TicksGame;
    internal int ReloadRemaining, SetupRemaining;
    double? forcedAge;

    public FiringWorld()
    {
        RimFrontGame.Active?.Release("Fixture replaced.");
        CombatExtended.Controller.settings.Enabled = true;
        RimFrontMod.Settings.movingFire = true; RimFrontMod.Settings.analogMovement = true;
        Log.Errors.Clear(); Log.Entries.Clear();
        Find.TickManager = new TickManager(); Find.CurrentMap = new Map();
        Pawn = new Pawn(this) { Map = Find.CurrentMap };
        Game = new RimFrontGame(new Game());
        Game.Control(Pawn);
        AssertNoErrors();
    }

    public void Frame(bool held = false, bool reload = false, bool cycleMode = false, bool paused = false,
        float forward = 0, float strafe = 0, bool release = false, bool brace = false, bool bipod = false, bool crouch = false)
    {
        Paused = paused; forcedAge = null;
        NativeView.FireHeld = held;
        NativeView.Command = new InputCommand { action = release ? "release" : "input", fire = held && !paused,
            crouch = crouch, reload = reload, cycleFireMode = cycleMode, toggleBrace = brace, toggleBipod = bipod, forward = forward, strafe = strafe };
        Game.GameComponentUpdate();
        AssertNoErrors();
    }

    public void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (Paused) continue;
            // Inject sampled-input age, rather than let machine speed/GC make this suite flaky.
            AcceptedAt.SetValue(Game, DateTime.UtcNow.AddMilliseconds(-(forcedAge ?? 0)));
            Find.TickManager.TicksGame++;
            NativeTick();
            Game.GameComponentTick();
            AssertNoErrors();
        }
    }

    // Native game order: ticks from the previous sample, then the next rendered input sample.
    public void Step(bool held = false, int ticks = 1, bool reload = false, bool cycleMode = false,
        bool paused = false, float forward = 0, float strafe = 0)
    { Tick(ticks); Frame(held, reload, cycleMode, paused, forward, strafe); }

    public void ExpireInput(double milliseconds = 351)
    { forcedAge = milliseconds; AcceptedAt.SetValue(Game, DateTime.UtcNow.AddMilliseconds(-milliseconds)); }

    public void SetSetupWait(int ticks)
    {
        Pawn.jobs.StartJob(new Job { def = new JobDef("JobDef_SetUpBipod") }, JobCondition.InterruptForced);
        SetupRemaining = ticks;
    }

    public void SetExternalJob(string name, bool forced = true)
    {
        Job job = new Job { def = name == "AttackStatic" ? JobDefOf.AttackStatic : new JobDef(name),
            playerForced = forced, verbToUse = name == "AttackStatic" ? Verb : null };
        Pawn.jobs.StartJob(job, JobCondition.InterruptForced);
    }

    public bool StartReload(bool resume = true)
    {
        if (Reserve <= 0 || Ammo >= MagazineSize || Pawn.CurJob?.def.defName == "ReloadWeapon") return false;
        Reloads++; ReloadRemaining = Math.Max(1, ReloadTicks);
        Pawn.jobs.StartJob(new Job { def = new JobDef("ReloadWeapon"), EquippedReload = true },
            JobCondition.InterruptForced, null, resume);
        return true;
    }

    internal void BeginAttack(Job job)
    {
        AttackOrders++;
        Verb.Remaining = Mode == "Single" || Mode == "Fixed" ? 1 : Math.Max(1, BurstRounds);
        Verb.Phase = "warmup"; Verb.Bursting = false;
        Pawn.stances.SetStance(new Stance_Warmup(Math.Max(1, WarmupTicks), job.targetA, Verb));
    }

    void NativeTick()
    {
        if (Pawn.CurJob == null && !HoldQueuedJobs) Pawn.jobs.StartNextQueued();
        Stance_Busy busy = Pawn.stances.curStance as Stance_Busy;
        if (busy != null && (busy.ticksLeft > 0 || ExpiredWarmup)) busy.ticksLeft--;
        // Simulate a native/mod expiration that fails to replace its stance.
        if (ExpiredWarmup && busy is Stance_Warmup && busy.ticksLeft <= 0) return;
        // Native VerbTick is independent of JobDriver lifetime. Removing a job alone
        // must not magically stop an already active burst; production must Reset it.
        if (Verb.Phase == "burst" && (busy == null || busy.ticksLeft <= 0))
        { FireRound(); return; }
        if (Pawn.CurJob?.def.defName == "ReloadWeapon")
        {
            if (--ReloadRemaining <= 0)
            {
                int amount = Math.Min(MagazineSize - Ammo, Reserve); Ammo += amount; Reserve -= amount;
                Pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
            return;
        }
        if (Pawn.CurJob?.def.defName == "JobDef_SetUpBipod")
        { if (--SetupRemaining <= 0)
            {
                foreach (var comp in Pawn.equipment.Primary.AllComps)
                    if (comp is CombatExtended.BipodComp native) native.IsSetUpRn = true;
                Pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            } return; }
        if (Pawn.CurJob?.def == JobDefOf.AttackStatic)
        {
            if (StallAttack) { Pawn.stances.curStance = new Stance(); return; }
            if (busy != null && busy.ticksLeft > 0) return;
            if (Verb.Phase == "warmup") FireRound();
            else if (Verb.Phase == "cooldown")
            { Verb.Phase = "idle"; Pawn.stances.curStance = new Stance(); Pawn.jobs.EndCurrentJob(JobCondition.Succeeded); }
            return;
        }
        if (busy != null && busy.ticksLeft <= 0) Pawn.stances.curStance = new Stance();
        if (Pawn.CurJob?.def.defName == "Wait_MaintainPosture")
        {
            Pawn.jobs.CleanupCurrentJob(JobCondition.Succeeded);
            if (Pawn.jobs.Pending.Count > 0)
            {
                if (!HoldQueuedJobs) Pawn.jobs.StartNextQueued();
            }
        }
    }

    void FireRound()
    {
        // Native failed casts finish the burst and pay cooldown; they do not hang
        // until RimFront's separate stalled-job watchdog intervenes.
        if (InvalidAim) { FinishCast(); return; }
        if (Ammo <= 0) { StartReload(); FinishCast(); return; }
        Verb.Bursting = true;
        Ammo--; Shots++; ShotTicks.Add(TickCount); Verb.LastShotTick = TickCount; Verb.Remaining--;
        Events.Add("shot:" + TickCount);
        // CE updates LastShotTick before OnCastSuccessful can start its automatic reload.
        if (Ammo == 0 && StartReload())
        {
            Verb.Bursting = false; Verb.Phase = "idle";
            Pawn.stances.SetStance(new Stance_Cooldown(Math.Max(1, CooldownTicks), default, Verb));
            return;
        }
        if (Verb.Remaining > 0)
        {
            Verb.Phase = "burst";
            Pawn.stances.SetStance(new Stance_Cooldown(Math.Max(1, BurstIntervalTicks), default, Verb));
        }
        else
            FinishCast();
    }

    void FinishCast()
    {
        Verb.Remaining = 0; Verb.Bursting = false; Verb.Phase = "cooldown";
        Pawn.stances.SetStance(new Stance_Cooldown(Math.Max(1, CooldownTicks), default, Verb));
    }

    public void AssertNoErrors()
    { if (Errors.Count != 0) throw new InvalidOperationException("Production controller error: " + string.Join("\n", Errors)); }
    public void Dispose() { Game.Release("Fixture disposed."); AssertNoErrors(); }
}

namespace HarmonyLib
{
    public enum MethodType { Setter, Getter }
    public static class AccessTools { public static Type TypeByName(string name) => typeof(FiringWorld).Assembly.GetType(name); }
    public sealed class Harmony { public Harmony(string id) { } public void Patch(MethodInfo method, HarmonyMethod prefix) { } }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string name) { } public HarmonyPatch(Type type, string name, MethodType method) { } }
}
namespace RimWorld
{
    public static class JobDefOf { public static readonly JobDef AttackStatic = new JobDef("AttackStatic"), Goto = new JobDef("Goto"); }
    public static class MessageTypeDefOf { public static readonly object RejectInput = new object(); }
}
namespace Verse
{
    public class Game { }
    public class GameComponent { public virtual void GameComponentUpdate() { } public virtual void GameComponentTick() { } public virtual void GameComponentOnGUI() { } }
    public class Map { }
    public class Faction { public static readonly Faction OfPlayer = new Faction(); }
    public class RaceProperties { public bool Humanlike = true; }
    public class Drafter { public bool FireAtWill = true; }
    public class ThingDef { public string defName = "Gun_AssaultRifle"; public bool IsRangedWeapon = true; }
    public class ThingComp { }
    public class ThingWithComps
    {
        public ThingDef def = new ThingDef();
        public List<ThingComp> AllComps = new List<ThingComp>();
    }
    public class Equipment { public ThingWithComps Primary = new ThingWithComps(); }
    public static class DefDatabase<T> where T : class
    {
        public static T GetNamedSilentFail(string name) => new Verse.AI.JobDef(name)
            { driverClass = typeof(CombatExtended.JobDriver_SetUpBipod) } as T;
    }
    public class Pawn
    {
        public readonly FiringWorld World; public readonly Verb Verb; public readonly Pawn_JobTracker jobs;
        public readonly Pawn_PathFollower pather = new Pawn_PathFollower(); public readonly StanceTracker stances = new StanceTracker();
        public readonly Drafter drafter = new Drafter(); public readonly RaceProperties RaceProps = new RaceProperties();
        public Faction Faction = Faction.OfPlayer; public bool Spawned = true, Drafted = true, Dead, Downed, InMentalState;
        public readonly Equipment equipment = new Equipment();
        public Map Map; public IntVec3 Position; public Job CurJob => jobs.curJob;
        public Pawn(FiringWorld world) { World = world; Verb = new Verb(world); jobs = new Pawn_JobTracker(this); }
    }
    public class Verb
    {
        public readonly FiringWorld World; public readonly VerbProperties verbProps;
        public bool IsMeleeAttack; public bool Bursting; public int LastShotTick = -1, Remaining; public string Phase = "idle";
        public Verb(FiringWorld world) { World = world; verbProps = new VerbProperties(world); }
        public void Reset() { World.ResetCount++; if (World.Pawn.stances.curStance is Stance_Warmup) World.WarmupPresentOnReset++; Bursting = false; Remaining = 0; Phase = "idle"; }
    }
    public class VerbProperties
    { readonly FiringWorld world; public VerbProperties(FiringWorld w) { world = w; } public int AdjustedCooldownTicks(Verb verb, Pawn pawn) => world.CooldownTicks; }
    public class Stance { public virtual bool StanceBusy => false; }
    public class Stance_Busy : Stance { public override bool StanceBusy => true; public int ticksLeft; public Verb verb; public Stance_Busy(int ticks, Verb v) { ticksLeft = ticks; verb = v; } }
    public class Stance_Cooldown : Stance_Busy { public Stance_Cooldown(int ticks, LocalTargetInfo target, Verb verb) : base(ticks, verb) { } }
    public class Stance_Warmup : Stance_Busy { public Stance_Warmup(int ticks, LocalTargetInfo target, Verb verb) : base(ticks, verb) { } }
    public class Stunner { public bool Stunned; }
    public class StanceTracker { public Stunner stunner = new Stunner(); public Stance curStance = new Stance(); public void SetStance(Stance stance) { curStance = stance; } public void CancelBusyStanceSoft() { if (curStance is Stance_Warmup) curStance = new Stance(); } }
    public class Pawn_PathFollower { public bool Moving; public LocalTargetInfo Destination; public void StopDead() { Moving = false; } public void StartPath(IntVec3 cell, PathEndMode mode) { Destination = cell; Moving = true; } }
    public readonly struct IntVec3 : IEquatable<IntVec3>
    {
        public readonly int x, y, z; public IntVec3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public bool InBounds(Map map) => true; public bool Fogged(Map map) => false;
        public Building_Door GetDoor(Map map) => null; public bool WalkableBy(Map map, Pawn pawn) => true;
        public static IntVec3 operator +(IntVec3 a, IntVec3 b) => new IntVec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public bool Equals(IntVec3 b) => x == b.x && y == b.y && z == b.z;
        public override bool Equals(object o) => o is IntVec3 b && Equals(b);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public static bool operator ==(IntVec3 a, IntVec3 b) => a.Equals(b); public static bool operator !=(IntVec3 a, IntVec3 b) => !a.Equals(b);
    }
    public struct LocalTargetInfo { public ThingWithComps Thing; public IntVec3 Cell; public static implicit operator LocalTargetInfo(ThingWithComps thing) => new LocalTargetInfo { Thing = thing }; public static implicit operator LocalTargetInfo(IntVec3 cell) => new LocalTargetInfo { Cell = cell }; }
    public class Building_Door { public bool Open => true; public bool IsForbiddenToPass(Pawn p) => false; public bool PawnCanOpen(Pawn p) => true; }
    public class TickManager { public bool Paused; public int TicksGame; }
    public static class Find { public static Map CurrentMap; public static TickManager TickManager = new TickManager(); }
    public static class Current { public static Game Game { get; set; } }
    public static class VersionControl { public static string CurrentVersionString => "deterministic-engine-fixture"; }
    public static class Messages { public static void Message(string text, object type, bool historical) { } }
    public static class Log
    {
        public static readonly List<string> Errors = new List<string>(), Entries = new List<string>();
        public static void Error(string text) { Errors.Add(text); }
        public static void Warning(string text) { Entries.Add(text); }
        public static void Message(string text) { Entries.Add(text); }
    }
}
namespace Verse.AI
{
    public enum JobCondition { None, Succeeded, InterruptForced }
    public enum PathEndMode { OnCell }
    public class JobDef { public Type driverClass; public readonly string defName; public JobDef(string name) { defName = name; } }
    public class Job
    {
        static int nextId;
        public int loadID = ++nextId;
        public JobDef def; public Verb verbToUse; public LocalTargetInfo targetA;
        public bool playerForced, forceSleep, EquippedReload, Suspended; public int expiryInterval;
        public void RecycleAs(JobDef nextDef)
        {
            loadID = ++nextId; def = nextDef; verbToUse = null; targetA = default;
            playerForced = true; forceSleep = EquippedReload = Suspended = false; expiryInterval = 0;
        }
    }
    public static class JobMaker { public static Job MakeJob(JobDef def, LocalTargetInfo target) => new Job { def = def, targetA = target }; }
    public class Pawn_JobTracker
    {
        readonly Pawn pawn; public Job curJob; public readonly JobQueue jobQueue = new JobQueue();
        public JobQueue Pending => jobQueue;
        public Pawn_JobTracker(Pawn p) { pawn = p; }
        public bool TryTakeOrderedJob(Job job)
        {
            job.playerForced = true;
            if (pawn.World.RecycleNextOrderedJob)
            {
                pawn.World.RecycleNextOrderedJob = false;
                // A nested native start may reject/pool the request and reuse the same
                // object before TryTakeOrderedJob returns. Reference identity alone is insufficient.
                job.RecycleAs(new JobDef("HaulToCell"));
                pawn.World.RecycledJob = job;
                if (pawn.World.QueueRecycledJob) jobQueue.EnqueueLast(job);
                else curJob = job;
                return true;
            }
            if (pawn.World.DeferNextOrderedJob)
            {
                pawn.World.DeferNextOrderedJob = false;
                jobQueue.EnqueueLast(job);
                return true; // Native acceptance does not imply this job is current.
            }
            jobQueue.Clear();
            jobQueue.EnqueueFirst(job);
            if (curJob != null) EndCurrentJob(JobCondition.InterruptForced);
            else StartNextQueued();
            return true;
        }
        public void StartNextQueued()
        {
            if (jobQueue.Count == 0 || pawn.World.HoldQueuedJobs) return;
            Job next = jobQueue.Dequeue();
            if (next.Suspended && next.def == JobDefOf.AttackStatic) pawn.World.ResumedForeignAttacks++;
            StartJob(next, JobCondition.None);
        }
        public void StartJob(Job next, JobCondition condition = JobCondition.None, object jobGiver = null, bool resumeCurJobAfterwards = false)
        {
            RimFrontGame.Active?.BeforeNativeJobStart(this, next, ref resumeCurJobAfterwards);
            // Native StartJob cancels busy stance before suspending/cleaning the previous job.
            pawn.stances.curStance = new Stance();
            if (curJob != null)
            {
                if (resumeCurJobAfterwards) { curJob.Suspended = true; Pending.EnqueueFirst(curJob); }
                CleanupCurrentJob(condition);
            }
            curJob = next;
            if (next?.def.defName == "JobDef_SetUpBipod") pawn.World.SetupRemaining = 5;
            pawn.World.Events.Add("job-start:" + next?.def?.defName);
            if (next?.def == JobDefOf.AttackStatic) pawn.World.BeginAttack(next);
        }
        public void CleanupCurrentJob(JobCondition condition)
        {
            if (curJob == null) return;
            RimFrontGame.Active?.BeforeNativeJobCleanup(this, condition);
            curJob = null;
            // Native soft cancellation ends warmup but retains cooldown.
            if (pawn.stances.curStance is Stance_Warmup) pawn.stances.curStance = new Stance();
        }
        public void EndCurrentJob(JobCondition condition, bool startNewJob = true, bool canReturnToPool = true)
        {
            Job previous = curJob;
            CleanupCurrentJob(condition);
            if (!startNewJob) return;
            if (condition == JobCondition.Succeeded && previous != null && previous.def.defName != "Wait_MaintainPosture" && previous.def != JobDefOf.Goto)
                curJob = new Job { def = new JobDef("Wait_MaintainPosture"), expiryInterval = 1 };
            else StartNextQueued();
        }
        public void EndCurrentOrQueuedJob(Job job, JobCondition condition, bool startNewJob = true, bool canReturnToPool = true)
        {
            jobQueue.Remove(job);
            if (ReferenceEquals(curJob, job)) EndCurrentJob(condition, startNewJob, canReturnToPool);
        }
    }
    public class JobQueue
    {
        readonly List<Job> jobs = new List<Job>();
        public int Count => jobs.Count;
        public bool Contains(Job job) => jobs.Exists(candidate => ReferenceEquals(candidate, job));
        public void EnqueueFirst(Job job) => jobs.Insert(0, job);
        public void EnqueueLast(Job job) => jobs.Add(job);
        public Job Dequeue() { Job job = jobs[0]; jobs.RemoveAt(0); return job; }
        public void Clear() => jobs.Clear();
        public void Remove(Job job) => jobs.RemoveAll(candidate => ReferenceEquals(candidate, job));
    }
}
namespace RimFront
{
    internal static class CombatAdapter
    {
        internal static string Mode => "simulated native/CE boundary";
        internal static bool IsReloading(Pawn p) => p.CurJob?.def.defName == "ReloadWeapon";
        internal static bool IsEquippedReload(Pawn p, Job j) => j?.EquippedReload == true;
        internal static bool IsSettingUpBipod(Pawn p) => p.CurJob?.def.defName == "JobDef_SetUpBipod" && p.World.SetupRemaining > 0;
        internal static bool TryReload(Pawn p, out string message) { message = "simulated reload"; return p.World.StartReload(); }
        internal static Job CreateAttack(Pawn p, float yaw, float pitch, out string message)
        {
            message = "simulated attack";
            if (p.World.InvalidAim || p.World.Ammo <= 0) return null;
            return new Job { def = JobDefOf.AttackStatic, verbToUse = p.Verb, playerForced = true };
        }
    }
    internal static class NativeBipods
    {
        internal static void Start(Pawn p) { p.World.AutoBipod = true; }
        internal static void Stop() { }
        internal static bool TryToggle(Pawn p, out string message)
        {
            message = "simulated bipod";
            if (!p.World.HasBipod) return false;
            p.World.AutoBipod = !p.World.AutoBipod; p.World.BipodToggles++;
            if (!p.World.AutoBipod && CombatAdapter.IsSettingUpBipod(p))
            { p.World.SetupRemaining = 0; p.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false); }
            return true;
        }
    }
    internal static class NativeFireModes
    {
        internal static bool RequiresTriggerPress(Pawn p) => p.World.Mode == "Single" || p.World.Mode == "Burst";
        internal static bool CanCycle(Pawn p) => p.World.Mode != "Fixed";
        internal static bool TryCycle(Pawn p, out string message)
        { p.World.Mode = p.World.Mode == "Auto" ? "Single" : p.World.Mode == "Single" ? "Burst" : "Auto"; message = p.World.Mode; return true; }
    }
    internal static class NativeManualAim
    { internal static bool CanCast(Verb verb) => !verb.World.InvalidAim; internal static void Update(Pawn p, float yaw, float pitch) { } internal static void Clear(Pawn p = null) { } }
    internal static class NativeView
    {
        internal static Pawn ControlledPawn; internal static bool Active, FireHeld; internal static float Yaw = 0; internal static InputCommand Command;
        internal static void Start(Pawn p) { ControlledPawn = p; Active = true; FireHeld = false; Command = null; }
        internal static void Stop() { Active = false; } internal static InputCommand Update() => Command;
        internal static void ApplyPose() { } internal static void OnGUI(string text) { }
    }
    internal static class AnalogMovement
    {
        internal static bool Active, IsMoving;
        internal static bool Start(Pawn p, out string fallback) { fallback = null; Active = RimFrontMod.Settings.analogMovement; return Active; }
        internal static void Stop() { Active = IsMoving = false; } internal static void Halt() { IsMoving = false; }
        internal static void Tick(float yaw, float forward, float strafe) { IsMoving = Math.Abs(forward) + Math.Abs(strafe) >= .1f; }
    }
    internal static class AnalogCombatCompatibility { internal static bool Supported => true; }
    internal sealed class FixtureSettings { public bool analogMovement = true, movingFire = true; }
    internal static class RimFrontMod { internal static readonly FixtureSettings Settings = new FixtureSettings(); }
    internal sealed class NativeFiringTrace
    {
        internal static bool Enabled => false;
        internal void Reset() { } internal void Event(Pawn p, string kind, string detail) { } internal void Observe(Pawn p, Job job) { }
        internal void RecordJobStart(Pawn p, Job next, Job owned, bool resumeCurrent) { }
        internal void ReportUnexpectedJob(Pawn p, Job owned, bool pendingReload) { }
        internal void Input(Pawn p, bool physical, bool raw, bool accepted, bool pending, bool gated, bool paused, bool reload, bool mode, bool suppress) { }
    }
    internal static class NativeWindowIcon { internal static void Tick() { } }
    internal static class NativeScene { internal static void Draw(Map map, Pawn pawn, float yaw) { } internal static void Dispose() { } }
    internal static class NativeShieldEffects { internal static void Draw2D() { } internal static void Dispose() { } }
    internal static class NativeTargetIndicators { internal static void Dispose() { } }
    internal static class NativeAimTargeting { internal static void Dispose() { } }
    internal static class NativePawns { internal static void ClearAimCache() { } }
    internal static class NativePawnAnimation { internal static void Dispose() { } }
}

namespace CombatExtended
{
    public sealed class Settings { public bool Enabled = true; public bool BipodMechanics => Enabled; }
    public static class Controller { public static Settings settings = new Settings(); }
    public sealed class JobDriver_SetUpBipod { }
    public sealed class BipodComp : Verse.ThingComp
    {
        public bool IsSetUpRn, ShouldSetUpint;
        public int DeployCalls, CloseCalls;
        public bool ShouldSetUp => ShouldSetUpint && !IsSetUpRn;
        public void DeployUpBipod() { ShouldSetUpint = true; DeployCalls++; }
        public void CloseBipod() { ShouldSetUpint = IsSetUpRn = false; CloseCalls++; }
    }
}
namespace RimFront
{
    // Geometry classification is exercised against the compiled renderer in NativePawnTests.
    internal static class NativeWeapons
    {
        internal enum WeaponKind { Rifle, Shotgun, Smg, Sniper, Heavy, Launcher, Pistol }
        internal static WeaponKind ClassifyName(string name, bool ranged) => WeaponKind.Rifle;
    }
}
