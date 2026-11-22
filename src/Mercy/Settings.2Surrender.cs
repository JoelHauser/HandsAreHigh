using BepInEx.Configuration;
using UnityEngine;

namespace Mercy
{
    // F12 section "2. Surrender": when and how readily bots give up.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<float> ScavChance;
        internal static ConfigEntry<float> PmcChance;
        internal static ConfigEntry<float> BossChance;
        internal static ConfigEntry<bool> LoneBossSurrender;
        internal static ConfigEntry<float> LoneBossChance;
        internal static ConfigEntry<float> BotSurrenderScale;
        internal static ConfigEntry<int> RequiredFactors;
        internal static ConfigEntry<float> SurrenderCooldown;
        internal static ConfigEntry<bool> BoltWhenShot;
        internal static ConfigEntry<float> HitGrace;
        internal static ConfigEntry<bool> FastHolster;
        internal static ConfigEntry<bool> FinishReloadInstantly;
        internal static ConfigEntry<float> NerveMin;
        internal static ConfigEntry<float> NerveMax;
        internal static ConfigEntry<float> HighNerve;
        internal static ConfigEntry<float> EvalInterval;
        internal static ConfigEntry<float> PromptCheckDelay;
        internal static ConfigEntry<float> SeenWithin;
        internal static ConfigEntry<float> NerveResistance;
        internal static ConfigEntry<float> CurveStart;
        internal static ConfigEntry<float> ScavThreshold;
        internal static ConfigEntry<float> ScavMax;
        internal static ConfigEntry<float> PmcThreshold;
        internal static ConfigEntry<float> PmcMax;
        internal static ConfigEntry<float> BossThreshold;
        internal static ConfigEntry<float> BossMax;
        internal static ConfigEntry<float> PersonalityMin;
        internal static ConfigEntry<float> PersonalityMax;
        internal static ConfigEntry<float> ArmourConfidence;
        internal static ConfigEntry<float> KitValueWeight;
        internal static ConfigEntry<float> KitValueFull;
        internal static ConfigEntry<float> ExperienceComposure;
        internal static ConfigEntry<float> KitRecheck;
        internal static ConfigEntry<float> InjuryThreshold;
        internal static ConfigEntry<float> VitalThreshold;
        internal static ConfigEntry<float> SevereInjury;
        internal static ConfigEntry<float> SeverePressure;
        internal static ConfigEntry<float> SevereChanceBoost;
        internal static ConfigEntry<float> InjuryWeight;
        internal static ConfigEntry<float> AmmoWeight;
        internal static ConfigEntry<float> SuppressionWeight;
        internal static ConfigEntry<float> SuppressionHalfLife;
        internal static ConfigEntry<float> SustainedFireEvents;
        internal static ConfigEntry<float> PinnedLevel;
        internal static ConfigEntry<float> PinnedPressure;
        internal static ConfigEntry<float> NearMissRadius;
        internal static ConfigEntry<float> NearMissRange;
        internal static ConfigEntry<float> OutnumberedWeight;
        internal static ConfigEntry<float> OutnumberedRatio;
        internal static ConfigEntry<float> EnemyMemory;
        internal static ConfigEntry<float> SquadWeight;
        internal static ConfigEntry<float> SquadMemory;
        internal static ConfigEntry<float> AloneFactor;
        internal static ConfigEntry<float> AllyRadius;
        internal static ConfigEntry<float> AcknowledgeDistance;
        internal static ConfigEntry<float> OfferMin;
        internal static ConfigEntry<float> OfferMax;
        internal static ConfigEntry<float> DropRetrySeconds;

