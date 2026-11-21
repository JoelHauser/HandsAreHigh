using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.21.0: a bot handed back to his own AI is followed in the log for a while (default 60 s):
    // three lines (+15 s, +30 s, the end) saying where he is relative to where he was held, how
    // far he has moved since the hand-back, which AI layer drives him, whether he is moving, his
    // posture, his target and what he is armed with. "Released bots continue their raid" is then
    // a log fact, not an impression: a bot still standing at the capture spot after 30 s is
    // flagged STILL NEAR. Positions are sampled once a second (no per-frame work, no logs between
    // the three lines). Nothing here acts on him.
    // 0.24.0: followed for the longer of this and "Remembers the capture for", with lines at +15,
    // +30, +60, +90, +120 s, the furthest he got from where he was held, and CIRCLED BACK when he is
    // back within the keep-away radius after having been 15 m further out (Escape.cs acts on that;
    // this only reports it).
    // 0.25.0: the hand-off timeline. For the first 10 s after the hand-back the active layer is read
    // every frame (at most 16 bots, one name lookup each) until his own AI is seen driving him and
    // moving him; then one line: from the first "left" cue (or the escape) to his AI moving him, every
    // step with its time and the longest gap named, so a lingering prisoner shows where the time went
    // (cues, his reaction, the arms, the rise, our flight, the layer switch, or his AI's first move).
    internal static class ReleaseWatch
    {
        private sealed class Entry
        {
            internal Player Bot;
            internal string Name;
            internal Vector3 HeldAt, HandedAt, Last;
            internal bool HeldKnown;
            internal float Since, NextSample, Travelled, LastMoveAt;
            internal float Furthest;
            internal int Reported;
            internal bool Circled;
            // 0.25.0 timeline (Time.time, -1 unknown)
            internal float TCue, TNoticed, TEscape, TArmsDown, TOnFeet, TFirstMove, TLayerOff;
            internal string EscapeKind;
            internal bool Walking;
            internal float AiAt = -1f, AiMoveAt = -1f;
            internal string AiLayer;
            internal bool TimelineDone;
        }

        private static readonly List<Entry> Entries = new List<Entry>(4);
        private static readonly float[] ReportAt = { 15f, 30f, 60f, 90f, 120f };
        private static float Limit => Mathf.Max(Settings.ReleaseWatchSeconds.Value, Settings.EscapeMemory.Value);

        internal static int Count => Entries.Count;

        internal static void Start(SurrenderState s, Player p)
        {
            float secs = Limit;
            if (Settings.ReleaseWatchSeconds.Value <= 0f || p == null || Entries.Count >= 16) return;
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Bot == p) { Entries.RemoveAt(i); break; }
            float now = Time.time;
            Entries.Add(new Entry
            {
                Bot = p, Name = s.Name, HeldAt = s.HeldSet ? s.HeldAt : s.DropPosition, HeldKnown = true,
                HandedAt = p.Position, Last = p.Position, Since = now, NextSample = now + 1f, LastMoveAt = now,
                TCue = s.TLeftCue, TNoticed = s.TLeftNoticed, TEscape = s.TEscape, TArmsDown = s.THandsDown, TOnFeet = s.TOnFeet,
                TFirstMove = s.TFirstMove, TLayerOff = s.TLayerOff >= 0f ? s.TLayerOff : now, EscapeKind = s.EscapeKind, Walking = s.HandBackMoving
            });
        }

        // Every frame for an entry whose timeline isn't logged yet (<= 10 s after the hand-back).
        private static void WatchHandoff(Entry e, float now)
        {
            BotOwner o = e.Bot.AIData?.BotOwner;
            if (e.AiAt < 0f)
            {
                string layer = null;
                try { if (o != null) layer = BrainManager.GetActiveLayerName(o); } catch { }
                if (!string.IsNullOrEmpty(layer) && layer != "HandsAreHigh_Surrender") { e.AiAt = now; e.AiLayer = layer; }
            }
            if (e.AiAt >= 0f && e.AiMoveAt < 0f && e.Bot.Velocity.sqrMagnitude > 0.5f * 0.5f) e.AiMoveAt = now;
            if ((e.AiAt >= 0f && e.AiMoveAt >= 0f) || now - e.Since > 10f) LogTimeline(e, now);
        }

        private static readonly string[] StepNames = new string[9];
        private static readonly float[] StepTimes = new float[9];

        private static void LogTimeline(Entry e, float now)
        {
            e.TimelineDone = true;
            if (!Settings.LogEscape.Value && !Settings.LogEncounter.Value) return;
            int n = 0;
            void Add(string name, float t) { if (t >= 0f) { StepNames[n] = name; StepTimes[n] = t; n++; } }
            Add("left cues", e.TCue);
            Add("noticed", e.TNoticed);
            Add(e.EscapeKind ?? "escape", e.TEscape);
            Add("arms down", e.TArmsDown);
            Add("on his feet", e.TOnFeet);
            Add("moving", e.TFirstMove);
            Add("our layer off" + (e.Walking ? " (mid-stride)" : ""), e.TLayerOff);
            Add($"his AI ({e.AiLayer ?? "not seen"})", e.AiAt);
            Add("moved by his AI", e.AiMoveAt);
            if (n == 0) return;
            float t0 = StepTimes[0];
            for (int i = 1; i < n; i++) if (StepTimes[i] < t0) t0 = StepTimes[i];
            var sb = new System.Text.StringBuilder($"{e.Name}: hand-off timeline: ");
            float gap = -1f; string gapAt = null;
            for (int i = 0; i < n; i++)
            {
                sb.Append(i == 0 ? "" : ", ").Append(StepNames[i]).Append(" +").Append((StepTimes[i] - t0).ToString("0.00"));
                if (i > 0 && StepTimes[i] - StepTimes[i - 1] > gap) { gap = StepTimes[i] - StepTimes[i - 1]; gapAt = $"{StepNames[i - 1]} -> {StepNames[i]}"; }
            }
            if (e.AiAt < 0f) sb.Append("; HIS OWN AI NOT SEEN DRIVING HIM WITHIN 10 s");
            else if (e.AiMoveAt < 0f) sb.Append("; his AI didn't move him within 10 s");
            if (gapAt != null) sb.Append($"; longest step {gapAt} ({gap:0.00} s)");
            HandsAreHighPlugin.Log.LogInfo(sb.ToString());
        }

        internal static void Tick()
        {
            if (Entries.Count == 0) return;
            float now = Time.time;
            float limit = Limit;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
                if (!e.TimelineDone && Perception.Alive(e.Bot) && !SurrenderRegistry.HoldsBot(e.Bot))
                {
                    try { WatchHandoff(e, now); } catch (System.Exception ex) { e.TimelineDone = true; HandsAreHighPlugin.Report("hand-off timeline", ex); }
                }
                if (now < e.NextSample) continue;
                e.NextSample = now + 1f;
                if (!Perception.Alive(e.Bot))
                {
                    HandsAreHighPlugin.Log.LogInfo($"{e.Name}: after release +{now - e.Since:0} s: dead or gone");
                    Entries.RemoveAt(i);
                    continue;
                }
                // Already ours again (caught, gave up): the registry has him; this watch ends.
                if (SurrenderRegistry.HoldsBot(e.Bot)) { Entries.RemoveAt(i); continue; }
                Vector3 pos = e.Bot.Position;
                float step = Perception.Flat(pos, e.Last);
                if (step > 0.3f) { e.Travelled += step; e.LastMoveAt = now; }
                e.Last = pos;
                float t = now - e.Since;
                float held = Perception.Flat(pos, e.HeldAt);
                if (held > e.Furthest) e.Furthest = held;
                if (!e.Circled && e.Furthest - held > 15f && held < Settings.KeepAwayRadius.Value)
                {
                    e.Circled = true;
                    try { Report(e, now, t, false, "CIRCLED BACK"); } catch (System.Exception ex) { HandsAreHighPlugin.Report("release watch", ex); }
                }
                bool end = t >= limit;
                if (!end && (e.Reported >= ReportAt.Length || t < ReportAt[e.Reported])) continue;
                e.Reported++;
                try { Report(e, now, t, end); } catch (System.Exception ex) { HandsAreHighPlugin.Report("release watch", ex); }
                if (end) Entries.RemoveAt(i);
            }
        }

        private static void Report(Entry e, float now, float t, bool end, string flag = null)
        {
            BotOwner o = e.Bot.AIData?.BotOwner;
            string layer = "?";
            try { if (o != null) layer = BrainManager.GetActiveLayerName(o) ?? "?"; } catch { }
            float fromHeld = Perception.Flat(e.Bot.Position, e.HeldAt);
            float fromHand = Perception.Flat(e.Bot.Position, e.HandedAt);
            bool moving = e.Bot.Velocity.sqrMagnitude > 0.3f * 0.3f;
            string pose = e.Bot.PoseLevel < 0.5f ? "crouched" : "standing";
            Player enemy = Perception.PlayerOf(o?.Memory?.GoalEnemy?.Person);
            string target = enemy != null ? Restraint.Name(enemy) : "none";
            string idle = moving ? "moving" : $"still for {now - e.LastMoveAt:0} s";
            bool stuck = t >= 30f && fromHeld < 10f && e.Travelled < 10f && !moving && enemy == null;
            HandsAreHighPlugin.Log.LogInfo($"{e.Name}: after release +{t:0} s{(end ? " (last check)" : "")}{(flag != null ? $": {flag}" : "")}: {fromHeld:0} m from where he was held (furthest {e.Furthest:0} m), " +
                $"{fromHand:0} m from the hand-back ({e.Travelled:0} m walked), layer {layer}, {idle}, {pose}, target {target}, {Arsenal.DescribeArmed(e.Bot)}" +
                $"{(stuck ? "; STILL NEAR THE CAPTURE SPOT" : "")}");
        }

        internal static void Clear() => Entries.Clear();
    }
}
