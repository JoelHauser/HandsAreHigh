using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Mercy
{
    // 0.16.0: scenario bots wait for each other.
    //
    // 0.15.0 log (2026-11-20, scenario #1): the Prisoner was active and acting at +4.7 s, PMC-1 at
    // +9.9 s, and PMC-2 was only requested then (one squad member after another) and was still on
    // its way when the scenario ended at +21 s. Bots spawned seconds apart and each one acted at
    // once: a Scav could run off before his captor existed.
    //
    // Now (ScenarioSpawner, Scenarios.Advance): every profile is requested at launch, in parallel;
    // each bot is held here from the moment the game creates him (the spawner's group action, before
    // his first brain update) until every participant is created, active, his brain is running this
    // layer, and the start is validated (spots, squads, relationships, sight and distance where the
    // preset asks); then all are released in the same frame and the setup starts.
    //
    // The hold is a BigBrain layer, the same mechanism as the surrender layer, above it (priority
    // +3) and only for bots in Held: he stands, doesn't move, doesn't shoot (the shield's truce and
    // observer override also hold fire), faces his spot's facing. SAIN's own layer flag is cleared
    // as for a prisoner. Released on: the release, the scenario's cleanup or failure, the raid's end,
    // teardown, and a hard limit (spawn timeout + 15 s) so nothing can stay frozen.
    internal static class ScenarioStaging
    {
        private sealed class Hold
        {
            internal Scenario Sc;
            internal Participant P;
            internal float Since;
        }

        private static readonly Dictionary<BotOwner, Hold> Held = new Dictionary<BotOwner, Hold>();
        private static readonly List<BotOwner> Scratch = new List<BotOwner>(8);

        internal const string LayerName = "Mercy_Staged";

        internal static int Count => Held.Count;

        internal static bool Holds(BotOwner o) => Held.Count > 0 && o != null && Held.ContainsKey(o);

        internal static void Add(Scenario sc, Participant p, BotOwner o)
        {
            // Only bots arriving for the start: a third party sent in mid-scenario acts at once.
            if (o == null || !Settings.ScenarioStaging.Value || sc.Stage != Stage.Spawning) return;
            Held[o] = new Hold { Sc = sc, P = p, Since = Time.time };
        }

        // Is his brain running the hold (the brain ticks, our layer won)?
        internal static bool BrainReady(BotOwner o)
        {
            try { return o?.Brain != null && o.Brain.ActiveLayerName() == LayerName; }
            catch { return false; }
        }

        internal static int Release(Scenario sc, string why)
        {
            Scratch.Clear();
            foreach (KeyValuePair<BotOwner, Hold> kv in Held) if (kv.Value.Sc == sc) Scratch.Add(kv.Key);
            foreach (BotOwner o in Scratch) Held.Remove(o);
            int n = Scratch.Count;
            Scratch.Clear();
            if (n > 0 && Settings.VerboseLogs.Value) MercyPlugin.Log.LogInfo($"Scenario {sc.Title}: {n} staged bot(s) released ({why})");
            return n;
        }

        internal static void Drop(BotOwner o) { if (o != null) Held.Remove(o); }

        internal static void Clear() => Held.Clear();

        // Runtime (scenario tick): nothing stays held past the limit.
        internal static void Tick()
        {
            if (Held.Count == 0) return;
            float limit = Settings.ScenarioSpawnTimeout.Value + 15f, now = Time.time;
            Scratch.Clear();
            foreach (KeyValuePair<BotOwner, Hold> kv in Held)
                if (kv.Key == null || kv.Key.IsDead || kv.Value.Sc.Cancelled || now - kv.Value.Since > limit) Scratch.Add(kv.Key);
            foreach (BotOwner o in Scratch)
            {
                if (Held.TryGetValue(o, out Hold h) && !h.Sc.Cancelled && o != null && !o.IsDead)
                    MercyPlugin.Log.LogWarning($"Scenario {h.Sc.Title}: {h.P.Tag} held {now - h.Since:0} s, past the staging limit: released (scenario stage {h.Sc.Stage})");
                Held.Remove(o);
            }
            Scratch.Clear();
        }

        internal static void RegisterLayers(List<string> brains, int priority) =>
            BrainManager.AddCustomLayer(typeof(StagingLayer), brains, priority);
    }

    internal sealed class StagingLayer : CustomLayer
    {
        public StagingLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        public override string GetName() => ScenarioStaging.LayerName;
        public override bool IsActive()
        {
            long t0 = Perf.Begin();
            bool on = ScenarioStaging.Holds(BotOwner);
            Perf.End(Perf.Sec.LayerCheck, t0);
            return on;
        }
        public override Action GetNextAction() => new Action(typeof(StagingLogic), "Staged");
        public override bool IsCurrentActionEnding() => !IsActive();
    }

    // Stands where he was placed, still, eyes on his scenario facing; no shot is started.
    internal sealed class StagingLogic : CustomLogic
    {
        private bool _stopped;
        private float _nextRelease;

        public StagingLogic(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            _stopped = false;
            BotOwner.Sprint(false);
            BotOwner.ShootData?.EndShoot();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            try
            {
                if (!_stopped) { BotOwner.StopMove(); BotOwner.SetTargetMoveSpeed(0f); BotOwner.Sprint(false); BotOwner.SetPose(1f); _stopped = true; }
                if (Time.time >= _nextRelease)
                {
                    _nextRelease = Time.time + 0.25f;
                    BotOwner.ShootData?.EndShoot();
                    SainAdapter.ReleaseLayer(BotOwner, SurrenderRegistry.HistoryOf(BotOwner.GetPlayer));
                }
            }
            catch (System.Exception e) { MercyPlugin.Report("scenario staging", e); }
        }
    }
}
