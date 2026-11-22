using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Mercy
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
    //            the memory: if his own AI walks him back within "Keep away radius" of his ex-captor
    //            (where he last saw him), or of the capture spot while the ex-captor is near it,
    //            getting closer over the last 3 s (0.28.3; before, the spot counted on its own, which
    //            fought SAIN taking him back to his squad), the KeepAway layer (priority between SAIN's
    //            combat layers and its avoid-threat layer) takes him one leg away (Perception.
    //            FleePoint, the same route rules as an escape), then hands him back. At most "Keep
    //            away legs" per release. Logged with the layer and target that brought him back.
    //   Armed    a usable gun: his AI's return is its own decision (logged once, not prevented).
    //   Fetch    0.30.2 (Joel): neither SAIN nor the game's AI ever picks a gun up, so a man handed
    //            back unarmed walked past guns for the rest of the raid. While remembered, unarmed and
    //            not under fire, every 2 s: the nearest loose gun (ground or body) within his own reach
    //            (FetchRangeFor: nerve and personality, 6-40 m) that he can see, can walk to, and that
    //            his ex-captor can see neither him nor (Joel: "not in your sight"). The KeepAway layer
    //            walks him there, picks it up (Arsenal.PickUp) and has his selector draw it; his own AI
    //            then has an armed man. Ends the moment his ex-captor can see him, fire comes in or a gun
    //            is in his hands. At most 2 fetches per release; guns he couldn't get are skipped.
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
            // 0.26.0: ORBIT objectives dropped for leading back to the capture (OrbitObjectives).
            internal int OrbitDrops;
            internal float NextOrbit;
            internal readonly float[] Dist = new float[4];
            internal int Samples;
            internal bool ArmedNoted, FireNoted;
            // A leg in progress (KeepAwayLogic).
            internal bool Active;
            internal string Why;
            // 0.30.2: a gun fetch (the leg is a walk to a gun, not away).
            internal bool Fetch;
            internal Item FetchGun;
            internal Vector3 FetchAt;
            internal float NextFetchLook, FetchRange;
            internal int FetchTries;
            internal List<Item> FetchSkip;
            internal string FetchNote;
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
                MercyPlugin.Log.LogInfo($"{s.Name}: not going for {what}: {why}");
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
                MercyPlugin.Log.LogInfo($"{s.Name}: remembers for {secs:0} s where he was held ({Perception.Flat(p.Position, e.HeldAt):0} m away) and where he last saw {(e.Captor != null ? Restraint.Name(e.Captor) : "the threat")} ({Perception.Flat(p.Position, e.ThreatAt):0} m away); {Arsenal.Equipment(p)}");
            // 0.26.0: the objective his AI is handed back with (ORBIT keeps the pre-capture one): one
            // that leads straight back to the capture is dropped now, before his AI turns him round.
            CheckObjective(e, now, always: true);
        }

        // At the hand-back, then every 2 s while remembered (at most 4 drops per release: ORBIT's
        // next pick may lie that way too, but it can't be fought forever).
        private static void CheckObjective(Entry e, float now, bool always)
        {
            if (e.OrbitDrops >= 4 || now < e.NextOrbit) return;
            e.NextOrbit = now + 2f;
            BotOwner o = e.Bot?.AIData?.BotOwner;
            string said = OrbitObjectives.RetireIfBack(o, e.HeldAt, e.ThreatAt, Settings.KeepAwayRadius.Value, out bool dropped);
            if (dropped) e.OrbitDrops++;
            if (said != null && (dropped || always) && Settings.LogEscape.Value)
                MercyPlugin.Log.LogInfo($"{e.Name}: {said}");
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
                        MercyPlugin.Log.LogInfo($"{e.Name}: forgets the capture ({now - e.Since:0} s): {Perception.Flat(p.Position, e.HeldAt):0} m from where he was held, {e.Legs} keep-away leg(s); {Arsenal.DescribeArmed(p)}");
                    Scratch.Add(kv.Key);
                    continue;
                }
                try { Sample(e, now); } catch (System.Exception ex) { MercyPlugin.Report("escape memory", ex); }
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
            // 0.28.3 (Joel's call after the live 0.28.2 raid): what he keeps away from is his ex-captor,
            // where he last saw him. The capture spot counts only while that is near it; with the
            // captor gone elsewhere (Давид: SAIN's Squad Layer took him back to his squad there three
            // times, his ex-captor 60-77 m away) going back is rejoining his squad, his AI's own call.
            // A dead ex-captor leaves nothing to keep away from.
            bool captorAlive = Perception.Alive(c);
            bool atSpot = CaptorAtSpot(e);
            float d = !captorAlive ? float.MaxValue
                    : atSpot ? Mathf.Min(Perception.Flat(p.Position, e.HeldAt), Perception.Flat(p.Position, e.ThreatAt))
                    : Perception.Flat(p.Position, e.ThreatAt);
            for (int i = e.Dist.Length - 1; i > 0; i--) e.Dist[i] = e.Dist[i - 1];
            e.Dist[0] = d;
            e.Samples++;
            CheckObjective(e, now, always: false);
            if (e.Active) return;
            if (TryStartFetch(e, now)) return;

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
                    MercyPlugin.Log.LogInfo($"{e.Name}: comes back towards where he was held ({d:0} m), armed ({Arsenal.DescribeArmed(p)}): his own AI's decision (layer {layer}, target {targetName}); not steered away");
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
                    MercyPlugin.Log.LogInfo($"{e.Name}: {d:0} m from where he was held and under fire: his own AI handles it (layer {layer}, target {targetName})");
                }
                return;
            }
            if (e.Legs >= Settings.KeepAwayLegs.Value) return;
            e.Legs++;
            e.Active = true;
            e.Why = $"drifting back: {d:0} m from {(atSpot && Perception.Flat(p.Position, e.HeldAt) <= Perception.Flat(p.Position, e.ThreatAt) ? "where he was held (his ex-captor still near it)" : "where he last saw his ex-captor")}, {e.Dist[3] - d:0} m closer in 3 s; layer {layer}, target {targetName}, {Arsenal.DescribeArmed(p)}";
            if (Settings.LogEscape.Value)
                MercyPlugin.Log.LogInfo($"{e.Name}: keeps away, leg {e.Legs} of at most {Settings.KeepAwayLegs.Value} ({e.Why})");
        }

        // ---- 0.30.2: a gun on the way (see the header) ---------------------------------------

        // How far he goes out of his way for a gun: his own nerve and personality (a brave man walks
        // further, a coward stays near), 20 m for an average man, 6-40 m.
        private static float FetchRangeFor(Player p, BotHistory h)
        {
            float pers = SainAdapter.Multiplier(SurrenderRegistry.PersonalityOf(p, h));
            return Mathf.Clamp(20f * (0.5f + h.Nerve) / Mathf.Max(0.3f, pers), 6f, 40f);
        }

        // His ex-captor (alive, within 100 m) has a clear line to him or to the gun.
        private static bool CaptorWatches(Entry e, Player p, Vector3 gunAt)
        {
            Player c = e.Captor;
            if (!Perception.Alive(c)) return false;
            Vector3 eye = Perception.Eye(c);
            bool nearMe = (c.Position - p.Position).sqrMagnitude < 100f * 100f;
            bool nearGun = (c.Position - gunAt).sqrMagnitude < 100f * 100f;
            return (nearMe && Perception.LineClear(eye, Perception.Eye(p))) ||
                   (nearGun && Perception.LineClear(eye, gunAt + Vector3.up * 0.3f));
        }

        private static void FetchNote(Entry e, string what)
        {
            if (e.FetchNote == what) return;
            e.FetchNote = what;
            if (Settings.LogEscape.Value) MercyPlugin.Log.LogInfo($"{e.Name}: {what}");
        }

        private static bool TryStartFetch(Entry e, float now)
        {
            if (now < e.NextFetchLook || e.FetchTries >= 2 || !Settings.ScavengeWeapons.Value) return false;
            e.NextFetchLook = now + 2f;
            Player p = e.Bot;
            if (Arsenal.GunInHands(p) || Arsenal.UsableGun(p) != null) return false;
            BotHistory h = SurrenderRegistry.HistoryOf(p);
            BotOwner o = p.AIData?.BotOwner;
            if (now - h.FireAt < 4f || (o?.Memory != null && o.Memory.IsUnderFire)) return false;
            if (e.FetchRange <= 0f) e.FetchRange = FetchRangeFor(p, h);
            if (!Perception.TakeHeavy()) { e.NextFetchLook = now + 0.25f; return false; }
            Item gun = Arsenal.FindWeapon(p, p.Position, 0f, e.FetchRange, e.FetchSkip, out Vector3 at);
            if (gun == null) return false;
            string name = gun.ShortName.Localized();
            float d = Perception.Flat(p.Position, at);
            if (!Perception.LineClear(Perception.Eye(p), at + Vector3.up * 0.3f)) { FetchNote(e, $"a {name} {d:0} m away, out of his sight: not now"); return false; }
            if (CaptorWatches(e, p, at)) { FetchNote(e, $"a {name} {d:0} m away, but {Restraint.Name(e.Captor)} would see him: waits"); return false; }
            if (!Perception.PathTo(p.Position, at, out Vector3[] path, out string np))
            {
                (e.FetchSkip ?? (e.FetchSkip = new List<Item>(2))).Add(gun);
                FetchNote(e, $"a {name} {d:0} m away, no route ({np}): leaves it");
                return false;
            }
            // 0.31.8: his reach is a walk, not a straight line. The 0.31.7 raid: Twitch_JACK went for an
            // AKS-74U 6 m away (reach 8 m) whose route was 285 m round (the gun behind a wall), and the leg
            // ended stuck at a door. The way there may be longer than the line, not much longer than his reach.
            float walk = Captors.PathLength(path);
            if (walk > Mathf.Max(e.FetchRange * 1.5f, 2f * d + 2f))
            {
                (e.FetchSkip ?? (e.FetchSkip = new List<Item>(2))).Add(gun);
                FetchNote(e, $"a {name} {d:0} m away, but the way round is {walk:0} m (his reach {e.FetchRange:0} m): leaves it");
                return false;
            }
            if (Perception.Alive(e.Captor) && Perception.RoutePassesNear(path, e.ThreatAt, 8f)) { FetchNote(e, $"a {name} {d:0} m away, but the way passes his ex-captor: not now"); return false; }
            e.Fetch = true;
            e.Active = true;
            e.FetchGun = gun;
            e.FetchAt = at;
            e.FetchTries++;
            e.Why = $"fetches a {name} {d:0} m away (his reach {e.FetchRange:0} m)";
            if (Settings.LogEscape.Value) MercyPlugin.Log.LogInfo($"{e.Name}: unarmed, out of his ex-captor's sight: {e.Why}");
            return true;
        }

        internal static bool Fetching(Player p, out Item gun, out Vector3 at)
        {
            gun = null; at = Vector3.zero;
            if (Entries.Count == 0 || p == null || !Entries.TryGetValue(p, out Entry e) || !e.Active || !e.Fetch) return false;
            gun = e.FetchGun; at = e.FetchAt;
            return true;
        }

        internal static bool FetchWatched(Player p) =>
            p != null && Entries.TryGetValue(p, out Entry e) && CaptorWatches(e, p, e.FetchAt);

        // A gun he couldn't get (gone, refused): not tried again this release.
        internal static void FetchSkip(Player p, Item gun)
        {
            if (p == null || gun == null || !Entries.TryGetValue(p, out Entry e)) return;
            (e.FetchSkip ?? (e.FetchSkip = new List<Item>(2))).Add(gun);
        }

        // KeepAwayLayer.IsActive: one dictionary lookup while anyone is remembered.
        internal static bool KeepAway(Player p) =>
            Entries.Count > 0 && p != null && Entries.TryGetValue(p, out Entry e) && e.Active;

        private static bool CaptorAtSpot(Entry e) =>
            Perception.Flat(e.ThreatAt, e.HeldAt) <= Settings.KeepAwayRadius.Value;

        internal static bool Target(Player p, out Vector3 heldAt, out Vector3 threat, out Vector3 vel, out string name)
        {
            heldAt = threat = vel = Vector3.zero;
            name = null;
            if (p == null || !Entries.TryGetValue(p, out Entry e)) return false;
            // 0.28.3: the capture spot is only a place to avoid while his ex-captor is near it.
            heldAt = CaptorAtSpot(e) ? e.HeldAt : e.ThreatAt; threat = e.ThreatAt; name = e.Name;
            // His velocity only if seen this very second (Sample keeps ThreatVel from the last sight).
            vel = Perception.Alive(e.Captor) && Perception.Sees(p, e.Captor, Vector3.Distance(p.Position, e.Captor.Position)) ? e.ThreatVel : Vector3.zero;
            return true;
        }

        internal static void EndLeg(Player p, string how)
        {
            if (p == null || !Entries.TryGetValue(p, out Entry e) || !e.Active) return;
            e.Active = false;
            e.Samples = 0;   // a fresh 3 s window before another leg
            if (e.Fetch)
            {
                e.Fetch = false;
                e.FetchNote = null;
                e.NextFetchLook = Time.time + 2f;
                if (Settings.LogEscape.Value)
                    MercyPlugin.Log.LogInfo($"{e.Name}: gun fetch ends ({how}); {Arsenal.DescribeArmed(p)}; back to his own AI");
                return;
            }
            if (Settings.LogEscape.Value)
                MercyPlugin.Log.LogInfo($"{e.Name}: keep-away leg {e.Legs} ends ({how}): {Perception.Flat(p.Position, e.HeldAt):0} m from where he was held, {Perception.Flat(p.Position, e.ThreatAt):0} m from where he last saw his captor; back to his own AI");
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
        public override string GetName() => "Mercy_KeepAway";
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
        private bool _have, _moving, _sprint;
        private float _since, _nextPath, _progressAt, _nextSprintCheck;
        private Vector3 _progressFrom;
        // 0.30.2: a gun fetch's pick-up (one transaction at a time; its callback may be stale).
        private bool _pickAsked;
        private float _pickAt, _nextWatchCheck, _handsBusySince = -1f;
        private int _pickToken;

        public KeepAwayLogic(BotOwner botOwner) : base(botOwner) { }

        private Player Me => BotOwner.GetPlayer;

        public override void Start()
        {
            _have = false; _moving = false; _sprint = false;
            _since = Time.time;
            _nextSprintCheck = 0f;
            _pickAsked = false; _nextWatchCheck = 0f; _pickToken++; _handsBusySince = -1f;
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
            catch (System.Exception e) { MercyPlugin.Report("keep away", e); Escape.EndLeg(me, "error"); }
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
            if (Escape.Fetching(me, out Item gun, out Vector3 gunAt)) { RunFetch(me, gun, gunAt, now); return; }
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
                    MercyPlugin.Log.LogInfo($"{Restraint.Name(me)}: keep-away to a spot {Perception.Flat(me.Position, _dest):0} m away, {Perception.Flat(_dest, heldAt):0} m from where he was held{(leg ? " (a leg through cover)" : "")}; {why}");
            }
            if (Perception.Flat(me.Position, _dest) < 1.5f) { Escape.EndLeg(me, "got there"); return; }
            if (now - _since > 20f) { Escape.EndLeg(me, "20 s on the way"); return; }
            // 0.31.8: the stall clock waits while the door opener has him (see RunFetch); 20 s still bounds it.
            if (_moving && DoorBusy()) { _progressFrom = me.Position; _progressAt = now; BotOwner.Sprint(false); return; }
            if ((me.Position - _progressFrom).sqrMagnitude > 1f) { _progressFrom = me.Position; _progressAt = now; }
            else if (now - _progressAt > 4f) { Escape.EndLeg(me, StuckWhy(me)); return; }

            // The header's rule: a brisk walk, a sprint only while he sees the threat within 30 m.
            // Pre-0.29 also sprinted whenever the ex-captor's LAST SEEN velocity was non-zero (stale,
            // and almost never exactly zero): every keep-away leg was a sprint. Asked every 0.5 s,
            // not with a sight line every frame.
            if (now >= _nextSprintCheck)
            {
                _nextSprintCheck = now + 0.5f;
                _sprint = Perception.Flat(me.Position, threat) < 30f && Perception.CanSee(me, threat);
            }
            bool sprint = _sprint;
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
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

        // 0.30.2: the walk to a gun, the pick-up and the draw. Ends when his ex-captor can see him, the
        // gun is gone, it takes too long, or (the caller) fire comes in / a gun is in his hands.
        private void RunFetch(Player me, Item gun, Vector3 at, float now)
        {
            if (now - _since > 25f) { Escape.EndLeg(me, "25 s and not there"); return; }
            if (_pickAsked)
            {
                if (now - _pickAt > 6f) { Escape.FetchSkip(me, gun); Escape.EndLeg(me, "the pick-up didn't finish"); }
                return;
            }
            if (now >= _nextWatchCheck)
            {
                _nextWatchCheck = now + 0.5f;
                if (Escape.FetchWatched(me)) { Escape.EndLeg(me, "his ex-captor can see him: leaves it for now"); return; }
            }
            if (Perception.Flat(me.Position, at) < 1.3f)
            {
                if (_moving) { BotOwner.StopMove(); _moving = false; }
                if (!Arsenal.Locate(gun, at, 2f, out _)) { Escape.FetchSkip(me, gun); Escape.EndLeg(me, "the gun isn't there any more"); return; }
                Item inHands = me.HandsController?.Item;
                // 0.31.6: empty hands are free. The empty-hands controller still reports an Item (a
                // placeholder), so "an item that isn't a knife" read empty hands as busy: every fetch
                // stood at the gun until the leg's limit (0.31.3 Тимурка, Карлуша x2; 0.31.5 log:
                // "his hands are busy (empty)"). Only meds, a grenade or similar in hands wait.
                if (!Arsenal.HandsEmpty(me) && inHands != null && !Arsenal.IsMelee(inHands))
                {
                    // Meds or a grenade in hands: a short wait, then his own AI has him. 0.31.4: the wait had no
                    // limit but the leg's 25 s and said nothing: three fetches in the 0.31.3 Customs raid stood
                    // frozen at the gun (hurt men, ContinuousHealing installed: probably healing).
                    if (_handsBusySince < 0f) _handsBusySince = now;
                    if (now - _handsBusySince > 3f)
                        Escape.EndLeg(me, $"at the gun, but his hands are busy ({Arsenal.DescribeHands(me)}): leaves it to his own AI");
                    return;
                }
                _handsBusySince = -1f;
                _pickAsked = true;
                _pickAt = now;
                int token = ++_pickToken;
                MercyPlugin.Try("fetch pick-up", () => Arsenal.PickUp(me, gun, ok => OnPicked(me, gun, ok, token)));
                return;
            }
            // 0.31.8: while the game's door opener has him, the stall clock waits (as the prisoner's own walk,
            // SurrenderLogic.MoveTo). Before, time at a door counted: the 0.31.7 raid ended a fetch "stuck ...
            // at a door" after 4 s. The leg's own time limit still bounds a door that never opens.
            if (_moving && DoorBusy()) { _progressFrom = me.Position; _progressAt = now; BotOwner.Sprint(false); return; }
            if ((me.Position - _progressFrom).sqrMagnitude > 1f) { _progressFrom = me.Position; _progressAt = now; }
            else if (_moving && now - _progressAt > 4f) { Escape.FetchSkip(me, gun); Escape.EndLeg(me, StuckWhy(me)); return; }
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Sprint(false);
            if (!_moving || now >= _nextPath)
            {
                _nextPath = now + 3f;
                if (!Perception.PathTo(me.Position, at, out Vector3[] corners, out string why))
                {
                    Escape.FetchSkip(me, gun);
                    Escape.EndLeg(me, $"no route to the gun ({why})");
                    return;
                }
                if (!_moving) { Relink(me); _progressFrom = me.Position; _progressAt = now; }
                _moving = true;
                BotOwner.MovementResume();
                BotOwner.Mover.GoToByWay(corners, 0.5f);
            }
            BotOwner.Steering.LookToMovingDirection();
        }

        private void OnPicked(Player me, Item gun, bool ok, int token)
        {
            if (token != _pickToken || !Perception.Alive(me)) return;   // a later leg, or he is gone
            if (!ok || !Arsenal.Carries(me, gun))
            {
                Escape.FetchSkip(me, gun);
                Escape.EndLeg(me, "the game refused the pick-up");
                return;
            }
            BotWeaponSelector sel = BotOwner.WeaponManager?.Selector;
            EquipmentSlot? slot = Arsenal.UsableGun(me) ?? Arsenal.GunOnBody(me);
            if (sel == null || slot == null) { Escape.EndLeg(me, $"picked up the {gun.ShortName.Localized()}; nothing to draw"); return; }
            MercyPlugin.Try("fetch draw", () =>
            {
                sel.UpdateWeaponsList();
                if (!sel.TryChangeToSlot(slot.Value, true, r => { }))
                    Escape.EndLeg(me, $"picked up the {gun.ShortName.Localized()}; the game refused the draw (his own AI may)");
            });
            // The leg ends when the gun is in his hands (Run: "a gun in his hands"), or at 25 s.
        }

        // 0.28.3: the live 0.28.2 raid had a leg "stuck" with a complete 27 m path and no word on why
        // (the next leg walked fine). What the game's mover and body were doing, once, at the event.
        private string StuckWhy(Player me)
        {
            string mover = "?", stamina = "?";
            try { mover = BotOwner.Mover?.CurrentState.ToString() ?? "none"; } catch { }
            try { stamina = me.Physical?.Stamina != null ? $"{me.Physical.Stamina.NormalValue:0.00}" : "?"; } catch { }
            float speed = 0f;
            try { Vector3 v = me.Velocity; v.y = 0f; speed = v.magnitude; } catch { }
            string door = "?";
            try { door = BotOwner.DoorOpener != null && BotOwner.DoorOpener.Interacting ? "at a door" : "no door"; } catch { }
            return $"stuck: under 1 m in 4 s; mover {mover}, {speed:0.0} m/s, stamina {stamina}, {door}, {Perception.Flat(me.Position, _dest):0} m to go";
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
