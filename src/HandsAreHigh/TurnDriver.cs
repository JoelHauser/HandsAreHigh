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
    //     is past it, so DeltaRotation never exceeds 45: bots spin. 0.16.0 turned the root itself
    //     (ApplyRotation) with the look 25 deg ahead: a spin with planted feet.
    // So for a big turn the prisoner's own TrunkRotationLimit is raised (the game itself sets it per
    // player, e.g. 25 while going prone) and his look runs StepLead (60) ahead of his root: the
    // animator plays its stepping turn and its root motion turns him; we never rotate the root then.
    // The limit is restored as soon as the step turn ends, he stops complying, leaves the registry or
    // the mod is torn down. Small corrections (StepFrom and under) are a pivot at a turning pace with
    // the look under 45 ahead (no step clip for a few degrees, as when a player nudges his aim). If
    // the root doesn't turn at all within StepCheck of a step turn (a rig or state without the clip),
    // that bot pivots from then on (logged once).
    internal static class TurnDriver
    {
        internal const float StepFrom = 70f;     // deg left to turn: from here the stepping turn
        private const float StepLead = 60f;      // look ahead of the root while stepping (> 45, < RaisedLimit)
        private const float RaisedLimit = 80f;
        private const float PivotLead = 35f;     // < 45: a pivot never starts a step clip
        private const float Dead = 3f;
        private const float StepCheck = 0.9f;    // s: the root must have turned by the clip by then

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
            s.DriveFrame = Time.frameCount;
            if (s.DriveMode == 0 && a <= start && a > Dead)
            {
                // Within the hysteresis: no turn; the look on it (a small trunk turn, the head the rest).
                s.DriveLook = dir;
                return 0f;
            }
            if (a <= Dead)
            {
                if (s.DriveMode == 1) EndStep(s, mc, "settled");
                s.DriveMode = 0;
                s.DriveLook = dir;
                return off;
            }
            bool step = a > StepFrom && !s.NoStepTurn && Settings.TurnSteps.Value;
            if (step)
            {
                if (s.DriveMode != 1)
                {
                    s.DriveMode = 1;
                    s.DriveSince = now;
                    s.DriveYaw0 = bot.Transform.Original.eulerAngles.y;
                    s.DriveWhat = what;
                    if (s.TrunkLimitOrig < 0f) { s.TrunkLimitOrig = mc.TrunkRotationLimit; mc.TrunkRotationLimit = RaisedLimit; }
                }
                s.DriveLook = Quaternion.AngleAxis(Mathf.Sign(off) * StepLead, Vector3.up) * f;
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
            s.DriveLook = Quaternion.AngleAxis(Mathf.Clamp(rest, -PivotLead, PivotLead), Vector3.up) * nf;
            return off;
        }

        private static void EndStep(SurrenderState s, MovementContext mc, string how)
        {
            if (s.DriveMode == 1 && how != null && (Settings.LogDecisions.Value || Settings.VerboseLogs.Value))
            {
                float turned = Mathf.Abs(Mathf.DeltaAngle(s.DriveYaw0, s.Bot.Transform.Original.eulerAngles.y));
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: stepped round {turned:0} deg with the game's turn animation in {Time.time - s.DriveSince:0.0} s ({s.DriveWhat}; {how})");
            }
            Restore(s, mc);
            s.DriveMode = 0;
        }

        private static void Restore(SurrenderState s, MovementContext mc)
        {
            if (s.TrunkLimitOrig < 0f) return;
            try { if (mc != null) mc.TrunkRotationLimit = s.TrunkLimitOrig; } catch { }
            s.TrunkLimitOrig = -1f;
        }

        // Nothing to turn to (moving, kneeling, acting, released, gone): the game's own limit back.
        internal static void Stop(SurrenderState s)
        {
            if (s == null) return;
            if (s.DriveMode == 1 && s.Bot != null) EndStep(s, s.Bot.MovementContext, "stopped");
            Restore(s, s.Bot?.MovementContext);
            s.DriveMode = 0;
        }

        // The look the driver wants this frame (Facing.Look), if it drove him this frame or the last.
        internal static bool Look(SurrenderState s, out Vector3 look)
        {
            look = s.DriveLook;
            return Time.frameCount - s.DriveFrame <= 1 && look.sqrMagnitude > 0.5f;
        }
    }
}
