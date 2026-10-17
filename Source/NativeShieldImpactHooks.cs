using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimFront
{
    // Observes native resolution only. In particular the CE damage iterator must
    // never be enumerated a second time: its MoveNext calls consume combat RNG.
    internal static class NativeShieldImpactHooks
    {
        internal class DamageSample
        {
            internal float Amount;
            internal int Items;
            internal bool Finished;
            internal void Add(float amount)
            {
                if (Finished || !Positive(amount)) return;
                Amount = Sum(Amount, amount); Items++;
            }
            internal float Complete(bool success)
            {
                if (Finished) return 0;
                Finished = true;
                return success && Items > 0 ? Amount : 0;
            }
        }
        internal sealed class ParryScope : DamageSample
        {
            internal Verb Verb;
            internal Pawn Defender;
            internal Thing Shield;
            internal bool Physical, Riposte;
            internal ParryScope Parent;
        }
        internal sealed class MeleeScope
        {
            internal Verb Verb;
            internal Pawn Target;
            internal MeleeScope Parent;
            internal bool Finished;
        }
        internal sealed class Contact
        {
            internal Pawn Pawn;
            internal DamageInfo Info;
            internal bool ActiveShield;
        }
        [ThreadStatic] static ParryScope parry;
        [ThreadStatic] static MeleeScope melee;
        static readonly Type CeVerb = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
        internal static MethodInfo ParryMethod => CeVerb == null ? null : AccessTools.Method(CeVerb, "DoParry",
            new[] { typeof(Pawn), typeof(Thing), typeof(bool), typeof(bool) });
        internal static MethodInfo DamageIterator => CeVerb == null ? null : AccessTools.Method(CeVerb, "DamageInfosToApply",
            new[] { typeof(LocalTargetInfo), typeof(bool) });
        internal static bool ParrySupported => ParryMethod?.ReturnType == typeof(void)
            && DamageIterator?.ReturnType == typeof(IEnumerable<DamageInfo>);
        internal static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        internal static float Sum(float total, float amount)
            => Positive(amount) ? (float)Math.Min(float.MaxValue, (double)(Positive(total) ? total : 0) + amount) : (Positive(total) ? total : 0);
        internal static IEnumerable<T> Observe<T>(IEnumerable<T> source, Action<T> observed)
        {
            foreach (T item in source)
            {
                observed(item);
                yield return item;
            }
        }
        internal static bool OwnsContact(object target, object instigator, object expectedTarget, object expectedAttacker)
            => target != null && instigator != null && ReferenceEquals(target, expectedTarget) && ReferenceEquals(instigator, expectedAttacker);

        internal static ParryScope BeginParry(Verb verb, Pawn defender, Thing shield, bool riposte)
        {
            var scope = new ParryScope { Verb = verb, Defender = defender, Shield = shield, Riposte = riposte,
                Physical = shield != null && NativePawnEquipment.IsPhysicalShield(shield), Parent = parry };
            parry = scope;
            return scope;
        }
        internal static void EndParry(ParryScope scope, bool success)
        {
            if (scope == null || scope.Finished) return;
            if (ReferenceEquals(parry, scope)) parry = scope.Parent;
            float amount = scope.Complete(success);
            if (scope.Physical && Positive(amount))
                NativeShieldEffects.NotifyParry(scope.Defender, scope.Shield, scope.Verb?.CasterPawn, amount);
        }
        internal static void Wrap(Verb verb, LocalTargetInfo target, ref IEnumerable<DamageInfo> source)
        {
            ParryScope scope = parry;
            if (source == null || scope == null || scope.Finished || !ReferenceEquals(scope.Verb, verb)
                || !ReferenceEquals(scope.Defender, target.Thing)) return;
            source = Observe(source, info =>
            {
                // DamageInfo is a value type; the original value is yielded unchanged.
                // Only the iterator owned by this DoParry contributes, not a riposte's.
                if (ReferenceEquals(parry, scope) && !scope.Finished) scope.Add(info.Amount);
            });
        }
        internal static MeleeScope BeginMelee(Verb verb, LocalTargetInfo target)
        {
            var scope = new MeleeScope { Verb = verb, Target = target.Thing as Pawn, Parent = melee };
            melee = scope;
            return scope;
        }
        internal static void EndMelee(MeleeScope scope)
        {
            if (scope == null || scope.Finished) return;
            scope.Finished = true;
            if (ReferenceEquals(melee, scope)) melee = scope.Parent;
        }
        internal static Contact Capture(Thing target, DamageInfo info)
        {
            Pawn pawn = target as Pawn;
            if (pawn == null || info.Def == null || !Positive(info.Amount)) return null;
            bool nativeMelee = melee != null && OwnsContact(pawn, info.Instigator, melee.Target, melee.Verb?.CasterPawn);
            // CE physical-shield ripostes apply a native six-damage bash directly,
            // without entering ApplyMeleeDamageToTarget on a second verb.
            bool nativeRiposte = parry?.Riposte == true && OwnsContact(pawn, info.Instigator, parry.Verb?.CasterPawn, parry.Defender);
            if ((!nativeMelee && !nativeRiposte) || info.Def.isRanged || info.Def.isExplosive
                || !info.Def.ExternalViolenceFor(pawn) || !NativeShieldEffects.HasActiveShield(pawn)) return null;
            return new Contact { Pawn = pawn, Info = info, ActiveShield = true };
        }
        internal static float ContactForce(bool completed, float incoming)
            => completed && Positive(incoming) ? incoming : 0;
        internal static void Report(Contact contact, bool completed)
        {
            if (contact == null) return;
            // The matching native melee scope and TakeDamage call already prove
            // contact. Armor reducing the result to zero does not turn it into a
            // miss; this reaction is cosmetic and makes no claim of absorption.
            float force = ContactForce(completed, contact.Info.Amount);
            if (Positive(force))
                NativeShieldEffects.NotifyMeleeContact(contact.Pawn, contact.Info, force, contact.ActiveShield);
        }
        internal static IEnumerable<MethodBase> MeleeMethods()
        {
            MethodInfo vanilla = AccessTools.Method(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget", new[] { typeof(LocalTargetInfo) });
            if (vanilla != null && !vanilla.IsAbstract && vanilla.ReturnType == typeof(DamageWorker.DamageResult)) yield return vanilla;
            MethodInfo ce = CeVerb == null ? null : AccessTools.Method(CeVerb, "ApplyMeleeDamageToTarget", new[] { typeof(LocalTargetInfo) });
            if (ce != null && ce != vanilla && !ce.IsAbstract && ce.ReturnType == typeof(DamageWorker.DamageResult)) yield return ce;
        }
    }

    [HarmonyPatch]
    internal static class ObserveNativeShieldParryScope
    {
        static bool Prepare() => NativeShieldImpactHooks.ParrySupported;
        static MethodBase TargetMethod() => NativeShieldImpactHooks.ParryMethod;
        static void Prefix(Verb __instance, Pawn __0, Thing __1, bool __2, out NativeShieldImpactHooks.ParryScope __state)
        {
            __state = null;
            try { __state = NativeShieldImpactHooks.BeginParry(__instance, __0, __1, __2); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
        static Exception Finalizer(Exception __exception, NativeShieldImpactHooks.ParryScope __state)
        {
            try { NativeShieldImpactHooks.EndParry(__state, __exception == null); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
            return __exception;
        }
    }
    [HarmonyPatch]
    internal static class ObserveNativeShieldParryDamage
    {
        static bool Prepare() => NativeShieldImpactHooks.ParrySupported;
        static MethodBase TargetMethod() => NativeShieldImpactHooks.DamageIterator;
        static void Postfix(Verb __instance, LocalTargetInfo __0, ref IEnumerable<DamageInfo> __result)
        {
            try { NativeShieldImpactHooks.Wrap(__instance, __0, ref __result); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
    }
    [HarmonyPatch]
    internal static class ObserveNativeMeleeContactScope
    {
        static IEnumerable<MethodBase> TargetMethods() => NativeShieldImpactHooks.MeleeMethods();
        static void Prefix(Verb __instance, LocalTargetInfo __0, out NativeShieldImpactHooks.MeleeScope __state)
        {
            __state = null;
            try { __state = NativeShieldImpactHooks.BeginMelee(__instance, __0); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
        static Exception Finalizer(Exception __exception, NativeShieldImpactHooks.MeleeScope __state)
        {
            NativeShieldImpactHooks.EndMelee(__state);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage), new[] { typeof(DamageInfo) })]
    internal static class ObserveNativeEnergyShieldMeleeContact
    {
        static void Prefix(Thing __instance, DamageInfo __0, out NativeShieldImpactHooks.Contact __state)
        {
            __state = null;
            try { __state = NativeShieldImpactHooks.Capture(__instance, __0); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
        static void Postfix(DamageWorker.DamageResult __result, NativeShieldImpactHooks.Contact __state)
        {
            try { NativeShieldImpactHooks.Report(__state, __result != null); }
            catch (Exception error) { NativeShieldEffects.Warn(error); }
        }
    }
}
