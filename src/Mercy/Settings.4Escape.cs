using BepInEx.Configuration;
using UnityEngine;

namespace Mercy
{
    // F12 section "4. Prisoner escape and recovery": openings, escape, recovery, hand-back to the AI.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<bool> PrisonerNoticesAbandon;
        internal static ConfigEntry<float> IgnoredRecoverRange;
        internal static ConfigEntry<bool> RecoverOwnWeapon;
        internal static ConfigEntry<bool> ScavengeWeapons;
        internal static ConfigEntry<bool> HighNerveKnife;
        internal static ConfigEntry<float> BreakChance;
        internal static ConfigEntry<bool> FireBreaks;
        internal static ConfigEntry<bool> DefendWhenFleeing;
        internal static ConfigEntry<bool> QuickHandBack;
        internal static ConfigEntry<bool> LocalReactions;
        internal static ConfigEntry<float> OpeningDelay;
        internal static ConfigEntry<float> BreakInterval;
        internal static ConfigEntry<float> OpportunityBoost;
        internal static ConfigEntry<float> SquadBoost;
        internal static ConfigEntry<float> KnifeRange;
        internal static ConfigEntry<float> SidearmRange;
        internal static ConfigEntry<float> ScrambleRange;
        internal static ConfigEntry<float> ExposureDistance;
        internal static ConfigEntry<float> EscapeMinDistance;
        internal static ConfigEntry<float> DrawWeight;
        internal static ConfigEntry<float> KnifeWeight;
        internal static ConfigEntry<float> ScrambleWeight;
        internal static ConfigEntry<float> EscapeWeight;
        internal static ConfigEntry<float> HideWeight;
        internal static ConfigEntry<float> ScrambleTimeout;
        internal static ConfigEntry<float> ScrambleCooldown;
        internal static ConfigEntry<float> ReactionMin;
        internal static ConfigEntry<float> ReactionMax;
        internal static ConfigEntry<float> IntimidationSeconds;
        internal static ConfigEntry<float> OrderCompliance;
        internal static ConfigEntry<float> ApproachStep;
        internal static ConfigEntry<bool> Quickdraw;
        internal static ConfigEntry<float> DrawHandoffTimeout;
        internal static ConfigEntry<float> DrawRetrySeconds;
        internal static ConfigEntry<float> WatchAngle;
        internal static ConfigEntry<float> LeaveDistance;
        internal static ConfigEntry<float> WalkAwayMargin;
        internal static ConfigEntry<float> LeaveSeconds;
        internal static ConfigEntry<float> AbandonAssessMin;
        internal static ConfigEntry<float> AbandonAssessMax;
        internal static ConfigEntry<float> WitnessRadius;
        internal static ConfigEntry<float> WitnessEscape;
        internal static ConfigEntry<float> FleeDistance;
        internal static ConfigEntry<float> RetreatDistance;
        internal static ConfigEntry<float> RetreatTimeout;
        internal static ConfigEntry<float> ArrivalTolerance;
        internal static ConfigEntry<float> Grace;
        internal static ConfigEntry<float> ResurrenderDistance;
        internal static ConfigEntry<float> AimTolerance;
        internal static ConfigEntry<float> AimConfirm;
        internal static ConfigEntry<float> RecaptureResistance;
        internal static ConfigEntry<float> FleeCommit;
        internal static ConfigEntry<float> EscapeMemory;
        internal static ConfigEntry<float> KeepAwayRadius;
        internal static ConfigEntry<int> KeepAwayLegs;
        internal static ConfigEntry<float> HeadingLookAhead;
        internal static ConfigEntry<float> QuickHandBackDistance;
        internal static ConfigEntry<float> QuickHandBackSeconds;
        internal static ConfigEntry<float> AvoidRadius;
        internal static ConfigEntry<float> ScavengeRadius;
        internal static ConfigEntry<float> ScavengeAvoid;
        internal static ConfigEntry<float> RecoveryTimeout;
        internal static ConfigEntry<float> PickupRange;
        internal static ConfigEntry<float> PickupTimeout;
        internal static ConfigEntry<float> FireBreakNearMiss;
        internal static ConfigEntry<float> FireBreakQuiet;
        internal static ConfigEntry<float> FireRecoverRange;
        internal static ConfigEntry<float> FireCoverRadius;
        internal static ConfigEntry<float> FireCoverSeconds;
        internal static ConfigEntry<float> CoverRadius;
        internal static ConfigEntry<int> PathCandidates;
        internal static ConfigEntry<int> PathRetries;
        internal static ConfigEntry<float> StallSeconds;
        internal static ConfigEntry<float> ProgressMeters;

