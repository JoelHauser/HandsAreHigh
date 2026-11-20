using System.Collections.Generic;
using System.Text;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.16.0: Drop your weapons, honestly or not.
    //
    // The order covers every weapon he carries in a weapon slot: the gun in his hands was already
    // thrown when he gave up; this is the slung primary or secondary, the holstered sidearm and the
    // knife (Arsenal.WeaponSlots). Grenades and anything in a bag or a pocket are not weapons the
    // order names (and no captor can see them).
    //
    // One decision per accepted demand (never per frame), from what he is and what he faces:
    //   first demand    drop everything, or quietly keep one (a holstered pistol or the knife first,
    //                   a slung gun last); keeping his only weapon is possible only if it can be
    //                   hidden (pistol, knife), and rarer. Nerve, personality, wounds, fire, how many
    //                   guns he sees covering him and how close they are all weigh in.
    //   told again      hand it over, refuse (stays put, hands up, keeps it), or, rarely and only
    //                   when bold and close enough, go for it there and then (a real draw).
    // Only real inventory: what he keeps is an item in one of his slots; nothing is made up. What
    // dropped is read back from his equipment once the drop is done (OnDropReport), so "disarmed"
    // in the log means his weapon slots are really empty.
    //
    // What a captor knows is separate (Judgement.Notice): a slung rifle he can see, a holstered
    // pistol only close up, a knife never; how many items he saw land.
    internal static class Compliance
    {
        internal const string Covers = "guns slung or holstered and his knife";

        // Scenario forcing (cleared with the scenario): this bot's answer is forced.
        private static readonly Dictionary<Player, ComplianceChoice> Forced = new Dictionary<Player, ComplianceChoice>();
        internal static void Force(Player p, ComplianceChoice c) { if (p != null) { if (c == ComplianceChoice.Auto) Forced.Remove(p); else Forced[p] = c; } }
        // Scenario forcing: which slot he keeps back when he keeps one (so a test can choose a gun the
        // captor can see, or one he can't).
        private static readonly Dictionary<Player, EquipmentSlot> ForcedSlot = new Dictionary<Player, EquipmentSlot>();
        internal static void ForceSlot(Player p, EquipmentSlot? slot) { if (p == null) return; if (slot == null) ForcedSlot.Remove(p); else ForcedSlot[p] = slot.Value; }
        internal static void Clear() { Forced.Clear(); ForcedSlot.Clear(); }

        // The items to drop now (null: none, he refuses or escalates; outcome says which).
        internal static List<KeyValuePair<EquipmentSlot, Item>> OnDemand(SurrenderState s, Player by, bool human,
            List<KeyValuePair<EquipmentSlot, Item>> all, out string outcome)
        {
            outcome = null;
            float now = Time.time;
            PruneKept(s);
            bool repeat = s.KeptBack.Count > 0 && s.Demands > 0;
            s.Demands++;
            ComplianceChoice forced = Forced.TryGetValue(s.Bot, out ComplianceChoice f) ? f : Settings.ComplianceTest.Value;
            string who = human ? "you" : Restraint.Name(by);
            var log = new StringBuilder($"{s.Name}: told to drop his weapons by {who} (demand {s.Demands}{(repeat ? ", again: he kept something back" : "")}; covers {Covers}; he carries {Describe(all)}): ");

            if (!Settings.PartialCompliance.Value && forced == ComplianceChoice.Auto)
            {
                s.KeptBack.Clear();
                s.Deceiving = false;
                Done(s, log.Append("drops all of it (Prisoners may keep a weapon is off)").ToString());
                return all;
            }

            float nerve = s.Nerve, hurt = s.Injury, fire = Fire(s);
            int guards = Guards(s, by, out string guardWhy);
            float dist = by != null ? Vector3.Distance(by.Position, s.Bot.Position) : 10f;
            float guardsK = guards <= 1 ? 1f : guards == 2 ? 0.6f : 0.35f;
            float closeK = dist < 3f ? 0.7f : 1f;
            float pers = Sly(s.Personality);
            log.Append($"nerve {nerve:0.00}, {(string.IsNullOrEmpty(s.Personality) ? "no personality" : s.Personality)} x{pers:0.00}, fight x{s.Fight:0.00}, hurt {hurt:0.00}, fire {fire:0.00}, {guards} gun(s) on him ({guardWhy}) x{guardsK:0.00}, {dist:0.0} m x{closeK:0.00}; ");

            if (!repeat)
            {
                KeyValuePair<EquipmentSlot, Item>? pick = PickToKeep(all);
                if (ForcedSlot.TryGetValue(s.Bot, out EquipmentSlot fs))
                    foreach (var kv in all) if (kv.Key == fs) { pick = kv; log.Append($"(FORCED: keeps the {fs} one) "); break; }
                if (pick == null)
                {
                    s.KeptBack.Clear();
                    Done(s, log.Append("nothing he could hide -> drops all of it").ToString());
                    return all;
                }
                bool only = all.Count == 1;
                float chance = Settings.KeepWeaponChance.Value * Mathf.Lerp(0.3f, 1.4f, nerve) * s.Fight * pers * (1f - 0.6f * hurt) * (1f - 0.5f * fire) *
                               guardsK * closeK * (only ? Settings.KeepOnlyWeapon.Value : 1f);
                chance = Mathf.Clamp(chance, 0f, 0.6f);
                float roll = Dice.Value;
                bool keep = roll < chance;
                // Refuses: keeps one back now, and refuses when told again.
                if (forced == ComplianceChoice.KeepsOne || forced == ComplianceChoice.Refuses) keep = true;
                else if (forced == ComplianceChoice.DropsAll) keep = false;
                string keptName = Name(pick.Value.Value);
                log.Append($"keep the {keptName} ({Arsenal.SlotName(pick.Value.Key == EquipmentSlot.Scabbard ? (EquipmentSlot?)null : pick.Value.Key)}{(only ? ", his only weapon" : "")})? chance {chance:0.00}, roll {roll:0.00}" +
                           $"{(forced != ComplianceChoice.Auto ? $" (FORCED: {forced})" : "")} -> ");
                if (!keep)
                {
                    s.KeptBack.Clear();
                    s.Deceiving = false;
                    Done(s, log.Append("drops all of it").ToString());
                    return all;
                }
                s.KeptBack.Clear();
                s.KeptBack.Add(pick.Value.Value);
                s.Deceiving = true;
                var rest = new List<KeyValuePair<EquipmentSlot, Item>>(all.Count);
                foreach (var kv in all) if (kv.Value != pick.Value.Value) rest.Add(kv);
                Done(s, log.Append($"drops {(rest.Count == 0 ? "nothing" : Describe(rest))}, keeps the {keptName} back (deception: not a plan to attack, a chance kept open)").ToString());
                if (rest.Count == 0) { outcome = "keeps his hands up, drops nothing"; return null; }
                return rest;
            }

            // Told again: he kept something back and the demand is repeated.
            Item kept = s.KeptBack[0];
            bool gun = kept is Weapon && !Arsenal.IsMelee(kept);
            float comply = Settings.RepeatComply.Value * (1.3f - 0.6f * nerve) / Mathf.Sqrt(Mathf.Max(0.3f, s.Fight)) * (guards > 1 ? 1.25f : 1f) * (1f + 0.5f * hurt) * (1f + 0.3f * (s.Demands - 2));
            bool reach = gun ? dist <= Settings.SidearmRange.Value : dist <= Settings.KnifeRange.Value;
            float escalate = reach ? Settings.RepeatEscalate.Value * Mathf.Max(0f, nerve - 0.4f) * 2.5f * s.Fight * (1f - hurt) * (dist <= 4f ? 1.3f : 0.7f) * guardsK : 0f;
            comply = Mathf.Clamp01(comply);
            escalate = Mathf.Clamp(escalate, 0f, 0.3f);
            float refuse = Mathf.Max(0.05f, 1f - comply - escalate);
            float total = comply + escalate + refuse;
            float r = Dice.Value * total;
            string choice = r < comply ? "comply" : r < comply + escalate ? "escalate" : "refuse";
            if (forced == ComplianceChoice.DropsAll || forced == ComplianceChoice.KeepsOne) choice = "comply";   // KeepsOne: kept once, hands it over when told again
            else if (forced == ComplianceChoice.Refuses) choice = "refuse";
            log.Append($"hand over the {Name(kept)}: comply {comply:0.00} / refuse {refuse:0.00} / escalate {escalate:0.00}{(reach ? "" : " (out of reach to escalate)")}, roll {r:0.00} of {total:0.00}" +
                       $"{(forced != ComplianceChoice.Auto ? $" (FORCED: {forced})" : "")} -> ");
            switch (choice)
            {
                case "comply":
                    s.KeptBack.Clear();
                    s.Deceiving = false;
                    Done(s, log.Append($"drops it too").ToString());
                    return all;
                case "escalate":
                    s.EscalateAt = now + s.NoticeDelay();
                    Done(s, log.Append($"goes for the {Name(kept)} instead").ToString());
                    outcome = $"won't give it up: goes for his {Name(kept)}";
                    return null;
                default:
                    Done(s, log.Append($"refuses: keeps his hands up and the {Name(kept)} on him").ToString());
                    outcome = "refuses to drop it";
                    return null;
            }
        }

        // What he'd keep, if anything: a holstered pistol (quick, hidden), then the knife, then a
        // slung gun (seen by anyone who looks). A lone slung gun can't be hidden.
        private static KeyValuePair<EquipmentSlot, Item>? PickToKeep(List<KeyValuePair<EquipmentSlot, Item>> all)
        {
            KeyValuePair<EquipmentSlot, Item>? holster = null, knife = null, slung = null;
            foreach (var kv in all)
            {
                if (kv.Key == EquipmentSlot.Holster) holster = kv;
                else if (kv.Key == EquipmentSlot.Scabbard) knife = kv;
                else if (slung == null) slung = kv;
            }
            if (holster != null) return holster;
            if (knife != null) return knife;
            if (all.Count > 1) return slung;
            return null;
        }

        // His captor and the captor's squadmates who hold fire at him (restrained), close (25 m),
        // aiming at him and in his view: at most three line checks.
        internal static int Guards(SurrenderState s, Player by, out string who)
        {
            int n = 0;
            var names = new StringBuilder();
            if (by != null && Perception.Alive(by)) { n++; names.Append(by.IsAI ? Restraint.Name(by) : "you"); }
            var map = Restraint.EntriesFor(s.Bot);
            int tries = 0;
            if (map != null)
                foreach (RestraintEntry e in map.Values)
                {
                    if (e.Observer == by || !e.Holds || !Perception.Alive(e.Observer)) continue;
                    if ((e.Observer.Position - s.Bot.Position).sqrMagnitude > 25f * 25f) continue;
                    if (tries++ >= 3) break;
                    if (!Presence.InView(s, Perception.Eye(e.Observer))) continue;
                    if (!Perception.Covering(e.Observer, s.Bot, Settings.WatchAngle.Value)) continue;
                    if (!Perception.LineClear(Perception.Eye(s.Bot), Perception.Eye(e.Observer))) continue;
                    n++;
                    names.Append(names.Length > 0 ? ", " : "").Append(Restraint.Name(e.Observer));
                }
            who = names.Length > 0 ? names.ToString() : "nobody";
            return n;
        }

        // Personality: how readily he deceives (not how brave he is).
        private static float Sly(string p)
        {
            switch (p)
            {
                case "Rat": return 1.4f;
                case "SnappingTurtle": return 1.2f;
                case "Wreckless": return 1.1f;
                case "GigaChad": case "Chad": return 1f;
                case "Timmy": return 0.6f;
                case "Coward": return 0.5f;
                default: return 1f;
            }
        }

        private static float Fire(SurrenderState s)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(s.Bot);
            return Mathf.Clamp01(Suppression.Decayed(h, Time.time) / Mathf.Max(1f, Settings.SustainedFireEvents.Value));
        }

        // The weapons he decided to keep that he really still carries.
        private static void PruneKept(SurrenderState s)
        {
            for (int i = s.KeptBack.Count - 1; i >= 0; i--)
                if (!Carries(s.Bot, s.KeptBack[i])) s.KeptBack.RemoveAt(i);
            if (s.KeptBack.Count == 0) s.Deceiving = false;
        }

        internal static bool Carries(Player bot, Item item)
        {
            if (bot?.Equipment == null || item == null) return false;
            foreach (EquipmentSlot slot in Arsenal.WeaponSlots)
                if (bot.Equipment.GetSlot(slot)?.ContainedItem == item) return true;
            return false;
        }

        // Genuinely: no gun and no knife in any of his weapon slots, and nothing in his hands.
        internal static bool Disarmed(Player bot) =>
            bot != null && Arsenal.GunOnBody(bot) == null && !Arsenal.HasMelee(bot) && Arsenal.HandsEmpty(bot);

        // Something he threw (the gun in his hands at the surrender, items dropped on order).
        internal static void NoteDropped(SurrenderState s, Item item)
        {
            if (s == null || item == null || s.Dropped.Contains(item)) return;
            if (s.Dropped.Count >= 12) s.Dropped.RemoveAt(0);
            s.Dropped.Add(item);
        }

        // The order's report (SurrenderLogic.ProcessDropQueue): read back from his equipment.
        internal static string OnDropReport(SurrenderState s)
        {
            var sb = new StringBuilder();
            var off = new List<string>(4);
            foreach (var kv in s.Ordered)
                if (!Arsenal.StillWorn(s.Bot, kv.Key, kv.Value)) { off.Add(Name(kv.Value)); NoteDropped(s, kv.Value); }
            if (off.Count > 0) sb.Append($"; on the ground: {string.Join(", ", off)}");
            if (s.OrderName != "drop your weapons") return sb.ToString();
            PruneKept(s);
            if (s.KeptBack.Count > 0) sb.Append($"; kept back on purpose: {Name(s.KeptBack[0])} (only the log knows; a captor sees it only if it shows)");
            var still = Arsenal.ItemsFor(s.Bot, Arsenal.WeaponSlots, false);
            sb.Append(Disarmed(s.Bot) ? "; disarmed: no weapon left on him" : $"; NOT disarmed: still carries {Describe(still)}");
            return sb.ToString();
        }

        private static void Done(SurrenderState s, string line)
        {
            if (Settings.LogDecisions.Value || Scenarios.Watching(s.Bot)) HandsAreHighPlugin.Log.LogInfo(line);
        }

        internal static string Name(Item it) => it == null ? "nothing" : it.ShortName.Localized();

        internal static string Describe(List<KeyValuePair<EquipmentSlot, Item>> items)
        {
            if (items == null || items.Count == 0) return "nothing";
            var sb = new StringBuilder();
            foreach (var kv in items) sb.Append(sb.Length > 0 ? ", " : "").Append(Name(kv.Value)).Append(" (").Append(kv.Key).Append(')');
            return sb.ToString();
        }
    }
}
