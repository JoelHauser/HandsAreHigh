using BepInEx.Configuration;

namespace HandsAreHigh
{
    // 0.20.0: a squadmate covering the captor (Overwatch.cs). Same rules as Settings.cs: no
    // = \ " ' [ ] in section or key names (scripts\pack.ps1 checks).
    internal static partial class Settings
    {
        private const string SquadCoverSec = "23. Squad cover";

        internal static ConfigEntry<bool> SquadCover;
        internal static ConfigEntry<float> GuardDistance;
        internal static ConfigEntry<float> CoverWait;
        internal static ConfigEntry<float> GuardReaction;
        internal static ConfigEntry<bool> GuardScans;
        internal static ConfigEntry<float> GuardGiveUp;
        internal static ConfigEntry<bool> LogSquadCover;

        private static void BindSquadCover(ConfigFile c)
        {
            SquadCover = c.Bind(SquadCoverSec, "A squadmate covers the captor", true,
                "When a bot in a squad takes a prisoner, one free squadmate (not fighting, holding fire at the prisoner, armed, close) takes a spot to the side and covers the prisoner and the captor. The captor stays the captor. Never a player ally (your group, Pit Fireteam teammates).");
            GuardDistance = c.Bind(SquadCoverSec, "Guard distance from the prisoner (m)", 6f,
                Range("Where the covering squadmate prefers to stand: about this far from the prisoner, off to the side of the captor (never in line with him).", 3f, 12f));
            CoverWait = c.Bind(SquadCoverSec, "Captor waits for cover (s)", 6f,
                Range("Before something that takes his eyes and gun off the prisoner (fetching dropped gear, closing in) a captor whose squadmate isn't covering yet waits this long, then goes ahead and accepts the risk. 0: never waits.", 0f, 20f));
            GuardReaction = c.Bind(SquadCoverSec, "Guard reaction time (s)", 0.3f,
                Range("How fast a covering squadmate reacts to a prisoner he sees go for a weapon (x0.75-1.35 per reaction; longer from the corner of his eye, while walking, or while fighting someone else).", 0.1f, 1.5f));
            GuardScans = c.Bind(SquadCoverSec, "Guard looks around", true,
                "The covering squadmate looks out behind the captor now and then (never while the captor is busy or the prisoner moves). Those moments are real openings for the prisoner.");
            GuardGiveUp = c.Bind(SquadCoverSec, "Guard leaves the role after fighting (s)", 12f,
                Range("A covering squadmate drawn into a fight (another enemy in sight, under fire) goes back to covering once it is quiet; fighting longer than this, he leaves the role and another squadmate may take it.", 3f, 60f));
            LogSquadCover = c.Bind(SquadCoverSec, "Log squad cover", true,
                "Log who covers whom, getting in position, coverage gained and lost (with the reason), what the guard sees the prisoner do and how he reacts, hand-overs, and a summary when the role ends. No per-frame lines.");
        }
    }
}
