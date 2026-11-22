using EFT;
using EFT.AnimatedInteractionsSubsystem;
using HarmonyLib;
using UnityEngine;

namespace Mercy
{
    // Body gestures while held. Only one owns the body at a time.
    //   Wiggle:  the game's lean, left-right, hands up.
    //   Defiant: the game's middle finger, EInteraction.GetOffGesture. The game itself treats
    //            it as that (BotGesture.ApplyRequest: GESTUS_FUCK_TO_SHOOT, EBotEnemyCause
    //            .fuckGestus). Its third-person clip, tp_gesture_04_getoff (player_anim_controller
    //            bundle), moves the LEFT arm and every left finger (palm 86 deg, fingers 60-125
    //            deg); the right fingers don't move and the right arm only ~20 deg. So the left
    //            arm is handed to the game's animation for the gesture and our pose keeps the
    //            right hand up (ArmPose runs after the animator and IK every frame).
    internal enum GestureKind { None, Wiggle, Defiant }

    // The lean, verified in Assembly-CSharp (0.16.9):
    //   MovementContext.SetTilt(t) only stores _tilt (clamped to +-5) and raises OnTiltChanged.
    //   MovementContext.ManualUpdate -> SmoothTilt(dt) eases SmoothedTilt towards _tilt (the
    //   game's own inertia) and SmoothedTilt drives the animator's tilt parameter. Nothing in
    //   it moves the body, the capsule or the NavMesh position.
    // SAIN's LeanClass calls SetTilt every tick for any bot with an enemy, layer active or
    // not, so while a bot holds his hands up this prefix replaces _tilt with ours right before
    // the game reads it. MovementContext.ManualUpdate returns early for the dead, so this
    // never runs on a corpse.
    [HarmonyPatch(typeof(MovementContext), nameof(MovementContext.SmoothTilt))]
    internal static class TiltPatch
    {
        private static void Prefix(MovementContext __instance, Player ____player)
        {
            if (!SurrenderRegistry.Any || ____player == null) return;
            long t0 = Perf.Begin();
            if (SurrenderRegistry.TryGet(____player, out SurrenderState s) && Gestures.OwnsTilt(s))
                __instance._tilt = Gestures.DesiredTilt(s);
            Perf.End(Perf.Sec.Tilt, t0);
        }
    }

    internal static class Gestures
    {
        private const float MaxTilt = 5f;           // MovementContext clamps to +-5
        private const float FacingTolerance = 35f;  // degrees between his look and the captor
        private const float CheckEvery = 0.2f;      // start conditions (with sphere casts)
        private const float StartTimeout = 0.6f;    // the game must have started the gesture by then
        private const float DefiantMaxTime = 3f;    // clip is 1.23 s; safety cap
        private static readonly float[] ClearHeights = { 1.35f, 1.65f };

        internal static bool OwnsTilt(SurrenderState s) =>
            Settings.Enabled.Value && s.Phase == Phase.HandsUp && !s.Suspended && !s.Dead && s.Bot != null;

        internal static float DesiredTilt(SurrenderState s)
        {
            if (s.Gesture != GestureKind.Wiggle) return 0f;
            float t = Time.time - s.GestureStart;
            int swing = Mathf.FloorToInt(t / Mathf.Max(0.05f, Settings.WiggleSwingSeconds.Value));
            if (swing < 0 || swing >= Settings.WiggleSwings.Value) return 0f;
            float side = swing % 2 == 0 ? s.GestureSign : -s.GestureSign;
            return side * Mathf.Clamp01(Settings.WiggleStrength.Value) * MaxTilt;
        }

        private static float WiggleDuration => Settings.WiggleSwings.Value * Settings.WiggleSwingSeconds.Value;

        private static void Log(SurrenderState s, string msg)
        {
            if (Settings.LogGestures.Value) MercyPlugin.Log.LogInfo($"{s.Name}: {msg}");
        }

        // ---- wiggle sampling --------------------------------------------------------------

