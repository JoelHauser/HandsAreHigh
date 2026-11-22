using System.Collections.Generic;
using System.Text;
using EFT;
using UnityEngine;

namespace Mercy
{
    // The scenario menu (keyboard only) and the scenario overlay.
    //
    // Input: while the menu is open it owns input through InputGuard (the order menu's guard):
    // every hotbar command and the mouse wheel are kept from the game and keys pressed while it
    // was open stay latched until released, so choosing an option can't use a hotbar item.
    // The navigation keys default to the arrow keys and Enter (section 7); the order menu
    // doesn't open while this one is open.
    // Drawing: IMGUI on Repaint only; the texts are rebuilt on input and at most 4x a second.
    internal static class ScenarioUi
    {
        private enum Page { Main, Events }
        private enum Row { Preset, Mode, Param, Launch, Repeat, Events, Inspect, Detail, CleanNewest, CleanAll, Close, EvScenario, EvTarget, EvEvent, EvFire, EvShield, EvBack }

        internal static bool Open;
        private static Page _page;
        private static int _sel;
        private static int _preset;
        private static int _evScenario, _evTarget, _evEvent;
        private static bool _full = true;
        private static string _status;
        private static ScenarioPreset _lastPreset;
        private static ParamValues _lastValues;
        private static ScenarioMode _lastMode;

        private static readonly List<Row> Rows = new List<Row>(32);
        private static readonly List<ParamDef> RowParams = new List<ParamDef>(32);
        private static readonly StringBuilder Sb = new StringBuilder(4096);
        private static string _menuText, _overlayText;
        private static float _menuAt, _overlayAt;
        private static bool _dirty = true;
        private static GUIStyle _style;

        internal static bool NeedsGui => Open || (Settings.ScenarioOverlay.Value && Scenarios.Any);

        internal static void Reset()
        {
            if (Open) InputGuard.Release();
            Open = false;
            _overlayText = null;
            _dirty = true;
        }

        private static bool Pressed(BepInEx.Configuration.KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None || !Input.GetKeyDown(k.MainKey)) return false;
            foreach (KeyCode m in k.Modifiers)
                if (!Input.GetKey(m)) return false;
            return true;
        }

        // ---- input -------------------------------------------------------------------------

        internal static void Update(Player me)
        {
            if (!Settings.ScenariosEnabled.Value) { if (Open) Close(); return; }
            if (Pressed(Settings.ScenarioKey.Value))
            {
                if (Open) Close();
                else
                {
                    Open = true;
                    _dirty = true;
                    MercyPlugin.Log.LogInfo($"Scenario menu opened ({Scenarios.List.Count} scenario(s) running); hotbar and wheel held back while open");
                    Runtime.Menu.Reset();          // never both menus
                    InputGuard.ReadBindings();
                }
            }
            if (!Open) return;
            if (Cursor.visible) return;           // a game screen (inventory, pause) is over it
            InputGuard.Claim();

            BuildRows();
            if (Pressed(Settings.ScenarioUp.Value)) { _sel = (_sel - 1 + Rows.Count) % Rows.Count; _dirty = true; }
            if (Pressed(Settings.ScenarioDown.Value)) { _sel = (_sel + 1) % Rows.Count; _dirty = true; }
            if (_sel >= Rows.Count) _sel = 0;
            int dir = Pressed(Settings.ScenarioMore.Value) ? 1 : Pressed(Settings.ScenarioLess.Value) ? -1 : 0;
            bool go = Pressed(Settings.ScenarioConfirm.Value);
            if (dir != 0 || go) { Act(me, dir, go); _dirty = true; }
        }

        private static void Close()
        {
            Open = false;
            MercyPlugin.Log.LogInfo("Scenario menu closed");
            InputGuard.Release();
        }

        private static ScenarioPreset Current => ScenarioPresets.All[Mathf.Clamp(_preset, 0, ScenarioPresets.All.Count - 1)];

        private static void BuildRows()
        {
            Rows.Clear();
            RowParams.Clear();
            if (_page == Page.Main)
            {
                Add(Row.Preset);
                Add(Row.Mode);
                foreach (ParamDef d in Current.Params) { Rows.Add(Row.Param); RowParams.Add(d); }
                Add(Row.Launch);
                if (_lastPreset != null) Add(Row.Repeat);
                if (Scenarios.Any) { Add(Row.Events); Add(Row.Inspect); Add(Row.CleanNewest); Add(Row.CleanAll); }
                Add(Row.Detail);
                Add(Row.Close);
            }
            else
            {
                Add(Row.EvScenario); Add(Row.EvTarget); Add(Row.EvEvent); Add(Row.EvFire); Add(Row.EvShield); Add(Row.EvBack);
            }
        }

