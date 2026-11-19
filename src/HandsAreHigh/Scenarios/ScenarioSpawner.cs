using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace HandsAreHigh
{
    // Spawning and removing scenario bots, and validating where they stand.
    //
    // Spawn (Assembly-CSharp 0.16.9, verified against C:\HUH 2026-11-19): the same steps as the
    // game's own BotSpawner.SpawnBotsInZoneOnPositions -> method_10, with our own group action:
    //   BotCreationData.Create(GetProfileDataParams(side, role, difficulty), BotSpawner._botCreator,
    //     1, spawner)      profile from the server (SPT generates it; SPT's PmcBotSidePatch sets a
    //                      PMC's side from its role)
    //   data.AddPosition(spot, nearest AICorePoint id)   (BotCreatorClient.CreateBot needs a core point)
    //   spawner._inSpawnProcess++                       (method_7's bookkeeping; ActivateBotCallback --)
    //   IBotCreator.ActivateBot(data, closest zone, false, groupAction, bot =>
    //     spawner.ActivateBotCallback(bot, data, ourCallback, false, stopwatch), token)
    // groupAction: the first member of a scenario squad gets a new BotsGroup built exactly as
    // BotSpawner.GetGroupAndSetEnemies builds one (enemies list, SetBotAsEnemy, Groups.AddNoKey);
    // later members join it the way that method's TryGetValue branch does (SetBotAsEnemy, same
    // group). So a scenario squad is a real BotsGroup and never joins a random raid group.
    // Squad members are requested one after another (the next after the previous one's callback).
    //
    // Remove: BotLeaveData.RemoveFromMap (the game's path for a bot leaving the map: Deactivate,
    // Dispose, IBotGame.BotDespawn), only for living bots this scenario spawned. Dead ones stay
    // where they fell (vanilla corpse and ragdoll; removing corpses is not a supported operation).
    internal static class ScenarioSpawner
    {
        private static readonly NavMeshPath Path = new NavMeshPath();

        internal static bool Ready(out string why)
        {
            why = null;
            IBotGame game = Singleton<IBotGame>.Instance;
            if (game == null) { why = "no bot game (not in a raid)"; return false; }
            if (game.Status != GameStatus.Started) { why = $"the raid isn't running ({game.Status})"; return false; }
            BotSpawner sp = game.BotsController?.BotSpawner;
            if (sp == null || sp._botCreator == null) { why = "the bot spawner isn't ready"; return false; }
            if (game.BotsController.CoversData?.AICorePointsHolder?.CorePoints == null || game.BotsController.CoversData.AICorePointsHolder.CorePoints.Count == 0)
            { why = "this map has no AI core points (bots can't be created here)"; return false; }
            return true;
        }

        // ---- placement ------------------------------------------------------------------------

        // The anchor: on walkable ground `distance` m ahead of the player (flat), or where his
        // crosshair meets the world (up to 250 m). forward: the scenario frame (player -> anchor).
        internal static bool Anchor(Player me, bool atCrosshair, float distance, out Vector3 anchor, out Vector3 forward, out string why)
        {
            anchor = Vector3.zero; why = null;
            forward = me.LookDirection; forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = me.Transform.forward;
            forward.Normalize();
            Vector3 target;
            if (atCrosshair)
            {
                Vector3 eye = Perception.Eye(me);
                if (!Physics.Raycast(eye, me.LookDirection, out RaycastHit hit, 250f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
                { why = "nothing under your crosshair within 250 m"; return false; }
                target = hit.point - me.LookDirection.normalized * 0.6f;
            }
            else target = me.Position + forward * distance;
            if (!NavMesh.SamplePosition(target, out NavMeshHit nh, 4f, NavMesh.AllAreas))
            { why = $"no walkable ground within 4 m of the chosen point ({Vector3.Distance(me.Position, target):0} m from you)"; return false; }
            anchor = nh.position;
            Vector3 f = anchor - me.Position; f.y = 0f;
            if (f.sqrMagnitude > 1f) forward = f.normalized;
            return true;
        }

        internal static Vector3 World(Scenario sc, Vector3 offset)
        {
            Vector3 right = Vector3.Cross(Vector3.up, sc.Forward).normalized;
            return sc.Anchor + right * offset.x + sc.Forward * offset.z;
        }

        // A valid spot near `desired`: on the NavMesh (within 1.5 m) at the same level (1.6 m),
        // solid ground under it, no wall or ceiling in a standing capsule, at least 1.6 m from
        // every other spot and every living person, and a complete NavMesh path from the
        // anchor (no other floor, no closed-off room). Rings out to 6 m before giving up.
        internal static bool FindSpot(Vector3 desired, Vector3 anchor, List<Vector3> taken, out Vector3 spot, out string why)
        {
            spot = Vector3.zero;
            string last = "no walkable ground";
            float[] radii = { 0f, 1f, 2f, 3f, 4.5f, 6f };
            for (int r = 0; r < radii.Length; r++)
            {
                int steps = radii[r] == 0f ? 1 : 10;
                for (int k = 0; k < steps; k++)
                {
                    float a = k * Mathf.PI * 2f / steps;
                    Vector3 c = desired + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radii[r];
                    string no = Check(c, anchor, taken, out Vector3 p);
                    if (no == null) { spot = p; why = null; return true; }
                    last = no;
                }
            }
            why = $"no valid spot within 6 m ({last})";
            return false;
        }

        private static string Check(Vector3 c, Vector3 anchor, List<Vector3> taken, out Vector3 p)
        {
            p = Vector3.zero;
            // The level: drop onto whatever is below the intended point (from 2 m above).
            if (Physics.Raycast(c + Vector3.up * 2f, Vector3.down, out RaycastHit g, 6f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
                c.y = g.point.y;
            if (!NavMesh.SamplePosition(c, out NavMeshHit nh, 1.5f, NavMesh.AllAreas)) return "no walkable ground";
            p = nh.position;
            if (Mathf.Abs(p.y - c.y) > 1.6f) return "walkable ground only on another level";
            if (!Physics.Raycast(p + Vector3.up * 0.8f, Vector3.down, out RaycastHit under, 1.6f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
                return "no solid ground under the NavMesh";
            if (Mathf.Abs(under.point.y - p.y) > 0.5f) return "NavMesh and ground disagree";
            if (Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.65f, 0.35f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
                return "a wall or ceiling in the way";
            for (int i = 0; i < taken.Count; i++)
                if (Flat(taken[i], p) < 1.6f) return "too close to another participant";
            List<Player> all = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && Flat(all[i].Position, p) < 1.6f && Mathf.Abs(all[i].Position.y - p.y) < 2f) return "someone already stands there";
            if (!NavMesh.CalculatePath(anchor, p, NavMesh.AllAreas, Path) || Path.status != NavMeshPathStatus.PathComplete)
                return "not reachable from the scenario centre";
            float len = 0f;
            Vector3[] corners = Path.corners;
            for (int i = 1; i < corners.Length; i++) len += Vector3.Distance(corners[i - 1], corners[i]);
            if (len > 3f * Vector3.Distance(anchor, p) + 15f) return $"only reachable by a long way round ({len:0} m)";
            return null;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ---- spawning ---------------------------------------------------------------------------

        internal static void Request(Scenario sc, Participant p)
        {
            p.Requested = true;
            p.SpawnedAt = -1f;
            p.RequestedAt = Time.time;
            sc.Note(TL.Real, $"{p.Tag}: spawn requested ({p.Role}, {p.Side}, squad {p.Squad}) at {p.Spot} ({Vector3.Distance(sc.Anchor, p.Spot):0} m from the centre)");
            RequestAsync(sc, p);
        }

        private static async void RequestAsync(Scenario sc, Participant p)
        {
            try
            {
                IBotGame game = Singleton<IBotGame>.Instance;
                BotSpawner sp = game?.BotsController?.BotSpawner;
                if (sp == null) { Fail(sc, p, "no bot spawner"); return; }
                var spawnParams = new BotSpawnParams { ShallBeGroup = new ShallBeGroupParams(false, false) };
                var profileData = new GetProfileDataParams(p.Side, p.Role, BotDifficulty.normal, 0f, spawnParams);
                BotCreationData data = await BotCreationData.Create(profileData, sp._botCreator, 1, sp);
                if (sc.Cancelled || !SurrenderRegistry.InRaid) return;
                if (data == null || data.Count == 0) { Fail(sc, p, $"the server returned no {p.Role} profile"); return; }
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"Scenario {sc.Title}: {p.Tag}: profile ready after {Time.time - p.RequestedAt:0.0} s");
                int core = NearestCorePoint(game.BotsController, p.Spot);
                if (core < 0) { Fail(sc, p, "no AI core point"); return; }
                data.AddPosition(p.Spot, core);
                BotZone zone = sp.GetClosestZone(p.Spot, out _);
                if (zone == null) { Fail(sc, p, "no bot zone"); return; }
                if (!sc.Groups.ContainsKey(p.Squad)) sc.Zones[p.Squad] = zone;
                else if (sc.Zones.TryGetValue(p.Squad, out BotZone z0) && z0 != null) zone = z0;
                sp._inSpawnProcess++;
                Func<BotOwner, BotZone, BotsGroup> groupAction = (bot, z) => GroupFor(sc, p, sp, bot, z);
                await sp._botCreator.ActivateBot(data, zone, false, groupAction,
                    bot => sp.ActivateBotCallback(bot, data, b => Spawned(sc, p, b), false, new System.Diagnostics.Stopwatch()),
                    sp.GetCancelToken());
            }
            catch (Exception e)
            {
                Fail(sc, p, $"spawn threw {e.GetType().Name}: {e.Message}");
                HandsAreHighPlugin.Report("scenario spawn", e);
            }
        }

        private static void Fail(Scenario sc, Participant p, string why)
        {
            if (p.Failed || p.Spawned) return;
            p.Failed = true;
            p.FailWhy = why;
            sc.Note(TL.ExecFail, $"{p.Tag}: spawn failed: {why}");
        }

        private static BotsGroup GroupFor(Scenario sc, Participant p, BotSpawner sp, BotOwner bot, BotZone zone)
        {
            if (sc.Groups.TryGetValue(p.Squad, out BotsGroup g) && g != null)
            {
                sp.SetBotAsEnemy(bot);
                return g;
            }
            var enemies = new List<BotOwner>(sp.GetBotEnemiesList(bot));
            sp.SetBotAsEnemy(bot);
            g = new BotsGroup(zone, sp.BotGame, bot, enemies, sp._deadBodiesController, sp._allPlayers, forBoss: false);
            sp.Groups.AddNoKey(g, zone);
            sc.Groups[p.Squad] = g;
            return g;
        }

        private static int NearestCorePoint(BotsController bc, Vector3 at)
        {
            List<AICorePoint> pts = bc?.CoversData?.AICorePointsHolder?.CorePoints;
            if (pts == null) return -1;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i] == null) continue;
                float sq = (pts[i].Position - at).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = pts[i].Id; }
            }
            return best;
        }

        private static void Spawned(Scenario sc, Participant p, BotOwner bot)
        {
            if (bot == null) { Fail(sc, p, "the game returned no bot"); return; }
            p.Owner2 = bot;
            p.Player = bot.GetPlayer;
            p.Spawned = true;
            p.SpawnedAt = Time.time;
            Scenarios.Register(sc, p);
            if (sc.Cancelled)
            {
                sc.Note(TL.Info, $"{p.Tag}: arrived after the scenario was cleaned up: {Despawn(p)}");
                return;
            }
            sc.Note(TL.Real, $"{p.Tag} spawned after {Time.time - p.RequestedAt:0.0} s: {p.Player.Profile?.Nickname} ({p.ProfileId()}, {p.Role}, {p.Side}), group {bot.BotsGroup?.Id}, " +
                $"{Vector3.Distance(p.Spot, p.Player.Position):0.0} m from the validated spot, {Vector3.Distance(sc.Anchor, p.Player.Position):0} m from the centre, state {bot.BotState}");
            Scenarios.RequestNext(sc, p.Squad);
        }

        // ---- removal ---------------------------------------------------------------------------

        internal static string Despawn(Participant p)
        {
            if (p.Existing != null) return "an existing raid bot: left alone";
            if (p.Removed) return "already removed";
            BotOwner o = p.Owner2;
            Player pl = p.Player;
            if (o == null || (object)pl == null) return "never spawned";
            if (p.Dead || pl == null || !Perception.Alive(pl)) return "dead: corpse left where the game put it (vanilla ragdoll)";
            SurrenderRegistry.Discharge(pl, "scenario cleanup");
            Captors.EndFor(pl, "scenario cleanup");
            try
            {
                if (o.LeaveData == null) return "no leave data: left in the raid (couldn't be removed safely)";
                o.LeaveData.RemoveFromMap();
                p.Removed = true;
                return "removed (BotLeaveData.RemoveFromMap, the game's own leave path)";
            }
            catch (Exception e)
            {
                HandsAreHighPlugin.Report("scenario despawn", e);
                return $"removal failed ({e.GetType().Name}): left in the raid";
            }
        }
    }
}
