using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Kneeling with the hands behind the head (0.11.0; styles, rise intents, knee brace 0.12.0).
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

    internal sealed class KneelLeg
    {
        internal Transform Hip, Knee, Foot, Toe;
        internal float Thigh, Shin, FootLen;
        internal float Side;   // -1 left, +1 right
    }

    internal sealed class KneelRig
    {
        internal Transform Pelvis, Head;
        internal readonly KneelLeg[] Legs = new KneelLeg[2];   // 0 left, 1 right
        internal float HipHalfWidth;
        internal float TorsoLength;   // hips to head bone
    }

    // Timing of one transition. Windows are in t (0 standing .. 1 kneeling); the brace windows
    // (rises only) are where the hand reaches the front thigh (In) and leaves it (Out).
    internal sealed class KneelStyle
    {
        internal string Name;
        internal float LeadA, LeadB, TrailA, TrailB, HandsA, HandsB, LeanA, LeanB, LeanDeg, Step;
        internal float BraceInA, BraceInB, BraceOutA, BraceOutB;
        internal bool CanBrace => BraceInB > 0f;
    }

    internal static class Kneel
    {
        // Descents. Offline-checked (scratchpad kneel_sim2.py): nothing under the ground, knee
        // 44-129 deg, continuous at 20-60 fps, reversals continuous.
        internal static readonly KneelStyle Steady = new KneelStyle
        { Name = "steady", LeadA = 0.04f, LeadB = 0.52f, TrailA = 0.50f, TrailB = 0.86f, HandsA = 0.76f, HandsB = 1f, LeanA = 0.04f, LeanB = 0.84f, LeanDeg = 12f, Step = 0.30f };
        internal static readonly KneelStyle Frightened = new KneelStyle
        { Name = "frightened", LeadA = 0.02f, LeadB = 0.46f, TrailA = 0.24f, TrailB = 0.70f, HandsA = 0.38f, HandsB = 0.82f, LeanA = 0.02f, LeanB = 0.72f, LeanDeg = 18f, Step = 0.14f };
        internal static readonly KneelStyle OneKnee = new KneelStyle
        { Name = "one knee first", LeadA = 0.03f, LeadB = 0.36f, TrailA = 0.62f, TrailB = 0.90f, HandsA = 0.80f, HandsB = 1f, LeanA = 0.03f, LeanB = 0.88f, LeanDeg = 10f, Step = 0.34f };
        // Rises. The brace holds while the front knee is up and within reach (0.78-0.98 of the
        // arm at full contact in the offline check).
        internal static readonly KneelStyle RiseCompliant = new KneelStyle
        { Name = "compliant", LeadA = 0.05f, LeadB = 0.50f, TrailA = 0.46f, TrailB = 0.84f, HandsA = 0.80f, HandsB = 1f, LeanA = 0.08f, LeanB = 0.86f, LeanDeg = 16f, Step = 0.30f,
          BraceInA = 0.52f, BraceInB = 0.62f, BraceOutA = 0.31f, BraceOutB = 0.40f };
        internal static readonly KneelStyle RiseDefensive = new KneelStyle
        { Name = "defensive", LeadA = 0.06f, LeadB = 0.48f, TrailA = 0.36f, TrailB = 0.78f, HandsA = 0.86f, HandsB = 1f, LeanA = 0.06f, LeanB = 0.80f, LeanDeg = 18f, Step = 0.26f,
          BraceInA = 0.48f, BraceInB = 0.58f, BraceOutA = 0.30f, BraceOutB = 0.38f };
        internal static readonly KneelStyle RiseEscape = new KneelStyle
        { Name = "escape", LeadA = 0.10f, LeadB = 0.52f, TrailA = 0.30f, TrailB = 0.74f, HandsA = 0.88f, HandsB = 1f, LeanA = 0.10f, LeanB = 0.92f, LeanDeg = 24f, Step = 0.36f,
          BraceInA = 0.47f, BraceInB = 0.57f, BraceOutA = 0.30f, BraceOutB = 0.38f };

        private const float CrouchAtT = 0.35f;    // going down: the game's crouch (capsule) from here
        private const float StandAtT = 0.50f;     // rising: the game's stand (capsule) from here
        private const float LegsFreeT = 0.15f;    // an urgent rise hands the legs back here
        private const float ToeHeight = 0.025f;
        private const float ToeTuck = 15f * Mathf.Deg2Rad;
        private const float HeadRadius = 0.10f;
        private const float StepLift = 0.08f;
        private const float BraceLean = 16f;
        private const float WaitLimit = 6f;
        private const float FadeCompliant = 0.2f, FadeUrgent = 0.14f;
        private static readonly Collider[] Hits = new Collider[4];

        private static void Log(SurrenderState s, string msg)
        {
            if (Settings.LogKneel.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {msg}");
        }

        // ---- ownership (asked by the layer, gestures, menu, registry, pose patch) ----------

        internal static bool Active(SurrenderState s) => s.Kneel != KneelPhase.Standing;

        // Our pose is on the bones (kneeling, a transition, or the short hand-back blend).
        internal static bool Posing(SurrenderState s) => s.Kneel != KneelPhase.Standing || s.KneelFade > 0f;

        // His legs are in the kneel: no step, no sprint, no turning on the spot.
        internal static bool LegsLocked(SurrenderState s) => s.Kneel != KneelPhase.Standing && s.KneelT > LegsFreeT;

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
            return s.Order == Order.Kneel && s.KneelFallback != null ? $"can't kneel: {s.KneelFallback}" : null;
        }

        // ---- requests ---------------------------------------------------------------------

        // Orders.Give (Kneel): repeating it changes nothing already happening.
        internal static string Request(SurrenderState s, out bool ok)
        {
            ok = true;
            s.KneelFallback = null;
            s.KneelWaitSince = Time.time;
            s.KneelCheckAt = 0f;
            if (s.Kneel == KneelPhase.Down) return $"{s.Name} is already on his knees";
            if (s.Kneel == KneelPhase.GoingDown) return $"{s.Name} is getting down";
            if (s.Kneel == KneelPhase.GettingUp && s.KneelUrgent) return $"{s.Name} is getting up";
            if (s.Kneel == KneelPhase.GettingUp) return $"{s.Name} gets back down";
            if (!StaticChecks(s, out string why))
            {
                ok = false;
                Fallback(s, why);
                return $"{s.Name} can't kneel here ({why}): {(Settings.KneelFallbackCrouch.Value ? "crouches" : "stays standing")}, hands up";
            }
            return $"{s.Name} gets down on his knees, hands behind his head";
        }

        // The layer, as it commits to an action that needs his legs (or his hands): what kind of
        // rise it needs. Consumed when Update sees he is no longer complying (same frame or next).
        internal static void Urge(SurrenderState s, KneelRise rise)
        {
            if (s == null || s.Kneel == KneelPhase.Standing) return;
            s.KneelIntent = rise;
            s.KneelIntentSet = true;
        }

        private static void Fallback(SurrenderState s, string why)
        {
            if (s.KneelFallback == why) return;
            s.KneelFallback = why;
            Log(s, $"kneel: not possible ({why}); {(Settings.KneelFallbackCrouch.Value ? "crouches with his hands up instead" : "stays standing with his hands up")}");
        }

        // ---- per frame (SurrenderRegistry.Tick, before gestures) ----------------------------

        internal static void Update(SurrenderState s, float dt)
        {
            if (s.Dead || s.Bot == null) return;
            if (s.KneelFade > 0f) s.KneelFade = Mathf.Max(0f, s.KneelFade - dt);
            if (s.Suspended) return;   // asleep: the pose stays where it is
            float now = Time.time;

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
                    if (s.Order != Order.Kneel || s.Phase != Phase.HandsUp) { s.KneelFallback = null; break; }
                    if (s.KneelFallback != null) break;
                    if (!Settings.KneelEnabled.Value) { Fallback(s, "kneeling is off in the config"); break; }
                    TryBegin(s, now);
                    break;

                case KneelPhase.GoingDown:
                    if (!want) { StartRise(s, s.KneelUrgent ? s.KneelRise : KneelRise.Compliant, WhyUp(s)); break; }
                    s.KneelT += dt / Mathf.Max(0.1f, s.KneelSecs);
                    if (s.KneelT >= 1f)
                    {
                        s.KneelT = 1f;
                        s.Kneel = KneelPhase.Down;
                        Log(s, $"kneel: on his knees, hands behind his head ({s.KneelStyle.Name}; {Timeline(s)})");
                        s.KneelTestNextAt = now + 1.2f;
                    }
                    break;

                case KneelPhase.Down:
                    if (!want) { StartRise(s, KneelRise.Compliant, WhyUp(s)); break; }
                    CheckMoved(s, now);
                    break;

                case KneelPhase.GettingUp:
                    if (want && !s.KneelUrgent)
                    {
                        s.Kneel = KneelPhase.GoingDown;
                        s.KneelSecs = DownSeconds(s, s.KneelStyle);
                        s.KneelBrace = false;
                        Log(s, $"kneel: back down from {s.KneelT:0.00} (ordered down again while rising; {s.KneelStyle.Name} timing kept)");
                        StartClock(s);
                        break;
                    }
                    s.KneelT -= dt / Mathf.Max(0.1f, s.KneelSecs);
                    // An urgent rise hands his legs back as soon as they are under him, so his
                    // first step follows without a pause; a compliant one finishes standing.
                    float end = s.KneelUrgent ? LegsFreeT : 0f;
                    if (s.KneelT <= end)
                    {
                        s.KneelT = Mathf.Max(0f, s.KneelT);
                        s.Kneel = KneelPhase.Standing;
                        s.KneelFade = s.KneelFadeTotal = s.KneelUrgent ? FadeUrgent : FadeCompliant;
                        Log(s, $"kneel: back on his feet ({s.KneelRise}{(s.KneelBrace ? ", braced on his knee" : "")}; {Timeline(s)})");
                        s.KneelUrgent = false;
                        s.KneelBrace = false;
                        s.KneelTestNextAt = now + 1.2f;
                    }
                    break;
            }

            DebugTest(s, now);
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
            float steady = Settings.KneelWeightSteady.Value * (0.6f + nerve) * (hurt > 0.3f ? 1.5f : 1f) / Mathf.Max(0.6f, timid);
            float scared = Settings.KneelWeightFrightened.Value * (1.5f - nerve) * timid * (hurt > 0.5f ? 0.3f : 1f) * (heavy ? 0.7f : 1f);
            float one = Settings.KneelWeightOneKnee.Value * (heavy ? 1.4f : 1f) * (s.Personality == "Rat" ? 1.3f : 1f) * (hurt > 0.3f ? 1.2f : 1f);
            float total = steady + scared + one;
            float roll = Dice.Value * Mathf.Max(0.0001f, total);
            why = $"steady {steady:0.00} / frightened {scared:0.00} / one knee {one:0.00}, roll {roll:0.00} (nerve {nerve:0.00}, hurt {hurt:0.00}{(heavy ? ", heavy kit" : "")})";
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
            return b * (1f + 0.5f * s.Injury) * (Heavy(s.Bot) ? 1.1f : 1f);
        }

        // Rise duration from intent, slowed by wounds, bad legs and heavy kit: a badly hurt man
        // never gets the healthy man's escape speed.
        private static float RiseSeconds(SurrenderState s, KneelRise rise)
        {
            float b, hurt;
            switch (rise)
            {
                case KneelRise.Escape: b = Settings.KneelQuickSeconds.Value; hurt = 0.9f; break;
                case KneelRise.Defensive: b = Settings.KneelDefensiveSeconds.Value; hurt = 0.7f; break;
                default: b = Settings.KneelUpSeconds.Value * (s.KneelDescent == Frightened ? 0.85f : 1f); hurt = 0.5f; break;
            }
            return b * (1f + hurt * s.Injury) * (s.LegsBad ? 1.5f : 1f) * (Heavy(s.Bot) ? 1.08f : 1f);
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
                s.KneelStyle = rise == KneelRise.Escape ? RiseEscape : rise == KneelRise.Defensive ? RiseDefensive : RiseCompliant;
                s.KneelBrace = PickBrace(s, rise, out string braceWhy);
                s.KneelStepNow = Mathf.Min(s.KneelStyle.Step, s.KneelStep + 0.06f);   // no longer than the room found
                if (Settings.LogKneel.Value && braceWhy != null) Log(s, $"kneel: brace on his knee: {braceWhy}");
            }
            s.KneelRise = rise;
            s.KneelSecs = RiseSeconds(s, rise);
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
            if (s.DrawPending) { why = "no: his hands are drawing"; return false; }
            if (c == KneelBraceChoice.Always) { why = "yes (debug choice)"; return true; }
            float chance = Settings.KneelBraceChance.Value * (rise == KneelRise.Escape ? 1.3f : rise == KneelRise.Defensive ? 0.45f : 1f) *
                           (1f + 0.8f * s.Injury) * (Heavy(s.Bot) ? 1.2f : 1f);
            float roll = Dice.Value;
            bool yes = roll < chance;
            why = $"chance {chance:0.00}, roll {roll:0.00} -> {(yes ? "yes" : "no")}";
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
                if (now - s.KneelWaitSince > WaitLimit) Fallback(s, $"never settled ({wait})");
                else if (Settings.VerboseLogs.Value) Log(s, $"kneel: waits ({wait})");
                return;
            }
            if (!StaticChecks(s, out string why)) { Fallback(s, why); return; }

            KneelStyle style = PickDescent(s, out string styleWhy);
            // Lead leg: his own side (from his per-raid seed) most of the time; the other side if
            // there's no room for the front foot's step on this one; a short step if neither.
            int dominant = s.Seed >= 50f ? 1 : 0;
            int lead = Dice.Value < 0.8f ? dominant : 1 - dominant;
            float step = style.Step;
            string stepWhy = "";
            if (!StepRoom(s, 1 - lead, step))
            {
                if (StepRoom(s, lead, step)) { lead = 1 - lead; stepWhy = ", other leg leads (no room for the step)"; }
                else { step = 0.08f; stepWhy = ", short step (no room in front)"; }
            }

            Gestures.Cancel(s, "getting down on his knees");
            s.Kneel = KneelPhase.GoingDown;
            s.KneelUrgent = false;
            s.KneelT = 0f;
            s.KneelStyle = style;
            s.KneelDescent = style;
            s.KneelStep = step;
            s.KneelStepNow = step;
            s.KneelBraceWarned = false;
            s.KneelLead = lead;
            s.KneelBrace = false;
            s.KneelRefOk = false;
            s.KneelSecs = DownSeconds(s, style);
            // Small, stable differences in how he ends up (from his seed, not per frame).
            float n1 = Mathf.Sin(s.Seed * 12.9898f) * 0.5f, n2 = Mathf.Sin(s.Seed * 78.233f) * 0.5f, n3 = Mathf.Sin(s.Seed * 37.719f) * 0.5f;
            s.KneelThighOff = 4f * n1;
            s.KneelLeanOff = 3f * n2 + (style == Frightened ? 5f : 0f);
            s.KneelSpreadOff = 0.02f * n3;
            s.KneelHelmet = bot.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem != null;
            s.KneelHelmetAt = now + 1f;
            StartClock(s);
            Log(s, $"kneel: gets down, {style.Name} ({styleWhy}), {s.KneelSecs:0.0} s, {(lead == 1 ? "right" : "left")} knee first{stepWhy}; " +
                   $"ground {s.KneelGround[0]:+0.00;-0.00}/{s.KneelGround[1]:+0.00;-0.00} knees, {s.KneelGround[2]:+0.00;-0.00}/{s.KneelGround[3]:+0.00;-0.00} feet, " +
                   $"{(s.KneelHelmet ? "helmet" : "no helmet")}");
        }

        private static bool StaticChecks(SurrenderState s, out string why)
        {
            Player bot = s.Bot;
            if (!Settings.KneelEnabled.Value) { why = "kneeling is off in the config"; return false; }
            if (s.LegsBad) { why = "a leg is too badly hurt"; return false; }
            if (bot.IsInPronePose) { why = "prone"; return false; }
            if (bot.UsedSimplifiedSkeleton) { why = "simplified skeleton"; return false; }
            if (s.Rig == null && !FindRig(bot, out s.Rig, out why)) return false;
            return Ground(s, out why) && Room(s, out why);
        }

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
            return leg;
        }

        private static Transform FindDeep(Transform t, string name, int depth)
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

        // ---- ground and room (at the start, and again only if he is moved) ------------------

        private static void Frame(Player p, out Vector3 o, out Vector3 f, out Vector3 r)
        {
            o = p.Position;
            f = p.Transform.Original.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
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
            Player bot = s.Bot;
            Frame(bot, out Vector3 o, out Vector3 f, out Vector3 r);
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
                    s.KneelGround[slot] = dy;
                }
            }
            s.KneelAnchor = o;
            s.KneelAnchorFwd = f;
            why = null;
            return true;
        }

        // Shins behind him, elbows beside his head, his kneeling body. Static geometry only.
        private static bool Room(SurrenderState s, out string why)
        {
            Player bot = s.Bot;
            KneelRig rig = s.Rig;
            Frame(bot, out Vector3 o, out Vector3 f, out Vector3 r);
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
            if (Blocked(Physics.OverlapCapsuleNonAlloc(o + Vector3.up * (hipY + 0.2f), o + Vector3.up * (headY + 0.12f), 0.16f, Hits, mask, QueryTriggerInteraction.Ignore), out string top))
            { why = $"{top} in the way of his kneeling body"; return false; }
            why = null;
            return true;
        }

        // Room and ground for the front foot's step on this side (a lifted step forward).
        private static bool StepRoom(SurrenderState s, int leg, float step)
        {
            Player bot = s.Bot;
            Frame(bot, out Vector3 o, out Vector3 f, out Vector3 r);
            float side = s.Rig.Legs[leg].Side;
            Vector3 at = o + r * (side * 0.12f) + f * step;
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            if (Blocked(Physics.OverlapCapsuleNonAlloc(o + r * (side * 0.12f) + Vector3.up * 0.15f, at + Vector3.up * 0.15f, 0.08f, Hits, mask, QueryTriggerInteraction.Ignore), out _)) return false;
            if (!Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 0.5f + Settings.KneelMaxStep.Value + 0.3f, mask, QueryTriggerInteraction.Ignore)) return false;
            float dy = hit.point.y - o.y;
            if (Mathf.Abs(dy) > Settings.KneelMaxStep.Value) return false;
            s.KneelGround[4 + leg] = dy;
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
            Fallback(s, $"moved while down: {why}");
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
            return Ss(st.BraceOutA, st.BraceOutB, t) * (1f - Ss(st.BraceInA, st.BraceInB, t));
        }

        // Player._bodyupdated (protected): true on frames the body animator ran.
        private static readonly AccessTools.FieldRef<Player, bool> BodyUpdated = AccessTools.FieldRefAccess<Player, bool>("_bodyupdated");

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
        }

        private static readonly Vector3[] RH = new Vector3[2], RK = new Vector3[2], RF = new Vector3[2], RT = new Vector3[2];

        // Pelvis, torso and legs, all from the standing reference. Skipped on a frame the game
        // didn't animate the body (the bones still hold last frame's pose).
        internal static void ApplyBody(Player p, SurrenderState s)
        {
            KneelRig rig = s.Rig;
            KneelStyle st = s.KneelStyle;
            if (rig == null || st == null || !BodyUpdated(p) || p.CustomAnimationsAreProcessing) return;
            Frame(p, out Vector3 o, out Vector3 f, out Vector3 r);
            if (!s.KneelRefOk)
            {
                // Only at the very start of a descent is the animator's pose his standing one.
                if (s.Kneel != KneelPhase.GoingDown || s.KneelT > 0.2f) return;
                CaptureRef(s, rig, o, f, r);
            }
            float t = s.KneelT;
            float w = (s.Kneel != KneelPhase.Standing ? 1f : s.KneelFade / Mathf.Max(0.01f, s.KneelFadeTotal)) * Settings.KneelStrength.Value;
            if (w <= 0.0005f) return;

            for (int i = 0; i < 2; i++)
            {
                RH[i] = World(s.KneelRef[i], o, f, r);
                RK[i] = World(s.KneelRef[2 + i], o, f, r);
                RF[i] = World(s.KneelRef[4 + i], o, f, r);
                RT[i] = World(s.KneelRef[6 + i], o, f, r);
            }
            Quaternion headRot = rig.Head.rotation;

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
            Vector3 wantHips = Vector3.Lerp(refMid, kneelHips, Ss(st.LeadA, st.LeadB, t));
            rig.Pelvis.position += Vector3.Lerp(hipsNow, wantHips, w) - hipsNow;

            rig.Head.rotation = Quaternion.Slerp(rig.Head.rotation, headRot, w);

            int trail = 1 - s.KneelLead;
            for (int i = 0; i < 2; i++)
            {
                float k = i == s.KneelLead ? Ss(st.LeadA, st.LeadB, t) : Ss(st.TrailA, st.TrailB, t);
                // The front foot steps forward (a lifted arc) during the lead window.
                float stepW = i == trail ? Ss(st.LeadA, st.LeadA + 0.6f * (st.LeadB - st.LeadA), t) : 0f;
                Vector3 planted = RF[i] + f * (s.KneelStepNow * stepW) + Vector3.up * (StepLift * Mathf.Sin(Mathf.PI * stepW) + s.KneelGround[4 + i] * stepW);
                SolveLeg(s, rig, i, k, w, o, f, r, planted);
            }
        }

        private static void SolveLeg(SurrenderState s, KneelRig rig, int i, float k, float w, Vector3 o, Vector3 f, Vector3 r, Vector3 planted)
        {
            KneelLeg leg = rig.Legs[i];
            Vector3 hip = leg.Hip.position;
            float gK = o.y + s.KneelGround[i], gF = o.y + s.KneelGround[i + 2];
            float kneeH = Settings.KneeHeight.Value, ankleH = AnkleHeight(leg);

            // Planted: his standing foot (or the stepped one), the knee bending the way it did.
            Vector3 hint = Perp(RK[i] - RH[i], RF[i] - RH[i], f);
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

            ArmPose.Aim(leg.Hip, leg.Knee, knee - hip, w);
            ArmPose.Aim(leg.Knee, leg.Foot, foot - knee, w);
            ArmPose.Aim(leg.Foot, leg.Toe, toeDir, w);
            s.KneelKneePos[i] = leg.Knee.position;

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

        // Hands from the hands-up pose to the back of his skull, elbows out, along a path pushed
        // out around the head (helmet included). Weighted by the arms' own raise.
        internal static void ApplyHands(Player p, SurrenderState s)
        {
            KneelStyle st = s.KneelStyle;
            if (st == null || s.Kneel == KneelPhase.Standing) return;
            float h = Ss(st.HandsA, st.HandsB, s.KneelT) * s.Weight * Settings.HandsBehindHeadStrength.Value;
            if (h <= 0.001f) return;
            PlayerBones b = p.PlayerBones;
            if (b?.Upperarms == null || b.Forearms == null || b.Head == null || b.Ribcage == null) return;
            if (!ArmPose.TorsoFrame(p, b, out Vector3 right, out Vector3 up, out Vector3 fwd)) return;

            float helmet = s.KneelHelmet ? Settings.HelmetExtra.Value : 0f;
            Vector3 skull = b.Head.Original.position + up * Settings.HandsAboveNeck.Value;
            float minR = HeadRadius + helmet + 0.02f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (!Arm(b, side, right, out Transform upper, out Transform fore, out Transform palm)) continue;
                Vector3 sh = upper.position, el = fore.position, pa = palm.position;
                float a = Vector3.Distance(sh, el), c = Vector3.Distance(el, pa);
                if (a < 0.05f || c < 0.05f) continue;

                Vector3 target = skull - fwd * (Settings.HandsBehindHead.Value + helmet) + right * (side * 0.05f);
                Vector3 pos = Vector3.Lerp(pa, target, h);
                Vector3 fromSkull = pos - skull;
                if (fromSkull.sqrMagnitude < minR * minR)
                    pos = skull + (fromSkull.sqrMagnitude > 1e-6f ? fromSkull.normalized : -fwd) * minR;

                Vector3 bendNow = Perp(el - sh, pa - sh, right * side);
                Vector3 bendOut = (right * (side * Settings.ElbowsOut.Value) + fwd * 0.35f + up * 0.25f).normalized;
                TwoBone(sh, pos, a, c, Vector3.Slerp(bendNow, bendOut, h), out Vector3 elbow, out Vector3 hand);
                ArmPose.Aim(upper, fore, elbow - sh, 1f);
                ArmPose.Aim(fore, palm, hand - elbow, 1f);
                if (Settings.KneelGizmos.Value) s.GizmoPalm[side > 0 ? 1 : 0] = hand;
            }
        }

        // The front-side hand on top of the front thigh, just above the knee, while he pushes up
        // over that leg. The point rides on the thigh (so the hand moves with the leg, never
        // across it). Beyond comfortable reach the brace eases off instead of stretching the arm.
        internal static void ApplyBrace(Player p, SurrenderState s)
        {
            float bw = BraceWeight(s);
            if (bw <= 0.001f || s.Rig == null) return;
            PlayerBones b = p.PlayerBones;
            if (b?.Upperarms == null || b.Forearms == null || b.Ribcage == null) return;
            Frame(p, out _, out Vector3 f, out Vector3 r);
            int leg = 1 - s.KneelLead;
            float side = s.Rig.Legs[leg].Side;
            if (!Arm(b, side, r, out Transform upper, out Transform fore, out Transform palm)) return;
            Vector3 knee = s.KneelKneePos[leg], hip = s.Rig.Legs[leg].Hip.position;
            Vector3 target = knee + Vector3.up * 0.07f + (hip - knee).normalized * 0.10f;

            Vector3 sh = upper.position, el = fore.position, pa = palm.position;
            float a = Vector3.Distance(sh, el), c = Vector3.Distance(el, pa);
            if (a < 0.05f || c < 0.05f) return;
            float ratio = Vector3.Distance(sh, target) / (a + c);
            float reach = Mathf.Clamp01((1.04f - ratio) / 0.06f);
            if (reach < 1f && !s.KneelBraceWarned && bw > 0.9f)
            {
                s.KneelBraceWarned = true;
                Log(s, $"kneel: brace eased off (knee at {ratio:0.00} of his reach)");
            }
            bw *= reach;
            if (bw <= 0.001f) return;
            Vector3 pos = Vector3.Lerp(pa, target, bw);
            Vector3 bendNow = Perp(el - sh, pa - sh, r * side);
            Vector3 bendOut = (r * (side * 0.8f) - f * 0.3f + Vector3.up * 0.2f).normalized;
            TwoBone(sh, pos, a, c, Vector3.Slerp(bendNow, bendOut, bw), out Vector3 elbow, out Vector3 hand);
            ArmPose.Aim(upper, fore, elbow - sh, 1f);
            ArmPose.Aim(fore, palm, hand - elbow, 1f);
            if (Settings.KneelGizmos.Value) s.GizmoPalm[side > 0 ? 1 : 0] = hand;
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
                if (s.Dead || s.Bot == null || !Posing(s)) continue;
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
