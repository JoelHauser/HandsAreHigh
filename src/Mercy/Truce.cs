using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Mercy
{
    // 0.28.0: after an ignored surrender (Captors.TryIgnore), the man who gave up and the bot that
    // wasn't interested in him (and that bot's squadmates who held fire at him) leave each other be
    // for a while, both ways, armed or not. Without it the hand-back dropped every hold-fire decision
    // about a man who had calmly picked his own gun back up (Restraint.ForgetPrisoner), and both
    // combat AIs still had the other as a known enemy a few metres away: a stale target became an
    // instant firefight that neither had chosen.
    //
    // Scope: (holder, other) pairs, never global, never damage immunity, no relationship edit. Made
    // to hold by the same hooks as restraint (GoalEnemy setter, ShootData.Shoot, SetTriggerPressed,
    // SAIN's target choice); the EnemyInfo ignore flag is not touched (a restraint may own it).
    // Ends for a pair:
    //   - self-defence: either of the two is hit by the other (BeingHitAction on both), at once,
    //     both ways; the victim's AI answers with its own perception;
    //   - either dies, or is held again (surrenders, is taken prisoner);
    //   - the time runs out. Then a holder who doesn't see the other has a stale target on him
    //     cleared (nothing reactivates from old memory); one who does sees an enemy and is judged as
    //     anyone else from here (normal faction relationships).
    internal static class Truce
    {
        private sealed class Pair
        {
            internal Player Holder, Other;
            internal string Why;
            internal float Since, Until;
        }

        internal static bool Active;   // the patches read only this when no truce exists
        private static readonly List<Pair> Pairs = new List<Pair>(4);
        private static readonly Dictionary<Player, System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float>> Hooks =
            new Dictionary<Player, System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float>>();
        private static readonly List<Player> Gone = new List<Player>(4);
        private static float _nextTick, _logAt;
        internal static int Blocked;

        internal static int Count => Pairs.Count;

        internal static bool Blocks(Player holder, IPlayer other)
        {
            if (!Active || holder == null || other == null) return false;
            for (int i = 0; i < Pairs.Count; i++)
                if (Pairs[i].Holder == holder && Perception.Same(other, Pairs[i].Other)) return true;
            return false;
        }

        // By profile id (SAIN's Enemy objects carry the id).
        internal static bool BlocksId(Player holder, string profileId)
        {
            if (!Active || holder == null || profileId == null) return false;
            for (int i = 0; i < Pairs.Count; i++)
                if (Pairs[i].Holder == holder && Pairs[i].Other != null && Pairs[i].Other.ProfileId == profileId) return true;
            return false;
        }

        internal static bool Holds(Player holder)
        {
            if (!Active || holder == null) return false;
            for (int i = 0; i < Pairs.Count; i++) if (Pairs[i].Holder == holder) return true;
            return false;
        }

        // The trigger guard's aim test (the restraint one: his body on the actual line of fire).
        internal static bool AimedAtPartner(Player shooter, out Player other)
        {
            other = null;
            for (int i = 0; i < Pairs.Count; i++)
            {
                Pair p = Pairs[i];
                if (p.Holder != shooter || !Perception.InLineOfFire(shooter, p.Other, 150f)) continue;
                other = p.Other;
                return true;
            }
            return false;
        }

        // The hand-back of a man whose surrender this bot ignored (SurrenderRegistry.Tick).
        internal static void Begin(SurrenderState s)
        {
            float secs = Settings.IgnoreTruceSeconds.Value;
            Player man = s?.Bot, ignorer = s?.IgnoredBy;
            if (secs <= 0f || !Perception.Alive(man) || !Perception.Alive(ignorer)) return;
            float now = Time.time;
            var names = new System.Text.StringBuilder(Restraint.Name(ignorer));
            Add(ignorer, man, now, secs, "ignored his surrender");
            Add(man, ignorer, now, secs, "was ignored by him");
            // His squadmates who went along with it (they held fire at the man).
            BotsGroup g = ignorer.AIData?.BotOwner?.BotsGroup;
            if (g != null)
            {
                for (int i = 0; i < g.MembersCount; i++)
                {
                    BotOwner m = g.Member(i);
                    Player mp = m?.GetPlayer;
                    if (mp == null || mp == ignorer || m.IsDead || !Perception.Alive(mp)) continue;
                    if (!Restraint.TryGet(man, mp, out RestraintEntry e) || !e.Holds || e.Final) continue;
                    Add(mp, man, now, secs, $"{Restraint.Name(ignorer)} ignored his surrender");
                    Add(man, mp, now, secs, $"{Restraint.Name(ignorer)}'s squad ignored him");
                    names.Append(", ").Append(Restraint.Name(mp));
                }
            }
            Active = Pairs.Count > 0;
            Hook(man);
            Hook(ignorer);
            if (Settings.LogRestraint.Value || Settings.LogEncounter.Value)
                MercyPlugin.Log.LogInfo($"Truce: {s.Name} ({Arsenal.DescribeArmed(man)}) and {names} leave each other be for up to {secs:0} s after the ignored surrender (ends at once if either hits the other)");
        }

        private static void Add(Player holder, Player other, float now, float secs, string why)
        {
            for (int i = 0; i < Pairs.Count; i++)
                if (Pairs[i].Holder == holder && Pairs[i].Other == other) { Pairs[i].Until = now + secs; return; }
            if (Pairs.Count >= 32) return;
            Pairs.Add(new Pair { Holder = holder, Other = other, Why = why, Since = now, Until = now + secs });
            Hook(holder);
            // His current target, if it is the other man, goes now (the chooser picks someone else).
            try
            {
                BotOwner o = holder.AIData?.BotOwner;
                EnemyInfo goal = o?.Memory?.GoalEnemy;
                if (goal != null && Perception.Same(goal.Person, other)) { o.ShootData?.EndShoot(); o.Memory.GoalEnemy = null; }
            }
            catch (System.Exception e) { MercyPlugin.Report("truce target", e); }
        }

        private static void Hook(Player p)
        {
            if ((object)p == null || Hooks.ContainsKey(p)) return;
            System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float> h = (info, part, absorbed) =>
            {
                Player shooter = null;
                try { shooter = Perception.PlayerOf(info.Player?.iPlayer); } catch { }
                if (shooter != null && shooter != p) Attacked(p, shooter);
            };
            try { p.BeingHitAction += h; Hooks[p] = h; } catch { }
        }

        private static void Unhook(Player p)
        {
            if ((object)p == null || !Hooks.TryGetValue(p, out var h)) return;
            try { p.BeingHitAction -= h; } catch { }
            Hooks.Remove(p);
        }

        // Self-defence: a hit from his truce partner ends their truce both ways, now.
        private static void Attacked(Player victim, Player shooter)
        {
            if (!Active) return;
            bool ended = false;
            for (int i = Pairs.Count - 1; i >= 0; i--)
            {
                Pair p = Pairs[i];
                if ((p.Holder == victim && p.Other == shooter) || (p.Holder == shooter && p.Other == victim)) { Pairs.RemoveAt(i); ended = true; }
            }
            if (!ended) return;
            Prune();
            MercyPlugin.Log.LogInfo($"Truce: ENDED: {Restraint.Name(shooter)} hit {Restraint.Name(victim)}; {Restraint.Name(victim)} may defend himself (his own AI)");
        }

        // Every 0.5 s (Restraint.Tick): dead or re-held partners, and the time limit.
        internal static void Tick()
        {
            if (!Active || Time.time < _nextTick) return;
            float now = Time.time;
            _nextTick = now + 0.5f;
            for (int i = Pairs.Count - 1; i >= 0; i--)
            {
                Pair p = Pairs[i];
                if (!Perception.Alive(p.Holder) || !Perception.Alive(p.Other)) { Pairs.RemoveAt(i); continue; }
                if (SurrenderRegistry.HoldsBot(p.Holder) || SurrenderRegistry.HoldsBot(p.Other))
                {
                    Pairs.RemoveAt(i);
                    if (Settings.LogRestraint.Value) MercyPlugin.Log.LogInfo($"Truce: {Restraint.Name(p.Holder)} / {Restraint.Name(p.Other)} ends: one of them is held again (that surrender's own decisions apply)");
                    continue;
                }
                if (now < p.Until) continue;
                Pairs.RemoveAt(i);
                BotOwner o = p.Holder.AIData?.BotOwner;
                EnemyInfo info = o != null ? Perception.InfoAbout(o, p.Other) : null;
                bool sees = info != null && info.IsVisible;
                bool cleared = false;
                if (!sees)
                    try { if (o?.Memory?.GoalEnemy != null && Perception.Same(o.Memory.GoalEnemy.Person, p.Other)) { o.Memory.GoalEnemy = null; cleared = true; } } catch { }
                if (Settings.LogRestraint.Value || Settings.LogEncounter.Value)
                    MercyPlugin.Log.LogInfo($"Truce: {Restraint.Name(p.Holder)}'s truce with {Restraint.Name(p.Other)} ends after {now - p.Since:0} s " +
                        $"({(sees ? $"he sees him, {Vector3.Distance(p.Holder.Position, p.Other.Position):0} m: judged as anyone else from here" : "not in sight")}){(cleared ? "; a stale target on him cleared" : "")}");
            }
            Prune();
        }

        // Hooks only on men still in a pair; Active follows the list.
        private static void Prune()
        {
            Active = Pairs.Count > 0;
            if (Hooks.Count == 0) return;
            Gone.Clear();
            foreach (Player p in Hooks.Keys)
            {
                bool used = false;
                for (int i = 0; i < Pairs.Count && !used; i++) used = Pairs[i].Holder == p || Pairs[i].Other == p;
                if (!used) Gone.Add(p);
            }
            for (int i = 0; i < Gone.Count; i++) Unhook(Gone[i]);
            Gone.Clear();
        }

        internal static void LogBlocked(Player shooter, Player other, string where)
        {
            Blocked++;
            if (Time.realtimeSinceStartup - _logAt < 2f) return;
            _logAt = Time.realtimeSinceStartup;
            MercyPlugin.Log.LogInfo($"Truce: {Restraint.Name(shooter)}'s fire at {Restraint.Name(other)} held back at {where} (ignored surrender, truce)");
        }

        // Raid end, disable, teardown.
        internal static void Clear()
        {
            Gone.Clear();
            foreach (Player p in Hooks.Keys) Gone.Add(p);
            for (int i = 0; i < Gone.Count; i++) Unhook(Gone[i]);
            Gone.Clear();
            Hooks.Clear();
            Pairs.Clear();
            Active = false;
            Blocked = 0;
        }
    }
}
