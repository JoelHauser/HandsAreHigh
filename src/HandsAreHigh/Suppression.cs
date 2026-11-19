using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Incoming fire, per bot. The game calls BotMemory.SetUnderFire when the bot is hit
    // (GetHitInner) and when it hears gunfire within BULLET_FEEL_DIST aimed close to it
    // (BotHearingSensor). Each call adds one "shot at him"; the total fades with a half-life,
    // computed from timestamps so it doesn't depend on frame rate. SAIN's own suppression
    // level is read as well and the higher of the two counts, so one burst is never counted twice.
    [HarmonyPatch(typeof(BotMemory), nameof(BotMemory.SetUnderFire))]
    internal static class UnderFirePatch
    {
        private static void Postfix(BotMemory __instance)
        {
            Player bot = __instance?._owner?.GetPlayer;
            if (bot != null) Suppression.Record(bot);
        }
    }

    internal static class Suppression
    {
        private const float MinEventGap = 0.05f;   // one shot can reach SetUnderFire twice (hit + sound)
        private const float Cap = 40f;

        internal static void Record(Player bot)
        {
            if (!Settings.Enabled.Value) return;
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            float now = Time.time;
            if (now - h.FireAt < MinEventGap) return;
            h.Fire = Mathf.Min(Cap, Decayed(h, now) + 1f);
            h.FireAt = now;
        }

        // Shots at him, faded by the half-life.
        internal static float Decayed(BotHistory h, float now)
        {
            if (h.Fire <= 0f) return 0f;
            float halfLife = Mathf.Max(0.1f, Settings.SuppressionHalfLife.Value);
            return h.Fire * Mathf.Pow(0.5f, (now - h.FireAt) / halfLife);
        }

        // 0..1: max of the game's incoming-fire signal and SAIN's suppression state.
        internal static float Factor(BotOwner owner, BotHistory h, out float own, out float sain)
        {
            own = Mathf.Clamp01(Decayed(h, Time.time) / Mathf.Max(1f, Settings.SustainedFireEvents.Value));
            sain = SainAdapter.SuppressionLevel(owner);
            return Mathf.Max(own, sain);
        }
    }
}
