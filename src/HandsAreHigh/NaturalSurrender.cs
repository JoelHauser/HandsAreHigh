using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace HandsAreHigh
{
    // When a bot gives up on his own.
    //
    // Pressure: what is happening to him, each 0..1, weighted and summed:
    //   injury       the worst of: overall health below InjuryThreshold, head or thorax below
    //                VitalThreshold, a destroyed limb, bleeding (heavy 0.5, light 0.25). The
    //                worst one, never a sum: they describe the same wounds from different angles.
    //   ammo         no rounds for his gun and nothing to reload, or no gun at all
    //   suppression  fire at him (Suppression.cs: near misses, hits, SAIN's level; the highest)
    //   outnumbered  enemies he knows of per friendly close to him
    //   squad        squadmates killed recently, or squad alive but nobody close
    // Armour dulls suppression and outnumbered. A hit raises suppression and lowers health, but
    // injury reads health and suppression reads fire, so no single input is counted twice.
    //
    // Eligible when either:
    //   - at least RequiredFactors reasons (each >= 0.2) and pressure >= PressureThreshold, or
    //   - severe injury (injury >= SevereInjury): one reason is enough; pressure floor
    //     SeverePressure and the chance x SevereChanceBoost, or
    //   - pinned down (suppression >= PinnedLevel): one reason is enough; pressure floor
    //     PinnedPressure.
    // Then one roll: BaseChance x pressure x (1 - NerveResistance x nerve) x role x personality
    //   x kit value x experience (x severe boost), capped at MaxChance.
    // Checks every EvalInterval per bot, staggered, no catch-up; a hit or near miss brings the
    // next check forward (PromptCheckDelay, at most every 1.5 s). Every check where anything
    // is pressing on him is logged with every input and the exact reason for the outcome.
    internal static class NaturalSurrender
    {
        private const float ReasonFloor = 0.2f;    // a factor this small (one stray shot ~0.15) isn't a reason
        private const float PromptGap = 1.5f;
        private const float LogFloor = 0.15f;      // pressure below this is only logged with Verbose logs
        private static readonly List<Player> Bots = new List<Player>();
        private static readonly EBodyPart[] Limbs =
            { EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg, EBodyPart.Stomach };
        private static readonly EBodyPart[] Parts =
            { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };

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

        // A hit or a near miss: check him again soon, not in up to 5 s.
        internal static void CheckSoon(BotHistory h)
        {
            float now = Time.time;
            if (now - h.PromptAt < PromptGap) return;
            h.PromptAt = now;
            h.NextEval = Mathf.Min(h.NextEval, now + Settings.PromptCheckDelay.Value);
        }

        internal enum RoleClass { None, Scav, Pmc, Boss }

        // From the profile's WildSpawnType (and the game's own WildSpawnTypeExtension.IsBossOrFollower),
        // never from names. Raiders, rogues, cultists and others are not surrender candidates.
        internal static RoleClass ClassOf(Player p)
        {
            WildSpawnType role = p.Profile.Info.Settings.Role;
            if (role == WildSpawnType.assault || role == WildSpawnType.cursedAssault) return RoleClass.Scav;
            if (role == WildSpawnType.pmcBEAR || role == WildSpawnType.pmcUSEC) return RoleClass.Pmc;
            if (role.IsBossOrFollower()) return RoleClass.Boss;
            return RoleClass.None;
        }

        internal static bool Eligible(Player p)
        {
            switch (ClassOf(p))
            {
                case RoleClass.Scav: return true;
                case RoleClass.Pmc: return Settings.IncludePmcs.Value;
                case RoleClass.Boss: return Settings.IncludeBosses.Value && HandsAreHighPlugin.BossLayersRegistered;
                default: return false;
            }
        }

        private static void RoleOdds(RoleClass c, out float baseChance, out float threshold, out float max)
        {
            switch (c)
            {
                case RoleClass.Pmc: baseChance = Settings.PmcChance.Value; threshold = Settings.PmcThreshold.Value; max = Settings.PmcMax.Value; return;
                case RoleClass.Boss: baseChance = Settings.BossChance.Value; threshold = Settings.BossThreshold.Value; max = Settings.BossMax.Value; return;
                default: baseChance = Settings.ScavChance.Value; threshold = Settings.ScavThreshold.Value; max = Settings.ScavMax.Value; return;
            }
        }

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
            // (a man pinned behind cover still knows where you are), or to the player whose
            // Stop demand he heard (checked in StopDemand when it was made).
            float now = Time.time;
            bool demanded = Settings.DemandEnabled.Value && now < h.DemandUntil && Perception.Alive(h.DemandBy);
            Player enemy;
            bool seenNow;
            if (demanded)
            {
                enemy = h.DemandBy;
                seenNow = Perception.LineClear(Perception.Eye(bot), Perception.Eye(enemy));
            }
            else
            {
                EnemyInfo goal = owner.Memory?.GoalEnemy;
                if (goal == null) { Blocked(bot, h, "no enemy targeted"); return; }
                if (!(goal.Person is Player human) || human.IsAI || !Perception.Alive(human)) { Blocked(bot, h, "his target isn't a human player"); return; }
                enemy = human;
                seenNow = goal.IsVisible;
                if (!seenNow && now - goal.PersonalLastSeenTime > Settings.SeenWithin.Value) { Blocked(bot, h, $"hasn't seen you for {now - goal.PersonalLastSeenTime:0} s"); return; }
            }
            float dist = Vector3.Distance(enemy.Position, bot.Position);
            float maxDist = demanded ? Mathf.Max(Settings.SurrenderDistance.Value, Settings.DemandRange.Value) : Settings.SurrenderDistance.Value;
            if (dist > maxDist) { Blocked(bot, h, $"you're {dist:0} m away (max {maxDist:0})"); return; }

            // Mid weapon change: wait for the next check. (Mid-reload is fine: the surrender
            // cuts the reload short and drops the gun.)
            if (owner.WeaponManager?.Selector?.IsChanging == true) return;

            Arsenal.AssessKit(bot, h);
            float armourDull = 1f - Settings.ArmourConfidence.Value * h.Protection;

            float injury = Injury(bot, out string injuryWhy);
            float ammo = Arsenal.OutOfAmmo(owner) ? 1f : 0f;
            float rawFire = Suppression.Factor(owner, h, out float ownFire, out float sainFire, out float shots);
            float suppression = rawFire * armourDull;

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

            int reasons = Count(injury) + Count(ammo) + Count(suppression) + Count(outnumbered) + Count(squad) + (demanded ? 1 : 0);
            float pressure = Mathf.Clamp01(
                Settings.InjuryWeight.Value * injury +
                Settings.AmmoWeight.Value * ammo +
                Settings.SuppressionWeight.Value * suppression +
                Settings.OutnumberedWeight.Value * outnumbered +
                Settings.SquadWeight.Value * squad +
                (demanded ? Settings.DemandPressure.Value : 0f));

            RoleClass rc = ClassOf(bot);
            RoleOdds(rc, out float roleBase, out float threshold, out float roleMax);

            bool severe = injury >= Settings.SevereInjury.Value;
            bool pinned = rawFire >= Settings.PinnedLevel.Value;
            if (severe) pressure = Mathf.Max(pressure, Settings.SeverePressure.Value);
            if (pinned) pressure = Mathf.Max(pressure, Settings.PinnedPressure.Value);

            string gate;
            bool eligible;
            if (severe || pinned || (demanded && pressure >= threshold * 0.75f))
            {
                eligible = true;
                var parts = new List<string>(3);
                if (severe) parts.Add("severely injured");
                if (pinned) parts.Add("pinned down");
                if (demanded) parts.Add("told to stop");
                gate = string.Join(", ", parts);
            }
            else if (reasons < Settings.RequiredFactors.Value)
            {
                eligible = false;
                gate = $"not eligible: {reasons} reason(s), needs {Settings.RequiredFactors.Value}";
            }
            else if (pressure < threshold)
            {
                eligible = false;
                gate = $"not eligible: pressure {pressure:0.00} < {threshold:0.00} for a {rc}";
            }
            else
            {
                eligible = true;
                gate = $"{reasons} reasons";
            }

            if (!eligible && pressure < LogFloor && !Settings.VerboseLogs.Value) return;

            WildSpawnType role = bot.Profile.Info.Settings.Role;
            string personality = SainAdapter.Personality(owner);
            float persMult = SainAdapter.Multiplier(personality);
            float kitMult = h.KitValue < 0f ? 1f : 1f + Settings.KitValueWeight.Value * Mathf.Clamp01(h.KitValue / Settings.KitValueFull.Value);
            float experience = Experience(bot, owner);
            float expMult = 1f - Settings.ExperienceComposure.Value * experience;
            float severeMult = severe ? Settings.SevereChanceBoost.Value : 1f;
            float demandMult = demanded ? Settings.DemandChanceBoost.Value : 1f;
            float nerve = Settings.EffectiveNerve(h.Nerve);
            // The pressure curve: 0 at CurveStart, 1 at full pressure.
            float curve = Mathf.Clamp01((pressure - Settings.CurveStart.Value) / Mathf.Max(0.05f, 1f - Settings.CurveStart.Value));
            float chance = Mathf.Min(roleMax,
                roleBase * curve * (1f - Settings.NerveResistance.Value * nerve) * persMult * kitMult * expMult * severeMult * demandMult);
            chance = Mathf.Clamp01(chance);
            float roll = Random.value;
            bool gives = eligible && roll < chance;
            if (demanded) h.DemandUntil = 0f;   // one evaluation per demand: it never stacks

            var sb = new StringBuilder();
            sb.Append($"{bot.Profile?.Nickname} ({role}, lvl {bot.Profile?.Info?.Level}{(string.IsNullOrEmpty(personality) ? "" : ", " + personality)}) ");
            sb.Append($"vs {enemy.Profile?.Nickname} {dist:0} m{(seenNow ? "" : " (just lost sight)")}: pressure {pressure:0.00} [");
            sb.Append($"hurt {injury:0.00} ({injuryWhy}), ammo {ammo:0}, ");
            sb.Append($"fire {suppression:0.00} (near misses/hits {shots:0.0} = {ownFire:0.00}, SAIN {sainFire:0.00}, armour x{armourDull:0.00}), ");
            sb.Append($"outnumbered {outnumbered:0.00} ({hostiles} vs {friends}), squad {squad:0.00} ({squadWhy})]; {gate}; ");
            sb.Append($"kit {(h.KitValue < 0f ? "n/a" : (h.KitValue / 1000f).ToString("0") + "k")} x{kitMult:0.00}, armour class {h.Protection * 6f:0}, ");
            sb.Append($"experience {experience:0.00} x{expMult:0.00}, personality x{persMult:0.00}, {rc} base {roleBase:0.00} x curve {curve:0.00}, nerve {nerve:0.00}");
            if (severe) sb.Append($", severe x{severeMult:0.0}");
            if (demanded) sb.Append($", demand x{demandMult:0.0}");
            sb.Append($"; chance {chance:0.000} (max {roleMax:0.00}), roll {roll:0.000}");
            sb.Append(gives ? " -> surrenders" : eligible ? " -> holds on" : " -> no roll");
            HandsAreHighPlugin.Log.LogInfo(sb.ToString());

            if (!gives) return;
            SurrenderRegistry.Surrender(bot, enemy, $"gave up ({gate}, pressure {pressure:0.00}, chance {chance:0.000})");
            HandsAreHighPlugin.Log.LogInfo(SurrenderRegistry.HoldsBot(bot)
                ? $"{bot.Profile?.Nickname}: surrender activated (layer will take him on its next brain update)"
                : $"{bot.Profile?.Nickname}: surrender activation FAILED (mod disabled or registry refused)");
        }

        // Why a bot under pressure isn't even considered: logged for any bot carrying pressure
        // (fire or injury), so a quiet log means nothing was pressing on anyone.
        private static void Blocked(Player bot, BotHistory h, string why)
        {
            if (!Settings.VerboseLogs.Value && Suppression.Decayed(h, Time.time) < 2f && Arsenal.HealthFraction(bot) > Settings.InjuryThreshold.Value) return;
            HandsAreHighPlugin.Log.LogInfo($"{bot.Profile?.Nickname} ({bot.Profile?.Info?.Settings?.Role}): no surrender check: {why}");
        }

        // The worst of several views of the same wounds (never a sum).
        private static float Injury(Player bot, out string why)
        {
            why = "none";
            IHealthController hc = bot.HealthController;
            if (hc == null) return 0f;
            float worst = 0f;

            float health = Arsenal.HealthFraction(bot);
            float thr = Settings.InjuryThreshold.Value;
            if (health < thr)
                Worst(ref worst, ref why, 0.3f + 0.7f * (thr - health) / thr, $"health {health:P0}");

            float vital = Settings.VitalThreshold.Value;
            foreach (EBodyPart part in new[] { EBodyPart.Head, EBodyPart.Chest })
            {
                var p = hc.GetBodyPartHealth(part);
                float f = p.Maximum > 0f ? p.Current / p.Maximum : 1f;
                if (f < vital) Worst(ref worst, ref why, 0.4f + 0.6f * (vital - f) / vital, $"{part} {f:P0}");
            }

            foreach (EBodyPart part in Limbs)
                if (hc.IsBodyPartDestroyed(part)) Worst(ref worst, ref why, part == EBodyPart.Stomach ? 0.6f : 0.45f, $"{part} destroyed");

            foreach (EBodyPart part in Parts)
            {
                if (hc.FindActiveEffect<IHeavyBleeding>(part) != null) { Worst(ref worst, ref why, 0.5f, "heavy bleeding"); break; }
                if (hc.FindActiveEffect<ILightBleeding>(part) != null) Worst(ref worst, ref why, 0.25f, "light bleeding");
            }
            return Mathf.Clamp01(worst);
        }

        private static void Worst(ref float worst, ref string why, float value, string what)
        {
            if (value <= worst) return;
            worst = value;
            why = what;
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
