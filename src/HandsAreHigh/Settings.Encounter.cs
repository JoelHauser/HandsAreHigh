using BepInEx.Configuration;

namespace HandsAreHigh
{
    // 0.23.0: how an encounter ends and what both sides do next (Encounter.cs), squad speech
    // (CaptorVoice floor), equipment demands (GearNeeds.cs) and the approach to dropped gear
    // (CaptorLoot.StandPoint). Same rules as Settings.cs: no = \ " ' [ ] in section or key names
    // (scripts\pack.ps1 checks).
    internal static partial class Settings
    {
        private const string EndSec = "25. Encounter endings";

        internal static ConfigEntry<bool> NaturalEndings;
        internal static ConfigEntry<float> LingerMin;
        internal static ConfigEntry<float> LingerMax;
        internal static ConfigEntry<float> WalkAwayChance;
        internal static ConfigEntry<bool> GuardStaysBehind;
        internal static ConfigEntry<bool> PrisonerNoticesAbandon;
        internal static ConfigEntry<float> AbandonAssessMin;
        internal static ConfigEntry<float> AbandonAssessMax;
        internal static ConfigEntry<float> NoRecaptureSeconds;
        internal static ConfigEntry<bool> SquadFloor;
        internal static ConfigEntry<float> SquadFloorGap;
        internal static ConfigEntry<bool> GearDemands;
        internal static ConfigEntry<bool> LootStandBeside;
        internal static ConfigEntry<bool> LogEncounter;

        private static void BindEncounter(ConfigFile c)
        {
            NaturalEndings = c.Bind(EndSec, "Captors end encounters on their own", true,
                "Once a bot captor has done what he took the man for (disarmed him, taken what he wanted) he decides what next: tell him to go, simply walk away and carry on with his raid, or (in a squad) leave a squadmate to watch him. Weighed by personality, threats about, his squad moving on and anything still worth doing. Off: every encounter waits for the hold time and ends with a Leave order (0.22 behaviour).");
            LingerMin = c.Bind(EndSec, "Lingers after he is done min (s)", 2f,
                Range("How long a captor stays once nothing is left to do, before the ending he chose (x personality: cautious men linger longer, aggressive ones less).", 0f, 30f));
            LingerMax = c.Bind(EndSec, "Lingers after he is done max (s)", 9f, Range("", 0f, 60f));
            WalkAwayChance = c.Bind(EndSec, "Walk away instead of a Leave order", 0.45f,
                Range("Base weight of simply walking off (no order: the prisoner is left standing or kneeling) against telling him to go. Raised by an aggressive personality, an enemy seen nearby, his squad moving on, the prisoner on his knees or turned away; lowered by caution.", 0f, 1f));
            GuardStaysBehind = c.Bind(EndSec, "A squadmate may stay behind to watch him", true,
                "When the captor walks away and a squadmate is covering the prisoner, the captor may hand custody to him: the squad moves on, one guard stays a little longer, then ends it his own way.");
            PrisonerNoticesAbandon = c.Bind(EndSec, "Prisoners notice they were left", true,
                "A held man whose captor walks away (back turned, walking off, gun away from him, or out of sight) and whom nobody else is visibly covering takes it in over a personality-dependent moment, then lowers his hands, gets up if kneeling, and goes back to his own AI (may pick up his own gun first if it is still there and safe). A squadmate still covering him keeps him in place. Off: the 0.22 reaction (a run, hide or draw as if his captor vanished).");
            AbandonAssessMin = c.Bind(EndSec, "Prisoner takes in being left min (s)", 2.5f,
                Range("How long a held man watches his captor leave before he believes it (x nerve and personality: frightened men wait longer).", 0.5f, 30f));
            AbandonAssessMax = c.Bind(EndSec, "Prisoner takes in being left max (s)", 9f, Range("", 0.5f, 60f));
            NoRecaptureSeconds = c.Bind(EndSec, "No recapture after an ending for (s)", 60f,
                Range("After a captor lets a man go or walks away from him, that captor (and his squad) don't take the same man prisoner again for this long, and the man doesn't surrender to them again. Not immunity: fighting stays possible (a weapon in his hands ends any hold-fire).", 0f, 300f));
            SquadFloor = c.Bind(EndSec, "One voice per encounter", true,
                "Squad speech: the captor (custody) gives the orders; a covering squadmate speaks only for his role (covering, a threat, the prisoner going for a weapon). One line at a time per encounter with a short gap; urgent combat warnings (contact, incoming fire, a draw) interrupt.");
            SquadFloorGap = c.Bind(EndSec, "Gap between voices in an encounter (s)", 1.2f,
                Range("After one man of the encounter (captor, guard or prisoner) finishes a line, another non-urgent line waits this long.", 0f, 5f));
            GearDemands = c.Bind(EndSec, "Captors demand gear they lack", true,
                "A bot captor with no body armour, no rig or no backpack may order his prisoner to drop one he is wearing, if it suits him: the game itself must be able to put it in his empty slot, and it must be worth having (armour class and durability, carrying space). One demand per slot per encounter, weighed again after each transfer; then he picks it up the normal way (contents kept).");
            LootStandBeside = c.Bind(EndSec, "Captors step beside dropped gear", true,
                "A captor fetching an item picks a spot beside it (in reach, clear of the prisoner's body and the walls, on the NavMesh, reachable) instead of walking at the item itself, and keeps it unless the prisoner moves into the way. No spot: he asks the prisoner to step back once, or leaves it for after the release. Off: the 0.22 walk to the item.");
            LogEncounter = c.Bind(EndSec, "Log encounter endings", true,
                "Log the captor's wrap-up decision (with every input), hand-backs to the bots' own AI (and which layer drives them a few seconds later), the prisoner's assessment of being left, speech-floor waits (Verbose only), gear demands, and the stand spot chosen for each fetch. No per-frame lines.");
        }
    }
}
