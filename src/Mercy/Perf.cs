using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Mercy
{
    // Optional profiler (F12 > 7. Debugging > Profile, off by default). Off, every hook costs one
    // bool test. On, it times each of our entry points (Stopwatch ticks, no allocation) and
    // records every frame's unscaled delta time, and every ReportSeconds logs:
    //   frame times: average, p50, p99, p99.9, worst, 1% low FPS (whole game, for comparing a
    //                run with the mod enabled against one with "Enabled" off);
    //   our cost: per entry point total ms per second, average and worst microseconds per call;
    //   GC: gen-0 collections and managed heap size since the last report;
    //   our tracked objects: held bots, raid history, caches (should stay bounded).
    internal static class Perf
    {
        // 0.20.1: Natural is the natural-surrender checks alone (Captors, Restraint and Voice were
        // timed inside it); every other hook has its own section, and Scenarios (debug spawning and
        // staging) is reported apart from gameplay so a spawn spike isn't read as steady cost.
        //   Layer       held bots' layer logic (SurrenderLogic.Update)
        //   Captors     bot captors, squad cover, gear fetches, kill watch, and their layer logic
        //   Restraint   the observer scan (who holds fire at a prisoner)
        //   Voice       queued captor lines and the audio checks after any line
        //   LayerCheck  our layers' IsActive for every bot (BigBrain asks every brain update)
        //   SainFilter  the prefix/postfix on SAIN's ChooseEnemy (every SAIN bot, every 0.1 s)
        //   HeadLook    the game's bot head free-look prefix (every bot, every frame)
        //   FireGuard   the AI fire guards: GoalEnemy setter, ShootData.Shoot, trigger presses
        //   Scenarios   debug scenarios (spawning, staging, events), not gameplay
        // 0.28.2 detail sections, timed INSIDE the ones above (shown, never added to the totals):
        //   Kit         Arsenal.AssessKit (inventory walk + handbook prices; inside Natural or Captors)
        //   Start       SurrenderRegistry.Surrender (drop, captor decision, hooks; inside its caller)
        //   Decide      Captors.Decide (a bot captor's take/spare/ignore + custody; inside Start or Captors)
        internal enum Sec { Pose, Tilt, Shot, Hit, Registry, Natural, Captors, Restraint, Voice, Menu, Layer, Gui,
                            LayerCheck, SainFilter, HeadLook, FireGuard, Scenarios, Kit, Start, Decide, Count }

        private static bool Detail(int i) => i >= (int)Sec.Kit;

        internal static bool On;
        private static readonly long[] Ticks = new long[(int)Sec.Count];
        private static readonly long[] Max = new long[(int)Sec.Count];
        private static readonly int[] Calls = new int[(int)Sec.Count];
        private static readonly float[] Frames = new float[8192];
        private static readonly float[] Sorted = new float[8192];
        private static int _frames;
        private static float _since = -1f;
        private static int _gc0;
        private static long _heap0;
        private static readonly long[] FrameTicks = new long[(int)Sec.Count];
        private static float _nextSpikeLog;
        private static readonly StringBuilder Sb = new StringBuilder(1024);
        private const double SpikeMs = 1.0;
        private static int _gcFrame;

        internal static long Begin() => On ? Stopwatch.GetTimestamp() : 0L;

        internal static void End(Sec s, long t0)
        {
            if (t0 == 0L) return;
            long d = Stopwatch.GetTimestamp() - t0;
            int i = (int)s;
            Ticks[i] += d;
            FrameTicks[i] += d;
            Calls[i]++;
            if (d > Max[i]) Max[i] = d;
        }

        // Once per frame from Runtime.Update.
        internal static void Frame()
        {
            On = Settings.Profile.Value;
            if (!On) { _since = -1f; return; }
            float now = Time.realtimeSinceStartup;
            Spike(now);
            _gcFrame = GC.CollectionCount(0);
            if (_since < 0f) { Clear(now); return; }
            float dt = Time.unscaledDeltaTime;
            if (_frames < Frames.Length) Frames[_frames++] = dt;
            if (now - _since >= Settings.ProfileSeconds.Value) Report(now);
        }

        // Our own work over the last frame above SpikeMs: logged with its breakdown (at most
        // once a second), so a hitch on the first surrender, gesture, pick-up or a death shows
        // up with what caused it.
        private static void Spike(float now)
        {
            double tickMs = 1000.0 / Stopwatch.Frequency;
            long sum = 0;
            for (int i = 0; i < FrameTicks.Length; i++) if (!Detail(i)) sum += FrameTicks[i];
            if (sum * tickMs >= SpikeMs && now >= _nextSpikeLog)
            {
                _nextSpikeLog = now + 1f;
                Sb.Length = 0;
                Sb.Append($"Profile spike: Mercy took {sum * tickMs:0.00} ms in one frame (frame {Time.unscaledDeltaTime * 1000f:0.0} ms):");
                for (int i = 0; i < FrameTicks.Length; i++)
                    if (FrameTicks[i] > 0) Sb.Append($" {(Sec)i}{(Detail(i) ? " (part of the above)" : "")} {FrameTicks[i] * tickMs:0.00}");
                // 0.28.2: a garbage collection inside one of our timed calls is charged to that call
                // (Stopwatch is wall time); the 0.28.1 raid had spikes in sections that cost ~1 us.
                if (GC.CollectionCount(0) != _gcFrame) Sb.Append("; a GC collection ran this frame (part of these times may be the collection)");
                MercyPlugin.Log.LogInfo(Sb.ToString());
            }
            Array.Clear(FrameTicks, 0, FrameTicks.Length);
        }

        private static void Clear(float now)
        {
            Array.Clear(Ticks, 0, Ticks.Length);
            Array.Clear(Max, 0, Max.Length);
            Array.Clear(Calls, 0, Calls.Length);
            _frames = 0;
            _since = now;
            _gc0 = GC.CollectionCount(0);
            _heap0 = GC.GetTotalMemory(false);
        }

        internal static void Reset()
        {
            On = false;
            _since = -1f;
        }

        private static void Report(float now)
        {
            float span = Mathf.Max(0.001f, now - _since);
            int n = _frames;
            Array.Copy(Frames, Sorted, n);
            Array.Sort(Sorted, 0, n);
            double sum = 0;
            for (int i = 0; i < n; i++) sum += Sorted[i];
            float avg = n > 0 ? (float)(sum / n) : 0f;
            // 1% low: average FPS over the slowest 1% of frames.
            int worstCount = Mathf.Max(1, n / 100);
            double worstSum = 0;
            for (int i = n - worstCount; i < n; i++) if (i >= 0) worstSum += Sorted[i];
            float low1 = worstSum > 0 ? (float)(worstCount / worstSum) : 0f;

            double tickMs = 1000.0 / Stopwatch.Frequency;
            Sb.Length = 0;
            Sb.Append($"Profile {span:0} s, {n} frames: avg {avg * 1000f:0.00} ms ({(avg > 0 ? 1f / avg : 0):0} fps), ");
            Sb.Append($"p50 {P(n, 0.5f):0.00}, p99 {P(n, 0.99f):0.00}, p99.9 {P(n, 0.999f):0.00}, worst {(n > 0 ? Sorted[n - 1] * 1000f : 0):0.00} ms; 1% low {low1:0} fps; ");
            // Heap change over the span: a lower bound of what the whole game allocated (a
            // collection in between hides some), for comparing runs, not attributing to us.
            long heap = GC.GetTotalMemory(false);
            Sb.Append($"GC gen0 +{GC.CollectionCount(0) - _gc0}, heap {heap / 1048576f:0} MB ({(heap - _heap0) / 1048576f:+0;-0;0} MB); ");
            Sb.Append($"held {SurrenderRegistry.Count}, bot captors {Captors.Count}, unrecognised {Captors.PendingCount}, squad cover {Overwatch.Count}, ORBIT blocks {OrbitAdapter.Count}, restraint entries {Restraint.EntryCount}, history {SurrenderRegistry.HistoryCount} ({SurrenderRegistry.TrackedPlayers} live), loot cache {Arsenal.CacheCount}\n  Mercy cost:");
            double totalMs = 0, gameplayMs = 0;
            for (int i = 0; i < (int)Sec.Count; i++)
            {
                if (Calls[i] == 0) continue;
                double ms = Ticks[i] * tickMs;
                if (!Detail(i))
                {
                    totalMs += ms;
                    if (i != (int)Sec.Scenarios) gameplayMs += ms;
                }
                Sb.Append($" {(Sec)i} {ms / span:0.000} ms/s ({Calls[i]} calls, avg {ms * 1000.0 / Calls[i]:0.0} us, worst {Max[i] * tickMs * 1000.0:0} us);");
            }
            // ms per second of play is also the share of frame time (1000 ms of frames per second).
            Sb.Append($" gameplay {gameplayMs / span:0.000} ms/s = {gameplayMs / span / 10.0:0.000}% of frame time;");
            Sb.Append($" with scenarios {totalMs / span:0.000} ms/s");
            MercyPlugin.Log.LogInfo(Sb.ToString());
            Clear(now);
        }

        private static float P(int n, float q)
        {
            if (n == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(q * n) - 1, 0, n - 1);
            return Sorted[i] * 1000f;
        }
    }
}
