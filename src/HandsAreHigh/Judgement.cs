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
    //   intent     0.18.0, Captors.Accept, once: does he mean to shoot him when he's done with him?
    //              Kept for the custody, carried out at the release (a compliant man can be shot)
    //   notice     after each Drop your weapons is answered: does he see a weapon still on him?
    //              A slung gun, if he sees the man; a holstered pistol only close; a knife never;
    //              a gun he saw on him before and that didn't land. One roll per answer.
    //   repeat     noticed: he demands it again (Captor repeats a demand at most, a warning line)
    //   refusal    still kept after the repeats: open fire (Refusal turns lethal) or warn and be
    //              done with him (let go early, nothing near him)
    //   release    his plan is over: carry out the intent; only something new since the intent
    //              (a weapon kept back, he learned the man shot them) adds a chance, once
    //   resistance an observed draw or attack: always fights (Restraint.OnArmed, unchanged)
    //
    // Inputs, only what the bot knows: his personality (aggression), being hit by this prisoner
    // (his own EnemyInfo.LastGetHitTime, set by the game's BotOwner.GetHit; a squadmate's too), a
    // man he saw attack after surrendering (Restraint distrust), what he saw of the disarming.
    // 0.18.0: rivalry (BEAR/USEC, Scav/PMC) weighs too. Experience is NOT mercy: it only makes him
    // quicker to notice a weapon kept back (AfterDemand). Custody becoming impractical (a fight,
    // lost sight) is not itself a reason to kill: those endings stay Captors.End / CaptorDown.
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

        // ---- 0.18.0: what weighs on a lethal decision ---------------------------------------------
        //
        // Only what the bot knows or is: his nature (aggression), whether captor and prisoner are
        // rivals (BEAR/USEC, Scav/PMC), this man having shot him or his squad, a weapon he saw him
        // keep back. Level and skill are NOT a factor: experience is tactical (it makes him quicker
        // to notice a weapon kept back), never mercy. 0.16-0.17 cut every lethal chance by up to
        // 35% for experience ("discipline"): with a 4% base that left 2-4% (the 0.17.0 log: 9
        // release rolls, all mercy).
        private static float Weigh(CaptorState cs, SurrenderState s, bool kept, out bool hurt, StringBuilder why)
        {
            float aggr = Aggression(cs.Bot);
            float k = aggr;
            why.Append($"aggression {aggr:0.00}");
            if (Rivals(cs.Bot, s.Bot, out string rv)) { k *= Settings.RivalWeight.Value; why.Append($" x rivals {Settings.RivalWeight.Value:0.0} ({rv})"); }
            hurt = KnownViolence(cs.Owner, s.Bot, out string vWhy);
            if (hurt) { k *= Settings.ViolenceWeight.Value; why.Append($" x he shot ({vWhy}) {Settings.ViolenceWeight.Value:0.0}"); }
            if (kept) { k *= Settings.IncompleteWeight.Value; why.Append($" x kept a weapon back {Settings.IncompleteWeight.Value:0.0}"); }
            return k;
        }

        internal static string Faction(Player p)
        {
            string role = p?.Profile?.Info?.Settings?.Role.ToString() ?? "";
            if (role.IndexOf("pmcBEAR", System.StringComparison.OrdinalIgnoreCase) >= 0 || p?.Profile?.Info?.Side == EPlayerSide.Bear) return "BEAR";
            if (role.IndexOf("pmcUSEC", System.StringComparison.OrdinalIgnoreCase) >= 0 || p?.Profile?.Info?.Side == EPlayerSide.Usec) return "USEC";
            return NaturalSurrender.ClassOf(p) == NaturalSurrender.RoleClass.Scav ? "Scav" : null;
        }

        private static bool Rivals(Player a, Player b, out string why)
        {
            string fa = Faction(a), fb = Faction(b);
            why = $"{fa} vs {fb}";
            return fa != null && fb != null && fa != fb;
        }

        private static float Cap(float c) => Mathf.Clamp(c, 0f, Settings.ExecuteMax.Value);

        // ---- 0.18.0: intent, decided once when he takes custody ------------------------------------

        // Does he mean to shoot this man when he is done with him? Rolled once (Captors.Accept),
        // logged with every input, kept for the whole custody; carried out at the release point
        // (AtRelease), after the disarming and the gear, through his normal targeting and firing.
        // Compliance doesn't buy the prisoner anything here: a compliant man can be shot.
        internal static void Intend(CaptorState cs, SurrenderState s)
        {
            var why = new StringBuilder();
            float k = Weigh(cs, s, kept: false, out bool hurt, why);
            float chance = Cap(Settings.LethalIntent.Value * k);
            float roll = Dice.Value;
            cs.IntentChance = chance;
            cs.IntentHurt = hurt;
            cs.IntendsKill = roll < chance;
            cs.IntentWhy = $"chance {chance:0.00} (base {Settings.LethalIntent.Value:0.00} x {why}), roll {roll:0.00}";
            Log($"{cs.Name}: intent about {s.Name} (decided once, at custody): {cs.IntentWhy} -> {(cs.IntendsKill ? "MEANS TO KILL HIM when he is done with him" : "means to let him go")}");
        }

        // He kept a weapon the captor can see through every demand.
        private static void Refusal(CaptorState cs, SurrenderState s, float now, StringBuilder log, Item seen)
        {
            var why = new StringBuilder();
            float k = Weigh(cs, s, kept: false, out _, why);
            float chance = Cap(Settings.RefusalLethal.Value * k);
            JudgeForce f = ForcedFor(cs.Bot);
            float roll = Dice.Value;
            bool lethal = f == JudgeForce.Lethal || (f == JudgeForce.Auto && (roll < chance || cs.IntendsKill));
            log.Append($"lethal? chance {chance:0.00} (base {Settings.RefusalLethal.Value:0.00} x {why}), roll {roll:0.00}" +
                       $"{(cs.IntendsKill ? " (he meant to kill him anyway)" : "")}{(f != JudgeForce.Auto ? $" (FORCED {f})" : "")} -> ");
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

        // The plan is done (disarmed, gear taken or left): carry out what he decided when he took
        // him. Only something new since then (he saw a weapon kept back, the man turns out to have
        // shot him) is weighed, once, as the extra chance it adds. Once per custody.
        internal static bool AtRelease(CaptorState cs, SurrenderState s)
        {
            if (cs.ReleaseJudged) return false;
            cs.ReleaseJudged = true;
            JudgeForce f = ForcedFor(cs.Bot);
            string head = $"{cs.Name}: done with {s.Name} ({Time.time - cs.Since:0} s, plan {cs.PlanName}; {(cs.Incomplete ? "he kept something back" : "he did as told")}): ";
            if (f != JudgeForce.Auto)
            {
                Log(head + $"FORCED {f} (his own intent was: {(cs.IntendsKill ? "kill" : "let go")}, {cs.IntentWhy})");
                if (f == JudgeForce.Mercy) return false;
                Escalate(cs, s, "FORCED lethal at the release");
                return true;
            }
            if (cs.IntendsKill)
            {
                Log(head + $"carries out what he decided when he took him ({cs.IntentWhy}) -> DECIDES TO KILL him");
                Escalate(cs, s, $"meant to since he took him ({cs.IntentWhy})");
                return true;
            }
            var why = new StringBuilder();
            float k = Weigh(cs, s, cs.Incomplete, out bool hurt, why);
            bool news = cs.Incomplete || (hurt && !cs.IntentHurt);
            if (!news)
            {
                Log(head + $"lets him go, as he decided when he took him ({cs.IntentWhy})");
                return false;
            }
            float now = Cap(Settings.LethalIntent.Value * k);
            float add = cs.IntentChance >= 0.999f ? 1f : Mathf.Clamp01(1f - (1f - now) / (1f - cs.IntentChance));
            float roll = Dice.Value;
            bool lethal = roll < add;
            Log(head + $"something new since he took him ({(cs.Incomplete ? "a weapon kept back" : "")}{(cs.Incomplete && hurt && !cs.IntentHurt ? ", " : "")}{(hurt && !cs.IntentHurt ? "he learned the man shot at them" : "")}): " +
                $"now {now:0.00} (base {Settings.LethalIntent.Value:0.00} x {why}) vs {cs.IntentChance:0.00} at custody, extra chance {add:0.00}, roll {roll:0.00} -> {(lethal ? "DECIDES TO KILL him" : "lets him go")}");
            if (!lethal) return false;
            Escalate(cs, s, cs.Incomplete ? "done with a man who kept a weapon back" : "done with a man who shot at them");
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
            LootPlan loot = cs.Loot;
            CaptorVoice.Interrupt(cs.Bot);
            CaptorVoice.Say(cs.Bot, CaptorLine.Resist);
            Captors.End(cs, $"decided to kill him ({why})");
            bool held = Restraint.HoldsFire(cs.Bot, s.Bot);
            EnemyInfo info = null;
            try
            {
                info = Perception.InfoAbout(o, s.Bot);
                if (info != null && o.Memory != null) o.Memory.GoalEnemy = info;
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("judgement target", e); }
            // The restraint transition, read back: nothing of ours may still hold his fire.
            Log($"{cs.Name}: restraint on {s.Name} now {(entry == null ? "none" : $"{entry.Kind}{(entry.Final ? " (final)" : "")}")}; still holding fire at him: {(held ? "YES (blocked)" : "no")}; " +
                $"the game's ignore flag on him: {(info == null ? "no EnemyInfo yet" : info.IgnoreUntilAggression ? "SET (blocked)" : "clear")}; " +
                $"vanilla target {(o.Memory?.GoalEnemy != null && Perception.Same(o.Memory.GoalEnemy.Person, s.Bot) ? "him" : "not him")}{(SainTargetFilter.Patched ? "; SAIN chooses its own target (watched, steered to him)" : "")}");
            Watch(o, s, why, loot);
        }

        // ---- 0.17.0: a man running from custody --------------------------------------------------

        // Once per bolt, only for a run he saw (Captors.Check): let him run, or fire. Not an attack:
        // a judgement, from his nature, rivalry, what this man did to him and what he saw him keep
        // back. A captor who meant to kill him anyway fires. True when he decided to fire.
        internal static bool OnBolt(CaptorState cs, SurrenderState s)
        {
            var why = new StringBuilder();
            float k = Weigh(cs, s, cs.Incomplete, out _, why);
            float chance = Cap(Settings.BoltLethal.Value * k);
            JudgeForce f = ForcedFor(cs.Bot);
            float roll = Dice.Value;
            bool lethal = f == JudgeForce.Lethal || (f == JudgeForce.Auto && (cs.IntendsKill || roll < chance));
            Log($"{cs.Name}: {s.Name} runs from his custody ({s.Phase}, unarmed): fire on him? chance {chance:0.00} (base {Settings.BoltLethal.Value:0.00} x {why}), roll {roll:0.00}" +
                $"{(cs.IntendsKill ? " (he meant to kill him anyway)" : "")}{(f != JudgeForce.Auto ? $" (FORCED {f})" : "")} -> {(lethal ? "DECIDES TO KILL him" : "lets him run")}");
            if (!lethal) return false;
            Escalate(cs, s, "he ran from custody");
            return true;
        }

        // Debug (scenario event, Ctrl+U): the decision only (forced); the restraint change,
        // targeting and firing that follow are the real ones (and watched below).
        internal static string ForceKillNow(Player captor)
        {
            if (captor == null || !Captors.TryGet(captor, out CaptorState cs)) return "isn't guarding anyone";
            SurrenderState s = cs.Prisoner;
            if (s == null || s.Dead) return "his prisoner is gone";
            HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: lethal decision FORCED (debug; the decision only, everything after it is the real path)");
            Escalate(cs, s, "FORCED by debug");
            return null;
        }

        // ---- 0.17.0 / 0.18.0: is a kill decision carried out? --------------------------------------
        //
        // After Escalate: every 0.5 s, from the bot's real state. Logged once each: target acquired
        // (the game's GoalEnemy, or SAIN's own choice, read in SainTargetFilter), first trigger press
        // (TriggerPatch), the outcome. At 3 s without a shot an interim status line; at 10 s an
        // EXECUTION FAILURE warning naming what stands in the way (layer, SAIN's target, the game's
        // target and ignore flag, hands, sight, aim, restraint, shield). The game's target is set
        // back at most 3 times; a SAIN bot is steered by SainTargetFilter (his visible-enemy choice
        // made the prisoner while nothing is shooting at him). Nothing here fires a shot or touches
        // health: the AI's own combat does it.
        private sealed class KillOrder
        {
            internal BotOwner Owner;
            internal Player Captor, Prisoner;
            internal SurrenderState State;
            internal LootPlan Loot;
            internal string Name, Victim, Why;
            internal float Since, FirstShot = -1f, Acquired = -1f;
            internal int Retargets, SainSteers;
            internal bool Interim;
            internal string SainGoal = "?";
            internal bool SainOn;
        }

        private static readonly List<KillOrder> Kills = new List<KillOrder>(2);
        internal static bool Watching => Kills.Count > 0;

        private static void Watch(BotOwner o, SurrenderState s, string why, LootPlan loot)
        {
            if (Kills.Count >= 8) Kills.RemoveAt(0);
            Kills.Add(new KillOrder { Owner = o, Captor = o.GetPlayer, Prisoner = s.Bot, State = s, Loot = loot, Name = Restraint.Name(o.GetPlayer), Victim = s.Name, Why = why, Since = Time.time });
        }

        private static KillOrder OrderOf(BotOwner o)
        {
            for (int i = 0; i < Kills.Count; i++) if (Kills[i].Owner == o) return Kills[i];
            return null;
        }

        // SainTargetFilter: the man this bot decided to kill (null: none).
        internal static Player KillTarget(BotOwner o) => OrderOf(o)?.Prisoner;

        // SainTargetFilter, after SAIN chose: what it chose, and whether we steered it to him.
        internal static void NoteSainGoal(BotOwner o, string goalName, bool steered, bool onVictim = false)
        {
            KillOrder k = OrderOf(o);
            if (k == null) return;
            k.SainOn = onVictim || steered;
            k.SainGoal = k.SainOn ? k.Victim : goalName ?? "nobody";
            if (!steered) return;
            k.SainSteers++;
            if (k.SainSteers <= 3 || k.SainSteers % 20 == 0)
                Log($"{k.Name}: SAIN had chosen {goalName ?? "nobody"}; his target set to {k.Victim} (he sees him, nothing is shooting at him) [{k.SainSteers}]");
        }

        internal static void TickKills(float now)
        {
            for (int i = Kills.Count - 1; i >= 0; i--)
            {
                KillOrder k = Kills[i];
                string done = null;
                bool warn = false, carried = false;
                if (!Perception.Alive(k.Prisoner))
                {
                    carried = true;
                    done = $"KILL DECISION CARRIED OUT: {k.Victim} is dead {now - k.Since:0.0} s after {k.Name}'s decision ({k.Why}){(k.FirstShot >= 0f ? $"; his first shot +{k.FirstShot - k.Since:0.0} s" : "; no trigger press of his seen (someone else's shot?)")}";
                }
                else if (!Perception.Alive(k.Captor) || k.Owner == null || k.Owner.IsDead) done = $"kill decision about {k.Victim} ends: {k.Name} is down";
                else
                {
                    float shot = Restraint.LastShot(k.Captor);
                    if (k.FirstShot < 0f && shot > k.Since)
                    {
                        k.FirstShot = shot;
                        Log($"{k.Name}: FIRES at {k.Victim} (trigger pressed) {shot - k.Since:0.0} s after deciding to kill him");
                    }
                    EnemyInfo goal = k.Owner.Memory?.GoalEnemy;
                    bool onHim = goal != null && Perception.Same(goal.Person, k.Prisoner);
                    bool sainOn = k.SainOn;
                    if (k.Acquired < 0f && (onHim || sainOn))
                    {
                        k.Acquired = now;
                        Log($"{k.Name}: has {k.Victim} as his target {now - k.Since:0.0} s after deciding ({(sainOn ? "SAIN's choice" : "the game's GoalEnemy")})");
                    }
                    bool sees = Perception.Recognises(k.Owner, k.Prisoner);
                    if (!onHim && k.FirstShot < 0f && k.Retargets < 3 && sees)
                    {
                        EnemyInfo info = Perception.InfoAbout(k.Owner, k.Prisoner);
                        if (info != null && k.Owner.Memory != null)
                        {
                            k.Owner.Memory.GoalEnemy = info;
                            k.Retargets++;
                            Log($"{k.Name}: the game's target was {(goal == null ? "nobody" : Restraint.Name(Perception.PlayerOf(goal.Person)))}; set back to {k.Victim} ({k.Retargets}/3)");
                        }
                    }
                    if (k.FirstShot < 0f && (now - k.Since > 10f || (!k.Interim && now - k.Since > 3f)))
                    {
                        string state = Status(k, goal, sees);
                        if (now - k.Since > 10f)
                        {
                            warn = true;
                            done = $"EXECUTION FAILURE: KILL DECISION NOT CARRIED OUT after 10 s: {k.Name} -> {k.Victim}: {state}";
                        }
                        else
                        {
                            k.Interim = true;
                            Log($"{k.Name}: no shot at {k.Victim} yet, 3 s after deciding: {state}");
                        }
                    }
                    else if (k.FirstShot >= 0f && now - k.FirstShot > 20f) done = $"kill decision about {k.Victim}: {k.Name} fired, {k.Victim} still alive after 20 s (wounded, ran, or out of sight)";
                }
                if (done == null) continue;
                Kills.RemoveAt(i);
                if (warn) HandsAreHighPlugin.Log.LogWarning(done);
                else HandsAreHighPlugin.Log.LogInfo(done);
                // 0.18.0: what he made the man drop is still on the ground: fetched now, like after a release.
                if (carried && k.Loot != null) { try { CaptorLoot.AfterKill(k.Owner, k.State, k.Loot); } catch (System.Exception e) { HandsAreHighPlugin.Report("gear after a kill", e); } }
            }
        }

        // Everything that could stand between a kill decision and a shot, from the bot's real state.
        private static string Status(KillOrder k, EnemyInfo goal, bool sees)
        {
            string layer = "?";
            try { layer = k.Owner.Brain?.ActiveLayerName() ?? "?"; } catch { }
            Restraint.TryGet(k.Prisoner, k.Captor, out RestraintEntry e);
            EnemyInfo info = Perception.InfoAbout(k.Owner, k.Prisoner);
            Vector3 to = Perception.Chest(k.Prisoner) - Perception.Eye(k.Captor);
            float aim = Vector3.Angle(k.Captor.LookDirection, to);
            return $"layer {layer}, game target {(goal == null ? "none" : Restraint.Name(Perception.PlayerOf(goal.Person)))}, SAIN target {k.SainGoal}{(k.SainSteers > 0 ? $" (steered {k.SainSteers}x)" : "")}, " +
                   $"hands {Arsenal.DescribeHands(k.Captor)}, sees him {sees} (EnemyInfo {(info == null ? "none" : $"visible {info.IsVisible}, ignore flag {info.IgnoreUntilAggression}")}), " +
                   $"{to.magnitude:0.0} m, his aim {aim:0} deg off him, restraint {(e == null ? "none" : $"{e.Kind}{(e.Final ? " (final)" : "")}")}, " +
                   $"still holding fire {Restraint.HoldsFire(k.Captor, k.Prisoner)}, shielded {ScenarioShield.Blocks(k.Captor, k.Prisoner)}, prisoner {k.State?.Phase}";
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
