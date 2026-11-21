using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // The mod's per-frame driver: registry upkeep, the surrender checks, the order menu and
    // debug keys. One instance, owned by the plugin (torn down with it). Outside a raid (menus,
    // hideout) it does nothing but the profiler's frame count.
    internal sealed class Runtime : MonoBehaviour
    {
        internal static readonly OrderMenu Menu = new OrderMenu();
        private RuntimeGui _gui;

        private void Awake() => _gui = GetComponent<RuntimeGui>();

        // The GUI component only runs while there's something to draw. (A disabled component
        // gets no Update/LateUpdate, so this always-on one decides.)
        private void ToggleGui(bool inRaid)
        {
            if (_gui == null) _gui = GetComponent<RuntimeGui>();
            if (_gui == null) return;
            bool need = inRaid && (SurrenderRegistry.Any || OrderMenu.HasNote || ScenarioUi.NeedsGui);
            if (_gui.enabled != need) _gui.enabled = need;
        }

        private void Update()
        {
            Perf.Frame();
            // Latches end on key release even if nothing else runs; ownership lapses by itself.
            InputGuard.Tick();
            bool inRaid = SurrenderRegistry.InRaid;
            ToggleGui(inRaid);
            if (!inRaid)
            {
                if (Menu.IsOpen || InputGuard.Engaged) Menu.Reset();
                if (ScenarioUi.Open) ScenarioUi.Reset();
                SurrenderRegistry.EnsureRaid();
                return;
            }

            float dt = Time.deltaTime;
            long t0 = Perf.Begin();
            try { SurrenderRegistry.Tick(dt); } catch (Exception e) { HandsAreHighPlugin.Report("registry", e); }
            Perf.End(Perf.Sec.Registry, t0);

            t0 = Perf.Begin();
            try { NaturalSurrender.Tick(); SurrenderStats.Tick(); } catch (Exception e) { HandsAreHighPlugin.Report("surrender checks", e); }
            Perf.End(Perf.Sec.Natural, t0);
            // Bot captors (each every 0.25 s) and one prisoner's observer scan per frame.
            t0 = Perf.Begin();
            try { Captors.Tick(); } catch (Exception e) { HandsAreHighPlugin.Report("captors", e); }
            Perf.End(Perf.Sec.Captors, t0);
            t0 = Perf.Begin();
            try { Restraint.Tick(); } catch (Exception e) { HandsAreHighPlugin.Report("restraint", e); }
            Perf.End(Perf.Sec.Restraint, t0);
            // Captors' queued lines and the audio checks after any line (both empty almost always).
            t0 = Perf.Begin();
            try { CaptorVoice.Tick(); Voice.TickChecks(); } catch (Exception e) { HandsAreHighPlugin.Report("captor voice", e); }
            Perf.End(Perf.Sec.Voice, t0);

            Player me = Singleton<GameWorld>.Instance.MainPlayer;
            // Debug scenarios: nothing at all unless the tools are on or one is still running.
            if (Settings.ScenariosEnabled.Value || Scenarios.Any)
            {
                t0 = Perf.Begin();
                try { Scenarios.Tick(me); } catch (Exception e) { HandsAreHighPlugin.Report("scenarios", e); }
                Perf.End(Perf.Sec.Scenarios, t0);
            }
            t0 = Perf.Begin();
            if (!ScenarioUi.Open)
            {
                try { Menu.Update(me); } catch (Exception e) { HandsAreHighPlugin.Report("order menu", e); }
            }
            Perf.End(Perf.Sec.Menu, t0);

            // Gameplay, not debug: the optional Shout Stop key (unbound by default).
            if (Settings.DemandEnabled.Value && Settings.DemandKey.Value.IsDown())
                HandsAreHighPlugin.Try("stop key", () => StopDemand.Shout(me));

            if (!Settings.DebugKeys.Value) return;
            if (Settings.SurrenderKey.Value.IsDown()) DebugCommand("surrender", me);
            if (Settings.OpeningKey.Value.IsDown()) DebugCommand("opening", me);
            if (Settings.LeaveKey.Value.IsDown()) DebugCommand("leave", me);
            if (Settings.ForceRunKey.Value.IsDown()) DebugCommand("force run", me);
            if (Settings.ForceGrabKey.Value.IsDown()) DebugCommand("force grab", me);
            if (Settings.SurrenderToBotKey.Value.IsDown()) DebugCommand("surrender to his enemy", me);
            if (Settings.KneelKey.Value.IsDown()) DebugCommand("kneel", me);
            if (Settings.StandKey.Value.IsDown()) DebugCommand("stand", me);
            if (Settings.KneelLoopKey.Value.IsDown()) DebugCommand("kneel loop", me);
            if (Settings.KneelInterruptKey.Value.IsDown()) DebugCommand("kneel interrupt", me);
            if (Settings.KneelUrgentKey.Value.IsDown()) DebugCommand("kneel urgent rise", me);
            if (Settings.HandsOnHeadKey.Value.IsDown()) DebugCommand("hands", me);
            if (Settings.TurnKey.Value.IsDown()) DebugCommand("turn around", me);
            if (Settings.KillKey.Value.IsDown()) DebugCommand("captor decides to kill", me);
            if (Settings.IgnoreKey.Value.IsDown()) DebugCommand("captor ignores him", me);   // 0.28.0
        }

        // Every debug key acts on exactly one bot: the one under the crosshair, resolved once
        // here and passed on by reference. No bot under it: nothing happens, and it says so.
        private static void DebugCommand(string command, Player me)
        {
            try { DebugCommandInner(command, me); }
            catch (Exception e) { HandsAreHighPlugin.Report(command + " key", e); }
        }

        private static void DebugCommandInner(string command, Player me)
        {
            if (me == null || Singleton<GameWorld>.Instance == null) return;
            Player target = Targeting.BotUnderCrosshair(me, Settings.DebugKeyDistance.Value);
            string outcome;

            if (target == null)
            {
                outcome = "no bot under your crosshair";
            }
            else if (command == "surrender")
            {
                if (!NaturalSurrender.Eligible(target))
                    outcome = $"not a Scav or PMC ({target.Profile?.Info?.Settings?.Role}), can't surrender";
                else if (SurrenderRegistry.TryGet(target, out SurrenderState held) && held.Phase == Phase.HandsUp)
                    outcome = "already has his hands up";
                else
                {
                    SurrenderRegistry.Surrender(target, me, "debug key", source: SurrenderSource.DebugKey);
                    outcome = SurrenderRegistry.HoldsBot(target) ? "surrenders" : "didn't surrender (mod disabled?)";
                }
            }
            else if (command == "surrender to his enemy")
            {
                Player foe = Perception.PlayerOf(target.AIData?.BotOwner?.Memory?.GoalEnemy?.Person);
                if (!NaturalSurrender.Eligible(target)) outcome = "not a Scav or PMC, can't surrender";
                else if (SurrenderRegistry.HoldsBot(target)) outcome = "already surrendered";
                else if (foe == null || !foe.IsAI || !Perception.Alive(foe)) outcome = "isn't fighting a bot right now";
                else if (SurrenderRegistry.HoldsBot(foe)) outcome = $"his enemy {foe.Profile?.Nickname} is a prisoner himself";
                else
                {
                    // Towards where he knows his enemy to be, as a natural surrender would.
                    SurrenderRegistry.Surrender(target, foe, "debug key: to his enemy",
                        Perception.KnownPosition(target.AIData?.BotOwner, foe, out _), source: SurrenderSource.DebugKey);
                    outcome = SurrenderRegistry.HoldsBot(target) ? $"surrenders to {foe.Profile?.Nickname} (see the log for his decision)" : "didn't surrender";
                }
            }
            else if (command == "captor decides to kill")
            {
                // 0.18.0: the captor under the crosshair, or the captor of the prisoner under it. The
                // lethal decision only; what follows is the real path (Judgement.TickKills logs it).
                Player captor = Captors.TryGet(target, out _) ? target
                              : SurrenderRegistry.TryGet(target, out SurrenderState held) && held.Custody != null ? held.Custody.Bot : null;
                outcome = captor == null ? "isn't a bot captor and isn't held by one"
                        : (Judgement.ForceKillNow(captor) is string no ? $"{Restraint.Name(captor)} {no}" : $"{Restraint.Name(captor)} decides to kill his prisoner now (watch the log)");
            }
            else if (!SurrenderRegistry.TryGet(target, out SurrenderState s))
            {
                outcome = "isn't surrendered";
            }
            else
            {
                switch (command)
                {
                    case "opening": outcome = SurrenderRegistry.DebugOpening(s); break;
                    case "force run": outcome = SurrenderRegistry.DebugForceRun(s); break;
                    case "force grab": outcome = SurrenderRegistry.DebugForceGrab(s); break;
                    case "kneel": outcome = Kneel.DebugKneel(s, me); break;
                    case "stand": outcome = Kneel.DebugStand(s, me); break;
                    case "kneel loop": outcome = Kneel.DebugLoop(s, me); break;
                    case "kneel interrupt": outcome = Kneel.DebugInterrupt(s, me); break;
                    case "kneel urgent rise": outcome = Kneel.DebugUrgent(s, me); break;
                    case "hands": outcome = Kneel.DebugOrder(s, OrderMenu.Resolve(s, OrderKind.HandsOnHead), me); break;
                    case "turn around": outcome = Kneel.DebugOrder(s, OrderKind.TurnAround, me); break;
                    case "captor ignores him": outcome = Captors.ForceIgnore(s) is string no ? no : $"{Restraint.Name(s.Captor)} ignores his surrender (watch the log)"; break;
                    default: outcome = SurrenderRegistry.DebugLeave(s); break;
                }
            }

            string who = target != null ? target.Profile?.Nickname : null;
            HandsAreHighPlugin.Log.LogInfo($"Debug {command}: target {Targeting.Describe(target)}: {outcome}");
            OrderMenu.Notify(who == null ? $"Debug {command}: {outcome}" : $"Debug {command}: {who} {outcome}");
        }
    }

    // IMGUI drawing (order menu, hints, notes, debug overlay). Unity calls OnGUI on every
    // enabled MonoBehaviour that has one, several times a frame, whether it draws or not; so
    // this component is only enabled while there is something to draw (Runtime turns it on
    // and off from LateUpdate below). useGUILayout off: we only use GUI.*, no layout pass.
    internal sealed class RuntimeGui : MonoBehaviour
    {
        private GUIStyle _overlay;
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(512);

        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            long t0 = Perf.Begin();
            try { Draw(); } catch (Exception e) { HandsAreHighPlugin.Report("gui", e); }
            Perf.End(Perf.Sec.Gui, t0);
        }

        private void Draw()
        {
            Runtime.Menu.OnGUI();
            if (ScenarioUi.NeedsGui) ScenarioUi.OnGUI();
            if (!Settings.Overlay.Value || !SurrenderRegistry.Any) return;

            if (_overlay == null) _overlay = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13 };
            if (Event.current.type != EventType.Repaint) return;
            _sb.Length = 0;
            _sb.Append("Hands Are High\n");
            int lines = 1;
            Player me = Singleton<GameWorld>.Instance?.MainPlayer;
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Bot == null) continue;
                float d = me != null ? Vector3.Distance(me.Position, s.Bot.Position) : 0f;
                _sb.Append($"{(Scenarios.Watching(s.Bot) ? "[scenario] " : "")}{s.Name}: {s.Phase}{(s.Suspended ? " (asleep)" : "")}, order {s.Order}, nerve {s.Nerve:0.00}, arms {s.Weight:0.00}, {d:0} m");
                if (s.TargetWeapon != null) _sb.Append($", going for {s.TargetWeapon.ShortName.Localized()}");
                if (s.Phase == Phase.HandsUp)
                {
                    if (!s.LastCovered) _sb.Append($", opening: {s.OpeningKind}");
                    if (Time.time < s.ComplyUntil) _sb.Append($", complying {s.ComplyUntil - Time.time:0} s");
                    if (s.Gesture != GestureKind.None) _sb.Append($", {s.Gesture}");
                    else if (s.WigglePendingUntil >= 0f || s.DefiantPendingUntil >= 0f) _sb.Append(", gesture pending");
                }
                if (Kneel.Describe(s) is string kneel) _sb.Append($", {kneel}");
                if (Presence.Describe(s) is string look) _sb.Append($", {look}");
                if (s.PendingVoice != null) _sb.Append($", to say {s.PendingVoice}");
                _sb.Append('\n');
                lines++;
            }
            GUI.Box(new Rect(10f, 120f, 640f, 24f + 18f * lines), _sb.ToString(), _overlay);
            if (Settings.KneelGizmos.Value) Kneel.DrawGizmos();
        }
    }
}
