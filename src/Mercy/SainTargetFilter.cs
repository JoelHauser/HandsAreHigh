using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace Mercy
{
    // SAIN picks its own target (0.14.0). SAINEnemyController.ChooseEnemy -> SelectEnemy chooses
    // from SAIN's own VisibleEnemies / KnownEnemies lists and keeps the choice in its private
    // _goalEnemy; it only mirrors it into the game's BotMemory.GoalEnemy afterwards (setGoalEnemy,
    // which swallows exceptions). So our GoalEnemy setter prefix kept the vanilla field clear while
    // SAIN went on aiming at, and pressing the trigger on, a prisoner the bot had decided to spare:
    // the trigger guard was the only thing in the way (RESTRAINT LEAK lines in the 0.13.1 log).
    //
    // For a bot that holds fire at someone (Restraint) or is under a scenario shield, and only
    // then, the prisoners are taken out of those two lists for the duration of ChooseEnemy and
    // put back right after; a SAIN goal that is one of them is dropped. SAIN then picks the best
    // of everyone else, or nobody, by its own rules. Nothing else in SAIN is touched, and every
    // other bot costs one bool read. Members verified against SAIN 4.5.1 (decompiled):
    //   SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController
    //     public Enemy ChooseEnemy(); EnemyList VisibleEnemies, KnownEnemies (List<Enemy>);
    //     private Enemy _goalEnemy; BotOwner BotOwner (from BotBase)
    //   ...EnemyClasses.Enemy.EnemyProfileId (string)
    // Anything missing: not patched, logged once, the trigger guard stays the last line.
    //
    // 0.18.0, the other way round: a bot that DECIDED to kill his prisoner (Judgement.Escalate) gets
    // that man as SAIN's target when SAIN chose someone (or nobody) else, the man is in SAIN's own
    // VisibleEnemies and nothing is shooting at the bot (a real threat still comes first). Set
    // through SAIN's GoalEnemy setter (its EnemyChanged event fires as for its own choice) and
    // mirrored to BotMemory.GoalEnemy as SAIN's setGoalEnemy does. 0.19.0: that setter is PRIVATE
    // (SAIN 4.5.1: `public Enemy GoalEnemy { get; private set; }`, which is how ChooseEnemy itself
    // assigns it); 0.18.0 asked for a public one, got none, and never steered (12:43 log: "NO:
    // GoalEnemy has no public setter"). 0.17.0 only set the game's
    // GoalEnemy, which SAIN overwrites with its own pick: the retarget did nothing on SAIN bots.
    internal static class SainTargetFilter
    {
        private static bool _tried;
        private static PropertyInfo _owner, _visible, _known, _enemyId, _goalProp, _enemyInfo;
        private static MethodInfo _goalSet;
        internal static bool Patched;
        private static FieldInfo _goal;
        // 0.20.1: compiled reads (FastAccess.cs). The owner read runs for every SAIN bot's
        // ChooseEnemy (every 0.1 s) while anyone is restrained; the rest only for restrained bots.
        private static FastGetter<BotOwner> _ownerRead;
        private static FastGetter<IList> _visibleRead, _knownRead;
        private static FastGetter<object> _goalRead;
        private static FastGetter<string> _enemyIdRead;
        internal static int Filtered;     // times a prisoner was kept out of SAIN's choice

        private sealed class Removed
        {
            internal readonly List<object> Visible = new List<object>(2);
            internal readonly List<object> Known = new List<object>(2);
        }

        internal static void Reset()
        {
            _tried = false; Filtered = 0; Patched = false;
            _ownerRead = null; _visibleRead = null; _knownRead = null; _goalRead = null; _enemyIdRead = null;
        }

        internal static void TryPatch(Assembly sain)
        {
            if (_tried || sain == null) return;
            _tried = true;
            try
            {
                Type ctrl = sain.GetType("SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController");
                Type enemy = sain.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy");
                MethodInfo choose = ctrl != null ? AccessTools.Method(ctrl, "ChooseEnemy", Type.EmptyTypes) : null;
                _owner = ctrl != null ? AccessTools.Property(ctrl, "BotOwner") : null;
                _visible = ctrl != null ? AccessTools.Property(ctrl, "VisibleEnemies") : null;
                _known = ctrl != null ? AccessTools.Property(ctrl, "KnownEnemies") : null;
                _goal = ctrl != null ? AccessTools.Field(ctrl, "_goalEnemy") : null;
                _enemyId = enemy != null ? AccessTools.Property(enemy, "EnemyProfileId") : null;
                _goalProp = ctrl != null ? AccessTools.Property(ctrl, "GoalEnemy") : null;
                _goalSet = _goalProp?.GetSetMethod(true);
                _enemyInfo = enemy != null ? AccessTools.Property(enemy, "EnemyInfo") : null;
                _ownerRead = FastGetter<BotOwner>.For(_owner);
                _visibleRead = FastGetter<IList>.For(_visible);
                _knownRead = FastGetter<IList>.For(_known);
                _goalRead = FastGetter<object>.For(_goal);
                _enemyIdRead = FastGetter<string>.For(_enemyId);
                if (choose == null || _owner == null || _visible == null || _known == null || _goal == null || _enemyId == null ||
                    _ownerRead == null || _visibleRead == null || _knownRead == null || _goalRead == null || _enemyIdRead == null ||
                    !typeof(IList).IsAssignableFrom(_visible.PropertyType) || MercyPlugin.Harmony == null)
                {
                    MercyPlugin.Log.LogWarning("SAIN target filter: SAIN's enemy controller didn't match (different SAIN version?); " +
                        "restraint relies on the game's target and the trigger guard");
                    return;
                }
                MercyPlugin.Harmony.Patch(choose,
                    prefix: new HarmonyMethod(typeof(SainTargetFilter), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(SainTargetFilter), nameof(Postfix)));
                Patched = true;
                MercyPlugin.Log.LogInfo($"SAIN target filter on: a bot that holds fire at a prisoner doesn't pick him as SAIN's target; " +
                    $"a bot that decided to kill one is steered to him ({(_goalSet != null ? "yes" : "NO: SAIN's GoalEnemy has no setter")})");
            }
            catch (Exception e)
            {
                MercyPlugin.Log.LogWarning($"SAIN target filter not applied: {e.Message}");
            }
        }

        private static bool Blocked(Player self, object enemy)
        {
            if (enemy == null) return false;
            string id = _enemyIdRead.Get(enemy);
            return Restraint.HoldsFireAtId(self, id) || ScenarioShield.BlocksId(self, id) || Truce.BlocksId(self, id);
        }

        private static void Prefix(object __instance, ref object __state)
        {
            __state = null;
            if (!Restraint.Any && !ScenarioShield.Active && !Truce.Active) return;
            long t0 = Perf.Begin();
            try
            {
                Player self = _ownerRead.Get(__instance)?.GetPlayer;
                if (!Restraint.Restrains(self) && !ScenarioShield.Shields(self) && !Truce.Holds(self)) return;
                Removed r = null;
                Take(_visibleRead.Get(__instance), self, ref r, true);
                Take(_knownRead.Get(__instance), self, ref r, false);
                object goal = _goalRead.Get(__instance);
                if (goal != null && Blocked(self, goal))
                {
                    _goal.SetValue(__instance, null);
                    if (r == null) r = new Removed();
                }
                if (r != null) Filtered++;
                __state = r;
            }
            catch { __state = null; }
            finally { Perf.End(Perf.Sec.SainFilter, t0); }
        }

        private static void Take(IList list, Player self, ref Removed r, bool visible)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                object e = list[i];
                if (!Blocked(self, e)) continue;
                if (r == null) r = new Removed();
                (visible ? r.Visible : r.Known).Add(e);
                list.RemoveAt(i);
            }
        }

        private static void Postfix(object __instance, object __state, ref object __result)
        {
            if (!(__state is Removed) && !Judgement.Watching) return;
            long t0 = Perf.Begin();
            if (__state is Removed r)
            {
                try
                {
                    Put(_visibleRead.Get(__instance), r.Visible);
                    Put(_knownRead.Get(__instance), r.Known);
                }
                catch { }
            }
            if (Judgement.Watching) Steer(__instance, ref __result);
            Perf.End(Perf.Sec.SainFilter, t0);
        }

        // 0.18.0: see the header. Only bots with a kill decision being watched get here.
        private static void Steer(object ctrl, ref object result)
        {
            try
            {
                BotOwner o = _owner.GetValue(ctrl, null) as BotOwner;
                Player victim = Judgement.KillTarget(o);
                if (victim == null) return;
                string chosen = result != null ? _enemyId.GetValue(result, null) as string : null;
                if (chosen == victim.ProfileId) { Judgement.NoteSainGoal(o, Restraint.Name(victim), steered: false, onVictim: true); return; }
                string chosenName = chosen == null ? null : Restraint.Name(FindPlayer(chosen)) ?? chosen;
                MethodInfo set = _goalSet;
                if (set == null || Suppression.Decayed(SurrenderRegistry.HistoryOf(o.GetPlayer), UnityEngine.Time.time) >= 1f)
                {
                    Judgement.NoteSainGoal(o, chosenName, steered: false);
                    return;
                }
                IList vis = _visible.GetValue(ctrl, null) as IList;
                if (vis == null) { Judgement.NoteSainGoal(o, chosenName, steered: false); return; }
                for (int i = 0; i < vis.Count; i++)
                {
                    object e = vis[i];
                    if (e == null || (_enemyId.GetValue(e, null) as string) != victim.ProfileId) continue;
                    set.Invoke(ctrl, new[] { e });
                    if (_goalProp.GetValue(ctrl, null) != e) break;   // SAIN refused it (no known position yet)
                    result = e;
                    if (_enemyInfo?.GetValue(e, null) is EnemyInfo info && o.Memory != null && o.Memory.GoalEnemy != info)
                    {
                        o.Memory.IsPeace = false;
                        try { o.Memory.GoalEnemy = info; } catch { }
                    }
                    Judgement.NoteSainGoal(o, chosenName, steered: true);
                    return;
                }
                Judgement.NoteSainGoal(o, chosenName, steered: false);
            }
            catch { }
        }

        private static Player FindPlayer(string profileId)
        {
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return null;
            foreach (Player p in world.AllAlivePlayersList) if (p != null && p.ProfileId == profileId) return p;
            return null;
        }

        private static void Put(IList list, List<object> items)
        {
            if (list == null) return;
            for (int i = 0; i < items.Count; i++)
                if (!list.Contains(items[i])) list.Add(items[i]);
        }
    }
}
