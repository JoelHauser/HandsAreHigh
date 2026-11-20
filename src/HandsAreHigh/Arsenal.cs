using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.HandBook;
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
        // Big enough for a 60 m sphere over a few bodies (a dozen-plus colliders each) without
        // silently dropping guns; cleared after use so it holds no references between calls.
        private static readonly Collider[] Hits = new Collider[1024];

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

        // His hands hold nothing at all (the empty-hands controller). Meds or a grenade in hand
        // count as busy: the arms only rise once the game has put them away too.
        internal static bool HandsEmpty(Player p)
        {
            Player.AbstractHandsController hc = p?.HandsController;
            return hc == null || hc is Player.EmptyHandsController;
        }

        // For the logs: what his hands are doing right now.
        internal static string DescribeHands(Player p)
        {
            Player.AbstractHandsController hc = p?.HandsController;
            if (hc == null) return "no hands controller";
            if (hc is Player.EmptyHandsController) return "empty";
            string what = hc.Item?.ShortName?.Localized() ?? hc.GetType().Name;
            if (hc is Player.FirearmController fc)
            {
                if (fc.IsInReloadOperation()) return $"reloading {what}";
                if (fc.IsTriggerPressed) return $"firing {what}";
                return $"holding {what}";
            }
            return $"{hc.GetType().Name} ({what})";
        }

        // A gun in his hands goes on the ground (and is returned); a knife goes back in its
        // sheath; meds or a grenade are put away. Holstered and slung guns stay on him, which
        // is what makes a later draw possible.
        //
        // Order matters: hands first, then the throw. Throwing the item still in hands makes
        // the game equip his next weapon in its place (Player.SetControllerInsteadRemovedOne),
        // which would put his pistol in his hands.
        // onEmpty: the game's own callback once the hands controller is empty (arms may rise).
        // onFailed: the game refused or dropped the hands change.
        // onThrown: the throw transaction's result (false: the gun went back on his body).
        internal static Item EmptyHands(Player bot, Action onEmpty, Action onFailed, Action<bool> onThrown)
        {
            Item inHands = bot.HandsController?.Item;
            if (inHands is Weapon)
            {
                Empty(bot, () =>
                {
                    onEmpty?.Invoke();
                    HandsAreHighPlugin.Try("throw weapon",
                        () => bot.InventoryController.ThrowItem(inHands, false, result => onThrown?.Invoke(result.Succeed)));
                }, onFailed);
                return inHands;
            }
            Empty(bot, onEmpty, onFailed);
            return null;
        }

        // Player.SetEmptyHands is Proceed(empty hands) + SetInHands(false). With Fast weapon
        // drop on, the same Process is started with fastHide = true, the flag the game itself
        // uses for a quick weapon switch (Player.Proceed(Weapon): CheckForFastWeaponSwitch),
        // so the firearm plays its short hide instead of the full holster.
        private static void Empty(Player bot, Action onEmpty, Action onFailed)
        {
            // The callback also arrives when the change fails; only a success empties the hands.
            Callback<IEmptyHandsController> done = result =>
            {
                if (!result.Succeed) { onFailed?.Invoke(); return; }
                HandsAreHighPlugin.Try("hands empty", () => bot.SetInHands(false));
                onEmpty?.Invoke();
            };
            if (!Settings.FastHolster.Value)
            {
                bot.SetEmptyHands(result => { if (result.Succeed) onEmpty?.Invoke(); else onFailed?.Invoke(); });
                return;
            }
            new Player.Process<Player.EmptyHandsController, IEmptyHandsController>(
                bot,
                () => Player.EmptyHandsController.CreateController<Player.EmptyHandsController>(bot),
                null,
                fastHide: true).Proceed(null, done, true);
        }

        internal static bool IsMelee(Item item) => item?.GetItemComponent<KnifeComponent>() != null;

        // What he reaches for: the gun slung on his back first, then the sidearm. A knife only
        // when he carries no gun at all (Joel, 2026-11-18).
        private static readonly EquipmentSlot[] DrawOrder =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster
        };

        internal static EquipmentSlot? GunOnBody(Player bot)
        {
            foreach (EquipmentSlot slot in DrawOrder)
                if (bot.Equipment?.GetSlot(slot)?.ContainedItem is Weapon) return slot;
            return null;
        }

        internal static string SlotName(EquipmentSlot? slot) =>
            slot == EquipmentSlot.Holster ? "sidearm" : slot == null ? "nothing" : "slung gun";

        // 0.21.0: for the hand-back line: what he goes back to his own AI with.
        internal static string DescribeArmed(Player bot)
        {
            if (bot == null) return "?";
            EquipmentSlot? gun = GunOnBody(bot);
            if (gun != null) return $"armed: {SlotName(gun)}";
            return HasMelee(bot) ? "no gun, a knife" : "unarmed";
        }

        internal static bool HasMelee(Player bot) =>
            bot.Equipment?.GetSlot(EquipmentSlot.Scabbard)?.ContainedItem != null;

        // Through the bot's own selector, as a main-weapon change: TryChangeToSlot only lets
        // a non-main change through when _canChangeToSupportWeapons is set (usually off for a
        // holster), which is why pistol draws were refused. If the selector still refuses
        // (IsWeaponReady false, its CHANGE_WEAPON_PERIOD not over), the same Player.SetSlotItem
        // it would call, with its OnWeaponTaken so the bot's weapon manager picks the gun up.
        //
        // One equipment operation at a time (the "busy hands" glitch is a second hands change
        // on top of one in progress): nothing while his drop is unanswered or an earlier draw
        // is pending, and the direct fallback only with really empty hands and an idle
        // selector. The callback (BotWeaponSelector.OnWeaponTaken, then ours) arrives when the
        // game's new hands controller is ready to operate (Player.Process: _onReadyToOperate
        // from the idle-start of the draw), i.e. at the end of the draw animation: that is what
        // marks the weapon ready (SurrenderRegistry.OnDrawn) and lets the layer hand him on.
        //
        // Sidearm quickdraw: the game's own pistol fast draw (FirearmController.Remove.Start ->
        // QuickdrawWeaponFast/QuickdrawTime, read by SetAnimatorAndProceduralValues when the
        // new controller's first operation starts: draw speed = GetWeaponDrawSpeedMultiplier,
        // 1.5-2x by weapon skill, for that one draw only, then the flag is cleared). The game
        // only sets it when a gun in hand is swapped for the holster; from empty hands it is
        // set here, per player, for this one draw. Primaries have no fast draw in the game.
        internal static bool Draw(SurrenderState s, BotOwner owner, bool melee)
        {
            Player bot = owner?.GetPlayer;
            if (bot == null || s == null) return false;
            if (s.DrawPending || s.HandsPending || s.DropPending)
            {
                if (Settings.VerboseLogs.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: draw not asked: {(s.DrawPending ? "an earlier draw is still pending" : "his drop isn't answered yet")}");
                return false;
            }
            EquipmentSlot? slot = melee ? (HasMelee(bot) ? EquipmentSlot.Scabbard : (EquipmentSlot?)null) : GunOnBody(bot);
            if (slot == null) return false;
            BotWeaponSelector selector = owner.WeaponManager?.Selector;
            if (selector == null) return false;

            int token = ++s.DrawToken;
            bool quick = !melee && slot == EquipmentSlot.Holster && Settings.Quickdraw.Value && HandsEmpty(bot);
            float now = Time.realtimeSinceStartup;
            // The timeline starts at the decision: kept across retries of one committed draw.
            if (s.DrawCommitUntil <= 0f || s.TDecided < 0f) s.TDecided = now;
            s.DrawPending = true;     // before the call: the callback may come back at once
            s.DrawSince = Time.time;
            s.DrawSlot = slot.Value;
            s.DrawQuick = quick;
            s.TDrawAsked = now;
            s.TDrawReady = -1f;
            s.TArmsDown = -1f;
            if (quick)
            {
                bot.QuickdrawWeaponFast = true;
                bot.QuickdrawTime = GameHelper.PastTime;
            }

            selector.UpdateWeaponsList();
            if (selector.TryChangeToSlot(slot.Value, true, r => SurrenderRegistry.OnDrawn(s, token, Succeeded(r))))
                return true;

            if (selector.IsChanging || !HandsEmpty(bot))
            {
                s.DrawPending = false;
                if (quick) bot.QuickdrawWeaponFast = false;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: can't draw yet, hands busy ({DescribeHands(bot)})");
                return false;
            }
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: selector refused, drawing his {SlotName(slot)} directly");
            bot.SetSlotItem(slot.Value, result =>
            {
                HandsAreHighPlugin.Try("weapon taken", () => selector.OnWeaponTaken(result));
                SurrenderRegistry.OnDrawn(s, token, Succeeded(result));
            });
            return true;
        }

        private static bool Succeeded(Result<IHandsController> r)
        {
            try { return r.Succeed && r.Value != null; }
            catch { return false; }
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

        // ---- what he has to lose -----------------------------------------------------------

        // Kit value: handbook base price (the trader reference price, Singleton<Handbook>,
        // the same source ORBIT uses) of everything he carries; -1 when the handbook isn't
        // loaded, which the surrender model treats as neutral. Looked up through StructuredItems
        // directly: Handbook.GetBasePrice logs an error for every item without an entry.
        // Protection: best armour class worn (vest, rig, helmet, including plates) / 6.
        // Cached per bot for KitRecheck seconds, so the inventory is walked rarely.
        internal static void AssessKit(Player bot, BotHistory h)
        {
            if (Time.time - h.KitAt < Settings.KitRecheck.Value) return;
            h.KitAt = Time.time;

            InventoryEquipment eq = bot.Equipment;
            if (eq == null) return;

            int best = 0;
            foreach (EquipmentSlot slot in ArmourSlots)
            {
                Item item = eq.GetSlot(slot)?.ContainedItem;
                if (item == null) continue;
                ArmorComponent own = item.GetItemComponent<ArmorComponent>();
                if (own != null) best = Math.Max(best, own.ArmorClass);
                foreach (ArmorComponent plate in item.GetItemComponentsInChildren<ArmorComponent>())
                    best = Math.Max(best, plate.ArmorClass);
            }
            h.Protection = Mathf.Clamp01(best / 6f);

            Handbook handbook = Singleton<Handbook>.Instance;
            if (handbook?.StructuredItems == null) { h.KitValue = -1f; return; }
            // The indexer, not TryGetValue: HandbookNodes keeps every item in its node cache,
            // but its dictionary base only holds the top-level categories (HandbookNodes ctor),
            // which is why 0.2.0 valued every kit at 0.
            double total = 0;
            int priced = 0;
            foreach (Item item in eq.GetAllItems())
            {
                HandbookNode node = handbook.StructuredItems[item.TemplateId.ToString()];
                if (node?.Data == null) continue;
                total += node.Data.Price * Math.Max(1, item.StackObjectsCount);
                priced++;
            }
            h.KitValue = priced > 0 ? (float)total : -1f;
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

        // Same test as ItemsFor, without the list: does he have anything for this order?
        internal static bool HasItems(Player bot, EquipmentSlot[] slots, bool armourOnly)
        {
            foreach (EquipmentSlot slot in slots)
            {
                Item item = bot.Equipment?.GetSlot(slot)?.ContainedItem;
                if (item == null) continue;
                if (armourOnly && slot != EquipmentSlot.ArmorVest && !HasArmour(item)) continue;
                return true;
            }
            return false;
        }

        internal static EquipmentSlot[] ArmourOrderSlots => ArmourSlots;

        internal static bool StillWorn(Player bot, EquipmentSlot slot, Item item) =>
            bot.Equipment?.GetSlot(slot)?.ContainedItem == item;

        internal static void Drop(Player bot, Item item) =>
            bot.InventoryController.ThrowItem(item, false, null);

        // ---- guns in the world ----------------------------------------------------------

        // ---- loot lookups (allocation-free) --------------------------------------------
        //
        // A body is many colliders (one per ragdoll bone) on the Deadbody layer, so a sphere
        // over a few bodies returns dozens of colliders. Each collider's InteractableObject is
        // found once (GetComponentInParent walks the hierarchy) and cached for the raid, keyed
        // by the collider's instance id; a destroyed object (picked up, despawned) is looked up
        // again. Nothing here reads or changes a body's physics: only its inventory.
        private static readonly Dictionary<int, InteractableObject> OwnerOf = new Dictionary<int, InteractableObject>(512);
        private static readonly HashSet<InteractableObject> Seen = new HashSet<InteractableObject>();
        private static readonly HashSet<Item> Claimed = new HashSet<Item>();
        private const int MaxCache = 8192;

        internal static int CacheCount => OwnerOf.Count;

        internal static void ClearCache()
        {
            OwnerOf.Clear();
            Seen.Clear();
            Claimed.Clear();
        }

        private static InteractableObject OwnerOfCollider(Collider c)
        {
            int id = c.GetInstanceID();
            if (OwnerOf.TryGetValue(id, out InteractableObject io) && io != null) return io;
            io = c.GetComponentInParent<InteractableObject>();
            if (OwnerOf.Count >= MaxCache) OwnerOf.Clear();   // bounded; rebuilt as needed
            OwnerOf[id] = io;
            return io;
        }

        // Is this gun still lying loose (or on a body) near where we expect it?
        internal static bool StillThere(Item weapon, Vector3 near, float radius = 3f)
        {
            if (weapon == null) return false;
            int n = Physics.OverlapSphereNonAlloc(near, radius, Hits, LootMask, QueryTriggerInteraction.Ignore);
            Seen.Clear();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    Collider c = Hits[i];
                    if (c == null) continue;
                    InteractableObject io = OwnerOfCollider(c);
                    if (io == null || !Seen.Add(io)) continue;
                    if (io is LootItem loot) { if (loot.Item == weapon) return true; }
                    else if (io is Corpse corpse && corpse.ItemOwner?.RootItem is InventoryEquipment eq)
                    {
                        foreach (EquipmentSlot slot in GunSlots)
                            if (eq.GetSlot(slot)?.ContainedItem == weapon) return true;
                    }
                }
                return false;
            }
            finally { Seen.Clear(); Array.Clear(Hits, 0, n); }
        }

        // Nearest gun on the ground or on a body, not too close to the threat and not
        // already claimed by another surrendered bot or in the skip list.
        internal static Item FindWeapon(Player bot, Vector3 threat, float avoidRadius, float radius,
            ICollection<Item> skip, out Vector3 at)
        {
            at = Vector3.zero;
            Claimed.Clear();
            foreach (SurrenderState s in SurrenderRegistry.All)
                if (s.Bot != bot && s.TargetWeapon != null) Claimed.Add(s.TargetWeapon);

            Vector3 centre = bot.Position;
            float avoid2 = avoidRadius * avoidRadius;
            int n = Physics.OverlapSphereNonAlloc(centre, radius, Hits, LootMask, QueryTriggerInteraction.Ignore);
            Item best = null;
            float bestSq = float.MaxValue;
            Seen.Clear();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    Collider c = Hits[i];
                    if (c == null) continue;
                    InteractableObject io = OwnerOfCollider(c);
                    if (io == null || !Seen.Add(io)) continue;

                    Vector3 pos = io.transform.position;
                    float sq = (pos - centre).sqrMagnitude;
                    if (sq >= bestSq || (pos - threat).sqrMagnitude <= avoid2) continue;

                    Item weapon = null;
                    if (io is LootItem loot)
                    {
                        if (loot.Item is Weapon w && Wanted(w, skip)) weapon = w;
                    }
                    else if (io is Corpse corpse && corpse.ItemOwner?.RootItem is InventoryEquipment eq)
                    {
                        foreach (EquipmentSlot slot in GunSlots)
                            if (eq.GetSlot(slot)?.ContainedItem is Weapon w && Wanted(w, skip)) { weapon = w; break; }
                    }
                    if (weapon == null) continue;
                    bestSq = sq; best = weapon; at = pos;
                }
                return best;
            }
            finally { Seen.Clear(); Claimed.Clear(); Array.Clear(Hits, 0, n); }
        }

        private static bool Wanted(Item w, ICollection<Item> skip) =>
            !Claimed.Contains(w) && (skip == null || !skip.Contains(w));

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
