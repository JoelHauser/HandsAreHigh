using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.16.0 tuning: hand position and turning orders, partial compliance, captors looting what a
    // prisoner drops, the captor's judgement (mercy, warnings, lethal escalation) and scenario
    // staging. Same rules as Settings.cs: no = \ " ' [ ] in section or key names.
    internal static partial class Settings
    {
        private const string HandsSec = "19. Hands on head and turning";
        private const string ComplySec = "20. Partial compliance";
        private const string LootSec = "21. Captors taking surrendered gear";
        private const string JudgeSec = "22. Captor judgement";

        // 19
        internal static ConfigEntry<bool> HandsOnHeadOrder;
        internal static ConfigEntry<bool> TurnOrder;
        internal static ConfigEntry<bool> KneelHandsOnHead;
        internal static ConfigEntry<float> HandsOnHeadSeconds;
        internal static ConfigEntry<float> HandsOnHeadDown;
        internal static ConfigEntry<float> HandsOnHeadUp;
        internal static ConfigEntry<float> HandsOnHeadYaw;
        internal static ConfigEntry<float> TurnSpeed;
        internal static ConfigEntry<bool> TurnFromKneeling;
        internal static ConfigEntry<bool> TurnSteps;
        internal static ConfigEntry<float> CaptorHandsOnHeadChance;
        internal static ConfigEntry<float> CaptorTurnChance;
        internal static ConfigEntry<KeyboardShortcut> HandsOnHeadKey;
        internal static ConfigEntry<KeyboardShortcut> TurnKey;
        // 20
        internal static ConfigEntry<bool> PartialCompliance;
        internal static ConfigEntry<float> KeepWeaponChance;
        internal static ConfigEntry<float> KeepOnlyWeapon;
        internal static ConfigEntry<float> RepeatComply;
        internal static ConfigEntry<float> RepeatEscalate;
        internal static ConfigEntry<float> KeptDrawBoost;
        internal static ConfigEntry<int> CaptorRepeats;
        internal static ConfigEntry<float> HolsterSeenWithin;
        internal static ConfigEntry<ComplianceChoice> ComplianceTest;
        // 21
        internal static ConfigEntry<bool> CaptorLooting;
        internal static ConfigEntry<float> LootChance;
        internal static ConfigEntry<float> LootMinValue;
        internal static ConfigEntry<int> LootMaxItems;
        internal static ConfigEntry<float> LootRisk;
        internal static ConfigEntry<bool> LootAfterRelease;
        internal static ConfigEntry<float> LootTimeLimit;
        internal static ConfigEntry<bool> LogLoot;
        internal static ConfigEntry<bool> CollectDemanded;
        internal static ConfigEntry<float> DemandedRisk;
        // 22
        internal static ConfigEntry<float> LethalIntent;
        internal static ConfigEntry<float> RivalWeight;
        internal static ConfigEntry<float> ExecuteMax;
        internal static ConfigEntry<KeyboardShortcut> KillKey;
        internal static ConfigEntry<float> ViolenceWeight;
        internal static ConfigEntry<float> ViolenceMemory;
        internal static ConfigEntry<float> IncompleteWeight;
        internal static ConfigEntry<float> RefusalLethal;
        internal static ConfigEntry<float> BoltLethal;
        internal static ConfigEntry<bool> LogJudgement;
        internal static ConfigEntry<float> CaptivityRadius;
        internal static ConfigEntry<float> FleeCommit;
        // 17 (scenarios)
        internal static ConfigEntry<bool> ScenarioStaging;
        internal static ConfigEntry<float> ScenarioSlowSpawn;

        private static void BindRealism(ConfigFile c)
        {
            // 19 Hands on head and turning
            HandsOnHeadOrder = c.Bind(HandsSec, "Hands on head order", true,
                "The order menu offers Hands on head (and Hands up to undo it). Standing or kneeling: his posture stays as it is. A man still holding something empties his hands through the normal drop first.");
            TurnOrder = c.Bind(HandsSec, "Turn around order", true,
                "The order menu offers Turn around: he turns his back to whoever gave the order, measured where that man stood when the order was given, and stays that way (glances over his shoulder now and then, never turns back by himself).");
            KneelHandsOnHead = c.Bind(HandsSec, "Get down puts hands on head", true,
                "Get down puts his hands on his head as before, unless he was told Hands up during this surrender (then he kneels with his hands up). Off: Get down never changes where his hands are.");
            HandsOnHeadSeconds = c.Bind(HandsSec, "Hands to head (s)", 0.9f, Range("Standing: how long the hands take from up to the back of his head, and back.", 0.3f, 3f));
            HandsOnHeadDown = c.Bind(HandsSec, "Head down max with hands on head (deg)", 12f,
                Range("With his hands clasped behind his head the neck can only bow a little before the arms go with it; this replaces Head down max while they are there (section 18).", 0f, 35f));
            HandsOnHeadUp = c.Bind(HandsSec, "Head up max with hands on head (deg)", 16f, Range("Looking up with the hands on his head.", 0f, 30f));
            HandsOnHeadYaw = c.Bind(HandsSec, "Head turn max with hands on head (deg)", 50f,
                Range("Standing with his hands on his head (kneeling uses Kneeling head turn max): the hands go round with the head.", 15f, 80f));
            TurnSpeed = c.Bind(HandsSec, "Turn speed (deg per s)", 110f, Range("How fast he turns round on his feet (eased in and out).", 40f, 300f));
            TurnSteps = c.Bind(HandsSec, "Turn with stepping feet", true,
                "A standing prisoner who must turn more than 70 deg (Turn around, facing a captor who walked round him, turning before he kneels) turns with the game's own standing-turn animation (the feet step, the clip turns him). Smaller turns are a short pivot. Off: always the pivot.");
            TurnFromKneeling = c.Bind(HandsSec, "Turn around from his knees", true,
                "A kneeling man told to turn gets up, turns and kneels again facing away (knees are never spun on the ground). Off: the order is refused while he kneels.");
            CaptorHandsOnHeadChance = c.Bind(HandsSec, "Captors order hands on head", 0.35f,
                Range("Chance a bot captor's plan includes Hands on head (once he is disarmed and under control).", 0f, 1f));
            CaptorTurnChance = c.Bind(HandsSec, "Captors order a prisoner to turn around", 0.3f,
                Range("Chance a bot captor's plan includes Turn around (before he takes the prisoner's gear, or to keep him looking away).", 0f, 1f));
            HandsOnHeadKey = c.Bind(HandsSec, "Debug key: hands on head", new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl),
                "Debug keys on (section 14): the held bot under your crosshair gets Hands on head (or Hands up, if they're there), from any distance, through the order menu's own path.");
            TurnKey = c.Bind(HandsSec, "Debug key: turn around", new KeyboardShortcut(KeyCode.T, KeyCode.LeftControl), "The Turn around order, the same way.");

            // 20 Partial compliance
            PartialCompliance = c.Bind(ComplySec, "Prisoners may keep a weapon", true,
                "Drop your weapons covers every gun (in hand, slung, holstered) and his knife. Once per order he decides whether to give up all of them or quietly keep one (a sidearm or knife first). Off: he always drops everything the order covers.");
            KeepWeaponChance = c.Bind(ComplySec, "Keeps one back", 0.2f,
                Range("Base chance on the first demand, before nerve, personality, wounds, fire, how many guns cover him and how close they are. Never more than 0.6.", 0f, 1f));
            KeepOnlyWeapon = c.Bind(ComplySec, "Keeps his only weapon", 0.4f,
                Range("Times the chance when the weapon he'd keep is his only one (only a holstered pistol or a knife can be hidden that way).", 0f, 1f));
            RepeatComply = c.Bind(ComplySec, "Complies when told again", 0.7f,
                Range("Base chance he hands over what he kept when the demand is repeated (the rest: he refuses or, rarely, escalates).", 0f, 1f));
            RepeatEscalate = c.Bind(ComplySec, "Escalates when told again", 0.06f,
                Range("Base chance that a repeated demand makes a bold man go for the weapon he kept there and then (needs nerve; never a hidden weapon he doesn't have).", 0f, 0.5f));
            KeptDrawBoost = c.Bind(ComplySec, "Kept weapon draw weight", 1.3f,
                Range("A man who kept a weapon on purpose weighs drawing it this much more when a real opening comes. It is never a guaranteed attack.", 1f, 3f));
            CaptorRepeats = c.Bind(ComplySec, "Captor repeats a demand at most", 2, IntRange("Times a bot captor who noticed a weapon kept back demands it again before he decides what to do.", 0, 4));
            HolsterSeenWithin = c.Bind(ComplySec, "Holstered pistol seen within (m)", 4f,
                Range("A bot captor notices a pistol left in the holster only this close, in sight. A slung rifle is seen at any range he sees the man; a knife or anything in a bag never.", 0f, 15f));
            ComplianceTest = c.Bind(ComplySec, "Test outcome", ComplianceChoice.Auto,
                "Debug: forces the prisoner's answer to Drop your weapons (Auto: his own decision). Logged as forced.");

            // 21 Captors taking surrendered gear
            CaptorLooting = c.Bind(LootSec, "Captors take dropped gear", true,
                "A bot captor weighs what his prisoner dropped: worth taking (value, a gap in his own kit, room to carry it), safe to fetch (no threat, the prisoner under control, a squadmate covering him), and his own nature. He may take some of it, all, nothing, or wait until it is safe. Real inventory moves only: no duplicates, nothing of his own thrown away, never past his capacity.");
            LootChance = c.Bind(LootSec, "Wants gear", 0.6f, Range("Base appetite for an item worth taking, before personality (Rats most, cautious men least).", 0f, 1f));
            LootMinValue = c.Bind(LootSec, "Worth taking from (roubles)", 8000f,
                Range("Handbook value below which an item isn't worth the walk, unless it fills an empty slot of his.", 0f, 200000f));
            LootMaxItems = c.Bind(LootSec, "Items per prisoner at most", 3, IntRange("", 0, 8));
            LootRisk = c.Bind(LootSec, "Risk he accepts", 0.5f,
                Range("How much loss of control he accepts to fetch an item (0: only with a squadmate covering or the prisoner kneeling and turned away; 1: walks over regardless).", 0f, 1f));
            LootAfterRelease = c.Bind(LootSec, "Fetch it after letting him go", true,
                "Gear he wanted but didn't dare fetch while guarding: picked up once the released man is well away (or out of sight), if nothing threatens him.");
            LootTimeLimit = c.Bind(LootSec, "Gives up fetching after (s)", 20f, Range("Per item.", 5f, 60f));
            LogLoot = c.Bind(LootSec, "Log gear decisions", true, "Each item weighed (value, gap, room, risk, nature) and each pick-up's real inventory result.");
            CollectDemanded = c.Bind(LootSec, "Collects the weapons he made him drop", true,
                "0.18.0: a weapon the prisoner dropped because this captor demanded it (the gun at the surrender, guns and knife on Drop your weapons) is always wanted and fetched first, when he has a slot it fits and the walk is safe enough (Risk he accepts for those). No room, no way to it, or someone else got it first: logged and left. Off: those weapons are weighed like any other item.");
            DemandedRisk = c.Bind(LootSec, "Risk he accepts for those", 0.9f,
                Range("Like Risk he accepts, for the weapons he made him drop. Higher than for other gear: leaving a gun at the feet of a man he disarmed is a risk too.", 0f, 2f));

            // 22 Captor judgement
            // 0.18.0: new key names so the new defaults apply (BepInEx keeps a saved value per key).
            LethalIntent = c.Bind(JudgeSec, "Lethal intent toward a prisoner", 0.25f,
                Range("Base chance, decided ONCE when a bot takes custody, that he means to shoot the man when he is done with him (after the disarming and the gear), even if he does everything he is told. Kept for the whole custody and carried out at the release through his normal targeting and firing. Scaled by personality (aggressive men far more, timid ones less), rival factions, and what this man did to him. Level and skill never lower it.", 0f, 1f));
            RivalWeight = c.Bind(JudgeSec, "Weight: rival faction", 1.4f,
                Range("Times every lethal chance when captor and prisoner are rivals: BEAR and USEC, or a Scav and a PMC either way.", 1f, 4f));
            ExecuteMax = c.Bind(JudgeSec, "Lethal decision cap", 0.85f, Range("Cap on any one lethal decision.", 0f, 1f));
            KillKey = c.Bind(JudgeSec, "Debug key: captor decides to kill", new KeyboardShortcut(KeyCode.U, KeyCode.LeftControl),
                "Debug keys on (section 14): the bot captor under your crosshair (or the captor of the prisoner under it) makes the lethal decision about his prisoner now. Only the decision is forced: restraint, targeting, aiming and firing are the real ones, watched and logged (KILL DECISION CARRIED OUT / NOT CARRIED OUT).");
            ViolenceWeight = c.Bind(JudgeSec, "Weight: he shot me", 2.5f,
                Range("Times the lethal chance when this prisoner hit him (or his squad) recently, by the bot's own memory of who hit him.", 1f, 6f));
            ViolenceMemory = c.Bind(JudgeSec, "Remembers being shot for (s)", 120f, Range("", 10f, 900f));
            IncompleteWeight = c.Bind(JudgeSec, "Weight: kept a weapon back", 1.8f,
                Range("Times the lethal chance when he saw the prisoner keep a weapon back (and get it off him after a repeat).", 1f, 6f));
            RefusalLethal = c.Bind(JudgeSec, "Refusal turns lethal", 0.25f,
                Range("Base chance he opens fire when, after his repeated demands, the prisoner still keeps a weapon he can see. Otherwise he warns once more and lets him go, or keeps him covered until he leaves.", 0f, 1f));
            BoltLethal = c.Bind(JudgeSec, "Fires at a prisoner who runs", 0.3f,
                Range("Base chance, decided once when he sees his prisoner run from custody unarmed (not let go), that he opens fire instead of letting him go. Scaled like the others (aggression, rival faction, he-shot-me, kept back). A man going for a weapon is always fought; this is the unarmed run.", 0f, 1f));
            CaptivityRadius = c.Bind(Retreat, "Leaves the captivity area (m)", 40f,
                Range("A man who runs from captivity (let go, bolting, shot at) heads for a spot at least this far from where he was held, along a complete route that doesn't pass his captor; with no way out from there he goes in legs through cover. He only stops to regroup once out of it.", 10f, 120f));
            FleeCommit = c.Bind(Retreat, "No going back for a gun for (s)", 60f,
                Range("Once he has fled, a gun lying in the captivity area (his own or another) is left alone this long: he leaves, he doesn't loop back to it. Guns elsewhere, and weapons he still carries, stay options.", 0f, 600f));
            LogJudgement = c.Bind(JudgeSec, "Log judgement", true, "Every decision point (intent, warn, repeat, release, lethal) with its inputs, and how a kill decision is carried out (target, first shot, or what blocked it).");

            // 17 Debug Scenarios (0.16.0)
            ScenarioStaging = c.Bind("17. Debug Scenarios", "Hold bots until all are ready", true,
                "Scenario bots only: each one stands still (no moving, shooting or talking) from the moment the game creates him until every participant is placed, active and checked; then all are released together. Off: each acts as soon as he spawns (0.15.0 behaviour).");
            ScenarioSlowSpawn = c.Bind("17. Debug Scenarios", "Test: delay one spawn (s)", 0f,
                Range("Debug: holds the activation of the last participant this long, to test staging under slow initialisation. 0: off.", 0f, 30f));
        }
    }

    internal enum ComplianceChoice { Auto, DropsAll, KeepsOne, Refuses }
}
