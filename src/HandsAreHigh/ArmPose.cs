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
            // The arms' hinge axes, from the game's own pose, before anything of ours touches it.
            if (!s.ArmAxisOk[0] || !s.ArmAxisOk[1]) ArmPose.MeasureAxes(__instance, s);
            bool kneel = Kneel.Posing(s);
            bool look = Presence.Posing(s);
            bool head = HandsPose.Posing(s);   // 0.16.0: hands on head, standing or kneeling
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
            if (kneel || head)
            {
                try { if (head) Kneel.ApplyHands(__instance, s); if (kneel) Kneel.ApplyBrace(__instance, s); }
                catch (System.Exception e) { HandsAreHighPlugin.Report("kneel hands", e); }
            }
            Perf.End(Perf.Sec.Pose, t0);
        }
    }

    // A hand's own bones (0 left, 1 right), found by the skeleton's names: the middle finger's
    // root (which way the fingers run), the thumb's root (which side the palm is), and the
    // forearm twist bones (Forearm2/Forearm3, the palm's parents) that share a wrist's roll.
    internal sealed class HandRig
    {
        internal readonly Transform[] Middle = new Transform[2], Thumb = new Transform[2], TwistA = new Transform[2], TwistB = new Transform[2];
        internal bool Ok(int hi) => Middle[hi] != null && Thumb[hi] != null;
    }

    internal static class ArmPose
    {
        private static bool _loggedFailure;

        internal const float WristMax = 80f;      // deg of wrist bend at most
        internal const float ThumbTilt = 27.7f;   // thumb root: 0.040 m to the side, 0.021 m palm-ward

        // Found once per surrender (null if the palms are missing); destroyed bones read as null.
        internal static HandRig HandsOf(Player p, SurrenderState s)
        {
            if (s.HandsLooked) return s.Hands;
            s.HandsLooked = true;
            PlayerBones b = p.PlayerBones;
            Transform lp = b?.LeftPalm, rp = b?.RightPalm;
            if (lp == null || rp == null) return null;
            var h = new HandRig();
            h.Middle[0] = Kneel.FindDeep(lp, "Base HumanLDigit31", 1);
            h.Thumb[0] = Kneel.FindDeep(lp, "Base HumanLDigit11", 1);
            h.Middle[1] = Kneel.FindDeep(rp, "Base HumanRDigit31", 1);
            h.Thumb[1] = Kneel.FindDeep(rp, "Base HumanRDigit11", 1);
            for (int i = 0; i < 2; i++)
            {
                Transform t3 = (i == 0 ? lp : rp).parent;
                string sd = i == 0 ? "L" : "R";
                if (t3 != null && t3.name == $"Base Human{sd}Forearm3" && t3.parent != null && t3.parent.name == $"Base Human{sd}Forearm2")
                { h.TwistB[i] = t3; h.TwistA[i] = t3.parent; }
            }
            s.Hands = h;
            if (!h.Ok(0) || !h.Ok(1))
                HandsAreHighPlugin.Log.LogWarning($"{s.Name}: no Digit31/Digit11 finger bones under the palms: wrists follow the forearms (no palm orientation)");
            return h;
        }

        // Lay the palm: fingers along `fingers`, the thumb towards `thumbDir`, the palm facing
        // `palmFaces`; the wrist bends at most WristMax from the forearm. Measured from the live
        // finger bones (no assumption about the palm bone's axes). A third of the roll about the
        // forearm on each twist bone, the rest (and all of the bend) at the wrist, so the wrist
        // doesn't candy-wrap. Blended from the current palm by ww. "Kneel brace palm flip"
        // (14. Debug) mirrors the thumb if this rig's palm turns out to be on the other side.
        internal static void SetWrist(Transform palm, Transform middle, Transform thumb, Transform ta, Transform tb,
                                      Vector3 foreDir, Vector3 fingers, Vector3 thumbDir, Vector3 palmFaces, float ww)
        {
            if (ww <= 0.001f || palm == null || middle == null || thumb == null) return;
            if (foreDir.sqrMagnitude < 1e-6f || fingers.sqrMagnitude < 1e-6f) return;
            Vector3 fW = fingers.normalized;
            float bend = Vector3.Angle(foreDir, fW);
            if (bend > WristMax) fW = Vector3.Slerp(foreDir.normalized, fW, WristMax / bend);
            Vector3 inward = Vector3.ProjectOnPlane(thumbDir, fW);
            if (inward.sqrMagnitude < 1e-6f) return;
            inward.Normalize();
            Vector3 thumbWant = inward * Mathf.Cos(ThumbTilt * Mathf.Deg2Rad) + palmFaces.normalized * Mathf.Sin(ThumbTilt * Mathf.Deg2Rad);
            if (Settings.KneelBracePalmFlip.Value) thumbWant = -thumbWant;
            thumbWant = Vector3.ProjectOnPlane(thumbWant, fW);
            Vector3 fLive = middle.position - palm.position;
            Vector3 tLive = Vector3.ProjectOnPlane(thumb.position - palm.position, fLive);
            if (thumbWant.sqrMagnitude < 1e-6f || tLive.sqrMagnitude < 1e-8f || fLive.sqrMagnitude < 1e-6f) return;
            Quaternion turn = Quaternion.LookRotation(fW, thumbWant) * Quaternion.Inverse(Quaternion.LookRotation(fLive, tLive));
            Quaternion want = Quaternion.Slerp(palm.rotation, turn * palm.rotation, ww);
            if (ta != null && tb != null)
            {
                Quaternion part = Quaternion.Slerp(Quaternion.identity, TwistAbout(want * Quaternion.Inverse(palm.rotation), foreDir), 1f / 3f);
                ta.rotation = part * ta.rotation;
                tb.rotation = part * tb.rotation;
            }
            palm.rotation = want;
        }

        // The part of q that turns about axis (swing-twist decomposition).
        internal static Quaternion TwistAbout(Quaternion q, Vector3 axis)
        {
            Vector3 v = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
            float m = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z + q.w * q.w);
            if (m < 1e-6f) return Quaternion.identity;
            return new Quaternion(v.x / m, v.y / m, v.z / m, q.w / m);
        }

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

                    PoseArm(upper, fore, palm, upperDir, foreDir, s, side > 0f ? 1 : 0, armW, 1f);

                    float twist = Settings.ForearmTwist.Value * side * armW;
                    if (twist != 0f)
                        fore.rotation = Quaternion.AngleAxis(twist, palm.position - fore.position) * fore.rotation;

                    // Palms to the captor, fingers up along the forearm, thumbs towards the head:
                    // with the arm's roll now its own, the wrist is set too (before, the palm
                    // faced wherever the swings left it; the hands-behind-head move starts here).
                    HandRig hr = HandsOf(player, s);
                    int hi = side > 0f ? 1 : 0;
                    if (hr != null && hr.Ok(hi))
                        SetWrist(palm, hr.Middle[hi], hr.Thumb[hi], hr.TwistA[hi], hr.TwistB[hi],
                                 palm.position - fore.position, foreDir, -outward, fwd, armW);
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

        // ---- arm rotations with roll (0.13.1) ---------------------------------------------
        //
        // Aim (below) is a swing: FromToRotation turns the bone the shortest way and leaves its
        // roll to chance. Swung from the animator's hanging arm to hands-up (100-170 deg of
        // forearm swing) and on to the back of the head, each bone's roll drifts on its own:
        // offline (tools/armsim/armsim.py) the forearm ended ~120 deg rolled against the upper
        // arm's hinge behind the head, the upper arm bending ~80 deg off its own hinge: the
        // twisted arms. Hands-up alone carried ~30 deg of it.
        //
        // The cure needs each bone's elbow hinge in its own local space. The rig's bone axes are
        // not assumed: they are measured once from the game's pose (any frame where the elbow is
        // bent 20-160 deg, before anything of ours writes the bones), as the normal of the plane
        // shoulder-elbow-wrist. Then a bone's full rotation follows from (direction, hinge):
        // R = LookRotation(dir, hinge) * Inverse(LookRotation(localAlong, localHinge)).
        // Upper arm and forearm share the hinge, so the elbow is a pure hinge (no twist).

        internal static void MeasureAxes(Player player, SurrenderState s)
        {
            PlayerBones b = player.PlayerBones;
            if (b?.Upperarms == null || b.Forearms == null || b.Ribcage == null) return;
            if (s.ArmAxisTries > 600) return;   // ~10 s of straight arms: keep the swing fallback
            s.ArmAxisTries++;
            if (!TorsoFrame(player, b, out Vector3 right, out _, out _)) return;
            Vector3 ribcage = b.Ribcage.Original.position;
            int count = Mathf.Min(b.Upperarms.Length, b.Forearms.Length);
            for (int i = 0; i < count; i++)
            {
                Transform upper = b.Upperarms[i], fore = b.Forearms[i];
                if (upper == null || fore == null) continue;
                int hi = Vector3.Dot(upper.position - ribcage, right) >= 0f ? 1 : 0;
                if (s.ArmAxisOk[hi]) continue;
                Transform palm = hi == 1 ? b.RightPalm : b.LeftPalm;
                if (palm == null) continue;
                Vector3 u = fore.position - upper.position, f = palm.position - fore.position;
                if (u.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f) continue;
                float flex = Vector3.Angle(u, f);
                if (flex < 20f || flex > 160f) continue;
                Vector3 hinge = Vector3.Cross(u, f).normalized;
                Quaternion iu = Quaternion.Inverse(upper.rotation), ifo = Quaternion.Inverse(fore.rotation);
                s.ArmUpAlong[hi] = iu * u.normalized;
                s.ArmUpHinge[hi] = iu * hinge;
                s.ArmForeAlong[hi] = ifo * f.normalized;
                s.ArmForeHinge[hi] = ifo * hinge;
                s.ArmAxisOk[hi] = true;
                if (Settings.VerboseLogs.Value || Settings.LogKneel.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {(hi == 1 ? "right" : "left")} arm hinge measured from the game's pose (elbow bent {flex:0} deg); arms posed with roll");
            }
        }

        // Pose one arm along two world directions (upper arm, forearm) as full rotations.
        // roll 0..1: how much of the measured hinge roll to take (1 = the arm's own hinge;
        // 0 = keep the roll the bones have now, carried rigidly to the new directions).
        // Blended from the bones' current rotations by w. Falls back to swings (Aim) where a
        // hinge is undefined (arm straight, or axes not measured yet).
        internal static void PoseArm(Transform upper, Transform fore, Transform palm, Vector3 upperDir, Vector3 foreDir, SurrenderState s, int hi, float w, float roll)
        {
            if (w <= 0f) return;
            Vector3 hn = Vector3.Cross(upperDir, foreDir);
            Vector3 cu = fore.position - upper.position, cf = palm.position - fore.position;
            Vector3 hc = Vector3.Cross(cu, cf);
            if (hn.sqrMagnitude < 1e-6f || upperDir.sqrMagnitude < 1e-6f || foreDir.sqrMagnitude < 1e-6f || cu.sqrMagnitude < 1e-6f || cf.sqrMagnitude < 1e-6f)
            {
                Aim(upper, fore, upperDir, w);
                Aim(fore, palm, foreDir, w);
                return;
            }
            bool abs = s != null && s.ArmAxisOk[hi] && roll > 0f;
            Quaternion lookU = Quaternion.LookRotation(upperDir, hn);
            // Carried rigidly (keeps today's roll): needs the current bend plane.
            Quaternion relU = hc.sqrMagnitude > 1e-6f
                ? lookU * Quaternion.Inverse(Quaternion.LookRotation(cu, hc)) * upper.rotation
                : Quaternion.FromToRotation(cu, upperDir) * upper.rotation;
            Quaternion wantU = abs
                ? Quaternion.Slerp(relU, lookU * Quaternion.Inverse(Quaternion.LookRotation(s.ArmUpAlong[hi], s.ArmUpHinge[hi])), roll)
                : relU;
            upper.rotation = Quaternion.Slerp(upper.rotation, wantU, w);

            // The forearm, from where the upper arm put it.
            cu = fore.position - upper.position;
            cf = palm.position - fore.position;
            hc = Vector3.Cross(cu, cf);
            if (cf.sqrMagnitude < 1e-6f) return;
            Quaternion lookF = Quaternion.LookRotation(foreDir, hn);
            Quaternion relF = hc.sqrMagnitude > 1e-6f
                ? lookF * Quaternion.Inverse(Quaternion.LookRotation(cf, hc)) * fore.rotation
                : Quaternion.FromToRotation(cf, foreDir) * fore.rotation;
            Quaternion wantF = abs
                ? Quaternion.Slerp(relF, lookF * Quaternion.Inverse(Quaternion.LookRotation(s.ArmForeAlong[hi], s.ArmForeHinge[hi])), roll)
                : relF;
            fore.rotation = Quaternion.Slerp(fore.rotation, wantF, w);
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