        // A moment where a wiggle would make sense. One roll per opportunity, at most one roll
        // per WiggleSampleGap, and a chosen wiggle starts the per-bot cooldown (kept in the
        // raid history, so recapturing him doesn't reset it). Frequent calls can't add up.
        internal static void Opportunity(SurrenderState s, string when)
        {
            if (!Settings.Wiggle.Value || s.Bot == null || s.Dead || s.Phase != Phase.HandsUp) return;
            float now = Time.time;
            if (s.Gesture != GestureKind.None || s.WigglePendingUntil > now) return;
            if (now < s.NextWiggleSample) return;
            s.NextWiggleSample = now + Settings.WiggleSampleGap.Value;

            BotHistory h = SurrenderRegistry.HistoryOf(s.Bot);
            if (now < h.WiggleCooldownUntil)
            {
                if (Settings.VerboseLogs.Value) Log(s, $"wiggle opportunity ({when}) skipped: cooldown {h.WiggleCooldownUntil - now:0} s");
                return;
            }

            float pers = WigglePersonality(s.Personality);
            float nerve = 1.3f - 0.6f * s.Nerve;
            float chance = Mathf.Min(Settings.WiggleMaxChance.Value, Settings.WiggleChance.Value * pers * nerve);
            float roll = Dice.Value;
            bool chosen = roll < chance;
            if (Settings.LogGestures.Value)
                Log(s, $"wiggle opportunity ({when}): base {Settings.WiggleChance.Value:0.00} x personality {pers:0.0} x nerve {nerve:0.00} " +
                       $"= {chance:0.00} (max {Settings.WiggleMaxChance.Value:0.00}), roll {roll:0.00} -> {(chosen ? "will wiggle" : "no")}");
            if (!chosen) return;
            h.WiggleCooldownUntil = now + Settings.WiggleCooldown.Value;
            s.WigglePendingUntil = now + Settings.WiggleWindow.Value;
        }

        // Appeasing personalities wiggle more, bold ones less.
        private static float WigglePersonality(string p)
        {
            switch (p)
            {
                case "Coward": return 1.6f;
                case "Timmy": return 1.5f;
                case "Rat": return 1.2f;
                case "Chad": return 0.7f;
                case "SnappingTurtle": return 0.6f;
                case "GigaChad": return 0.4f;
                case "Wreckless": return 0.3f;
                default: return 1f;
            }
        }

        // ---- middle finger sampling -------------------------------------------------------

        // Bold personalities more, timid ones less.
        private static float DefiantPersonality(string p)
        {
            switch (p)
            {
                case "Wreckless": return 2f;
                case "GigaChad": return 1.8f;
                case "Chad": return 1.5f;
                case "SnappingTurtle": return 1.3f;
                case "Rat": return 0.6f;
                case "Timmy": return 0.4f;
                case "Coward": return 0.3f;
                default: return 1f;
            }
        }

        // Exactly one roll per surrender, at a random moment after he settles. A failed roll
        // means no attempt this encounter; a chosen one starts the per-bot cooldown (kept
        // across recaptures) at once, whether or not a moment comes up to do it.
        //   chance = min(max, base x (1 + influence x (personality x (0.5 + nerve) - 1)))
        private static void RollDefiant(SurrenderState s)
        {
            if (!Settings.Defiant.Value) return;
            float now = Time.time;
            BotHistory h = SurrenderRegistry.HistoryOf(s.Bot);
            if (now < h.DefiantCooldownUntil)
            {
                if (Settings.VerboseLogs.Value) Log(s, $"middle finger: cooldown {h.DefiantCooldownUntil - now:0} s, no roll");
                return;
            }
            float bias = DefiantPersonality(s.Personality) * (0.5f + s.Nerve);
            float factor = 1f + Settings.DefiantInfluence.Value * (bias - 1f);
            float chance = Mathf.Clamp01(Mathf.Min(Settings.DefiantMaxChance.Value, Settings.DefiantChance.Value * factor));
            float roll = Dice.Value;
            bool chosen = roll < chance;
            if (Settings.LogGestures.Value)
                Log(s, $"middle finger roll: base {Settings.DefiantChance.Value:0.00} x {factor:0.00} (personality/nerve {bias:0.00}) = {chance:0.000} " +
                       $"(max {Settings.DefiantMaxChance.Value:0.00}), roll {roll:0.000} -> {(chosen ? "will" : "no")}");
            if (!chosen) return;
            h.DefiantCooldownUntil = now + Settings.DefiantCooldown.Value;
            s.DefiantPendingUntil = now + Settings.DefiantWindow.Value;
        }

