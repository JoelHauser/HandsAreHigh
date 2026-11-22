using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Mercy
{
    // Optional (off by default): your character says a line from your own voice when you give
    // an order from the menu. The menu stays authoritative: OrderMenu.Give applies the order
    // once, in the same frame, whether a line plays or not; nothing waits for the audio.
    //
    // Played with Player.Speaker.Play, never Player.Say: Say raises
    // GlobalEventDispatcher.SayPhrase (vanilla friendly bots within 10 m react to commands
    // through BotReceiver) and is what our own Stop-demand postfix (SayPatch) listens to.
    // Speaker.Play only picks a clip and plays it (Player.OnPhraseTold -> PlayPhraseClip;
    // ClientPlayer also sends it to the server as a phrase packet, as any phrase does). So a
    // spoken order can't make a Stop demand or move other bots.
    //
    // Lines by order, from the phrase banks in the 0.16.9 voice bundles (19 Scav/BEAR/USEC
    // voices). The UI label stays exact; the spoken line is the nearest one the game has:
    //   Drop your weapons  Stop                                   every voice
    //   Drop your kit      none fitting                           silent
    //   Armour off         none fitting                           silent
    //   Follow me          FollowMe; Regroup                      FollowMe every voice; Regroup BEAR/USEC
    //   Get down           GetInCover (approximate: "take cover") every voice but scav_6
    //   Stay there         HoldPosition; Stop                     HoldPosition BEAR/USEC; Stop every voice
    //   Get lost           GetBack; OnYourOwn; Gogogo             BEAR/USEC only; Scav voices silent
    //   Hands on your head Stop (no fitting line)                 every voice
    //   Turn around        Look (Attention); Stop                 every voice
    // Your own voice decides faction and language (bear_1 Russian, bear_1_eng English...).
    internal static class CommandVoice
    {
        private static readonly Dictionary<OrderKind, EPhraseTrigger[]> Map = new Dictionary<OrderKind, EPhraseTrigger[]>
        {
            { OrderKind.DropWeapons, new[] { EPhraseTrigger.Stop } },
            { OrderKind.DropKit, new EPhraseTrigger[0] },
            { OrderKind.ArmourOff, new EPhraseTrigger[0] },
            { OrderKind.Follow, new[] { EPhraseTrigger.FollowMe, EPhraseTrigger.Regroup } },
            { OrderKind.Kneel, new[] { EPhraseTrigger.GetInCover } },
            { OrderKind.Stay, new[] { EPhraseTrigger.HoldPosition, EPhraseTrigger.Stop } },
            { OrderKind.Stand, new[] { EPhraseTrigger.GoForward, EPhraseTrigger.Gogogo } },   // BEAR/USEC only
            { OrderKind.Leave, new[] { EPhraseTrigger.GetBack, EPhraseTrigger.OnYourOwn, EPhraseTrigger.Gogogo } },
            // 0.16.0: no voice has a "hands on your head" or "turn around" line; the nearest
            // commands every voice has (Stop; Look = the Attention bank).
            { OrderKind.HandsOnHead, new[] { EPhraseTrigger.Stop } },
            { OrderKind.HandsUp, new[] { EPhraseTrigger.Stop } },
            { OrderKind.TurnAround, new[] { EPhraseTrigger.Look, EPhraseTrigger.Stop } },
        };

        private static EPhraseTrigger _last = EPhraseTrigger.None;
        private static readonly List<EPhraseTrigger> Options = new List<EPhraseTrigger>(4);

        internal static void Reset() => _last = EPhraseTrigger.None;

        internal static void Speak(Player me, OrderKind order)
        {
            if (!Settings.CommandSpeech.Value) return;
            Say(me, order);
        }

        // A bot captor's order lines: CaptorVoice (queued, per-bot gap, waits for a free speaker).

        private static void Say(Player me, OrderKind order)
        {
            if (me?.Speaker == null || !Perception.Alive(me)) return;
            BaseSpeaker speaker = me.Speaker;
            string voice = speaker.PlayerVoice;

            Options.Clear();
            if (Map.TryGetValue(order, out EPhraseTrigger[] lines))
                foreach (EPhraseTrigger l in lines)
                    if (Voice.Has(speaker, l)) Options.Add(l);
            if (Options.Count == 0)
            {
                Note($"order {order}: no fitting line in your voice {voice}, given silently");
                return;
            }
            if (Options.Count > 1) Options.Remove(_last);

            ETagStatus tags = me.HealthStatus | ETagStatus.Combat;
            while (Options.Count > 0)
            {
                int i = Dice.Range(0, Options.Count);
                EPhraseTrigger line = Options[i];
                Options.RemoveAt(i);
                TagBank played = speaker.Play(line, tags, true);
                if (played == null) continue;
                _last = line;
                Note($"order {order}: you say {line} ({voice})");
                return;
            }
            Note($"order {order}: your voice {voice} couldn't play a line now (already speaking?), order given silently");
        }

        private static void Note(string msg)
        {
            if (Settings.LogVoice.Value) MercyPlugin.Log.LogInfo(msg);
        }
    }
}
