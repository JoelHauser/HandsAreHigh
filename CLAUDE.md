# Hands Are High

SPT 4.1 client mod (BepInEx + BigBrain): a bot can surrender, drop the weapon in his hands and
put his hands up, then act on his own: break when you give him an opening, retreat when you
leave, and come back armed later. Repo https://github.com/JoelHauser/HandsAreHigh.

## Status

**0.0.2 prototype, built 2026-11-18 and pushed to `main` (first commit); never run in game,
no GitHub release.** Surrender is still started by a debug key; no surrender rules (health,
odds) or interaction menu yet; no Fika.

Next: run the prototype test below, then design when a bot surrenders on his own (health,
outnumbered, nerve) and what the player can do with a held bot.

Joel's direction (2026-11-18): standing, not crouched; drop **only** the weapon in hand; may
pull a sidearm or knife depending on distance; left alone he retreats and later gets his own
gun back or finds a new one; "extremely dynamic".

## Build

```
scripts\pack.ps1 -SPTPath C:\HUH -BigBrainDll <path>\DrakiaXYZ-BigBrain.dll [-Install]
```

`C:\HUH` (work laptop) is a patched SPT 4.1 install without BigBrain; BigBrain 1.5.0 comes
from https://github.com/DrakiaXYZ/SPT-BigBrain/releases (never packed in our zip). Without
`-BigBrainDll` the csproj looks in `<SPTPath>\BepInEx\plugins`.

## Behaviour (SurrenderLayer.cs, one phase machine per bot)

- **HandsUp**: stands (Stance 1), hands up, faces the captor. Each bot rolls a Nerve 0-1.
  - Shot while hands up -> arms down fast, **Retreating**.
  - Captor beyond LeaveDistance (25 m) or out of his sight for LeaveSeconds (6 s), or dead ->
    **Retreating**.
  - Captor not *covering* him (no gun in hands, reloading, inventory open, looking more than
    WatchAngle 30 deg away, or no clear line) for OpeningDelay (1.5 s): each second a
    BreakChance (0.35) x Nerve roll. On success: within KnifeRange (4 m) and has a scabbard
    item -> knife; else within SidearmRange (30 m) and a gun still on him (holster first,
    then slung primaries) -> draw; either way **Rearmed**. Otherwise he keeps waiting.
- **Retreating**: NavMesh point ~RetreatDistance (35 m) away from where he last saw you,
  preferring points you can't see; sprints. 4 s grace before he can be caught again.
- **Regrouping**: crouched-ish, watching your last position for 4-12 s, then decides:
  gun on body -> draw; own gun still lying where he dropped it and he last saw you more than
  AvoidRadius (20 m) from it -> **Recovering** it; else nearest loose gun / gun on a body in
  ScavengeRadius (60 m), not near you, not claimed by another surrendered bot -> **Recovering**;
  else knife if Nerve > 0.75; else **Hiding**.
- **Recovering / PickingUp**: walks there; gives up (Skip list) if it's gone or unreachable;
  picks up the Looting Bots way (FindSlotToPickUp, ItemManipulator.Move, TryRunNetworkTransaction),
  then UpdateWeaponsList + TryChangeToSlot -> **Rearmed**.
- **Hiding**: crouched in cover, looks again every 8 s.
- **Caught again** (Retreating/Regrouping/Recovering/Hiding): a human player within 12 m
  covering him -> hands up again; if he still carries a gun, a Nerve roll draws instead.
- **Rearmed**: arms down, then the bot leaves the registry and SAIN / vanilla AI takes over
  with whatever he drew.

## Prototype test (in raid)

F12 > Hands Are High. Keypad 7 surrenders the bot nearest your crosshair; Keypad 8 gives every
held bot an opening now (knife/draw test; tries at the current distance); Keypad 9 makes every
held bot act as if you left. Log lines: `Surrender:`, `<name>: <Phase>`, `draws at`,
`goes for his knife`, `going back for his own gun`, `going for <gun>`, `rearmed with`.

1. Scav with a rifle in hand and a pistol: rifle drops, pistol stays, stands, faces you,
   arms rise. Never shoots or moves (vanilla AI, then SAIN).
2. Keypad 8 at 3 m (knife), 15 m (pistol), 45 m (keeps hands up).
3. Look away / heal / reload near a high-nerve bot: does he break by himself?
4. Shoot him in the leg: he bolts.
5. Walk 30 m away or break line of sight: after 6 s he retreats, regroups, then
   draws his pistol, or (no pistol) walks back to his rifle when you're away from it.
6. Pick up his rifle yourself, leave a dead Scav's gun nearby: he goes for that one.
7. Nothing to find: he hides; walk up and aim: hands go up again.
8. Pose: jitter, clipping, palms; tune in F12 sections 2-3.
9. Repeat with ORBIT, and Questing Bots + Looting Bots.

## How it works (engine side)

- **Layer**: BigBrain `CustomLayer` on brains Assault, CursAssault, PMC, PmcBear, PmcUsec
  (names from SAIN's Brain.cs), priority 95. SAIN: combat 20-24, avoid threat 80, debug 99;
  SAIN starts shots only from its layer actions (ManualShootClass.TryShoot), blind fire is
  gated on SAINLayersActive. Source read from Solarint/SAIN (last commit Aug 2025, older than
  Forge 4.5.1).
- **Drop**: `Player.SetEmptyHands`, then `InventoryController.ThrowItem` on the item that was
  in hands (Weapon or KnifeComponent). Holster/slung guns stay on him.
- **Draw**: `BotWeaponSelector.TryChangeToSlot(slot, slot == FirstPrimaryWeapon)`,
  `ChangeToMelee()`. BotWeaponSelector.ManualUpdate only re-equips from support weapons.
- **Finding guns**: `Physics.OverlapSphereNonAlloc` on layers Interactive/Loot/Deadbody
  (Looting Bots' mask), `GetComponentInParent<InteractableObject>()`: `LootItem.Item is Weapon`
  or `Corpse.ItemOwner.RootItem` equipment slots.
- **Arm pose** (`ArmPose.cs`): postfix on `Player.VisualPass` (runs from Player.LateUpdate;
  FBBIKUpdate, IkProcess, AdjustElbows, IkApply, HandsController.ManualLateUpdate), so it's
  the last write before render. `PlayerBones.Upperarms[]/Forearms[]/LeftPalm/RightPalm`, side
  by position relative to the Ribcage. Bones aimed in the body's yaw frame with
  `FromToRotation(child - bone, desired)`, slerped from the animated pose by
  `Weight * PoseStrength`; Perlin tremble.
- Sight checks: `Physics.Linecast` with `LayersMaskController.HighPolyWithTerrainMask`.

## Known unknowns

- Whether SAIN's own per-frame components (not layers) do anything to an unarmed held bot.
- Linecast mask may count player colliders as blockers (would make "out of sight" trigger
  early); check in test 5.
- `GoToPoint` with SAIN installed: SAIN also has a mover; ours runs while our layer is
  active, which should be fine but is untested.
- Forearm twist bones untouched; wrists may look twisted (`Forearm twist`).
- Fika: layer and decisions must run on the host; the arm pose needs a synced flag.
