using System.Collections.Generic;
using Comfort.Common;
using EFT;

namespace Mercy
{
    // 0.29.0: a boss can only think about giving up once every guard he had is CONFIRMED dead.
    //
    // Verified in Assembly-CSharp 0.16.9 (decompile):
    //   - BotsGroup shrinks through RemoveAlly, called from BotsList.Remove: on death AND on a
    //     despawn, so the group's member count can't tell a dead guard from one who left.
    //   - Player.OnDead invokes the static Player.OnPlayerDeadStatic; the unspawn/Dispose path only
    //     invokes OnPlayerDeadOrUnspawn. OnPlayerDeadStatic therefore means "died", never "left".
    //   - BotOwner.Boss (BotBoss).Followers lists a boss's followers; his BotsGroup holds his squad.
    //
    // So: a roster of every man ever seen in his squad (BotsGroup members + BotBoss.Followers), kept
    // when they leave the group, and a guard counts as dead only on OnPlayerDeadStatic or a corpse
    // that still exists with a dead HealthController. Distant, asleep, missing or despawned guards
    // stay "not confirmed dead". A boss whose roster is empty spawned alone and never qualifies.
    internal static class BossSquad
    {
        // Profile ids of AI who died this raid (event-driven, one insert per death).
        private static readonly HashSet<string> Dead = new HashSet<string>();
        private static GameWorld _world;

        // The game's own boss roles (IsPureBoss: boss, not a follower, not a cultist), minus Kaban's
        // sniper, whom the game itself labels a follower (ScavRole/Follower). Followers, Raiders,
        // Rogues and other "boss or follower" roles keep their own rules.
        internal static bool IsSquadBoss(WildSpawnType role) =>
            role.IsPureBoss() && role != WildSpawnType.bossBoarSniper;

        internal static void OnDeath(Player dead, IPlayer killer, EFT.Ballistics.DamageInfo info, EBodyPart part)
        {
            try
            {
                if (dead == null || !dead.IsAI) return;
                CheckRaid();
                Dead.Add(dead.ProfileId);
            }
            catch (System.Exception e) { MercyPlugin.Report("boss squad death", e); }
        }

        // Called on each of the boss's natural checks (every few seconds while he is active):
        // anyone now in his squad joins his roster for the rest of the raid.
        internal static void Track(BotOwner owner, BotHistory h)
        {
            if (h.GuardIds == null) { h.GuardIds = new List<string>(4); h.Guards = new List<Player>(4); }
            Player self = owner.GetPlayer;
            BotsGroup g = owner.BotsGroup;
            if (g != null)
                for (int i = 0; i < g.MembersCount; i++) Add(g.Member(i), self, h);
            List<BotOwner> followers = owner.Boss?.Followers;
            if (followers != null)
                for (int i = 0; i < followers.Count; i++) Add(followers[i], self, h);
        }

        private static void Add(BotOwner m, Player self, BotHistory h)
        {
            Player p = m?.GetPlayer;
            if (p == null || p == self) return;
            string id = p.ProfileId;
            if (string.IsNullOrEmpty(id) || h.GuardIds.Contains(id)) return;
            h.GuardIds.Add(id);
            h.Guards.Add(p);
        }

        // Guards on his roster not confirmed dead (alive, asleep, far away, despawned, unknown).
        internal static int NotConfirmedDead(BotHistory h)
        {
            if (h.GuardIds == null) return 0;
            CheckRaid();
            int n = 0;
            for (int i = 0; i < h.GuardIds.Count; i++)
                if (!ConfirmedDead(h.GuardIds[i], h.Guards[i])) n++;
            return n;
        }

        internal static int RosterSize(BotHistory h) => h.GuardIds?.Count ?? 0;

        private static bool ConfirmedDead(string id, Player p)
        {
            if (Dead.Contains(id)) return true;
            // A corpse still in the world (covers a death before a hot reload subscribed us).
            // A destroyed (despawned) Player compares equal to null and is NOT a death.
            return p != null && p.HealthController != null && !p.HealthController.IsAlive;
        }

        // Ids are unique per raid; a new GameWorld starts a new list.
        private static void CheckRaid()
        {
            GameWorld w = Singleton<GameWorld>.Instance;
            if (w == _world) return;
            _world = w;
            Dead.Clear();
        }

        internal static void Clear()
        {
            Dead.Clear();
            _world = null;
        }
    }
}
