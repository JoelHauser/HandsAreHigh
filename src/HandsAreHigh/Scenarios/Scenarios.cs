using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // The debug scenario sandbox: launch, staged setup, the timeline, cleanup.
    //
    // Dormant unless "Scenario tools" is on or a scenario is running: Runtime calls Tick only
    // then, and the hooks in the core read Active (one bool) or ScenarioShield.Active.
    //
    // Lifecycle of one scenario:
    //   Placing   the anchor and every spot are validated before anything spawns (a launch
    //             either places everyone or spawns nobody)
    //   Spawning  one spawn request per squad at a time (ScenarioSpawner); every arrival is
    //             registered: tagged (Nickname#S1-Scav in every log line), tracked, its death
    //             event hooked, its mod-side identity applied, a setup truce with the others,
    //             the observer override if wanted
    //   Setup     the preset's steps run in order (each waits, completes or fails, with a
    //             timeout); then the truce is lifted (except pairs the preset keeps, labelled)
    //   Running   scheduled events; the participants act on their own decisions
    //   Ended     cleanup: overrides lifted, loose scenario items removed, living scenario
    //             bots despawned, corpses left, report written
    //
    // Timeline: while a scenario runs, a BepInEx log listener reads this mod's own log lines
    // (the decisions are already logged with their reasons) and keeps those that name a
    // participant, classified (choice / no action / not eligible / execution failure / event).
    internal static class Scenarios
    {
        internal static bool Active;   // any scenario not yet cleaned up
        private static readonly List<Scenario> Running = new List<Scenario>();
        private static readonly Dictionary<Player, Participant> ByPlayer = new Dictionary<Player, Participant>();
        private static readonly List<Scenario> Scratch = new List<Scenario>(4);
        private static int _nextId = 1;
        private static Listener _listener;
        private static bool _inListener;
        private static float _nextDiff;

        // Bumped when a raid's scenarios are forgotten: a spawn answer from an older raid is stale.
        internal static int RaidSerial;

        internal static List<Scenario> List => Running;
        internal static bool Any => Running.Count > 0;

        internal static bool Watching(Player bot) => Active && (object)bot != null && ByPlayer.ContainsKey(bot);

        internal static string Label(Player p)
        {
            string nick = p.Profile?.Nickname ?? p.ProfileId;
            if (Active && ByPlayer.TryGetValue(p, out Participant q)) return $"{nick}#{q.Label}";
            return nick;
        }

        internal static Participant ParticipantOf(Player p) =>
            Active && (object)p != null && ByPlayer.TryGetValue(p, out Participant q) ? q : null;

        // ---- launch ------------------------------------------------------------------------

        internal static string Launch(ScenarioPreset preset, Player me, ParamValues values = null, ScenarioMode? mode = null)
        {
            if (!SurrenderRegistry.InRaid || me == null) return "not in a raid";
            if (!Settings.Enabled.Value) return "the mod is disabled (01. General: Enabled)";
            if (!ScenarioSpawner.Ready(out string notReady)) return notReady;
            if (Running.Count > 0 && !Settings.ScenarioConcurrent.Value) return $"scenario {Running[0].Title} is still running: clean it up first (or allow concurrent scenarios)";
            // Even with concurrent scenarios: not a second copy of one whose bots are still on
            // their way (repeated Enter presses queued six copies of a failing spawn).
            foreach (Scenario r in Running)
                if (r.Preset == preset && !r.Cancelled && ScenarioSpawner.Spawning(r))
                    return $"scenario {r.Title} is still spawning its bots: wait for it (or clean it up)";
            string missing = preset.Unavailable?.Invoke();
            if (missing != null) return $"unavailable: {missing}";

            var sc = new Scenario
            {
                Id = _nextId++,
                Preset = preset,
                Mode = mode ?? preset.Mode,
                Values = (values ?? preset.Values).Clone(),
                Observer = me,
                LaunchedAt = Time.time,
                StageSince = Time.time
            };
            sc.Seed = sc.Values.I("seed");
            sc.ShieldWanted = sc.Values.Is("shield", "On");
            if (!preset.OwnAnchor)
            {
                bool cross = sc.Values.Is("place", "At your crosshair");
                float dist = sc.Values.Has("dist") ? sc.Values.F("dist") : 30f;
                if (!ScenarioSpawner.Anchor(me, cross, dist, out sc.Anchor, out sc.Forward, out string aw)) return $"placement: {aw}";
                sc.Placement = cross ? "at your crosshair" : $"{dist:0} m ahead of you";
            }
            else sc.Placement = "relative to an existing bot";

            string planFail = preset.Plan(sc);
            if (planFail != null) return $"plan: {planFail}";

            int spawning = 0;
            foreach (Participant p in sc.People) if (p.Existing == null) spawning++;
            int already = 0;
            foreach (Scenario r in Running) foreach (Participant p in r.People) if (p.Existing == null && !p.Removed && !p.Dead) already++;
            if (already + spawning > Settings.ScenarioMaxBots.Value)
                return $"{spawning} bot(s) would make {already + spawning}, over 'Scenario bots at most' ({Settings.ScenarioMaxBots.Value})";

            // Every spot validated before anything spawns.
            var taken = new List<Vector3>();
            foreach (Participant p in sc.People)
            {
                if (p.Existing != null) { taken.Add(p.Existing.Position); continue; }
                Vector3 desired = ScenarioSpawner.World(sc, p.Offset);
                if (!ScenarioSpawner.FindSpot(desired, sc.Anchor, taken, out p.Spot, out string why)) return $"{p.Tag}: {why}";
                taken.Add(p.Spot);
            }
            float nearest = float.MaxValue;
            foreach (Vector3 v in taken) nearest = Mathf.Min(nearest, Vector3.Distance(v, me.Position));

            if (sc.Seed > 0)
            {
                if (Dice.Seeded) sc.Note(TL.Info, $"seed {sc.Seed}: another scenario's seed ({Dice.Seed}) is in use, so only per-participant values use this one");
                else Dice.UseSeed(sc.Seed);
            }
            Running.Add(sc);
            Active = true;
            EnsureListener();
            sc.Stage = Stage.Spawning;
            sc.Note(TL.Info, $"launched {preset.Name} ({(sc.Mode == ScenarioMode.Forced ? "FORCED STATE" : "natural trigger")}), {sc.Placement}, nearest participant {nearest:0} m from you; " +
                $"params: {sc.Values.Describe()}{(sc.Seed > 0 ? $"; seed {sc.Seed} (mod rolls only; the game is not deterministic)" : "")}");
            if (sc.ShieldWanted) sc.Overrides.Add("observer override: participants don't target you");

            var squads = new HashSet<string>();
            foreach (Participant p in sc.People)
            {
                if (p.Existing != null) { Register(sc, p); continue; }
                if (squads.Add(p.Squad)) ScenarioSpawner.Request(sc, p);
            }
            return $"launched {sc.Title}: spawning {spawning} bot(s) {sc.Placement}";
        }

        // The next member of a squad, after the previous one arrived.
        internal static void RequestNext(Scenario sc, string squad)
        {
            foreach (Participant p in sc.People)
                if (p.Squad == squad && p.Existing == null && !p.Requested) { ScenarioSpawner.Request(sc, p); return; }
        }

        internal static void Register(Scenario sc, Participant p)
        {
            if (p.Player == null) return;
            ByPlayer[p.Player] = p;
            if (p.Existing == null)
            {
                Participant q = p;
                p.DeathHandler = _ => OnDeath(q);
                p.Player.OnPlayerDeadOrUnspawn += p.DeathHandler;
                ScenarioOps.TrackWeapons(p);
                string id = ScenarioOps.ApplyIdentity(sc, p);
                if (id != null) { sc.Conditions.Add($"{p.Tag}: {id}"); sc.Note(TL.Info, $"{p.Tag}: {id}"); }
                if (sc.ShieldWanted && sc.Observer != null) ScenarioShield.Add(sc, ShieldKind.Observer, p.Player, sc.Observer);
            }
            if (p.Existing == null && sc.Stage == Stage.Running) Face(sc, p);
            if (p.Existing != null) sc.Note(TL.Info, $"{p.Tag}: existing raid bot {p.Player.Profile?.Nickname} ({p.Role}) joins the scenario (never despawned by it)");
            // The setup truce: participants arrive seconds apart and must not start shooting
            // before the scenario's start state exists. Third parties (Running) get none.
            if (sc.ShieldWanted && p.Existing == null) sc.Note(TL.Info, $"{p.Tag}: observer override applied (won't take you as a target)");
            if (sc.Stage == Stage.Spawning || sc.Stage == Stage.Setup)
                foreach (Participant o in sc.People)
                    if (o != p && o.Player != null && ByPlayer.ContainsKey(o.Player))
                    {
                        ScenarioShield.Add(sc, ShieldKind.Truce, p.Player, o.Player);
                        ScenarioShield.Add(sc, ShieldKind.Truce, o.Player, p.Player);
                    }
        }

        private static void OnDeath(Participant p)
        {
            if (p.Dead || p.Removed) return;
            p.Dead = true;
            p.Owner.Note(TL.Event, $"{p.Tag} died");
        }

        // A hostile bot near the target, away from you (preset "Captor comes under fire", or the menu).
        internal static string SpawnThirdParty(Scenario sc, Participant target)
        {
            int live = 0;
            foreach (Scenario r in Running) foreach (Participant p in r.People) if (p.Existing == null && !p.Removed && !p.Dead) live++;
            if (live >= Settings.ScenarioMaxBots.Value) return "refused: 'Scenario bots at most' reached";
            Vector3 from = target.Player.Position;
            Vector3 away = from - sc.Observer.Position; away.y = 0f;
            away = away.sqrMagnitude < 1f ? sc.Forward : away.normalized;
            var taken = new List<Vector3>();
            foreach (Participant q in sc.People) if (q.Player != null) taken.Add(q.Player.Position);
            float[] angles = { 0f, 50f, -50f, 100f, -100f };
            string why = null;
            foreach (float a in angles)
            {
                Vector3 desired = from + Quaternion.Euler(0f, a, 0f) * away * 40f;
                if (!ScenarioSpawner.FindSpot(desired, from, taken, out Vector3 spot, out why)) continue;
                var p = new Participant { Owner = sc, Index = sc.People.Count, Tag = "Third party", Squad = "X" + sc.People.Count, FaceTag = target.Tag, Spot = spot };
                ScenarioPresets.SetKind(p, target.Side == EPlayerSide.Bear ? "USEC" : "BEAR");
                sc.People.Add(p);
                ScenarioSpawner.Request(sc, p);
                return $"{p.Role} requested {Vector3.Distance(spot, from):0} m from {target.Tag} (real bot, hostile to him, no truce)";
            }
            return $"refused: no valid spot 40 m out ({why})";
        }

        // ---- per frame -----------------------------------------------------------------------

        internal static void Tick(Player me)
        {
            if (!Settings.ScenariosEnabled.Value && Running.Count > 0) CleanupAll("Scenario tools turned off");
            ScenarioUi.Update(me);
            if (Running.Count == 0) return;
            ScenarioShield.Tick();
            Scratch.Clear();
            Scratch.AddRange(Running);
            bool diff = Time.time >= _nextDiff;
            if (diff) _nextDiff = Time.time + 0.25f;
            for (int i = 0; i < Scratch.Count; i++)
            {
                Scenario sc = Scratch[i];
                try { Advance(sc, diff); }
                catch (Exception e) { HandsAreHighPlugin.Report("scenario", e); }
            }
            Scratch.Clear();
        }

        private static void Advance(Scenario sc, bool slow)
        {
            float now = Time.time;
            switch (sc.Stage)
            {
                case Stage.Spawning:
                {
                    bool all = true;
                    foreach (Participant p in sc.People)
                    {
                        if (p.Failed) { Cleanup(sc, $"setup failed: {p.Tag} {p.FailWhy}"); return; }
                        if (p.Dead) { Cleanup(sc, $"setup failed: {p.Tag} died before the start"); return; }
                        if (!p.Active) all = false;
                    }
                    if (!all)
                    {
                        if (now - sc.LaunchedAt > Settings.ScenarioSpawnTimeout.Value)
                        {
                            var late = new List<string>();
                            foreach (Participant p in sc.People) if (!p.Active) late.Add($"{p.Tag} ({(p.Spawned ? "spawned, not active" : p.Requested ? "no answer from the spawner" : "not requested")})");
                            Cleanup(sc, $"setup failed: not active after {Settings.ScenarioSpawnTimeout.Value:0} s: {string.Join(", ", late)}");
                        }
                        return;
                    }
                    foreach (Participant p in sc.People) Face(sc, p);
                    sc.Stage = Stage.Setup;
                    sc.StageSince = now;
                    sc.Note(TL.Info, $"everyone active after {now - sc.LaunchedAt:0.0} s; setup");
                    sc.Preset.Setup?.Invoke(sc);
                    return;
                }
                case Stage.Setup:
                {
                    while (sc.StepIndex < sc.Steps.Count)
                    {
                        SetupStep st = sc.Steps[sc.StepIndex];
                        if (st.StartedAt < 0f) st.StartedAt = now;
                        string r = st.Run();
                        if (r == "")
                        {
                            if (now - st.StartedAt < st.Timeout) return;
                            r = $"timed out after {st.Timeout:0} s";
                        }
                        if (r == null) sc.Note(TL.Info, $"setup step '{st.Label}' done ({now - st.StartedAt:0.0} s)");
                        if (r != null)
                        {
                            sc.Note(TL.ExecFail, $"setup step '{st.Label}': {r}");
                            if (st.Fatal) { sc.SetupFailed = $"{st.Label}: {r}"; sc.StepIndex = sc.Steps.Count; break; }
                        }
                        sc.StepIndex++;
                    }
                    LiftTruce(sc);
                    sc.Stage = Stage.Running;
                    sc.StageSince = now;
                    sc.Note(TL.Info, sc.SetupFailed != null
                        ? $"SETUP INCOMPLETE ({sc.SetupFailed}): running anyway for inspection; the start state is not what the preset describes"
                        : "setup complete: every participant acts on his own decisions from here");
                    return;
                }
                case Stage.Running:
                {
                    for (int i = 0; i < sc.Events.Count; i++)
                    {
                        ScheduledEvent ev = sc.Events[i];
                        if (ev.Done || now < ev.At) continue;
                        if (ev.When != null)
                        {
                            bool met;
                            try { met = ev.When(); }
                            catch (Exception e) { met = false; HandsAreHighPlugin.Report($"scenario {sc.Title} condition '{ev.Label}'", e); }
                            if (!met)
                            {
                                if (now > ev.Until) { ev.Done = true; sc.Note(TL.Info, $"'{ev.Label}': its condition never came up"); }
                                continue;
                            }
                        }
                        ev.Done = true;
                        sc.Note(TL.Info, $"scheduled '{ev.Label}' fires{(ev.When != null ? " (its condition came up)" : "")}");
                        try { ev.Run(); }
                        catch (Exception e) { sc.Note(TL.ExecFail, $"'{ev.Label}' threw {e.GetType().Name}: {e.Message}"); }
                    }
                    if (slow) Watch(sc);
                    return;
                }
            }
        }

        // Things the core doesn't log itself, noted on change (every 0.25 s).
        private static void Watch(Scenario sc)
        {
            foreach (Participant p in sc.People)
            {
                if (p.Player == null || p.Dead) continue;
                SurrenderState s = ScenarioOps.StateOf(p);
                if (s != null && s.DroppedWeapon != null && !p.Items.Contains(s.DroppedWeapon) && p.Existing == null) p.Items.Add(s.DroppedWeapon);
                bool captor = Captors.TryGet(p.Player, out CaptorState cs);
                CaptorMode? mode = captor ? cs.Mode : (CaptorMode?)null;
                if (captor != p.SeenCaptor) sc.Note(TL.Event, captor ? $"{p.Tag} now guards {cs.Prisoner?.Name}" : $"{p.Tag} no longer guards anyone");
                p.SeenCaptor = captor;
                p.SeenMode = mode;
                if (s != null && s.TargetWeapon != p.SeenTarget)
                {
                    if (s.TargetWeapon != null)
                        sc.Note(TL.Event, $"{p.Tag}: recovery target {s.TargetWeapon.ShortName.Localized()} ({(s.TargetWeapon == s.DroppedWeapon ? "the gun he dropped" : "another gun")}), {Vector3.Distance(p.Player.Position, s.TargetPoint):0} m away");
                    else if (p.SeenTarget != null) sc.Note(TL.Event, $"{p.Tag}: no recovery target now ({s.Phase})");
                    p.SeenTarget = s.TargetWeapon;
                }
            }
        }

        private static void Face(Scenario sc, Participant p)
        {
            if (p.Existing != null || p.Owner2 == null) return;
            Vector3 at = sc.Anchor + Vector3.up * 1.4f;
            if (p.FaceTag == "you" && sc.Observer != null) at = Perception.Eye(sc.Observer);
            else if (!string.IsNullOrEmpty(p.FaceTag) && sc.Find(p.FaceTag)?.Player is Player f) at = Perception.Chest(f);
            try
            {
                p.Owner2.Steering?.LookToPoint(at);
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"Scenario {sc.Title}: {p.Tag} turned towards {(string.IsNullOrEmpty(p.FaceTag) ? "the centre" : p.FaceTag)} (initial look only; the AI turns freely)");
            }
            catch (Exception e) { HandsAreHighPlugin.Report("scenario facing", e); }
        }

        private static void LiftTruce(Scenario sc)
        {
            if (sc.TruceLifted) return;
            sc.TruceLifted = true;
            int n = ScenarioShield.Remove(sc, ShieldKind.Truce);
            int kept = 0;
            if (sc.KeepTruce)
            {
                foreach (Participant a in sc.People)
                    foreach (Participant b in sc.People)
                        if (a != b && a.Player != null && b.Player != null) { ScenarioShield.Add(sc, ShieldKind.Truce, a.Player, b.Player); kept++; }
            }
            else
            {
                foreach (KeyValuePair<string, string> kv in sc.KeptTruces)
                {
                    Participant a = sc.Find(kv.Key), b = sc.Find(kv.Value);
                    if (a?.Player == null || b?.Player == null) continue;
                    ScenarioShield.Add(sc, ShieldKind.Truce, a.Player, b.Player);
                    ScenarioShield.Add(sc, ShieldKind.Truce, b.Player, a.Player);
                    kept += 2;
                }
            }
            if (kept > 0) sc.Overrides.Add($"truce kept between {kept} participant pair(s) (forced by the preset)");
            sc.Note(TL.Info, $"setup truce lifted ({n} pair(s)){(kept > 0 ? $"; {kept} kept by the preset (FORCED)" : "")}");
        }

        internal static void SetObserverShield(Scenario sc, bool on)
        {
            sc.ShieldWanted = on;
            sc.Overrides.RemoveAll(o => o.StartsWith("observer override"));
            if (on)
            {
                sc.Overrides.Add("observer override: participants don't target you");
                foreach (Participant p in sc.People)
                    if (p.Existing == null && p.Player != null && !p.Dead && sc.Observer != null) ScenarioShield.Add(sc, ShieldKind.Observer, p.Player, sc.Observer);
            }
            else ScenarioShield.Remove(sc, ShieldKind.Observer);
            sc.Note(TL.Info, $"observer override {(on ? "ON: participants won't take you as a target" : "OFF: you are a normal target again")}");
        }

        // ---- cleanup -------------------------------------------------------------------------

        internal static void Cleanup(Scenario sc, string why)
        {
            if (!Running.Contains(sc)) return;
            sc.Cancelled = true;
            sc.EndWhy = why;
            sc.Events.Clear();
            int lifted = ScenarioShield.Remove(sc);
            var report = new StringBuilder();
            report.Append($"Scenario {sc.Title} report ({(sc.Mode == ScenarioMode.Forced || sc.Forced.Count > 0 ? "FORCED STATE: not evidence that natural surrender works" : "natural trigger")}), ended after {sc.Since:0} s: {why}");
            report.Append($"\n  params: {sc.Values.Describe()}");
            if (sc.Forced.Count > 0) report.Append($"\n  forced: {string.Join("; ", sc.Forced)}");
            if (sc.Conditions.Count > 0) report.Append($"\n  set up: {string.Join("; ", sc.Conditions)}");
            if (sc.SetupFailed != null) report.Append($"\n  setup incomplete: {sc.SetupFailed}");
            report.Append($"\n  overrides lifted: {lifted} shield/truce pair(s); fire held back by the shield {ScenarioShield.Blocked} time(s)");
            ScenarioShield.Blocked = 0;

            // Items first (who holds what is read from the living), then the bots.
            var seen = new HashSet<Item>();
            foreach (Participant p in sc.People)
            {
                if (p.Existing != null) continue;
                foreach (Item it in p.Items)
                {
                    if (it == null || !seen.Add(it)) continue;
                    report.Append($"\n  item ({p.Tag}'s): {ScenarioOps.CleanItem(sc, it, remove: true)}");
                }
            }
            foreach (Participant p in sc.People)
            {
                if (p.DeathHandler != null && (object)p.Player != null)
                {
                    try { p.Player.OnPlayerDeadOrUnspawn -= p.DeathHandler; }
                    catch (Exception e) { HandsAreHighPlugin.Report("scenario unhook death event", e); }
                    p.DeathHandler = null;
                }
                string what = p.Spawned || p.Existing != null ? ScenarioSpawner.Despawn(p) : (p.Requested ? "spawn still pending: removed on arrival" : "never spawned");
                if (p.Existing == null && p.Player != null && !p.Removed && !p.Dead && Perception.Alive(p.Player))
                    SurrenderRegistry.Discharge(p.Player, "scenario cleanup");
                report.Append($"\n  {p.Tag} ({(p.Player != null ? p.Player.Profile?.Nickname : "-")}): {what}");
                if ((object)p.Player != null) ByPlayer.Remove(p.Player);
            }
            report.Append($"\n  timeline ({sc.Timeline.Count} entries):");
            foreach (TimelineEntry e in sc.Timeline)
                report.Append($"\n    +{e.At:0.0}s [{Scenario.Tag(e.Kind)}] {e.Text}{(e.Repeats > 0 ? $" (x{e.Repeats + 1})" : "")}");
            if (sc.Seed > 0 && Dice.Seeded && Dice.Seed == sc.Seed) Dice.Unseed();
            sc.Stage = Stage.Ended;
            Running.Remove(sc);
            if (Running.Count == 0) Idle();
            HandsAreHighPlugin.Log.LogInfo(report.ToString());
            OrderMenu.Notify($"Scenario {sc.Title} cleaned up: {why}");
        }

        internal static void CleanupAll(string why)
        {
            Scratch.Clear();
            Scratch.AddRange(Running);
            foreach (Scenario sc in Scratch) Cleanup(sc, why);
            Scratch.Clear();
        }

        private static void Idle()
        {
            Active = false;
            ByPlayer.Clear();
            RemoveListener();
            if (Dice.Seeded) Dice.Unseed();
        }

        // A new raid (or back in the menus): the old world is gone; nothing to despawn.
        internal static void ForgetRaid()
        {
            RaidSerial++;
            if (Running.Count == 0 && !Active) { ScenarioUi.Reset(); return; }
            foreach (Scenario sc in Running) { sc.Cancelled = true; sc.Stage = Stage.Ended; }
            HandsAreHighPlugin.Log.LogInfo($"New raid: {Running.Count} debug scenario(s) forgotten (their raid ended)");
            Running.Clear();
            ScenarioShield.Clear();
            Idle();
            ScenarioUi.Reset();
        }

        // Hot reload / plugin teardown: a full cleanup while a raid still runs, else forget.
        internal static void Teardown()
        {
            if (Running.Count > 0 && SurrenderRegistry.InRaid) CleanupAll("plugin teardown");
            ForgetRaid();
            ScenarioShield.Clear();
            RemoveListener();
        }

        // ---- the timeline from our own log lines ----------------------------------------------

        private sealed class Listener : ILogListener
        {
            public void LogEvent(object sender, LogEventArgs e)
            {
                if (!Active || e == null || e.Source != HandsAreHighPlugin.Log || _inListener) return;
                _inListener = true;
                try { OnLog(e.Data as string ?? e.Data?.ToString(), e.Level); }
                catch (Exception ex) { HandsAreHighPlugin.Report("scenario timeline listener", ex); }
                finally { _inListener = false; }
            }

            public void Dispose() { }
        }

        private static void EnsureListener()
        {
            if (_listener != null) return;
            _listener = new Listener();
            BepInEx.Logging.Logger.Listeners.Add(_listener);
            HandsAreHighPlugin.Log.LogInfo("Scenarios: timeline listener attached (our own log lines naming a participant are kept per scenario)");
        }

        private static void RemoveListener()
        {
            if (_listener == null) return;
            BepInEx.Logging.Logger.Listeners.Remove(_listener);
            _listener = null;
            HandsAreHighPlugin.Log?.LogInfo("Scenarios: no scenario running; timeline listener detached, scenario hooks idle");
        }

        private static void OnLog(string msg, LogLevel level)
        {
            if (string.IsNullOrEmpty(msg) || msg.StartsWith("Scenario ") || msg.StartsWith("Profile")) return;
            if (msg.Contains(": fire +")) return;   // per-shot suppression increments (Verbose): the check lines carry the total
            for (int i = 0; i < Running.Count; i++)
            {
                Scenario sc = Running[i];
                if (sc.Stage == Stage.Ended) continue;
                // The line's subject (it starts with his name) first, else anyone it names.
                Participant hit = null, subject = null;
                foreach (Participant p in sc.People)
                {
                    string nick = p.Player != null ? p.Player.Profile?.Nickname : null;
                    if (string.IsNullOrEmpty(nick) || !msg.Contains(nick)) continue;
                    if (hit == null) hit = p;
                    if (msg.StartsWith(nick)) { subject = p; break; }
                }
                if (hit == null) continue;
                if (subject != null && (msg.Contains("pressure ") || msg.Contains("no surrender check")))
                {
                    subject.LastCheck = msg;
                    subject.LastCheckAt = Time.time;
                }
                sc.Note(Classify(msg, level), msg.Length > 400 ? msg.Substring(0, 400) + "..." : msg, fromLog: true);
            }
        }

        private static TL Classify(string msg, LogLevel level)
        {
            string m = msg.ToLowerInvariant();
            if (m.Contains("leak")) return TL.ExecFail;
            if (m.Contains("-> surrenders") || m.Contains("takes an opening") || m.Contains("takes him prisoner") || m.Contains("spares him") ||
                m.Contains("refuses the surrender") || m.Contains("refuses (") || m.Contains("takes over") || m.Contains("decides")) return TL.Choice;
            if (m.Contains("no surrender check") || m.Contains("not eligible") || m.Contains("-> no roll")) return TL.Gate;
            if (m.Contains("-> holds on") || m.Contains("declines") || m.Contains("stays put") || m.Contains("-> stays") || m.Contains("no action")) return TL.NoAct;
            if ((level & (LogLevel.Warning | LogLevel.Error)) != 0 || m.Contains("failed") || m.Contains("refused") || m.Contains("can't ") ||
                m.Contains("couldn't") || m.Contains("stuck") || m.Contains("no route") || m.Contains("didn't complete") || m.Contains("no answer")) return TL.ExecFail;
            return TL.Event;
        }
    }
}
