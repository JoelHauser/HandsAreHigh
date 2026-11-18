using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // Who is surrendered. Keyed by the bot's Player so the layer (BotOwner) and the
    // arm pose (Player) can both find the state.
    internal static class SurrenderRegistry
    {
        private static readonly Dictionary<Player, SurrenderState> States = new Dictionary<Player, SurrenderState>();
        private static readonly List<Player> Finished = new List<Player>();

        internal static bool Any => States.Count > 0;

        internal static IEnumerable<SurrenderState> All => States.Values;

        internal static bool TryGet(Player bot, out SurrenderState state) => States.TryGetValue(bot, out state);

        // The layer keeps the bot until he's rearmed and his arms are fully down.
        internal static bool HoldsBot(Player bot) => bot != null && States.ContainsKey(bot);

        internal static void Surrender(Player bot, Player captor)
        {
            if (bot == null || captor == null) return;

            if (States.TryGetValue(bot, out SurrenderState existing))
            {
                // Found again while retreating or hiding: hands go back up.
                existing.Captor = captor;
                existing.RaiseArms();
                existing.UnwatchedSince = -1f;
                existing.CaptorGoneSince = -1f;
                existing.SetPhase(Phase.HandsUp);
                bot.AIData?.BotOwner?.StopMove();
                return;
            }

            var s = new SurrenderState
            {
                Bot = bot,
                Captor = captor,
                Seed = Random.value * 100f,
                Nerve = Random.value,
                LastHealth = Arsenal.Health(bot),
                CaptorLastSeen = captor.Position,
                DropPosition = bot.Position
            };
            s.SetPhase(Phase.HandsUp);
            States[bot] = s;
            HandsAreHighPlugin.Log.LogInfo(
                $"Surrender: {bot.Profile?.Nickname} ({bot.Profile?.Info?.Settings?.Role}), nerve {s.Nerve:0.00}");

            HandsAreHighPlugin.Try("stop shooting", () => bot.AIData?.BotOwner?.ShootData?.EndShoot());
            HandsAreHighPlugin.Try("drop weapon in hands", () => s.DroppedWeapon = Arsenal.DropWeaponInHands(bot));
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

        // Called once a frame: arm blending and cleanup. Behaviour lives in SurrenderLogic.
        internal static void Tick(float dt)
        {
            Finished.Clear();
            foreach (KeyValuePair<Player, SurrenderState> kv in States)
            {
                Player bot = kv.Key;
                SurrenderState s = kv.Value;
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                {
                    Finished.Add(bot);
                    continue;
                }

                if (s.ArmsUp)
                {
                    // Raise only once the hands are empty, so the arms don't fight the grip.
                    if (Arsenal.HandsEmpty(bot) || Time.time - s.PhaseSince > 1f)
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
                if (p != null && States.TryGetValue(p, out SurrenderState s) && s.Phase == Phase.Rearmed)
                    HandsAreHighPlugin.Log.LogInfo($"{p.Profile?.Nickname}: back to normal AI");
                States.Remove(p);
            }
        }
    }
}
