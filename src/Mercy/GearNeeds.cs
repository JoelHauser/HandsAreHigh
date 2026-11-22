using System.Collections.Generic;
using System.Text;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Mercy
{
    // 0.23.0: a captor who lacks body armour, a rig or a backpack may tell his prisoner to drop one
    // he is wearing, when it suits him. Only from the real items:
    //   fits      the game's own InventoryEquipment.FindSlotToPickUp on the CAPTOR (it checks the
    //             slot's own conditions: type, blocking / conflicting gear such as an armoured rig
    //             over body armour). It must land in the very slot he is missing; no answer = no.
    //   armour    best ArmorComponent class on the item (its own and its plates') and the worst
    //             durability fraction among them (RepairableComponent Durability / MaxDurability):
    //             class >= 2 and >= 30 % durability, else not worth wearing.
    //   space     a rig or backpack's grid cells (CompoundItem.Grids, GridWidth x GridHeight): a
    //             rig >= 6 cells (or armoured), a backpack >= 12.
    // Weight is not weighed (bots aren't slowed by it in a way that would change this choice).
    // One demand per slot per encounter (never repeated, never for what he doesn't wear); after
    // each transfer the needs are read again from the captor's equipment, so a filled slot ends
    // that need. The item goes down through the normal queued drop (Orders: DropItem, the same
    // Player.InventoryController.ThrowItem every order uses: contents stay inside) and comes up
    // through CaptorLoot's normal fetch (QuickFindAppropriatePlace with PickUp, which includes
    // TryEquip: FindSlotToPickUp first; one transaction, so nothing is duplicated or lost; it is
    // read back from his equipment afterwards). Nothing here touches hands: busy hands can't arise.
    internal sealed class GearDemand
    {
        internal readonly HashSet<EquipmentSlot> Asked = new HashSet<EquipmentSlot>();
        internal readonly HashSet<Item> Items = new HashSet<Item>();   // demanded for a need (CaptorLoot: Needed)
        internal EquipmentSlot Slot;
        internal Item Item;
        internal string Name;
        internal bool Queued, Given;
        internal float GivenAt, NextLook;
    }

    internal static class GearNeeds
    {
        private static readonly EquipmentSlot[] Needs = { EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Backpack };

        internal static string SlotWord(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.ArmorVest: return "body armour";
                case EquipmentSlot.TacticalVest: return "rig";
                case EquipmentSlot.Backpack: return "backpack";
                default: return slot.ToString();
            }
        }

        // A demand given and not resolved yet (the item still on its way to him).
        internal static bool Pending(CaptorState cs)
        {
            GearDemand g = cs?.Gear;
            if (g == null || (!g.Queued && !g.Given)) return false;
            if (g.Queued && !g.Given) return true;   // in the plan, not yet given
            if (Time.time - g.GivenAt > 30f) return false;
            LootCandidate c = Candidate(cs, g.Item);
            if (c == null) return Time.time - g.GivenAt < 6f;   // not landed / not weighed yet
            return c.Decision != "leave" && c.Decision != "failed" && c.Decision != "taken" && c.Decision != "after release";
        }

        internal static bool IsNeeded(CaptorState cs, Item it) => cs?.Gear != null && it != null && cs.Gear.Items.Contains(it);

        private static LootCandidate Candidate(CaptorState cs, Item it)
        {
            if (cs.Loot == null || it == null) return null;
            foreach (LootCandidate c in cs.Loot.Items) if (c.Item == it) return c;
            return null;
        }

        // Captors.Check, when the next step is Leave. True: a demand was put in front of it.
        internal static bool Consider(CaptorState cs, SurrenderState s, float now)
        {
            if (!Settings.GearDemands.Value || !Settings.CaptorLooting.Value || cs.Unsafe) return false;
            GearDemand g = cs.Gear ?? (cs.Gear = new GearDemand());
            if (Pending(cs) || now < g.NextLook) return false;
            g.NextLook = now + 2f;
            // Under control only: hands up and empty, nothing being dropped, no other enemy about.
            if (s.Weight < 0.95f || s.DropPending || s.HandsPending || s.DropQueue.Count > 0 || s.ReportAt > 0f || !Arsenal.HandsEmpty(s.Bot)) return false;
            if (Restraint.OtherThreat(cs.Owner, s.Bot, out _)) return false;
            Player me = cs.Bot;
            InventoryEquipment mine = me.Equipment, his = s.Bot.Equipment;
            if (mine == null || his == null || me.InventoryController == null) return false;

            var log = new StringBuilder();
            foreach (EquipmentSlot slot in Needs)
            {
                if (g.Asked.Contains(slot)) continue;
                if (mine.GetSlot(slot)?.ContainedItem != null) continue;    // he has one
                Item it = his.GetSlot(slot)?.ContainedItem;
                g.Asked.Add(slot);                                           // weighed once per encounter
                if (it == null) continue;                                    // the man has none: nothing to ask for
                string worth = Suits(me, slot, it, out string fitWhy);
                string name = Compliance.Name(it);
                if (worth == null)
                {
                    log.Append($"{(log.Length > 0 ? "; " : "")}{SlotWord(slot)}: his {name} won't do ({fitWhy})");
                    continue;
                }
                float want = Mathf.Clamp01(0.8f * Greed(me));
                float roll = Dice.Value;
                bool asks = roll < want;
                log.Append($"{(log.Length > 0 ? "; " : "")}{SlotWord(slot)}: none of his own; the man wears a {name} ({worth}): want {want:0.00}, roll {roll:0.00} -> {(asks ? "tells him to drop it" : "lets it be")}");
                if (!asks) continue;
                g.Slot = slot;
                g.Item = it;
                g.Name = name;
                g.Queued = true;
                g.Given = false;
                cs.Plan.Insert(Mathf.Min(cs.Step, cs.Plan.Count - 1), OrderKind.DropItem);
                cs.NextOrderAt = Mathf.Max(cs.NextOrderAt, now + Dice.Range(0.4f, 1f));
                if (Settings.LogEncounter.Value || Settings.LogLoot.Value) MercyPlugin.Log.LogInfo($"{cs.Name}: gear he lacks, from {s.Name}: {log}");
                return true;
            }
            if (log.Length > 0 && (Settings.LogEncounter.Value || Settings.LogLoot.Value)) MercyPlugin.Log.LogInfo($"{cs.Name}: gear he lacks, from {s.Name}: {log}");
            return false;
        }

        // Just before the order: still worn there, still needed. Sets what the order drops.
        internal static bool Arm(CaptorState cs, SurrenderState s)
        {
            GearDemand g = cs.Gear;
            string why = null;
            if (g == null || g.Item == null) why = "nothing to ask for";
            else if (!Arsenal.StillWorn(s.Bot, g.Slot, g.Item)) why = $"he no longer wears the {g.Name}";
            else if (cs.Bot.Equipment?.GetSlot(g.Slot)?.ContainedItem != null) why = $"he has a {SlotWord(g.Slot)} of his own now";
            if (why != null)
            {
                if (g != null) g.Queued = false;
                if (Settings.LogEncounter.Value) MercyPlugin.Log.LogInfo($"{cs.Name}: drops his demand for {s.Name}'s {SlotWord(g?.Slot ?? EquipmentSlot.ArmorVest)} ({why})");
                return false;
            }
            s.DemandSlot = g.Slot;
            s.DemandItem = g.Item;
            return true;
        }

        internal static void Given(CaptorState cs, SurrenderState s, float now)
        {
            GearDemand g = cs.Gear;
            if (g == null) return;
            g.Queued = false;
            g.Given = true;
            g.GivenAt = now;
            if (g.Item != null) g.Items.Add(g.Item);
        }

        // Null: not worth it (why says); else what makes it worth it.
        private static string Suits(Player me, EquipmentSlot slot, Item it, out string why)
        {
            why = null;
            ItemAddress at = null;
            try { at = me.InventoryController.FindSlotToPickUp(it); } catch { }
            if (!(at?.Container is Slot sl) || sl.ID != slot.ToString())
            {
                why = at == null ? "the game won't let him wear it (conflicting or blocked slot)" : $"it would go in his {(at.Container as Slot)?.ID ?? "bags"}, not his {SlotWord(slot)} slot";
                return null;
            }
            bool armoured = Armour(it, out int cls, out float dur);
            if (slot == EquipmentSlot.ArmorVest || (armoured && slot == EquipmentSlot.TacticalVest))
            {
                if (!armoured) { why = "no armour in it"; return null; }
                if (cls < 2) { why = $"class {cls}: hardly armour"; return null; }
                if (dur < 0.3f) { why = $"class {cls} but {dur * 100f:0} % durability"; return null; }
                if (slot == EquipmentSlot.ArmorVest) return $"class {cls}, {dur * 100f:0} % durability";
            }
            int cells = Cells(it);
            if (slot == EquipmentSlot.TacticalVest)
            {
                if (!armoured && cells < 6) { why = $"{cells} cells: too small to bother"; return null; }
                return armoured ? $"armoured rig class {cls}, {dur * 100f:0} %, {cells} cells" : $"{cells} cells";
            }
            if (cells < 12) { why = $"{cells} cells: too small to bother"; return null; }
            return $"{cells} cells";
        }

        // Best class on the item (own and plates), worst durability fraction among those components.
        private static bool Armour(Item it, out int cls, out float dur)
        {
            cls = 0; dur = 1f;
            bool any = false;
            ArmorComponent own = it.GetItemComponent<ArmorComponent>();
            if (own != null) { any = true; Take(own, ref cls, ref dur); }
            foreach (ArmorComponent a in it.GetItemComponentsInChildren<ArmorComponent>())
            {
                if (a == own) continue;
                any = true;
                Take(a, ref cls, ref dur);
            }
            return any;
        }

        private static void Take(ArmorComponent a, ref int cls, ref float dur)
        {
            cls = Mathf.Max(cls, a.ArmorClass);
            RepairableComponent r = a.Repairable;
            if (r != null && r.MaxDurability > 0f) dur = Mathf.Min(dur, Mathf.Clamp01(r.Durability / r.MaxDurability));
        }

        private static int Cells(Item it)
        {
            if (!(it is CompoundItem ci) || ci.Grids == null) return 0;
            int n = 0;
            foreach (var gr in ci.Grids) if (gr != null) n += gr.GridWidth * gr.GridHeight;
            return n;
        }

        private static float Greed(Player p)
        {
            switch (SurrenderRegistry.PersonalityOf(p, SurrenderRegistry.HistoryOf(p)))
            {
                case "Rat": return 1.2f;
                case "Wreckless": return 1.1f;
                case "Coward": return 0.75f;
                case "Timmy": return 0.9f;
                default: return 1f;
            }
        }
    }
}
