using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
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

        // Incoming fire (Suppression.cs): shots at him, faded by half-life from FireAt.
        internal float Fire;
        internal float FireAt;
        internal float NearMissAt = -1000f;

        // Stop demand (StopDemand.cs): who demanded, until when it weighs, and the cooldown.
        internal Player DemandBy;
        internal float DemandUntil;
        internal float DemandCooldownUntil;
        internal float PromptAt = -1000f;     // last time a hit / near miss brought a check forward

        // Cached kit assessment (Arsenal.AssessKit), refreshed every KitRecheck seconds.
        internal float KitAt = -1000f;
        internal float KitValue = -1f;     // handbook roubles; -1 = no handbook, neutral
        internal float Protection;         // best armour class worn / 6
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
                // Found again while retreating or hiding: hands go back up. Stopped mid-scramble,
                // he won't try that again straight away (no flip-flopping).
                if (existing.Scrambling)
                    existing.ScrambleCooldownUntil = Time.time + Settings.ScrambleCooldown.Value;
                existing.Captor = captor;
                existing.RaiseArms();
                existing.Order = Order.None;
                existing.TargetWeapon = null;
                existing.UnwatchedSince = -1f;
                existing.CaptorHiddenSince = -1f;
                existing.CaptorLastSeen = captor.Position;
                existing.WasHit = false;
                existing.AimedBy = null;
                existing.DebugOpeningUntil = 0f;
                existing.FaceSet = false;
                existing.SetPhase(Phase.HandsUp, reason);
                if (existing.Moving) { bot.AIData?.BotOwner?.StopMove(); existing.Moving = false; }
                StartDrop(existing);
                Voice.Say(existing, VoiceEvent.Surrender);
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
            Voice.Say(s, VoiceEvent.Surrender);
        }

        // Empties his hands at once: a gun goes on the ground, a knife goes back in its sheath,
        // anything else (meds, a grenade) is put away. The game's callback (hands empty) is what
        // lets the arms rise, the same frame. Timeline logged when the arms are fully up (Tick).
        internal static void StartDrop(SurrenderState s)
        {
            s.TStart = Time.realtimeSinceStartup;
            s.TDropRequested = s.THandsEmpty = s.TThrown = s.TInterrupt = -1f;
            s.HandsAtStart = Arsenal.DescribeHands(s.Bot);
            s.TimingLogged = false;
            s.DropAttempts = 0;
            s.DropSince = Time.time;
            s.HandsPending = false;
            s.DropPending = !Arsenal.HandsEmpty(s.Bot);
            if (!s.DropPending) { s.THandsEmpty = s.TStart; return; }
            RequestDrop(s);
        }

        // Is this callback still about the request that is current for this bot?
        private static bool Current(SurrenderState s, int token) =>
            s.Bot != null && States.TryGetValue(s.Bot, out SurrenderState now) && now == s && s.HandsToken == token;

        // One request at a time: never while the previous one is unresolved (CheckDrop decides
        // when an unanswered one is given up). A magazine reload in progress holds the hide
        // until it ends (BaseReloadOperation.HideWeapon only stores the callback), so it is
        // finished instantly with the game's own FastForwardCurrentState, which then runs
        // the stored hide (ReloadExternalMagOperation.SwitchToIdlingState).
        internal static void RequestDrop(SurrenderState s)
        {
            if (s.HandsPending || s.Bot == null) return;
            int token = ++s.HandsToken;
            s.HandsPending = true;
            s.HandsSince = Time.time;
            s.DropAttempts++;
            if (s.TDropRequested < 0f) s.TDropRequested = Time.realtimeSinceStartup;

            HandsAreHighPlugin.Try("drop weapon in hands", () =>
            {
                Item dropped = Arsenal.EmptyHands(s.Bot,
                    onEmpty: () =>
                    {
                        if (!Current(s, token)) return;
                        s.HandsPending = false;
                        if (s.THandsEmpty < 0f) s.THandsEmpty = Time.realtimeSinceStartup;
                        s.DropPending = false;
                    },
                    onFailed: () =>
                    {
                        if (!Current(s, token)) return;
                        s.HandsPending = false;
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: the game refused the hands change ({Arsenal.DescribeHands(s.Bot)})");
                    },
                    onThrown: ok =>
                    {
                        if (ok) s.TThrown = Time.realtimeSinceStartup;
                        else HandsAreHighPlugin.Log.LogWarning($"{s.Name}: the game refused to throw his gun; it went back on his body");
                    });
                if (dropped != null)
                {
                    s.DroppedWeapon = dropped;
                    s.DropPosition = s.Bot.Position;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: drops {dropped.ShortName.Localized()} (hands were: {s.HandsAtStart}, request {s.DropAttempts})");
                }
                CutReload(s);
            });
        }

        internal static void CutReload(SurrenderState s)
        {
            if (!Settings.FinishReloadInstantly.Value || s.TInterrupt >= 0f) return;
            if (!(s.Bot?.HandsController is Player.FirearmController fc) || !fc.IsInReloadOperation()) return;
            s.TInterrupt = Time.realtimeSinceStartup;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: mid-reload: the reload is finished instantly so the gun can drop now");
            HandsAreHighPlugin.Try("finish reload", () => s.Bot.HandsController.FastForwardCurrentState());
        }

        // CheckDrop: the outstanding request got no answer in time. Forget it (its callback
        // will be ignored) so a fresh one can be made.
        internal static void AbandonRequest(SurrenderState s)
        {
            s.HandsToken++;
            s.HandsPending = false;
        }

        // Debug keys: one bot only, resolved by the caller from the crosshair.
        internal static string DebugLeave(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase}), nothing to leave";
            s.ForcedLeave = true;
            return "acts as if you left";
        }

        internal static string DebugOpening(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase}), no opening to give";
            float secs = Settings.DebugOpeningSeconds.Value;
            s.DebugOpeningUntil = Time.time + secs;
            // The opening has to last OpeningDelay like a real one; make the first roll due then.
            s.UnwatchedSince = Time.time;
            s.NextBreakRoll = 0f;
            return $"not covered for {secs:0} s (you can keep aiming, it doesn't count); his own rolls decide";
        }

        internal static string DebugForceRun(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            s.ForceRun = true;
            return "runs now (forced, no roll)";
        }

        internal static string DebugForceGrab(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            if (s.DroppedWeapon == null) return "didn't drop a gun";
            s.ForceGrab = true;
            return "goes for his gun now (forced, no roll); the log says if he can't";
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
                Voice.Tick(s);

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

                if (s.ArmsUp && s.Progress >= 1f && !s.TimingLogged && s.TStart > 0f)
                {
                    s.TimingLogged = true;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: hands up. Timeline from surrender (hands were: {s.HandsAtStart}): " +
                        $"drop asked {Ms(s, s.TDropRequested)}, reload cut {Ms(s, s.TInterrupt)}, " +
                        $"hands empty {Ms(s, s.THandsEmpty)} (game hide animation), gun on the ground {Ms(s, s.TThrown)}, " +
                        $"arms up {Ms(s, Time.realtimeSinceStartup)} (raise {Settings.RaiseSeconds.Value:0.00} s), requests {s.DropAttempts}");
                }

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

        private static string Ms(SurrenderState s, float t) =>
            t < 0f ? "n/a" : $"+{(t - s.TStart) * 1000f:0} ms";

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
