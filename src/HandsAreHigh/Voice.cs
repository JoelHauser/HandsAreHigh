using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // The game's own voice lines, played straight through the bot's speaker
    // (Player.Speaker.Play), the call SAIN itself uses (PlayerComponent.PlayVoiceLine).
    // BotTalk.Say and Player.Say don't work for SAIN bots: SAIN's BotTalkPatch and
    // PlayerTalkPatch swallow every phrase but hurt/agony/death/breath. Speaker.Play is audio
    // only: it never touches the hands, the pose or the AI.
    //
    // Which lines exist (read from the 0.16.9 phrase bundles, 2026-11-19): every Scav, BEAR
    // and USEC voice has Stop, Negative, Roger, Going, DontKnow, NeedHelp, OnMutter,
    // OnBeingHurt, HurtHeavy, HurtNearDeath, OnFight, Fire. Only BEAR/USEC voices have
    // HoldFire, Cooperation, Gogogo; only Scav voices have Toxic. Speaker.PhrasesBanks says
    // per voice what it has, so a missing line is skipped (silence), never replaced by an
    // unrelated one.
    //
    // Playing is verified where the engine allows: Speaker.Play returns the bank only once
    // it picked a clip and raised OnPhraseTold, whose handler (Player.PlaySpeechFromTime)
    // starts the clip on the speech source unless the bot is out of the player's earshot.
    // A moment later the speech source is checked for that clip actually playing.
    internal enum VoiceEvent { Surrender, Acknowledge, Refuse, Break, Hurt, Plead, Threatened, Approached, Released, Idle, Defiant }

    // Urgent lines (surrender, hurt, breaking) never wait for other bots and override any
    // queued line; idle chatter never replaces anything.
    internal enum VoicePriority { Idle, Normal, High, Urgent }

    internal static class Voice
    {
        private const float RetryWindow = 2f;
        private const float RetryStep = 0.25f;
        private const float HighGap = 2.5f;
        private const float VerifyAfter = 0.15f;

        private static readonly AccessTools.FieldRef<Player, BetterSource> SpeechSourceRef =
            AccessTools.FieldRefAccess<Player, BetterSource>("_speechSource");
        private static readonly HashSet<string> MissingLogged = new HashSet<string>();
        private static readonly List<EPhraseTrigger> Options = new List<EPhraseTrigger>(8);

        internal static void Reset() { MissingLogged.Clear(); Checks.Clear(); }

        private static VoicePriority PriorityOf(VoiceEvent e)
        {
            switch (e)
            {
                case VoiceEvent.Surrender:
                case VoiceEvent.Hurt:
                case VoiceEvent.Break: return VoicePriority.Urgent;
                case VoiceEvent.Threatened: return VoicePriority.High;
                case VoiceEvent.Idle:
                case VoiceEvent.Defiant: return VoicePriority.Idle;
                default: return VoicePriority.Normal;
            }
        }

        // Lines that only make sense while he stands there with his hands up; dropped if he
        // has started running or fighting by the time they would play.
        private static bool NeedsHandsUp(VoiceEvent e) =>
            e == VoiceEvent.Plead || e == VoiceEvent.Threatened || e == VoiceEvent.Approached ||
            e == VoiceEvent.Idle || e == VoiceEvent.Defiant || e == VoiceEvent.Acknowledge || e == VoiceEvent.Refuse;

        // Line tables, built once (they used to be new arrays on every line).
        private static readonly EPhraseTrigger[] LSurrender = { EPhraseTrigger.HoldFire, EPhraseTrigger.Stop };
        private static readonly EPhraseTrigger[] LNegative = { EPhraseTrigger.Negative };
        private static readonly EPhraseTrigger[] LPleadHurt = { EPhraseTrigger.HurtNearDeath, EPhraseTrigger.HurtHeavy, EPhraseTrigger.NeedHelp };
        private static readonly EPhraseTrigger[] LPlead = { EPhraseTrigger.NeedHelp, EPhraseTrigger.Cooperation, EPhraseTrigger.HoldFire };
        private static readonly EPhraseTrigger[] LStop = { EPhraseTrigger.Stop };
        private static readonly EPhraseTrigger[] LApproached = { EPhraseTrigger.Stop, EPhraseTrigger.HoldFire, EPhraseTrigger.Cooperation };
        private static readonly EPhraseTrigger[] LAck = { EPhraseTrigger.Roger, EPhraseTrigger.Going };
        private static readonly EPhraseTrigger[] LRefuse = { EPhraseTrigger.Negative, EPhraseTrigger.DontKnow };
        private static readonly EPhraseTrigger[] LIdle = { EPhraseTrigger.OnMutter, EPhraseTrigger.OnMutter, EPhraseTrigger.NeedHelp };
        private static readonly EPhraseTrigger[] LDefiant = { EPhraseTrigger.Toxic };
        private static readonly EPhraseTrigger[] LBreak = { EPhraseTrigger.OnFight, EPhraseTrigger.Fire, EPhraseTrigger.Gogogo };
        private static readonly EPhraseTrigger[] LHurt = { EPhraseTrigger.OnBeingHurt };

        // Scavs just babble (Joel, 2026-11-19: "it doesn't matter what they say"): for every
        // non-urgent line a Scav voice draws from its own lines plus this pool, so a Scav is
        // never silent for lack of a fitting line. All of these exist in every scav_1-6 voice.
        private static readonly EPhraseTrigger[] ScavBabble =
        {
            EPhraseTrigger.OnMutter, EPhraseTrigger.Toxic, EPhraseTrigger.NeedHelp, EPhraseTrigger.Negative,
            EPhraseTrigger.Stop, EPhraseTrigger.DontKnow, EPhraseTrigger.Roger, EPhraseTrigger.Going,
            EPhraseTrigger.Repeat, EPhraseTrigger.Look, EPhraseTrigger.Silence, EPhraseTrigger.Covering
        };

        // Preferred lines, then a fallback only when the voice has none of those.
        private static void Lines(SurrenderState s, VoiceEvent e, out EPhraseTrigger[] first, out EPhraseTrigger[] fallback)
        {
            fallback = null;
            switch (e)
            {
                case VoiceEvent.Surrender: first = LSurrender; fallback = LNegative; return;
                case VoiceEvent.Plead: first = s.Injury >= 0.5f ? LPleadHurt : LPlead; fallback = LStop; return;
                case VoiceEvent.Threatened: first = LSurrender; fallback = LNegative; return;
                case VoiceEvent.Approached: first = LApproached; return;
                case VoiceEvent.Acknowledge:
                case VoiceEvent.Released: first = LAck; return;
                case VoiceEvent.Refuse: first = LRefuse; return;
                case VoiceEvent.Idle: first = LIdle; return;   // mostly nervous muttering
                case VoiceEvent.Defiant: first = LDefiant; return;
                case VoiceEvent.Break: first = LBreak; return;
                default: first = LHurt; return;
            }
        }

        internal static bool Has(BaseSpeaker speaker, EPhraseTrigger line) =>
            speaker?.PhrasesBanks != null && speaker.PhrasesBanks.TryGetValue(line, out TagBank bank) &&
            bank != null && bank.Clips != null && bank.Clips.Length > 0;

        // Queue a line (the more important one wins); played from Tick when the speaker,
        // and for non-urgent lines the bots around him, are free. Never blocks anything.
        internal static void Say(SurrenderState s, VoiceEvent e, float delay = 0f)
        {
            if (!Settings.BotVoice.Value || s?.Bot == null || s.Dead) return;
            float now = Time.time;
            VoicePriority pr = PriorityOf(e);
            if (e == VoiceEvent.Surrender && now < s.NextSurrenderVoice) return;
            if (pr <= VoicePriority.Normal && now < s.NextVoice)
            {
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: holds his tongue (cooldown {s.NextVoice - now:0} s)");
                return;
            }
            if (pr == VoicePriority.High && now - s.SpokeAt < HighGap) return;
            if (s.PendingVoice != null && s.PendingPriority > pr) return;
            s.PendingVoice = e;
            s.PendingPriority = pr;
            s.PendingVoiceAt = now + Mathf.Max(0f, delay);
            s.PendingVoiceUntil = s.PendingVoiceAt + RetryWindow;
        }

        // A reaction to something the player did: only sometimes, after his reaction time.
        internal static void Reply(SurrenderState s, VoiceEvent e)
        {
            if (!Settings.Chatter.Value || Random.value >= Settings.ReplyChance.Value) return;
            Say(s, e, s.NoticeDelay());
        }

        internal static float AckDelay() => Settings.Between(Settings.AckDelayMin, Settings.AckDelayMax);

        internal static void ScheduleChatter(SurrenderState s) =>
            s.NextChatter = Time.time + Settings.Between(Settings.ChatterMin, Settings.ChatterMax);

        internal static void Tick(SurrenderState s)
        {
            if (s.Bot == null || s.Dead) return;
            float now = Time.time;
            if (s.VerifyAt > 0f && now >= s.VerifyAt) Verify(s);

            Unprompted(s, now);

            if (s.PendingVoice == null) return;
            VoiceEvent e = s.PendingVoice.Value;
            if (NeedsHandsUp(e) && s.Phase != Phase.HandsUp)
            {
                s.PendingVoice = null;
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: dropped, he's {s.Phase} now");
                return;
            }
            if (now < s.PendingVoiceAt) return;
            if (now > s.PendingVoiceUntil)
            {
                s.PendingVoice = null;
                if (Settings.LogVoice.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: no chance to speak within {RetryWindow:0} s (speaker or nearby bots busy), skipped");
                return;
            }
            BaseSpeaker speaker = s.Bot.Speaker;
            if (speaker == null) { s.PendingVoice = null; return; }
            if (speaker.Speaking || speaker.Busy) return;
            if (s.PendingPriority < VoicePriority.Urgent && FloorTaken(s, now)) return;
            // His captor (a bot) is speaking: answer after him, not over him.
            if (s.PendingPriority < VoicePriority.Urgent && s.Custody != null && CaptorVoice.Talking(s.Custody.Bot)) return;
            Play(s, e, speaker, now);
        }

        // Now and then, unprompted, while he stands there with his hands up.
        private static void Unprompted(SurrenderState s, float now)
        {
            if (!Settings.Chatter.Value || s.Phase != Phase.HandsUp || s.Suspended || s.Progress < 1f) return;
            if (s.NextChatter <= 0f || now < s.NextChatter || s.PendingVoice != null) return;
            ScheduleChatter(s);
            if (Random.value >= Settings.ChatterChance.Value) return;

            bool bold = s.Nerve >= 0.9f || (s.Nerve >= 0.7f && s.Fight >= 1.2f);
            VoiceEvent e;
            if (Settings.DefiantRemarks.Value && bold && Random.value < 0.5f) e = VoiceEvent.Defiant;
            else if (s.Injury >= 0.5f || Random.value < 0.35f) e = VoiceEvent.Plead;
            else e = VoiceEvent.Idle;
            Say(s, e);
        }

        // Another held bot close by is talking or just finished: wait for a gap.
        private static bool FloorTaken(SurrenderState s, float now)
        {
            float r = Settings.ChatterRadius.Value;
            if (r <= 0f) return false;
            Vector3 at = s.Bot.Position;
            foreach (SurrenderState o in SurrenderRegistry.All)
            {
                if (o == s || o.Bot == null) continue;
                if ((o.Bot.Position - at).sqrMagnitude > r * r) continue;
                if (o.Bot.Speaker != null && o.Bot.Speaker.Busy) return true;
                if (now - o.SpokeAt < Settings.ChatterGap.Value) return true;
            }
            return false;
        }

        private static void Play(SurrenderState s, VoiceEvent e, BaseSpeaker speaker, float now)
        {
            Lines(s, e, out EPhraseTrigger[] first, out EPhraseTrigger[] fallback);
            Options.Clear();
            Collect(speaker, first);
            if (Options.Count == 0 && fallback != null) Collect(speaker, fallback);
            if (speaker.SideTag == ETagStatus.Scav && s.PendingPriority < VoicePriority.Urgent) Collect(speaker, ScavBabble);
            string voice = speaker.PlayerVoice;
            if (Options.Count == 0)
            {
                s.PendingVoice = null;
                if (Settings.LogVoice.Value && MissingLogged.Add(voice + "/" + e))
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: voice {e}: voice {voice} has no suitable line, stays silent (logged once per voice)");
                return;
            }
            // Not the line he said last, unless it's the only one.
            if (Options.Count > 1)
                for (int k = Options.Count - 1; k >= 0; k--)
                    if (Options[k] == s.LastLine) Options.RemoveAt(k);

            ETagStatus mask = MaskFor(s.Bot);
            while (Options.Count > 0)
            {
                int i = Random.Range(0, Options.Count);
                EPhraseTrigger line = Options[i];
                Options.RemoveAt(i);
                TagBank played = null;
                HandsAreHighPlugin.Try("voice", () => played = speaker.Play(line, mask, true));
                if (played == null) continue;

                s.PendingVoice = null;
                s.SpokeAt = now;
                s.LastLine = line;
                s.NextVoice = now + Settings.VoiceCooldown.Value;
                if (e == VoiceEvent.Surrender) s.NextSurrenderVoice = now + Settings.VoiceCooldown.Value;
                s.NextChatter = Mathf.Max(s.NextChatter, now + Settings.ChatterMin.Value);
                s.VerifyAt = now + VerifyAfter;
                s.VerifyClip = speaker.Clip;
                s.VerifyWhat = $"{e}: {line} ({voice})";
                return;
            }
            // The game declined each line (higher-importance line playing, a group blocker,
            // no clip for his current tags). Try again shortly, within the retry window.
            s.PendingVoiceAt = now + RetryStep;
        }

        private static void Collect(BaseSpeaker speaker, EPhraseTrigger[] lines)
        {
            foreach (EPhraseTrigger l in lines)
                if (Has(speaker, l) && !Options.Contains(l)) Options.Add(l);
        }

        // Did audio start? The clip must be the one Speaker.Play picked and be playing on his
        // speech source; otherwise say why not, as far as the engine tells.
        private static void Verify(SurrenderState s)
        {
            Player bot = s.Bot;
            TaggedClip clip = s.VerifyClip;
            string what = s.VerifyWhat;
            s.VerifyAt = -1f;
            s.VerifyClip = null;
            if (!Settings.LogVoice.Value || bot == null) return;

            BetterSource src = SpeechSourceRef(bot);
            AudioSource audio = src != null ? src.source1 : null;
            string result;
            if (clip?.Clip != null && audio != null && audio.isPlaying && audio.clip == clip.Clip)
                result = $"audible ({clip.Clip.name}, {clip.Length:0.0} s)";
            else if (src != null && bot.IsOutOfProtagonistsRange(src.MaxDistance))
                result = "line chosen, but he is out of your earshot so the game played no audio";
            else
                result = "line chosen, but no audio started on his speech source";
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: says {what}: {result}");
        }

        // Lines of bots we don't hold (captors): the same check, a moment after the game took
        // the line. A small list, empty almost always.
        private struct Pending { internal Player Bot; internal TaggedClip Clip; internal string What; internal float At; }
        private static readonly List<Pending> Checks = new List<Pending>(4);

        internal static void VerifyLater(Player bot, TaggedClip clip, string what)
        {
            if (!Settings.LogVoice.Value || bot == null) return;
            if (Checks.Count >= 16) Checks.RemoveAt(0);
            Checks.Add(new Pending { Bot = bot, Clip = clip, What = what, At = Time.time + VerifyAfter });
        }

        internal static void TickChecks()
        {
            if (Checks.Count == 0) return;
            float now = Time.time;
            for (int i = Checks.Count - 1; i >= 0; i--)
            {
                Pending c = Checks[i];
                if (now < c.At) continue;
                Checks.RemoveAt(i);
                if (c.Bot == null) continue;
                HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(c.Bot)}: says {c.What}: {Outcome(c.Bot, c.Clip)}");
            }
        }

        private static string Outcome(Player bot, TaggedClip clip)
        {
            BetterSource src = SpeechSourceRef(bot);
            AudioSource audio = src != null ? src.source1 : null;
            if (clip?.Clip != null && audio != null && audio.isPlaying && audio.clip == clip.Clip)
                return $"audible ({clip.Clip.name}, {clip.Length:0.0} s)";
            if (src != null && bot.IsOutOfProtagonistsRange(src.MaxDistance))
                return "line chosen, but he is out of your earshot so the game played no audio";
            return "line chosen, but no audio started on his speech source";
        }

        internal static void ClearChecks() => Checks.Clear();

        // The bot's own tag mask (faction, combat state, injury), as BotTalk builds it.
        private static ETagStatus MaskFor(Player bot)
        {
            try
            {
                BotTalk talk = bot.AIData?.BotOwner?.BotTalk;
                if (talk != null) return talk.GetMask(null);
            }
            catch { }
            return ETagStatus.Combat;
        }
    }
}
