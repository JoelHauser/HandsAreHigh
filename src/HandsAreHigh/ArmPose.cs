using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Player.VisualPass (run from Player.LateUpdate) is where the game finishes the arm
    // IK that keeps the hands on the weapon: FBBIKUpdate, IkProcess, AdjustElbows,
    // IkApply, then HandsController.ManualLateUpdate. A postfix there is the last word
    // on the arm bones before the frame renders.
    [HarmonyPatch(typeof(Player), nameof(Player.VisualPass))]
    internal static class ArmPosePatch
    {
        // Runs for every player every frame, so the common case (nobody held) is one static
        // count test. The game only calls VisualPass for the living (Player.LateUpdate checks
        // HealthController.IsAlive), and a held bot that died is flagged Dead by the game's
        // death event: no bone is ever written after death, the ragdoll is the game's alone.
        private static void Postfix(Player __instance)
        {
            if (!SurrenderRegistry.Any) return;
            if (!SurrenderRegistry.TryGet(__instance, out SurrenderState s) || s.Dead) return;
            // Frames on which the body animator didn't run (Player.ComplexUpdate sets
            // _bodyupdated; a bot updated in the fixed-update queue skips frames at high fps):
            // the bones still hold last frame's finished pose, ours included. Posing again on top
            // of it blended partial weights twice (arms and hands crept towards their targets,
            // then jumped back on the next animated frame). Nothing else writes the arm bones on
            // those frames with empty hands (AbstractHandsController.ManualLateUpdate is empty).
            if (!Kneel.BodyUpdated(__instance)) return;
            bool kneel = Kneel.Posing(s);
            bool look = Presence.Posing(s);
            if (s.Weight <= 0f && !kneel && !look) return;
            long t0 = Perf.Begin();
            // One owner per bone, in order: the kneel's body (pelvis, torso, legs), the torso's
            // share of a look and a flinch's hunch (upper spine, collarbones), then the hands-up
            // arms in the corrected torso frame, the neck and head (look, flinch), then the
            // hands-behind-head arms (they follow the head), then the knee brace (one arm). The
            // standing pose never runs after the kneel's.
            if (kneel)
            {
                try { Kneel.ApplyBody(__instance, s); }
                catch (System.Exception e) { HandsAreHighPlugin.Report("kneel pose", e); }
            }
            if (look)
            {
                try { Presence.ApplyUpper(__instance, s); }
                catch (System.Exception e) { HandsAreHighPlugin.Report("look torso", e); }
            }
            if (s.Weight > 0f) ArmPose.Apply(__instance, s);
            if (look)
            {
                try { Presence.ApplyHead(__instance, s); }
                catch (System.Exception e) { HandsAreHighPlugin.Report("look head", e); }
            }
            if (kneel)
            {
                try { Kneel.ApplyHands(__instance, s); Kneel.ApplyBrace(__instance, s); }
                catch (System.Exception e) { HandsAreHighPlugin.Report("kneel hands", e); }
            }
            Perf.End(Perf.Sec.Pose, t0);
        }
    }

    internal static class ArmPose
    {
        private static bool _loggedFailure;

        // Each frame the animator has already written its pose. Each arm bone is turned
        // so it points where the pose wants it, then blended from the animated rotation
        // by the weight, so the raise is eased and breathing still shows at < 100%.
        //
        // Directions are built in the torso's own frame and bones are aimed by where their
        // child bone sits, so this needs no knowledge of the rig's local bone axes.
        //
        // The frame comes from the animated torso, not the world: right = the shoulder line,
        // up = ribcage to head. While he walks, the walk animation leans and twists the spine;
        // a world-upright frame (0.2.0-0.3.0) kept the arms pointing the same way regardless,
        // so they came loose from the shoulders. In the torso frame they move with him.
        internal static void Apply(Player player, SurrenderState s)
        {
            PlayerBones bones = player.PlayerBones;
            if (bones?.Upperarms == null || bones.Forearms == null || bones.Ribcage == null) return;

            try
            {
                if (!TorsoFrame(player, bones, out Vector3 right, out Vector3 up, out Vector3 fwd)) return;

                float w = s.Weight * Settings.PoseStrength.Value;
                Vector3 ribcage = bones.Ribcage.Original.position;

                int count = Mathf.Min(bones.Upperarms.Length, bones.Forearms.Length);
                for (int i = 0; i < count; i++)
                {
                    Transform upper = bones.Upperarms[i];
                    Transform fore = bones.Forearms[i];
                    if (upper == null || fore == null) continue;

                    // Which side this arm is on, from where it actually is.
                    float side = Vector3.Dot(upper.position - ribcage, right) >= 0f ? 1f : -1f;
                    Transform palm = side > 0f ? bones.RightPalm : bones.LeftPalm;
                    if (palm == null) continue;
                    // The left arm is handed to the game's middle-finger animation while it plays.
                    float armW = side < 0f ? w * (1f - s.GestureArm) : w;
                    if (armW <= 0.001f) continue;

                    Vector2 shake = Tremble(s, i);

                    float raise = (Settings.UpperArmRaise.Value + shake.x) * Mathf.Deg2Rad;
                    float ahead = Settings.UpperArmForward.Value * Mathf.Deg2Rad;
                    Vector3 outward = right * side;
                    Vector3 upperDir = (outward * Mathf.Cos(ahead) + fwd * Mathf.Sin(ahead)) * Mathf.Cos(raise)
                                       + up * Mathf.Sin(raise);

                    // Forearm starts straight up, tips forward about the body's right
                    // axis, then in towards the head about its forward axis.
                    Vector3 foreDir =
                        Quaternion.AngleAxis((Settings.ForearmInward.Value + shake.y) * side, fwd) *
                        Quaternion.AngleAxis(Settings.ForearmForward.Value + shake.y * 0.5f, right) * up;

                    Aim(upper, fore, upperDir, armW);
                    Aim(fore, palm, foreDir, armW);

                    float twist = Settings.ForearmTwist.Value * side * armW;
                    if (twist != 0f)
                        fore.rotation = Quaternion.AngleAxis(twist, palm.position - fore.position) * fore.rotation;
                }
            }
            catch (System.Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    HandsAreHighPlugin.Log.LogError($"Arm pose failed (logged once): {e}");
                }
            }
        }

        // right: along the shoulders (both upper arms), signed to match the body's right;
        // up: ribcage to head, made square to right; fwd: right x up. Falls back to the body's
        // yaw frame if the bones are missing or degenerate.
        internal static bool TorsoFrame(Player player, PlayerBones bones, out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            Transform body = player.Transform.Original;
            Vector3 bodyRight = body.right;
            right = bodyRight;
            up = Vector3.up;
            fwd = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;

            Transform head = bones.Head?.Original;
            if (bones.Upperarms.Length >= 2 && bones.Upperarms[0] != null && bones.Upperarms[1] != null && head != null)
            {
                Vector3 shoulders = bones.Upperarms[1].position - bones.Upperarms[0].position;
                if (Vector3.Dot(shoulders, bodyRight) < 0f) shoulders = -shoulders;
                Vector3 spine = head.position - bones.Ribcage.Original.position;
                if (shoulders.sqrMagnitude > 1e-4f && spine.sqrMagnitude > 1e-4f)
                {
                    Vector3 r = shoulders.normalized;
                    Vector3 u = Vector3.ProjectOnPlane(spine, r).normalized;
                    if (u.sqrMagnitude > 0.5f)
                    {
                        right = r;
                        up = u;
                        fwd = Vector3.Cross(right, up);
                        return true;
                    }
                }
            }
            if (fwd.sqrMagnitude < 0.5f) return false;
            right = Vector3.Cross(Vector3.up, fwd);
            return true;
        }

        // Turn `bone` so the direction to `child` matches `desired`, blended by w.
        internal static void Aim(Transform bone, Transform child, Vector3 desired, float w)
        {
            Vector3 current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-6f) return;
            Quaternion target = Quaternion.FromToRotation(current, desired) * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, target, w);
        }

        // Smooth noise, centred on zero, in degrees. Per-bot seed and per-arm offset so
        // arms and bots don't shake in step.
        private static Vector2 Tremble(SurrenderState s, int arm)
        {
            float amp = Settings.TrembleDegrees.Value * Presence.Tremble(s);
            if (amp <= 0f) return Vector2.zero;
            float t = Time.time * Settings.TrembleSpeed.Value + s.Seed + arm * 13.7f;
            return new Vector2(Mathf.PerlinNoise(t, 0.5f) - 0.5f, Mathf.PerlinNoise(0.5f, t) - 0.5f) * (2f * amp);
        }
    }
}
