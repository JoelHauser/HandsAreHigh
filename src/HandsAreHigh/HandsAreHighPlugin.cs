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
        public const string PluginVersion = "0.6.0";

        // Brain names, as SAIN lists them: Scavs (Assault, CursAssault) and PMCs
        // (PmcBear, PmcUsec, plus SPT's "PMC").
        private static readonly List<string> Brains = new List<string>
        {
            "Assault", "CursAssault", "PMC", "PmcBear", "PmcUsec"
        };

        // Boss and follower brains, from the game's own brain ShortName() strings (Assembly-CSharp
        // 0.16.9). Only registered when "Bosses and followers can surrender" is on at startup.
        private static readonly List<string> BossBrains = new List<string>
        {
            "BigPipe", "BirdEye", "BoarSniper", "BossBoar", "BossBully", "BossGluhar", "BossKojaniy", "BossKolontay",
            "BossPartisan", "BossSanitar", "BossZryachiy", "Fl_Zraychiy", "FlKlnAslt", "FollowerBully",
            "FollowerGluharAssault", "FollowerGluharProtect", "FollowerGluharScout", "FollowerKojaniy",
            "FollowerSanitar", "Killa", "KillaAgro", "Knight", "KolonSec", "Tagilla", "TagillaAgro", "TagillaFollower"
        };

        internal static bool BossLayersRegistered;

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
            if (Settings.IncludeBosses.Value)
            {
                BrainManager.AddCustomLayer(typeof(SurrenderLayer), BossBrains, Settings.LayerPriority.Value);
                BossLayersRegistered = true;
            }
            _teardown.Add(SurrenderRegistry.Clear);
            _teardown.Add(SainAdapter.Reset);
            _teardown.Add(OrderMenu.ClearNotes);
            _teardown.Add(Voice.Reset);
            _teardown.Add(CommandVoice.Reset);
            _teardown.Add(ErrorLoggedAt.Clear);

            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(HandsAreHighPlugin).Assembly);
            _teardown.Add(harmony.UnpatchSelf);

            _teardown.Own(gameObject.AddComponent<Runtime>());
            _teardown.Own(gameObject.AddComponent<RuntimeGui>());
            _teardown.Add(Perf.Reset);

            PrepareMethods();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded; surrender layer at priority {Settings.LayerPriority.Value}");
        }

        private void OnDestroy() => _teardown.Run(Logger);

        // JIT-compile our own methods at startup (the loading screen), not in the frame of the
        // first surrender, first gesture or first pick-up. Mono implements PrepareMethod as a
        // compile; generic and abstract methods are skipped. Failures only cost the warm-up.
        private static void PrepareMethods()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 0;
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (Type t in typeof(HandsAreHighPlugin).Assembly.GetTypes())
            {
                if (t.IsGenericTypeDefinition || t.IsInterface) continue;
                foreach (System.Reflection.MethodInfo m in t.GetMethods(all))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); n++; }
                    catch { }
                }
            }
            Log.LogInfo($"Pre-compiled {n} methods in {sw.ElapsedMilliseconds} ms");
        }

        // One failure never breaks a bot's brain or the frame. The same error is logged at
        // most every ErrorLogCooldown seconds. Hot paths use try/catch + Report directly:
        // a lambda passed to Try allocates a closure every call.
        internal static void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Report(what, e); }
        }

        internal static void Report(string what, Exception e)
        {
            string key = what + e.GetType().Name;
            float now = Time.realtimeSinceStartup;
            if (ErrorLoggedAt.TryGetValue(key, out float at) && now - at < Settings.ErrorLogCooldown.Value) return;
            ErrorLoggedAt[key] = now;
            Log.LogError($"{what}: {e}");
        }
    }
}
