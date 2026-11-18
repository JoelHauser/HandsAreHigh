using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // Everything to do with the bot's weapons and kit: emptying his hands, drawing what he
    // still carries, finding and picking up a gun in the world, and dropping items on order.
    internal static class Arsenal
    {
        // Same layers Looting Bots scans for loose items and bodies.
        private static readonly int LootMask = LayerMask.GetMask("Interactive", "Loot", "Deadbody");
        private static readonly Collider[] Hits = new Collider[256];

        internal static readonly EquipmentSlot[] GunSlots =
        {
            // Sidearm first: it's the fast draw.
            EquipmentSlot.Holster, EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon
        };

        internal static readonly EquipmentSlot[] WeaponSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard
        };

        // Everything a man can take off and hand over. Pockets, secure container, dogtag and
        // armband can't be removed, so they're never asked for.
        internal static readonly EquipmentSlot[] KitSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard,
            EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.ArmorVest,
            EquipmentSlot.Headwear, EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear
        };

        private static readonly EquipmentSlot[] ArmourSlots =
        {
            EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Headwear
        };

        internal static float Health(Player p) =>
            p?.HealthController == null ? 0f : p.HealthController.GetBodyPartHealth(EBodyPart.Common).Current;

        internal static float HealthFraction(Player p)
        {
            if (p?.HealthController == null) return 1f;
            var h = p.HealthController.GetBodyPartHealth(EBodyPart.Common);
            return h.Maximum > 0f ? h.Current / h.Maximum : 1f;
        }

        // Nothing he could fight with is in his hands.
        internal static bool HandsFree(Player p)
        {
            Item inHands = p?.HandsController?.Item;
            return inHands == null || !(inHands is Weapon || IsMelee(inHands));
        }

        // A gun in his hands goes on the ground (and is returned); a knife goes back in its
        // sheath. Holstered and slung guns stay on him, which is what makes a later draw possible.
        internal static Item EmptyHands(Player bot)
        {
            Item inHands = bot.HandsController?.Item;
            if (inHands is Weapon)
            {
                bot.SetEmptyHands(_ => HandsAreHighPlugin.Try("throw weapon",
                    () => bot.InventoryController.ThrowItem(inHands, false, null)));
                return inHands;
            }
            if (IsMelee(inHands)) bot.SetEmptyHands(_ => { });
            return null;
        }

        internal static bool IsMelee(Item item) => item?.GetItemComponent<KnifeComponent>() != null;

        internal static EquipmentSlot? GunOnBody(Player bot)
        {
            foreach (EquipmentSlot slot in GunSlots)
                if (bot.Equipment?.GetSlot(slot)?.ContainedItem is Weapon) return slot;
            return null;
        }

        internal static bool HasMelee(Player bot) =>
            bot.Equipment?.GetSlot(EquipmentSlot.Scabbard)?.ContainedItem != null;

        internal static bool DrawGun(BotOwner owner)
        {
            EquipmentSlot? slot = GunOnBody(owner.GetPlayer);
            if (slot == null) return false;
            owner.WeaponManager.Selector.UpdateWeaponsList();
            return owner.WeaponManager.Selector.TryChangeToSlot(slot.Value, slot == EquipmentSlot.FirstPrimaryWeapon);
        }

        internal static bool DrawMelee(BotOwner owner)
        {
            if (!HasMelee(owner.GetPlayer)) return false;
            owner.WeaponManager.Selector.ChangeToMelee();
            return true;
        }

        // No rounds in the gun and nothing to reload it with, or no gun at all.
        internal static bool OutOfAmmo(BotOwner owner)
        {
            BotWeaponManager wm = owner.WeaponManager;
            if (wm == null) return false;
            if (wm.CurrentWeapon == null) return GunOnBody(owner.GetPlayer) == null;
            try
            {
                // HaveBullets/Reload read the current weapon info, which can be missing
                // mid weapon change; then he's not counted as out of ammo.
                return !wm.HaveBullets && !wm.Reload.CanReload(false);
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        // ---- orders --------------------------------------------------------------------

        internal static bool HasArmour(Item item)
        {
            if (item == null) return false;
            if (item.GetItemComponent<ArmorComponent>() != null) return true;
            return item.GetItemComponentsInChildren<ArmorComponent>().Any();
        }

        // What an order asks for, slot by slot. A container goes with everything in it,
        // so nothing inside is ever listed (or dropped) twice.
        internal static List<KeyValuePair<EquipmentSlot, Item>> ItemsFor(Player bot, EquipmentSlot[] slots, bool armourOnly)
        {
            var list = new List<KeyValuePair<EquipmentSlot, Item>>();
            foreach (EquipmentSlot slot in slots)
            {
                Item item = bot.Equipment?.GetSlot(slot)?.ContainedItem;
                if (item == null) continue;
                if (armourOnly && slot != EquipmentSlot.ArmorVest && !HasArmour(item)) continue;
                list.Add(new KeyValuePair<EquipmentSlot, Item>(slot, item));
            }
            return list;
        }

        internal static EquipmentSlot[] ArmourOrderSlots => ArmourSlots;

        internal static bool StillWorn(Player bot, EquipmentSlot slot, Item item) =>
            bot.Equipment?.GetSlot(slot)?.ContainedItem == item;

        internal static void Drop(Player bot, Item item) =>
            bot.InventoryController.ThrowItem(item, false, null);

        // ---- guns in the world ----------------------------------------------------------

        // Is this gun still lying loose (or on a body) near where we expect it?
        internal static bool StillThere(Item weapon, Vector3 near, float radius = 3f) =>
            weapon != null && FindLying(near, radius, item => item == weapon, null, out _) != null;

        // Nearest gun on the ground or on a body, not too close to the threat and not
        // already claimed by another surrendered bot or in the skip list.
        internal static Item FindWeapon(Player bot, Vector3 threat, float avoidRadius, float radius,
            ICollection<Item> skip, out Vector3 at)
        {
            var claimed = new HashSet<Item>();
            foreach (SurrenderState s in SurrenderRegistry.All)
                if (s.Bot != bot && s.TargetWeapon != null) claimed.Add(s.TargetWeapon);

            return FindLying(bot.Position, radius,
                item => !claimed.Contains(item) && (skip == null || !skip.Contains(item)),
                pos => (pos - threat).sqrMagnitude > avoidRadius * avoidRadius,
                out at);
        }

        private static Item FindLying(Vector3 centre, float radius, Func<Item, bool> wantItem,
            Func<Vector3, bool> wantPlace, out Vector3 at)
        {
            at = Vector3.zero;
            int n = Physics.OverlapSphereNonAlloc(centre, radius, Hits, LootMask, QueryTriggerInteraction.Ignore);
            Item best = null;
            float bestSq = float.MaxValue;
            var seen = new HashSet<InteractableObject>();

            for (int i = 0; i < n; i++)
            {
                InteractableObject io = Hits[i] != null ? Hits[i].GetComponentInParent<InteractableObject>() : null;
                if (io == null || !seen.Add(io)) continue;

                Item weapon = null;
                if (io is Corpse corpse)
                {
                    if (corpse.ItemOwner?.RootItem is InventoryEquipment eq)
                    {
                        foreach (EquipmentSlot slot in GunSlots)
                        {
                            if (eq.GetSlot(slot)?.ContainedItem is Weapon w && wantItem(w)) { weapon = w; break; }
                        }
                    }
                }
                else if (io is LootItem loot && loot.Item is Weapon w && wantItem(w))
                {
                    weapon = w;
                }
                if (weapon == null) continue;

                Vector3 pos = io.transform.position;
                if (wantPlace != null && !wantPlace(pos)) continue;

                float sq = (pos - centre).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = weapon; at = pos; }
            }
            return best;
        }

        // Equip a gun from the ground or a body into an empty weapon slot, the way
        // Looting Bots does: FindSlotToPickUp, Move, then a network transaction.
        // (Looting Bots calls the mover InteractionsHandlerClass; in this 4.1 build it is
        // EFT.InventoryLogic.ItemManipulator.) done(true) only once the transaction succeeded.
        internal static void PickUp(Player bot, Item weapon, Action<bool> done)
        {
            InventoryController ic = bot.InventoryController;
            var address = ic.FindSlotToPickUp(weapon);
            if (address == null) { done(false); return; }

            var move = ItemManipulator.Move(weapon, address, ic, true);
            if (move.Failed) { done(false); return; }

            ic.TryRunNetworkTransaction(move, result => done(result.Succeed));
        }

        // After a pick-up: is the gun really in one of his weapon slots now?
        internal static bool Carries(Player bot, Item weapon)
        {
            foreach (EquipmentSlot slot in GunSlots)
                if (bot.Equipment?.GetSlot(slot)?.ContainedItem == weapon) return true;
            return false;
        }
    }
}
