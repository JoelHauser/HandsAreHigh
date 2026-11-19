using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // Every tuning value, in numbered F12 sections. All of it applies live except the
    // layer priority, which BigBrain only reads once at startup.
    // Section and key names must never contain = \ " ' [ ] (BepInEx throws; pack.ps1 checks).
    internal static class Settings
    {
        private const string General = "01. General";
        private const string NerveSec = "02. Nerve";
        private const string Natural = "03. Surrendering on their own";
        private const string Pressure = "04. Pressure";
        private const string Role = "05. Role and personality";
        private const string Coverage = "06. While held";
        private const string Opening = "06. Taking an opening";
        private const string Retreat = "07. Retreat and recapture";
        private const string Recovery = "08. Regroup and rearm";
        private const string Hiding = "09. Hiding and movement";
        private const string Interact = "10. Orders";
        private const string Demand = "10. Stop demand";
        private const string Feedback = "11. Reactions and voice";
        private const string Pose = "12. Arm pose";
        private const string Compat = "13. Other mods";
        private const string Debug = "14. Debug";

        // 01
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> NaturalSurrender;
        internal static ConfigEntry<bool> IncludePmcs;
        internal static ConfigEntry<bool> IncludeBosses;
        internal static ConfigEntry<int> LayerPriority;
        // 02
        internal static ConfigEntry<float> NerveMin;
        internal static ConfigEntry<float> NerveMax;
        internal static ConfigEntry<float> HighNerve;
        // 03
        internal static ConfigEntry<float> EvalInterval;
        internal static ConfigEntry<float> SurrenderCooldown;
        internal static ConfigEntry<float> SurrenderDistance;
        internal static ConfigEntry<float> SeenWithin;
        internal static ConfigEntry<int> RequiredFactors;
        internal static ConfigEntry<float> NerveResistance;
        internal static ConfigEntry<float> CurveStart;
        // 04
        internal static ConfigEntry<float> InjuryThreshold;
        internal static ConfigEntry<float> InjuryWeight;
        internal static ConfigEntry<float> AmmoWeight;
        internal static ConfigEntry<float> SuppressionWeight;
        internal static ConfigEntry<float> SuppressionHalfLife;
        internal static ConfigEntry<float> SustainedFireEvents;
        internal static ConfigEntry<float> OutnumberedWeight;
        internal static ConfigEntry<float> OutnumberedRatio;
        internal static ConfigEntry<float> EnemyMemory;
        internal static ConfigEntry<float> SquadWeight;
        internal static ConfigEntry<float> SquadMemory;
        internal static ConfigEntry<float> AloneFactor;
        internal static ConfigEntry<float> AllyRadius;
        internal static ConfigEntry<float> VitalThreshold;
        internal static ConfigEntry<float> SevereInjury;
        internal static ConfigEntry<float> SeverePressure;
        internal static ConfigEntry<float> SevereChanceBoost;
        internal static ConfigEntry<float> PinnedLevel;
        internal static ConfigEntry<float> PinnedPressure;
        internal static ConfigEntry<float> NearMissRadius;
        internal static ConfigEntry<float> NearMissRange;
        internal static ConfigEntry<float> PromptCheckDelay;
        // 05
        internal static ConfigEntry<float> ScavChance;
        internal static ConfigEntry<float> ScavThreshold;
        internal static ConfigEntry<float> ScavMax;
        internal static ConfigEntry<float> PmcChance;
        internal static ConfigEntry<float> PmcThreshold;
        internal static ConfigEntry<float> PmcMax;
        internal static ConfigEntry<float> BossChance;
        internal static ConfigEntry<float> BossThreshold;
        internal static ConfigEntry<float> BossMax;
        // 10 Stop demand
        internal static ConfigEntry<bool> DemandEnabled;
        internal static ConfigEntry<KeyboardShortcut> DemandKey;
        internal static ConfigEntry<float> DemandRange;
        internal static ConfigEntry<float> DemandCooldown;
        internal static ConfigEntry<float> DemandWindow;
        internal static ConfigEntry<float> DemandPressure;
        internal static ConfigEntry<float> DemandChanceBoost;
        internal static ConfigEntry<float> PersonalityMin;
        internal static ConfigEntry<float> PersonalityMax;
        internal static ConfigEntry<float> ArmourConfidence;
        internal static ConfigEntry<float> KitValueWeight;
        internal static ConfigEntry<float> KitValueFull;
        internal static ConfigEntry<float> ExperienceComposure;
        internal static ConfigEntry<float> KitRecheck;
        // 06
        internal static ConfigEntry<float> LeaveDistance;
        internal static ConfigEntry<float> LeaveSeconds;
        internal static ConfigEntry<float> WatchAngle;
        internal static ConfigEntry<bool> BoltWhenShot;
        internal static ConfigEntry<bool> FastHolster;
        internal static ConfigEntry<float> DropRetrySeconds;
        internal static ConfigEntry<bool> FinishReloadInstantly;
        internal static ConfigEntry<float> FaceHysteresis;
        internal static ConfigEntry<bool> ReleaseSainControl;
        internal static ConfigEntry<float> OpeningDelay;
        internal static ConfigEntry<float> BreakInterval;
        internal static ConfigEntry<float> BreakChance;
        internal static ConfigEntry<float> OpportunityBoost;
        internal static ConfigEntry<float> KnifeRange;
        internal static ConfigEntry<float> SidearmRange;
        internal static ConfigEntry<float> ScrambleRange;
        internal static ConfigEntry<float> ExposureDistance;
        internal static ConfigEntry<float> EscapeMinDistance;
        internal static ConfigEntry<float> DrawWeight;
        internal static ConfigEntry<float> KnifeWeight;
        internal static ConfigEntry<float> ScrambleWeight;
        internal static ConfigEntry<float> EscapeWeight;
        internal static ConfigEntry<float> ScrambleTimeout;
        internal static ConfigEntry<float> ScrambleCooldown;
        // 08
        internal static ConfigEntry<float> RetreatDistance;
        internal static ConfigEntry<float> Grace;
        internal static ConfigEntry<float> ResurrenderDistance;
        internal static ConfigEntry<float> AimTolerance;
        internal static ConfigEntry<float> AimConfirm;
        internal static ConfigEntry<float> RecaptureResistance;
        internal static ConfigEntry<float> RetreatTimeout;
        internal static ConfigEntry<float> ArrivalTolerance;
        // 09
        internal static ConfigEntry<float> RegroupMin;
        internal static ConfigEntry<float> RegroupMax;
        internal static ConfigEntry<bool> RecoverOwnWeapon;
        internal static ConfigEntry<float> AvoidRadius;
        internal static ConfigEntry<bool> ScavengeWeapons;
        internal static ConfigEntry<float> ScavengeRadius;
        internal static ConfigEntry<float> ScavengeAvoid;
        internal static ConfigEntry<float> RecoveryTimeout;
        internal static ConfigEntry<float> PickupRange;
        internal static ConfigEntry<float> PickupTimeout;
        internal static ConfigEntry<bool> HighNerveKnife;
        // 10
        internal static ConfigEntry<float> HideRecheck;
        internal static ConfigEntry<float> CoverRadius;
        internal static ConfigEntry<int> PathCandidates;
        internal static ConfigEntry<int> PathRetries;
        internal static ConfigEntry<float> StallSeconds;
        internal static ConfigEntry<float> ProgressMeters;
        // 11
        internal static ConfigEntry<bool> OrdersEnabled;
        internal static ConfigEntry<KeyboardShortcut> MenuKey;
        internal static ConfigEntry<KeyboardShortcut> NextKey;
        internal static ConfigEntry<KeyboardShortcut> PrevKey;
        internal static ConfigEntry<float> InteractionRange;
        internal static ConfigEntry<float> FollowDistance;
        internal static ConfigEntry<float> FollowTolerance;
        internal static ConfigEntry<float> DropItemDelay;
        internal static ConfigEntry<bool> KitOrder;
        internal static ConfigEntry<bool> ArmourOrder;
        internal static ConfigEntry<bool> FollowOrder;
        // 12
        internal static ConfigEntry<bool> LocalReactions;
        internal static ConfigEntry<float> WitnessRadius;
        internal static ConfigEntry<float> WitnessEscape;
        internal static ConfigEntry<bool> BotVoice;
        internal static ConfigEntry<float> VoiceCooldown;
        internal static ConfigEntry<bool> Hints;
        // 13
        internal static ConfigEntry<float> UpperArmRaise;
        internal static ConfigEntry<float> UpperArmForward;
        internal static ConfigEntry<float> ForearmForward;
        internal static ConfigEntry<float> ForearmInward;
        internal static ConfigEntry<float> ForearmTwist;
        internal static ConfigEntry<float> RaiseSeconds;
        internal static ConfigEntry<float> LowerSeconds;
        internal static ConfigEntry<float> EmergencyLowerSeconds;
        internal static ConfigEntry<float> PoseStrength;
        internal static ConfigEntry<float> TrembleDegrees;
        internal static ConfigEntry<float> TrembleSpeed;
        internal static ConfigEntry<float> Stance;
        internal static ConfigEntry<bool> FaceCaptor;
        // 14
        internal static ConfigEntry<bool> SainPersonality;
        internal static ConfigEntry<bool> SainSuppression;
        internal static ConfigEntry<bool> PauseWhenAsleep;
        // 15
        internal static ConfigEntry<bool> DebugKeys;
        internal static ConfigEntry<KeyboardShortcut> SurrenderKey;
        internal static ConfigEntry<KeyboardShortcut> OpeningKey;
        internal static ConfigEntry<KeyboardShortcut> LeaveKey;
        internal static ConfigEntry<float> DebugKeyDistance;
        internal static ConfigEntry<float> DebugOpeningSeconds;
        internal static ConfigEntry<KeyboardShortcut> ForceRunKey;
        internal static ConfigEntry<KeyboardShortcut> ForceGrabKey;
        internal static ConfigEntry<bool> Overlay;
        internal static ConfigEntry<bool> VerboseLogs;
        internal static ConfigEntry<bool> NerveOverride;
        internal static ConfigEntry<float> NerveOverrideValue;
        internal static ConfigEntry<float> ErrorLogCooldown;

        private static ConfigDescription Range(string text, float min, float max) =>
            new ConfigDescription(text, new AcceptableValueRange<float>(min, max));

        private static ConfigDescription IntRange(string text, int min, int max) =>
            new ConfigDescription(text, new AcceptableValueRange<int>(min, max));

        internal static void Bind(ConfigFile c)
        {
            // 01 General
            Enabled = c.Bind(General, "Enabled", true, "Off: no new surrenders, and bots already held are let go.");
            NaturalSurrender = c.Bind(General, "Bots surrender on their own", true,
                "Hurt, pinned down, out of ammo, outnumbered or left alone, a bot may give up. Off: only the debug key starts a surrender.");
            IncludePmcs = c.Bind(General, "PMCs can surrender", true, "Off: only Scavs.");
            IncludeBosses = c.Bind(General, "Bosses and followers can surrender (restart)", false,
                "Off (default): bosses and their followers never surrender. On: they can, but far more reluctantly than PMCs (section 05). Needs a game restart.");
            LayerPriority = c.Bind(General, "Layer priority (restart)", 95,
                IntRange("BigBrain priority. SAIN uses 20-24 and 80, ORBIT 19.", 1, 200));

            // 02 Nerve
            NerveMin = c.Bind(NerveSec, "Nerve min", 0f,
                Range("Each bot rolls nerve once per raid, between min and max. High nerve: slower to give up, quicker to try something.", 0f, 1f));
            NerveMax = c.Bind(NerveSec, "Nerve max", 1f, Range("", 0f, 1f));
            HighNerve = c.Bind(NerveSec, "High nerve from", 0.75f, Range("Bots at or above this will come back with only a knife.", 0f, 1f));

            // 03 Surrendering on their own
            EvalInterval = c.Bind(Natural, "Check every (s)", 5f, Range("How often each bot weighs giving up. One roll per check, never caught up after a pause.", 1f, 60f));
            SurrenderCooldown = c.Bind(Natural, "Cooldown after a surrender (s)", 30f,
                Range("Once he's fighting again, how long before he can give up again.", 0f, 300f));
            SurrenderDistance = c.Bind(Natural, "Within (m)", 30f, Range("Only gives up to a player this close.", 1f, 100f));
            SeenWithin = c.Bind(Natural, "Seen within (s)", 3f,
                Range("He must see that player now or have seen him this recently: a pinned-down man behind cover still knows where you are.", 0f, 15f));
            RequiredFactors = c.Bind(Natural, "Reasons needed", 2,
                IntRange("How many separate reasons must apply at once (severe injury, being pinned down or a Stop demand count on their own).", 1, 5));
            NerveResistance = c.Bind(Natural, "Nerve resistance", 0.85f, Range("1: a bot with full nerve never gives up.", 0f, 1f));
            CurveStart = c.Bind(Natural, "Chance starts at pressure", 0.3f,
                Range("Below this pressure the chance is 0; it rises to the role's full base chance at pressure 1.", 0f, 0.9f));

            // 04 Pressure
            InjuryThreshold = c.Bind(Pressure, "Hurt below (health fraction)", 0.35f, Range("Where injury starts to count.", 0.05f, 1f));
            InjuryWeight = c.Bind(Pressure, "Weight: injury", 0.45f, Range("", 0f, 1f));
            AmmoWeight = c.Bind(Pressure, "Weight: out of ammo", 0.35f, Range("No rounds left for his gun and none to reload, or no gun at all.", 0f, 1f));
            SuppressionWeight = c.Bind(Pressure, "Weight: suppression", 0.40f,
                Range("Incoming fire: hits and shots passing close to him (and SAIN's suppression level if installed).", 0f, 1f));
            SuppressionHalfLife = c.Bind(Pressure, "Suppression half-life (s)", 4f, Range("How fast the effect of incoming fire fades.", 0.5f, 30f));
            SustainedFireEvents = c.Bind(Pressure, "Full suppression at (shots)", 6f,
                Range("Recent shots at him (after fading) for the full suppression weight. One stray shot counts for little.", 1f, 30f));
            OutnumberedWeight = c.Bind(Pressure, "Weight: outnumbered", 0.25f, Range("", 0f, 1f));
            OutnumberedRatio = c.Bind(Pressure, "Fully outnumbered at", 2f,
                Range("Enemies per friendly (counting himself) for the full outnumbered weight.", 1.1f, 6f));
            EnemyMemory = c.Bind(Pressure, "Enemy counts for (s)", 10f,
                Range("An enemy he saw this recently counts toward being outnumbered.", 1f, 60f));
            SquadWeight = c.Bind(Pressure, "Weight: squad", 0.30f, Range("Squadmates killed recently, or squad alive but none of them close.", 0f, 1f));
            SquadMemory = c.Bind(Pressure, "Squad loss counts for (s)", 30f, Range("", 1f, 180f));
            AloneFactor = c.Bind(Pressure, "Squad too far away", 0.5f,
                Range("Squad factor when his squad is alive but nobody is within the ally radius.", 0f, 1f));
            AllyRadius = c.Bind(Pressure, "Allies within (m)", 40f, Range("", 5f, 150f));
            VitalThreshold = c.Bind(Pressure, "Head or thorax below (fraction)", 0.4f,
                Range("Injury also counts a badly hurt head or thorax, a destroyed limb and bleeding; the worst of these is used, never their sum.", 0.05f, 1f));
            SevereInjury = c.Bind(Pressure, "Severe injury from", 0.6f,
                Range("Injury this bad lets him consider surrender on its own, without a second reason. Still a roll; still nerve and personality.", 0.1f, 1.1f));
            SeverePressure = c.Bind(Pressure, "Severe injury pressure floor", 0.6f, Range("", 0f, 1f));
            SevereChanceBoost = c.Bind(Pressure, "Severe injury chance boost", 2f, Range("Times the per-check chance while severely injured.", 1f, 5f));
            PinnedLevel = c.Bind(Pressure, "Pinned down from", 0.8f,
                Range("Suppression this heavy (sustained fire close to him) also counts on its own, like severe injury. 1.1 turns it off.", 0.1f, 1.1f));
            PinnedPressure = c.Bind(Pressure, "Pinned down pressure floor", 0.5f, Range("", 0f, 1f));
            NearMissRadius = c.Bind(Pressure, "Near miss within (m)", 2f,
                Range("A shot from an enemy passing this close to him (with nothing solid in between) counts as fire at him; closer counts more.", 0.25f, 6f));
            NearMissRange = c.Bind(Pressure, "Near miss checked up to (m)", 150f, Range("", 10f, 500f));
            PromptCheckDelay = c.Bind(Pressure, "Recheck after a hit within (s)", 0.5f,
                Range("Being hit or shot at brings his next surrender check forward to this soon (at most every 1.5 s).", 0.1f, 5f));

            // 05 Who he is
            // Role sets the base odds and the bar to clear. Per check (every 5 s, every 1.5 s under
            // fire): chance = base x pressure curve x (1 - nerve resistance x nerve) x personality
            // x kit x experience (x2 severe injury), capped at max.
            ScavChance = c.Bind(Role, "Scav base chance", 0.35f,
                Range("Scavs are hired guns with little to die for: at full pressure an average Scav gives up about 1 check in 6.", 0f, 1f));
            ScavThreshold = c.Bind(Role, "Scav pressure needed", 0.4f, Range("Pressure a Scav needs (with enough reasons) before he rolls at all.", 0f, 1f));
            ScavMax = c.Bind(Role, "Scav max chance", 0.5f, Range("Per check, after everything.", 0f, 1f));
            PmcChance = c.Bind(Role, "PMC base chance", 0.12f,
                Range("PMCs are trained and committed: about a third of a Scav's odds.", 0f, 1f));
            PmcThreshold = c.Bind(Role, "PMC pressure needed", 0.55f, Range("", 0f, 1f));
            PmcMax = c.Bind(Role, "PMC max chance", 0.25f, Range("", 0f, 1f));
            BossChance = c.Bind(Role, "Boss and follower base chance", 0.03f,
                Range("Only with Bosses and followers can surrender on (section 01).", 0f, 1f));
            BossThreshold = c.Bind(Role, "Boss and follower pressure needed", 0.75f, Range("", 0f, 1f));
            BossMax = c.Bind(Role, "Boss and follower max chance", 0.08f, Range("", 0f, 1f));

            // 10 Stop demand
            DemandEnabled = c.Bind(Demand, "Stop demand", false,
                "Off by default. On: saying Stop (the game's own phrase, from the gesture/phrase menu or its quick bind) to a bot under your crosshair asks him to surrender. It brings his surrender check forward and adds pressure; it doesn't make him comply.");
            DemandKey = c.Bind(Demand, "Shout Stop key", KeyboardShortcut.Empty,
                "Optional, unbound by default. Makes your character say the game's Stop line and counts as the same demand.");
            DemandRange = c.Bind(Demand, "Demand range (m)", 20f, Range("He must be under your crosshair, this close, with a clear line to you.", 2f, 60f));
            DemandCooldown = c.Bind(Demand, "Demand cooldown (s)", 8f, Range("Per bot: repeated shouting within this time does nothing more.", 1f, 60f));
            DemandWindow = c.Bind(Demand, "Demand lasts (s)", 6f, Range("How long the demand weighs on him (it doesn't stack).", 1f, 30f));
            DemandPressure = c.Bind(Demand, "Demand pressure", 0.2f, Range("Added to his pressure while it lasts; also counts as a reason.", 0f, 1f));
            DemandChanceBoost = c.Bind(Demand, "Demand chance boost", 1.5f, Range("Times his chance on the checks while it lasts.", 1f, 5f));
            PersonalityMin = c.Bind(Role, "Personality multiplier min", 0.5f, Range("Applied to the bravest SAIN personalities.", 0f, 3f));
            PersonalityMax = c.Bind(Role, "Personality multiplier max", 1.5f, Range("Applied to Cowards.", 0f, 3f));
            ArmourConfidence = c.Bind(Role, "Armour confidence", 0.3f,
                Range("How much class 6 armour dulls the fear from incoming fire and being outnumbered. Never removes it.", 0f, 0.9f));
            KitValueWeight = c.Bind(Role, "Kit value weight", 0.25f,
                Range("A man with expensive kit has more to lose: up to this much more willing to bargain for his life.", 0f, 1f));
            KitValueFull = c.Bind(Role, "Kit value full at (roubles)", 250000f,
                Range("Handbook value of everything he carries for the full kit value weight.", 10000f, 3000000f));
            ExperienceComposure = c.Bind(Role, "Experience composure", 0.35f,
                Range("Level and difficulty make him calmer: up to this much less likely to give up. Never immune.", 0f, 0.8f));
            KitRecheck = c.Bind(Role, "Re-value kit every (s)", 30f, Range("", 5f, 300f));

            // 06 While held
            LeaveDistance = c.Bind(Coverage, "Captor gone beyond (m)", 25f, Range("Further than this while he can see you: he runs.", 5f, 100f));
            LeaveSeconds = c.Bind(Coverage, "Out of sight for (s)", 6f, Range("He can't see you this long: he runs.", 0.5f, 30f));
            WatchAngle = c.Bind(Coverage, "Covered within (deg)", 30f,
                Range("Looking further away than this counts as an opening. So do reloading, the inventory, no gun in hand and no line of sight.", 1f, 90f));
            BoltWhenShot = c.Bind(Coverage, "Bolt when shot", true, "Hit while his hands are up, by anyone: he runs.");
            FastHolster = c.Bind(Coverage, "Fast weapon drop", true,
                "Empties his hands with the game's quick holster (as in a fast weapon switch) before throwing the gun. Off: the normal holster animation.");
            DropRetrySeconds = c.Bind(Coverage, "Ask again after (s)", 1f,
                Range("If the game hasn't emptied his hands by then, check what is holding it up. One request at a time; after 3 tries he fights instead.", 0.3f, 5f));
            FinishReloadInstantly = c.Bind(Coverage, "Cut a reload short", true,
                "A magazine reload in progress can't be aborted (the game holds the drop until it ends). On: it is finished instantly the way the game itself does for bots (FastForwardCurrentState) and the gun drops at once. Off: he finishes the reload first.");
            FaceHysteresis = c.Bind(Coverage, "Turn to you after (deg)", 4f,
                Range("He re-aims his face only when you've moved this far around him, so he doesn't twitch with every step.", 0.5f, 30f));
            ReleaseSainControl = c.Bind(Coverage, "Take control from SAIN", true,
                "SAIN keeps steering and blocks the game's movement for a bot whose SAIN layer was active when he surrendered. On: SAIN is told its layer is no longer active (its own BotComponent.ActiveLayer = None) while he is held. No effect without SAIN.");

            // 07 Taking an opening
            OpeningDelay = c.Bind(Opening, "Opening must last (s)", 1.5f, Range("", 0f, 10f));
            BreakInterval = c.Bind(Opening, "Roll every (s)", 1f, Range("While there's an opening.", 0.1f, 10f));
            BreakChance = c.Bind(Opening, "Break chance", 0.15f, Range("Per roll, scaled by nerve (30% at nerve 0, 100% at nerve 1).", 0f, 1f));
            OpportunityBoost = c.Bind(Opening, "Gun nearby boost", 2f,
                Range("His own gun close, reachable and away from you multiplies the break chance by up to 1 + this.", 0f, 5f));
            KnifeRange = c.Bind(Opening, "Knife range (m)", 4f, Range("Closer than this a knife is an option.", 0.5f, 10f));
            SidearmRange = c.Bind(Opening, "Draw range (m)", 30f, Range("Closer than this drawing a gun he still carries is an option.", 1f, 100f));
            ScrambleRange = c.Bind(Opening, "Scramble range (m)", 10f,
                Range("His dropped gun within this distance (and reachable) is an option: he runs for it.", 1f, 30f));
            ExposureDistance = c.Bind(Opening, "Gun safe from you beyond (m)", 8f,
                Range("The closer you stand to his dropped gun, the less he wants to go for it.", 1f, 30f));
            EscapeMinDistance = c.Bind(Opening, "Run only beyond (m)", 6f, Range("Closer than this, running isn't an option.", 0f, 30f));
            DrawWeight = c.Bind(Opening, "Weight: draw", 1f, Range("How the options compare. Each is scaled by nerve, personality and the situation.", 0f, 5f));
            KnifeWeight = c.Bind(Opening, "Weight: knife", 0.8f, Range("", 0f, 5f));
            ScrambleWeight = c.Bind(Opening, "Weight: scramble for his gun", 1.6f, Range("", 0f, 5f));
            EscapeWeight = c.Bind(Opening, "Weight: run", 0.5f, Range("", 0f, 5f));
            ScrambleTimeout = c.Bind(Opening, "Scramble time limit (s)", 8f, Range("", 2f, 30f));
            ScrambleCooldown = c.Bind(Opening, "No second scramble for (s)", 15f, Range("After a scramble is stopped, so he doesn't flip back and forth.", 0f, 120f));

            // 08 Retreat and recapture
            RetreatDistance = c.Bind(Retreat, "Retreat distance (m)", 35f, Range("", 5f, 150f));
            Grace = c.Bind(Retreat, "Grace after running (s)", 4f, Range("After he starts running, how long before he can be caught again.", 0f, 30f));
            ResurrenderDistance = c.Bind(Retreat, "Caught again within (m)", 12f, Range("", 1f, 50f));
            AimTolerance = c.Bind(Retreat, "Aim within (deg)", 10f, Range("How directly you must aim at him to catch him again.", 1f, 45f));
            AimConfirm = c.Bind(Retreat, "Aim for (s)", 0.4f, Range("", 0f, 3f));
            RecaptureResistance = c.Bind(Retreat, "Resist chance", 0.5f,
                Range("Times nerve: with a gun still on him, the chance he draws instead of giving up again.", 0f, 1f));
            RetreatTimeout = c.Bind(Retreat, "Retreat time limit (s)", 15f, Range("", 3f, 60f));
            ArrivalTolerance = c.Bind(Retreat, "Arrived within (m)", 2f, Range("", 0.5f, 5f));

            // 09 Regroup and rearm
            RegroupMin = c.Bind(Recovery, "Regroup min (s)", 4f, Range("Time watching where he last saw you before deciding.", 0f, 60f));
            RegroupMax = c.Bind(Recovery, "Regroup max (s)", 12f, Range("", 0f, 60f));
            RecoverOwnWeapon = c.Bind(Recovery, "Go back for his own gun", true, "If it's still there and you weren't near it.");
            AvoidRadius = c.Bind(Recovery, "Own gun: keep away from you (m)", 20f, Range("", 0f, 100f));
            ScavengeWeapons = c.Bind(Recovery, "Look for another gun", true, "Loose guns and guns on bodies.");
            ScavengeRadius = c.Bind(Recovery, "Search radius (m)", 60f, Range("", 5f, 200f));
            ScavengeAvoid = c.Bind(Recovery, "Other guns: keep away from you (m)", 20f, Range("", 0f, 100f));
            RecoveryTimeout = c.Bind(Recovery, "Give up walking after (s)", 30f, Range("", 5f, 120f));
            PickupRange = c.Bind(Recovery, "Pick-up range (m)", 1.5f, Range("", 0.5f, 3f));
            PickupTimeout = c.Bind(Recovery, "Pick-up time limit (s)", 5f, Range("", 1f, 20f));
            HighNerveKnife = c.Bind(Recovery, "Fight with a knife", true, "High-nerve bots with no gun to find come back with their knife.");

            // 10 Hiding and movement
            HideRecheck = c.Bind(Hiding, "Look again every (s)", 8f, Range("", 1f, 60f));
            CoverRadius = c.Bind(Hiding, "Cover search radius (m)", 20f, Range("", 3f, 100f));
            PathCandidates = c.Bind(Hiding, "Points tried", 12, IntRange("Candidate spots per retreat or cover search.", 2, 48));
            PathRetries = c.Bind(Hiding, "Retries when stuck", 3, IntRange("", 0, 10));
            StallSeconds = c.Bind(Hiding, "Stuck after (s)", 3f, Range("Without making progress.", 0.5f, 15f));
            ProgressMeters = c.Bind(Hiding, "Progress (m)", 0.5f, Range("Movement that counts as progress.", 0.1f, 3f));

            // 11 Orders
            OrdersEnabled = c.Bind(Interact, "Orders", true, "Look at a held bot up close and press the menu key.");
            MenuKey = c.Bind(Interact, "Menu / confirm key", new KeyboardShortcut(KeyCode.Slash),
                "Opens the order menu while you look at a held bot; press again to give the highlighted order.");
            NextKey = c.Bind(Interact, "Next order", new KeyboardShortcut(KeyCode.Period), "");
            PrevKey = c.Bind(Interact, "Previous order", new KeyboardShortcut(KeyCode.Equals), "");
            InteractionRange = c.Bind(Interact, "Order range (m)", 3f, Range("", 1f, 8f));
            FollowDistance = c.Bind(Interact, "Follow distance (m)", 4f, Range("", 1f, 15f));
            FollowTolerance = c.Bind(Interact, "Follow slack (m)", 1f, Range("", 0.25f, 5f));
            DropItemDelay = c.Bind(Interact, "Time per dropped item (s)", 0.4f, Range("", 0.1f, 3f));
            KitOrder = c.Bind(Interact, "Allow kit order", true, "");
            ArmourOrder = c.Bind(Interact, "Allow armour order", true, "");
            FollowOrder = c.Bind(Interact, "Allow follow order", true, "");

            // 12 Reactions and voice
            LocalReactions = c.Bind(Feedback, "Other held bots react", true, "A held bot who sees another one shot runs.");
            WitnessRadius = c.Bind(Feedback, "Sees it within (m)", 40f, Range("", 5f, 150f));
            WitnessEscape = c.Bind(Feedback, "Chance he runs", 1f, Range("", 0f, 1f));
            BotVoice = c.Bind(Feedback, "Bot voice lines", true, "The game's own lines (hold fire, roger, negative...).");
            VoiceCooldown = c.Bind(Feedback, "Voice cooldown (s)", 12f, Range("", 1f, 120f));
            Hints = c.Bind(Feedback, "On-screen hints", true, "A small prompt when you can give orders, and debug confirmations.");

            // 13 Arm pose
            UpperArmRaise = c.Bind(Pose, "Upper arm raise (deg)", 12f,
                Range("Elbow height: 0 = level with the shoulder, positive = above.", -40f, 80f));
            UpperArmForward = c.Bind(Pose, "Upper arm forward (deg)", 25f,
                Range("0 = straight out to the side, 90 = straight ahead.", -20f, 90f));
            ForearmForward = c.Bind(Pose, "Forearm forward (deg)", 12f,
                Range("0 = forearm straight up, positive tips the hands forward.", -30f, 80f));
            ForearmInward = c.Bind(Pose, "Forearm inward (deg)", 8f,
                Range("Positive tips the hands in towards the head.", -30f, 45f));
            ForearmTwist = c.Bind(Pose, "Forearm twist (deg)", 0f,
                Range("Turns the forearms about their length, to swing the palms forward.", -180f, 180f));
            RaiseSeconds = c.Bind(Pose, "Raise time (s)", 0.35f, Range("", 0f, 2f));
            LowerSeconds = c.Bind(Pose, "Lower time (s)", 0.2f, Range("", 0f, 2f));
            EmergencyLowerSeconds = c.Bind(Pose, "Fast lower time (s)", 0.08f, Range("Shot, or going for a weapon.", 0f, 0.5f));
            PoseStrength = c.Bind(Pose, "Pose strength", 1f, Range("Below 1 lets breathing and sway show through the arms.", 0f, 1f));
            TrembleDegrees = c.Bind(Pose, "Tremble (deg)", 0.8f, Range("0 turns it off.", 0f, 5f));
            TrembleSpeed = c.Bind(Pose, "Tremble speed", 6f, Range("", 0f, 20f));
            Stance = c.Bind(Pose, "Stance with hands up", 1f, Range("1 = standing, 0 = full crouch.", 0f, 1f));
            FaceCaptor = c.Bind(Pose, "Face the player", true, "Turns to face whoever holds him.");

            // 14 Other mods
            SainPersonality = c.Bind(Compat, "Read SAIN personality", true,
                "Cowards and Timmys give up sooner and run more, Chads and GigaChads hold out and fight more. No effect without SAIN.");
            SainSuppression = c.Bind(Compat, "Read SAIN suppression", true,
                "Uses SAIN's suppression level as well as the game's own incoming-fire signal (the higher counts). No effect without SAIN.");
            PauseWhenAsleep = c.Bind(Compat, "Pause while a bot is asleep", true,
                "If another mod (ORBIT's Ghost Mode) puts a held bot to sleep, he drops any plan to fetch a gun and decides again when he wakes.");

            // 15 Debug
            DebugKeys = c.Bind(Debug, "Debug keys", false, "Turns on the three keys below. Each acts only on the bot under your crosshair.");
            SurrenderKey = c.Bind(Debug, "Surrender bot", new KeyboardShortcut(KeyCode.LeftBracket),
                "The bot under your crosshair surrenders. Nothing happens if no bot is under it.");
            OpeningKey = c.Bind(Debug, "Give them an opening", new KeyboardShortcut(KeyCode.RightBracket),
                "The held bot under your crosshair treats you as not covering him for a few seconds; his own rolls decide what he does.");
            LeaveKey = c.Bind(Debug, "Pretend you left", new KeyboardShortcut(KeyCode.Backspace),
                "The held bot under your crosshair acts as if you walked off.");
            DebugKeyDistance = c.Bind(Debug, "Surrender key range (m)", 60f, Range("", 2f, 200f));
            DebugOpeningSeconds = c.Bind(Debug, "Opening lasts (s)", 6f, Range("", 1f, 30f));
            ForceRunKey = c.Bind(Debug, "Force run", new KeyboardShortcut(KeyCode.Backspace, KeyCode.LeftControl),
                "Deterministic test: the held bot under your crosshair runs now (no roll).");
            ForceGrabKey = c.Bind(Debug, "Force gun grab", new KeyboardShortcut(KeyCode.RightBracket, KeyCode.LeftControl),
                "Deterministic test: the held bot under your crosshair scrambles for his dropped gun now (no roll), if it's there and reachable.");
            Overlay = c.Bind(Debug, "Overlay", false, "Lists every held bot: phase, order, nerve, arms.");
            VerboseLogs = c.Bind(Debug, "Verbose logs", false, "Logs every roll, including the ones that change nothing.");
            NerveOverride = c.Bind(Debug, "Override nerve", false, "Every bot uses the value below instead of his own roll, live.");
            NerveOverrideValue = c.Bind(Debug, "Nerve value", 0.5f, Range("", 0f, 1f));
            ErrorLogCooldown = c.Bind(Debug, "Repeat errors every (s)", 10f, Range("The same error is logged at most this often.", 1f, 120f));
        }

        internal static float RollNerve()
        {
            float lo = Mathf.Min(NerveMin.Value, NerveMax.Value);
            float hi = Mathf.Max(NerveMin.Value, NerveMax.Value);
            return Random.Range(lo, hi);
        }

        // The override applies live, to bots already met this raid too.
        internal static float EffectiveNerve(float rolled) => NerveOverride.Value ? NerveOverrideValue.Value : rolled;

        internal static float RegroupSeconds()
        {
            float lo = Mathf.Min(RegroupMin.Value, RegroupMax.Value);
            float hi = Mathf.Max(RegroupMin.Value, RegroupMax.Value);
            return Random.Range(lo, hi);
        }
    }
}
