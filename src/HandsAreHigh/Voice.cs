using System;
using System.Linq;
using System.Reflection;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // The game's own bot lines, through BotTalk, with a cooldown per bot. Which lines a
    // voice actually has varies; BotTalk skips the ones it doesn't.
    internal static class Voice
    {
        internal static void Say(SurrenderState s, EPhraseTrigger line)
        {
            if (!Settings.BotVoice.Value || s?.Bot == null) return;
            if (Time.time < s.NextVoice) return;
            s.NextVoice = Time.time + Settings.VoiceCooldown.Value;
            HandsAreHighPlugin.Try("voice", () => s.Bot.AIData?.BotOwner?.BotTalk?.Say(line, true));
        }
    }

    // SAIN, found by reflection so it stays optional (verified against the installed SAIN.dll):
    //   SAIN.Interop.SAINExternal.GetPersonality(BotOwner) -> string of EPersonality
    //   SAIN.Interop.SAINExternal.GetBotComponent(BotOwner) (private static) -> BotComponent
    //   BotComponent.Suppression (SAINBotSuppressClass).CurrentState -> ESuppressionState
    //     None, Light, Medium, Heavy, Extreme (built from SAIN's own near-miss tracking, decays)
    internal static class SainAdapter
    {
        private static bool _resolved;
        private static MethodInfo _getPersonality;
        private static MethodInfo _getBotComponent;
        private static PropertyInfo _suppression;
        private static PropertyInfo _suppressionState;

        internal static void Reset()
        {
            _resolved = false;
            _getPersonality = null;
            _getBotComponent = null;
            _suppression = null;
            _suppressionState = null;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Assembly sain = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SAIN");
                Type external = sain?.GetType("SAIN.Interop.SAINExternal");
                if (external == null)
                {
                    HandsAreHighPlugin.Log.LogInfo("SAIN not installed: neutral personality, the game's own incoming-fire signal only");
                    return;
                }
                Type[] arg = { typeof(BotOwner) };
                _getPersonality = external.GetMethod("GetPersonality", BindingFlags.Public | BindingFlags.Static, null, arg, null);
                _getBotComponent = external.GetMethod("GetBotComponent", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, arg, null);
                _suppression = _getBotComponent?.ReturnType.GetProperty("Suppression", BindingFlags.Public | BindingFlags.Instance);
                _suppressionState = _suppression?.PropertyType.GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Instance);
                HandsAreHighPlugin.Log.LogInfo(
                    $"SAIN found: personality {(_getPersonality != null ? "yes" : "no")}, suppression {(_suppressionState != null ? "yes" : "no")}");
            }
            catch (Exception e)
            {
                HandsAreHighPlugin.Log.LogWarning($"SAIN lookup failed, using neutral fallbacks: {e.Message}");
            }
        }

        internal static string Personality(BotOwner owner)
        {
            if (!Settings.SainPersonality.Value || owner == null) return null;
            Resolve();
            if (_getPersonality == null) return null;
            try { return _getPersonality.Invoke(null, new object[] { owner }) as string; }
            catch { return null; }
        }

        // 0 (None) .. 1 (Extreme); 0 without SAIN.
        internal static float SuppressionLevel(BotOwner owner)
        {
            if (!Settings.SainSuppression.Value || owner == null) return 0f;
            Resolve();
            if (_suppressionState == null) return 0f;
            try
            {
                object component = _getBotComponent.Invoke(null, new object[] { owner });
                object suppression = component != null ? _suppression.GetValue(component, null) : null;
                if (suppression == null) return 0f;
                int state = Convert.ToInt32(_suppressionState.GetValue(suppression, null));
                return Mathf.Clamp01(state / 4f);
            }
            catch { return 0f; }
        }

        // How a personality leans when he gets an opening: fight (draw, knife, scramble) or run.
        internal static void Leanings(string personality, out float fight, out float flee)
        {
            switch (personality)
            {
                case "Wreckless": fight = 1.5f; flee = 0.5f; return;
                case "GigaChad": fight = 1.4f; flee = 0.6f; return;
                case "SnappingTurtle": fight = 1.2f; flee = 0.7f; return;
                case "Chad": fight = 1.2f; flee = 0.8f; return;
                case "Rat": fight = 0.8f; flee = 1.3f; return;
                case "Timmy": fight = 0.7f; flee = 1.4f; return;
                case "Coward": fight = 0.6f; flee = 1.6f; return;
                default: fight = 1f; flee = 1f; return;
            }
        }

        // Bolder personalities resist giving up; cautious ones give up sooner.
        internal static float Multiplier(string personality)
        {
            float lo = Settings.PersonalityMin.Value, hi = Settings.PersonalityMax.Value;
            switch (personality)
            {
                case "Wreckless":
                case "GigaChad": return lo;
                case "SnappingTurtle": return Mathf.Lerp(lo, 1f, 0.25f);
                case "Chad": return Mathf.Lerp(lo, 1f, 0.5f);
                case "Rat": return Mathf.Lerp(1f, hi, 0.5f);
                case "Timmy": return Mathf.Lerp(1f, hi, 0.75f);
                case "Coward": return hi;
                default: return 1f;
            }
        }
    }
}
