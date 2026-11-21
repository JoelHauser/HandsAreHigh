using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    internal enum KneelVariantKind { Upright, Settled, Tense }

    // 0.25.0: men kneel in different ways (Joel's 5th point). Three variants of the same kneel:
    //
    //   Upright   the 0.24 kneel: thighs ~8 deg forward of vertical, back straight;
    //   Settled   sat back over his heels: thighs at "Settled thigh angle" (46 deg), the hips back
    //             and down (about a hand above the heels), a little more forward lean to balance;
    //   Tense     forward on his knees: thighs ~2 deg, hips a little ahead, leaning forward.
    //
    // Only the hips and the trunk differ. The knees and feet are where every variant puts them (the
    // base kneel's Layout), so the ground and room checks for the knees and shins hold for all of
    // them, nothing slides on the ground when he changes variant, and the feet keep the frame the
    // 0.23 twisted-foot fix gave them. The hips are placed so the thigh reaches the same knee point:
    //   hips forward = knee forward - thigh * sin(angle), hips height = knee + thigh * cos(angle).
    // Offline (tools/kneelsim/variants.py, the rig's 0.456/0.460 m thigh and shin, flat and on the
    // allowed knee-to-foot steps): knee flexion upright ~109, tense ~103, settled ~147 (140-153) deg
    // (deep kneeling, under the ~155-160 a knee allows); the settled hips stay above the heels.
    //
    // Chosen once when he gets down (Choose, in TryBegin after the base kneel fits), from his
    // demeanor, nerve, hurt legs and whether he is weighing an opening, with one roll kept for the
    // whole surrender (so the same man kneels the same way again after a turn-around). Settled needs
    // knees and feet on level ground and room for his back and pack further back; otherwise upright.
    // The descent ends upright and he then settles (or leans in) over BlendSettle/BlendTense: people
    // kneel first, then sit back. Held: a re-check every 2 s only acts on a real change of mood and at
    // most once per "Keeps a variant at least" (no back and forth). Before a rise a settled man shifts
    // his weight forward first (RiseReady), faster when it is urgent; the rise starts from that pose
    // (the hips' rise is a lerp from wherever the variant has them: no snap to a standard kneel).
    // Per frame: one smoothstep of two floats. Physics queries only at the choice.
    internal static partial class Kneel
    {
        private const float BlendSettle = 1.4f, BlendTense = 0.7f, BlendChange = 1.6f;
        private const float SettledLean = 5f, RiseReadyThigh = -4f, RiseReadyLean = 12f;

        // Tilt with the variant on top: the hips' placement (ApplyBody). Layout keeps Tilt (the base).
        private static float TiltBody(SurrenderState s) => Tilt(s) + s.KVarThigh * Mathf.Deg2Rad;

        // The variant's offsets on this man's base kneel (degrees of thigh angle, degrees of lean).
        private static void VariantOffsets(SurrenderState s, KneelVariantKind k, out float thigh, out float lean)
        {
            float baseDeg = Settings.KneelThighAngle.Value + s.KneelThighOff;
            switch (k)
            {
                case KneelVariantKind.Settled:
                    thigh = Mathf.Max(0f, Settings.KneelSettledThigh.Value - baseDeg);
                    lean = SettledLean;
                    return;
                case KneelVariantKind.Tense:
                    thigh = -Mathf.Min(6f, Mathf.Max(0f, baseDeg - 2f));
                    lean = Settings.KneelTenseLean.Value;
                    return;
            }
            thigh = 0f; lean = 0f;
        }

        private static void BlendTo(SurrenderState s, float thigh, float lean, float secs)
        {
            s.KVarFromThigh = s.KVarThigh;
            s.KVarFromLean = s.KVarLean;
            s.KVarToThigh = thigh;
            s.KVarToLean = lean;
            s.KVarBlendT = 0f;
            s.KVarBlendSecs = Mathf.Max(0.05f, secs);
        }

        // Every frame from Update (any phase): the eased move to the current target.
        private static void VariantTick(SurrenderState s, float dt)
        {
            if (s.KVarBlendT >= 1f) return;
            s.KVarBlendT = Mathf.Min(1f, s.KVarBlendT + dt / s.KVarBlendSecs);
            float e = Mathf.SmoothStep(0f, 1f, s.KVarBlendT);
            s.KVarThigh = Mathf.Lerp(s.KVarFromThigh, s.KVarToThigh, e);
            s.KVarLean = Mathf.Lerp(s.KVarFromLean, s.KVarToLean, e);
        }

        // TryBegin, once the base kneel fits here: which variant, checked against where he is. The
        // descent itself is upright (offsets 0); the variant comes in once he is down (VariantDown).
        private static void ChooseVariant(SurrenderState s, Vector3 o, Vector3 f, Vector3 r)
        {
            s.KVarThigh = s.KVarLean = 0f;
            s.KVarBlendT = 1f;
            s.KVarShifting = false;
            if (s.KVarRoll < 0f) s.KVarRoll = Dice.Value;
            string why = "variants off";
            KneelVariantKind want = Settings.KneelVariants.Value ? Wanted(s, out why) : KneelVariantKind.Upright;
            if (want == KneelVariantKind.Settled && !SettledFits(s, o, f, r, out string no))
            {
                why += $"; can't sit back here ({no}): upright";
                want = KneelVariantKind.Upright;
            }
            s.KVarTarget = want;
            s.KVarChosenAt = Time.time;
            s.KVarCheckAt = Time.time + 2f;
            s.KVarWhy = why;
            if (Settings.LogKneel.Value) Log(s, $"kneel: will kneel {Describe(want)} ({why})");
        }

        internal static string Describe(KneelVariantKind k) =>
            k == KneelVariantKind.Settled ? "settled back on his heels" : k == KneelVariantKind.Tense ? "forward and tense" : "upright";

        // Weights from what he is like now; one roll per surrender (KVarRoll), so only a change in
        // these weights can change the pick.
        private static KneelVariantKind Wanted(SurrenderState s, out string why)
        {
            float wU = 1f, wS = Settings.KneelSettledWeight.Value, wT = Settings.KneelTenseWeight.Value;
            DemeanorKind d = s.Look != null ? s.Look.Demeanor : DemeanorKind.Cautious;
            switch (d)
            {
                case DemeanorKind.Resigned: wS *= 1.8f; wT *= 0.4f; break;
                case DemeanorKind.Frightened: wS *= 0.7f; wT *= 1.2f; break;
                case DemeanorKind.Cautious: wS *= 0.8f; wT *= 1.3f; break;
                case DemeanorKind.Defiant: wS *= 0.3f; wT *= 1.8f; break;
            }
            wS *= Mathf.Lerp(1.4f, 0.7f, s.Nerve);
            wT *= Mathf.Lerp(0.6f, 1.4f, s.Nerve);
            float legs = Mathf.Max(s.KneelLegHurt[0], s.KneelLegHurt[1]);
            wS *= 1f + 0.6f * legs;              // weight off the knees
            bool weighing = s.OpenSince >= 0f;   // he is watching an opening: ready to move
            if (weighing) { wS *= 0.5f; wT *= 1.5f; }
            float total = wU + wS + wT;
            float pick = s.KVarRoll * total;
            KneelVariantKind k = pick < wS ? KneelVariantKind.Settled : pick < wS + wT ? KneelVariantKind.Tense : KneelVariantKind.Upright;
            why = $"{d}, nerve {s.Nerve:0.00}{(legs > 0.1f ? $", legs hurt {legs:0.00}" : "")}{(weighing ? ", weighing an opening" : "")}; " +
                  $"settled {wS:0.00} / tense {wT:0.00} / upright 1.00, roll {pick:0.00} of {total:0.00}";
            return k;
        }

        // Sitting back moves his trunk and pack back and down: the ground under knees and feet must
        // be level enough to sit on his heels, and his back and pack need room further back.
        private static bool SettledFits(SurrenderState s, Vector3 o, Vector3 f, Vector3 r, out string why)
        {
            for (int i = 0; i < 2; i++)
            {
                float dk = s.KneelGround[i], df = s.KneelGround[i + 2];
                if (Mathf.Abs(dk - df) > 0.05f) { why = $"his {(i == 0 ? "left" : "right")} foot is {df - dk:+0.00;-0.00} m off his knee's level"; return false; }
            }
            KneelRig rig = s.Rig;
            float thigh = 0.5f * (rig.Legs[0].Thigh + rig.Legs[1].Thigh);
            VariantOffsets(s, KneelVariantKind.Settled, out float dThigh, out _);
            float baseT = Tilt(s), t = baseT + dThigh * Mathf.Deg2Rad;
            float back = thigh * (Mathf.Sin(t) - Mathf.Sin(baseT));
            float hipY = Settings.KneeHeight.Value + thigh * Mathf.Cos(t);
            float headY = hipY + rig.TorsoLength;
            Vector3 c = o - f * back;
            Kit(s.Bot, out bool pack, out bool vest, out bool helmet);
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            float body = 0.16f + (vest ? 0.03f : 0f);
            float top = headY + 0.12f + (helmet ? 0.04f : 0f);
            if (Blocked(Physics.OverlapCapsuleNonAlloc(c + Vector3.up * (hipY + 0.12f), c + Vector3.up * top, body, Hits, mask, QueryTriggerInteraction.Ignore), out string torso))
            { why = $"{torso} behind him"; return false; }
            if (pack && Blocked(Physics.OverlapCapsuleNonAlloc(c - f * 0.26f + Vector3.up * (hipY + 0.12f), c - f * 0.26f + Vector3.up * (headY - 0.2f), 0.13f, Hits, mask, QueryTriggerInteraction.Ignore), out string bk))
            { why = $"{bk} where his backpack would go"; return false; }
            why = null;
            return true;
        }

        // GoingDown -> Down: into the chosen variant.
        private static void VariantDown(SurrenderState s)
        {
            VariantOffsets(s, s.KVarTarget, out float thigh, out float lean);
            s.KVarShifting = false;
            BlendTo(s, thigh, lean, s.KVarTarget == KneelVariantKind.Settled ? BlendSettle : BlendTense);
        }

        // Down, every 2 s: a real change of mood may move him to another variant, once per hold time.
        private static void VariantCheck(SurrenderState s, float now)
        {
            if (!Settings.KneelVariants.Value || now < s.KVarCheckAt) return;
            s.KVarCheckAt = now + 2f;
            if (now - s.KVarChosenAt < Settings.KneelVariantHold.Value || s.KVarBlendT < 1f) return;
            if (StillTurning(s, now, out _)) return;
            KneelVariantKind want = Wanted(s, out string why);
            if (want == s.KVarTarget) return;
            if (want == KneelVariantKind.Settled)
            {
                if (!SettledFits(s, s.KneelAnchor, s.KneelAnchorFwd, Vector3.Cross(Vector3.up, s.KneelAnchorFwd), out string no))
                {
                    s.KVarChosenAt = now;   // not again for a while
                    if (Settings.VerboseLogs.Value) Log(s, $"kneel: would settle back, but {no}");
                    return;
                }
            }
            KneelVariantKind was = s.KVarTarget;
            s.KVarTarget = want;
            s.KVarChosenAt = now;
            s.KVarWhy = why;
            VariantOffsets(s, want, out float thigh, out float lean);
            BlendTo(s, thigh, lean, BlendChange);
            if (Settings.LogKneel.Value) Log(s, $"kneel: {Describe(was)} -> {Describe(want)} ({why})");
        }

        // A rise from his knees: a settled man brings his weight forward over his knees first.
        // Returns how long the rise's clock waits for it (it overlaps the last part).
        private static float VariantRise(SurrenderState s, KneelRise rise)
        {
            if (s.KVarThigh <= 8f && s.KVarToThigh <= 8f) return 0f;
            float secs = rise == KneelRise.Escape ? 0.3f : rise == KneelRise.Defensive ? 0.4f : 0.6f;   // tools/kneelsim/variants.py: hips <= 30 mm per frame
            VariantOffsets(s, KneelVariantKind.Tense, out float tThigh, out _);
            BlendTo(s, Mathf.Max(tThigh, RiseReadyThigh), RiseReadyLean, secs);
            s.KVarShifting = true;
            return secs * 0.65f;
        }
    }
}
