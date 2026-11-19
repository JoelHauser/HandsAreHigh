using System;
using System.Linq;
using System.Reflection;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // The game's own voice lines, played straight through the bot's speaker
    // (Player.Speaker.Play), the call SAIN itself uses (PlayerComponent.PlayVoiceLine).
    // BotTalk.Say and Player.Say don't work for SAIN bots: SAIN's BotTalkPatch and
    // PlayerTalkPatch swallow every phrase but hurt/agony/death/breath, which is why
    // surrenders were silent. Speaker.Play returns the TagBank it played, or null when this
    // voice has no bank for the phrase, so each line falls back to the next one and the log
    // says what really played. A busy speaker (mid-sentence) is retried for a moment.
    internal enum VoiceEvent { Surrender, Acknowledge, Refuse, Break, Hurt }

    internal static class Voice
    {
        private const float RetryWindow = 2f;

        private static EPhraseTrigger[] Lines(VoiceEvent e)
        {
            switch (e)
            {
                case VoiceEvent.Surrender: return new[] { EPhraseTrigger.HoldFire, EPhraseTrigger.Stop, EPhraseTrigger.Negative, EPhraseTrigger.OnBeingHurt };
                case VoiceEvent.Acknowledge: return new[] { EPhraseTrigger.Roger, EPhraseTrigger.Going };
                case VoiceEvent.Refuse: return new[] { EPhraseTrigger.Negative, EPhraseTrigger.DontKnow };
                case VoiceEvent.Break: return new[] { EPhraseTrigger.OnFight, EPhraseTrigger.Fire, EPhraseTrigger.Gogogo };
                default: return new[] { EPhraseTrigger.OnBeingHurt };
            }
        }

        // Queues one event (the newest wins); played from Tick when the speaker is free.
        internal static void Say(SurrenderState s, VoiceEvent e)
        {
            if (!Settings.BotVoice.Value || s?.Bot == null) return;
            if (e != VoiceEvent.Surrender && Time.time < s.NextVoice) return;
            if (e == VoiceEvent.Surrender && Time.time < s.NextSurrenderVoice) return;
            s.PendingVoice = e;
            s.PendingVoiceUntil = Time.time + RetryWindow;
        }

        internal static void Tick(SurrenderState s)
        {
            if (s.PendingVoice == null || s.Bot == null) return;
            VoiceEvent e = s.PendingVoice.Value;
            if (Time.time > s.PendingVoiceUntil)
            {
                s.PendingVoice = null;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: speaker stayed busy {RetryWindow:0} s, skipped");
                return;
            }
            BaseSpeaker speaker = s.Bot.Speaker;
            if (speaker == null) { s.PendingVoice = null; return; }
            if (speaker.Speaking || speaker.Busy) return;

            s.PendingVoice = null;
            ETagStatus mask = MaskFor(s.Bot);
            string voice = speaker.PlayerVoice;
            foreach (EPhraseTrigger line in Lines(e))
            {
                TagBank played = null;
                HandsAreHighPlugin.Try("voice", () => played = speaker.Play(line, mask, true));
                if (played == null) continue;
                s.NextVoice = Time.time + Settings.VoiceCooldown.Value;
                if (e == VoiceEvent.Surrender) s.NextSurrenderVoice = Time.time + Settings.VoiceCooldown.Value;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: played {line} ({voice})");
                return;
            }
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: voice {voice} has none of {string.Join(", ", Lines(e))}");
        }

        // The bot's own tag mask (faction, combat state, injury), as BotTalk builds it.
        private static ETagStatus MaskFor(Player bot)
        {
            try
            {
                BotTalk talk = bot.AIData?.BotOwner?.BotTalk;
                if (talk != null) return talk.GetMask(null);
            }
            catch { }
            return ETagStatus.Combat;
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
        private static PropertyInfo _activeLayer;
        private static object _noLayer;

        internal static void Reset()
        {
            _resolved = false;
            _getPersonality = null;
            _getBotComponent = null;
            _suppression = null;
            _suppressionState = null;
            _activeLayer = null;
            _noLayer = null;
        }

        // SAIN only clears BotComponent.ActiveLayer from its own layer's IsActive(); once our
        // higher-priority layer wins, BigBrain stops asking SAIN's layers, so the flag stays
        // set. While it is set SAIN's BotMoverManualUpdate/FixedUpdate prefixes skip the game's
        // mover (our path is never walked: the "stuck" retreats) and SmoothTurnPatch keeps
        // turning the bot itself (fighting our facing: jitter). Setting it to None is exactly
        // what SAINLayer.SetLayer(false) does. Returns true when it had to be cleared.
        internal static bool ReleaseLayer(BotOwner owner)
        {
            if (!Settings.ReleaseSainControl.Value || owner == null) return false;
            Resolve();
            if (_activeLayer == null || _noLayer == null) return false;
            try
            {
                object component = _getBotComponent.Invoke(null, new object[] { owner });
                if (component == null) return false;
                object current = _activeLayer.GetValue(component, null);
                if (Equals(current, _noLayer)) return false;
                _activeLayer.SetValue(component, _noLayer, null);
                return true;
            }
            catch { return false; }
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
                _activeLayer = _getBotComponent?.ReturnType.GetProperty("ActiveLayer", BindingFlags.Public | BindingFlags.Instance);
                if (_activeLayer != null && _activeLayer.CanWrite && _activeLayer.PropertyType.IsEnum)
                    _noLayer = Enum.Parse(_activeLayer.PropertyType, "None");
                HandsAreHighPlugin.Log.LogInfo(
                    $"SAIN found: personality {(_getPersonality != null ? "yes" : "no")}, suppression {(_suppressionState != null ? "yes" : "no")}, " +
                    $"layer release {(_noLayer != null ? "yes" : "no")}");
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
