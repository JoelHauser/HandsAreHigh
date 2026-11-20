using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // ConfigurationManager (and F12 replacements that follow it) read this by name: entries
    // tagged Browsable = false are kept out of the menu.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
    }

    // 0.19.0 settings: distant dropped weapons, hits right after giving up, and the one-time
    // migration of defaults that changed in 0.19.0.
    internal static partial class Settings
    {
        internal static ConfigEntry<float> LootNearDistance;
        internal static ConfigEntry<float> LootMaxDistance;
        internal static ConfigEntry<float> LootDistanceWeight;
        internal static ConfigEntry<float> LootReweighSeconds;
        internal static ConfigEntry<int> LootMaxPostpone;
        internal static ConfigEntry<float> LootSightRange;
        internal static ConfigEntry<float> LootSearchSeconds;
        internal static ConfigEntry<float> HitGrace;
        private static ConfigEntry<int> ConfigVersion;

        private const int CurrentConfigVersion = 19;

        private static void BindV19(ConfigFile c)
        {
            LootNearDistance = c.Bind(LootSec, "Close by within (m)", 4f,
                Range("Walking distance (the real path) within which a dropped item counts as close: weighed as before by how risky the few steps are (so is anything lying at his prisoner's feet). Farther items, away from the man, get a deliberate decision (below).", 1f, 15f));
            LootMaxDistance = c.Bind(LootSec, "Never walks farther than (m)", 30f,
                Range("A dropped or thrown item farther than this (walking distance) is left where it is.", 5f, 80f));
            LootDistanceWeight = c.Bind(LootSec, "Distance weighs", 1f,
                Range("How much the walk itself counts against fetching a distant item (with the time his prisoner is left unwatched, unless a squadmate covers him). 0: distance costs nothing.", 0f, 3f));
            LootReweighSeconds = c.Bind(LootSec, "Weighs a postponed item again after (s)", 4f,
                Range("A distant item he decided to leave for now (another enemy about, his prisoner would be unwatched) is weighed again this often, or as soon as the situation changes.", 1f, 30f));
            LootMaxPostpone = c.Bind(LootSec, "Postpones at most", 3,
                IntRange("After this many postponements he stops weighing it while he guards the man: it waits for after the release (if that is on) or stays where it is.", 0, 10));
            LootSightRange = c.Bind(LootSec, "Sees where things land within (m)", 40f,
                Range("He only knows where a dropped or thrown item lies if he saw it there (in front of him, with a clear line) or saw it fall at his prisoner's feet. Otherwise he looks for it for a while.", 5f, 100f));
            LootSearchSeconds = c.Bind(LootSec, "Searches for an item he never saw land for (s)", 12f,
                Range("Then he forgets about it.", 2f, 60f));
            HitGrace = c.Bind(Coverage, "Hits ignored right after giving up (s)", 2f,
                Range("A man hit this soon after dropping his gun was shot by someone who hadn't seen the hands go up yet (a firefight doesn't stop in an instant): he stays down instead of bolting. Later hits still make him run.", 0f, 6f));
            ConfigVersion = c.Bind(Debug, "Config version (set by the mod)", 0,
                new ConfigDescription("Which default changes have been applied to this file. Not a setting.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            Migrate();
        }

        // Defaults changed in 0.19.0 (natural surrender was effectively unreachable in ordinary
        // fights; see NaturalSurrender and tools/natsim). A value still at its OLD default moves to
        // the new one, once; anything the user set themselves is left alone.
        private static void Migrate()
        {
            if (ConfigVersion.Value >= CurrentConfigVersion) return;
            var moved = new List<string>();
            Move(EvalInterval, 5f, moved);
            Move(PromptCheckDelay, 0.5f, moved);
            Move(InjuryThreshold, 0.35f, moved);
            Move(VitalThreshold, 0.4f, moved);
            Move(PmcThreshold, 0.55f, moved);
            ConfigVersion.Value = CurrentConfigVersion;
            if (moved.Count > 0)
                HandsAreHighPlugin.Log.LogInfo($"Config updated to the 0.19.0 defaults (values you had changed were kept): {string.Join("; ", moved)}");
        }

        private static void Move(ConfigEntry<float> e, float oldDefault, List<string> moved)
        {
            float now = (float)e.DefaultValue;
            if (Mathf.Abs(e.Value - oldDefault) > 1e-4f || Mathf.Abs(now - oldDefault) < 1e-4f) return;
            e.Value = now;
            moved.Add($"{e.Definition.Key} {oldDefault:0.##} -> {now:0.##}");
        }
    }
}
