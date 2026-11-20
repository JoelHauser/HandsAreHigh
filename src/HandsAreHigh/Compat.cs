using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
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
        private const string Source = "HandsAreHigh";
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
                HandsAreHighPlugin.Log.LogInfo(_setSleepAllowed != null
                    ? "ORBIT found: bot-held encounters can be kept awake (SetBotSleepAllowed)"
                    : "ORBIT sleep API not found (not installed, or older than 3.1.1): encounters are paused while asleep");
            }
            catch (Exception e) { HandsAreHighPlugin.Log.LogWarning($"ORBIT lookup failed: {e.Message}"); }
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
                HandsAreHighPlugin.Log.LogInfo($"ORBIT: {(keep ? "sleep blocked" : "sleep allowed again")} for {Restraint.Name(o.GetPlayer)} ({why}){(ok ? "" : ", not accepted")}");
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

namespace HandsAreHigh
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
                    HandsAreHighPlugin.Log.LogInfo(_prevent != null
                        ? "Looting Bots found: captors are kept from its own looting while they guard or fetch (PreventBotFromLooting)"
                        : "Looting Bots found, but not PreventBotFromLooting(BotOwner, float): no coordination beyond the game's one-move-per-item rule");
                    return;
                }
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("Looting Bots lookup", e); }
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
            catch (System.Exception e) { HandsAreHighPlugin.Report("Looting Bots PreventBotFromLooting", e); }
            finally { Args[0] = null; }
        }

        internal static void Reset() { Until.Clear(); _resolved = false; _prevent = null; }
    }
}
