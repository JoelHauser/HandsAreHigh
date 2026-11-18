using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // What a bot carries between surrenders in one raid: his nerve never re-rolls.
    internal sealed class BotHistory
    {
        internal float Nerve;
        internal float CooldownUntil;
        internal float NextEval;
        internal int SquadPeak;
        internal int SquadSeen;
        internal float SquadLossTime = -1000f;
    }

    // Who is surrendered, keyed by the bot's Player so the layer (BotOwner) and the arm
    // pose (Player) can both find the state. Everything here is raid-scoped: a new
    // GameWorld clears it, and so does hot-reload teardown.
    internal static class SurrenderRegistry
    {
        private static readonly Dictionary<Player, SurrenderState> States = new Dictionary<Player, SurrenderState>();
        private static readonly List<Player> Finished = new List<Player>();
        private static readonly Dictionary<string, BotHistory> History = new Dictionary<string, BotHistory>();
        private static GameWorld _world;

        internal static bool Any => States.Count > 0;

        internal static IEnumerable<SurrenderState> All => States.Values;

        internal static bool TryGet(Player bot, out SurrenderState state) => States.TryGetValue(bot, out state);

        // The layer keeps the bot until he's rearmed and his arms are fully down.
        internal static bool HoldsBot(Player bot) => bot != null && States.ContainsKey(bot);

        // New raid (or back in the menu): forget everything from the last one.
        internal static void EnsureRaid()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == _world) return;
            if (States.Count > 0 || History.Count > 0)
                HandsAreHighPlugin.Log.LogInfo("New raid: surrender state cleared");
            _world = world;
            UnhookAll();
            States.Clear();
            Finished.Clear();
            History.Clear();
        }

        internal static BotHistory HistoryOf(Player bot)
        {
            string id = bot.ProfileId;
            if (!History.TryGetValue(id, out BotHistory h))
            {
                h = new BotHistory { Nerve = Settings.RollNerve(), NextEval = Time.time + Random.value * Settings.EvalInterval.Value };
                History[id] = h;
            }
            return h;
        }

        internal static void Surrender(Player bot, Player captor, string reason)
        {
            if (bot == null || captor == null || !Settings.Enabled.Value) return;
            EnsureRaid();

            if (States.TryGetValue(bot, out SurrenderState existing))
            {
                // Found again while retreating or hiding: hands go back up.
                existing.Captor = captor;
                existing.RaiseArms();
                existing.Order = Order.None;
                existing.TargetWeapon = null;
                existing.UnwatchedSince = -1f;
                existing.CaptorHiddenSince = -1f;
                existing.CaptorLastSeen = captor.Position;
                existing.WasHit = false;
                existing.AimedBy = null;
                existing.SetPhase(Phase.HandsUp, reason);
                bot.AIData?.BotOwner?.StopMove();
                StartDrop(existing);
                Voice.Say(existing, EPhraseTrigger.HoldFire);
                return;
            }

            BotHistory h = HistoryOf(bot);
            var s = new SurrenderState
            {
                Bot = bot,
                Name = bot.Profile?.Nickname ?? bot.ProfileId,
                Captor = captor,
                Seed = Random.value * 100f,
                RolledNerve = h.Nerve,
                CaptorLastSeen = captor.Position,
                DropPosition = bot.Position,
                LastPosition = bot.Position
            };
            // Hits only: bleeding lowers health too, and must not count as being shot.
            s.HitHandler = (info, part, absorbed) => s.WasHit = true;
            bot.BeingHitAction += s.HitHandler;
            States[bot] = s;
            HandsAreHighPlugin.Log.LogInfo(
                $"Surrender: {s.Name} ({bot.Profile?.Info?.Settings?.Role}), nerve {s.Nerve:0.00}, to {captor.Profile?.Nickname}: {reason}");
            s.SetPhase(Phase.HandsUp, reason);

            BotOwner owner = bot.AIData?.BotOwner;
            HandsAreHighPlugin.Try("stop shooting", () => owner?.ShootData?.EndShoot());
            HandsAreHighPlugin.Try("stop sprinting", () => owner?.Sprint(false));
            StartDrop(s);
            Voice.Say(s, EPhraseTrigger.HoldFire);
        }

        // Empties his hands: a gun goes on the ground, a knife goes back in its sheath.
        // The arms only rise once nothing is left in his hands (Tick).
        internal static void StartDrop(SurrenderState s)
        {
            s.DropAttempts = 1;
            s.DropSince = Time.time;
            s.DropPending = !Arsenal.HandsFree(s.Bot);
            if (!s.DropPending) return;

            HandsAreHighPlugin.Try("drop weapon in hands", () =>
            {
                var dropped = Arsenal.EmptyHands(s.Bot);
                if (dropped != null)
                {
                    s.DroppedWeapon = dropped;
                    s.DropPosition = s.Bot.Position;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: drops {dropped.ShortName.Localized()}");
                }
            });
        }

        // Debug: act as if every captor just walked away.
        internal static void ForceCaptorLeft()
        {
            foreach (SurrenderState s in States.Values)
                if (s.Phase == Phase.HandsUp) s.ForcedLeave = true;
        }

        // Debug: give every surrendered bot an opening right now.
        internal static void ForceOpening()
        {
            foreach (SurrenderState s in States.Values)
                if (s.Phase == Phase.HandsUp) s.ForcedOpening = true;
        }

        // A held bot was shot or killed: others with their hands up who can see it run.
        internal static void Witness(Vector3 at, Player victim, string what)
        {
            if (!Settings.LocalReactions.Value) return;
            float r = Settings.WitnessRadius.Value;
            foreach (SurrenderState s in States.Values)
            {
                if (s.Bot == null || s.Bot == victim || s.Phase != Phase.HandsUp || s.Suspended) continue;
                if ((s.Bot.Position - at).sqrMagnitude > r * r) continue;
                if (!Perception.LineClear(Perception.Eye(s.Bot), at + Vector3.up * 1.2f)) continue;
                float roll = Random.value;
                if (roll >= Settings.WitnessEscape.Value) continue;
                s.BeginRetreat($"saw {what}", emergency: true);
            }
        }

        // Called once a frame: sleep/wake, arm blending, deaths and cleanup.
        // Behaviour itself lives in SurrenderLogic.
        internal static void Tick(float dt)
        {
            EnsureRaid();
            Finished.Clear();
            bool enabled = Settings.Enabled.Value;

            foreach (KeyValuePair<Player, SurrenderState> kv in States)
            {
                Player bot = kv.Key;
                SurrenderState s = kv.Value;
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                {
                    if (s.Phase == Phase.HandsUp)
                    {
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: killed with his hands up");
                        Witness(s.LastPosition, bot, "a surrendered man killed");
                    }
                    Finished.Add(bot);
                    continue;
                }
                s.LastPosition = bot.Position;

                if (!enabled && s.Phase != Phase.Rearmed)
                {
                    s.LowerArms(Settings.LowerSeconds.Value);
                    s.SetPhase(Phase.Rearmed, "mod disabled");
                }

                UpdateSuspension(bot, s);

                if (s.ArmsUp)
                {
                    // Raise only once the hands are empty, so he never poses holding a gun.
                    if (!s.DropPending)
                        s.Progress += dt / Mathf.Max(0.01f, Settings.RaiseSeconds.Value);
                }
                else
                {
                    s.Progress -= dt / Mathf.Max(0.01f, s.LowerSeconds > 0f ? s.LowerSeconds : Settings.LowerSeconds.Value);
                }
                s.Progress = Mathf.Clamp01(s.Progress);
                s.Weight = Mathf.SmoothStep(0f, 1f, s.Progress);

                if (s.Phase == Phase.Rearmed && s.Progress <= 0f) Finished.Add(bot);
            }

            foreach (Player p in Finished)
            {
                if (States.TryGetValue(p, out SurrenderState s) && p != null)
                {
                    if (s.Phase == Phase.Rearmed)
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: back to normal AI");
                    HistoryOf(p).CooldownUntil = Time.time + Settings.SurrenderCooldown.Value;
                }
                if (States.TryGetValue(p, out SurrenderState gone)) Unhook(gone);
                States.Remove(p);
            }
        }

        // Every state that leaves the registry must come through here, or the old
        // handler stays on the Player (and survives a hot reload).
        private static void Unhook(SurrenderState s)
        {
            if (s.HitHandler == null) return;
            object bot = s.Bot;   // a destroyed Player is still a live C# object; its event can be cleared
            if (bot != null) HandsAreHighPlugin.Try("unhook hit event", () => s.Bot.BeingHitAction -= s.HitHandler);
            s.HitHandler = null;
        }

        private static void UnhookAll()
        {
            foreach (SurrenderState s in States.Values) Unhook(s);
        }

        // ORBIT's Ghost Mode (or anything else) can put a bot to sleep. While asleep he
        // can't fetch anything, so the claim on a gun goes; on waking he decides again.
        private static void UpdateSuspension(Player bot, SurrenderState s)
        {
            if (!Settings.PauseWhenAsleep.Value) return;
            BotOwner owner = bot.AIData?.BotOwner;
            bool awake = owner != null && owner.BotState == EBotState.Active && bot.gameObject.activeInHierarchy;

            if (!awake && !s.Suspended)
            {
                s.Suspended = true;
                s.TargetWeapon = null;
                s.DropQueue.Clear();
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: asleep ({s.Phase}), plans dropped");
            }
            else if (awake && s.Suspended)
            {
                s.Suspended = false;
                if (s.Phase != Phase.HandsUp && s.Phase != Phase.Rearmed)
                {
                    s.RegroupUntil = Time.time + 1f;
                    s.SetPhase(Phase.Regrouping, "woke up");
                }
                else
                {
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: woke up ({s.Phase})");
                }
            }
        }

        // Hot-reload teardown: release every held bot so the old assembly's layer goes
        // inert. A bot held at that moment stays unarmed and returns to normal AI.
        internal static void Clear()
        {
            if (States.Count > 0)
                HandsAreHighPlugin.Log?.LogInfo($"Teardown: releasing {States.Count} surrendered bot(s)");
            UnhookAll();
            States.Clear();
            Finished.Clear();
            History.Clear();
            _world = null;
        }
    }
}