        private static void Add(Row r) { Rows.Add(r); RowParams.Add(null); }

        private static Scenario EvScenario
        {
            get
            {
                List<Scenario> l = Scenarios.List;
                if (l.Count == 0) return null;
                _evScenario = Mathf.Clamp(_evScenario, 0, l.Count - 1);
                return l[_evScenario];
            }
        }

        private static Participant EvTarget
        {
            get
            {
                Scenario sc = EvScenario;
                if (sc == null || sc.People.Count == 0) return null;
                _evTarget = Mathf.Clamp(_evTarget, 0, sc.People.Count - 1);
                return sc.People[_evTarget];
            }
        }

        private static int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;

        private static void Act(Player me, int dir, bool go)
        {
            Row r = Rows[_sel];
            ScenarioPreset p = Current;
            switch (r)
            {
                case Row.Preset:
                    if (dir != 0) { _preset = Wrap(_preset + dir, ScenarioPresets.All.Count); _sel = 0; }
                    break;
                case Row.Mode:
                    if ((dir != 0 || go) && p.Natural && p.Forced) p.Mode = p.Mode == ScenarioMode.Natural ? ScenarioMode.Forced : ScenarioMode.Natural;
                    break;
                case Row.Param:
                    p.Values.Step(RowParams[_sel], dir != 0 ? dir : go ? 1 : 0);
                    break;
                case Row.Launch:
                    if (go) Launch(me, p, p.Values, p.Mode);
                    break;
                case Row.Repeat:
                    if (go && _lastPreset != null) Launch(me, _lastPreset, _lastValues, _lastMode);
                    break;
                case Row.Events:
                    if (go) { _page = Page.Events; _sel = 0; }
                    break;
                case Row.Inspect:
                    if (go) { MercyPlugin.Log.LogInfo("Scenario inspect:\n" + BuildOverlay(full: true, timeline: 300)); _status = "full state written to the log"; }
                    break;
                case Row.Detail:
                    if (go || dir != 0) _full = !_full;
                    break;
                case Row.CleanNewest:
                    if (go && Scenarios.Any) Scenarios.Cleanup(Scenarios.List[Scenarios.List.Count - 1], "cleaned up from the menu");
                    if (go) _status = "cleaned up (report in the log)";
                    break;
                case Row.CleanAll:
                    if (go) { Scenarios.CleanupAll("cleaned up from the menu"); _status = "all cleaned up (reports in the log)"; }
                    break;
                case Row.Close:
                    if (go) Close();
                    break;
                case Row.EvScenario:
                    if (dir != 0) { _evScenario = Wrap(_evScenario + dir, Scenarios.List.Count); _evTarget = 0; }
                    break;
                case Row.EvTarget:
                    if (dir != 0 && EvScenario != null) _evTarget = Wrap(_evTarget + dir, EvScenario.People.Count);
                    break;
                case Row.EvEvent:
                    if (dir != 0) _evEvent = Wrap(_evEvent + dir, ScenarioEvents.All.Length);
                    break;
                case Row.EvFire:
                    if (go)
                    {
                        Scenario sc = EvScenario;
                        if (sc == null) { _status = "no scenario running"; break; }
                        MercyPlugin.Log.LogInfo($"Scenario {sc.Title}: event from the menu: {ScenarioEvents.Label(ScenarioEvents.All[_evEvent])} on {EvTarget?.Tag ?? "nobody"}");
                        _status = ScenarioEvents.Run(sc, ScenarioEvents.All[_evEvent], EvTarget);
                        OrderMenu.Notify(_status);
                    }
                    break;
                case Row.EvShield:
                    if ((go || dir != 0) && EvScenario != null) Scenarios.SetObserverShield(EvScenario, !EvScenario.ShieldWanted);
                    break;
                case Row.EvBack:
                    if (go) { _page = Page.Main; _sel = 0; }
                    break;
            }
        }

        private static void Launch(Player me, ScenarioPreset p, ParamValues v, ScenarioMode mode)
        {
            string r;
            try { r = Scenarios.Launch(p, me, v, mode); }
            catch (System.Exception e) { MercyPlugin.Report("scenario launch", e); r = $"launch failed: {e.Message}"; }
            _status = r;
            MercyPlugin.Log.LogInfo($"Scenario launch ({p.Name}): {r}");
            OrderMenu.Notify(r, r.StartsWith("launched") ? NoteKind.Success : NoteKind.Error);
            if (r.StartsWith("launched")) { _lastPreset = p; _lastValues = v.Clone(); _lastMode = mode; }
        }

