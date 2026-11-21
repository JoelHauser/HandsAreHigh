using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // Setup operations shared by every preset. Each goes through the game's own mechanism
    // (health controller, inventory transactions, the selector, the surrender registry, the
    // captor's Accept) and returns what it did, for the timeline. None of them imitates the
    // surrender system: they only set up the conditions it then works from.
    internal static class ScenarioOps
    {
        internal static readonly string[] HealthPresets = { "Full", "Wounded", "Badly wounded", "Bleeding", "Leg destroyed" };
        internal static readonly string[] AmmoPresets = { "As generated", "No spare ammo", "Empty gun" };
        internal static readonly string[] KitPresets = { "As generated", "Keeps sidearm", "Keeps slung primary", "Knife only", "Disarmed", "Grenades only" };
        // 0.23.0: a captor missing gear (GearNeeds tests): removed by the same transaction.
        // 0.24.0: "No knife" (the dropped-knife tests).
        internal static readonly string[] CaptorGearPresets = { "As generated", "No body armour", "No rig", "No backpack", "No armour, rig or backpack", "No knife" };
        internal static readonly string[] NervePresets = { "Rolled", "0.0", "0.25", "0.5", "0.75", "1.0" };
        internal static readonly string[] Personalities = { "SAIN's own", "Coward", "Timmy", "Rat", "Normal", "Chad", "GigaChad", "Wreckless" };

        // ---- who he is --------------------------------------------------------------------

        // Per-participant values from the seed (independent of spawn order), else his own rolls.
        // Mod-side values only: nerve, reaction time, mercy, personality as this mod reads it.
        internal static string ApplyIdentity(Scenario sc, Participant p)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(p.Player);
            var parts = new List<string>(4);
            if (sc.Seed > 0)
            {
                var r = new System.Random(sc.Seed * 7919 + p.Index * 104729 + 17);
                float lo = Mathf.Min(Settings.NerveMin.Value, Settings.NerveMax.Value), hi = Mathf.Max(Settings.NerveMin.Value, Settings.NerveMax.Value);
                h.Nerve = lo + (float)r.NextDouble() * (hi - lo);
                float rlo = Mathf.Min(Settings.ReactionMin.Value, Settings.ReactionMax.Value), rhi = Mathf.Max(Settings.ReactionMin.Value, Settings.ReactionMax.Value);
                h.Reaction = rlo + (float)r.NextDouble() * (rhi - rlo);
                h.Mercy = 0.75f + (float)r.NextDouble() * 0.5f;
                parts.Add($"seeded nerve {h.Nerve:0.00}, reaction {h.Reaction:0.00} s, disposition x{h.Mercy:0.00}");
            }
            if (p.Nerve != null)
            {
                h.Nerve = Mathf.Clamp01(p.Nerve.Value);
                parts.Add($"nerve set to {h.Nerve:0.00}");
            }
            if (!string.IsNullOrEmpty(p.Personality))
            {
                h.PersonalityRead = true;
                h.Personality = p.Personality == "Normal" ? "" : p.Personality;
                parts.Add($"personality read by this mod as {p.Personality} (SAIN's own behaviour unchanged)");
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        // ---- health -----------------------------------------------------------------------

        // Health changes through ActiveHealthController (no hit event: Player.BeingHitAction is
        // for real hits). Never below 12 HP on a part, so no preset kills.
        internal static string ApplyHealth(Player p, string preset)
        {
            if (preset == null || preset == "Full") return null;
            ActiveHealthController hc = p.ActiveHealthController;
            if (hc == null) return "no active health controller: health unchanged";
            float before = Arsenal.HealthFraction(p);
            switch (preset)
            {
                case "Wounded":
                    Hurt(hc, EBodyPart.Chest, 35f); Hurt(hc, EBodyPart.Stomach, 30f); Hurt(hc, EBodyPart.LeftLeg, 30f);
                    break;
                case "Badly wounded":
                    Hurt(hc, EBodyPart.Chest, 55f); Hurt(hc, EBodyPart.Stomach, 45f); Hurt(hc, EBodyPart.RightLeg, 50f); Hurt(hc, EBodyPart.LeftArm, 40f);
                    break;
                case "Bleeding":
                    Hurt(hc, EBodyPart.Chest, 35f); Hurt(hc, EBodyPart.LeftLeg, 30f);
                    hc.DoBleed(false, EBodyPart.LeftLeg);
                    break;
                case "Leg destroyed":
                    Hurt(hc, EBodyPart.Stomach, 25f);
                    Hurt(hc, EBodyPart.LeftLeg, 1000f);
                    hc.DestroyBodyPart(EBodyPart.LeftLeg, EDamageType.Bullet);
                    break;
                default: return $"unknown health preset {preset}";
            }
            string injury;
            float inj = NaturalSurrender.Injury(p, out injury);
            return $"health {preset}: {before:P0} -> {Arsenal.HealthFraction(p):P0}, injury {inj:0.00} ({injury})";
        }

        // Damage to one part, never below 12 HP there (lethal only through Kill).
        internal static float Hurt(ActiveHealthController hc, EBodyPart part, float damage)
        {
            var h = hc.GetBodyPartHealth(part);
            float d = Mathf.Min(damage, Mathf.Max(0f, h.Current - 12f));
            if (d <= 0f) return 0f;
            return hc.ApplyDamage(part, d, new DamageInfo { DamageType = EDamageType.Bullet, Damage = d });
        }

        // ---- kit --------------------------------------------------------------------------

        // Weapons and removable kit he carries at spawn, tracked by identity: whatever of it
        // ends up lying loose (a dropped gun, kit dropped on an order) is cleaned up; nothing
        // else in the world is.
        internal static void TrackWeapons(Participant p)
        {
            InventoryEquipment eq = p.Player?.Equipment;
            if (eq == null) return;
            foreach (EquipmentSlot slot in Arsenal.KitSlots)
            {
                Item it = eq.GetSlot(slot)?.ContainedItem;
                if (it != null) p.Items.Add(it);
            }
        }

        // Removal through the game's inventory transaction (ItemManipulator.Remove +
        // TryRunNetworkTransaction): the item leaves his inventory, nothing is dropped in the
        // world. Never the item in his hands.
        internal static string ApplyKit(Scenario sc, Participant p, string preset)
        {
            if (preset == null || preset == "As generated") return null;
            Player bot = p.Player;
            var remove = new List<EquipmentSlot>(3);
            string note = null;
            switch (preset)
            {
                case "Keeps sidearm":
                    remove.Add(EquipmentSlot.SecondPrimaryWeapon); remove.Add(EquipmentSlot.Scabbard);
                    if (!Has(bot, EquipmentSlot.Holster)) note = "his generated kit has no sidearm: after the drop he keeps nothing (relaunch, or another seed)";
                    break;
                case "Keeps slung primary":
                    remove.Add(EquipmentSlot.SecondPrimaryWeapon); remove.Add(EquipmentSlot.Scabbard);
                    if (!Has(bot, EquipmentSlot.Holster) || !Has(bot, EquipmentSlot.FirstPrimaryWeapon))
                        note = "needs a primary and a sidearm (he drops the sidearm, keeps the primary slung); his generated kit lacks one";
                    break;
                case "Knife only":
                    remove.Add(EquipmentSlot.SecondPrimaryWeapon); remove.Add(EquipmentSlot.Holster);
                    if (!Has(bot, EquipmentSlot.Scabbard)) note = "his generated kit has no knife";
                    break;
                case "Disarmed":
                    remove.Add(EquipmentSlot.SecondPrimaryWeapon); remove.Add(EquipmentSlot.Holster); remove.Add(EquipmentSlot.Scabbard);
                    break;
                case "Grenades only":   // 0.24.0: no gun or knife left after the drop; his grenades stay
                    remove.Add(EquipmentSlot.FirstPrimaryWeapon); remove.Add(EquipmentSlot.SecondPrimaryWeapon); remove.Add(EquipmentSlot.Holster); remove.Add(EquipmentSlot.Scabbard);
                    if (Arsenal.Throwables(bot) == 0) note = "his generated kit has no grenade: relaunch, or another seed (the test needs at least one)";
                    break;
                case "No knife": remove.Add(EquipmentSlot.Scabbard); break;
                case "No body armour": remove.Add(EquipmentSlot.ArmorVest); break;
                case "No rig": remove.Add(EquipmentSlot.TacticalVest); break;
                case "No backpack": remove.Add(EquipmentSlot.Backpack); break;
                case "No armour, rig or backpack": remove.Add(EquipmentSlot.ArmorVest); remove.Add(EquipmentSlot.TacticalVest); remove.Add(EquipmentSlot.Backpack); break;
                default: return $"unknown kit preset {preset}";
            }
            var done = new List<string>(3);
            Item inHands = bot.HandsController?.Item;
            foreach (EquipmentSlot slot in remove)
            {
                Item it = bot.Equipment?.GetSlot(slot)?.ContainedItem;
                if (it == null || it == inHands) continue;
                done.Add($"{Arsenal.SlotName(slot)} {it.ShortName.Localized()}");
                RemoveItem(sc, p, it);
            }
            return $"kit {preset}: removed {(done.Count == 0 ? "nothing" : string.Join(", ", done))}{(note == null ? "" : " (" + note + ")")}";
        }

        private static bool Has(Player bot, EquipmentSlot slot) => bot.Equipment?.GetSlot(slot)?.ContainedItem != null;

        internal static void RemoveItem(Scenario sc, Participant p, Item it)
        {
            InventoryController ic = p.Player.InventoryController;
            // simulate: the transaction below performs it (every game call site that hands the
            // result to TryRunNetworkTransaction simulates; executing here first removed the item
            // and then the transaction threw "Trying to get parent of an item that doesn't have a
            // parent", aborting the rest of the ammo setup: 0.13.1 log, every ammo preset).
            if (it.CurrentAddress == null) return;
            var op = ItemManipulator.Remove(it, ic, simulate: true);
            if (op.Failed)
            {
                sc.Note(TL.ExecFail, $"{p.Tag}: the game refused to remove {it.ShortName.Localized()} ({op.Error})");
                return;
            }
            p.Items.Remove(it);
            ic.TryRunNetworkTransaction(op, r =>
            {
                if (!r.Succeed) sc.Note(TL.ExecFail, $"{p.Tag}: removing {it.ShortName.Localized()} failed in the transaction");
            });
        }

        // Spare magazines and loose rounds (and with "Empty gun" also the rounds in his gun),
        // removed by transaction. The game may still refill a bot's magazines on its own
        // (BotReload.AddAmmoToMagazines); the overlay shows his ammo state live.
        internal static string ApplyAmmo(Scenario sc, Participant p, string preset)
        {
            if (preset == null || preset == "As generated") return null;
            Player bot = p.Player;
            InventoryEquipment eq = bot.Equipment;
            if (eq == null) return "no equipment";
            var spare = new List<Item>(16);
            var loaded = new List<Item>(8);
            foreach (Item it in eq.GetAllItems())
            {
                if (it is Magazine mag)
                {
                    if (mag.Parent?.Container?.ParentItem is Weapon) continue;   // in a gun
                    spare.Add(mag);
                }
                else if (it is Ammo)
                {
                    Item parent = it.Parent?.Container?.ParentItem;
                    if (parent is Magazine pm && pm.Parent?.Container?.ParentItem is Weapon) loaded.Add(it);
                    else if (parent is Weapon) loaded.Add(it);       // chambered
                    else if (!(parent is Magazine)) spare.Add(it);   // loose rounds; rounds in a spare mag go with it
                }
            }
            int n = 0;
            foreach (Item it in spare) { RemoveItem(sc, p, it); n++; }
            int m = 0;
            if (preset == "Empty gun") foreach (Item it in loaded) { RemoveItem(sc, p, it); m++; }
            return $"ammo {preset}: removed {n} spare magazine(s)/stack(s){(preset == "Empty gun" ? $", {m} loaded stack(s)" : "")}; out of ammo now: {(Arsenal.OutOfAmmo(p.Owner2) ? "yes" : "not yet (transactions pending, or the bot refilled)")}";
        }

        // "Keeps slung primary": the sidearm in his hands first (the real selector draw), so the
        // surrender drops the sidearm. Returns null when it's in his hands, "" to wait.
        internal static string HoldSidearm(Participant p, ref bool asked)
        {
            Player bot = p.Player;
            Item pistol = bot.Equipment?.GetSlot(EquipmentSlot.Holster)?.ContainedItem;
            if (pistol == null) return "no sidearm to hold";
            if (bot.HandsController?.Item == pistol) return null;
            BotWeaponSelector sel = p.Owner2.WeaponManager?.Selector;
            if (sel == null) return "no weapon selector";
            if (!asked && !sel.IsChanging)
            {
                asked = true;
                sel.UpdateWeaponsList();
                if (!sel.TryChangeToSlot(EquipmentSlot.Holster, true, null)) asked = false;   // retried next tick
            }
            return "";
        }

        // ---- the encounter ------------------------------------------------------------------

        // The real surrender entry (drop, voice, registry, events) with the scenario's reason.
        // decideNow false: a bot captor's decision is skipped because custody follows (forced).
        internal static string Surrender(Scenario sc, Participant prisoner, Player captor, bool decideNow, string why)
        {
            Player bot = prisoner.Player;
            if (!Perception.Alive(bot)) return $"{prisoner.Tag} is dead";
            if (!Perception.Alive(captor)) return "the captor is dead";
            Vector3 knownAt = captor.Position;
            SurrenderRegistry.Surrender(bot, captor, $"scenario {sc.Title}: {why}", knownAt, decideNow, SurrenderSource.Scenario);
            if (!SurrenderRegistry.HoldsBot(bot)) return "the registry refused the surrender (mod disabled?)";
            sc.Force($"{prisoner.Tag} surrenders to {Restraint.Name(captor)} ({why})");
            return null;
        }

        internal static string Custody(Scenario sc, Participant prisoner, Participant captor)
        {
            if (!SurrenderRegistry.TryGet(prisoner.Player, out SurrenderState s)) return $"{prisoner.Tag} isn't surrendered";
            if (s.Custody != null && s.Custody.Bot == captor.Player) return null;
            if (!Captors.ForceAccept(s, captor.Owner2, "scenario: custody given (forced)", out string refused)) return refused;
            sc.Force($"{captor.Tag} has custody of {prisoner.Tag} (Captors.ForceAccept: the real Accept without the decision roll)");
            return null;
        }

        internal static SurrenderState StateOf(Participant p) =>
            p?.Player != null && SurrenderRegistry.TryGet(p.Player, out SurrenderState s) ? s : null;

        // ---- items at cleanup -----------------------------------------------------------------

        internal static LootItem FindLoose(Item item)
        {
            List<IKillable> loot = Singleton<GameWorld>.Instance?.LootList;
            if (loot == null || item == null) return null;
            for (int i = 0; i < loot.Count; i++)
                if (loot[i] is LootItem li && li != null && li.Item == item) return li;
            return null;
        }

        // Where a tracked item is now, and what cleanup does with it. Only loose items lying in
        // the world are removed (GameWorld.DestroyLoot, the call the game makes when a loose
        // item is taken). On you: reported, never touched. On anyone else: left alone.
        internal static string CleanItem(Scenario sc, Item item, bool remove)
        {
            string name = item.ShortName.Localized();
            LootItem loose = FindLoose(item);
            if (loose != null)
            {
                if (!remove) return $"{name}: on the ground";
                try
                {
                    Singleton<GameWorld>.Instance.DestroyLoot(loose);
                    return $"{name}: removed from the ground";
                }
                catch (System.Exception e) { return $"{name}: on the ground, removal failed ({e.Message})"; }
            }
            // Whose equipment the item now sits in (its owner's root item), compared by identity.
            Item root = null;
            try { root = item.Owner?.RootItem; } catch { }
            Player me = sc.Observer;
            if (me != null && root != null && root == me.Equipment)
                return $"{name}: IN YOUR INVENTORY (you took it): left with you, not deleted";
            foreach (Participant q in sc.People)
                if (q.Player != null && root != null && root == q.Player.Equipment)
                    return $"{name}: carried by {q.Tag}{(q.Dead ? " (on his body, left)" : "")}";
            List<Player> all = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (all != null && root != null)
                foreach (Player pl in all)
                    if (pl != null && root == pl.Equipment)
                        return $"{name}: now carried by {Restraint.Name(pl)} (not a scenario bot): left alone";
            return $"{name}: not loose and not carried by anyone known (on a body or gone): left alone";
        }
    }
}