        private static void BindEscape(ConfigFile c)
        {
            PrisonerNoticesAbandon = c.Bind(S4, "Notices being left", true,
                Text("A held man whose bot captor walks away, and whom nobody else covers, notices it, lowers his hands and leaves or goes for a gun. Off: he reacts as if his captor vanished (run, hide or draw).", true));
            IgnoredRecoverRange = c.Bind(S4, "Fetch his gun after being ignored (m)", 15f,
                Range("Ignored by the bot he gave up to, a man walks back for his dropped gun if it lies within this walking distance, is reachable and nobody has him covered; otherwise he moves to safety. 0: never.", 0f, 40f, true));
            RecoverOwnWeapon = c.Bind(S4, "Go back for his own gun", true,
                Text("After getting away, he returns for his dropped gun if it is still there and you weren't near it.", false));
            ScavengeWeapons = c.Bind(S4, "Look for another gun", true,
                Text("After getting away unarmed, he may go for a loose gun or one on a body nearby.", false));
            HighNerveKnife = c.Bind(S4, "Fight with a knife", true,
                Text("High-nerve bots with no gun to find come back with their knife.", false));
            BreakChance = c.Bind(S4, "Break chance", 0.15f,
                Range("When you give him an opening (look away, reload, step out of sight): the chance each time he weighs it that he acts. Scaled by his nerve.", 0f, 1f, false));
            FireBreaks = c.Bind(S4, "Incoming fire breaks a hold-up", true,
                Text("A hit or near miss on a prisoner or his bot captor from anyone outside the hold-up ends it: the captor fights, the prisoner gets to safety. A human captor keeps control.", false));
            DefendWhenFleeing = c.Bind(S4, "Fight back when fleeing", true,
                Text("A man getting away who comes under fire and carries a usable gun may draw it instead of running for cover. Off: cover only.", true));
            QuickHandBack = c.Bind(S4, "Quick hand-back to his own AI", true,
                Text("A man let go or left behind is handed to his own AI (SAIN, ORBIT, the game) as soon as he is on his feet and on his way. Off: he flees in legs and regroups first.", true));
            LocalReactions = c.Bind(S4, "Runs when another prisoner is shot", true,
                Text("A held bot who sees another one shot runs.", true));
            OpeningDelay = c.Bind(S4, "Opening: must last (s)", 1.5f,
                Range("For gradual openings (you look away, he loses sight of you) he waits this long on top of his reaction time. Clear ones (reloading, inventory, gun away) only take his reaction time.", 0f, 10f, true));
            BreakInterval = c.Bind(S4, "Opening: roll every (s)", 1f,
                Range("While an opening lasts he reassesses this often (varied a little each time).", 0.1f, 10f, true));
            OpportunityBoost = c.Bind(S4, "Opening: gun nearby boost", 2f,
                Range("His own gun close, reachable and away from you multiplies the break chance by up to 1 + this.", 0f, 5f, true));
            SquadBoost = c.Bind(S4, "Opening: squad nearby boost", 0.5f,
                Range("Free squadmates close by make him up to 1 + this more likely to act on an opening.", 0f, 2f, true));
            KnifeRange = c.Bind(S4, "Opening: knife range (m)", 4f,
                Range("Closer than this a knife is an option.", 0.5f, 10f, true));
            SidearmRange = c.Bind(S4, "Opening: draw range (m)", 30f,
                Range("Closer than this drawing a gun he still carries is an option.", 1f, 100f, true));
            ScrambleRange = c.Bind(S4, "Opening: scramble range (m)", 10f,
                Range("His dropped gun within this distance (and reachable) is an option: he runs for it.", 1f, 30f, true));
            ExposureDistance = c.Bind(S4, "Opening: gun safe from you beyond (m)", 8f,
                Range("The closer you stand to his dropped gun, the less he wants to go for it.", 1f, 30f, true));
            EscapeMinDistance = c.Bind(S4, "Opening: run only beyond (m)", 6f,
                Range("Closer than this, running isn't an option.", 0f, 30f, true));
            DrawWeight = c.Bind(S4, "Opening: draw weight", 1f,
                Range("How the options compare; each is scaled by nerve, personality and the situation.", 0f, 5f, true));
            KnifeWeight = c.Bind(S4, "Opening: knife weight", 0.8f,
                Range("Relative weight of a knife attack.", 0f, 5f, true));
            ScrambleWeight = c.Bind(S4, "Opening: scramble weight", 1.6f,
                Range("Relative weight of running for his dropped gun.", 0f, 5f, true));
            EscapeWeight = c.Bind(S4, "Opening: run weight", 0.5f,
                Range("Relative weight of running away.", 0f, 5f, true));
            HideWeight = c.Bind(S4, "Opening: slip into cover weight", 0.6f,
                Range("Ducking out of sight nearby instead of running far. Likelier for low nerve, cautious men and bad legs.", 0f, 5f, true));
            ScrambleTimeout = c.Bind(S4, "Opening: scramble time limit (s)", 8f,
                Range("A run for his gun gives up after this long.", 2f, 30f, true));
            ScrambleCooldown = c.Bind(S4, "Opening: no second scramble for (s)", 15f,
                Range("After a scramble is stopped, so he doesn't flip back and forth.", 0f, 120f, true));
            ReactionMin = c.Bind(S4, "Reaction time min (s)", 0.35f,
                Range("Each bot rolls his reaction time once per raid between min and max. What he sees reaches his decisions only after it (quicker with high nerve, slower when hurt).", 0.05f, 3f, true));
            ReactionMax = c.Bind(S4, "Reaction time max (s)", 0.9f,
                Range("Upper end of the reaction time roll.", 0.05f, 3f, true));
            IntimidationSeconds = c.Bind(S4, "Warning shot holds him (s)", 6f,
                Range("A shot from you that passes close (not a hit) keeps him still this long, scaled by nerve.", 0f, 30f, true));
            OrderCompliance = c.Bind(S4, "Order holds him (s)", 3f,
                Range("A fresh order holds him to it this long before he looks for an opening again.", 0f, 15f, true));
            ApproachStep = c.Bind(S4, "Coming closer counts after (m)", 2f,
                Range("You closing this much distance (within 8 m) is noticed and may get a reaction.", 0.5f, 10f, true));
            Quickdraw = c.Bind(S4, "Quickdraw sidearm", true,
                Text("A sidearm drawn when he resists uses the game's fast pistol draw. Primaries always use their normal draw.", true));
            DrawHandoffTimeout = c.Bind(S4, "Draw hand-off limit (s)", 3f,
                Range("While he draws, the mod keeps him (facing you, not firing) until the weapon is ready, at most this long, then hands him to his AI.", 0.5f, 10f, true));
            DrawRetrySeconds = c.Bind(S4, "Retry a refused draw (s)", 1.5f,
                Range("A draw the game refuses (hands still busy) is tried again for this long without a new decision.", 0f, 5f, true));
            WatchAngle = c.Bind(S4, "Covered within (deg)", 30f,
                Range("Your aim further off him than this counts as an opening. So do reloading, the inventory, no gun in hand and no line of sight.", 1f, 90f, true));
            LeaveDistance = c.Bind(S4, "Captor gone beyond (m)", 25f,
                Range("The captor counts as walking away when farther than this AND farther than his closest approach plus the margin below.", 5f, 100f, true));
            WalkAwayMargin = c.Bind(S4, "Captor walks away by (m)", 12f,
                Range("How much farther than his closest approach the captor must go before it counts as leaving.", 2f, 60f, true));
            LeaveSeconds = c.Bind(S4, "Captor out of sight for (s)", 6f,
                Range("He can't see his captor this long: he treats him as gone.", 0.5f, 30f, true));
            AbandonAssessMin = c.Bind(S4, "Left: reaction min (s)", 0.3f,
                Range("Once it is clear his captor has left him, this long before he acts (scaled by nerve and personality, at most 2.5 s).", 0.1f, 30f, true));
            AbandonAssessMax = c.Bind(S4, "Left: reaction max (s)", 1.2f,
                Range("Upper end of that reaction.", 0.1f, 60f, true));
            WitnessRadius = c.Bind(S4, "Sees a prisoner shot within (m)", 40f,
                Range("How close another held bot must be to react to a prisoner being shot.", 5f, 150f, true));
            WitnessEscape = c.Bind(S4, "Runs when he sees it chance", 1f,
                Range("Chance he runs when he sees it.", 0f, 1f, true));
            FleeDistance = c.Bind(S4, "Flee distance from captivity (m)", 40f,
                Range("A man fleeing or let go keeps going, leg by leg through cover, until he is this far from where he was held and from the threat he last saw, and out of its sight.", 10f, 120f, true));
            RetreatDistance = c.Bind(S4, "Retreat distance (m)", 35f,
                Range("How far a single retreat leg aims to take him.", 5f, 150f, true));
            RetreatTimeout = c.Bind(S4, "Retreat time limit (s)", 15f,
                Range("A retreat leg ends after this long wherever he is.", 3f, 60f, true));
            ArrivalTolerance = c.Bind(S4, "Arrived within (m)", 2f,
                Range("How close to a destination counts as there.", 0.5f, 5f, true));
            Grace = c.Bind(S4, "Grace after running (s)", 4f,
                Range("After he starts running, how long before he can be caught again.", 0f, 30f, true));
            ResurrenderDistance = c.Bind(S4, "Catch: within (m)", 12f,
                Range("Aiming at a running man this close makes him give up again.", 1f, 50f, true));
            AimTolerance = c.Bind(S4, "Catch: aim within (deg)", 10f,
                Range("How directly you must aim at him to catch him again.", 1f, 45f, true));
            AimConfirm = c.Bind(S4, "Catch: aim for (s)", 0.4f,
                Range("How long you must hold that aim.", 0f, 3f, true));
            RecaptureResistance = c.Bind(S4, "Catch: resist chance", 0.5f,
                Range("Times nerve: with a gun still on him, the chance he draws instead of giving up again.", 0f, 1f, true));
            FleeCommit = c.Bind(S4, "No going back for a gun (s)", 60f,
                Range("Once he has fled, a gun lying where he was held is left alone this long; guns elsewhere stay options.", 0f, 600f, true));
            EscapeMemory = c.Bind(S4, "Remembers the capture (s)", 150f,
                Range("After his release a man remembers where he was held and the threat he last saw for this long; unarmed, he doesn't drift back there.", 0f, 600f, true));
            KeepAwayRadius = c.Bind(S4, "Keep away radius (m)", 25f,
                Range("Within that memory, an unarmed ex-prisoner whose own AI walks him back this close to the capture spot is taken away again for one leg.", 5f, 80f, true));
            KeepAwayLegs = c.Bind(S4, "Keep away legs at most", 3,
                IntRange("How many times per release he is steered away again.", 0, 10, true));
            HeadingLookAhead = c.Bind(S4, "Allow for the captor walking (s)", 6f,
                Range("Escape routes are judged against where a walking captor will be in this many seconds, so he doesn't leave the same way. 0: only the captor's position.", 0f, 15f, true));
            QuickHandBackDistance = c.Bind(S4, "Hand back after (m)", 8f,
                Range("With quick hand-back on: handed to his own AI once this far from where he was held (or sooner once out of sight and 4 m away).", 2f, 40f, true));
            QuickHandBackSeconds = c.Bind(S4, "Hand back within (s)", 5f,
                Range("With quick hand-back on: his own AI has him after this long on his way, whatever the distance.", 1f, 30f, true));
            AvoidRadius = c.Bind(S4, "Own gun: keep away from you (m)", 20f,
                Range("He only goes back for his gun if you are at least this far from it.", 0f, 100f, true));
            ScavengeRadius = c.Bind(S4, "Other guns: search radius (m)", 60f,
                Range("How far he looks for another gun.", 5f, 200f, true));
            ScavengeAvoid = c.Bind(S4, "Other guns: keep away from you (m)", 20f,
                Range("He only goes for another gun if you are at least this far from it.", 0f, 100f, true));
            RecoveryTimeout = c.Bind(S4, "Give up walking after (s)", 30f,
                Range("A walk to a gun is abandoned after this long.", 5f, 120f, true));
            PickupRange = c.Bind(S4, "Pick-up range (m)", 1.5f,
                Range("How close he must be to pick a gun up.", 0.5f, 3f, true));
            PickupTimeout = c.Bind(S4, "Pick-up time limit (s)", 5f,
                Range("A pick-up that hasn't finished by then is given up.", 1f, 20f, true));
            FireBreakNearMiss = c.Bind(S4, "Fire: near miss within (m)", 1.5f,
                Range("How close a round must pass the prisoner or his captor to count as fire at them.", 0.3f, 4f, true));
            FireBreakQuiet = c.Bind(S4, "Fire: no new surrender for (s)", 8f,
                Range("After a hold-up is broken by fire, neither man can give up or take a prisoner again until this long after the last round near him.", 2f, 30f, true));
            FireRecoverRange = c.Bind(S4, "Fire: go for his gun within (m)", 6f,
                Range("Under fire, a prisoner only dashes for his own dropped gun this close to it; otherwise he goes for cover. 0: never.", 0f, 15f, true));
            FireCoverRadius = c.Bind(S4, "Fire: cover within (m)", 15f,
                Range("How far a prisoner looks for a spot hidden from the fire.", 4f, 40f, true));
            FireCoverSeconds = c.Bind(S4, "Fire: move to cover at most (s)", 8f,
                Range("A move to cover ends after this long wherever he is, and his own AI has him from there.", 2f, 30f, true));
            CoverRadius = c.Bind(S4, "Hiding: cover search radius (m)", 20f,
                Range("How far he looks for a hidden spot when he slips away.", 3f, 100f, true));
            PathCandidates = c.Bind(S4, "Movement: points tried", 12,
                IntRange("Candidate spots per retreat or cover search.", 2, 48, true));
            PathRetries = c.Bind(S4, "Movement: retries when stuck", 3,
                IntRange("New routes tried when he gets stuck.", 0, 10, true));
            StallSeconds = c.Bind(S4, "Movement: stuck after (s)", 3f,
                Range("Without making progress for this long he counts as stuck.", 0.5f, 15f, true));
            ProgressMeters = c.Bind(S4, "Movement: progress (m)", 0.5f,
                Range("Movement that counts as progress.", 0.1f, 3f, true));
        }
    }
}
