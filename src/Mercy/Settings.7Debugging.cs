using BepInEx.Configuration;
using UnityEngine;

namespace Mercy
{
    // F12 section "7. Debugging": debug keys, logs, profiling, scenarios.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<bool> DebugKeys;
        internal static ConfigEntry<KeyboardShortcut> SurrenderKey;
        internal static ConfigEntry<KeyboardShortcut> OpeningKey;
        internal static ConfigEntry<KeyboardShortcut> LeaveKey;
        internal static ConfigEntry<KeyboardShortcut> SurrenderToBotKey;
        internal static ConfigEntry<KeyboardShortcut> IgnoreKey;
        internal static ConfigEntry<bool> Overlay;
        internal static ConfigEntry<bool> ScenariosEnabled;
        internal static ConfigEntry<KeyboardShortcut> ScenarioKey;
        internal static ConfigEntry<bool> VerboseLogs;
        internal static ConfigEntry<bool> Profile;
        internal static ConfigEntry<KeyboardShortcut> ForceRunKey;
        internal static ConfigEntry<KeyboardShortcut> ForceGrabKey;
        internal static ConfigEntry<KeyboardShortcut> KillKey;
        internal static ConfigEntry<KeyboardShortcut> HandsOnHeadKey;
        internal static ConfigEntry<KeyboardShortcut> TurnKey;
        internal static ConfigEntry<float> DebugKeyDistance;
        internal static ConfigEntry<float> DebugOpeningSeconds;
        internal static ConfigEntry<bool> DebugOrderBotPrisoners;
        internal static ConfigEntry<bool> NerveOverride;
        internal static ConfigEntry<float> NerveOverrideValue;
        internal static ConfigEntry<ComplianceChoice> ComplianceTest;
        internal static ConfigEntry<float> ErrorLogCooldown;
        internal static ConfigEntry<bool> LogDecisions;
        internal static ConfigEntry<bool> LogRestraint;
        internal static ConfigEntry<bool> LogJudgement;
        internal static ConfigEntry<bool> LogLoot;
        internal static ConfigEntry<bool> LogSquadCover;
        internal static ConfigEntry<bool> LogFireBreak;
        internal static ConfigEntry<bool> LogEncounter;
        internal static ConfigEntry<bool> LogEscape;
        internal static ConfigEntry<bool> LogVoice;
        internal static ConfigEntry<bool> LogGestures;
        internal static ConfigEntry<bool> LogAttention;
        internal static ConfigEntry<bool> LogKneel;
        internal static ConfigEntry<bool> LogPose;
        internal static ConfigEntry<float> ReleaseWatchSeconds;
        internal static ConfigEntry<float> ProfileSeconds;
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
        internal static ConfigEntry<bool> ScenarioStaging;
        internal static ConfigEntry<float> ScenarioSlowSpawn;
        internal static ConfigEntry<bool> KneelGizmos;
        internal static ConfigEntry<KeyboardShortcut> KneelKey;
        internal static ConfigEntry<KeyboardShortcut> StandKey;
        internal static ConfigEntry<KeyboardShortcut> KneelLoopKey;
        internal static ConfigEntry<KeyboardShortcut> KneelInterruptKey;
        internal static ConfigEntry<KeyboardShortcut> KneelUrgentKey;
        internal static ConfigEntry<int> KneelTestCycles;
        internal static ConfigEntry<KneelInterrupt> KneelTestInterrupt;
        internal static ConfigEntry<KneelInterruptDuring> KneelTestDuring;
        internal static ConfigEntry<float> KneelTestAt;
        internal static ConfigEntry<KneelStyleChoice> KneelTestStyle;
        internal static ConfigEntry<KneelRiseChoice> KneelTestRise;
        internal static ConfigEntry<KneelBraceChoice> KneelTestBrace;
        internal static ConfigEntry<KneelEscapeChoice> KneelTestEscape;
        internal static ConfigEntry<KneelPoseIsolate> KneelPoseIsolate;
        internal static ConfigEntry<bool> KneelBracePalmFlip;
        internal static ConfigEntry<bool> HeadNeutralTest;
        private static ConfigEntry<int> ConfigVersion;

