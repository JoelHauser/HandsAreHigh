using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Mercy
{
    // 0.16.0: where his hands are is an order of its own (Hands on head / Hands up), apart from his
    // posture (standing, kneeling: Order + Kneel). Both a player's menu and a bot captor's plan reach
    // it through Orders.Give, the same validated path.
    //
    //   HandsOnHead  what he was told (cleared when he stops complying: SetPhase, ResetEncounter)
    //   HeadHands    0..1, eased here every frame; Kneel.ApplyHands poses the arms from it, standing
    //                or kneeling, on top of the hands-up arms (ArmPose.Apply), so a change either way
    //                is a blend, never a snap.
    //
    // Only empty hands move: with a drop still under way (a weapon in his hands, items being
    // dropped on order) the target stays 0 until the game reports his hands empty, so a weapon is
    // never hidden inside the pose. Arms coming down (an escape, a draw) bring the weight down with
    // them (it is multiplied by the arms' own weight).
    //
    // Kneeling keeps the timing every descent style was tuned with: while he goes down with his
    // hands going to his head, the style's own hand window leads; while he gets up without the
    // order to keep them there, they leave by that window. A compliant rise with the order kept
    // leaves them on his head (and he doesn't brace on his knee: Kneel.PickBrace).
    internal static class HandsPose
    {
        internal static string Set(SurrenderState s, bool on, string why)
        {
            string posture = Posture(s);
            if (s.HandsOnHead == on)
                return on ? $"{s.Name} already has his hands on his head" : $"{s.Name} already has his hands up";
            s.HandsOnHead = on;
            s.HeadHandsLogged = false;
            if (on) s.HeadHelmet = s.Bot?.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem != null;
            Gestures.Cancel(s, on ? "hands to his head" : "hands back up");
            bool waits = on && !HandsFree(s);
            if (Settings.LogDecisions.Value || Settings.LogKneel.Value)
                MercyPlugin.Log.LogInfo($"{s.Name}: hands {(on ? "on his head" : "up")} ({why}; {posture}, posture unchanged){(waits ? "; waits for his hands to be empty first" : "")}");
            return on ? $"{s.Name} puts his hands on his head ({posture})" : $"{s.Name} raises his hands ({posture})";
        }

        internal static string Posture(SurrenderState s)
        {
            switch (s.Kneel)
            {
                case KneelPhase.Down: return "on his knees";
                case KneelPhase.GoingDown: return "getting down";
                case KneelPhase.GettingUp: return "getting up";
            }
            return "standing";
        }

        // Empty hands, arms up, nothing being dropped: the hands may move.
        private static bool HandsFree(SurrenderState s) =>
            s.ArmsUp && s.Progress >= 0.999f && !s.DropPending && !s.HandsPending && !s.DrawPending &&
            s.DropQueue.Count == 0 && s.ReportAt <= 0f && Arsenal.HandsEmpty(s.Bot);

        // SurrenderRegistry.Tick, after Kneel.Update, every frame.
        internal static void Update(SurrenderState s, float dt)
        {
            if (s.Dead || s.Bot == null) return;
            bool want = s.HandsOnHead && s.Phase == Phase.HandsUp && HandsFree(s);
            float secs = Mathf.Max(0.05f, Settings.HandsOnHeadSeconds.Value);
            // 0.22.0: out of the hold-up (fire, an escape, a draw) the hands leave his head promptly.
            if (s.Phase != Phase.HandsUp) secs = Mathf.Min(secs, 0.35f);
            float next = Profile(s, want ? 1f : 0f, secs, dt);
            KneelStyle st = s.KneelStyle;
            // About to kneel (turning to face, looking for room) or going down: the descent's own
            // window brings the hands up as he settles (if they weren't on his head already), the
            // timing each descent style was tuned with; they don't go up first while he stands.
            // (Eased here: the window is linear in the kneel's time; the weight is used as it is.)
            bool aboutToKneel = want && s.Kneel == KneelPhase.Standing && s.Order == Order.Kneel && s.KneelFallback == null && Settings.KneelEnabled.Value;
            if (aboutToKneel) next = s.HeadHands;
            else if (st != null && s.Kneel == KneelPhase.GoingDown && want)
                next = Mathf.Max(s.HeadHands, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(st.HandsA, st.HandsB, s.KneelT)));
            else if (st != null && s.Kneel == KneelPhase.GettingUp && !s.HandsOnHead)
                next = Mathf.Min(next, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(st.HandsA, st.HandsB, s.KneelT)));
            next = Mathf.Clamp01(next);
            // Whatever moved it this frame, its speed is carried on (a window handing over to the
            // profile, or the profile reversing, continues at the speed the hands already have).
            s.HeadHandsVel = dt > 1e-4f ? (next - s.HeadHands) / dt : 0f;
            if (next <= 0f || next >= 1f) s.HeadHandsVel = 0f;
            s.HeadHands = next;
            if (s.HeadHands >= 1f && !s.HeadHandsLogged)
            {
                s.HeadHandsLogged = true;
                if (Settings.LogKneel.Value)
                    MercyPlugin.Log.LogInfo($"{s.Name}: hands on his head ({Posture(s)}{(s.HeadHelmet || s.KneelHelmet ? ", helmet" : "")})");
            }
        }

        // 0.22.0: up to the head or back down as one eased move whose speed never jumps. Before, a
        // constant rate eased by SmoothStep: an order reversed mid-move flipped the hands' speed from
        // +v to -v in one frame (and SmoothStep's slope made it worst halfway). Now an acceleration-
        // limited profile: from rest the move takes `secs` (accelerate half, brake half); reversed
        // halfway, the hands slow, stop and come back along the same path (Kneel.ApplyHands' curve).
        private static float Profile(SurrenderState s, float target, float secs, float dt)
        {
            float x = s.HeadHands, v = s.HeadHandsVel;
            float d = target - x;
            if (Mathf.Abs(d) < 1e-4f && Mathf.Abs(v) < 0.05f) { s.HeadHandsVel = 0f; return target; }
            float acc = 4f / (secs * secs), vmax = 2f / secs;
            float vWant = Mathf.Sign(d) * Mathf.Min(vmax, Mathf.Sqrt(2f * acc * Mathf.Abs(d)));
            v = Mathf.MoveTowards(v, vWant, acc * dt);
            float nx = x + v * dt;
            if ((target - nx) * d <= 0f) { nx = target; v = 0f; }   // arrived (no overshoot)
            s.HeadHandsVel = v;
            return nx;
        }

        // The weight the arms are posed with (0 none .. 1 fully on his head). Already eased by its
        // profile (Update).
        internal static float Weight(SurrenderState s)
        {
            if (s == null || s.HeadHands <= 0f || Kneel.Off(KneelPoseIsolate.Hands)) return 0f;
            return s.HeadHands * s.Weight * Settings.HandsBehindHeadStrength.Value;
        }

        internal static bool Posing(SurrenderState s) => s != null && s.HeadHands > 0f && s.Weight > 0f;

        internal static bool Helmet(SurrenderState s) => Kneel.Active(s) ? s.KneelHelmet : s.HeadHelmet;
    }
}
