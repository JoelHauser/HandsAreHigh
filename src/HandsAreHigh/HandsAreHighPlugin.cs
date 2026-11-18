using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Bots can surrender: the weapon in hand goes on the ground, hands go up, and from then
    // on the bot acts on his own (breaks, runs, rearms). A BigBrain layer owns the bot while
    // he's held; a postfix on Player.VisualPass poses the arms.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
    public class HandsAreHighPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.joelhauser.handsarehigh";
        public const string PluginName = "Hands Are High";
        public const string PluginVersion = "0.1.0";

        // Brain names, as SAIN lists them: Scavs (Assault, CursAssault) and PMCs
        // (PmcBear, PmcUsec, plus SPT's "PMC").
        private static readonly List<string> Brains = new List<string>
        {
            "Assault", "CursAssault", "PMC", "PmcBear", "PmcUsec"
        };

        internal static ManualLogSource Log;
        private static readonly Dictionary<string, float> ErrorLoggedAt = new Dictionary<string, float>();

        private readonly HotReloadTeardown _teardown = new HotReloadTeardown();

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);

            // SAIN registers its layers at 20-24 (combat, extract), 80 (avoid threat)
            // and 99 (its debug layer), ORBIT at 19; the default 95 sits above them all.
            // BigBrain can't unregister a custom layer, so after a hot reload the old
            // SurrenderLayer stays in every brain. It only activates for bots in the old
            // assembly's registry, so emptying that registry (below) leaves it inert.
            BrainManager.AddCustomLayer(typeof(SurrenderLayer), Brains, Settings.LayerPriority.Value);
            _teardown.Add(SurrenderRegistry.Clear);
            _teardown.Add(SainAdapter.Reset);
            _teardown.Add(OrderMenu.ClearNotes);
            _teardown.Add(ErrorLoggedAt.Clear);

            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(HandsAreHighPlugin).Assembly);
            _teardown.Add(harmony.UnpatchSelf);

            _teardown.Own(gameObject.AddComponent<Runtime>());

            Log.LogInfo($"{PluginName} {PluginVersion} loaded; surrender layer at priority {Settings.LayerPriority.Value}");
        }

        private void OnDestroy() => _teardown.Run(Logger);

        // One failure never breaks a bot's brain or the frame. The same error is logged at
        // most every ErrorLogCooldown seconds.
        internal static void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                string key = what + e.GetType().Name;
                float now = Time.realtimeSinceStartup;
                if (ErrorLoggedAt.TryGetValue(key, out float at) && now - at < Settings.ErrorLogCooldown.Value) return;
                ErrorLoggedAt[key] = now;
                Log.LogError($"{what}: {e}");
            }
        }
    }
}
