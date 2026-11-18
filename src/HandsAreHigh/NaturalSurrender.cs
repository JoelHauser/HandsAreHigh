using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // When a bot gives up on his own. Pressure from separate reasons (hurt, out of ammo,
    // outnumbered, squad lost, under fire) must pass a threshold, then one roll per check:
    //
    //   chance = min(MaxChance, BaseChance * pressure * (1 - NerveResistance * nerve) * role * personality)
    //
    // Checks are spread out (every EvalInterval per bot, staggered) and never catch up
    // after a pause or sleep.
    internal static class NaturalSurrender
    {
        private static readonly List<Player> Bots = new List<Player>();

        internal static void Tick()
        {
            if (!Settings.Enabled.Value || !Settings.NaturalSurrender.Value) return;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return;

            Bots.Clear();
            foreach (Player p in world.AllAlivePlayersList)
                if (p != null && p.IsAI) Bots.Add(p);

            float now = Time.time;
            foreach (Player bot in Bots)
            {
                if (SurrenderRegistry.HoldsBot(bot) || !Eligible(bot)) continue;
                BotOwner owner = bot.AIData?.BotOwner;
                if (owner == null || owner.BotState != EBotState.Active || owner.IsDead) continue;

                BotHistory h = SurrenderRegistry.HistoryOf(bot);
                if (now < h.NextEval) continue;
                h.NextEval = now + Settings.EvalInterval.Value * Random.Range(0.8f, 1.2f);

                TrackSquad(owner, h);
                if (now < h.CooldownUntil) continue;
                HandsAreHighPlugin.Try("surrender check", () => Evaluate(bot, owner, h));
            }
        }

        // Scavs and PMCs only: bosses, followers, raiders, rogues and cultist priests have
        // other roles and never get here.
        internal static bool Eligible(Player p)
        {
            WildSpawnType role = p.Profile.Info.Settings.Role;
            if (role == WildSpawnType.assault || role == WildSpawnType.cursedAssault) return true;
            return Settings.IncludePmcs.Value && IsPmc(role);
        }

        private static bool IsPmc(WildSpawnType role) => role == WildSpawnType.pmcBEAR || role == WildSpawnType.pmcUSEC;

        // Squad deaths: the group shrinks when a member dies (BotsGroup.RemoveAlly).
        private static void TrackSquad(BotOwner owner, BotHistory h)
        {
            int count = owner.BotsGroup?.MembersCount ?? 1;
            if (count > h.SquadPeak) h.SquadPeak = count;
            if (h.SquadSeen > 0 && count < h.SquadSeen) h.SquadLossTime = Time.time;
            h.SquadSeen = count;
        }

        private static void Evaluate(Player bot, BotOwner owner, BotHistory h)
        {
            // He gives up to a human player he can see, close by.
            EnemyInfo goal = owner.Memory?.GoalEnemy;
            if (goal == null || !goal.IsVisible) return;
            if (!(goal.Person is Player enemy) || enemy.IsAI || !Perception.Alive(enemy)) return;
            float dist = Vector3.Distance(enemy.Position, bot.Position);
            if (dist > Settings.SurrenderDistance.Value) return;

            // Mid-reload or mid-weapon-change: wait for the next check.
            if (owner.WeaponManager?.Selector?.IsChanging == true) return;
            if (bot.HandsController is Player.FirearmController fc && fc.IsInReloadOperation()) return;

            float now = Time.time;
            float injury = 0f;
            float health = Arsenal.HealthFraction(bot);
            float thr = Settings.InjuryThreshold.Value;
            if (health < thr) injury = Mathf.Clamp01((thr - health) / thr + 0.25f);

            float ammo = Arsenal.OutOfAmmo(owner) ? 1f : 0f;

            float outnumbered = 0f;
            int hostiles = CountHostiles(owner, now);
            int friends = CountFriends(owner, bot) + 1;
            float ratio = hostiles / (float)friends;
            if (ratio > 1f) outnumbered = Mathf.Clamp01((ratio - 1f) / (Settings.OutnumberedRatio.Value - 1f));

            float squad = 0f;
            if (h.SquadPeak > 1 && now - h.SquadLossTime < Settings.SquadMemory.Value)
                squad = Mathf.Clamp01((h.SquadPeak - h.SquadSeen) / (float)(h.SquadPeak - 1));

            float suppression = owner.Memory.IsUnderFire || now - owner.Memory.LastTimeHit < Settings.SuppressionMemory.Value ? 1f : 0f;

            int reasons = (injury > 0f ? 1 : 0) + (ammo > 0f ? 1 : 0) + (outnumbered > 0f ? 1 : 0) + (squad > 0f ? 1 : 0) + (suppression > 0f ? 1 : 0);
            float pressure = Mathf.Clamp01(
                Settings.InjuryWeight.Value * injury +
                Settings.AmmoWeight.Value * ammo +
                Settings.OutnumberedWeight.Value * outnumbered +
                Settings.SquadWeight.Value * squad +
                Settings.SuppressionWeight.Value * suppression);

            bool gated = reasons >= Settings.RequiredFactors.Value && pressure >= Settings.PressureThreshold.Value;
            if (!gated && !Settings.VerboseLogs.Value) return;

            WildSpawnType role = bot.Profile.Info.Settings.Role;
            float roleMult = IsPmc(role) ? Settings.PmcMultiplier.Value : Settings.ScavMultiplier.Value;
            string personality = SainAdapter.Personality(owner);
            float persMult = SainAdapter.Multiplier(personality);
            float nerve = Settings.EffectiveNerve(h.Nerve);
            float chance = Mathf.Min(Settings.MaxChance.Value,
                Settings.BaseChance.Value * pressure * (1f - Settings.NerveResistance.Value * nerve) * roleMult * persMult);
            chance = Mathf.Clamp01(chance);
            float roll = Random.value;
            bool gives = gated && roll < chance;

            var sb = new StringBuilder();
            sb.Append($"{bot.Profile?.Nickname} ({role}{(string.IsNullOrEmpty(personality) ? "" : ", " + personality)}): pressure {pressure:0.00} [");
            sb.Append($"hurt {injury:0.00}, ammo {ammo:0}, outnumbered {outnumbered:0.00} ({hostiles} vs {friends}), squad {squad:0.00}, fire {suppression:0}");
            sb.Append($"], {reasons} reason(s), nerve {nerve:0.00}, chance {chance:0.000}, roll {roll:0.000}");
            sb.Append(gives ? " -> surrenders" : gated ? " -> holds on" : " -> below threshold");
            HandsAreHighPlugin.Log.LogInfo(sb.ToString());

            if (gives)
                SurrenderRegistry.Surrender(bot, enemy, $"gave up (pressure {pressure:0.00}, chance {chance:0.000})");
        }

        // Enemies he's seen recently and who are still alive.
        private static int CountHostiles(BotOwner owner, float now)
        {
            var infos = owner.EnemiesController?.EnemyInfos;
            if (infos == null) return 1;
            int n = 0;
            float memory = Settings.EnemyMemory.Value;
            foreach (EnemyInfo e in infos.Values)
            {
                if (e?.Person == null || !e.Person.HealthController.IsAlive) continue;
                if (e.IsVisible || now - e.PersonalLastSeenTime < memory) n++;
            }
            return Mathf.Max(1, n);
        }

        // Living squadmates close enough to matter, not counting himself.
        private static int CountFriends(BotOwner owner, Player self)
        {
            BotsGroup group = owner.BotsGroup;
            if (group == null) return 0;
            float r = Settings.AllyRadius.Value;
            int n = 0;
            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner m = group.Member(i);
                if (m == null || m == owner || m.IsDead) continue;
                Player mp = m.GetPlayer;
                if (mp == null || !Perception.Alive(mp)) continue;
                if ((mp.Position - self.Position).sqrMagnitude <= r * r) n++;
            }
            return n;
        }
    }
}
