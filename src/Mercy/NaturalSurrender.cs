using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace Mercy
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
        // 0.19.0: 1.5 -> 0.8 s. A bot hit twice in a second used to get one check 0.5 s after the
        // first hit and none after the second: in an ordinary firefight he was dead before the
        // second check (natural surrender never seen in a raid; tools/natsim).
        private const float PromptGap = 0.8f;
        private const float LogFloor = 0.15f;      // pressure below this is only logged with Verbose logs
        private static readonly EBodyPart[] Vitals = { EBodyPart.Head, EBodyPart.Chest };
        private static readonly EBodyPart[] Limbs =
            { EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg, EBodyPart.Stomach };
        private static readonly EBodyPart[] Parts =
            { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };

        private const int MaxChecksPerFrame = 2;
        private const float PruneEvery = 30f;
        private static float _nextPrune;

        // Per frame for every AI: one reference-keyed dictionary lookup and a time compare
        // (role resolved once per bot). A full check runs every EvalInterval per bot, staggered,
        // and at most MaxChecksPerFrame per frame: when many come due together (a fight
        // starts, many hits), the rest wait a frame instead of piling into one.
        internal static void Tick()
        {
            if (!Settings.Enabled.Value || !Settings.NaturalSurrender.Value) return;
            GameWorld world = Singleton<GameWorld>.Instance;
            List<Player> all = world?.AllAlivePlayersList;
            if (all == null) return;

            float now = Time.time;
            if (now >= _nextPrune) { _nextPrune = now + PruneEvery; SurrenderRegistry.PruneGone(); }

            int budget = MaxChecksPerFrame;
            for (int i = 0; i < all.Count; i++)
            {
                Player bot = all[i];
                if (bot == null || !bot.IsAI) continue;
                BotHistory h = SurrenderRegistry.HistoryOf(bot);
                if (now < h.NextEval) continue;
                if (budget <= 0) break;

                // 0.29.2: a boss-type role is only resolved once his brain exists (HasOurLayers reads it);
                // resolving before would cache None for the raid.
                if ((int)h.Role < 0 && (bot.AIData?.BotOwner?.Brain?.BaseBrain != null
                                        || ClassOfRole(bot.Profile.Info.Settings.Role) != RoleClass.Boss))
                {
                    h.Role = ClassOf(bot);
                    h.SquadBoss = h.Role == RoleClass.Boss && BossSquad.IsSquadBoss(bot.Profile.Info.Settings.Role);
                }
                if (!EligibleRole(h.Role) || SurrenderRegistry.HoldsBot(bot))
                {
                    h.NextEval = now + Settings.EvalInterval.Value;
                    if (!EligibleRole(h.Role))
                    {
                        SurrenderStats.Gate(NaturalGate.Role);
                        if (Scenarios.Watching(bot)) Blocked(bot, $"role {h.Role} not eligible (01. General: PMCs / bosses)");
                    }
                    continue;
                }
                BotOwner owner = bot.AIData?.BotOwner;
                if (owner == null || owner.BotState != EBotState.Active || owner.IsDead)
                {
                    h.NextEval = now + Settings.EvalInterval.Value;
                    SurrenderStats.Gate(NaturalGate.NotActive);
                    continue;
                }

                budget--;
                h.NextEval = now + Settings.EvalInterval.Value * Dice.Range(0.8f, 1.2f);
                // Your teammates (your group, Pit Fireteam followers) stay under your orders.
                if (!Settings.TeammatesSurrender.Value && Teammates.IsPlayerTeammate(owner))
                {
                    SurrenderStats.Gate(NaturalGate.Teammate);
                    if (Scenarios.Watching(bot)) Blocked(bot, "your teammate (Your teammates can surrender is off)");
                    continue;
                }
                TrackSquad(owner, h);
                if (now < h.CooldownUntil)
                {
                    SurrenderStats.Gate(NaturalGate.Cooldown);
                    if (Scenarios.Watching(bot)) Blocked(bot, $"surrender cooldown {h.CooldownUntil - now:0} s");
                    continue;
                }
                // 0.21.0: the fire that broke a hold-up he was in is still coming (FireBreak).
                if (now < h.FireBreakUntil && Settings.FireBreaks.Value)
                {
                    SurrenderStats.Gate(NaturalGate.UnderBreakingFire);
                    if (Scenarios.Watching(bot)) Blocked(bot, $"still under the fire that broke a hold-up ({h.FireBreakUntil - now:0} s)");
                    continue;
                }
                try { Evaluate(bot, owner, h); }
                catch (System.Exception e) { MercyPlugin.Report("surrender check", e); }
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

        internal enum RoleClass { None, Scav, Pmc, Boss, Raider }

        // From the profile's WildSpawnType (and the game's own WildSpawnTypeExtension.IsBossOrFollower),
        // never from names. Boss covers bosses, followers and the game's other "boss or follower"
        // roles; within it only real bosses (BotHistory.SquadBoss, BossSquad.IsSquadBoss) need the
        // last-survivor rule. 0.29.0: Raiders (pmcBot) are their own class, tested BEFORE the boss
        // check: the game's table marks pmcBot isBoss AND isFollower (WildSpawnTypeExtension 0.16.9),
        // so IsBossOrFollower had filed them as bosses and kept them out unless bosses were on.
        // Their brain is "PMC" (PmcLayersStrategy), which always carries our layers.
        // 0.29.2: a Boss-class bot whose brain has none of our layers (Rogues, sectants, BTR gunner,
        // arena fighters) is None: he can't surrender, be held or guard (MercyPlugin.HasOurLayers).
        internal static RoleClass ClassOf(Player p)
        {
            RoleClass c = ClassOfRole(p.Profile.Info.Settings.Role);
            if (c == RoleClass.Boss && !MercyPlugin.HasOurLayers(p.AIData?.BotOwner)) return RoleClass.None;
            return c;
        }

        internal static RoleClass ClassOfRole(WildSpawnType role)
        {
            if (role == WildSpawnType.assault || role == WildSpawnType.cursedAssault) return RoleClass.Scav;
            if (role == WildSpawnType.pmcBEAR || role == WildSpawnType.pmcUSEC) return RoleClass.Pmc;
            if (role == WildSpawnType.pmcBot) return RoleClass.Raider;
            if (role.IsBossOrFollower()) return RoleClass.Boss;
            return RoleClass.None;
        }

        internal static bool Eligible(Player p) => EligibleRole(ClassOf(p));

        internal static bool EligibleRole(RoleClass c)
        {
            switch (c)
            {
                case RoleClass.Scav: return true;
                case RoleClass.Raider: return true;
                case RoleClass.Pmc: return Settings.IncludePmcs.Value;
                case RoleClass.Boss: return Settings.IncludeBosses.Value && MercyPlugin.BossLayersRegistered;
                default: return false;
            }
        }

        private static void RoleOdds(RoleClass c, out float baseChance, out float threshold, out float max)
        {
            switch (c)
            {
                // Raiders: PMC odds (trained men); experience and personality make them harder still.
                case RoleClass.Raider:
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
            if (h.SquadBoss) BossSquad.Track(owner, h);
        }

        private static void Evaluate(Player bot, BotOwner owner, BotHistory h)
        {
            // He gives up, at any range, to the enemy he is fighting (a human, or a hostile bot
            // with bot captors on) whom he sees now, saw a moment ago, or is under fire from while
            // he knows of him; or to the player whose Stop demand he heard (StopDemand checks range
            // and line when it is made: a spoken demand carries DemandRange, no further).
            float now = Time.time;
            SurrenderStats.Checked(bot);
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
                // The usual exits (no human target, far away): no message is built unless
                // Verbose logs are on.
                EnemyInfo goal = owner.Memory?.GoalEnemy;
                if (goal == null) { SurrenderStats.Gate(NaturalGate.NoTarget); if (Verbose(bot)) Blocked(bot, "no enemy targeted"); return; }
                Player foe = Perception.PlayerOf(goal.Person);
                if (foe == null || !Perception.Alive(foe)) { SurrenderStats.Gate(NaturalGate.TargetNotLiving); if (Verbose(bot)) Blocked(bot, "his target isn't a living player"); return; }
                if (foe.IsAI)
                {
                    string no = BotFoeBlocked(bot, foe);
                    if (no != null) { SurrenderStats.Gate(NaturalGate.BotTarget); if (Verbose(bot)) Blocked(bot, no); return; }
                }
                enemy = foe;
                seenNow = goal.IsVisible;
                // A credible threat he perceives, at any range (0.9.0: no distance limit): in
                // sight, seen a moment ago, or fire coming in while he knows of this enemy (the
                // game's own enemy memory). A man pinned behind cover still knows you're there;
                // a man who only remembers you from a minute ago doesn't give up to you.
                float since = now - goal.PersonalLastSeenTime;
                bool fire = now - h.FireAt < 3f || (owner.Memory != null && owner.Memory.IsUnderFire);
                if (!seenNow && since > Settings.SeenWithin.Value && !(fire && goal.HaveSeen && since < Settings.EnemyMemory.Value))
                {
                    SurrenderStats.Gate(NaturalGate.NotPerceived);
                    if (Verbose(bot)) Blocked(bot, $"hasn't seen {Restraint.Name(foe)} for {since:0} s and isn't under fire");
                    return;
                }
            }
            bool botFoe = enemy.IsAI;
            // Distance is logged, never a gate. Where he gives up towards is where he knows the
            // threat to be (in sight: exactly; else the EnemyInfo's last sighting), never more.
            float dist = Vector3.Distance(enemy.Position, bot.Position);
            Vector3 knownAt = demanded ? enemy.Position : Perception.KnownPosition(owner, enemy, out _);

            // Mid weapon change: wait for the next check. (Mid-reload is fine: the surrender
            // cuts the reload short and drops the gun.)
            if (owner.WeaponManager?.Selector?.IsChanging == true) { SurrenderStats.Gate(NaturalGate.WeaponChange); return; }

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
            float bossStress = 0f;
            if (h.SquadBoss)
            {
                // 0.29.0: a boss never gives up under the ordinary rules, only as the last survivor
                // of his squad, and then on his own state (not the squad loss itself).
                bossStress = LoneBossStress(injury, suppression, ammo);
                eligible = LoneBossGate(h, bossStress, out gate);
            }
            else if (severe || pinned || (demanded && pressure >= threshold * 0.75f))
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
                SurrenderStats.Gate(NaturalGate.TooFewReasons);
                gate = $"not eligible: {reasons} reason(s), needs {Settings.RequiredFactors.Value}";
            }
            else if (pressure < threshold)
            {
                eligible = false;
                SurrenderStats.Gate(NaturalGate.LowPressure);
                gate = $"not eligible: pressure {pressure:0.00} < {threshold:0.00} for a {rc}";
            }
            else
            {
                eligible = true;
                gate = $"{reasons} reasons";
            }

            // Checks that can't lead to a roll are only logged with Verbose logs (they repeat
            // every few seconds for every bot in a fight); a roll is always logged.
            if (!eligible && !(Settings.VerboseLogs.Value && pressure >= LogFloor) && !Scenarios.Watching(bot)) return;

            WildSpawnType role = bot.Profile.Info.Settings.Role;
            string personality = SurrenderRegistry.PersonalityOf(bot, h);
            float persMult = SainAdapter.Multiplier(personality);
            float kitMult = h.KitValue < 0f ? 1f : 1f + Settings.KitValueWeight.Value * Mathf.Clamp01(h.KitValue / Settings.KitValueFull.Value);
            float experience = Experience(bot, owner);
            float expMult = 1f - Settings.ExperienceComposure.Value * experience;
            float severeMult = severe ? Settings.SevereChanceBoost.Value : 1f;
            float demandMult = demanded ? Settings.DemandChanceBoost.Value : 1f;
            // A bot threat: scaled (BotSurrenderScale), and only as credible as his hold on him:
            // a gun in his hands with rounds in it, else far less.
            float botMult = 1f;
            string credible = null;
            if (botFoe)
            {
                BotOwner fo = enemy.AIData?.BotOwner;
                bool gun = enemy.HandsController is Player.FirearmController && fo != null && !Arsenal.OutOfAmmo(fo);
                botMult = Settings.BotSurrenderScale.Value * (gun ? 1f : 0.35f);
                credible = gun ? "armed" : "not credibly armed";
            }
            float nerve = Settings.EffectiveNerve(h.Nerve);
            // The pressure curve: 0 at CurveStart, 1 at full pressure.
            float curve = Mathf.Clamp01((pressure - Settings.CurveStart.Value) / Mathf.Max(0.05f, 1f - Settings.CurveStart.Value));
            float chance = Mathf.Min(roleMax,
                roleBase * curve * (1f - Settings.NerveResistance.Value * nerve) * persMult * kitMult * expMult * severeMult * demandMult * botMult);
            if (h.SquadBoss)
            {
                roleBase = roleMax = Settings.LoneBossChance.Value;
                chance = LoneBossChanceFor(bossStress, nerve, persMult * expMult * botMult, out curve);
            }
            chance = Mathf.Clamp01(chance);
            float roll = Dice.Value;
            bool gives = eligible && roll < chance;
            if (eligible) SurrenderStats.Rolled(rc, gives);
            if (eligible && h.SquadBoss) { h.LoneBossRolls++; h.LoneBossStress = bossStress; }
            if (demanded) h.DemandUntil = 0f;   // one evaluation per demand: it never stacks

            var sb = new StringBuilder();
            sb.Append($"{Restraint.Name(bot)} ({role}, lvl {bot.Profile?.Info?.Level}{(string.IsNullOrEmpty(personality) ? "" : ", " + personality)}) ");
            sb.Append($"vs {Restraint.Name(enemy)}{(botFoe ? $" (bot {enemy.Profile?.Info?.Settings?.Role}, {credible})" : "")} {dist:0} m{(seenNow ? "" : $" (not in sight; believes him {Vector3.Distance(knownAt, enemy.Position):0} m from where he is)")}: pressure {pressure:0.00} [");
            sb.Append($"hurt {injury:0.00} ({injuryWhy}), ammo {ammo:0}, ");
            sb.Append($"fire {suppression:0.00} (near misses/hits {shots:0.0} = {ownFire:0.00}, SAIN {sainFire:0.00}, armour x{armourDull:0.00}), ");
            sb.Append($"outnumbered {outnumbered:0.00} ({hostiles} vs {friends}), squad {squad:0.00} ({squadWhy})]; {gate}; ");
            sb.Append($"kit {(h.KitValue < 0f ? "n/a" : (h.KitValue / 1000f).ToString("0") + "k")} x{kitMult:0.00}, armour class {h.Protection * 6f:0}, ");
            sb.Append($"experience {experience:0.00} x{expMult:0.00}, personality x{persMult:0.00}, {rc} base {roleBase:0.00} x curve {curve:0.00}, nerve {nerve:0.00}");
            if (h.SquadBoss) sb.Append($", boss stress {bossStress:0.00}, last-survivor rolls {h.LoneBossRolls}/{LoneBossMaxRolls}");
            if (severe) sb.Append($", severe x{severeMult:0.0}");
            if (demanded) sb.Append($", demand x{demandMult:0.0}");
            if (botFoe) sb.Append($", bot threat x{botMult:0.00}");
            sb.Append($"; chance {chance:0.000} (max {roleMax:0.00}), roll {roll:0.000}");
            sb.Append(gives ? " -> surrenders" : eligible ? " -> holds on" : " -> no roll");
            MercyPlugin.Log.LogInfo(sb.ToString());

            if (!gives) return;
            SurrenderRegistry.Surrender(bot, enemy, $"gave up ({gate}, pressure {pressure:0.00}, chance {chance:0.000}, {dist:0} m)", knownAt,
                source: Scenarios.Watching(bot) ? SurrenderSource.ScenarioNatural : demanded ? SurrenderSource.Demand : SurrenderSource.Natural);
            bool held = SurrenderRegistry.HoldsBot(bot);
            SurrenderStats.Activation(held);
            MercyPlugin.Log.LogInfo(held
                ? $"{Restraint.Name(bot)}: surrender activated ({(Scenarios.Watching(bot) ? "natural decision, scenario bot" : "NATURAL")}; layer will take him on its next brain update)"
                : $"{Restraint.Name(bot)}: surrender activation FAILED (mod disabled or registry refused)");
        }

        // A bot he's fighting can only be the one he gives up to if bot captors are on, that bot
        // isn't held himself, is active, and his own group really counts him as an enemy
        // (BotsGroup.IsEnemy: verified relationship, not faction). A player ally's bot only
        // with "Your allies make bots surrender".
        private static string BotFoeBlocked(Player bot, Player foe)
        {
            if (!Settings.BotCaptors.Value) return "his target is a bot (bot captors off)";
            if (SurrenderRegistry.HoldsBot(foe)) return "his target is a prisoner himself";
            BotOwner fo = foe.AIData?.BotOwner;
            if (fo == null || fo.IsDead || fo.BotState != EBotState.Active) return "his target isn't an active bot";
            if (fo.BotsGroup == null || !fo.BotsGroup.IsEnemy(bot)) return "his target's group doesn't count him as an enemy";
            if (!Settings.AlliedCaptors.Value && Captors.IsPlayerAlly(fo)) return "his target is your ally (allies can't make bots surrender)";
            return null;
        }

        // Why a bot isn't even considered (Verbose logs only: in a long fight between bots
        // this fired every few seconds per bot).
        // Debug scenario participants: every check is logged (and so reaches the scenario timeline).
        private static bool Verbose(Player bot) => Settings.VerboseLogs.Value || Scenarios.Watching(bot);

        private static void Blocked(Player bot, string why) =>
            MercyPlugin.Log.LogInfo($"{Restraint.Name(bot)} ({bot.Profile?.Info?.Settings?.Role}): no surrender check: {why}");

        // The worst of several views of the same wounds (never a sum).
        // 0.19.0: a destroyed limb 0.45 -> 0.55, stomach 0.6 -> 0.65, bleeding 0.5/0.25 -> 0.55/0.3
        // (with the thresholds at 60%): a man with a shattered leg who is bleeding is a reason on
        // his own; before, nothing short of near-death was (0.3.0 raid log, tools/natsim).
        internal static float Injury(Player bot, out string why)
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
            foreach (EBodyPart part in Vitals)
            {
                var p = hc.GetBodyPartHealth(part);
                float f = p.Maximum > 0f ? p.Current / p.Maximum : 1f;
                if (f < vital) Worst(ref worst, ref why, 0.4f + 0.6f * (vital - f) / vital, $"{part} {f:P0}");
            }

            foreach (EBodyPart part in Limbs)
                if (hc.IsBodyPartDestroyed(part)) Worst(ref worst, ref why, part == EBodyPart.Stomach ? 0.65f : 0.55f, $"{part} destroyed");

            foreach (EBodyPart part in Parts)
            {
                if (hc.FindActiveEffect<IHeavyBleeding>(part) != null) { Worst(ref worst, ref why, 0.55f, "heavy bleeding"); break; }
                if (hc.FindActiveEffect<ILightBleeding>(part) != null) Worst(ref worst, ref why, 0.3f, "light bleeding");
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

        // 0.29.0 boss last-survivor rule. A few rolls per raid, each needing his state to be
        // clearly worse than at his last refusal, so waiting him out never makes it a certainty.
        private const int LoneBossMaxRolls = 3;
        private const float LoneBossStep = 0.15f;
        private const float LoneBossFloor = 0.3f;

        // Injury, incoming fire (armour-dulled) and an empty gun, combined like independent
        // blows: any one can carry it, none adds past 1. The squad loss isn't in it: that is
        // what makes him eligible, not what breaks him.
        internal static float LoneBossStress(float injury, float suppression, float ammo) =>
            Mathf.Clamp01(1f - (1f - injury) * (1f - 0.8f * suppression) * (1f - 0.6f * ammo));

        // His own chance setting, scaled by his state (0.25 at the floor, 1 at full stress), nerve
        // (the ordinary Nerve resistance) and the rest (personality x experience x bot threat);
        // never above the setting.
        internal static float LoneBossChanceFor(float stress, float nerve, float mult, out float curve)
        {
            curve = 0.25f + 0.75f * Mathf.Clamp01((stress - LoneBossFloor) / (1f - LoneBossFloor));
            float max = Settings.LoneBossChance.Value;
            return Mathf.Clamp(max * curve * (1f - Settings.NerveResistance.Value * nerve) * mult, 0f, max);
        }

        internal static bool LoneBossGate(BotHistory h, float stress, out string gate)
        {
            int roster = BossSquad.RosterSize(h);
            if (!Settings.LoneBossSurrender.Value)
            {
                SurrenderStats.Gate(NaturalGate.BossNotLastSurvivor);
                gate = "boss: never gives up (Boss last survivor can surrender is off)";
                return false;
            }
            if (roster == 0)
            {
                SurrenderStats.Gate(NaturalGate.BossNotLastSurvivor);
                gate = "boss: no squad seen this raid (alone from the start), never gives up";
                return false;
            }
            int left = BossSquad.NotConfirmedDead(h);
            if (left > 0)
            {
                SurrenderStats.Gate(NaturalGate.BossNotLastSurvivor);
                gate = $"boss: {left} of {roster} guard(s) not confirmed dead";
                return false;
            }
            if (h.LoneBossRolls >= LoneBossMaxRolls)
            {
                SurrenderStats.Gate(NaturalGate.BossNoRollDue);
                gate = $"boss: last survivor, refused {h.LoneBossRolls} times already";
                return false;
            }
            if (stress < LoneBossFloor)
            {
                SurrenderStats.Gate(NaturalGate.BossNoRollDue);
                gate = $"boss: last survivor, stress {stress:0.00} < {LoneBossFloor:0.00}";
                return false;
            }
            // 0.29.2: capped at full stress (Lighthouse raid: a first roll at 0.86 asked for 1.01, which
            // can't happen, so he never rolled again); at the cap he must still be worse than before.
            float needs = Mathf.Min(h.LoneBossStress + LoneBossStep, 1f);
            if (h.LoneBossRolls > 0 && (stress < needs || stress <= h.LoneBossStress))
            {
                SurrenderStats.Gate(NaturalGate.BossNoRollDue);
                gate = $"boss: last survivor, no worse than at his last refusal (stress {stress:0.00}, needs {needs:0.00})";
                return false;
            }
            gate = $"boss: last survivor ({roster} guard(s) confirmed dead), roll {h.LoneBossRolls + 1} of {LoneBossMaxRolls}";
            return true;
        }

        // 0..1 from level (0 at 1, 1 at 40+) and difficulty (easy 0 .. impossible 1).
        // 0.19.0: a Scav's level is not his experience: SPT rolls Scav levels around the player's
        // own (the 0.3.0 raid log: Scavs at 48-78 against a high-level player, every one of them
        // "experienced", x0.79 on every roll). Scavs: difficulty only.
        internal static float Experience(Player bot, BotOwner owner)
        {
            int level = bot.Profile?.Info?.Level ?? 1;
            float lvl = ClassOf(bot) == RoleClass.Scav ? 0f : Mathf.Clamp01((level - 1) / 39f);
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
                if (ScenarioShield.Active && ScenarioShield.Blocks(owner.GetPlayer, e.Person)) continue;
                if (Truce.Active && Truce.Blocks(owner.GetPlayer, e.Person)) continue;   // 0.28.0
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
