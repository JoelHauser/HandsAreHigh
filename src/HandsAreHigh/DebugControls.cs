using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // Prototype controls: the surrender key picks the bot closest to your crosshair
    // (within 20 degrees), or failing that the nearest one in range.
    internal sealed class DebugControls : MonoBehaviour
    {
        private void Update()
        {
            SurrenderRegistry.Tick(Time.deltaTime);

            if (Settings.SurrenderKey.Value.IsDown())
                HandsAreHighPlugin.Try("surrender key", SurrenderTarget);
            if (Settings.OpeningKey.Value.IsDown())
                SurrenderRegistry.ForceOpening();
            if (Settings.LeaveKey.Value.IsDown())
                SurrenderRegistry.ForceCaptorLeft();
        }

        private static void SurrenderTarget()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player me = world?.MainPlayer;
            if (me == null) return;

            Vector3 eye = me.Position + Vector3.up * 1.5f;
            Vector3 look = me.LookDirection;
            float maxDist = Settings.MaxDistance.Value;

            Player best = null;
            float bestAngle = 20f;
            Player nearest = null;
            float nearestDist = maxDist;

            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p == me || !p.IsAI || p.AIData?.BotOwner == null) continue;
                if (!Eligible(p)) continue;
                if (SurrenderRegistry.HoldsBot(p)) continue;

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
            SurrenderRegistry.Surrender(target, me);
        }

        // Only the roles whose brains the layer is registered on; anything else would
        // drop its gun but keep fighting.
        private static bool Eligible(Player p)
        {
            WildSpawnType role = p.Profile.Info.Settings.Role;
            if (role == WildSpawnType.assault || role == WildSpawnType.cursedAssault) return true;
            return Settings.IncludePmcs.Value &&
                   (role == WildSpawnType.pmcBEAR || role == WildSpawnType.pmcUSEC);
        }
    }
}
