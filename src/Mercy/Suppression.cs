using System.Collections.Generic;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Mercy
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
        // Every shot by anyone: no closure (a lambda here allocated per shot), nothing at all
        // outside a raid or with the mod off.
        private static void Postfix(Player.FirearmController __instance, Player ____player, Vector3 shotPosition, Vector3 shotDirection)
        {
            if (____player == null || !Settings.Enabled.Value) return;
            long t0 = Perf.Begin();
            try { Suppression.OnShot(____player, shotPosition, shotDirection); }
            catch (System.Exception e) { MercyPlugin.Report("near miss", e); }
            // 0.28.4: the burst guard. This round is already fired; the next ones in the burst are
            // stopped by releasing the trigger when a man this bot holds fire at is on its line.
            if (Restraint.Any && ____player.IsAI)
            {
                try
                {
                    if (Restraint.ShotAtRestrained(____player, shotPosition, shotDirection, out Player prisoner))
                    {
                        // As the other guards (ShootDataPatch): the AI's own shot ends (its trigger with it).
                        BotOwner bo = ____player.AIData?.BotOwner;
                        if (bo?.ShootData != null) bo.ShootData.EndShoot();
                        else __instance.SetTriggerPressed(false);
                        Restraint.LogBurstStopped(____player, prisoner);
                    }
                }
                catch (System.Exception e) { MercyPlugin.Report("burst guard", e); }
            }
            Perf.End(Perf.Sec.Shot, t0);
        }
    }

    [HarmonyPatch(typeof(BotMemory), nameof(BotMemory.SetUnderFire))]
    internal static class UnderFirePatch
    {
        // 0.21.0: the game passes who fired (GetHitInner: the hit's player; the hearing sensor: the
        // shooter); kept as the bot's last known source of fire while anyone is held or guarded.
        private static void Postfix(BotMemory __instance, IPlayer source)
        {
            if (!Settings.Enabled.Value) return;
            Player bot = __instance?._owner?.GetPlayer;
            if (bot == null) return;
            long t0 = Perf.Begin();
            try
            {
                if (SurrenderRegistry.Any || Captors.Any)
                {
                    Player by = Perception.PlayerOf(source);
                    if (by != null && by != bot)
                    {
                        BotHistory h = SurrenderRegistry.HistoryOf(bot);
                        h.FireBy = by;
                        h.FireByAt = Time.time;
                    }
                }
                if (Settings.NaturalSurrender.Value) Suppression.OnUnderFire(bot);
            }
            catch (System.Exception e) { MercyPlugin.Report("under fire", e); }
            Perf.End(Perf.Sec.Hit, t0);
        }
    }

    internal static class Suppression
    {
        private const float MinEventGap = 0.1f;
        private const float SameBulletWindow = 0.5f;
        private const float Cap = 40f;
        private const float HitWeight = 2f;
        private static readonly List<Player> Candidates = new List<Player>();

        internal static void OnShot(Player shooter, Vector3 from, Vector3 dir)
        {
            if (!Settings.Enabled.Value) return;
            dir.Normalize();
            float radius = Settings.NearMissRadius.Value;
            float range = Settings.NearMissRange.Value;
            if (!shooter.IsAI && SurrenderRegistry.Any) WarningShots(shooter, from, dir, radius, range);
            // Near misses past a held man's head, by anyone: a cosmetic flinch (Presence).
            if (SurrenderRegistry.Any) Presence.OnShot(shooter, from, dir);
            // 0.21.0: a credible near miss on a prisoner or his captor breaks the hold-up.
            if (SurrenderRegistry.Any) FireBreak.OnShot(shooter, from, dir);
            // Gunfire is heard: a held man can tell his captor is shooting (Reactions), a captor
            // can tell he is being shot at. Only kept while anyone is held (one lookup per shot).
            if (SurrenderRegistry.Any) SurrenderRegistry.HistoryOf(shooter).ShotAt = Time.time;
            // Near misses feed the surrender checks and the captors' threat sense.
            if (!Settings.NaturalSurrender.Value && !Captors.Any) return;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return;

            // Cheapest tests first: geometry (a few multiplies), then eligibility, then
            // hostility, and only then one Linecast for the rare shot that passes close.
            Candidates.Clear();
            List<Player> all = world.AllAlivePlayersList;
            for (int i = 0; i < all.Count; i++)
            {
                Player p = all[i];
                if (p != null && p != shooter && p.IsAI) Candidates.Add(p);
            }
            float r2 = radius * radius;
            for (int i = 0; i < Candidates.Count; i++)
            {
                Player bot = Candidates[i];
                Vector3 chest = Perception.Chest(bot);
                Vector3 v = chest - from;
                float along = Vector3.Dot(v, dir);
                if (along <= 0f || along > range) continue;
                Vector3 closest = from + dir * along;
                float miss2 = (chest - closest).sqrMagnitude;
                if (miss2 > r2) continue;
                if (!NaturalSurrender.Eligible(bot)) continue;
                BotOwner owner = bot.AIData?.BotOwner;
                if (owner == null || !IsHostile(owner, shooter)) continue;
                if (Physics.Linecast(from, closest, LayersMaskController.HighPolyWithTerrainMask)) continue;

                float miss = Mathf.Sqrt(miss2);
                float weight = 1f - 0.5f * (miss / radius);   // 1 at the body, 0.5 at the edge
                Record(bot, weight, nearMiss: true, shooter, miss);
                // Where it came from, as he can tell it from the crack and thump: the muzzle
                // position, blurred by a tenth of the distance. Never the shooter himself.
                BotHistory bh = SurrenderRegistry.HistoryOf(bot);
                float blur = 0.1f * along;
                bh.FireFrom = from + new Vector3(Dice.Range(-blur, blur), 0f, Dice.Range(-blur, blur));
                bh.FireFromAt = Time.time;
            }
            Candidates.Clear();
        }

        // A player's shot passing close to a bot holding his hands up, without hitting him (a
        // hit is Player.BeingHitAction: he runs). He freezes for a while and may plead.
        private static void WarningShots(Player shooter, Vector3 from, Vector3 dir, float radius, float range)
        {
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Bot == null || s.Phase != Phase.HandsUp || s.Suspended) continue;
                Vector3 chest = Perception.Chest(s.Bot);
                float along = Vector3.Dot(chest - from, dir);
                if (along <= 0f || along > range) continue;
                Vector3 closest = from + dir * along;
                float miss = (chest - closest).magnitude;
                if (miss > radius) continue;
                if (Physics.Linecast(from, closest, LayersMaskController.HighPolyWithTerrainMask)) continue;
                Reactions.WarningShot(s, shooter, miss);
            }
        }

        internal static void OnUnderFire(Player bot)
        {
            if (!Settings.Enabled.Value || !Settings.NaturalSurrender.Value) return;
            if (!NaturalSurrender.Eligible(bot)) return;
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            if (Time.time - h.NearMissAt < SameBulletWindow) return;   // the near miss already counted it
            // 0.19.0: a hit weighs two near misses (one hit was 1/6 of full suppression: below the
            // 0.2 a reason needs, so the first hit of a fight never counted as being under fire).
            Record(bot, HitWeight, nearMiss: false, null, 0f);
        }

        // Debug scenarios only: a synthetic fire event (no shot, no near-miss geometry). It tests
        // what the decisions do with suppression, never whether suppression is detected.
        internal static void Synthetic(Player bot, float weight)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            float now = Time.time;
            // Keep what he had, re-based just past the event gap so this event isn't swallowed
            // (the fade over that 0.1 s is negligible).
            h.Fire = Decayed(h, now);
            h.FireAt = now - MinEventGap - 0.001f;
            Record(bot, weight, nearMiss: false, null, 0f);
        }

        internal static bool IsHostile(BotOwner owner, Player shooter)
        {
            try
            {
                if (owner.BotsGroup != null && owner.BotsGroup.IsEnemy(shooter)) return true;
                return Perception.InfoAbout(owner, shooter) != null;   // either key (Perception.PlayerOf)
            }
            catch { return false; }
        }

        // The log message is only built with Verbose logs on.
        private static void Record(Player bot, float weight, bool nearMiss, Player shooter, float miss)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            float now = Time.time;
            if (nearMiss) h.NearMissAt = now;
            if (shooter != null) { h.FireBy = shooter; h.FireByAt = now; }
            FireBreak.Extend(h, now);   // 0.21.0: still under the fire that broke his hold-up
            if (now - h.FireAt < MinEventGap) return;
            h.Fire = Mathf.Min(Cap, Decayed(h, now) + weight);
            h.FireAt = now;
            NaturalSurrender.CheckSoon(h);
            if (Settings.VerboseLogs.Value)
                MercyPlugin.Log.LogInfo($"{bot.Profile?.Nickname}: fire +{weight:0.00} (" +
                    (nearMiss ? $"shot from {shooter?.Profile?.Nickname} passed {miss:0.0} m away" : "under fire (hit or heard)") +
                    $"), now {h.Fire:0.00} shots' worth");
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
            sain = SainAdapter.SuppressionLevel(owner, h);
            return Mathf.Max(own, sain);
        }
    }
}
