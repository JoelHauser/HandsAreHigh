using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.17.0: how a standing prisoner turns his body (Turn around, facing his captor, turning before
    // he kneels). One driver, used from SurrenderRegistry.Tick through Facing.Update; the layer
    // only applies the look it asks for (Facing.Look).
    //
    // The game's own standing turn (verified 2026-11-20 in player_anim_controller, read with UnityPy,
    // and the 0.16.9 decompile):
    //   - the body animator goes Idle_Aim -> Turn_Right_90_Aim when its DeltaRotation parameter is
    //     greater than 45, -> Turn_Left_90_Aim when less than -45; DeltaRotation is the look yaw minus
    //     the root yaw (MovementContext.UpdateAnimatorRotationParameters);
    //   - the turn clip steps the feet and turns the root with its root motion: every movement state
    //     applies AnimatorDeltaRotation to the root (MovementState.ProcessAnimatorMovement), and
    //     ApplyRotation adds the clip's Turn curve to DeltaRotation while it plays;
    //   - but a bot never gets there: MovementState.ProcessUpperbodyRotation snaps an AI root to
    //     exactly TrunkRotationLimit (45, EFTHardSettings.HANDS_TO_BODY_MAX_ANGLE) as soon as the look
    //     is past it (isAi: no Lerp, the root is set in that frame), so DeltaRotation never exceeds
    //     45: bots spin. 0.16.0 turned the root itself (ApplyRotation) with the look 25 deg ahead: a
    //     spin with planted feet.
    // So for a big turn the prisoner's own TrunkRotationLimit is raised (the game itself sets it per
    // player, e.g. 25 while going prone) and his look runs ahead of his root: the animator plays its
    // stepping turn and its root motion turns him; we never rotate the root then.
    //
    // 0.22.0 (the shoulders lagging behind a turn; 0.21.0 log: "stepped round 113 deg ... the rest by
    // a pivot" on every Turn around):
    //   - the step turn handed over to the pivot at 70 deg left, so a 177 deg turn ended with ~65 deg
    //     of the root spun on planted feet. Steps now continue while more than StepExit (48) is left:
    //     the look sits on the target itself once it is less than StepLead away (still > 45, so the
    //     clip keeps stepping); the pivot only settles the last few degrees under the trunk's reach;
    //   - at that hand-over the look's lead dropped from 60 to <= 35 deg in one frame and the trunk
    //     limit went from 80 back to 45 in the same frame: the game snapped the root 15 deg (isAi) and
    //     the steering swung the upper body back 25 deg against the turn. The lead is now eased
    //     (LeadRate deg/s) and the raised limit comes down with the look (EaseLimit: never below the
    //     current look/root difference, so ProcessUpperbodyRotation never snaps the root);
    //   - Settled in Facing is above Dead here (a turn that stopped 2.5 deg short never "finished":
    //     "couldn't finish turning (2 deg off after 8 s)").
    // Small corrections (StepFrom and under) are a pivot at a turning pace with the look ahead (no
    // step clip for a few degrees, as when a player nudges his aim). If the root doesn't turn at all
    // within StepCheck of a step turn (a rig or state without the clip), that bot pivots from then on
    // (logged once).
    internal static class TurnDriver
    {
        internal const float StepFrom = 70f;     // deg left to turn: from here the stepping turn
        private const float StepExit = 48f;      // ... and it keeps stepping while more than this is left
        private const float StepLead = 60f;      // look ahead of the root while stepping (> 45, < RaisedLimit)
        private const float RaisedLimit = 80f;
        private const float PivotLead = 35f;     // < 45: a pivot never starts a step clip
        internal const float Dead = 3f;
        private const float StepCheck = 0.9f;    // s: the root must have turned by the clip by then
        private const float LeadRate = 220f;     // deg/s: how fast the look's lead over the root may change
        private const float LimitEaseMax = 0.75f; // s: the raised trunk limit is back by then at the latest

        // Turns him towards dir (flat); returns the signed degrees still to turn.
        // start: how far off he must be before a turn begins (hysteresis; once turning he goes to Dead).
        internal static float Drive(SurrenderState s, Vector3 dir, float dt, string what, float start = Dead)
        {
            Player bot = s.Bot;
            MovementContext mc = bot.MovementContext;
            dir.y = 0f;
            Vector3 f = bot.Transform.Original.forward; f.y = 0f;
            if (mc == null || dir.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f) { Stop(s); return 0f; }
            dir.Normalize(); f.Normalize();
            float off = Vector3.SignedAngle(f, dir, Vector3.up);
            float a = Mathf.Abs(off), now = Time.time;
            bool fresh = Time.frameCount - s.DriveFrame > 2;
            s.DriveFrame = Time.frameCount;
            s.DriveOff = off;
            s.DriveFor = what;
            if (fresh && s.DriveMode == 0) s.DriveLead = 0f;
            if (s.DriveMode == 0 && a <= start && a > Dead)
            {
                // Within the hysteresis: no turn; the look on it (a small trunk turn, the head the rest).
                Lead(s, off, dt, f);
                EaseLimit(s, mc, now);
                return 0f;
            }
            if (a <= Dead)
            {
                if (s.DriveMode == 1) EndStep(s, mc, "settled");
                s.DriveMode = 0;
                Lead(s, off, dt, f);
                EaseLimit(s, mc, now);
                return off;
            }
            bool canStep = !s.NoStepTurn && Settings.TurnSteps.Value;
            bool step = canStep && (a > StepFrom || (s.DriveMode == 1 && a > StepExit));
            if (step)
            {
                if (s.DriveMode != 1)
                {
                    s.DriveMode = 1;
                    s.DriveSince = now;
                    s.DriveYaw0 = bot.Transform.Original.eulerAngles.y;
                    s.DriveWhat = what;
                    if (s.TrunkLimitOrig < 0f) s.TrunkLimitOrig = mc.TrunkRotationLimit;
                    mc.TrunkRotationLimit = RaisedLimit;
                    s.LimitEaseSince = -1f;
                }
                // Ahead of the root by StepLead, or on the target itself once it is nearer (> 45 still:
                // the clip keeps stepping until StepExit).
                Lead(s, Mathf.Sign(off) * Mathf.Min(a, StepLead), dt, f, LeadRate * 1.5f);
                float turned = Mathf.Abs(Mathf.DeltaAngle(s.DriveYaw0, bot.Transform.Original.eulerAngles.y));
                if (now - s.DriveSince > StepCheck && turned < 8f)
                {
                    s.NoStepTurn = true;
                    EndStep(s, mc, null);
                    HandsAreHighPlugin.Log.LogWarning($"{s.Name}: no stepping turn from the game's animator ({turned:0} deg in {now - s.DriveSince:0.0} s, {what}); he pivots instead from now on");
                }
                return off;
            }
            if (s.DriveMode == 1) EndStep(s, mc, "the rest by a pivot");
            s.DriveMode = 2;
            float speed = Mathf.Max(30f, Settings.TurnSpeed.Value * Mathf.SmoothStep(0f, 1f, a / 35f));
            float stepDeg = Mathf.Sign(off) * Mathf.Min(a, speed * dt);
            mc.ApplyRotation(Quaternion.AngleAxis(stepDeg, Vector3.up) * mc.TransformRotation);
            float rest = off - stepDeg;
            Vector3 nf = Quaternion.AngleAxis(stepDeg, Vector3.up) * f;
            Lead(s, Mathf.Clamp(rest, -PivotLead, PivotLead), dt, nf);
            EaseLimit(s, mc, now);
            return off;
        }

        // The look this frame: `want` deg ahead of the root (signed), reached at `rate` deg/s from
        // the lead it had (the upper body never swings back or jumps ahead in one frame).
        private static void Lead(SurrenderState s, float want, float dt, Vector3 rootFwd, float rate = LeadRate)
        {
            s.DriveLead = Mathf.MoveTowards(s.DriveLead, want, rate * Mathf.Max(dt, 1e-3f));
            s.DriveLook = Quaternion.AngleAxis(s.DriveLead, Vector3.up) * rootFwd;
        }

        private static void EndStep(SurrenderState s, MovementContext mc, string how)
        {
            if (s.DriveMode == 1 && how != null && (Settings.LogDecisions.Value || Settings.VerboseLogs.Value))
            {
                float turned = Mathf.Abs(Mathf.DeltaAngle(s.DriveYaw0, s.Bot.Transform.Original.eulerAngles.y));
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: stepped round {turned:0} deg with the game's turn animation in {Time.time - s.DriveSince:0.0} s ({s.DriveWhat}; {how})");
            }
            s.DriveMode = 0;
            if (s.TrunkLimitOrig >= 0f && s.LimitEaseSince < 0f) s.LimitEaseSince = Time.time;
        }

        // The raised trunk limit comes back down no faster than the look comes back over the root:
        // never below the current look/root difference (+1), so the game's AI snap in
        // ProcessUpperbodyRotation has nothing to snap. Back to the game's own value once the look is
        // within it, or after LimitEaseMax at the latest.
        private static void EaseLimit(SurrenderState s, MovementContext mc, float now)
        {
            if (s.TrunkLimitOrig < 0f || s.DriveMode == 1) return;
            if (s.LimitEaseSince < 0f) s.LimitEaseSince = now;
            if (mc == null) { s.TrunkLimitOrig = -1f; s.LimitEaseSince = -1f; return; }
            float orig = s.TrunkLimitOrig;
            float diff = 0f;
            try { diff = Mathf.Abs(mc.HandsToBodyAngle); } catch { }
            if (diff <= orig || now - s.LimitEaseSince > LimitEaseMax) { Restore(s, mc); return; }
            try { mc.TrunkRotationLimit = Mathf.Clamp(Mathf.Min(mc.TrunkRotationLimit, diff + 1f), orig, RaisedLimit); } catch { }
        }

        private static void Restore(SurrenderState s, MovementContext mc)
        {
            if (s.TrunkLimitOrig < 0f) return;
            try { if (mc != null) mc.TrunkRotationLimit = s.TrunkLimitOrig; } catch { }
            s.TrunkLimitOrig = -1f;
            s.LimitEaseSince = -1f;
        }

        // Nothing to turn to (moving, kneeling, acting, asleep): the game's limit comes back with the
        // look (EaseLimit), within LimitEaseMax. Facing.Update calls this every frame he isn't turned.
        internal static void Stop(SurrenderState s)
        {
            if (s == null) return;
            MovementContext mc = s.Bot?.MovementContext;
            if (s.DriveMode == 1 && s.Bot != null) EndStep(s, mc, "stopped");
            s.DriveMode = 0;
            EaseLimit(s, mc, Time.time);
        }

        // Leaving us (released, gone, mod off, hot reload): the game's own limit back at once.
        internal static void StopNow(SurrenderState s)
        {
            if (s == null) return;
            MovementContext mc = s.Bot?.MovementContext;
            if (s.DriveMode == 1 && s.Bot != null) EndStep(s, mc, "stopped");
            s.DriveMode = 0;
            s.DriveLead = 0f;
            Restore(s, mc);
        }

        // The look the driver wants this frame (Facing.Look), if it drove him this frame or the last.
        internal static bool Look(SurrenderState s, out Vector3 look)
        {
            look = s.DriveLook;
            return Time.frameCount - s.DriveFrame <= 1 && look.sqrMagnitude > 0.5f;
        }

        // 0.22.0: is he being turned right now (not merely held on a direction), and which way:
        // Presence leads his head into the turn instead of holding it on where he was looking.
        internal static bool Turning(SurrenderState s, out float off, out string what)
        {
            off = s.DriveOff; what = s.DriveFor;
            return Time.frameCount - s.DriveFrame <= 1 && s.DriveMode != 0 && Mathf.Abs(off) > Dead;
        }
    }
}
