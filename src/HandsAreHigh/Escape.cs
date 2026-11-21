using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.24.0: after a release. The 0.23.0 raid (LogOutput 2026-11-21): every released man went to
    // "SAIN : Combat Layer" with his ex-captor as target; #2 walked from 54 m back to 25 m of his
    // captors and died there, #19 had walked 32 m and was 8 m from where he was held, #2/#9/#20 took
    // a grenade into their hands seconds after the hand-back. Root cause (decompiles): the game's
    // BotWeaponManager keeps the thrown gun's controller as ShootController, so HaveBullets stayed
    // true for an empty-handed man and SAIN's decisions treated him as armed (WeaponState.cs fixes
    // that). This file is the rest of a coherent disengagement:
    //
    //   Memory   from the hand-back, for "Remembers the capture for": where he was held, and the
    //            threat where he last SAW it (updated only while he sees his ex-captor: no
    //            tracking of what he can't perceive).
    //   Keep-away  while unarmed (no usable gun in his hands or on him), not under fire, and within
    //            the memory: if his own AI walks him back within "Keep away radius" of either place
    //            (getting closer over the last 3 s), the KeepAway layer (priority between SAIN's
    //            combat layers and its avoid-threat layer) takes him one leg away (Perception.
    //            FleePoint, the same route rules as an escape), then hands him back. At most "Keep
    //            away legs" per release. Logged with the layer and target that brought him back.
    //   Armed    a usable gun: his AI's return is its own decision (logged once, not prevented).
    //
    // Bounded: at most 16 entries, one sample per entry per second, one route search per leg on a
    // frame with heavy-search budget. No inventory walk per frame (Arsenal.Usable is cached 4 s).
    internal static class Escape
    {
        private sealed class Entry
        {
            internal Player Bot;
            internal string Name;
            internal Player Captor;
            internal Vector3 HeldAt, ThreatAt, ThreatVel;
            internal float Until, Next, Since;
            internal int Legs;
            internal readonly float[] Dist = new float[4];
            internal int Samples;
            internal bool ArmedNoted, FireNoted;
            // A leg in progress (KeepAwayLogic).
            internal bool Active;
            internal string Why;
        }

        private static readonly Dictionary<Player, Entry> Entries = new Dictionary<Player, Entry>();
        private static readonly List<Player> Scratch = new List<Player>(8);

        internal static int Count => Entries.Count;

        // ---- helpers for the flight in our own layer ------------------------------------------

        internal static Vector3 FlatVelocity(Player p)
        {
            if (p == null) return Vector3.zero;
            Vector3 v = p.Velocity; v.y = 0f;
            return v;
        }

        // Where a man he sees will be in "Allow for where the captor is walking" seconds (<= 20 m).
        internal static Vector3 Ahead(Player c)
        {
            Vector3 step = FlatVelocity(c) * Settings.HeadingLookAhead.Value;
            if (step.magnitude > 20f) step = step.normalized * 20f;
            return c.Position + step;
        }

        // He would leave the same way his ex-captor is walking (within 35 deg), the captor moving.
        internal static bool SameWay(Player me, Player c, Vector3 target)
        {
            Vector3 v = FlatVelocity(c);
            if (v.sqrMagnitude < 0.5f * 0.5f) return false;
            Vector3 go = target - me.Position; go.y = 0f;
            return go.sqrMagnitude > 1f && Vector3.Angle(go, v) < 35f;
        }

        // One line per refused recovery reason per flight.
        internal static void LogRefusal(SurrenderState s, string what, string why)
        {
            string key = what + "|" + why;
            if (s.LastRefused == key) return;
            s.LastRefused = key;
            if (Settings.LogEscape.Value || Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: not going for {what}: {why}");
        }

        // ---- the memory after the hand-back -----------------------------------------------------

        internal static void Begin(SurrenderState s, Player p)
        {
            float secs = Settings.EscapeMemory.Value;
            if (secs <= 0f || p == null) return;
            if (!Entries.ContainsKey(p) && Entries.Count >= 16) return;
            float now = Time.time;
            var e = new Entry
            {
                Bot = p, Name = s.Name, Captor = s.Captor,
                HeldAt = s.HeldSet ? s.HeldAt : (s.DropPosition != Vector3.zero ? s.DropPosition : p.Position),
                ThreatAt = s.CaptorLastSeen != Vector3.zero ? s.CaptorLastSeen : (s.HeldSet ? s.HeldAt : p.Position),
                ThreatVel = s.CaptorLastVel,
                Since = now, Until = now + secs, Next = now + 1f
            };
            Entries[p] = e;
            if (Settings.LogEscape.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: remembers for {secs:0} s where he was held ({Perception.Flat(p.Position, e.HeldAt):0} m away) and where he last saw {(e.Captor != null ? Restraint.Name(e.Captor) : "the threat")} ({Perception.Flat(p.Position, e.ThreatAt):0} m away); {Arsenal.Equipment(p)}");
        }

        internal static void Tick()
        {
            if (Entries.Count == 0) return;
            float now = Time.time;
            Scratch.Clear();
            foreach (KeyValuePair<Player, Entry> kv in Entries)
            {
                Entry e = kv.Value;
                if (now < e.Next) continue;
                e.Next = now + 1f;
                Player p = e.Bot;
                if (!Perception.Alive(p) || SurrenderRegistry.HoldsBot(p)) { Scratch.Add(kv.Key); continue; }
                if (now >= e.Until && !e.Active)
                {
                    if (Settings.LogEscape.Value)
                        HandsAreHighPlugin.Log.LogInfo($"{e.Name}: forgets the capture ({now - e.Since:0} s): {Perception.Flat(p.Position, e.HeldAt):0} m from where he was held, {e.Legs} keep-away leg(s); {Arsenal.DescribeArmed(p)}");
                    Scratch.Add(kv.Key);
                    continue;
                }
                try { Sample(e, now); } catch (System.Exception ex) { HandsAreHighPlugin.Report("escape memory", ex); }
            }
            for (int i = 0; i < Scratch.Count; i++) Entries.Remove(Scratch[i]);
            Scratch.Clear();
        }

        private static void Sample(Entry e, float now)
        {
            Player p = e.Bot;
            // What he perceives of his ex-captor (and only that).
            Player c = e.Captor;
            if (Perception.Alive(c) && Perception.Sees(p, c, Vector3.Distance(p.Position, c.Position)))
            {
                e.ThreatAt = c.Position;
                e.ThreatVel = FlatVelocity(c);
            }
            float d = Mathf.Min(Perception.Flat(p.Position, e.HeldAt), Perception.Flat(p.Position, e.ThreatAt));
            for (int i = e.Dist.Length - 1; i > 0; i--) e.Dist[i] = e.Dist[i - 1];
            e.Dist[0] = d;
            e.Samples++;
            if (e.Active) return;

            float radius = Settings.KeepAwayRadius.Value;
            bool closing = e.Samples >= 4 && e.Dist[3] - d >= 3f;
            if (d >= radius || !closing) return;

            BotOwner o = p.AIData?.BotOwner;
            string layer = "?";
            try { if (o != null) layer = BrainManager.GetActiveLayerName(o) ?? "?"; } catch { }
            Player target = Perception.PlayerOf(o?.Memory?.GoalEnemy?.Person);
            string targetName = target != null ? Restraint.Name(target) : "none";

            // A gun he can fight with: his AI's decision to come back (deliberate), not ours to stop.
            if (Arsenal.GunInHands(p) || Arsenal.UsableGun(p) != null)
            {
                if (!e.ArmedNoted && Settings.LogEscape.Value)
                {
                    e.ArmedNoted = true;
                    HandsAreHighPlugin.Log.LogInfo($"{e.Name}: comes back towards where he was held ({d:0} m), armed ({Arsenal.DescribeArmed(p)}): his own AI's decision (layer {layer}, target {targetName}); not steered away");
                }
                return;
            }
            // Under fire or hit: his own AI reacts (cover, flight); a steer now would fight it.
            BotHistory h = SurrenderRegistry.HistoryOf(p);
            if (now - h.FireAt < 4f || o?.Memory != null && o.Memory.IsUnderFire)
            {
                if (!e.FireNoted && Settings.LogEscape.Value)
                {
                    e.FireNoted = true;
                    HandsAreHighPlugin.Log.LogInfo($"{e.Name}: {d:0} m from where he was held and under fire: his own AI handles it (layer {layer}, target {targetName})");
                }
                return;
            }
            if (e.Legs >= Settings.KeepAwayLegs.Value) return;
            e.Legs++;
            e.Active = true;
            e.Why = $"drifting back: {d:0} m from {(Perception.Flat(p.Position, e.HeldAt) <= Perception.Flat(p.Position, e.ThreatAt) ? "where he was held" : "where he last saw his captor")}, {e.Dist[3] - d:0} m closer in 3 s; layer {layer}, target {targetName}, {Arsenal.DescribeArmed(p)}";
            if (Settings.LogEscape.Value)
                HandsAreHighPlugin.Log.LogInfo($"{e.Name}: keeps away, leg {e.Legs} of at most {Settings.KeepAwayLegs.Value} ({e.Why})");
        }

        // KeepAwayLayer.IsActive: one dictionary lookup while anyone is remembered.
        internal static bool KeepAway(Player p) =>
            Entries.Count > 0 && p != null && Entries.TryGetValue(p, out Entry e) && e.Active;

        internal static bool Target(Player p, out Vector3 heldAt, out Vector3 threat, out Vector3 vel, out string name)
        {
            heldAt = threat = vel = Vector3.zero;
            name = null;
            if (p == null || !Entries.TryGetValue(p, out Entry e)) return false;
            heldAt = e.HeldAt; threat = e.ThreatAt; name = e.Name;
            // His velocity only if seen this very second (Sample keeps ThreatVel from the last sight).
            vel = Perception.Alive(e.Captor) && Perception.Sees(p, e.Captor, Vector3.Distance(p.Position, e.Captor.Position)) ? e.ThreatVel : Vector3.zero;
            return true;
        }

        internal static void EndLeg(Player p, string how)
        {
            if (p == null || !Entries.TryGetValue(p, out Entry e) || !e.Active) return;
            e.Active = false;
            e.Samples = 0;   // a fresh 3 s window before another leg
            if (Settings.LogEscape.Value)
                HandsAreHighPlugin.Log.LogInfo($"{e.Name}: keep-away leg {e.Legs} ends ({how}): {Perception.Flat(p.Position, e.HeldAt):0} m from where he was held, {Perception.Flat(p.Position, e.ThreatAt):0} m from where he last saw his captor; back to his own AI");
        }

        internal static void Forget(Player p) { if (p != null) Entries.Remove(p); }

        internal static void Clear() { Entries.Clear(); Scratch.Clear(); }

        internal static void RegisterLayers(List<string> brains, int priority) =>
            BrainManager.AddCustomLayer(typeof(KeepAwayLayer), brains, priority);
    }

    // Between SAIN's combat layers (20-24) / ORBIT (19) and SAIN's avoid-threat layer (80): a
    // grenade at his feet still comes first. Inert unless Escape has a leg running for this bot.
    internal sealed class KeepAwayLayer : CustomLayer
    {
        public KeepAwayLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        public override string GetName() => "HandsAreHigh_KeepAway";
        public override bool IsActive()
        {
            long t0 = Perf.Begin();
            bool on = Escape.KeepAway(BotOwner?.GetPlayer);
            Perf.End(Perf.Sec.LayerCheck, t0);
            return on;
        }
        public override Action GetNextAction() => new Action(typeof(KeepAwayLogic), "KeepAway");
        public override bool IsCurrentActionEnding() => !IsActive();
    }

    // One leg away from the capture spot and the threat he last saw, on foot (a brisk walk; a
    // sprint while he sees his ex-captor within 30 m), through the same route rules as an escape.
    internal sealed class KeepAwayLogic : CustomLogic
    {
        private Vector3 _dest;
        private bool _have, _moving;
        private float _since, _nextPath, _progressAt;
        private Vector3 _progressFrom;

        public KeepAwayLogic(BotOwner botOwner) : base(botOwner) { }

        private Player Me => BotOwner.GetPlayer;

        public override void Start()
        {
            _have = false; _moving = false;
            _since = Time.time;
            BotOwner.ShootData?.EndShoot();
        }

        public override void Stop()
        {
            if (_moving) { BotOwner.StopMove(); _moving = false; }
            BotOwner.Sprint(false);
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            Player me = Me;
            if (me == null) return;
            try { Run(me); }
            catch (System.Exception e) { HandsAreHighPlugin.Report("keep away", e); Escape.EndLeg(me, "error"); }
        }

        private void Run(Player me)
        {
            float now = Time.time;
            BotHistory h = SurrenderRegistry.HistoryOf(me);
            // Self-defence first: fire on him (a hit or a credible near miss, the game's or SAIN's
            // under-fire flag) or a gun in his hands ends the leg at once; his own AI reacts.
            if (now - h.FireAt < 1.5f || (BotOwner.Memory != null && BotOwner.Memory.IsUnderFire)) { Escape.EndLeg(me, "under fire: his own AI defends him"); return; }
            if (me.HandsController is Player.FirearmController) { Escape.EndLeg(me, "a gun in his hands: his own AI decides"); return; }
            SainAdapter.ReleaseLayer(BotOwner, h);
            if (!Escape.Target(me, out Vector3 heldAt, out Vector3 threat, out Vector3 vel, out _)) return;
            if (!_have)
            {
                if (!Perception.TakeHeavy()) return;
                float radius = Settings.KeepAwayRadius.Value + 15f;
                if (!Perception.FleePoint(me, threat, heldAt, radius, out _dest, out bool leg, out string why, vel))
                {
                    Escape.EndLeg(me, $"no way further away: {why}");
                    return;
                }
                _have = true;
                _since = now;
                _nextPath = 0f;
                _progressFrom = me.Position; _progressAt = now;
                if (Settings.LogEscape.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(me)}: keep-away to a spot {Perception.Flat(me.Position, _dest):0} m away, {Perception.Flat(_dest, heldAt):0} m from where he was held{(leg ? " (a leg through cover)" : "")}; {why}");
            }
            if (Perception.Flat(me.Position, _dest) < 1.5f) { Escape.EndLeg(me, "got there"); return; }
            if (now - _since > 20f) { Escape.EndLeg(me, "20 s on the way"); return; }
            if ((me.Position - _progressFrom).sqrMagnitude > 1f) { _progressFrom = me.Position; _progressAt = now; }
            else if (now - _progressAt > 4f) { Escape.EndLeg(me, "stuck"); return; }

            bool sprint = vel.sqrMagnitude > 0f || Perception.Flat(me.Position, threat) < 30f && Perception.CanSee(me, threat);
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            if (_moving && DoorBusy()) { BotOwner.Sprint(false); return; }
            if (!_moving || now >= _nextPath)
            {
                _nextPath = now + 3f;
                if (!Perception.PathTo(me.Position, _dest, out Vector3[] corners, out string why))
                {
                    Escape.EndLeg(me, $"no route ({why})");
                    return;
                }
                if (!_moving) Relink(me);
                _moving = true;
                BotOwner.MovementResume();
                BotOwner.Mover.GoToByWay(corners, 0.5f);
            }
            BotOwner.Steering.LookToMovingDirection();
            BotOwner.Sprint(sprint);
        }

        // As SurrenderLogic.Relink: the mover's last good NavMesh position is where he stands now.
        private void Relink(Player me)
        {
            BotMover mover = BotOwner.Mover;
            if (mover == null) return;
            Vector3 here = me.Position;
            mover.PositionOnWayInner = here;
            mover._prevSuccessLinkedFrom = here;
            mover._prevLinkPos = here;
            mover._lastGoodCastPoint = here;
            mover._lastGoodCastPointTime = Time.time;
            mover._prevPosLinkedTime = 0f;
        }

        // As SurrenderLogic.DoorBusy: the game's door opener, asked every moving frame.
        private bool DoorBusy()
        {
            BotDoorOpener d = BotOwner.DoorOpener;
            if (d == null) return false;
            DoorInteractionStatus st;
            try { st = d.UpdateDoorInteractionStatus(); }
            catch { return false; }
            return st == DoorInteractionStatus.OpeningDoor || d.Interacting || BotOwner.Mover.CurrentState == EBotMoverState.NearDoor;
        }
    }
}
