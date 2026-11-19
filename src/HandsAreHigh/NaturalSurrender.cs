using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // When a bot gives up on his own.
    //
    // Pressure: what is happening to him, each 0..1, weighted and summed:
    //   injury       health below the threshold
    //   ammo         no rounds for his gun and nothing to reload, or no gun at all
    //   suppression  incoming fire (Suppression.cs: hits + shots aimed close, fading; or SAIN's level)
    //   outnumbered  enemies he knows of per friendly close to him
    //   squad        squadmates killed recently, or squad alive but nobody close
    // Armour dulls suppression and outnumbered (protection makes fire less frightening; it
    // never removes it). Each input is a separate signal: a hit raises suppression and lowers
    // health, but injury reads health and suppression reads fire, so neither is counted twice.
    //
    // Chance, per check, once pressure passes the threshold with enough separate reasons:
    //   BaseChance * pressure * (1 - NerveResistance * nerve) * role * personality * kit * experience
    //   kit:        expensive kit, more to lose: up to 1 + KitValueWeight
    //   experience: level and difficulty, calmer: down to 1 - ExperienceComposure, never 0
    // Checks are spread out (every EvalInterval per bot, staggered) and never catch up after
    // a pause or sleep.
    internal static class NaturalSurrender
    {
        private const float ReasonFloor = 0.15f;   // a factor this small (one stray shot) isn't a reason
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
            // He gives up to a human player close by, whom he sees now or saw a moment ago
            // (a man pinned behind cover still knows where you are).
            float now = Time.time;
            EnemyInfo goal = owner.Memory?.GoalEnemy;
            if (goal == null) return;
            if (!(goal.Person is Player enemy) || enemy.IsAI || !Perception.Alive(enemy)) return;
            bool seenNow = goal.IsVisible;
            if (!seenNow && now - goal.PersonalLastSeenTime > Settings.SeenWithin.Value) return;
            float dist = Vector3.Distance(enemy.Position, bot.Position);
            if (dist > Settings.SurrenderDistance.Value) return;

            // Mid-reload or mid-weapon-change: wait for the next check.
            if (owner.WeaponManager?.Selector?.IsChanging == true) return;
            if (bot.HandsController is Player.FirearmController fc && fc.IsInReloadOperation()) return;

            Arsenal.AssessKit(bot, h);
            float armourDull = 1f - Settings.ArmourConfidence.Value * h.Protection;

            float injury = 0f;
            float health = Arsenal.HealthFraction(bot);
            float thr = Settings.InjuryThreshold.Value;
            if (health < thr) injury = Mathf.Clamp01((thr - health) / thr + 0.25f);

            float ammo = Arsenal.OutOfAmmo(owner) ? 1f : 0f;

            float suppression = Suppression.Factor(owner, h, out float ownFire, out float sainFire) * armourDull;

            int hostiles = CountHostiles(owner, now);
            int friends = CountFriends(owner, bot, out int squadAlive) + 1;
            float ratio = hostiles / (float)friends;
            float outnumbered = ratio > 1f ? Mathf.Clamp01((ratio - 1f) / (Settings.OutnumberedRatio.Value - 1f)) * armourDull : 0f;

            float squad = 0f;
            string squadWhy = "none";
            if (h.SquadPeak > 1 && now - h.SquadLossTime < Settings.SquadMemory.Value)
            {
                squad = Mathf.Clamp01((h.SquadPeak - h.SquadSeen) / (float)(h.SquadPeak - 1));
                squadWhy = $"lost {h.SquadPeak - h.SquadSeen} of {h.SquadPeak - 1}";
            }
            if (squadAlive > 0 && friends == 1 && Settings.AloneFactor.Value > squad)
            {
                squad = Settings.AloneFactor.Value;
                squadWhy = $"{squadAlive} alive, none within {Settings.AllyRadius.Value:0} m";
            }

            int reasons = Count(injury) + Count(ammo) + Count(suppression) + Count(outnumbered) + Count(squad);
            float pressure = Mathf.Clamp01(
                Settings.InjuryWeight.Value * injury +
                Settings.AmmoWeight.Value * ammo +
                Settings.SuppressionWeight.Value * suppression +
                Settings.OutnumberedWeight.Value * outnumbered +
                Settings.SquadWeight.Value * squad);

            bool gated = reasons >= Settings.RequiredFactors.Value && pressure >= Settings.PressureThreshold.Value;
            if (!gated && !Settings.VerboseLogs.Value) return;

            WildSpawnType role = bot.Profile.Info.Settings.Role;
            float roleMult = IsPmc(role) ? Settings.PmcMultiplier.Value : Settings.ScavMultiplier.Value;
            string personality = SainAdapter.Personality(owner);
            float persMult = SainAdapter.Multiplier(personality);
            float kitMult = h.KitValue < 0f ? 1f : 1f + Settings.KitValueWeight.Value * Mathf.Clamp01(h.KitValue / Settings.KitValueFull.Value);
            float experience = Experience(bot, owner);
            float expMult = 1f - Settings.ExperienceComposure.Value * experience;
            float nerve = Settings.EffectiveNerve(h.Nerve);
            float chance = Mathf.Min(Settings.MaxChance.Value,
                Settings.BaseChance.Value * pressure * (1f - Settings.NerveResistance.Value * nerve) * roleMult * persMult * kitMult * expMult);
            chance = Mathf.Clamp01(chance);
            float roll = Random.value;
            bool gives = gated && roll < chance;

            var sb = new StringBuilder();
            sb.Append($"{bot.Profile?.Nickname} ({role}, lvl {bot.Profile?.Info?.Level}{(string.IsNullOrEmpty(personality) ? "" : ", " + personality)}) ");
            sb.Append($"vs {enemy.Profile?.Nickname} {dist:0} m{(seenNow ? "" : " (just lost sight)")}: pressure {pressure:0.00} [");
            sb.Append($"hurt {injury:0.00}, ammo {ammo:0}, fire {suppression:0.00} (game {ownFire:0.00}, SAIN {sainFire:0.00}), ");
            sb.Append($"outnumbered {outnumbered:0.00} ({hostiles} vs {friends}), squad {squad:0.00} ({squadWhy})], {reasons} reason(s); ");
            sb.Append($"armour class {h.Protection * 6f:0} (dulls fire x{armourDull:0.00}), kit {(h.KitValue < 0f ? "n/a" : (h.KitValue / 1000f).ToString("0") + "k")} x{kitMult:0.00}, ");
            sb.Append($"experience {experience:0.00} x{expMult:0.00}, personality x{persMult:0.00}, role x{roleMult:0.00}, nerve {nerve:0.00}; ");
            sb.Append($"chance {chance:0.000}, roll {roll:0.000}");
            sb.Append(gives ? " -> surrenders" : gated ? " -> holds on" : " -> below threshold");
            HandsAreHighPlugin.Log.LogInfo(sb.ToString());

            if (gives)
                SurrenderRegistry.Surrender(bot, enemy, $"gave up (pressure {pressure:0.00}, chance {chance:0.000})");
        }

        private static int Count(float factor) => factor >= ReasonFloor ? 1 : 0;

        // 0..1 from level (0 at 1, 1 at 40+) and difficulty (easy 0 .. impossible 1).
        private static float Experience(Player bot, BotOwner owner)
        {
            int level = bot.Profile?.Info?.Level ?? 1;
            float lvl = Mathf.Clamp01((level - 1) / 39f);
            float diff;
            switch (owner.Settings?._difficulty)
            {
                case BotDifficulty.easy: diff = 0f; break;
                case BotDifficulty.hard: diff = 0.67f; break;
                case BotDifficulty.impossible: diff = 1f; break;
                default: diff = 0.33f; break;
            }
            return Mathf.Clamp01(0.6f * lvl + 0.4f * diff);
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
                if (e?.Person?.HealthController == null || !e.Person.HealthController.IsAlive) continue;
                if (e.IsVisible || now - e.PersonalLastSeenTime < memory) n++;
            }
            return Mathf.Max(1, n);
        }

        // Living squadmates close enough to matter, not counting himself; squadAlive counts
        // them wherever they are.
        private static int CountFriends(BotOwner owner, Player self, out int squadAlive)
        {
            squadAlive = 0;
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
                squadAlive++;
                if ((mp.Position - self.Position).sqrMagnitude <= r * r) n++;
            }
            return n;
        }
    }
}
