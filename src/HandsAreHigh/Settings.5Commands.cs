using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // F12 section "5. Commands and dialogue": the order menu, orders, compliance, voice.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<KeyboardShortcut> MenuKey;
        internal static ConfigEntry<KeyboardShortcut> NextKey;
        internal static ConfigEntry<KeyboardShortcut> PrevKey;
        internal static ConfigEntry<bool> InteractOpensMenu;
        internal static ConfigEntry<NumberKeysMode> NumberKeysChoice;
        internal static ConfigEntry<bool> WheelSelect;
        internal static ConfigEntry<float> MenuScale;
        internal static ConfigEntry<bool> Hints;
        internal static ConfigEntry<bool> DemandEnabled;
        internal static ConfigEntry<KeyboardShortcut> DemandKey;
        internal static ConfigEntry<bool> KneelEnabled;
        internal static ConfigEntry<bool> HandsOnHeadOrder;
        internal static ConfigEntry<bool> TurnOrder;
        internal static ConfigEntry<bool> KitOrder;
        internal static ConfigEntry<bool> ArmourOrder;
        internal static ConfigEntry<bool> FollowOrder;
        internal static ConfigEntry<bool> PartialCompliance;
        internal static ConfigEntry<bool> BotVoice;
        internal static ConfigEntry<bool> Chatter;
        internal static ConfigEntry<bool> Acknowledge;
        internal static ConfigEntry<bool> DefiantRemarks;
        internal static ConfigEntry<bool> CaptorSpeech;
        internal static ConfigEntry<bool> CommandSpeech;
        internal static ConfigEntry<float> InteractionRange;
        internal static ConfigEntry<float> FollowDistance;
        internal static ConfigEntry<float> FollowTolerance;
        internal static ConfigEntry<float> DropItemDelay;
        internal static ConfigEntry<bool> KneelHandsOnHead;
        internal static ConfigEntry<bool> TurnFromKneeling;
        internal static ConfigEntry<float> DemandRange;
        internal static ConfigEntry<float> DemandCooldown;
        internal static ConfigEntry<float> DemandWindow;
        internal static ConfigEntry<float> DemandPressure;
        internal static ConfigEntry<float> DemandChanceBoost;
        internal static ConfigEntry<float> KeepWeaponChance;
        internal static ConfigEntry<float> KeepOnlyWeapon;
        internal static ConfigEntry<float> RepeatComply;
        internal static ConfigEntry<float> RepeatEscalate;
        internal static ConfigEntry<float> KeptDrawBoost;
        internal static ConfigEntry<int> CaptorRepeats;
        internal static ConfigEntry<float> HolsterSeenWithin;
        internal static ConfigEntry<float> VoiceCooldown;
        internal static ConfigEntry<float> ChatterMin;
        internal static ConfigEntry<float> ChatterMax;
        internal static ConfigEntry<float> ChatterChance;
        internal static ConfigEntry<float> ReplyChance;
        internal static ConfigEntry<float> ChatterRadius;
        internal static ConfigEntry<float> ChatterGap;
        internal static ConfigEntry<float> AckDelayMin;
        internal static ConfigEntry<float> AckDelayMax;
        internal static ConfigEntry<float> CaptorVoiceGap;
        internal static ConfigEntry<bool> SquadFloor;
        internal static ConfigEntry<float> SquadFloorGap;

        private static void BindCommands(ConfigFile c)
        {
            MenuKey = c.Bind(S5, "Menu key", new KeyboardShortcut(KeyCode.Slash),
                Text("Opens the order menu while you look at a held bot; press again to give the highlighted order.", false));
            NextKey = c.Bind(S5, "Next order key", new KeyboardShortcut(KeyCode.Period),
                Text("Moves the highlight down.", false));
            PrevKey = c.Bind(S5, "Previous order key", new KeyboardShortcut(KeyCode.Equals),
                Text("Moves the highlight up.", false));
            InteractOpensMenu = c.Bind(S5, "Interact key opens the menu", true,
                Text("Looking at a held man within order range: your Interact key (F unless rebound) opens the menu and gives the highlighted order. The key is kept from the game meanwhile, so it never also opens a door or loots.", false));
            NumberKeysChoice = c.Bind(S5, "Number keys", NumberKeysMode.Numpad,
                Text("While the menu is open, 1-9 give the numbered order and 0 closes it; the hotbar ignores those keys meanwhile. Numpad: the numpad works too. Off: no number keys.", false));
            WheelSelect = c.Bind(S5, "Mouse wheel selects", true,
                Text("While the menu is open the mouse wheel moves the highlight (and doesn't change your walking speed).", false));
            MenuScale = c.Bind(S5, "Menu size", 1f,
                Range("On top of the automatic scaling to your screen height.", 0.6f, 2f, false));
            Hints = c.Bind(S5, "On-screen hints", true,
                Text("A small prompt when you can give orders, and debug confirmations.", false));
            DemandEnabled = c.Bind(S5, "Stop demand", false,
                Text("Saying the game's Stop line to a bot under your crosshair asks him to surrender: it brings his surrender check forward and adds pressure; it doesn't make him comply.", false));
            DemandKey = c.Bind(S5, "Shout Stop key", KeyboardShortcut.Empty,
                Text("Optional. Makes your character say the Stop line; counts as the same demand.", false));
            KneelEnabled = c.Bind(S5, "Kneel on Get down", true,
                Text("Get down puts him on both knees with his hands behind his head; On your feet brings him back up. Off (or where the ground doesn't allow it): a crouch with hands up.", false));
            HandsOnHeadOrder = c.Bind(S5, "Hands on head order", true,
                Text("The menu offers Hands on head (and Hands up to undo it). A man still holding something drops it first.", false));
            TurnOrder = c.Bind(S5, "Turn around order", true,
                Text("The menu offers Turn around: he turns his back to whoever gave the order and stays that way.", false));
            KitOrder = c.Bind(S5, "Kit order", true,
                Text("The menu offers the order to drop his kit.", false));
            ArmourOrder = c.Bind(S5, "Armour order", true,
                Text("The menu offers the order to drop his armour.", false));
            FollowOrder = c.Bind(S5, "Follow order", true,
                Text("The menu offers Follow me.", false));
            PartialCompliance = c.Bind(S5, "Prisoners may keep a weapon", true,
                Text("Drop your weapons covers every gun and his knife. Each time he decides whether to give up all of them or quietly keep one (a sidearm or knife first). Off: he always drops everything.", false));
            BotVoice = c.Bind(S5, "Bot voice lines", true,
                Text("Held bots use the game's own lines (hold fire, roger, negative...).", false));
            Chatter = c.Bind(S5, "Talk while held", true,
                Text("A held bot sometimes speaks on his own (mutters, asks for help) and replies to what you do. Lines his voice lacks are skipped.", false));
            Acknowledge = c.Bind(S5, "Answer orders out loud", true,
                Text("He answers an order (roger, going; negative if he can't) after a short, varied delay.", false));
            DefiantRemarks = c.Bind(S5, "Defiant remarks", true,
                Text("Bold bots sometimes insult you instead. Only Scav voices have such lines.", false));
            CaptorSpeech = c.Bind(S5, "Captors speak", true,
                Text("Bot captors talk: surrender demands, orders, warnings, hand-overs, in their own voice. The order applies either way.", false));
            CommandSpeech = c.Bind(S5, "Speak your orders", false,
                Text("Your character says a matching line when you give an order from the menu. Never counts as a Stop demand.", false));
            InteractionRange = c.Bind(S5, "Order range (m)", 3f,
                Range("How close you must be to give orders.", 1f, 8f, true));
            FollowDistance = c.Bind(S5, "Follow distance (m)", 4f,
                Range("How far behind you a following prisoner walks.", 1f, 15f, true));
            FollowTolerance = c.Bind(S5, "Follow slack (m)", 1f,
                Range("How far you can move before he walks after you.", 0.25f, 5f, true));
            DropItemDelay = c.Bind(S5, "Time per dropped item (s)", 0.4f,
                Range("How long each item takes when he drops his kit.", 0.1f, 3f, true));
            KneelHandsOnHead = c.Bind(S5, "Get down puts hands on head", true,
                Text("Get down also puts his hands on his head, unless he was told Hands up during this surrender. Off: Get down never moves his hands.", true));
            TurnFromKneeling = c.Bind(S5, "Turn around from his knees", true,
                Text("A kneeling man told to turn gets up, turns and kneels again facing away. Off: the order is refused while he kneels.", true));
            DemandRange = c.Bind(S5, "Stop: range (m)", 20f,
                Range("He must be under your crosshair, this close, with a clear line to you.", 2f, 60f, true));
            DemandCooldown = c.Bind(S5, "Stop: cooldown (s)", 8f,
                Range("Per bot: repeated shouting within this time does nothing more.", 1f, 60f, true));
            DemandWindow = c.Bind(S5, "Stop: lasts (s)", 6f,
                Range("How long the demand weighs on him (it doesn't stack).", 1f, 30f, true));
            DemandPressure = c.Bind(S5, "Stop: pressure", 0.2f,
                Range("Added to his pressure while it lasts; also counts as a reason.", 0f, 1f, true));
            DemandChanceBoost = c.Bind(S5, "Stop: chance boost", 1.5f,
                Range("Multiplies his chance on the checks while it lasts.", 1f, 5f, true));
            KeepWeaponChance = c.Bind(S5, "Keep one back chance", 0.2f,
                Range("Base chance on the first demand that he keeps a weapon, before nerve, personality, wounds and how many guns cover him. Never more than 0.6.", 0f, 1f, true));
            KeepOnlyWeapon = c.Bind(S5, "Keep his only weapon", 0.4f,
                Range("Multiplies that chance when the weapon he'd keep is his only one.", 0f, 1f, true));
            RepeatComply = c.Bind(S5, "Comply when told again", 0.7f,
                Range("Base chance he hands over what he kept when the demand is repeated.", 0f, 1f, true));
            RepeatEscalate = c.Bind(S5, "Escalate when told again", 0.06f,
                Range("Base chance a repeated demand makes a bold man go for the weapon he kept, there and then.", 0f, 0.5f, true));
            KeptDrawBoost = c.Bind(S5, "Kept weapon draw weight", 1.3f,
                Range("A man who kept a weapon on purpose weighs drawing it this much more when a real opening comes.", 1f, 3f, true));
            CaptorRepeats = c.Bind(S5, "Captor repeats a demand at most", 2,
                IntRange("Times a bot captor who noticed a kept weapon demands it again before he decides what to do.", 0, 4, true));
            HolsterSeenWithin = c.Bind(S5, "Holstered pistol seen within (m)", 4f,
                Range("A bot captor notices a pistol left in the holster only this close, in sight.", 0f, 15f, true));
            VoiceCooldown = c.Bind(S5, "Voice cooldown (s)", 12f,
                Range("Least time between two lines of one held bot.", 1f, 120f, true));
            ChatterMin = c.Bind(S5, "Talk every min (s)", 18f,
                Range("How often he gets a chance to speak on his own, between min and max.", 5f, 300f, true));
            ChatterMax = c.Bind(S5, "Talk every max (s)", 45f,
                Range("Upper end of that interval.", 5f, 300f, true));
            ChatterChance = c.Bind(S5, "Talk chance", 0.5f,
                Range("Chance he actually says something at each of those moments.", 0f, 1f, true));
            ReplyChance = c.Bind(S5, "Reply chance", 0.6f,
                Range("Chance he reacts out loud to something you do.", 0f, 1f, true));
            ChatterRadius = c.Bind(S5, "Take turns within (m)", 25f,
                Range("Held bots this close to each other don't talk over one another.", 0f, 100f, true));
            ChatterGap = c.Bind(S5, "Gap between speakers (s)", 1.5f,
                Range("Pause between two held bots' lines.", 0f, 10f, true));
            AckDelayMin = c.Bind(S5, "Answer delay min (s)", 0.4f,
                Range("Delay before he answers an order, between min and max.", 0f, 5f, true));
            AckDelayMax = c.Bind(S5, "Answer delay max (s)", 1.2f,
                Range("Upper end of the answer delay.", 0f, 5f, true));
            CaptorVoiceGap = c.Bind(S5, "Captor line gap (s)", 3.5f,
                Range("Least time between two non-urgent lines of one captor.", 0.5f, 30f, true));
            SquadFloor = c.Bind(S5, "One voice per encounter", true,
                Text("The captor gives the orders; a covering squadmate only speaks for his role. One line at a time per encounter; urgent warnings interrupt.", true));
            SquadFloorGap = c.Bind(S5, "Gap between voices in an encounter (s)", 1.2f,
                Range("After one man of the encounter finishes a line, another non-urgent line waits this long.", 0f, 5f, true));
        }
    }
}
