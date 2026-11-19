using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace HandsAreHigh
{
    // What the bot can tell about the people around him and his surroundings.
    internal static class Perception
    {
        private static readonly NavMeshPath Path = new NavMeshPath();

        internal static Vector3 Eye(Player p)
        {
            Transform head = p.PlayerBones?.Head?.Original;
            return head != null ? head.position : p.Position + Vector3.up * 1.5f;
        }

        internal static Vector3 Chest(Player p) => p.Position + Vector3.up * 1.2f;

        internal static bool LineClear(Vector3 from, Vector3 to) =>
            !Physics.Linecast(from, to, LayersMaskController.HighPolyWithTerrainMask);

        internal static bool Alive(Player p) =>
            p != null && p.HealthController != null && p.HealthController.IsAlive;

        // The player is "covering" him when he has a gun up, isn't reloading or in his
        // inventory, is looking at him within `angle`, and has a clear line to him.
        // Anything else is an opening.
        internal static bool Covering(Player captor, Player bot, float angle)
        {
            if (!Alive(captor)) return false;
            if (!(captor.HandsController is Player.FirearmController fc)) return false;
            if (fc.IsInReloadOperation() || captor.HandsController.IsInventoryOpen()) return false;

            Vector3 eye = Eye(captor);
            Vector3 to = Chest(bot) - eye;
            if (Vector3.Angle(captor.LookDirection, to) > angle) return false;
            return LineClear(eye, Chest(bot));
        }

        internal static bool CanSee(Player from, Vector3 point) => LineClear(Eye(from), point + Vector3.up * 1.2f);

        // A spot roughly `distance` away from the threat with a complete path to it,
        // preferring ones the threat can't see. hiddenOnly: only spots out of its sight.
        internal static bool RetreatPoint(Player bot, Vector3 threat, float distance, bool hiddenOnly, out Vector3 point)
        {
            point = bot.Position;
            Vector3 away = bot.Position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -bot.Transform.forward;
            away.Normalize();

            Vector3 threatEye = threat + Vector3.up * 1.5f;
            float bestScore = float.MinValue;
            bool found = false;
            int tries = Settings.PathCandidates.Value;

            for (int i = 0; i < tries; i++)
            {
                float angle = Random.Range(-75f, 75f);
                float dist = distance * Random.Range(0.6f, 1.3f);
                Vector3 candidate = bot.Position + Quaternion.Euler(0f, angle, 0f) * away * dist;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;

                bool hidden = !LineClear(threatEye, hit.position + Vector3.up * 1.2f);
                if (hiddenOnly && !hidden) continue;
                float score = (hidden ? 100f : 0f) + Vector3.Distance(hit.position, threat) - Mathf.Abs(angle) * 0.1f;
                if (score <= bestScore) continue;
                if (!Reachable(bot.Position, hit.position)) continue;

                bestScore = score;
                point = hit.position;
                found = true;
            }
            return found;
        }

        // A complete NavMesh path's corners, for BotMover.GoToByWay. (BotOwner.GoToPoint is not
        // used: it routes over the bot's cover-point graph, which failed for most targets in
        // testing, and its default mustHaveWay = true teleports the bot to a cover point when
        // it fails; BotPathFinderCorePoints.GoToPosition.)
        internal static bool PathTo(Vector3 from, Vector3 to, out Vector3[] corners)
        {
            corners = null;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit end, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, Path) ||
                Path.status != NavMeshPathStatus.PathComplete) return false;
            corners = Path.corners;
            return corners != null && corners.Length > 0;
        }

        internal static bool Reachable(Vector3 from, Vector3 to)
        {
            // Both ends snapped to the NavMesh: a gun on a table or a body's root sits off it.
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit end, 2f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, Path) &&
                   Path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
