using EFT;
using UnityEngine;

namespace Mercy
{
    // 0.16.0: Turn around. One validated path for you and bot captors (Orders.Give).
    //
    //   - The direction is fixed when the order is accepted: his back towards where the issuer
    //     stood then. Nothing later (the issuer walking round him) changes it.
    //   - Standing: the root turns at Turn speed, eased in and out, with MovementContext.ApplyRotation
    //     (the call the game's own movement states use for the root), while his look runs up to Turn
    //     led by the upper body ahead of the hips. The look/root difference is what the game's body
    //     animator receives as DeltaRotation (MovementContext.UpdateAnimatorRotationParameters), so
    //     its own trunk turn (and whatever turn-in-place stepping its controller plays from it)
    //     comes from the game; it stays under the 45 deg past which the game snaps a bot's root.
    //     A turn on the spot moves no capsule, so it can't push him into anything; the room he needs
    //     in front once turned (his raised hands) is checked before he starts.
    //   - Kneeling: his knees are never spun on the ground. With Turn around from his knees on he
    //     gets up (a compliant rise), turns standing, then kneels again facing away (Kneel.Request
    //     takes TurnDir); off, the order is refused while he kneels. Not while getting up or down.
    //   - Afterwards he keeps facing that way: the layer no longer turns him towards his captor
    //     (SurrenderLogic.HandsUp asks Look first), and his head treats the captor behind him as
    //     out of view (Presence: glances over the shoulder now and then, never a turn back).
    //   - It ends with a new Turn around, a follow order, an escape, release or a new surrender.
    internal static class Facing
    {
        // deg: turned. 0.22.0: above TurnDriver.Dead (3), where the driver stops turning: at 2 a turn
        // that came to rest 2-3 deg short never finished ("couldn't finish turning (2 deg off after 8 s)").
        private const float Settled = TurnDriver.Dead + 0.5f;
        private const float TurnLimit = 8f;    // s: then he stays as he is (logged); step turns take longer than a pivot
        private const float EaseIn = 0.25f;    // s
        private const float EaseOutDeg = 35f;
        private const float MinSpeed = 20f;    // deg/s at the end of the ease

        internal static void Clear(SurrenderState s)
        {
            s.Turning = false;
            s.TurnedAway = false;
            s.TurnReKneel = false;
            s.TurnSince = -1f;
            s.TurnBy = null;
        }

        // Null when the order can be taken now; else a short reason (menu).
        internal static string Unavailable(SurrenderState s)
        {
            if (s == null || s.Bot == null) return "Gone";
            switch (s.Kneel)
            {
                case KneelPhase.GoingDown: return "Getting down";
                case KneelPhase.GettingUp: return "Getting up";
                case KneelPhase.Down: if (!Settings.TurnFromKneeling.Value) return "On his knees"; break;
            }
            if (s.KneelSpotMoving) return "Moving";
            if (s.DropQueue.Count > 0 || s.ReportAt > 0f) return "Still dropping";
            if (s.TurnReKneel) return "Turning";
            return null;
        }

        internal static string Request(SurrenderState s, Player by, out bool ok)
        {
            ok = false;
            string no = Unavailable(s);
            if (no != null)
            {
                Log(s, $"turn around refused: {no.ToLowerInvariant()}");
                return $"{s.Name} can't turn now ({no.ToLowerInvariant()})";
            }
            Vector3 away = s.Bot.Position - by.Position; away.y = 0f;
            if (away.sqrMagnitude < 0.09f) { away = by.LookDirection; away.y = 0f; }
            if (away.sqrMagnitude < 1e-4f) { Log(s, "turn around refused: no direction (on top of him)"); return $"{s.Name} can't tell which way to turn"; }
            away.Normalize();
            if (!Room(s, away, out string why))
            {
                Log(s, $"turn around refused: {why}");
                return $"{s.Name} can't turn round here ({why})";
            }
            ok = true;
            s.TurnDir = away;
            s.TurnBy = by;
            if (s.Order == Order.Follow) s.Order = Order.None;
            Gestures.Cancel(s, "turning round");
            Vector3 f = s.Bot.Transform.Original.forward; f.y = 0f;
            float off = f.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(f, away, Vector3.up) : 0f;
            if (s.Kneel == KneelPhase.Down)
            {
                // Up first (compliant rise; his hands stay where they are told), then the turn, then
                // down again the new way (Update, once his legs are his own).
                s.TurnReKneel = true;
                s.Turning = false;
                s.TurnedAway = false;
                s.Order = Order.None;
                Log(s, $"turn around from his knees: gets up, turns {Mathf.Abs(off):0} deg, kneels again with his back to {Restraint.Name(by)} (direction fixed now)");
                return $"{s.Name} gets up to turn round";
            }
            s.Turning = true;
            s.TurnedAway = false;
            s.TurnSince = Time.time;
            Log(s, $"turns his back to {Restraint.Name(by)}: {Mathf.Abs(off):0} deg to the {(off >= 0f ? "right" : "left")} (direction fixed now, {Vector3.Distance(by.Position, s.Bot.Position):0.0} m away)");
            return $"{s.Name} turns around";
        }