        // ---- per frame --------------------------------------------------------------------

        // Every frame for every held bot (SurrenderRegistry.Tick). Cheap unless a gesture is
        // pending (start checks at most 5x a second) or running.
        internal static void Update(SurrenderState s)
        {
            if (s.Bot == null || s.Dead) return;
            float now = Time.time;

            // The left arm's hand-over to the game's gesture eases in and out.
            float armTarget = s.Gesture == GestureKind.Defiant ? 1f : 0f;
            if (s.GestureArm != armTarget)
                s.GestureArm = Mathf.MoveTowards(s.GestureArm, armTarget, Time.deltaTime / Mathf.Max(0.01f, Settings.GestureBlend.Value));

            if (s.Phase != Phase.HandsUp) return;

            // Moments for the two rolls, measured from when his arms are fully up.
            if (s.ArmsUp && s.Progress >= 1f && !s.DropPending)
            {
                if (!s.SettledSampled)
                {
                    if (s.SettledAt < 0f) s.SettledAt = now + Settings.Between(Settings.WiggleDelayMin, Settings.WiggleDelayMax);
                    else if (now >= s.SettledAt) { s.SettledSampled = true; Opportunity(s, "settled with his hands up"); }
                }
                if (!s.DefiantSampled)
                {
                    if (s.DefiantAt < 0f) s.DefiantAt = now + Settings.Between(Settings.DefiantDelayMin, Settings.DefiantDelayMax);
                    else if (now >= s.DefiantAt) { s.DefiantSampled = true; RollDefiant(s); }
                }
            }

            switch (s.Gesture)
            {
                case GestureKind.Wiggle: UpdateWiggle(s, now); return;
                case GestureKind.Defiant: UpdateDefiant(s, now); return;
            }

            // Nothing running: a pending gesture starts when the moment is right. Never both.
            if (s.DefiantPendingUntil >= 0f) TryStartDefiant(s, now);
            else if (s.WigglePendingUntil >= 0f) TryStartWiggle(s, now);
        }

        private static void UpdateWiggle(SurrenderState s, float now)
        {
            if (!StillValid(s, out string why)) { Cancel(s, why); return; }
            if (now - s.GestureStart < WiggleDuration) return;
            s.Gesture = GestureKind.None;
            Log(s, "wiggle done, back upright");
        }

        private static void UpdateDefiant(SurrenderState s, float now)
        {
            if (!StillValid(s, out string why)) { Cancel(s, why); return; }
            IAnimatedInteractions ai = Interactions(s.Bot);
            bool playing = ai != null && ai.IsInteractionPlaying;
            float t = now - s.GestureStart;
            if (!s.GestureConfirmed)
            {
                if (playing) { s.GestureConfirmed = true; return; }
                if (t < StartTimeout) return;
                s.Gesture = GestureKind.None;
                Log(s, "middle finger: the game didn't start the gesture (animator busy?), skipped");
                return;
            }
            if (playing && t < DefiantMaxTime) return;
            s.Gesture = GestureKind.None;
            Log(s, $"middle finger done ({t:0.0} s), left hand back up");
        }

