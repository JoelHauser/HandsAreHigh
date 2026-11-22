using BepInEx.Configuration;
using UnityEngine;

namespace Mercy
{
    // F12 section "3. Captors and squads": bots taking, sparing or ignoring prisoners; squads; endings.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<float> AcceptChance;
        internal static ConfigEntry<float> SpareChance;
        internal static ConfigEntry<bool> IgnoreSurrender;
        internal static ConfigEntry<float> IgnoreChance;
        internal static ConfigEntry<float> IgnoreInfluence;
        internal static ConfigEntry<float> IgnoreTruceSeconds;
        internal static ConfigEntry<int> MaxAiEncounters;
        internal static ConfigEntry<float> HoldMin;
        internal static ConfigEntry<float> HoldMax;
        internal static ConfigEntry<bool> AlliedCaptors;
        internal static ConfigEntry<bool> NaturalEndings;
        internal static ConfigEntry<bool> GuardStaysBehind;
        internal static ConfigEntry<bool> CaptorLooting;
        internal static ConfigEntry<bool> GearDemands;
        internal static ConfigEntry<float> CaptorKneelChance;
        internal static ConfigEntry<float> CaptorHandsOnHeadChance;
        internal static ConfigEntry<float> CaptorTurnChance;
        internal static ConfigEntry<float> CaptorCloseChance;
        internal static ConfigEntry<float> LethalIntent;
        internal static ConfigEntry<float> BoltLethal;
        internal static ConfigEntry<bool> SquadRestraint;
        internal static ConfigEntry<bool> ObserverRestraint;
        internal static ConfigEntry<bool> SquadCover;
        internal static ConfigEntry<float> AcceptRange;
        internal static ConfigEntry<float> CaptorApproachFrom;
        internal static ConfigEntry<float> CaptorGuardDistance;
        internal static ConfigEntry<float> CaptorApproachSpeed;
        internal static ConfigEntry<float> CaptorStandoff;
        internal static ConfigEntry<float> OrderGapMin;
        internal static ConfigEntry<float> OrderGapMax;
        internal static ConfigEntry<float> AiOrderRange;
        internal static ConfigEntry<float> BusyRelease;
        internal static ConfigEntry<float> QuietBeforeGuarding;
        internal static ConfigEntry<float> MinEngageSeconds;
        internal static ConfigEntry<bool> CaptorGlances;
        internal static ConfigEntry<float> LingerMin;
        internal static ConfigEntry<float> LingerMax;
        internal static ConfigEntry<float> WalkAwayChance;
        internal static ConfigEntry<float> NoRecaptureSeconds;
        internal static ConfigEntry<float> LootChance;
        internal static ConfigEntry<float> LootMinValue;
        internal static ConfigEntry<int> LootMaxItems;
        internal static ConfigEntry<float> LootRisk;
        internal static ConfigEntry<bool> CollectDemanded;
        internal static ConfigEntry<float> DemandedRisk;
        internal static ConfigEntry<float> MeleePickupChance;
        internal static ConfigEntry<bool> LootAfterRelease;
        internal static ConfigEntry<float> LootTimeLimit;
        internal static ConfigEntry<bool> LootStandBeside;
        internal static ConfigEntry<float> LootNearDistance;
        internal static ConfigEntry<float> LootMaxDistance;
        internal static ConfigEntry<float> LootDistanceWeight;
        internal static ConfigEntry<float> LootReweighSeconds;
        internal static ConfigEntry<int> LootMaxPostpone;
        internal static ConfigEntry<float> LootSightRange;
        internal static ConfigEntry<float> LootSearchSeconds;
        internal static ConfigEntry<float> RivalWeight;
        internal static ConfigEntry<float> ViolenceWeight;
        internal static ConfigEntry<float> ViolenceMemory;
        internal static ConfigEntry<float> IncompleteWeight;
        internal static ConfigEntry<float> RefusalLethal;
        internal static ConfigEntry<float> ExecuteMax;
        internal static ConfigEntry<float> SquadNoticeRadius;
        internal static ConfigEntry<float> SquadRejectChance;
        internal static ConfigEntry<float> WatchSeconds;
        internal static ConfigEntry<float> ObserverRadius;
        internal static ConfigEntry<float> ObserverRespect;
        internal static ConfigEntry<float> FleeingRespect;
        internal static ConfigEntry<float> RestraintMemory;
        internal static ConfigEntry<float> ExPrisonerHoldFire;
        internal static ConfigEntry<float> GuardDistance;
        internal static ConfigEntry<float> CoverWait;
        internal static ConfigEntry<float> GuardReaction;
        internal static ConfigEntry<bool> GuardScans;
        internal static ConfigEntry<float> GuardGiveUp;

