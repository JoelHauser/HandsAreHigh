using System.Collections.Generic;
using System.Text;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.16.0: what a bot captor decides about his prisoner, at explicit points, each decided once
    // and kept (never a check repeated until it comes up lethal):
    //
    //   take       Captors.Decide: practical? wants to? (mercy x what this man did to him)
    //   notice     after each Drop your weapons is answered: does he see a weapon still on him?
    //              A slung gun, if he sees the man; a holstered pistol only close; a knife never;
    //              a gun he saw on him before and that didn't land. One roll per answer.
    //   repeat     noticed: he demands it again (Captor repeats a demand at most, a warning line)
    //   refusal    still kept after the repeats: open fire (Refusal turns lethal) or warn and be
    //              done with him (let go early, nothing near him)
    //   release    his plan is over: let him go, or (Kills a compliant prisoner) shoot him
    //   resistance an observed draw or attack: always fights (Restraint.OnArmed, unchanged)
    //
    // Inputs, only what the bot knows: his personality (aggression), being hit by this prisoner
    // (his own EnemyInfo.LastGetHitTime, set by the game's BotOwner.GetHit; a squadmate's too), a
    // man he saw attack after surrendering (Restraint distrust), what he saw of the disarming.
    // Experience is discipline (fewer impulsive kills, quicker to spot a weapon kept back), never
    // cruelty. Custody becoming impractical (a fight, lost sight) is never a reason to kill: those
    // endings stay Captors.End / CaptorDown, restraint as decided.
    //
    // A deliberate decision is logged as "DECIDES TO KILL" with its reasons and set as Reject on
    // that captor only (his squad's restraint stays theirs); a shot from a bot whose decision still
    // holds fire is a RESTRAINT LEAK (Restraint.cs). DeathReport tells the two apart at the end.
    internal enum JudgeForce { Auto, Mercy, Lethal }

    internal static class Judgement
    {
        private static readonly Dictionary<Player, JudgeForce> Forced = new Dictionary<Player, JudgeForce>();
        internal static void Force(Player captor, JudgeForce f) { if (captor != null) { if (f == JudgeForce.Auto) Forced.Remove(captor); else Forced[captor] = f; } }
        internal static void Clear() { Forced.Clear(); Kills.Clear(); }
        private static JudgeForce ForcedFor(Player p) => p != null && Forced.TryGetValue(p, out JudgeForce f) ? f : JudgeForce.Auto;

        // Aggression by SAIN personality (no SAIN: by role). 1 = neutral. Tuning, not game data.
        internal static float Aggression(Player p)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(p);
            switch (SurrenderRegistry.PersonalityOf(p, h))
            {
                case "Wreckless": return 2.5f;
                case "GigaChad": return 1.8f;
                case "Chad": return 1.3f;
                case "SnappingTurtle": return 1.2f;
                case "Rat": return 0.9f;
                case "Coward": return 0.6f;
                case "Timmy": return 0.5f;
                case "": case null:
                    NaturalSurrender.RoleClass rc = NaturalSurrender.ClassOf(p);
                    return rc == NaturalSurrender.RoleClass.Boss ? 1.8f : rc == NaturalSurrender.RoleClass.Scav ? 1.1f : 1f;
                default: return 1f;
            }
        }

        // Did this man hit him (or one of his squad) recently, by the bots' own memory?
        internal static bool KnownViolence(BotOwner o, Player prisoner, out string why)
        {
            why = null;
            float now = Time.time, mem = Settings.ViolenceMemory.Value;
            EnemyInfo mine = Perception.InfoAbout(o, prisoner);
            if (mine != null && mine.LastGetHitTime > 0f && now - mine.LastGetHitTime < mem) { why = $"he shot him {now - mine.LastGetHitTime:0} s ago"; return true; }
            BotHistory h = SurrenderRegistry.HistoryOf(o.GetPlayer);
            if (h.Distrust != null && h.Distrust.Contains(prisoner.ProfileId)) { why = "he saw him attack after giving up before"; return true; }
            BotsGroup g = o.BotsGroup;
            if (g != null)
                for (int i = 0; i < g.MembersCount && i < 6; i++)
                {
                    BotOwner m = g.Member(i);
                    if (m == null || m == o || m.IsDead) continue;
                    EnemyInfo e = Perception.InfoAbout(m, prisoner);
                    if (e != null && e.LastGetHitTime > 0f && now - e.LastGetHitTime < mem) { why = $"he shot {Restraint.Name(m.GetPlayer)} {now - e.LastGetHitTime:0} s ago"; return true; }
                }
            return false;
        }

        private static float Experience(BotOwner o) => o?.GetPlayer == null ? 0f : NaturalSurrender.Experience(o.GetPlayer, o);

        // Captors.Decide: what he knows of this man changes how merciful he is (x on mercy).
        internal static float MercyFactor(BotOwner o, SurrenderState s, out string why)
        {
            why = null;
            if (!KnownViolence(o, s.Bot, out string v)) return 1f;
            why = v;
            return 1f / Mathf.Sqrt(Settings.ViolenceWeight.Value);
        }

        // ---- notice: what a captor can see of the weapons left on him --------------------------

        // A weapon he can see on the prisoner now (null: none he can see). Never what is in a bag.
        internal static Item Visible(CaptorState cs, SurrenderState s, out string how)
        {
            how = null;
            Player p = s.Bot;
            if (p?.Equipment == null || cs.Owner == null) return null;
            bool sees = Perception.Recognises(cs.Owner, p);
            if (!sees) return null;
            foreach (EquipmentSlot slot in Slung)
            {
                Item it = p.Equipment.GetSlot(slot)?.ContainedItem;
                if (it != null) { how = "slung on his back"; return it; }
            }
            Item pistol = p.Equipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem;
            if (pistol != null)
            {
                float d = Vector3.Distance(cs.Bot.Position, p.Position);
                if (d <= Settings.HolsterSeenWithin.Value && Perception.LineClear(Perception.Eye(cs.Bot), Perception.Chest(p) - Vector3.up * 0.25f))
                { how = $"in his holster ({d:0.0} m)"; return pistol; }
            }
            return null;
        }

        private static readonly EquipmentSlot[] Slung = { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon };

        // Every second while guarding: weapons he has seen on the prisoner are remembered.
        internal static void Watch(CaptorState cs, SurrenderState s, float now)
        {
            if (now < cs.NextWatchAt) return;
            cs.NextWatchAt = now + 1f;
            Item it = Visible(cs, s, out _);
            if (it != null) cs.SeenWeapons.Add(it);
            if (s.Bot.HandsController?.Item is Weapon held && Perception.Recognises(cs.Owner, s.Bot)) cs.SeenWeapons.Add(held);
        }

        // A Drop your weapons was answered (dropped, partly, or refused): what does he make of it?
        // Returns the order to give next, or null (nothing to add). May decide to fire (lethal).
        internal static void AfterDemand(CaptorState cs, SurrenderState s, float now)
        {
            Item visible = Visible(cs, s, out string how);
            Item remembered = null;
            foreach (Item it in cs.SeenWeapons) if (it != visible && Compliance.Carries(s.Bot, it)) { remembered = it; break; }
            Item seen = visible ?? remembered;
            var log = new StringBuilder($"{cs.Name}: after 'drop your weapons' ({s.Name}): ");
            if (seen == null)
            {
                cs.SawKept = null;
                Log(log.Append(Compliance.Disarmed(s.Bot) ? "sees nothing left on him" : "sees nothing left on him (he may still carry something he can't see)").ToString());
                return;
            }
            float exp = Experience(cs.Owner);
            float p = (visible != null ? (how.StartsWith("slung") ? 0.9f : 0.6f) : 0.85f) * (1f + 0.4f * exp);
            p = Mathf.Clamp(p, 0f, 0.98f);
            float roll = Dice.Value;
            string what = $"{Compliance.Name(seen)} {(visible != null ? how : "he saw on him earlier and didn't see land")}";
            log.Append($"a {what}: notices? chance {p:0.00} (experience {exp:0.00}), roll {roll:0.00} -> ");
            if (roll >= p) { cs.SawKept = null; Log(log.Append("doesn't notice it").ToString()); return; }
            cs.SawKept = seen;
            cs.Incomplete = true;
            if (cs.Repeats < Settings.CaptorRepeats.Value)
            {
                cs.Repeats++;
                Log(log.Append($"notices it; demands it again ({cs.Repeats}/{Settings.CaptorRepeats.Value}), with a warning").ToString());
                CaptorVoice.Say(cs.Bot, CaptorLine.Warn);
                cs.Plan.Insert(cs.Step, OrderKind.DropWeapons);
                cs.NextOrderAt = now + Dice.Range(1.2f, 2.5f);
                cs.HoldUntil = Mathf.Max(cs.HoldUntil, now + 12f);
                return;
            }
            log.Append($"notices it again after {cs.Repeats} demand(s): ");
            Refusal(cs, s, now, log, seen);
        }

        // He kept a weapon the captor can see through every demand.
        private static void Refusal(CaptorState cs, SurrenderState s, float now, StringBuilder log, Item seen)
        {
            float aggr = Aggression(cs.Bot);
            bool hurt = KnownViolence(cs.Owner, s.Bot, out string vWhy);
            float exp = Experience(cs.Owner);
            float chance = Settings.RefusalLethal.Value * aggr * (hurt ? Settings.ViolenceWeight.Value : 1f) * (1f - Settings.Discipline.Value * exp);
            chance = Mathf.Clamp(chance, 0f, Settings.ExecuteMax.Value);
            JudgeForce f = ForcedFor(cs.Bot);
            float roll = Dice.Value;
            bool lethal = f == JudgeForce.Lethal || (f == JudgeForce.Auto && roll < chance);
            log.Append($"lethal? chance {chance:0.00} (base {Settings.RefusalLethal.Value:0.00} x aggression {aggr:0.00}{(hurt ? $" x violence ({vWhy})" : "")} x discipline {1f - Settings.Discipline.Value * exp:0.00}), roll {roll:0.00}" +
                       $"{(f != JudgeForce.Auto ? $" (FORCED {f})" : "")} -> ");
            if (lethal)
            {
                Log(log.Append("DECIDES TO KILL him").ToString());
                Escalate(cs, s, $"he kept his {Compliance.Name(seen)} after {cs.Repeats + 1} demand(s)");
                return;
            }
            // Mercy, but no trust: a last warning, nothing near him, done with him soon. This was
            // his decision about this man: the release isn't weighed again (no second roll).
            cs.Unsafe = true;
            cs.ReleaseJudged = true;
            cs.HoldUntil = Mathf.Min(cs.HoldUntil, now + Dice.Range(3f, 6f));
            if (cs.Step < cs.Plan.Count - 1) cs.Step = cs.Plan.Count - 1;
            CaptorVoice.Say(cs.Bot, CaptorLine.Warn);
            Log(log.Append("warns him once more and lets him go soon, keeping his distance (nothing of his fetched)").ToString());
        }

        // The plan is done: let him go or shoot him. Once per custody (Captors.Check).
        internal static bool AtRelease(CaptorState cs, SurrenderState s)
        {
            if (cs.ReleaseJudged) return false;
            cs.ReleaseJudged = true;
            float aggr = Aggression(cs.Bot);
            bool hurt = KnownViolence(cs.Owner, s.Bot, out string vWhy);
            float exp = Experience(cs.Owner);
            float chance = Settings.ExecuteChance.Value * aggr * (hurt ? Settings.ViolenceWeight.Value : 1f) *
                           (cs.Incomplete ? Settings.IncompleteWeight.Value : 1f) * (1f - Settings.Discipline.Value * exp);
            chance = Mathf.Clamp(chance, 0f, Settings.ExecuteMax.Value);
            JudgeForce f = ForcedFor(cs.Bot);
            float roll = Dice.Value;
            bool lethal = f == JudgeForce.Lethal || (f == JudgeForce.Auto && roll < chance);
            Log($"{cs.Name}: done with {s.Name} ({Time.time - cs.Since:0} s, plan {cs.PlanName}; {(cs.Incomplete ? "he kept something back" : "he did as told")}): " +
                $"kill instead of letting go? chance {chance:0.00} (base {Settings.ExecuteChance.Value:0.00} x aggression {aggr:0.00}{(hurt ? $" x violence ({vWhy})" : "")}" +
                $"{(cs.Incomplete ? $" x kept back {Settings.IncompleteWeight.Value:0.0}" : "")} x discipline {1f - Settings.Discipline.Value * exp:0.00}), roll {roll:0.00}" +
                $"{(f != JudgeForce.Auto ? $" (FORCED {f})" : "")} -> {(lethal ? "DECIDES TO KILL him" : "lets him go")}");
            if (!lethal) return false;
            Escalate(cs, s, cs.Incomplete ? "done with a man who kept a weapon back" : hurt ? $"done with him ({vWhy})" : "done with him (his own nature)");
            return true;
        }

        // A deliberate lethal decision: this captor only, logged, his ceasefire lifted, the man made
        // his target, custody ended (the combat AI fires; the prisoner reacts to what he sees).
        internal static void Escalate(CaptorState cs, SurrenderState s, string why)
        {
            BotOwner o = cs.Owner;
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: DECIDES TO KILL {s.Name} ({why}); his squad's own decisions stand");
            RestraintEntry entry = Restraint.Set(s, o, RestraintKind.Reject, $"decides to kill him: {why}", squad: false);
            if (entry != null) entry.Final = true;
            CaptorVoice.Interrupt(cs.Bot);
            CaptorVoice.Say(cs.Bot, CaptorLine.Resist);
            Captors.End(cs, $"decided to kill him ({why})");
            try
            {
                EnemyInfo info = Perception.InfoAbout(o, s.Bot);
                if (info != null && o.Memory != null) o.Memory.GoalEnemy = info;
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("judgement target", e); }
            Watch(o, s, why);
        }

        // ---- 0.17.0: a man running from custody --------------------------------------------------

        // Once per bolt, only for a run he saw (Captors.Check): let him run, or fire. Not an attack:
        // a judgement, from his nature, what this man did to him and what he saw him keep back.
        // True when he decided to fire (custody already ended by Escalate).
        internal static bool OnBolt(CaptorState cs, SurrenderState s)
        {
            float aggr = Aggression(cs.Bot);
            bool hurt = KnownViolence(cs.Owner, s.Bot, out string vWhy);
            float exp = Experience(cs.Owner);
            float chance = Settings.BoltLethal.Value * aggr * (hurt ? Settings.ViolenceWeight.Value : 1f) *
                           (cs.Incomplete ? Settings.IncompleteWeight.Value : 1f) * (1f - Settings.Discipline.Value * exp);
            chance = Mathf.Clamp(chance, 0f, Settings.ExecuteMax.Value);
            JudgeForce f = ForcedFor(cs.Bot);
            float roll = Dice.Value;
            bool lethal = f == JudgeForce.Lethal || (f == JudgeForce.Auto && roll < chance);
            Log($"{cs.Name}: {s.Name} runs from his custody ({s.Phase}, unarmed): fire on him? chance {chance:0.00} (base {Settings.BoltLethal.Value:0.00} x aggression {aggr:0.00}" +
                $"{(hurt ? $" x violence ({vWhy})" : "")}{(cs.Incomplete ? $" x kept back {Settings.IncompleteWeight.Value:0.0}" : "")} x discipline {1f - Settings.Discipline.Value * exp:0.00}), roll {roll:0.00}" +
                $"{(f != JudgeForce.Auto ? $" (FORCED {f})" : "")} -> {(lethal ? "DECIDES TO KILL him" : "lets him run")}");
            if (!lethal) return false;
            Escalate(cs, s, "he ran from custody");
            return true;
        }

        // Debug scenarios: the decision only (forced); the restraint change, targeting and firing
        // that follow are the real ones (and watched below).
        internal static string ForceKillNow(Player captor)
        {
            if (captor == null || !Captors.TryGet(captor, out CaptorState cs)) return "isn't guarding anyone";
            SurrenderState s = cs.Prisoner;
            if (s == null || s.Dead) return "his prisoner is gone";
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: decision FORCED by the scenario (the decision only; what follows is the real path)");
            Escalate(cs, s, "FORCED by a debug scenario");
            return null;
        }

        // ---- 0.17.0: is a kill decision carried out? ------------------------------------------------
        //
        // After Escalate: every 0.5 s, from the bot's real state: his target, whether he pressed the
        // trigger since (Restraint.NoteShot via TriggerPatch), whether the man is dead. His target is
        // set again (at most 3 times) if something else took it while the man is in his sight; after
        // 10 s without a shot the reason is logged (layer, target, hands, sight, restraint) and the
        // watch ends. Nothing here fires a shot or touches health: the AI's own combat does it.
        private sealed class KillOrder
        {
            internal BotOwner Owner;
            internal Player Captor, Prisoner;
            internal string Name, Victim, Why;
            internal float Since, FirstShot = -1f;
            internal int Retargets;
        }

        private static readonly List<KillOrder> Kills = new List<KillOrder>(2);
        internal static bool Watching => Kills.Count > 0;

        private static void Watch(BotOwner o, SurrenderState s, string why)
        {
            if (Kills.Count >= 8) Kills.RemoveAt(0);
            Kills.Add(new KillOrder { Owner = o, Captor = o.GetPlayer, Prisoner = s.Bot, Name = Restraint.Name(o.GetPlayer), Victim = s.Name, Why = why, Since = Time.time });
        }

        internal static void TickKills(float now)
        {
            for (int i = Kills.Count - 1; i >= 0; i--)
            {
                KillOrder k = Kills[i];
                string done = null;
                bool warn = false;
                if (!Perception.Alive(k.Prisoner)) done = $"KILL DECISION CARRIED OUT: {k.Victim} is dead {now - k.Since:0.0} s after {k.Name}'s decision ({k.Why}){(k.FirstShot >= 0f ? $"; his first shot +{k.FirstShot - k.Since:0.0} s" : "; no trigger press of his seen (someone else's shot?)")}";
                else if (!Perception.Alive(k.Captor) || k.Owner == null || k.Owner.IsDead) done = $"kill decision about {k.Victim} ends: {k.Name} is down";
                else
                {
                    float shot = Restraint.LastShot(k.Captor);
                    if (k.FirstShot < 0f && shot > k.Since)
                    {
                        k.FirstShot = shot;
                        Log($"{k.Name}: opens fire on {k.Victim} {shot - k.Since:0.0} s after deciding to kill him");
                    }
                    EnemyInfo goal = k.Owner.Memory?.GoalEnemy;
                    bool onHim = goal != null && Perception.Same(goal.Person, k.Prisoner);
                    if (!onHim && k.FirstShot < 0f && k.Retargets < 3 && Perception.Recognises(k.Owner, k.Prisoner))
                    {
                        EnemyInfo info = Perception.InfoAbout(k.Owner, k.Prisoner);
                        if (info != null && k.Owner.Memory != null)
                        {
                            k.Owner.Memory.GoalEnemy = info;
                            k.Retargets++;
                            Log($"{k.Name}: his target was {(goal == null ? "nobody" : Restraint.Name(Perception.PlayerOf(goal.Person)))}; set back to {k.Victim} ({k.Retargets}/3)");
                        }
                    }
                    if (k.FirstShot < 0f && now - k.Since > 10f)
                    {
                        warn = true;
                        string layer = "?";
                        try { layer = k.Owner.Brain?.ActiveLayerName() ?? "?"; } catch { }
                        Restraint.TryGet(k.Prisoner, k.Captor, out RestraintEntry e);
                        done = $"KILL DECISION NOT CARRIED OUT after 10 s: {k.Name} -> {k.Victim}: layer {layer}, target {(goal == null ? "none" : Restraint.Name(Perception.PlayerOf(goal.Person)))}, " +
                               $"hands {Arsenal.DescribeHands(k.Captor)}, sees him {Perception.Recognises(k.Owner, k.Prisoner)}, {Vector3.Distance(k.Captor.Position, k.Prisoner.Position):0} m, " +
                               $"restraint {(e == null ? "none" : $"{e.Kind}{(e.Final ? " (final)" : "")}")}, shielded {ScenarioShield.Blocks(k.Captor, k.Prisoner)}";
                    }
                    else if (k.FirstShot >= 0f && now - k.FirstShot > 20f) done = $"kill decision about {k.Victim}: {k.Name} fired, {k.Victim} still alive after 20 s (wounded, ran, or out of sight)";
                }
                if (done == null) continue;
                Kills.RemoveAt(i);
                if (warn) HandsAreHighPlugin.Log.LogWarning(done);
                else HandsAreHighPlugin.Log.LogInfo(done);
            }
        }

        // ---- the end of a prisoner ---------------------------------------------------------------

        // " by X: <decided | RESTRAINT LEAK | ...>" for the 'killed with his hands up' line.
        internal static string DeathReport(SurrenderState s)
        {
            Player k = s.LastHitBy;
            if (k == null || Time.time - s.LastHitAt > 5f) return " (no hit recorded in the last 5 s: not by a bullet we saw)";
            string who = k.IsAI ? Restraint.Name(k) : "a player" + (k.IsYourPlayer ? " (you)" : "");
            if (!k.IsAI) return $" by {who}";
            if (!Restraint.TryGet(s.Bot, k, out RestraintEntry e)) return $" by {who}: he had made no decision about him (never saw the surrender)";
            if (e.Holds) return $" by {who}: RESTRAINT LEAK (his decision was to hold fire: {e.Why})";
            return $" by {who}: as decided ({e.Why})";
        }

        private static void Log(string msg)
        {
            if (Settings.LogJudgement.Value) HandsAreHighPlugin.Log.LogInfo(msg);
        }
    }
}
