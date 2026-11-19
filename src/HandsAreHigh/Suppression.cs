using System.Collections.Generic;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Incoming fire, per bot, from three signals; the highest wins, nothing is added twice.
    //
    // 1. Near misses (ShotPatch): every shot goes through Player.FirearmController.InitiateShot
    //    (ClientFirearmController's override calls base). For each eligible bot hostile to the
    //    shooter, the shot's line is measured against his chest: passing within NearMissRadius,
    //    in front of the muzzle, within NearMissRange, with no terrain or wall in between, counts
    //    as fire at him (closer counts more). This is the signal that works under SAIN: SAIN's
    //    HearingSensorPatch disables the game's BotHearingSensor, which is where the game's own
    //    near-miss call (BotMemory.SetUnderFire) comes from, so in 0.2.0 only hits counted.
    // 2. Hits (UnderFirePatch): BotMemory.SetUnderFire from GetHitInner (and from the hearing
    //    sensor without SAIN). Ignored for half a second after a near miss was recorded for the
    //    same bot, which is the same bullet arriving.
    // 3. SAIN's own suppression level, read in Factor().
    //
    // Events are rate-limited (one per 0.1 s per bot) so fire rate doesn't scale the result,
    // and fade with a half-life computed from timestamps, so frame rate doesn't either.
    [HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.InitiateShot))]
    internal static class ShotPatch
    {
        private static void Postfix(Player ____player, Vector3 shotPosition, Vector3 shotDirection)
        {
            if (____player == null) return;
            HandsAreHighPlugin.Try("near miss", () => Suppression.OnShot(____player, shotPosition, shotDirection));
        }
    }

    [HarmonyPatch(typeof(BotMemory), nameof(BotMemory.SetUnderFire))]
    internal static class UnderFirePatch
    {
        private static void Postfix(BotMemory __instance)
        {
            Player bot = __instance?._owner?.GetPlayer;
            if (bot != null) HandsAreHighPlugin.Try("under fire", () => Suppression.OnUnderFire(bot));
        }
    }

    internal static class Suppression
    {
        private const float MinEventGap = 0.1f;
        private const float SameBulletWindow = 0.5f;
        private const float Cap = 40f;
        private static readonly List<Player> Candidates = new List<Player>();

        internal static void OnShot(Player shooter, Vector3 from, Vector3 dir)
        {
            if (!Settings.Enabled.Value || !Settings.NaturalSurrender.Value) return;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return;
            dir.Normalize();
            float radius = Settings.NearMissRadius.Value;
            float range = Settings.NearMissRange.Value;

            Candidates.Clear();
            foreach (Player p in world.AllAlivePlayersList)
                if (p != null && p != shooter && p.IsAI) Candidates.Add(p);

            foreach (Player bot in Candidates)
            {
                Vector3 chest = Perception.Chest(bot);
                Vector3 v = chest - from;
                float along = Vector3.Dot(v, dir);
                if (along <= 0f || along > range) continue;
                Vector3 closest = from + dir * along;
                float miss = (chest - closest).magnitude;
                if (miss > radius) continue;
                if (!NaturalSurrender.Eligible(bot)) continue;
                BotOwner owner = bot.AIData?.BotOwner;
                if (owner == null || !IsHostile(owner, shooter)) continue;
                if (Physics.Linecast(from, closest, LayersMaskController.HighPolyWithTerrainMask)) continue;

                float weight = 1f - 0.5f * (miss / radius);   // 1 at the body, 0.5 at the edge
                Record(bot, weight, $"shot from {shooter.Profile?.Nickname} passed {miss:0.0} m away");
            }
        }

        internal static void OnUnderFire(Player bot)
        {
            if (!Settings.Enabled.Value || !Settings.NaturalSurrender.Value) return;
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            if (Time.time - h.NearMissAt < SameBulletWindow) return;   // the near miss already counted it
            Record(bot, 1f, "under fire (hit or heard)");
        }

        internal static bool IsHostile(BotOwner owner, Player shooter)
        {
            try
            {
                if (owner.BotsGroup != null && owner.BotsGroup.IsEnemy(shooter)) return true;
                return owner.EnemiesController != null && owner.EnemiesController.IsEnemy(shooter);
            }
            catch { return false; }
        }

        private static void Record(Player bot, float weight, string what)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            float now = Time.time;
            if (what.StartsWith("shot")) h.NearMissAt = now;
            if (now - h.FireAt < MinEventGap) return;
            h.Fire = Mathf.Min(Cap, Decayed(h, now) + weight);
            h.FireAt = now;
            NaturalSurrender.CheckSoon(h);
            if (Settings.VerboseLogs.Value)
                HandsAreHighPlugin.Log.LogInfo($"{bot.Profile?.Nickname}: fire +{weight:0.00} ({what}), now {h.Fire:0.00} shots' worth");
        }

        // Shots at him, faded by the half-life.
        internal static float Decayed(BotHistory h, float now)
        {
            if (h.Fire <= 0f) return 0f;
            float halfLife = Mathf.Max(0.1f, Settings.SuppressionHalfLife.Value);
            return h.Fire * Mathf.Pow(0.5f, (now - h.FireAt) / halfLife);
        }

        // 0..1: max of our fire count and SAIN's suppression state.
        internal static float Factor(BotOwner owner, BotHistory h, out float own, out float sain, out float shots)
        {
            shots = Decayed(h, Time.time);
            own = Mathf.Clamp01(shots / Mathf.Max(1f, Settings.SustainedFireEvents.Value));
            sain = SainAdapter.SuppressionLevel(owner);
            return Mathf.Max(own, sain);
        }
    }
}
