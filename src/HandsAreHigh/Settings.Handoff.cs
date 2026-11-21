using BepInEx.Configuration;

namespace HandsAreHigh
{
    // 0.25.0: hands one over the other behind the head (Kneel.ApplyHands, Fingers), the quick
    // hand-back to his own AI (SurrenderLogic.QuickClear, ReleaseWatch timeline), the interact key
    // opening the order menu (OrderMenu, InputGuard), and kneel variants (KneelVariant.cs). Same
    // rules as Settings.cs: no = \ " ' [ ] in section or key names (scripts\pack.ps1 checks).
    internal static partial class Settings
    {
        private const string V25Sec = "27. Hand-off, interact key, kneel variants";

        internal static ConfigEntry<bool> HandsOverHands;
        internal static ConfigEntry<bool> QuickHandBack;
        internal static ConfigEntry<float> QuickHandBackDistance;
        internal static ConfigEntry<float> QuickHandBackSeconds;
        internal static ConfigEntry<bool> InteractOpensMenu;
        internal static ConfigEntry<bool> DebugOrderBotPrisoners;
        internal static ConfigEntry<bool> KneelVariants;
        internal static ConfigEntry<float> KneelSettledWeight;
        internal static ConfigEntry<float> KneelTenseWeight;
        internal static ConfigEntry<float> KneelSettledThigh;
        internal static ConfigEntry<float> KneelTenseLean;
        internal static ConfigEntry<float> KneelVariantHold;

        private static void BindV25(ConfigFile c)
        {
            HandsOverHands = c.Bind(V25Sec, "Hands behind head: one over the other", true,
                "Hands behind the head rest one over the other: one palm on the back of the skull, the other hand lying over it, fingers lightly bent (which hand is on top is chosen once per man). The top hand arrives after the bottom one and leaves first. Off: both palms on the skull side by side, fingers meeting in the middle (0.24). Interlaced fingers are not used: on the game's hand rig the knuckles are closer together than two gloved fingers are wide, so laced fingers always pass through each other.");
            QuickHandBack = c.Bind(V25Sec, "Hand back to his own AI on the way out", true,
                "A man let go, or left behind by his captor, is handed to his own AI (SAIN, ORBIT, the game) as soon as his arms are down, he is on his feet and on his way (see the two keys below), mid-stride: no further flight legs, regroup or stop first. His own AI then has his squad, personality and raid objectives; the capture memory (Keep away radius) still stops him drifting back while unarmed. Off: the 0.24 flight (legs until clear, then a decision).");
            QuickHandBackDistance = c.Bind(V25Sec, "Hand back once this far from where he was held (m)", 8f,
                Range("He is handed back once this far away, or sooner once out of his ex-captor's sight and 4 m away.", 2f, 40f));
            QuickHandBackSeconds = c.Bind(V25Sec, "Hand back at most this long after he set off (s)", 5f,
                Range("Whatever the distance, his own AI has him after this long on his way (a slow, hurt walker included).", 1f, 30f));
            InteractOpensMenu = c.Bind(V25Sec, "Interact key opens the order menu", true,
                "Looking at a held man you can order (within the order range): your Interact key (F unless rebound) opens the order menu; pressed again, it gives the highlighted order. Number keys still work. While the menu is open (and while you look at such a man) the key is kept from the game, so it never also opens a door, loots or picks something up; holding it never repeats an order.");
            DebugOrderBotPrisoners = c.Bind(V25Sec, "Debug: order prisoners held by bots", true,
                "Debug keys on (section 14) only: the order menu also opens on a man held by a bot (a debug scenario's PMC captures Scav, for example), marked DEBUG. The first order you give takes him over: the bot's custody ends cleanly first (his guarding layer and squad cover are released, his hold-fire decision stays), so two captors never hold or steer the same man. Off, or debug keys off: bot-held men can't be ordered (normal play).");
            KneelVariants = c.Bind(V25Sec, "Kneel variants", true,
                "Men kneel in different ways: upright, settled back over the heels, or forward and tense. Chosen when he gets down from who he is and how he feels (resigned men settle back, defiant or watchful ones stay forward), checked against the ground and what is around him, and held; a big change of mood may move him to another once, slowly. A settled man shifts his weight forward before he rises. Off: everyone kneels upright (0.24).");
            KneelSettledWeight = c.Bind(V25Sec, "Settled back on the heels weight", 1f,
                Range("How often men settle back over their heels (relative to upright = 1). 0: never.", 0f, 4f));
            KneelTenseWeight = c.Bind(V25Sec, "Forward and tense weight", 1f,
                Range("How often men kneel forward and tense (relative to upright = 1). 0: never.", 0f, 4f));
            KneelSettledThigh = c.Bind(V25Sec, "Settled thigh angle (deg)", 46f,
                Range("Thighs forward of vertical when settled back (the knees stay where they are; the hips go back and down). 46: the hips about a hand above the heels, knees bent ~145 deg.", 20f, 55f));
            KneelTenseLean = c.Bind(V25Sec, "Tense forward lean (deg)", 10f,
                Range("Extra forward lean of the tense kneel (hips a little forward over the knees).", 0f, 20f));
            KneelVariantHold = c.Bind(V25Sec, "Keeps a variant at least (s)", 20f,
                Range("A man changes how he kneels at most this often, and only on a real change of mood (never back and forth).", 5f, 120f));
        }
    }
}
