using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // What a held bot can perceive about his captor and himself, for SurrenderLogic's
    // decisions. Only what he could see or feel: the captor's gun, aim and line of sight,
    // his own wounds and legs, squadmates near him, and his dropped gun if he can see the spot.
    internal static class Reactions
    {
        // Is the captor covering him? If not, what kind of opening it is:
        //   clear (gun not up, reloading, inventory open): he only needs his reaction time;
        //   gradual (looking away, something in between, out of sight): he waits OpeningDelay
        //   on top, to be sure it isn't a glance.
        // strength 0..1: how good the opening is (looking 90 deg away beats 35 deg).
        internal static bool Covered(Player captor, Player bot, bool seen, float watchAngle,
            out string kind, out float strength, out bool clear)
        {
            kind = null; strength = 0f; clear = false;
            if (!seen) { kind = "he can't see you"; strength = 0.8f; return false; }
            if (!(captor.HandsController is Player.FirearmController fc)) { kind = "your gun isn't up"; strength = 1f; clear = true; return false; }
            if (captor.HandsController.IsInventoryOpen()) { kind = "you're in your inventory"; strength = 1f; clear = true; return false; }
            if (fc.IsInReloadOperation()) { kind = "you're reloading"; strength = 0.9f; clear = true; return false; }

            Vector3 eye = Perception.Eye(captor);
            float angle = Vector3.Angle(captor.LookDirection, Perception.Chest(bot) - eye);
            if (angle > watchAngle)
            {
                kind = $"you look {angle:0} deg away";
                strength = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(watchAngle, 90f, angle));
                return false;
            }
            if (!Perception.LineClear(eye, Perception.Chest(bot))) { kind = "something is between you"; strength = 0.7f; return false; }
            return true;
        }

        // Every second: how hurt he is (NaturalSurrender's model: the worst of health, head and
        // thorax, destroyed limbs, bleeding) and whether his legs still carry him.
        internal static void RefreshBody(SurrenderState s)
        {
            Player bot = s.Bot;
            if (bot?.HealthController == null) return;
            s.Injury = NaturalSurrender.Injury(bot, out _);
            s.LegsBad = bot.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg) ||
                        bot.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg);
        }

        // Squadmates alive, close (AllyRadius) and not held themselves: they make a move
        // more tempting.
        internal static int FreeAlliesNear(BotOwner owner, Player self)
        {
            BotsGroup group = owner?.BotsGroup;
            if (group == null) return 0;
            float r = Settings.AllyRadius.Value;
            int n = 0;
            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner m = group.Member(i);
                if (m == null || m == owner || m.IsDead) continue;
                Player mp = m.GetPlayer;
                if (mp == null || !Perception.Alive(mp) || SurrenderRegistry.HoldsBot(mp)) continue;
                if ((mp.Position - self.Position).sqrMagnitude <= r * r) n++;
            }
            return n;
        }

        // A shot from a player passed close (Suppression.WarningShots). He freezes, and the
        // decision he was about to make is dropped: a new opening must be noticed afresh.
        internal static void WarningShot(SurrenderState s, Player shooter, float miss)
        {
            float now = Time.time;
            bool fresh = now >= s.ComplyUntil;
            float hold = Settings.IntimidationSeconds.Value * (1.3f - 0.6f * s.Nerve) * Random.Range(0.8f, 1.2f);
            s.ComplyUntil = Mathf.Max(s.ComplyUntil, now + hold);
            s.DecideAt = -1f;
            s.OpenSince = -1f;
            s.LastCovered = true;
            Gestures.Cancel(s, "warning shot");
            if (!fresh) return;
            if (Settings.Chatter.Value && Random.value < Mathf.Clamp01(Settings.ReplyChance.Value + 0.3f))
                Voice.Say(s, VoiceEvent.Threatened, s.NoticeDelay());
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: warning shot from {shooter.Profile?.Nickname} passed {miss:0.0} m away: freezes for {hold:0.0} s (nerve {s.Nerve:0.00})");
        }
    }
}
