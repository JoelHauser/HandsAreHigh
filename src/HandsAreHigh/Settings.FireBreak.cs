using BepInEx.Configuration;

namespace HandsAreHigh
{
    // 0.21.0: incoming fire breaks a hold-up (FireBreak.cs), what a man does about it, and the
    // hand-back to his own AI after any release (SurrenderLayer, ReleaseWatch). Same rules as
    // Settings.cs: no = \ " ' [ ] in section or key names (scripts\pack.ps1 checks).
    internal static partial class Settings
    {
        private const string FireSec = "24. Incoming fire and release";

        internal static ConfigEntry<bool> FireBreaks;
        internal static ConfigEntry<float> FireBreakNearMiss;
        internal static ConfigEntry<float> FireBreakQuiet;
        internal static ConfigEntry<float> FireRecoverRange;
        internal static ConfigEntry<float> FireCoverRadius;
        internal static ConfigEntry<float> FireCoverSeconds;
        internal static ConfigEntry<float> ReleaseWatchSeconds;
        internal static ConfigEntry<bool> LogFireBreak;

        private static void BindFireBreak(ConfigFile c)
        {
            FireBreaks = c.Bind(FireSec, "Incoming fire breaks a hold-up", true,
                "A hit or a near miss on a prisoner or his captor, from anyone outside the hold-up (not the captor himself, not his side), ends it at once: the captor goes to his combat AI, the prisoner gets to safety (cover away from the fire, a weapon he still carries, or his own dropped gun if it is close and not out towards the shooter), then his own AI has him. A human captor keeps full control. Gunfire elsewhere never counts.");
            FireBreakNearMiss = c.Bind(FireSec, "Near miss counts within (m)", 1.5f,
                Range("How close a round must pass to the chest of the prisoner or his captor (with no wall in between) to count as fire at them.", 0.3f, 4f));
            FireBreakQuiet = c.Bind(FireSec, "No new surrender while under fire for (s)", 8f,
                Range("After a hold-up is broken by fire, neither man can give up or take a prisoner again until this long after the last round that came near him (no recapture loops in the same firefight).", 2f, 30f));
            FireRecoverRange = c.Bind(FireSec, "Goes for his gun under fire within (m)", 6f,
                Range("Under fire, a prisoner only weighs a dash for his own dropped gun this close to it (reachable, not taken, and not out towards the shooter); otherwise he goes for cover. 0: never.", 0f, 15f));
            FireCoverRadius = c.Bind(FireSec, "Cover from fire within (m)", 15f,
                Range("How far a prisoner looks for a spot hidden from the fire (a complete path only).", 4f, 40f));
            FireCoverSeconds = c.Bind(FireSec, "Move to cover at most (s)", 8f,
                Range("A move to cover (from fire, or slipping away) ends after this long wherever he is, and his own AI has him from there. Stuck or no reachable cover ends it at once.", 2f, 30f));
            ReleaseWatchSeconds = c.Bind(FireSec, "Log a released bot for (s)", 60f,
                Range("After a bot is handed back to his own AI, three log lines (+15 s, +30 s, then this) say where he is, how far he moved, which AI layer drives him and his target; STILL NEAR THE CAPTURE SPOT flags one that never left. 0: off.", 0f, 300f));
            LogFireBreak = c.Bind(FireSec, "Log incoming fire", true,
                "Log what broke a hold-up (who was hit or nearly hit, by whom, from where), each side's reaction, the prisoner's choice under fire with every option weighed, and refused recaptures. No per-frame lines.");
        }
    }
}
