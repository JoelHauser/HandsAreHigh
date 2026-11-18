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
                HandsAreHighPlugin.Try("surrender key", () => DebugSurrender(me));
            if (Settings.OpeningKey.Value.IsDown())
                SurrenderRegistry.ForceOpening();
            if (Settings.LeaveKey.Value.IsDown())
                SurrenderRegistry.ForceCaptorLeft();
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

        // The debug key picks the bot closest to your crosshair (within 20 degrees), or
        // failing that the nearest one in range.
        private static void DebugSurrender(Player me)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null || me == null) return;

            Vector3 eye = me.Position + Vector3.up * 1.5f;
            Vector3 look = me.LookDirection;
            float maxDist = Settings.DebugKeyDistance.Value;

            Player best = null;
            float bestAngle = 20f;
            Player nearest = null;
            float nearestDist = maxDist;

            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p == me || !p.IsAI || p.AIData?.BotOwner == null) continue;
                if (!NaturalSurrender.Eligible(p) || SurrenderRegistry.HoldsBot(p)) continue;

                Vector3 to = p.Position + Vector3.up * 1.2f - eye;
                float dist = to.magnitude;
                if (dist > maxDist) continue;

                float angle = Vector3.Angle(look, to);
                if (angle < bestAngle) { bestAngle = angle; best = p; }
                if (dist < nearestDist) { nearestDist = dist; nearest = p; }
            }

            Player target = best ?? nearest;
            if (target == null)
            {
                HandsAreHighPlugin.Log.LogInfo("Surrender key: no eligible bot in range");
                return;
            }
            SurrenderRegistry.Surrender(target, me, "debug key");
        }
    }
}
