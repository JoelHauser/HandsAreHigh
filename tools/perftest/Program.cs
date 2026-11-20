using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace HandsAreHigh
{
    // Shapes copied from SAIN 4.5.1 (decompiled): what SainAdapter / SainTargetFilter read.
    internal sealed class Owner { public string Name = "bot"; }
    internal enum ESAINLayer { None, Combat, Squad, Extract }                 // int-backed, as SAIN's
    internal enum ESuppressionState { None, Light, Medium, Heavy, Extreme }
    internal enum LongEnum : long { A, B = 5 }                                // not int: must not compile
    internal sealed class Enemy { public Enemy(string id) { EnemyProfileId = id; } public string EnemyProfileId { get; } }
    internal sealed class EnemyList : List<Enemy> { }
    internal sealed class Bot { public Owner BotOwner { get; } = new Owner(); }
    internal abstract class BotBase { protected Bot Bot = new Bot(); public Owner BotOwner => Bot.BotOwner; }   // inherited, as SAIN's BotBase
    internal sealed class EnemyController : BotBase
    {
        private Enemy _goalEnemy;
        public EnemyList VisibleEnemies { get; private set; } = new EnemyList();
        public Enemy GoalEnemy { get => _goalEnemy; private set => _goalEnemy = value; }
        internal void SetGoal(Enemy e) => GoalEnemy = e;
    }
    internal sealed class Suppress { public ESuppressionState CurrentState { get; private set; } internal void Set(ESuppressionState s) => CurrentState = s; }
    internal sealed class BotComponent
    {
        private ESAINLayer _layer = ESAINLayer.Combat;
        public ESAINLayer ActiveLayer { get => _layer; set => _layer = value; }
        public Suppress Suppression { get; private set; } = new Suppress();
        public LongEnum Wide { get; set; } = LongEnum.B;
        private int _n;
        public int Counter => ++_n;   // a different answer every read: the safety net must catch it
    }

    internal static class Program
    {
        private static int _fail;
        private static readonly List<string> Warnings = new List<string>();

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
            if (!ok) _fail++;
        }

        private static PropertyInfo P(Type t, string n) => t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static int Main(string[] args)
        {
            FastAccess.Warn = Warnings.Add;
            var ctrl = new EnemyController();
            var comp = new BotComponent();

            // Inherited property declared on an abstract base, read through the derived instance.
            var owner = FastGetter<Owner>.For(P(typeof(EnemyController), "BotOwner"));
            Check(owner != null && owner.Compiled, "owner getter compiles (inherited property)");
            bool same = true;
            for (int i = 0; i < 10; i++) same &= ReferenceEquals(owner.Get(ctrl), P(typeof(EnemyController), "BotOwner").GetValue(ctrl, null));
            Check(same, "owner reads = reflection (10 reads)");
            Check(owner.Verified, "owner getter verified after its first reads");

            // List-derived property as IList; edits through it reach the same list.
            var vis = FastGetter<IList>.For(P(typeof(EnemyController), "VisibleEnemies"));
            ctrl.VisibleEnemies.Add(new Enemy("a"));
            Check(vis.Compiled && ReferenceEquals(vis.Get(ctrl), ctrl.VisibleEnemies), "visible list getter returns SAIN's own list");

            // Private field (SAIN's _goalEnemy), null and set.
            var goal = FastGetter<object>.For(typeof(EnemyController).GetField("_goalEnemy", BindingFlags.NonPublic | BindingFlags.Instance));
            Check(goal.Compiled && goal.Get(ctrl) == null, "private goal field reads null");
            var e = new Enemy("b");
            ctrl.SetGoal(e);
            for (int i = 0; i < 10; i++) goal.Get(ctrl);
            Check(goal.Verified && ReferenceEquals(goal.Get(ctrl), e), "private goal field reads the set enemy after verification");

            // Get-only string property.
            var id = FastGetter<string>.For(P(typeof(Enemy), "EnemyProfileId"));
            for (int i = 0; i < 10; i++) id.Get(e);
            Check(id.Verified && id.Get(e) == "b", "enemy id reads (verified)");

            // int-backed enums as int, the way SainAdapter compares them.
            var layer = FastGetter<int>.For(P(typeof(BotComponent), "ActiveLayer"));
            Check(layer.Compiled && layer.Get(comp) == (int)ESAINLayer.Combat, "active layer reads as int (Combat = 1)");
            comp.ActiveLayer = ESAINLayer.None;
            for (int i = 0; i < 10; i++) layer.Get(comp);
            Check(layer.Verified && layer.Get(comp) == Convert.ToInt32(Enum.Parse(typeof(ESAINLayer), "None")), "active layer None = SainAdapter's _noLayerValue");

            var sup = FastGetter<object>.For(P(typeof(BotComponent), "Suppression"));
            var state = FastGetter<int>.For(P(typeof(Suppress), "CurrentState"));
            comp.Suppression.Set(ESuppressionState.Heavy);
            for (int i = 0; i < 10; i++) state.Get(sup.Get(comp));
            Check(state.Verified && state.Get(sup.Get(comp)) == 3, "suppression Heavy reads 3 (level 0.75)");

            // A long-backed enum must not be compiled as int; reflection still answers correctly.
            var wide = FastGetter<int>.For(P(typeof(BotComponent), "Wide"));
            Check(wide != null && !wide.Compiled && wide.Get(comp) == 5, "non-int enum: no compiled reader, reflection value kept");

            // Safety net: a disagreement drops the compiled reader for good and warns once.
            var counter = FastGetter<int>.For(P(typeof(BotComponent), "Counter"));
            Check(counter.Compiled, "side-effect property compiles");
            counter.Get(comp);
            Check(!counter.Compiled && Warnings.Count == 1, "disagreement falls back to reflection, one warning");
            counter.Get(comp);
            Check(Warnings.Count == 1, "no further warnings after the fallback");

            // Missing members: no reader (callers treat it as the member being absent, as before).
            Check(FastGetter<int>.For((PropertyInfo)null) == null && FastGetter<object>.For((FieldInfo)null) == null, "missing member gives no reader");

            // Null target: an exception (callers wrap every read in try/catch, as with reflection).
            bool threw = false;
            try { owner.Get(null); } catch (Exception) { threw = true; }
            Check(threw, "null target throws (caught by the callers)");

            // Rough cost on .NET Framework (indicative only; the game runs Mono).
            PropertyInfo pi = P(typeof(EnemyController), "BotOwner");
            PropertyInfo li = P(typeof(BotComponent), "ActiveLayer");
            const int N = 2_000_000;
            object sink = null;
            int isink = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) sink = pi.GetValue(ctrl, null);
            double refl = sw.Elapsed.TotalMilliseconds * 1e6 / N;
            sw.Restart();
            for (int i = 0; i < N; i++) sink = owner.Get(ctrl);
            double fast = sw.Elapsed.TotalMilliseconds * 1e6 / N;
            int gc0 = GC.CollectionCount(0);
            sw.Restart();
            for (int i = 0; i < N; i++) isink += Convert.ToInt32(li.GetValue(comp, null));
            double reflEnum = sw.Elapsed.TotalMilliseconds * 1e6 / N;
            int reflGcs = GC.CollectionCount(0) - gc0;
            gc0 = GC.CollectionCount(0);
            sw.Restart();
            for (int i = 0; i < N; i++) isink += layer.Get(comp);
            double fastEnum = sw.Elapsed.TotalMilliseconds * 1e6 / N;
            int fastGcs = GC.CollectionCount(0) - gc0;
            Console.WriteLine($"cost per read: owner reflection {refl:0} ns, compiled {fast:0} ns; enum reflection {reflEnum:0} ns, compiled {fastEnum:0} ns");
            Console.WriteLine($"gen0 collections over {N:N0} enum reads: reflection {reflGcs}, compiled {fastGcs}");
            GC.KeepAlive(sink);
            GC.KeepAlive(isink);
            Check(fastGcs == 0, "compiled enum reads cause no collections (no allocation)");

            if (args.Length >= 2 && args[0] == "sain")
            {
                try { RealSain.Run(args[1], Check); }
                catch (Exception ex) { Check(false, "real SAIN run threw: " + ex); }
            }

            Console.WriteLine(_fail == 0 ? "ALL PASS" : $"{_fail} FAILED");
            return _fail == 0 ? 0 : 1;
        }
    }
}
