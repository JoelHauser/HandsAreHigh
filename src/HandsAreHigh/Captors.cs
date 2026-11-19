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
    // On the surrender (SurrenderRegistry.Surrender with an AI threat), the bot decides once:
    //   Accept  practical (firearm in hands with rounds, no other enemy in sight, not under
    //           fire, the prisoner in sight within AcceptRange, under the encounter and
    //           per-captor limits, not himself held, not a player ally) and he wants to
    //           (AcceptChance x mercy). He takes custody: holds fire, his squad holds fire,
    //           he guards and gives orders.
    //   Spare   not taking custody (impractical, at a limit, or declined) but holding fire
    //           anyway (SpareChance x mercy): restraint without custody. The prisoner is then
    //           on his own: nobody covers him, so his own logic finds openings or leaves.
    //   Reject  keeps fighting him (logged with the reason).
    // A captor at his limit first offers the prisoner to a free squadmate.
    //
    // Custody: one captor per prisoner (SurrenderState.Custody), one prisoner per bot, MaxAiEncounters across the map. A held bot can't be a captor and a captor
    // who surrenders loses his prisoner, so no capture loops. Squadmates who hold fire only
    // support; they never become extra captors.
    //
    // Guarding (CaptorLayer, BigBrain, below the surrender layer and SAIN's avoid-threat
    // layer, above SAIN's combat layers): only while he isn't busy. Stands, faces the prisoner
    // (the aim his weapon follows), gives his plan's orders through the same validated path as
    // yours (Orders.Give). Busy (another enemy in sight, incoming fire) his layer lets go at once
    // and SAIN / vanilla fight; busy for BusyRelease he gives up custody. Ends: plan done (he
    // lets him go), hold limit, prisoner armed / ran / gone, captor down (a squadmate in sight
    // may take over; otherwise custody ends and the restraint stays as decided: nobody opens
    // fire because the captor died). Player allies (BotsGroup.IsAlly) never get the layer.
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
        internal bool Busy;
        internal float BusySince = -1f;
        internal string BusyWhy;
        internal float LostSightSince = -1f;
        internal int OrderFailures;
    }

    internal static class Captors
    {
        private static readonly Dictionary<Player, CaptorState> ByBot = new Dictionary<Player, CaptorState>();
        private static readonly List<CaptorState> Scratch = new List<CaptorState>(8);

        internal static int Count => ByBot.Count;
        internal static bool Any => ByBot.Count > 0;

        internal static bool TryGet(Player bot, out CaptorState cs) => ByBot.TryGetValue(bot, out cs);

        // ---- the decision ------------------------------------------------------------

        internal static void OnSurrender(SurrenderState s, Player captor, string reason)
        {
            BotOwner o = captor?.AIData?.BotOwner;
            if (o == null || s?.Bot == null) return;
            var log = new StringBuilder();
            log.Append($"{Restraint.Name(captor)} ({captor.Profile?.Info?.Settings?.Role}): {s.Name} surrenders to him: ");

            string notPractical = Impractical(o, s, out bool allied);
            float mercy = Restraint.Mercy(captor, SurrenderRegistry.HistoryOf(captor));
            if (notPractical == null)
            {
                float chance = Mathf.Clamp01(Settings.AcceptChance.Value * mercy);
                float roll = Random.value;
                log.Append($"practical, mercy x{mercy:0.00}, take chance {chance:0.00}, roll {roll:0.00}");
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
            float r2 = Random.value;
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
            if (d > Settings.AcceptRange.Value) return $"{d:0} m away";
            if (!Perception.LineClear(Perception.Eye(c), Perception.Chest(s.Bot))) return "can't see him";
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

        // Verified relationship, not faction: the bot's own group lists the main player as an ally.
        internal static bool IsPlayerAlly(BotOwner o)
        {
            Player me = Singleton<GameWorld>.Instance?.MainPlayer;
            try { return me != null && o?.BotsGroup != null && o.BotsGroup.IsAlly(me); }
            catch { return false; }
        }

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
            if (Random.value > 0.5f * Restraint.Mercy(o.GetPlayer, SurrenderRegistry.HistoryOf(o.GetPlayer))) return;
            Accept(s, o, "nobody holds him: takes custody");
        }

        private static void Accept(SurrenderState s, BotOwner o, string why)
        {
            Player c = o.GetPlayer;
            var cs = new CaptorState
            {
                Bot = c, Owner = o, Group = o.BotsGroup, Name = Restraint.Name(c), Prisoner = s,
                Since = Time.time,
                HoldUntil = Time.time + Settings.Between(Settings.HoldMin, Settings.HoldMax),
                NextOrderAt = Time.time + Random.Range(0.4f, 1.2f)
            };
            BuildPlan(cs);
            ByBot[c] = cs;
            s.Custody = cs;
            s.Captor = c;
            s.CaptorLastSeen = c.Position;
            s.CaptorHiddenSince = -1f;
            s.FaceSet = false;
            Restraint.Set(s, o, RestraintKind.Respect, why, squad: true);
            Restraint.InformSquad(s, o, "prisoner", custody: true);
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: holds {s.Name} ({why}); plan {cs.PlanName}, lets him go within {cs.HoldUntil - Time.time:0} s");
        }

        // Varied per encounter; never follow (no escorts), always ends with letting him go.
        private static void BuildPlan(CaptorState cs)
        {
            bool armed = Arsenal.HasItems(cs.Prisoner.Bot, Arsenal.WeaponSlots, false);
            float a = armed ? 1f : 0f, b = 0.6f, c = armed ? 0.6f : 0f;
            float pick = Random.value * (a + b + c);
            cs.Plan.Clear();
            if (pick < a) { cs.PlanName = "disarm and release"; cs.Plan.Add(OrderKind.Stay); cs.Plan.Add(OrderKind.DropWeapons); }
            else if (pick < a + b) { cs.PlanName = "hold and release"; cs.Plan.Add(OrderKind.Stay); }
            else { cs.PlanName = "disarm and hold"; cs.Plan.Add(OrderKind.DropWeapons); cs.Plan.Add(OrderKind.Stay); }
            cs.Plan.Add(OrderKind.Leave);
            cs.Step = 0;
            if (cs.PlanName == "disarm and release") cs.HoldUntil = Mathf.Min(cs.HoldUntil, Time.time + Settings.HoldMin.Value);
        }

        // ---- custody upkeep ------------------------------------------------------------

        // Every frame; each captor checked every 0.25 s.
        internal static void Tick()
        {
            if (ByBot.Count == 0) return;
            Scratch.Clear();
            foreach (CaptorState cs in ByBot.Values) Scratch.Add(cs);
            float now = Time.time;
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

        private static void Check(CaptorState cs, float now)
        {
            SurrenderState s = cs.Prisoner;
            if (s == null || s.Dead || s.Bot == null || !SurrenderRegistry.TryGet(s.Bot, out SurrenderState cur) || cur != s || s.Custody != cs)
            { End(cs, "prisoner gone"); return; }

            if (!Perception.Alive(cs.Bot) || cs.Owner == null || cs.Owner.IsDead) { CaptorDown(cs, "captor down"); return; }
            if (SurrenderRegistry.HoldsBot(cs.Bot)) { CaptorDown(cs, "captor gave up himself"); return; }
            if (s.Phase != Phase.HandsUp) { End(cs, $"prisoner {s.Phase}"); return; }
            if (cs.Owner.BotState != EBotState.Active || s.Suspended) return;   // asleep: nothing, revalidated on wake

            // Busy: the layer lets go at once (combat AI fights), custody goes after BusyRelease.
            bool busy = Restraint.OtherThreat(cs.Owner, s.Bot, out string threat);
            if (busy)
            {
                if (!cs.Busy) { cs.Busy = true; cs.BusySince = now; cs.BusyWhy = threat; if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: busy with {threat}, stops guarding {s.Name} for now"); }
                if (now - cs.BusySince > Settings.BusyRelease.Value) End(cs, $"busy with {cs.BusyWhy} for {Settings.BusyRelease.Value:0} s");
                return;
            }
            if (cs.Busy) { cs.Busy = false; cs.NextOrderAt = Mathf.Max(cs.NextOrderAt, now + 1f); }

            bool sees = Perception.LineClear(Perception.Eye(cs.Bot), Perception.Chest(s.Bot));
            if (!sees)
            {
                if (cs.LostSightSince < 0f) cs.LostSightSince = now;
                else if (now - cs.LostSightSince > 6f) { End(cs, "lost sight of him"); return; }
            }
            else cs.LostSightSince = -1f;

            if (now >= cs.HoldUntil && cs.Step < cs.Plan.Count - 1) cs.Step = cs.Plan.Count - 1;   // time's up: let him go
            if (now < cs.NextOrderAt || !sees) return;
            if (s.DropQueue.Count > 0 || s.ReportAt > 0f) return;   // his last order is still being carried out
            OrderKind next = cs.Plan[cs.Step];
            if (next == OrderKind.Leave && now < cs.HoldUntil && cs.PlanName != "disarm and release") return;

            if (Orders.Give(s, next, cs.Bot, human: false, out string why))
            {
                cs.OrderFailures = 0;
                if (next == OrderKind.Leave) { End(cs, "let him go", keepRestraint: true); return; }
                cs.Step++;
                cs.NextOrderAt = now + Settings.Between(Settings.OrderGapMin, Settings.OrderGapMax);
            }
            else
            {
                cs.NextOrderAt = now + 1f;
                if (++cs.OrderFailures >= 8) End(cs, $"couldn't give orders ({why})");
            }
        }

        // The captor fell or gave up: a squadmate who sees the prisoner and is free may take over;
        // otherwise custody ends and every restraint stays as decided (no execution because the
        // captor died: the squad's ceasefire is theirs, not the captor's).
        private static void CaptorDown(CaptorState cs, string why)
        {
            SurrenderState s = cs.Prisoner;
            ByBot.Remove(cs.Bot);
            if (s != null && s.Custody == cs) s.Custody = null;
            if (s != null && cs.Group != null && s.Phase == Phase.HandsUp && !s.Dead)
            {
                for (int i = 0; i < cs.Group.MembersCount; i++)
                {
                    BotOwner m = cs.Group.Member(i);
                    if (m == null || m == cs.Owner || m.IsDead) continue;
                    if (Impractical(m, s, out _) != null) continue;
                    var log = $"{cs.Name}: {why}; {Restraint.Name(m.GetPlayer)} takes over {s.Name}";
                    HandsAreHighPlugin.Log.LogInfo(log);
                    Accept(s, m, $"takes over from {cs.Name} ({why})");
                    return;
                }
            }
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {why}; nobody takes over {s?.Name}, restraint stays as decided");
        }

        // Custody ends. keepRestraint: always (the decisions are per observer and outlive
        // custody); the prisoner's own logic sees nobody covering him.
        internal static void End(CaptorState cs, string why, bool keepRestraint = true)
        {
            if (!ByBot.Remove(cs.Bot) && cs.Prisoner?.Custody != cs) return;
            if (cs.Prisoner != null && cs.Prisoner.Custody == cs) cs.Prisoner.Custody = null;
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: stops holding {cs.Prisoner?.Name} ({why}) after {Time.time - cs.Since:0} s");
        }

        // He surrendered himself, or a human took over his prisoner.
        internal static void EndFor(Player bot, string why)
        {
            if (bot != null && ByBot.TryGetValue(bot, out CaptorState cs)) End(cs, why);
        }

        internal static void EndCustody(SurrenderState s, string why)
        {
            if (s?.Custody != null) End(s.Custody, why);
        }

        internal static void Clear()
        {
            foreach (CaptorState cs in ByBot.Values) if (cs.Prisoner != null) cs.Prisoner.Custody = null;
            ByBot.Clear();
        }

        // ---- the guarding layer -----------------------------------------------------------

        // Who the layer drives: a captor with custody, not busy, prisoner hands up; or a
        // squadmate watching (his decision's WatchUntil), with no target of his own.
        internal static bool WantsControl(BotOwner o, out SurrenderState prisoner)
        {
            prisoner = null;
            Player p = o?.GetPlayer;
            if (p == null || !Settings.Enabled.Value) return false;
            if (ByBot.TryGetValue(p, out CaptorState cs))
            {
                if (cs.Busy || cs.Prisoner == null || cs.Prisoner.Phase != Phase.HandsUp || cs.Prisoner.Suspended) return false;
                prisoner = cs.Prisoner;
                return true;
            }
            if (!Restraint.Any || Settings.WatchSeconds.Value <= 0f) return false;
            // Watchers: squadmates of a captor, idle (no target), within their watch time.
            foreach (CaptorState c in ByBot.Values)
            {
                if (c.Group != o.BotsGroup || c.Busy || c.Prisoner == null || c.Prisoner.Phase != Phase.HandsUp) continue;
                if (!Restraint.TryGet(c.Prisoner.Bot, p, out RestraintEntry e) || !e.Holds || Time.time >= e.WatchUntil) continue;
                if (o.Memory?.GoalEnemy != null) return false;
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

    // Stands, faces the prisoner (his gun follows his look), doesn't sprint, doesn't walk.
    // Orders come from Captors.Check, not from here.
    internal sealed class CaptorLogic : CustomLogic
    {
        private float _pose = -1f;
        private float _nextRelease;
        private bool _stopped;

        public CaptorLogic(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            _stopped = false;
            BotOwner.Sprint(false);
        }

        public override void Stop()
        {
            _pose = -1f;
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            long t0 = Perf.Begin();
            try
            {
                if (!Captors.WantsControl(BotOwner, out SurrenderState s) || s?.Bot == null) return;
                if (!_stopped) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); _stopped = true; }
                if (Mathf.Abs(_pose - 1f) > 0.01f) { _pose = 1f; BotOwner.SetPose(1f); }
                BotOwner.Steering.LookToPoint(Perception.Chest(s.Bot));
                if (Time.time >= _nextRelease)
                {
                    _nextRelease = Time.time + 0.25f;
                    SainAdapter.ReleaseLayer(BotOwner, SurrenderRegistry.HistoryOf(BotOwner.GetPlayer));
                }
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("captor logic", e); }
            Perf.End(Perf.Sec.Layer, t0);
        }
    }
}