        private static void TryStartWiggle(SurrenderState s, float now)
        {
            if (now > s.WigglePendingUntil)
            {
                s.WigglePendingUntil = -1f;
                Log(s, $"wiggle skipped: no suitable moment within {Settings.WiggleWindow.Value:0} s ({s.GestureBlock})");
                return;
            }
            if (now < s.GestureCheckAt) return;
            s.GestureCheckAt = now + CheckEvery;
            if (!CanStart(s, needsRoom: true, out s.GestureBlock)) return;

            s.WigglePendingUntil = -1f;
            s.Gesture = GestureKind.Wiggle;
            s.GestureStart = now;
            s.GestureSign = Dice.Value < 0.5f ? -1f : 1f;
            Log(s, $"wiggle starts ({Settings.WiggleSwings.Value} x {Settings.WiggleSwingSeconds.Value:0.00} s, strength {Settings.WiggleStrength.Value:0.00})");
        }

        private static void TryStartDefiant(SurrenderState s, float now)
        {
            if (now > s.DefiantPendingUntil)
            {
                s.DefiantPendingUntil = -1f;
                Log(s, $"middle finger skipped: no suitable moment within {Settings.DefiantWindow.Value:0} s ({s.GestureBlock})");
                return;
            }
            if (now < s.GestureCheckAt) return;
            s.GestureCheckAt = now + CheckEvery;
            if (!CanStart(s, needsRoom: false, out s.GestureBlock)) return;

            Player bot = s.Bot;
            // The game's own preconditions in MovementContext.SetInteractInHands, checked first so
            // a refusal is a logged skip, not a silent no-op: hands animator idle, not sprinting,
            // and the full skeleton (on a simplified one the game plays a door push instead).
            if (bot.UsedSimplifiedSkeleton) { s.GestureBlock = "simplified skeleton (far away)"; return; }
            var fa = bot.HandsController?.FirearmsAnimator;
            if (fa == null || !fa.IsIdling()) { s.GestureBlock = "hands animator busy"; return; }
            IAnimatedInteractions ai = Interactions(bot);
            if (ai == null) { s.GestureBlock = "no interaction animator"; return; }
            if (ai.IsInteractionPlaying || !ai.CanInteract) { s.GestureBlock = "another interaction playing"; return; }
            if (!s.LastCovered) { s.GestureBlock = "you're not watching him"; return; }

            s.DefiantPendingUntil = -1f;
            s.Gesture = GestureKind.Defiant;
            s.GestureStart = now;
            s.GestureConfirmed = false;
            // The bot path (BotGesture.TryGestus): body animator only. Unlike
            // Player.ShowGesture / HandsController.ShowGesture it raises no
            // GlobalEventDispatcher.ShowGesture, so no bot "receives" it and nobody turns
            // hostile over it (LocalPlayer.OnAnimatedInteraction is empty). No item, no
            // hands change: RemoveLeftHandItem has nothing to remove with empty hands.
            bot.MovementContext.SetInteractInHands(EInteraction.GetOffGesture);
            Log(s, "middle finger starts (left hand; right hand stays up)");
        }

        private static IAnimatedInteractions Interactions(Player bot) =>
            bot?.MovementContext?.PlayerAnimator?.AnimatedInteractions;

        // Standing still, arms fully up, hands empty and settled, nothing being dropped,
        // facing the captor who can see him; for a wiggle also upright with room to lean.
        private static bool CanStart(SurrenderState s, bool needsRoom, out string why)
        {
            Player bot = s.Bot;
            if (!StillValid(s, out why)) return false;
            if (needsRoom && bot.PoseLevel < 0.85f) { why = "not standing"; return false; }
            if (s.DropPending || s.HandsPending) { why = "hands still busy"; return false; }
            if (s.Order == Order.Follow) { why = "following you"; return false; }
            Player captor = s.Captor;
            if (!Perception.Alive(captor)) { why = "no captor"; return false; }
            Vector3 look = bot.LookDirection; look.y = 0f;
            Vector3 to = captor.Position - bot.Position; to.y = 0f;
            if (look.sqrMagnitude < 1e-4f || Vector3.Angle(look, to) > FacingTolerance) { why = "not facing you yet"; return false; }
            if (!Perception.LineClear(Perception.Eye(bot), Perception.Eye(captor))) { why = "you can't see him"; return false; }
            if (needsRoom && !Clear(bot, out why)) return false;
            why = null;
            return true;
        }

