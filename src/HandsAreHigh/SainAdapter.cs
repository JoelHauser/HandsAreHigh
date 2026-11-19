using System;
using System.Linq;
using System.Reflection;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
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
        // Runs every 0.25 s per held bot: the component is looked up once per state (cached,
        // retried every 2 s if SAIN hasn't made it yet), so each check is one property read.
        internal static bool ReleaseLayer(BotOwner owner, SurrenderState s)
        {
            if (!Settings.ReleaseSainControl.Value || owner == null) return false;
            Resolve();
            if (_activeLayer == null || _noLayer == null) return false;
            try
            {
                object component = s.SainComponent;
                if (component == null)
                {
                    BotHistory h = SurrenderRegistry.HistoryOf(s.Bot);
                    component = ComponentOf(owner, h);
                    if (component == null) return false;
                    s.SainComponent = component;
                }
                object current = _activeLayer.GetValue(component, null);
                if (Equals(current, _noLayer)) return false;
                _activeLayer.SetValue(component, _noLayer, null);
                return true;
            }
            catch { return false; }
        }

        // SAIN's BotComponent for a bot, cached in his raid history; looked up again at most
        // every 2 s while missing. One shared argument array: no allocation per call.
        private static readonly object[] Args = new object[1];
        private static object ComponentOf(BotOwner owner, BotHistory h)
        {
            if (_getBotComponent == null) return null;
            if (h.SainComponent != null) return h.SainComponent;
            if (Time.time - h.SainLookupAt < 2f) return null;
            h.SainLookupAt = Time.time;
            Args[0] = owner;
            try { h.SainComponent = _getBotComponent.Invoke(null, Args); }
            finally { Args[0] = null; }
            return h.SainComponent;
        }

        // At raid start (SurrenderRegistry.EnsureRaid): the assembly scan happens here, not
        // in the frame of the first surrender check.
        internal static void Prewarm() => Resolve();

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
            Args[0] = owner;
            try { return _getPersonality.Invoke(null, Args) as string; }
            catch { return null; }
            finally { Args[0] = null; }
        }

        // 0 (None) .. 1 (Extreme); 0 without SAIN.
        internal static float SuppressionLevel(BotOwner owner, BotHistory h)
        {
            if (!Settings.SainSuppression.Value || owner == null) return 0f;
            Resolve();
            if (_suppressionState == null) return 0f;
            try
            {
                object component = ComponentOf(owner, h);
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
