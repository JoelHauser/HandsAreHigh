using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // What a bot that holds (or held, or deals with) a prisoner says. Same playback path as the
    // prisoner's lines (Voice.cs): Player.Speaker.Play from the bot's own speaker, so the game's
    // spatial audio, his own voice (faction, language) and his own tag mask; never BotTalk.Say /
    // Player.Say, which SAIN's talk patches swallow and which raise GlobalEventDispatcher events.
    //
    // Coordination (SAIN 4.5.1 PlayerComponent.PlayVoiceLine, Pit Fireteam 1.1.2 BotTalk.TrySay):
    // all of them refuse to start a line while the speaker is Speaking or Busy, and so do we, so
    // a bot never talks over himself and SAIN's own squad/enemy talk is never replaced. SAIN only
    // talks with a human within 150 m (CanTalk/PlayerInEarshot); the game itself plays no audio
    // out of the player's earshot, which Verify reports. Pit Fireteam teammates never become
    // captors, so their dialogue is untouched.
    //
    // One pending line per bot: a more urgent one replaces it, urgent lines (a new threat, a
    // prisoner going for a gun) clear anything queued and skip the line gap; nothing waits on a
    // line: the order, the move, the fight all happen at once whether audio plays or not.
    internal enum CaptorLine { Demand, Stay, Disarm, Follow, Leave, Warn, Resist, Threat, UnderFire, TakeCover, HandOver, TakeOver, Abandon, LostSight }

    internal static class CaptorVoice
    {
        private sealed class Speaker
        {
            internal Player Bot;
            internal CaptorLine? Pending;
            internal VoicePriority Priority;
            internal float At, Until;
            internal float NextNormal;
            internal float SpokeAt = -1000f;
            internal EPhraseTrigger Last = EPhraseTrigger.None;
        }

        private static readonly Dictionary<Player, Speaker> Speakers = new Dictionary<Player, Speaker>();
        private static readonly List<Player> Done = new List<Player>(4);
        private static readonly List<EPhraseTrigger> Options = new List<EPhraseTrigger>(6);

        // Line tables from the 0.16.9 phrase bundles (19 voices, read with UnityPy 2026-11-19):
        // every voice: Stop, Fire, Look (bank "Attention"), Covering, CoverMe, FollowMe, Spreadout, Negative,
        //   Roger, Going, OnFirstContact, OnRepeatedContact, OnSix, InTheFront, OnLostVisual,
        //   LostVisual; GetInCover all but scav_6; OnFight all Scav, 4 of 13 PMC.
        // BEAR/USEC only: HoldFire, HoldPosition, GetBack, OnYourOwn, Regroup, CheckHim.
        // A missing line is skipped (silence), the first table first, then the fallback.
        private static readonly EPhraseTrigger[] LDemand = { EPhraseTrigger.Stop, EPhraseTrigger.Look };
        private static readonly EPhraseTrigger[] LStay = { EPhraseTrigger.HoldPosition, EPhraseTrigger.Stop };
        private static readonly EPhraseTrigger[] LDisarm = { EPhraseTrigger.Stop, EPhraseTrigger.Look };
        private static readonly EPhraseTrigger[] LFollow = { EPhraseTrigger.FollowMe, EPhraseTrigger.Regroup };
        private static readonly EPhraseTrigger[] LLeave = { EPhraseTrigger.GetBack, EPhraseTrigger.OnYourOwn };
        private static readonly EPhraseTrigger[] LLeaveScav = { EPhraseTrigger.Spreadout, EPhraseTrigger.Going };
        private static readonly EPhraseTrigger[] LWarn = { EPhraseTrigger.Stop };
        private static readonly EPhraseTrigger[] LResist = { EPhraseTrigger.Fire, EPhraseTrigger.OnFight };
        private static readonly EPhraseTrigger[] LThreat = { EPhraseTrigger.OnFirstContact, EPhraseTrigger.OnRepeatedContact, EPhraseTrigger.InTheFront };
        private static readonly EPhraseTrigger[] LThreatBehind = { EPhraseTrigger.OnSix };
        private static readonly EPhraseTrigger[] LUnderFire = { EPhraseTrigger.GetInCover, EPhraseTrigger.CoverMe };
        private static readonly EPhraseTrigger[] LTakeCover = { EPhraseTrigger.GetInCover, EPhraseTrigger.CoverMe };
        private static readonly EPhraseTrigger[] LHandOver = { EPhraseTrigger.CoverMe, EPhraseTrigger.CheckHim };
        private static readonly EPhraseTrigger[] LTakeOver = { EPhraseTrigger.Covering, EPhraseTrigger.Roger };
        private static readonly EPhraseTrigger[] LAbandon = { EPhraseTrigger.OnYourOwn, EPhraseTrigger.Negative, EPhraseTrigger.Going };
        private static readonly EPhraseTrigger[] LLostSight = { EPhraseTrigger.OnLostVisual, EPhraseTrigger.LostVisual };

        private static VoicePriority PriorityOf(CaptorLine l)
        {
            switch (l)
            {
                case CaptorLine.Resist:
                case CaptorLine.Threat:
                case CaptorLine.UnderFire: return VoicePriority.Urgent;
                case CaptorLine.Warn:
                case CaptorLine.TakeCover:
                case CaptorLine.Abandon: return VoicePriority.High;
                default: return VoicePriority.Normal;
            }
        }

        private static EPhraseTrigger[] Lines(CaptorLine l, BaseSpeaker sp, out EPhraseTrigger[] fallback)
        {
            fallback = null;
            switch (l)
            {
                case CaptorLine.Demand: return LDemand;
                case CaptorLine.Stay: return LStay;
                case CaptorLine.Disarm: return LDisarm;
                case CaptorLine.Follow: return LFollow;
                case CaptorLine.Leave: fallback = LLeaveScav; return LLeave;
                case CaptorLine.Warn: return LWarn;
                case CaptorLine.Resist: fallback = LWarn; return LResist;
                case CaptorLine.Threat: return LThreat;
                case CaptorLine.UnderFire: return LUnderFire;
                case CaptorLine.TakeCover: return LTakeCover;
                case CaptorLine.HandOver: return LHandOver;
                case CaptorLine.TakeOver: return LTakeOver;
                case CaptorLine.Abandon: return LAbandon;
                default: return LLostSight;
            }
        }

        internal static CaptorLine ForOrder(OrderKind o)
        {
            switch (o)
            {
                case OrderKind.DropWeapons:
                case OrderKind.DropKit:
                case OrderKind.ArmourOff: return CaptorLine.Disarm;
                case OrderKind.Follow: return CaptorLine.Follow;
                case OrderKind.Leave: return CaptorLine.Leave;
                default: return CaptorLine.Stay;
            }
        }

        internal static bool Any => Speakers.Count > 0;

        // Queue a line for this bot. behind: the threat is behind him (OnSix instead of contact).
        internal static void Say(Player bot, CaptorLine line, float delay = 0f, bool behind = false)
        {
            if (!Settings.CaptorSpeech.Value || !Settings.BotVoice.Value || bot == null || !Perception.Alive(bot)) return;
            if (!Speakers.TryGetValue(bot, out Speaker sp)) { sp = new Speaker { Bot = bot }; Speakers[bot] = sp; }
            float now = Time.time;
            VoicePriority pr = PriorityOf(line);
            if (pr <= VoicePriority.Normal && now < sp.NextNormal) { Log(bot, $"{line}: holds it (line gap {sp.NextNormal - now:0.0} s)", verbose: true); return; }
            if (sp.Pending != null && sp.Priority > pr) return;
            sp.Pending = line;
            sp.Priority = pr;
            sp.At = now + Mathf.Max(0f, delay);
            sp.Until = sp.At + (pr == VoicePriority.Urgent ? 1.5f : 3f);
            _behind[bot] = behind;
        }

        private static readonly Dictionary<Player, bool> _behind = new Dictionary<Player, bool>();

        // Urgent combat drops whatever this bot was about to say (an order line no longer fits).
        internal static void Interrupt(Player bot)
        {
            if (bot != null && Speakers.TryGetValue(bot, out Speaker sp) && sp.Priority < VoicePriority.Urgent) sp.Pending = null;
        }

        // Is this bot talking, or did he just finish? (Prisoners wait for their captor's line.)
        internal static bool Talking(Player bot)
        {
            if (bot?.Speaker == null) return false;
            if (bot.Speaker.Speaking || bot.Speaker.Busy) return true;
            return Speakers.TryGetValue(bot, out Speaker sp) && Time.time - sp.SpokeAt < 0.6f;
        }

        internal static void Tick()
        {
            if (Speakers.Count == 0) return;
            float now = Time.time;
            Done.Clear();
            foreach (KeyValuePair<Player, Speaker> kv in Speakers)
            {
                Speaker sp = kv.Value;
                if (sp.Bot == null || !Perception.Alive(sp.Bot)) { Done.Add(kv.Key); continue; }
                if (sp.Pending == null)
                {
                    if (now - sp.SpokeAt > 30f) Done.Add(kv.Key);   // idle: forget him (re-made on the next line)
                    continue;
                }
                if (now < sp.At) continue;
                CaptorLine line = sp.Pending.Value;
                if (now > sp.Until)
                {
                    sp.Pending = null;
                    Log(sp.Bot, $"{line}: no chance to speak in time (speaker busy), skipped", verbose: false);
                    continue;
                }
                BaseSpeaker speaker = sp.Bot.Speaker;
                if (speaker == null) { sp.Pending = null; continue; }
                if (speaker.Speaking || speaker.Busy) continue;            // his own, SAIN's or anyone's line
                if (sp.Priority < VoicePriority.Urgent && PrisonerTalking(sp.Bot)) continue;
                Play(sp, line, speaker, now);
            }
            for (int i = 0; i < Done.Count; i++) { Speakers.Remove(Done[i]); _behind.Remove(Done[i]); }
            Done.Clear();
        }

        // His prisoner is mid-sentence: a non-urgent line waits (turn-taking, no talking over).
        private static bool PrisonerTalking(Player captor)
        {
            if (!Captors.TryGet(captor, out CaptorState cs) || cs.Prisoner?.Bot?.Speaker == null) return false;
            BaseSpeaker p = cs.Prisoner.Bot.Speaker;
            return p.Speaking;
        }

        private static void Play(Speaker sp, CaptorLine line, BaseSpeaker speaker, float now)
        {
            EPhraseTrigger[] first = Lines(line, speaker, out EPhraseTrigger[] fallback);
            if (line == CaptorLine.Threat && _behind.TryGetValue(sp.Bot, out bool b) && b) first = LThreatBehind;
            Options.Clear();
            Collect(speaker, first);
            if (Options.Count == 0 && line == CaptorLine.Threat) Collect(speaker, LThreat);
            if (Options.Count == 0 && fallback != null) Collect(speaker, fallback);
            if (Options.Count == 0)
            {
                sp.Pending = null;
                Log(sp.Bot, $"{line}: voice {speaker.PlayerVoice} has no suitable line, stays silent", verbose: true);
                return;
            }
            if (Options.Count > 1) Options.Remove(sp.Last);
            ETagStatus mask = MaskFor(sp.Bot);
            while (Options.Count > 0)
            {
                int i = Dice.Range(0, Options.Count);
                EPhraseTrigger trig = Options[i];
                Options.RemoveAt(i);
                TagBank played = null;
                try { played = speaker.Play(trig, mask, true); } catch (System.Exception e) { HandsAreHighPlugin.Report("captor voice", e); }
                if (played == null) continue;
                sp.Pending = null;
                sp.SpokeAt = now;
                sp.Last = trig;
                sp.NextNormal = now + Settings.CaptorVoiceGap.Value;
                Voice.VerifyLater(sp.Bot, speaker.Clip, $"{line}: {trig} ({speaker.PlayerVoice})");
                return;
            }
            sp.At = now + 0.25f;   // the game declined each (group blocker, no clip for his tags): retry in the window
        }

        private static void Collect(BaseSpeaker speaker, EPhraseTrigger[] lines)
        {
            if (lines == null) return;
            foreach (EPhraseTrigger l in lines)
                if (Voice.Has(speaker, l) && !Options.Contains(l)) Options.Add(l);
        }

        private static ETagStatus MaskFor(Player bot)
        {
            try
            {
                BotTalk talk = bot.AIData?.BotOwner?.BotTalk;
                if (talk != null) return talk.GetMask(null) | ETagStatus.Combat;
            }
            catch { }
            return ETagStatus.Combat;
        }

        private static void Log(Player bot, string what, bool verbose)
        {
            if (!Settings.LogVoice.Value || (verbose && !Settings.VerboseLogs.Value)) return;
            HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(bot)}: captor voice {what}");
        }

        internal static void Clear()
        {
            Speakers.Clear();
            _behind.Clear();
            Done.Clear();
        }
    }
}