        private static void BindSurrender(ConfigFile c)
        {
            ScavChance = c.Bind(S2, "Scav base chance", 0.35f,
                Range("How readily Scavs give up: at full pressure an average Scav surrenders about 1 check in 6. Scaled by pressure, nerve, personality, kit and experience.", 0f, 1f, false));
            PmcChance = c.Bind(S2, "PMC base chance", 0.12f,
                Range("How readily PMCs give up: about a third of a Scav's odds.", 0f, 1f, false));
            BossChance = c.Bind(S2, "Boss base chance", 0.03f,
                Range("Boss followers; only with Bosses can surrender on (section 1). Bosses themselves use the last survivor settings below.", 0f, 1f, true));
            LoneBossSurrender = c.Bind(S2, "Boss last survivor can surrender", true,
                Text("A boss never gives up while any of his guards may still be alive (far away, asleep or despawned counts as alive). Once every guard is confirmed dead he may consider it. A boss who spawned alone never does. Needs Bosses can surrender on (section 1).", true));
            LoneBossChance = c.Bind(S2, "Boss last survivor chance", 0.3f,
                Range("Highest chance of a lone boss giving up, at full stress (badly hurt, pinned down, out of ammo); nerve, personality and experience lower it. He weighs it at most 3 times per raid, each time only if he is clearly worse off than when he last refused.", 0f, 1f, true));
            BotSurrenderScale = c.Bind(S2, "Chance against bots", 0.85f,
                Range("Multiplies the chance of giving up when the threat is a bot instead of you (you: 1).", 0f, 2f, true));
            RequiredFactors = c.Bind(S2, "Reasons needed", 2,
                IntRange("How many separate reasons must apply at once (injury, ammo, fire, being outnumbered, losing his squad). Severe injury, being pinned down or a Stop demand count on their own.", 1, 5, true));
            SurrenderCooldown = c.Bind(S2, "Cooldown after a surrender (s)", 30f,
                Range("Once he is fighting again, how long before he can give up again.", 0f, 300f, false));
            BoltWhenShot = c.Bind(S2, "Bolt when shot", true,
                Text("Hit while his hands are up, by anyone: he runs.", false));
            HitGrace = c.Bind(S2, "Hits ignored after giving up (s)", 2f,
                Range("A man hit this soon after dropping his gun was shot by someone who hadn't seen his hands go up yet: he stays down instead of bolting. Later hits still make him run.", 0f, 6f, true));
            FastHolster = c.Bind(S2, "Fast weapon drop", true,
                Text("Empties his hands with the game's quick holster before throwing the gun. Off: the normal holster animation.", true));
            FinishReloadInstantly = c.Bind(S2, "Cut a reload short", true,
                Text("A reload in progress would hold up the drop. On: it is finished at once (as the game does for bots) and the gun drops. Off: he finishes the reload first.", true));
            NerveMin = c.Bind(S2, "Nerve min", 0f,
                Range("Each bot rolls his nerve once per raid between min and max. High nerve: slower to give up, quicker to try something.", 0f, 1f, true));
            NerveMax = c.Bind(S2, "Nerve max", 1f,
                Range("Upper end of the nerve roll.", 0f, 1f, true));
            HighNerve = c.Bind(S2, "High nerve from", 0.75f,
                Range("Bots at or above this nerve come back with only a knife if they find no gun.", 0f, 1f, true));
            EvalInterval = c.Bind(S2, "Check every (s)", 3f,
                Range("How often each bot in a fight weighs giving up (a hit or near miss brings the next check forward).", 1f, 60f, true));
            PromptCheckDelay = c.Bind(S2, "Recheck after a hit (s)", 0.25f,
                Range("Being hit or shot at brings his next check forward to this soon (at most every 0.8 s).", 0.1f, 5f, true));
            SeenWithin = c.Bind(S2, "Threat seen within (s)", 3f,
                Range("He only gives up to a threat he sees now, saw this recently, or is under fire from while he knows of him.", 0f, 15f, true));
            NerveResistance = c.Bind(S2, "Nerve resistance", 0.85f,
                Range("How much nerve holds him back. 1: a bot with full nerve never gives up.", 0f, 1f, true));
            CurveStart = c.Bind(S2, "Chance starts at pressure", 0.3f,
                Range("Below this pressure the chance is 0; it rises to the full base chance at pressure 1.", 0f, 0.9f, true));
            ScavThreshold = c.Bind(S2, "Scav pressure needed", 0.4f,
                Range("Pressure a Scav needs (with enough reasons) before he rolls at all.", 0f, 1f, true));
            ScavMax = c.Bind(S2, "Scav max chance", 0.5f,
                Range("Cap on a Scav's chance per check, after everything.", 0f, 1f, true));
            PmcThreshold = c.Bind(S2, "PMC pressure needed", 0.5f,
                Range("Pressure a PMC needs before he rolls at all.", 0f, 1f, true));
            PmcMax = c.Bind(S2, "PMC max chance", 0.25f,
                Range("Cap on a PMC's chance per check.", 0f, 1f, true));
            BossThreshold = c.Bind(S2, "Boss pressure needed", 0.75f,
                Range("Pressure a boss or follower needs before he rolls at all.", 0f, 1f, true));
            BossMax = c.Bind(S2, "Boss max chance", 0.08f,
                Range("Cap on a boss's or follower's chance per check.", 0f, 1f, true));
            PersonalityMin = c.Bind(S2, "Personality multiplier min", 0.5f,
                Range("Applied to the bravest SAIN personalities.", 0f, 3f, true));
            PersonalityMax = c.Bind(S2, "Personality multiplier max", 1.5f,
                Range("Applied to Cowards.", 0f, 3f, true));
            ArmourConfidence = c.Bind(S2, "Armour confidence", 0.3f,
                Range("How much class 6 armour dulls the fear of incoming fire and being outnumbered. Never removes it.", 0f, 0.9f, true));
            KitValueWeight = c.Bind(S2, "Kit value weight", 0.25f,
                Range("A man with expensive kit has more to lose: up to this much more willing to give up.", 0f, 1f, true));
            KitValueFull = c.Bind(S2, "Kit value full at (roubles)", 250000f,
                Range("Handbook value of everything he carries for the full kit value weight.", 10000f, 3000000f, true));
            ExperienceComposure = c.Bind(S2, "Experience composure", 0.35f,
                Range("Level and difficulty make him calmer: up to this much less likely to give up. Never immune.", 0f, 0.8f, true));
            KitRecheck = c.Bind(S2, "Re-value kit every (s)", 30f,
                Range("How often the value of a bot's kit is looked up again.", 5f, 300f, true));
            InjuryThreshold = c.Bind(S2, "Hurt below (health)", 0.6f,
                Range("Health fraction where injury starts to count (a little at first, fully near death).", 0.05f, 1f, true));
            VitalThreshold = c.Bind(S2, "Head or thorax below (health)", 0.6f,
                Range("Injury also counts a badly hurt head or thorax, a destroyed limb and bleeding; the worst of these is used.", 0.05f, 1f, true));
            SevereInjury = c.Bind(S2, "Severe injury from", 0.6f,
                Range("Injury this bad is a reason on its own (still a roll, still nerve and personality).", 0.1f, 1.1f, true));
            SeverePressure = c.Bind(S2, "Severe injury pressure floor", 0.6f,
                Range("Pressure is at least this while he is severely injured.", 0f, 1f, true));
            SevereChanceBoost = c.Bind(S2, "Severe injury chance boost", 2f,
                Range("Multiplies the chance per check while severely injured.", 1f, 5f, true));
            InjuryWeight = c.Bind(S2, "Weight: injury", 0.45f,
                Range("How much injury adds to pressure.", 0f, 1f, true));
            AmmoWeight = c.Bind(S2, "Weight: out of ammo", 0.35f,
                Range("No rounds left for his gun and none to reload, or no gun at all.", 0f, 1f, true));
            SuppressionWeight = c.Bind(S2, "Weight: suppression", 0.40f,
                Range("Incoming fire: hits and shots passing close to him.", 0f, 1f, true));
            SuppressionHalfLife = c.Bind(S2, "Suppression half-life (s)", 4f,
                Range("How fast the effect of incoming fire fades.", 0.5f, 30f, true));
            SustainedFireEvents = c.Bind(S2, "Full suppression at (shots)", 6f,
                Range("Recent shots at him for the full suppression weight. One stray shot counts for little.", 1f, 30f, true));
            PinnedLevel = c.Bind(S2, "Pinned down from", 0.8f,
                Range("Suppression this heavy is a reason on its own, like severe injury. 1.1 turns it off.", 0.1f, 1.1f, true));
            PinnedPressure = c.Bind(S2, "Pinned down pressure floor", 0.5f,
                Range("Pressure is at least this while he is pinned down.", 0f, 1f, true));
            NearMissRadius = c.Bind(S2, "Near miss within (m)", 2f,
                Range("An enemy shot passing this close to him (nothing solid in between) counts as fire at him; closer counts more.", 0.25f, 6f, true));
            NearMissRange = c.Bind(S2, "Near miss checked up to (m)", 150f,
                Range("Shots from farther away than this are not checked for near misses.", 10f, 500f, true));
            OutnumberedWeight = c.Bind(S2, "Weight: outnumbered", 0.25f,
                Range("How much being outnumbered adds to pressure.", 0f, 1f, true));
            OutnumberedRatio = c.Bind(S2, "Fully outnumbered at", 2f,
                Range("Enemies per friendly (counting himself) for the full outnumbered weight.", 1.1f, 6f, true));
            EnemyMemory = c.Bind(S2, "Enemy counts for (s)", 10f,
                Range("An enemy he saw this recently counts toward being outnumbered.", 1f, 60f, true));
            SquadWeight = c.Bind(S2, "Weight: squad", 0.30f,
                Range("Squadmates killed recently, or his squad alive but none of them close.", 0f, 1f, true));
            SquadMemory = c.Bind(S2, "Squad loss counts for (s)", 30f,
                Range("How long a dead squadmate weighs on him.", 1f, 180f, true));
            AloneFactor = c.Bind(S2, "Squad too far away", 0.5f,
                Range("Squad factor when his squad is alive but nobody is within the ally radius.", 0f, 1f, true));
            AllyRadius = c.Bind(S2, "Allies within (m)", 40f,
                Range("Squadmates closer than this count as near him.", 5f, 150f, true));
            AcknowledgeDistance = c.Bind(S2, "Acknowledged within (m)", 25f,
                Range("You take his surrender by coming this close in sight, aiming at him for a moment, or giving an order. A bot takes it by taking custody.", 3f, 100f, true));
            OfferMin = c.Bind(S2, "Unanswered surrender min (s)", 15f,
                Range("Nobody has taken his surrender: he stays compliant this long (longer while someone closes in), then looks after himself.", 3f, 120f, true));
            OfferMax = c.Bind(S2, "Unanswered surrender max (s)", 35f,
                Range("Upper end of the unanswered wait.", 3f, 240f, true));
            DropRetrySeconds = c.Bind(S2, "Drop retry after (s)", 1f,
                Range("If the game hasn't emptied his hands by then, the drop is checked and asked again. After 3 tries he fights instead.", 0.3f, 5f, true));
        }
    }
}
