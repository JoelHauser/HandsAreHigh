using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Mercy
{
    // Optional (off by default): the player says Stop to the bot under his crosshair.
    //
    // How the game routes it (verified): the gesture/phrase menu and its quick binds call
    // GamePlayerOwner.PlayPhraseOrGesture -> Player.Say(phrase, demand: true, ...), which plays
    // the player's own voice line and raises GlobalEventDispatcher.SayPhrase (vanilla friendly
    // bots react to Stop within 10 m through BotReceiver). A postfix on Player.Say for the
    // player's own character and EPhraseTrigger.Stop is the demand. No subclass overrides Say.
    // The optional key just calls the same Player.Say(Stop), so both paths are one.
    //
    // A demand only reaches the one bot under the crosshair, in range, hostile, with a clear
    // line between you (he can hear and see you). It brings his surrender check forward and
    // adds DemandPressure for that one check; it never forces compliance. Per-bot cooldown.
    [HarmonyPatch(typeof(Player), nameof(Player.Say))]
    internal static class SayPatch
    {
        private static void Postfix(Player __instance, EPhraseTrigger phrase)
        {
            if (phrase != EPhraseTrigger.Stop || !Settings.DemandEnabled.Value || !Settings.Enabled.Value) return;
            if (__instance == null || __instance.IsAI || __instance != Singleton<GameWorld>.Instance?.MainPlayer) return;
            MercyPlugin.Try("stop demand", () => StopDemand.Demand(__instance));
        }
    }

    internal static class StopDemand
    {
        // The optional key: the player's character says the game's Stop line (his own voice).
        internal static void Shout(Player me)
        {
            if (!Settings.DemandEnabled.Value || me == null || !Perception.Alive(me)) return;
            me.Say(EPhraseTrigger.Stop, true, 0f, (ETagStatus)0, 100, false);
        }

        internal static void Demand(Player me)
        {
            float range = Settings.DemandRange.Value;
            Player bot = Targeting.BotUnderCrosshair(me, range);
            string outcome;
            if (bot == null) outcome = "no bot under your crosshair in range";
            else if (!Perception.Alive(bot)) outcome = "dead";
            else if (!NaturalSurrender.Eligible(bot)) outcome = $"can't surrender ({bot.Profile?.Info?.Settings?.Role})";
            else if (SurrenderRegistry.HoldsBot(bot)) outcome = "already surrendered";
            else outcome = Accept(me, bot);
            MercyPlugin.Log.LogInfo($"Stop demand: target {Targeting.Describe(bot)}: {outcome}");
        }

        private static string Accept(Player me, Player bot)
        {
            BotOwner owner = bot.AIData?.BotOwner;
            if (owner == null || owner.BotState != EBotState.Active) return "not active";
            if (!Suppression.IsHostile(owner, me)) return "not hostile to you";
            if (!Perception.LineClear(Perception.Eye(bot), Perception.Eye(me))) return "can't see or hear you clearly (blocked)";

            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            float now = Time.time;
            if (now < h.DemandCooldownUntil) return $"told already, cooldown {h.DemandCooldownUntil - now:0.0} s";
            if (now < h.CooldownUntil) return "just went back to fighting, not yet";

            h.DemandBy = me;
            h.DemandUntil = now + Settings.DemandWindow.Value;
            h.DemandCooldownUntil = now + Settings.DemandCooldown.Value;
            h.NextEval = now;   // evaluated on the next frame, with the demand
            return "accepted, evaluating now";
        }
    }
}
