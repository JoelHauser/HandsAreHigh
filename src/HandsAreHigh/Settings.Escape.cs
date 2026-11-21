using BepInEx.Configuration;

namespace HandsAreHigh
{
    // 0.24.0: open hands in the surrender poses (Fingers.cs), the captor's choice about a dropped
    // knife (CaptorLoot), what counts as being armed (Arsenal.Usable), and getting clear after a
    // release (Escape.cs, Perception.FleePoint, Restraint after the hand-back). Same rules as
    // Settings.cs: no = \ " ' [ ] in section or key names (scripts\pack.ps1 checks).
    internal static partial class Settings
    {
        private const string EscSec = "26. Release and escape";

        internal static ConfigEntry<float> OpenHands;
        internal static ConfigEntry<float> MeleePickupChance;
        internal static ConfigEntry<float> SafeDistance;
        internal static ConfigEntry<float> EscapeMemory;
        internal static ConfigEntry<float> KeepAwayRadius;
        internal static ConfigEntry<int> KeepAwayLegs;
        internal static ConfigEntry<float> HeadingLookAhead;
        internal static ConfigEntry<bool> FixStaleWeapon;
        internal static ConfigEntry<bool> DefendWhenFleeing;
        internal static ConfigEntry<bool> LogEscape;

        private static void BindEscape(ConfigFile c)
        {
            OpenHands = c.Bind(EscSec, "Open hands", 1f,
                Range("How far the fingers are opened while the hands are up (relaxed, slightly curved, spread like the game's own hand), and how they rest curved round the head with hands on head. 0: the game's own finger animation (the gripping curl).", 0f, 1f));
            MeleePickupChance = c.Bind(EscSec, "Captors pick up a dropped knife", 0.3f,
                Range("Base chance a captor bothers with the knife or other melee weapon his prisoner dropped. Raised when he has none of his own and by its value; then weighed against the risk of bending down by the man (the same risk as any other item, with a lower tolerance). Leaving it is final: no second walk over for it, nothing kept waiting for it.", 0f, 1f));
            SafeDistance = c.Bind(EscSec, "Escape ends this far from the threat (m)", 40f,
                Range("A fleeing or released man keeps going, leg by leg through cover, until he is at least this far from where he was held and from the threat he last saw, and out of its sight (or out of legs). Then he decides what next: a usable gun, or his own AI.", 15f, 120f));
            EscapeMemory = c.Bind(EscSec, "Remembers the capture for (s)", 150f,
                Range("After his release a man remembers where he was held and where he last saw the threat for this long. Unarmed, he doesn't drift back there (see Keep away radius); his own gun lying there is off limits for the same time unless it is safe to fetch.", 0f, 600f));
            KeepAwayRadius = c.Bind(EscSec, "Keep away radius (m)", 25f,
                Range("Within the memory time, an unarmed ex-prisoner whose own AI walks him back within this distance of the capture spot or the threat he last saw is taken away again for one leg (logged with the layer and target that drove him back). With a usable gun in his hands his AI decides freely.", 5f, 80f));
            KeepAwayLegs = c.Bind(EscSec, "Keep away legs at most", 3,
                IntRange("How many times per release he is steered away again; after that his own AI has him whatever it does.", 0, 10));
            HeadingLookAhead = c.Bind(EscSec, "Allow for where the captor is walking (s)", 6f,
                Range("When he can see his captor walking, an escape route and spot are judged against where the captor will be in this many seconds too, so he doesn't leave the same way, walk beside him or overtake him. 0: only the captor's position.", 0f, 15f));
            FixStaleWeapon = c.Bind(EscSec, "Disarmed bots know they are unarmed", true,
                "The game keeps a bot's last gun as his current weapon after it leaves his hands (BotWeaponManager.ShootController is only cleared when the bot is destroyed), so SAIN treated a disarmed man as armed with a loaded gun: it pushed him at his ex-captor, sent him to search and chose grenade throws instead of retreating. On: for bots this mod disarmed, with no gun in their hands, 'has bullets' reads false (SAIN then retreats and takes cover) and no reload is started on the gun that is no longer in his hands. Their own gun back in hand: back to the game's value.");
            DefendWhenFleeing = c.Bind(EscSec, "Fleeing men may fight back", true,
                "A man getting away (retreat, cover, regroup) who comes under fire and carries a usable gun weighs drawing it against getting to cover (hit, a close shooter, no cover near: more likely to draw); his own AI fights from there. Off: cover only (0.23 behaviour).");
            LogEscape = c.Bind(EscSec, "Log escapes", true,
                "Log escape destinations (with the candidates rejected and why), equipment classification at each decision, weapon recoveries refused, the hand-back to his own AI, keep-away legs with what drove him back, and every reason hold-fire about an ex-prisoner ended (renewed hostility). No per-frame lines.");
        }
    }
}