        private static void BindCaptors(ConfigFile c)
        {
            AcceptChance = c.Bind(S3, "Take prisoner chance", 0.65f,
                Range("When guarding him is practical (gun with rounds, not under fire, no other threat in sight, prisoner in sight and close): the chance the bot takes custody, before personality.", 0f, 1f, false));
            SpareChance = c.Bind(S3, "Spare chance", 0.75f,
                Range("Not taking custody: the chance he still holds his fire instead of fighting on. Being unable to guard a man never by itself means killing him.", 0f, 1f, false));
            IgnoreSurrender = c.Bind(S3, "Ignore surrenders", true,
                Text("A bot may decide a man giving up to him isn't worth his time: no custody, no orders, no walk over for gear, no fight. He holds fire and goes on with his raid. The man soon notices, lowers his hands, takes his gun back if he safely can (or moves to safety), and the two leave each other be for a while. Other bots still guarding the man keep him covered.", false));
            IgnoreChance = c.Bind(S3, "Ignore chance", 0.2f,
                Range("Base chance a bot ignores a surrender, rolled once before he decides anything else.", 0f, 1f, false));
            IgnoreInfluence = c.Bind(S3, "Ignore: personality and situation", 1f,
                Range("0: every bot uses the plain ignore chance. 1: full influence. Bold or timid bots, a bot busy with another threat, far away or unable to guard ignore more; careful ones, looters, rivals and men with valuable kit get more attention. Never a man who hurt him.", 0f, 1f, false));
            IgnoreTruceSeconds = c.Bind(S3, "Truce after an ignored surrender (s)", 30f,
                Range("After an ignored surrender the man and that bot (and the squadmates who held fire) don't fire at each other for this long, armed or not, so a target left over from the fight can't start a firefight. Ends at once if either hits the other. 0: no truce.", 0f, 180f, false));
            MaxAiEncounters = c.Bind(S3, "Bot-held prisoners at once", 4,
                IntRange("Across the map. Further surrenders to bots are spared or refused instead of taken.", 0, 16, false));
            HoldMin = c.Bind(S3, "Hold time min (s)", 25f,
                Range("How long a bot keeps a prisoner before letting him go (or earlier, by his plan). Never indefinite.", 5f, 300f, false));
            HoldMax = c.Bind(S3, "Hold time max (s)", 70f,
                Range("Upper end of the hold time.", 5f, 600f, false));
            AlliedCaptors = c.Bind(S3, "Your allies make bots surrender", true,
                Text("Bots on your side can make a bot give up and hold fire, but never guard him themselves; you can take over their prisoner with the order menu.", false));
            NaturalEndings = c.Bind(S3, "Captors end encounters on their own", true,
                Text("Once done with a prisoner (disarmed, gear taken), a bot decides what next: tell him to go, walk away, or leave a squadmate to watch him. Off: every encounter waits for the hold time and ends with a Leave order.", false));
            GuardStaysBehind = c.Bind(S3, "Squadmate may stay behind", true,
                Text("A captor walking away may hand his prisoner to the squadmate covering him; the squad moves on and the guard follows a little later.", false));
            CaptorLooting = c.Bind(S3, "Captors take dropped gear", true,
                Text("A bot captor weighs what his prisoner dropped (value, gaps in his own kit, room, safety, his nature) and may take some, all or nothing. Real inventory moves only.", false));
            GearDemands = c.Bind(S3, "Captors demand gear", true,
                Text("A captor without body armour, a rig or a backpack may order his prisoner to drop one that fits him and is worth having, then picks it up.", false));
            CaptorKneelChance = c.Bind(S3, "Kneel order chance", 0.4f,
                Range("Chance a captor's plan orders his prisoner onto his knees (needs Kneel on Get down, section 5).", 0f, 1f, false));
            CaptorHandsOnHeadChance = c.Bind(S3, "Hands on head order chance", 0.35f,
                Range("Chance a captor's plan includes Hands on head once the prisoner is disarmed.", 0f, 1f, false));
            CaptorTurnChance = c.Bind(S3, "Turn around order chance", 0.3f,
                Range("Chance a captor's plan includes Turn around (before he takes the prisoner's gear, or to keep him looking away).", 0f, 1f, false));
            CaptorCloseChance = c.Bind(S3, "Close in chance", 0.55f,
                Range("Chance (once per custody) a captor walks closer to a disarmed prisoner to give orders and control him. Only with nothing threatening him; stops on any threat.", 0f, 1f, false));
            LethalIntent = c.Bind(S3, "Lethal intent chance", 0.25f,
                Range("Base chance, decided once when a bot takes custody, that he means to shoot the man when he is done with him, even if he complies. More for aggressive personalities and rivals, less for timid ones.", 0f, 1f, false));
            BoltLethal = c.Bind(S3, "Fire at a runner chance", 0.3f,
                Range("Base chance a captor opens fire on a prisoner who runs from custody unarmed instead of letting him go. A man going for a weapon is always fought.", 0f, 1f, false));
            SquadRestraint = c.Bind(S3, "Squad holds fire", true,
                Text("When a bot takes, spares or ignores a prisoner, his nearby squadmates hold their fire at that man too. Every other enemy is fought as normal.", false));
            ObserverRestraint = c.Bind(S3, "Other bots may spare him", true,
                Text("Bots outside the captor's squad who see a man with his hands up decide for themselves: hold fire, ignore him for another threat, let him leave, or fight.", false));
            SquadCover = c.Bind(S3, "Squadmate covers the captor", true,
                Text("One free squadmate takes a spot to the side and covers the prisoner while the captor works. Never one of your allies.", false));
            AcceptRange = c.Bind(S3, "Guard range (m)", 20f,
                Range("Farther than this a bot won't take custody unless he can walk up to the man. The surrender itself can happen at any range.", 4f, 80f, true));
            CaptorApproachFrom = c.Bind(S3, "Walk up from (m)", 60f,
                Range("A captor farther than his order range walks up along a complete path, only while nothing threatens him. 0: never walks up.", 0f, 120f, true));
            CaptorGuardDistance = c.Bind(S3, "Guard distance (m)", 8f,
                Range("Where a captor who walks up stops (closer only if he lost sight of the prisoner).", 3f, 20f, true));
            CaptorApproachSpeed = c.Bind(S3, "Walk-up speed", 0.8f,
                Range("Fraction of his walking speed (never a sprint).", 0.3f, 1f, true));
            CaptorStandoff = c.Bind(S3, "Close-in distance (m)", 3.5f,
                Range("Where a captor who closes in stops: out of arm's reach, never on the dropped gun.", 2.5f, 8f, true));
            OrderGapMin = c.Bind(S3, "Between orders min (s)", 2.5f,
                Range("Time between a bot captor's orders, between min and max.", 0.5f, 30f, true));
            OrderGapMax = c.Bind(S3, "Between orders max (s)", 7f,
                Range("Upper end of the time between orders.", 0.5f, 60f, true));
            AiOrderRange = c.Bind(S3, "Bot order range (m)", 15f,
                Range("A bot only gives an order within this range and in sight.", 3f, 30f, true));
            BusyRelease = c.Bind(S3, "Give up custody after busy (s)", 12f,
                Range("Pulled into another fight this long, a captor stops guarding; the prisoner is on his own and the hold-fire stays as decided.", 2f, 120f, true));
            QuietBeforeGuarding = c.Bind(S3, "Back to guarding after quiet (s)", 2.5f,
                Range("After a fight he goes back to guarding once no enemy is in sight and no fire came in for this long.", 0.5f, 20f, true));
            MinEngageSeconds = c.Bind(S3, "Fight at least (s)", 3f,
                Range("Once he turns to a threat he stays on it at least this long (no flip-flopping).", 0.5f, 20f, true));
            CaptorGlances = c.Bind(S3, "Captors look around", true,
                Text("A guarding captor glances away now and then. Each glance is a real opening the prisoner can see.", true));
            LingerMin = c.Bind(S3, "Ending: linger min (s)", 2f,
                Range("How long a captor stays once nothing is left to do, before the ending he chose (cautious men longer).", 0f, 30f, true));
            LingerMax = c.Bind(S3, "Ending: linger max (s)", 9f,
                Range("Upper end of the linger time.", 0f, 60f, true));
            WalkAwayChance = c.Bind(S3, "Ending: walk away weight", 0.45f,
                Range("Base weight of simply walking off (the prisoner is left standing or kneeling) against telling him to go.", 0f, 1f, true));
            NoRecaptureSeconds = c.Bind(S3, "Ending: no recapture for (s)", 60f,
                Range("After a captor lets a man go, walks away or ignores him, he and his squad don't take the same man again for this long, and the man doesn't give up to them again. Fighting stays possible.", 0f, 300f, true));
            LootChance = c.Bind(S3, "Gear: appetite", 0.6f,
                Range("Base appetite for an item worth taking, before personality (Rats most, cautious men least).", 0f, 1f, true));
            LootMinValue = c.Bind(S3, "Gear: worth taking from (roubles)", 8000f,
                Range("Handbook value below which an item isn't worth the walk, unless it fills an empty slot of his.", 0f, 200000f, true));
            LootMaxItems = c.Bind(S3, "Gear: items per prisoner", 3,
                IntRange("Most items a captor takes from one prisoner.", 0, 8, true));
            LootRisk = c.Bind(S3, "Gear: risk accepted", 0.5f,
                Range("How much loss of control he accepts to fetch an item (0: only with cover or the prisoner kneeling and turned away; 1: regardless).", 0f, 1f, true));
            CollectDemanded = c.Bind(S3, "Gear: collect demanded weapons", true,
                Text("Weapons the prisoner dropped because this captor demanded it are always wanted and fetched first, when they fit and the walk is safe enough. Off: weighed like any other item.", true));
            DemandedRisk = c.Bind(S3, "Gear: risk accepted for weapons", 0.9f,
                Range("Like Gear: risk accepted, for the weapons he made him drop.", 0f, 2f, true));
            MeleePickupChance = c.Bind(S3, "Gear: knife pick-up chance", 0.3f,
                Range("Base chance a captor bothers with a dropped knife (more if he has none); leaving it is final.", 0f, 1f, true));
            LootAfterRelease = c.Bind(S3, "Gear: fetch after release", true,
                Text("Gear he wanted but didn't dare fetch while guarding is picked up once the released man is well away, if nothing threatens him.", true));
            LootTimeLimit = c.Bind(S3, "Gear: give up fetching after (s)", 20f,
                Range("Per item.", 5f, 60f, true));
            LootStandBeside = c.Bind(S3, "Gear: step beside it", true,
                Text("A captor fetching an item stands beside it (clear of the prisoner and walls) instead of walking onto it. Off: walks straight to the item.", true));
            LootNearDistance = c.Bind(S3, "Gear: close by within (m)", 4f,
                Range("Walking distance within which an item counts as close; farther items get a deliberate decision.", 1f, 15f, true));
            LootMaxDistance = c.Bind(S3, "Gear: never farther than (m)", 30f,
                Range("Items farther than this (walking distance) are left where they are.", 5f, 80f, true));
            LootDistanceWeight = c.Bind(S3, "Gear: distance weight", 1f,
                Range("How much the walk counts against fetching a distant item. 0: distance costs nothing.", 0f, 3f, true));
            LootReweighSeconds = c.Bind(S3, "Gear: reweigh after (s)", 4f,
                Range("A postponed item is weighed again this often, or as soon as the situation changes.", 1f, 30f, true));
            LootMaxPostpone = c.Bind(S3, "Gear: postpone at most", 3,
                IntRange("After this many postponements he stops weighing it while he guards the man.", 0, 10, true));
            LootSightRange = c.Bind(S3, "Gear: sees it land within (m)", 40f,
                Range("He only knows where an item lies if he saw it land there; otherwise he looks for it for a while.", 5f, 100f, true));
            LootSearchSeconds = c.Bind(S3, "Gear: search for (s)", 12f,
                Range("How long he looks for an item he never saw land.", 2f, 60f, true));
            RivalWeight = c.Bind(S3, "Judgement: rival faction weight", 1.4f,
                Range("Multiplies every lethal chance when captor and prisoner are rivals: BEAR and USEC, or a Scav and a PMC.", 1f, 4f, true));
            ViolenceWeight = c.Bind(S3, "Judgement: he shot me weight", 2.5f,
                Range("Multiplies the lethal chance when this prisoner hit him or his squad recently.", 1f, 6f, true));
            ViolenceMemory = c.Bind(S3, "Judgement: remembers being shot (s)", 120f,
                Range("How long being hit by this man counts.", 10f, 900f, true));
            IncompleteWeight = c.Bind(S3, "Judgement: kept a weapon weight", 1.8f,
                Range("Multiplies the lethal chance when he saw the prisoner keep a weapon back.", 1f, 6f, true));
            RefusalLethal = c.Bind(S3, "Judgement: refusal turns lethal", 0.25f,
                Range("Base chance he opens fire when the prisoner still keeps a weapon after repeated demands.", 0f, 1f, true));
            ExecuteMax = c.Bind(S3, "Judgement: lethal cap", 0.85f,
                Range("Cap on any one lethal decision.", 0f, 1f, true));
            SquadNoticeRadius = c.Bind(S3, "Squad: hears it within (m)", 80f,
                Range("Squadmates this close follow a decision to spare or ignore a man (custody reaches the whole squad).", 5f, 300f, true));
            SquadRejectChance = c.Bind(S3, "Squad: refuses anyway chance", 0.02f,
                Range("Chance a squadmate doesn't go along with his squad's decision. 0 forbids it.", 0f, 1f, true));
            WatchSeconds = c.Bind(S3, "Squad: watch him for (s)", 15f,
                Range("Idle squadmates of the captor near the prisoner stand and watch him this long. 0: never.", 0f, 120f, true));
            ObserverRadius = c.Bind(S3, "Others: notice within (m)", 50f,
                Range("Other bots notice a surrendered man this close.", 5f, 150f, true));
            ObserverRespect = c.Bind(S3, "Others: hold fire chance", 0.8f,
                Range("Base chance a bot holds fire at a man with his hands up, before personality, danger and what he has seen him do.", 0f, 1f, true));
            FleeingRespect = c.Bind(S3, "Others: let him run chance", 0.7f,
                Range("Base chance a bot that spared him lets him run unarmed instead of firing.", 0f, 1f, true));
            RestraintMemory = c.Bind(S3, "Hold-fire decision kept for (s)", 120f,
                Range("A decision about a surrendered man is kept this long unless something meaningful changes (he draws, hits someone, runs).", 10f, 900f, true));
            ExPrisonerHoldFire = c.Bind(S3, "Hold fire after an unarmed release (s)", 30f,
                Range("Bots that held fire at a man keep holding it this long after he is let go unarmed, so his ex-captor deals with whoever is shooting instead. Ends at once if he takes a weapon in his hands. 0: off.", 0f, 120f, true));
            GuardDistance = c.Bind(S3, "Cover: distance from prisoner (m)", 6f,
                Range("Where the covering squadmate prefers to stand, off to the side of the captor.", 3f, 12f, true));
            CoverWait = c.Bind(S3, "Cover: captor waits (s)", 6f,
                Range("Before fetching gear or closing in, a captor whose cover isn't in place waits this long, then goes ahead. 0: never waits.", 0f, 20f, true));
            GuardReaction = c.Bind(S3, "Cover: reaction time (s)", 0.3f,
                Range("How fast the covering squadmate reacts to a prisoner going for a weapon (slower from the corner of his eye or while moving).", 0.1f, 1.5f, true));
            GuardScans = c.Bind(S3, "Cover: guard looks around", true,
                Text("The covering squadmate looks out now and then; those moments are real openings for the prisoner.", true));
            GuardGiveUp = c.Bind(S3, "Cover: leave role after fighting (s)", 12f,
                Range("A covering squadmate drawn into a fight for longer than this leaves the role; another may take it.", 3f, 60f, true));
        }
    }
}
