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
        private const float PivotSpeed = 140f;        // deg/s: turning on his planted feet at the end of a rise
        private const float FirstStepWindow = 0.6f;   // s after his legs are free: an escape can still be called off

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
            // A decision or a leave reaction that comes due between checks runs on that frame
            // (once per due time), not up to a check interval later.
            bool check = Time.time >= s.NextCheck || DueNow(s);
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

            // An escape from his knees, and his captor covers him again before he's away.
            if (s.KneelEscape && check && (s.Phase == Phase.Retreating || s.Phase == Phase.Hiding || s.Phase == Phase.Recovering) && ReassessEscape(s))
                return;

            switch (s.Phase)
            {
                case Phase.HandsUp: HandsUp(s, check); break;
                case Phase.Retreating: Retreating(s, check); break;
                case Phase.Regrouping: Regrouping(s, check); break;
                case Phase.Recovering: Recovering(s, check); break;
                case Phase.PickingUp: PickingUp(s); break;
                case Phase.Hiding: Hiding(s, check); break;
                case Phase.Rearmed: Rearmed(s); break;
            }
            KeepLookOnBody(s);
        }

        // 0.15.0, the twist at its source (verified, 0.16.9 MovementContext / MovementState):
        // a bot's look yaw (Player.Rotation, steered by BotSteering every fixed update towards the
        // last LookToPoint) and his root's yaw differ by HandsToBodyAngle; MovementContext feeds
        // that to the body animator (PlayerAnimator.SetDeltaRotation), which turns the animated
        // trunk towards the look by up to TrunkRotationLimit (EFTHardSettings
        // HANDS_TO_BODY_MAX_ANGLE, 45 deg); past it MovementState.ProcessUpperbodyRotation turns
        // the root itself (at once for AI). While he knelt, the last look point (where his captor
        // stood when he went down) stayed his steering target: the animated chest stayed twisted up
        // to 45 deg against his planted hips, our torso look added up to 35 more on top, and his
        // root could even swing past the knees. So while any part of the kneel has his legs, his
        // look is kept on the way his knees point: HandsToBodyAngle ~0, the animator adds no trunk
        // twist, and the root never turns on his planted knees. Where he looks is the head's job
        // (Presence), within its limits. The pivot at the end of a rise turns him on purpose.
        private void KeepLookOnBody(SurrenderState s)
        {
            if (Kneel.Off(KneelPoseIsolate.LookAlign)) return;
            if (!Kneel.Active(s) || Kneel.PivotOpen(s)) return;
            Vector3 a = s.KneelAnchorFwd; a.y = 0f;
            if (a.sqrMagnitude < 1e-4f) return;
            BotOwner.Steering.LookToDirection(a.normalized, 360f);
        }

        private static bool DueNow(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return false;
            float now = Time.time;
            if (s.DecideAt >= 0f && now >= s.DecideAt && s.RanDecideAt != s.DecideAt) { s.RanDecideAt = s.DecideAt; return true; }
            if (s.LeaveAt >= 0f && now >= s.LeaveAt && s.RanLeaveAt != s.LeaveAt) { s.RanLeaveAt = s.LeaveAt; return true; }
            return false;
        }

        // Drawing: standing, turned to the man he's drawing on, arms already coming down while
        // the game's draw animation plays. He doesn't fire from here: the layer hands him to the
        // normal AI once the weapon is ready (SurrenderRegistry.Tick / DrawSettled), so his
        // first shot comes from the game's own aim and shoot logic with a ready weapon.
        private void Rearmed(SurrenderState s)
        {
            Hold(1f);
            if (!s.DrawPending) return;
            if (Kneel.LegsLocked(s))
            {
                // Turns on his planted feet once his knees are off the ground, the rest after.
                if (Kneel.PivotOpen(s)) BotOwner.Steering.LookToDirection(Kneel.PivotDir(s, ThreatPos(s, s.Captor) - Me.Position), PivotSpeed);
                return;
            }
            Face(s, ThreatPos(s, s.Captor) + Vector3.up * 1.3f);
        }

        // ---- hands up ------------------------------------------------------------------

        private void HandsUp(SurrenderState s, bool check)
        {
            Player captor = s.Captor;
            bool captorAlive = Perception.Alive(captor);

            switch (s.Order)
            {
                // Kneeling (Kneel.cs) owns the pose while it runs; its fallback (logged) is the
                // crouch with hands up that 0.10.0 and earlier used for this order.
                case Order.Kneel:
                    // No room where he stands: a short walk to a spot that has it (Kneel.SearchSpot),
                    // hands up, no sprint, still facing his captor. Captors and observers see a
                    // man with his hands up (Phase HandsUp), as for Follow: no escape.
                    if (Kneel.SpotWalking(s))
                    {
                        if (!MoveTo(s, s.KneelSpot, sprint: false)) Kneel.SpotFailed(s, "no path from here");
                    }
                    else Hold(Kneel.HoldPose(s));
                    break;
                case Order.Follow when captorAlive: Follow(s, captor); break;
                default: Hold(Settings.Stance.Value); break;
            }
            // He faces the captor he sees; otherwise where he last knew him to be. Not while any
            // part of a kneel is on: turning the body would slide his knees over the ground.
            // 0.15.0: about to kneel, he turns to the direction fixed when he took the order (not
            // to wherever his captor has walked since); Kneel turns his root to it (the game turns a
            // bot's root only once his look is 45 deg off, so a look alone never settled him).
            // 0.16.0: told to turn around, he faces the way he was told (Facing): the captor-facing
            // below would undo the order at once.
            // 0.17.0: his body is turned by TurnDriver (Facing.Update: the game's stepping turn for
            // big turns, a pivot for small ones); the look it needs is applied here. The captor-facing
            // target is only updated (FaceTarget): looking straight at a man 120 deg round would make
            // the game snap his root (or twist his trunk) instead of stepping round.
            bool facing = Settings.FaceCaptor.Value && captorAlive && !Kneel.Active(s);
            if (facing) AimAt(s, ThreatPos(s, captor) + Vector3.up * 1.6f);
            if (Facing.Look(s, BotOwner)) { }
            else if (Kneel.PreTurn(s, out Vector3 kneelDir)) BotOwner.Steering.LookToDirection(kneelDir, 360f);
            else if (facing) BotOwner.Steering.LookToPoint(s.FaceTarget);

            ProcessDropQueue(s);
            if (!check) return;

            if (s.DropPending && !CheckDrop(s)) return;

            // Deterministic debug commands (this bot only).
            if (s.ForceRun)
            {
                s.ForceRun = false;
                s.BeginRetreat("debug: forced run", emergency: true);
                MarkKneelEscape(s, "run");
                return;
            }
            if (s.ForceGrab)
            {
                s.ForceGrab = false;
                if (!captorAlive) { HandsAreHighPlugin.Log.LogInfo($"{s.Name}: forced grab: no captor"); return; }
                float q = ScrambleQuality(s, captor.Position, Perception.Eye(captor), out string qWhy, forced: true);
                if (q > 0f) { StartScramble(s, captor.Position, $"debug: forced, {qWhy}"); MarkKneelEscape(s, "scramble"); }
                else HandsAreHighPlugin.Log.LogInfo($"{s.Name}: forced grab not possible: {qWhy}");
                return;
            }
            // 0.16.0: told again to hand over what he kept, a bold man went for it instead
            // (Compliance): the real draw (or knife), after his own reaction time; the readiness
            // checks and the hand-off to the AI are the same as for any draw.
            if (s.EscalateAt > 0f && Time.time >= s.EscalateAt)
            {
                s.EscalateAt = 0f;
                bool knife = Arsenal.GunOnBody(Me) == null && Arsenal.HasMelee(Me);
                if (knife ? Arsenal.Draw(s, BotOwner, true) : Arsenal.Draw(s, BotOwner, false))
                    Rearm(s, $"refuses to hand over his {(knife ? "knife" : Arsenal.SlotName(Arsenal.GunOnBody(Me)))} and goes for it");
                else if (!knife && Arsenal.GunOnBody(Me) != null) CommitDraw(s, Arsenal.SlotName(Arsenal.GunOnBody(Me)));
                else HandsAreHighPlugin.Log.LogInfo($"{s.Name}: was going for what he kept, but it isn't on him any more; keeps his hands up");
                return;
            }
            if (s.ForceDraw)
            {
                // Debug (kneel interruption test): the draw an opening would make, without the roll.
                s.ForceDraw = false;
                EquipmentSlot? kept = Arsenal.GunOnBody(Me);
                if (kept == null) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: forced draw: no gun on him");
                else if (s.DrawPending) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: forced draw: already drawing");
                else if (Arsenal.Draw(s, BotOwner, false)) Rearm(s, $"debug: forced draw of his {Arsenal.SlotName(kept)}");
                else CommitDraw(s, Arsenal.SlotName(kept));
                return;
            }

            // Shot while his hands are up, by anyone: he runs. (A hit, not health: a bleeding
            // man loses health every second without anyone firing.)
            bool hit = s.WasHit;
            s.WasHit = false;
            if (hit) Gestures.Cancel(s, "hit");
            // 0.19.0: hit in the first moments after giving up, by a shooter who can't have seen the
            // hands go up yet (a natural surrender happens mid-firefight: the next rounds are already
            // on their way). Running from that made almost every natural surrender to a player look
            // like an ordinary retreat. He stays down and pleads; a later hit still makes him run.
            if (hit && s.TStart > 0f && Time.realtimeSinceStartup - s.TStart < Settings.HitGrace.Value)
            {
                SurrenderStats.HitGrace();
                if (Settings.LogDecisions.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: hit {Time.realtimeSinceStartup - s.TStart:0.0} s after giving up (by {Restraint.Name(s.LastHitBy)}, who can't have seen his hands go up yet): stays down");
                Voice.Say(s, VoiceEvent.Hurt);
                hit = false;
            }
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

            // He has decided to draw and the game refused (hands still busy): the same draw is
            // tried again every check for a moment, without a new decision or a new roll.
            if (s.DrawCommitUntil > 0f)
            {
                if (Time.time > s.DrawCommitUntil || Arsenal.GunOnBody(Me) == null)
                {
                    s.DrawCommitUntil = 0f;
                    s.TDecided = -1f;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: couldn't get the draw through ({Arsenal.DescribeHands(Me)}), keeps his hands up");
                }
                else
                {
                    if (Arsenal.Draw(s, BotOwner, false))
                    {
                        s.DrawCommitUntil = 0f;
                        Rearm(s, $"draws his {Arsenal.SlotName(s.DrawSlot)} (second try)");
                    }
                    return;
                }
            }

            float now = Time.time;
            if (now >= s.NextSlowCheck)
            {
                s.NextSlowCheck = now + 1f;
                Reactions.RefreshBody(s);
                NoticeGunGone(s, captorAlive ? captor : null);
            }

            // What he can see of the captor (a clear line; beyond close range the game's own sight
            // of him). Out of sight, he only knows where he last saw him.
            float dist = captorAlive ? Vector3.Distance(captor.Position, Me.Position) : float.MaxValue;
            bool seen = captorAlive && Perception.Sees(Me, captor, dist);
            s.CaptorSeen = seen;
            // 0.15.0: in view = within what his eyes can reach from his planted body (kneeling).
            // A captor behind a kneeling man is still there (a clear line, close: heard, so no
            // "captor gone"), but not watched: where he stands is only updated coarsely when he
            // walks (footsteps), and where his gun points is unknown (Reactions.Covered).
            bool inView = seen && Presence.InView(s, Perception.Eye(captor));
            if (inView != s.CaptorInView)
            {
                if (Settings.LogDecisions.Value || Settings.LogAttention.Value)
                    HandsAreHighPlugin.Log.LogInfo(inView
                        ? $"{s.Name}: has {Restraint.Name(captor)} in view again"
                        : $"{s.Name}: {Restraint.Name(captor)} is behind him, out of view ({dist:0.0} m); knows he's there, can't see what he does");
            }
            s.CaptorInView = inView;
            string gone = null;
            if (!captorAlive) gone = "captor is dead";
            else if (seen)
            {
                if (inView || (captor.Position - s.CaptorLastSeen).sqrMagnitude > 1.5f * 1.5f) s.CaptorLastSeen = captor.Position;
                s.CaptorHiddenSince = -1f;
                // Closing in on an unanswered surrender keeps it open a little longer.
                if (!s.Accepted && dist <= s.OfferRef - 3f)
                {
                    if (s.OfferRef < float.MaxValue) s.OfferUntil = Mathf.Max(s.OfferUntil, now + 8f);
                    s.OfferRef = dist;
                }
                if (dist < s.ClosestDist) s.ClosestDist = dist;
                if (!s.Accepted && !captor.IsAI && dist <= Settings.AcknowledgeDistance.Value) s.Accept($"came within {dist:0} m");
                // Leaving is walking away from where he was, not being far: a surrender made at
                // 150 m is not cancelled for being at 150 m (0.9.0; the old rule was a flat 25 m).
                float walkAway = Mathf.Max(Settings.LeaveDistance.Value, s.ClosestDist + Settings.WalkAwayMargin.Value);
                if (dist > walkAway) gone = $"captor walked away ({dist:0} m, closest {s.ClosestDist:0} m)";
            }
            else
            {
                if (s.CaptorHiddenSince < 0f) s.CaptorHiddenSince = now;
                // An unanswered surrender to a man he can't see (pinned behind cover, an unseen
                // shooter) stands a little longer, so it can be seen; the offer timer still ends it.
                float hiddenLimit = s.Accepted ? Settings.LeaveSeconds.Value : 2.5f * Settings.LeaveSeconds.Value;
                if (now - s.CaptorHiddenSince > hiddenLimit) gone = $"captor out of sight {hiddenLimit:0} s";
            }
            // An unanswered surrender does not hold him forever: nobody acknowledged it (no human
            // came close or aimed at him, no bot took custody) by OfferUntil, so he seeks safety.
            if (gone == null && !s.Accepted && s.Custody == null && now >= s.OfferUntil && now >= s.ComplyUntil)
                gone = captor.IsAI ? $"{Restraint.Name(captor)} never took him prisoner" : "nobody came for his surrender";

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
            bool covered = Reactions.Covered(s, captor, Me, seen, inView, Settings.WatchAngle.Value, out kind, out strength, out clear);
            if (debugOpening) { covered = false; kind = "debug opening"; strength = 1f; clear = true; }

            // A human captor holding him at gunpoint for a moment has acknowledged the surrender.
            if (covered && !captor.IsAI && !s.Accepted)
            {
                if (s.CoveredSince < 0f) s.CoveredSince = now;
                else if (now - s.CoveredSince >= Mathf.Max(0.8f, 2f * Settings.AimConfirm.Value)) s.Accept($"aims at him from {dist:0} m");
            }
            else s.CoveredSince = -1f;

            if (seen) NoticeApproach(s, dist);

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
            s.DecideAt = now + Settings.BreakInterval.Value * Dice.Range(0.7f, 1.3f);
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
            float scramble = ScrambleQuality(s, ThreatPos(s, captor), ThreatEye(s, captor), out string scrambleWhy);
            int allies = Reactions.FreeAlliesNear(BotOwner, Me);
            float nerveScale = Mathf.Lerp(0.3f, 1f, s.Nerve);
            float squad = 1f + Settings.SquadBoost.Value * Mathf.Clamp01(allies / 2f);
            float hurt = 1f - 0.5f * s.Injury;
            float closeK = CloseQuarters(s, captor, dist, out string closeWhy);
            float poseK = PostureFactor(s, out string poseWhy);
            float fire = Mathf.Clamp01(Suppression.Decayed(SurrenderRegistry.HistoryOf(Me), now) / Mathf.Max(1f, Settings.SustainedFireEvents.Value));
            float fireK = 1f - 0.4f * fire;
            float pickK = PickupWindow(s, captor, out string pickWhy);
            float chance = Mathf.Clamp01(Settings.BreakChance.Value * nerveScale * s.OpeningStrength *
                                         (1f + Settings.OpportunityBoost.Value * scramble) * squad * hurt * closeK * poseK * fireK * pickK);
            float roll = Dice.Value;
            bool acts = roll < chance;
            string why = $"{s.OpeningKind} for {now - s.OpenSince:0.0} s";
            if (Settings.VerboseLogs.Value || debug || Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: opening ({why}, {dist:0.0} m{(debug ? ", debug" : "")}): nerve {s.Nerve:0.00}, " +
                    $"opening x{s.OpeningStrength:0.00}, own gun {scramble:0.00} ({scrambleWhy}), squad x{squad:0.00} ({allies} near), hurt x{hurt:0.00}, " +
                    $"range x{closeK:0.00} ({closeWhy}), posture x{poseK:0.00} ({poseWhy}), fire x{fireK:0.00}{(pickWhy != null ? $", pick-up window x{pickK:0.00} ({pickWhy})" : "")}; " +
                    $"chance {chance:0.00}, roll {roll:0.00} -> {(acts ? "acts" : "keeps still")}");
            if (!acts) return;
            TakeOpening(s, captor, dist, scramble, scrambleWhy, allies, why);
        }

        // 0.18.0: his captor bending for (or walking to) the gear he made him drop: his gun is off
        // him and his hands are busy. A man with a weapon still on him (one he kept back, or one the
        // captor never asked for) weighs it more, by his own fight; one with nothing on him less
        // (his only chance is the gun the captor is reaching for). Only what the prisoner can see:
        // the captor's own covering squadmates already closed the opening (Reactions.Covered).
        private float PickupWindow(SurrenderState s, Player captor, out string why)
        {
            why = null;
            if (captor == null || !captor.IsAI || !CaptorLoot.Busy(captor, out string busy)) return 1f;
            bool armed = Arsenal.GunOnBody(Me) != null || Arsenal.HasMelee(Me);
            float nature = Mathf.Lerp(0.8f, 1.3f, Mathf.InverseLerp(0.6f, 1.6f, s.Fight));
            float k = (armed ? 1.6f : 0.8f) * nature;
            why = $"his captor {busy}, {(armed ? "a weapon still on him" : "nothing on him")}, fight x{nature:0.00}";
            return k;
        }

        // 0.15.0: how close the man covering him is. Within arm's reach plus a step (2.5 m) a man
        // with a knife or a gun still on him has a real chance to lunge or draw before the captor
        // reacts; one with nothing has only a closer gun pointing at him. A captor walking up is
        // weighed like any other: closer is both danger and opportunity.
        private float CloseQuarters(SurrenderState s, Player captor, float dist, out string why)
        {
            bool kept = Arsenal.GunOnBody(Me) != null || Arsenal.HasMelee(Me);
            bool moving = captor != null && s.CaptorInView && captor.Velocity.sqrMagnitude > 0.5f * 0.5f;
            string mv = moving ? ", he's walking up" : "";
            if (dist <= 2.5f) { why = kept ? $"within a lunge, armed{mv}" : $"within a lunge, nothing on him{mv}"; return kept ? 1.3f : 0.75f; }
            if (dist <= 5f) { why = kept ? $"close, armed{mv}" : $"close{mv}"; return kept ? 1.1f : 0.9f; }
            why = $"{dist:0} m{mv}";
            return 1f;
        }

        // 0.15.0: what his body is doing. On his knees he must get up first (the rise takes time,
        // in sight); halfway down or up he is off balance; standing still is the easiest.
        private static float PostureFactor(SurrenderState s, out string why)
        {
            switch (s.Kneel)
            {
                case KneelPhase.Down: why = "kneeling, must get up first"; return 0.75f;
                case KneelPhase.GoingDown: why = "getting down, off balance"; return 0.5f;
                case KneelPhase.GettingUp: why = "getting up"; return 0.6f;
            }
            why = "standing";
            return 1f;
        }

        // You can only take what you can see: his dropped gun is "gone" once he can see the
        // spot and it isn't there. Until then he believes it is.
        private void NoticeGunGone(SurrenderState s, Player captor)
        {
            if (s.GunGone || s.DroppedWeapon == null) return;
            if (!Perception.LineClear(Perception.Eye(Me), s.DropPosition + Vector3.up * 0.3f)) return;
            if (Arsenal.StillThere(s.DroppedWeapon, s.DropPosition)) return;
            s.GunGone = true;
            bool you = captor != null && s.CaptorSeen && Vector3.Distance(captor.Position, s.DropPosition) < 3f;
            // 0.19.0: "you" was printed for bot captors too.
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: sees his {s.DroppedWeapon.ShortName.Localized()} is gone{(you ? (captor.IsAI ? $" ({Restraint.Name(captor)} took it)" : " (you took it)") : "")}");
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
            // Only what he knows: the captor's position if he sees him, else his last sighting.
            Vector3 threat = ThreatPos(s, captor);
            Vector3 threatEye = ThreatEye(s, captor);
            bool seen = captor != null && s.CaptorSeen;
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
            float reach = EasyReach(s, out string reachWhy);
            if (scramble <= 0f) log.Append($"his gun: {scrambleWhy}; ");
            else Add(options, log, $"get his gun{reachWhy}", Settings.ScrambleWeight.Value * scramble * reach * (0.5f + nerve) * s.Fight);

            Add(options, log, "hide", Settings.HideWeight.Value * (1.3f - nerve) * s.Flee * (s.LegsBad ? 2f : 1f));

            Choice chosen = Pick(options, log);
            if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());

            if (chosen.Name != null && chosen.Name.StartsWith("get his gun"))
            {
                StartScramble(s, threat, $"you left, {scrambleWhy}");
                return;
            }
            if (chosen.Name != null && chosen.Name.StartsWith("draw") && Arsenal.Draw(s, BotOwner, false))
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
            float pick = Dice.Value * total;
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
            s.MarkFlight();   // 0.17.0: slipping into cover is leaving captivity too (no going back for the gun)
            Kneel.Urge(s, KneelRise.Escape);
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.GraceUntil = Time.time + Settings.Grace.Value;
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.ReportAt = -1f;
            s.CaptorLastSeen = threat;
            GoHide(s, why);
            s.HideUrgent = true;   // breaking away into cover: he hurries there
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
            // Another of them standing over it (a squadmate who holds fire at him and can see
            // the spot from close by) makes the grab as exposed as the captor being near it.
            string watcher = null;
            if (WatchedBySquad(s, out string wName)) { exposure = Mathf.Max(exposure, 0.6f); watcher = wName; }
            float proximity = 1f - d / range;
            why = $"{d:0.0} m from him, you {fromCaptor:0.0} m from it{(watcher != null ? $", {watcher} watches it" : "")}";
            return Mathf.Clamp01(Mathf.Sqrt(proximity) * (1f - 0.7f * exposure));
        }

        private float EasyReach(SurrenderState s, out string why)
        {
            why = "";
            if (s.DroppedWeapon == null) return 1f;
            float d = Vector3.Distance(Me.Position, s.DropPosition);
            if (d <= 3f) { why = " (within reach)"; return 2.5f; }
            if (d <= 5f) { why = " (close)"; return 1.6f; }
            return 1f;
        }

        // Up to three of the bots who decided about him (his captor's squad and other observers),
        // alive, within 25 m of his gun, with a clear line to it.
        private bool WatchedBySquad(SurrenderState s, out string who)
        {
            who = null;
            var map = Restraint.EntriesFor(Me);
            if (map == null || map.Count == 0) return false;
            Vector3 spot = s.DropPosition + Vector3.up * 0.3f;
            int tries = 0;
            foreach (var kv in map)
            {
                Player o = kv.Key;
                if (o == null || o == s.Captor || !Perception.Alive(o) || !kv.Value.Holds) continue;
                if ((o.Position - s.DropPosition).sqrMagnitude > 25f * 25f) continue;
                if (++tries > 3) break;
                if (!Perception.LineClear(Perception.Eye(o), spot)) continue;
                who = Restraint.Name(o);
                return true;
            }
            return false;
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
            // On his knees he has to get up first (Kneel quick stand) before he can move: anything
            // on foot weighs less, a draw a little less (he can draw while rising).
            bool kneeling = Kneel.Active(s) && s.KneelT > 0.5f;
            float onFoot = kneeling ? 0.6f : 1f, drawK = kneeling ? 0.85f : 1f;

            var options = new List<Choice>(5);
            var log = new StringBuilder();
            log.Append($"{s.Name} takes an opening ({opening}) at {dist:0.0} m (nerve {nerve:0.00}" +
                       $"{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}, hurt {s.Injury:0.00}{(s.LegsBad ? ", legs hurt" : "")}, " +
                       $"{allies} squadmate(s) near{(kneeling ? $", kneeling: on foot x{onFoot:0.0}, draw x{drawK:0.00}" : "")}): ");

            // 0.15.0: at arm's reach (2.5 m) a lunge with the knife beats a draw in front of him;
            // a draw then is slower than the man already aiming.
            bool lunge = dist <= 2.5f;
            if (gun == null) log.Append("draw: no gun on him; ");
            else if (dist > Settings.SidearmRange.Value) log.Append($"draw: you're beyond {Settings.SidearmRange.Value:0} m; ");
            else Add(options, log, "draw " + Arsenal.SlotName(gun) + (lunge ? " (at arm's reach)" : "") + (s.Deceiving ? " (kept back for this)" : ""),
                Settings.DrawWeight.Value * (0.4f + nerve) * fight * (allies > 0 ? 1.2f : 1f) * (1f - 0.4f * s.Injury) * drawK * (lunge ? 0.7f : 1f) *
                (s.Deceiving ? Settings.KeptDrawBoost.Value : 1f));

            if (gun != null) log.Append("knife: has a gun to draw; ");
            else if (!Arsenal.HasMelee(Me)) log.Append("knife: none; ");
            else if (dist > Settings.KnifeRange.Value) log.Append($"knife: you're beyond {Settings.KnifeRange.Value:0} m; ");
            else Add(options, log, "knife", Settings.KnifeWeight.Value * (0.2f + nerve) * fight * (1f - 0.5f * s.Injury) * Mathf.Lerp(legs, 1f, 0.5f) * onFoot * (lunge ? 1.5f : 1f));

            // 0.14.0: a gun within easy reach is the strong choice (x2.5 within 3 m, x1.6 within
            // 5 m); on his knees it costs little more than standing when it lies right by him.
            float reach = EasyReach(s, out string reachWhy);
            float scrambleFoot = kneeling && reach > 1.5f ? 0.85f : onFoot;
            if (scramble <= 0f) log.Append($"scramble: {scrambleWhy}; ");
            else Add(options, log, $"scramble{reachWhy}", Settings.ScrambleWeight.Value * scramble * reach * (0.5f + nerve) * fight * (gun != null ? 0.5f : 1f) * legs * scrambleFoot);

            if (dist < Settings.EscapeMinDistance.Value) log.Append($"run: you're within {Settings.EscapeMinDistance.Value:0} m; ");
            else if (!Perception.RetreatPoint(Me, ThreatPos(s, captor), Settings.RetreatDistance.Value, false, out _)) log.Append("run: no way out; ");
            else Add(options, log, "run", Settings.EscapeWeight.Value * Mathf.Max(0.1f, 1.3f - nerve) * flee * legs * onFoot);

            Vector3 hidePoint = Vector3.zero;
            if (dist < Settings.EscapeMinDistance.Value * 0.5f) log.Append("hide: you're right there; ");
            else if (!Perception.RetreatPoint(Me, ThreatPos(s, captor), Settings.CoverRadius.Value, true, out hidePoint)) log.Append("hide: no hidden spot near; ");
            else Add(options, log, "hide", Settings.HideWeight.Value * (1.3f - nerve) * flee * (s.LegsBad ? 1.5f : 1f) * (kneeling ? 0.8f : 1f));

            if (options.Count == 0)
            {
                HandsAreHighPlugin.Log.LogInfo(log.Append("-> nothing he can do, keeps his hands up").ToString());
                return;
            }
            Choice chosen = Pick(options, log);
            HandsAreHighPlugin.Log.LogInfo(log.ToString());

            if (chosen.Name == "knife")
            {
                if (Arsenal.Draw(s, BotOwner, true)) Rearm(s, $"goes for his knife at {dist:0.0} m");
                else CommitDraw(s, "knife");
            }
            else if (chosen.Name != null && chosen.Name.StartsWith("scramble"))
            {
                StartScramble(s, ThreatPos(s, captor), scrambleWhy);
                MarkKneelEscape(s, "scramble");
            }
            else if (chosen.Name == "run")
            {
                s.BeginRetreat("bolts during an opening", emergency: true);
                MarkKneelEscape(s, "run");
            }
            else if (chosen.Name == "hide")
            {
                BeginHide(s, ThreatPos(s, captor), "slips into cover during an opening");
                s.HideSearchPending = false;   // already found one
                s.TargetPoint = hidePoint;
                s.HideMoving = true;
                MarkKneelEscape(s, "hide");
            }
            else if (Arsenal.Draw(s, BotOwner, false))
            {
                Rearm(s, $"draws his {Arsenal.SlotName(gun)} at {dist:0.0} m");
            }
            else
            {
                CommitDraw(s, Arsenal.SlotName(gun));
            }
        }

        // ---- an escape from the knees, called off or not ---------------------------------

        // Only escapes on foot that he chose (an opening, or the debug run/grab), started while
        // he was on his knees or getting there. Draws are never called off: the draw is already
        // with the game's hands controller, and a second hands change on top of it is the
        // busy-hands glitch. Hits are never reconsidered (a man who was shot runs).
        private static void MarkKneelEscape(SurrenderState s, string kind)
        {
            if (!Kneel.Active(s)) return;
            s.KneelEscape = true;
            s.KneelEscapeKind = kind;
            s.KneelEscapeT = s.KneelT;
            s.KneelEscapeSeen = -1f;
        }

        // His captor covers him again while he is getting up (or on his first step): noticed after
        // his own reaction time, then one decision for this attempt, never re-made (no bouncing
        // between rising and kneeling):
        //   carry on     - more likely the further up he is, with nerve, a fighter or a runner by
        //                  personality, and with distance between them;
        //   back down    - only in the first half of the rise: low nerve, timid;
        //   up, hands up - a standing surrender from where he is.
        // Giving up often starts with a moment frozen, hands showing. The motion continues from
        // his actual pose (Kneel.Recomply: same t and timing, no restart, no snap). Recapture
        // proper (SpottedAgain, with its grace) is unchanged.
        private bool ReassessEscape(SurrenderState s)
        {
            float now = Time.time;
            bool rising = Kneel.Active(s);
            if (!rising && now - s.KneelFreedAt > FirstStepWindow) { s.KneelEscape = false; return false; }
            Player captor = s.Captor;
            if (!Perception.Alive(captor)) { s.KneelEscape = false; return false; }
            float dist = Vector3.Distance(captor.Position, Me.Position);
            bool seen = Perception.Sees(Me, captor, dist);
            // A debug opening ignores your aim until it ends, here as everywhere.
            // 0.15.0: only a captor he can see aiming makes him reconsider (one behind him, unseen,
            // doesn't call off an escape already under way).
            bool covered = seen && now >= s.DebugOpeningUntil && Presence.InView(s, Perception.Eye(captor)) &&
                           Reactions.Covered(s, captor, Me, true, true, Settings.WatchAngle.Value, out _, out _, out _);
            if (!covered) { s.KneelEscapeSeen = -1f; return false; }
            if (s.KneelEscapeSeen < 0f)
            {
                s.KneelEscapeSeen = now;
                s.KneelEscapeNotice = now + s.NoticeDelay();
                return false;
            }
            if (now < s.KneelEscapeNotice) return false;
            s.KneelEscape = false;   // decided: once per attempt

            float p = rising ? Mathf.Clamp01(1f - s.KneelT / Mathf.Max(0.05f, s.KneelEscapeT)) : 1f;
            float nerve = s.Nerve;
            float timid = Presence.Timid(s.Personality);
            string kind = s.KneelEscapeKind ?? "run";
            float kindW = kind == "scramble" ? 1.15f * s.Fight : kind == "hide" ? 0.8f * s.Flee : s.Flee;
            float commit = (0.25f + nerve) * kindW * (0.5f + 1.2f * p) * (dist > 1.5f * Settings.EscapeMinDistance.Value ? 1.3f : 0.8f) * (1f - 0.4f * s.Injury);
            float down = rising && p < 0.55f ? (1.3f - nerve) * timid * (1f - p / 0.55f) * 1.3f : 0f;
            float up = (1.2f - nerve) * (0.4f + p) * 0.8f;
            var options = new List<Choice>(3);
            var log = new StringBuilder($"{s.Name}: covered again {(rising ? $"{p:P0} of the way up" : "on his first step")} ({kind}, {dist:0.0} m, nerve {nerve:0.00}" +
                                        $"{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}): ");
            switch (Settings.KneelTestEscape.Value)
            {
                case KneelEscapeChoice.Commit: down = up = 0f; log.Append("(debug: carry on) "); break;
                case KneelEscapeChoice.BackDown: commit = up = 0f; down = rising ? 1f : 0f; log.Append("(debug: back down) "); break;
                case KneelEscapeChoice.StandUp: commit = down = 0f; up = 1f; log.Append("(debug: stand) "); break;
            }
            Add(options, log, "carry on", commit);
            Add(options, log, "back down", down);
            Add(options, log, "stand, hands up", up);
            Choice c = Pick(options, log);
            if (c.Name == null || c.Name == "carry on")
            {
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());
                return false;
            }
            bool backDown = c.Name == "back down";
            float freeze = Dice.Value < 0.6f * (timid > 1f ? 1.25f : 1f) ? Dice.Range(0.35f, 0.8f) : 0f;
            if (freeze > 0f) log.Append($"; freezes {freeze:0.0} s first");
            if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());

            // Everything the escape had set up is dropped: the walk, the sprint, the claim on a
            // gun, the hide spot, the grace.
            StopMoving();
            BotOwner.Sprint(false);
            s.SprintHeld = false;
            if (s.Scrambling) s.ScrambleCooldownUntil = now + Settings.ScrambleCooldown.Value;
            s.Scrambling = false;
            s.TargetWeapon = null;
            s.HideMoving = false;
            s.HideSearchPending = false;
            s.GraceUntil = 0f;
            s.AimedBy = null;
            s.CaptorLastSeen = captor.Position;
            s.CaptorHiddenSince = -1f;
            s.Order = backDown ? Order.Kneel : Order.None;
            if (backDown && Settings.KneelHandsOnHead.Value && !s.HandsOrdered) s.HandsOnHead = true;   // 0.16.0: as a Get down
            s.KneelFallback = null;
            s.KneelMoves = 0;
            s.RaiseArms();
            s.SetPhase(Phase.HandsUp, backDown ? "gives up the escape, back down on his knees" : "gives up the escape, stands with his hands up");
            s.LastCovered = true;
            s.OpenSince = -1f;
            s.DecideAt = -1f;
            s.LeaveAt = -1f;
            s.ComplyUntil = now + Mathf.Max(4f, Settings.OrderCompliance.Value) * Dice.Range(0.8f, 1.2f);
            Kneel.Recomply(s, backDown, freeze, backDown ? "gave up the escape" : "gave up the escape, stands");
            Voice.Reply(s, VoiceEvent.Plead);
            return true;
        }

        // The game refused the draw he decided on: he stays committed to it for a moment
        // (HandsUp retries it every check) instead of waiting for the next decision. Knife:
        // no retry (it's only offered at arm's length; the moment passes).
        private static void CommitDraw(SurrenderState s, string what)
        {
            if (what == "knife" || Settings.DrawRetrySeconds.Value <= 0f)
            {
                s.TDecided = -1f;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: tried to draw his {what}, the game refused the weapon change");
                return;
            }
            s.DrawCommitUntil = Time.time + Settings.DrawRetrySeconds.Value;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: tried to draw his {what}, the game refused the weapon change; tries again for {Settings.DrawRetrySeconds.Value:0.0} s");
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
            Kneel.Urge(s, KneelRise.Escape);
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
            if (Arsenal.GunOnBody(Me) != null && Arsenal.Draw(s, BotOwner, false))
            {
                Rearm(s, "scramble failed, draws what he has");
                return;
            }
            s.BeginRetreat($"scramble failed ({why})", emergency: true);
        }

        // The draw is already asked for (Arsenal.Draw) when this runs: the arms come down over
        // DrawLowerSeconds while the game's draw animation starts underneath, in the same frame.
        private void Rearm(SurrenderState s, string reason)
        {
            // A draw from the knees: the prompt, guarded rise (the draw itself starts now, on the
            // hands controller; the legs are ours and the AI gets him only once he's up).
            Kneel.Urge(s, KneelRise.Defensive);
            s.DrawCommitUntil = 0f;
            s.LowerArms(Settings.DrawLowerSeconds.Value);
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.TargetWeapon = null;
            Voice.Say(s, VoiceEvent.Break);
            s.SetPhase(Phase.Rearmed, reason);
        }

        // Where he believes his captor is: exactly while he sees him, else the last sighting.
        private static Vector3 ThreatPos(SurrenderState s, Player c) =>
            s.CaptorSeen && Perception.Alive(c) ? c.Position : s.CaptorLastSeen;

        private static Vector3 ThreatEye(SurrenderState s, Player c) =>
            s.CaptorSeen && Perception.Alive(c) ? Perception.Eye(c) : s.CaptorLastSeen + Vector3.up * 1.5f;

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
            // No path (MoveTo false): stand and try again in a second, never a path solve per frame.
            if (Time.time < s.FollowRetryAt) { Hold(Settings.Stance.Value); return; }
            if (!MoveTo(s, point, sprint: false))
            {
                s.FollowRetryAt = Time.time + 1f;
                Hold(Settings.Stance.Value);
            }
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
                // 0.16.0: what really happened, item by item (and what he kept back on purpose:
                // in the log only, never on screen or to a captor who couldn't see it).
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: order '{s.OrderName}': {msg}{Compliance.OnDropReport(s)}");
                if (s.OrderName == "drop your weapons") s.DemandDoneAt = Time.time;
                if (s.OrderByHuman) OrderMenu.Notify(msg, kept == 0 ? NoteKind.Success : NoteKind.Error);
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
                LegDone(s, "got there");
                return;
            }
            if (Time.time - s.LegSince > Settings.RetreatTimeout.Value)
            {
                LegDone(s, "ran out of time on this leg");
                return;
            }
            // 0.17.0: the threat he can see now stands by where he is heading: a new destination
            // (at most twice a leg, checked once a second).
            if (Time.time >= s.NextFleeCheck && s.FleeReplans < 2)
            {
                s.NextFleeCheck = Time.time + 1f;
                Player c = s.Captor;
                if (Perception.Alive(c) && Perception.Flat(c.Position, s.TargetPoint) < 10f && Perception.Sees(Me, c, Vector3.Distance(Me.Position, c.Position)) && Perception.TakeHeavy())
                {
                    s.FleeReplans++;
                    s.CaptorLastSeen = c.Position;
                    if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {Restraint.Name(c)} is {Perception.Flat(c.Position, s.TargetPoint):0} m from where he is running to: another way");
                    if (PickRetreat(s)) { s.NextMoveOrder = -1f; ResetProgress(s); }
                }
            }
        }

        // 0.17.0: out of the captivity area (FleePoint), or the next leg towards it through cover.
        private bool PickRetreat(SurrenderState s)
        {
            if (!s.HeldSet) { s.HeldAt = Me.Position; s.HeldSet = true; s.FleeSince = s.LegSince = Time.time; s.FleeFrom = Me.Position; }
            float radius = Settings.CaptivityRadius.Value;
            Vector3 threat = Perception.Alive(s.Captor) && Perception.Sees(Me, s.Captor, Vector3.Distance(Me.Position, s.Captor.Position)) ? s.Captor.Position : s.CaptorLastSeen;
            if (Perception.FleePoint(Me, threat, s.HeldAt, radius, out s.TargetPoint, out bool leg, out string why))
            {
                if (Settings.LogDecisions.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: flees to a spot {Perception.Flat(s.TargetPoint, Me.Position):0} m away, {Perception.Flat(s.TargetPoint, s.HeldAt):0} m from where he was held " +
                        $"({(leg ? $"a leg through cover {s.FleeLegs + 1}: no way straight out of {radius:0} m" : $"out of the {radius:0} m area")}; {Perception.Flat(s.TargetPoint, threat):0} m from the threat, " +
                        $"{(Perception.LineClear(threat + Vector3.up * 1.5f, s.TargetPoint + Vector3.up * 1.2f) ? "in its sight" : "hidden from it")})");
                return true;
            }
            if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {why}");
            GoHide(s, "no way out");
            return false;
        }

        // 0.17.0: a leg reached, or out of time on it: on out of the area, or done.
        private void LegDone(SurrenderState s, string why)
        {
            float radius = Settings.CaptivityRadius.Value;
            float sep = Perception.Flat(Me.Position, s.HeldAt);
            if (sep < radius && s.FleeLegs < 4)
            {
                s.FleeLegs++;
                s.LegSince = Time.time;
                s.FleeReplans = 0;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {why} {sep:0} m from where he was held, still inside {radius:0} m: keeps going (leg {s.FleeLegs + 1})");
                s.NextMoveOrder = 0f;
                ResetProgress(s);
                return;
            }
            EndFlight(s, sep >= radius ? why : $"{why}, after {s.FleeLegs + 1} legs");
            Regroup(s, why);
        }

        private void EndFlight(SurrenderState s, string why)
        {
            if (!Settings.LogDecisions.Value || s.FleeSince < 0f) return;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: retreat ends ({why}): {Perception.Flat(Me.Position, s.HeldAt):0} m from where he was held, {Perception.Flat(Me.Position, s.FleeFrom):0} m covered in {Time.time - s.FleeSince:0} s, {s.FleeLegs + 1} leg(s)");
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
            if (!Kneel.LegsLocked(s)) BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
            if (!check) return;
            if (SpottedAgain(s)) return;
            if (Time.time < s.RegroupUntil) return;

            Decide(s);
        }

        // How to get back in the fight, best option first.
        private void Decide(SurrenderState s)
        {
            EquipmentSlot? kept = Arsenal.GunOnBody(Me);
            if (kept != null && Arsenal.Draw(s, BotOwner, false))
            {
                Rearm(s, $"draws the {Arsenal.SlotName(kept)} he kept");
                return;
            }

            // 0.17.0: fled from captivity: no going back into that area for a gun until the commitment
            // runs out (no escape, linger, recapture loop). Weapons he carries stay options (above).
            bool committed = s.HeldSet && Time.time < s.FleeLockUntil;
            float area = Settings.CaptivityRadius.Value;
            bool ownInArea = committed && s.DroppedWeapon != null && Perception.Flat(s.DropPosition, s.HeldAt) < area;
            if (ownInArea && !s.FleeLockLogged && Settings.LogDecisions.Value)
            {
                s.FleeLockLogged = true;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: his {s.DroppedWeapon.ShortName.Localized()} lies where he was held: not going back for it for {s.FleeLockUntil - Time.time:0} s");
            }
            // What he believes: unless he saw it gone, his gun is where he dropped it.
            if (!ownInArea && Settings.RecoverOwnWeapon.Value && s.DroppedWeapon != null && !s.Skip.Contains(s.DroppedWeapon) &&
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
                if (found != null && committed && Perception.Flat(at, s.HeldAt) < area)
                {
                    // In the area he just fled: not now (looked at again once the commitment ends).
                    s.NextScavengeScan = s.FleeLockUntil;
                    found = null;
                }
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

            if (Settings.HighNerveKnife.Value && s.Nerve >= Settings.HighNerve.Value && Arsenal.HasMelee(Me) && Arsenal.Draw(s, BotOwner, true))
            {
                Rearm(s, $"no gun to find, comes back with his knife (nerve {s.Nerve:0.00})");
                return;
            }

            s.TargetWeapon = null;
            GoHide(s, "nothing to fight with", quiet: s.Phase == Phase.Regrouping && s.HidCount++ > 0);
        }

        private void Recovering(SurrenderState s, bool check)
        {
            // 0.14.0: going for a gun is never a stroll: he hurries unless it is right there
            // (a scramble always sprints; the walk back after regrouping used to walk).
            bool hurry = s.Scrambling || (s.TargetPoint - Me.Position).sqrMagnitude > 3f * 3f;
            if (!MoveTo(s, s.TargetPoint, sprint: hurry))
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
            if (Arsenal.Draw(s, BotOwner, false))
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
            s.HideUrgent = false;
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
                if (!MoveTo(s, s.TargetPoint, sprint: s.HideUrgent)) s.HideMoving = false;
            }
            else
            {
                Hold(0f);
                if (!Kneel.LegsLocked(s)) BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
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
                float roll = Dice.Value;
                if (roll < chance && Arsenal.Draw(s, BotOwner, false))
                {
                    Rearm(s, $"caught again but draws (chance {chance:0.00}, roll {roll:0.00})");
                    return true;
                }
            }
            s.HidCount = 0;
            SurrenderRegistry.Surrender(Me, aiming, s.Scrambling ? "caught mid-scramble" : "caught again", source: SurrenderSource.Recapture);
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
            pose = Kneel.Pose(_s, pose);   // the game's crouch stays on while he kneels or rises
            if (Mathf.Abs(pose - _pose) < 0.01f) return;
            _pose = pose;
            BotOwner.SetPose(pose);
        }

        // Facing the captor: the look point only moves when he has moved more than
        // FaceHysteresis degrees around the bot, and it's his body (not his bobbing head bone),
        // so the turn is one smooth steering target instead of a new one every frame.
        // The facing target only (hysteresis as Face), no look.
        private void AimAt(SurrenderState s, Vector3 point)
        {
            Vector3 me = Me.Position;
            Vector3 want = point - me; want.y = 0f;
            Vector3 have = s.FaceTarget - me; have.y = 0f;
            if (!s.FaceSet || have.sqrMagnitude < 0.01f || Vector3.Angle(have, want) > Settings.FaceHysteresis.Value)
            {
                s.FaceTarget = point;
                s.FaceSet = true;
            }
        }

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
            // Still on his knees (getting up, quickly if it's urgent): no step, no sprint until
            // his legs are free. Progress timers wait with him; the path is made fresh after.
            if (Kneel.LegsLocked(s))
            {
                if (s.Moving) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); s.Moving = false; }
                BotOwner.Sprint(false);
                // The game's stand comes back as he rises (Kneel.Pose keeps the crouch until
                // t 0.5): his capsule and the animator are already standing at the hand-off, so
                // the walk starts upright (0.12.1 asked for the crouch here, a dip after the rise).
                SetPose(1f);
                ResetProgress(s);
                // The route is kept for when his legs are free: Retreating picks a route only
                // while NextMoveOrder is 0, and 0.12.1 reset it here, so a rising bot searched a
                // new retreat route (up to PathCandidates path solves) every frame of the rise.
                if (s.NextMoveOrder == 0f) s.NextMoveOrder = -1f;
                // His rise hands the legs over at its hand-off point, and near the end he may turn
                // on his planted feet towards where he's going (the rest of the turn while walking).
                Kneel.WantsToWalk(s, point);
                if (Kneel.PivotOpen(s)) BotOwner.Steering.LookToDirection(Kneel.PivotDir(s, point - Me.Position), PivotSpeed);
                return true;
            }
            SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            bool first = s.NextMoveOrder == 0f || !s.Moving;
            bool moved = (point - s.PathTarget).sqrMagnitude > 1f;
            if (s.SprintHeld && Kneel.SprintAllowed(s)) { s.SprintHeld = false; if (sprint) BotOwner.Sprint(true); }
            // 0.14.0: an urgent move (escape, scramble, run for cover, going for a gun) faces the
            // way he goes, and the sprint is asked for until it is granted. Before, he kept facing
            // his captor (the last LookToPoint) and asked once per path: SAIN's patch on
            // BotMover.Sprint (SprintLookDirPatch) turns a sprint request into a walk when the bot
            // looks more than 20 deg away from his destination, so he walked off backwards at
            // walking pace and never asked again. The game's own rules still apply: a damaged
            // leg or no stamina means no sprint (MovementContext.CanSprint, Physical.CanSprint).
            if (sprint && s.Moving)
            {
                BotOwner.Steering.LookToMovingDirection();
                if (Kneel.SprintAllowed(s) && BotOwner.Mover != null && !BotOwner.Mover.Sprinting) BotOwner.Sprint(true);
                MoveDiag(s);
            }
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
            if (first && sprint)
            {
                s.UrgentSince = Time.time;
                s.UrgentFrom = Me.Position;
                s.UrgentLogged = false;
                s.UrgentPath = corners.Length;
            }
            s.Moving = true;
            BotOwner.MovementResume();
            BotOwner.Mover.GoToByWay(corners, ReachDist);
            // Straight out of a kneel he walks his first steps while our pose blends out
            // (0.14 s), then sprints: never a sprint cycle under a kneeling body.
            bool sprintNow = sprint && Kneel.SprintAllowed(s);
            if (sprint) BotOwner.Steering.LookToMovingDirection();
            BotOwner.Sprint(sprintNow);
            s.SprintHeld = sprint && !sprintNow;
            return true;
        }

        // Once per urgent move, 1.5 s in: what was asked and what the body is doing, so a slow
        // escape can be told apart (refused sprint, hurt leg, stamina, not moving at all).
        private void MoveDiag(SurrenderState s)
        {
            if (s.UrgentLogged || s.UrgentSince <= 0f || Time.time - s.UrgentSince < 1.5f) return;
            s.UrgentLogged = true;
            if (!Settings.LogDecisions.Value && !Settings.VerboseLogs.Value && !Scenarios.Watching(Me)) return;
            try
            {
                float t = Time.time - s.UrgentSince;
                Vector3 d = Me.Position - s.UrgentFrom; d.y = 0f;
                MovementContext mc = Me.MovementContext;
                string legs = mc == null ? "?" : mc.PhysicalConditionIs(EPhysicalCondition.LeftLegDamaged) || mc.PhysicalConditionIs(EPhysicalCondition.RightLegDamaged) ? "damaged leg" : "legs fine";
                bool stamina = Me.Physical != null && Me.Physical.CanSprint;
                Vector3 look = Me.LookDirection; look.y = 0f;
                Vector3 to = BotOwner.Mover.RealDestPoint - Me.Position; to.y = 0f;
                float off = look.sqrMagnitude > 1e-4f && to.sqrMagnitude > 1e-4f ? Vector3.Angle(look, to) : 0f;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {s.Phase} move after {t:0.0} s: {d.magnitude / t:0.0} m/s average ({d.magnitude:0.0} m, path {s.UrgentPath} corners); " +
                    $"sprint asked, mover {(BotOwner.Mover.Sprinting ? "sprinting" : "NOT sprinting")}, body {(Me.IsSprintEnabled ? "sprinting" : "not sprinting")}; " +
                    $"{legs}, stamina {(stamina ? "ok" : "too low")}, facing {off:0} deg off his destination, arms {s.Weight:0.00}, pose {Me.PoseLevel:0.00}");
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("move diagnostics", e); }
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
                sb.AppendLine($"HandsAreHigh: {s.Phase}, order {s.Order}, arms {s.Weight:0.00}, nerve {s.Nerve:0.00}{(Kneel.Describe(s) is string k ? ", " + k : "")}");
        }
    }
}
