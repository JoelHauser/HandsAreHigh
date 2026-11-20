using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // A bot that a man surrendered to, and what that bot does about it.
    //
    // Recognition first (0.9.0): a surrender can be made at any range, but the bot only decides
    // about it once he perceives it: the game's own sight of the man (EnemyInfo.IsVisible) and a
    // clear line to him (Perception.Recognises). Until then the surrender is an unanswered offer
    // (Pending, checked every 0.5 s); the prisoner's own offer timer bounds it.
    //
    // The decision, once:
    //   Accept  practical (firearm in hands with rounds, no other enemy in sight, not under
    //           fire, recognises the prisoner, within AcceptRange (custody, not surrender),
    //           under the limits, not himself held, not a player ally) and he wants to
    //           (AcceptChance x mercy, a little more with squadmates near to back him).
    //   Spare   holds fire anyway without custody (SpareChance x mercy). The prisoner's offer
    //           stays unanswered: nobody covers him, and his offer timer ends it.
    //   Reject  keeps fighting him (logged with the reason).
    //
    // Custody is a relationship that can weaken or break (0.9.0). Each captor is assessed every
    // 0.25 s from the game's own threat information (and SAIN's where installed; nothing
    // omniscient):
    //   Contact  another enemy he sees now (EnemyInfo.IsVisible; not a man he spares)
    //   Fire     incoming fire: BotMemory.IsUnderFire (hits, heard fire), our near misses
    //            (Suppression.cs), SAIN's suppression state >= Medium
    //   Alert    an enemy he saw in the last few seconds, out of sight now
    // and is in one of three modes:
    //   Guard    the layer has him: stands, covers the prisoner, gives his plan's orders,
    //            glances now and then (towards an Alert, or around him): real openings.
    //   Cover    fire from a direction he can tell: he moves to a reachable spot hidden from
    //            it within CaptorCoverRadius (preferring one that keeps the prisoner in sight),
    //            a close prisoner ordered to follow (complete path only) and to stay once there.
    //            Contact interrupts it. At most 2 moves per custody, 10 s apart; stuck -> Engage.
    //   Engage   survival and combat first: the layer lets go at once (SAIN / vanilla combat
    //            fight, take cover, shoot). Pending dialogue is dropped. The first time, a free
    //            squadmate who can actually cover the prisoner may take custody over. He stays in
    //            Engage at least MinEngageSeconds and goes back to Guard only after
    //            QuietBeforeGuarding with no contact and no fire; engaged longer than BusyRelease
    //            he gives custody up.
    // Losing custody is never a reason to kill the prisoner: every restraint stays as decided
    // (Restraint.cs); his offer becomes unanswered again, so he reassesses (stays, runs, hides,
    // rearms) from what he can see.
    //
    // The prisoner's side (Decisions.cs) only reads what he can observe: where the captor's gun
    // points, whether he can see him, gunfire, reloads, a running captor. Not the mode.
    //
    // ORBIT: while a custody lasts, both bots are kept out of Ghost sleep (OrbitAdapter), lifted
    // when it ends. Pit Fireteam: player allies never become captors (their movement stays with
    // Pit Fireteam and your orders; its follower layers at 71-76 sit above ours anyway).
    internal enum CaptorMode { Guard, Cover, Engage }

    internal enum ThreatLevel { None, Alert, Fire, Contact }

    internal sealed class CaptorState
    {
        internal Player Bot;
        internal BotOwner Owner;
        internal BotsGroup Group;
        internal string Name;
        internal SurrenderState Prisoner;
        internal float Since;
        internal float HoldUntil;
        internal float NextOrderAt;
        internal float NextCheck;
        internal readonly List<OrderKind> Plan = new List<OrderKind>(4);
        internal int Step;
        internal string PlanName;
        internal float LostSightSince = -1f;
        internal int OrderFailures;

        internal CaptorMode Mode = CaptorMode.Guard;
        internal float ModeSince;
        internal float QuietSince = -1f;
        internal string ModeWhy;
        internal bool HandOverTried;

        // Cover.
        internal Vector3 CoverPoint;
        internal float CoverPendingSince = -1f;
        internal Vector3 CoverFrom;
        internal int CoverMoves;
        internal float NextCoverAt;
        internal Vector3 ProgressFrom;
        internal float ProgressSince;
        internal bool PathIssued;
        internal float NextPathAt;

        // Attention: mostly on the prisoner, glances away (towards an alert or around him).
        internal float NextGlanceAt;
        internal float GlanceUntil;
        internal Vector3 GlancePoint;
        internal Vector3 AlertAt;
        internal float AlertSeenAt = -1000f;

        // Walking up to the prisoner (0.14.0), in Guard mode only.
        internal bool Approaching;
        internal float ApproachSince;
        internal float NextApproachAt;
        internal Vector3 ApproachTarget;
        internal float ApproachKeep;

        // 0.15.0: closing in on a disarmed prisoner (once per custody, a fixed spot, bounded).
        internal bool CloseDecided;         // rolled once
        internal bool Close;                // the current approach is the close-in one
        internal Vector3 CloseSpot;
        internal Vector3 CloseFrom;         // where the prisoner stood when the spot was chosen
        internal int CloseTries;
        // 0.15.0: a kneel order given, followed until he is down or can't (never repeated).
        internal bool AwaitKneel;
        internal float KneelIssuedAt;
        internal float ReadySince = -1f;    // waiting for control before a kneel order
        internal int WaitLoggedStep = -1;   // the plan step a wait (range, control) was logged for
        // 0.15.0: resistance noticed only once it shows (arms coming down, moving, a draw), after
        // his own reaction time.
        internal float ResistSeenAt = -1f;

        // 0.16.0: what he has seen of the prisoner's weapons, and his judgement (Judgement.cs).
        internal readonly HashSet<Item> SeenWeapons = new HashSet<Item>();
        internal float NextWatchAt;
        internal Item SawKept;              // a weapon he noticed still on him after a demand
        internal bool Incomplete;           // he saw him keep something back (weighs the release decision)
        internal int Repeats;               // demands repeated
        internal bool Unsafe;               // let him go armed: keeps his distance, fetches nothing
        internal bool ReleaseJudged;
        // 0.18.0: the intent decided once at custody (Judgement.Intend), carried out at the release.
        internal bool IntendsKill, IntentHurt;
        internal float IntentChance;
        internal string IntentWhy = "not decided";
        internal float DemandSeen = -1f;    // the prisoner's last answered demand he has judged
        internal float StepWaitSince = -1f; // a hands / turn order waiting for control
        // 0.16.0: the prisoner's dropped gear (CaptorLoot.cs).
        internal LootPlan Loot;
        // 0.20.0: the squadmate covering him (Overwatch.cs), or why nobody can.
        internal CoverGuard Guard;
        internal float NextGuardPick;
        internal string AloneWhy, AloneKey;
        internal float CoverWaitSince = -1f;
        internal string CoverWaitFor, CoverAcceptedFor, CoverNotedFor;

        internal bool Busy => Mode == CaptorMode.Engage;
    }

    internal static class Captors
    {
        private static readonly Dictionary<Player, CaptorState> ByBot = new Dictionary<Player, CaptorState>();
        private static readonly List<CaptorState> Scratch = new List<CaptorState>(8);

        // Surrenders the bot they were made to hasn't perceived yet: prisoner -> that bot.
        private static readonly Dictionary<SurrenderState, BotOwner> Pending = new Dictionary<SurrenderState, BotOwner>();
        private static readonly List<SurrenderState> PendingScratch = new List<SurrenderState>(8);
        private static float _nextPending, _nextLater, _nextKillCheck;

        private const int MaxCoverMoves = 2;
        private const float CoverMoveLimit = 6f;

        internal static int Count => ByBot.Count;
        internal static int PendingCount => Pending.Count;
        internal static bool Any => ByBot.Count > 0 || Pending.Count > 0 || CaptorLoot.Count > 0 || Judgement.Watching;

        internal static bool TryGet(Player bot, out CaptorState cs) => ByBot.TryGetValue(bot, out cs);

        // ---- the decision ------------------------------------------------------------

        internal static void OnSurrender(SurrenderState s, Player captor, string reason)
        {
            BotOwner o = captor?.AIData?.BotOwner;
            if (o == null || s?.Bot == null) return;
            // He can only decide about a surrender he perceives. Far off, behind him, out of his
            // sight: the offer stands, unanswered, until he does (or it runs out).
            if (!Perception.Recognises(o, s.Bot))
            {
                if (Pending.Count < 16) Pending[s] = o;
                HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(captor)}: hasn't seen {s.Name} give up ({Vector3.Distance(captor.Position, s.Bot.Position):0} m); the surrender is unanswered for now");
                return;
            }
            Decide(s, o);
        }

        private static void Decide(SurrenderState s, BotOwner o)
        {
            Player captor = o.GetPlayer;
            var log = new StringBuilder();
            log.Append($"{Restraint.Name(captor)} ({captor.Profile?.Info?.Settings?.Role}): {s.Name} surrenders to him ({Vector3.Distance(captor.Position, s.Bot.Position):0} m): ");

            string notPractical = Impractical(o, s, out bool allied);
            float mercy = Restraint.Mercy(captor, SurrenderRegistry.HistoryOf(captor));
            // 0.16.0: what this man did to him (his own memory of being hit) weighs on it.
            float known = Judgement.MercyFactor(o, s, out string knownWhy);
            if (known < 1f) { mercy *= known; log.Append($"knows: {knownWhy} (mercy x{known:0.00}); "); }
            if (notPractical == null)
            {
                // Squadmates close by to back him make taking a prisoner a little more attractive.
                int backing = Mathf.Min(2, Reactions.FreeAlliesNear(o, captor));
                float chance = Mathf.Clamp01(Settings.AcceptChance.Value * mercy * (1f + 0.1f * backing));
                float roll = Dice.Value;
                log.Append($"practical, mercy x{mercy:0.00}, {backing} squadmate(s) near, take chance {chance:0.00}, roll {roll:0.00}");
                if (roll < chance)
                {
                    HandsAreHighPlugin.Log.LogInfo(log.Append(" -> takes him prisoner").ToString());
                    Accept(s, o, "his prisoner");
                    return;
                }
                notPractical = "declines to guard him";
            }
            log.Append(notPractical);

            // At a limit or busy himself: a free squadmate in sight may take him.
            if (!allied && TryHandToSquad(s, o, log)) { HandsAreHighPlugin.Log.LogInfo(log.ToString()); return; }

            float spare = Mathf.Clamp(Settings.SpareChance.Value * mercy, 0f, 0.98f);
            float r2 = Dice.Value;
            log.Append($"; spare chance {spare:0.00}, roll {r2:0.00}");
            if (r2 < spare)
            {
                HandsAreHighPlugin.Log.LogInfo(log.Append(" -> spares him (no custody)").ToString());
                Restraint.Set(s, o, RestraintKind.Respect, $"spares him ({notPractical})", squad: false);
                Restraint.InformSquad(s, o, "decision to spare him", custody: false);
                return;
            }
            HandsAreHighPlugin.Log.LogInfo(log.Append(" -> refuses the surrender").ToString());
            Restraint.Set(s, o, RestraintKind.Reject, $"refuses ({notPractical}, mercy x{mercy:0.00})", squad: false);
        }

        // Null when taking custody is practical for him; otherwise why not.
        private static string Impractical(BotOwner o, SurrenderState s, out bool allied)
        {
            Player c = o.GetPlayer;
            allied = IsPlayerAlly(o);
            if (allied) return "a player ally: holds fire, the player decides about prisoners";
            if (!Settings.BotCaptors.Value) return "bot captors off";
            if (!CanGuard(c)) return $"no guarding behaviour for a {c.Profile?.Info?.Settings?.Role}";
            if (o.BotState != EBotState.Active || o.IsDead) return "not active";
            if (SurrenderRegistry.HoldsBot(c)) return "held himself";
            if (ByBot.Count >= Settings.MaxAiEncounters.Value) return $"{ByBot.Count} bot-held prisoners already";
            if (ByBot.ContainsKey(c)) return "already guarding someone";   // one prisoner per bot
            if (!(c.HandsController is Player.FirearmController)) return "no gun in his hands";
            if (Arsenal.OutOfAmmo(o)) return "out of ammo";
            float d = Vector3.Distance(c.Position, s.Bot.Position);
            // Beyond the guard range he can still take him if he can walk up (a complete NavMesh
            // path; he only walks while nothing threatens him: Captors.Check / Approach).
            if (d > Settings.AcceptRange.Value &&
                (d > Settings.CaptorApproachFrom.Value || !Perception.Reachable(c.Position, s.Bot.Position)))
                return d > Settings.CaptorApproachFrom.Value ? $"{d:0} m away, too far to guard" : $"{d:0} m away, no way to walk up to him";
            if (!Perception.Recognises(o, s.Bot)) return "can't see him";
            if (Restraint.OtherThreat(o, s.Bot, out string threat)) return $"busy with {threat}";
            if (Restraint.SquadDecidedToKill(o, s.Bot)) return "his squad decided to kill him";
            return null;
        }

        // Only brains this mod registers its captor layer on (Scavs, PMCs; bosses when enabled).
        private static bool CanGuard(Player p)
        {
            NaturalSurrender.RoleClass rc = NaturalSurrender.ClassOf(p);
            return rc == NaturalSurrender.RoleClass.Scav || rc == NaturalSurrender.RoleClass.Pmc ||
                   (rc == NaturalSurrender.RoleClass.Boss && HandsAreHighPlugin.BossLayersRegistered);
        }

        // Verified relationship, not faction: the bot's own group lists the main player as an
        // ally, or he follows Pit Fireteam's boss for you (Teammates).
        internal static bool IsPlayerAlly(BotOwner o) => Teammates.IsPlayerTeammate(o);

        private static bool TryHandToSquad(SurrenderState s, BotOwner from, StringBuilder log)
        {
            BotsGroup g = from.BotsGroup;
            if (g == null || !Settings.BotCaptors.Value) return false;
            for (int i = 0; i < g.MembersCount; i++)
            {
                BotOwner m = g.Member(i);
                if (m == null || m == from || m.IsDead) continue;
                if (Impractical(m, s, out _) != null) continue;
                log.Append($"; squadmate {Restraint.Name(m.GetPlayer)} takes him instead");
                Restraint.Set(s, from, RestraintKind.Respect, $"{Restraint.Name(m.GetPlayer)} takes him", squad: true);
                Accept(s, m, $"takes {Restraint.Name(from.GetPlayer)}'s prisoner");
                return true;
            }
            return false;
        }

        // A bot who chose to spare a man nobody guards may take him (a third party, or the
        // captor's squadmate after the captor fell). Never from a living human captor.
        internal static void MaybeTakeOver(SurrenderState s, BotOwner o)
        {
            if (s.Custody != null || s.Phase != Phase.HandsUp) return;
            Player captor = s.Captor;
            if (captor != null && !captor.IsAI && Perception.Alive(captor)) return;
            if (Impractical(o, s, out _) != null) return;
            if (Dice.Value > 0.5f * Restraint.Mercy(o.GetPlayer, SurrenderRegistry.HistoryOf(o.GetPlayer))) return;
            Accept(s, o, "nobody holds him: takes custody");
        }

        // Debug scenarios (forced state): custody through the same Accept a real decision uses
        // (plan, restraint, squad informed, ORBIT, voice), skipping only the decision itself and
        // the perception/range/threat checks. Hard limits still refuse: he must be an active,
        // living guard-capable bot, not held, not already guarding, not a player ally.
        internal static bool ForceAccept(SurrenderState s, BotOwner o, string why, out string refused)
        {
            refused = null;
            Player c = o?.GetPlayer;
            if (s?.Bot == null || s.Dead || s.Phase != Phase.HandsUp) refused = "prisoner isn't holding his hands up";
            else if (c == null || o.IsDead || !Perception.Alive(c)) refused = "captor isn't alive";
            else if (o.BotState != EBotState.Active) refused = "captor isn't active yet";
            else if (!CanGuard(c)) refused = $"no guarding behaviour for a {c.Profile?.Info?.Settings?.Role}";
            else if (IsPlayerAlly(o)) refused = "captor is a player ally (allies never guard)";
            else if (SurrenderRegistry.HoldsBot(c)) refused = "captor is held himself";
            else if (ByBot.ContainsKey(c)) refused = "captor already guards someone";
            else if (s.Custody != null) refused = $"already in {s.Custody.Name}'s custody";
            if (refused != null) return false;
            s.Captor = c;
            Accept(s, o, why);
            return true;
        }

        // Debug scenarios (0.15.0): the captor's plan forced: close in or not (the roll skipped,
        // the spot, path and threat checks real), kneel or stay (the readiness and terrain checks
        // real), then let go after the usual hold. Null when applied.
        internal static string DebugPlan(Player captor, bool close, bool kneel)
        {
            if (captor == null || !ByBot.TryGetValue(captor, out CaptorState cs)) return "isn't guarding anyone";
            cs.CloseDecided = true;
            cs.Close = close;
            cs.CloseTries = 0;
            cs.Plan.Clear();
            cs.PlanName = $"debug: {(close ? "close in, " : "")}{(kneel ? "kneel" : "stay")}, then release";
            cs.Plan.Add(kneel ? OrderKind.Kneel : OrderKind.Stay);
            cs.Plan.Add(OrderKind.Leave);
            cs.Step = 0;
            cs.AwaitKneel = false;
            cs.HoldUntil = Mathf.Max(cs.HoldUntil, Time.time + 40f);
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: plan forced: {cs.PlanName} (holds him at least 40 s)");
            return null;
        }

        // Debug scenarios (0.16.0): the captor's plan forced to these steps, in order (then Leave
        // after the usual hold). Every step still goes through Orders.Give and its readiness checks.
        internal static string DebugSteps(Player captor, string name, params OrderKind[] steps)
        {
            if (captor == null || !ByBot.TryGetValue(captor, out CaptorState cs)) return "isn't guarding anyone";
            cs.Plan.Clear();
            foreach (OrderKind k in steps) cs.Plan.Add(k);
            if (cs.Plan.Count == 0 || cs.Plan[cs.Plan.Count - 1] != OrderKind.Leave) cs.Plan.Add(OrderKind.Leave);
            cs.PlanName = $"debug: {name}";
            cs.Step = 0;
            cs.CloseDecided = true;
            cs.Close = false;
            cs.AwaitKneel = false;
            cs.HoldUntil = Mathf.Max(cs.HoldUntil, Time.time + 30f + 5f * steps.Length);
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: plan forced: {cs.PlanName} ({string.Join(", ", cs.Plan)}; holds him at least {cs.HoldUntil - Time.time:0} s)");
            return null;
        }

        // Debug scenarios: start one of the captor's own glances now (the real glance code picks
        // where he looks and for how long; the prisoner sees it as a real opening).
        internal static bool ForceGlance(Player captor, out string why)
        {
            why = null;
            if (captor == null || !ByBot.TryGetValue(captor, out CaptorState cs)) { why = "isn't guarding anyone"; return false; }
            if (!Settings.CaptorGlances.Value) { why = "Captors look around is off"; return false; }
            if (cs.Mode != CaptorMode.Guard) { why = $"is in {cs.Mode}, not guarding"; return false; }
            cs.NextGlanceAt = Time.time;
            cs.NextCheck = Time.time;
            return true;
        }

        private static void Accept(SurrenderState s, BotOwner o, string why, bool takingOver = false)
        {
            Player c = o.GetPlayer;
            Pending.Remove(s);
            float now = Time.time;
            var cs = new CaptorState
            {
                Bot = c, Owner = o, Group = o.BotsGroup, Name = Restraint.Name(c), Prisoner = s,
                Since = now,
                HoldUntil = now + Settings.Between(Settings.HoldMin, Settings.HoldMax),
                NextOrderAt = now + Dice.Range(0.4f, 1.2f),
                ModeSince = now,
                NextGlanceAt = now + Dice.Range(4f, 9f)
            };
            BuildPlan(cs);
            SurrenderStats.Acknowledged(s, botCustody: true);
            try { Judgement.Intend(cs, s); } catch (System.Exception e) { HandsAreHighPlugin.Report("captor intent", e); }
            cs.DemandSeen = s.DemandDoneAt;   // 0.16.0: an earlier demand (someone else's) isn't his to judge
            ByBot[c] = cs;
            s.Custody = cs;
            s.Captor = c;
            s.CaptorLastSeen = c.Position;
            s.CaptorHiddenSince = -1f;
            s.FaceSet = false;
            s.Accept(takingOver ? $"{cs.Name} takes over custody" : $"{cs.Name} takes custody");
            Restraint.Set(s, o, RestraintKind.Respect, why, squad: true);
            Restraint.InformSquad(s, o, "prisoner", custody: true);
            OrbitAdapter.KeepAwake(o, true, "holds a prisoner");
            OrbitAdapter.KeepAwake(s.Bot.AIData?.BotOwner, true, "held by a bot");
            // Turn-taking: his demand waits for the prisoner's own line (CaptorVoice).
            CaptorVoice.Say(c, takingOver ? CaptorLine.TakeOver : CaptorLine.Demand, Dice.Range(0.3f, 0.8f));
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: holds {s.Name} ({why}); plan {cs.PlanName}, lets him go within {cs.HoldUntil - now:0} s");
        }

        // Varied per encounter; never an escort (Follow is only used into nearby cover), always
        // ends with letting him go.
        private static void BuildPlan(CaptorState cs)
        {
            bool armed = Arsenal.HasItems(cs.Prisoner.Bot, Arsenal.WeaponSlots, false);
            float a = armed ? 1f : 0f, b = 0.6f, c = armed ? 0.6f : 0f;
            float pick = Dice.Value * (a + b + c);
            cs.Plan.Clear();
            if (pick < a) { cs.PlanName = "disarm and release"; cs.Plan.Add(OrderKind.Stay); cs.Plan.Add(OrderKind.DropWeapons); }
            else if (pick < a + b) { cs.PlanName = "hold and release"; cs.Plan.Add(Dice.Value < Settings.CaptorKneelChance.Value ? OrderKind.Kneel : OrderKind.Stay); }
            else { cs.PlanName = "disarm and hold"; cs.Plan.Add(OrderKind.DropWeapons); cs.Plan.Add(Dice.Value < Settings.CaptorKneelChance.Value ? OrderKind.Kneel : OrderKind.Stay); }
            // 0.16.0: hands on his head (a kneel already brings them there) and turning him away,
            // each by its own chance; they come after the disarming, before he is let go.
            if (!cs.Plan.Contains(OrderKind.Kneel) && Settings.HandsOnHeadOrder.Value && Dice.Value < Settings.CaptorHandsOnHeadChance.Value)
            { cs.Plan.Add(OrderKind.HandsOnHead); cs.PlanName += ", hands on head"; }
            if (Settings.TurnOrder.Value && Dice.Value < Settings.CaptorTurnChance.Value)
            { cs.Plan.Add(OrderKind.TurnAround); cs.PlanName += ", turned away"; }
            // Kneel (0.12.0): a held man is put on his knees; Leave later gets him up calmly.
            cs.Plan.Add(OrderKind.Leave);
            cs.Step = 0;
            if (cs.PlanName.StartsWith("disarm and release")) cs.HoldUntil = Mathf.Min(cs.HoldUntil, Time.time + Settings.HoldMin.Value);
            cs.HoldUntil = Mathf.Max(cs.HoldUntil, Time.time + 6f + 4f * (cs.Plan.Count - 1));
        }

        // ---- custody upkeep ------------------------------------------------------------

        // Every frame; each captor checked every 0.25 s, unrecognised surrenders every 0.5 s.
        internal static void Tick()
        {
            float now = Time.time;
            if (Pending.Count > 0 && now >= _nextPending)
            {
                _nextPending = now + 0.5f;
                try { TickPending(); } catch (System.Exception e) { HandsAreHighPlugin.Report("captor recognition", e); Pending.Clear(); }
            }
            if (Judgement.Watching && now >= _nextKillCheck)
            {
                _nextKillCheck = now + 0.5f;
                try { Judgement.TickKills(now); } catch (System.Exception e) { HandsAreHighPlugin.Report("kill decision watch", e); }
            }
            if (CaptorLoot.Count > 0 && now >= _nextLater)
            {
                _nextLater = now + 0.25f;
                try { CaptorLoot.TickLater(now); } catch (System.Exception e) { HandsAreHighPlugin.Report("captor gear after release", e); CaptorLoot.Clear(); }
            }
            // 0.20.0: covering squadmates watch the prisoner 10x a second (their own reaction time).
            if (Overwatch.Count > 0)
            {
                try { Overwatch.Tick(now); } catch (System.Exception e) { HandsAreHighPlugin.Report("squad cover", e); }
            }
            if (ByBot.Count == 0) return;
            Scratch.Clear();
            foreach (CaptorState cs in ByBot.Values) Scratch.Add(cs);
            for (int i = 0; i < Scratch.Count; i++)
            {
                CaptorState cs = Scratch[i];
                if (now < cs.NextCheck) continue;
                cs.NextCheck = now + 0.25f;
                try { Check(cs, now); }
                catch (System.Exception e) { HandsAreHighPlugin.Report("captor", e); End(cs, "error"); }
            }
            Scratch.Clear();
        }

        // The bot a man gave up to has now seen it (or the offer is over).
        private static void TickPending()
        {
            PendingScratch.Clear();
            foreach (SurrenderState s in Pending.Keys) PendingScratch.Add(s);
            float now = Time.time;
            for (int i = 0; i < PendingScratch.Count; i++)
            {
                SurrenderState s = PendingScratch[i];
                BotOwner o = Pending[s];
                bool over = s.Dead || s.Bot == null || !SurrenderRegistry.TryGet(s.Bot, out SurrenderState cur) || cur != s ||
                            s.Phase != Phase.HandsUp || s.Custody != null || o == null || o.IsDead || s.Captor != o.GetPlayer;
                if (over) { Pending.Remove(s); continue; }
                if (o.BotState != EBotState.Active || s.Suspended) continue;   // asleep: waits, nobody woken
                // The observer scan may have decided for him meanwhile: that decision stands.
                if (Restraint.TryGet(s.Bot, o.GetPlayer, out RestraintEntry e) && now < e.Until) { Pending.Remove(s); continue; }
                if (!Perception.Recognises(o, s.Bot)) continue;
                Pending.Remove(s);
                HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(o.GetPlayer)}: now sees {s.Name} with his hands up ({Vector3.Distance(o.GetPlayer.Position, s.Bot.Position):0} m)");
                Decide(s, o);
            }
            PendingScratch.Clear();
        }

        private static void Check(CaptorState cs, float now)
        {
            SurrenderState s = cs.Prisoner;
            if (s == null || s.Dead || s.Bot == null || !SurrenderRegistry.TryGet(s.Bot, out SurrenderState cur) || cur != s || s.Custody != cs)
            { End(cs, "prisoner gone"); return; }

            if (!Perception.Alive(cs.Bot) || cs.Owner == null || cs.Owner.IsDead) { CaptorDown(cs, "captor down"); return; }
            if (SurrenderRegistry.HoldsBot(cs.Bot)) { CaptorDown(cs, "captor gave up himself"); return; }
            // 0.20.0: his squadmate's cover: chosen, positioned, measured (Overwatch.cs).
            try { Overwatch.Update(cs, s, now); } catch (System.Exception e) { HandsAreHighPlugin.Report("squad cover", e); }
            if (s.Phase != Phase.HandsUp)
            {
                // 0.15.0: he reacts to what he can see, not to the prisoner's decision: the arms
                // coming down, the man moving, a draw in his hands; then his own reaction time.
                string shown = Reactions.Shows(s);
                bool seesHim = shown != null && Perception.Recognises(cs.Owner, s.Bot);
                if (cs.ResistSeenAt < 0f)
                {
                    if (!seesHim && now - s.PhaseSince < 2.5f) return;   // nothing visible yet (2.5 s at most)
                    // 0.16.0: bent over the man's gear (or walking to it, eyes on it), he is slower to see it.
                    bool fetching = CaptorLoot.Fetching(cs.Bot);
                    cs.ResistSeenAt = now + (seesHim ? Dice.Range(0.18f, 0.45f) + (fetching ? Dice.Range(0.35f, 0.8f) : 0f) : 0f);
                    if (Settings.LogDecisions.Value)
                        HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {(seesHim ? $"sees {s.Name} {shown}" : $"{s.Name} is no longer where he held him")} ({s.Phase}, {now - s.PhaseSince:0.00} s after he decided)");
                }
                if (now < cs.ResistSeenAt) return;
                // He resists: going for a weapon gets the urgent line, running a warning.
                bool armed = s.Phase == Phase.Rearmed || s.Phase == Phase.Recovering || s.Phase == Phase.PickingUp;
                // 0.17.0: a man running from his custody (not let go, unarmed) is a decision point of
                // its own, made once, only if he saw it: let him run, or fire (Judgement.OnBolt).
                // Going for a weapon is fought as before (Restraint.OnArmed).
                if (!armed && seesHim && Judgement.OnBolt(cs, s)) return;
                CaptorVoice.Say(cs.Bot, armed ? CaptorLine.Resist : CaptorLine.Warn);
                End(cs, $"prisoner {s.Phase}");
                return;
            }
            cs.ResistSeenAt = -1f;
            if (cs.Owner.BotState != EBotState.Active || s.Suspended) return;   // asleep: nothing, revalidated on wake
            LootingBotsAdapter.Hold(cs.Owner, 10f);
            Judgement.Watch(cs, s, now);

            ThreatLevel t = Assess(cs, now, out string why, out Vector3 at, out bool atKnown);
            switch (cs.Mode)
            {
                case CaptorMode.Guard:
                    if (t == ThreatLevel.Contact) { Engage(cs, s, now, why, at, atKnown); return; }
                    if (t == ThreatLevel.Fire)
                    {
                        if (TryCover(cs, s, now, why, at, atKnown)) return;
                        if (cs.CoverPendingSince >= 0f && now - cs.CoverPendingSince < 0.75f) return;   // search waits for budget
                        Engage(cs, s, now, why, at, atKnown);
                        return;
                    }
                    cs.CoverPendingSince = -1f;
                    break;
                case CaptorMode.Cover:
                    if (t == ThreatLevel.Contact) { Engage(cs, s, now, why, at, atKnown); return; }
                    UpdateCover(cs, s, now);
                    if (cs.Mode != CaptorMode.Guard) return;
                    break;
                case CaptorMode.Engage:
                    if (t >= ThreatLevel.Fire) cs.QuietSince = -1f;
                    else if (cs.QuietSince < 0f) cs.QuietSince = now;
                    if (now - cs.ModeSince > Settings.BusyRelease.Value)
                    {
                        CaptorVoice.Say(cs.Bot, CaptorLine.Abandon);
                        End(cs, $"fighting {cs.ModeWhy} for {Settings.BusyRelease.Value:0} s, gives up custody");
                        return;
                    }
                    bool committed = now - cs.ModeSince < Settings.MinEngageSeconds.Value;
                    bool quiet = cs.QuietSince >= 0f && now - cs.QuietSince >= Settings.QuietBeforeGuarding.Value;
                    if (committed || !quiet) return;
                    SetMode(cs, CaptorMode.Guard, $"quiet for {now - cs.QuietSince:0.0} s");
                    cs.NextOrderAt = Mathf.Max(cs.NextOrderAt, now + 1f);
                    cs.LostSightSince = -1f;
                    return;   // guarding resumes next check
            }

            // Guarding.
            if (t == ThreatLevel.Alert) { cs.AlertAt = at; cs.AlertSeenAt = now; }
            bool sees = Perception.Recognises(cs.Owner, s.Bot);
            // 0.16.0: fetching the man's gear: the fetch has him (it ends on any threat, the man
            // moving or the item gone); no walk-up or glances meanwhile.
            if (CaptorLoot.Fetching(cs.Bot))
            {
                CaptorLoot.Step(cs, s, now, t);
                if (CaptorLoot.Fetching(cs.Bot)) return;
                cs.NextOrderAt = Mathf.Max(cs.NextOrderAt, now + Dice.Range(0.5f, 1.2f));
            }
            Approach(cs, s, now, t, sees);
            if (!cs.Approaching) Glances(cs, s, now);

            if (!sees && !cs.Approaching)
            {
                if (cs.LostSightSince < 0f) cs.LostSightSince = now;
                else if (now - cs.LostSightSince > 6f)
                {
                    CaptorVoice.Say(cs.Bot, CaptorLine.LostSight);
                    End(cs, "lost sight of him");
                    return;
                }
            }
            else cs.LostSightSince = -1f;

            if (now >= cs.HoldUntil && cs.Step < cs.Plan.Count - 1) cs.Step = cs.Plan.Count - 1;   // time's up: let him go
            if (cs.AwaitKneel) FollowKneel(cs, s, now);
            if (cs.Approaching || now < cs.NextOrderAt || !sees || now < cs.GlanceUntil) return;
            if (s.DropQueue.Count > 0 || s.ReportAt > 0f) return;   // his last order is still being carried out
            // 0.16.0: a Drop your weapons just answered: what did he see of it? (May demand again,
            // or decide to fire.)
            if (s.DemandDoneAt > 0f && s.DemandDoneAt > cs.DemandSeen)
            {
                cs.DemandSeen = s.DemandDoneAt;
                Judgement.AfterDemand(cs, s, now);
                if (!ByBot.ContainsKey(cs.Bot) || s.Custody != cs) return;
                if (now < cs.NextOrderAt) return;
            }
            OrderKind next = cs.Plan[Mathf.Min(cs.Step, cs.Plan.Count - 1)];
            // 0.16.0: his gear, once it is all on the ground and he is under control (a choice per
            // item: now, later, never). Before he's let go too.
            if (CaptorLoot.Step(cs, s, now, t)) return;
            // 0.18.0: CaptorLoot.Step may have put a Turn around in front of this step (wants the
            // gear, not with him watching). 0.16-0.17 kept the step read above: the old order was
            // given again and Step++ skipped the turn ("orders him to turn around first", never given).
            next = cs.Plan[Mathf.Min(cs.Step, cs.Plan.Count - 1)];
            if (next == OrderKind.Leave && now < cs.HoldUntil && !cs.PlanName.StartsWith("disarm and release")) return;
            if (cs.AwaitKneel && next != OrderKind.Leave) return;   // the kneel is still being carried out

            // 0.15.0: closing in once the prisoner is under control (a contextual choice).
            if (next != OrderKind.Leave && MaybeCloseIn(cs, s, now)) return;

            // 0.15.0: out of range or without a line, the order isn't tried (it used to be tried
            // and refused every second); a walk-up is asked for instead.
            if (!Orders.CanGive(s, cs.Bot, human: false, out string cant))
            {
                WaitLog(cs, s, next, cant);
                cs.NextOrderAt = now + 1f;
                if (cant == "out of range" || cant == "no clear line") cs.NextApproachAt = Mathf.Min(cs.NextApproachAt, now);
                else if (++cs.OrderFailures >= 8) End(cs, $"couldn't give orders ({cant})");
                return;
            }

            if (next == OrderKind.Kneel && !KneelReady(cs, s, now, out next)) return;
            // 0.16.0: hands on head / turn around need his hands empty and up (and, to turn, not
            // halfway up or down); waited for up to 10 s, else the step is skipped (logged).
            if ((next == OrderKind.HandsOnHead || next == OrderKind.TurnAround) && !StepReady(cs, s, now, next)) return;
            // 0.16.0: the plan is done: let him go, or his judgement says otherwise (once).
            if (next == OrderKind.Leave && Judgement.AtRelease(cs, s)) return;

            if (Orders.Give(s, next, cs.Bot, human: false, out string whyNot))
            {
                cs.OrderFailures = 0;
                if (next == OrderKind.Leave) { End(cs, "let him go", released: true); return; }
                if (next == OrderKind.Kneel)
                {
                    cs.AwaitKneel = true;
                    cs.KneelIssuedAt = now;
                }
                cs.Step++;
                cs.NextOrderAt = now + Settings.Between(Settings.OrderGapMin, Settings.OrderGapMax);
            }
            else
            {
                cs.NextOrderAt = now + 1f;
                if (++cs.OrderFailures >= 8) End(cs, $"couldn't give orders ({whyNot})");
            }
        }

        // 0.16.0: Hands on head / Turn around only once his hands are empty and up; Turn around not
        // mid-kneel. Not ready within 10 s: the step is dropped (never an impossible order repeated).
        private static bool StepReady(CaptorState cs, SurrenderState s, float now, OrderKind next)
        {
            string wait = null;
            if (next == OrderKind.HandsOnHead && s.HandsOnHead) { cs.Step++; return false; }   // already there
            if (s.Weight < 0.95f || !s.ArmsUp) wait = "his hands aren't up";
            else if (s.DropPending || s.HandsPending || !Arsenal.HandsEmpty(s.Bot)) wait = "his hands aren't empty yet";
            else if (next == OrderKind.TurnAround && Facing.Unavailable(s) is string no) wait = no.ToLowerInvariant();
            if (wait == null) { cs.StepWaitSince = -1f; return true; }
            if (cs.StepWaitSince < 0f) cs.StepWaitSince = now;
            if (now - cs.StepWaitSince < 10f)
            {
                WaitLog(cs, s, next, wait);
                cs.NextOrderAt = now + 0.5f;
                return false;
            }
            cs.StepWaitSince = -1f;
            cs.Step++;
            if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: drops the {next} order for {s.Name} ({wait} for 10 s)");
            return false;
        }

        private static void WaitLog(CaptorState cs, SurrenderState s, OrderKind next, string why)
        {
            if (cs.WaitLoggedStep == cs.Step) return;
            cs.WaitLoggedStep = cs.Step;
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: order {next} to {s.Name} waits ({why}){(why == "out of range" || why == "no clear line" ? "; walks up first" : "")}");
        }

        // A kneel order only once he is under control: hands up and empty, the gun that was in
        // his hands on the ground (or nothing in them), covered by his captor; and only where
        // kneeling can work (Kneel.Suitable). Not ready within 10 s, or unsuitable: he is told to
        // stay standing instead (never an impossible order repeated).
        private static bool KneelReady(CaptorState cs, SurrenderState s, float now, out OrderKind give)
        {
            give = OrderKind.Kneel;
            string wait = null;
            if (s.Weight < 0.95f || !s.ArmsUp) wait = "his hands aren't up";
            else if (s.DropPending || s.HandsPending || !Arsenal.HandsEmpty(s.Bot)) wait = "his hands aren't empty yet";
            else if (!Perception.Covering(cs.Bot, s.Bot, Settings.WatchAngle.Value)) wait = "isn't covering him";
            if (wait != null)
            {
                if (cs.ReadySince < 0f) cs.ReadySince = now;
                if (now - cs.ReadySince < 10f)
                {
                    WaitLog(cs, s, OrderKind.Kneel, wait);
                    cs.NextOrderAt = now + 0.5f;
                    return false;
                }
                give = OrderKind.Stay;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: no control for a kneel order after 10 s ({wait}); tells {s.Name} to stay instead");
                return true;
            }
            cs.ReadySince = -1f;
            if (!Kneel.Suitable(s, out string why))
            {
                give = OrderKind.Stay;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {s.Name} can't kneel there ({why}); tells him to stay standing instead");
                return true;
            }
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: puts {s.Name} on his knees ({Vector3.Distance(cs.Bot.Position, s.Bot.Position):0.0} m, covering him{(why != null ? "; " + why : "")})");
            return true;
        }

        // The kneel he ordered, followed (one order, never repeated): down, or not possible.
        private static void FollowKneel(CaptorState cs, SurrenderState s, float now)
        {
            string done = null;
            if (s.Kneel == KneelPhase.Down) done = $"sees {s.Name} on his knees ({now - cs.KneelIssuedAt:0.0} s after the order)";
            else if (s.KneelFallback != null) done = $"{s.Name} can't kneel ({s.KneelFallback}); lets him stay standing, hands up";
            else if (s.Order != Order.Kneel) done = $"{s.Name}'s kneel order was replaced";
            else if (now - cs.KneelIssuedAt > 15f && s.Kneel == KneelPhase.Standing) done = $"{s.Name} still isn't down after 15 s; leaves it at that";
            if (done == null) return;
            cs.AwaitKneel = false;
            if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {done}");
        }

        // ---- closing in (0.15.0) -------------------------------------------------------------
        //
        // Once per custody, after the prisoner has his hands up and has dropped the gun in his
        // hands, the captor may walk closer (Captors close in on a disarmed prisoner) to a fixed
        // spot at the close-in distance on his own side of the prisoner (never within 1.3 m of
        // the dropped gun, a clear line to the prisoner from it, a complete short path). Only with
        // no threat (Approach stops it on any Alert, Fire or Contact), at a walk, gun and eyes on
        // the prisoner; at most two tries; stuck or slow: guards from where he is.
        private static readonly float[] CloseAngles = { 0f, 30f, -30f, 60f, -60f };

        private static bool MaybeCloseIn(CaptorState cs, SurrenderState s, float now)
        {
            if (cs.Approaching || cs.CloseTries >= 2 || cs.Unsafe) return false;
            if (cs.CloseDecided && !cs.Close) return false;
            // Control first: hands up and empty (the held gun on the ground), no order in progress.
            if (s.Weight < 0.95f || s.DropPending || s.HandsPending || !Arsenal.HandsEmpty(s.Bot) || Kneel.Active(s) || s.Order == Order.Follow) return false;
            float d = Vector3.Distance(cs.Bot.Position, s.Bot.Position);
            float standoff = Settings.CaptorStandoff.Value;
            if (d <= standoff + 1.5f) { cs.CloseDecided = true; return false; }
            if (!cs.CloseDecided)
            {
                cs.CloseDecided = true;
                bool kept = Arsenal.GunOnBody(s.Bot) != null || Arsenal.HasMelee(s.Bot);
                int backing = Reactions.FreeAlliesNear(cs.Owner, cs.Bot);
                float hurt = cs.Bot.HealthController != null ? NaturalSurrender.Injury(cs.Bot, out _) : 0f;
                float chance = Mathf.Clamp01(Settings.CaptorCloseChance.Value * (kept ? 0.6f : 1f) * (1f + 0.15f * Mathf.Min(2, backing)) * (1f - 0.6f * hurt));
                float roll = Dice.Value;
                cs.Close = roll < chance;
                if (Settings.LogDecisions.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: close in on {s.Name}? {d:0.0} m, {(kept ? "he still carries a weapon" : "nothing left on him")}, {backing} squadmate(s) near, hurt {hurt:0.00}: chance {chance:0.00}, roll {roll:0.00} -> {(cs.Close ? "yes" : "no, guards from here")}");
                if (!cs.Close) return false;
            }
            // 0.20.0: walking up takes his full attention off the rest of the area: his squadmate's
            // cover first (bounded wait, then his own risk).
            if (Overwatch.HoldForCover(cs, s, now, $"closing in on {s.Name}")) return true;
            if (!Perception.TakeHeavy()) return true;   // waits a frame with path budget
            cs.CloseTries++;
            if (!PickCloseSpot(cs, s, standoff, out string why))
            {
                cs.Close = false;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: no good spot to close in on {s.Name} ({why}); guards from {d:0.0} m");
                return false;
            }
            cs.Approaching = true;
            cs.ApproachKeep = standoff;
            cs.ApproachSince = now;
            cs.ApproachTarget = s.Bot.Position;
            cs.ProgressFrom = cs.Bot.Position;
            cs.ProgressSince = now;
            cs.PathIssued = false;
            cs.GlanceUntil = 0f;
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: closes in on {s.Name} ({d:0.0} m -> {Vector3.Distance(cs.CloseSpot, s.Bot.Position):0.0} m, try {cs.CloseTries}/2), gun on him");
            return true;
        }

        private static bool PickCloseSpot(CaptorState cs, SurrenderState s, float standoff, out string why)
        {
            why = "no candidate";
            Vector3 pr = s.Bot.Position;
            Vector3 dir = cs.Bot.Position - pr; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { why = "on top of him"; return false; }
            dir.Normalize();
            bool gun = s.DroppedWeapon != null && !s.GunGone;
            float now = Vector3.Distance(cs.Bot.Position, pr);
            for (int i = 0; i < CloseAngles.Length; i++)
            {
                Vector3 c = pr + Quaternion.AngleAxis(CloseAngles[i], Vector3.up) * dir * standoff;
                if (!UnityEngine.AI.NavMesh.SamplePosition(c, out UnityEngine.AI.NavMeshHit hit, 0.6f, UnityEngine.AI.NavMesh.AllAreas)) { why = "off the NavMesh"; continue; }
                Vector3 spot = hit.position;
                if (Mathf.Abs(spot.y - pr.y) > 0.6f) { why = "another level"; continue; }
                if (gun && (spot - s.DropPosition).sqrMagnitude < 1.3f * 1.3f) { why = "on his dropped gun"; continue; }
                float sd = Vector3.Distance(spot, pr);
                if (sd < 2.2f || sd > now - 1.2f) { why = "not closer"; continue; }
                if (!Perception.LineClear(spot + Vector3.up * 1.5f, Perception.Chest(s.Bot))) { why = "no line to him from there"; continue; }
                if (!Perception.PathTo(cs.Bot.Position, spot, out Vector3[] corners, out string pw)) { why = pw; continue; }
                if (PathLength(corners) > 2f * Vector3.Distance(cs.Bot.Position, spot) + 2f) { why = "the way round is too long"; continue; }
                cs.CloseSpot = spot;
                cs.CloseFrom = pr;
                return true;
            }
            return false;
        }

        // ---- walking up to him (0.14.0) ---------------------------------------------------
        //
        // A captor too far to give orders (or who lost his line to the prisoner) walks up to a
        // practical distance along a complete NavMesh path (BotMover.GoToByWay; never a teleport),
        // gun on the prisoner, at a walk. Only while nothing threatens him: any Alert, Fire or
        // Contact stops it at once (Fire goes to Cover, Contact to Engage, as before), so he never
        // crosses open ground under fire; guarding from where he stands is always valid. Stuck
        // 2.5 s, 25 s total or no path: he stops and guards from there (one retry after 8 s).
        private const float ApproachStuck = 2.5f, ApproachLimit = 25f;

        private static void Approach(CaptorState cs, SurrenderState s, float now, ThreatLevel t, bool sees)
        {
            float d = Vector3.Distance(cs.Bot.Position, s.Bot.Position);
            float keep = Mathf.Min(Settings.CaptorGuardDistance.Value, Settings.AiOrderRange.Value - 2f);
            if (cs.Approaching && cs.Close)
            {
                // Closing in: to a fixed spot (no new spot per frame, no circling).
                string stop = null;
                Vector3 to = cs.CloseSpot - cs.Bot.Position; to.y = 0f;
                if (t != ThreatLevel.None) stop = "something else needs his attention";
                else if (to.magnitude <= 0.6f || d <= Settings.CaptorStandoff.Value + 0.3f) stop = $"in position ({d:0.0} m)";
                else if ((s.Bot.Position - cs.CloseFrom).sqrMagnitude > 1.5f * 1.5f) stop = "the prisoner moved";
                else if (now - cs.ApproachSince > 12f) stop = "took too long";
                else if ((cs.Bot.Position - cs.ProgressFrom).sqrMagnitude >= 0.25f) { cs.ProgressFrom = cs.Bot.Position; cs.ProgressSince = now; }
                else if (now - cs.ProgressSince > ApproachStuck) stop = "stuck";
                if (stop != null)
                {
                    cs.Approaching = false;
                    cs.PathIssued = false;
                    bool done = stop.StartsWith("in position");
                    if (done || t != ThreatLevel.None) cs.CloseTries = 2;   // no second go after a threat
                    else cs.Close = cs.CloseTries < 2;
                    cs.NextApproachAt = now + 6f;
                    cs.NextOrderAt = Mathf.Max(cs.NextOrderAt, now + (done ? Dice.Range(0.4f, 1f) : 1f));
                    if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: stops closing in on {s.Name}: {stop}");
                }
                return;
            }
            if (cs.Approaching)
            {
                string stop = null;
                if (t != ThreatLevel.None) stop = "something else needs his attention";
                else if (d <= Mathf.Max(cs.ApproachKeep, 1.5f) + 0.5f && sees) stop = $"close enough ({d:0.0} m)";
                else if (d <= keep && sees && cs.ApproachKeep >= keep) stop = $"close enough ({d:0.0} m)";
                else if (now - cs.ApproachSince > ApproachLimit) stop = "took too long";
                else if ((cs.Bot.Position - cs.ProgressFrom).sqrMagnitude >= 0.25f) { cs.ProgressFrom = cs.Bot.Position; cs.ProgressSince = now; }
                else if (now - cs.ProgressSince > ApproachStuck) stop = "stuck";
                if (stop == null && (s.Bot.Position - cs.ApproachTarget).sqrMagnitude > 4f) cs.PathIssued = false;   // he moved: a fresh path
                if (stop != null)
                {
                    cs.Approaching = false;
                    cs.PathIssued = false;
                    cs.NextApproachAt = now + (stop.StartsWith("close") ? 3f : 8f);
                    if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: stops walking up to {s.Name}: {stop}");
                }
                return;
            }
            if (Settings.CaptorApproachFrom.Value <= 0f || t != ThreatLevel.None || now < cs.NextApproachAt || now < cs.GlanceUntil) return;
            bool far = d > Settings.AiOrderRange.Value - 1f;
            bool blind = !sees && d > 2.5f;
            if (!far && !blind) return;
            if (!Perception.TakeHeavy()) return;   // one path solve: a frame with budget
            if (!Perception.PathTo(cs.Bot.Position, s.Bot.Position, out Vector3[] corners, out string why))
            {
                cs.NextApproachAt = now + 8f;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: can't walk up to {s.Name} ({why}); guards from {d:0} m");
                return;
            }
            cs.Approaching = true;
            cs.ApproachKeep = blind && !far ? 2.5f : keep;   // lost his line close by: closer, to see him again
            cs.ApproachSince = now;
            cs.ApproachTarget = s.Bot.Position;
            cs.ProgressFrom = cs.Bot.Position;
            cs.ProgressSince = now;
            cs.PathIssued = false;
            cs.GlanceUntil = 0f;
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: walks up to {s.Name} ({d:0} m{(blind ? ", can't see him from here" : "")}, path {PathLength(corners):0} m, stops at ~{cs.ApproachKeep:0.0} m)");
        }

        // The part of a path that ends `keep` metres (along the path) short of its end.
        internal static Vector3[] Shorten(Vector3[] corners, float keep)
        {
            if (corners == null || corners.Length < 2) return corners;
            float total = PathLength(corners);
            float want = total - keep;
            if (want <= 0.3f) return new[] { corners[0] };
            var list = new List<Vector3>(corners.Length) { corners[0] };
            float acc = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                float seg = Vector3.Distance(corners[i - 1], corners[i]);
                if (acc + seg >= want)
                {
                    list.Add(Vector3.Lerp(corners[i - 1], corners[i], Mathf.Clamp01((want - acc) / Mathf.Max(seg, 1e-4f))));
                    return list.ToArray();
                }
                acc += seg;
                list.Add(corners[i]);
            }
            return list.ToArray();
        }

        internal static float PathLength(Vector3[] c)
        {
            float l = 0f;
            for (int i = 1; i < c.Length; i++) l += Vector3.Distance(c[i - 1], c[i]);
            return l;
        }

        // ---- threat ----------------------------------------------------------------------

        // From what the game (and SAIN) say he perceives. at: where it is (exact for a visible
        // enemy; for fire, where the near miss came from, blurred; for an alert, his last
        // sighting); atKnown false when he can't tell (a hit with no near miss to place it).
        private static ThreatLevel Assess(CaptorState cs, float now, out string why, out Vector3 at, out bool atKnown) =>
            Threat(cs.Owner, cs.Bot, cs.Prisoner.Bot, now, out why, out at, out atKnown);

        // 0.20.0: the same threat sense for any bot about a prisoner (a covering squadmate too).
        internal static ThreatLevel Threat(BotOwner o, Player self, Player prisoner, float now, out string why, out Vector3 at, out bool atKnown)
        {
            at = Vector3.zero; atKnown = false; why = null;
            string recentName = null;
            Vector3 recentAt = Vector3.zero;
            float recentSeen = -1000f;
            var infos = o.EnemiesController?.EnemyInfos;
            if (infos != null)
            {
                foreach (KeyValuePair<IPlayer, EnemyInfo> kv in infos)
                {
                    EnemyInfo e = kv.Value;
                    // 0.14.0: the prisoner is often in this map as his BotOwner (bot enemies are
                    // registered that way), so "is this him" goes through Perception.Same. Before,
                    // he counted as "another enemy in sight": Guard -> Engage at once, the layer let
                    // go, SAIN fought him, and custody was dropped after BusyRelease.
                    if (e == null || Perception.Same(e.Person, prisoner)) continue;
                    if (e.Person?.HealthController == null || !e.Person.HealthController.IsAlive) continue;
                    Player ep = Perception.PlayerOf(e.Person);
                    if (ep != null && (Restraint.HoldsFire(self, ep) || SurrenderRegistry.HoldsBot(ep) || ScenarioShield.Blocks(self, ep))) continue;
                    if (e.IsVisible)
                    {
                        why = ep != null ? Restraint.Name(ep) : "an enemy";
                        at = e.CurrPosition; atKnown = true;
                        return ThreatLevel.Contact;
                    }
                    if (e.HaveSeenPersonal && e.PersonalLastSeenTime > recentSeen)
                    {
                        recentSeen = e.PersonalLastSeenTime;
                        recentAt = e.PersonalLastPos;
                        recentName = ep != null ? Restraint.Name(ep) : "an enemy";
                    }
                }
            }
            BotHistory h = SurrenderRegistry.HistoryOf(self);
            bool memFire = o.Memory != null && o.Memory.IsUnderFire;
            bool nearMisses = now - h.FireAt < 2.5f && Suppression.Decayed(h, now) > 0.3f;
            float sain = SainAdapter.SuppressionLevel(o, h);
            if (memFire || nearMisses || sain >= 0.5f)
            {
                why = memFire ? "incoming fire" : nearMisses ? "rounds passing close" : "suppressed (SAIN)";
                if (now - h.FireFromAt < 4f) { at = h.FireFrom; atKnown = true; }
                else if (now - recentSeen < 10f) { at = recentAt; atKnown = true; }   // he assumes it's the man he just saw
                return ThreatLevel.Fire;
            }
            if (now - recentSeen < 5f)
            {
                why = $"{recentName} seen {now - recentSeen:0.0} s ago";
                at = recentAt; atKnown = true;
                return ThreatLevel.Alert;
            }
            return ThreatLevel.None;
        }

        private static void SetMode(CaptorState cs, CaptorMode m, string why)
        {
            if (cs.Mode == m) return;
            CaptorMode old = cs.Mode;
            cs.Mode = m;
            cs.ModeSince = Time.time;
            cs.ModeWhy = why;
            cs.QuietSince = -1f;
            if (cs.Approaching && cs.Close) { cs.CloseTries = 2; cs.Close = false; }   // no second close-in after a threat
            cs.Approaching = false;
            if (m != CaptorMode.Cover) cs.PathIssued = false;
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {old} -> {m} ({why}), prisoner {cs.Prisoner?.Name}");
        }

        // Survival and combat first: the layer lets go this check and the combat AI has him.
        private static void Engage(CaptorState cs, SurrenderState s, float now, string why, Vector3 at, bool atKnown)
        {
            SetMode(cs, CaptorMode.Engage, why);
            cs.CoverPendingSince = -1f;
            cs.GlanceUntil = 0f;
            CaptorVoice.Interrupt(cs.Bot);
            bool behind = atKnown && Vector3.Angle(cs.Bot.LookDirection, at - cs.Bot.Position) > 120f;
            CaptorVoice.Say(cs.Bot, why.StartsWith("incoming") || why.StartsWith("rounds") || why.StartsWith("suppressed") ? CaptorLine.UnderFire : CaptorLine.Threat, 0f, behind);
            // A prisoner following him into cover stops where he is: he isn't led into the fight.
            if (s.FollowToCover) { s.FollowToCover = false; if (s.Order == Order.Follow) s.Order = Order.None; }
            if (cs.HandOverTried) return;
            cs.HandOverTried = true;
            TryHandOver(cs, s, why);
        }

        // A free squadmate who can actually cover him (practical by the same rule as taking a
        // prisoner: armed, not busy himself, sees him, in range) takes custody over.
        private static void TryHandOver(CaptorState cs, SurrenderState s, string why)
        {
            BotsGroup g = cs.Group;
            if (g == null) return;
            // Out of the count while asking (a hand-over doesn't add a prisoner to the map limit).
            ByBot.Remove(cs.Bot);
            BotOwner m = SuccessorFor(cs, s);
            if (m != null)
            {
                bool guard = m == cs.Guard?.Owner;
                Overwatch.Release(cs, $"custody goes to {Restraint.Name(m.GetPlayer)}", resisted: false);
                OrbitAdapter.KeepAwake(cs.Owner, false, "handed his prisoner over");
                s.Custody = null;
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: busy with {why}, hands {s.Name} over to {Restraint.Name(m.GetPlayer)}{(guard ? " (his covering squadmate)" : "")}");
                Accept(s, m, $"takes over from {cs.Name} ({cs.Name} is fighting {why})", takingOver: true);
                return;
            }
            ByBot[cs.Bot] = cs;   // nobody free: he keeps custody while he fights
        }

        // 0.20.0: who takes custody when the captor can't keep it: his covering squadmate first (he
        // is already on the man), then any squadmate, each only if practical (Impractical).
        private static BotOwner SuccessorFor(CaptorState cs, SurrenderState s)
        {
            BotOwner guard = cs.Guard?.Owner;
            if (guard != null && !guard.IsDead && Impractical(guard, s, out _) == null) return guard;
            BotsGroup g = cs.Group;
            if (g == null) return null;
            for (int i = 0; i < g.MembersCount; i++)
            {
                BotOwner m = g.Member(i);
                if (m == null || m == cs.Owner || m == guard || m.IsDead) continue;
                if (Impractical(m, s, out _) == null) return m;
            }
            return null;
        }

        // ---- cover -------------------------------------------------------------------------

        private static bool TryCover(CaptorState cs, SurrenderState s, float now, string why, Vector3 from, bool known)
        {
            if (!Settings.CaptorCover.Value || !known || cs.CoverMoves >= MaxCoverMoves || now < cs.NextCoverAt) return false;
            if (cs.CoverPendingSince < 0f) cs.CoverPendingSince = now;
            // A cover search is up to PathCandidates path solves: only on a frame with budget.
            if (!Perception.TakeHeavy()) return false;
            cs.CoverPendingSince = -1f;
            if (!Perception.CoverPoint(cs.Bot, from, Settings.CaptorCoverRadius.Value, s.Bot, out Vector3 point, out bool keepsSight))
            {
                cs.NextCoverAt = now + 10f;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {why}, no reachable cover within {Settings.CaptorCoverRadius.Value:0} m");
                return false;
            }
            cs.CoverMoves++;
            cs.NextCoverAt = now + 10f;
            cs.CoverPoint = point;
            cs.CoverFrom = from;
            cs.ProgressFrom = cs.Bot.Position;
            cs.ProgressSince = now;
            cs.PathIssued = false;
            cs.GlanceUntil = 0f;
            SetMode(cs, CaptorMode.Cover, $"{why}: cover {Vector3.Distance(point, cs.Bot.Position):0.0} m away{(keepsSight ? ", keeps him in sight" : ", loses sight of him")}");
            CaptorVoice.Interrupt(cs.Bot);
            CaptorVoice.Say(cs.Bot, CaptorLine.TakeCover);

            // A close prisoner comes along (his own complete path to it), if that is allowed.
            if (Settings.PrisonerFollowsToCover.Value &&
                Vector3.Distance(s.Bot.Position, cs.Bot.Position) <= Settings.FollowToCoverWithin.Value &&
                Perception.Reachable(s.Bot.Position, point) &&
                Orders.Give(s, OrderKind.Follow, cs.Bot, human: false, out _))
            {
                s.FollowToCover = true;
            }
            return true;
        }

        private static void UpdateCover(CaptorState cs, SurrenderState s, float now)
        {
            Vector3 d = cs.CoverPoint - cs.Bot.Position; d.y = 0f;
            if (d.magnitude <= 1f) { ArriveCover(cs, s, "in cover"); return; }
            if ((cs.Bot.Position - cs.ProgressFrom).sqrMagnitude >= 0.25f) { cs.ProgressFrom = cs.Bot.Position; cs.ProgressSince = now; }
            else if (now - cs.ProgressSince > 2f) { Engage(cs, s, now, "stuck on the way to cover", cs.CoverFrom, true); return; }
            if (now - cs.ModeSince > CoverMoveLimit) Engage(cs, s, now, "didn't reach cover in time", cs.CoverFrom, true);
        }

        private static void ArriveCover(CaptorState cs, SurrenderState s, string why)
        {
            SetMode(cs, CaptorMode.Guard, why);
            cs.NextOrderAt = Mathf.Max(cs.NextOrderAt, Time.time + 1f);
            if (s.FollowToCover)
            {
                s.FollowToCover = false;
                // Stay there, near him (the order checks range and line; he stops either way).
                if (!Orders.Give(s, OrderKind.Stay, cs.Bot, human: false, out _) && s.Order == Order.Follow) s.Order = Order.None;
            }
        }

        // ---- attention ---------------------------------------------------------------------

        // Not locked on the prisoner: now and then a glance (towards a recent enemy, else around).
        private static void Glances(CaptorState cs, SurrenderState s, float now)
        {
            if (!Settings.CaptorGlances.Value || now < cs.NextGlanceAt) return;
            bool alert = now - cs.AlertSeenAt < 5f;
            if (alert) cs.GlancePoint = cs.AlertAt + Vector3.up * 1.4f;
            else
            {
                Vector3 toPrisoner = s.Bot.Position - cs.Bot.Position; toPrisoner.y = 0f;
                if (toPrisoner.sqrMagnitude < 0.01f) toPrisoner = cs.Bot.Transform.forward;
                float side = Dice.Value < 0.5f ? -1f : 1f;
                Vector3 dir = Quaternion.Euler(0f, side * Dice.Range(60f, 150f), 0f) * toPrisoner.normalized;
                cs.GlancePoint = cs.Bot.Position + dir * 10f + Vector3.up * 1.5f;
            }
            cs.GlanceUntil = now + Dice.Range(0.8f, 1.8f);
            cs.NextGlanceAt = cs.GlanceUntil + (alert ? Dice.Range(2f, 4f) : Dice.Range(6f, 14f));
            if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: glances {(alert ? "towards where he last saw an enemy" : "around")} for {cs.GlanceUntil - now:0.0} s");
        }

        // ---- endings ---------------------------------------------------------------------

        // The captor fell or gave up: a squadmate who sees the prisoner and is free may take over;
        // otherwise custody ends and every restraint stays as decided (no execution because the
        // captor died: the squad's ceasefire is theirs, not the captor's).
        private static void CaptorDown(CaptorState cs, string why)
        {
            SurrenderState s = cs.Prisoner;
            ByBot.Remove(cs.Bot);
            try { CaptorLoot.OnCustodyEnd(cs, released: false); } catch (System.Exception e) { HandsAreHighPlugin.Report("captor gear after custody", e); }
            OrbitAdapter.KeepAwake(cs.Owner, false, why);
            if (s != null && s.Custody == cs) s.Custody = null;
            if (s != null && cs.Group != null && s.Phase == Phase.HandsUp && !s.Dead)
            {
                BotOwner m = SuccessorFor(cs, s);
                if (m != null)
                {
                    bool guard = m == cs.Guard?.Owner;
                    Overwatch.Release(cs, $"{why}; custody goes to {Restraint.Name(m.GetPlayer)}", resisted: false);
                    HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {why}; {Restraint.Name(m.GetPlayer)} takes over {s.Name}{(guard ? " (he was covering him)" : "")}");
                    Accept(s, m, $"takes over from {cs.Name} ({why})", takingOver: true);
                    return;
                }
            }
            Overwatch.Release(cs, why, resisted: s != null && !s.Dead && s.Phase != Phase.HandsUp);
            Unanswered(s);
            OrbitAdapter.KeepAwake(s?.Bot?.AIData?.BotOwner, false, "custody over");
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {why}; nobody takes over {s?.Name}, restraint stays as decided");
        }

        // Custody ends. The decisions are per observer and outlive custody (restraint stays);
        // unless he was released, his surrender is unanswered again: he reassesses from what he
        // sees (a bounded offer, then he seeks safety).
        internal static void End(CaptorState cs, string why, bool released = false)
        {
            if (!ByBot.Remove(cs.Bot) && cs.Prisoner?.Custody != cs) return;
            try { CaptorLoot.OnCustodyEnd(cs, released); } catch (System.Exception e) { HandsAreHighPlugin.Report("captor gear after custody", e); }
            SurrenderState s = cs.Prisoner;
            // 0.20.0: his squadmate's cover ends with the custody (a man resisting is still watched
            // by him for a moment: his own reaction).
            try { Overwatch.Release(cs, why, resisted: !released && s != null && !s.Dead && s.Phase != Phase.HandsUp); }
            catch (System.Exception e) { HandsAreHighPlugin.Report("squad cover release", e); }
            if (s != null && s.Custody == cs) s.Custody = null;
            OrbitAdapter.KeepAwake(cs.Owner, false, why);
            OrbitAdapter.KeepAwake(s?.Bot?.AIData?.BotOwner, false, why);
            if (!released) Unanswered(s);
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: stops holding {s?.Name} ({why}) after {Time.time - cs.Since:0} s");
        }

        private static void Unanswered(SurrenderState s)
        {
            if (s == null || s.Dead || s.Phase != Phase.HandsUp) return;
            s.Accepted = false;
            s.OfferUntil = Time.time + 0.6f * Settings.Between(Settings.OfferMin, Settings.OfferMax);
            if (s.FollowToCover) { s.FollowToCover = false; if (s.Order == Order.Follow) s.Order = Order.None; }
        }

        // He surrendered himself, or a human took over his prisoner.
        internal static void EndFor(Player bot, string why)
        {
            if (bot != null && ByBot.TryGetValue(bot, out CaptorState cs)) End(cs, why);
        }

        internal static void EndCustody(SurrenderState s, string why)
        {
            if (s == null) return;
            Pending.Remove(s);
            if (s.Custody != null) End(s.Custody, why);
        }

        internal static void Clear()
        {
            foreach (CaptorState cs in ByBot.Values) { if (cs.Prisoner != null) cs.Prisoner.Custody = null; cs.Guard = null; }
            ByBot.Clear();
            Overwatch.Clear();   // 0.20.0 (ORBIT blocks are lifted by ReleaseAll below)
            Pending.Clear();
            PendingScratch.Clear();
            CaptorLoot.Clear();
            OrbitAdapter.ReleaseAll();
        }

        // ---- the guarding layer -----------------------------------------------------------

        // Who the layer drives: a captor with custody guarding or moving to cover, prisoner hands
        // up; or a squadmate watching (his decision's WatchUntil), with no target of his own.
        internal static bool WantsControl(BotOwner o, out SurrenderState prisoner)
        {
            prisoner = null;
            // 0.20.1: asked for every bot on every brain update (CaptorLayer.IsActive). With no
            // custody, no gear fetch and no squad cover anywhere, nothing below can be true.
            if (ByBot.Count == 0 && CaptorLoot.Count == 0 && Overwatch.Count == 0) return false;
            Player p = o?.GetPlayer;
            if (p == null || !Settings.Enabled.Value) return false;
            // 0.16.0: fetching a released man's gear (custody already over).
            if (CaptorLoot.WantsControl(p)) return true;
            if (ByBot.TryGetValue(p, out CaptorState cs))
            {
                if (cs.Mode == CaptorMode.Engage || cs.Prisoner == null || cs.Prisoner.Suspended) return false;
                // 0.15.0: a prisoner who has stopped complying keeps the guard on him until the
                // guard has noticed it (Check: ResistSeenAt, his reaction time); then custody ends.
                if (cs.Prisoner.Phase != Phase.HandsUp && cs.ResistSeenAt >= 0f && Time.time >= cs.ResistSeenAt) return false;
                prisoner = cs.Prisoner;
                return true;
            }
            // 0.20.0: the squadmate covering a captor (not while he fights or after he reacted: the
            // combat AI has him then, never the idle watcher below).
            if (Overwatch.WantsControl(p, out prisoner)) return true;
            if (Overwatch.TryDrive(p, out _)) return false;
            if (!Restraint.Any || Settings.WatchSeconds.Value <= 0f) return false;
            // Watchers: squadmates of a captor, idle (no target), within their watch time.
            foreach (CaptorState c in ByBot.Values)
            {
                if (c.Group != o.BotsGroup || c.Busy || c.Prisoner == null || c.Prisoner.Phase != Phase.HandsUp) continue;
                if (!Restraint.TryGet(c.Prisoner.Bot, p, out RestraintEntry e) || !e.Holds || Time.time >= e.WatchUntil) continue;
                if (o.Memory?.GoalEnemy != null || o.Memory?.IsUnderFire == true) return false;
                prisoner = c.Prisoner;
                return true;
            }
            return false;
        }

        internal static void RegisterLayers(List<string> brains, int priority) =>
            BrainManager.AddCustomLayer(typeof(CaptorLayer), brains, priority);
    }

    internal sealed class CaptorLayer : CustomLayer
    {
        public CaptorLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        public override string GetName() => "HandsAreHigh_Captor";
        public override bool IsActive()
        {
            long t0 = Perf.Begin();
            bool on = Captors.WantsControl(BotOwner, out _);
            Perf.End(Perf.Sec.LayerCheck, t0);
            return on;
        }
        public override Action GetNextAction() => new Action(typeof(CaptorLogic), "Guarding");
        public override bool IsCurrentActionEnding() => !IsActive();
    }

    // Guard: stands, faces the prisoner (his gun follows his look) or glances away. Cover: walks
    // a NavMesh path to the cover spot (BotMover.GoToByWay, as the prisoner moves; no teleport,
    // the NavMesh is the only wall). Orders and mode changes come from Captors.Check.
    internal sealed class CaptorLogic : CustomLogic
    {
        private float _pose = -1f;
        private float _nextRelease;
        private bool _stopped;
        private bool _moving;

        public CaptorLogic(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            _stopped = false;
            _moving = false;
            BotOwner.Sprint(false);
        }

        public override void Stop()
        {
            _pose = -1f;
            if (_moving) { BotOwner.Sprint(false); _moving = false; }
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            long t0 = Perf.Begin();
            try
            {
                if (!Captors.WantsControl(BotOwner, out SurrenderState s)) return;
                if (Time.time >= _nextRelease)
                {
                    _nextRelease = Time.time + 0.25f;
                    SainAdapter.ReleaseLayer(BotOwner, SurrenderRegistry.HistoryOf(BotOwner.GetPlayer));
                }
                // 0.16.0: walking to the prisoner's gear and picking it up.
                if (CaptorLoot.TryJob(BotOwner.GetPlayer, out LootJob job)) { Fetch(job); return; }
                if (s?.Bot == null) return;
                // 0.20.0: covering a squadmate's prisoner.
                if (Overwatch.TryDrive(BotOwner.GetPlayer, out CoverGuard guard)) { Cover(guard, s); return; }
                Captors.TryGet(BotOwner.GetPlayer, out CaptorState cs);
                if (cs != null && cs.Mode == CaptorMode.Cover) { MoveToCover(cs); return; }
                if (cs != null && cs.Approaching) { WalkUp(cs, s); return; }

                if (_moving || !_stopped) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); BotOwner.Sprint(false); _stopped = true; _moving = false; }
                if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
                if (cs != null && Time.time < cs.GlanceUntil) BotOwner.Steering.LookToPoint(cs.GlancePoint);
                else BotOwner.Steering.LookToPoint(Perception.Chest(s.Bot));
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("captor logic", e); }
            Perf.End(Perf.Sec.Captors, t0);
        }

        // One path (re-issued every 3 s while not there), sprinting, looking where he goes.
        private void MoveToCover(CaptorState cs)
        {
            if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
            if (!cs.PathIssued || Time.time >= cs.NextPathAt)
            {
                cs.NextPathAt = Time.time + 3f;
                if (!Perception.PathTo(BotOwner.GetPlayer.Position, cs.CoverPoint, out Vector3[] corners, out _))
                {
                    cs.ProgressSince = -1000f;   // Captors.Check: stuck -> Engage (no repeated solves)
                    cs.PathIssued = true;
                    return;
                }
                if (!_moving) Relink();
                cs.PathIssued = true;
                _moving = true;
                _stopped = false;
                BotOwner.SetTargetMoveSpeed(1f);
                BotOwner.MovementResume();
                BotOwner.Mover.GoToByWay(corners, 0.5f);
                BotOwner.Sprint(true);
            }
            BotOwner.Steering.LookToMovingDirection();
        }

        // Walking up to the prisoner: one path (re-made when he has moved 2 m, at most every
        // 1.5 s), at a walk, gun and eyes on him (BotMover moves him along the path whatever way
        // he faces; no sprint, so SAIN's sprint-direction rule doesn't apply).
        private void WalkUp(CaptorState cs, SurrenderState s)
        {
            if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
            if (cs.Close && !cs.PathIssued)
            {
                // Closing in: one path to the fixed spot.
                cs.PathIssued = true;
                if (!Perception.PathTo(BotOwner.GetPlayer.Position, cs.CloseSpot, out Vector3[] cc, out _) || cc.Length < 2)
                { cs.ProgressSince = -1000f; return; }
                if (!_moving) Relink();
                _moving = true;
                _stopped = false;
                BotOwner.Sprint(false);
                BotOwner.SetTargetMoveSpeed(Mathf.Min(Settings.CaptorApproachSpeed.Value, 0.6f));
                BotOwner.MovementResume();
                BotOwner.Mover.GoToByWay(cc, 0.4f);
            }
            if (cs.Close) { BotOwner.Steering.LookToPoint(Perception.Chest(s.Bot)); return; }
            if (!cs.PathIssued && Time.time >= cs.NextPathAt)
            {
                cs.NextPathAt = Time.time + 1.5f;
                float keep = cs.ApproachKeep;
                if (!Perception.PathTo(BotOwner.GetPlayer.Position, s.Bot.Position, out Vector3[] corners, out _))
                {
                    cs.ProgressSince = -1000f;   // Captors.Approach: stuck -> stops, guards from here
                    cs.PathIssued = true;
                    return;
                }
                Vector3[] way = Captors.Shorten(corners, keep);
                cs.ApproachTarget = s.Bot.Position;
                cs.PathIssued = true;
                if (way.Length < 2) { cs.ProgressSince = -1000f; return; }   // already as close as the path allows
                if (!_moving) Relink();
                _moving = true;
                _stopped = false;
                BotOwner.Sprint(false);
                BotOwner.SetTargetMoveSpeed(Settings.CaptorApproachSpeed.Value);
                BotOwner.MovementResume();
                BotOwner.Mover.GoToByWay(way, 0.5f);
            }
            BotOwner.Steering.LookToPoint(Perception.Chest(s.Bot));
        }

        // 0.20.0: a covering squadmate. Moving: one complete path to his spot (re-made at most every
        // 2 s if lost), at a walk (no sprint: his gun stays on the prisoner, as BotMover moves him
        // whichever way he faces). Holding: still, standing, eyes and gun on the prisoner, or on
        // his look outward (Overwatch.UpdateScan). Where and when to move: Overwatch.UpdatePosition.
        private void Cover(CoverGuard g, SurrenderState s)
        {
            if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
            Vector3 chest = Perception.Chest(s.Bot);
            if (g.Stage == GuardStage.Moving)
            {
                if (!g.PathIssued && Time.time >= g.NextPathAt)
                {
                    g.NextPathAt = Time.time + 2f;
                    g.PathIssued = true;
                    if (!Perception.PathTo(BotOwner.GetPlayer.Position, g.Spot, out Vector3[] corners, out _) || corners.Length < 2)
                    {
                        g.ProgressSince = -1000f;   // Overwatch: stuck -> covers from where he is
                        return;
                    }
                    if (!_moving) Relink();
                    _moving = true;
                    _stopped = false;
                    BotOwner.Sprint(false);
                    BotOwner.SetTargetMoveSpeed(Vector3.Distance(BotOwner.GetPlayer.Position, g.Spot) > 10f ? 1f : 0.75f);
                    BotOwner.MovementResume();
                    BotOwner.Mover.GoToByWay(corners, 0.5f);
                }
                BotOwner.Steering.LookToPoint(chest);
                return;
            }
            if (_moving || !_stopped) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); BotOwner.Sprint(false); _stopped = true; _moving = false; }
            if (Time.time < g.ScanUntil) BotOwner.Steering.LookToPoint(g.ScanPoint);
            else BotOwner.Steering.LookToPoint(chest);
        }

        // 0.16.0: one path to a point just short of the item, at a walk; eyes on the prisoner until
        // close, then on the item; then still, looking down at it, for the game's pick-up.
        // 0.19.0: to ~0.7 m (in reach), slowing over the last 2 m; a distant item is a real walk (eyes
        // where he goes, a look back at the man every few seconds); the look goes down to the item
        // from his eyes at a human speed; after the pick-up he straightens up and looks back (Recover)
        // before CaptorLoot hands him back to guarding. Why it snapped: CaptorLoot's header.
        private const float LookDownSpeed = 150f;   // deg/s
        private const float LookBackSpeed = 140f;

        private void Fetch(LootJob j)
        {
            if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
            Player me = BotOwner.GetPlayer;
            SurrenderState p = j.Prisoner;
            bool watchable = !j.AfterRelease && p?.Bot != null && Perception.Alive(p.Bot);
            float now = Time.time;
            if (j.Stage == LootStage.Walk)
            {
                if (!j.PathIssued)
                {
                    j.PathIssued = true;
                    if (!Perception.PathTo(me.Position, j.At, out Vector3[] corners, out _)) { j.ProgressSince = -1000f; return; }
                    Vector3[] way = Captors.Shorten(corners, 0.7f);
                    if (way.Length >= 2)
                    {
                        if (!_moving) Relink();
                        _moving = true;
                        _stopped = false;
                        BotOwner.Sprint(false);
                        BotOwner.SetTargetMoveSpeed(j.Distant ? 0.8f : 0.65f);
                        BotOwner.MovementResume();
                        BotOwner.Mover.GoToByWay(way, 0.3f);
                    }
                }
                float sq = (me.Position - j.At).sqrMagnitude;
                if (_moving && sq < 2f * 2f) BotOwner.SetTargetMoveSpeed(0.4f);   // eases into the stop
                bool close = sq < 3f * 3f;
                if (j.Distant && !close)
                {
                    if (watchable && now >= j.NextGlanceBack)
                    {
                        j.GlanceBackUntil = now + Dice.Range(0.6f, 1f);
                        j.NextGlanceBack = now + Dice.Range(2.2f, 3.5f);
                    }
                    if (watchable && now < j.GlanceBackUntil) BotOwner.Steering.LookToPoint(Perception.Chest(p.Bot), LookBackSpeed);
                    else BotOwner.Steering.LookToMovingDirection();
                }
                else if (!close && watchable) BotOwner.Steering.LookToPoint(Perception.Chest(p.Bot));
                else BotOwner.Steering.LookToPoint(j.At + Vector3.up * 0.1f, LookDownSpeed);
                return;
            }
            if (_moving || !_stopped) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); BotOwner.Sprint(false); _stopped = true; _moving = false; }
            if (j.Stage == LootStage.Recover)
            {
                if (watchable) BotOwner.Steering.LookToPoint(Perception.Chest(p.Bot), LookBackSpeed);
                else
                {
                    Vector3 f = me.LookDirection; f.y = 0f;
                    if (f.sqrMagnitude > 1e-4f) BotOwner.Steering.LookToDirection(f.normalized, LookBackSpeed);
                }
                return;
            }
            // From his eyes to the item: a look over 60 deg down is what makes the game's pick-up
            // lower his body to the floor (PickUpState.Enter); from the feet it was ~17 deg.
            if (j.Stage == LootStage.Settle) BotOwner.Steering.LookToDirection(CaptorLoot.PickupLook(me, j.At), LookDownSpeed);
            CaptorLoot.Settled(j, now);
        }

        // As SurrenderLogic.Relink: the mover's last-good-link fields set to where he stands, so
        // BotMover doesn't snap a walking bot back to a stale point (possibly behind a wall).
        private void Relink()
        {
            BotMover mover = BotOwner.Mover;
            if (mover == null) return;
            Vector3 here = BotOwner.GetPlayer.Position;
            mover.PositionOnWayInner = here;
            mover._prevSuccessLinkedFrom = here;
            mover._prevLinkPos = here;
            mover._lastGoodCastPoint = here;
            mover._lastGoodCastPointTime = Time.time;
            mover._prevPosLinkedTime = 0f;
        }
    }
}
