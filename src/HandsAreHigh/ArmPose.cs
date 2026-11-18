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
        private static void Postfix(Player __instance)
        {
            if (!SurrenderRegistry.Any) return;
            if (!SurrenderRegistry.TryGet(__instance, out SurrenderState s) || s.Weight <= 0f) return;
            ArmPose.Apply(__instance, s);
        }
    }

    internal static class ArmPose
    {
        private static bool _loggedFailure;

        // Each frame the animator has already written its pose. Each arm bone is turned
        // so it points where the pose wants it, then blended from the animated rotation
        // by the weight, so the raise is eased and breathing still shows at < 100%.
        //
        // Directions are built in the body's own frame (forward/right/up) and bones are
        // aimed by where their child bone sits, so this needs no knowledge of the rig's
        // local bone axes.
        internal static void Apply(Player player, SurrenderState s)
        {
            PlayerBones bones = player.PlayerBones;
            if (bones?.Upperarms == null || bones.Forearms == null || bones.Ribcage == null) return;

            try
            {
                Transform body = player.Transform.Original;
                Vector3 up = Vector3.up;
                Vector3 fwd = Vector3.ProjectOnPlane(body.forward, up).normalized;
                if (fwd.sqrMagnitude < 0.5f) return;
                Vector3 right = Vector3.Cross(up, fwd);

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

                    Aim(upper, fore, upperDir, w);
                    Aim(fore, palm, foreDir, w);

                    float twist = Settings.ForearmTwist.Value * side * w;
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

        // Turn `bone` so the direction to `child` matches `desired`, blended by w.
        private static void Aim(Transform bone, Transform child, Vector3 desired, float w)
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
            float amp = Settings.TrembleDegrees.Value;
            if (amp <= 0f) return Vector2.zero;
            float t = Time.time * Settings.TrembleSpeed.Value + s.Seed + arm * 13.7f;
            return new Vector2(Mathf.PerlinNoise(t, 0.5f) - 0.5f, Mathf.PerlinNoise(0.5f, t) - 0.5f) * (2f * amp);
        }
    }
}
