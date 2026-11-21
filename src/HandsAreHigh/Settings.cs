using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace HandsAreHigh
{
    // ConfigurationManager (and F12 replacements built on it, such as BetterBepInEx) read this by
    // name. Browsable = false: kept out of the menu. Order: higher first within a section (Bind
    // assigns it in bind order). IsAdvanced: hidden until the menu's "Show advanced" switch is on
    // (stock ConfigurationManager: the Show advanced tick box; BetterBepInEx: the title-bar switch,
    // and a search always lists them). Neither menu can collapse a section, so the advanced entries
    // are also bound after the common ones of their section.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public int? Order;
        public bool? IsAdvanced;
    }

    // Every tuning value, in seven numbered F12 sections (0.28.0; before: 27 sections, see
    // Settings.Layout.cs). One file per section, bound in display order:
    //   1 General               Settings.1General.cs
    //   2 Surrender             Settings.2Surrender.cs
    //   3 Captors and squads    Settings.3Captors.cs
    //   4 Prisoner escape ...   Settings.4Escape.cs
    //   5 Commands and dialogue Settings.5Commands.cs
    //   6 Animations            Settings.6Animations.cs
    //   7 Debugging             Settings.7Debugging.cs
    // All of it applies live except the layer priorities, which BigBrain only reads at startup.
    // Section and key names must never contain = \ " ' [ ] (BepInEx throws; pack.ps1 checks).
    internal static partial class Settings
    {
        private const string S1 = "1. General";
        private const string S2 = "2. Surrender";
        private const string S3 = "3. Captors and squads";
        private const string S4 = "4. Prisoner escape and recovery";
        private const string S5 = "5. Commands and dialogue";
        private const string S6 = "6. Animations";
        private const string S7 = "7. Debugging";

        // Display order: each Bind takes the next lower Order, so a section lists in bind order.
        private static int _order;

        private static ConfigurationManagerAttributes Attr(bool advanced) =>
            new ConfigurationManagerAttributes { Order = --_order, IsAdvanced = advanced ? true : (bool?)null };

        private static ConfigDescription Text(string text, bool advanced) =>
            new ConfigDescription(text, null, Attr(advanced));

        private static ConfigDescription Range(string text, float min, float max, bool advanced) =>
            new ConfigDescription(text, new AcceptableValueRange<float>(min, max), Attr(advanced));

        private static ConfigDescription IntRange(string text, int min, int max, bool advanced) =>
            new ConfigDescription(text, new AcceptableValueRange<int>(min, max), Attr(advanced));

        internal static void Bind(ConfigFile c)
        {
            // One save at the end instead of one per Bind (430+ writes of the whole file).
            bool save = c.SaveOnConfigSet;
            c.SaveOnConfigSet = false;
            try
            {
                MigrateLayout(c);    // first: old (section, key) lines renamed to the new ones (Settings.Layout.cs)
                _order = 0;
                BindGeneral(c);
                BindSurrender(c);
                BindCaptors(c);
                BindEscape(c);
                BindCommands(c);
                BindAnimations(c);
                BindDebugging(c);
                Migrate();           // last: default changes of earlier versions
            }
            finally
            {
                c.SaveOnConfigSet = save;
                c.Save();
            }
        }

        // ---- default changes of earlier versions ------------------------------------------
        //
        // A value still at its OLD default moves to the new one, once; anything the user set
        // themselves is left alone. Per version, so a file already at 19 still gets the later steps.
        private const int CurrentConfigVersion = 25;

        internal static void Migrate()
        {
            int from = ConfigVersion.Value;
            if (from >= CurrentConfigVersion) return;
            var moved = new List<string>();
            if (from < 19)
            {
                // 0.19.0: natural surrender was effectively unreachable in ordinary fights
                // (NaturalSurrender, tools/natsim).
                Move(EvalInterval, 5f, moved);
                Move(PromptCheckDelay, 0.5f, moved);
                Move(InjuryThreshold, 0.35f, moved);
                Move(VitalThreshold, 0.4f, moved);
                Move(PmcThreshold, 0.55f, moved);
            }
            if (from < 22)
            {
                // 0.22.0: the hands-up pose (ArmPose.ArmFrame, tools/armsim/armsim3.py): hands above the head.
                Move(UpperArmRaise, 12f, moved);
                Move(UpperArmForward, 25f, moved);
                Move(ForearmForward, 12f, moved);
                Move(ForearmInward, 8f, moved);
            }
            if (from < 25)
            {
                // 0.25.0: abandonment noticed from the cues, then a reaction time (0.24.0: 2.5-9 s).
                Move(AbandonAssessMin, 2.5f, moved);
                Move(AbandonAssessMax, 9f, moved);
            }
            ConfigVersion.Value = CurrentConfigVersion;
            if (moved.Count > 0)
                HandsAreHighPlugin.Log.LogInfo($"Config updated to the new defaults (values you had changed were kept): {string.Join("; ", moved)}");
        }

        private static void Move(ConfigEntry<float> e, float oldDefault, List<string> moved)
        {
            float now = (float)e.DefaultValue;
            if (Mathf.Abs(e.Value - oldDefault) > 1e-4f || Mathf.Abs(now - oldDefault) < 1e-4f) return;
            e.Value = now;
            moved.Add($"{e.Definition.Key} {oldDefault:0.##} -> {now:0.##}");
        }

        // ---- helpers ------------------------------------------------------------------------

        internal static float RollNerve()
        {
            float lo = Mathf.Min(NerveMin.Value, NerveMax.Value);
            float hi = Mathf.Max(NerveMin.Value, NerveMax.Value);
            return Dice.Range(lo, hi);
        }

        // The override applies live, to bots already met this raid too.
        internal static float EffectiveNerve(float rolled) => NerveOverride.Value ? NerveOverrideValue.Value : rolled;

        internal static float Between(ConfigEntry<float> a, ConfigEntry<float> b) =>
            Dice.Range(Mathf.Min(a.Value, b.Value), Mathf.Max(a.Value, b.Value));
    }

    internal enum ComplianceChoice { Auto, DropsAll, KeepsOne, Refuses }
}
