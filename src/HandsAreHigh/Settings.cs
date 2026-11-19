using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // Every tuning value, in numbered F12 sections. All of it applies live except the
    // layer priority, which BigBrain only reads once at startup.
    internal static class Settings
    {
        private const string General = "01. General";
        private const string NerveSec = "02. Nerve";
        private const string Natural = "03. Surrendering on their own";
        private const string Pressure = "04. Pressure";
        private const string Role = "05. Role and personality";
        private const string Coverage = "06. While held";
        private const string Retreat = "07. Retreat and recapture";
        private const string Recovery = "08. Regroup and rearm";
        private const string Hiding = "09. Hiding and movement";
        private const string Interact = "10. Orders";
        private const string Feedback = "11. Reactions and voice";
        private const string Pose = "12. Arm pose";
        private const string Compat = "13. Other mods";
        private const string Debug = "14. Debug";

        // 01
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> NaturalSurrender;
        internal static ConfigEntry<bool> IncludePmcs;
        internal static ConfigEntry<int> LayerPriority;
        // 02
        internal static ConfigEntry<float> NerveMin;
        internal static ConfigEntry<float> NerveMax;
        internal static ConfigEntry<float> HighNerve;
        // 03
        internal static ConfigEntry<float> EvalInterval;
        internal static ConfigEntry<float> SurrenderCooldown;
        internal static ConfigEntry<float> SurrenderDistance;
        internal static ConfigEntry<float> PressureThreshold;
        internal static ConfigEntry<int> RequiredFactors;
        internal static ConfigEntry<float> BaseChance;
        internal static ConfigEntry<float> MaxChance;
        internal static ConfigEntry<float> NerveResistance;
        // 04
        internal static ConfigEntry<float> InjuryThreshold;
        internal static ConfigEntry<float> InjuryWeight;
        internal static ConfigEntry<float> AmmoWeight;
        internal static ConfigEntry<float> OutnumberedWeight;
        internal static ConfigEntry<float> SquadWeight;
        internal static ConfigEntry<float> SuppressionWeight;
        internal static ConfigEntry<float> SquadMemory;
        internal static ConfigEntry<float> SuppressionMemory;
        internal static ConfigEntry<float> EnemyMemory;
        internal static ConfigEntry<float> AllyRadius;
        internal static ConfigEntry<float> OutnumberedRatio;
        // 05
        internal static ConfigEntry<float> ScavMultiplier;
        internal static ConfigEntry<float> PmcMultiplier;
        internal static ConfigEntry<float> PersonalityMin;
        internal static ConfigEntry<float> PersonalityMax;
        // 06
        internal static ConfigEntry<float> LeaveDistance;
        internal static ConfigEntry<float> LeaveSeconds;
        internal static ConfigEntry<float> WatchAngle;
        internal static ConfigEntry<float> OpeningDelay;
        internal static ConfigEntry<float> BreakInterval;
        internal static ConfigEntry<float> BreakChance;
        internal static ConfigEntry<float> KnifeRange;
        internal static ConfigEntry<float> SidearmRange;
        internal static ConfigEntry<bool> BoltWhenShot;
        internal static ConfigEntry<float> DropRetrySeconds;
        // 07
        internal static ConfigEntry<float> RetreatDistance;
        internal static ConfigEntry<float> Grace;
        internal static ConfigEntry<float> ResurrenderDistance;
        internal static ConfigEntry<float> AimTolerance;
        internal static ConfigEntry<float> AimConfirm;
        internal static ConfigEntry<float> RecaptureResistance;
        internal static ConfigEntry<float> RetreatTimeout;
        internal static ConfigEntry<float> ArrivalTolerance;
        // 08
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
        // 09
        internal static ConfigEntry<float> HideRecheck;
        internal static ConfigEntry<float> CoverRadius;
        internal static ConfigEntry<int> PathCandidates;
        internal static ConfigEntry<int> PathRetries;
        internal static ConfigEntry<float> StallSeconds;
        internal static ConfigEntry<float> ProgressMeters;
        // 10
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
        // 11
        internal static ConfigEntry<bool> LocalReactions;
        internal static ConfigEntry<float> WitnessRadius;
        internal static ConfigEntry<float> WitnessEscape;
        internal static ConfigEntry<bool> BotVoice;
        internal static ConfigEntry<float> VoiceCooldown;
        internal static ConfigEntry<bool> Hints;
        // 12
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
        // 13
        internal static ConfigEntry<bool> SainPersonality;
        internal static ConfigEntry<bool> PauseWhenAsleep;
        // 14
        internal static ConfigEntry<bool> DebugKeys;
        internal static ConfigEntry<KeyboardShortcut> SurrenderKey;
        internal static ConfigEntry<KeyboardShortcut> OpeningKey;
        internal static ConfigEntry<KeyboardShortcut> LeaveKey;
        internal static ConfigEntry<float> DebugKeyDistance;
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
                "Hurt, out of ammo, outnumbered or left alone, a bot may give up. Off: only the debug key starts a surrender.");
            IncludePmcs = c.Bind(General, "PMCs can surrender", true, "Off: only Scavs. Bosses and followers never surrender.");
            LayerPriority = c.Bind(General, "Layer priority (restart)", 95,
                IntRange("BigBrain priority. SAIN uses 20-24 and 80, ORBIT 19.", 1, 200));

            // 02 Nerve
            NerveMin = c.Bind(NerveSec, "Nerve min", 0f,
                Range("Each bot rolls nerve once per raid, between min and max. High nerve: slower to give up, quicker to try something.", 0f, 1f));
            NerveMax = c.Bind(NerveSec, "Nerve max", 1f, Range("", 0f, 1f));
            HighNerve = c.Bind(NerveSec, "High nerve from", 0.75f, Range("Bots at or above this will come back with only a knife.", 0f, 1f));

            // 03 Surrendering on their own
            EvalInterval = c.Bind(Natural, "Check every (s)", 5f, Range("How often each bot weighs giving up. One roll per check.", 1f, 60f));
            SurrenderCooldown = c.Bind(Natural, "Cooldown after a surrender (s)", 30f,
                Range("Once he's fighting again, how long before he can give up again.", 0f, 300f));
            SurrenderDistance = c.Bind(Natural, "Within (m)", 30f, Range("Only gives up to a player he can see this close.", 1f, 100f));
            PressureThreshold = c.Bind(Natural, "Pressure needed", 0.5f,
                Range("Sum of the weighted reasons (section 04) before he rolls at all.", 0f, 1f));
            RequiredFactors = c.Bind(Natural, "Reasons needed", 2, IntRange("How many separate reasons must apply at once.", 1, 5));
            BaseChance = c.Bind(Natural, "Base chance", 0.08f, Range("Per check, times pressure, nerve, role and personality.", 0f, 1f));
            MaxChance = c.Bind(Natural, "Max chance", 0.20f, Range("Per check, after everything.", 0f, 1f));
            NerveResistance = c.Bind(Natural, "Nerve resistance", 0.85f, Range("1: a bot with full nerve never gives up.", 0f, 1f));

            // 04 Pressure
            InjuryThreshold = c.Bind(Pressure, "Hurt below (health fraction)", 0.35f, Range("Where injury starts to count.", 0.05f, 1f));
            InjuryWeight = c.Bind(Pressure, "Injury weight", 0.35f, Range("", 0f, 1f));
            AmmoWeight = c.Bind(Pressure, "Out of ammo weight", 0.30f, Range("No rounds left for his gun and none to reload, or no gun at all.", 0f, 1f));
            OutnumberedWeight = c.Bind(Pressure, "Outnumbered weight", 0.20f, Range("", 0f, 1f));
            SquadWeight = c.Bind(Pressure, "Squad lost weight", 0.30f, Range("", 0f, 1f));
            SuppressionWeight = c.Bind(Pressure, "Under fire weight", 0.10f, Range("", 0f, 1f));
            SquadMemory = c.Bind(Pressure, "Squad loss counts for (s)", 30f, Range("", 1f, 180f));
            SuppressionMemory = c.Bind(Pressure, "Being hit counts for (s)", 5f, Range("", 0.5f, 30f));
            EnemyMemory = c.Bind(Pressure, "Enemy counts for (s)", 10f,
                Range("An enemy he saw this recently counts toward being outnumbered.", 1f, 60f));
            AllyRadius = c.Bind(Pressure, "Allies within (m)", 40f, Range("", 5f, 150f));
            OutnumberedRatio = c.Bind(Pressure, "Fully outnumbered at", 2f,
                Range("Enemies per friendly (counting himself) for the full outnumbered weight.", 1.1f, 6f));

            // 05 Role and personality
            ScavMultiplier = c.Bind(Role, "Scav multiplier", 1f, Range("", 0f, 3f));
            PmcMultiplier = c.Bind(Role, "PMC multiplier", 0.65f, Range("", 0f, 3f));
            PersonalityMin = c.Bind(Role, "Personality multiplier min", 0.5f, Range("Applied to the bravest SAIN personalities.", 0f, 3f));
            PersonalityMax = c.Bind(Role, "Personality multiplier max", 1.5f, Range("Applied to Cowards.", 0f, 3f));

            // 06 While held
            LeaveDistance = c.Bind(Coverage, "Captor gone beyond (m)", 25f, Range("Further than this while he can see you: he runs.", 5f, 100f));
            LeaveSeconds = c.Bind(Coverage, "Out of sight for (s)", 6f, Range("He can't see you this long: he runs.", 0.5f, 30f));
            WatchAngle = c.Bind(Coverage, "Covered within (deg)", 30f,
                Range("Looking further away than this counts as an opening. So do reloading, the inventory, no gun in hand and no line of sight.", 1f, 90f));
            OpeningDelay = c.Bind(Coverage, "Opening must last (s)", 1.5f, Range("", 0f, 10f));
            BreakInterval = c.Bind(Coverage, "Roll every (s)", 1f, Range("While there's an opening.", 0.1f, 10f));
            BreakChance = c.Bind(Coverage, "Break chance", 0.15f, Range("Per roll, times his nerve.", 0f, 1f));
            KnifeRange = c.Bind(Coverage, "Knife range (m)", 4f, Range("Closer than this he goes for his knife.", 0.5f, 10f));
            SidearmRange = c.Bind(Coverage, "Draw range (m)", 30f,
                Range("Closer than this (beyond knife range) he draws a gun he still carries. Further away he keeps waiting.", 1f, 100f));
            BoltWhenShot = c.Bind(Coverage, "Bolt when shot", true, "Hit while his hands are up, by anyone: he runs.");
            DropRetrySeconds = c.Bind(Coverage, "Drop retry after (s)", 1.5f,
                Range("If the gun is still in his hands, he tries dropping it again. After 3 tries he fights instead.", 0.5f, 5f));

            // 07 Retreat and recapture
            RetreatDistance = c.Bind(Retreat, "Retreat distance (m)", 35f, Range("", 5f, 150f));
            Grace = c.Bind(Retreat, "Grace after running (s)", 4f, Range("After he starts running.", 0f, 30f));
            ResurrenderDistance = c.Bind(Retreat, "Caught again within (m)", 12f, Range("", 1f, 50f));
            AimTolerance = c.Bind(Retreat, "Aim within (deg)", 10f, Range("How directly you must aim at him to catch him again.", 1f, 45f));
            AimConfirm = c.Bind(Retreat, "Aim for (s)", 0.4f, Range("", 0f, 3f));
            RecaptureResistance = c.Bind(Retreat, "Resist chance", 0.5f,
                Range("Times nerve: with a gun still on him, the chance he draws instead of giving up again.", 0f, 1f));
            RetreatTimeout = c.Bind(Retreat, "Retreat time limit (s)", 15f, Range("", 3f, 60f));
            ArrivalTolerance = c.Bind(Retreat, "Arrived within (m)", 2f, Range("", 0.5f, 5f));

            // 08 Regroup and rearm
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

            // 09 Hiding and movement
            HideRecheck = c.Bind(Hiding, "Look again every (s)", 8f, Range("", 1f, 60f));
            CoverRadius = c.Bind(Hiding, "Cover search radius (m)", 20f, Range("", 3f, 100f));
            PathCandidates = c.Bind(Hiding, "Points tried", 12, IntRange("Candidate spots per retreat or cover search.", 2, 48));
            PathRetries = c.Bind(Hiding, "Retries when stuck", 3, IntRange("", 0, 10));
            StallSeconds = c.Bind(Hiding, "Stuck after (s)", 3f, Range("Without making progress.", 0.5f, 15f));
            ProgressMeters = c.Bind(Hiding, "Progress (m)", 0.5f, Range("Movement that counts as progress.", 0.1f, 3f));

            // 10 Orders
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

            // 11 Reactions and voice
            LocalReactions = c.Bind(Feedback, "Other held bots react", true, "A held bot who sees another one shot runs.");
            WitnessRadius = c.Bind(Feedback, "Sees it within (m)", 40f, Range("", 5f, 150f));
            WitnessEscape = c.Bind(Feedback, "Chance he runs", 1f, Range("", 0f, 1f));
            BotVoice = c.Bind(Feedback, "Bot voice lines", true, "The game's own lines (hold fire, roger, negative...).");
            VoiceCooldown = c.Bind(Feedback, "Voice cooldown (s)", 12f, Range("", 1f, 120f));
            Hints = c.Bind(Feedback, "On-screen hints", true, "A small prompt when you can give orders.");

            // 12 Arm pose
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

            // 13 Other mods
            SainPersonality = c.Bind(Compat, "Read SAIN personality", true,
                "Cowards and Timmys give up sooner, Chads and GigaChads later. No effect without SAIN.");
            PauseWhenAsleep = c.Bind(Compat, "Pause while a bot is asleep", true,
                "If another mod (ORBIT's Ghost Mode) puts a held bot to sleep, he drops any plan to fetch a gun and decides again when he wakes.");

            // 14 Debug
            DebugKeys = c.Bind(Debug, "Debug keys", false, "Turns on the three keys below.");
            SurrenderKey = c.Bind(Debug, "Surrender bot", new KeyboardShortcut(KeyCode.LeftBracket),
                "Forces the bot nearest your crosshair (or nearest in range) to surrender.");
            OpeningKey = c.Bind(Debug, "Give them an opening", new KeyboardShortcut(KeyCode.RightBracket),
                "Every held bot acts as if you looked away, whatever his nerve: tests the knife / draw.");
            LeaveKey = c.Bind(Debug, "Pretend you left", new KeyboardShortcut(KeyCode.Backspace),
                "Every held bot acts as if you walked off: tests retreat and rearming.");
            DebugKeyDistance = c.Bind(Debug, "Surrender key range (m)", 40f, Range("", 2f, 150f));
            Overlay = c.Bind(Debug, "Overlay", false, "Lists every held bot: phase, order, nerve, arms.");
            VerboseLogs = c.Bind(Debug, "Verbose logs", false, "Logs every roll, including the ones that change nothing.");
            NerveOverride = c.Bind(Debug, "Override nerve", false, "Every bot uses the value below for new surrenders and surrender checks, instead of his own roll.");
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
