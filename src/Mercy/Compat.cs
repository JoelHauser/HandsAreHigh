using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Mercy
{
    // ORBIT (optional, found by reflection; read from github.com/Chazut/ORBIT at tag 3.1.1, the
    // version Joel runs):
    //   Orbit.Api.OrbitBotControl.SetBotSleepAllowed(BotOwner, bool allowed, string source)
    //   Per BotOwner, Unity thread, returns false outside ORBIT's runtime. false blocks Ghost
    //   sleep for that bot (and, ORBIT's rule, for its ORBIT squad while blocked); true removes the
    //   block. Removal and raid end clear it on ORBIT's side.
    // Why it is needed: DormancySystem only refuses sleep while Memory.GoalEnemy != null or
    // IsUnderFire. A captor calmly guarding has neither (restraint clears the prisoner as his
    // target), so a bot-vs-bot encounter far from the player could have one side ghosted mid-
    // custody. We block sleep only for the two participants of a custody, only while both are
    // already awake (Accept requires it), and lift it when custody ends: nobody is woken.
    internal static class OrbitAdapter
    {
        private const string Source = "Mercy";
        private static bool _resolved;
        private static MethodInfo _setSleepAllowed;
        private static readonly HashSet<BotOwner> Blocked = new HashSet<BotOwner>();
        private static readonly object[] Args = new object[3];

        internal static bool Present { get { Resolve(); return _setSleepAllowed != null; } }

        internal static void Prewarm() => Resolve();

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Orbit.Api.OrbitBotControl", false);
                    if (t == null) continue;
                    _setSleepAllowed = t.GetMethod("SetBotSleepAllowed", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(BotOwner), typeof(bool), typeof(string) }, null);
                    break;
                }
                MercyPlugin.Log.LogInfo(_setSleepAllowed != null
                    ? "ORBIT found: bot-held encounters can be kept awake (SetBotSleepAllowed)"
                    : "ORBIT sleep API not found (not installed, or older than 3.1.1): encounters are paused while asleep");
            }
            catch (Exception e) { MercyPlugin.Log.LogWarning($"ORBIT lookup failed: {e.Message}"); }
        }

        // Keep (true) or stop keeping (false) one bot awake. Only an awake bot is ever blocked.
        internal static void KeepAwake(BotOwner o, bool keep, string why)
        {
            if (o == null) return;
            if (keep && (!Settings.OrbitKeepAwake.Value || o.IsDead || o.BotState != EBotState.Active)) return;
            if (!keep && !Blocked.Contains(o)) return;
            Resolve();
            if (_setSleepAllowed == null) return;
            bool ok = Invoke(o, !keep);
            if (keep && ok) Blocked.Add(o);
            if (!keep) Blocked.Remove(o);
            if (Settings.VerboseLogs.Value)
                MercyPlugin.Log.LogInfo($"ORBIT: {(keep ? "sleep blocked" : "sleep allowed again")} for {Restraint.Name(o.GetPlayer)} ({why}){(ok ? "" : ", not accepted")}");
        }

        private static bool Invoke(BotOwner o, bool allowed)
        {
            Args[0] = o; Args[1] = allowed; Args[2] = Source;
            try { return _setSleepAllowed.Invoke(null, Args) is bool b && b; }
            catch { return false; }
            finally { Args[0] = null; }
        }

        // Raid end, disable, teardown: every block we set is lifted (ORBIT also clears its own
        // policy at raid end; lifting twice is safe).
        internal static void ReleaseAll()
        {
            if (Blocked.Count == 0) return;
            if (_setSleepAllowed != null)
                foreach (BotOwner o in Blocked)
                    if (o != null && !o.IsDead) Invoke(o, true);
            Blocked.Clear();
        }

        internal static int Count => Blocked.Count;

        internal static void Reset()
        {
            ReleaseAll();
            _resolved = false;
            _setSleepAllowed = null;
        }
    }

    // 0.26.0: ORBIT's objective for a released man. ORBIT keeps the objective it had before the
    // capture; handed back, he walked straight back to it (0.25.0 raid, #1: handed back 9 m away
    // walking off, 3 s later "drifting back ... layer OrbitBrainLayer"; three keep-away legs, each
    // undone by ORBIT; at +46 s CIRCLED BACK to 14 m). ORBIT 3.1.1 has no public API to drop an
    // objective (OrbitBotControl only excludes / sleeps), so this mirrors ORBIT's own
    // GotoObjectiveAction.AbandonStalledObjective (decompiled from the installed ORBIT.dll):
    // squad.CompletedPoiIds.Add(id) (the waypoint is skipped by every later pick for that squad),
    // the matching squad.Objective.Location = null, agent.Objective.Location / SplinterParent = null,
    // Status = None; GotoObjectiveStrategy then calls AssignNewObjective for the squad (RequestNear
    // his own position). Only an objective at the capture spot / ex-captor, or one whose straight
    // line passes him; only for a man who leads his ORBIT squad (or is alone in it): a follower's
    // objective is his squad's, not ours to change. Every member resolved once; any miss turns the
    // whole thing off with one log line.
    internal static class OrbitObjectives
    {
        private static bool _resolved, _ok;
        private static PropertyInfo _instance;
        private static FieldInfo _agentData, _agentActive, _agentSquad, _agentObjective;
        private static MethodInfo _byProfile;
        private static FieldInfo _objLocation, _objSplinter, _objStatus;
        private static FieldInfo _wpPosition;
        private static PropertyInfo _wpId;
        private static FieldInfo _squadObjective, _squadCompleted, _squadLeader, _squadObjLocation;
        private static PropertyInfo _squadSize;
        private static object _statusNone;
        private static readonly object[] Arg = new object[1];

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Assembly asm = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetType("Orbit.Core.OrbitManager", false) != null) { asm = a; break; }
                if (asm == null) return;   // ORBIT not installed: nothing to say
                const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                Type mgr = asm.GetType("Orbit.Core.OrbitManager"), agentData = asm.GetType("Orbit.Entities.AgentData"),
                     agent = asm.GetType("Orbit.Entities.Agent"), obj = asm.GetType("Orbit.Entities.Objective"),
                     squad = asm.GetType("Orbit.Entities.Squad"), sobj = asm.GetType("Orbit.Entities.SquadObjective"),
                     wp = asm.GetType("Orbit.Navigation.Waypoint"), status = asm.GetType("Orbit.Entities.ObjectiveStatus");
                _instance = typeof(Singleton<>).MakeGenericType(mgr).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _agentData = mgr.GetField("AgentData", I);
                _byProfile = agentData?.GetMethod("GetByProfileId", I, null, new[] { typeof(string) }, null);
                _agentActive = agent?.GetField("IsActive", I);
                _agentSquad = agent?.GetField("Squad", I);
                _agentObjective = agent?.GetField("Objective", I);
                _objLocation = obj?.GetField("Location", I);
                _objSplinter = obj?.GetField("SplinterParent", I);
                _objStatus = obj?.GetField("Status", I);
                _wpPosition = wp?.GetField("Position", I);
                _wpId = wp?.GetProperty("Id", I);
                _squadObjective = squad?.GetField("Objective", I);
                _squadCompleted = squad?.GetField("CompletedPoiIds", I);
                _squadLeader = squad?.GetField("Leader", I);
                _squadSize = squad?.GetProperty("Size", I);
                _squadObjLocation = sobj?.GetField("Location", I);
                _statusNone = status != null && Enum.IsDefined(status, "None") ? Enum.Parse(status, "None") : null;
                _ok = _instance != null && _agentData != null && _byProfile != null && _agentActive != null && _agentSquad != null &&
                      _agentObjective != null && _objLocation != null && _objSplinter != null && _objStatus != null &&
                      _wpPosition != null && _wpId != null && _squadObjective != null && _squadCompleted != null &&
                      _squadLeader != null && _squadSize != null && _squadObjLocation != null && _statusNone != null;
                MercyPlugin.Log.LogInfo(_ok
                    ? "ORBIT: a released man's stale objective at his capture can be dropped (ORBIT then picks a new one)"
                    : "ORBIT: objective members not found (ORBIT version differs from 3.1.1): released men keep ORBIT's objective");
            }
            catch (Exception e) { _ok = false; MercyPlugin.Log.LogWarning($"ORBIT objective lookup failed: {e.Message}"); }
        }

        // His ORBIT objective's position, if ORBIT drives him and he has one.
        private static object AgentOf(BotOwner o)
        {
            object mgr = _instance.GetValue(null, null);
            object data = mgr != null ? _agentData.GetValue(mgr) : null;
            if (data == null) return null;
            Arg[0] = o.ProfileId;
            try { return _byProfile.Invoke(data, Arg); } finally { Arg[0] = null; }
        }

        // Drops his ORBIT objective when it leads back to the capture: within `radius` of where he
        // was held or of his ex-captor (as he last saw him), or its straight line from him passing
        // within 10 m of the ex-captor. Returns a log phrase when something was checked, else null.
        internal static string RetireIfBack(BotOwner o, Vector3 heldAt, Vector3 threat, float radius, out bool retired)
        {
            retired = false;
            if (o == null || o.IsDead) return null;
            Resolve();
            if (!_ok) return null;
            try
            {
                object agent = AgentOf(o);
                if (agent == null || !(_agentActive.GetValue(agent) is bool active) || !active) return null;
                object objective = _agentObjective.GetValue(agent);
                object loc = objective != null ? _objLocation.GetValue(objective) : null;
                if (loc == null) return null;
                Vector3 pos = (Vector3)_wpPosition.GetValue(loc);
                Vector3 me = o.Position;
                float toHeld = Perception.Flat(pos, heldAt), toThreat = Perception.Flat(pos, threat);
                float pass = FlatSegmentDistance(threat, me, pos);
                bool back = toHeld < radius || toThreat < radius || (pass < 10f && Perception.Flat(me, pos) > Perception.Flat(me, threat));
                string where = $"ORBIT objective {Perception.Flat(me, pos):0} m from him, {toHeld:0} m from where he was held, {toThreat:0} m from his ex-captor";
                if (!back) return where + ": leads away, kept";
                object squad = _agentSquad.GetValue(agent);
                if (squad != null && (int)_squadSize.GetValue(squad, null) > 1 && !ReferenceEquals(_squadLeader.GetValue(squad), agent))
                    return where + ": leads back, but it is his squad's (he follows): kept";
                int id = (int)_wpId.GetValue(loc, null);
                if (squad != null)
                {
                    if (_squadCompleted.GetValue(squad) is HashSet<int> done) done.Add(id);
                    object sobj = _squadObjective.GetValue(squad);
                    if (sobj != null && ReferenceEquals(_squadObjLocation.GetValue(sobj), loc)) _squadObjLocation.SetValue(sobj, null);
                }
                _objLocation.SetValue(objective, null);
                _objSplinter.SetValue(objective, null);
                _objStatus.SetValue(objective, _statusNone);
                retired = true;
                return where + ": leads back to the capture, dropped (ORBIT picks a new one)";
            }
            catch (Exception e)
            {
                _ok = false;
                MercyPlugin.Log.LogWarning($"ORBIT objective drop failed, turned off for this session: {e.Message}");
                return null;
            }
        }

        private static float FlatSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
            Vector2 ab = B - A;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(P, A + ab * t);
        }

        internal static void Prewarm() => Resolve();

        internal static void Reset()
        {
            _resolved = _ok = false;
            _instance = null; _agentData = _agentActive = _agentSquad = _agentObjective = null; _byProfile = null;
            _objLocation = _objSplinter = _objStatus = _wpPosition = null; _wpId = null;
            _squadObjective = _squadCompleted = _squadLeader = _squadObjLocation = null; _squadSize = null;
            _statusNone = null;
        }
    }

    // Your teammates (verified relationship, not faction):
    //   - BotsGroup.IsAlly(main player): the game's own ally list. Pit Fireteam puts its
    //     followers in a group that AddAlly's the real player (BossPlayers.AddGroupToBoss).
    //   - Pit Fireteam 1.1.2: a follower's BotFollower.BossToFollow is its pitAIBossPlayer
    //     (FollowerCombatLayer / FollowerRequestLayer IsActive check exactly that). Matched by
    //     type name, so no reference to Pit Fireteam is needed.
    // Pit Fireteam's layers sit at 71-76 and 101 on PMC/Scav brains, above our captor layer (70)
    // and below our surrender layer (95): a teammate who surrendered would leave the player's
    // orders, so teammates don't surrender on their own (TeammatesSurrender), never get the
    // captor layer, and their own talk (BotTalk.TrySay) is left to Pit Fireteam.
    internal static class Teammates
    {
        internal static bool IsPlayerTeammate(BotOwner o)
        {
            if (o == null) return false;
            try
            {
                Player me = Singleton<GameWorld>.Instance?.MainPlayer;
                if (me != null && o.BotsGroup != null && o.BotsGroup.IsAlly(me)) return true;
                IBossToFollow boss = o.BotFollower?.BossToFollow;
                return boss != null && boss.GetType().Name == "pitAIBossPlayer";
            }
            catch { return false; }
        }

        // A real Pit Fireteam follower (its boss is Pit Fireteam's pitAIBossPlayer), not merely
        // a friendly bot. Debug scenarios use this to find an existing teammate.
        internal static bool IsPitFollower(BotOwner o)
        {
            try
            {
                IBossToFollow boss = o?.BotFollower?.BossToFollow;
                return boss != null && boss.GetType().Name == "pitAIBossPlayer";
            }
            catch { return false; }
        }
    }
}

