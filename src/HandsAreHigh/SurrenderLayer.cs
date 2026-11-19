using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // While a bot is in the registry this layer outranks SAIN's, ORBIT's and the vanilla
    // ones, so nothing else moves, aims or shoots with him until it hands him back.
    internal sealed class SurrenderLayer : CustomLayer
    {
        public SurrenderLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }

        public override string GetName() => "HandsAreHigh_Surrender";

        public override bool IsActive() => SurrenderRegistry.HoldsBot(BotOwner?.GetPlayer);

        public override Action GetNextAction() => new Action(typeof(SurrenderLogic), "Surrendered");

        public override bool IsCurrentActionEnding() => !IsActive();
    }

    internal sealed class SurrenderLogic : CustomLogic
    {
        private const float CheckInterval = 0.25f;
        private const float RepathInterval = 3f;
        private const float ReachDist = 0.5f;
        private const float ScrambleGrace = 0.75f;
        private const int MaxDropAttempts = 3;

        public SurrenderLogic(BotOwner botOwner) : base(botOwner) { }

        private Player Me => BotOwner.GetPlayer;

        public override void Start()
        {
            BotOwner.ShootData?.EndShoot();
            BotOwner.Sprint(false);
        }

        private SurrenderState _s;   // the state this frame's Update is working on (for Hold)
        private float _pose = -1f;

        public override void Update(CustomLayer.ActionData data)
        {
            if (!SurrenderRegistry.TryGet(Me, out SurrenderState s) || s.Suspended || s.Dead) return;
            _s = s;
            // Inline: the old Try($"phase {s.Phase}", () => Run(s)) built a string and a closure
            // per held bot per frame. The label is only made when something throws.
            long t0 = Perf.Begin();
            try { Run(s); }
            catch (System.Exception e) { HandsAreHighPlugin.Report(PhaseLabels[(int)s.Phase], e); }
            Perf.End(Perf.Sec.Layer, t0);
        }

        private static readonly string[] PhaseLabels = System.Array.ConvertAll(
            (Phase[])System.Enum.GetValues(typeof(Phase)), p => "phase " + p);

        public override void Stop()
        {
            // Handing the bot back: leave the mover and pose for the next layer to set.
            _pose = -1f;
            base.Stop();
        }

        private void Run(SurrenderState s)
        {
            bool check = Time.time >= s.NextCheck;
            if (check)
            {
                s.NextCheck = Time.time + CheckInterval;
                // While we hold him, nothing else moves or turns him (see SainAdapter.ReleaseLayer).
                if (SainAdapter.ReleaseLayer(BotOwner, s) && !s.SainReleased)
                {
                    s.SainReleased = true;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: SAIN still had its layer marked active; released so it stops steering him and blocking his movement");
                }
            }

            switch (s.Phase)
            {
                case Phase.HandsUp: HandsUp(s, check); break;
                case Phase.Retreating: Retreating(s, check); break;
                case Phase.Regrouping: Regrouping(s, check); break;
                case Phase.Recovering: Recovering(s, check); break;
                case Phase.PickingUp: PickingUp(s); break;
                case Phase.Hiding: Hiding(s, check); break;
                case Phase.Rearmed: Hold(1f); break;
            }
        }

        // ---- hands up ------------------------------------------------------------------

        private void HandsUp(SurrenderState s, bool check)
        {
            Player captor = s.Captor;
            bool captorAlive = Perception.Alive(captor);

            switch (s.Order)
            {
                case Order.Kneel: Hold(0f); break;   // no kneel animation exists: lowest crouch, hands up
                case Order.Follow when captorAlive: Follow(s, captor); break;
                default: Hold(Settings.Stance.Value); break;
            }
            if (Settings.FaceCaptor.Value && captorAlive)
                Face(s, captor.Position + Vector3.up * 1.6f);

            ProcessDropQueue(s);
            if (!check) return;

            if (s.DropPending && !CheckDrop(s)) return;

            // Deterministic debug commands (this bot only).
            if (s.ForceRun)
            {
                s.ForceRun = false;
                s.BeginRetreat("debug: forced run", emergency: true);
                return;
            }
            if (s.ForceGrab)
            {
                s.ForceGrab = false;
                if (!captorAlive) { HandsAreHighPlugin.Log.LogInfo($"{s.Name}: forced grab: no captor"); return; }
                float q = ScrambleQuality(s, captor.Position, Perception.Eye(captor), out string qWhy, forced: true);
                if (q > 0f) StartScramble(s, captor.Position, $"debug: forced, {qWhy}");
                else HandsAreHighPlugin.Log.LogInfo($"{s.Name}: forced grab not possible: {qWhy}");
                return;
            }

            // Shot while his hands are up, by anyone: he runs. (A hit, not health: a bleeding
            // man loses health every second without anyone firing.)
            bool hit = s.WasHit;
            s.WasHit = false;
            if (hit) Gestures.Cancel(s, "hit");
            if (hit && Settings.BoltWhenShot.Value)
            {
                Voice.Say(s, VoiceEvent.Hurt);
                SurrenderRegistry.Witness(Me.Position, Me, "a surrendered man shot");
                s.BeginRetreat("shot with his hands up", emergency: true);
                return;
            }

            if (s.ForcedLeave)
            {
                s.ForcedLeave = false;
                s.BeginRetreat("debug: captor left", emergency: false);
                return;
            }

            float now = Time.time;
            if (now >= s.NextSlowCheck)
            {
                s.NextSlowCheck = now + 1f;
                Reactions.RefreshBody(s);
                NoticeGunGone(s, captorAlive ? captor : null);
            }

            // What he can see of the captor. Out of sight, he only knows where he last saw him.
            float dist = captorAlive ? Vector3.Distance(captor.Position, Me.Position) : float.MaxValue;
            bool seen = captorAlive && Perception.LineClear(Perception.Eye(Me), Perception.Eye(captor));
            string gone = null;
            if (!captorAlive) gone = "captor is dead";
            else if (seen)
            {
                s.CaptorLastSeen = captor.Position;
                s.CaptorHiddenSince = -1f;
                if (dist > Settings.LeaveDistance.Value) gone = $"captor {dist:0} m away";
            }
            else
            {
                if (s.CaptorHiddenSince < 0f) s.CaptorHiddenSince = now;
                if (now - s.CaptorHiddenSince > Settings.LeaveSeconds.Value) gone = $"captor out of sight {Settings.LeaveSeconds.Value:0} s";
            }

            // Captor gone: he reacts after his own reaction time, and only if it still holds.
            if (gone != null)
            {
                if (s.LeaveAt < 0f) { s.LeaveAt = now + s.NoticeDelay(); s.LeaveWhy = gone; }
                else if (now >= s.LeaveAt) { s.LeaveAt = -1f; DecideOnLeave(s, captorAlive ? captor : null, dist, s.LeaveWhy); }
                return;
            }
            if (s.LeaveAt >= 0f)
            {
                s.LeaveAt = -1f;
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: you came back before he reacted ({s.LeaveWhy})");
            }

            // Is he covered? A change reaches his decisions after his reaction time.
            bool debugOpening = now < s.DebugOpeningUntil;
            string kind; float strength; bool clear;
            bool covered = Reactions.Covered(captor, Me, seen, Settings.WatchAngle.Value, out kind, out strength, out clear);
            if (debugOpening) { covered = false; kind = "debug opening"; strength = 1f; clear = true; }

            NoticeApproach(s, dist);

            if (covered)
            {
                if (!s.LastCovered)
                {
                    float openFor = s.OpenSince >= 0f ? now - s.OpenSince : 0f;
                    s.LastCovered = true;
                    s.OpenSince = -1f;
                    s.DecideAt = -1f;
                    if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: covered again after {openFor:0.0} s");
                    // You look back at him after a real opening: a moment for a wiggle or a plea.
                    if (openFor >= 1f)
                    {
                        Gestures.Opportunity(s, "you look back at him");
                        Voice.Reply(s, VoiceEvent.Plead);
                    }
                }
                return;
            }

            if (s.LastCovered)
            {
                s.LastCovered = false;
                s.OpenSince = now;
                s.OpeningKind = kind;
                float delay = s.NoticeDelay() + (clear ? 0f : Settings.OpeningDelay.Value);
                s.DecideAt = now + delay;
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: opening ({kind}), he takes it in after {delay:0.0} s");
            }
            else if (kind != s.OpeningKind)
            {
                // It got clearer (you started reloading while looking away): noticed quickly.
                if (clear && strength > s.OpeningStrength)
                {
                    float soon = now + s.NoticeDelay();
                    s.DecideAt = s.DecideAt < 0f ? soon : Mathf.Min(s.DecideAt, soon);
                }
                s.OpeningKind = kind;
            }
            s.OpeningStrength = strength;
            if (s.DecideAt < 0f || now < s.DecideAt) return;
            s.DecideAt = now + Settings.BreakInterval.Value * Random.Range(0.7f, 1.3f);
            DecideOnOpening(s, captor, dist, debugOpening);
        }

        // He has noticed the opening: does he move now? Most of the time not. More likely
        // with nerve, a clear opening, his own gun close and away from you, squadmates near;
        // less when hurt; never while a warning shot or a fresh order still holds him.
        private void DecideOnOpening(SurrenderState s, Player captor, float dist, bool debug)
        {
            float now = Time.time;
            if (now < s.ComplyUntil)
            {
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: opening, but still complying for {s.ComplyUntil - now:0.0} s");
                return;
            }
            float scramble = ScrambleQuality(s, captor.Position, Perception.Eye(captor), out string scrambleWhy);
            int allies = Reactions.FreeAlliesNear(BotOwner, Me);
            float nerveScale = Mathf.Lerp(0.3f, 1f, s.Nerve);
            float squad = 1f + Settings.SquadBoost.Value * Mathf.Clamp01(allies / 2f);
            float hurt = 1f - 0.5f * s.Injury;
            float chance = Mathf.Clamp01(Settings.BreakChance.Value * nerveScale * s.OpeningStrength *
                                         (1f + Settings.OpportunityBoost.Value * scramble) * squad * hurt);
            float roll = Random.value;
            bool acts = roll < chance;
            string why = $"{s.OpeningKind} for {now - s.OpenSince:0.0} s";
            if (Settings.VerboseLogs.Value || debug || (acts && Settings.LogDecisions.Value))
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: opening ({why}, {dist:0} m{(debug ? ", debug" : "")}): nerve {s.Nerve:0.00}, " +
                    $"opening x{s.OpeningStrength:0.00}, own gun {scramble:0.00} ({scrambleWhy}), squad x{squad:0.00} ({allies} near), hurt x{hurt:0.00}; " +
                    $"chance {chance:0.00}, roll {roll:0.00} -> {(acts ? "acts" : "keeps still")}");
            if (!acts) return;
            TakeOpening(s, captor, dist, scramble, scrambleWhy, allies, why);
        }

        // You can only take what you can see: his dropped gun is "gone" once he can see the
        // spot and it isn't there. Until then he believes it is.
        private void NoticeGunGone(SurrenderState s, Player captor)
        {
            if (s.GunGone || s.DroppedWeapon == null) return;
            if (!Perception.LineClear(Perception.Eye(Me), s.DropPosition + Vector3.up * 0.3f)) return;
            if (Arsenal.StillThere(s.DroppedWeapon, s.DropPosition)) return;
            s.GunGone = true;
            bool you = captor != null && Vector3.Distance(captor.Position, s.DropPosition) < 3f;
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: sees his {s.DroppedWeapon.ShortName.Localized()} is gone{(you ? " (you took it)" : "")}");
            if (you) Voice.Reply(s, VoiceEvent.Plead);
            if (!s.LastCovered)
            {
                float soon = Time.time + s.NoticeDelay();
                s.DecideAt = s.DecideAt < 0f ? soon : Mathf.Min(s.DecideAt, soon);
            }
        }

        // You walking up to him: noticed per ApproachStep closed within 8 m; he may say
        // something, and a pending opening is weighed again soon (knife range may change).
        private void NoticeApproach(SurrenderState s, float dist)
        {
            if (s.ApproachRef < 0f || dist > s.ApproachRef) { s.ApproachRef = dist; return; }
            if (dist >= 8f || s.ApproachRef - dist < Settings.ApproachStep.Value) return;
            s.ApproachRef = dist;
            if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: you come closer ({dist:0.0} m)");
            Voice.Reply(s, VoiceEvent.Approached);
            if (!s.LastCovered)
            {
                float soon = Time.time + s.NoticeDelay();
                s.DecideAt = s.DecideAt < 0f ? soon : Mathf.Min(s.DecideAt, soon);
            }
        }

        // The captor is gone (walked off, out of sight, dead). What he does depends on what he
        // has and who he is: run, slip into cover, draw a gun he kept, or go for his own.
        private void DecideOnLeave(SurrenderState s, Player captor, float dist, string gone)
        {
            Gestures.Cancel(s, "you left");
            Vector3 threat = captor != null ? captor.Position : s.CaptorLastSeen;
            Vector3 threatEye = captor != null ? Perception.Eye(captor) : s.CaptorLastSeen + Vector3.up * 1.5f;
            bool seen = captor != null && Perception.LineClear(Perception.Eye(Me), threatEye);
            EquipmentSlot? gun = Arsenal.GunOnBody(Me);
            float nerve = s.Nerve;

            var options = new List<Choice>(4);
            var log = new StringBuilder($"{s.Name}: {gone}, reacts (nerve {nerve:0.00}{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}" +
                                        $"{(s.LegsBad ? ", legs hurt" : "")}): ");

            Add(options, log, "run", Settings.EscapeWeight.Value * 2f * s.Flee * (s.LegsBad ? 0.4f : 1f));

            if (gun == null) log.Append("draw: no gun on him; ");
            else Add(options, log, "draw " + Arsenal.SlotName(gun),
                Settings.DrawWeight.Value * Mathf.Max(0f, nerve - 0.25f) * s.Fight * (seen ? 0.5f : 1f));

            float scramble = ScrambleQuality(s, threat, threatEye, out string scrambleWhy);
            if (scramble <= 0f) log.Append($"his gun: {scrambleWhy}; ");
            else Add(options, log, "get his gun", Settings.ScrambleWeight.Value * scramble * (0.5f + nerve) * s.Fight);

            Add(options, log, "hide", Settings.HideWeight.Value * (1.3f - nerve) * s.Flee * (s.LegsBad ? 2f : 1f));

            Choice chosen = Pick(options, log);
            if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());

            if (chosen.Name == "get his gun")
            {
                StartScramble(s, threat, $"you left, {scrambleWhy}");
                return;
            }
            if (chosen.Name != null && chosen.Name.StartsWith("draw") && Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, $"draws the {Arsenal.SlotName(gun)} he kept ({gone})");
                return;
            }
            if (chosen.Name == "hide")
            {
                s.CaptorLastSeen = threat;
                BeginHide(s, threat, $"lies low ({gone})");
                return;
            }
            s.BeginRetreat(gone, emergency: false);
        }

        // Weighted random pick; logs the outcome. Name null if there were no options.
        private static Choice Pick(List<Choice> options, StringBuilder log)
        {
            if (options.Count == 0) { log.Append("-> nothing"); return default; }
            float total = 0f;
            foreach (Choice c in options) total += c.Score;
            float pick = Random.value * total;
            Choice chosen = options[options.Count - 1];
            float acc = 0f;
            foreach (Choice c in options)
            {
                acc += c.Score;
                if (pick <= acc) { chosen = c; break; }
            }
            log.Append($"-> {chosen.Name} (roll {pick:0.00} of {total:0.00})");
            return chosen;
        }

        // Arms down and into cover nearby, out of the captor's sight: a quieter way out than
        // running. Hiding then crouches, watches and decides again (Regrouping).
        private void BeginHide(SurrenderState s, Vector3 threat, string why)
        {
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.GraceUntil = Time.time + Settings.Grace.Value;
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.ReportAt = -1f;
            s.CaptorLastSeen = threat;
            GoHide(s, why);
        }

        // The game's callback normally clears DropPending within the hide animation. One
        // request at a time: while it is unanswered nothing new is asked (a second hands
        // change on top of a pending one is what leaves a bot with "busy hands"). After
        // DropRetrySeconds: a reload still running is cut short; after twice that with no
        // answer the request is abandoned (its callback ignored) and a new one made. After
        // MaxDropAttempts requests he fights instead of posing with a gun.
        private bool CheckDrop(SurrenderState s)
        {
            if (Arsenal.HandsEmpty(Me))
            {
                s.DropPending = false;
                s.HandsPending = false;
                return true;
            }
            float waited = Time.time - s.DropSince;
            float limit = Settings.DropRetrySeconds.Value;
            if (waited < limit) return false;

            if (s.HandsPending)
            {
                SurrenderRegistry.CutReload(s);
                if (Time.time - s.HandsSince < limit * 2f) return false;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: no answer to the hands change in {limit * 2f:0.0} s ({Arsenal.DescribeHands(Me)}), abandoning it");
                SurrenderRegistry.AbandonRequest(s);
            }

            if (s.DropAttempts >= MaxDropAttempts)
            {
                HandsAreHighPlugin.Log.LogWarning($"{s.Name}: couldn't empty his hands after {s.DropAttempts} requests ({Arsenal.DescribeHands(Me)}), fights instead");
                SurrenderRegistry.AbandonRequest(s);
                s.DropPending = false;
                s.LowerArms(Settings.EmergencyLowerSeconds.Value);
                s.SetPhase(Phase.Rearmed, "drop failed");
                return false;
            }
            s.DropSince = Time.time;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: hands still busy ({Arsenal.DescribeHands(Me)}), new request {s.DropAttempts + 1}");
            SurrenderRegistry.RequestDrop(s);
            return false;
        }

        // ---- taking an opening -----------------------------------------------------------

        // 0..1: how good a grab for his own dropped gun would be right now. Closer, reachable,
        // and the further you are from it (and the less you can see it), the better.
        // "Gone" is what he has seen (NoticeGunGone), not what is true: if he can't see the
        // spot he still believes it's there, and finds out when he gets there.
        private float ScrambleQuality(SurrenderState s, Vector3 captorPos, Vector3 captorEye, out string why, bool forced = false)
        {
            Item gun = s.DroppedWeapon;
            if (gun == null) { why = "he didn't drop a gun"; return 0f; }
            if (!forced && Time.time < s.ScrambleCooldownUntil) { why = "stopped once, not again yet"; return 0f; }
            if (!forced && s.Skip.Contains(gun)) { why = "failed to get it before"; return 0f; }
            if (s.GunGone && !forced) { why = "he saw it's gone"; return 0f; }
            float d = Vector3.Distance(Me.Position, s.DropPosition);
            float range = Settings.ScrambleRange.Value;
            if (d > range) { why = $"{d:0} m away, over {range:0} m"; return 0f; }
            if (ClaimedByOther(s, gun)) { why = "another held bot is going for it"; return 0f; }
            if (forced && !Arsenal.StillThere(gun, s.DropPosition)) { why = "it's gone"; return 0f; }
            if (!DropReachable(s)) { why = "no path to it"; return 0f; }

            float fromCaptor = Vector3.Distance(captorPos, s.DropPosition);
            float exposure = 1f - Mathf.Clamp01(fromCaptor / Settings.ExposureDistance.Value);
            if (!Perception.LineClear(captorEye, s.DropPosition + Vector3.up * 0.3f)) exposure *= 0.5f;
            float proximity = 1f - d / range;
            why = $"{d:0.0} m from him, you {fromCaptor:0.0} m from it";
            return Mathf.Clamp01(Mathf.Sqrt(proximity) * (1f - 0.7f * exposure));
        }

        // A path solve to his dropped gun, reused for 3 s while he stays within 0.5 m of where
        // it was solved from (he stands still with his hands up, so it nearly always is).
        private bool DropReachable(SurrenderState s)
        {
            Vector3 here = Me.Position;
            if (Time.time - s.DropReachableAt < 3f && (here - s.DropReachableFrom).sqrMagnitude < 0.25f) return s.DropReachable;
            s.DropReachable = Perception.Reachable(here, s.DropPosition);
            s.DropReachableAt = Time.time;
            s.DropReachableFrom = here;
            return s.DropReachable;
        }

        private static bool ClaimedByOther(SurrenderState s, Item gun)
        {
            foreach (SurrenderState other in SurrenderRegistry.All)
                if (other != s && other.TargetWeapon == gun) return true;
            return false;
        }

        private struct Choice
        {
            internal string Name;
            internal float Score;
        }

        // He takes his chance: every option he has is weighed, logged, and one is picked at
        // random in proportion to its weight. A gun he still carries beats a knife (slung gun
        // first, then sidearm); the knife is only for a man with no gun on him.
        // Situation decides which options exist and how they weigh: weapons on him, range,
        // his legs, wounds and squadmates, a reachable way out or a hidden spot near.
        private void TakeOpening(SurrenderState s, Player captor, float dist, float scramble, string scrambleWhy, int allies, string opening)
        {
            Gestures.Cancel(s, "takes his chance");   // a gesture never holds up a real action
            float fight = s.Fight, flee = s.Flee;
            float nerve = s.Nerve;
            EquipmentSlot? gun = Arsenal.GunOnBody(Me);
            float legs = s.LegsBad ? 0.35f : 1f;

            var options = new List<Choice>(5);
            var log = new StringBuilder();
            log.Append($"{s.Name} takes an opening ({opening}) at {dist:0.0} m (nerve {nerve:0.00}" +
                       $"{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}, hurt {s.Injury:0.00}{(s.LegsBad ? ", legs hurt" : "")}, " +
                       $"{allies} squadmate(s) near): ");

            if (gun == null) log.Append("draw: no gun on him; ");
            else if (dist > Settings.SidearmRange.Value) log.Append($"draw: you're beyond {Settings.SidearmRange.Value:0} m; ");
            else Add(options, log, "draw " + Arsenal.SlotName(gun),
                Settings.DrawWeight.Value * (0.4f + nerve) * fight * (allies > 0 ? 1.2f : 1f) * (1f - 0.4f * s.Injury));

            if (gun != null) log.Append("knife: has a gun to draw; ");
            else if (!Arsenal.HasMelee(Me)) log.Append("knife: none; ");
            else if (dist > Settings.KnifeRange.Value) log.Append($"knife: you're beyond {Settings.KnifeRange.Value:0} m; ");
            else Add(options, log, "knife", Settings.KnifeWeight.Value * (0.2f + nerve) * fight * (1f - 0.5f * s.Injury) * Mathf.Lerp(legs, 1f, 0.5f));

            if (scramble <= 0f) log.Append($"scramble: {scrambleWhy}; ");
            else Add(options, log, "scramble", Settings.ScrambleWeight.Value * scramble * (0.5f + nerve) * fight * (gun != null ? 0.5f : 1f) * legs);

            if (dist < Settings.EscapeMinDistance.Value) log.Append($"run: you're within {Settings.EscapeMinDistance.Value:0} m; ");
            else if (!Perception.RetreatPoint(Me, captor.Position, Settings.RetreatDistance.Value, false, out _)) log.Append("run: no way out; ");
            else Add(options, log, "run", Settings.EscapeWeight.Value * Mathf.Max(0.1f, 1.3f - nerve) * flee * legs);

            Vector3 hidePoint = Vector3.zero;
            if (dist < Settings.EscapeMinDistance.Value * 0.5f) log.Append("hide: you're right there; ");
            else if (!Perception.RetreatPoint(Me, captor.Position, Settings.CoverRadius.Value, true, out hidePoint)) log.Append("hide: no hidden spot near; ");
            else Add(options, log, "hide", Settings.HideWeight.Value * (1.3f - nerve) * flee * (s.LegsBad ? 1.5f : 1f));

            if (options.Count == 0)
            {
                HandsAreHighPlugin.Log.LogInfo(log.Append("-> nothing he can do, keeps his hands up").ToString());
                return;
            }
            Choice chosen = Pick(options, log);
            HandsAreHighPlugin.Log.LogInfo(log.ToString());

            if (chosen.Name == "knife")
            {
                if (Arsenal.DrawMelee(BotOwner)) Rearm(s, $"goes for his knife at {dist:0.0} m");
            }
            else if (chosen.Name == "scramble")
            {
                StartScramble(s, captor.Position, scrambleWhy);
            }
            else if (chosen.Name == "run")
            {
                s.BeginRetreat("bolts during an opening", emergency: true);
            }
            else if (chosen.Name == "hide")
            {
                BeginHide(s, captor.Position, "slips into cover during an opening");
                s.HideSearchPending = false;   // already found one
                s.TargetPoint = hidePoint;
                s.HideMoving = true;
            }
            else if (Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, $"draws his {Arsenal.SlotName(gun)} at {dist:0.0} m");
            }
            else
            {
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: tried to draw, the game refused the weapon change");
            }
        }

        private static void Add(List<Choice> options, StringBuilder log, string name, float score)
        {
            score = Mathf.Max(0f, score);
            log.Append($"{name} {score:0.00}; ");
            if (score > 0f) options.Add(new Choice { Name = name, Score = score });
        }

        // Arms down at once and a sprint for his own gun; the pick-up is the real transaction.
        private void StartScramble(SurrenderState s, Vector3 captorPos, string why)
        {
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.TargetWeapon = s.DroppedWeapon;
            s.TargetPoint = s.DropPosition;
            s.GraceUntil = Time.time + ScrambleGrace;
            s.CaptorLastSeen = captorPos;
            Voice.Say(s, VoiceEvent.Break);
            s.SetPhase(Phase.Recovering, $"scrambles for his {s.DroppedWeapon.ShortName.Localized()} ({why})");
            s.Scrambling = true;
        }

        // The scramble didn't work: he won't try it again straight away. With a gun still on
        // him and you close, he draws; otherwise he runs.
        private void FailScramble(SurrenderState s, string why)
        {
            if (s.TargetWeapon != null) s.Skip.Add(s.TargetWeapon);
            s.TargetWeapon = null;
            s.Scrambling = false;
            s.ScrambleCooldownUntil = Time.time + Settings.ScrambleCooldown.Value;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: scramble failed: {why}");
            if (Arsenal.GunOnBody(Me) != null && Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, "scramble failed, draws what he has");
                return;
            }
            s.BeginRetreat($"scramble failed ({why})", emergency: true);
        }

        private void Rearm(SurrenderState s, string reason)
        {
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.TargetWeapon = null;
            Voice.Say(s, VoiceEvent.Break);
            s.SetPhase(Phase.Rearmed, reason);
        }

        private void Follow(SurrenderState s, Player captor)
        {
            Vector3 to = captor.Position - Me.Position;
            to.y = 0f;
            float d = to.magnitude;
            float want = Settings.FollowDistance.Value;
            if (d <= want + Settings.FollowTolerance.Value)
            {
                Hold(Settings.Stance.Value);
                return;
            }
            Vector3 point = captor.Position - to.normalized * want;
            MoveTo(s, point, sprint: false);
        }

        // One item at a time, in order; a report once the last one should have landed.
        private void ProcessDropQueue(SurrenderState s)
        {
            if (s.DropQueue.Count > 0 && Time.time >= s.NextDropAt)
            {
                var next = s.DropQueue.Dequeue();
                s.NextDropAt = Time.time + Settings.DropItemDelay.Value;
                if (Arsenal.StillWorn(Me, next.Key, next.Value))
                    HandsAreHighPlugin.Try($"drop {next.Key}", () => Arsenal.Drop(Me, next.Value));
                if (s.DropQueue.Count == 0) s.ReportAt = Time.time + Settings.DropItemDelay.Value + 0.5f;
            }

            if (s.ReportAt > 0f && Time.time >= s.ReportAt)
            {
                s.ReportAt = -1f;
                int kept = 0;
                foreach (var kv in s.Ordered)
                    if (Arsenal.StillWorn(Me, kv.Key, kv.Value)) kept++;
                int done = s.Ordered.Count - kept;
                string msg = kept == 0
                    ? $"{s.Name} did it: {done} item(s) dropped"
                    : $"{s.Name} dropped {done} of {s.Ordered.Count}; {kept} wouldn't come off";
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: order '{s.OrderName}': {msg}");
                OrderMenu.Notify(msg);
                if (kept > 0) Voice.Say(s, VoiceEvent.Refuse);
            }
        }

        // ---- after the captor leaves ---------------------------------------------------

        private void Retreating(SurrenderState s, bool check)
        {
            if (s.NextMoveOrder == 0f)
            {
                // A route search is heavy: when other bots already used this frame's budget
                // he picks his route next frame (a frame's delay, no spike).
                if (!Perception.TakeHeavy()) return;
                if (!PickRetreat(s)) return;
            }

            if (!MoveTo(s, s.TargetPoint, sprint: true))
            {
                Retry(s, "no path");
                return;
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            if (Stalled(s))
            {
                Retry(s, "stuck");
                return;
            }
            if (Arrived(s.TargetPoint, Settings.ArrivalTolerance.Value))
            {
                Regroup(s, "got away");
                return;
            }
            if (Time.time - s.PhaseSince > Settings.RetreatTimeout.Value)
                Regroup(s, "ran out of time running");
        }

        private bool PickRetreat(SurrenderState s)
        {
            if (Perception.RetreatPoint(Me, s.CaptorLastSeen, Settings.RetreatDistance.Value, false, out s.TargetPoint))
                return true;
            GoHide(s, "no way out");
            return false;
        }

        // Stuck or blocked: a few fresh routes, then hide where he is.
        private void Retry(SurrenderState s, string why)
        {
            s.Retries++;
            if (s.Retries > Settings.PathRetries.Value)
            {
                GoHide(s, $"{why}, giving up running");
                return;
            }
            if (Settings.VerboseLogs.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {why}, new route ({s.Retries})");
            s.NextMoveOrder = 0f;   // Retreating picks a fresh route next frame
            ResetProgress(s);
        }

        private void Regroup(SurrenderState s, string why)
        {
            StopMoving();
            // Bold men catch their breath faster.
            s.RegroupUntil = Time.time + Settings.RegroupSeconds() * (1.3f - 0.6f * s.Nerve);
            s.SetPhase(Phase.Regrouping, why);
        }

        private void Regrouping(SurrenderState s, bool check)
        {
            Hold(0.6f);
            BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
            if (!check) return;
            if (SpottedAgain(s)) return;
            if (Time.time < s.RegroupUntil) return;

            Decide(s);
        }

        // How to get back in the fight, best option first.
        private void Decide(SurrenderState s)
        {
            EquipmentSlot? kept = Arsenal.GunOnBody(Me);
            if (kept != null && Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, $"draws the {Arsenal.SlotName(kept)} he kept");
                return;
            }

            // What he believes: unless he saw it gone, his gun is where he dropped it.
            if (Settings.RecoverOwnWeapon.Value && s.DroppedWeapon != null && !s.Skip.Contains(s.DroppedWeapon) &&
                Vector3.Distance(s.DropPosition, s.CaptorLastSeen) > Settings.AvoidRadius.Value &&
                !s.GunGone &&
                Perception.Reachable(Me.Position, s.DropPosition))
            {
                s.TargetWeapon = s.DroppedWeapon;
                s.TargetPoint = s.DropPosition;
                s.SetPhase(Phase.Recovering, "going back for his own gun");
                return;
            }

            // A 60 m sweep of loose guns and bodies is the heaviest thing he does: it waits for
            // a frame with budget, and after finding nothing he doesn't sweep again for 20 s
            // (a hiding bot looks around every 8 s; it used to sweep every time).
            if (Settings.ScavengeWeapons.Value && Time.time >= s.NextScavengeScan)
            {
                if (!Perception.TakeHeavy()) return;   // decides next check (0.25 s)
                Item found = Arsenal.FindWeapon(Me, s.CaptorLastSeen, Settings.ScavengeAvoid.Value,
                    Settings.ScavengeRadius.Value, s.Skip, out Vector3 at);
                if (found == null) s.NextScavengeScan = Time.time + 20f;
                if (found != null)
                {
                    if (Perception.Reachable(Me.Position, at))
                    {
                        s.TargetWeapon = found;
                        s.TargetPoint = at;
                        s.SetPhase(Phase.Recovering, $"going for {found.ShortName.Localized()} {Vector3.Distance(at, Me.Position):0} m away");
                        return;
                    }
                    s.Skip.Add(found);
                }
            }

            if (Settings.HighNerveKnife.Value && s.Nerve >= Settings.HighNerve.Value && Arsenal.HasMelee(Me) && Arsenal.DrawMelee(BotOwner))
            {
                Rearm(s, $"no gun to find, comes back with his knife (nerve {s.Nerve:0.00})");
                return;
            }

            s.TargetWeapon = null;
            GoHide(s, "nothing to fight with", quiet: s.Phase == Phase.Regrouping && s.HidCount++ > 0);
        }

        private void Recovering(SurrenderState s, bool check)
        {
            if (!MoveTo(s, s.TargetPoint, sprint: s.Scrambling))
            {
                if (s.Scrambling) FailScramble(s, "no path to it");
                else GiveUpOn(s, "no path to it");
                return;
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            // Someone else got there first.
            if (Time.time - s.PhaseSince > 0.5f && !Arsenal.StillThere(s.TargetWeapon, s.TargetPoint))
            {
                if (s.Scrambling) FailScramble(s, "it's gone");
                else GiveUpOn(s, "it's gone");
                return;
            }

            // Walking back for a gun (not a scramble): a player he can see standing by it
            // changes the plan.
            if (!s.Scrambling && PlayerNear(s.TargetPoint, Settings.AvoidRadius.Value * 0.5f, out float near))
            {
                GiveUpOn(s, $"a player he can see is {near:0} m from it");
                return;
            }

            if (Arrived(s.TargetPoint, Settings.PickupRange.Value))
            {
                StopMoving();
                Item weapon = s.TargetWeapon;
                bool scrambling = s.Scrambling;
                s.SetPhase(Phase.PickingUp, $"picking up {weapon.ShortName.Localized()}");
                s.Scrambling = scrambling;
                Arsenal.PickUp(Me, weapon, ok => HandsAreHighPlugin.Try("after pick-up", () => AfterPickUp(weapon, ok)));
                return;
            }
            if (Stalled(s))
            {
                if (s.Scrambling) FailScramble(s, "stuck on the way");
                else GiveUpOn(s, "stuck on the way");
                return;
            }
            float limit = s.Scrambling ? Settings.ScrambleTimeout.Value : Settings.RecoveryTimeout.Value;
            if (Time.time - s.PhaseSince > limit)
            {
                if (s.Scrambling) FailScramble(s, "took too long");
                else GiveUpOn(s, "took too long");
            }
        }

        private void AfterPickUp(Item weapon, bool ok)
        {
            if (!SurrenderRegistry.TryGet(Me, out SurrenderState s) || s.Phase != Phase.PickingUp || s.TargetWeapon != weapon) return;
            if (!ok)
            {
                if (s.Scrambling) FailScramble(s, "pick-up failed (no room, or taken)");
                else GiveUpOn(s, "pick-up failed (no room, or taken)");
                return;
            }
            if (!Arsenal.Carries(Me, weapon))
            {
                GiveUpOn(s, "pick-up reported success but the gun isn't on him");
                return;
            }
            if (Arsenal.DrawGun(BotOwner))
                Rearm(s, $"rearmed with {weapon.ShortName.Localized()}{(s.Scrambling ? " after a scramble" : "")}");
            else
                GiveUpOn(s, "has the gun but couldn't draw it");
        }

        private bool PlayerNear(Vector3 point, float radius, out float distance)
        {
            distance = 0f;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return false;
            Vector3 eye = Perception.Eye(Me);
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p.IsAI) continue;
                float d = Vector3.Distance(p.Position, point);
                if (d > radius || !Perception.LineClear(eye, Perception.Eye(p))) continue;
                distance = d;
                return true;
            }
            return false;
        }

        private void PickingUp(SurrenderState s)
        {
            Hold(Settings.Stance.Value);
            if (Time.time - s.PhaseSince > Settings.PickupTimeout.Value)
            {
                if (s.Scrambling) FailScramble(s, "pick-up never finished");
                else GiveUpOn(s, "pick-up never finished");
            }
        }

        private void GiveUpOn(SurrenderState s, string why)
        {
            if (s.TargetWeapon != null) s.Skip.Add(s.TargetWeapon);
            s.TargetWeapon = null;
            StopMoving();
            s.RegroupUntil = Time.time + 1f;
            s.SetPhase(Phase.Regrouping, $"gave up on the gun: {why}");
        }

        // Nothing to fight with: gets out of sight of where he last saw you, stays low,
        // and looks again every few seconds.
        private void GoHide(SurrenderState s, string why, bool quiet = false)
        {
            StopMoving();
            s.SetPhase(Phase.Hiding, why, quiet);
            // The cover search (up to PathCandidates path solves) runs from Hiding on a frame
            // with heavy-search budget, usually this one or the next.
            s.HideSearchPending = Perception.CanSee(Me, s.CaptorLastSeen);
        }

        private void Hiding(SurrenderState s, bool check)
        {
            if (s.HideSearchPending && Perception.TakeHeavy())
            {
                s.HideSearchPending = false;
                if (Perception.RetreatPoint(Me, s.CaptorLastSeen, Settings.CoverRadius.Value, true, out s.TargetPoint))
                    s.HideMoving = true;
            }
            if (s.HideMoving)
            {
                if (!MoveTo(s, s.TargetPoint, sprint: false)) s.HideMoving = false;
            }
            else
            {
                Hold(0f);
                BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            if (s.HideMoving && (Arrived(s.TargetPoint, Settings.ArrivalTolerance.Value) || Stalled(s)))
            {
                s.HideMoving = false;
                StopMoving();
            }
            if (Time.time - s.PhaseSince > Settings.HideRecheck.Value)
            {
                s.RegroupUntil = Time.time;
                // Decide comes back to Hiding if there's still nothing; logged once, not every 8 s.
                s.SetPhase(Phase.Regrouping, "looks again", quiet: true);
            }
        }

        // A human player close by aims straight at him for a moment: hands go up again.
        // With a gun still on him, a bold bot may draw instead.
        private bool SpottedAgain(SurrenderState s)
        {
            if (Time.time < s.GraceUntil) return false;
            // A debug opening simulates you not covering him: your aim doesn't count until it ends.
            if (Time.time < s.DebugOpeningUntil) { s.AimedBy = null; return false; }
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null) return false;

            float range = Settings.ResurrenderDistance.Value;
            Player aiming = null;
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p.IsAI) continue;
                if ((p.Position - Me.Position).sqrMagnitude > range * range) continue;
                if (!Perception.Covering(p, Me, Settings.AimTolerance.Value)) continue;
                aiming = p;
                break;
            }

            if (aiming == null) { s.AimedBy = null; return false; }
            if (s.AimedBy != aiming) { s.AimedBy = aiming; s.AimedSince = Time.time; }
            // Mid-scramble he's committed: it takes a steadier aim (hysteresis) to stop him.
            float confirm = s.Scrambling ? Mathf.Max(0.8f, Settings.AimConfirm.Value * 2f) : Settings.AimConfirm.Value;
            confirm *= Mathf.Clamp(s.Reaction / 0.6f, 0.6f, 1.5f);   // his own reaction time
            if (Time.time - s.AimedSince < confirm) return false;

            s.AimedBy = null;
            if (Arsenal.GunOnBody(Me) != null)
            {
                float chance = Mathf.Clamp01(Settings.RecaptureResistance.Value * s.Nerve);
                float roll = Random.value;
                if (roll < chance && Arsenal.DrawGun(BotOwner))
                {
                    Rearm(s, $"caught again but draws (chance {chance:0.00}, roll {roll:0.00})");
                    return true;
                }
            }
            s.HidCount = 0;
            SurrenderRegistry.Surrender(Me, aiming, s.Scrambling ? "caught mid-scramble" : "caught again");
            return true;
        }

        // ---- movement ------------------------------------------------------------------

        // Standing still: the mover is stopped once (not every frame), the pose only set when
        // it changes. Repeating Stop/SetPose every frame restarts the mover's and the pose's
        // transitions and shows as twitching.
        private void Hold(float pose)
        {
            SurrenderState s = _s;
            if (s != null && s.Moving)
            {
                BotOwner.StopMove();
                BotOwner.SetTargetMoveSpeed(0f);
                s.Moving = false;
            }
            SetPose(pose);
        }

        private void StopMoving()
        {
            BotOwner.StopMove();
            if (_s != null) _s.Moving = false;
        }

        private void SetPose(float pose)
        {
            if (Mathf.Abs(pose - _pose) < 0.01f) return;
            _pose = pose;
            BotOwner.SetPose(pose);
        }

        // Facing the captor: the look point only moves when he has moved more than
        // FaceHysteresis degrees around the bot, and it's his body (not his bobbing head bone),
        // so the turn is one smooth steering target instead of a new one every frame.
        private void Face(SurrenderState s, Vector3 point)
        {
            Vector3 me = Me.Position;
            Vector3 want = point - me; want.y = 0f;
            Vector3 have = s.FaceTarget - me; have.y = 0f;
            if (!s.FaceSet || have.sqrMagnitude < 0.01f || Vector3.Angle(have, want) > Settings.FaceHysteresis.Value)
            {
                s.FaceTarget = point;
                s.FaceSet = true;
            }
            BotOwner.Steering.LookToPoint(s.FaceTarget);
        }

        // Follows a NavMesh path we computed (BotMover.GoToByWay). The path is only re-issued
        // when the destination moves (following you), or every few seconds as a fallback,
        // never every frame: each GoToByWay restarts the mover on the new path.
        // False if there's no complete way there. Never BotOwner.GoToPoint (see Perception.PathTo).
        private bool MoveTo(SurrenderState s, Vector3 point, bool sprint)
        {
            SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            bool first = s.NextMoveOrder == 0f || !s.Moving;
            bool moved = (point - s.PathTarget).sqrMagnitude > 1f;
            if (!first && !moved && Time.time < s.NextMoveOrder) return true;
            // A moving destination (following you) re-paths at most twice a second.
            if (!first && moved && Time.time - (s.NextMoveOrder - RepathInterval) < 0.5f) return true;
            s.NextMoveOrder = Time.time + RepathInterval;
            if (first) ResetProgress(s);

            if (!Perception.PathTo(Me.Position, point, out Vector3[] corners, out string why))
            {
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: no route ({why})");
                return false;
            }
            if (first) Relink();
            s.PathTarget = point;
            s.Moving = true;
            BotOwner.MovementResume();
            BotOwner.Mover.GoToByWay(corners, ReachDist);
            BotOwner.Sprint(sprint);
            return true;
        }

        // Before he starts walking, the mover's record of his last good NavMesh position is set
        // to where he stands now. Left stale (it is not updated while he stands with his hands
        // up, nor while SAIN drove him), BotMover.method_12 snaps a walking bot back to it as
        // soon as he is 0.3 m away from it, which can put him on the far side of a wall.
        // The same fields SAINLayer.OnLayerChanged resets when it hands a bot over; no
        // SetPlayerToNavMesh, which can teleport (FindBetterPosition / Teleport).
        private void Relink()
        {
            BotMover mover = BotOwner.Mover;
            if (mover == null) return;
            Vector3 here = Me.Position;
            mover.PositionOnWayInner = here;
            mover._prevSuccessLinkedFrom = here;
            mover._prevLinkPos = here;
            mover._lastGoodCastPoint = here;
            mover._lastGoodCastPointTime = Time.time;
            mover._prevPosLinkedTime = 0f;
        }

        private void ResetProgress(SurrenderState s)
        {
            s.ProgressFrom = Me.Position;
            s.ProgressSince = Time.time;
        }

        // No real progress for a while: blocked by a door, a wall or another body.
        private bool Stalled(SurrenderState s)
        {
            float moved = (Me.Position - s.ProgressFrom).magnitude;
            if (moved >= Settings.ProgressMeters.Value)
            {
                ResetProgress(s);
                return false;
            }
            return Time.time - s.ProgressSince > Settings.StallSeconds.Value;
        }

        private bool Arrived(Vector3 point, float within)
        {
            Vector3 d = point - Me.Position;
            d.y = 0f;
            return d.magnitude <= within;
        }

        public override void BuildDebugText(StringBuilder sb)
        {
            if (SurrenderRegistry.TryGet(Me, out SurrenderState s))
                sb.AppendLine($"HandsAreHigh: {s.Phase}, order {s.Order}, arms {s.Weight:0.00}, nerve {s.Nerve:0.00}");
        }
    }
}