        // What must hold for the whole gesture; anything else interrupts it.
        private static bool StillValid(SurrenderState s, out string why)
        {
            Player bot = s.Bot;
            why = null;
            if (s.Dead || !Perception.Alive(bot)) { why = "dead"; return false; }
            if (s.Phase != Phase.HandsUp) { why = $"now {s.Phase}"; return false; }
            if (Presence.Neutral) { why = "head neutral test"; return false; }
            if (s.Suspended) { why = "asleep"; return false; }
            if (s.Kneel != KneelPhase.Standing) { why = "kneeling"; return false; }   // the kneel owns body and arms
            if (s.HandsOnHead || s.HeadHands > 0f) { why = "hands on his head"; return false; }   // 0.16.0: the hands-up gestures need hands up
            if (s.Turning || s.TurnedAway) { why = "turned away"; return false; }
            if (s.WasHit) { why = "hit"; return false; }
            if (!s.ArmsUp || s.Progress < 1f) { why = "arms not fully up"; return false; }
            if (s.Moving) { why = "moving"; return false; }
            if (s.DropQueue.Count > 0 || s.ReportAt > 0f) { why = "dropping items on order"; return false; }
            if (bot.IsInPronePose) { why = "prone"; return false; }
            if (bot.IsSprintEnabled) { why = "sprinting"; return false; }
            if (s.Gesture == GestureKind.Wiggle && (!Settings.Wiggle.Value || bot.PoseLevel < 0.85f)) { why = "wiggle off or not standing"; return false; }
            if (s.Gesture == GestureKind.Defiant && !Settings.Defiant.Value) { why = "turned off"; return false; }
            return true;
        }

        // A lean tips the head and shoulders about half a metre to the side; a wall there
        // would put them through it. Sphere casts both ways at shoulder and head height.
        private static bool Clear(Player bot, out string why)
        {
            Vector3 right = bot.Transform.Original.right; right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) { why = "no facing"; return false; }
            right.Normalize();
            float dist = Settings.WiggleClearance.Value * Mathf.Clamp01(Settings.WiggleStrength.Value) + 0.15f;
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            for (int i = 0; i < ClearHeights.Length; i++)
            {
                Vector3 from = bot.Position + Vector3.up * ClearHeights[i];
                if (Physics.SphereCast(from, 0.12f, right, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
                { why = $"wall {hit.distance:0.00} m to his right"; return false; }
                if (Physics.SphereCast(from, 0.12f, -right, out hit, dist, mask, QueryTriggerInteraction.Ignore))
                { why = $"wall {hit.distance:0.00} m to his left"; return false; }
            }
            why = null;
            return true;
        }

        // Ends any gesture now. The lean eases back upright (SetTilt without force: the game's
        // own smoothing); a middle finger is stopped with the game's own reset
        // (AnimatedInteractions.ForceStopInteractions) and the left arm blends back. Nothing is
        // written to a dead or destroyed bot.
        internal static void Cancel(SurrenderState s, string why)
        {
            GestureKind had = s.Gesture;
            bool pending = s.WigglePendingUntil >= 0f || s.DefiantPendingUntil >= 0f;
            s.Gesture = GestureKind.None;
            s.WigglePendingUntil = -1f;
            s.DefiantPendingUntil = -1f;
            if (had == GestureKind.None)
            {
                if (pending && Settings.VerboseLogs.Value) Log(s, $"pending gesture dropped ({why})");
                return;
            }
            Log(s, $"{(had == GestureKind.Wiggle ? "wiggle" : "middle finger")} interrupted ({why})");
            Player bot = s.Bot;
            if (s.Dead || bot == null || bot.MovementContext == null || !Perception.Alive(bot)) return;
            if (had == GestureKind.Wiggle) bot.MovementContext.SetTilt(0f);
            else if (had == GestureKind.Defiant)
            {
                IAnimatedInteractions ai = Interactions(bot);
                if (ai != null && ai.IsInteractionPlaying) ai.ForceStopInteractions();
            }
        }
    }
}
