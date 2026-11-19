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
            if (fc.IsInReloadOperation() || (!captor.IsAI && captor.HandsController.IsInventoryOpen())) return false;

            Vector3 eye = Eye(captor);
            Vector3 to = Chest(bot) - eye;
            if (Vector3.Angle(captor.LookDirection, to) > angle) return false;
            return LineClear(eye, Chest(bot));
        }

        internal static bool CanSee(Player from, Vector3 point) => LineClear(Eye(from), point + Vector3.up * 1.2f);

        // Within this a clear line is enough to know where someone is (sight, hearing, peripheral).
        internal const float CloseNotice = 25f;

        // Does a bot see this man? A clear line, and beyond CloseNotice the game's own sight of him
        // (EnemyInfo.IsVisible, which models distance, light and the vision cone; a blink of under
        // a second is bridged by PersonalLastSeenTime). No EnemyInfo (not an enemy of his): the
        // line alone. Never through walls: the line is always required.
        internal static bool Sees(Player bot, Player other, float dist)
        {
            if (!Alive(other) || !LineClear(Eye(bot), Eye(other))) return false;
            if (dist <= CloseNotice) return true;
            var infos = bot.AIData?.BotOwner?.EnemiesController?.EnemyInfos;
            if (infos == null || !infos.TryGetValue(other, out EnemyInfo e) || e == null) return true;
            return e.IsVisible || Time.time - e.PersonalLastSeenTime < 1f;
        }

        // A bot recognising another man's surrender: the game's own sight of him (EnemyInfo
        // IsVisible) and a clear line to his chest; at arm's length (6 m) the line alone.
        internal static bool Recognises(BotOwner o, Player prisoner)
        {
            Player p = o?.GetPlayer;
            if (p == null || !Alive(prisoner)) return false;
            if (!LineClear(Eye(p), Chest(prisoner))) return false;
            if ((p.Position - prisoner.Position).sqrMagnitude <= 36f) return true;
            var infos = o.EnemiesController?.EnemyInfos;
            if (infos == null || !infos.TryGetValue(prisoner, out EnemyInfo e) || e == null) return false;
            return e.IsVisible || Time.time - e.PersonalLastSeenTime < 1f;
        }

        // Where a bot believes someone is, from what he has perceived: in sight, exactly; else the
        // last place he personally saw him, else his group's last known position (EnemyInfo).
        internal static Vector3 KnownPosition(BotOwner o, Player other, out bool exact)
        {
            exact = false;
            var infos = o?.EnemiesController?.EnemyInfos;
            if (infos != null && infos.TryGetValue(other, out EnemyInfo e) && e != null)
            {
                if (e.IsVisible) { exact = true; return other.Position; }
                if (e.HaveSeenPersonal) return e.PersonalLastPos;
                if (e.HaveSeen) return e.EnemyLastPosition;
            }
            exact = true;   // no record: only reached for a man right in front of him (debug, recapture)
            return other.Position;
        }

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
                float angle = Dice.Range(-75f, 75f);
                float dist = distance * Dice.Range(0.6f, 1.3f);
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

        // A captor's cover: a NavMesh spot within `radius`, hidden from where the fire came from
        // (no clear line from that eye height to his chest height there), with a complete path,
        // preferring one that keeps `keepInSight` (his prisoner) in view, then the nearest.
        // At most PathCandidates path solves; callers spend Perception.TakeHeavy first.
        internal static bool CoverPoint(Player bot, Vector3 threat, float radius, Player keepInSight, out Vector3 point, out bool keepsSight)
        {
            point = bot.Position;
            keepsSight = false;
            Vector3 away = bot.Position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -bot.Transform.forward;
            away.Normalize();
            Vector3 threatEye = threat + Vector3.up * 1.5f;
            float best = float.MinValue;
            bool found = false;
            int tries = Settings.PathCandidates.Value;
            for (int i = 0; i < tries; i++)
            {
                float angle = Dice.Range(-110f, 110f);
                float dist = radius * Dice.Range(0.3f, 1f);
                Vector3 candidate = bot.Position + Quaternion.Euler(0f, angle, 0f) * away * dist;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                if (LineClear(threatEye, hit.position + Vector3.up * 1.2f)) continue;   // still exposed
                bool sight = keepInSight != null && LineClear(hit.position + Vector3.up * 1.6f, Chest(keepInSight));
                float score = (sight ? 50f : 0f) - 2f * Vector3.Distance(hit.position, bot.Position);
                if (score <= best) continue;
                if (!Reachable(bot.Position, hit.position)) continue;
                best = score;
                point = hit.position;
                keepsSight = sight;
                found = true;
            }
            return found;
        }

        // A complete NavMesh path's corners, for BotMover.GoToByWay. (BotOwner.GoToPoint is not
        // used: it routes over the bot's cover-point graph, which failed for most targets in
        // testing, and its default mustHaveWay = true teleports the bot to a cover point when
        // it fails; BotPathFinderCorePoints.GoToPosition.)
        //
        // The game's BotMover doesn't push the body with physics: it walks a point along the
        // path (PositionOnWay) and sets the transform onto the NavMesh under it
        // (BotMover.CastFromPos). The NavMesh is the only wall. So the path must start where
        // the bot really stands: the start may only be snapped StartSnap metres (horizontally,
        // same floor). The old 2 m snap could start the path on the far side of a thin wall or
        // on another floor, and the mover then carried him straight through.
        private const float StartSnap = 0.5f;
        private const float StartSnapHeight = 0.6f;
        private const float EndSnap = 1.5f;
        private const float EndSnapHeight = 1.2f;

        internal static bool PathTo(Vector3 from, Vector3 to, out Vector3[] corners, out string why) =>
            Solve(from, to, true, out corners, out why);

        // NavMeshPath.corners allocates a new array on every read: only paths that will be
        // walked read it; a reachability check only needs the status.
        private static bool Solve(Vector3 from, Vector3 to, bool wantCorners, out Vector3[] corners, out string why)
        {
            corners = null;
            if (!Snap(from, StartSnap, StartSnapHeight, out Vector3 start)) { why = "he isn't on the NavMesh"; return false; }
            if (!Snap(to, EndSnap, EndSnapHeight, out Vector3 end)) { why = "the spot isn't on the NavMesh"; return false; }
            if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, Path) || Path.status != NavMeshPathStatus.PathComplete)
            {
                why = "no complete path";
                return false;
            }
            why = null;
            if (!wantCorners) return true;
            corners = Path.corners;
            if (corners == null || corners.Length == 0) { why = "empty path"; return false; }
            return true;
        }

        internal static bool Reachable(Vector3 from, Vector3 to) => Solve(from, to, false, out _, out _);

        // Heavy searches (a retreat or cover point: up to PathCandidates path solves, a 60 m
        // weapon search) are limited to HeavyPerFrame per frame across all bots, so several
        // bots fleeing at once (a witnessed shooting) spread over a few frames instead of one.
        private const int HeavyPerFrame = 2;
        private static int _heavyFrame = -1, _heavyUsed;
        internal static bool TakeHeavy()
        {
            int f = Time.frameCount;
            if (f != _heavyFrame) { _heavyFrame = f; _heavyUsed = 0; }
            if (_heavyUsed >= HeavyPerFrame) return false;
            _heavyUsed++;
            return true;
        }

        // Nearest NavMesh point within `radius` horizontally and `height` vertically.
        private static bool Snap(Vector3 p, float radius, float height, out Vector3 snapped)
        {
            snapped = p;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, Mathf.Max(radius, height), NavMesh.AllAreas)) return false;
            Vector3 d = hit.position - p;
            if (Mathf.Abs(d.y) > height) return false;
            d.y = 0f;
            if (d.magnitude > radius) return false;
            snapped = hit.position;
            return true;
        }
    }
}