        private static void BindDebugging(ConfigFile c)
        {
            DebugKeys = c.Bind(S7, "Debug keys", false,
                Text("Turns on the debug keys in this section. Each acts only on the bot under your crosshair.", false));
            SurrenderKey = c.Bind(S7, "Key: surrender", new KeyboardShortcut(KeyCode.LeftBracket),
                Text("The bot under your crosshair surrenders.", false));
            OpeningKey = c.Bind(S7, "Key: give an opening", new KeyboardShortcut(KeyCode.RightBracket),
                Text("The held bot under your crosshair treats you as not covering him for a few seconds; his own rolls decide what he does.", false));
            LeaveKey = c.Bind(S7, "Key: pretend you left", new KeyboardShortcut(KeyCode.Backspace),
                Text("The held bot under your crosshair acts as if you walked off.", false));
            SurrenderToBotKey = c.Bind(S7, "Key: surrender to his enemy", new KeyboardShortcut(KeyCode.LeftBracket, KeyCode.LeftControl),
                Text("The bot under your crosshair gives up to the bot he is fighting, and that bot decides what to do.", false));
            IgnoreKey = c.Bind(S7, "Key: captor ignores him", new KeyboardShortcut(KeyCode.M, KeyCode.LeftControl),
                Text("The bot the held man under your crosshair gave up to ignores the surrender now (the roll skipped; everything after is real).", false));
            Overlay = c.Bind(S7, "Overlay", false,
                Text("Lists every held bot: phase, order, nerve, arms.", false));
            ScenariosEnabled = c.Bind(S7, "Scenario tools", false,
                Text("Debug only, spawns bots. On: the scenario key opens the scenario menu in a raid to spawn a test encounter, watch it and clean it up. Does nothing when off.", false));
            ScenarioKey = c.Bind(S7, "Scenario menu key", KeyboardShortcut.Empty,
                Text("Opens and closes the scenario menu (unbound by default).", false));
            VerboseLogs = c.Bind(S7, "Verbose logs", false,
                Text("Logs every roll, including the ones that change nothing.", false));
            Profile = c.Bind(S7, "Profile", false,
                Text("Costs nothing when off. On: logs frame times, GC activity and this mod's own cost, for comparing runs with the mod on and off.", false));
            ForceRunKey = c.Bind(S7, "Key: force run", new KeyboardShortcut(KeyCode.Backspace, KeyCode.LeftControl),
                Text("The held bot under your crosshair runs now (no roll).", true));
            ForceGrabKey = c.Bind(S7, "Key: force gun grab", new KeyboardShortcut(KeyCode.RightBracket, KeyCode.LeftControl),
                Text("The held bot under your crosshair scrambles for his dropped gun now (no roll), if it's there and reachable.", true));
            KillKey = c.Bind(S7, "Key: captor decides to kill", new KeyboardShortcut(KeyCode.U, KeyCode.LeftControl),
                Text("The bot captor under your crosshair (or the captor of the prisoner under it) makes the lethal decision now; what follows is real and logged.", true));
            HandsOnHeadKey = c.Bind(S7, "Key: hands on head", new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl),
                Text("The held bot under your crosshair gets Hands on head (or Hands up), from any distance.", true));
            TurnKey = c.Bind(S7, "Key: turn around", new KeyboardShortcut(KeyCode.T, KeyCode.LeftControl),
                Text("The held bot under your crosshair gets Turn around, from any distance.", true));
            DebugKeyDistance = c.Bind(S7, "Key range (m)", 60f,
                Range("How far the debug keys reach.", 2f, 200f, true));
            DebugOpeningSeconds = c.Bind(S7, "Opening lasts (s)", 6f,
                Range("How long the opening key's opening lasts.", 1f, 30f, true));
            DebugOrderBotPrisoners = c.Bind(S7, "Order prisoners held by bots", true,
                Text("Debug keys on only: the order menu also opens on a man held by a bot, marked DEBUG; your first order takes him over cleanly.", true));
            NerveOverride = c.Bind(S7, "Override nerve", false,
                Text("Every bot uses the value below instead of his own roll, live.", true));
            NerveOverrideValue = c.Bind(S7, "Nerve value", 0.5f,
                Range("The nerve every bot uses while Override nerve is on.", 0f, 1f, true));
            ComplianceTest = c.Bind(S7, "Test: Drop your weapons answer", ComplianceChoice.Auto,
                Text("Forces the prisoner's answer to Drop your weapons (Auto: his own decision).", true));
            ErrorLogCooldown = c.Bind(S7, "Repeat errors every (s)", 10f,
                Range("The same error is logged at most this often.", 1f, 120f, true));
            LogDecisions = c.Bind(S7, "Log: decisions", true,
                Text("Each choice a held bot makes with its main reasons.", true));
            LogRestraint = c.Bind(S7, "Log: hold-fire decisions", true,
                Text("Every hold-fire decision with its reasons. A bot that fires anyway is always logged as RESTRAINT LEAK.", true));
            LogJudgement = c.Bind(S7, "Log: captor judgement", true,
                Text("Every lethal decision point with its inputs, and how a kill decision is carried out.", true));
            LogLoot = c.Bind(S7, "Log: gear decisions", true,
                Text("Each item a captor weighs and each pick-up's result.", true));
            LogSquadCover = c.Bind(S7, "Log: squad cover", true,
                Text("Who covers whom, coverage gained and lost, what the guard sees and does.", true));
            LogFireBreak = c.Bind(S7, "Log: incoming fire", true,
                Text("What broke a hold-up, each side's reaction and the prisoner's choice under fire.", true));
            LogEncounter = c.Bind(S7, "Log: encounter endings", true,
                Text("Captor wrap-up decisions, ignored surrenders and truces, hand-backs, being left, gear demands.", true));
            LogEscape = c.Bind(S7, "Log: escapes", true,
                Text("Escape destinations, refused weapon recoveries, hand-backs, keep-away legs, renewed hostility.", true));
            LogVoice = c.Bind(S7, "Log: voice", true,
                Text("Every line said and whether audio actually started (or why not).", true));
            LogGestures = c.Bind(S7, "Log: gestures", true,
                Text("Wiggle and middle finger rolls, starts, skips and interruptions.", true));
            LogAttention = c.Bind(S7, "Log: attention", false,
                Text("Each glance and each change of demeanor.", true));
            LogKneel = c.Bind(S7, "Log: kneeling", true,
                Text("Kneel starts, finishes (with a timeline), reversals, fallbacks and interruptions.", true));
            LogPose = c.Bind(S7, "Log: pose diagnostics", false,
                Text("Once a second per kneeling bot: the angles that twist or turn him.", true));
            ReleaseWatchSeconds = c.Bind(S7, "Log: released bot for (s)", 60f,
                Range("After a hand-back, log lines at +15 s, +30 s and this say where he is and which AI drives him. 0: off.", 0f, 300f, true));
            ProfileSeconds = c.Bind(S7, "Profile report every (s)", 10f,
                Range("How often the profile report is logged.", 2f, 120f, true));
            ScenarioUp = c.Bind(S7, "Scenario: menu up", new KeyboardShortcut(KeyCode.UpArrow),
                Text("While the scenario menu is open.", true));
            ScenarioDown = c.Bind(S7, "Scenario: menu down", new KeyboardShortcut(KeyCode.DownArrow),
                Text("While the scenario menu is open.", true));
            ScenarioLess = c.Bind(S7, "Scenario: previous value", new KeyboardShortcut(KeyCode.LeftArrow),
                Text("While the scenario menu is open.", true));
            ScenarioMore = c.Bind(S7, "Scenario: next value", new KeyboardShortcut(KeyCode.RightArrow),
                Text("While the scenario menu is open.", true));
            ScenarioConfirm = c.Bind(S7, "Scenario: activate", new KeyboardShortcut(KeyCode.Return),
                Text("Launches, fires an event, or runs the highlighted action.", true));
            ScenarioOverlay = c.Bind(S7, "Scenario: overlay", true,
                Text("While a scenario runs: participants, relationships, phases and a short timeline, top left.", true));
            ScenarioConcurrent = c.Bind(S7, "Scenario: allow several at once", false,
                Text("Off: a scenario can only be launched when none is running.", true));
            ScenarioMaxBots = c.Bind(S7, "Scenario: bots at most", 12,
                IntRange("Across all running scenarios; a launch that would exceed it is refused.", 1, 30, true));
            ScenarioSpawnTimeout = c.Bind(S7, "Scenario: spawn timeout (s)", 30f,
                Range("A participant not spawned and active within this is reported as failed and the launch cleaned up.", 5f, 120f, true));
            ScenarioNoSpawnMagCheck = c.Bind(S7, "Scenario: no magazine check on spawn", true,
                Text("Scenario bots only: the magazine check at spawn is postponed. Raid bots are not touched.", true));
            ScenarioStaging = c.Bind(S7, "Scenario: hold bots until all are ready", true,
                Text("Scenario bots stand still until every participant is placed and active, then all start together.", true));
            ScenarioSlowSpawn = c.Bind(S7, "Scenario: delay one spawn (s)", 0f,
                Range("Holds the last participant's activation this long, to test staging. 0: off.", 0f, 30f, true));
            KneelGizmos = c.Bind(S7, "Kneel: gizmos", false,
                Text("With the overlay on: marks the knees, feet and hands of kneeling bots on screen.", true));
            KneelKey = c.Bind(S7, "Kneel test: kneel key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl),
                Text("The held bot under your crosshair gets Get down, from any distance.", true));
            StandKey = c.Bind(S7, "Kneel test: stand key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftShift),
                Text("The On your feet order, the same way.", true));
            KneelLoopKey = c.Bind(S7, "Kneel test: loop key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftAlt),
                Text("Kneel, hold, stand, hold, repeated Kneel test: cycles times. Any real order stops it.", true));
            KneelInterruptKey = c.Bind(S7, "Kneel test: interrupt key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl, KeyCode.LeftShift),
                Text("Starts the chosen transition and interrupts it at the chosen point with the chosen event.", true));
            KneelUrgentKey = c.Bind(S7, "Kneel test: urgent rise key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl, KeyCode.LeftAlt),
                Text("A kneeling bot under the crosshair gets up for real (Escape: forced run; Defensive: forced draw or a simulated hit).", true));
            KneelTestCycles = c.Bind(S7, "Kneel test: cycles", 3,
                IntRange("Repeats of the loop key.", 1, 10, true));
            KneelTestInterrupt = c.Bind(S7, "Kneel test: interruption", KneelInterrupt.Reverse,
                Text("Reverse: the opposite order. Hit: a simulated hit (no damage). Run: forced run. Draw: forced draw. Kill: kills him.", true));
            KneelTestDuring = c.Bind(S7, "Kneel test: interrupts", KneelInterruptDuring.GoingDown,
                Text("Which transition the interruption test runs.", true));
            KneelTestAt = c.Bind(S7, "Kneel test: interrupt at", 0.5f,
                Range("How far into that transition (0 start, 1 end).", 0.02f, 0.98f, true));
            KneelTestStyle = c.Bind(S7, "Kneel test: style", KneelStyleChoice.Auto,
                Text("Forces the descent style for every kneel. Auto: chosen by the bot.", true));
            KneelTestRise = c.Bind(S7, "Kneel test: rise", KneelRiseChoice.Auto,
                Text("Forces the rise for an ordered stand-up. Auto: compliant for orders.", true));
            KneelTestBrace = c.Bind(S7, "Kneel test: brace", KneelBraceChoice.Auto,
                Text("Always / Never forces the knee brace on rises.", true));
            KneelTestEscape = c.Bind(S7, "Kneel test: escape outcome", KneelEscapeChoice.Auto,
                Text("When a bot escaping from his knees is covered again: forces what he does. Auto: his own roll.", true));
            KneelPoseIsolate = c.Bind(S7, "Kneel test: pose isolate", Mercy.KneelPoseIsolate.All,
                Text("Turns one part of the kneeling pose off to see which one distorts it. All: normal.", true));
            KneelBracePalmFlip = c.Bind(S7, "Kneel test: brace palm flip", false,
                Text("Turns the bracing hand over, if the back of the hand shows on the thigh.", true));
            HeadNeutralTest = c.Bind(S7, "Head neutral test", false,
                Text("Every held man only looks at his captor: no glances, tilt, tremble, flinch or gestures.", true));
            ConfigVersion = c.Bind(S7, "Config version (set by the mod)", 0,
                new ConfigDescription("Which default changes have been applied to this file. Not a setting.", null,
                    new ConfigurationManagerAttributes { Browsable = false, Order = --_order }));
        }
    }
}
