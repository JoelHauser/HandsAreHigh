using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using HarmonyLib;

namespace HandsAreHigh
{
    // Prototype: a debug key puts the nearest bot into surrender so the two open
    // questions can be answered in game:
    //   1. Does a BigBrain layer at high priority stop SAIN (and vanilla AI) from fighting?
    //   2. Does a coded arm pose, applied after the game's own arm IK, look acceptable?
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
    public class HandsAreHighPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.joelhauser.handsarehigh";
        public const string PluginName = "Hands Are High";
        public const string PluginVersion = "0.0.2";

        // Brain names, as SAIN lists them: Scavs (Assault, CursAssault) and PMCs
        // (PmcBear, PmcUsec, plus SPT's "PMC").
        private static readonly List<string> Brains = new List<string>
        {
            "Assault", "CursAssault", "PMC", "PmcBear", "PmcUsec"
        };

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);

            // SAIN registers its layers at 20-24 (combat, extract), 80 (avoid threat)
            // and 99 (its debug layer); the default 95 sits above every working one.
            BrainManager.AddCustomLayer(typeof(SurrenderLayer), Brains, Settings.LayerPriority.Value);

            new Harmony(PluginGuid).PatchAll(typeof(HandsAreHighPlugin).Assembly);
            gameObject.AddComponent<DebugControls>();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded; surrender layer at priority {Settings.LayerPriority.Value}");
        }

        internal static void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Log.LogError($"{what}: {e}"); }
        }
    }
}
