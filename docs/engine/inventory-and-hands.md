# Inventory, hands controllers and items (EFT 0.16.9)

Provenance: release notes 0.2.0-0.24.0 (`docs/history/`). Labels: see [README.md](README.md).
Code that does this: `Arsenal.cs` (drop, draw, pick-up, gear classes), `CaptorLoot.cs` (bot pick-up),
`GearNeeds.cs`, `WeaponState.cs`, `Scenarios/ScenarioOps.cs`.

## The one rule

One hands request at a time per bot. A second hands change stacked on an unanswered one is what
leaves a bot with "busy hands" (stuck, can't shoot or drop). Every request carries a token; stale
callbacks are ignored; a request is abandoned only after 2x the retry time; 3 requests max, then he
fights. No draw while a drop is pending, no drop/draw while a pick-up transaction runs, never cut a
transaction in progress. [code; log: no busy-hands lines in the 2026-11-21 0.24.0 raid]

## Dropping the weapon in hand

- `new Player.Process<EmptyHandsController, IEmptyHandsController>(bot, factory, null, fastHide: true)
  .Proceed(null, cb, true)` + `SetInHands(false)` (the fast hide the game uses for quick switches),
  then `ThrowItem` from his slot. Throwing the item still in hands is wrong:
  `SetControllerInsteadRemovedOne` equips his next weapon. [code + log: "hands empty +566 ms (game
  hide animation), gun on the ground +585 ms"]
- Mid-reload: `BaseReloadOperation.HideWeapon` only stores the callback; the reload is finished
  instantly with `HandsController.FastForwardCurrentState()` (as `BotWeaponManager.PreActivate`
  does), whose `SwitchToIdlingState` runs the stored hide. A magazine swap can't be aborted midway.
  [code + 0.3.0 log]

## Drawing

- `TryChangeToSlot(slot, changeToMain: true)` (with false a holster only passes when
  `_canChangeToSupportWeapons`); fallback `Player.SetSlotItem` + `BotWeaponSelector.OnWeaponTaken`.
  Melee: `ChangeToMelee()`. [code + 0.1.0 log]
- The game's sidearm quickdraw (`QuickdrawWeaponFast/QuickdrawTime`) applies only when a gun in hand
  is swapped for the holster; primaries have no fast draw. [code]
- Hand him to the AI only after the draw callback says the weapon is ready (limit 3 s): the first
  shot is the AI's own. [code]

## What counts as armed

- `EFT.InventoryLogic.Knife : Item` (KnifeComponent) is NOT a `Weapon`; the scabbard item is a Knife.
  Grenades are throwables, never a weapon for fight/flee decisions. `Arsenal.GearKind`
  (Firearm/Melee/Throwable/Other) from the game's classes. [code, 0.24.0]
- Usable gun = not a flare gun, durability > 0, rounds in it or ammo of its calibre on him
  (`Arsenal.Usable`, cached 4 s). `GunOnBody` (anything that must be disarmed) is a different question.
- Hands controller types for "a weapon in his hands": `Player.FirearmController`,
  `BaseKnifeController`, `BaseGrenadeHandsController`. [code]

## Bot pick-up (mirrors vanilla BotItemTaker)

- Claim check `ItemTaker._itemToTake` of squadmates; room = `InventoryController.FindSlotToPickUp`
  (only an EMPTY equipment slot that takes it; else `FindGridToPickUp`, never for a Weapon). Then
  `CurrentManagedState.Pickup(true, cb)`; in cb `ItemManipulator.QuickFindAppropriatePlace(simulate)`
  + `StrictCheckMagazine` + `InventoryController.RunNetworkTransaction`, then leave the state.
  QuickFindAppropriatePlace(PickUp) includes TryEquip (PickUp = 0x33, TryEquip = 0x20). [code]
- `IdlePlayerState.Pickup` IGNORES its flag and enters PickUpState; only `PickUpState.Pickup` exits.
  Calling Pickup(false) on a bot not in PickUpState starts a second, action-less pick-up (up to 5 s,
  rotation clamped). Every exit goes through `CaptorLoot.LeavePickupState` (only `is PickUpState`).
  PickUpState fires cb after 0.6 s and exits by itself after 5 s. [code, 0.19.0]
- PickUpState lowers the body only with the look > 60 deg down (pitch positive = down); look from
  the EYES, stand 0.5-0.85 m beside the item (`CaptorLoot.StandPoint`). [code + sim tools/standsim]
- The item must still be at its address right before the move; the planned move is refused if it
  left it (`IItemOperationResult.From`, as the player's pick-up checks). [code]
- Slot fit for demanded gear: `InventoryEquipment.FindSlotToPickUp -> Slot.FindLocationForItem /
  CheckConditions`. Bots never pack guns into grids. [code]
- Removing items in scenarios: `ItemManipulator.Remove` with `simulate: true` before the transaction
  (without it the item was removed and then the transaction threw "Trying to get parent"); loose
  items via `GameWorld.DestroyLoot`. [code + 0.14.0 log]
- Items are never deleted or duplicated by gameplay code; scenario cleanup removes only tracked loose
  scenario items and reports anything in the player's inventory.

## Prices and protection

- Handbook price: `Singleton<Handbook>.Instance.StructuredItems` via the INDEXER (the dictionary base
  only holds top-level categories) -> `Data.Price` x stack. Not `GetBasePrice` (logs an error per
  missing item); `ItemTemplate.CreditsPrice` is empty in SPT's items.json. [code + 0.2.0 log: kit 0k]
- Protection: best `ArmorComponent.ArmorClass` in vest/rig/helmet incl. plates; durability via
  `ArmorComponent.Repairable`. [compiled]

## Finding loose guns

- `Physics.OverlapSphere` on Interactive/Loot/Deadbody layers; collider -> InteractableObject cached
  per raid; buffer 1024 (bodies have a dozen-plus colliders; 256 could drop guns silently). [code]
