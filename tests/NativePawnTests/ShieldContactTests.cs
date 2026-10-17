using System;
using System.Collections.Generic;
using System.Reflection;

internal static partial class Program
{
    static void TestShieldContact(Assembly assembly)
    {
        Type hooks = assembly.GetType("RimFront.NativeShieldImpactHooks", true);
        MethodInfo observe = hooks.GetMethod("Observe", Flags).MakeGenericMethod(typeof(int));
        int enumerated = 0, disposed = 0, observed = 0;
        IEnumerable<int> Source(bool fail = false)
        {
            try
            {
                enumerated++; yield return 3;
                if (fail) throw new InvalidOperationException("native iterator sentinel");
                enumerated++; yield return 7;
                enumerated++; yield return 11;
            }
            finally { disposed++; }
        }
        IEnumerable<int> Wrap(IEnumerable<int> source)
            => (IEnumerable<int>)observe.Invoke(null, new object[] { source, (Action<int>)(_ => observed++) });
        IEnumerable<int> wrapped = Wrap(Source());
        Check(enumerated == 0 && observed == 0 && disposed == 0,
            "parry observer does not eagerly consume native combat RNG");
        var values = new List<int>(wrapped);
        Check(values.Count == 3 && values[0] == 3 && values[1] == 7 && values[2] == 11,
            "parry observer yields native damage items unchanged and in order");
        Check(enumerated == 3 && observed == 3 && disposed == 1,
            "full parry enumeration observes each native item exactly once and disposes once");
        enumerated = observed = disposed = 0;
        foreach (int value in Wrap(Source())) { Check(value == 3, "early consumer receives original item"); break; }
        Check(enumerated == 1 && observed == 1 && disposed == 1,
            "early parry exit disposes the native iterator without advancing unused damage or RNG");
        enumerated = observed = disposed = 0;
        bool threw = false;
        try { foreach (int value in Wrap(Source(true))) { } }
        catch (InvalidOperationException error) { threw = error.Message == "native iterator sentinel"; }
        Check(threw && enumerated == 1 && observed == 1 && disposed == 1,
            "native iterator failure propagates unchanged and releases its iterator");

        Type sampleType = hooks.GetNestedType("DamageSample", Flags);
        MethodInfo add = sampleType.GetMethod("Add", Flags), complete = sampleType.GetMethod("Complete", Flags);
        object sample = Activator.CreateInstance(sampleType, true);
        foreach (float amount in new[] { 6f, 4f, 0f, -5f, float.NaN, float.PositiveInfinity })
            add.Invoke(sample, new object[] { amount });
        Check((float)Get(sample, "Amount") == 10 && (int)Get(sample, "Items") == 2,
            "parry force sums only positive finite original amounts");
        Check((float)complete.Invoke(sample, new object[] { true }) == 10,
            "successful parry emits original combined force, including native deflected parries");
        add.Invoke(sample, new object[] { 9f });
        Check((float)complete.Invoke(sample, new object[] { true }) == 0 && (float)Get(sample, "Amount") == 10,
            "finalized parry cannot emit twice or accept late iterator samples");
        sample = Activator.CreateInstance(sampleType, true);
        add.Invoke(sample, new object[] { 8f });
        Check((float)complete.Invoke(sample, new object[] { false }) == 0
            && (float)complete.Invoke(sample, new object[] { true }) == 0,
            "exceptional parry discards partial observations permanently");
        sample = Activator.CreateInstance(sampleType, true);
        Check((float)complete.Invoke(sample, new object[] { true }) == 0,
            "unconsumed damage enumerable cannot produce a parry ghost");
        float saturated = (float)hooks.GetMethod("Sum", Flags).Invoke(null, new object[] { float.MaxValue, float.MaxValue });
        Check(saturated == float.MaxValue, "large combined parry force remains finite");

        MethodInfo owns = hooks.GetMethod("OwnsContact", Flags);
        object defender = new object(), attacker = new object(), other = new object();
        Check((bool)owns.Invoke(null, new[] { defender, attacker, defender, attacker }),
            "melee contact requires the native resolution's exact defender and attacker");
        Check(!(bool)owns.Invoke(null, new[] { other, attacker, defender, attacker })
            && !(bool)owns.Invoke(null, new[] { defender, other, defender, attacker })
            && !(bool)owns.Invoke(null, new[] { defender, null, defender, null }),
            "unrelated target, environmental damage and other instigators do not fake melee contact");
        MethodInfo force = hooks.GetMethod("ContactForce", Flags);
        Check((float)force.Invoke(null, new object[] { true, 12f }) == 12,
            "completed native melee contact retains force even when downstream armor absorbs all damage");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Check((float)force.Invoke(null, new object[] { true, invalid }) == 0, "nonpositive or invalid contact force does not emit");
        Check((float)force.Invoke(null, new object[] { false, 12f }) == 0,
            "failed native damage application cannot emit cosmetic contact");

        // Null verbs/targets exercise only managed nesting; no game or native
        // shield constructors, random calls, or rendering are involved.
        FieldInfo parry = hooks.GetField("parry", Flags), melee = hooks.GetField("melee", Flags);
        object savedParry = parry.GetValue(null), savedMelee = melee.GetValue(null);
        try
        {
            parry.SetValue(null, null); melee.SetValue(null, null);
            MethodInfo beginParry = hooks.GetMethod("BeginParry", Flags), endParry = hooks.GetMethod("EndParry", Flags);
            object first = beginParry.Invoke(null, new object[] { null, null, null, false });
            object nested = beginParry.Invoke(null, new object[] { null, null, null, true });
            Check(ReferenceEquals(parry.GetValue(null), nested), "nested CE parry owns its observation scope");
            endParry.Invoke(null, new[] { nested, (object)false });
            Check(ReferenceEquals(parry.GetValue(null), first), "exceptional nested CE parry restores its parent scope");
            endParry.Invoke(null, new[] { nested, (object)true });
            Check(ReferenceEquals(parry.GetValue(null), first), "repeated finalizer cannot discard the parent scope");
            endParry.Invoke(null, new[] { first, (object)false });
            Check(parry.GetValue(null) == null, "outer CE parry finalizer removes observation state");
            MethodInfo beginMelee = hooks.GetMethod("BeginMelee", Flags), endMelee = hooks.GetMethod("EndMelee", Flags);
            object emptyTarget = Activator.CreateInstance(beginMelee.GetParameters()[1].ParameterType);
            first = beginMelee.Invoke(null, new[] { null, emptyTarget });
            nested = beginMelee.Invoke(null, new[] { null, emptyTarget });
            endMelee.Invoke(null, new[] { nested });
            Check(ReferenceEquals(melee.GetValue(null), first), "nested melee/riposte cleanup restores native caller scope");
            endMelee.Invoke(null, new[] { first });
            Check(melee.GetValue(null) == null, "melee cleanup removes scope after success or exception");
        }
        finally { parry.SetValue(null, savedParry); melee.SetValue(null, savedMelee); }
    }
}
