using System;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // Everything to do with the bot's weapons: dropping the one in his hands, drawing
    // what he still carries, and finding and picking up a gun in the world.
    internal static class Arsenal
    {
        // Same layers Looting Bots scans for loose items and bodies.
        private static readonly int LootMask = LayerMask.GetMask("Interactive", "Loot", "Deadbody");
        private static readonly Collider[] Hits = new Collider[512];

        private static readonly EquipmentSlot[] GunSlots =
        {
            // Sidearm first: it's the fast draw.
            EquipmentSlot.Holster, EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon
        };

        internal static float Health(Player p) =>
            p?.HealthController == null ? 0f : p.HealthController.GetBodyPartHealth(EBodyPart.Common).Current;

        internal static bool HandsEmpty(Player p) => p.HandsController is Player.EmptyHandsController;

        // Only what's in his hands goes on the ground; anything holstered or slung stays
        // on him, which is what makes a later draw possible.
        internal static Item DropWeaponInHands(Player bot)
        {
            Item inHands = bot.HandsController?.Item;
            if (inHands == null || !(inHands is Weapon || IsMelee(inHands))) return null;

            bot.SetEmptyHands(_ => HandsAreHighPlugin.Try("throw weapon",
                () => bot.InventoryController.ThrowItem(inHands, false, null)));
            return inHands;
        }

        private static bool IsMelee(Item item) => item?.GetItemComponent<KnifeComponent>() != null;

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

        // Is this gun still lying loose (or on a body) near where we expect it?
        internal static bool StillThere(Item weapon, Vector3 near, float radius = 3f) =>
            FindLying(near, radius, item => item == weapon, null, out _) != null;

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
        // EFT.InventoryLogic.ItemManipulator.)
        internal static void PickUp(Player bot, Item weapon, Action<bool> done)
        {
            InventoryController ic = bot.InventoryController;
            var address = ic.FindSlotToPickUp(weapon);
            if (address == null) { done(false); return; }

            var move = ItemManipulator.Move(weapon, address, ic, true);
            if (move.Failed) { done(false); return; }

            ic.TryRunNetworkTransaction(move, result => done(result.Succeed));
        }
    }
}
