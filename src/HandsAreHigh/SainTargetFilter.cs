using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace HandsAreHigh
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
    internal static class SainTargetFilter
    {
        private static bool _tried;
        private static PropertyInfo _owner, _visible, _known, _enemyId;
        private static FieldInfo _goal;
        internal static int Filtered;     // times a prisoner was kept out of SAIN's choice

        private sealed class Removed
        {
            internal readonly List<object> Visible = new List<object>(2);
            internal readonly List<object> Known = new List<object>(2);
        }

        internal static void Reset() { _tried = false; Filtered = 0; }

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
                if (choose == null || _owner == null || _visible == null || _known == null || _goal == null || _enemyId == null ||
                    !typeof(IList).IsAssignableFrom(_visible.PropertyType) || HandsAreHighPlugin.Harmony == null)
                {
                    HandsAreHighPlugin.Log.LogWarning("SAIN target filter: SAIN's enemy controller didn't match (different SAIN version?); " +
                        "restraint relies on the game's target and the trigger guard");
                    return;
                }
                HandsAreHighPlugin.Harmony.Patch(choose,
                    prefix: new HarmonyMethod(typeof(SainTargetFilter), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(SainTargetFilter), nameof(Postfix)));
                HandsAreHighPlugin.Log.LogInfo("SAIN target filter on: a bot that holds fire at a prisoner doesn't pick him as SAIN's target");
            }
            catch (Exception e)
            {
                HandsAreHighPlugin.Log.LogWarning($"SAIN target filter not applied: {e.Message}");
            }
        }

        private static bool Blocked(Player self, object enemy)
        {
            if (enemy == null) return false;
            string id = _enemyId.GetValue(enemy, null) as string;
            return Restraint.HoldsFireAtId(self, id) || ScenarioShield.BlocksId(self, id);
        }

        private static void Prefix(object __instance, ref object __state)
        {
            __state = null;
            if (!Restraint.Any && !ScenarioShield.Active) return;
            try
            {
                Player self = (_owner.GetValue(__instance, null) as BotOwner)?.GetPlayer;
                if (!Restraint.Restrains(self) && !ScenarioShield.Shields(self)) return;
                Removed r = null;
                Take(_visible.GetValue(__instance, null) as IList, self, ref r, true);
                Take(_known.GetValue(__instance, null) as IList, self, ref r, false);
                object goal = _goal.GetValue(__instance);
                if (goal != null && Blocked(self, goal))
                {
                    _goal.SetValue(__instance, null);
                    if (r == null) r = new Removed();
                }
                if (r != null) Filtered++;
                __state = r;
            }
            catch { __state = null; }
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

        private static void Postfix(object __instance, object __state)
        {
            if (!(__state is Removed r)) return;
            try
            {
                Put(_visible.GetValue(__instance, null) as IList, r.Visible);
                Put(_known.GetValue(__instance, null) as IList, r.Known);
            }
            catch { }
        }

        private static void Put(IList list, List<object> items)
        {
            if (list == null) return;
            for (int i = 0; i < items.Count; i++)
                if (!list.Contains(items[i])) list.Add(items[i]);
        }
    }
}
