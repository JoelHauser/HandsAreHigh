using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Mercy
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

        // ---- who an IPlayer is (0.14.0) ----------------------------------------------------
        //
        // The game keys a bot's enemies by whatever object registered them: BotOwner implements
        // IPlayer, and BotSpawner.SetBotAsEnemy / BotsGroup's constructor add other BOTS as their
        // BotOwner (group.AddEnemy(bot, ...)), while human players (and BotsGroup's allPlayers
        // pass) are added as their Player. So EnemyInfo.Person and the EnemyInfos keys are a
        // BotOwner for most bot-vs-bot pairs. Every comparison with a Player goes through these;
        // 0.8.0-0.13.1 compared by Player reference only, which made bot-vs-bot surrender
        // impossible and left restraint working only at the trigger.
        internal static Player PlayerOf(IPlayer p)
        {
            if (p == null) return null;
            if (p is Player pl) return pl;
            if (p is BotOwner bo) return bo.GetPlayer;
            return null;
        }

        internal static bool Same(IPlayer a, Player b)
        {
            if (a == null || (object)b == null) return false;
            if ((object)a == b) return true;
            if (a is BotOwner bo) return (object)bo.GetPlayer == b;
            return false;
        }

        // A bot's EnemyInfo about someone, under either key the game may have used.
        internal static EnemyInfo InfoAbout(BotOwner o, Player target)
        {
            var infos = o?.EnemiesController?.EnemyInfos;
            if (infos == null || (object)target == null) return null;
            if (infos.TryGetValue(target, out EnemyInfo info) && info != null) return info;
            BotOwner to = target.AIData?.BotOwner;
            if (to != null && infos.TryGetValue(to, out info) && info != null) return info;
            return null;
        }

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

        // 0.21.0: would a round fired now along his look reach this man first? His body is a box
        // round the line (0.55 m either side plus a little spread per metre, feet to the top of his
        // head), in front of the muzzle, within range. A man further down the line than the
        // shooter's own target, which stands on that line, is not in it: the round stops there.
        // 0.20 tested a flat 6 deg cone at any range (4 m wide at 40 m) and never the target in
        // front: a prisoner or an observing player standing anywhere near the bearing of a fight
        // blocked every trigger press (a scenario's hostile third party never fired a shot in 30 s).
        internal static bool InLineOfFire(Player shooter, Player man, float maxRange)
        {
            if (shooter == null || !Alive(man)) return false;
            return OnLineOfFire(shooter, man, maxRange, Eye(shooter), shooter.LookDirection);
        }

        // 0.28.4: the same test along a given line (a round's own muzzle position and direction,
        // Suppression.ShotPatch): a bot's burst after a press that was clear can sweep onto a man.
        internal static bool OnLineOfFire(Player shooter, Player man, float maxRange, Vector3 eye, Vector3 look)
        {
            if (shooter == null || !Alive(man)) return false;
            if (look.sqrMagnitude < 1e-4f) return false;
            look.Normalize();
            if (!OnLine(eye, look, man, maxRange, out float along)) return false;
            Player goal = null;
            try { goal = PlayerOf(shooter.AIData?.BotOwner?.Memory?.GoalEnemy?.Person); } catch { }
            if (goal != null && goal != man && Alive(goal) && OnLine(eye, look, goal, maxRange, out float goalAlong) && goalAlong + 0.75f < along)
                return false;
            return true;
        }

        private static bool OnLine(Vector3 eye, Vector3 look, Player p, float maxRange, out float along)
        {
            Vector3 feet = p.Position;
            along = Vector3.Dot(feet + Vector3.up - eye, look);
            if (along <= 0.2f || along > maxRange) return false;
            Vector3 closest = eye + look * along;
            Vector3 off = feet - closest;
            float up = -off.y;                       // height of the line above his feet
            if (up < -0.1f || up > 1.95f) return false;
            off.y = 0f;
            // 0.28.4: 0.8 m + 1.5 cm/m (was 0.55 + 1 cm/m). The 0.28.3 raid: a captor fighting past
            // his prisoner put a round 0.9 m from him, then hit him; bots' scatter is wider than that.
            return off.magnitude <= 0.8f + 0.015f * along;
        }

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
            EnemyInfo e = InfoAbout(bot.AIData?.BotOwner, other);
            if (e == null) return true;
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
            // Either key: bot enemies are usually registered as their BotOwner (InfoAbout). With
            // the Player key only (0.9.0-0.13.1) a bot never recognised another bot's surrender
            // beyond 6 m: the offer stayed pending, with nobody holding fire.
            EnemyInfo e = InfoAbout(o, prisoner);
            if (e == null) return false;
            return e.IsVisible || Time.time - e.PersonalLastSeenTime < 1f;
        }

        // Where a bot believes someone is, from what he has perceived: in sight, exactly; else the
        // last place he personally saw him, else his group's last known position (EnemyInfo).
        // 0.29.1: EnemyInfo.HaveSeenPersonal is set on ANY sighting (SetVisible, any visible type), but
        // PersonalLastPos only on a full one (EEnemyPartVisibleType.Visible; EnemyInfo, 0.16.9), so
        // after a partial/sensed sighting it is still Vector3.zero, the map origin (0.29.0 Labs log:
        // "believes him 444-458 m from where he is"). Unset positions are skipped (no map has play
        // space within 1 m of the origin that this could misjudge).
        internal static bool PositionSet(Vector3 p) => p.sqrMagnitude > 1f;

        internal static Vector3 KnownPosition(BotOwner o, Player other, out bool exact)
        {
            exact = false;
            EnemyInfo e = InfoAbout(o, other);
            if (e != null)
            {
                if (e.IsVisible) { exact = true; return other.Position; }
                if (e.HaveSeenPersonal && PositionSet(e.PersonalLastPos)) return e.PersonalLastPos;
                if (e.HaveSeen && PositionSet(e.EnemyLastPosition)) return e.EnemyLastPosition;
            }
            exact = true;   // no record: only reached for a man right in front of him (debug, recapture)
            return other.Position;
        }

        // A spot roughly `distance` away from the threat with a complete path to it,
        // preferring ones the threat can't see. hiddenOnly: only spots out of its sight.
        // 0.28.2 anyOnly: the caller only asks whether one exists (SurrenderLogic.TakeOpening's "run"
        // option, whose spot is never used: the flight searches its own). It stops at the first
        // reachable candidate instead of path-solving every better-scoring one: the 0.28.1 raid had a
        // 16.8 ms frame from an opening's run + hide searches together (two full searches, up to
        // PathCandidates path solves each).
        // maxSolves (0.31.6): give up after this many path solves failed (availability checks: TakeOpening's
        // "can he run / hide", 1-4 ms frames on the 0.31.5 raid when the first spots were unreachable).
        internal static bool RetreatPoint(Player bot, Vector3 threat, float distance, bool hiddenOnly, out Vector3 point, bool anyOnly = false, int maxSolves = int.MaxValue)
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
            int solves = 0;

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
                if (solves >= maxSolves) break;
                solves++;
                if (!Reachable(bot.Position, hit.position)) continue;

                bestScore = score;
                point = hit.position;
                found = true;
                if (anyOnly) return true;
            }
            return found;
        }

        // 0.17.0: where a fleeing man goes. 0.16 RetreatPoint scored any spot hidden from the threat
        // +100, so a bush 10 m off beat open ground 40 m away, and nothing looked at where he had
        // been held: escapes "got away" a few metres from the captor and lingered there.
        //
        // Candidates (PathCandidates x 1.5, spread +-80 deg around "away from the threat, away from
        // where he was held") are placed beyond `radius` from `heldAt`; each needs a complete path
        // (its corners are checked: no part of the route within 8 m of the threat), and is scored by
        // concealment from the threat, separation from heldAt and the threat, minus route length.
        // None out of the area reachable (a closed room, a cliff): a leg instead, the best hidden spot
        // that gains at least 8 m on heldAt within `radius` of him, so he works his way out through
        // cover. False when not even that exists.
        //
        // 0.24.0: threatVel, the captor's flat velocity when he SEES him walking (zero otherwise: no
        // knowledge of what he can't see). His path over "Allow for where the captor is walking"
        // seconds (at most 20 m) counts as the threat too: a route that crosses it within 6 m is
        // refused, the spot is scored against where he will be as well as where he is, and a spot
        // the same way he is walking (within 35 deg of his heading, while he is within 25 m) is
        // penalised, so the man doesn't leave alongside him. Route stand-off from the threat 5 -> 8 m.
        // why: on success, how many candidates were refused and for what (the escape log).
        internal static bool FleePoint(Player bot, Vector3 threat, Vector3 heldAt, float radius, out Vector3 point, out bool leg, out string why, Vector3 threatVel = default)
        {
            point = bot.Position;
            leg = false;
            why = null;
            Vector3 me = bot.Position;
            Vector3 away = (me - threat); away.y = 0f;
            Vector3 fromHeld = (me - heldAt); fromHeld.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -bot.Transform.forward;
            Vector3 dir = away.normalized + (fromHeld.sqrMagnitude > 1f ? fromHeld.normalized * 0.5f : Vector3.zero);
            threatVel.y = 0f;
            bool walking = threatVel.sqrMagnitude > 0.5f * 0.5f;
            Vector3 ahead = threat;
            if (walking)
            {
                Vector3 step = threatVel * Settings.HeadingLookAhead.Value;
                if (step.magnitude > 20f) step = step.normalized * 20f;
                ahead = threat + step;
                // Walking: the search leans away from where he WILL be (weight 1) over where he is
                // (0.5): with the captor walking through the man's spot, "away from him" points down
                // his path (escsim: 100% of escapes then came within 6 m of him again).
                Vector3 fromAhead = me - ahead; fromAhead.y = 0f;
                dir = away.normalized * 0.5f + (fromHeld.sqrMagnitude > 1f ? fromHeld.normalized * 0.5f : Vector3.zero) +
                      (fromAhead.sqrMagnitude > 1f ? fromAhead.normalized : Vector3.zero);
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = away;
            dir.Normalize();
            Vector3 threatEye = threat + Vector3.up * 1.5f;
            float here = Flat(me, heldAt);
            bool nearHim = Flat(me, threat) < 25f;
            int tries = Mathf.RoundToInt(Settings.PathCandidates.Value * 1.5f);
            float best = float.MinValue, bestLeg = float.MinValue;
            Vector3 legPoint = me;
            int noMesh = 0, noPath = 0, pastThreat = 0, crossesHim = 0, sameWay = 0, hiddenN = 0, pruned = 0;
            // 0.31.4: two passes. First every candidate (the same random draws as before, in the same
            // order) gets its cheap tests: NavMesh sample, concealment, separation, and an upper bound on
            // its score with the route length at its floor (the straight line: a route is never shorter).
            // Then routes are solved best bound first, and a candidate whose bound can't beat the best
            // found is skipped. The chosen spot is the same; most of the ~18 path solves (3.8-4.9 ms in
            // one frame per bolt in the 0.31.3 raid) are no longer made.
            if (FleeBuf.Length < tries) { FleeBuf = new FleeCand[tries]; FleeOrder = new int[tries]; }
            int n = 0;
            for (int i = 0; i < tries; i++)
            {
                float angle = Dice.Range(-80f, 80f);
                bool far = i % 3 != 2;   // two of three aim out of the area, one tries a nearer leg
                float dist = far ? Mathf.Max(radius - here, 0f) + Dice.Range(6f, 25f) : Dice.Range(8f, Mathf.Max(10f, radius * 0.6f));
                Vector3 candidate = me + Quaternion.Euler(0f, angle, 0f) * dir * dist;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas)) { noMesh++; continue; }
                Vector3 c = hit.position;
                bool hidden = !LineClear(threatEye, c + Vector3.up * 1.2f);
                float sep = Flat(c, heldAt);
                float fromThreat = walking ? Mathf.Min(Flat(c, threat), Flat(c, ahead)) : Flat(c, threat);
                float cheap = (hidden ? 30f : 0f) + sep + 0.5f * fromThreat;
                bool way = false;
                if (walking && nearHim)
                {
                    Vector3 go = c - me; go.y = 0f;
                    if (go.sqrMagnitude > 1f && Vector3.Angle(go, threatVel) < 35f) { cheap -= 25f; way = true; }
                }
                bool isLeg = sep < radius;
                if (isLeg && sep < here + 8f) continue;   // neither out of the area nor a useful leg (as before: never chosen)
                float bound = cheap - 0.35f * Flat(me, c) + (isLeg && hidden ? 40f : 0f) + 0.5f;   // +0.5: the path start may snap a little nearer
                FleeBuf[n] = new FleeCand { Point = c, Cheap = cheap, Bound = bound, Hidden = hidden, Leg = isLeg, SameWay = way };
                FleeOrder[n] = n;
                n++;
            }
            // Best bound first: out-of-area spots before legs (a leg is only used when none exists).
            for (int i = 1; i < n; i++)
            {
                int k = FleeOrder[i], j = i - 1;
                while (j >= 0 && Before(FleeBuf[k], FleeBuf[FleeOrder[j]])) { FleeOrder[j + 1] = FleeOrder[j]; j--; }
                FleeOrder[j + 1] = k;
            }
            for (int o = 0; o < n; o++)
            {
                FleeCand f = FleeBuf[FleeOrder[o]];
                if (f.Leg ? (best > float.MinValue || f.Bound <= bestLeg) : f.Bound <= best) { pruned++; continue; }
                if (!PathTo(me, f.Point, out Vector3[] corners, out _)) { noPath++; continue; }
                if (PassesNear(corners, threat, 8f)) { pastThreat++; continue; }
                if (walking && PassesNearSegment(corners, threat, ahead, 6f)) { crossesHim++; continue; }
                float len = 0f;
                for (int k = 1; k < corners.Length; k++) len += Vector3.Distance(corners[k - 1], corners[k]);
                if (f.Hidden) hiddenN++;
                if (f.SameWay) sameWay++;
                float score = f.Cheap - 0.35f * len;
                if (!f.Leg)
                {
                    if (score > best) { best = score; point = f.Point; }
                }
                else
                {
                    float ls = score + (f.Hidden ? 40f : 0f);   // a leg is worth most when it hides him
                    if (ls > bestLeg) { bestLeg = ls; legPoint = f.Point; }
                }
            }
            string stats = $"{tries} spots tried: {noMesh} off the NavMesh, {pruned} could not beat the best, {noPath} no complete path, {pastThreat} past the threat" +
                           (walking ? $", {crossesHim} across his path, {sameWay} his way (penalised); he walks {threatVel.magnitude:0.0} m/s" : "") +
                           $", {hiddenN} hidden from him";
            if (best > float.MinValue) { why = stats; return true; }
            if (bestLeg > float.MinValue) { point = legPoint; leg = true; why = stats; return true; }
            why = $"no way out of {radius:0} m ({stats})";
            return false;
        }

        private struct FleeCand { internal Vector3 Point; internal float Cheap, Bound; internal bool Hidden, Leg, SameWay; }
        private static FleeCand[] FleeBuf = new FleeCand[24];
        private static int[] FleeOrder = new int[24];
        private static bool Before(in FleeCand a, in FleeCand b) => a.Leg != b.Leg ? !a.Leg : a.Bound > b.Bound;

        // 0.24.0: for a fetch route (SurrenderLogic.Decide): the same stand-off test as an escape.
        internal static bool RoutePassesNear(Vector3[] corners, Vector3 p, float r) => corners != null && corners.Length > 1 && PassesNear(corners, p, r);

        // Does this route come closer to p than he already is (or than r, if he is further)? Leaving
        // from beside his captor is fine; running past him is not.
        private static bool PassesNear(Vector3[] corners, Vector3 p, float r)
        {
            p.y = 0f;
            Vector3 s0 = corners[0]; s0.y = 0f;
            float limit = Mathf.Min(r, (s0 - p).magnitude - 0.5f);
            if (limit <= 0f) return false;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i]; a.y = 0f; b.y = 0f;
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                if ((a + ab * t - p).sqrMagnitude < limit * limit) return true;
            }
            return false;
        }

        // 0.24.0: the same against the captor's path p0 -> p1 (sampled every metre: a few dozen
        // point tests per candidate at most).
        private static bool PassesNearSegment(Vector3[] corners, Vector3 p0, Vector3 p1, float r)
        {
            float len = Flat(p0, p1);
            int n = Mathf.Clamp(Mathf.CeilToInt(len), 1, 20);
            for (int i = 1; i <= n; i++)
                if (PassesNear(corners, Vector3.Lerp(p0, p1, i / (float)n), r)) return true;
            return false;
        }

        internal static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

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
            // 0.21.0: a path that will be walked may not cross solid geometry (BotMover puts him on
            // the NavMesh along it whatever stands there). The NavMesh is continuous through a shut
            // door (it is only carved for an open leaf), and can be through a locked one.
            if (CrossesGeometry(corners, out string what)) { why = $"the route crosses {what}"; corners = null; return false; }
            return true;
        }

        // 0.21.0: each leg of the route at hip height (1 m over the corners) against everything a
        // player's capsule collides with (walls, props, doors; not players). A door that can be
        // opened is allowed: the game's door opener opens it on the way (SurrenderLogic.DoorBusy).
        // A locked door, or anything else, refuses the route: the caller picks another or hands
        // the bot to his own AI. A few raycasts per walked path (paths are re-made every few s).
        private static readonly RaycastHit[] GeoHits = new RaycastHit[8];
        internal static bool CrossesGeometry(Vector3[] corners, out string what)
        {
            what = null;
            int mask = LayersMaskController.PlayerStaticCollisionsMask;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 a = corners[i - 1] + Vector3.up, b = corners[i] + Vector3.up;
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 0.05f) continue;
                int n = Physics.RaycastNonAlloc(a, d / len, GeoHits, len, mask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    Collider c = GeoHits[k].collider;
                    if (c == null) continue;
                    EFT.Interactive.Door door = c.GetComponentInParent<EFT.Interactive.Door>();
                    if (door != null)
                    {
                        if (door.DoorState == EFT.Interactive.EDoorState.Locked) { what = "a locked door"; return true; }
                        continue;
                    }
                    what = $"{c.name} ({LayerMask.LayerToName(c.gameObject.layer)}) {GeoHits[k].distance:0.0} m along leg {i}";
                    return true;
                }
            }
            return false;
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
