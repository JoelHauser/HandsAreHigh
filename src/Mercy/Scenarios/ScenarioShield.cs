using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Mercy
{
    // Debug-scenario fire control, scoped to (shooter, target) pairs a scenario created, never
    // global. Two kinds:
    //   Observer  a scenario participant doesn't take the observing player as a target (debug
    //             override, on by default for bot-vs-bot presets, shown on the overlay, can be
    //             turned off at any time to intervene naturally).
    //   Truce     two participants don't fire at each other while the scenario is being set up
    //             (they spawn a second or two apart), or for a whole comparison scenario.
    // Made to hold by the same hooks as restraint (Restraint.cs): the BotMemory.GoalEnemy setter
    // prefix, the ShootData.Shoot prefix and the SetTriggerPressed last line. Observer pairs
    // also set the game's own EnemyInfo ignore flag (only where it was clear, cleared on lift),
    // so the vanilla chooser skips the player. Truce pairs don't touch the flag: a captor's
    // restraint may own the same EnemyInfo's flag.
    // Nothing else changes: no damage immunity, perception, hearing or relationship edit. A
    // participant's bullet that still hits the observer is logged as SCENARIO SHIELD LEAK.
    internal enum ShieldKind { Observer, Truce }

    internal static class ScenarioShield
    {
        private sealed class Pair
        {
            internal Scenario Owner;
            internal ShieldKind Kind;
            internal Player Shooter;
            internal Player Target;
            internal EnemyInfo Flagged;
        }

        internal static bool Active;   // the patches read only this when no scenario runs
        private static readonly List<Pair> Pairs = new List<Pair>();
        private static readonly Dictionary<Player, List<Pair>> ByShooter = new Dictionary<Player, List<Pair>>();
        private static float _nextScan;
        private static float _logAt;
        private static Player _hooked;
        private static System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float> _hitHandler;

        internal static bool Blocks(Player shooter, IPlayer target)
        {
            if (!Active || shooter == null || target == null) return false;
            if (!ByShooter.TryGetValue(shooter, out List<Pair> list)) return false;
            for (int i = 0; i < list.Count; i++)
                if (Perception.Same(target, list[i].Target)) return true;
            return false;
        }

        // By profile id (SAIN's Enemy objects).
        internal static bool BlocksId(Player shooter, string profileId)
        {
            if (!Active || shooter == null || profileId == null) return false;
            if (!ByShooter.TryGetValue(shooter, out List<Pair> list)) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Target != null && list[i].Target.ProfileId == profileId) return true;
            return false;
        }

        internal static bool Shields(Player shooter) => Active && shooter != null && ByShooter.ContainsKey(shooter);

        // Same aim test as the restraint trigger guard: pressing the trigger with the aim on a
        // shielded target.
        internal static bool AimedAtShielded(Player shooter, out Player target)
        {
            target = null;
            if (!ByShooter.TryGetValue(shooter, out List<Pair> list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                Player p = list[i].Target;
                // 0.21.0: on the actual line of fire, and not behind the shooter's own target
                // (Perception.InLineOfFire). The flat 6 deg cone blocked every shot a hostile third
                // party fired at a captor with you anywhere near that bearing: it looked friendly.
                if (!Perception.InLineOfFire(shooter, p, 200f)) continue;
                target = p;
                return true;
            }
            return false;
        }

        internal static void Add(Scenario sc, ShieldKind kind, Player shooter, Player target)
        {
            if (shooter == null || target == null || shooter == target) return;
            if (ByShooter.TryGetValue(shooter, out List<Pair> list))
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Target == target && list[i].Kind == kind) return;
            var p = new Pair { Owner = sc, Kind = kind, Shooter = shooter, Target = target };
            Pairs.Add(p);
            if (list == null) { list = new List<Pair>(2); ByShooter[shooter] = list; }
            list.Add(p);
            Active = true;
            Apply(p);
            if (Settings.VerboseLogs.Value)
                MercyPlugin.Log.LogInfo($"Scenario shield: {kind} pair {Restraint.Name(shooter)} -> {Restraint.Name(target)}{(p.Flagged != null ? " (ignore flag set)" : "")}");
            if (kind == ShieldKind.Observer) HookObserver(target);
        }

        internal static int Count(Scenario sc, ShieldKind kind)
        {
            int n = 0;
            for (int i = 0; i < Pairs.Count; i++) if (Pairs[i].Owner == sc && Pairs[i].Kind == kind) n++;
            return n;
        }

        // Lift every pair of one scenario (optionally one kind).
        internal static int Remove(Scenario sc, ShieldKind? kind = null)
        {
            int n = 0;
            for (int i = Pairs.Count - 1; i >= 0; i--)
            {
                Pair p = Pairs[i];
                if (p.Owner != sc || (kind != null && p.Kind != kind.Value)) continue;
                Lift(p);
                Pairs.RemoveAt(i);
                if (ByShooter.TryGetValue(p.Shooter, out List<Pair> list))
                {
                    list.Remove(p);
                    if (list.Count == 0) ByShooter.Remove(p.Shooter);
                }
                n++;
            }
            Active = Pairs.Count > 0;
            if (!AnyObserver()) UnhookObserver();
            return n;
        }

        private static bool AnyObserver()
        {
            for (int i = 0; i < Pairs.Count; i++) if (Pairs[i].Kind == ShieldKind.Observer) return true;
            return false;
        }

        private static void Apply(Pair p)
        {
            BotOwner o = p.Shooter.AIData?.BotOwner;
            if (o == null) return;
            try
            {
                EnemyInfo goal = o.Memory?.GoalEnemy;
                if (goal != null && Perception.Same(goal.Person, p.Target))
                {
                    o.ShootData?.EndShoot();
                    o.Memory.GoalEnemy = null;
                }
                if (p.Kind != ShieldKind.Observer || p.Flagged != null) return;
                EnemyInfo info = Perception.InfoAbout(o, p.Target);
                if (info == null) return;
                if (info.IgnoreUntilAggression) return;   // set by someone else: left alone
                info.SetIgnoreState(true);
                p.Flagged = info;
            }
            catch (System.Exception e) { MercyPlugin.Report("scenario shield apply", e); }
        }

        private static void Lift(Pair p)
        {
            if (p.Flagged == null) return;
            try { p.Flagged.SetIgnoreState(false); } catch { }
            p.Flagged = null;
        }

        // Every 0.5 s while any pair exists: flags for EnemyInfos that appeared since, dead
        // shooters dropped.
        internal static void Tick()
        {
            if (!Active || Time.time < _nextScan) return;
            _nextScan = Time.time + 0.5f;
            for (int i = Pairs.Count - 1; i >= 0; i--)
            {
                Pair p = Pairs[i];
                if (!Perception.Alive(p.Shooter))
                {
                    Lift(p);
                    Pairs.RemoveAt(i);
                    if (ByShooter.TryGetValue(p.Shooter, out List<Pair> list)) { list.Remove(p); if (list.Count == 0) ByShooter.Remove(p.Shooter); }
                    continue;
                }
                if (p.Flagged == null || !p.Flagged.IgnoreUntilAggression) { p.Flagged = null; Apply(p); }
            }
            Active = Pairs.Count > 0;
            if (!AnyObserver()) UnhookObserver();
        }

        // A participant's hit on the observer despite the shield: the hooks missed a fire path.
        private static void HookObserver(Player me)
        {
            if (_hooked == me || me == null) return;
            UnhookObserver();
            _hooked = me;
            _hitHandler = (info, part, absorbed) =>
            {
                Player shooter = null;
                try { shooter = Perception.PlayerOf(info.Player?.iPlayer); } catch { }
                if (shooter != null && Blocks(shooter, me))
                    MercyPlugin.Log.LogWarning($"SCENARIO SHIELD LEAK (hit): {Restraint.Name(shooter)} hit you through the observer override ({part})");
            };
            me.BeingHitAction += _hitHandler;
        }

        private static void UnhookObserver()
        {
            if ((object)_hooked != null && _hitHandler != null)
            {
                try { _hooked.BeingHitAction -= _hitHandler; } catch { }
            }
            _hooked = null;
            _hitHandler = null;
        }

        // 0.22.0: the observer hit a participant (FireBreak.OnHit: a captor or a prisoner). He is no
        // longer watching, he is in the fight: the Observer pairs that kept that participant and his
        // squad from taking him as a target are lifted, so they answer him with their own AI and
        // perception (nothing tells them where he is; the game's own hit reaction does that). The
        // 0.21.0 raid: you shot a captor six times and he never fought back (the override held).
        internal static void ObserverAttacked(Player victim, Player attacker)
        {
            if (!Active || victim == null || attacker == null || attacker.IsAI || !victim.IsAI) return;
            BotsGroup g = victim.AIData?.BotOwner?.BotsGroup;
            int n = 0;
            for (int i = Pairs.Count - 1; i >= 0; i--)
            {
                Pair p = Pairs[i];
                if (p.Kind != ShieldKind.Observer || p.Target != attacker) continue;
                bool squad = p.Shooter == victim || (g != null && p.Shooter?.AIData?.BotOwner?.BotsGroup == g);
                if (!squad) continue;
                Lift(p);
                Pairs.RemoveAt(i);
                if (ByShooter.TryGetValue(p.Shooter, out List<Pair> list)) { list.Remove(p); if (list.Count == 0) ByShooter.Remove(p.Shooter); }
                n++;
            }
            if (n == 0) return;
            Active = Pairs.Count > 0;
            if (!AnyObserver()) UnhookObserver();
            MercyPlugin.Log.LogInfo($"Scenario shield: you hit {Restraint.Name(victim)}: the observer override is lifted for him and his squad ({n} pair(s)); they may fight you now");
        }

        internal static int Blocked;    // fire held back since the last report

        internal static void LogBlocked(Player shooter, Player target, string where)
        {
            Blocked++;
            if (Time.realtimeSinceStartup - _logAt < 2f) return;
            _logAt = Time.realtimeSinceStartup;
            MercyPlugin.Log.LogInfo($"Scenario shield: {Restraint.Name(shooter)}'s fire at {Restraint.Name(target)} blocked at {where} (scenario override, not a game decision)");
        }

        internal static void Clear()
        {
            for (int i = 0; i < Pairs.Count; i++) Lift(Pairs[i]);
            Pairs.Clear();
            ByShooter.Clear();
            Active = false;
            UnhookObserver();
        }

        internal static int Total => Pairs.Count;
    }
}
