using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // Everything here is tunable live in F12 except the layer priority, which BigBrain
    // only reads once at startup.
    internal static class Settings
    {
        private const string Proto = "1. Prototype";
        private const string Pose = "2. Arm pose";
        private const string Feel = "3. Feel";
        private const string Captive = "4. While held";
        private const string After = "5. After you leave";

        internal static ConfigEntry<KeyboardShortcut> SurrenderKey;
        internal static ConfigEntry<KeyboardShortcut> OpeningKey;
        internal static ConfigEntry<KeyboardShortcut> LeaveKey;
        internal static ConfigEntry<float> MaxDistance;
        internal static ConfigEntry<bool> IncludePmcs;
        internal static ConfigEntry<int> LayerPriority;

        internal static ConfigEntry<float> UpperArmRaise;
        internal static ConfigEntry<float> UpperArmForward;
        internal static ConfigEntry<float> ForearmForward;
        internal static ConfigEntry<float> ForearmInward;
        internal static ConfigEntry<float> ForearmTwist;

        internal static ConfigEntry<float> RaiseSeconds;
        internal static ConfigEntry<float> LowerSeconds;
        internal static ConfigEntry<float> PoseStrength;
        internal static ConfigEntry<float> TrembleDegrees;
        internal static ConfigEntry<float> TrembleSpeed;
        internal static ConfigEntry<float> Stance;
        internal static ConfigEntry<bool> FaceCaptor;

        internal static ConfigEntry<float> WatchAngle;
        internal static ConfigEntry<float> OpeningDelay;
        internal static ConfigEntry<float> BreakChance;
        internal static ConfigEntry<float> KnifeRange;
        internal static ConfigEntry<float> SidearmRange;
        internal static ConfigEntry<bool> BoltWhenShot;
        internal static ConfigEntry<float> LeaveDistance;
        internal static ConfigEntry<float> LeaveSeconds;

        internal static ConfigEntry<float> RetreatDistance;
        internal static ConfigEntry<float> RegroupMin;
        internal static ConfigEntry<float> RegroupMax;
        internal static ConfigEntry<bool> RecoverOwnWeapon;
        internal static ConfigEntry<bool> ScavengeWeapons;
        internal static ConfigEntry<float> ScavengeRadius;
        internal static ConfigEntry<float> AvoidRadius;
        internal static ConfigEntry<float> ResurrenderDistance;

        private static ConfigDescription Range(string text, float min, float max) =>
            new ConfigDescription(text, new AcceptableValueRange<float>(min, max));

        internal static void Bind(ConfigFile c)
        {
            SurrenderKey = c.Bind(Proto, "Surrender bot", new KeyboardShortcut(KeyCode.Keypad7),
                "Forces the bot nearest your crosshair (or nearest in range) to surrender.");
            OpeningKey = c.Bind(Proto, "Give them an opening", new KeyboardShortcut(KeyCode.Keypad8),
                "Every held bot acts as if you looked away, with full nerve: tests the knife / sidearm draw.");
            LeaveKey = c.Bind(Proto, "Pretend you left", new KeyboardShortcut(KeyCode.Keypad9),
                "Every held bot acts as if you walked off: tests retreat and rearming.");
            MaxDistance = c.Bind(Proto, "Max distance (m)", 40f, Range("How far the surrender key looks.", 2f, 150f));
            IncludePmcs = c.Bind(Proto, "Include PMCs", true, "Off: the key only picks Scavs.");
            LayerPriority = c.Bind(Proto, "Layer priority (restart)", 95,
                new ConfigDescription("BigBrain priority. SAIN uses 20-24, 80 and 99 (debug).",
                    new AcceptableValueRange<int>(1, 200)));

            UpperArmRaise = c.Bind(Pose, "Upper arm raise (deg)", 12f,
                Range("Elbow height: 0 = level with the shoulder, positive = above.", -40f, 80f));
            UpperArmForward = c.Bind(Pose, "Upper arm forward (deg)", 25f,
                Range("0 = straight out to the side, 90 = straight ahead.", -20f, 90f));
            ForearmForward = c.Bind(Pose, "Forearm forward (deg)", 12f,
                Range("0 = forearm straight up, positive tips the hands forward.", -30f, 80f));
            ForearmInward = c.Bind(Pose, "Forearm inward (deg)", 8f,
                Range("Positive tips the hands in towards the head.", -30f, 45f));
            ForearmTwist = c.Bind(Pose, "Forearm twist (deg)", 0f,
                Range("Turns the forearms about their length, to swing the palms forward.", -180f, 180f));

            RaiseSeconds = c.Bind(Feel, "Raise time (s)", 0.5f, Range("", 0.05f, 3f));
            LowerSeconds = c.Bind(Feel, "Lower time (s)", 0.3f, Range("When he gives up waiting. Going for a weapon is always fast.", 0.05f, 3f));
            PoseStrength = c.Bind(Feel, "Pose strength", 0.92f,
                Range("Below 1 lets breathing and sway show through the arms.", 0.1f, 1f));
            TrembleDegrees = c.Bind(Feel, "Tremble (deg)", 1.5f, Range("0 turns it off.", 0f, 8f));
            TrembleSpeed = c.Bind(Feel, "Tremble speed", 6f, Range("", 0.5f, 20f));
            Stance = c.Bind(Feel, "Stance with hands up", 1f, Range("1 = standing, 0 = full crouch.", 0f, 1f));
            FaceCaptor = c.Bind(Feel, "Face the player", true, "Turns to face whoever holds him.");

            WatchAngle = c.Bind(Captive, "You're covering him within (deg)", 30f,
                Range("Looking further away than this from him counts as an opening. So does healing, reloading, the inventory or no gun in hand.", 5f, 90f));
            OpeningDelay = c.Bind(Captive, "Opening must last (s)", 1.5f, Range("", 0f, 10f));
            BreakChance = c.Bind(Captive, "Break chance per second", 0.35f,
                Range("While there's an opening, times the bot's nerve (random 0-1 per bot).", 0f, 1f));
            KnifeRange = c.Bind(Captive, "Knife range (m)", 4f, Range("Closer than this he goes for his knife.", 0f, 15f));
            SidearmRange = c.Bind(Captive, "Draw range (m)", 30f,
                Range("Closer than this (and beyond knife range) he draws a gun he still carries. Further away he keeps waiting.", 0f, 100f));
            BoltWhenShot = c.Bind(Captive, "Bolt when shot", true, "Hit while his hands are up: he runs.");
            LeaveDistance = c.Bind(Captive, "You've left beyond (m)", 25f, Range("", 5f, 100f));
            LeaveSeconds = c.Bind(Captive, "Left for (s)", 6f,
                Range("How long you must be beyond that distance or out of his sight.", 0f, 60f));

            RetreatDistance = c.Bind(After, "Retreat distance (m)", 35f, Range("", 10f, 120f));
            RegroupMin = c.Bind(After, "Regroup min (s)", 4f, Range("Time in cover before he decides what to do.", 0f, 60f));
            RegroupMax = c.Bind(After, "Regroup max (s)", 12f, Range("", 0f, 120f));
            RecoverOwnWeapon = c.Bind(After, "Go back for his own gun", true, "If it's still lying there and you're not near it.");
            ScavengeWeapons = c.Bind(After, "Look for another gun", true, "Loose guns and guns on bodies.");
            ScavengeRadius = c.Bind(After, "Search radius (m)", 60f, Range("", 5f, 200f));
            AvoidRadius = c.Bind(After, "Keep away from you (m)", 20f,
                Range("Ignores guns this close to where he last saw you.", 0f, 100f));
            ResurrenderDistance = c.Bind(After, "Caught again within (m)", 12f,
                Range("Unarmed and you cover him this close: hands go up again.", 2f, 50f));
        }
    }
}
