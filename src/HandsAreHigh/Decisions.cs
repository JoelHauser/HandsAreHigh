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
        //   clear (gun not up, reloading, switching weapons, inventory open, busy fighting
        //   someone else): he only needs his reaction time;
        //   gradual (looking away, something in between, out of sight): he waits OpeningDelay
        //   on top, to be sure it isn't a glance.
        // strength 0..1: how good the opening is (looking 90 deg away beats 35 deg).
        // Bot captors: only what the prisoner could see of them (weapon in hands, reload or
        // weapon change, where they look, a clear line, that they're fighting someone else);
        // the inventory check is for players only. A captor looking away isn't an opening when
        // a squadmate the prisoner can see is holding him at gunpoint.
        internal static bool Covered(SurrenderState s, Player captor, Player bot, bool seen, bool inView, float watchAngle,
            out string kind, out float strength, out bool clear)
        {
            bool covered = CoveredBy(s, captor, bot, seen, inView, watchAngle, out kind, out strength, out clear);
            if (covered) return true;
            if (SquadCovers(s, captor, bot, watchAngle, out string who))
            {
                kind = null; strength = 0f; clear = false;
                if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: his captor isn't covering him, but {who} is");
                return true;
            }
            return false;
        }

        private static bool CoveredBy(SurrenderState s, Player captor, Player bot, bool seen, bool inView, float watchAngle,
            out string kind, out float strength, out bool clear)
        {
            kind = null; strength = 0f; clear = false;
            bool ai = captor.IsAI;
            if (!seen) { kind = ai ? "his captor can't see him" : "he can't see you"; strength = 0.8f; return false; }
            // 0.15.0: behind him (out of view, still there): only what he hears tells him
            // anything: a reload, shots at someone else, a weapon change, running footsteps. Where
            // the gun points he can't know, so he assumes it's on him. A captor walking round
            // behind a prisoner is not proof that nobody covers him.
            if (!inView)
            {
                if (captor.HandsController is Player.FirearmController hfc && hfc.IsInReloadOperation()) { kind = ai ? "he hears his captor reload" : "he hears you reload"; strength = 0.8f; clear = true; return false; }
                if (Time.time - SurrenderRegistry.HistoryOf(captor).ShotAt < 1.5f) { kind = ai ? "his captor fires at someone else, behind him" : "you fire at someone else, behind him"; strength = 0.85f; clear = true; return false; }
                if (captor.IsSprintEnabled) { kind = ai ? "his captor runs off behind him" : "you run off behind him"; strength = 0.85f; clear = true; return false; }
                if (ai && captor.AIData?.BotOwner?.WeaponManager?.Selector?.IsChanging == true) { kind = "he hears his captor switch weapons"; strength = 0.8f; clear = true; return false; }
                // 0.16.0: behind him, picking something up: heard (steps stopping, kit rustling).
                if (ai && CaptorLoot.Busy(captor, out string busyBehind) && !busyBehind.StartsWith("walking")) { kind = $"he hears his captor {busyBehind}"; strength = 0.75f; clear = true; return false; }
                return true;   // assumed covered
            }
            if (!(captor.HandsController is Player.FirearmController fc)) { kind = ai ? "his captor's gun isn't up" : "your gun isn't up"; strength = 1f; clear = true; return false; }
            if (!ai && captor.HandsController.IsInventoryOpen()) { kind = "you're in your inventory"; strength = 1f; clear = true; return false; }
            if (fc.IsInReloadOperation()) { kind = ai ? "his captor reloads" : "you're reloading"; strength = 0.9f; clear = true; return false; }
            if (ai)
            {
                BotOwner o = captor.AIData?.BotOwner;
                if (o == null || o.BotState != EBotState.Active) { kind = "his captor stopped moving"; strength = 0.9f; clear = true; return false; }
                if (o.WeaponManager?.Selector?.IsChanging == true) { kind = "his captor switches weapons"; strength = 1f; clear = true; return false; }
                if (captor.IsSprintEnabled) { kind = "his captor is running"; strength = 0.95f; clear = true; return false; }
                // 0.16.0: his captor going for (or bent over) his gear: eyes and gun elsewhere.
                if (CaptorLoot.Busy(captor, out string busy)) { kind = $"his captor is {busy}"; strength = busy.StartsWith("walking") ? 0.7f : 0.9f; clear = !busy.StartsWith("walking"); return false; }
            }

            // Only what he can see or hear: where the gun points, and gunfire (0.9.0: no longer
            // the captor's internal "busy" state or whether anyone had custody of him).
            Vector3 eye = Perception.Eye(captor);
            float angle = Vector3.Angle(captor.LookDirection, Perception.Chest(bot) - eye);
            if (Time.time - SurrenderRegistry.HistoryOf(captor).ShotAt < 1.5f && angle > watchAngle * 0.5f)
            {
                kind = ai ? "his captor is shooting at someone else" : "you're shooting at someone else";
                strength = 0.95f; clear = true;
                return false;
            }
            if (angle > watchAngle)
            {
                kind = ai ? $"his captor looks {angle:0} deg away" : $"you look {angle:0} deg away";
                strength = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(watchAngle, 90f, angle));
                return false;
            }
            if (!Perception.LineClear(eye, Perception.Chest(bot))) { kind = "something is between them"; strength = 0.7f; return false; }
            return true;
        }

        // 0.15.0: what the bots around him can see of a prisoner who stopped complying (null:
        // nothing yet): his arms coming down, the man moving off, starting up from his knees in a
        // hurry, something in his hands or a draw under way.
        internal static string Shows(SurrenderState s)
        {
            Player b = s?.Bot;
            if (b == null) return null;
            if (s.Weight < 0.75f) return "lowering his hands";
            if (b.Velocity.sqrMagnitude > 0.6f * 0.6f) return "moving off";
            if (s.Kneel == KneelPhase.GettingUp && s.KneelUrgent && s.KneelT < 0.8f) return "starting to get up";
            if (s.DrawPending || !Arsenal.HandsEmpty(b)) return "reaching for a weapon";
            return null;
        }

        // The captor's squadmates who hold fire at him (decided with the squad), close, aiming
        // at him with a gun, and seen by him: at most three line checks.
        private static bool SquadCovers(SurrenderState s, Player captor, Player bot, float watchAngle, out string who)
        {
            who = null;
            var map = Restraint.EntriesFor(bot);
            if (map == null) return false;
            Vector3 eye = Perception.Eye(bot);
            int tries = 0;
            foreach (RestraintEntry e in map.Values)
            {
                if (!e.Squad || !e.Holds || e.Observer == captor || !Perception.Alive(e.Observer)) continue;
                if ((e.Observer.Position - bot.Position).sqrMagnitude > 25f * 25f) continue;
                // 0.15.0: a squadmate he can't see (behind a kneeling man) is assumed to cover
                // him only if close (heard); far behind him, unknown, so not counted either way.
                if (!Presence.InView(s, Perception.Eye(e.Observer)))
                {
                    if ((e.Observer.Position - bot.Position).sqrMagnitude > 8f * 8f || !Perception.LineClear(eye, Perception.Eye(e.Observer))) continue;
                    who = $"{Restraint.Name(e.Observer)} (behind him, assumed)";
                    return true;
                }
                if (tries++ >= 3) break;
                if (!Perception.Covering(e.Observer, bot, watchAngle)) continue;
                if (!Perception.LineClear(eye, Perception.Eye(e.Observer))) continue;
                who = Restraint.Name(e.Observer);
                return true;
            }
            return false;
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
            float hold = Settings.IntimidationSeconds.Value * (1.3f - 0.6f * s.Nerve) * Dice.Range(0.8f, 1.2f);
            s.ComplyUntil = Mathf.Max(s.ComplyUntil, now + hold);
            s.DecideAt = -1f;
            s.OpenSince = -1f;
            s.LastCovered = true;
            Gestures.Cancel(s, "warning shot");
            Presence.OnWarningShot(s);
            if (!fresh) return;
            if (Settings.Chatter.Value && Dice.Value < Mathf.Clamp01(Settings.ReplyChance.Value + 0.3f))
                Voice.Say(s, VoiceEvent.Threatened, s.NoticeDelay());
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: warning shot from {shooter.Profile?.Nickname} passed {miss:0.0} m away: freezes for {hold:0.0} s (nerve {s.Nerve:0.00})");
        }
    }
}
