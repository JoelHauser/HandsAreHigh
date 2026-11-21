using System.Collections.Generic;
using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.23.0: how an encounter ends, and what both sides do next.
    //
    // The 0.22.0 log (LogOutput 2026-11-21 04:58, scenario #7 "PMC squad captures a bot"): the
    // captor's plan "hold and release, turned away" was finished 38 s in (kneeling, turned away,
    // weapon collected) and he then stood over a kneeling man until his hold time ran out ("lets
    // him go within 61 s"): Captors.Check waited for HoldUntil before the Leave step of every plan
    // but "disarm and release", and every plan ended in a Leave order. Nothing else could end it.
    //
    // Now, once nothing is left to do (the plan's steps given, no gear to take now, no demand in
    // progress), the captor lingers a moment (personality) and decides ONCE, logged with every
    // input:
    //   - his lethal intent (Judgement.AtRelease) first, unchanged;
    //   - a Leave order (the 0.22 ending);
    //   - walking away: custody ends with no order, the prisoner is left as he is (standing or
    //     kneeling, hands up) and has to notice it himself (SurrenderLogic.Abandonment);
    //   - in a squad with a covering squadmate: the squad moves on and that squadmate takes custody
    //     (the leader's decision to let him go is inherited, no new lethal roll), watches a little,
    //     then ends it his own way.
    // A captor fetching gear is NOT abandoning the encounter: custody (and the layer) stays with him
    // through the whole fetch; only the ending above hands him back to his own AI (SAIN / ORBIT /
    // vanilla: BigBrain picks the next layer as soon as ours stops asking for him).
    //
    // After an ending, neither side picks the encounter up again for NoRecaptureSeconds (the
    // captor's squad takes nobody of that man, the man doesn't give himself up to them). Restraint
    // decisions stay as they were (an unarmed man is still not shot for being near), which is not
    // immunity: a weapon in his hands ends them (Restraint.TickDetached / OnArmed).
    internal static class Encounter
    {
        // ---- the captor's ending -------------------------------------------------------------

        internal enum Ending { None, Release, WalkAway, GuardBehind }

        // Captors.Check, every check while the next plan step is Leave. True: this check is spent
        // (lingering, or the ending was carried out). False: go on with the Leave order as before.
        internal static bool WrapUp(CaptorState cs, SurrenderState s, float now, ThreatLevel t)
        {
            if (!Settings.NaturalEndings.Value) return false;
            if (cs.Wrap == Ending.Release) return false;   // decided: the Leave order (or the kill check) runs
            if (cs.Wrap != Ending.None) return true;       // carried out already (custody is ending)

            // Something still useful to do: wait (bounded by his hold time).
            string busy = StillUseful(cs, s);
            if (busy != null && now < cs.HoldUntil)
            {
                if (cs.DoneSince >= 0f) cs.DoneSince = -1f;
                return true;
            }

            if (cs.DoneSince < 0f)
            {
                cs.DoneSince = now;
                float caution = Caution(cs.Bot);
                float linger = Settings.Between(Settings.LingerMin, Settings.LingerMax) * caution;
                if (t == ThreatLevel.Alert) linger *= 0.35f;   // an enemy about: no lingering
                if (now >= cs.HoldUntil) linger = 0f;
                cs.WrapAt = now + linger;
                if (Settings.LogEncounter.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: done with {s.Name} ({now - cs.Since:0} s, plan {cs.PlanName}{(busy != null ? $"; {busy}, but his time is up" : "")}): " +
                        $"lingers {linger:0.0} s before he decides how it ends (nature x{caution:0.00}{(t == ThreatLevel.Alert ? ", an enemy about x0.35" : "")})");
            }
            if (now < cs.WrapAt) return true;

            // The lethal intent decided at custody comes first (unchanged; logged by Judgement).
            if (Judgement.AtRelease(cs, s)) return true;
            if (!Captors.TryGet(cs.Bot, out CaptorState still) || still != cs) return true;

            Ending e = Choose(cs, s, now, t, out string why);
            cs.Wrap = e;
            cs.WrapWhy = why;
            if (Settings.LogEncounter.Value) HandsAreHighPlugin.Log.LogInfo(why);
            switch (e)
            {
                case Ending.WalkAway: WalkAway(cs, s, "walks away"); return true;
                case Ending.GuardBehind:
                    if (LeaveGuard(cs, s)) return true;
                    cs.Wrap = Ending.WalkAway;
                    WalkAway(cs, s, "walks away (his squadmate couldn't take him)");
                    return true;
                default: return false;   // the Leave order, as in 0.22
            }
        }

        // What still keeps him there: gear he means to take now (or is still weighing), a demand being
        // carried out. Null: nothing.
        private static string StillUseful(CaptorState cs, SurrenderState s)
        {
            if (s.DropQueue.Count > 0 || s.ReportAt > 0f) return "his last order is still being carried out";
            if (CaptorLoot.Fetching(cs.Bot)) return "fetching gear";
            LootPlan plan = cs.Loot;
            if (plan != null)
                foreach (LootCandidate c in plan.Items)
                    // A postponed demanded/needed item keeps him for 20 s at most (then it is fetched
                    // after a release, or left): no stand-off until the hold cap over a "later".
                    if (c.Decision == "take" || c.Decision == "pending" || c.Decision == "unseen" || c.Decision == "fetching" ||
                        (c.Decision == "later" && c.Postponed < Settings.LootMaxPostpone.Value && (c.Demanded || c.Needed) && Time.time - c.FirstAt < 20f))
                        return $"still means to take his {c.Name}";
            if (GearNeeds.Pending(cs)) return "a gear demand under way";
            return null;
        }

        // Scenario forcing (cleared with the scenario): this captor's ending is forced (the linger,
        // the lethal intent check and everything after the ending stay real).
        private static readonly Dictionary<Player, Ending> Forced = new Dictionary<Player, Ending>();
        internal static void ForceEnding(Player captor, Ending e) { if (captor == null) return; if (e == Ending.None) Forced.Remove(captor); else Forced[captor] = e; }

        private static Ending Choose(CaptorState cs, SurrenderState s, float now, ThreatLevel t, out string why)
        {
            if (Forced.TryGetValue(cs.Bot, out Ending forced))
            {
                bool guardOk = cs.Guard != null && cs.Guard.Effective && Perception.Alive(cs.Guard.Bot);
                Ending e0 = forced == Ending.GuardBehind && !guardOk ? Ending.WalkAway : forced;
                why = $"{cs.Name}: how his hold on {s.Name} ends: FORCED {forced}{(e0 != forced ? $" (no squadmate covering him now ({cs.Guard?.CoverWhy ?? "none assigned"}): walks away instead)" : "")}";
                return e0;
            }
            var sb = new StringBuilder($"{cs.Name}: how his hold on {s.Name} ends: ");
            float walk = Settings.WalkAwayChance.Value;
            float nature = Boldness(cs.Bot, out string pers);
            walk *= nature;
            sb.Append($"walk off {Settings.WalkAwayChance.Value:0.00} x nature {nature:0.00} ({pers})");
            if (t == ThreatLevel.Alert) { walk *= 1.8f; sb.Append(", an enemy about x1.8"); }
            bool moving = SquadMovingOn(cs, out string squadWhy);
            if (moving) { walk *= 1.5f; sb.Append($", {squadWhy} x1.5"); }
            if (s.Kneel == KneelPhase.Down || s.TurnedAway) { walk *= 1.3f; sb.Append($", he {(s.Kneel == KneelPhase.Down ? "kneels" : "has his back turned")} x1.3"); }
            if (cs.SawKept != null || cs.Unsafe) { walk *= 0.4f; sb.Append(", a weapon still on him (keeps his eyes on him to the end) x0.4"); }
            walk = Mathf.Clamp(walk, 0.03f, 0.95f);
            float roll = Dice.Value;
            Ending e = roll < walk ? Ending.WalkAway : Ending.Release;
            sb.Append($" = {walk:0.00}, roll {roll:0.00} -> {(e == Ending.WalkAway ? "walks off" : "tells him to go")}");
            // A squadmate on him: the squad may leave him to watch the man (the leader's ending).
            if (e == Ending.WalkAway && Settings.GuardStaysBehind.Value && cs.Guard != null && cs.Guard.Effective && Perception.Alive(cs.Guard.Bot))
            {
                float stay = Mathf.Clamp(0.45f * Caution(cs.Bot), 0.1f, 0.8f);
                float r2 = Dice.Value;
                sb.Append($"; {cs.Guard.Name} covers him: leave him to watch? chance {stay:0.00}, roll {r2:0.00} -> {(r2 < stay ? "yes" : "no")}");
                if (r2 < stay) e = Ending.GuardBehind;
            }
            why = sb.ToString();
            return e;
        }

        // The rest of his squad (not the guard) has moved off or is moving away from here.
        private static bool SquadMovingOn(CaptorState cs, out string why)
        {
            why = null;
            BotsGroup g = cs.Group;
            if (g == null) return false;
            Vector3 at = cs.Bot.Position;
            int away = 0, members = 0;
            for (int i = 0; i < g.MembersCount; i++)
            {
                BotOwner m = g.Member(i);
                if (m == null || m == cs.Owner || m.IsDead || m.GetPlayer == null) continue;
                if (cs.Guard != null && m == cs.Guard.Owner) continue;
                members++;
                Player mp = m.GetPlayer;
                float d = Perception.Flat(mp.Position, at);
                Vector3 v = mp.Velocity; v.y = 0f;
                Vector3 off = mp.Position - at; off.y = 0f;
                if (d > 25f || (d > 8f && v.sqrMagnitude > 0.5f * 0.5f && Vector3.Dot(v, off) > 0f)) away++;
            }
            if (members == 0 || away == 0) return false;
            why = $"{away} of his squad moving on";
            return true;
        }

        // Custody ends with no order: he simply goes (his own AI from the next brain update).
        internal static void WalkAway(CaptorState cs, SurrenderState s, string what)
        {
            cs.WalkedAway = true;
            Remember(cs.Owner, s, "walked away from him");
            Captors.End(cs, $"{what}; {s.Name} is left {(s.Kneel != KneelPhase.Standing ? "on his knees" : "standing")}, hands up", released: true);
            WatchHandback(cs.Bot, cs.Name, $"walked away from {s.Name}");
        }

        // The squad moves on; the covering squadmate takes custody with the leader's decision.
        private static bool LeaveGuard(CaptorState cs, SurrenderState s)
        {
            BotOwner m = cs.Guard?.Owner;
            if (m == null || m.IsDead) return false;
            if (!Captors.HandTo(cs, s, m, $"stays behind to watch him while {cs.Name} moves on", out CaptorState next, out string refused))
            {
                if (Settings.LogEncounter.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {Restraint.Name(m.GetPlayer)} can't take {s.Name} ({refused})");
                return false;
            }
            // The leader decided to let him go: no new lethal roll, a short watch, his own ending after.
            next.IntendsKill = false;
            next.ReleaseJudged = true;
            next.IntentWhy = $"{cs.Name}'s decision to let him go";
            next.Plan.Clear();
            next.Plan.Add(OrderKind.Stay);
            next.Plan.Add(OrderKind.Leave);
            next.Step = 0;
            next.PlanName = $"watches him after {cs.Name} moved on";
            next.CloseDecided = true;
            next.Close = false;
            next.Loot = new LootPlan { Assessed = true };   // the leader took what he wanted
            next.HoldUntil = Time.time + Dice.Range(8f, 20f);
            cs.WalkedAway = true;
            Remember(cs.Owner, s, "moved on, left a guard");
            if (Settings.LogEncounter.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: moves on and leaves {next.Name} to watch {s.Name} (up to {next.HoldUntil - Time.time:0} s; inherits the decision to let him go)");
            WatchHandback(cs.Bot, cs.Name, $"moved on, {next.Name} watches {s.Name}");
            return true;
        }

        // ---- no recapture loop -----------------------------------------------------------------

        private sealed class Ended { internal BotsGroup Group; internal BotOwner Owner; internal Player Prisoner; internal float Until; internal string How; }
        private static readonly List<Ended> Endings = new List<Ended>(8);

        // An encounter between this captor (and his squad) and this man ended on the captor's
        // terms (released, walked away, left with a guard who then let him go).
        internal static void Remember(BotOwner captor, SurrenderState s, string how)
        {
            if (captor == null || s?.Bot == null || Settings.NoRecaptureSeconds.Value <= 0f) return;
            float until = Time.time + Settings.NoRecaptureSeconds.Value;
            for (int i = 0; i < Endings.Count; i++)
                if (Endings[i].Prisoner == s.Bot && (Endings[i].Owner == captor || (Endings[i].Group != null && Endings[i].Group == captor.BotsGroup)))
                { Endings[i].Until = until; Endings[i].How = how; return; }
            if (Endings.Count >= 32) Endings.RemoveAt(0);
            Endings.Add(new Ended { Group = captor.BotsGroup, Owner = captor, Prisoner = s.Bot, Until = until, How = how });
        }

        // This bot (or his squad) ended an encounter with this man a moment ago.
        internal static bool Recently(BotOwner o, Player prisoner, out string why)
        {
            why = null;
            if (o == null || prisoner == null || Endings.Count == 0) return false;
            float now = Time.time;
            for (int i = Endings.Count - 1; i >= 0; i--)
            {
                Ended e = Endings[i];
                if (now >= e.Until || !Perception.Alive(e.Prisoner)) { Endings.RemoveAt(i); continue; }
                if (e.Prisoner != prisoner) continue;
                if (e.Owner != o && (e.Group == null || e.Group != o.BotsGroup)) continue;
                why = $"{(e.Owner == o ? "he" : "his squad")} {e.How} {Settings.NoRecaptureSeconds.Value - (e.Until - now):0} s ago";
                return true;
            }
            return false;
        }

        // The man's side: no surrender to the captor (or the captor's squad) he was just let go by.
        internal static bool RecentlyFreedFrom(Player prisoner, Player captor, out string why)
        {
            why = null;
            BotOwner o = captor != null && captor.IsAI ? captor.AIData?.BotOwner : null;
            return o != null && Recently(o, prisoner, out why);
        }

        // ---- the hand-back, followed once in the log -------------------------------------------

        private sealed class Handback { internal Player Bot; internal string Name, What; internal float At; internal Vector3 From; }
        private static readonly List<Handback> Handbacks = new List<Handback>(4);

        internal static void WatchHandback(Player bot, string name, string what)
        {
            if (!Settings.LogEncounter.Value || bot == null || Handbacks.Count >= 16) return;
            Handbacks.Add(new Handback { Bot = bot, Name = name, What = what, At = Time.time + 4f, From = bot.Position });
            HandsAreHighPlugin.Log.LogInfo($"{name}: back to his own AI ({what}): our guarding layer, movement, look and pick-up overrides are off him; whatever layer his brain picks next has him");
        }

        // ---- the speech floor (0.23.0) -----------------------------------------------------------
        //
        // One encounter (a prisoner and the bots dealing with him) speaks one line at a time. The
        // custody holder gives the orders; a covering squadmate says only what his role needs
        // (Covering / Threat / UnderFire / Resist); urgent lines always go (and take the floor).
        // The prisoner's own lines take the floor too. SAIN's and the game's own squad talk are not
        // ours: their speakers' Speaking/Busy still blocks any of ours (CaptorVoice, Voice).

        internal static SurrenderState Of(Player bot)
        {
            if (bot == null) return null;
            if (Captors.TryGet(bot, out CaptorState cs)) return cs.Prisoner;
            if (Overwatch.TryGet(bot, out CoverGuard g)) return g.Prisoner;
            return null;
        }

        // A line this bot may say in his role (false: not his to say; logged by the caller).
        internal static bool MaySay(Player bot, CaptorLine line, out string why)
        {
            why = null;
            if (!Settings.SquadFloor.Value) return true;
            SurrenderState s = Of(bot);
            if (s == null) return true;
            bool primary = s.Custody != null && s.Custody.Bot == bot;
            if (primary) return true;
            switch (line)
            {
                case CaptorLine.Covering:
                case CaptorLine.Threat:
                case CaptorLine.UnderFire:
                case CaptorLine.Resist:
                case CaptorLine.TakeOver:
                    return true;
            }
            why = $"not his to say ({(s.Custody != null ? s.Custody.Name : "nobody")} speaks for the encounter)";
            return false;
        }

        // Is the floor free for this speaker (non-urgent)? Urgent lines never wait here.
        internal static bool FloorFree(Player bot, SurrenderState s, VoicePriority pr, float now, out string busy)
        {
            busy = null;
            if (!Settings.SquadFloor.Value || pr >= VoicePriority.Urgent) return true;
            if (s == null) s = Of(bot);
            if (s == null) return true;
            if (s.FloorBy != null && s.FloorBy != bot && now < s.FloorUntil) { busy = $"{Restraint.Name(s.FloorBy)} is speaking"; return false; }
            // Anyone of this encounter mid-line (their own speaker, whatever started it).
            if (s.Bot != bot && s.Bot?.Speaker != null && s.Bot.Speaker.Speaking) { busy = $"{s.Name} is speaking"; return false; }
            if (s.Custody != null && s.Custody.Bot != bot && s.Custody.Bot?.Speaker != null && s.Custody.Bot.Speaker.Speaking) { busy = $"{s.Custody.Name} is speaking"; return false; }
            CoverGuard g = s.Custody?.Guard;
            if (g?.Bot != null && g.Bot != bot && g.Bot.Speaker != null && g.Bot.Speaker.Speaking) { busy = $"{g.Name} is speaking"; return false; }
            return true;
        }

        internal static void TookFloor(Player bot, SurrenderState s, float length)
        {
            if (!Settings.SquadFloor.Value) return;
            if (s == null) s = Of(bot);
            if (s == null) return;
            s.FloorBy = bot;
            s.FloorUntil = Time.time + Mathf.Clamp(length, 0.4f, 4f) + Settings.SquadFloorGap.Value;
        }

        // ---- personality ---------------------------------------------------------------------------

        // How readily he just walks off (no ceremony). Tuning, not data.
        private static float Boldness(Player p, out string pers)
        {
            pers = SurrenderRegistry.PersonalityOf(p, SurrenderRegistry.HistoryOf(p));
            switch (pers)
            {
                case "Wreckless": return 1.6f;
                case "GigaChad": return 1.4f;
                case "Chad": return 1.25f;
                case "SnappingTurtle": return 1.15f;
                case "Rat": return 1.0f;
                case "Timmy": return 0.6f;
                case "Coward": return 0.5f;
                case "": case null:
                    pers = NaturalSurrender.ClassOf(p) == NaturalSurrender.RoleClass.Scav ? "a Scav" : "no personality";
                    return NaturalSurrender.ClassOf(p) == NaturalSurrender.RoleClass.Scav ? 1.2f : 1f;
                default: return 1f;
            }
        }

        // How long he lingers (and how readily a squad leaves a man watched).
        internal static float Caution(Player p)
        {
            switch (SurrenderRegistry.PersonalityOf(p, SurrenderRegistry.HistoryOf(p)))
            {
                case "Coward": return 1.5f;
                case "Timmy": return 1.35f;
                case "Rat": return 1.1f;
                case "Chad": return 0.85f;
                case "GigaChad": return 0.75f;
                case "Wreckless": return 0.55f;
                default: return 1f;
            }
        }

        // The prisoner's side: how long he takes to believe he was left (x the setting).
        internal static float AssessFactor(SurrenderState s)
        {
            float k = 1.3f - 0.6f * s.Nerve;
            switch (s.Personality)
            {
                case "Coward": k *= 1.4f; break;
                case "Timmy": k *= 1.25f; break;
                case "Rat": k *= 0.9f; break;
                case "Chad": k *= 0.8f; break;
                case "GigaChad": k *= 0.7f; break;
                case "Wreckless": k *= 0.6f; break;
            }
            if (s.Kneel == KneelPhase.Down) k *= 1.15f;
            return Mathf.Clamp(k, 0.4f, 2.2f);
        }

        // ---- per frame ---------------------------------------------------------------------------

        internal static int Count => Handbacks.Count + Endings.Count;

        internal static void Tick()
        {
            if (Handbacks.Count == 0) return;
            float now = Time.time;
            for (int i = Handbacks.Count - 1; i >= 0; i--)
            {
                Handback h = Handbacks[i];
                if (now < h.At) continue;
                Handbacks.RemoveAt(i);
                if (!Perception.Alive(h.Bot)) continue;
                string layer = "?";
                try { BotOwner o = h.Bot.AIData?.BotOwner; if (o != null) layer = BrainManager.GetActiveLayerName(o) ?? "?"; } catch { }
                bool ours = layer.StartsWith("HandsAreHigh");
                float moved = Perception.Flat(h.Bot.Position, h.From);
                HandsAreHighPlugin.Log.LogInfo($"{h.Name}: 4 s after the hand-back ({h.What}): layer {layer}, {moved:0.0} m moved, {(h.Bot.Velocity.sqrMagnitude > 0.3f * 0.3f ? "moving" : "still")}" +
                    $"{(ours ? "; STILL ON OUR LAYER (a fetch after the release, or a new custody)" : "")}");
            }
        }

        internal static void Clear()
        {
            Endings.Clear();
            Handbacks.Clear();
            Forced.Clear();
        }
    }
}