        // Raised hands need ~0.4 m in front of his chest once he faces the new way.
        private static bool Room(SurrenderState s, Vector3 dir, out string why)
        {
            why = null;
            Vector3 chest = Perception.Chest(s.Bot) + Vector3.up * 0.25f;
            if (Physics.SphereCast(chest, 0.12f, dir, out RaycastHit hit, 0.32f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
            {
                why = $"a wall {hit.distance + 0.12f:0.00} m in front once turned";
                return false;
            }
            return true;
        }

        // SurrenderRegistry.Tick, every frame: what his body should face, turned by TurnDriver.
        //   Turn around: TurnDir (fixed at the order) until settled, then held there.
        //   About to kneel: Kneel's own fixed direction (Kneel.PreTurn).
        //   Otherwise, standing still with his hands up: towards the captor he faces (FaceTarget).
        // Never while he moves, kneels, rises, acts for himself or is asleep: the game's limit back.
        internal static void Update(SurrenderState s, float dt)
        {
            if (s.Dead || s.Bot == null || s.Suspended || s.Phase != Phase.HandsUp) { TurnDriver.Stop(s); return; }
            if (s.TurnReKneel && !s.Turning && !s.TurnedAway)
            {
                // Waiting for his rise to finish (the kneel has his legs until then).
                if (Kneel.Active(s) || s.KneelFade > 0f) { TurnDriver.Stop(s); return; }
                s.Turning = true;
                s.TurnSince = Time.time;
            }
            if (Kneel.Active(s) || s.KneelFade > 0f || s.Moving || s.Bot.IsSprintEnabled || Kneel.SpotWalking(s)) { TurnDriver.Stop(s); return; }

            Vector3 want; string what; float start = 3f;
            if (s.Turning || s.TurnedAway) { want = s.TurnDir; what = "turning his back"; }
            else if (Kneel.PreTurn(s, out Vector3 kd)) { want = kd; what = "turning to kneel"; }
            else if (s.FaceSet && Settings.FaceCaptor.Value && s.Order != Order.Kneel) { want = s.FaceTarget - s.Bot.Position; what = "facing his captor"; start = 15f; }
            else { TurnDriver.Stop(s); return; }

            float off = TurnDriver.Drive(s, want, dt, what, start);
            if (!s.Turning) return;
            float now = Time.time;
            if (Mathf.Abs(off) <= Settled || now - s.TurnSince > TurnLimit)
                Finish(s, Mathf.Abs(off) <= Settled ? null : $"couldn't finish turning ({Mathf.Abs(off):0} deg off after {TurnLimit:0} s)");
        }

        private static void Finish(SurrenderState s, string short_)
        {
            s.Turning = false;
            s.TurnedAway = true;
            if (short_ != null)
            {
                Vector3 f = s.Bot.Transform.Original.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-4f) s.TurnDir = f.normalized;   // he stays as he is
            }
            Log(s, short_ != null
                ? $"turn around: {short_}; stays facing as he is"
                : $"turned round in {Time.time - s.TurnSince:0.0} s, his back to {Restraint.Name(s.TurnBy)}; keeps facing away");
            if (s.TurnReKneel)
            {
                s.TurnReKneel = false;
                s.Order = Order.Kneel;
                string note = Kneel.Request(s, s.TurnBy, out bool ok);
                Log(s, $"turn around: kneels again facing away ({note})");
                if (!ok && s.OrderByHuman) OrderMenu.Notify(note, NoteKind.Error);
            }
        }

        private static float Off(SurrenderState s)
        {
            Vector3 f = s.Bot.Transform.Original.forward; f.y = 0f;
            return f.sqrMagnitude < 1e-4f ? 0f : Vector3.SignedAngle(f, s.TurnDir, Vector3.up);
        }

        // The layer, each update while he stands with his hands up: true when the facing is this
        // order's (the layer then doesn't turn him towards his captor). While he turns, the look
        // leads the hips by up to Turn led by the upper body; once turned, it stays along TurnDir.
        internal static bool Look(SurrenderState s, BotOwner o)
        {
            if (s.TurnReKneel && !s.Turning) return true;    // getting up: the kneel keeps his look on his knees
            if (Kneel.Active(s)) return s.Turning || s.TurnedAway;   // kneeling the new way: the kneel owns it
            if (TurnDriver.Look(s, out Vector3 look)) { o.Steering.LookToDirection(look, 360f); return true; }
            if (s.TurnedAway) { o.Steering.LookToDirection(s.TurnDir, 360f); return true; }
            return false;
        }

        // Standing turned away: the way his body faces (Presence and InView measure from it).
        internal static bool Away(SurrenderState s) => s != null && (s.TurnedAway || s.Turning);

        private static void Log(SurrenderState s, string msg)
        {
            if (Settings.LogDecisions.Value) MercyPlugin.Log.LogInfo($"{s.Name}: {msg}");
        }
    }
}
