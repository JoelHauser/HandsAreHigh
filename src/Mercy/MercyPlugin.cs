using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using HarmonyLib;
using UnityEngine;

namespace Mercy
{
    // Bots can surrender: the weapon in hand goes on the ground, hands go up, and from then
    // on the bot acts on his own (breaks, runs, rearms). A BigBrain layer owns the bot while
    // he's held; a postfix on Player.VisualPass poses the arms.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
    public class MercyPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.mercy";
        public const string PluginName = "Mercy";
        public const string PluginVersion = "0.31.3";

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

        // 0.29.2: whether this bot's brain carries our layers (BotOwner.Brain.BaseBrain.ShortName(),
        // the same strings registered above). The game's "boss or follower" roles include Rogues
        // (exUsec, brain "ExUsec"), sectants, the BTR gunner and others we never register on: a
        // surrender rolled for one of those would be held by the registry with no layer to drive him.
        internal static bool HasOurLayers(EFT.BotOwner o)
        {
            string brain;
            try { brain = o?.Brain?.BaseBrain?.ShortName(); } catch { return false; }
            if (brain == null) return false;
            return Brains.Contains(brain) || (BossLayersRegistered && BossBrains.Contains(brain));
        }
        internal static Harmony Harmony;

        internal static ManualLogSource Log;
        private static readonly Dictionary<string, float> ErrorLoggedAt = new Dictionary<string, float>();

        private readonly HotReloadTeardown _teardown = new HotReloadTeardown();

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            FastAccess.Warn = m => Log?.LogWarning(m);   // 0.20.1: a compiled SAIN reader fell back to reflection
            _teardown.Add(() => FastAccess.Warn = null);

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
            // The captor layer (a bot guarding a prisoner), on the same brains, below the surrender
            // layer and SAIN's avoid-threat layer (80). Inert unless Captors holds a custody.
            Captors.RegisterLayers(Brains, Settings.CaptorPriority.Value);
            if (BossLayersRegistered) Captors.RegisterLayers(BossBrains, Settings.CaptorPriority.Value);
            // 0.24.0: keep-away legs for released men (Escape.cs): above SAIN's combat layers and
            // ORBIT, below SAIN's avoid-threat layer (80). Inert unless Escape runs a leg for him.
            int keepAway = UnityEngine.Mathf.Clamp(Settings.CaptorPriority.Value + 2, 1, Settings.LayerPriority.Value - 1);
            Escape.RegisterLayers(Brains, keepAway);
            if (BossLayersRegistered) Escape.RegisterLayers(BossBrains, keepAway);
            // 0.16.0: debug scenarios hold their bots until all are ready (inert for every other
            // bot: active only for a bot in ScenarioStaging.Held). Above the surrender layer.
            ScenarioStaging.RegisterLayers(Brains, Mathf.Min(200, Settings.LayerPriority.Value + 3));
            _teardown.Add(ScenarioStaging.Clear);
            _teardown.Add(LootingBotsAdapter.Reset);
            _teardown.Add(SurrenderRegistry.Clear);
            // 0.29.0: confirmed deaths for the boss last-survivor rule (fires on death only, not despawn).
            EFT.Player.OnPlayerDeadStatic += BossSquad.OnDeath;
            _teardown.Add(() => EFT.Player.OnPlayerDeadStatic -= BossSquad.OnDeath);
            _teardown.Add(BossSquad.Clear);
            // 0.20.1: the loot lookup cache holds scene objects and items; a hot-reloaded assembly
            // never unloads, so anything its statics keep stays referenced for the session.
            _teardown.Add(Arsenal.ClearCache);
            _teardown.Add(SainAdapter.Reset);
            _teardown.Add(OrbitObjectives.Reset);   // 0.26.0: cached ORBIT members dropped
            _teardown.Add(OrbitAdapter.Reset);      // every ORBIT sleep block we set is lifted
            _teardown.Add(CaptorVoice.Clear);
            _teardown.Add(OrderMenu.ClearNotes);
            _teardown.Add(Runtime.Menu.Reset);      // closes the menu; InputGuard.Reset: nothing blocked
            _teardown.Add(Voice.Reset);
            _teardown.Add(CommandVoice.Reset);
            _teardown.Add(ErrorLoggedAt.Clear);
            _teardown.Add(() => { Presence.TestNeutral = Presence.TestDiag = false; });   // 0.18.0

            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(MercyPlugin).Assembly);
            Harmony = harmony;   // late patches (SAIN, found at raid start) go through the same instance
            _teardown.Add(harmony.UnpatchSelf);
            _teardown.Add(() => Harmony = null);

            _teardown.Own(gameObject.AddComponent<Runtime>());
            _teardown.Own(gameObject.AddComponent<RuntimeGui>());
            _teardown.Add(Perf.Reset);
            // Runs first: despawns scenario bots and lifts their overrides while the registry
            // and patches still exist.
            _teardown.Add(Scenarios.Teardown);

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
            foreach (Type t in typeof(MercyPlugin).Assembly.GetTypes())
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
