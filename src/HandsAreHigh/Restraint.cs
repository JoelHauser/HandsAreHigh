using System.Collections.Generic;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // How each bot treats one surrendered man: a decision per (observer, prisoner) pair, kept
    // for a while, never a global ceasefire and never damage immunity.
    //
    //   Respect  holds fire at him (he's compliant, or his squad took/spared him)
    //   Ignore   holds fire at him because something else matters more (another threat)
    //   LetGo    holds fire while he runs off unarmed
    //   Reject   fights him as before (logged with the reason)
    //
    // How "holds fire" is made to hold (Assembly-CSharp, verified against C:\HUH 2026-11-19):
    //   1. The game's own per-target ignore flag, EnemyInfo.SetIgnoreState(true) (used by the
    //      Christmas-tree event: KhorovodBotGameEvent / ChristmasTreePoI). BotEnemyChooser
    //      .BetterEnemy skips such an enemy when picking the bot's target. Nothing in the game
    //      clears it by itself, so we set it only where it was clear and clear only what we set.
    //   2. A prefix on the BotMemory.GoalEnemy setter: the single place every vanilla path
    //      assigns a target (the chooser, BotMemory.AddEnemy's close-range shortcut, hit
    //      reactions). A restrained bot's target can't become the prisoner. On the decision the
    //      current target is cleared (setter with null) and ShootData.EndShoot() lifts the
    //      finger: a pending burst stops; bullets already fired are not touched.
    //   3. A prefix on ShootData.Shoot (the vanilla AI shot): no shot while the target is a
    //      prisoner this bot restrains himself from.
    //   4. Last line, for any AI fire path (SAIN included, whose code isn't available here):
    //      a prefix on Player.FirearmController.SetTriggerPressed (reached through the
    //      IFirearmHandsController interface, so not inlined): a restrained bot pressing the
    //      trigger while his aim is on that prisoner doesn't press it. That is a failure of
    //      1-3 and is logged as RESTRAINT LEAK (blocked), and a hit by a restrained bot that
    //      still lands is logged as RESTRAINT LEAK (hit). An intentional Reject is logged as
    //      "refuses", never as a leak.
    internal enum RestraintKind { Respect, Ignore, LetGo, Reject }

    internal sealed class RestraintEntry
    {
        internal Player Observer;
        internal BotOwner Owner;
        internal RestraintKind Kind;
        internal float DecidedAt;
        internal float Until;
        internal string Why;
        internal bool Squad;            // decided with his squad (captor's group)
        internal int Situation;         // what the prisoner was doing: 0 hands up, 1 running unarmed
        internal float WatchUntil;      // squadmate: stands and watches him until then (when idle)
        internal EnemyInfo Flagged;     // the EnemyInfo whose ignore flag we set (cleared on release)
        internal bool Final;            // 0.16.0: a deliberate lethal decision (Judgement): kept, never re-rolled
        internal bool Rejudged;         // 0.24.0: after a release, his one renewed decision on seeing him (Rejudge)
        internal bool Holds => Kind != RestraintKind.Reject;
    }

    internal static class Restraint
    {
        // prisoner -> observer -> decision; observer -> prisoners he holds fire at.
        private static readonly Dictionary<Player, Dictionary<Player, RestraintEntry>> ByPrisoner =
            new Dictionary<Player, Dictionary<Player, RestraintEntry>>();
        private static readonly Dictionary<Player, List<Player>> HeldFireBy = new Dictionary<Player, List<Player>>();
        private static readonly List<Player> Scratch = new List<Player>(16);
        private static readonly List<RestraintEntry> EntryScratch = new List<RestraintEntry>(16);

        internal static bool Any;          // anyone holding fire at anyone (patches read only this when idle)
        internal static int EntryCount;

        // ---- queries (patches) ---------------------------------------------------------

        // 0.17.0: a bot of this group decided to kill him (Judgement.Escalate): no one of that squad
        // takes custody of him afterwards (which would ask the killer to hold fire again).
        internal static bool SquadDecidedToKill(BotOwner o, Player prisoner)
        {
            if (o?.BotsGroup == null || prisoner == null || !ByPrisoner.TryGetValue(prisoner, out var map)) return false;
            foreach (RestraintEntry e in map.Values)
                if (e.Final && e.Owner != null && (e.Owner == o || e.Owner.BotsGroup == o.BotsGroup)) return true;
            return false;
        }

        internal static bool HoldsFire(Player observer, IPlayer prisoner)
        {
            if (!Any || observer == null || prisoner == null) return false;
            if (!HeldFireBy.TryGetValue(observer, out List<Player> list)) return false;
            for (int i = 0; i < list.Count; i++)
                if (Perception.Same(prisoner, list[i])) return true;
            return false;
        }

        // By profile id (SAIN's own Enemy objects carry the id, not our Player).
        internal static bool HoldsFireAtId(Player observer, string profileId)
        {
            if (!Any || observer == null || profileId == null) return false;
            if (!HeldFireBy.TryGetValue(observer, out List<Player> list)) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ProfileId == profileId) return true;
            return false;
        }

        internal static bool Restrains(Player observer) => Any && observer != null && HeldFireBy.ContainsKey(observer);

        internal static bool TryGet(Player prisoner, Player observer, out RestraintEntry e)
        {
            e = null;
            return prisoner != null && observer != null && ByPrisoner.TryGetValue(prisoner, out var map) && map.TryGetValue(observer, out e);
        }

        internal static Dictionary<Player, RestraintEntry> EntriesFor(Player prisoner) =>
            prisoner != null && ByPrisoner.TryGetValue(prisoner, out var map) ? map : null;

        // ---- decisions -----------------------------------------------------------------

        // Set (or replace) one observer's decision and make it hold at once (flag, target, finger).
        internal static RestraintEntry Set(SurrenderState s, BotOwner owner, RestraintKind kind, string why, bool squad, float watch = 0f)
        {
            Player observer = owner?.GetPlayer;
            if (s?.Bot == null || observer == null) return null;
            if (!ByPrisoner.TryGetValue(s.Bot, out var map))
            {
                map = new Dictionary<Player, RestraintEntry>();
                ByPrisoner[s.Bot] = map;
            }
            if (!map.TryGetValue(observer, out RestraintEntry e))
            {
                e = new RestraintEntry { Observer = observer, Owner = owner };
                map[observer] = e;
                EntryCount++;
            }
            else if (e.Final && kind != RestraintKind.Reject)
            {
                // 0.17.0: his own decision to kill this man stands. Before, a squadmate taking custody
                // afterwards (InformSquad -> SquadDecision) set him back to holding fire: the
                // ceasefire came back on its own and the decision was never carried out.
                if (Settings.LogRestraint.Value)
                    HandsAreHighPlugin.Log.LogInfo($"Restraint: {Name(observer)} keeps his decision to kill {s.Name} (asked to {Verb(kind)} him: {why})");
                return e;
            }
            bool was = e.Holds && e.DecidedAt > 0f;
            e.Kind = kind;
            e.Why = why;
            e.Squad = squad;
            e.DecidedAt = Time.time;
            e.Until = Time.time + Settings.RestraintMemory.Value;
            e.Situation = SituationOf(s);
            e.WatchUntil = watch > 0f ? Time.time + watch : 0f;
            if (e.Holds) Apply(s, e);
            else if (was || e.Flagged != null) Lift(s, e);
            if (Settings.LogRestraint.Value)
                HandsAreHighPlugin.Log.LogInfo($"Restraint: {Name(observer)} {Verb(kind)} {s.Name}{(squad ? " (squad)" : "")}: {why}");
            return e;
        }

        private static string Verb(RestraintKind k)
        {
            switch (k)
            {
                case RestraintKind.Respect: return "holds fire at";
                case RestraintKind.Ignore: return "ignores";
                case RestraintKind.LetGo: return "lets go";
                default: return "refuses the surrender of";
            }
        }

        private static void Apply(SurrenderState s, RestraintEntry e)
        {
            BotOwner o = e.Owner;
            if (!HeldFireBy.TryGetValue(e.Observer, out List<Player> list))
            {
                list = new List<Player>(2);
                HeldFireBy[e.Observer] = list;
            }
            if (!list.Contains(s.Bot)) list.Add(s.Bot);
            Any = true;
            Flag(s, e);
            try
            {
                EnemyInfo goal = o?.Memory?.GoalEnemy;
                if (goal != null && Perception.Same(goal.Person, s.Bot))
                {
                    o.ShootData?.EndShoot();
                    o.Memory.GoalEnemy = null;   // the chooser picks someone else (or nobody)
                }
            }
            catch (System.Exception ex) { HandsAreHighPlugin.Report("restraint apply", ex); }
        }

        // The game's ignore flag on his EnemyInfo for the prisoner (if he has one yet; the scan
        // re-applies it when one appears later). Only set where it was clear.
        private static void Flag(SurrenderState s, RestraintEntry e)
        {
            EnemyInfo info = Perception.InfoAbout(e.Owner, s.Bot);
            if (info == null) return;
            if (e.Flagged == info) { if (!info.IgnoreUntilAggression) info.SetIgnoreState(true); return; }
            if (info.IgnoreUntilAggression) return;   // someone else's (a game event): leave it alone
            info.SetIgnoreState(true);
            e.Flagged = info;
        }

        private static void Lift(SurrenderState s, RestraintEntry e)
        {
            if (e.Flagged != null)
            {
                try { e.Flagged.SetIgnoreState(false); } catch { }
                e.Flagged = null;
            }
            if (HeldFireBy.TryGetValue(e.Observer, out List<Player> list))
            {
                list.Remove(s.Bot);
                if (list.Count == 0) HeldFireBy.Remove(e.Observer);
            }
            Any = HeldFireBy.Count > 0;
        }

        internal static int SituationOf(SurrenderState s) => s.Phase == Phase.HandsUp ? 0 : 1;

        // The captor's squad (same BotsGroup, verified group membership) within the notice
        // radius follows his decision about this prisoner, at once (same frame as his
        // decision, before their next attack update). A squadmate may refuse: rarely, and
        // only for a reason (he saw this man attack, or the configured personality chance).
        internal static void InformSquad(SurrenderState s, BotOwner captor, string decision, bool custody)
        {
            if (!Settings.SquadRestraint.Value || captor?.BotsGroup == null) return;
            BotsGroup g = captor.BotsGroup;
            float r = Settings.SquadNoticeRadius.Value;
            Vector3 at = captor.GetPlayer.Position;
            for (int i = 0; i < g.MembersCount; i++)
            {
                BotOwner m = g.Member(i);
                if (m == null || m == captor || m.IsDead || m.BotState != EBotState.Active) continue;
                Player mp = m.GetPlayer;
                if (mp == null || mp == s.Bot || !Perception.Alive(mp) || SurrenderRegistry.HoldsBot(mp)) continue;
                // 0.23.0: custody is the whole squad's business (they talk): a squadmate outside the
                // notice radius used to get no decision at all, so his own AI (and SAIN's, which
                // shares the group's enemies) treated the man as any enemy once he came into view:
                // the "squadmate suddenly kills the prisoner". A spare decision (no custody) still
                // reaches only those near enough to see it.
                if (!custody && (mp.Position - at).sqrMagnitude > r * r) continue;
                SquadDecision(s, m, captor, decision, custody);
            }
        }

        private static void SquadDecision(SurrenderState s, BotOwner m, BotOwner captor, string decision, bool custody)
        {
            Player mp = m.GetPlayer;
            BotHistory h = SurrenderRegistry.HistoryOf(mp);
            string captorName = Name(captor.GetPlayer);
            RestraintKind holds = SituationOf(s) == 0 ? RestraintKind.Respect : RestraintKind.LetGo;
            if (m == captor)
            {
                Set(s, m, holds, $"his own {decision}", squad: true);
                return;
            }
            if (Distrusts(h, s.Bot))
            {
                SquadKill(s, m, $"goes against {captorName}'s {decision}: he saw {s.Name} attack before");
                return;
            }
            // 0.23.0: his disposition about this man is rolled ONCE. 0.22 rolled it again at every
            // re-decision (the man ran and came back, a hand-over, the scan renewing an expired squad
            // entry), so a squadmate who had held fire for a minute could "decide" to shoot a
            // compliant prisoner out of the blue. A decision that holds now carries over to the new
            // situation (Respect / LetGo) without a roll.
            if (TryGet(s.Bot, mp, out RestraintEntry had) && had.DecidedAt > 0f && had.Holds && had.Squad)
            {
                Set(s, m, holds, $"{captorName}'s {decision} (as he decided before)", squad: true, watch: had.WatchUntil > Time.time ? had.WatchUntil - Time.time : 0f);
                return;
            }
            float roll = Dice.Value;
            float rejectChance = Settings.SquadRejectChance.Value * (2f - Mercy(mp, h));
            if (roll < rejectChance)
            {
                SquadKill(s, m, $"goes against {captorName}'s {decision} (personality {Personality(mp, h)}, chance {rejectChance:0.000}, roll {roll:0.000})");
                return;
            }
            bool near = (mp.Position - s.Bot.Position).sqrMagnitude < 25f * 25f;
            float watch = custody && near ? Settings.WatchSeconds.Value * Dice.Range(0.6f, 1.2f) : 0f;
            Set(s, m, holds, $"{captorName}'s {decision}{(watch > 0f ? ", watches him" : "")}", squad: true, watch: watch);
        }

        // 0.23.0: a squadmate's refusal is a deliberate decision to kill the man, and is logged as
        // one (once per squadmate and man; the squad's other decisions stand; his own combat AI does
        // the rest with its own targeting).
        private static void SquadKill(SurrenderState s, BotOwner m, string why)
        {
            RestraintEntry e = Set(s, m, RestraintKind.Reject, why, squad: true);
            if (e == null || e.Final) return;
            HandsAreHighPlugin.Log.LogInfo($"{Name(m.GetPlayer)}: DECIDES TO KILL {s.Name} against his squad ({why}){(s.Phase == Phase.HandsUp ? "; the man has his hands up" : "")}");
            e.Final = true;   // decided: never re-rolled into a ceasefire by a later squad decision
        }

        // Debug scenario: a squadmate's deliberate decision to kill the man, FORCED (the decision only;
        // his own combat AI does the rest, watched like any kill decision's outcome by the hit log).
        internal static string ForceSquadKill(SurrenderState s, BotOwner m)
        {
            if (s?.Bot == null || m == null || m.IsDead) return "no such squadmate";
            SquadKill(s, m, "FORCED by a debug scenario (the decision only; his own AI targets and fires)");
            return null;
        }

        // ---- the prisoner's conduct ---------------------------------------------------------

        // He went for a weapon (draws, scrambles, walks to a gun, picks one up): nobody holds
        // fire at him any more; whoever saw it remembers it for this raid.
        internal static void OnArmed(SurrenderState s, string what)
        {
            if (s?.Bot == null) return;
            s.ArmedShownAt = Time.time;
            if (!ByPrisoner.TryGetValue(s.Bot, out var map) || map.Count == 0) return;
            EntryScratch.Clear();
            foreach (RestraintEntry e in map.Values) EntryScratch.Add(e);
            Vector3 eye = Perception.Eye(s.Bot);
            float r = Settings.ObserverRadius.Value;
            // 0.28.0: a man calmly taking his gun back after the bot he gave up to ignored him: that
            // bot and his squad already decided he isn't their business (Truce at the hand-back).
            // Anyone else who sees it reacts as before.
            BotsGroup ignorers = s.Ignored && s.CalmRearm ? s.IgnoredBy.AIData?.BotOwner?.BotsGroup : null;
            for (int i = 0; i < EntryScratch.Count; i++)
            {
                RestraintEntry e = EntryScratch[i];
                if (!Perception.Alive(e.Observer)) continue;
                if (s.Ignored && s.CalmRearm && !e.Final && (e.Observer == s.IgnoredBy || (ignorers != null && e.Owner?.BotsGroup == ignorers))) continue;
                // 0.20.0: a squadmate covering this man reacts by his own sight and reaction time
                // (Overwatch.Watch); one who never sees it takes his squad's word a moment later.
                if (Overwatch.ReactsHimself(s, e.Observer)) continue;
                bool saw = (e.Observer.Position - s.Bot.Position).sqrMagnitude < r * r && Perception.LineClear(Perception.Eye(e.Observer), eye);
                if (saw) Distrust(SurrenderRegistry.HistoryOf(e.Observer), s.Bot);
                if (e.Holds) Set(s, e.Owner, RestraintKind.Reject, $"{s.Name} {what}{(saw ? "" : " (his squad's word)")}", e.Squad);
            }
            EntryScratch.Clear();
        }

        // He runs (unarmed) or comes back to hands up: a meaningful change, so the bots who had
        // decided about him decide again, once, for the new situation.
        internal static void OnSituationChanged(SurrenderState s)
        {
            if (s?.Bot == null || !ByPrisoner.TryGetValue(s.Bot, out var map)) return;
            int now = SituationOf(s);
            foreach (RestraintEntry e in map.Values)
                if (e.Situation != now && !e.Final) e.Until = 0f;   // the scan re-decides (next pass)
        }

        // Every hit on a held bot: who fired, and was that a decision or a leak?
        internal static void OnPrisonerHit(SurrenderState s, EFT.Ballistics.DamageInfo info)
        {
            if (!Any && EntryCount == 0) return;
            Player shooter = null;
            try { shooter = Perception.PlayerOf(info.Player?.iPlayer); } catch { }
            if (shooter == null || shooter == s.Bot) return;
            if (!TryGet(s.Bot, shooter, out _) && shooter.IsAI)
            {
                // 0.23.0: hit by a bot that never decided about him. Say why (no decision is the
                // missing-awareness case), and if he is of the custody squad, the squad rule applies
                // from this moment (he was never informed: a bug, not a decision).
                BotOwner so = shooter.AIData?.BotOwner;
                bool squad = s.Custody != null && so != null && so.BotsGroup != null && so.BotsGroup == s.Custody.Group;
                EnemyInfo seenInfo = so != null ? Perception.InfoAbout(so, s.Bot) : null;
                Player goal = Perception.PlayerOf(so?.Memory?.GoalEnemy?.Person);
                string layer = "?";
                try { if (so != null) layer = DrakiaXYZ.BigBrain.Brains.BrainManager.GetActiveLayerName(so) ?? "?"; } catch { }
                string key = shooter.ProfileId + "|" + s.Bot.ProfileId;
                if (HitNoted.Count > 64) HitNoted.Clear();
                if (HitNoted.Add(key))
                {
                    string line = $"{Name(shooter)} hit {s.Name} ({s.Phase}{(s.Custody != null ? $", in {s.Custody.Name}'s custody" : "")}): " +
                        $"{(squad ? "he is of the captor's squad and was never told" : "not of the captor's squad: an outsider's own fight")}; his target {(goal != null ? Name(goal) : "none")}, " +
                        $"his EnemyInfo on him {(seenInfo == null ? "none" : seenInfo.IsVisible ? "visible" : "not visible")}, layer {layer}, {Vector3.Distance(shooter.Position, s.Bot.Position):0} m";
                    if (squad) HandsAreHighPlugin.Log.LogWarning("PRISONER HIT WITHOUT A DECISION: " + line);
                    else if (Settings.LogRestraint.Value) HandsAreHighPlugin.Log.LogInfo("Restraint: no decision about him: " + line);
                }
                if (squad) SquadDecision(s, so, s.Custody.Owner, "prisoner", custody: true);
                return;
            }
            if (TryGet(s.Bot, shooter, out RestraintEntry e))
            {
                // A round fired before he decided (already in the air, or the end of a burst the
                // decision's EndShoot cut) is not a new attack: his last trigger press that went
                // through (TriggerPatch) was before the decision.
                float lastShot = LastShot(shooter);
                if (e.Holds && lastShot >= 0f && lastShot <= e.DecidedAt + 0.05f && Time.time - e.DecidedAt < 1.5f)
                    HandsAreHighPlugin.Log.LogInfo($"Restraint: {Name(shooter)}'s round, fired {e.DecidedAt - lastShot:0.00} s before his decision, hit {s.Name} (in flight, not a new attack)");
                else if (e.Holds)
                    HandsAreHighPlugin.Log.LogWarning($"RESTRAINT LEAK (hit): {Name(shooter)} hit {s.Name} while his decision was '{Verb(e.Kind)}' ({e.Why}); " +
                        $"his last trigger press {(lastShot < 0f ? "unknown" : $"{Time.time - lastShot:0.00} s ago")}, decided {Time.time - e.DecidedAt:0.0} s ago");
                else if (Settings.LogRestraint.Value)
                    HandsAreHighPlugin.Log.LogInfo($"Restraint: {Name(shooter)} shot {s.Name} as decided (refused: {e.Why})");
            }
        }

        // ---- the scan: observers who can see a surrendered man ------------------------------
        //
        // One prisoner per frame, each at most every 0.5 s; per prisoner, the living active AI
        // within ObserverRadius are looked at with cheap tests first (distance, an EnemyInfo
        // for him: the game's own awareness, visible now). No bot-vs-bot scan.
        private static int _cursor;
        private static readonly List<SurrenderState> Prisoners = new List<SurrenderState>(8);

        internal static void Tick()
        {
            if (Detached.Count > 0) TickDetached();
            if (Truce.Active) Truce.Tick();   // 0.28.0
            if (!SurrenderRegistry.Any) return;
            GameWorld world = Singleton<GameWorld>.Instance;
            List<Player> all = world?.AllAlivePlayersList;
            if (all == null) return;

            Prisoners.Clear();
            foreach (SurrenderState st in SurrenderRegistry.All)
                if (st.Bot != null && !st.Dead && st.Phase != Phase.Rearmed) Prisoners.Add(st);
            if (Prisoners.Count == 0) return;
            if (_cursor >= Prisoners.Count) _cursor = 0;
            SurrenderState s = Prisoners[_cursor++];
            Prisoners.Clear();
            float now = Time.time;
            if (now < s.NextObserverScan) return;
            s.NextObserverScan = now + 0.5f;
            if (s.Suspended) return;
            if (!Restrainable(s)) return;

            float r = Settings.ObserverRadius.Value;
            Vector3 at = s.Bot.Position;
            for (int i = 0; i < all.Count; i++)
            {
                Player p = all[i];
                if (p == null || !p.IsAI || p == s.Bot) continue;
                if ((p.Position - at).sqrMagnitude > r * r) continue;
                BotOwner o = p.AIData?.BotOwner;
                if (o == null || o.IsDead || o.BotState != EBotState.Active || SurrenderRegistry.HoldsBot(p)) continue;
                EnemyInfo info = Perception.InfoAbout(o, s.Bot);
                // 0.23.0: his squad holds this man: he knows it whether or not he sees him (and gets
                // the decision before an EnemyInfo on him even exists: SAIN shares the group's enemies).
                if (s.Custody != null && o.BotsGroup != null && o.BotsGroup == s.Custody.Group && !TryGet(s.Bot, p, out _))
                {
                    SquadDecision(s, o, s.Custody.Owner, "prisoner", custody: true);
                    continue;
                }
                if (info == null) continue;

                if (TryGet(s.Bot, p, out RestraintEntry e) && e.Final) { e.Until = now + Settings.RestraintMemory.Value; continue; }
                if (e != null && now < e.Until)
                {
                    if (e.Holds) Flag(s, e);   // an EnemyInfo made after the decision gets the flag too
                    continue;
                }
                // 0.14.0: a decision that merely ran out of time, for the same situation, is
                // renewed as it was, not rolled again. Re-rolling every RestraintMemory seconds
                // turned a held man's survival into a series of rolls, one of which eventually
                // came up Reject. Only a real change (he runs, comes back, goes for a gun) makes
                // anyone decide again (OnSituationChanged / OnArmed). Ignore was a judgement about
                // another threat, so it is looked at again.
                if (e != null && e.DecidedAt > 0f && e.Situation == SituationOf(s) && e.Kind != RestraintKind.Ignore)
                {
                    e.Until = now + Settings.RestraintMemory.Value;
                    if (e.Holds) Apply(s, e);
                    continue;
                }
                if (e != null && e.Squad && now >= e.Until && SquadOf(s, o, out BotOwner lead))
                {
                    SquadDecision(s, o, lead, s.Custody != null ? "prisoner" : "decision about him", s.Custody != null);   // still his squad's call
                    continue;
                }
                // A decision needs perception: he sees him now (the game's own visibility).
                if (!info.IsVisible) continue;
                Decide(s, o, info);
            }
        }

        // The squad whose decision applies to this observer: the bot guarding the prisoner, or
        // the bot he gave up to while that bot still holds fire at him (spared, let go), when
        // the observer is in that bot's group.
        private static bool SquadOf(SurrenderState s, BotOwner o, out BotOwner lead)
        {
            lead = null;
            if (!Settings.SquadRestraint.Value || o?.BotsGroup == null) return false;
            if (s.Custody != null)
            {
                if (s.Custody.Group != o.BotsGroup) return false;
                lead = s.Custody.Owner;
                return lead != null;
            }
            Player c = s.Captor;
            BotOwner co = c != null && c.IsAI ? c.AIData?.BotOwner : null;
            if (co == null || co.BotsGroup != o.BotsGroup) return false;
            if (!TryGet(s.Bot, c, out RestraintEntry ce) || !ce.Holds) return false;
            lead = co;
            return true;
        }

        // Only a man who looks surrendered gets a choice: hands up, or running unarmed.
        private static bool Restrainable(SurrenderState s) =>
            s.Phase == Phase.HandsUp || s.Phase == Phase.Retreating || s.Phase == Phase.Regrouping || s.Phase == Phase.Hiding || s.Phase == Phase.Released;

        private static void Decide(SurrenderState s, BotOwner o, EnemyInfo info)
        {
            Player p = o.GetPlayer;
            BotHistory h = SurrenderRegistry.HistoryOf(p);
            int situation = SituationOf(s);
            string who = s.Name;

            if (Distrusts(h, s.Bot)) { Set(s, o, RestraintKind.Reject, $"saw {who} attack before", false); return; }

            // His squad already decided (a squadmate holds or spared him, or let him go): the squad rule.
            if (SquadOf(s, o, out BotOwner lead)) { SquadDecision(s, o, lead, s.Custody != null ? "prisoner" : "decision about him", s.Custody != null); return; }
            // A human captor's allies (BotsGroup.IsAlly: e.g. a teammate mod's squad) treat your
            // prisoner as their squad's.
            Player captor = s.Captor;
            if (captor != null && !captor.IsAI && Perception.Alive(captor) && o.BotsGroup != null && o.BotsGroup.IsAlly(captor))
            {
                if (Dice.Value < Settings.SquadRejectChance.Value * (2f - Mercy(p, h)))
                    Set(s, o, RestraintKind.Reject, $"won't spare your prisoner {who} (personality {Personality(p, h)})", false);
                else
                    Set(s, o, RestraintKind.Respect, $"your prisoner", true);
                return;
            }
            if (!Settings.ObserverRestraint.Value) return;   // vanilla behaviour: he fights him

            // Busy: another threat in sight or under fire. He leaves the unarmed man be.
            if (OtherThreat(o, s.Bot, out string threat))
            {
                Set(s, o, RestraintKind.Ignore, $"{who} {(situation == 0 ? "has his hands up" : "is running unarmed")}, busy with {threat}", false);
                return;
            }

            float mercy = Mercy(p, h);
            float baseChance = situation == 0 ? Settings.ObserverRespect.Value : Settings.FleeingRespect.Value;
            float chance = Mathf.Clamp(baseChance * mercy, 0f, 0.97f);
            float roll = Dice.Value;
            string why = $"{who} {(situation == 0 ? "has his hands up" : "runs, unarmed")}; mercy x{mercy:0.00} ({Personality(p, h)}), chance {chance:0.00}, roll {roll:0.00}";
            if (roll < chance)
            {
                Set(s, o, situation == 0 ? RestraintKind.Respect : RestraintKind.LetGo, why, false);
                if (situation == 0) Captors.MaybeTakeOver(s, o);
            }
            else Set(s, o, RestraintKind.Reject, why, false);
        }

        // Another enemy he sees now (not a prisoner he's sparing), or fire on him in the last 3 s.
        internal static bool OtherThreat(BotOwner o, Player prisoner, out string what)
        {
            what = null;
            var infos = o.EnemiesController?.EnemyInfos;
            Player self = o.GetPlayer;
            if (infos != null)
            {
                foreach (KeyValuePair<IPlayer, EnemyInfo> kv in infos)
                {
                    EnemyInfo e = kv.Value;
                    if (e == null || !e.IsVisible || Perception.Same(e.Person, prisoner)) continue;
                    if (e.Person?.HealthController == null || !e.Person.HealthController.IsAlive) continue;
                    Player ep = Perception.PlayerOf(e.Person);
                    if (ep != null && (HoldsFire(self, ep) || SurrenderRegistry.HoldsBot(ep) || ScenarioShield.Blocks(self, ep) || Truce.Blocks(self, ep))) continue;
                    what = ep != null ? Name(ep) : "an enemy";
                    return true;
                }
            }
            BotHistory h = SurrenderRegistry.HistoryOf(self);
            if (Time.time - h.FireAt < 3f && Suppression.Decayed(h, Time.time) > 0.3f) { what = "incoming fire"; return true; }
            return false;
        }

        // ---- disposition: who spares, who doesn't ------------------------------------------
        //
        // Not nerve (nerve is how readily he acts, not how cruel he is). SAIN personality where
        // available (aggressive ones spare less; cautious ones avoid escalating), else the role
        // (bosses and followers spare least), times his own disposition rolled once per raid
        // (0.75-1.25). Documented tuning, not game data.
        internal static float Mercy(Player p, BotHistory h)
        {
            if (h.Mercy <= 0f) h.Mercy = Dice.Range(0.75f, 1.25f);
            float m;
            switch (Personality(p, h))
            {
                case "Wreckless": m = 0.55f; break;
                case "GigaChad": m = 0.75f; break;
                case "SnappingTurtle": m = 0.8f; break;
                case "Chad": m = 0.85f; break;
                case "Rat": m = 0.95f; break;
                case "Timmy": m = 1.15f; break;
                case "Coward": m = 1.1f; break;
                case "": case null:
                    if ((int)h.Role < 0) h.Role = NaturalSurrender.ClassOf(p);
                    m = h.Role == NaturalSurrender.RoleClass.Boss ? 0.6f : h.Role == NaturalSurrender.RoleClass.Scav ? 0.9f : 1f;
                    break;
                default: m = 1f; break;
            }
            return m * h.Mercy;
        }

        private static string Personality(Player p, BotHistory h)
        {
            string s = SurrenderRegistry.PersonalityOf(p, h);
            return string.IsNullOrEmpty(s) ? "" : s;
        }

        private static bool Distrusts(BotHistory h, Player prisoner) =>
            h.Distrust != null && prisoner != null && h.Distrust.Contains(prisoner.ProfileId);

        // 0.20.0: he saw this man attack (a covering squadmate's own reaction): remembered for the raid.
        internal static void Remember(Player observer, Player prisoner)
        {
            if (observer != null) Distrust(SurrenderRegistry.HistoryOf(observer), prisoner);
        }

        private static void Distrust(BotHistory h, Player prisoner)
        {
            if (prisoner == null) return;
            if (h.Distrust == null) h.Distrust = new HashSet<string>();
            h.Distrust.Add(prisoner.ProfileId);
        }

        // ---- lifecycle ----------------------------------------------------------------

        // The prisoner left the registry (back to normal AI, dead, despawned): every decision
        // about him is dropped and every flag we set is cleared.
        internal static void ForgetPrisoner(Player prisoner)
        {
            for (int i = Detached.Count - 1; i >= 0; i--) if (Detached[i].Bot == prisoner) Detached.RemoveAt(i);
            if ((object)prisoner == null || !ByPrisoner.TryGetValue(prisoner, out var map)) return;
            foreach (RestraintEntry e in map.Values)
            {
                if (e.Flagged != null) { try { e.Flagged.SetIgnoreState(false); } catch { } e.Flagged = null; }
                if (HeldFireBy.TryGetValue(e.Observer, out List<Player> list))
                {
                    list.Remove(prisoner);
                    if (list.Count == 0) HeldFireBy.Remove(e.Observer);
                }
                EntryCount--;
            }
            ByPrisoner.Remove(prisoner);
            Any = HeldFireBy.Count > 0;
        }

        // ---- after the hand-back (0.22.0) ------------------------------------------------------
        //
        // The 0.21.0 raid, "Incoming fire breaks the hold-up": a near miss broke the hold-up, the
        // captor went to his own AI, the prisoner (no cover within 15 m) was handed back to his own
        // AI 0.1 s later, and the hand-back forgot every decision about him (ForgetPrisoner): the
        // ignore flag came off, the GoalEnemy/Shoot/trigger guards let go, and the nearest known
        // enemy of the captor's combat AI was the unarmed man 2 m in front of him. He shot him 1.2 s
        // later. Being an enemy before, and being near, is not a reason to shoot him now.
        // So a man handed back UNARMED keeps the hold-fire decisions about him (Respect / Ignore /
        // LetGo; a Reject or a decision to kill is dropped as before) for "Unarmed ex-prisoner spared
        // for", while his hands stay empty. A weapon in his hands (a gun, a knife, a grenade) ends
        // them at once, and whoever saw it remembers it (Distrust, as OnArmed). The captor's AI is
        // left to pick its own target with its own perception: nothing here points it anywhere.
        private sealed class Detachment
        {
            internal Player Bot;
            internal string Name;
            internal float Until, Next;
            internal float Since, Hard;   // 0.24.0: when he was handed back; the memory's end
        }

        private static readonly List<Detachment> Detached = new List<Detachment>(4);

        internal static void Detach(SurrenderState s, string why)
        {
            Player p = s?.Bot;
            if ((object)p == null) return;
            float secs = Settings.ExPrisonerHoldFire.Value;
            if (secs <= 0f || !ByPrisoner.TryGetValue(p, out var map) || HoldsWeapon(p, out _)) { ForgetPrisoner(p); return; }
            // Only decisions to hold fire carry over.
            EntryScratch.Clear();
            foreach (RestraintEntry e in map.Values)
                if (!e.Holds || e.Final || !Perception.Alive(e.Observer)) EntryScratch.Add(e);
            for (int i = 0; i < EntryScratch.Count; i++)
            {
                RestraintEntry e = EntryScratch[i];
                if (e.Flagged != null) { try { e.Flagged.SetIgnoreState(false); } catch { } e.Flagged = null; }
                if (HeldFireBy.TryGetValue(e.Observer, out List<Player> list)) { list.Remove(p); if (list.Count == 0) HeldFireBy.Remove(e.Observer); }
                map.Remove(e.Observer);
                EntryCount--;
            }
            EntryScratch.Clear();
            Any = HeldFireBy.Count > 0;
            if (map.Count == 0) { ByPrisoner.Remove(p); return; }
            foreach (RestraintEntry e in map.Values) e.Rejudged = false;   // 0.24.0: one renewed decision per release
            float now = Time.time;
            for (int i = Detached.Count - 1; i >= 0; i--) if (Detached[i].Bot == p) Detached.RemoveAt(i);
            Detached.Add(new Detachment { Bot = p, Name = s.Name, Until = now + secs, Next = now + 0.25f, Since = now, Hard = now + Mathf.Max(secs, Settings.EscapeMemory.Value) });
            if (Settings.LogRestraint.Value || Settings.LogFireBreak.Value)
            {
                var names = new System.Text.StringBuilder();
                foreach (Player o in map.Keys) names.Append(names.Length > 0 ? ", " : "").Append(Name(o));
                HandsAreHighPlugin.Log.LogInfo($"Restraint: {s.Name} handed back with empty hands ({why}): {names} keep holding fire at him for up to {secs:0} s, unless he takes a weapon in his hands");
            }
        }

        private static void TickDetached()
        {
            float now = Time.time;
            for (int i = Detached.Count - 1; i >= 0; i--)
            {
                Detachment d = Detached[i];
                if (now < d.Next) continue;
                d.Next = now + 0.25f;
                Player p = d.Bot;
                if (!Perception.Alive(p)) { Detached.RemoveAt(i); ForgetPrisoner(p); continue; }
                // Ours again (gave up, caught): the registry's decisions take over from these.
                if (SurrenderRegistry.HoldsBot(p)) { Detached.RemoveAt(i); continue; }
                if (HoldsWeapon(p, out string what))
                {
                    Detached.RemoveAt(i);
                    string saw = Witnesses(p);
                    string driver = Driver(p);
                    ForgetPrisoner(p);
                    HandsAreHighPlugin.Log.LogInfo($"Restraint: RENEWED HOSTILITY: {d.Name} takes a {what} in his hands {now - d.Since:0} s after his release ({driver}): nobody holds fire at him any more{(saw.Length > 0 ? $" (seen by {saw})" : "")}");
                    continue;
                }
                if (now >= d.Until && !Rejudge(d, now)) Detached.RemoveAt(i);
            }
        }

        // 0.24.0: the hold-fire window ran out. 0.22/0.23 forgot every decision at once here
        // (ForgetPrisoner): the ignore flag came off and the GoalEnemy / SAIN guards let go for every
        // observer together, so a bot whose combat memory still held the man (seen 30 s ago, out of
        // sight since) could take him straight back as a target: a timer acting as a provocation.
        // Now each observer separately, from his own perception:
        //   sees him now   ONE explicit decision per observer per release (no series of rolls): let
        //                  him go again (the running-unarmed respect chance x the observer's mercy),
        //                  kept until the capture memory ends, or engage (logged as RENEWED HOSTILITY
        //                  with the roll); a weapon in the man's hands is handled above;
        //   doesn't        the hold stays (nothing points him at the man) until he sees him again or
        //                  the capture memory ends.
        // At the memory's end every hold is dropped (no permanent immunity); one on a man he can't
        // see also clears a vanilla target still on him, so nothing reactivates from stale memory.
        // True: someone still holds, keep the detachment; false: it is over.
        private static bool Rejudge(Detachment d, float now)
        {
            Player p = d.Bot;
            if (!ByPrisoner.TryGetValue(p, out var map) || map.Count == 0) { ForgetPrisoner(p); return false; }
            EntryScratch.Clear();
            foreach (RestraintEntry e in map.Values) EntryScratch.Add(e);
            bool hard = now >= d.Hard;
            for (int k = 0; k < EntryScratch.Count; k++)
            {
                RestraintEntry e = EntryScratch[k];
                Player o = e.Observer;
                if (!Perception.Alive(o)) { DropEntry(p, map, e); continue; }
                BotOwner ob = e.Owner ?? o.AIData?.BotOwner;
                EnemyInfo info = ob != null ? Perception.InfoAbout(ob, p) : null;
                bool sees = info != null && info.IsVisible;
                float dist = Vector3.Distance(o.Position, p.Position);
                if (hard)
                {
                    // The memory is over: no permanent immunity. Unseen: nothing to reactivate from.
                    bool cleared = false;
                    if (!sees)
                        try { if (ob?.Memory?.GoalEnemy != null && Perception.Same(ob.Memory.GoalEnemy.Person, p)) { ob.Memory.GoalEnemy = null; cleared = true; } } catch { }
                    DropEntry(p, map, e);
                    if (Settings.LogRestraint.Value || Settings.LogEscape.Value)
                        HandsAreHighPlugin.Log.LogInfo($"Restraint: {Name(o)}'s hold on {d.Name} ends {now - d.Since:0} s after his release ({(sees ? $"he sees him, {dist:0} m: judged as anyone else from here" : "not seen since")}){(cleared ? "; a stale target on him cleared" : "")}");
                    continue;
                }
                if (!sees || e.Rejudged) continue;
                BotHistory h = SurrenderRegistry.HistoryOf(o);
                float mercy = Mercy(o, h);
                float chance = Mathf.Clamp(Settings.FleeingRespect.Value * mercy, 0f, 0.97f);
                float roll = Dice.Value;
                string armed = Arsenal.DescribeArmed(p);
                if (roll < chance)
                {
                    e.Rejudged = true;
                    if (Settings.LogRestraint.Value || Settings.LogEscape.Value)
                        HandsAreHighPlugin.Log.LogInfo($"Restraint: {Name(o)} sees {d.Name} again ({dist:0} m, {armed}) {now - d.Since:0} s after his release: lets him go again (chance {chance:0.00} = respect {Settings.FleeingRespect.Value:0.00} x mercy {mercy:0.00}, roll {roll:0.00})");
                }
                else
                {
                    DropEntry(p, map, e);
                    HandsAreHighPlugin.Log.LogInfo($"Restraint: RENEWED HOSTILITY: {Name(o)} sees {d.Name} again ({dist:0} m, {armed}) {now - d.Since:0} s after his release and decides to engage him (chance to let him go {chance:0.00}, roll {roll:0.00}); his own AI from here");
                }
            }
            EntryScratch.Clear();
            if (map.Count == 0) { ByPrisoner.Remove(p); Any = HeldFireBy.Count > 0; return false; }
            d.Until = now + 1f;   // looked at again each second (cheap: a few EnemyInfo reads)
            return true;
        }

        // 0.24.0, scenarios: an explicit, logged decision by one observer (an ex-captor) to attack a
        // man he holds fire at, held or already handed back. Only his own decision changes: the hold
        // is dropped (or turned to a refusal while the man is held) and, if he has an EnemyInfo on
        // him, the man becomes his vanilla target; SAIN picks its own target from what it perceives.
        internal static string ForceAttack(Player observer, Player man, string why)
        {
            if (observer == null || man == null) return "nobody";
            BotOwner o = observer.AIData?.BotOwner;
            if (o == null) return "not a bot";
            string how;
            if (SurrenderRegistry.TryGet(man, out SurrenderState s))
            {
                RestraintEntry e = Set(s, o, RestraintKind.Reject, $"attacks him: {why}", squad: false);
                if (e != null) e.Final = true;
                how = "held: his hold turned to a refusal";
            }
            else if (ByPrisoner.TryGetValue(man, out var map) && map.TryGetValue(observer, out RestraintEntry e2))
            {
                DropEntry(man, map, e2);
                if (map.Count == 0) { ByPrisoner.Remove(man); for (int i = Detached.Count - 1; i >= 0; i--) if (Detached[i].Bot == man) Detached.RemoveAt(i); }
                how = "handed back: his hold dropped";
            }
            else how = "no hold of his on him";
            bool target = false;
            try
            {
                EnemyInfo info = Perception.InfoAbout(o, man);
                if (info != null && o.Memory != null) { o.Memory.GoalEnemy = info; target = true; }
            }
            catch (System.Exception ex) { HandsAreHighPlugin.Report("force attack", ex); }
            HandsAreHighPlugin.Log.LogInfo($"Restraint: RENEWED HOSTILITY: {Name(observer)} DECIDES TO ATTACK {Name(man)} ({why}; {how}; {(target ? "made his target" : "no EnemyInfo: his AI must find him")})");
            return null;
        }

        // One observer's decision about him gone (flag, guards), the rest kept.
        private static void DropEntry(Player prisoner, Dictionary<Player, RestraintEntry> map, RestraintEntry e)
        {
            if (e.Flagged != null) { try { e.Flagged.SetIgnoreState(false); } catch { } e.Flagged = null; }
            if (HeldFireBy.TryGetValue(e.Observer, out List<Player> list)) { list.Remove(prisoner); if (list.Count == 0) HeldFireBy.Remove(e.Observer); }
            if (map.Remove(e.Observer)) EntryCount--;
            Any = HeldFireBy.Count > 0;
        }

        // For the renewed-hostility line: which layer drives him and whom it targets.
        private static string Driver(Player p)
        {
            BotOwner o = p?.AIData?.BotOwner;
            string layer = "?";
            try { if (o != null) layer = DrakiaXYZ.BigBrain.Brains.BrainManager.GetActiveLayerName(o) ?? "?"; } catch { }
            Player t = Perception.PlayerOf(o?.Memory?.GoalEnemy?.Person);
            return $"his AI: layer {layer}, target {(t != null ? Name(t) : "none")}";
        }

        // Who saw him take it (in range, a clear line): they remember him as a man who went for a
        // weapon after giving up (Distrusts: a later surrender of his is refused by them).
        private static string Witnesses(Player p)
        {
            if (!ByPrisoner.TryGetValue(p, out var map)) return "";
            var sb = new System.Text.StringBuilder();
            float r = Settings.ObserverRadius.Value;
            Vector3 eye = Perception.Eye(p);
            foreach (RestraintEntry e in map.Values)
            {
                if (!Perception.Alive(e.Observer)) continue;
                if ((e.Observer.Position - p.Position).sqrMagnitude > r * r || !Perception.LineClear(Perception.Eye(e.Observer), eye)) continue;
                Distrust(SurrenderRegistry.HistoryOf(e.Observer), p);
                sb.Append(sb.Length > 0 ? ", " : "").Append(Name(e.Observer));
            }
            return sb.ToString();
        }

        // A weapon in his hands (0.16.9: Player.FirearmController, Player.BaseKnifeController,
        // Player.BaseGrenadeHandsController are the hands controllers that can hurt someone).
        internal static bool HoldsWeapon(Player p, out string what)
        {
            what = null;
            object hc = p?.HandsController;
            if (hc is Player.FirearmController) what = "gun";
            else if (hc is Player.BaseKnifeController) what = "knife";
            else if (hc is Player.BaseGrenadeHandsController) what = "grenade";
            return what != null;
        }

        internal static int DetachedCount => Detached.Count;

        // Every 30 s: observers that died or despawned are dropped from every map.
        internal static void PruneObservers()
        {
            if (ByPrisoner.Count == 0) return;
            Scratch.Clear();
            foreach (var map in ByPrisoner.Values)
                foreach (Player o in map.Keys)
                    if (o == null || !Perception.Alive(o)) Scratch.Add(o);
            for (int i = 0; i < Scratch.Count; i++)
            {
                Player o = Scratch[i];
                foreach (var map in ByPrisoner.Values)
                    if (map.Remove(o)) EntryCount--;
                HeldFireBy.Remove(o);
            }
            Scratch.Clear();
            Any = HeldFireBy.Count > 0;
        }

        // Raid end, disable, teardown.
        internal static void Clear()
        {
            foreach (var map in ByPrisoner.Values)
                foreach (RestraintEntry e in map.Values)
                    if (e.Flagged != null) { try { e.Flagged.SetIgnoreState(false); } catch { } e.Flagged = null; }
            ByPrisoner.Clear();
            HeldFireBy.Clear();
            Detached.Clear();
            Truce.Clear();   // 0.28.0
            ShotAt.Clear();
            HitNoted.Clear();
            Any = false;
            EntryCount = 0;
            _cursor = 0;
        }

        // Scenario participants carry their scenario tag (Nickname#S1-Scav) in every log line.
        internal static string Name(Player p) => p == null ? "someone" : Scenarios.Label(p);

        // ---- the trigger guard ----------------------------------------------------------------

        private static float _leakLogAt;

        // When each AI last pressed his trigger and it went through (TriggerPatch, only while
        // anyone holds fire): tells a round fired before a decision from an attack after it.
        private static readonly Dictionary<Player, float> ShotAt = new Dictionary<Player, float>();
        private static readonly HashSet<string> HitNoted = new HashSet<string>();   // 0.23.0: one diagnosis per shooter and man
        internal static void NoteShot(Player shooter) { if (ShotAt.Count > 256) ShotAt.Clear(); ShotAt[shooter] = Time.time; }
        internal static float LastShot(Player shooter) => shooter != null && ShotAt.TryGetValue(shooter, out float t) ? t : -1f;

        // A restrained bot pressing the trigger with his aim on a prisoner he holds fire at.
        internal static bool AimedAtRestrained(Player shooter, out Player prisoner)
        {
            prisoner = null;
            if (!HeldFireBy.TryGetValue(shooter, out List<Player> list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                Player p = list[i];
                // Any part of him (feet to head, his width), not only the chest (0.13.1: aimed shots
                // at the head, arms or legs got through a chest cone). 0.21.0: his body's extent on
                // the line, not a flat 6 deg at any range, and not when the shooter's own target
                // stands in front of him on it (Perception.InLineOfFire): a captor's squad could
                // not fire past a prisoner at a third party behind him.
                if (!Perception.InLineOfFire(shooter, p, 150f)) continue;
                prisoner = p;
                return true;
            }
            return false;
        }

        internal static void LogLeakBlocked(Player shooter, Player prisoner, string where)
        {
            if (Time.realtimeSinceStartup - _leakLogAt < 2f) return;
            _leakLogAt = Time.realtimeSinceStartup;
            string why = TryGet(prisoner, shooter, out RestraintEntry e) ? e.Why : "?";
            HandsAreHighPlugin.Log.LogWarning($"RESTRAINT LEAK (blocked at {where}): {Name(shooter)} tried to fire at {Name(prisoner)} while holding fire ({why})");
        }
    }

    // BotMemory.GoalEnemy setter: a restrained bot's target can't become that prisoner.
    [HarmonyPatch(typeof(BotMemory), nameof(BotMemory.GoalEnemy), MethodType.Setter)]
    internal static class GoalEnemyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(EnemyInfo value, BotOwner ____owner)
        {
            if ((!Restraint.Any && !ScenarioShield.Active && !Truce.Active) || value == null) return true;
            long t0 = Perf.Begin();
            try
            {
                Player self = ____owner?.GetPlayer;
                return !Restraint.HoldsFire(self, value.Person) && !ScenarioShield.Blocks(self, value.Person) && !Truce.Blocks(self, value.Person);
            }
            catch { return true; }
            finally { Perf.End(Perf.Sec.FireGuard, t0); }
        }
    }

    // ShootData.Shoot: the vanilla AI shot. No shot at a prisoner he holds fire at.
    [HarmonyPatch(typeof(ShootData), nameof(ShootData.Shoot))]
    internal static class ShootDataPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BotOwner ____owner, ref bool __result)
        {
            if (!Restraint.Any && !ScenarioShield.Active && !Truce.Active) return true;
            long t0 = Perf.Begin();
            try
            {
                EnemyInfo goal = ____owner?.Memory?.GoalEnemy;
                if (goal == null) return true;
                if (Truce.Blocks(____owner.GetPlayer, goal.Person))
                {
                    Truce.LogBlocked(____owner.GetPlayer, Perception.PlayerOf(goal.Person), "ShootData.Shoot");
                    ____owner.ShootData.EndShoot();
                    __result = false;
                    return false;
                }
                if (ScenarioShield.Blocks(____owner.GetPlayer, goal.Person))
                {
                    ScenarioShield.LogBlocked(____owner.GetPlayer, Perception.PlayerOf(goal.Person), "ShootData.Shoot");
                    ____owner.ShootData.EndShoot();
                    __result = false;
                    return false;
                }
                if (!Restraint.HoldsFire(____owner.GetPlayer, goal.Person)) return true;
                Restraint.LogLeakBlocked(____owner.GetPlayer, Perception.PlayerOf(goal.Person), "ShootData.Shoot");
                ____owner.ShootData.EndShoot();
                __result = false;
                return false;
            }
            catch { return true; }
            finally { Perf.End(Perf.Sec.FireGuard, t0); }
        }
    }

    // FirearmController.SetTriggerPressed: last line for any AI fire path.
    [HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.SetTriggerPressed))]
    internal static class TriggerPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player.FirearmController __instance, ref bool pressed)
        {
            if (!pressed || (!Restraint.Any && !ScenarioShield.Active && !SurrenderRegistry.Any && !Truce.Active)) return;
            long t0 = Perf.Begin();
            try
            {
                Player shooter = __instance._player;
                if (shooter == null || !shooter.IsAI) return;
                // While anyone is surrendered, every AI press is timed (one dictionary write per
                // press) so a hit right after a decision can be told from a new attack.
                if (Truce.Active && Truce.AimedAtPartner(shooter, out Player partner))
                {
                    pressed = false;
                    Truce.LogBlocked(shooter, partner, "trigger");
                    return;
                }
                if (!Restraint.Any && !ScenarioShield.Active) { Restraint.NoteShot(shooter); return; }
                if (ScenarioShield.Active && ScenarioShield.AimedAtShielded(shooter, out Player shielded))
                {
                    pressed = false;
                    ScenarioShield.LogBlocked(shooter, shielded, "trigger");
                    return;
                }
                // 0.18.0: noted here too (a shield active, nobody restrained): 0.17.0 returned
                // without noting, so a kill decision's shots could go unseen by the watch.
                if (!Restraint.Any) { Restraint.NoteShot(shooter); return; }
                if (!Restraint.AimedAtRestrained(shooter, out Player prisoner)) { Restraint.NoteShot(shooter); return; }
                pressed = false;
                Restraint.LogLeakBlocked(shooter, prisoner, "trigger");
            }
            catch { }
            finally { Perf.End(Perf.Sec.FireGuard, t0); }
        }
    }
}
