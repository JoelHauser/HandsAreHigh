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

        // BigBrain asks every bot's brain this on every brain update: nobody held, one count.
        public override bool IsActive()
        {
            long t0 = Perf.Begin();
            bool on = SurrenderRegistry.Any && SurrenderRegistry.HoldsBot(BotOwner?.GetPlayer);
            Perf.End(Perf.Sec.LayerCheck, t0);
            return on;
        }

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
            // Handing the bot back: the pose is the next layer's to set. 0.21.0: our last path is
            // cleared (the registry's hand-back does it too), so a brain without SAIN doesn't keep
            // walking an outdated destination of ours.
            _pose = -1f;
            if (_s != null && _s.Moving && !_s.HandBackMoving) { BotOwner.StopMove(); _s.Moving = false; }
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

            // 0.21.0: incoming fire (FireBreak) is acted on the frame it arrives, before anything
            // else this frame: no waiting for the next check.
            if (s.FirePending)
            {
                s.FirePending = false;
                UnderFire(s);
            }

            // An escape from his knees, and his captor covers him again before he's away. Never
            // one driven by fire (he isn't talked back down in the middle of it).
            if (s.KneelEscape && check && !FireBreak.Blocks(Me) && (s.Phase == Phase.Retreating || s.Phase == Phase.Hiding || s.Phase == Phase.Recovering) && ReassessEscape(s))
                return;

            if (s.Phase != Phase.HandsUp) Stamp(s);
            switch (s.Phase)
            {
                case Phase.HandsUp: HandsUp(s, check); break;
                case Phase.Retreating: Retreating(s, check); break;
                case Phase.Regrouping: Regrouping(s, check); break;
                case Phase.Recovering: Recovering(s, check); break;
                case Phase.PickingUp: PickingUp(s); break;
                case Phase.Hiding: Hiding(s, check); break;
                case Phase.Rearmed: Rearmed(s); break;
                case Phase.Released: Released(s); break;
            }
            KeepLookOnBody(s);
        }

        // 0.21.0: free and about to be handed to his own AI (SurrenderRegistry.Tick, once his arms
        // are down, his legs his own and no hands change of ours is open): standing, not moving,
        // no new look (the next AI decides where he looks and goes). A rise still under way
        // finishes first (the AI never gets a kneeling body).
        private void Released(SurrenderState s)
        {
            // 0.25.0: handed back mid-stride: he keeps walking our last path until his own AI's
            // first move replaces it (no stop between us and it).
            if (s.Moving && !s.HandBackMoving) StopMoving();
            if (s.SprintHeld) { s.SprintHeld = false; BotOwner.Sprint(false); }
            if (!Kneel.LegsLocked(s)) SetPose(1f);
        }

        // 0.25.0: the hand-off timeline (ReleaseWatch logs it). Cheap: a few float compares a frame.
        private void Stamp(SurrenderState s)
        {
            float now = Time.time;
            if (s.TEscape < 0f) { s.TEscape = now; s.EscapeKind = s.Phase.ToString(); }
            if (s.THandsDown < 0f && !s.ArmsUp && s.Progress <= 0f) s.THandsDown = now;
            if (s.TOnFeet < 0f && !Kneel.Posing(s)) s.TOnFeet = now;
            if (s.TFirstMove < 0f && Me.Velocity.sqrMagnitude > 0.5f * 0.5f) s.TFirstMove = now;
        }

        // 0.25.0: a calm way out (let go, left behind, his captor gone) is over for us once he is on
        // his way: arms down, legs his own, walking, and either "Hand back once this far" from where
        // he was held, or out of his ex-captor's sight and 4 m away, or "Hand back at most this long"
        // after he set off. 0.24.0 ran up to 4 legs until 40 m away and unseen, then a regroup; the
        // 0.24.0 log: hand-backs 11-14 s after leaving captivity.
        private bool QuickClear(SurrenderState s, out string why)
        {
            why = null;
            if (!s.QuickHandBack || s.ArmsUp || s.Progress > 0f || Kneel.Posing(s)) return false;
            float sep = s.HeldSet ? Perception.Flat(Me.Position, s.HeldAt) : 0f;
            float t = Time.time - (s.FleeSince > 0f ? s.FleeSince : s.PhaseSince);
            bool walking = s.Moving && Me.Velocity.sqrMagnitude > 0.4f * 0.4f;
            if (walking && sep >= Settings.QuickHandBackDistance.Value) why = $"{sep:0} m from where he was held";
            else if (walking && sep >= 4f && !CaptorSeesMe(s)) why = $"out of his ex-captor's sight, {sep:0} m away";
            else if (t >= Settings.QuickHandBackSeconds.Value) why = $"{t:0.0} s on his way, {sep:0} m away";
            return why != null;
        }

        // The one way he leaves us without a draw: arms down, nothing of ours left on him.
        private void HandBack(SurrenderState s, string why)
        {
            // 0.25.0: on his way out (a let-go or walk-off, arms down, on his feet, walking): no stop.
            s.HandBackMoving = s.QuickHandBack && s.Moving && s.Progress <= 0f && !Kneel.Posing(s);
            if (!s.HandBackMoving) StopMoving();
            BotOwner.Sprint(false);
            s.SprintHeld = false;
            s.HandBackWhy = why;
            s.TargetWeapon = null;
            s.Scrambling = false;
            s.FireRecover = false;
            s.HideMoving = false;
            s.HideSearchPending = false;
            s.Order = Order.None;
            if (s.ArmsUp) s.LowerArms(Settings.LowerSeconds.Value);
            // Still down (not already rising): a calm rise. A rise under way keeps its own timing.
            // An urgent intent set this same frame (an escape under fire) is kept.
            if ((s.Kneel == KneelPhase.Down || s.Kneel == KneelPhase.GoingDown) && !s.KneelIntentSet) Kneel.Urge(s, KneelRise.Compliant);
            s.SetPhase(Phase.Released, why);
        }

        // 0.21.0: his look while any part of the kneel is on his bones (kneeling, rising, or the short
        // blend back to the animator): only within MaxPivot of the way his knees point, and only once
        // the rise allows turning on his planted feet. A look further round would make the game turn
        // his root (or twist his trunk) over legs that are still posed in the kneel's frame.
        private void SteerTo(SurrenderState s, Vector3 dir, float speed = 360f)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            if (Kneel.Posing(s))
            {
                if (Kneel.LegsLocked(s) && !Kneel.PivotOpen(s)) return;   // KeepLookOnBody holds the look
                dir = Kneel.PivotDir(s, dir);
                speed = Mathf.Min(speed, PivotSpeed);
            }
            BotOwner.Steering.LookToDirection(dir.normalized, speed);
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
            // 0.26.0: he picked up the gun his captor left: the draw plays where he looks; whether he
            // turns on anyone is his own AI's call once he is handed back (no revenge by default).
            if (s.CalmRearm) return;
            // Turns on his planted feet once his knees are off the ground (within MaxPivot), the
            // rest once the kneel pose is off him entirely (SteerTo).
            // 0.24.0: fighting back under fire: towards the shooter, not the captor.
            Vector3 at = s.FaceThreat ?? ThreatPos(s, s.Captor);
            if (Kneel.Posing(s)) { SteerTo(s, at - Me.Position, PivotSpeed); return; }
            Face(s, at + Vector3.up * 1.3f);
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
                // 0.23.0: off the gear at his feet, to the spot his captor picked (one short walk,
                // hands up, eyes on his captor; at most StepBackUntil).
                case Order.StepBack:
                    if (Time.time > s.StepBackUntil || Arrived(s.StepBackTo, 0.3f) || !MoveTo(s, s.StepBackTo, sprint: false))
                    {
                        StopMoving();
                        s.Order = Order.None;
                        if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: stepped back off his gear ({Perception.Flat(Me.Position, s.StepBackTo):0.0} m from the spot)");
                        Hold(Settings.Stance.Value);
                    }
                    else BotOwner.SetTargetMoveSpeed(0.5f);
                    break;
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
            // 0.21.0: not during the short blend back from a kneel either (Kneel.Posing): his legs
            // are still partly the kneel's there.
            bool facing = Settings.FaceCaptor.Value && captorAlive && !Kneel.Posing(s);
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
                // 0.28.4: who fired (the 0.28.3 raid: "shot with his hands up" with no shooter named
                // when nobody held fire at him, so the hit diagnosis in Restraint never ran).
                s.BeginRetreat($"shot with his hands up by {(s.LastHitBy != null ? Restraint.Name(s.LastHitBy) : "someone unseen")}", emergency: true);
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
            // 0.23.0: left by a bot captor (custody over, the captor alive): his own assessment of
            // what he can see (Abandonment), never the captor's decision; it replaces the 0.22
            // "captor gone" flight (run / hide / draw) for this case. While he weighs it his captor
            // isn't guarding him, so no "opening" decisions either.
            // 0.28.0: a bot that wasn't interested in him (Captors.TryIgnore) is read the same way,
            // whatever "Notices being left" says: nobody will come for him.
            if (captorAlive && captor.IsAI && s.Custody == null && ((s.Accepted && Settings.PrisonerNoticesAbandon.Value) || s.Ignored))
            {
                s.LeaveAt = -1f;
                Abandonment(s, captor, seen, inView, dist, now);
                return;
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

        // ---- 0.23.0: left by his captor ------------------------------------------------------------
        //
        // Only what he can see or hear: his captor walking off (more so with his back turned), his
        // gun away from him, standing off further than he ever was, or gone out of sight; heard
        // walking off behind him when he faces away. Each check adds "being left" evidence (and his
        // captor covering him again takes it away fast). A squadmate he can see covering him keeps
        // him in place (evidence reset).
        // 0.25.0: once the evidence is there he acts after his reaction time (AbandonAssess 0.3-1.2 s
        // x nature, at most 2.5 s), not after a 2.5-9 s "taking it in" on top of it: the 0.24.0 raid
        // log had men standing 7-10 s after their captor walked off (#3 8.4 s, #4 7.2 s, #8 10.1 s).
        // The cues build faster when clear (back turned and walking off: ~0.5 s; out of sight: ~0.6
        // s after the 1 s it takes to be sure he's gone) and slower while the captor is barely moving
        // away (repositioning, a step aside). Not being left: a captor seeing to gear lying close to
        // him (CaptorLoot.GatheringNear: he is coming back), a squadmate covering him, a player aiming
        // his way. Bounded: 20 s after custody ended he decides anyway (was 45).
        private const float LeftThreshold = 1.0f, LeftBound = 20f;
        // 0.28.0: an ignored man decides within this however the cues went (nobody is coming).
        private const float IgnoredBound = 8f, IgnoredSquadLimit = 30f;

        private void Abandonment(SurrenderState s, Player captor, bool seen, bool inView, float dist, float now)
        {
            if (s.LeftSince < 0f) { s.LeftSince = now; s.LeftEvidence = 0f; s.LeftDecideAt = -1f; s.LeftLastCheck = now; }
            float dt = Mathf.Clamp(now - s.LeftLastCheck, 0f, 0.5f);
            s.LeftLastCheck = now;

            // Someone else of them holding him at gunpoint: he stays put (and is not left).
            // 0.28.0: so does any other bot who spared him and has him at gunpoint (ObserverCovers):
            // one bot losing interest doesn't free him from everyone.
            bool bySquad = Reactions.SquadCovers(s, captor, Me, Settings.WatchAngle.Value, out string who, out _);
            bool covered = bySquad || PlayerCovers(out who) || ObserverCovers(s, captor, out who);
            // 0.28.0: the squadmates of a bot that ignored him went along with it; one whose gun
            // happens to point his way holds him a while, not for ever (a player or another bot
            // with him at gunpoint still does).
            if (covered && bySquad && s.Ignored && now - s.IgnoredAt > IgnoredSquadLimit)
            {
                if (!s.IgnoredWaitLogged && Settings.LogEncounter.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {who} still has a gun his way, but nobody has come for him in {now - s.IgnoredAt:0} s: stops waiting");
                s.IgnoredWaitLogged = true;
                covered = false;
                s.LeftSince = Mathf.Min(s.LeftSince, now - IgnoredBound);   // decide now
            }
            else if (covered)
            {
                if (!s.LeftCoveredLogged && Settings.LogEncounter.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: his captor isn't on him, but {who} still covers him: stays put");
                s.LeftCoveredLogged = true;
                s.LeftEvidence = 0f;
                s.TLeftCue = -1f;
                s.LeftSince = now;   // the 20 s bound counts unguarded time only
                if (s.LeftDecideAt >= 0f) Cancel(s, $"{who} covers him");
                return;
            }
            s.LeftCoveredLogged = false;

            string cue = null;
            float w;
            Vector3 toMe = Me.Position - captor.Position; toMe.y = 0f;
            Vector3 v = captor.Velocity; v.y = 0f;
            bool goingAway = v.sqrMagnitude > 0.4f * 0.4f && toMe.sqrMagnitude > 0.01f && Vector3.Dot(v, toMe) < 0f;
            // Barely further than he has been: could be a step aside, not leaving (yet).
            float gained = dist - s.ClosestDist;
            float sure = Mathf.Clamp01(Mathf.InverseLerp(0.5f, 3f, gained));
            if (seen && CaptorLoot.GatheringNear(captor, Me.Position, 6f, out string gear))
            {
                // Seeing to gear by him: he'll be back. Not left; what was building up fades.
                cue = null;
                w = -1.5f;
                if (s.LeftEvidence > 0.3f && Settings.VerboseLogs.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {Restraint.Name(captor)} is {gear} by him: not leaving him (yet)");
            }
            else if (seen && inView)
            {
                Vector3 look = captor.LookDirection; look.y = 0f;
                float lookAway = toMe.sqrMagnitude > 0.01f && look.sqrMagnitude > 1e-4f ? Vector3.Angle(look, toMe) : 0f;
                string gunOff = Overwatch.Visibly(captor, Me, Settings.WatchAngle.Value);
                bool further = gained > 3f;
                if (goingAway && lookAway > 90f) { cue = "walks off with his back to him"; w = 2.4f * Mathf.Lerp(0.35f, 1f, sure); }
                else if (goingAway) { cue = "walks away from him"; w = 1.5f * Mathf.Lerp(0.35f, 1f, sure); }
                else if (further && gunOff != null) { cue = $"stands off further than before, {gunOff}"; w = 0.8f; }
                else if (gunOff != null && lookAway > 100f) { cue = "has turned away from him"; w = 0.4f; }
                else w = -3f;   // he's on him: not left
            }
            else if (seen)
            {
                // Behind him (he faces away or kneels turned away): what he hears.
                if (goingAway) { cue = "hears him walk off behind him"; w = 1.4f * Mathf.Lerp(0.35f, 1f, sure); }
                else if (gained > 4f) { cue = "his steps behind him have gone off"; w = 0.9f; }
                else w = 0f;   // still there behind him: assumed on him
            }
            else
            {
                float hidden = s.CaptorHiddenSince >= 0f ? now - s.CaptorHiddenSince : 0f;
                cue = "his captor is out of sight";
                w = hidden > 1f ? 1.8f : 0.4f;
            }
            // 0.28.0: ignored: no orders, no approach, no one coming for him. It sinks in fast once
            // the bot is plainly busy with something else; slowly while he still faces him (a gun that
            // happens to point his way, but nobody giving orders); not while he walks up to him.
            if (s.Ignored)
            {
                bool closing = seen && captor.Velocity.sqrMagnitude > 0.4f * 0.4f && toMe.sqrMagnitude > 0.01f && Vector3.Dot(captor.Velocity, toMe) > 0f && dist < s.ClosestDist + 0.5f;
                float iw;
                string icue;
                if (closing) { iw = -1f; icue = null; }
                else if (seen && inView && Overwatch.Visibly(captor, Me, Settings.WatchAngle.Value) == null) { iw = 0.5f; icue = "keeps looking his way but gives no orders"; }
                else { iw = 2.2f; icue = "pays him no attention"; }
                if (iw > w) { w = iw; cue = icue; }
                else if (iw < 0f) { w = Mathf.Min(w, iw); cue = null; }
            }
            s.LeftEvidence = Mathf.Clamp(s.LeftEvidence + w * dt, 0f, 10f);
            if (cue != null)
            {
                s.LeftWhy = cue;
                if (s.TLeftCue < 0f && w > 0f) s.TLeftCue = now;
            }
            else if (s.LeftEvidence <= 0f) s.TLeftCue = -1f;

            bool bound = now - s.LeftSince > (s.Ignored ? IgnoredBound : LeftBound);
            if (s.LeftDecideAt < 0f)
            {
                if (s.LeftEvidence < LeftThreshold && !bound) return;
                float assess = bound ? 0.3f : Mathf.Min(2.5f, Settings.Between(Settings.AbandonAssessMin, Settings.AbandonAssessMax) * Encounter.AssessFactor(s));
                s.LeftDecideAt = now + assess;
                s.LeftCancelLogged = false;
                s.TLeftNoticed = now;
                if (s.TLeftCue < 0f) s.TLeftCue = now;
                if (Settings.LogEncounter.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: notices {Restraint.Name(captor)} {(bound ? $"hasn't guarded him for {(s.Ignored ? IgnoredBound : LeftBound):0} s" : s.LeftWhy)}{(s.Ignored ? " (ignored his surrender)" : "")} ({dist:0} m, " +
                        $"cues for {now - s.TLeftCue:0.0} s); reacts in {assess:0.0} s (nerve {s.Nerve:0.00}{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}{(s.Kneel == KneelPhase.Down ? ", on his knees" : "")})");
                return;
            }
            if (s.LeftEvidence <= 0f && !bound) { Cancel(s, $"{Restraint.Name(captor)} has his gun on him again"); return; }
            if (now < s.LeftDecideAt) return;
            s.LeftDecideAt = -1f;
            LeftDecide(s, captor, seen, dist);
        }

        // 0.25.0: a player (not his ex-captor's squad) close by with a clear line, looking his way:
        // being left by the bot doesn't mean nobody has him covered.
        private bool PlayerCovers(out string who)
        {
            who = null;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return false;
            Vector3 eye = Perception.Eye(Me);
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p.IsAI || p == Me) continue;
                if ((p.Position - Me.Position).sqrMagnitude > 25f * 25f) continue;
                if (Overwatch.Visibly(p, Me, Settings.WatchAngle.Value) != null) continue;
                if (!Perception.LineClear(eye, Perception.Eye(p))) continue;
                who = Restraint.Name(p);
                return true;
            }
            return false;
        }

        // 0.28.0: another bot (not his captor) who decided to hold fire at him has him at gunpoint:
        // close, a clear line, gun on him. He is still covered.
        private bool ObserverCovers(SurrenderState s, Player captor, out string who)
        {
            who = null;
            var map = Restraint.EntriesFor(Me);
            if (map == null) return false;
            Vector3 eye = Perception.Eye(Me);
            foreach (RestraintEntry e in map.Values)
            {
                Player o = e.Observer;
                if (!e.Holds || o == captor || !Perception.Alive(o) || e.Squad) continue;   // squadmates: SquadCovers
                if (s.Ignored && o.AIData?.BotOwner?.BotsGroup != null && o.AIData.BotOwner.BotsGroup == captor?.AIData?.BotOwner?.BotsGroup) continue;
                if ((o.Position - Me.Position).sqrMagnitude > 25f * 25f) continue;
                if (!Presence.InView(s, Perception.Eye(o)) || !Perception.LineClear(eye, Perception.Eye(o))) continue;
                if (Overwatch.Visibly(o, Me, Settings.WatchAngle.Value) != null) continue;
                who = Restraint.Name(o);
                return true;
            }
            return false;
        }

        private static void Cancel(SurrenderState s, string why)
        {
            s.LeftDecideAt = -1f;
            if (!s.LeftCancelLogged && Settings.LogEncounter.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: thought he was left, but {why}: keeps his hands up");
            s.LeftCancelLogged = true;
        }

        private void LeftDecide(SurrenderState s, Player captor, bool seen, float dist)
        {
            Gestures.Cancel(s, "left by his captor");
            float now = Time.time;
            // 0.26.0: the gun his captor left lying at his feet comes before any walk-off or roll.
            if (TryGunAtFeet(s, captor, $"left by {Restraint.Name(captor)}")) return;
            // 0.28.0: ignored: nobody is coming for him, so he takes his own gun back if it is still
            // there, reachable and nobody has him covered (a walk, not a scramble), or draws one he
            // kept; otherwise he walks off to safety. No roll against walking off.
            if (s.Ignored && IgnoredRecover(s, captor)) return;
            var log = new StringBuilder($"{s.Name}: {(s.Ignored ? "ignored" : "left")} by {Restraint.Name(captor)} ({s.LeftWhy}, {dist:0} m), lowers his hands (nerve {s.Nerve:0.00}): ");
            if (s.Ignored)
            {
                if (Settings.LogEncounter.Value || Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo(log.Append("no safe way to his gun: walks off to safety").ToString());
                s.CalmFlight = true;
                s.BeginRetreat($"ignored by {Restraint.Name(captor)}: walks off to safety", emergency: false);
                return;
            }
            var options = new List<Choice>(3);
            Add(options, log, "walk off", Mathf.Lerp(0.8f, 1.3f, Mathf.InverseLerp(0.6f, 1.6f, s.Flee)));
            bool far = !seen || dist > 20f;
            // His own gun: still where he believes it is, close, reachable, his captor well away from it.
            float toGun = s.DroppedWeapon != null ? Perception.Flat(Me.Position, s.DropPosition) : 999f;
            if (s.DroppedWeapon == null) log.Append("his gun: he didn't drop one; ");
            else if (s.GunGone) log.Append("his gun: he saw it's gone; ");
            else if (toGun > 12f) log.Append($"his gun: {toGun:0} m away; ");
            else if (!far) log.Append($"his gun: {Restraint.Name(captor)} still too close ({dist:0} m); ");
            else if (!Perception.Reachable(Me.Position, s.DropPosition)) log.Append("his gun: can't get to it; ");
            else Add(options, log, "get his gun", 0.9f * s.Fight * (0.5f + s.Nerve));
            EquipmentSlot? kept = Arsenal.UsableGun(Me, out string keptWhy);
            if (kept == null) log.Append($"draw: {keptWhy}; ");
            else if (!far) log.Append("draw: his captor too close; ");
            else Add(options, log, "draw " + Arsenal.SlotName(kept), 0.5f * s.Fight * s.Nerve);
            Choice chosen = Pick(options, log);
            if (Settings.LogEncounter.Value || Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());

            if (chosen.Name == "get his gun")
            {
                s.TEscape = now; s.EscapeKind = "goes for his gun";
                s.MarkFlight();
                s.FleeLockUntil = now;   // he wasn't chased off: his own gun isn't off limits
                Kneel.Urge(s, KneelRise.Compliant);
                s.LowerArms(Settings.LowerSeconds.Value);
                s.GraceUntil = now + Settings.Grace.Value;
                s.Order = Order.None;
                s.TargetWeapon = s.DroppedWeapon;
                s.TargetPoint = s.DropPosition;
                s.RecoverTries++;
                s.CalmRearm = true;
                s.SetPhase(Phase.Recovering, "left by his captor: goes back for his own gun");
                return;
            }
            if (chosen.Name != null && chosen.Name.StartsWith("draw") && Arsenal.Draw(s, BotOwner, false))
            {
                s.TEscape = now; s.EscapeKind = "draws";
                Kneel.Urge(s, KneelRise.Compliant);
                Rearm(s, $"left by his captor: draws the {Arsenal.SlotName(kept)} he kept");
                return;
            }
            // 0.24.0: "walk off" was a hand-back on the spot (0 m from where he was held): his own AI
            // then had a man whose ex-captor was still its nearest known enemy, and SAIN, reading a
            // stale loaded gun (see WeaponState), walked him after that captor or threw grenades
            // (0.23.0 raid: #9, #18, #19, #26). Now he walks away first, leg by leg, his route judged
            // against where his ex-captor is and where he is heading (FleePoint), and decides once
            // clear (Regrouping), like any flight.
            s.CalmFlight = true;
            s.BeginRetreat($"left by {Restraint.Name(captor)}: walks off, away from him", emergency: false);
        }

        // ---- 0.26.0: the gun at his feet -----------------------------------------------------------
        //
        // His captor has left (Abandonment -> LeftDecide, or DecideOnLeave) and the gun he made him
        // drop still lies at his feet: picking it up comes first, before any walk-off, regroup or
        // weighted roll (0.25.0 rolled it against "walk off" and only with the captor > 20 m or out of
        // sight; once walking off, Decide refused it as "lies where he was held" for 150 s). Only the
        // real item where it lies now (Arsenal.Locate), picked up by the normal inventory move
        // (Recovering -> PickingUp -> Arsenal.PickUp, one hands request at a time) and drawn. Not when
        // it is gone, out of reach, under fire, or someone has him covered (his ex-captor, one of his
        // squad, a player): then he goes on as before. Afterwards (CalmRearm) no battle cry and no turn
        // to his ex-captor; his own AI decides cover, leaving or a fight.
        private const float AtFeet = 4f;

        private bool TryGunAtFeet(SurrenderState s, Player captor, string context)
        {
            Item gun = s.DroppedWeapon;
            if (gun == null) return false;
            string what = $"his {gun.ShortName.Localized()} at his feet";
            float d = Perception.Flat(Me.Position, s.DropPosition);
            Vector3 at = s.DropPosition;
            string no = null;
            if (!Settings.RecoverOwnWeapon.Value) no = "recovering his own gun is off";
            else if (s.GunGone) no = "he saw it's gone";
            else if (s.Skip.Contains(gun)) no = "failed to get it before";
            else if (d > AtFeet + 2f) no = $"{d:0} m away";
            else if (!Arsenal.Locate(gun, s.DropPosition, 3f, out at)) { s.GunGone = true; no = "it isn't there any more (taken)"; }
            else if ((d = Perception.Flat(Me.Position, at)) > AtFeet) no = $"{d:0.0} m away, not at his feet";
            else if (FireBreak.Blocks(Me)) no = "under fire";
            else if (s.HandsPending || s.DrawPending || s.DropPending) no = "a hands change of ours is still open";
            else if (Endangered(s, captor, at, out string danger)) no = danger;
            else if (!Perception.PathTo(Me.Position, at, out _, out string noPath)) no = $"can't reach it ({noPath})";
            if (no != null)
            {
                Escape.LogRefusal(s, what, no);
                return false;
            }
            float now = Time.time;
            s.TEscape = now; s.EscapeKind = "picks up his gun";
            s.MarkFlight();
            s.FleeLockUntil = now;   // his own gun, left with him: not off limits
            Kneel.Urge(s, KneelRise.Compliant);
            s.LowerArms(Settings.LowerSeconds.Value);
            s.GraceUntil = now + Settings.Grace.Value;
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.TargetWeapon = gun;
            s.TargetPoint = at;
            s.Scrambling = false;
            s.RecoverTries++;
            s.CalmRearm = true;
            s.SetPhase(Phase.Recovering, $"{context}: picks up {what} first ({d:0.0} m)");
            return true;
        }

        // 0.28.0: after an ignored surrender. His own dropped gun within "Ignored: fetch his gun
        // within" (walking distance), still where it fell (Arsenal.Locate), reachable, not under fire,
        // nobody covering him (Endangered): he walks to it and picks it up through the normal path
        // (Recovering -> PickingUp -> draw), calmly (CalmRearm: no battle cry, no turn on anyone).
        // Else a usable gun he kept is drawn calmly. False: nothing safe; the caller walks him off.
        private bool IgnoredRecover(SurrenderState s, Player captor)
        {
            Item gun = s.DroppedWeapon;
            float max = Settings.IgnoredRecoverRange.Value;
            string no = null;
            Vector3 at = s.DropPosition;
            if (gun == null) no = "he didn't drop one";
            else if (!Settings.RecoverOwnWeapon.Value) no = "recovering his own gun is off";
            else if (s.GunGone) no = "he saw it's gone";
            else if (s.Skip.Contains(gun)) no = "failed to get it before";
            else if (Perception.Flat(Me.Position, s.DropPosition) > max) no = $"{Perception.Flat(Me.Position, s.DropPosition):0} m away";
            else if (!Arsenal.Locate(gun, s.DropPosition, 3f, out at)) { s.GunGone = true; no = "it isn't there any more (taken)"; }
            else if (FireBreak.Blocks(Me)) no = "under fire";
            else if (s.HandsPending || s.DrawPending || s.DropPending) no = "a hands change of ours is still open";
            else if (Endangered(s, captor, at, out string danger)) no = danger;
            else if (!Perception.PathTo(Me.Position, at, out Vector3[] path, out string noPath)) no = $"can't reach it ({noPath})";
            else if (Captors.PathLength(path) > max * 1.5f) no = $"{Captors.PathLength(path):0} m to walk";
            float now = Time.time;
            if (no == null)
            {
                s.TEscape = now; s.EscapeKind = "fetches his gun (ignored)";
                s.MarkFlight();
                s.FleeLockUntil = now;   // nobody chased him off it
                Kneel.Urge(s, KneelRise.Compliant);
                s.LowerArms(Settings.LowerSeconds.Value);
                s.GraceUntil = now + Settings.Grace.Value;
                s.Order = Order.None;
                s.DropQueue.Clear();
                s.TargetWeapon = gun;
                s.TargetPoint = at;
                s.Scrambling = false;
                s.RecoverTries++;
                s.CalmRearm = true;
                s.SetPhase(Phase.Recovering, $"ignored by {Restraint.Name(captor)}: lowers his hands and goes back for his {gun.ShortName.Localized()} ({Perception.Flat(Me.Position, at):0.0} m)");
                return true;
            }
            Escape.LogRefusal(s, gun != null ? $"his {gun.ShortName.Localized()} (ignored)" : "his gun (ignored)", no);
            EquipmentSlot? kept = Arsenal.UsableGun(Me, out string keptWhy);
            if (kept != null && !FireBreak.Blocks(Me) && !Endangered(s, captor, Me.Position, out string keptDanger))
            {
                s.CalmRearm = true;
                if (Arsenal.Draw(s, BotOwner, false))
                {
                    s.TEscape = now; s.EscapeKind = "draws (ignored)";
                    Kneel.Urge(s, KneelRise.Compliant);
                    Rearm(s, $"ignored by {Restraint.Name(captor)}: lowers his hands and draws the {Arsenal.SlotName(kept)} he kept");
                    return true;
                }
                s.CalmRearm = false;
            }
            return false;
        }

        // Picking it up now would expose him: someone has him covered, or his ex-captor (in sight)
        // stands by the gun.
        private bool Endangered(SurrenderState s, Player captor, Vector3 gunAt, out string why)
        {
            why = null;
            if (Perception.Alive(captor) && Perception.LineClear(Perception.Eye(captor), Perception.Chest(Me)))
            {
                float cd = Perception.Flat(captor.Position, Me.Position);
                if (cd < 40f && Overwatch.Visibly(captor, Me, Settings.WatchAngle.Value) == null) { why = $"{Restraint.Name(captor)} has his gun on him ({cd:0} m)"; return true; }
                if (Perception.Flat(captor.Position, gunAt) < 5f) { why = $"{Restraint.Name(captor)} is by it"; return true; }
            }
            if (captor != null && Reactions.SquadCovers(s, captor, Me, Settings.WatchAngle.Value, out string who, out _)) { why = $"{who} covers him"; return true; }
            if (PlayerCovers(out who)) { why = $"{who} covers him"; return true; }
            return false;
        }

        // The captor is gone (walked off, out of sight, dead). What he does depends on what he
        // has and who he is: run, slip into cover, draw a gun he kept, or go for his own.
        private void DecideOnLeave(SurrenderState s, Player captor, float dist, string gone)
        {
            Gestures.Cancel(s, "you left");
            // 0.26.0: the gun his captor left lying at his feet comes before run / hide / draw.
            if (TryGunAtFeet(s, captor, gone)) return;
            // Only what he knows: the captor's position if he sees him, else his last sighting.
            Vector3 threat = ThreatPos(s, captor);
            Vector3 threatEye = ThreatEye(s, captor);
            bool seen = captor != null && s.CaptorSeen;
            EquipmentSlot? gun = Arsenal.UsableGun(Me, out string gunWhy);
            float nerve = s.Nerve;

            var options = new List<Choice>(4);
            var log = new StringBuilder($"{s.Name}: {gone}, reacts (nerve {nerve:0.00}{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}" +
                                        $"{(s.LegsBad ? ", legs hurt" : "")}): ");

            Add(options, log, "run", Settings.EscapeWeight.Value * 2f * s.Flee * (s.LegsBad ? 0.4f : 1f));

            if (gun == null) log.Append($"draw: {gunWhy}; ");
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
                // 0.26.0: nobody is guarding him: on his way to cover his own AI takes over as soon as
                // he is out of sight / away (QuickClear), not only once he gets there.
                s.QuickHandBack = Settings.QuickHandBack.Value;
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
            GoHide(s, why, urgent: true);   // breaking away into cover: he hurries there
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
            // 0.24.0: a gun he can fight with (loaded, or ammo for it on him; not broken).
            EquipmentSlot? gun = Arsenal.UsableGun(Me);
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
            else if (!Perception.RetreatPoint(Me, ThreatPos(s, captor), Settings.RetreatDistance.Value, false, out _, anyOnly: true)) log.Append("run: no way out; ");
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
                SetHideTime(s);
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

        // ---- incoming fire (0.21.0, FireBreak.cs) -----------------------------------------

        // Fire reached him or his captor. Held: the hold-up is over (the captor's side was ended by
        // FireBreak); he gets to safety. Already getting away: cover from this fire, then his own
        // AI. A pick-up transaction under way is never cut (it finishes; then the draw and his AI).
        private void UnderFire(SurrenderState s)
        {
            Vector3 threat = s.FireFromKnown ? s.FireFrom : Me.Position + Me.LookDirection * 15f;
            switch (s.Phase)
            {
                case Phase.HandsUp:
                    EscapeFire(s, threat);
                    return;
                case Phase.Recovering:
                    // Going for a gun: carried on only while it is still sane (close, not in the line
                    // of the fire, and not hit since he set off).
                    bool unsafeNow = RecoveryExposed(s, threat, out string exposed);
                    if (s.TargetWeapon != null && !s.FireHit && !unsafeNow)
                    {
                        if (Settings.LogFireBreak.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: fire while going for his gun: keeps going ({exposed})");
                        s.FireRecover = true;
                        s.FireRecoverUntil = Mathf.Max(s.FireRecoverUntil, Time.time + 1.2f + Vector3.Distance(Me.Position, s.TargetPoint) / 3.5f);
                        s.ThreatAt = threat;
                        return;
                    }
                    TakeCoverFromFire(s, threat, s.FireHit ? "hit on the way to his gun: leaves it" : $"fire while going for his gun: leaves it ({exposed})");
                    return;
                case Phase.Retreating:
                case Phase.Regrouping:
                case Phase.Hiding:
                    s.CalmFlight = false;   // fire: no more walking
                    if (DefendSelf(s, threat)) return;
                    if (s.Phase == Phase.Hiding && s.HideFromFire && (s.HideMoving || s.HideSearchPending)) { s.ThreatAt = threat; return; }   // already on his way to cover from it
                    TakeCoverFromFire(s, threat, $"under fire while {(s.Phase == Phase.Retreating ? "getting away" : "out of captivity")}");
                    return;
                default:
                    return;   // PickingUp (transaction finishes), Rearmed / Released (his AI next)
            }
        }

        // The hold-up is broken by fire: he gets to safety. Weighed once (logged), then committed:
        //   cover    away from the fire, sprinting (always possible: no spot -> his own AI at once);
        //   draw     a weapon he still carries, only with his hands free (no draw over a pending
        //            drop or hands change: the busy-hands glitch);
        //   his gun  a dash for his own dropped gun: close, reachable, still lying there, not out
        //            towards the shooter (FireRecoverQuality); abandoned if it turns unsafe.
        // A knife is never drawn under fire (charging the man who is shooting is not survival).
        private void EscapeFire(SurrenderState s, Vector3 threat)
        {
            float now = Time.time;
            Gestures.Cancel(s, "under fire");
            Voice.Say(s, s.FireHit ? VoiceEvent.Hurt : VoiceEvent.Break);
            s.MarkFlight();
            Kneel.Urge(s, KneelRise.Escape);
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.Order = Order.None;
            s.DropQueue.Clear();      // items not yet dropped stay on him; the one in flight lands on its own
            s.ReportAt = -1f;
            s.GraceUntil = now + Settings.Grace.Value;
            s.DrawCommitUntil = 0f;
            s.EscalateAt = 0f;
            s.ThreatAt = threat;
            if (Perception.Alive(s.Captor)) s.CaptorLastSeen = ThreatPos(s, s.Captor);

            float nerve = s.Nerve;
            bool handsBusy = s.DropPending || s.HandsPending || s.DrawPending || !Arsenal.HandsEmpty(Me);
            EquipmentSlot? gun = Arsenal.UsableGun(Me, out string gunWhy);
            bool kneeling = Kneel.Active(s) && s.KneelT > 0.5f;
            var options = new List<Choice>(3);
            var log = new StringBuilder($"{s.Name} under fire ({(s.FireHit ? "hit" : "near miss")}{(s.FireBy != null ? $" from {Restraint.Name(s.FireBy)}" : "")}, " +
                                        $"{Vector3.Distance(Me.Position, threat):0} m{(s.FireFromKnown ? "" : ", direction unknown")}; nerve {nerve:0.00}" +
                                        $"{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}{(kneeling ? ", on his knees" : "")}): ");

            Add(options, log, "cover", Settings.EscapeWeight.Value * 1.5f * s.Flee * (1.3f - 0.3f * nerve) * (s.FireHit ? 1.3f : 1f));
            if (gun == null) log.Append($"draw: {gunWhy}; ");
            else if (handsBusy) log.Append($"draw: hands busy ({Arsenal.DescribeHands(Me)}); ");
            else Add(options, log, "draw " + Arsenal.SlotName(gun),
                Settings.DrawWeight.Value * (0.4f + nerve) * s.Fight * (1f - 0.4f * s.Injury) * (kneeling ? 0.85f : 1f));
            float q = FireRecoverQuality(s, threat, out string qWhy);
            if (q <= 0f) log.Append($"his gun: {qWhy}; ");
            else if (s.DropPending) log.Append("his gun: still leaving his hands; ");
            else Add(options, log, $"his gun ({qWhy})", Settings.ScrambleWeight.Value * q * (0.5f + nerve) * s.Fight * (s.LegsBad ? 0.4f : 1f));

            Choice c = Pick(options, log);
            if (Settings.LogFireBreak.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());

            if (c.Name != null && c.Name.StartsWith("draw") && Arsenal.Draw(s, BotOwner, false))
            {
                Rearm(s, $"draws his {Arsenal.SlotName(gun)} under fire");
                return;
            }
            if (c.Name != null && c.Name.StartsWith("his gun"))
            {
                s.RecoverTries++;
                StartScramble(s, ThreatPos(s, s.Captor), $"under fire, {qWhy}");
                s.FireRecover = true;
                s.FireRecoverUntil = now + 1.2f + Vector3.Distance(Me.Position, s.DropPosition) / 3.5f;
                return;
            }
            TakeCoverFromFire(s, threat, c.Name != null && c.Name.StartsWith("draw") ? "couldn't draw (the game refused): cover from the fire" : "gets to cover from the fire");
        }

        // 0..1: a dash for his own dropped gun under fire. Only what he can know: where it fell
        // (unless he saw it gone), whether the spot is in the shooter's view, whether it lies out
        // towards the shooter; plus whether it is really still there (he'd see that on the way).
        private float FireRecoverQuality(SurrenderState s, Vector3 threat, out string why)
        {
            float range = Settings.FireRecoverRange.Value;
            Item gun = s.DroppedWeapon;
            if (range <= 0f) { why = "off"; return 0f; }
            if (gun == null) { why = "he didn't drop a gun"; return 0f; }
            if (s.GunGone) { why = "he saw it's gone"; return 0f; }
            if (s.Skip.Contains(gun)) { why = "failed to get it before"; return 0f; }
            if (s.RecoverTries >= 2) { why = "tried twice already"; return 0f; }
            float d = Vector3.Distance(Me.Position, s.DropPosition);
            if (d > range) { why = $"{d:0} m away, over {range:0} m under fire"; return 0f; }
            if (ClaimedByOther(s, gun)) { why = "another held bot is going for it"; return 0f; }
            if (!Arsenal.StillThere(gun, s.DropPosition)) { why = "it isn't lying there any more"; return 0f; }
            if (!DropReachable(s)) { why = "no path to it"; return 0f; }
            bool exposed = RecoveryExposed(s, threat, out string ex);
            float k = Mathf.Sqrt(1f - d / range) * (exposed ? 0.35f : 1f);
            why = $"{d:0.0} m from him, {ex}";
            return Mathf.Clamp01(k);
        }

        // Exposed: the spot is in the shooter's view, or it lies out towards him (within 50 deg of
        // the fire's direction and more than 2 m off).
        private bool RecoveryExposed(SurrenderState s, Vector3 threat, out string why)
        {
            Vector3 spot = s.TargetWeapon != null ? s.TargetPoint : s.DropPosition;
            Vector3 toGun = spot - Me.Position; toGun.y = 0f;
            Vector3 toFire = threat - Me.Position; toFire.y = 0f;
            bool towards = toGun.magnitude > 2f && toFire.sqrMagnitude > 1f && Vector3.Angle(toGun, toFire) < 50f;
            bool seen = s.FireFromKnown && Perception.LineClear(threat + Vector3.up * 1.5f, spot + Vector3.up * 0.4f);
            why = towards ? "out towards the fire" : seen ? "in the shooter's view" : "out of the line of fire";
            return towards || seen;
        }

        // 0.24.0: under fire while getting away, with a gun he can fight with on him and free hands:
        // fight back or keep going for cover, weighed once per burst (logged). Drawing is likelier
        // when he was hit, the shooter is close, he is already in the shooter's sight with nowhere
        // hidden near, and with nerve and a fighter's personality; cover is likelier the further
        // the shooter and the more he flees by nature. A draw goes through Arsenal.Draw (one hands
        // operation at a time: nothing while a drop or another draw is open) and Rearm hands him
        // to his own AI once the gun is ready, with no restriction of ours left on his aim or
        // trigger. False: no gun, hands busy, off, or cover chosen.
        private bool DefendSelf(SurrenderState s, Vector3 threat)
        {
            if (!Settings.DefendWhenFleeing.Value) return false;
            float now = Time.time;
            if (now < s.DefendWeighedUntil) return false;
            s.DefendWeighedUntil = now + 2f;
            EquipmentSlot? gun = Arsenal.UsableGun(Me, out string gunWhy);
            bool busy = s.DropPending || s.HandsPending || s.DrawPending || !Arsenal.HandsEmpty(Me) || Kneel.LegsLocked(s);
            float d = Vector3.Distance(Me.Position, threat);
            var log = new StringBuilder($"{s.Name} under fire while {s.Phase} ({(s.FireHit ? "hit" : "near miss")}{(s.FireBy != null ? $" from {Restraint.Name(s.FireBy)}" : "")}, {d:0} m{(s.FireFromKnown ? "" : ", direction unknown")}): ");
            if (gun == null) { if (Settings.LogFireBreak.Value || Settings.LogEscape.Value) HandsAreHighPlugin.Log.LogInfo(log.Append($"fight back: {gunWhy}; -> cover").ToString()); return false; }
            if (busy) { if (Settings.LogFireBreak.Value || Settings.LogEscape.Value) HandsAreHighPlugin.Log.LogInfo(log.Append($"fight back: hands or legs busy ({Arsenal.DescribeHands(Me)}); -> cover").ToString()); return false; }
            bool exposed = s.FireFromKnown && Perception.LineClear(threat + Vector3.up * 1.5f, Perception.Chest(Me));
            float close = Mathf.Lerp(1.6f, 0.5f, Mathf.InverseLerp(8f, 60f, d));
            float fight = Settings.DrawWeight.Value * (0.4f + s.Nerve) * s.Fight * close * (s.FireHit ? 1.5f : 1f) * (exposed ? 1.3f : 0.8f) * (1f - 0.4f * s.Injury);
            float cover = Settings.EscapeWeight.Value * 1.2f * s.Flee * (1.3f - 0.3f * s.Nerve) * (exposed ? 0.8f : 1.2f);
            var options = new List<Choice>(2);
            Add(options, log, $"fight back ({gunWhy})", fight);
            Add(options, log, "cover", cover);
            Choice c = Pick(options, log);
            if (Settings.LogFireBreak.Value || Settings.LogEscape.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());
            if (c.Name == null || !c.Name.StartsWith("fight")) return false;
            StopMoving();
            s.TargetWeapon = null;
            s.Scrambling = false;
            s.HideMoving = false;
            s.HideSearchPending = false;
            if (!Arsenal.Draw(s, BotOwner, false)) return false;
            s.FaceThreat = threat;
            Rearm(s, $"fights back under fire with his {Arsenal.SlotName(gun)}");
            return true;
        }

        // Cover from fire: the nearest reachable spot hidden from where it came from (Hiding), at a
        // sprint; his own AI on arrival, when stuck, out of time, or when there is none. Direction
        // unknown: his own AI at once (it knows who is shooting at him; we don't).
        private void TakeCoverFromFire(SurrenderState s, Vector3 threat, string why)
        {
            s.TargetWeapon = null;
            s.Scrambling = false;
            s.FireRecover = false;
            if (!s.FireFromKnown) { HandBack(s, $"{why}; can't tell where it comes from: his own AI"); return; }
            s.ThreatAt = threat;
            GoHide(s, why, urgent: true, fromFire: true);
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
            // 0.21.0: under fire, the failed dash ends in cover from it (then his own AI), never in
            // standing where the gun was.
            if (s.FireRecover || FireBreak.Blocks(Me)) { TakeCoverFromFire(s, s.ThreatAt, $"gives up on his gun ({why})"); return; }
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
            if (!s.CalmRearm) Voice.Say(s, VoiceEvent.Break);
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

            // 0.24.0: a man left behind walks off (briskly, not a sprint: nobody is chasing him).
            if (!MoveTo(s, s.TargetPoint, sprint: !s.CalmFlight))
            {
                Retry(s, "no path");
                return;
            }
            if (s.CalmFlight && s.Moving) BotOwner.SetTargetMoveSpeed(0.85f);
            if (!check) return;
            if (SpottedAgain(s)) return;
            // 0.25.0: on his way: his own AI from here (a kept gun is still drawn first: Decide).
            if (QuickClear(s, out string quick))
            {
                if (!s.QuickEnded) EndFlight(s, $"on his way, {quick}: his own AI takes over");
                s.QuickEnded = true;   // Decide may wait a frame for search budget: logged once
                Decide(s);   // draw / cover under fire / a safe gun / else HandBack mid-stride
                return;
            }

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
                // 0.24.0: also where the captor he sees is walking to (his heading over the look-ahead):
                // a man leaving the same way as his ex-captor, or beside him, takes another way.
                if (Perception.Alive(c) && Perception.Sees(Me, c, Vector3.Distance(Me.Position, c.Position)))
                {
                    Vector3 ahead = Escape.Ahead(c);
                    float near = Mathf.Min(Perception.Flat(c.Position, s.TargetPoint), Perception.Flat(ahead, s.TargetPoint));
                    float beside = Perception.Flat(c.Position, Me.Position);
                    bool sameWay = Escape.SameWay(Me, c, s.TargetPoint);
                    if ((near < 10f || (sameWay && beside < 15f)) && Perception.TakeHeavy())
                    {
                        s.FleeReplans++;
                        s.CaptorLastSeen = c.Position;
                        s.CaptorLastVel = Escape.FlatVelocity(c);
                        if (Settings.LogDecisions.Value || Settings.LogEscape.Value)
                            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {Restraint.Name(c)} is {near:0} m from where he is going{(sameWay ? $" and heading the same way ({beside:0} m from him)" : "")}: another way");
                        if (PickRetreat(s)) { s.NextMoveOrder = -1f; ResetProgress(s); }
                    }
                }
            }
        }

        // 0.17.0: out of the captivity area (FleePoint), or the next leg towards it through cover.
        private bool PickRetreat(SurrenderState s)
        {
            if (!s.HeldSet) { s.HeldAt = Me.Position; s.HeldSet = true; s.FleeSince = s.LegSince = Time.time; s.FleeFrom = Me.Position; }
            float radius = Settings.FleeDistance.Value;
            // Only what he perceives: the captor where he sees him now (and how he is moving), else
            // where he last saw him (standing still, as far as he knows).
            bool sees = Perception.Alive(s.Captor) && Perception.Sees(Me, s.Captor, Vector3.Distance(Me.Position, s.Captor.Position));
            if (sees) { s.CaptorLastSeen = s.Captor.Position; s.CaptorLastVel = Escape.FlatVelocity(s.Captor); }
            Vector3 threat = s.CaptorLastSeen;
            Vector3 vel = sees ? s.CaptorLastVel : Vector3.zero;
            if (Perception.FleePoint(Me, threat, s.HeldAt, radius, out s.TargetPoint, out bool leg, out string why, vel))
            {
                if (Settings.LogDecisions.Value || Settings.LogEscape.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {(s.CalmFlight ? "walks off" : "flees")} to a spot {Perception.Flat(s.TargetPoint, Me.Position):0} m away, {Perception.Flat(s.TargetPoint, s.HeldAt):0} m from where he was held " +
                        $"({(leg ? $"a leg through cover {s.FleeLegs + 1}: no way straight out of {radius:0} m" : $"out of the {radius:0} m area")}; {Perception.Flat(s.TargetPoint, threat):0} m from the threat{(sees ? "" : " (last seen there)")}, " +
                        $"{(Perception.LineClear(threat + Vector3.up * 1.5f, s.TargetPoint + Vector3.up * 1.2f) ? "in its sight" : "hidden from it")}; {why})");
                return true;
            }
            if (Settings.LogDecisions.Value || Settings.LogEscape.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {why}");
            GoHide(s, "no way out");
            return false;
        }

        // 0.17.0: a leg reached, or out of time on it: on out of the area, or done.
        // 0.21.0: also done once he is out of his captor's sight and well clear of where he was held
        // (12 m): a hidden spot is what the flight was for; the area radius is no longer a goal he
        // must reach leg after leg. At most 3 legs (was 5).
        // 0.24.0: done only once he is clear: "Escape ends this far from the threat" from where he was
        // held and 3/4 of it from the threat he last saw, out of its sight; or hidden and well away
        // (60% of it from where he was held, half of it from the threat). Up to 4 legs (was 3, and
        // 12 m out of sight was enough: the released men of the 0.23.0 raid stopped 5-15 m away).
        private void LegDone(SurrenderState s, string why)
        {
            float radius = Settings.FleeDistance.Value;
            float sep = Perception.Flat(Me.Position, s.HeldAt);
            Vector3 threat = ThreatPos(s, s.Captor);
            float fromThreat = Perception.Flat(Me.Position, threat);
            bool unseen = !Perception.CanSee(Me, threat) && !CaptorSeesMe(s);
            bool hidden = unseen && sep >= Mathf.Max(12f, radius * 0.6f) && fromThreat >= radius * 0.5f;
            bool clear = sep >= radius && fromThreat >= radius * 0.75f && unseen;
            if (!clear && !hidden && s.FleeLegs < 3)
            {
                s.FleeLegs++;
                s.LegSince = Time.time;
                s.FleeReplans = 0;
                if (Settings.LogDecisions.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {why} {sep:0} m from where he was held, still inside {radius:0} m: keeps going (leg {s.FleeLegs + 1})");
                s.NextMoveOrder = 0f;
                ResetProgress(s);
                return;
            }
            EndFlight(s, clear ? $"{why}, clear" : hidden ? $"{why}, out of his captor's sight" : $"{why}, after {s.FleeLegs + 1} legs");
            Regroup(s, why);
        }

        // His captor (alive) has a clear line to him now: the captor's eyes, not his.
        private bool CaptorSeesMe(SurrenderState s)
        {
            Player c = s.Captor;
            return Perception.Alive(c) && Perception.LineClear(Perception.Eye(c), Perception.Chest(Me));
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

        // 0.21.0: away from his captor: he decides now (a kept gun, his own or another gun near, or
        // his own AI). 0.20 and earlier stopped him crouched (pose 0.6) for 4-12 s, then hid him in
        // a crouch (pose 0) and looked again every 8 s for the rest of the raid when he had nothing
        // to fight with: the "runs, stops, crouches" every release ended in. No waiting here.
        private void Regroup(SurrenderState s, string why)
        {
            StopMoving();
            s.RegroupUntil = Time.time;
            s.SetPhase(Phase.Regrouping, why);
            Decide(s);
        }

        // A decision waiting for a frame with path budget (Decide returned without changing phase).
        private void Regrouping(SurrenderState s, bool check)
        {
            Hold(1f);
            if (!check) return;
            if (SpottedAgain(s)) return;
            Decide(s);
        }

        // How to get back in the fight, best option first.
        // 0.24.0: only a gun he can fight with counts (Arsenal.UsableGun: loaded or ammo for it on
        // him, not broken); a grenade never does. Under fire, safety before any fetch (the fetch is
        // refused, he goes to cover). Every refused recovery is logged once with its reason; a fetch
        // whose route passes the threat he last saw is refused like an escape route would be. The
        // capture area is off limits for "Remembers the capture for" (not FleeCommit alone).
        // The knife comeback only when the man he fled is dead: an unarmed man with a knife walking
        // back at a gun was SAIN's melee charge (IsMelee) on the ex-captor.
        private void Decide(SurrenderState s)
        {
            EquipmentSlot? kept = Arsenal.UsableGun(Me, out string keptWhy);
            if (!s.EquipLogged && (Settings.LogEscape.Value || Settings.LogDecisions.Value))
            {
                s.EquipLogged = true;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: away from his captor ({Perception.Flat(Me.Position, s.HeldSet ? s.HeldAt : Me.Position):0} m from where he was held), what he has: {Arsenal.Equipment(Me)}");
            }
            // 0.28.2: a usable gun already in his hands (a pick-up the game put straight there) needs
            // no draw: asking for one is refused (hands busy) and 0.28.1 then handed him back as if he
            // couldn't arm himself.
            if (Arsenal.GunInHands(Me) && !s.DrawPending && !s.HandsPending && !s.DropPending)
            {
                Rearm(s, $"already has his {Me.HandsController.Item?.ShortName?.Localized() ?? "gun"} in his hands");
                return;
            }
            if (kept != null && Arsenal.Draw(s, BotOwner, false))
            {
                Rearm(s, $"draws the {Arsenal.SlotName(kept)} he kept ({keptWhy})");
                return;
            }

            // Under fire: cover first, no fetching.
            if (FireBreak.Blocks(Me) && s.ThreatAt != Vector3.zero)
            {
                Escape.LogRefusal(s, "any gun", "under fire: cover before a gun");
                TakeCoverFromFire(s, s.ThreatAt, "under fire after getting away: cover before a gun");
                return;
            }

            // 0.17.0: fled from captivity: no going back into that area for a gun until the commitment
            // runs out (no escape, linger, recapture loop). Weapons he carries stay options (above).
            float lockUntil = Mathf.Max(s.FleeLockUntil, s.HeldSet && s.FleeSince > 0f ? s.FleeSince + Settings.EscapeMemory.Value : 0f);
            bool committed = s.HeldSet && Time.time < lockUntil;
            float area = Mathf.Max(Settings.FleeDistance.Value, Settings.KeepAwayRadius.Value);
            bool ownInArea = committed && s.DroppedWeapon != null && Perception.Flat(s.DropPosition, s.HeldAt) < area;
            // 0.21.0: two tries at a gun after getting away, then his own AI (no endless fetching).
            bool fetch = s.RecoverTries < 2;
            // What he believes: unless he saw it gone, his gun is where he dropped it.
            string refuse = null;
            if (s.DroppedWeapon != null)
            {
                if (ownInArea) refuse = $"it lies where he was held (off limits for {lockUntil - Time.time:0} s more)";
                else if (!fetch) refuse = "tried twice already";
                else if (!Settings.RecoverOwnWeapon.Value) refuse = "recovering his own gun is off";
                else if (s.Skip.Contains(s.DroppedWeapon)) refuse = "failed to get it before";
                else if (s.GunGone) refuse = "he saw it's gone";
                else if (Vector3.Distance(s.DropPosition, s.CaptorLastSeen) <= Settings.AvoidRadius.Value) refuse = $"{Vector3.Distance(s.DropPosition, s.CaptorLastSeen):0} m from where he last saw his captor";
                else if (!Perception.PathTo(Me.Position, s.DropPosition, out Vector3[] gunPath, out string noPath)) refuse = $"no route ({noPath})";
                else if (Perception.RoutePassesNear(gunPath, s.CaptorLastSeen, 8f)) refuse = "the way there passes where he last saw his captor";
                if (refuse != null) Escape.LogRefusal(s, $"his {s.DroppedWeapon.ShortName.Localized()}", refuse);
            }
            if (s.DroppedWeapon != null && refuse == null)
            {
                s.TargetWeapon = s.DroppedWeapon;
                s.TargetPoint = s.DropPosition;
                s.RecoverTries++;
                s.SetPhase(Phase.Recovering, "going back for his own gun");
                return;
            }

            // A 60 m sweep of loose guns and bodies is the heaviest thing he does: it waits for
            // a frame with budget, and after finding nothing he doesn't sweep again for 20 s
            // (a hiding bot looks around every 8 s; it used to sweep every time).
            if (fetch && Settings.ScavengeWeapons.Value && Time.time >= s.NextScavengeScan)
            {
                if (!Perception.TakeHeavy()) return;   // decides next check (0.25 s)
                Item found = Arsenal.FindWeapon(Me, s.CaptorLastSeen, Settings.ScavengeAvoid.Value,
                    Settings.ScavengeRadius.Value, s.Skip, out Vector3 at);
                if (found == null) s.NextScavengeScan = Time.time + 20f;
                if (found != null && committed && Perception.Flat(at, s.HeldAt) < area)
                {
                    // In the area he just fled: not now (looked at again once the commitment ends).
                    Escape.LogRefusal(s, found.ShortName.Localized(), "lies in the area he just fled");
                    s.NextScavengeScan = lockUntil;
                    found = null;
                }
                if (found != null)
                {
                    if (Perception.PathTo(Me.Position, at, out Vector3[] fp, out string fw) && !Perception.RoutePassesNear(fp, s.CaptorLastSeen, 8f))
                    {
                        s.TargetWeapon = found;
                        s.TargetPoint = at;
                        s.RecoverTries++;
                        s.SetPhase(Phase.Recovering, $"going for {found.ShortName.Localized()} {Vector3.Distance(at, Me.Position):0} m away");
                        return;
                    }
                    Escape.LogRefusal(s, found.ShortName.Localized(), fw != null ? $"no route ({fw})" : "the way there passes where he last saw his captor");
                    s.Skip.Add(found);
                }
            }

            bool captorGone = !Perception.Alive(s.Captor);
            if (Settings.HighNerveKnife.Value && s.Nerve >= Settings.HighNerve.Value && Arsenal.HasMelee(Me))
            {
                if (captorGone && Arsenal.Draw(s, BotOwner, true))
                {
                    Rearm(s, $"no gun to find, his captor is dead: comes back with his knife (nerve {s.Nerve:0.00})");
                    return;
                }
                if (!captorGone) Escape.LogRefusal(s, "his knife", "a knife against the armed man he fled: keeps getting away");
            }

            s.TargetWeapon = null;
            // 0.21.0: nothing to fight with: his own AI has him from here (it moves, regroups, finds
            // a weapon, fights or avoids with the whole raid in view). He used to crouch in a hide
            // and look again every 8 s for the rest of the raid.
            HandBack(s, kept != null ? $"couldn't draw his {Arsenal.SlotName(kept)} (the game refused)" : "away, nothing to fight with");
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
            // 0.21.0: a dash for his gun under fire has its own short budget (1.2 s + 1 s per 3.5 m).
            if (s.FireRecover && Time.time > s.FireRecoverUntil)
            {
                FailScramble(s, "took too long under fire");
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
            // 0.28.2: the game can put a picked-up gun straight into his hands (0.28.1 raid: a Scav's
            // TOZ-106 was "holding TOZ-106" when the draw was asked). A draw then is refused (hands
            // busy) and 0.28.1 gave up on the gun he was holding, handing him back as an unarmed man.
            if (InHands(weapon))
            {
                Rearm(s, $"rearmed with {weapon.ShortName.Localized()}{(s.Scrambling ? " after a scramble" : "")} (already in his hands)");
                return;
            }
            if (Arsenal.Draw(s, BotOwner, false))
                Rearm(s, $"rearmed with {weapon.ShortName.Localized()}{(s.Scrambling ? " after a scramble" : "")}");
            else
                GiveUpOn(s, "has the gun but couldn't draw it");
        }

        private bool InHands(Item weapon) =>
            weapon != null && Me.HandsController is Player.FirearmController fc && fc.Item == weapon;

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
            if (FireBreak.Blocks(Me)) { TakeCoverFromFire(s, s.ThreatAt, $"gave up on the gun under fire: {why}"); return; }
            // 0.21.0: decides again on the next check (another gun near, or his own AI), standing; not
            // 1 s crouched. Not here and now: this can run inside the game's pick-up callback.
            StopMoving();
            s.RegroupUntil = Time.time;
            s.SetPhase(Phase.Regrouping, $"gave up on the gun: {why}");
        }

        // 0.21.0: a move to cover with a clear end. Out of sight of what he hides from (ThreatAt: his
        // captor's last sighting, or the fire): a reachable hidden spot (the nearest one for fire,
        // CoverPoint; a hiding place a little further for slipping away, RetreatPoint), at a sprint
        // when urgent; his own AI has him once he is there, stuck, out of time (Move to cover at
        // most), or when there is no spot (or he is already out of sight). 0.20 and earlier stood
        // him in a crouch there and looked again every 8 s, for good when he had no gun.
        private void GoHide(SurrenderState s, string why, bool urgent = false, bool fromFire = false)
        {
            StopMoving();
            s.HideUrgent = urgent;
            s.HideFromFire = fromFire;
            if (!fromFire) s.ThreatAt = s.CaptorLastSeen;
            s.HideUntil = Time.time + Settings.FireCoverSeconds.Value;
            s.SetPhase(Phase.Hiding, why);
            // The cover search (up to PathCandidates path solves) runs from Hiding on a frame
            // with heavy-search budget, usually this one or the next.
            s.HideSearchPending = Perception.CanSee(Me, s.ThreatAt);
        }

        // 0.28.3: a hide not driven by fire had the FIRE move's limit ("Fire: move to cover at most",
        // 8 s) for a spot up to CoverRadius x 1.3 (26 m) away. In the live 0.28.2 raid every opening
        // hide (Санька, Давид, Fantia x2; hurt legs) ended "8 s on the way to cover", stopped short in
        // the open. Now the time a man in his state needs to get there (never less than the fire
        // limit, at most 25 s); a man who stops making progress still ends on Stalled. Called once
        // the spot is known: the cover search below, or TakeOpening's hide, which brings its own
        // spot (the first 0.28.3 build only set it in the search, so opening hides kept 8 s).
        private void SetHideTime(SurrenderState s)
        {
            if (s.HideFromFire) return;
            float speed = s.LegsBad ? 1.6f : s.HideUrgent ? 4f : 2.8f;
            float need = Perception.Flat(Me.Position, s.TargetPoint) * 1.4f / speed + 3f;
            s.HideUntil = Time.time + Mathf.Clamp(need, Settings.FireCoverSeconds.Value, 25f);
        }

        private void Hiding(SurrenderState s, bool check)
        {
            if (s.HideSearchPending)
            {
                if (!Perception.TakeHeavy()) { Hold(1f); return; }
                s.HideSearchPending = false;
                bool found = s.HideFromFire
                    ? Perception.CoverPoint(Me, s.ThreatAt, Settings.FireCoverRadius.Value, null, out s.TargetPoint, out _)
                    : Perception.RetreatPoint(Me, s.ThreatAt, Settings.CoverRadius.Value, true, out s.TargetPoint);
                if (!found)
                {
                    HandBack(s, $"no reachable cover within {(s.HideFromFire ? Settings.FireCoverRadius.Value : Settings.CoverRadius.Value):0} m");
                    return;
                }
                s.HideMoving = true;
                SetHideTime(s);
                if (Settings.LogDecisions.Value || (s.HideFromFire && Settings.LogFireBreak.Value))
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: cover {Vector3.Distance(Me.Position, s.TargetPoint):0} m away, hidden from {(s.HideFromFire ? "the fire" : Restraint.Name(s.Captor))}");
            }
            if (!s.HideMoving)
            {
                HandBack(s, "out of sight of the threat already");
                return;
            }
            if (!MoveTo(s, s.TargetPoint, sprint: s.HideUrgent))
            {
                HandBack(s, "no route to cover");
                return;
            }
            if (!check) return;
            if (SpottedAgain(s)) return;
            if (!s.HideFromFire && QuickClear(s, out string quick)) { HandBack(s, $"on his way to cover, {quick}: his own AI takes over"); return; }
            if (Arrived(s.TargetPoint, Settings.ArrivalTolerance.Value)) { HandBack(s, s.HideFromFire ? "in cover from the fire" : "in cover"); return; }
            if (Stalled(s)) { HandBack(s, "stuck on the way to cover"); return; }
            if (Time.time > s.HideUntil && !Kneel.LegsLocked(s)) HandBack(s, $"{Time.time - s.PhaseSince:0} s on the way to cover, {Perception.Flat(Me.Position, s.TargetPoint):0} m short");
        }

        // A human player close by aims straight at him for a moment: hands go up again.
        // With a gun still on him, a bold bot may draw instead.
        private bool SpottedAgain(SurrenderState s)
        {
            if (Time.time < s.GraceUntil) return false;
            // 0.21.0: not while the fire that broke his hold-up is still on (no recapture loop).
            if (FireBreak.Blocks(Me)) { s.AimedBy = null; return false; }
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
            // 0.21.0: doors. The game opens a door on a bot's path only when the logic driving him
            // asks (vanilla MoveNode.DoorOpen -> BotDoorOpener.UpdateDoorInteractionStatus, every
            // frame); SAIN does it inside its own mover. Our layer never asked, and a shut door
            // doesn't cut the NavMesh (NavMeshDoorLink carves only for an open leaf), so BotMover
            // carried him straight through it. While the opener works a door (its own NearDoor
            // mover state, the interaction), he is its: no new path, no sprint, no stall count.
            if (s.Moving && DoorBusy())
            {
                BotOwner.Sprint(false);
                s.SprintHeld = sprint;
                ResetProgress(s);
                return true;
            }
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
                // 0.21.0: during the short blend back from a kneel the turn to his way stays within
                // reach of his planted knees (SteerTo); the full turn follows a moment later.
                if (Kneel.Posing(s)) SteerTo(s, s.PathTarget - Me.Position, PivotSpeed);
                else BotOwner.Steering.LookToMovingDirection();
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
            if (sprint && !Kneel.Posing(s)) BotOwner.Steering.LookToMovingDirection();
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

        // 0.21.0: on the same floor too (a spot on the floor above or below, straight overhead,
        // used to count as reached).
        private bool Arrived(Vector3 point, float within)
        {
            Vector3 d = point - Me.Position;
            if (Mathf.Abs(d.y) > 1.6f) return false;
            d.y = 0f;
            return d.magnitude <= within;
        }

        // 0.21.0: the game's door opener, asked every moving frame as vanilla's own move nodes do;
        // true while it has him at a door (walking to the handle, opening, stepping through).
        private bool DoorBusy()
        {
            BotDoorOpener d = BotOwner.DoorOpener;
            if (d == null) return false;
            DoorInteractionStatus st;
            try { st = d.UpdateDoorInteractionStatus(); }
            catch { return false; }
            return st == DoorInteractionStatus.OpeningDoor || d.Interacting || BotOwner.Mover.CurrentState == EBotMoverState.NearDoor;
        }

        public override void BuildDebugText(StringBuilder sb)
        {
            if (SurrenderRegistry.TryGet(Me, out SurrenderState s))
                sb.AppendLine($"HandsAreHigh: {s.Phase}, order {s.Order}, arms {s.Weight:0.00}, nerve {s.Nerve:0.00}{(Kneel.Describe(s) is string k ? ", " + k : "")}");
        }
    }
}
