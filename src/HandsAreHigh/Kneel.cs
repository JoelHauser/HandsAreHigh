using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Kneeling with the hands behind the head (0.11.0; styles, rise intents, knee brace 0.12.0; brace rework and transition fixes 0.12.1).
    //
    // What the game offers (verified in the 0.16.9 client and its bundles):
    //   - No kneel, sit, get-up or brace clip exists in player_anim_controller (UnityPy search of
    //     every clip name). Third-person bodies only have stand / crouch (PoseLevel 0..1) / prone.
    //   - BotOwner.SetPose -> BotMover.TargetPose; the mover eases Player.PoseLevel towards it
    //     (ChangePose, 0.2 of the gap per frame). MovementContext.SetPoseLevel sizes the capsule:
    //     height Lerp(1.2, 1.6, pose), so a full crouch is the game's own 1.2 m capsule.
    //   - Player.VisualPass (LateUpdate, living players only) runs the animator's result through
    //     FBBIK / LimbIK for the hands; the FinalIK grounder (Player.Grounder) is disabled in
    //     InitVisuals, so nothing re-poses the legs after the animator. Our postfix (ArmPosePatch)
    //     is the last writer before render, and the animator overwrites every bone the next
    //     frame: nothing we write accumulates, and nothing is left on a corpse.
    //   - Skeleton (skeleton.bundle): Base HumanPelvis -> HumanLThigh1 (hip) -> HumanLThigh2
    //     (mid-thigh twist, 0.228 m) -> HumanLCalf (knee, 0.228 m on) -> HumanLFoot (ankle,
    //     0.46 m) -> HumanLToe (0.155 m). Hit colliders are children of these bones.
    //
    // So: procedural, fully ours from the first frame. When he starts down, his standing pose is
    // captured (body-local) and every frame after that the pelvis, torso and legs are solved
    // from that reference, not from whatever the animator plays underneath. The game's crouch is
    // only asked for once he is low (CrouchAtT) so the capsule follows; its squat never shows
    // (0.11.0 followed the animated crouch, which looked like crouching before kneeling).
    //
    // Choreography (one progress t, 0 standing .. 1 kneeling; windows per style):
    //   lead window   pelvis down; lead foot swings back, lead knee down (half-kneel)
    //                 meanwhile the trail foot steps forward (lifted arc) to be the front foot
    //   trail window  front knee down beside the first
    //   lean window   torso forward for balance in the middle, upright at the end
    //   hands window  hands from up to the back of the head
    // Rising runs t backwards with a rise style picked when he starts up from the kneel: front
    // foot planted, a push up over it (optionally the front-side hand braced on that thigh), the
    // foot steps back. A reversal or a change of urgency mid-way keeps the style and only changes
    // speed, so it is continuous. The pose depends only on (t, style), never on the direction.
    internal enum KneelPhase { Standing, GoingDown, Down, GettingUp }
    internal enum KneelRise { Compliant, Defensive, Escape }

    public enum KneelInterrupt { Reverse, Hit, Run, Draw, Kill }
    public enum KneelInterruptDuring { GoingDown, GettingUp }
    public enum KneelStyleChoice { Auto, Steady, Frightened, OneKneeFirst }
    public enum KneelRiseChoice { Auto, Compliant, Defensive, Escape }
    public enum KneelBraceChoice { Auto, Always, Never }
    public enum KneelEscapeChoice { Auto, Commit, BackDown, StandUp }
    // 14. Debug "Kneel pose isolate": the one contributor switched off (All: none off).
    public enum KneelPoseIsolate { All, LookAlign, PelvisSquare, TorsoSquare, LegHinge, Head, Hands, BodyOnly }

    internal sealed class KneelLeg
    {
        internal Transform Hip, Knee, Foot, Toe;
        internal float Thigh, Shin, FootLen;
        internal float Side;   // -1 left, +1 right
        // 0.15.0: bones between hip and knee and between knee and ankle (the thigh twist bone):
        // the animator rolls them differently in its crouch; they keep the standing pose's local
        // rotation for the whole kneel (captured with the reference).
        internal Transform[] Mid;
        internal Quaternion[] MidRef;
    }

    internal sealed class KneelRig
    {
        internal Transform Pelvis, Head;
        internal readonly KneelLeg[] Legs = new KneelLeg[2];   // 0 left, 1 right
        internal float HipHalfWidth;
        internal float TorsoLength;   // hips to head bone
        // Hand bones for laying the bracing palm flat (0 left, 1 right): the middle finger's root
        // (finger direction) and the thumb's root (which way the palm faces). skeleton.bundle:
        // fingers run along the palm's local -X, Digit31 0.108 m out, fingertip ~0.20 m.
        internal readonly Transform[] Middle = new Transform[2], Thumb = new Transform[2];
        // Forearm twist bones (Forearm1 -> Forearm2 -> Forearm3 -> Palm, 0.084 m apart): the
        // roll in the palm's turn is shared with them so the wrist doesn't candy-wrap.
        internal readonly Transform[] TwistA = new Transform[2], TwistB = new Transform[2];
        internal bool HandOk;

        internal bool Valid()
        {
            if (Pelvis == null || Head == null) return false;
            for (int i = 0; i < 2; i++)
            {
                KneelLeg l = Legs[i];
                if (l == null || l.Hip == null || l.Knee == null || l.Foot == null || l.Toe == null) return false;
            }
            return true;
        }
    }

    // Timing of one transition. Windows are in t (0 standing .. 1 kneeling); the brace windows
    // (rises only, t falling): BraceInB the hand sets off, BraceInA it is on the thigh; BraceOutB
    // it starts to leave, BraceOutA it is free.
    internal sealed class KneelStyle
    {
        internal string Name;
        internal float LeadA, LeadB, TrailA, TrailB, HandsA, HandsB, LeanA, LeanB, LeanDeg, Step;
        internal float BraceInA, BraceInB, BraceOutA, BraceOutB;
        // Rising: t at which his legs go back to the game when he is about to walk (0.13.0). The
        // earliest point where the hips are within ~12 cm of standing height (lead window
        // smoothstep <= 0.27) and the trailing knee is still up: both knees well off the ground,
        // weight over the planted front foot. Computed from each style's windows.
        internal float Free;
        internal bool CanBrace => BraceInB > 0f;
    }

    internal static class Kneel
    {
        // Descents. Offline-checked (scratchpad kneel_sim2.py): nothing under the ground, knee
        // 44-129 deg, continuous at 20-60 fps, reversals continuous.
        internal static readonly KneelStyle Steady = new KneelStyle
        { Name = "steady", LeadA = 0.04f, LeadB = 0.52f, TrailA = 0.50f, TrailB = 0.86f, HandsA = 0.76f, HandsB = 1f, LeanA = 0.04f, LeanB = 0.84f, LeanDeg = 12f, Step = 0.30f, Free = 0.20f };
        internal static readonly KneelStyle Frightened = new KneelStyle
        { Name = "frightened", LeadA = 0.02f, LeadB = 0.46f, TrailA = 0.24f, TrailB = 0.70f, HandsA = 0.38f, HandsB = 0.82f, LeanA = 0.02f, LeanB = 0.72f, LeanDeg = 18f, Step = 0.14f, Free = 0.17f };
        internal static readonly KneelStyle OneKnee = new KneelStyle
        { Name = "one knee first", LeadA = 0.03f, LeadB = 0.36f, TrailA = 0.62f, TrailB = 0.90f, HandsA = 0.80f, HandsB = 1f, LeanA = 0.03f, LeanB = 0.88f, LeanDeg = 10f, Step = 0.34f, Free = 0.14f };
        // Rises. Brace (0.12.1, offline brace_sim.py, 20-60 fps): the hand sets off as the hands
        // leave his head and lands on the front thigh as that foot plants, when the hips have
        // risen 0-3%; it holds through the push to ~45-50% risen while the IK goal stays within
        // 0.86-0.92 of his arm, the elbow straightening (compliant 89->47 deg, defensive 79->56;
        // escape ~constant: his knee is still rising into the hand), and leaves before the arm
        // would be straight (1.0 at ~55-65% risen). 0.12.0 reached in 0.1 of t (70 ms on an
        // escape) and let go at ~12% risen: a snap down and a snap off. Urgent rises are free by
        // 0.17-0.18 (> the 0.15 leg hand-back); the compliant one releases until 0.03 (the hand
        // goes all the way back up to hands-up), so an urgent switch mid-brace lets go by time.
        internal static readonly KneelStyle RiseCompliant = new KneelStyle
        { Name = "compliant", LeadA = 0.05f, LeadB = 0.50f, TrailA = 0.46f, TrailB = 0.84f, HandsA = 0.80f, HandsB = 1f, LeanA = 0.08f, LeanB = 0.86f, LeanDeg = 16f, Step = 0.30f,
          BraceInA = 0.50f, BraceInB = 0.88f, BraceOutA = 0.03f, BraceOutB = 0.28f, Free = 0.20f };
        internal static readonly KneelStyle RiseDefensive = new KneelStyle
        { Name = "defensive", LeadA = 0.06f, LeadB = 0.48f, TrailA = 0.36f, TrailB = 0.78f, HandsA = 0.86f, HandsB = 1f, LeanA = 0.06f, LeanB = 0.80f, LeanDeg = 18f, Step = 0.26f,
          BraceInA = 0.44f, BraceInB = 0.78f, BraceOutA = 0.21f, BraceOutB = 0.31f, Free = 0.20f };
        internal static readonly KneelStyle RiseEscape = new KneelStyle
        { Name = "escape", LeadA = 0.10f, LeadB = 0.52f, TrailA = 0.30f, TrailB = 0.74f, HandsA = 0.88f, HandsB = 1f, LeanA = 0.10f, LeanB = 0.92f, LeanDeg = 24f, Step = 0.36f,
          BraceInA = 0.50f, BraceInB = 0.80f, BraceOutA = 0.25f, BraceOutB = 0.36f, Free = 0.24f };
        // 0.13.0: urgent brace windows moved up so the hand is off before the legs are handed back
        // (defensive free at 0.21 > 0.20, escape at 0.25 > 0.24). Not re-run offline (brace_sim.py);
        // the stretch let-go (UpdateBrace) still covers an arm that would end straight.

        private const float CrouchAtT = 0.35f;    // going down: the game's crouch (capsule) from here
        private const float StandAtT = 0.50f;     // rising: the game's stand (capsule) from here
        private const float LegsFreeT = 0.15f;    // fallback hand-off when no style is set
        private const float PivotSpan = 0.12f;    // t before the hand-off in which he may turn on his planted feet
        private const float MaxPivot = 70f;       // deg his body may turn on his feet before they lift
        internal const int MaxSpotMoves = 2;      // walks to a better spot per kneel order
        private const float SpotArrive = 0.3f, SpotStall = 2f, SpotLimit = 6f;
        private static readonly float[] SpotRings = { 0.25f, 0.45f, 0.7f, 1f };   // x Room search radius
        private const int SpotAngles = 8, SpotPerFrame = 6;
        private const float ToeHeight = 0.025f;
        private const float ToeTuck = 15f * Mathf.Deg2Rad;
        private const float HeadRadius = 0.10f;
        private const float StepLift = 0.08f;
        private const float BraceLean = 16f;
        private const float WaitLimit = 6f;
        private const float FadeCompliant = 0.2f, FadeUrgent = 0.14f, FadeWalk = 0.18f;
        private const float RateEase = 0.06f;       // s: speed changes (retime, reversal) ease in, never jump
        private const float RefWaitLimit = 0.5f;    // s without an animated frame to start from: no kneel
        internal const float MinLowerSeconds = 0.25f;   // arms from behind his head: never faster
        // Brace contact (sizes from skeleton.bundle; thigh radius approximate): palm centre on the
        // top of the front thigh 0.15 m above the knee joint, wrist bent so the fingers lie along
        // it towards the knee (tips at the kneecap), thumb inwards.
        private const float BraceUp = 0.15f, ThighRadius = 0.075f, PalmCentre = 0.055f, PalmThick = 0.02f;
        private const float WristMax = 80f;         // deg of wrist bend at most (offline: 59-63 needed)
        private const float ThumbTilt = 27.7f;      // thumb root: 0.040 m to the side, 0.021 m palm-ward
        private const float LetGoSeconds = 0.10f;   // an early let-go (draw, bolt, out of reach)
        private static readonly Collider[] Hits = new Collider[4];

        private static void Log(SurrenderState s, string msg)
        {
            if (Settings.LogKneel.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {msg}");
        }

        // 14. Debug "Kneel pose isolate": is this contributor switched off? BodyOnly turns off the
        // head look and the hands-behind-head arms.
        internal static bool Off(KneelPoseIsolate part)
        {
            KneelPoseIsolate v = Settings.KneelPoseIsolate.Value;
            if (v == part) return true;
            return v == KneelPoseIsolate.BodyOnly && (part == KneelPoseIsolate.Head || part == KneelPoseIsolate.Hands);
        }

        // ---- ownership (asked by the layer, gestures, menu, registry, pose patch) ----------

        internal static bool Active(SurrenderState s) => s.Kneel != KneelPhase.Standing;

        // Our pose is on the bones (kneeling, a transition, or the short hand-back blend).
        internal static bool Posing(SurrenderState s) => s.Kneel != KneelPhase.Standing || s.KneelFade > 0f;

        // His legs are in the kneel: no step, no sprint, no turning on the spot.
        internal static bool LegsLocked(SurrenderState s) => s.Kneel != KneelPhase.Standing && s.KneelT > FreeT(s);

        private static float FreeT(SurrenderState s) => s.KneelStyle != null && s.KneelStyle.Free > 0f ? s.KneelStyle.Free : LegsFreeT;

        // The layer wants to walk (MoveTo while his legs are still ours): the rise hands them back
        // at the style's hand-off point instead of finishing to the last frame.
        internal static void WantsToWalk(SurrenderState s, Vector3 to)
        {
            if (s.Kneel == KneelPhase.Standing) return;
            s.KneelMoveFrame = Time.frameCount;
        }

        private static bool Walking(SurrenderState s) => s.KneelUrgent || Time.frameCount - s.KneelMoveFrame <= 2;

        // The last part of a rise: both knees up, weight on the planted feet. His body may turn
        // towards where he is going (BotSteering, at a limited speed) while the feet stay where
        // they are (ApplyBody solves the legs in the kneel's own frame, KneelAnchorFwd).
        internal static bool PivotOpen(SurrenderState s) =>
            s.Kneel == KneelPhase.GettingUp && s.KneelT <= FreeT(s) + PivotSpan && s.KneelHoldUntil <= Time.time;

        // A direction to turn towards, no more than MaxPivot from the way his feet point.
        internal static Vector3 PivotDir(SurrenderState s, Vector3 want)
        {
            want.y = 0f;
            Vector3 a = s.KneelAnchorFwd;
            if (want.sqrMagnitude < 1e-4f || a.sqrMagnitude < 1e-4f) return a;
            float ang = Mathf.Clamp(Vector3.SignedAngle(a, want, Vector3.up), -MaxPivot, MaxPivot);
            return Quaternion.AngleAxis(ang, Vector3.up) * a;
        }

        // How far his hands are behind his head (0..1), for the head look's limits.
        // 0.16.0: the hand-position order's own weight (HandsPose), standing or kneeling.
        internal static float HandsWeight(SurrenderState s) => HandsPose.Weight(s);

        // Sprinting waits for the hand-back blend: never a sprint cycle under a kneeling pose.
        internal static bool SprintAllowed(SurrenderState s) => s == null || (s.Kneel == KneelPhase.Standing && s.KneelFade <= 0f);

        // The game pose (capsule) the layer may set while the kneel owns him.
        internal static float Pose(SurrenderState s, float wanted)
        {
            if (s == null) return wanted;
            switch (s.Kneel)
            {
                case KneelPhase.GoingDown: return s.KneelT >= CrouchAtT ? 0f : wanted;
                case KneelPhase.Down: return 0f;
                case KneelPhase.GettingUp: return s.KneelT <= StandAtT ? wanted : 0f;
            }
            return wanted;
        }

        internal static float HoldPose(SurrenderState s)
        {
            if (s.Kneel != KneelPhase.Standing) return 0f;
            if (s.KneelFallback != null) return Settings.KneelFallbackCrouch.Value ? 0f : Settings.Stance.Value;
            return Settings.Stance.Value;
        }

        internal static string Describe(SurrenderState s)
        {
            switch (s.Kneel)
            {
                case KneelPhase.GoingDown: return $"kneeling down ({s.KneelStyle?.Name}) {s.KneelT:0.00}";
                case KneelPhase.Down: return $"kneeling ({s.KneelStyle?.Name})";
                case KneelPhase.GettingUp: return $"getting up ({s.KneelStyle?.Name}, {s.KneelRise}{(s.KneelBrace ? ", brace" : "")}) {s.KneelT:0.00}";
            }
            if (s.Order == Order.Kneel && s.KneelSpotMoving) return $"stepping to room to kneel ({s.KneelMoves}/{MaxSpotMoves})";
            if (s.Order == Order.Kneel && s.KneelSearchI >= 0) return "looking for room to kneel";
            return s.Order == Order.Kneel && s.KneelFallback != null ? $"can't kneel: {s.KneelFallback}" : null;
        }

        // ---- requests ---------------------------------------------------------------------

        // Orders.Give (Kneel): repeating it changes nothing already happening.
        internal static string Request(SurrenderState s, Player by, out bool ok)
        {
            ok = true;
            s.KneelFallback = null;
            s.KneelWaitSince = Time.time;
            s.KneelCheckAt = 0f;
            if (s.Kneel == KneelPhase.Down) return $"{s.Name} is already on his knees";
            if (s.Kneel == KneelPhase.GoingDown) return $"{s.Name} is getting down";
            if (s.Kneel == KneelPhase.GettingUp && s.KneelUrgent) return $"{s.Name} is getting up";
            if (s.Kneel == KneelPhase.GettingUp) return $"{s.Name} gets back down";
            s.KneelMoves = 0;
            s.KneelSearchI = -1;
            s.KneelSpotMoving = false;
            s.KneelSearchWhy = null;
            // 0.15.0: the way he will face, fixed now: towards whoever gave the order, where that
            // man stands now. Nothing later (the captor walking round him) moves it.
            s.KneelFaceSet = false;
            s.KneelFaceSince = -1f;
            s.KneelFaceLogged = false;
            // 0.16.0: turned away on an order, he kneels the way he was told to face.
            if ((s.TurnedAway || s.Turning) && s.TurnDir.sqrMagnitude > 0.5f) { s.KneelFaceDir = s.TurnDir; s.KneelFaceSet = true; }
            else if (Settings.FaceCaptor.Value && by != null && s.Bot != null)
            {
                Vector3 d = by.Position - s.Bot.Position; d.y = 0f;
                if (d.sqrMagnitude > 0.25f) { s.KneelFaceDir = d.normalized; s.KneelFaceSet = true; }
            }
            // Only what no step can fix is refused here (rig, legs, skeleton); ground and room are
            // checked as he starts, and a better spot nearby is looked for then (TryBegin).
            if (!HardChecks(s, out string why))
            {
                ok = false;
                Fallback(s, why);
                return $"{s.Name} can't kneel ({why}): {(Settings.KneelFallbackCrouch.Value ? "crouches" : "stays standing")}, hands up";
            }
            return $"{s.Name} gets down on his knees, {(s.HandsOnHead ? "hands behind his head" : "hands up")}";
        }

        // The layer, as it commits to an action that needs his legs (or his hands): what kind of
        // rise it needs. Consumed when Update sees he is no longer complying (same frame or next).
        internal static void Urge(SurrenderState s, KneelRise rise)
        {
            if (s == null || s.Kneel == KneelPhase.Standing) return;
            s.KneelIntent = rise;
            s.KneelIntentSet = true;
        }

        private static void Fallback(SurrenderState s, string why, bool tell = false)
        {
            s.KneelSpotMoving = false;
            s.KneelSearchI = -1;
            if (s.KneelFallback == why) return;
            s.KneelFallback = why;
            string what = Settings.KneelFallbackCrouch.Value ? "crouches with his hands up instead" : "stays standing with his hands up";
            Log(s, $"kneel: not possible ({why}); {what}");
            // Found out after the order was given (ground, room, no spot nearby): the menu says so.
            if (tell && s.OrderByHuman) OrderMenu.Notify($"{s.Name} can't kneel: {why}", NoteKind.Error);
        }

        // ---- per frame (SurrenderRegistry.Tick, before gestures) ----------------------------

        internal static void Update(SurrenderState s, float dt)
        {
            if (s.Dead || s.Bot == null) return;
            if (s.KneelFade > 0f) s.KneelFade = Mathf.Max(0f, s.KneelFade - dt);
            if (s.Suspended) return;   // asleep: the pose stays where it is
            float now = Time.time;

            // Bones gone (body rebuilt, despawn racing us): stop posing at once; the animator
            // owns every bone next frame. The rig is looked up again at the next kneel.
            if (s.Rig != null && Posing(s) && !s.Rig.Valid())
            {
                HandsAreHighPlugin.Log.LogWarning($"{s.Name}: kneel: his skeleton changed under the pose; stops posing ({s.Kneel})");
                s.Rig = null;
                s.Kneel = KneelPhase.Standing;
                s.KneelFade = 0f;
                s.KneelVel = 0f;
                s.KneelUrgent = false;
                if (s.Order == Order.Kneel) Fallback(s, "his skeleton changed");
                return;
            }

            // No longer complying (escape, draw, scramble, shot, let go): up now, at the speed the
            // action needs. Already rising: same motion, new speed (never a restart).
            if (s.Phase != Phase.HandsUp && s.Kneel != KneelPhase.Standing && !s.KneelUrgent)
            {
                s.KneelUrgent = true;
                KneelRise rise = s.KneelIntentSet ? s.KneelIntent : KneelRise.Defensive;
                StartRise(s, rise, $"now {s.Phase}");
            }
            else if (s.KneelUrgent && s.KneelIntentSet && s.KneelIntent != s.KneelRise && s.Kneel == KneelPhase.GettingUp)
            {
                // A second decision while rising (a draw that turned into a bolt): retimed only.
                StartRise(s, s.KneelIntent, "new decision while rising");
            }
            s.KneelIntentSet = false;

            // Wounds taken during a transition: slower at once (the speed change eases in), the
            // legs he uses stay the same until the next transition (never swapped mid-move).
            if ((s.Kneel == KneelPhase.GoingDown || s.Kneel == KneelPhase.GettingUp) && now - s.KneelLimbsAt >= 0.5f) AdaptToWounds(s);

            bool want = Wanted(s);
            if (s.Kneel != KneelPhase.Standing)
            {
                s.KneelFrames++;
                if (dt < s.KneelDtMin) s.KneelDtMin = dt;
                if (dt > s.KneelDtMax) s.KneelDtMax = dt;
                if (now >= s.KneelHelmetAt)
                {
                    s.KneelHelmetAt = now + 1f;
                    s.KneelHelmet = s.Bot.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem != null;
                }
            }

            switch (s.Kneel)
            {
                case KneelPhase.Standing:
                    if (s.Order != Order.Kneel || s.Phase != Phase.HandsUp)
                    {
                        s.KneelFallback = null;
                        s.KneelSpotMoving = false;
                        s.KneelSearchI = -1;
                        break;
                    }
                    if (s.KneelFallback != null) break;
                    if (!Settings.KneelEnabled.Value) { Fallback(s, "kneeling is off in the config"); break; }
                    if (s.KneelSpotMoving) { SpotWalk(s, now); break; }
                    if (s.KneelSearchI >= 0) { SearchSpot(s); break; }
                    // 0.17.0: the turn to KneelFaceDir is TurnDriver's (Facing.Update), with the
                    // game's stepping turn for a big one (0.19.0: the old TurnRoot pivot removed).
                    TryBegin(s, now);
                    break;

                case KneelPhase.GoingDown:
                    if (!want) { StartRise(s, s.KneelUrgent ? s.KneelRise : KneelRise.Compliant, WhyUp(s)); break; }
                    // Nothing moves until his standing pose is captured from an animated frame
                    // (ApplyBody): starting the clock first made the first posed frame a jump.
                    if (!s.KneelRefOk)
                    {
                        s.KneelRefWait += dt;
                        if (s.KneelRefWait > RefWaitLimit)
                        {
                            s.Kneel = KneelPhase.Standing;
                            s.KneelVel = 0f;
                            Fallback(s, "no animated frame to start from");
                        }
                        break;
                    }
                    if (now < s.KneelHoldUntil) { Hold(s, dt); break; }
                    Advance(s, dt, 1f);
                    if (s.KneelT >= 1f)
                    {
                        s.KneelT = 1f;
                        s.KneelVel = 0f;
                        s.KneelBrace = false;
                        s.Kneel = KneelPhase.Down;
                        Log(s, $"kneel: on his knees, {(s.HandsOnHead ? "hands behind his head" : "hands up")} ({s.KneelStyle.Name}; {Timeline(s)})");
                        s.KneelTestNextAt = now + 1.2f;
                    }
                    break;

                case KneelPhase.Down:
                    if (!want) { StartRise(s, KneelRise.Compliant, WhyUp(s)); break; }
                    CheckMoved(s, now);
                    break;

                case KneelPhase.GettingUp:
                    // A freeze first (an escape called off): still, then whichever way he goes.
                    if (now < s.KneelHoldUntil) { Hold(s, dt); break; }
                    if (want && !s.KneelUrgent)
                    {
                        // The brace is kept: the pose depends only on t, so the hand comes off
                        // his knee the way it came on (0.12.0 cleared it here: a snap).
                        s.Kneel = KneelPhase.GoingDown;
                        s.KneelSecs = DownSeconds(s, s.KneelStyle);
                        Log(s, $"kneel: back down from {s.KneelT:0.00} (ordered down again while rising; {s.KneelStyle.Name} timing kept)");
                        StartClock(s);
                        break;
                    }
                    Advance(s, dt, -1f);
                    // Going somewhere (urgent, or a walk asked for: follow, a spot): his legs go
                    // back to the game at the style's hand-off point, as soon as they are under
                    // him, so the first step follows without a pause. Standing up on the spot
                    // finishes the whole motion.
                    bool walk = Walking(s);
                    float end = walk ? FreeT(s) : 0f;
                    if (s.KneelT <= end)
                    {
                        s.KneelT = Mathf.Max(0f, s.KneelT);
                        s.KneelVel = 0f;
                        s.Kneel = KneelPhase.Standing;
                        s.KneelFade = s.KneelFadeTotal = s.KneelUrgent ? FadeUrgent : walk ? FadeWalk : FadeCompliant;
                        s.KneelFreedAt = now;
                        Log(s, $"kneel: back on his feet ({s.KneelRise}{(s.KneelBrace ? ", braced on his knee" : "")}{(walk ? $", legs to the walk at t {end:0.00}" : "")}; {Timeline(s)})");
                        s.KneelUrgent = false;
                        // KneelBrace stays: whatever is left of it fades with the legs (BraceWeight).
                        s.KneelTestNextAt = now + 1.2f;
                    }
                    break;
            }

            UpdateBrace(s, dt);
            DebugTest(s, now);
        }

        // t moves at the current transition's speed; a change of speed or direction (urgency,
        // reversal) eases in over RateEase instead of jumping. Fresh starts set the speed
        // outright: every window starts with a smoothstep, so the pose itself eases in.
        private static void Advance(SurrenderState s, float dt, float dir)
        {
            float target = dir / Mathf.Max(0.1f, s.KneelSecs);
            s.KneelVel += (target - s.KneelVel) * (1f - Mathf.Exp(-dt / RateEase));
            s.KneelT = Mathf.Clamp01(s.KneelT + s.KneelVel * dt);
        }

        // A freeze (reconsidering an escape): the motion eases to a stop where it is; Advance
        // eases it back to speed afterwards.
        private static void Hold(SurrenderState s, float dt)
        {
            s.KneelVel -= s.KneelVel * (1f - Mathf.Exp(-dt / RateEase));
            s.KneelT = Mathf.Clamp01(s.KneelT + s.KneelVel * dt);
        }

        // Early let-go: the hands are needed (a draw, drop or hands change pending: the arm must
        // be the game's before the animation shows), he is bolting on a timing that would still
        // hold the knee at the leg hand-back, or the pose found the knee out of reach. Latched
        // for the rest of the rise; the gate falls over LetGoSeconds (frame-rate independent).
        private static void UpdateBrace(SurrenderState s, float dt)
        {
            if (!s.KneelBrace) return;
            if (!s.KneelBraceLetGo)
            {
                string why = null;
                int arm = s.Rig != null && s.Rig.Legs[1 - s.KneelLead] != null && s.Rig.Legs[1 - s.KneelLead].Side > 0f ? 1 : 0;
                if (s.DrawPending || s.HandsPending || s.DropPending) why = "his hands are needed (draw)";
                else if (s.KneelBraceStretch) why = $"his knee is out of reach ({s.KneelBraceRatio:0.00} of his arm)";
                else if (s.KneelArmBad[arm]) why = "that arm is hurt";
                else if (Walking(s) && s.Kneel == KneelPhase.GettingUp && s.KneelStyle != null && s.KneelStyle.BraceOutA < FreeT(s))
                    why = s.KneelUrgent ? "he's getting up in a hurry" : "he's about to walk";
                if (why != null)
                {
                    s.KneelBraceLetGo = true;
                    if (BraceWeight(s) > 0.01f) Log(s, $"kneel: lets go of his knee early ({why})");
                }
            }
            if (s.KneelBraceLetGo && s.KneelBraceGate > 0f) s.KneelBraceGate = Mathf.Max(0f, s.KneelBraceGate - dt / LetGoSeconds);
        }

        private static bool Wanted(SurrenderState s) =>
            Settings.Enabled.Value && Settings.KneelEnabled.Value && s.Phase == Phase.HandsUp &&
            s.Order == Order.Kneel && s.KneelFallback == null;

        private static string WhyUp(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"now {s.Phase}";
            if (!Settings.KneelEnabled.Value) { Fallback(s, "kneeling turned off"); return "kneeling turned off"; }
            if (s.KneelFallback != null) return s.KneelFallback;
            return s.Order == Order.Follow ? "ordered to follow" : "ordered up";
        }

        // ---- choosing (once per transition, kept until it ends or is interrupted) ----------

        // Descent: who he is and what he carries; restrained randomness. Logged with the weights.
        private static KneelStyle PickDescent(SurrenderState s, out string why)
        {
            switch (Settings.KneelTestStyle.Value)
            {
                case KneelStyleChoice.Steady: why = "debug choice"; return Steady;
                case KneelStyleChoice.Frightened: why = "debug choice"; return Frightened;
                case KneelStyleChoice.OneKneeFirst: why = "debug choice"; return OneKnee;
            }
            float nerve = s.Nerve, hurt = s.Injury;
            bool heavy = Heavy(s.Bot);
            float timid = Personality(s.Personality, 1.6f, 0.6f);
            // A hurt leg: no dropping onto the knees (frightened), more of the careful one-knee
            // descent with a pause in the half-kneel. Demeanor shapes the rest (presentation).
            float leg = Mathf.Max(s.KneelLegHurt[0], s.KneelLegHurt[1]);
            float steady = Settings.KneelWeightSteady.Value * (0.6f + nerve) * (hurt > 0.3f ? 1.5f : 1f) / Mathf.Max(0.6f, timid) * Presence.DescentSteady(s);
            float scared = Settings.KneelWeightFrightened.Value * (1.5f - nerve) * timid * (hurt > 0.5f ? 0.3f : 1f) * (heavy ? 0.7f : 1f) *
                           (leg >= 0.5f ? 0.15f : 1f) * Presence.DescentScared(s);
            float one = Settings.KneelWeightOneKnee.Value * (heavy ? 1.4f : 1f) * (s.Personality == "Rat" ? 1.3f : 1f) * (hurt > 0.3f ? 1.2f : 1f) *
                        (leg >= 0.5f ? 2f : leg >= 0.3f ? 1.4f : 1f) * Presence.DescentOneKnee(s);
            float total = steady + scared + one;
            float roll = Dice.Value * Mathf.Max(0.0001f, total);
            why = $"steady {steady:0.00} / frightened {scared:0.00} / one knee {one:0.00}, roll {roll:0.00} (nerve {nerve:0.00}, hurt {hurt:0.00}, legs {s.KneelLegHurt[0]:0.00}/{s.KneelLegHurt[1]:0.00}" +
                  $"{(heavy ? ", heavy kit" : "")}{(Presence.Describe(s) is string d ? ", " + d : "")})";
            if (roll < steady) return Steady;
            if (roll < steady + scared) return Frightened;
            return OneKnee;
        }

        // Timid personalities high, bold ones low; 1 when unknown.
        private static float Personality(string p, float timid, float bold)
        {
            switch (p)
            {
                case "Coward": case "Timmy": return timid;
                case "Rat": return 1.2f;
                case "Chad": case "GigaChad": case "Wreckless": case "SnappingTurtle": return bold;
                default: return 1f;
            }
        }

        private static bool Heavy(Player bot)
        {
            var eq = bot.Equipment;
            if (eq == null) return false;
            bool pack = eq.GetSlot(EquipmentSlot.Backpack)?.ContainedItem != null;
            Item vest = eq.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem ?? eq.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            return pack && vest != null && Arsenal.HasArmour(vest);
        }

        private static float DownSeconds(SurrenderState s, KneelStyle st)
        {
            float b = st == Frightened ? Settings.KneelFrightenedSeconds.Value
                    : st == OneKnee ? Settings.KneelOneKneeSeconds.Value
                    : Settings.KneelDownSeconds.Value;
            // One slowing for wounds (the worse of the overall wound and the front leg he lowers
            // himself on), never both multiplied.
            return b * (1f + 0.5f * Mathf.Max(s.Injury, FrontHurt(s))) * (Heavy(s.Bot) ? 1.1f : 1f) * Presence.DownTime(s);
        }

        // Rise duration from intent, slowed by wounds and heavy kit: a badly hurt man never gets
        // the healthy man's escape speed. Urgency sets the effort (base time and how much the
        // overall wound slows him); the front leg he pushes up on limits every rise alike
        // (0.13.0; was a flat x1.5 for any bad leg on top of the injury, counting it twice).
        private static float RiseSeconds(SurrenderState s, KneelRise rise)
        {
            float b, hurt;
            switch (rise)
            {
                case KneelRise.Escape: b = Settings.KneelQuickSeconds.Value; hurt = 0.9f; break;
                case KneelRise.Defensive: b = Settings.KneelDefensiveSeconds.Value; hurt = 0.7f; break;
                default: b = Settings.KneelUpSeconds.Value * (s.KneelDescent == Frightened ? 0.85f : 1f) * Presence.RiseTime(s); hurt = 0.5f; break;
            }
            float wounds = Mathf.Max(1f + hurt * s.Injury, 1f + 0.9f * FrontHurt(s));
            return b * wounds * (Heavy(s.Bot) ? 1.08f : 1f);
        }

        private static KneelRise DebugRise(KneelRise rise)
        {
            switch (Settings.KneelTestRise.Value)
            {
                case KneelRiseChoice.Compliant: return KneelRise.Compliant;
                case KneelRiseChoice.Defensive: return KneelRise.Defensive;
                case KneelRiseChoice.Escape: return KneelRise.Escape;
            }
            return rise;
        }

        // Starting up (or changing speed while rising). From the full kneel a rise style and the
        // brace are chosen; mid-transition the current timing is kept, so nothing jumps.
        private static void StartRise(SurrenderState s, KneelRise rise, string why)
        {
            if (s.Kneel == KneelPhase.Standing) return;
            if (!s.KneelUrgent) rise = DebugRise(rise);
            bool fresh = s.Kneel == KneelPhase.Down || (s.Kneel == KneelPhase.GoingDown && s.KneelT >= 0.999f);
            bool retime = s.Kneel == KneelPhase.GettingUp;
            if (fresh)
            {
                ReadLimbs(s);
                // KneelAnchorFwd is not reset here: his legs were solved in it while he knelt, and
                // stay in it (FeetFrame) while his body may turn at the end of the rise.
                s.KneelStyle = rise == KneelRise.Escape ? RiseEscape : rise == KneelRise.Defensive ? RiseDefensive : RiseCompliant;
                s.KneelBrace = PickBrace(s, rise, out string braceWhy);
                s.KneelBraceGate = 1f;
                s.KneelBraceLetGo = false;
                s.KneelBraceStretch = false;
                s.KneelStepNow = Mathf.Min(s.KneelStyle.Step, s.KneelStep + 0.06f);   // no longer than the room found
                if (Settings.LogKneel.Value && braceWhy != null) Log(s, $"kneel: brace on his knee: {braceWhy}");
            }
            s.KneelRise = rise;
            s.KneelSecs = RiseSeconds(s, rise);
            // From rest at full speed (the windows ease in); mid-motion Advance eases the change.
            if (fresh) s.KneelVel = -1f / Mathf.Max(0.1f, s.KneelSecs);
            if (!retime) { s.Kneel = KneelPhase.GettingUp; StartClock(s); }
            string ceiling = "";
            MovementContext mc = s.Bot.MovementContext;
            if (!retime && mc != null && !mc.CanStandAt(1f)) ceiling = "; low ceiling: the game will keep him crouched";
            Log(s, $"kneel: {(retime ? "rise retimed" : "gets up")} from {s.KneelT:0.00} ({why}): {rise} rise, {s.KneelSecs:0.00} s, " +
                   $"{s.KneelStyle.Name} motion{(s.KneelBrace ? ", hand on his knee" : "")}{ceiling}");
        }

        // The hand pushes on the front thigh: not with arms busy drawing, not with that arm's
        // gesture, chance by intent. Never an item or inventory action: bones only.
        private static bool PickBrace(SurrenderState s, KneelRise rise, out string why)
        {
            why = null;
            if (!Settings.KneelBrace.Value) return false;
            KneelBraceChoice c = Settings.KneelTestBrace.Value;
            if (c == KneelBraceChoice.Never) { why = "off (debug choice)"; return false; }
            if (s.DrawPending || s.HandsPending || s.DropPending) { why = "no: his hands are busy"; return false; }
            // 0.16.0: told to keep his hands on his head, he stands up without them (no brace).
            if (s.HandsOnHead && s.Phase == Phase.HandsUp) { why = "no: his hands stay on his head"; return false; }
            // Without the finger bones the wrist can't be laid flat: the fingers would go into
            // the thigh. The rise goes ahead without the brace.
            if (s.Rig == null || !s.Rig.HandOk) { why = "no: no finger bones in this rig to lay the hand flat"; return false; }
            int arm = s.Rig.Legs[1 - s.KneelLead].Side > 0f ? 1 : 0;
            if (s.KneelArmBad[arm]) { why = $"no: his {(arm == 1 ? "right" : "left")} arm is broken or destroyed"; return false; }
            if (c == KneelBraceChoice.Always) { why = "yes (debug choice)"; return true; }
            // More arm support when the leg he pushes up on is hurt.
            float front = FrontHurt(s);
            float chance = Settings.KneelBraceChance.Value * (rise == KneelRise.Escape ? 1.3f : rise == KneelRise.Defensive ? 0.45f : 1f) *
                           (1f + 0.8f * s.Injury) * (1f + front) * (Heavy(s.Bot) ? 1.2f : 1f) * Presence.Brace(s);
            if (front >= 0.5f && rise != KneelRise.Defensive) chance = Mathf.Max(chance, 0.85f);
            float roll = Dice.Value;
            bool yes = roll < chance;
            why = $"chance {chance:0.00}{(front > 0.05f ? $" (front leg hurt {front:0.00})" : "")}, roll {roll:0.00} -> {(yes ? "yes" : "no")}";
            return yes;
        }

        private static void StartClock(SurrenderState s)
        {
            s.KneelClock = Time.realtimeSinceStartup;
            s.KneelFrames = 0;
            s.KneelDtMin = float.MaxValue;
            s.KneelDtMax = 0f;
        }

        private static string Timeline(SurrenderState s) =>
            $"{Time.realtimeSinceStartup - s.KneelClock:0.00} s, {s.KneelFrames} frames, dt {s.KneelDtMin * 1000f:0}-{s.KneelDtMax * 1000f:0} ms";

        private static void TryBegin(SurrenderState s, float now)
        {
            if (now < s.KneelCheckAt) return;
            s.KneelCheckAt = now + 0.2f;
            Player bot = s.Bot;
            string wait = null;
            if (!s.ArmsUp || s.Progress < 1f) wait = "hands not up yet";
            else if (s.DropPending || s.HandsPending || s.DrawPending) wait = "hands busy";
            else if (s.Moving || bot.IsSprintEnabled) wait = "still moving";
            else if (s.KneelFade > 0f) wait = "still settling from the last rise";
            else if (bot.CustomAnimationsAreProcessing) wait = "playing an animation";
            else if (bot.MovementContext?.PlayerAnimator?.AnimatedInteractions?.IsInteractionPlaying == true) wait = "an interaction is playing";
            if (wait != null)
            {
                if (s.KneelWaitSince <= 0f) s.KneelWaitSince = now;
                if (now - s.KneelWaitSince > WaitLimit) Fallback(s, $"never settled ({wait})", tell: true);
                else if (Settings.VerboseLogs.Value) Log(s, $"kneel: waits ({wait})");
                return;
            }
            // Facing is never a reason to give up (0.14.0 waited for a 15 deg match that the game's
            // root never reaches while the look is within 45 deg; with a captor walking, never).
            if (!FacingSettled(s, now)) return;
            if (!HardChecks(s, out string why)) { Fallback(s, why, tell: true); return; }
            Frame(bot, out Vector3 o, out Vector3 f, out Vector3 r);
            if (!Fits(s, o, f, r, s.KneelScratch, out why))
            {
                // Not here: a short, bounded look round for a spot that fits (SearchSpot), then a
                // walk there with his hands up. Never more than MaxSpotMoves walks per order.
                if (Settings.KneelReposition.Value && s.KneelMoves < MaxSpotMoves)
                {
                    s.KneelSearchWhy = why;
                    s.KneelSearchI = 0;
                    s.KneelSearchFrom = o;
                    if (Settings.LogKneel.Value) Log(s, $"kneel: no room where he stands ({why}); looks for a spot within {Settings.KneelSpotRadius.Value:0.0} m");
                    return;
                }
                Fallback(s, s.KneelMoves > 0 ? $"{why} (after {s.KneelMoves} step(s) to find room)" : why, tell: true);
                return;
            }
            Commit(s, o, f, s.KneelScratch);
            ReadLimbs(s);

            KneelStyle style = PickDescent(s, out string styleWhy);
            // Lead leg (goes back and down first; the other is the front leg that lowers him and
            // later pushes him up): the hurt leg leads, so the weight is on the healthy one. Else
            // his own side (from his per-raid seed) most of the time; the other side if there's
            // no room for the front foot's step on this one; a short step if neither.
            int dominant = s.Seed >= 50f ? 1 : 0;
            int lead = Dice.Value < 0.8f ? dominant : 1 - dominant;
            string legWhy = "";
            float dl = s.KneelLegHurt[0] - s.KneelLegHurt[1];
            bool favour = Mathf.Abs(dl) >= 0.15f;
            if (favour)
            {
                lead = dl > 0f ? 0 : 1;
                legWhy = $", favours his healthy {(lead == 0 ? "right" : "left")} leg (hurt {s.KneelLegHurt[lead]:0.00} vs {s.KneelLegHurt[1 - lead]:0.00})";
            }
            float step = style.Step;
            string stepWhy = "";
            if (!StepRoom(s, o, f, r, 1 - lead, step, s.KneelGround))
            {
                if (!favour && StepRoom(s, o, f, r, lead, step, s.KneelGround)) { lead = 1 - lead; stepWhy = ", other leg leads (no room for the step)"; }
                else { step = 0.08f; stepWhy = ", short step (no room in front)"; }
            }

            Gestures.Cancel(s, "getting down on his knees");
            s.Kneel = KneelPhase.GoingDown;
            s.KneelUrgent = false;
            s.KneelT = 0f;
            s.KneelHoldUntil = 0f;
            s.KneelStyle = style;
            s.KneelDescent = style;
            s.KneelStep = step;
            s.KneelStepNow = step;
            s.KneelBraceStretch = false;
            s.KneelBraceLetGo = false;
            s.KneelBraceGate = 1f;
            s.KneelLead = lead;
            s.KneelBrace = false;
            s.KneelRefOk = false;
            s.KneelRefWait = 0f;
            s.KneelSecs = DownSeconds(s, style);
            s.KneelVel = 1f / Mathf.Max(0.1f, s.KneelSecs);
            // Small, stable differences in how he ends up (from his seed, not per frame).
            float n1 = Mathf.Sin(s.Seed * 12.9898f) * 0.5f, n2 = Mathf.Sin(s.Seed * 78.233f) * 0.5f, n3 = Mathf.Sin(s.Seed * 37.719f) * 0.5f;
            s.KneelThighOff = 4f * n1;
            s.KneelLeanOff = 3f * n2 + (style == Frightened ? 5f : 0f);
            s.KneelSpreadOff = 0.02f * n3;
            s.KneelHelmet = bot.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem != null;
            s.KneelHelmetAt = now + 1f;
            StartClock(s);
            Log(s, $"kneel: gets down, {style.Name} ({styleWhy}), {s.KneelSecs:0.0} s, {(lead == 1 ? "right" : "left")} knee first{legWhy}{stepWhy}; " +
                   $"ground {s.KneelGround[0]:+0.00;-0.00}/{s.KneelGround[1]:+0.00;-0.00} knees, {s.KneelGround[2]:+0.00;-0.00}/{s.KneelGround[3]:+0.00;-0.00} feet, " +
                   $"{(s.KneelHelmet ? "helmet" : "no helmet")}{(s.KneelMoves > 0 ? $", after {s.KneelMoves} step(s) to find room" : "")}");
        }

        // 0.15.0: before going down he turns to KneelFaceDir (fixed when the order was taken). The
        // game turns a bot's root only once his look is over 45 deg from it (MovementState.
        // ProcessRotation / TrunkRotationLimit) and otherwise twists the animated trunk towards the
        // look; so the layer keeps his look on that direction (PreTurn) and the root is turned here
        // at a walking-turn pace (MovementContext.ApplyRotation, the call the game's own states use
        // for the root). Standing, hands up, not moving: a turn on the spot, bounded in time.
        private const float FaceTolerance = 10f;
        private const float FaceTurnLimit = 6f;     // s: then he kneels the way he faces (a step turn takes ~1 s per 90 deg)

        internal static bool PreTurn(SurrenderState s, out Vector3 dir)
        {
            dir = s.KneelFaceDir;
            return s.KneelFaceSet && s.Order == Order.Kneel && s.Kneel == KneelPhase.Standing && s.KneelFallback == null &&
                   !s.KneelSpotMoving && s.Phase == Phase.HandsUp && Settings.KneelEnabled.Value;
        }

        private static float FaceOff(SurrenderState s)
        {
            Vector3 f = s.Bot.Transform.Original.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(f, s.KneelFaceDir, Vector3.up);
        }

        private static bool FacingSettled(SurrenderState s, float now)
        {
            if (!s.KneelFaceSet) return true;
            if (s.KneelFaceSince < 0f) s.KneelFaceSince = now;
            float off = Mathf.Abs(FaceOff(s));
            if (off <= FaceTolerance)
            {
                if (s.KneelFaceSince >= 0f && !s.KneelFaceLogged)
                    Log(s, $"kneel: turned to face where the order came from ({now - s.KneelFaceSince:0.0} s, {off:0} deg off)");
                s.KneelFaceLogged = true;
                return true;
            }
            if (s.KneelFaceSince >= 0f && now - s.KneelFaceSince > FaceTurnLimit)
            {
                if (!s.KneelFaceLogged) Log(s, $"kneel: couldn't finish turning ({off:0} deg off after {FaceTurnLimit:0.0} s); kneels the way he faces");
                s.KneelFaceLogged = true;
                return true;
            }
            return false;
        }

        // 0.15.0: a bot captor's check before he orders a kneel (terrain and clearance where the
        // prisoner stands, his legs, his rig). True with a note when the spot itself won't do but
        // the prisoner may step to room nearby (Kneel reposition); false when kneeling can't work.
        internal static bool Suitable(SurrenderState s, out string why)
        {
            if (s?.Bot == null || s.Kneel != KneelPhase.Standing) { why = "already kneeling or getting there"; return false; }
            if (!HardChecks(s, out why)) return false;
            Frame(s.Bot, out Vector3 o, out Vector3 f, out Vector3 r);
            if (Fits(s, o, f, r, s.KneelScratch, out why)) { why = null; return true; }
            if (Settings.KneelReposition.Value) { why = $"no room right where he stands ({why}); he may step to room nearby"; return true; }
            return false;
        }

        // What no change of place can fix. One bad leg: he still kneels (the hurt one goes down
        // first, the healthy one carries him); both legs broken or destroyed: no.
        private static bool HardChecks(SurrenderState s, out string why)
        {
            Player bot = s.Bot;
            if (!Settings.KneelEnabled.Value) { why = "kneeling is off in the config"; return false; }
            ReadLimbs(s);
            if (s.KneelLegHurt[0] >= 0.7f && s.KneelLegHurt[1] >= 0.7f) { why = "both legs too badly hurt"; return false; }
            if (bot.IsInPronePose) { why = "prone"; return false; }
            if (bot.UsedSimplifiedSkeleton) { why = "simplified skeleton"; return false; }
            if ((s.Rig == null || !s.Rig.Valid()) && !FindRig(bot, out s.Rig, out why)) return false;
            why = null;
            return true;
        }

        // Ground, room for his body, kit and the front foot's step (either leg), in a given frame.
        private static bool Fits(SurrenderState s, Vector3 o, Vector3 f, Vector3 r, float[] ground, out string why)
        {
            if (!GroundAt(s, o, f, r, ground, out why) || !RoomAt(s, o, f, r, out why)) return false;
            if (!StepRoom(s, o, f, r, 0, 0.08f, ground) && !StepRoom(s, o, f, r, 1, 0.08f, ground)) { why = "no room in front for a foot to step (to get down or back up)"; return false; }
            return true;
        }

        private static void Commit(SurrenderState s, Vector3 o, Vector3 f, float[] ground)
        {
            for (int i = 0; i < 6; i++) s.KneelGround[i] = ground[i];
            s.KneelAnchor = o;
            s.KneelAnchorFwd = f;
        }

        // ---- limbs (verified health information only) ----------------------------------------

        private static readonly EBodyPart[] LegParts = { EBodyPart.LeftLeg, EBodyPart.RightLeg };
        private static readonly EBodyPart[] ArmParts = { EBodyPart.LeftArm, EBodyPart.RightArm };

        // Per leg 0..1: lost health (x0.6), heavy bleeding on it 0.4, broken 0.7, destroyed 1.
        // Broken or destroyed is what the game itself limps on (EPhysicalCondition.Left/Right
        // LegDamaged): the game's walk penalty is its own; this only shapes our transitions.
        // Arms: broken or destroyed can't push on a knee.
        internal static void ReadLimbs(SurrenderState s)
        {
            s.KneelLimbsAt = Time.time;
            var hc = s.Bot?.HealthController;
            if (hc == null) return;
            for (int i = 0; i < 2; i++)
            {
                var h = hc.GetBodyPartHealth(LegParts[i]);
                float frac = h.Maximum > 0f ? h.Current / h.Maximum : 1f;
                float hurt = Mathf.Clamp01((1f - frac) * 0.6f);
                if (hc.FindActiveEffect<EFT.HealthSystem.IHeavyBleeding>(LegParts[i]) != null) hurt = Mathf.Max(hurt, 0.4f);
                if (hc.IsBodyPartBroken(LegParts[i])) hurt = Mathf.Max(hurt, 0.7f);
                if (hc.IsBodyPartDestroyed(LegParts[i])) hurt = 1f;
                s.KneelLegHurt[i] = hurt;
                s.KneelArmBad[i] = hc.IsBodyPartBroken(ArmParts[i]) || hc.IsBodyPartDestroyed(ArmParts[i]);
            }
        }

        private static float FrontHurt(SurrenderState s) => s.KneelLegHurt[1 - s.KneelLead];

        // During a transition: re-read the limbs; a slower time takes over at once (Advance eases
        // the speed), the motion and the legs are kept.
        private static void AdaptToWounds(SurrenderState s)
        {
            float front = FrontHurt(s);
            ReadLimbs(s);
            if (FrontHurt(s) <= front + 0.1f || s.KneelStyle == null) return;
            float secs = s.Kneel == KneelPhase.GoingDown ? DownSeconds(s, s.KneelStyle) : RiseSeconds(s, s.KneelRise);
            if (secs <= s.KneelSecs * 1.05f) return;
            Log(s, $"kneel: hurt while {(s.Kneel == KneelPhase.GoingDown ? "getting down" : "getting up")} (front leg {front:0.00} -> {FrontHurt(s):0.00}): slower, {s.KneelSecs:0.00} -> {secs:0.00} s, same legs");
            s.KneelSecs = secs;
        }

        // ---- a better spot (bounded) -------------------------------------------------------

        // Rings round where he stands (SpotRings x Room search radius), SpotAngles each, nearest
        // first, at most SpotPerFrame checked a frame and only on a frame with heavy-search budget.
        // The first that fits facing his captor, on the NavMesh at his level, in his captor's sight
        // if he can see him now, with a short complete path, is where he walks. One search per
        // walk; none of it is repeated for the same order beyond MaxSpotMoves.
        private static void SearchSpot(SurrenderState s)
        {
            if (!Perception.TakeHeavy()) return;
            Player bot = s.Bot;
            Vector3 o = s.KneelSearchFrom;
            Vector3 f0 = bot.Transform.Original.forward; f0.y = 0f;
            if (f0.sqrMagnitude < 1e-4f) f0 = Vector3.forward;
            f0.Normalize();
            Player captor = s.Captor;
            bool seen = s.CaptorSeen && Perception.Alive(captor);
            Vector3 threat = seen ? captor.Position : s.CaptorLastSeen;
            Vector3 threatEye = seen ? Perception.Eye(captor) : threat + Vector3.up * 1.5f;
            float radius = Settings.KneelSpotRadius.Value;
            int total = SpotRings.Length * SpotAngles;
            int end = Mathf.Min(total, s.KneelSearchI + SpotPerFrame);
            for (; s.KneelSearchI < end; s.KneelSearchI++)
            {
                int ring = s.KneelSearchI / SpotAngles, k = s.KneelSearchI % SpotAngles;
                // 0, +45, -45, +90, -90, ... from his right: sideways first, then front and back.
                float ang = 90f + ((k + 1) / 2) * 45f * (k % 2 == 0 ? 1f : -1f);
                Vector3 want = o + Quaternion.AngleAxis(ang, Vector3.up) * f0 * (SpotRings[ring] * radius);
                if (!UnityEngine.AI.NavMesh.SamplePosition(want, out UnityEngine.AI.NavMeshHit hit, 0.35f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                Vector3 c = hit.position;
                if (Mathf.Abs(c.y - o.y) > 0.3f || (c - o).sqrMagnitude < 0.15f * 0.15f) continue;
                if (threat != Vector3.zero && (threat - c).sqrMagnitude < 1.5f * 1.5f) continue;
                Vector3 f = threat != Vector3.zero ? threat - c : f0;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = f0;
                f.Normalize();
                Vector3 r = Vector3.Cross(Vector3.up, f);
                // Compliance: he doesn't step out of his captor's sight to kneel.
                if (seen && !Perception.LineClear(c + Vector3.up * 1.3f, threatEye)) continue;
                if (Physics.CheckCapsule(c + Vector3.up * 0.45f, c + Vector3.up * 1.6f, 0.25f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Fits(s, c, f, r, s.KneelScratch, out _)) continue;
                if (!Perception.PathTo(bot.Position, c, out Vector3[] corners, out _)) continue;
                float len = 0f;
                for (int i = 1; i < corners.Length; i++) len += Vector3.Distance(corners[i - 1], corners[i]);
                if (len > 2.5f * Vector3.Distance(bot.Position, c) + 1f) continue;
                s.KneelSearchI = -1;
                s.KneelSpot = c;
                s.KneelSpotMoving = true;
                s.KneelMoves++;
                s.KneelSpotSince = Time.time;
                s.KneelSpotProgress = bot.Position;
                s.KneelSpotProgressAt = Time.time;
                s.KneelWaitSince = 0f;
                Log(s, $"kneel: steps {Vector3.Distance(bot.Position, c):0.0} m to a spot with room (here: {s.KneelSearchWhy}), hands up; walk {s.KneelMoves} of {MaxSpotMoves}");
                return;
            }
            if (s.KneelSearchI < total) return;   // more candidates next frame
            s.KneelSearchI = -1;
            Fallback(s, $"{s.KneelSearchWhy}; no room to kneel within {radius:0.0} m", tell: true);
        }

        // The walk itself is the layer's (MoveTo, hands up, no sprint, facing his captor); this
        // only watches it: arrived, stuck, or too long.
        private static void SpotWalk(SurrenderState s, float now)
        {
            Player bot = s.Bot;
            Vector3 d = s.KneelSpot - bot.Position; d.y = 0f;
            if (d.magnitude <= SpotArrive)
            {
                s.KneelSpotMoving = false;
                s.KneelCheckAt = now + 0.35f;   // stop and turn to his captor first
                s.KneelWaitSince = now;
                if (Settings.VerboseLogs.Value) Log(s, "kneel: at the new spot");
                return;
            }
            if ((bot.Position - s.KneelSpotProgress).sqrMagnitude > 0.15f * 0.15f) { s.KneelSpotProgress = bot.Position; s.KneelSpotProgressAt = now; }
            string why = now - s.KneelSpotProgressAt > SpotStall ? "stuck on the way" : now - s.KneelSpotSince > SpotLimit ? "took too long" : null;
            if (why != null) SpotFailed(s, why);
        }

        // The layer couldn't walk him there (no path now), or SpotWalk gave up.
        internal static void SpotFailed(SurrenderState s, string why)
        {
            if (!s.KneelSpotMoving) return;
            s.KneelSpotMoving = false;
            if (s.KneelMoves < MaxSpotMoves && Settings.KneelReposition.Value)
            {
                Log(s, $"kneel: couldn't reach the spot ({why}); looks once more from here");
                s.KneelSearchI = 0;
                s.KneelSearchFrom = s.Bot.Position;
                return;
            }
            Fallback(s, $"couldn't reach a spot with room ({why})", tell: true);
        }

        internal static bool SpotWalking(SurrenderState s) => s.KneelSpotMoving && s.Kneel == KneelPhase.Standing;

        // ---- rig (once per surrender; by the skeleton's own names) --------------------------

        private static bool FindRig(Player p, out KneelRig rig, out string why)
        {
            rig = null;
            PlayerBones b = p.PlayerBones;
            Transform pelvis = b?.Pelvis?.Original, head = b?.Head?.Original;
            if (pelvis == null || head == null) { why = "rig: no pelvis or head bone"; LogRig(p, why); return false; }
            KneelLeg l = Leg(b.LeftThigh1?.Original, "L", -1f, out why);
            if (l == null) { LogRig(p, why); return false; }
            KneelLeg r = Leg(b.RightThigh1?.Original, "R", 1f, out why);
            if (r == null) { LogRig(p, why); return false; }
            rig = new KneelRig { Pelvis = pelvis, Head = head };
            rig.Legs[0] = l;
            rig.Legs[1] = r;
            Vector3 hips = r.Hip.position - l.Hip.position;
            rig.HipHalfWidth = Mathf.Clamp(hips.magnitude * 0.5f, 0.05f, 0.15f);
            rig.TorsoLength = Mathf.Clamp(Vector3.Distance(head.position, (l.Hip.position + r.Hip.position) * 0.5f), 0.5f, 0.9f);
            if (Vector3.Dot(hips, p.Transform.Original.right) < 0f)
                HandsAreHighPlugin.Log.LogWarning($"Kneel rig: {p.Profile?.Nickname}'s left hip is on his right (mirrored rig?); kneeling may look wrong");
            Transform lp = b.LeftPalm, rp = b.RightPalm;
            rig.Middle[0] = lp != null ? FindDeep(lp, "Base HumanLDigit31", 1) : null;
            rig.Thumb[0] = lp != null ? FindDeep(lp, "Base HumanLDigit11", 1) : null;
            rig.Middle[1] = rp != null ? FindDeep(rp, "Base HumanRDigit31", 1) : null;
            rig.Thumb[1] = rp != null ? FindDeep(rp, "Base HumanRDigit11", 1) : null;
            rig.HandOk = rig.Middle[0] != null && rig.Thumb[0] != null && rig.Middle[1] != null && rig.Thumb[1] != null;
            for (int i = 0; i < 2; i++)
            {
                Transform palm = i == 0 ? lp : rp;
                Transform t3 = palm != null ? palm.parent : null;
                string sd = i == 0 ? "L" : "R";
                if (t3 != null && t3.name == $"Base Human{sd}Forearm3" && t3.parent != null && t3.parent.name == $"Base Human{sd}Forearm2")
                { rig.TwistB[i] = t3; rig.TwistA[i] = t3.parent; }
            }
            if (!rig.HandOk)
                HandsAreHighPlugin.Log.LogWarning($"Kneel rig: {p.Profile?.Nickname}: no Digit31/Digit11 finger bones under the palms; he kneels, but never braces on his knee");
            if (Settings.VerboseLogs.Value)
                HandsAreHighPlugin.Log.LogInfo($"Kneel rig: {p.Profile?.Nickname}: thigh {l.Thigh:0.000}/{r.Thigh:0.000} m, shin {l.Shin:0.000}/{r.Shin:0.000} m, " +
                    $"foot {l.FootLen:0.000} m, hips {rig.HipHalfWidth * 2f:0.000} m apart, torso {rig.TorsoLength:0.00} m");
            why = null;
            return true;
        }

        private static void LogRig(Player p, string why) =>
            HandsAreHighPlugin.Log.LogWarning($"Kneel rig: {p.Profile?.Nickname}: {why}; he can't kneel");

        private static KneelLeg Leg(Transform hip, string side, float sign, out string why)
        {
            why = null;
            if (hip == null) { why = $"rig: no {side} thigh bone"; return null; }
            Transform knee = FindDeep(hip, $"Base Human{side}Calf", 3);
            Transform foot = knee != null ? FindDeep(knee, $"Base Human{side}Foot", 2) : null;
            Transform toe = foot != null ? FindDeep(foot, $"Base Human{side}Toe", 2) : null;
            if (knee == null || foot == null || toe == null) { why = $"rig: no {side} calf/foot/toe under {hip.name}"; return null; }
            var leg = new KneelLeg
            {
                Hip = hip, Knee = knee, Foot = foot, Toe = toe, Side = sign,
                Thigh = Vector3.Distance(hip.position, knee.position),
                Shin = Vector3.Distance(knee.position, foot.position),
                FootLen = Vector3.Distance(foot.position, toe.position)
            };
            if (leg.Thigh < 0.25f || leg.Thigh > 0.7f || leg.Shin < 0.25f || leg.Shin > 0.7f || leg.FootLen < 0.05f)
            { why = $"rig: unexpected {side} leg (thigh {leg.Thigh:0.00} m, shin {leg.Shin:0.00} m)"; return null; }
            var mid = new System.Collections.Generic.List<Transform>(2);
            for (Transform t = knee.parent; t != null && t != hip; t = t.parent) mid.Add(t);
            for (Transform t = foot.parent; t != null && t != knee; t = t.parent) mid.Add(t);
            leg.Mid = mid.ToArray();
            leg.MidRef = new Quaternion[leg.Mid.Length];
            return leg;
        }

        internal static Transform FindDeep(Transform t, string name, int depth)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.name == name) return c;
                if (depth > 1)
                {
                    Transform f = FindDeep(c, name, depth - 1);
                    if (f != null) return f;
                }
            }
            return null;
        }

        // ---- ground and room (at the start, at candidate spots, and again only if he is moved) --

        private static void Frame(Player p, out Vector3 o, out Vector3 f, out Vector3 r)
        {
            o = p.Position;
            f = p.Transform.Original.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            r = Vector3.Cross(Vector3.up, f);
        }

        // The frame his legs are solved in: where he knelt, facing the way his knees point
        // (KneelAnchorFwd). The same as his body while kneeling (his facing is frozen); during the
        // last part of a rise his body may turn on his planted feet (PivotOpen) and the feet stay.
        private static void FeetFrame(SurrenderState s, Player p, out Vector3 o, out Vector3 f, out Vector3 r)
        {
            Frame(p, out o, out f, out r);
            Vector3 a = s.KneelAnchorFwd; a.y = 0f;
            if (a.sqrMagnitude < 0.5f) return;
            f = a.normalized;
            r = Vector3.Cross(Vector3.up, f);
        }

        private static float AnkleHeight(KneelLeg leg) => ToeHeight + leg.FootLen * Mathf.Cos(ToeTuck);

        private static float Tilt(SurrenderState s) => (Settings.KneelThighAngle.Value + s.KneelThighOff) * Mathf.Deg2Rad;

        // Where the knee and the ankle go, on flat ground at his feet's height.
        private static void Layout(SurrenderState s, int i, Vector3 o, Vector3 f, Vector3 r, out Vector3 knee, out Vector3 foot)
        {
            KneelRig rig = s.Rig;
            KneelLeg leg = rig.Legs[i];
            knee = o + r * (leg.Side * (rig.HipHalfWidth + Settings.KneeSpread.Value + s.KneelSpreadOff)) +
                   f * (Settings.KneelHipsForward.Value + leg.Thigh * Mathf.Sin(Tilt(s)));
            float dy = AnkleHeight(leg) - Settings.KneeHeight.Value;
            foot = knee - f * Mathf.Sqrt(Mathf.Max(0.01f, leg.Shin * leg.Shin - dy * dy));
        }

        private static readonly string[] PartNames = { "left knee", "right knee", "left foot", "right foot" };

        private static bool Ground(SurrenderState s, out string why)
        {
            Frame(s.Bot, out Vector3 o, out Vector3 f, out Vector3 r);
            if (!GroundAt(s, o, f, r, s.KneelScratch, out why)) return false;
            for (int i = 0; i < 4; i++) s.KneelGround[i] = s.KneelScratch[i];
            s.KneelAnchor = o;
            s.KneelAnchorFwd = f;
            return true;
        }

        private static bool GroundAt(SurrenderState s, Vector3 o, Vector3 f, Vector3 r, float[] ground, out string why)
        {
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            float maxStep = Settings.KneelMaxStep.Value;
            float minUp = Mathf.Cos(Settings.KneelMaxSlope.Value * Mathf.Deg2Rad);
            for (int i = 0; i < 2; i++)
            {
                Layout(s, i, o, f, r, out Vector3 knee, out Vector3 foot);
                for (int j = 0; j < 2; j++)
                {
                    int slot = i + 2 * j;
                    Vector3 at = j == 0 ? knee : foot;
                    if (!Physics.Raycast(at + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 0.6f + maxStep + 0.3f, mask, QueryTriggerInteraction.Ignore))
                    { why = $"no ground under his {PartNames[slot]}"; return false; }
                    float dy = hit.point.y - o.y;
                    if (Mathf.Abs(dy) > maxStep) { why = $"uneven ground under his {PartNames[slot]} ({dy:+0.00;-0.00} m: a step, stairs or a ledge)"; return false; }
                    if (hit.normal.y < minUp) { why = $"{Mathf.Acos(Mathf.Clamp01(hit.normal.y)) * Mathf.Rad2Deg:0} deg slope under his {PartNames[slot]}"; return false; }
                    ground[slot] = dy;
                }
            }
            why = null;
            return true;
        }

        private static bool Room(SurrenderState s, out string why)
        {
            Frame(s.Bot, out Vector3 o, out Vector3 f, out Vector3 r);
            return RoomAt(s, o, f, r, out why);
        }

        // Shins behind him, elbows beside his head, his kneeling body (wider with an armoured
        // vest or rig on), a backpack behind his back, his head and helmet. Static geometry only.
        private static bool RoomAt(SurrenderState s, Vector3 o, Vector3 f, Vector3 r, out string why)
        {
            Player bot = s.Bot;
            KneelRig rig = s.Rig;
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            for (int i = 0; i < 2; i++)
            {
                Layout(s, i, o, f, r, out Vector3 knee, out Vector3 foot);
                Vector3 up = Vector3.up * (Settings.KneeHeight.Value + 0.08f);
                if (Blocked(Physics.OverlapCapsuleNonAlloc(knee + up, foot - f * 0.12f + up, 0.07f, Hits, mask, QueryTriggerInteraction.Ignore), out string what))
                { why = $"{what} where his {(i == 0 ? "left" : "right")} shin goes"; return false; }
            }
            float hipY = Settings.KneeHeight.Value + rig.Legs[0].Thigh * Mathf.Cos(Tilt(s));
            float headY = hipY + rig.TorsoLength;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 elbow = o + r * (side * 0.42f * Settings.ElbowsOut.Value) + f * 0.08f + Vector3.up * (headY - 0.02f);
                if (Blocked(Physics.OverlapSphereNonAlloc(elbow, 0.1f, Hits, mask, QueryTriggerInteraction.Ignore), out string what))
                { why = $"{what} at his {(side < 0 ? "left" : "right")} elbow"; return false; }
            }
            Kit(bot, out bool pack, out bool vest, out bool helmet);
            float body = 0.16f + (vest ? 0.03f : 0f);
            float top = headY + 0.12f + (helmet ? 0.04f : 0f);
            if (Blocked(Physics.OverlapCapsuleNonAlloc(o + Vector3.up * (hipY + 0.2f), o + Vector3.up * top, body, Hits, mask, QueryTriggerInteraction.Ignore), out string torso))
            { why = $"{torso} in the way of his kneeling body{(vest ? " and vest" : "")}"; return false; }
            if (pack && Blocked(Physics.OverlapCapsuleNonAlloc(o - f * 0.26f + Vector3.up * (hipY + 0.15f), o - f * 0.26f + Vector3.up * (headY - 0.2f), 0.13f, Hits, mask, QueryTriggerInteraction.Ignore), out string back))
            { why = $"{back} where his backpack goes"; return false; }
            why = null;
            return true;
        }

        private static void Kit(Player bot, out bool pack, out bool vest, out bool helmet)
        {
            var eq = bot.Equipment;
            pack = eq?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem != null;
            Item v = eq?.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem ?? eq?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            vest = v != null;
            helmet = eq?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem != null;
        }

        // Room and ground for the front foot's step on this side (a lifted step forward).
        private static bool StepRoom(SurrenderState s, Vector3 o, Vector3 f, Vector3 r, int leg, float step, float[] ground)
        {
            float side = s.Rig.Legs[leg].Side;
            Vector3 at = o + r * (side * 0.12f) + f * step;
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            if (Blocked(Physics.OverlapCapsuleNonAlloc(o + r * (side * 0.12f) + Vector3.up * 0.15f, at + Vector3.up * 0.15f, 0.08f, Hits, mask, QueryTriggerInteraction.Ignore), out _)) return false;
            if (!Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 0.5f + Settings.KneelMaxStep.Value + 0.3f, mask, QueryTriggerInteraction.Ignore)) return false;
            float dy = hit.point.y - o.y;
            if (Mathf.Abs(dy) > Settings.KneelMaxStep.Value) return false;
            ground[4 + leg] = dy;
            return true;
        }

        private static bool Blocked(int n, out string what)
        {
            what = null;
            if (n <= 0) return false;
            Collider c = Hits[0];
            what = c != null ? $"'{c.name}'" : "something";
            for (int i = 0; i < Hits.Length; i++) Hits[i] = null;
            return true;
        }

        private static void CheckMoved(SurrenderState s, float now)
        {
            if (now < s.KneelCheckAt) return;
            Player bot = s.Bot;
            Vector3 f = bot.Transform.Original.forward; f.y = 0f;
            bool moved = (bot.Position - s.KneelAnchor).sqrMagnitude > 0.15f * 0.15f ||
                         (f.sqrMagnitude > 1e-4f && Vector3.Angle(f, s.KneelAnchorFwd) > 10f);
            if (!moved) return;
            s.KneelCheckAt = now + 0.5f;
            if (Ground(s, out string why) && Room(s, out why))
            {
                if (Settings.VerboseLogs.Value) Log(s, "kneel: moved while down, ground checked again: fine");
                return;
            }
            Fallback(s, $"moved while down: {why}", tell: true);
            StartRise(s, KneelRise.Compliant, $"moved while down: {why}");
        }

        // ---- pose (ArmPosePatch, after the animator and the game's IK) ----------------------

        private static float Ss(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        private static float Bell(float a, float b, float t)
        {
            if (t <= a || t >= b) return 0f;
            return Mathf.Sin(Mathf.PI * (t - a) / (b - a));
        }

        private static float BraceWeight(SurrenderState s)
        {
            KneelStyle st = s.KneelStyle;
            if (!s.KneelBrace || st == null || !st.CanBrace) return 0f;
            float t = s.KneelT;
            float w = Ss(st.BraceOutA, st.BraceOutB, t) * (1f - Ss(st.BraceInA, st.BraceInB, t));
            if (w <= 0f) return 0f;
            w *= Mathf.SmoothStep(0f, 1f, s.KneelBraceGate);
            // Anything left when his legs are handed back fades with them (no snap at the end).
            if (s.Kneel == KneelPhase.Standing) w *= s.KneelFade / Mathf.Max(0.01f, s.KneelFadeTotal);
            return w;
        }

        // Player._bodyupdated (protected): true on frames the body animator ran (also gates
        // ArmPosePatch as a whole).
        internal static readonly AccessTools.FieldRef<Player, bool> BodyUpdated = AccessTools.FieldRefAccess<Player, bool>("_bodyupdated");

        private static Vector3 Local(Vector3 p, Vector3 o, Vector3 f, Vector3 r) { Vector3 d = p - o; return new Vector3(Vector3.Dot(d, r), d.y, Vector3.Dot(d, f)); }
        private static Vector3 World(Vector3 l, Vector3 o, Vector3 f, Vector3 r) => o + r * l.x + Vector3.up * l.y + f * l.z;
        private static Vector3 WorldDir(Vector3 l, Vector3 f, Vector3 r) => r * l.x + Vector3.up * l.y + f * l.z;

        // The standing pose the animator shows at the first frame down, body-local. Everything
        // is solved from it, so the crouch the game plays underneath never shows.
        private static void CaptureRef(SurrenderState s, KneelRig rig, Vector3 o, Vector3 f, Vector3 r)
        {
            for (int i = 0; i < 2; i++)
            {
                KneelLeg leg = rig.Legs[i];
                s.KneelRef[i] = Local(leg.Hip.position, o, f, r);
                s.KneelRef[2 + i] = Local(leg.Knee.position, o, f, r);
                s.KneelRef[4 + i] = Local(leg.Foot.position, o, f, r);
                s.KneelRef[6 + i] = Local(leg.Toe.position, o, f, r);
            }
            Vector3 mid = (rig.Legs[0].Hip.position + rig.Legs[1].Hip.position) * 0.5f;
            Vector3 spine = (rig.Head.position - mid).normalized;
            s.KneelRefSpine = new Vector3(Vector3.Dot(spine, r), spine.y, Vector3.Dot(spine, f));
            s.KneelRefOk = true;
            MeasureLegAxes(s, rig, r);
        }

        // 0.15.0: each leg bone's own (along, hinge) axes in its local space, from the standing
        // pose. The hinge is the leg's own bend plane (hip-knee-ankle) where the knee is bent
        // enough to tell (>= 4 deg, within 35 deg of his right), else his right; the standing knee
        // is bent ~8 deg. Then a leg bone's full rotation follows from (direction, hinge), the
        // way ArmPose.PoseArm does it for the arms (0.13.1): no roll left to chance.
        private static void MeasureLegAxes(SurrenderState s, KneelRig rig, Vector3 r)
        {
            s.KneelLegAxesOk = false;
            for (int i = 0; i < 2; i++)
            {
                KneelLeg leg = rig.Legs[i];
                if (leg.Mid != null)
                    for (int m = 0; m < leg.Mid.Length; m++) leg.MidRef[m] = leg.Mid[m] != null ? leg.Mid[m].localRotation : Quaternion.identity;
                Vector3 th = leg.Knee.position - leg.Hip.position, sh = leg.Foot.position - leg.Knee.position, ft = leg.Toe.position - leg.Foot.position;
                if (th.sqrMagnitude < 1e-4f || sh.sqrMagnitude < 1e-4f || ft.sqrMagnitude < 1e-6f) return;
                Vector3 h = Vector3.Cross(th.normalized, sh.normalized);
                if (h.magnitude < Mathf.Sin(4f * Mathf.Deg2Rad) || Vector3.Angle(h, r) > 35f) h = r;
                h.Normalize();
                SetAxes(s, 3 * i, leg.Hip, th, h);
                SetAxes(s, 3 * i + 1, leg.Knee, sh, h);
                SetAxes(s, 3 * i + 2, leg.Foot, ft, h);
            }
            s.KneelLegAxesOk = true;
        }

        private static void SetAxes(SurrenderState s, int k, Transform bone, Vector3 along, Vector3 hinge)
        {
            Vector3 a = along.normalized;
            Vector3 h = Vector3.ProjectOnPlane(hinge, a);
            if (h.sqrMagnitude < 1e-6f) h = Vector3.ProjectOnPlane(Vector3.up, a);
            Quaternion inv = Quaternion.Inverse(bone.rotation);
            s.KneelLegAlong[k] = inv * a;
            s.KneelLegHinge[k] = inv * h.normalized;
        }

        // A leg bone pointed along dir with its measured hinge on `hinge`, blended by w.
        private static void PoseBone(SurrenderState s, int k, Transform bone, Vector3 dir, Vector3 hinge, float w)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 h = Vector3.ProjectOnPlane(hinge, dir);
            if (h.sqrMagnitude < 1e-6f) return;
            Quaternion want = Quaternion.LookRotation(dir, h) * Quaternion.Inverse(Quaternion.LookRotation(s.KneelLegAlong[k], s.KneelLegHinge[k]));
            bone.rotation = Quaternion.Slerp(bone.rotation, want, w);
        }

        private static readonly Vector3[] RH = new Vector3[2], RHF = new Vector3[2], RK = new Vector3[2], RF = new Vector3[2], RT = new Vector3[2];

        // Pelvis, torso and legs, all from the standing reference. Skipped on a frame the game
        // didn't animate the body (the bones still hold last frame's pose).
        internal static void ApplyBody(Player p, SurrenderState s)
        {
            KneelRig rig = s.Rig;
            KneelStyle st = s.KneelStyle;
            if (rig == null || st == null || !BodyUpdated(p) || p.CustomAnimationsAreProcessing) return;
            Frame(p, out Vector3 o, out Vector3 f, out Vector3 r);
            FeetFrame(s, p, out Vector3 oF, out Vector3 fF, out Vector3 rF);
            if (!s.KneelRefOk)
            {
                // Only at the very start of a descent is the animator's pose his standing one;
                // Update holds t at 0 until this has run.
                if (s.Kneel != KneelPhase.GoingDown || s.KneelT > 0.001f) return;
                CaptureRef(s, rig, o, f, r);
            }
            float t = s.KneelT;
            float w = (s.Kneel != KneelPhase.Standing ? 1f : s.KneelFade / Mathf.Max(0.01f, s.KneelFadeTotal)) * Settings.KneelStrength.Value;
            if (w <= 0.0005f) return;

            // Hips from his body's frame; knees, feet and toes from the feet's frame (they differ
            // only while he turns on his planted feet at the end of a rise).
            for (int i = 0; i < 2; i++)
            {
                RH[i] = World(s.KneelRef[i], o, f, r);
                RHF[i] = World(s.KneelRef[i], oF, fF, rF);
                RK[i] = World(s.KneelRef[2 + i], oF, fF, rF);
                RF[i] = World(s.KneelRef[4 + i], oF, fF, rF);
                RT[i] = World(s.KneelRef[6 + i], oF, fF, rF);
            }
            Quaternion headRot = rig.Head.rotation;
            float shift = Presence.IdleShift(s);   // 0 unless settled on his knees (IdleW)
            // How much of the squaring is on: eased in over the first 12% of t (and out at the end
            // of a rise), times the kneel's own weight, so nothing snaps at either end.
            float sq = w * Ss(0f, 0.12f, t);

            // 0.15.0 Pelvis yaw: his hips square to the way his body (and knees) point. The swing
            // below only tilts the pelvis; its yaw was the animator's (with its trunk turned
            // towards a look), and the legs, solved in the planted frame, then twisted against it.
            s.KneelPelvisYaw = 0f;
            if (!Off(KneelPoseIsolate.PelvisSquare) && sq > 0f)
            {
                Vector3 hl = rig.Legs[1].Hip.position - rig.Legs[0].Hip.position; hl.y = 0f;
                if (hl.sqrMagnitude > 1e-4f)
                {
                    float yaw = Mathf.Clamp(Vector3.SignedAngle(hl, r, Vector3.up), -60f, 60f);
                    s.KneelPelvisYaw = yaw;
                    rig.Pelvis.rotation = Quaternion.AngleAxis(yaw * sq, Vector3.up) * rig.Pelvis.rotation;
                }
            }

            // Torso: from his standing lean to kneeling upright (with his own small lean), a
            // forward lean in the middle of the move, more while a hand pushes on his knee.
            Vector3 liveMid = (rig.Legs[0].Hip.position + rig.Legs[1].Hip.position) * 0.5f;
            Vector3 live = rig.Head.position - liveMid;
            if (live.sqrMagnitude > 1e-4f)
            {
                Vector3 refUp = WorldDir(s.KneelRefSpine, f, r);
                Vector3 kneelUp = Quaternion.AngleAxis(Settings.KneelLean.Value + s.KneelLeanOff, r) * Vector3.up;
                Vector3 want = Vector3.Slerp(refUp, kneelUp, Ss(st.LeadA, st.TrailB, t));
                float bump = st.LeanDeg * Settings.KneelLeanScale.Value * Bell(st.LeanA, st.LeanB, t) + BraceLean * BraceWeight(s);
                want = Quaternion.AngleAxis(bump, r) * want;
                // Idle weight shift: hips to one side, the upper body leans a touch the other way.
                if (shift != 0f) want = Quaternion.AngleAxis(shift * 40f, f) * want;
                Vector3 liveDir = live.normalized;
                rig.Pelvis.rotation = Quaternion.FromToRotation(liveDir, Vector3.Slerp(liveDir, want, w)) * rig.Pelvis.rotation;
            }

            // Pelvis: from his standing height down to kneeling height over the lead window.
            Vector3 hipsNow = (rig.Legs[0].Hip.position + rig.Legs[1].Hip.position) * 0.5f;
            float gK = o.y + 0.5f * (s.KneelGround[0] + s.KneelGround[1]);
            float thigh = 0.5f * (rig.Legs[0].Thigh + rig.Legs[1].Thigh);
            Vector3 kneelHips = o + f * Settings.KneelHipsForward.Value;
            kneelHips.y = gK + Settings.KneeHeight.Value + thigh * Mathf.Cos(Tilt(s));
            Vector3 refMid = (RH[0] + RH[1]) * 0.5f;
            Vector3 wantHips = Vector3.Lerp(refMid, kneelHips, Ss(st.LeadA, st.LeadB, t)) + r * shift;
            rig.Pelvis.position += Vector3.Lerp(hipsNow, wantHips, w) - hipsNow;

            rig.Head.rotation = Quaternion.Slerp(rig.Head.rotation, headRot, w);

            // 0.15.0 Torso: the shoulders square to the hips. Whatever trunk twist the animator
            // still plays (its look-to-body turn, its idle) is taken out here, so the head look's
            // torso share (Presence.ApplyUpper, bounded) starts from a square chest and is the only
            // turn above the hips. Split over the lower and upper spine about the spine's own axis.
            s.KneelChestYaw = 0f;
            if (!Off(KneelPoseIsolate.TorsoSquare) && sq > 0f) SquareTorso(p, s, rig, r, sq);
            if (Settings.LogPose.Value || Presence.TestDiag) PoseDiag(p, s, rig, f);

            int trail = 1 - s.KneelLead;
            for (int i = 0; i < 2; i++)
            {
                float k = i == s.KneelLead ? Ss(st.LeadA, st.LeadB, t) : Ss(st.TrailA, st.TrailB, t);
                // The front foot steps forward (a lifted arc) during the lead window.
                float stepW = i == trail ? Ss(st.LeadA, st.LeadA + 0.6f * (st.LeadB - st.LeadA), t) : 0f;
                Vector3 planted = RF[i] + fF * (s.KneelStepNow * stepW) + Vector3.up * (StepLift * Mathf.Sin(Mathf.PI * stepW) + s.KneelGround[4 + i] * stepW);
                SolveLeg(s, rig, i, k, w, oF, fF, rF, planted);
            }
        }

        // Shoulder line onto the hip line (both flattened), about the spine's own axis, half on
        // Spine1 and half on Spine3 (Spine3 under Spine1: it carries the first half too).
        private static void SquareTorso(Player p, SurrenderState s, KneelRig rig, Vector3 r, float sq)
        {
            PlayerBones b = p.PlayerBones;
            if (b?.Upperarms == null || b.Upperarms.Length < 2 || b.Upperarms[0] == null || b.Upperarms[1] == null) return;
            Vector3 sl = b.Upperarms[1].position - b.Upperarms[0].position;
            if (Vector3.Dot(sl, r) < 0f) sl = -sl;
            Vector3 hl = rig.Legs[1].Hip.position - rig.Legs[0].Hip.position;
            sl.y = 0f; hl.y = 0f;
            if (sl.sqrMagnitude < 1e-4f || hl.sqrMagnitude < 1e-4f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(sl, hl, Vector3.up), -60f, 60f);
            s.KneelChestYaw = yaw;
            Transform low = b.Spine1, high = b.Spine3?.Original, neck = b.Neck;
            if (low == null) return;
            Vector3 axis = Vector3.up;
            if (neck != null)
            {
                Vector3 v = neck.position - low.position;
                if (v.sqrMagnitude > 1e-4f && Vector3.Angle(v, Vector3.up) < 50f) axis = v.normalized;
            }
            float a = yaw * sq;
            if (high != null && high != low && high.IsChildOf(low))
            {
                low.rotation = Quaternion.AngleAxis(a * 0.5f, axis) * low.rotation;
                high.rotation = Quaternion.AngleAxis(a * 0.5f, axis) * high.rotation;
            }
            else low.rotation = Quaternion.AngleAxis(a, axis) * low.rotation;
        }

        // 14. Debug "Log pose diagnostics": once a second per posed bot, every contributor's share.
        private static void PoseDiag(Player p, SurrenderState s, KneelRig rig, Vector3 f)
        {
            float now = Time.time;
            if (now < s.KneelPoseLogAt) return;
            s.KneelPoseLogAt = now + 1f;
            MovementContext mc = p.MovementContext;
            float h2b = mc != null ? mc.HandsToBodyAngle : 0f;
            Vector3 a = s.KneelAnchorFwd; a.y = 0f;
            float root = a.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(a, f, Vector3.up) : 0f;
            PresenceState look = s.Look;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: pose: {s.Kneel} t {s.KneelT:0.00}; game look-to-body {h2b:+0;-0;0} deg (animated trunk twist follows this), " +
                $"root {root:+0;-0;0} deg from where he knelt; hips squared by {s.KneelPelvisYaw:+0;-0;0}, chest by {s.KneelChestYaw:+0;-0;0} deg; " +
                $"look yaw {look.Yaw:+0;-0;0} (torso {look.Assist:+0;-0;0}, head from chest {look.Yaw - look.Assist:+0;-0;0}), pitch {look.Pitch:+0;-0;0}, " +
                $"attention {look.Glance}{(s.CaptorInView ? "" : ", captor out of view")}; legs {(s.KneelLegAxesOk && !Off(KneelPoseIsolate.LegHinge) ? "hinged" : "swung")}; isolate {Settings.KneelPoseIsolate.Value}");
        }

        private static void SolveLeg(SurrenderState s, KneelRig rig, int i, float k, float w, Vector3 o, Vector3 f, Vector3 r, Vector3 planted)
        {
            KneelLeg leg = rig.Legs[i];
            Vector3 hip = leg.Hip.position;
            float gK = o.y + s.KneelGround[i], gF = o.y + s.KneelGround[i + 2];
            float kneeH = Settings.KneeHeight.Value, ankleH = AnkleHeight(leg);

            // Planted: his standing foot (or the stepped one), the knee bending the way it did.
            Vector3 hint = Perp(RK[i] - RHF[i], RF[i] - RHF[i], f);
            TwoBone(hip, planted, leg.Thigh, leg.Shin, hint, out Vector3 kneeP, out Vector3 footP);
            Vector3 thighP = (kneeP - hip).normalized;
            Vector3 shinP = (footP - kneeP).normalized;
            Vector3 toeP = RT[i] - RF[i];
            toeP = toeP.sqrMagnitude > 1e-6f ? toeP.normalized : f;

            // Kneeling: knee on the ground ahead of the hip, shin back, toes tucked under. (Not
            // instep-down: that turns the foot through pointing straight down near the ground.)
            Layout(s, i, o, f, r, out Vector3 kneeK, out _);
            kneeK.y = gK + kneeH;
            Vector3 thighK = (kneeK - hip).normalized;
            float dy = Mathf.Clamp((gF + ankleH) - (gK + kneeH), -0.8f * leg.Shin, 0.8f * leg.Shin);
            Vector3 shinK = (-f * Mathf.Sqrt(leg.Shin * leg.Shin - dy * dy) + Vector3.up * dy).normalized;
            Vector3 toeK = (f * Mathf.Sin(ToeTuck) - Vector3.up * Mathf.Cos(ToeTuck)).normalized;

            Vector3 thighDir = Vector3.Slerp(thighP, thighK, k);
            Vector3 knee = hip + thighDir * leg.Thigh;
            float kneeMin = gK + kneeH;
            if (knee.y < kneeMin && k > 0f) knee = OntoHeight(hip, thighDir, leg.Thigh, kneeMin, f);

            Vector3 shinDir = Vector3.Slerp(shinP, shinK, Ss(0f, 0.8f, k));
            Vector3 foot = knee + shinDir * leg.Shin;
            float footMin = gF + Mathf.Lerp(0.04f, ankleH, k);
            if (foot.y < footMin) foot = OntoHeight(knee, shinDir, leg.Shin, footMin, -f);

            Vector3 toeDir = Vector3.Slerp(toeP, toeK, k);
            Vector3 toe = foot + toeDir * leg.FootLen;
            if (toe.y < gF + ToeHeight) toeDir = (OntoHeight(foot, toeDir, leg.FootLen, gF + ToeHeight, -f) - foot).normalized;

            if (s.KneelLegAxesOk && !Off(KneelPoseIsolate.LegHinge))
            {
                // 0.15.0: full rotations on the leg's own hinge (the plane hip-knee-ankle as
                // solved, its normal on his right side), mid bones (thigh twist) at the standing
                // pose's local rotation: nothing of the animated crouch's roll is kept.
                if (leg.Mid != null)
                    for (int m = 0; m < leg.Mid.Length; m++)
                        if (leg.Mid[m] != null) leg.Mid[m].localRotation = Quaternion.Slerp(leg.Mid[m].localRotation, leg.MidRef[m], w);
                Vector3 T = knee - hip, S = foot - knee;
                Vector3 hn = Vector3.Cross(T.normalized, S.normalized);
                if (hn.magnitude < 0.05f) hn = r;
                else { hn.Normalize(); if (Vector3.Dot(hn, r) < 0f) hn = -hn; }
                PoseBone(s, 3 * i, leg.Hip, T, hn, w);
                PoseBone(s, 3 * i + 1, leg.Knee, foot - leg.Knee.position, hn, w);
                PoseBone(s, 3 * i + 2, leg.Foot, toeDir, hn, w);
            }
            else
            {
                ArmPose.Aim(leg.Hip, leg.Knee, knee - hip, w);
                ArmPose.Aim(leg.Knee, leg.Foot, foot - knee, w);
                ArmPose.Aim(leg.Foot, leg.Toe, toeDir, w);
            }
            s.KneelKneePos[i] = leg.Knee.position;
            s.KneelKneeFrame = Time.frameCount;

            if (Settings.KneelGizmos.Value)
            {
                s.GizmoKnee[i] = knee;
                s.GizmoFoot[i] = foot;
            }
        }

        private static Vector3 OntoHeight(Vector3 from, Vector3 dir, float length, float y, Vector3 fallback)
        {
            float dy = from.y - y;
            if (dy >= length) return from + Vector3.down * length;
            Vector3 flat = dir; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) { flat = fallback; flat.y = 0f; }
            flat.Normalize();
            return new Vector3(from.x, y, from.z) + flat * Mathf.Sqrt(Mathf.Max(0f, length * length - dy * dy));
        }

        private static Vector3 Perp(Vector3 v, Vector3 axis, Vector3 fallback)
        {
            if (axis.sqrMagnitude > 1e-6f)
            {
                Vector3 u = axis.normalized;
                v -= u * Vector3.Dot(v, u);
            }
            return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
        }

        // Shoulder-to-hand distance kept to what an elbow angle of 25 deg (fully folded) to 160 deg
        // (nearly straight, ~0.99 of the arm's length) gives: a two-bone solve then always has a
        // real, bent elbow.
        internal static Vector3 ClampReach(Vector3 shoulder, Vector3 target, float a, float b)
        {
            Vector3 d = target - shoulder;
            float m = d.magnitude;
            if (m < 1e-4f) return target;
            float max = Mathf.Sqrt(a * a + b * b - 2f * a * b * Mathf.Cos(160f * Mathf.Deg2Rad));
            float min = Mathf.Sqrt(a * a + b * b - 2f * a * b * Mathf.Cos(25f * Mathf.Deg2Rad));
            float want = Mathf.Clamp(m, min, max);
            return Mathf.Approximately(want, m) ? target : shoulder + d * (want / m);
        }

        internal static void TwoBone(Vector3 root, Vector3 target, float a, float b, Vector3 hint, out Vector3 joint, out Vector3 end)
        {
            Vector3 axis = target - root;
            float d = axis.magnitude;
            Vector3 u = d > 1e-4f ? axis / d : Vector3.down;
            d = Mathf.Clamp(d, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            float cos = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sin = Mathf.Sqrt(1f - cos * cos);
            Vector3 v = Perp(hint, u, Vector3.Cross(u, Vector3.up));
            if (v.sqrMagnitude < 1e-6f) v = Vector3.forward;
            joint = root + u * (a * cos) + v.normalized * (a * sin);
            end = root + u * d;
        }

        // Finds the arm on one side (by where the upper arm sits); false if the rig lacks it.
        private static bool Arm(PlayerBones b, float wantSide, Vector3 right, out Transform upper, out Transform fore, out Transform palm)
        {
            upper = fore = palm = null;
            Vector3 ribcage = b.Ribcage.Original.position;
            int count = Mathf.Min(b.Upperarms.Length, b.Forearms.Length);
            for (int i = 0; i < count; i++)
            {
                if (b.Upperarms[i] == null || b.Forearms[i] == null) continue;
                float side = Vector3.Dot(b.Upperarms[i].position - ribcage, right) >= 0f ? 1f : -1f;
                if (side != wantSide) continue;
                upper = b.Upperarms[i];
                fore = b.Forearms[i];
                palm = side > 0f ? b.RightPalm : b.LeftPalm;
                return palm != null;
            }
            return false;
        }

        // Hands from the hands-up pose to the back of his skull (0.13.1 rewrite). What 0.11.0-
        // 0.13.0 got wrong (offline, tools/armsim, defaults, look 0 / +-25 / 38 deg,
        // helmet on/off):
        //   - arms swung (ArmPose.Aim) from hands-up: the forearm ended up to ~120 deg rolled
        //     against the upper arm's hinge, the upper arm ~80 deg off its own: twisted arms;
        //   - the wrist was never set: the palm faced 19-56 deg away from the head;
        //   - the IK target was the wrist bone at the palm's resting place, so the palm centre and
        //     fingers ended 1-4 cm inside the head (helmet worst);
        //   - the path was a straight line from hands-up to behind the skull, projected onto a
        //     sphere when it cut through the head.
        // Now: the palm's centre rests on the back of the skull (Hands behind head), fingers
        // round it towards the middle, thumb down, palm to the skull; the wrist bone sits a palm
        // back from that. The wrist travels a curve beside the head (control point out by the
        // ear), never inside the skull sphere. The arm is posed by full rotations on its own hinge
        // (ArmPose.PoseArm), the roll eased in over the first 40% of the move; the wrist turns
        // over the last 65%. Offline: elbow twist 0, palm 0 deg from the skull, palm centre and
        // fingertips >= 3.5 cm clear (helmet incl.), wrist bend 25-45 deg, elbow 105-130 deg, no
        // elbow flips, hand/elbow steps <= 0.75 cm per 1/120 of the move.
        // 0.16.0: standing too (Hands on head); the weight is the hand-position order's (HandsPose),
        // which keeps each kneel style's own hand timing while he goes down or gets up.
        internal static void ApplyHands(Player p, SurrenderState s)
        {
            float h = HandsPose.Weight(s);
            if (h <= 0.001f) return;
            PlayerBones b = p.PlayerBones;
            if (b?.Upperarms == null || b.Forearms == null || b.Head == null || b.Ribcage == null) return;
            if (!ArmPose.TorsoFrame(p, b, out Vector3 right, out Vector3 up, out Vector3 fwd)) return;

            float helmet = HandsPose.Helmet(s) ? Settings.HelmetExtra.Value : 0f;
            // The head may be turned (Presence.ApplyHead, applied just before): the skull centre,
            // the hands' place on it and the elbows' lead turn with it, so the clearance round the
            // skull (and helmet) is kept wherever he looks.
            // 0.18.0: the skull's own frame when the look posed the head this frame (the head is set
            // absolutely now, so "torso frame x the turn applied" is no longer the skull's frame
            // when the animator had the head off the chest); else the torso frame as before.
            Vector3 upL, rightL, fwdL;
            if (!Presence.SkullFrame(s, out fwdL, out upL, out rightL))
            {
                Quaternion look = Presence.HeadDelta(s);
                upL = look * up; rightL = look * right; fwdL = look * fwd;
            }
            Vector3 skull = b.Head.Original.position + upL * Settings.HandsAboveNeck.Value;
            float minR = HeadRadius + helmet + 0.02f;
            float elbowsOut = Settings.ElbowsOut.Value + Presence.IdleElbow(s);
            HandRig hr = ArmPose.HandsOf(p, s);
            float roll = Ss(0f, 0.4f, h), ww = Ss(0.35f, 1f, h);
            for (int side = -1; side <= 1; side += 2)
            {
                if (!Arm(b, side, right, out Transform upper, out Transform fore, out Transform palm)) continue;
                int hi = side > 0 ? 1 : 0;
                Vector3 sh = upper.position, el = fore.position, pa = palm.position;
                float a = Vector3.Distance(sh, el), c = Vector3.Distance(el, pa);
                if (a < 0.05f || c < 0.05f) continue;
                bool hand = hr != null && hr.Ok(hi);

                // Where the palm ends: its centre on the back of the skull, a little to his side
                // of the middle (the two hands overlap there), fingers round towards the middle.
                Vector3 palmAt = skull - fwdL * (Settings.HandsBehindHead.Value + helmet) + rightL * (side * 0.05f);
                Vector3 faceEnd = skull - palmAt;
                Vector3 fingEnd = Vector3.ProjectOnPlane(-rightL * side + upL * 0.25f, faceEnd).normalized;
                Vector3 wristAt = hand ? palmAt - fingEnd * PalmCentre : palmAt;

                // The way there: a quadratic curve in skull space from where the hand is now,
                // past the side of his head (out at least 0.22 m, at the ear), to the back.
                Vector3 p0 = pa - skull, p2 = wristAt - skull;
                float lat = Mathf.Max(Mathf.Abs(Vector3.Dot(p0, rightL)), 0.22f);
                Vector3 p1 = rightL * (side * lat) + upL * 0.06f - fwdL * 0.02f;
                float u = 1f - h;
                Vector3 off = p0 * (u * u) + p1 * (2f * h * u) + p2 * (h * h);
                if (off.sqrMagnitude < minR * minR) off = (off.sqrMagnitude > 1e-6f ? off.normalized : -fwdL) * minR;
                Vector3 pos = skull + off;

                // Elbow: from where it is towards out to his side (a little forward and up), so it
                // bends the way an elbow bends all the way there; then the arm on its own hinge.
                Vector3 bendNow = Perp(el - sh, pa - sh, right * side);
                Vector3 bendOut = (rightL * (side * elbowsOut) + fwd * 0.35f + up * 0.25f).normalized;
                // 0.16.0: an arm never straightens to make contact (a locked elbow behind the head is
                // not a pose people hold) nor folds tighter than an elbow bends: the hand stops
                // short of an unreachable point instead (anatomy over exact contact), and nothing
                // else (neck, spine) is bent to reach it.
                pos = ClampReach(sh, pos, a, c);
                TwoBone(sh, pos, a, c, Vector3.Slerp(bendNow, bendOut, h), out Vector3 elbow, out Vector3 wrist);
                ArmPose.PoseArm(upper, fore, palm, elbow - sh, wrist - elbow, s, hi, 1f, roll);

                // Wrist: palm to the skull, fingers round it towards the middle, thumb down.
                if (hand)
                {
                    Vector3 face = skull - palm.position;
                    Vector3 fingers = Vector3.ProjectOnPlane(-rightL * side + upL * 0.25f, face);
                    ArmPose.SetWrist(palm, hr.Middle[hi], hr.Thumb[hi], hr.TwistA[hi], hr.TwistB[hi],
                                     palm.position - fore.position, fingers, -upL, face, ww);
                }
                if (Settings.KneelGizmos.Value) s.GizmoPalm[hi] = wrist;
            }
        }

        // The front-side hand on top of the front thigh, just above the knee, while he pushes up
        // over that leg (0.12.1 rewrite). What made 0.12.0 abrupt:
        //   - the reach and the let-go each took 0.1 of t (0.07-0.17 s), straight from the
        //     hands-up pose down to the knee and back;
        //   - a reach gate, (1.04 - ratio) / 0.06, multiplied the weight: the shoulder-to-knee
        //     distance crosses that 6 cm band in 1-2 frames as he leans and the knee comes up,
        //     so the hand jumped most of the way in a single frame (offline: 36-66 cm/frame);
        //   - the contact point was offset by world up, so it slid along the thigh as the thigh
        //     tilted, and the wrist stayed straight: the fingers went 2-6 cm into the thigh.
        // Now: the hand follows an arc (out to the side and over the thigh, landing from above)
        // from wherever the arm actually is this frame; the contact point is rebuilt every frame
        // from the solved leg on the thigh's top surface (it rides on the leg); the wrist bends so
        // the palm lies on the thigh with the fingers towards the knee; out of reach is handled
        // by the IK clamp on the way in and by a timed let-go while holding (UpdateBrace).
        internal static void ApplyBrace(Player p, SurrenderState s)
        {
            float e = BraceWeight(s);
            KneelRig rig = s.Rig;
            if (e <= 0.001f || rig == null || !rig.HandOk) return;
            // The knee positions are this frame's solve only (ApplyBody may have skipped).
            if (s.KneelKneeFrame != Time.frameCount) return;
            PlayerBones b = p.PlayerBones;
            if (b?.Upperarms == null || b.Forearms == null || b.Ribcage == null) return;
            Frame(p, out _, out Vector3 f, out Vector3 r);
            int leg = 1 - s.KneelLead;
            float side = rig.Legs[leg].Side;
            if (!Arm(b, side, r, out Transform upper, out Transform fore, out Transform palm)) return;
            int hi = side > 0f ? 1 : 0;
            Transform middle = rig.Middle[hi], thumb = rig.Thumb[hi];
            if (middle == null || thumb == null) return;

            // Contact frame on the front thigh: along = knee to hip; n = its top surface (up when
            // the thigh is level, forward as it stands up), a little to the outside.
            Vector3 knee = s.KneelKneePos[leg];
            Vector3 along = rig.Legs[leg].Hip.position - knee;
            if (along.sqrMagnitude < 1e-4f) return;
            along.Normalize();
            Vector3 outward = r * side;
            Vector3 n = Perp(Vector3.up + f, along, f);
            n = Perp(n + outward * 0.3f, along, n);
            Vector3 fingers = Vector3.ProjectOnPlane(-along - outward * 0.25f, n);
            if (fingers.sqrMagnitude < 1e-4f) return;
            fingers.Normalize();
            Vector3 target = knee + along * BraceUp + n * ThighRadius - fingers * PalmCentre + n * PalmThick;

            Vector3 sh = upper.position, el = fore.position, pa = palm.position;
            float a = Vector3.Distance(sh, el), c = Vector3.Distance(el, pa);
            if (a < 0.05f || c < 0.05f) return;
            // Arc from the arm's own position this frame: control point between the two, pushed
            // out from his side and up off the thigh, so the hand comes down onto it from above
            // and leaves it upwards, never through the leg or across his body.
            Vector3 ctrl = (pa + target) * 0.5f + outward * 0.10f + n * 0.12f;
            float u = 1f - e;
            Vector3 pos = pa * (u * u) + ctrl * (2f * e * u) + target * (e * e);

            // Stretched while holding (on or past the contact point, the IK goal beyond his arm):
            // let go by time rather than lock a straight arm. On the way in the IK clamp is
            // expected (the knee is still coming up to meet the hand) and is not a stretch.
            float ratio = Vector3.Distance(sh, pos) / (a + c);
            if (e > 0.9f && ratio > 1f && s.KneelStyle != null && s.KneelT <= s.KneelStyle.BraceInA && !s.KneelBraceStretch)
            {
                s.KneelBraceStretch = true;   // UpdateBrace lets go over LetGoSeconds
                s.KneelBraceRatio = ratio;
            }
            Vector3 bendNow = Perp(el - sh, pa - sh, outward);
            Vector3 bendOut = (outward * 0.8f - f * 0.3f + Vector3.up * 0.2f).normalized;
            TwoBone(sh, pos, a, c, Vector3.Slerp(bendNow, bendOut, e), out Vector3 elbow, out Vector3 hand);
            // On the arm's own hinge (0.13.1; swings before), the roll eased in with the reach.
            ArmPose.PoseArm(upper, fore, palm, elbow - sh, hand - elbow, s, hi, 1f, Ss(0f, 0.5f, e));

            // Wrist: fingers along the thigh towards the knee, thumb inwards, palm down onto the
            // thigh, bent at most WristMax. Eased in over the second half of the reach, so it
            // turns as the hand arrives.
            ArmPose.SetWrist(palm, middle, thumb, rig.TwistA[hi], rig.TwistB[hi], palm.position - fore.position, fingers, -outward, -n, Ss(0.4f, 1f, e));
            if (Settings.KneelGizmos.Value) s.GizmoPalm[hi] = hand;
        }

        // An escape from his knees called off (SurrenderLogic.ReassessEscape), continued from
        // wherever he is: t, the rise's motion and the legs are kept, so nothing restarts or
        // jumps. backDown: Update's reversal (Order Kneel, no longer urgent) takes him down the
        // same path; else the rest of the rise at the compliant pace. freeze: the motion eases to
        // a stop for that long first (hands coming back up meanwhile). Already on his feet (his
        // first step): the order decides (a fresh kneel, with its checks, or standing).
        internal static void Recomply(SurrenderState s, bool backDown, float freeze, string why)
        {
            s.KneelUrgent = false;
            s.KneelIntentSet = false;
            s.KneelMoveFrame = -100;
            s.KneelHoldUntil = freeze > 0f ? Time.time + freeze : 0f;
            if (s.Kneel == KneelPhase.Standing) return;
            if (backDown)
            {
                Log(s, $"kneel: {why}: back down from {s.KneelT:0.00}{(freeze > 0f ? $" after {freeze:0.0} s still" : "")}");
                return;
            }
            if (s.Kneel == KneelPhase.GettingUp) StartRise(s, KneelRise.Compliant, why);
        }

        // ---- debug (same order path as the menu, via Orders.Give with debug = true) ---------

        internal static string DebugKneel(SurrenderState s, Player me)
        {
            s.KneelTestLeft = 0;
            return Give(s, OrderKind.Kneel, me) + $" (style {Settings.KneelTestStyle.Value})";
        }

        internal static string DebugStand(SurrenderState s, Player me)
        {
            s.KneelTestLeft = 0;
            return Give(s, OrderKind.Stand, me) + $" (rise {Settings.KneelTestRise.Value}, brace {Settings.KneelTestBrace.Value})";
        }

        // An urgent rise for real: Escape = forced run, Defensive = forced draw (or a simulated
        // hit when he has no gun to draw). Both go through the layer's own decision paths.
        internal static string DebugUrgent(SurrenderState s, Player me)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            if (s.Kneel == KneelPhase.Standing) return "he isn't kneeling (Ctrl+K first)";
            s.KneelTestLeft = 0;
            if (Settings.KneelTestRise.Value == KneelRiseChoice.Defensive)
            {
                if (Arsenal.GunOnBody(s.Bot) != null) { s.ForceDraw = true; return "defensive rise: forced draw"; }
                s.WasHit = true;
                return "defensive rise: no gun on him, SIMULATED hit instead";
            }
            s.ForceRun = true;
            return "escape rise: forced run";
        }

        internal static string DebugLoop(SurrenderState s, Player me)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            int n = Settings.KneelTestCycles.Value;
            s.KneelTestNextAt = 0f;
            s.KneelTestBy = me;
            if (s.Kneel == KneelPhase.Standing)
            {
                string r = Give(s, OrderKind.Kneel, me);
                s.KneelTestLeft = n - 1;
                return $"{r}; kneel/stand loop x{n}";
            }
            s.KneelTestLeft = n;
            return $"kneel/stand loop x{n} (starts from where he is)";
        }

        internal static string DebugInterrupt(SurrenderState s, Player me)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            KneelInterruptDuring during = Settings.KneelTestDuring.Value;
            if (during == KneelInterruptDuring.GoingDown && s.Kneel != KneelPhase.Standing) return "stand him up first (test interrupts getting down)";
            if (during == KneelInterruptDuring.GettingUp && s.Kneel != KneelPhase.Down) return "kneel him first (test interrupts getting up)";
            s.KneelTestLeft = 0;
            s.KneelTestArmed = true;
            s.KneelTestBy = me;
            string r = Give(s, during == KneelInterruptDuring.GoingDown ? OrderKind.Kneel : OrderKind.Stand, me);
            return $"{r}; {Settings.KneelTestInterrupt.Value} at {Settings.KneelTestAt.Value:0.00} of {during}";
        }

        // 0.16.0: Hands on head / Hands up / Turn around from the debug keys (the menu's path).
        internal static string DebugOrder(SurrenderState s, OrderKind k, Player me)
        {
            s.KneelTestLeft = 0;
            return Give(s, k, me);
        }

        private static string Give(SurrenderState s, OrderKind k, Player me) =>
            Orders.Give(s, k, me, human: true, out string why, debug: true) ? $"ordered {k}" : $"order {k} refused: {why}";

        private static void DebugTest(SurrenderState s, float now)
        {
            if (s.KneelTestArmed)
            {
                KneelInterruptDuring during = Settings.KneelTestDuring.Value;
                bool inIt = during == KneelInterruptDuring.GoingDown ? s.Kneel == KneelPhase.GoingDown : s.Kneel == KneelPhase.GettingUp;
                float done = during == KneelInterruptDuring.GoingDown ? s.KneelT : 1f - s.KneelT;
                bool finished = during == KneelInterruptDuring.GoingDown ? s.Kneel == KneelPhase.Down : (s.Kneel == KneelPhase.Standing && s.KneelFade > 0f);
                if (s.Phase != Phase.HandsUp || s.KneelFallback != null || finished)
                {
                    s.KneelTestArmed = false;
                    Log(s, $"kneel test: no interruption ({(s.KneelFallback ?? (s.Phase != Phase.HandsUp ? $"now {s.Phase}" : "transition finished first"))})");
                }
                else if (inIt && done >= Settings.KneelTestAt.Value) Interrupt(s, during, done);
            }

            if (s.KneelTestLeft <= 0 || now < s.KneelTestNextAt) return;
            if (s.Phase != Phase.HandsUp || !Perception.Alive(s.KneelTestBy)) { s.KneelTestLeft = 0; Log(s, "kneel test loop stopped (no longer hands up)"); return; }
            if (s.Kneel == KneelPhase.Down)
            {
                s.KneelTestNextAt = float.MaxValue;
                Log(s, $"kneel test loop: stand ({s.KneelTestLeft} kneel(s) to go)");
                Give(s, OrderKind.Stand, s.KneelTestBy);
            }
            else if (s.Kneel == KneelPhase.Standing && s.KneelFade <= 0f && s.Order != Order.Kneel)
            {
                s.KneelTestNextAt = float.MaxValue;
                int left = s.KneelTestLeft - 1;
                Log(s, $"kneel test loop: kneel ({left} more after this)");
                Give(s, OrderKind.Kneel, s.KneelTestBy);
                s.KneelTestLeft = left;
                if (s.KneelFallback != null) { s.KneelTestLeft = 0; Log(s, $"kneel test loop stopped: {s.KneelFallback}"); }
            }
        }

        private static void Interrupt(SurrenderState s, KneelInterruptDuring during, float done)
        {
            s.KneelTestArmed = false;
            KneelInterrupt kind = Settings.KneelTestInterrupt.Value;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: kneel test: interrupting {during} at {done:0.00} (t {s.KneelT:0.00}) with {kind}");
            switch (kind)
            {
                case KneelInterrupt.Reverse:
                    Give(s, during == KneelInterruptDuring.GoingDown ? OrderKind.Stand : OrderKind.Kneel, s.KneelTestBy);
                    break;
                case KneelInterrupt.Hit:
                    s.WasHit = true;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: kneel test: SIMULATED hit (no damage)");
                    break;
                case KneelInterrupt.Run:
                    s.ForceRun = true;
                    break;
                case KneelInterrupt.Draw:
                    s.ForceDraw = true;
                    break;
                case KneelInterrupt.Kill:
                    HandsAreHighPlugin.Log.LogWarning($"{s.Name}: kneel test: killing him (debug) mid-{during}");
                    s.Bot.ActiveHealthController?.Kill(EDamageType.Undefined);
                    break;
            }
        }

        // ---- gizmos (overlay, debug only) ----------------------------------------------------

        private static GUIStyle _gizmo;

        internal static void DrawGizmos()
        {
            Camera cam = EFT.CameraControl.CameraManager.Instance?.Camera;
            if (cam == null) return;
            if (_gizmo == null) _gizmo = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Dead || s.Bot == null) continue;
                if (s.KneelSpotMoving) Mark(cam, s.KneelSpot, "kneel spot");
                if (!Posing(s)) continue;
                for (int i = 0; i < 2; i++)
                {
                    Mark(cam, s.GizmoKnee[i], "K");
                    Mark(cam, s.GizmoFoot[i], "F");
                    Mark(cam, s.GizmoPalm[i], "H");
                }
                Mark(cam, s.KneelAnchor, Describe(s) ?? "fade");
            }
        }

        private static void Mark(Camera cam, Vector3 world, string label)
        {
            if (world == Vector3.zero) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            float y = Screen.height - sp.y;
            GUI.Box(new Rect(sp.x - 3f, y - 3f, 6f, 6f), GUIContent.none);
            GUI.Label(new Rect(sp.x + 5f, y - 8f, 260f, 18f), label, _gizmo);
        }
    }
}
