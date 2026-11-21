using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.24.0: open hands. Joel's screenshot: a man with his hands up, fingers curled as if round
    // a grip. Nothing of ours ever wrote a finger bone: the arms, wrists and palms were posed, but
    // the digits kept whatever the game's animator left on them (the empty-hands pose: a loose
    // grip), and ArmPose.SetWrist only READS the middle-finger and thumb roots to lay the palm.
    //
    // The rig (skeleton.bundle, read with UnityPy: tools/fingersim/fingersim.py): five chains of
    // three bones under each palm, Base Human{L,R}Digit{1..5}{1..3}; each bone's child sits along
    // its local -X, and a finger flexes palm-ward about the bone's local +Z (the palm side is the
    // palm bone's -Y: the thumb root sits 0.021 m that way). The right hand mirrors the left: the
    // same local rotation with x and y negated. The bind pose itself is curled (index ~72 deg over
    // its three joints, little finger ~82).
    //
    // What is set: each joint's local rotation = the bind rotation's swing (the knuckle's splay and
    // roll, so the spacing is the rig's own) times a flexion about local Z from a table:
    //   open    hands up: index 5/9/5 deg, middle 7/11/6, ring 9/14/8, little 11/17/10 (a relaxed
    //           hand, each finger a little more curved than the one before it, never straight or
    //           bent back); thumb root as bound, its two joints 4/6;
    //   cupped  hands on the head: about 15/25/15, the fingers resting round the skull;
    //   flat    the bracing hand on his thigh: 3/5/3.
    // fingersim: no joint overextended, fingertips on the palm side, neighbouring tips 3.4-4.3 cm
    // apart (the bind hand: 3.2-3.7), and the open <-> cupped blend changes every joint one way.
    //
    // Blending, so nothing snaps: the shape is a mix of the tables by HandsPose's own eased weight
    // (s.HeadHands: the same profile that moves the arms round the head, so fingers, wrists, elbows
    // and shoulders travel together, and a reversal reverses all of them) and the brace weight; the
    // result is laid over the game's fingers by the hands-up weight (s.Weight). Arms coming down
    // (an escape, a draw, a release) fade it out with them; the left hand fades out while the game's
    // middle-finger gesture has that arm.
    //
    // Whether the game's animator writes the finger bones every frame is not assumed (not verified
    // offline): the first frame we pose a hand, its fifteen local rotations are kept (Base); on the
    // next posed frame, if every bone still holds exactly what we wrote, nothing else writes them
    // (logged once per surrender, either way). Animated: each frame blends from the animator's pose
    // and nothing needs restoring. Not animated: each frame blends from Base (no creep at partial
    // weights) and Restore puts Base back when he leaves the registry (any exit: released, drawn,
    // discharged, teardown); a dead man's bones are the ragdoll's and are left alone.
    internal static class Fingers
    {
        // Left hand bind local rotations [digit 1..5][joint 1..3] (skeleton.bundle).
        private static readonly Quaternion[,] Bind =
        {
            { new Quaternion(0.58061f, 0.32734f, 0.03451f, 0.74469f), new Quaternion(-0.01186f, -0.00014f, 0.07907f, 0.99680f), new Quaternion(0.01326f, -0.01721f, 0.15739f, 0.98730f) },
            { new Quaternion(0.02152f, -0.02071f, 0.18553f, 0.98218f), new Quaternion(-0.00384f, 0.00216f, 0.24600f, 0.96926f), new Quaternion(0.00562f, -0.00776f, 0.19340f, 0.98107f) },
            { new Quaternion(-0.00747f, -0.04152f, 0.23053f, 0.97215f), new Quaternion(0.00678f, -0.03026f, 0.22232f, 0.97448f), new Quaternion(0.00736f, 0.00007f, 0.25063f, 0.96805f) },
            { new Quaternion(-0.02075f, -0.11532f, 0.22563f, 0.96714f), new Quaternion(-0.01307f, -0.02371f, 0.30982f, 0.95041f), new Quaternion(-0.00600f, -0.01130f, 0.12789f, 0.99171f) },
            { new Quaternion(-0.07905f, -0.17327f, 0.26094f, 0.94638f), new Quaternion(0.00040f, -0.04904f, 0.32071f, 0.94591f), new Quaternion(0.00743f, 0.01055f, 0.11577f, 0.99319f) },
        };

        // Flexion in degrees [digit][joint]; NaN: the bind rotation as it is (the thumb root).
        private static readonly float[,] Open =
        {
            { float.NaN, 4f, 6f }, { 5f, 9f, 5f }, { 7f, 11f, 6f }, { 9f, 14f, 8f }, { 11f, 17f, 10f },
        };
        private static readonly float[,] Cupped =
        {
            { float.NaN, 10f, 12f }, { 14f, 24f, 14f }, { 17f, 27f, 15f }, { 19f, 29f, 17f }, { 21f, 31f, 19f },
        };
        // 0.25.0: the top hand of hands-over-hands (tools/fingersim/overhand.py): lightly bent over
        // the bottom hand's back, each finger a little more than the one before.
        private static readonly float[,] OverTop =
        {
            { float.NaN, 10f, 12f }, { 15f, 10f, 6f }, { 17f, 12f, 7f }, { 19f, 14f, 8f }, { 21f, 16f, 10f },
        };
        private static readonly float[,] Flat =
        {
            { float.NaN, 3f, 4f }, { 3f, 5f, 3f }, { 3f, 5f, 3f }, { 3f, 5f, 3f }, { 3f, 5f, 3f },
        };

        // Swing part of each bind rotation (left hand), computed once.
        private static Quaternion[,] _swing;

        private static void Init()
        {
            if (_swing != null) return;
            var sw = new Quaternion[5, 3];
            for (int d = 0; d < 5; d++)
                for (int j = 0; j < 3; j++)
                {
                    Quaternion b = Bind[d, j];
                    Quaternion twist = ArmPose.TwistAbout(b, Vector3.forward);
                    sw[d, j] = b * Quaternion.Inverse(twist);
                }
            _swing = sw;
        }

        // The fifteen bones of each hand, found once per surrender (HandRig). Missing bones: null.
        internal static void Find(HandRig h, Transform leftPalm, Transform rightPalm)
        {
            for (int hi = 0; hi < 2; hi++)
            {
                Transform palm = hi == 0 ? leftPalm : rightPalm;
                string sd = hi == 0 ? "L" : "R";
                for (int d = 0; d < 5; d++)
                {
                    Transform parent = palm;
                    for (int j = 0; j < 3; j++)
                    {
                        Transform t = parent == null ? null : Kneel.FindDeep(parent, $"Base Human{sd}Digit{d + 1}{j + 1}", 1);
                        h.Digits[hi, d, j] = t;
                        parent = t;
                    }
                }
            }
        }

        // ArmPosePatch, last of the frame's arm work (after the hands-up arms, the hands on the
        // head and the knee brace): the fingers of both hands.
        internal static void Apply(Player p, SurrenderState s)
        {
            float strength = Settings.OpenHands.Value;
            if (strength <= 0f || s.Weight <= 0f) return;
            HandRig hr = ArmPose.HandsOf(p, s);
            if (hr == null) return;
            Init();
            Observe(s, hr);
            float brace = Kneel.BraceFor(s, out int braceHand);
            // 0.25.0: each hand's own progress to the head (the bottom one leads, the top one follows):
            // the fingers stay open while the hand travels and settle into their shape as it lands
            // (the last 45% of its move), and open first when it leaves.
            bool over = Kneel.OverHands(s, out int top);
            float head = Mathf.Clamp01(s.HeadHands);
            for (int hi = 0; hi < 2; hi++)
            {
                bool isTop = over && hi == top;
                float cup = over ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, Kneel.HandT(head, true, isTop))) : head;
                float[,] shape = isTop ? OverTop : Cupped;
                float w = s.Weight * strength;
                if (hi == 0) w *= 1f - s.GestureArm;   // the game's gesture has the left hand
                float flat = hi == braceHand ? brace : 0f;
                for (int d = 0; d < 5; d++)
                    for (int j = 0; j < 3; j++)
                    {
                        Transform t = hr.Digits[hi, d, j];
                        if (t == null) continue;
                        // Not animated: blend from the hand as it was before we touched it.
                        Quaternion from = hr.FingerAnim == 0 ? hr.FingerBase[hi, d, j] : t.localRotation;
                        if (w <= 0.001f)
                        {
                            if (hr.FingerAnim == 0) t.localRotation = from;
                            hr.FingerLast[hi, d, j] = t.localRotation;
                            continue;
                        }
                        Quaternion target;
                        float o = Open[d, j];
                        if (float.IsNaN(o)) target = Bind[d, j];
                        else
                        {
                            float f = Mathf.Lerp(Mathf.Lerp(o, shape[d, j], cup), Flat[d, j], flat);
                            target = _swing[d, j] * Quaternion.AngleAxis(f, Vector3.forward);
                        }
                        if (hi == 1) target = new Quaternion(-target.x, -target.y, target.z, target.w);
                        t.localRotation = w >= 0.999f ? target : Quaternion.Slerp(from, target, w);
                        hr.FingerLast[hi, d, j] = t.localRotation;
                    }
            }
            hr.FingerWritten = true;
        }

        // First posed frame: keep the hands as they are. Next one: did anything write them since?
        private static void Observe(SurrenderState s, HandRig hr)
        {
            if (!hr.FingerBaseSet)
            {
                for (int hi = 0; hi < 2; hi++)
                    for (int d = 0; d < 5; d++)
                        for (int j = 0; j < 3; j++)
                            if (hr.Digits[hi, d, j] != null) hr.FingerBase[hi, d, j] = hr.Digits[hi, d, j].localRotation;
                hr.FingerBaseSet = true;
                return;
            }
            if (hr.FingerAnim >= 0 || !hr.FingerWritten) return;
            bool changed = false;
            for (int hi = 0; hi < 2 && !changed; hi++)
                for (int d = 0; d < 5 && !changed; d++)
                    for (int j = 0; j < 3; j++)
                    {
                        Transform t = hr.Digits[hi, d, j];
                        if (t != null && Quaternion.Angle(t.localRotation, hr.FingerLast[hi, d, j]) > 0.01f) { changed = true; break; }
                    }
            hr.FingerAnim = changed ? 1 : 0;
            if (Settings.LogDecisions.Value || Settings.VerboseLogs.Value)
                HandsAreHighPlugin.Log.LogInfo(changed
                    ? $"{s.Name}: fingers: the game's animator writes them every frame (open hands laid over it; nothing to restore)"
                    : $"{s.Name}: fingers: nothing else writes them (open hands blended from his hands as they were; put back when he leaves surrender)");
        }

        // SurrenderRegistry.Release (every exit from the registry): the hands as they were, if
        // nothing else would write them. Alive only (a dead man's bones are the ragdoll's).
        internal static void Restore(SurrenderState s)
        {
            HandRig hr = s?.Hands;
            if (hr == null || !hr.FingerBaseSet || hr.FingerAnim != 0 || s.Dead || !Perception.Alive(s.Bot)) return;
            for (int hi = 0; hi < 2; hi++)
                for (int d = 0; d < 5; d++)
                    for (int j = 0; j < 3; j++)
                    {
                        Transform t = hr.Digits[hi, d, j];
                        if (t != null) t.localRotation = hr.FingerBase[hi, d, j];
                    }
        }
    }
}