        // ---- drawing -----------------------------------------------------------------------

        internal static void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true, wordWrap = true };
            float now = Time.realtimeSinceStartup;
            if (Open)
            {
                if (_dirty || now - _menuAt > 0.25f) { _menuText = BuildMenu(); _menuAt = now; _dirty = false; }
                Draw(new Rect(10f, 60f, 600f, 0f), _menuText);
            }
            if (Settings.ScenarioOverlay.Value && Scenarios.Any)
            {
                if (_overlayText == null || now - _overlayAt > 0.25f) { _overlayText = BuildOverlay(_full, _full ? 10 : 5); _overlayAt = now; }
                float w = Mathf.Min(720f, Screen.width * 0.45f);
                Draw(new Rect(Screen.width - w - 10f, 60f, w, 0f), _overlayText);
            }
        }

        private static void Draw(Rect r, string text)
        {
            var c = new GUIContent(text);
            r.height = Mathf.Min(Screen.height - r.y - 10f, _style.CalcHeight(c, r.width));
            GUI.Box(r, c, _style);
        }

        private static string BuildMenu()
        {
            BuildRows();
            Sb.Length = 0;
            ScenarioPreset p = Current;
            Sb.Append("<b>Mercy: Debug Scenarios</b>  <color=#999999>(").Append(Settings.ScenarioUp.Value.MainKey).Append('/').Append(Settings.ScenarioDown.Value.MainKey)
              .Append(" move, ").Append(Settings.ScenarioLess.Value.MainKey).Append('/').Append(Settings.ScenarioMore.Value.MainKey).Append(" change, ")
              .Append(Settings.ScenarioConfirm.Value.MainKey).Append(" activate)</color>\n");
            if (_page == Page.Main)
            {
                Sb.Append("<color=#C5C3B2>").Append(p.Tests).Append("</color>\n");
                Sb.Append("<color=#999999>Watch: ").Append(p.Watch).Append("\nNeeds: ").Append(p.Prereq).Append("</color>\n");
                string un = p.Unavailable?.Invoke();
                if (un != null) Sb.Append("<color=#C75A4A>Unavailable: ").Append(un).Append("</color>\n");
                if (p.Mode == ScenarioMode.Forced) Sb.Append("<color=#D9A54A>FORCED STATE: starts after a forced transition; never evidence that natural surrender works.</color>\n");
            }
            Sb.Append('\n');
            for (int i = 0; i < Rows.Count; i++)
            {
                bool sel = i == _sel;
                Sb.Append(sel ? "<color=#FFFFFF><b>> " : "<color=#BBBBBB>   ");
                Sb.Append(RowText(i, p));
                Sb.Append(sel ? "</b></color>\n" : "</color>\n");
            }
            if (_status != null) Sb.Append("\n<color=#C5C3B2>").Append(_status).Append("</color>");
            return Sb.ToString();
        }

        private static string RowText(int i, ScenarioPreset p)
        {
            switch (Rows[i])
            {
                case Row.Preset: return $"Scenario: < {p.Name} >  ({_preset + 1}/{ScenarioPresets.All.Count})";
                case Row.Mode: return p.Natural && p.Forced ? $"Mode: < {Mode(p.Mode)} >" : $"Mode: {Mode(p.Mode)} (only)";
                case Row.Param: { ParamDef d = RowParams[i]; return $"{d.Label}: < {p.Values.Text(d)} >"; }
                case Row.Launch: return "[ Launch ]";
                case Row.Repeat: return $"[ Repeat last launch: {_lastPreset.Name} ]";
                case Row.Events: return "[ Events and overrides... ]";
                case Row.Inspect: return "[ Inspect: write full state and timeline to the log ]";
                case Row.Detail: return $"Overlay detail: < {(_full ? "full" : "compact")} >";
                case Row.CleanNewest: return "[ Clean up newest scenario ]";
                case Row.CleanAll: return "[ Clean up all scenarios ]";
                case Row.Close: return "[ Close ]";
                case Row.EvScenario: { Scenario sc = EvScenario; return sc == null ? "Scenario: none running" : $"Scenario: < {sc.Title} >"; }
                case Row.EvTarget:
                {
                    Participant t = EvTarget;
                    return t == null ? "Target: none" : $"Target: < {t.Tag} ({(t.Player != null ? t.Player.Profile?.Nickname : "not spawned")}) >";
                }
                case Row.EvEvent:
                {
                    ScenarioEvent e = ScenarioEvents.All[Mathf.Clamp(_evEvent, 0, ScenarioEvents.All.Length - 1)];
                    string no = EvScenario == null ? "no scenario" : ScenarioEvents.Unavailable(EvScenario, e, EvTarget);
                    return $"Event: < {ScenarioEvents.Label(e)} >{(no == null ? "" : $"  <color=#888888>({no})</color>")}";
                }
                case Row.EvFire: return "[ Fire event on the target ]";
                case Row.EvShield: { Scenario sc = EvScenario; return sc == null ? "Observer override: -" : $"Observer override: < {(sc.ShieldWanted ? "ON" : "off")} >"; }
                default: return "[ Back ]";
            }
        }

        private static string Mode(ScenarioMode m) => m == ScenarioMode.Natural ? "Natural trigger" : "Forced state";

        // ---- overlay -------------------------------------------------------------------------

        internal static string BuildOverlay(bool full, int timeline)
        {
            var sb = new StringBuilder(2048);
            Player me = Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer;
            foreach (Scenario sc in Scenarios.List)
            {
                bool forced = sc.Mode == ScenarioMode.Forced || sc.Forced.Count > 0;
                sb.Append($"<b>Scenario {sc.Title}</b> {(forced ? "<color=#D9A54A>FORCED STATE (not evidence of natural surrender)</color>" : "<color=#8FA86E>natural trigger</color>")}  ");
                sb.Append($"{sc.Stage} {(int)(sc.Since / 60f)}:{(int)sc.Since % 60:00}{(sc.Seed > 0 ? $"  seed {sc.Seed}" : "")}\n");
                if (sc.SetupFailed != null) sc_Append(sb, "#C75A4A", $"SETUP INCOMPLETE: {sc.SetupFailed}");
                foreach (string o in sc.Overrides) sc_Append(sb, "#D9A54A", "Override: " + o);
                if (!sc.TruceLifted && sc.Stage != Stage.Running) sc_Append(sb, "#D9A54A", "Override: setup truce (participants don't fire at each other until setup ends)");
                if (sc.Forced.Count > 0) sc_Append(sb, "#D9A54A", "Forced: " + string.Join("; ", sc.Forced));
                if (full && sc.Conditions.Count > 0) sc_Append(sb, "#999999", "Set up: " + string.Join("; ", sc.Conditions));
                foreach (Participant p in sc.People) Person(sb, sc, p, me, full);
                int from = Mathf.Max(0, sc.Timeline.Count - timeline);
                if (sc.Timeline.Count > 0) sb.Append("<color=#999999>Timeline:</color>\n");
                for (int i = from; i < sc.Timeline.Count; i++)
                {
                    TimelineEntry e = sc.Timeline[i];
                    string t = e.Text.Length > 170 && timeline < 50 ? e.Text.Substring(0, 170) + "..." : e.Text;
                    sb.Append($" <color={KindColour(e.Kind)}>+{e.At:0.0} [{Scenario.Tag(e.Kind)}]</color> {Esc(t)}{(e.Repeats > 0 ? $" (x{e.Repeats + 1})" : "")}\n");
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        private static void sc_Append(StringBuilder sb, string colour, string text) =>
            sb.Append("<color=").Append(colour).Append('>').Append(Esc(text)).Append("</color>\n");

        // Rich text: our angle brackets in log text must not open tags.
        private static string Esc(string s) => s == null ? "" : s.Replace("<", "‹").Replace(">", "›");

        private static string KindColour(TL k)
        {
            switch (k)
            {
                case TL.Forced: case TL.Simulated: return "#D9A54A";
                case TL.ExecFail: return "#C75A4A";
                case TL.Choice: return "#8FA86E";
                case TL.NoAct: case TL.Gate: return "#9A9A9A";
                case TL.Real: return "#7FA7C9";
                default: return "#C5C3B2";
            }
        }

        private static void Person(StringBuilder sb, Scenario sc, Participant p, Player me, bool full)
        {
            Player pl = p.Player;
            sb.Append($"<b>{p.Tag}</b> ");
            if (pl == null) { sb.Append(p.Failed ? $"<color=#C75A4A>spawn failed: {Esc(p.FailWhy)}</color>\n" : "<color=#999999>spawning...</color>\n"); return; }
            if (p.Removed) { sb.Append("removed\n"); return; }
            BotOwner o = p.Owner2;
            sb.Append($"{pl.Profile?.Nickname} ({p.Role}, squad {p.Squad}, group {o?.BotsGroup?.Id}){(p.Existing != null ? " existing raid bot" : "")}");
            if (p.Dead || !Perception.Alive(pl)) { sb.Append(" <color=#C75A4A>dead</color>\n"); return; }
            float d = me != null ? Vector3.Distance(me.Position, pl.Position) : 0f;
            sb.Append($" {d:0} m, hp {Arsenal.HealthFraction(pl):P0}");
            BotHistory h = SurrenderRegistry.HistoryOf(pl);
            sb.Append($", nerve {Settings.EffectiveNerve(h.Nerve):0.00}{(p.Nerve != null ? " (set)" : Settings.NerveOverride.Value ? " (debug override)" : "")}");
            if (full)
            {
                string layer = null;
                try { layer = o?.Brain?.ActiveLayerName(); } catch { }
                sb.Append($", layer {layer ?? "?"}, hands {Arsenal.DescribeHands(pl)}{(o != null && Arsenal.OutOfAmmo(o) ? ", OUT OF AMMO" : "")}");
                if (p.Personality != null) sb.Append($", personality (mod-side) {p.Personality}");
            }
            sb.Append('\n');

            if (Captors.TryGet(pl, out CaptorState cs))
                sb.Append($"   captor of {cs.Prisoner?.Name}: {cs.Mode} {Time.time - cs.ModeSince:0} s{(cs.ModeWhy != null ? $" ({Esc(cs.ModeWhy)})" : "")}, plan {cs.PlanName} step {cs.Step + 1}/{cs.Plan.Count}, lets go in {Mathf.Max(0f, cs.HoldUntil - Time.time):0} s{(Time.time < cs.GlanceUntil ? ", GLANCING AWAY" : "")}\n");

            SurrenderState s = ScenarioOps.StateOf(p);
            if (s != null)
            {
                sb.Append($"   {s.Phase} {Time.time - s.PhaseSince:0} s, captor {Restraint.Name(s.Captor)}{(s.Custody != null ? " (custody)" : "")}, {(s.Accepted ? "accepted" : $"unanswered ({Mathf.Max(0f, s.OfferUntil - Time.time):0} s left)")}");
                if (s.Order != Order.None) sb.Append($", order {s.Order}");
                if (s.Phase == Phase.HandsUp)
                {
                    sb.Append(s.LastCovered ? ", covered" : $", OPENING: {s.OpeningKind} ({s.OpeningStrength:0.00})");
                    if (Time.time < s.DebugOpeningUntil) sb.Append(" [simulated opening]");
                    if (Time.time < s.ComplyUntil) sb.Append($", complying {s.ComplyUntil - Time.time:0} s");
                    if (s.Gesture != GestureKind.None) sb.Append($", gesture {s.Gesture}");
                }
                if (s.DroppedWeapon != null)
                    sb.Append($", dropped {s.DroppedWeapon.ShortName.Localized()} {(s.GunGone ? "(he saw it gone)" : $"{Vector3.Distance(pl.Position, s.DropPosition):0.0} m away")}");
                if (s.TargetWeapon != null)
                    sb.Append($", going for {s.TargetWeapon.ShortName.Localized()} {Vector3.Distance(pl.Position, s.TargetPoint):0.0} m{(s.Moving ? " (moving)" : "")}{(s.Scrambling ? " SCRAMBLE" : "")}");
                else if (s.Moving) sb.Append($", moving, {Vector3.Distance(pl.Position, s.PathTarget):0} m to go");
                if (s.DrawPending) sb.Append(", drawing");
                sb.Append('\n');
                var entries = Restraint.EntriesFor(pl);
                if (entries != null && entries.Count > 0)
                {
                    sb.Append("   restraint: ");
                    bool first = true;
                    foreach (KeyValuePair<Player, RestraintEntry> kv in entries)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        Participant q = Scenarios.ParticipantOf(kv.Key);
                        sb.Append($"{(q != null ? q.Tag : Restraint.Name(kv.Key))} {kv.Value.Kind}");
                        if (full && kv.Value.Why != null) sb.Append($" ({Esc(kv.Value.Why)})");
                    }
                    sb.Append('\n');
                }
            }
            if (p.LastCheck != null && (full || s == null))
            {
                string c = p.LastCheck.Length > 260 ? "..." + p.LastCheck.Substring(p.LastCheck.Length - 260) : p.LastCheck;
                sb.Append($"   <color=#999999>last check {Time.time - p.LastCheckAt:0} s ago: {Esc(c)}</color>\n");
            }
        }
    }
}
