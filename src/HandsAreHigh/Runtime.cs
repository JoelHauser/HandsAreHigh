using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // The mod's per-frame driver: registry upkeep, the surrender checks, the order menu,
    // debug keys and the debug overlay. One instance, owned by the plugin (torn down with it).
    internal sealed class Runtime : MonoBehaviour
    {
        private readonly OrderMenu _menu = new OrderMenu();
        private GUIStyle _overlay;

        private void Update()
        {
            float dt = Time.deltaTime;
            HandsAreHighPlugin.Try("registry", () => SurrenderRegistry.Tick(dt));
            HandsAreHighPlugin.Try("surrender checks", NaturalSurrender.Tick);

            Player me = Singleton<GameWorld>.Instance?.MainPlayer;
            HandsAreHighPlugin.Try("order menu", () => _menu.Update(me));

            if (!Settings.DebugKeys.Value) return;
            if (Settings.SurrenderKey.Value.IsDown())
                HandsAreHighPlugin.Try("surrender key", () => DebugCommand("surrender", me));
            if (Settings.OpeningKey.Value.IsDown())
                HandsAreHighPlugin.Try("opening key", () => DebugCommand("opening", me));
            if (Settings.LeaveKey.Value.IsDown())
                HandsAreHighPlugin.Try("leave key", () => DebugCommand("leave", me));
        }

        // Every debug key acts on exactly one bot: the one under the crosshair, resolved once
        // here and passed on by reference. No bot under it: nothing happens, and it says so.
        private static void DebugCommand(string command, Player me)
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
                    SurrenderRegistry.Surrender(target, me, "debug key");
                    outcome = SurrenderRegistry.HoldsBot(target) ? "surrenders" : "didn't surrender (mod disabled?)";
                }
            }
            else if (!SurrenderRegistry.TryGet(target, out SurrenderState s))
            {
                outcome = "isn't surrendered";
            }
            else
            {
                outcome = command == "opening" ? SurrenderRegistry.DebugOpening(s) : SurrenderRegistry.DebugLeave(s);
            }

            string who = target != null ? target.Profile?.Nickname : null;
            HandsAreHighPlugin.Log.LogInfo($"Debug {command}: target {Targeting.Describe(target)}: {outcome}");
            OrderMenu.Notify(who == null ? $"Debug {command}: {outcome}" : $"Debug {command}: {who} {outcome}");
        }

        private void OnGUI()
        {
            if (Singleton<GameWorld>.Instance == null) return;
            _menu.OnGUI();
            if (!Settings.Overlay.Value || !SurrenderRegistry.Any) return;

            if (_overlay == null) _overlay = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13 };
            var sb = new System.Text.StringBuilder("Hands Are High\n");
            Player me = Singleton<GameWorld>.Instance.MainPlayer;
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Bot == null) continue;
                float d = me != null ? Vector3.Distance(me.Position, s.Bot.Position) : 0f;
                sb.Append($"{s.Name}: {s.Phase}{(s.Suspended ? " (asleep)" : "")}, order {s.Order}, nerve {s.Nerve:0.00}, arms {s.Weight:0.00}, {d:0} m");
                if (s.TargetWeapon != null) sb.Append($", going for {s.TargetWeapon.ShortName.Localized()}");
                sb.Append('\n');
            }
            GUI.Box(new Rect(10f, 120f, 560f, 24f + 18f * CountLines(sb)), sb.ToString(), _overlay);
        }

        private static int CountLines(System.Text.StringBuilder sb)
        {
            int n = 0;
            for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') n++;
            return n;
        }

    }
}
