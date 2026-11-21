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
                HandedAt = p.Position, Last = p.Position, Since = now, NextSample = now + 1f, LastMoveAt = now
            });
        }

        internal static void Tick()
        {
            if (Entries.Count == 0) return;
            float now = Time.time;
            float limit = Limit;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
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
