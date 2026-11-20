using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
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

        internal bool Busy => Mode == CaptorMode.Engage;
    }

    internal static class Captors
    {
        private static readonly Dictionary<Player, CaptorState> ByBot = new Dictionary<Player, CaptorState>();
        private static readonly List<CaptorState> Scratch = new List<CaptorState>(8);

        // Surrenders the bot they were made to hasn't perceived yet: prisoner -> that bot.
        private static readonly Dictionary<SurrenderState, BotOwner> Pending = new Dictionary<SurrenderState, BotOwner>();
        private static readonly List<SurrenderState> PendingScratch = new List<SurrenderState>(8);
        private static float _nextPending;

        private const int MaxCoverMoves = 2;
        private const float CoverMoveLimit = 6f;

        internal static int Count => ByBot.Count;
        internal static int PendingCount => Pending.Count;
        internal static bool Any => ByBot.Count > 0 || Pending.Count > 0;

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
            // Kneel (0.12.0): a held man is put on his knees; Leave later gets him up calmly.
            cs.Plan.Add(OrderKind.Leave);
            cs.Step = 0;
            if (cs.PlanName == "disarm and release") cs.HoldUntil = Mathf.Min(cs.HoldUntil, Time.time + Settings.HoldMin.Value);
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
            if (s.Phase != Phase.HandsUp)
            {
                // He resists: going for a weapon gets the urgent line, running a warning.
                bool armed = s.Phase == Phase.Rearmed || s.Phase == Phase.Recovering || s.Phase == Phase.PickingUp;
                CaptorVoice.Say(cs.Bot, armed ? CaptorLine.Resist : CaptorLine.Warn);
                End(cs, $"prisoner {s.Phase}");
                return;
            }
            if (cs.Owner.BotState != EBotState.Active || s.Suspended) return;   // asleep: nothing, revalidated on wake

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
            if (now < cs.NextOrderAt || !sees || now < cs.GlanceUntil) return;
            if (s.DropQueue.Count > 0 || s.ReportAt > 0f) return;   // his last order is still being carried out
            OrderKind next = cs.Plan[cs.Step];
            if (next == OrderKind.Leave && now < cs.HoldUntil && cs.PlanName != "disarm and release") return;

            if (Orders.Give(s, next, cs.Bot, human: false, out string whyNot))
            {
                cs.OrderFailures = 0;
                if (next == OrderKind.Leave) { End(cs, "let him go", released: true); return; }
                cs.Step++;
                cs.NextOrderAt = now + Settings.Between(Settings.OrderGapMin, Settings.OrderGapMax);
            }
            else
            {
                cs.NextOrderAt = now + 1f;
                if (++cs.OrderFailures >= 8) End(cs, $"couldn't give orders ({whyNot})");
            }
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

        private static float PathLength(Vector3[] c)
        {
            float l = 0f;
            for (int i = 1; i < c.Length; i++) l += Vector3.Distance(c[i - 1], c[i]);
            return l;
        }

        // ---- threat ----------------------------------------------------------------------

        // From what the game (and SAIN) say he perceives. at: where it is (exact for a visible
        // enemy; for fire, where the near miss came from, blurred; for an alert, his last
        // sighting); atKnown false when he can't tell (a hit with no near miss to place it).
        private static ThreatLevel Assess(CaptorState cs, float now, out string why, out Vector3 at, out bool atKnown)
        {
            BotOwner o = cs.Owner;
            Player prisoner = cs.Prisoner.Bot;
            at = Vector3.zero; atKnown = false; why = null;
            Player self = cs.Bot;
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
            for (int i = 0; i < g.MembersCount; i++)
            {
                BotOwner m = g.Member(i);
                if (m == null || m == cs.Owner || m.IsDead) continue;
                if (Impractical(m, s, out _) != null) continue;
                OrbitAdapter.KeepAwake(cs.Owner, false, "handed his prisoner over");
                s.Custody = null;
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: busy with {why}, hands {s.Name} over to {Restraint.Name(m.GetPlayer)}");
                Accept(s, m, $"takes over from {cs.Name} ({cs.Name} is fighting {why})", takingOver: true);
                return;
            }
            ByBot[cs.Bot] = cs;   // nobody free: he keeps custody while he fights
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
            OrbitAdapter.KeepAwake(cs.Owner, false, why);
            if (s != null && s.Custody == cs) s.Custody = null;
            if (s != null && cs.Group != null && s.Phase == Phase.HandsUp && !s.Dead)
            {
                for (int i = 0; i < cs.Group.MembersCount; i++)
                {
                    BotOwner m = cs.Group.Member(i);
                    if (m == null || m == cs.Owner || m.IsDead) continue;
                    if (Impractical(m, s, out _) != null) continue;
                    HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {why}; {Restraint.Name(m.GetPlayer)} takes over {s.Name}");
                    Accept(s, m, $"takes over from {cs.Name} ({why})", takingOver: true);
                    return;
                }
            }
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
            SurrenderState s = cs.Prisoner;
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
            foreach (CaptorState cs in ByBot.Values) if (cs.Prisoner != null) cs.Prisoner.Custody = null;
            ByBot.Clear();
            Pending.Clear();
            PendingScratch.Clear();
            OrbitAdapter.ReleaseAll();
        }

        // ---- the guarding layer -----------------------------------------------------------

        // Who the layer drives: a captor with custody guarding or moving to cover, prisoner hands
        // up; or a squadmate watching (his decision's WatchUntil), with no target of his own.
        internal static bool WantsControl(BotOwner o, out SurrenderState prisoner)
        {
            prisoner = null;
            Player p = o?.GetPlayer;
            if (p == null || !Settings.Enabled.Value) return false;
            if (ByBot.TryGetValue(p, out CaptorState cs))
            {
                if (cs.Mode == CaptorMode.Engage || cs.Prisoner == null || cs.Prisoner.Phase != Phase.HandsUp || cs.Prisoner.Suspended) return false;
                prisoner = cs.Prisoner;
                return true;
            }
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
        public override bool IsActive() => Captors.WantsControl(BotOwner, out _);
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
                if (!Captors.WantsControl(BotOwner, out SurrenderState s) || s?.Bot == null) return;
                if (Time.time >= _nextRelease)
                {
                    _nextRelease = Time.time + 0.25f;
                    SainAdapter.ReleaseLayer(BotOwner, SurrenderRegistry.HistoryOf(BotOwner.GetPlayer));
                }
                Captors.TryGet(BotOwner.GetPlayer, out CaptorState cs);
                if (cs != null && cs.Mode == CaptorMode.Cover) { MoveToCover(cs); return; }
                if (cs != null && cs.Approaching) { WalkUp(cs, s); return; }

                if (_moving || !_stopped) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); BotOwner.Sprint(false); _stopped = true; _moving = false; }
                if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
                if (cs != null && Time.time < cs.GlanceUntil) BotOwner.Steering.LookToPoint(cs.GlancePoint);
                else BotOwner.Steering.LookToPoint(Perception.Chest(s.Bot));
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("captor logic", e); }
            Perf.End(Perf.Sec.Layer, t0);
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