namespace Mercy
{
    // 0.16.0, Looting Bots (optional; not installed on Joel's PC, so unverified against a live
    // copy): its published external API is LootingBots.External.PreventBotFromLooting(BotOwner,
    // float duration). Called only if a static method with exactly that name and those parameter
    // types exists, so a different version means "not found" (logged once), never a wrong call.
    // Used to keep it from starting its own looting on a captor while he guards or fetches
    // (two systems moving the same bot to the same item). Its item claims are not readable here:
    // a race it wins is a failed transaction on our side (the game allows one move), logged.
    internal static class LootingBotsAdapter
    {
        private static bool _resolved;
        private static System.Reflection.MethodInfo _prevent;
        private static readonly object[] Args = new object[2];
        private static readonly System.Collections.Generic.Dictionary<EFT.BotOwner, float> Until = new System.Collections.Generic.Dictionary<EFT.BotOwner, float>();

        internal static bool Present { get { Resolve(); return _prevent != null; } }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type t = a.GetType("LootingBots.External", false);
                    if (t == null) continue;
                    _prevent = t.GetMethod("PreventBotFromLooting", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                        new[] { typeof(EFT.BotOwner), typeof(float) }, null);
                    MercyPlugin.Log.LogInfo(_prevent != null
                        ? "Looting Bots found: captors are kept from its own looting while they guard or fetch (PreventBotFromLooting)"
                        : "Looting Bots found, but not PreventBotFromLooting(BotOwner, float): no coordination beyond the game's one-move-per-item rule");
                    return;
                }
            }
            catch (System.Exception e) { MercyPlugin.Report("Looting Bots lookup", e); }
        }

        // At most once a second per bot (its own timer does the rest).
        internal static void Hold(EFT.BotOwner o, float seconds)
        {
            if (o == null) return;
            Resolve();
            if (_prevent == null) return;
            float now = UnityEngine.Time.time;
            if (Until.TryGetValue(o, out float u) && u - now > seconds - 1f) return;
            Until[o] = now + seconds;
            if (Until.Count > 64) Until.Clear();
            try { Args[0] = o; Args[1] = seconds; _prevent.Invoke(null, Args); }
            catch (System.Exception e) { MercyPlugin.Report("Looting Bots PreventBotFromLooting", e); }
            finally { Args[0] = null; }
        }

        internal static void Reset() { Until.Clear(); _resolved = false; _prevent = null; }
    }
}
