using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace HandsAreHigh
{
    // What the bot can tell about his captor and his surroundings.
    internal static class Perception
    {
        internal static Vector3 Eye(Player p)
        {
            Transform head = p.PlayerBones?.Head?.Original;
            return head != null ? head.position : p.Position + Vector3.up * 1.5f;
        }

        internal static Vector3 Chest(Player p) => p.Position + Vector3.up * 1.2f;

        internal static bool LineClear(Vector3 from, Vector3 to) =>
            !Physics.Linecast(from, to, LayersMaskController.HighPolyWithTerrainMask);

        // The captor is "covering" him when he has a gun up, isn't reloading or in his
        // inventory, is looking roughly at him, and has a clear line to him. Anything
        // else is an opening.
        internal static bool Covering(Player captor, Player bot)
        {
            if (captor == null || captor.HealthController == null || !captor.HealthController.IsAlive) return false;
            if (!(captor.HandsController is Player.FirearmController fc)) return false;
            if (fc.IsInReloadOperation() || captor.HandsController.IsInventoryOpen()) return false;

            Vector3 eye = Eye(captor);
            Vector3 to = Chest(bot) - eye;
            if (Vector3.Angle(captor.LookDirection, to) > Settings.WatchAngle.Value) return false;
            return LineClear(eye, Chest(bot));
        }

        // A reachable point roughly away from the threat, preferring ones the threat
        // can't see.
        internal static bool RetreatPoint(Player bot, Vector3 threat, float distance, out Vector3 point)
        {
            point = bot.Position;
            Vector3 away = bot.Position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -bot.Transform.forward;
            away.Normalize();

            Vector3 threatEye = threat + Vector3.up * 1.5f;
            float bestScore = float.MinValue;
            bool found = false;

            for (int i = 0; i < 18; i++)
            {
                float angle = Random.Range(-75f, 75f);
                float dist = distance * Random.Range(0.6f, 1.3f);
                Vector3 candidate = bot.Position + Quaternion.Euler(0f, angle, 0f) * away * dist;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;

                bool hidden = !LineClear(threatEye, hit.position + Vector3.up * 1.2f);
                float score = (hidden ? 100f : 0f) + Vector3.Distance(hit.position, threat) - Mathf.Abs(angle) * 0.1f;
                if (score > bestScore) { bestScore = score; point = hit.position; found = true; }
            }
            return found;
        }
    }
}
