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
    //     1, spawner)      profile from the server; PMCs are asked for as side Savage (RequestSide:
    //                      the server sends them as Savage, SPT's PmcBotSidePatch sets Bear/Usec)
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

        // A scenario with a bot still on its way (requested, neither here nor failed).
        internal static bool Spawning(Scenario sc)
        {
            foreach (Participant p in sc.People)
                if (p.Existing == null && p.Requested && !p.Spawned && !p.Failed) return true;
            return false;
        }

        internal static void Request(Scenario sc, Participant p)
        {
            // One request per participant: a second one (a repeated RequestNext, a retry racing
            // the first answer) would spawn a second bot for the same slot.
            if (p.Requested && !p.Failed)
            {
                HandsAreHighPlugin.Log.LogWarning($"Scenario {sc.Title}: {p.Tag}: spawn already requested {Time.time - p.RequestedAt:0.0} s ago; second request ignored");
                return;
            }
            p.Requested = true;
            p.Failed = false;
            p.FailWhy = null;
            p.Created = null;
            p.SpawnedAt = -1f;
            p.RequestedAt = Time.time;
            p.Raid = Scenarios.RaidSerial;
            sc.Note(TL.Real, $"{p.Tag}: spawn requested ({p.Role}, {p.Side}, squad {p.Squad}) at {p.Spot} ({Vector3.Distance(sc.Anchor, p.Spot):0} m from the centre)");
            RequestAsync(sc, p);
        }

        // The side to ask the profile cache for. SPT's server sends every PMC (Bear/Usec) with
        // Info.Side "Savage" (SPTarkov.Server.Core BotController.TryGenerateSingleBot), and the
        // game's GetProfileDataParams.ChooseProfile keeps only profiles whose Side, Role and
        // BotDifficulty all equal the request; SPT's PmcBotSidePatch (postfix on
        // BotCreationData.ChooseProfile) sets Bear/Usec from the role on the profile it chose.
        // Asking for Bear/Usec therefore matched nothing: ChooseProfile returned null and
        // BotProfileClient.CreateProfile dereferenced it (profile.GetAllPrefabPaths, the
        // NullReferenceException in 0.10.0-0.13.0). The vanilla debug spawn
        // (BotSpawner.DebugSpawnAnyway) asks for Savage too.
        internal static EPlayerSide RequestSide(WildSpawnType role, EPlayerSide side) =>
            role == WildSpawnType.pmcBEAR || role == WildSpawnType.pmcUSEC ? EPlayerSide.Savage : side;

        // Stale: the scenario was cleaned up, or the raid this request belongs to is over.
        private static string Stale(Scenario sc, Participant p)
        {
            if (p.Raid != Scenarios.RaidSerial || !SurrenderRegistry.InRaid) return "its raid ended";
            if (sc.Cancelled) return "the scenario was cleaned up";
            return null;
        }

        private static async void RequestAsync(Scenario sc, Participant p)
        {
            BotSpawner sp = null;
            bool counted = false, answered = false;
            string stage = "checks";
            try
            {
                // Prerequisites, checked again here (the menu checked them at launch; a squad's
                // later members are requested seconds after).
                if (!Ready(out string notReady)) { Fail(sc, p, notReady); return; }
                IBotGame game = Singleton<IBotGame>.Instance;
                sp = game.BotsController.BotSpawner;
                if (sp._gameEnd) { Fail(sc, p, "the raid is ending (the game stopped spawning)"); return; }
                string stale = Stale(sc, p);
                if (stale != null) { Fail(sc, p, $"not requested: {stale}"); return; }
                int core = NearestCorePoint(game.BotsController, p.Spot);
                if (core < 0) { Fail(sc, p, "no AI core point near the spot"); return; }
                BotZone zone = sp.GetClosestZone(p.Spot, out _);
                if (zone == null) { Fail(sc, p, "no bot zone near the spot"); return; }

                stage = "profile";
                EPlayerSide ask = RequestSide(p.Role, p.Side);
                var spawnParams = new BotSpawnParams { ShallBeGroup = new ShallBeGroupParams(false, false) };
                var profileData = new GetProfileDataParams(ask, p.Role, BotDifficulty.normal, 0f, spawnParams);
                BotCreationData data = await BotCreationData.Create(profileData, sp._botCreator, 1, sp);
                stale = Stale(sc, p);
                if (stale != null)
                {
                    // Nothing exists yet: a profile was taken from the cache, no player was made.
                    if (p.Raid == Scenarios.RaidSerial) Fail(sc, p, $"profile arrived after {stale}; nothing created");
                    return;
                }
                if (data == null || data.Count == 0) { Fail(sc, p, $"the server returned no {p.Role} profile (asked as side {ask}, difficulty normal)"); return; }
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"Scenario {sc.Title}: {p.Tag}: profile ready after {Time.time - p.RequestedAt:0.0} s");
                data.AddPosition(p.Spot, core);
                if (!sc.Groups.ContainsKey(p.Squad)) sc.Zones[p.Squad] = zone;
                else if (sc.Zones.TryGetValue(p.Squad, out BotZone z0) && z0 != null) zone = z0;

                // The game's bookkeeping, as BotSpawner.method_7 does it: ++ here, -- in
                // ActivateBotCallback. ActivateBot can finish without ever calling back
                // (cancelled, SpawnStopped, no position, player creation failed); the count is
                // then given back below, or the raid's bot cap (AllBotsWithLoaded) stays lowered.
                stage = "activation";
                sp._inSpawnProcess++;
                counted = true;
                Func<BotOwner, BotZone, BotsGroup> groupAction = (bot, z) => { p.Created = bot; return GroupFor(sc, p, sp, bot, z); };
                await sp._botCreator.ActivateBot(data, zone, false, groupAction,
                    bot => { answered = true; sp.ActivateBotCallback(bot, data, b => Spawned(sc, p, b), false, new System.Diagnostics.Stopwatch()); },
                    sp.GetCancelToken());
                if (!answered)
                {
                    sp._inSpawnProcess--;
                    counted = false;
                    Fail(sc, p, data.SpawnStopped ? "the game stopped this spawn (SpawnStopped)" : "the game's bot creator finished without a bot (cancelled or the player couldn't be created)");
                }
            }
            catch (Exception e)
            {
                // Give back what this attempt took, and nothing else.
                if (counted && !answered && sp != null) sp._inSpawnProcess--;
                string cleanup = "";
                if (p.Created != null && !p.Spawned)
                {
                    cleanup = $"; the half-made bot: {RemoveHalfMade(p.Created)}";
                    p.Created = null;
                }
                Fail(sc, p, Diagnose(stage, p, e) + cleanup);
                HandsAreHighPlugin.Report("scenario spawn", e);
            }
        }

        // What went wrong, in words. The ChooseProfile case is told apart by where it threw.
        private static string Diagnose(string stage, Participant p, Exception e)
        {
            string at = e.StackTrace ?? "";
            if (e is NullReferenceException && at.Contains("BotProfileClient.CreateProfile"))
                return $"no {p.Role} profile from the server matched the request (side {RequestSide(p.Role, p.Side)}, role {p.Role}, difficulty normal): " +
                       "the game's ChooseProfile found none and its CreateProfile used the missing profile (NullReferenceException). " +
                       "If this is a PMC, a server mod may be changing PMC side/difficulty (SPT pmc.json useDifficultyOverride)";
            return $"{stage} threw {e.GetType().Name}: {e.Message}";
        }

        // A bot the creator made but never handed over (our group action or the activation threw):
        // the game's own leave path if it got that far, else left alone and said so.
        private static string RemoveHalfMade(BotOwner bot)
        {
            try
            {
                if (bot == null) return "gone";
                if (bot.LeaveData == null) return "not activated, no leave data: left as the game made it";
                bot.LeaveData.RemoveFromMap();
                return "removed (BotLeaveData.RemoveFromMap)";
            }
            catch (Exception e)
            {
                HandsAreHighPlugin.Report("scenario half-made bot", e);
                return $"removal failed ({e.GetType().Name})";
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

        // Runs inside the game's ActivateBotCallback, before its own _inSpawnProcess--: nothing
        // here may throw into it (the count would stay taken for the rest of the raid).
        private static void Spawned(Scenario sc, Participant p, BotOwner bot)
        {
            try
            {
                p.Created = null;
                if (bot == null) { Fail(sc, p, "the game returned no bot"); return; }
                if (p.Spawned) { HandsAreHighPlugin.Log.LogWarning($"Scenario {sc.Title}: {p.Tag}: a second bot arrived for one request; removed: {RemoveHalfMade(bot)}"); return; }
                p.Owner2 = bot;
                p.Player = bot.GetPlayer;
                p.Spawned = true;
                p.SpawnedAt = Time.time;
                string stale = Stale(sc, p);
                if (stale != null)
                {
                    // Not registered with a scenario that is gone: removed on arrival.
                    string what = Despawn(p);
                    if (p.Raid == Scenarios.RaidSerial) sc.Note(TL.Info, $"{p.Tag}: arrived after {stale}: {what}");
                    return;
                }
                Scenarios.Register(sc, p);
                SpawnQuiet.Add(bot, Restraint.Name(p.Player));
                // SPT's PmcBotSidePatch gives a PMC its side from the role; check it took.
                EPlayerSide got = p.Player.Profile?.Info?.Side ?? EPlayerSide.Savage;
                string sideNote = got != p.Side ? $" (side {got}, expected {p.Side})" : "";
                sc.Note(TL.Real, $"{p.Tag} spawned after {Time.time - p.RequestedAt:0.0} s: {p.Player.Profile?.Nickname} ({p.ProfileId()}, {p.Role}, {got}){sideNote}, group {bot.BotsGroup?.Id}, " +
                    $"{Vector3.Distance(p.Spot, p.Player.Position):0.0} m from the validated spot, {Vector3.Distance(sc.Anchor, p.Player.Position):0} m from the centre, state {bot.BotState}");
                Scenarios.RequestNext(sc, p.Squad);
            }
            catch (Exception e)
            {
                Fail(sc, p, $"after spawning: {e.GetType().Name}: {e.Message}");
                HandsAreHighPlugin.Report("scenario spawned", e);
            }
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
