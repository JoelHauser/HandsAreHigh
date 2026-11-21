using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // F12 section "1. General": general behaviour, other mods.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> NaturalSurrender;
        internal static ConfigEntry<bool> IncludePmcs;
        internal static ConfigEntry<bool> BotCaptors;
        internal static ConfigEntry<bool> TeammatesSurrender;
        internal static ConfigEntry<bool> OrdersEnabled;
        internal static ConfigEntry<bool> IncludeBosses;
        internal static ConfigEntry<bool> SainPersonality;
        internal static ConfigEntry<bool> SainSuppression;
        internal static ConfigEntry<bool> ReleaseSainControl;
        internal static ConfigEntry<bool> PauseWhenAsleep;
        internal static ConfigEntry<bool> OrbitKeepAwake;
        internal static ConfigEntry<bool> FixStaleWeapon;
        internal static ConfigEntry<int> LayerPriority;
        internal static ConfigEntry<int> CaptorPriority;

        private static void BindGeneral(ConfigFile c)
        {
            Enabled = c.Bind(S1, "Enabled", true,
                Text("Off: no new surrenders, and bots already held are let go.", false));
            NaturalSurrender = c.Bind(S1, "Bots surrender on their own", true,
                Text("Hurt, pinned down, out of ammo, outnumbered or left alone, a bot may give up. Off: only debug keys and scenarios start a surrender.", false));
            IncludePmcs = c.Bind(S1, "PMCs can surrender", true,
                Text("Off: only Scavs give up.", false));
            BotCaptors = c.Bind(S1, "Bots take prisoners", true,
                Text("A Scav or PMC can give up to a hostile bot, not only to you. That bot then decides whether to take him prisoner, spare him, ignore him or keep fighting (section 3).", false));
            TeammatesSurrender = c.Bind(S1, "Your teammates can surrender", false,
                Text("Off: bots on your side (your group, Pit Fireteam teammates) never give up on their own, so they stay under your orders. Debug keys still work on them.", false));
            OrdersEnabled = c.Bind(S1, "Order menu", true,
                Text("Look at a held bot up close and press the menu key to give orders (section 5).", false));
            IncludeBosses = c.Bind(S1, "Bosses can surrender (restart)", false,
                Text("Off: bosses and their followers never surrender. On: they can, but far more reluctantly than PMCs (Boss chances in section 2). Needs a game restart.", false));
            SainPersonality = c.Bind(S1, "SAIN: use personality", true,
                Text("Cowards and Timmys give up sooner and run more; Chads and GigaChads hold out and fight more. No effect without SAIN.", true));
            SainSuppression = c.Bind(S1, "SAIN: use suppression", true,
                Text("Counts SAIN's suppression level as well as the game's own incoming-fire signal (the higher one is used). No effect without SAIN.", true));
            ReleaseSainControl = c.Bind(S1, "SAIN: take control while held", true,
                Text("SAIN would keep steering a bot whose SAIN layer was active when he gave up. On: SAIN is told to stand down while he is held. No effect without SAIN.", true));
            PauseWhenAsleep = c.Bind(S1, "Pause while a bot is asleep", true,
                Text("If another mod (ORBIT's Ghost Mode) puts a held bot to sleep, he drops any plan to fetch a gun and decides again when he wakes.", true));
            OrbitKeepAwake = c.Bind(S1, "ORBIT: keep encounters awake", true,
                Text("While a bot holds a prisoner, ORBIT is asked not to put those two to sleep, and allowed again when it ends. Nobody is woken up. Needs ORBIT 3.1.1 or later.", true));
            FixStaleWeapon = c.Bind(S1, "Disarmed bots know they are unarmed", true,
                Text("The game keeps treating a bot's dropped gun as his loaded weapon, so other AI (SAIN) sent disarmed men to fight or throw grenades. On: a bot this mod disarmed reads as having no ammo until a gun is back in his hands.", true));
            LayerPriority = c.Bind(S1, "Surrender layer priority (restart)", 95,
                IntRange("BigBrain priority of a held bot's behaviour. SAIN uses 20-24 and 80, ORBIT 19. Needs a game restart.", 1, 200, true));
            CaptorPriority = c.Bind(S1, "Captor layer priority (restart)", 70,
                IntRange("BigBrain priority of a bot guarding a prisoner: below SAIN's avoid-threat layer (80) and the surrender layer, above SAIN's combat layers (20-24). Needs a game restart.", 1, 200, true));
        }
    }
}
