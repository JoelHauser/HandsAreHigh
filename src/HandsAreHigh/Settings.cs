using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // Every tuning value, in numbered F12 sections. All of it applies live except the
    // layer priority, which BigBrain only reads once at startup.
    // Section and key names must never contain = \ " ' [ ] (BepInEx throws; pack.ps1 checks).
    internal static partial class Settings
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
        private const string KneelSec = "12. Kneeling";
        private const string Compat = "13. Other mods";
        private const string Debug = "14. Debug";
        private const string BotCaptor = "15. Bots taking prisoners";
        private const string RestraintSec = "16. Sparing surrendered enemies";
        private const string ScenarioSec = "17. Debug Scenarios";
        private const string PresenceSec = "18. Attention and demeanor";

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
        internal static ConfigEntry<float> WalkAwayMargin;
        internal static ConfigEntry<float> AcknowledgeDistance;
        internal static ConfigEntry<float> OfferMin;
        internal static ConfigEntry<float> OfferMax;
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
        internal static ConfigEntry<float> ReactionMin;
        internal static ConfigEntry<float> ReactionMax;
        internal static ConfigEntry<float> SquadBoost;
        internal static ConfigEntry<float> HideWeight;
        internal static ConfigEntry<float> IntimidationSeconds;
        internal static ConfigEntry<float> OrderCompliance;
        internal static ConfigEntry<float> ApproachStep;
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
        internal static ConfigEntry<bool> NumberKeys;
        internal static ConfigEntry<bool> NumpadKeys;
        internal static ConfigEntry<bool> WheelSelect;
        internal static ConfigEntry<float> MenuScale;
        // 12
        internal static ConfigEntry<bool> LocalReactions;
        internal static ConfigEntry<float> WitnessRadius;
        internal static ConfigEntry<float> WitnessEscape;
        internal static ConfigEntry<bool> BotVoice;
        internal static ConfigEntry<float> VoiceCooldown;
        internal static ConfigEntry<bool> Hints;
        internal static ConfigEntry<bool> Chatter;
        internal static ConfigEntry<float> ChatterMin;
        internal static ConfigEntry<float> ChatterMax;
        internal static ConfigEntry<float> ChatterChance;
        internal static ConfigEntry<float> ReplyChance;
        internal static ConfigEntry<bool> DefiantRemarks;
        internal static ConfigEntry<float> ChatterRadius;
        internal static ConfigEntry<float> ChatterGap;
        internal static ConfigEntry<bool> Acknowledge;
        internal static ConfigEntry<float> AckDelayMin;
        internal static ConfigEntry<float> AckDelayMax;
        internal static ConfigEntry<bool> CommandSpeech;
        // 13
        internal static ConfigEntry<float> UpperArmRaise;
        internal static ConfigEntry<float> UpperArmForward;
        internal static ConfigEntry<float> ForearmForward;
        internal static ConfigEntry<float> ForearmInward;
        internal static ConfigEntry<float> ForearmTwist;
        internal static ConfigEntry<float> RaiseSeconds;
        internal static ConfigEntry<float> LowerSeconds;
        internal static ConfigEntry<float> EmergencyLowerSeconds;
        internal static ConfigEntry<float> DrawLowerSeconds;
        internal static ConfigEntry<bool> Quickdraw;
        internal static ConfigEntry<float> DrawHandoffTimeout;
        internal static ConfigEntry<float> DrawRetrySeconds;
        internal static ConfigEntry<float> PoseStrength;
        internal static ConfigEntry<float> TrembleDegrees;
        internal static ConfigEntry<float> TrembleSpeed;
        internal static ConfigEntry<float> Stance;
        internal static ConfigEntry<bool> FaceCaptor;
        internal static ConfigEntry<bool> Wiggle;
        internal static ConfigEntry<float> WiggleChance;
        internal static ConfigEntry<float> WiggleMaxChance;
        internal static ConfigEntry<float> WiggleCooldown;
        internal static ConfigEntry<float> WiggleStrength;
        internal static ConfigEntry<int> WiggleSwings;
        internal static ConfigEntry<float> WiggleSwingSeconds;
        internal static ConfigEntry<float> WiggleDelayMin;
        internal static ConfigEntry<float> WiggleDelayMax;
        internal static ConfigEntry<float> WiggleWindow;
        internal static ConfigEntry<float> WiggleSampleGap;
        internal static ConfigEntry<float> WiggleClearance;
        internal static ConfigEntry<bool> Defiant;
        internal static ConfigEntry<float> DefiantChance;
        internal static ConfigEntry<float> DefiantInfluence;
        internal static ConfigEntry<float> DefiantMaxChance;
        internal static ConfigEntry<float> DefiantCooldown;
        internal static ConfigEntry<float> DefiantDelayMin;
        internal static ConfigEntry<float> DefiantDelayMax;
        internal static ConfigEntry<float> DefiantWindow;
        internal static ConfigEntry<float> GestureBlend;
        // 12 Kneeling
        internal static ConfigEntry<bool> KneelEnabled;
        internal static ConfigEntry<float> KneelDownSeconds;
        internal static ConfigEntry<float> KneelUpSeconds;
        internal static ConfigEntry<float> KneelQuickSeconds;
        internal static ConfigEntry<float> KneelStrength;
        internal static ConfigEntry<float> KneelLean;
        internal static ConfigEntry<float> KneelLeanScale;
        internal static ConfigEntry<float> KneelFrightenedSeconds;
        internal static ConfigEntry<float> KneelOneKneeSeconds;
        internal static ConfigEntry<float> KneelDefensiveSeconds;
        internal static ConfigEntry<float> KneelWeightSteady;
        internal static ConfigEntry<float> KneelWeightFrightened;
        internal static ConfigEntry<float> KneelWeightOneKnee;
        internal static ConfigEntry<bool> KneelBrace;
        internal static ConfigEntry<float> KneelBraceChance;
        internal static ConfigEntry<float> CaptorKneelChance;
        internal static ConfigEntry<KeyboardShortcut> KneelUrgentKey;
        internal static ConfigEntry<KneelStyleChoice> KneelTestStyle;
        internal static ConfigEntry<KneelRiseChoice> KneelTestRise;
        internal static ConfigEntry<KneelBraceChoice> KneelTestBrace;
        internal static ConfigEntry<bool> KneelBracePalmFlip;
        internal static ConfigEntry<float> KneeHeight;
        internal static ConfigEntry<float> KneelThighAngle;
        internal static ConfigEntry<float> KneeSpread;
        internal static ConfigEntry<float> KneelHipsForward;
        internal static ConfigEntry<float> HandsBehindHeadStrength;
        internal static ConfigEntry<float> HandsBehindHead;
        internal static ConfigEntry<float> HandsAboveNeck;
        internal static ConfigEntry<float> ElbowsOut;
        internal static ConfigEntry<float> HelmetExtra;
        internal static ConfigEntry<float> KneelMaxStep;
        internal static ConfigEntry<float> KneelMaxSlope;
        internal static ConfigEntry<bool> KneelFallbackCrouch;
        internal static ConfigEntry<bool> LogKneel;
        internal static ConfigEntry<bool> KneelReposition;
        internal static ConfigEntry<float> KneelSpotRadius;
        internal static ConfigEntry<KneelEscapeChoice> KneelTestEscape;
        // 0.15.0: pose ownership and diagnostics
        internal static ConfigEntry<KneelPoseIsolate> KneelPoseIsolate;
        internal static ConfigEntry<bool> LogPose;
        internal static ConfigEntry<float> KneelHeadYaw;
        internal static ConfigEntry<float> KneelTorsoYaw;
        // 0.15.0: bot captors close in and kneel prisoners
        internal static ConfigEntry<float> CaptorCloseChance;
        internal static ConfigEntry<float> CaptorStandoff;
        // 18 Attention and demeanor
        internal static ConfigEntry<bool> DemeanorEnabled;
        internal static ConfigEntry<float> DemeanorStrength;
        internal static ConfigEntry<bool> HeadMovement;
        internal static ConfigEntry<float> GlanceMin;
        internal static ConfigEntry<float> GlanceMax;
        internal static ConfigEntry<float> HeadMaxYaw;
        internal static ConfigEntry<float> HeadMaxDown;
        internal static ConfigEntry<float> HeadMaxUp;
        internal static ConfigEntry<float> HeadSpeed;
        internal static ConfigEntry<float> TorsoAssist;
        internal static ConfigEntry<bool> KneelIdle;
        internal static ConfigEntry<float> KneelIdleStrength;
        internal static ConfigEntry<bool> Flinch;
        internal static ConfigEntry<float> FlinchStrength;
        internal static ConfigEntry<float> FlinchCooldown;
        internal static ConfigEntry<bool> LogAttention;
        internal static ConfigEntry<bool> KneelGizmos;
        internal static ConfigEntry<KeyboardShortcut> KneelKey;
        internal static ConfigEntry<KeyboardShortcut> StandKey;
        internal static ConfigEntry<KeyboardShortcut> KneelLoopKey;
        internal static ConfigEntry<KeyboardShortcut> KneelInterruptKey;
        internal static ConfigEntry<int> KneelTestCycles;
        internal static ConfigEntry<KneelInterrupt> KneelTestInterrupt;
        internal static ConfigEntry<KneelInterruptDuring> KneelTestDuring;
        internal static ConfigEntry<float> KneelTestAt;
        // 14
        internal static ConfigEntry<bool> SainPersonality;
        internal static ConfigEntry<bool> SainSuppression;
        internal static ConfigEntry<bool> PauseWhenAsleep;
        internal static ConfigEntry<bool> OrbitKeepAwake;
        internal static ConfigEntry<bool> TeammatesSurrender;
        // 15
        internal static ConfigEntry<bool> DebugKeys;
        internal static ConfigEntry<KeyboardShortcut> SurrenderKey;
        internal static ConfigEntry<KeyboardShortcut> OpeningKey;
        internal static ConfigEntry<KeyboardShortcut> LeaveKey;
        internal static ConfigEntry<float> DebugKeyDistance;
        internal static ConfigEntry<float> DebugOpeningSeconds;
        internal static ConfigEntry<KeyboardShortcut> ForceRunKey;
        internal static ConfigEntry<KeyboardShortcut> ForceGrabKey;
        internal static ConfigEntry<KeyboardShortcut> SurrenderToBotKey;
        internal static ConfigEntry<bool> Overlay;
        internal static ConfigEntry<bool> VerboseLogs;
        internal static ConfigEntry<bool> NerveOverride;
        internal static ConfigEntry<float> NerveOverrideValue;
        internal static ConfigEntry<float> ErrorLogCooldown;
        internal static ConfigEntry<bool> LogDecisions;
        internal static ConfigEntry<bool> LogGestures;
        internal static ConfigEntry<bool> LogVoice;
        internal static ConfigEntry<bool> Profile;
        // 15 Bots taking prisoners
        internal static ConfigEntry<bool> BotCaptors;
        internal static ConfigEntry<int> CaptorPriority;
        internal static ConfigEntry<int> MaxAiEncounters;
        internal static ConfigEntry<float> BotSurrenderScale;
        internal static ConfigEntry<float> AcceptChance;
        internal static ConfigEntry<float> SpareChance;
        internal static ConfigEntry<float> AcceptRange;
        internal static ConfigEntry<float> HoldMin;
        internal static ConfigEntry<float> HoldMax;
        internal static ConfigEntry<float> OrderGapMin;
        internal static ConfigEntry<float> OrderGapMax;
        internal static ConfigEntry<float> AiOrderRange;
        internal static ConfigEntry<float> CaptorApproachFrom;
        internal static ConfigEntry<float> CaptorGuardDistance;
        internal static ConfigEntry<float> CaptorApproachSpeed;
        internal static ConfigEntry<float> BusyRelease;
        internal static ConfigEntry<bool> AlliedCaptors;
        internal static ConfigEntry<bool> CaptorSpeech;
        internal static ConfigEntry<bool> CaptorCover;
        internal static ConfigEntry<float> CaptorCoverRadius;
        internal static ConfigEntry<bool> PrisonerFollowsToCover;
        internal static ConfigEntry<float> FollowToCoverWithin;
        internal static ConfigEntry<float> QuietBeforeGuarding;
        internal static ConfigEntry<float> MinEngageSeconds;
        internal static ConfigEntry<bool> CaptorGlances;
        internal static ConfigEntry<float> CaptorVoiceGap;
        // 16 Sparing surrendered enemies
        internal static ConfigEntry<bool> SquadRestraint;
        internal static ConfigEntry<float> SquadNoticeRadius;
        internal static ConfigEntry<float> SquadRejectChance;
        internal static ConfigEntry<bool> ObserverRestraint;
        internal static ConfigEntry<float> ObserverRadius;
        internal static ConfigEntry<float> ObserverRespect;
        internal static ConfigEntry<float> FleeingRespect;
        internal static ConfigEntry<float> RestraintMemory;
        internal static ConfigEntry<float> WatchSeconds;
        internal static ConfigEntry<bool> LogRestraint;
        internal static ConfigEntry<float> ProfileSeconds;

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
            // 0.9.0: no distance limit ("Within (m)" removed). He gives up at any range to a threat
            // he perceives: sees, just saw, or is under fire while he knows of him.
            SeenWithin = c.Bind(Natural, "Seen within (s)", 3f,
                Range("He must see the threat now, have seen him this recently, or be under fire while he knows of him (the game's own enemy memory). He gives up towards where he last saw him, never to a position he doesn't know.", 0f, 15f));
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
            LeaveDistance = c.Bind(Coverage, "Captor gone beyond (m)", 25f,
                Range("The captor walks away: further than this AND further than his closest approach plus the margin below. A surrender made from far away is never cancelled just for being far.", 5f, 100f));
            WalkAwayMargin = c.Bind(Coverage, "Captor walks away by (m)", 12f,
                Range("How much further than his closest approach the captor must go before it counts as leaving.", 2f, 60f));
            AcknowledgeDistance = c.Bind(Coverage, "Surrender acknowledged within (m)", 25f,
                Range("A human captor who comes this close in sight, aims at him for a moment, or gives an order has taken the surrender. A bot captor acknowledges it by taking custody.", 3f, 100f));
            OfferMin = c.Bind(Coverage, "Unanswered surrender lasts min (s)", 15f,
                Range("Nobody has acknowledged his surrender: he stays visibly compliant this long (longer while the captor closes in), then seeks safety or reassesses.", 3f, 120f));
            OfferMax = c.Bind(Coverage, "Unanswered surrender lasts max (s)", 35f, Range("", 3f, 240f));
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
            OpeningDelay = c.Bind(Opening, "Opening must last (s)", 1.5f,
                Range("For gradual openings (you look away, he loses sight of you) he waits this long on top of his reaction time to be sure. Clear ones (you reload, open your inventory, put your gun away) only take his reaction time.", 0f, 10f));
            BreakInterval = c.Bind(Opening, "Roll every (s)", 1f, Range("While an opening lasts he reassesses this often (varied +-30% each time).", 0.1f, 10f));
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
            ReactionMin = c.Bind(Opening, "Reaction time min (s)", 0.35f,
                Range("Each bot rolls his own reaction time once per raid, between min and max. A change he can see (you reload, look away, come closer, leave) reaches his decisions only after it: quicker with high nerve, slower when hurt, varied every time.", 0.05f, 3f));
            ReactionMax = c.Bind(Opening, "Reaction time max (s)", 0.9f, Range("", 0.05f, 3f));
            Quickdraw = c.Bind(Opening, "Quickdraw sidearm", true,
                "A sidearm drawn when he resists uses the game's own pistol quickdraw speed (the fast weapon switch players get), instead of the slow draw from empty hands. Primaries always use their normal draw.");
            DrawHandoffTimeout = c.Bind(Opening, "Draw hand-off limit (s)", 3f,
                Range("While he draws, the mod keeps him (facing you, not firing) until the game reports the weapon ready, then hands him to the normal AI. This is the most it waits.", 0.5f, 10f));
            DrawRetrySeconds = c.Bind(Opening, "Retry a refused draw for (s)", 1.5f,
                Range("Once he has decided to draw, a draw the game refuses (hands still busy) is tried again every check for this long, without a new decision.", 0f, 5f));
            SquadBoost = c.Bind(Opening, "Squad nearby boost", 0.5f,
                Range("Free squadmates close by (Allies within, section 04) make him up to 1 + this more likely to act on an opening.", 0f, 2f));
            HideWeight = c.Bind(Opening, "Weight: slip into cover", 0.6f,
                Range("Ducking out of sight nearby instead of running far. Likelier for low nerve, cautious personalities and bad legs; needs a reachable hidden spot.", 0f, 5f));
            IntimidationSeconds = c.Bind(Opening, "Warning shot holds him for (s)", 6f,
                Range("A shot from you that passes close (not a hit: a hit makes him run) keeps him still this long, scaled by nerve.", 0f, 30f));
            OrderCompliance = c.Bind(Opening, "After an order he complies for (s)", 3f,
                Range("A fresh order holds him to it this long (varied) before he looks for an opening again.", 0f, 15f));
            ApproachStep = c.Bind(Opening, "Coming closer counts after (m)", 2f,
                Range("You closing this much distance (within 8 m) is noticed and may get a reaction.", 0.5f, 10f));

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
            NumberKeys = c.Bind(Interact, "Number keys give orders", true,
                "While the menu is open, 1-9 give the numbered order and 0 closes it. The game's hotbar (weapons, quick slots) ignores those keys while the menu is open, and a key used for the menu stays away from the hotbar until you release it. Your key bindings are never changed.");
            NumpadKeys = c.Bind(Interact, "Numpad keys too", true, "Numpad 1-9 / 0 work like the top-row numbers (same hotbar protection).");
            WheelSelect = c.Bind(Interact, "Mouse wheel selects", true, "While the menu is open the mouse wheel moves the highlight (and doesn't change your walking speed).");
            MenuScale = c.Bind(Interact, "Menu size", 1f, Range("On top of the automatic scaling to your screen height.", 0.6f, 2f));

            // 12 Reactions and voice
            LocalReactions = c.Bind(Feedback, "Other held bots react", true, "A held bot who sees another one shot runs.");
            WitnessRadius = c.Bind(Feedback, "Sees it within (m)", 40f, Range("", 5f, 150f));
            WitnessEscape = c.Bind(Feedback, "Chance he runs", 1f, Range("", 0f, 1f));
            BotVoice = c.Bind(Feedback, "Bot voice lines", true, "The game's own lines (hold fire, roger, negative...).");
            VoiceCooldown = c.Bind(Feedback, "Voice cooldown (s)", 12f, Range("", 1f, 120f));
            Hints = c.Bind(Feedback, "On-screen hints", true, "A small prompt when you can give orders, and debug confirmations.");
            Chatter = c.Bind(Feedback, "Talk while held", true,
                "A held bot sometimes speaks on his own (mutters, asks for help, a defiant remark) and replies to what you do (coming closer, a warning shot, looking back at him). Lines his voice lacks are skipped silently.");
            ChatterMin = c.Bind(Feedback, "Unprompted line every min (s)", 18f, Range("How often he gets a chance to speak on his own, between min and max.", 5f, 300f));
            ChatterMax = c.Bind(Feedback, "Unprompted line every max (s)", 45f, Range("", 5f, 300f));
            ChatterChance = c.Bind(Feedback, "Unprompted line chance", 0.5f, Range("Chance he actually says something at each of those moments.", 0f, 1f));
            ReplyChance = c.Bind(Feedback, "Reply chance", 0.6f, Range("Chance he reacts out loud to something you do (approach, warning shot, looking back at him).", 0f, 1f));
            DefiantRemarks = c.Bind(Feedback, "Defiant remarks", true,
                "Bold bots (high nerve, fighter personality) sometimes insult you instead. Only Scav voices have such lines (Toxic); PMCs stay silent.");
            ChatterRadius = c.Bind(Feedback, "Take turns within (m)", 25f, Range("Held bots this close to each other don't talk over one another.", 0f, 100f));
            ChatterGap = c.Bind(Feedback, "Gap between speakers (s)", 1.5f, Range("", 0f, 10f));
            Acknowledge = c.Bind(Feedback, "Acknowledge orders", true, "He answers an order out loud (roger, going; negative if he can't) after a short, varied delay.");
            AckDelayMin = c.Bind(Feedback, "Answer delay min (s)", 0.4f, Range("", 0f, 5f));
            AckDelayMax = c.Bind(Feedback, "Answer delay max (s)", 1.2f, Range("", 0f, 5f));
            CommandSpeech = c.Bind(Feedback, "Speak your orders", false,
                "Off by default. On: your character says a matching line from your own voice when you give an order from the menu (the menu still gives the order once, spoken or not). Never counts as a Stop demand and isn't heard by other bots as a command.");

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
            EmergencyLowerSeconds = c.Bind(Pose, "Fast lower time (s)", 0.08f, Range("Shot, bolting, or scrambling for his gun.", 0f, 0.5f));
            DrawLowerSeconds = c.Bind(Pose, "Draw lower time (s)", 0.22f,
                Range("When he goes for a gun or knife he kept: the arms come down this fast while the game's draw animation starts underneath (they overlap; no snap).", 0f, 1f));
            PoseStrength = c.Bind(Pose, "Pose strength", 1f, Range("Below 1 lets breathing and sway show through the arms.", 0f, 1f));
            TrembleDegrees = c.Bind(Pose, "Tremble (deg)", 0.8f, Range("0 turns it off.", 0f, 5f));
            TrembleSpeed = c.Bind(Pose, "Tremble speed", 6f, Range("", 0f, 20f));
            Stance = c.Bind(Pose, "Stance with hands up", 1f, Range("1 = standing, 0 = full crouch.", 0f, 1f));
            FaceCaptor = c.Bind(Pose, "Face the player", true, "Turns to face whoever holds him.");
            Wiggle = c.Bind(Pose, "Wiggle", true,
                "Now and then a held bot leans left and right with his hands up (the friendly wiggle). Uses the game's own lean; never moves him.");
            WiggleChance = c.Bind(Pose, "Wiggle chance", 0.15f,
                Range("Per opportunity, before personality and nerve (cowards x1.6 .. Wreckless x0.3; nerve 0 x1.3 .. nerve 1 x0.7). Opportunities: once shortly after he settles with his hands up, and when you look back at him after an opening, at most one per Wiggle sample gap.", 0f, 1f));
            WiggleMaxChance = c.Bind(Pose, "Wiggle max chance", 0.35f, Range("Cap on the final chance per opportunity.", 0f, 1f));
            WiggleCooldown = c.Bind(Pose, "Wiggle cooldown (s)", 90f, Range("Per bot, kept across recaptures; starts when a wiggle is chosen.", 0f, 900f));
            WiggleStrength = c.Bind(Pose, "Wiggle strength", 0.6f, Range("Fraction of a full lean.", 0.1f, 1f));
            WiggleSwings = c.Bind(Pose, "Wiggle swings", 4, IntRange("Leans, alternating sides, then back to upright.", 2, 8));
            WiggleSwingSeconds = c.Bind(Pose, "Wiggle swing time (s)", 0.3f, Range("Per lean; the game smooths between them.", 0.15f, 1f));
            WiggleDelayMin = c.Bind(Pose, "Wiggle after settling min (s)", 0.8f, Range("Delay after his arms are fully up before the first opportunity.", 0f, 10f));
            WiggleDelayMax = c.Bind(Pose, "Wiggle after settling max (s)", 2.5f, Range("", 0f, 10f));
            WiggleWindow = c.Bind(Pose, "Wiggle window (s)", 4f, Range("A chosen wiggle waits at most this long for a suitable moment (standing still, facing you, room to lean), else it is skipped.", 0.5f, 20f));
            WiggleSampleGap = c.Bind(Pose, "Wiggle sample gap (s)", 20f, Range("Minimum time between two wiggle rolls for one bot.", 1f, 300f));
            WiggleClearance = c.Bind(Pose, "Wiggle clearance (m)", 0.45f, Range("Free space needed on both sides at shoulder height; a wall closer than this skips the wiggle.", 0.1f, 1.5f));
            Defiant = c.Bind(Pose, "Middle finger", true,
                "A held bot may flip you off once: the game's own middle-finger gesture (GetOffGesture) on his left hand while his right hand stays up. Purely a gesture: no fight, no draw, no escape, and no bot hears it as a gesture.");
            DefiantChance = c.Bind(Pose, "Middle finger chance", 0.05f,
                Range("Per surrender, rolled once after he settles. Final chance = this x (1 + influence x (personality x nerve - 1)), capped at the max. Personality: Wreckless 2.0, GigaChad 1.8, Chad 1.5, SnappingTurtle 1.3, others 1, Rat 0.6, Timmy 0.4, Coward 0.3; nerve 0 x0.5 .. nerve 1 x1.5.", 0f, 1f));
            DefiantInfluence = c.Bind(Pose, "Middle finger personality influence", 1f, Range("0: every bot uses the plain chance; 1: full personality and nerve bias.", 0f, 1f));
            DefiantMaxChance = c.Bind(Pose, "Middle finger max chance", 0.15f, Range("Cap on the final per-surrender chance.", 0f, 1f));
            DefiantCooldown = c.Bind(Pose, "Middle finger cooldown (s)", 600f, Range("Per bot, kept across recaptures; starts when it is chosen.", 0f, 3600f));
            DefiantDelayMin = c.Bind(Pose, "Middle finger after settling min (s)", 3f, Range("When the roll happens after his arms are fully up, between min and max.", 0f, 60f));
            DefiantDelayMax = c.Bind(Pose, "Middle finger after settling max (s)", 10f, Range("", 0f, 60f));
            DefiantWindow = c.Bind(Pose, "Middle finger window (s)", 8f, Range("A chosen gesture waits at most this long for a suitable moment (still, facing you, you watching), else it is skipped.", 1f, 60f));
            GestureBlend = c.Bind(Pose, "Gesture arm blend (s)", 0.15f, Range("How quickly the gesturing arm leaves and rejoins the hands-up pose.", 0.05f, 1f));

            // 14 Other mods
            SainPersonality = c.Bind(Compat, "Read SAIN personality", true,
                "Cowards and Timmys give up sooner and run more, Chads and GigaChads hold out and fight more. No effect without SAIN.");
            SainSuppression = c.Bind(Compat, "Read SAIN suppression", true,
                "Uses SAIN's suppression level as well as the game's own incoming-fire signal (the higher counts). No effect without SAIN.");
            PauseWhenAsleep = c.Bind(Compat, "Pause while a bot is asleep", true,
                "If another mod (ORBIT's Ghost Mode) puts a held bot to sleep, he drops any plan to fetch a gun and decides again when he wakes.");
            OrbitKeepAwake = c.Bind(Compat, "Keep a bot-held encounter awake (ORBIT)", true,
                "While a bot holds a prisoner, ORBIT is asked not to put those two to sleep (OrbitBotControl.SetBotSleepAllowed, ORBIT 3.1.1+), and allowed again when custody ends. Only bots already awake; nobody is woken. No effect without ORBIT.");
            TeammatesSurrender = c.Bind(Compat, "Your teammates can surrender", false,
                "Off: bots allied to you (your group, Pit Fireteam teammates) never give up on their own, so they stay under your orders. Debug keys still work on them.");

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
            SurrenderToBotKey = c.Bind(Debug, "Surrender to his enemy", new KeyboardShortcut(KeyCode.LeftBracket, KeyCode.LeftControl),
                "Debug keys on: the bot under your crosshair gives up to the bot he is fighting (his current target), and that bot decides whether to take him.");
            ForceGrabKey = c.Bind(Debug, "Force gun grab", new KeyboardShortcut(KeyCode.RightBracket, KeyCode.LeftControl),
                "Deterministic test: the held bot under your crosshair scrambles for his dropped gun now (no roll), if it's there and reachable.");
            Overlay = c.Bind(Debug, "Overlay", false, "Lists every held bot: phase, order, nerve, arms.");
            VerboseLogs = c.Bind(Debug, "Verbose logs", false, "Logs every roll, including the ones that change nothing.");
            NerveOverride = c.Bind(Debug, "Override nerve", false, "Every bot uses the value below instead of his own roll, live.");
            NerveOverrideValue = c.Bind(Debug, "Nerve value", 0.5f, Range("", 0f, 1f));
            ErrorLogCooldown = c.Bind(Debug, "Repeat errors every (s)", 10f, Range("The same error is logged at most this often.", 1f, 120f));
            LogDecisions = c.Bind(Debug, "Log decisions", true,
                "Each choice a held bot makes (act on an opening, react to you leaving) with its main reasons. Rolls where he stays put only with Verbose logs.");
            LogGestures = c.Bind(Debug, "Log gestures", true, "Wiggle rolls, starts, completions, skips and interruptions.");
            LogAttention = c.Bind(Debug, "Log attention", false, "Each glance (what at, why) and each change of demeanor. Demeanor choices are also logged with Log decisions.");
            KneelTestEscape = c.Bind(Debug, "Kneel test escape outcome", KneelEscapeChoice.Auto,
                "When a bot escaping from his knees is covered again (aim at him while he rises from a forced run, Ctrl+Alt+K with Kneel test rise Escape): force what he does. Auto: his own roll.");
            LogVoice = c.Bind(Debug, "Log voice", true,
                "Every line a held bot (or you, for orders) says, and whether audio actually started (or why not: no such line in his voice, out of earshot, speaker busy).");
            // 15 Bots taking prisoners
            BotCaptors = c.Bind(BotCaptor, "Bots can take prisoners", true,
                "A Scav or PMC can give up to a hostile bot, not only to you (same pressure model; his target must be a bot he sees or just saw). The bot he gives up to decides whether to take him, spare him or keep fighting.");
            CaptorPriority = c.Bind(BotCaptor, "Captor layer priority (restart)", 70,
                IntRange("BigBrain priority of the layer a bot uses while guarding a prisoner. Below SAIN's avoid-threat layer (80) and the surrender layer; above SAIN's combat layers (20-24).", 1, 200));
            MaxAiEncounters = c.Bind(BotCaptor, "Bot-held prisoners at once", 4, IntRange("Across the map. Further surrenders to bots are spared or refused instead of taken.", 0, 16));
            BotSurrenderScale = c.Bind(BotCaptor, "Surrender chance against a bot", 0.85f,
                Range("Multiplies the chance of giving up when the threat is a bot (you: 1). Scavs still give up far more readily than PMCs (section 05).", 0f, 2f));
            AcceptChance = c.Bind(BotCaptor, "Takes a prisoner", 0.65f,
                Range("When it is practical (gun with rounds, not under fire, no other threat in sight, prisoner in sight and close): the chance he takes custody, before personality and his own disposition.", 0f, 1f));
            SpareChance = c.Bind(BotCaptor, "Spares if not taking him", 0.75f,
                Range("Not taking custody (impractical, busy, or declined): the chance he still holds his fire at him instead of fighting on. Being unable to guard him never by itself means killing him.", 0f, 1f));
            AcceptRange = c.Bind(BotCaptor, "Guards from up to (m)", 20f,
                Range("Custody (control), not surrender: farther than this he won't take custody, but the surrender itself can happen at any range and he may still spare him.", 4f, 80f));
            HoldMin = c.Bind(BotCaptor, "Holds a prisoner min (s)", 25f, Range("Then he lets him go (or earlier, by his plan). Never indefinite.", 5f, 300f));
            HoldMax = c.Bind(BotCaptor, "Holds a prisoner max (s)", 70f, Range("", 5f, 600f));
            OrderGapMin = c.Bind(BotCaptor, "Between his orders min (s)", 2.5f, Range("", 0.5f, 30f));
            OrderGapMax = c.Bind(BotCaptor, "Between his orders max (s)", 7f, Range("", 0.5f, 60f));
            AiOrderRange = c.Bind(BotCaptor, "Bot order range (m)", 15f, Range("A bot's order is only given within this range and in sight (the same checks as yours).", 3f, 30f));
            CaptorApproachFrom = c.Bind(BotCaptor, "Walks up to a prisoner from (m)", 60f,
                Range("A captor farther than his order range (or without a line to his prisoner) walks up along a complete path, only while nothing threatens him; custody beyond 'Guards from up to' is taken only when he can walk up. 0: never walks up.", 0f, 120f));
            CaptorGuardDistance = c.Bind(BotCaptor, "Guards from (m)", 8f,
                Range("Where a captor who walks up stops (never closer than this unless he lost sight of the prisoner).", 3f, 20f));
            CaptorApproachSpeed = c.Bind(BotCaptor, "Walk-up speed", 0.8f, Range("0-1 of his walking speed (never a sprint).", 0.3f, 1f));
            BusyRelease = c.Bind(BotCaptor, "Gives up custody after busy (s)", 12f,
                Range("Pulled into another fight (under fire, another enemy in sight) for this long, he stops guarding; the prisoner is then on his own and the restraint stays as decided.", 2f, 120f));
            AlliedCaptors = c.Bind(BotCaptor, "Your allies can make bots surrender", true,
                "Bots in a group allied to you (BotsGroup.IsAlly) can cause a surrender and hold fire, but never get the guarding layer: their movement stays with their own mod and your orders. You can take over their prisoner with the order menu.");
            CaptorSpeech = c.Bind(BotCaptor, "Bots speak their orders", true,
                "Captors talk: surrender demands, orders, warnings, new threats, hand-overs. Lines from the bot's own voice, queued, never blocking anything (the order is applied either way).");
            CaptorVoiceGap = c.Bind(BotCaptor, "Captor line gap (s)", 3.5f, Range("Least time between two non-urgent lines of one captor.", 0.5f, 30f));
            CaptorKneelChance = c.Bind(BotCaptor, "Captors put prisoners on their knees", 0.4f,
                Range("Chance a bot captor's plan orders his prisoner to kneel instead of just stay (needs Kneel on order). He is told to get up calmly when let go.", 0f, 1f));
            CaptorCloseChance = c.Bind(BotCaptor, "Captors close in on a disarmed prisoner", 0.55f,
                Range("Chance (once per custody) that a captor, once the prisoner has his hands up and has dropped the gun in his hands, walks closer to give orders and control him. Only with nothing threatening him, along a complete path, gun on the prisoner; stops on any threat.", 0f, 1f));
            CaptorStandoff = c.Bind(BotCaptor, "Close-in distance (m)", 3.5f,
                Range("Where a captor who closes in stops: room for both of them, out of arm's reach, never on the dropped gun.", 2.5f, 8f));
            CaptorCover = c.Bind(BotCaptor, "Captors take cover", true,
                "Under fire from a direction he can tell, a captor moves to a reachable spot hidden from it (keeping the prisoner in sight if he can) instead of standing exposed. A visible enemy always goes to the combat AI.");
            CaptorCoverRadius = c.Bind(BotCaptor, "Captor cover search (m)", 12f, Range("", 3f, 40f));
            PrisonerFollowsToCover = c.Bind(BotCaptor, "Prisoner follows into cover", true,
                "A captor going into cover orders a close prisoner to follow (hands up, his own path) and to stay once there. Only with a complete path and the prisoner in order range.");
            FollowToCoverWithin = c.Bind(BotCaptor, "Follows into cover within (m)", 10f, Range("", 2f, 25f));
            QuietBeforeGuarding = c.Bind(BotCaptor, "Back to guarding after quiet (s)", 2.5f,
                Range("After a fight he goes back to guarding only once no enemy is in sight and no fire came in for this long.", 0.5f, 20f));
            MinEngageSeconds = c.Bind(BotCaptor, "Fights at least (s)", 3f,
                Range("Once he turns to a threat he stays on it at least this long (no flip-flopping between guarding and fighting).", 0.5f, 20f));
            CaptorGlances = c.Bind(BotCaptor, "Captors look around", true,
                "A guarding captor glances away now and then (towards where he last knew an enemy, or around him). Each glance is a real opening the prisoner can see.");

            // 16 Sparing surrendered enemies
            SquadRestraint = c.Bind(RestraintSec, "Captor squad holds fire", true,
                "When a bot takes or spares a prisoner, his squadmates (same BotsGroup) within the notice radius hold their fire at that prisoner only, at once. Every other enemy is fought as normal.");
            SquadNoticeRadius = c.Bind(RestraintSec, "Squad hears it within (m)", 80f, Range("", 5f, 300f));
            SquadRejectChance = c.Bind(RestraintSec, "Squadmate refuses anyway", 0.02f,
                Range("Chance a squadmate doesn't go along with his squad's decision (logged with the reason). 0 forbids it.", 0f, 1f));
            ObserverRestraint = c.Bind(RestraintSec, "Other bots may spare him", true,
                "Bots outside the captor's squad who see a man with his hands up decide for themselves (once, kept for a while): hold fire, ignore him for another threat, let him leave, or fight.");
            ObserverRadius = c.Bind(RestraintSec, "Notices a surrender within (m)", 50f, Range("", 5f, 150f));
            ObserverRespect = c.Bind(RestraintSec, "Holds fire at a compliant man", 0.8f,
                Range("Base chance a bot holds fire at a man standing with his hands up, before personality, disposition, danger and what he has seen him do.", 0f, 1f));
            FleeingRespect = c.Bind(RestraintSec, "Lets an unarmed man run", 0.7f,
                Range("Base chance a bot that spared him lets him run (unarmed) instead of firing. Drawing a weapon or going for a gun is never running.", 0f, 1f));
            RestraintMemory = c.Bind(RestraintSec, "Keeps his decision (s)", 120f,
                Range("A decision about a surrendered man is kept this long unless something meaningful changes (he draws, hits someone, runs).", 10f, 900f));
            WatchSeconds = c.Bind(RestraintSec, "Squadmates watch him (s)", 15f,
                Range("Idle squadmates of the captor near the prisoner stand and watch him this long (0: never).", 0f, 120f));
            LogRestraint = c.Bind(RestraintSec, "Log restraint decisions", true,
                "Every decision (holds fire / ignores / lets go / refuses) with its reasons. A restrained bot that tries to shoot anyway is always logged as RESTRAINT LEAK.");

            Profile = c.Bind(Debug, "Profile", false,
                "Off by default; costs nothing when off. On: logs frame times (avg, p50, p99, worst, 1% low), GC activity, our own cost per hook, tracked object counts, and any frame where our code took over 1 ms. For comparing runs with Enabled on and off.");
            ProfileSeconds = c.Bind(Debug, "Profile report every (s)", 10f, Range("", 2f, 120f));
            LogKneel = c.Bind(Debug, "Log kneeling", true, "Kneel starts, finishes (with a timeline: seconds, frames, frame times), reversals, fallbacks with the reason, interruptions.");
            KneelGizmos = c.Bind(Debug, "Kneel gizmos", false, "With the overlay on: marks the solved knees (K), feet (F) and hands (H) of kneeling bots on screen.");
            KneelKey = c.Bind(Debug, "Kneel test: kneel", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl),
                "Debug keys on: the held bot under your crosshair gets the Get down order (the menu's own path, from any distance).");
            StandKey = c.Bind(Debug, "Kneel test: stand", new KeyboardShortcut(KeyCode.K, KeyCode.LeftShift), "The On your feet order, same way.");
            KneelLoopKey = c.Bind(Debug, "Kneel test: loop", new KeyboardShortcut(KeyCode.K, KeyCode.LeftAlt),
                "Kneel, hold, stand, hold, repeated the number of times below. Any real order stops it.");
            KneelInterruptKey = c.Bind(Debug, "Kneel test: interrupt", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl, KeyCode.LeftShift),
                "Starts the transition chosen below and interrupts it at the chosen point with the chosen event.");
            KneelTestCycles = c.Bind(Debug, "Kneel test cycles", 3, IntRange("", 1, 10));
            KneelTestInterrupt = c.Bind(Debug, "Kneel test interruption", KneelInterrupt.Reverse,
                "Reverse: the opposite order. Hit: a SIMULATED hit (no damage). Run: forced run. Draw: forced draw of a gun he carries. Kill: kills him (checks the corpse is left alone).");
            KneelTestDuring = c.Bind(Debug, "Kneel test interrupts", KneelInterruptDuring.GoingDown, "Which transition the interruption test runs.");
            KneelTestAt = c.Bind(Debug, "Kneel test interrupt at", 0.5f, Range("How far into that transition (0 start .. 1 end).", 0.02f, 0.98f));
            KneelUrgentKey = c.Bind(Debug, "Kneel test: urgent rise", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl, KeyCode.LeftAlt),
                "A kneeling bot under the crosshair gets up for real: Escape (Kneel test rise) = forced run; Defensive = forced draw, or a SIMULATED hit with no gun. For an opening he decides on himself, use Give them an opening.");
            KneelTestStyle = c.Bind(Debug, "Kneel test style", KneelStyleChoice.Auto, "Forces the descent style for every kneel (preview each one). Auto: chosen by the bot.");
            KneelTestRise = c.Bind(Debug, "Kneel test rise", KneelRiseChoice.Auto, "Forces the rise for an ordered stand-up (and picks the urgent rise key's kind). Auto: compliant for orders.");
            KneelTestBrace = c.Bind(Debug, "Kneel test brace", KneelBraceChoice.Auto, "Always / Never forces the knee brace on rises.");
            KneelPoseIsolate = c.Bind(Debug, "Kneel pose isolate", HandsAreHigh.KneelPoseIsolate.All,
                "Turns one contributor of the kneeling pose off to see which one distorts it: LookAlign (the bot's look kept on his planted body, so the game's trunk twist stays at 0), PelvisSquare, TorsoSquare, LegHinge (legs posed on their own knee hinge instead of swung), Head (head and torso look), Hands (hands behind the head), BodyOnly (legs, pelvis and torso only). All: everything on (normal).");
            LogPose = c.Bind(Debug, "Log pose diagnostics", false,
                "Once a second per kneeling bot: the game's look-to-body angle (what twists the animated trunk), root yaw vs where he knelt, hip-line and shoulder-line yaw, our torso and head turn, and how far the head is turned from the chest.");
            KneelBracePalmFlip = c.Bind(Debug, "Kneel brace palm flip", false,
                "Turns the bracing hand over (180 deg about the fingers). Only if the palm shows the back of the hand on the thigh: which side of the rig's palm bone faces down was inferred from the thumb root, not seen in game.");
            BindKneel(c);
            BindScenarios(c);
            BindRealism(c);
        }

        private static void BindKneel(ConfigFile c)
        {
            KneelEnabled = c.Bind(KneelSec, "Kneel on order", true,
                "On: the Get down order puts him on both knees with his hands behind his head, and On your feet brings him back up. Off (or where the ground or space doesn't allow it): the crouch with hands up of earlier versions.");
            KneelDownSeconds = c.Bind(KneelSec, "Kneel down time (s)", 2.3f, Range("Steady descent: one foot back, a knee down, the other beside it, hands behind his head.", 0.8f, 5f));
            KneelFrightenedSeconds = c.Bind(KneelSec, "Frightened kneel time (s)", 1.3f, Range("Frightened descent: short step, both knees down nearly together, hands up to his head early, hunched.", 0.6f, 4f));
            KneelOneKneeSeconds = c.Bind(KneelSec, "One knee first time (s)", 2.8f, Range("Down on one knee, a pause there, then the other.", 1f, 6f));
            KneelWeightSteady = c.Bind(KneelSec, "Style weight steady", 1f, Range("Relative chance of each descent before personality, nerve, wounds and kit adjust it. 0 turns a style off.", 0f, 5f));
            KneelWeightFrightened = c.Bind(KneelSec, "Style weight frightened", 1f, Range("", 0f, 5f));
            KneelWeightOneKnee = c.Bind(KneelSec, "Style weight one knee first", 0.8f, Range("", 0f, 5f));
            KneelUpSeconds = c.Bind(KneelSec, "Stand up time (s)", 1.7f, Range("Compliant rise (ordered up, let go): hands off the head, front foot planted, a push up over it, feet together, hands up.", 0.6f, 5f));
            KneelDefensiveSeconds = c.Bind(KneelSec, "Defensive stand time (s)", 0.9f, Range("Prompt, guarded rise when he draws or reacts to danger (the draw runs at the same time).", 0.4f, 3f));
            KneelQuickSeconds = c.Bind(KneelSec, "Quick stand time (s)", 0.7f,
                Range("Escape rise (bolting, scrambling, slipping away, shot): his fastest. His legs are handed back as soon as they're under him, so his first step follows at once; sprinting starts 0.14 s later. Wounds, a bad leg and heavy kit slow every rise.", 0.4f, 2f));
            KneelBrace = c.Bind(KneelSec, "Brace on the knee when rising", true,
                "The hand on the front side pushes on the front thigh while he rises over that leg. Bones only: no item, no inventory action, nothing on the hands controller. A hand planted on the ground is not offered: from a kneel the ground is out of an arm's reach unless he folds almost flat.");
            KneelBraceChance = c.Bind(KneelSec, "Brace chance", 0.55f, Range("For a compliant rise; more for an escape and for the wounded, less when he's drawing.", 0f, 1f));
            KneelStrength = c.Bind(KneelSec, "Leg pose strength", 1f, Range("1: full kneel. Lower blends towards the game's crouch (for comparison while tuning).", 0f, 1f));
            KneelLean = c.Bind(KneelSec, "Torso lean (deg)", 4f, Range("Forward lean of the straightened back while kneeling.", -10f, 20f));
            KneelLeanScale = c.Bind(KneelSec, "Transition lean scale", 1f, Range("Scales each style's forward lean in the middle of a transition (balance over the planted foot).", 0f, 2f));
            KneeHeight = c.Bind(KneelSec, "Knee height (m)", 0.09f, Range("Knee joint above the ground when kneeling. Raise if knees sink, lower if they float.", 0.03f, 0.2f));
            KneelThighAngle = c.Bind(KneelSec, "Thigh angle (deg)", 8f, Range("Thighs forward of vertical; more puts the knees further ahead of the hips.", 0f, 30f));
            KneeSpread = c.Bind(KneelSec, "Knee spread (m)", 0.04f, Range("Knees this much wider than the hips, each side.", 0f, 0.2f));
            KneelHipsForward = c.Bind(KneelSec, "Hips forward (m)", 0f, Range("Hips ahead (+) or behind (-) of where he stands.", -0.2f, 0.2f));
            HandsBehindHeadStrength = c.Bind(KneelSec, "Hands behind head strength", 1f, Range("0 keeps the hands-up arms while kneeling.", 0f, 1f));
            HandsBehindHead = c.Bind(KneelSec, "Hands behind head (m)", 0.11f, Range("How far behind the head the palms rest.", 0.04f, 0.25f));
            HandsAboveNeck = c.Bind(KneelSec, "Hands above neck (m)", 0.08f, Range("Palms this far above the head bone (which sits at the top of the neck).", 0f, 0.2f));
            ElbowsOut = c.Bind(KneelSec, "Elbows out", 1f, Range("How far the elbows point out to the sides.", 0.3f, 1.5f));
            HelmetExtra = c.Bind(KneelSec, "Helmet extra (m)", 0.035f, Range("Hands kept this much further from the head when he wears a helmet.", 0f, 0.1f));
            KneelMaxStep = c.Bind(KneelSec, "Max ground step (m)", 0.12f,
                Range("Ground under each knee and foot must be within this of where he stands, or he doesn't kneel (stairs, ledges, rubble).", 0.03f, 0.3f));
            KneelMaxSlope = c.Bind(KneelSec, "Max slope (deg)", 20f, Range("Steeper ground under a knee or foot: he doesn't kneel.", 5f, 40f));
            KneelFallbackCrouch = c.Bind(KneelSec, "Crouch when kneeling is impossible", false,
                "Where kneeling isn't possible (ground, walls, both legs hurt, no spot with room nearby): on, he crouches with his hands up; off (default from 0.13.0), he stays standing with his hands up. Either way the reason is logged and shown.");
            KneelReposition = c.Bind(KneelSec, "Step to a spot with room", true,
                "No room or ground to kneel where he stands (a wall at his back, stairs, a slope, kit that won't fit): he looks once round him for a spot that fits and walks there with his hands up, facing you, then kneels. At most two short walks per order; otherwise he stays as the setting above says and the reason is shown.");
            KneelSpotRadius = c.Bind(KneelSec, "Room search radius (m)", 2f,
                Range("How far from where he stands a spot to kneel is looked for (four rings of eight spots, nearest first).", 0.5f, 4f));

            DemeanorEnabled = c.Bind(PresenceSec, "Demeanor styles", true,
                "Each held bot carries himself in one of four ways for the encounter (resigned, frightened, cautious, defiant), from nerve, SAIN personality, wounds, incoming fire and squadmates near (faction only a mild tendency). It shapes how he kneels and rises, where he looks, how much he fidgets and flinches and what he says. Presentation only: it never changes whether he complies, breaks or fights.");
            DemeanorStrength = c.Bind(PresenceSec, "Demeanor strength", 1f, Range("How different the styles look from each other. 0: everyone neutral.", 0f, 1.5f));
            HeadMovement = c.Bind(PresenceSec, "Head movement", true,
                "Held bots watch their captor and now and then glance at what they know about: their dropped gun, where fire came from, a squadmate in sight, a way out, the ground. A glance holds briefly and returns to the captor; it never means he is about to act.");
            GlanceMin = c.Bind(PresenceSec, "Glance every min (s)", 3f, Range("Time between glances, between min and max, scaled by demeanor (defiant men hold your eyes longer).", 1f, 30f));
            GlanceMax = c.Bind(PresenceSec, "Glance every max (s)", 9f, Range("", 1f, 60f));
            HeadMaxYaw = c.Bind(PresenceSec, "Head turn max (deg)", 70f, Range("Left/right from his body. Standing. Kneeling uses the two Kneeling ... max settings below.", 20f, 85f));
            HeadMaxDown = c.Bind(PresenceSec, "Head down max (deg)", 35f, Range("", 5f, 60f));
            HeadMaxUp = c.Bind(PresenceSec, "Head up max (deg)", 20f, Range("At most 12 deg with his hands behind his head.", 0f, 40f));
            HeadSpeed = c.Bind(PresenceSec, "Head speed (deg per s)", 200f, Range("Top speed of a head turn (eased in and out).", 40f, 600f));
            TorsoAssist = c.Bind(PresenceSec, "Torso turn max (deg)", 15f, Range("Standing: a look further than 25 deg to the side turns his upper back by up to this much; never his hips. Kneeling: Kneeling torso turn max.", 0f, 30f));
            KneelIdle = c.Bind(PresenceSec, "Kneeling idle movement", true,
                "While he kneels: an occasional small weight shift of the hips and settling of the elbows, on his own random timing; knees and feet stay put. The game's own breathing is kept (not doubled).");
            KneelIdleStrength = c.Bind(PresenceSec, "Kneeling idle strength", 1f, Range("", 0f, 2f));
            Flinch = c.Bind(PresenceSec, "Flinch at near misses", true,
                "A round passing close to a held bot's head (anyone's, within the near-miss radius, nothing in between) makes him duck his head and hunch his shoulders for half a second. Cosmetic: a hit is handled as a hit, and nothing waits for the flinch.");
            FlinchStrength = c.Bind(PresenceSec, "Flinch strength", 1f, Range("", 0f, 1.5f));
            KneelHeadYaw = c.Bind(PresenceSec, "Kneeling head turn max (deg)", 55f,
                Range("While kneeling with his hands behind his head: how far the head turns from his chest (the hands go round with it). Measured against his torso, not the world.", 20f, 70f));
            KneelTorsoYaw = c.Bind(PresenceSec, "Kneeling torso turn max (deg)", 12f,
                Range("While kneeling: how far his upper back may turn from his planted hips to help a look. Someone further round than head + torso (+ the corner of his eye) is out of his sight: he doesn't twist after him.", 0f, 25f));
            FlinchCooldown = c.Bind(PresenceSec, "Flinch cooldown (s)", 0.7f, Range("Automatic fire can't restart it faster than this; each flinch leaves the next weaker for a few seconds.", 0.2f, 5f));
        }

        // 17 Debug Scenarios
        internal static ConfigEntry<bool> ScenariosEnabled;
        internal static ConfigEntry<KeyboardShortcut> ScenarioKey;
        internal static ConfigEntry<KeyboardShortcut> ScenarioUp;
        internal static ConfigEntry<KeyboardShortcut> ScenarioDown;
        internal static ConfigEntry<KeyboardShortcut> ScenarioLess;
        internal static ConfigEntry<KeyboardShortcut> ScenarioMore;
        internal static ConfigEntry<KeyboardShortcut> ScenarioConfirm;
        internal static ConfigEntry<bool> ScenarioOverlay;
        internal static ConfigEntry<bool> ScenarioConcurrent;
        internal static ConfigEntry<int> ScenarioMaxBots;
        internal static ConfigEntry<float> ScenarioSpawnTimeout;
        internal static ConfigEntry<bool> ScenarioNoSpawnMagCheck;

        private static void BindScenarios(ConfigFile c)
        {
            ScenariosEnabled = c.Bind(ScenarioSec, "Scenario tools", false,
                "Off by default; does nothing at all when off. On: the scenario key below opens the scenario menu in a raid, to spawn a test encounter, watch it and clean it up. Debug only: it spawns bots.");
            ScenarioKey = c.Bind(ScenarioSec, "Scenario menu key", KeyboardShortcut.Empty,
                "Opens and closes the scenario menu (unbound by default: set one to use the tools).");
            ScenarioUp = c.Bind(ScenarioSec, "Menu up", new KeyboardShortcut(KeyCode.UpArrow), "While the scenario menu is open.");
            ScenarioDown = c.Bind(ScenarioSec, "Menu down", new KeyboardShortcut(KeyCode.DownArrow), "");
            ScenarioLess = c.Bind(ScenarioSec, "Menu previous value", new KeyboardShortcut(KeyCode.LeftArrow), "");
            ScenarioMore = c.Bind(ScenarioSec, "Menu next value", new KeyboardShortcut(KeyCode.RightArrow), "");
            ScenarioConfirm = c.Bind(ScenarioSec, "Menu activate", new KeyboardShortcut(KeyCode.Return),
                "Launches, fires an event, or runs the highlighted action. While the menu is open the hotbar and the mouse wheel are kept from the game (same guard as the order menu).");
            ScenarioOverlay = c.Bind(ScenarioSec, "Scenario overlay", true,
                "While a scenario runs: its participants, relationships, phases, reasons, forced values and a short timeline, top left.");
            ScenarioConcurrent = c.Bind(ScenarioSec, "Allow concurrent scenarios", false,
                "Off: a scenario can only be launched when none is running. On: several at once (each tracked and cleaned up on its own).");
            ScenarioMaxBots = c.Bind(ScenarioSec, "Scenario bots at most", 12,
                IntRange("Across all running scenarios. A launch that would exceed it is refused.", 1, 30));
            ScenarioSpawnTimeout = c.Bind(ScenarioSec, "Spawn timeout (s)", 30f,
                Range("A participant not spawned and active within this is reported as failed and the launch is cleaned up.", 5f, 120f));
            ScenarioNoSpawnMagCheck = c.Bind(ScenarioSec, "No magazine check on spawn", true,
                "Scenario bots only: the magazine check the game and SAIN both start the moment a bot spawns is scheduled for later instead, as if he had just checked. Raid bots are not touched.");
        }

        internal static float RollNerve()
        {
            float lo = Mathf.Min(NerveMin.Value, NerveMax.Value);
            float hi = Mathf.Max(NerveMin.Value, NerveMax.Value);
            return Dice.Range(lo, hi);
        }

        // The override applies live, to bots already met this raid too.
        internal static float EffectiveNerve(float rolled) => NerveOverride.Value ? NerveOverrideValue.Value : rolled;

        internal static float RegroupSeconds()
        {
            float lo = Mathf.Min(RegroupMin.Value, RegroupMax.Value);
            float hi = Mathf.Max(RegroupMin.Value, RegroupMax.Value);
            return Dice.Range(lo, hi);
        }

        internal static float Between(ConfigEntry<float> a, ConfigEntry<float> b) =>
            Dice.Range(Mathf.Min(a.Value, b.Value), Mathf.Max(a.Value, b.Value));
    }
}
