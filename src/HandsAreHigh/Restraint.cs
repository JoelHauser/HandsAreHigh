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
                if ((mp.Position - at).sqrMagnitude > r * r) continue;
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
                Set(s, m, RestraintKind.Reject, $"goes against {captorName}'s {decision}: he saw {s.Name} attack before", squad: true);
                return;
            }
            float roll = Dice.Value;
            if (roll < Settings.SquadRejectChance.Value * (2f - Mercy(mp, h)))
            {
                Set(s, m, RestraintKind.Reject, $"goes against {captorName}'s {decision} (personality {Personality(mp, h)}, roll {roll:0.000})", squad: true);
                return;
            }
            bool near = (mp.Position - s.Bot.Position).sqrMagnitude < 25f * 25f;
            float watch = custody && near ? Settings.WatchSeconds.Value * Dice.Range(0.6f, 1.2f) : 0f;
            Set(s, m, holds, $"{captorName}'s {decision}{(watch > 0f ? ", watches him" : "")}", squad: true, watch: watch);
        }

        // ---- the prisoner's conduct ---------------------------------------------------------

        // He went for a weapon (draws, scrambles, walks to a gun, picks one up): nobody holds
        // fire at him any more; whoever saw it remembers it for this raid.
        internal static void OnArmed(SurrenderState s, string what)
        {
            if (s?.Bot == null || !ByPrisoner.TryGetValue(s.Bot, out var map) || map.Count == 0) return;
            EntryScratch.Clear();
            foreach (RestraintEntry e in map.Values) EntryScratch.Add(e);
            Vector3 eye = Perception.Eye(s.Bot);
            float r = Settings.ObserverRadius.Value;
            for (int i = 0; i < EntryScratch.Count; i++)
            {
                RestraintEntry e = EntryScratch[i];
                if (!Perception.Alive(e.Observer)) continue;
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
            s.Phase == Phase.HandsUp || s.Phase == Phase.Retreating || s.Phase == Phase.Regrouping || s.Phase == Phase.Hiding;

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
                    if (ep != null && (HoldsFire(self, ep) || SurrenderRegistry.HoldsBot(ep) || ScenarioShield.Blocks(self, ep))) continue;
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
            ShotAt.Clear();
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
        internal static void NoteShot(Player shooter) { if (ShotAt.Count > 256) ShotAt.Clear(); ShotAt[shooter] = Time.time; }
        internal static float LastShot(Player shooter) => shooter != null && ShotAt.TryGetValue(shooter, out float t) ? t : -1f;

        // A restrained bot pressing the trigger with his aim on a prisoner he holds fire at.
        internal static bool AimedAtRestrained(Player shooter, out Player prisoner)
        {
            prisoner = null;
            if (!HeldFireBy.TryGetValue(shooter, out List<Player> list)) return false;
            Vector3 eye = Perception.Eye(shooter);
            Vector3 look = shooter.LookDirection;
            for (int i = 0; i < list.Count; i++)
            {
                Player p = list[i];
                if (!Perception.Alive(p)) continue;
                Vector3 to = Perception.Chest(p) - eye;
                if (to.sqrMagnitude > 150f * 150f) continue;
                // Any part of him, not only the chest: a flat 6 deg round the chest let aimed
                // shots at the head, arms or legs through at close range (0.13.1 log: hits
                // with no blocked press before them).
                float cone = Mathf.Max(6f, Mathf.Atan2(1.1f, Mathf.Max(to.magnitude, 0.5f)) * Mathf.Rad2Deg);
                if (Vector3.Angle(look, to) > cone) continue;
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
            if ((!Restraint.Any && !ScenarioShield.Active) || value == null) return true;
            try
            {
                Player self = ____owner?.GetPlayer;
                return !Restraint.HoldsFire(self, value.Person) && !ScenarioShield.Blocks(self, value.Person);
            }
            catch { return true; }
        }
    }

    // ShootData.Shoot: the vanilla AI shot. No shot at a prisoner he holds fire at.
    [HarmonyPatch(typeof(ShootData), nameof(ShootData.Shoot))]
    internal static class ShootDataPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BotOwner ____owner, ref bool __result)
        {
            if (!Restraint.Any && !ScenarioShield.Active) return true;
            try
            {
                EnemyInfo goal = ____owner?.Memory?.GoalEnemy;
                if (goal == null) return true;
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
        }
    }

    // FirearmController.SetTriggerPressed: last line for any AI fire path.
    [HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.SetTriggerPressed))]
    internal static class TriggerPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player.FirearmController __instance, ref bool pressed)
        {
            if (!pressed || (!Restraint.Any && !ScenarioShield.Active && !SurrenderRegistry.Any)) return;
            try
            {
                Player shooter = __instance._player;
                if (shooter == null || !shooter.IsAI) return;
                // While anyone is surrendered, every AI press is timed (one dictionary write per
                // press) so a hit right after a decision can be told from a new attack.
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
        }
    }
}
