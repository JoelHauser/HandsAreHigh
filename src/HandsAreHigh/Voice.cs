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

    // SAIN's personality, through its public interop (SAIN.Interop.SAINExternal.GetPersonality),
    // found by reflection so SAIN stays optional. Names from SAIN's EPersonality enum.
    internal static class SainAdapter
    {
        private static bool _resolved;
        private static MethodInfo _getPersonality;

        internal static void Reset()
        {
            _resolved = false;
            _getPersonality = null;
        }

        internal static string Personality(BotOwner owner)
        {
            if (!Settings.SainPersonality.Value || owner == null) return null;
            if (!_resolved)
            {
                _resolved = true;
                try
                {
                    Assembly sain = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SAIN");
                    _getPersonality = sain?.GetType("SAIN.Interop.SAINExternal")?
                        .GetMethod("GetPersonality", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BotOwner) }, null);
                    HandsAreHighPlugin.Log.LogInfo(_getPersonality != null
                        ? "SAIN found: personalities shape surrender odds"
                        : "SAIN personality not available: every bot counts as Normal");
                }
                catch (Exception e)
                {
                    HandsAreHighPlugin.Log.LogWarning($"SAIN personality lookup failed, every bot counts as Normal: {e.Message}");
                }
            }
            if (_getPersonality == null) return null;
            try { return _getPersonality.Invoke(null, new object[] { owner }) as string; }
            catch { return null; }
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
