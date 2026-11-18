# Hands Are High

SPT 4.1 client mod (BepInEx + BigBrain): a bot can surrender, drop the weapon in his hands and
put his hands up, then act on his own: break when you give him an opening, retreat when you
leave, and come back armed later. Repo https://github.com/JoelHauser/HandsAreHigh.

## Status

**0.1.0, built and installed in `H:\SPT4.1.X` 2026-11-18; pushed to `main` (f875264); never
run in game; no GitHub release.** 0.1.0 implements the full spec Joel wrote with ChatGPT (bots surrender
on their own, orders menu, reactions, voice, SAIN personality, sleep handling, numbered
config) on top of the 0.0.2 prototype, which was never run either. Nothing below is
confirmed in game.

Joel's direction (2026-11-18): standing, not crouched; drop **only** the weapon in hand; may
pull a sidearm or knife depending on distance; left alone he retreats and later gets his own
gun back or finds a new one; "extremely dynamic". He runs SAIN + ORBIT 3.1.1, not Looting
Bots or Questing Bots.

Left out of 0.1.0 on purpose (the spec marks them optional): gear-disadvantage pressure,
reputation pressure, squadmates trying to free a held bot, player voice lines, unit tests.
No Fika.

## Build

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X [-Install]
```

Run it from PowerShell. H:\SPT4.1.X has BigBrain; on an install without it pass
`-BigBrainDll <path>\DrakiaXYZ-BigBrain.dll` (BigBrain 1.5.0, never packed in our zip).
`C:\HUH` is the work-laptop install. Zip = `BepInEx/plugins/HandsAreHigh.dll` only.

## Behaviour (one phase machine per bot, SurrenderLayer.cs)

Nerve 0-1 is rolled once per bot per raid (SurrenderRegistry history) and never re-rolled.
Order (none / follow / kneel) is separate from the phase: a following bot is still HandsUp.

- **Surrendering on his own** (NaturalSurrender.cs): Scav/PMC roles only (bosses, followers,
  raiders, rogues never). Every ~5 s per bot, staggered, no catch-up. Needs a visible human
  enemy (GoalEnemy) within 30 m, no reload/weapon change in progress, at least 2 reasons and
  pressure >= 0.5 (the spec said 0.65; with these weights that needs three reasons or a bot
  under 9% health, i.e. practically never). Reasons (weights): hurt below 35% health (0.35), out of ammo or no gun
  (0.30), outnumbered (0.20), squadmate died in the last 30 s (0.30), hit/under fire (0.10).
  Chance = min(0.20, 0.08 x pressure x (1 - 0.85 x nerve) x role (PMC 0.65) x SAIN
  personality (Coward 1.5 ... GigaChad/Wreckless 0.5)). Each gated check is logged with every
  number. 30 s cooldown after he's handed back.
- **HandsUp**: stops shooting/sprinting, empties his hands (gun thrown, knife sheathed),
  arms rise only once his hands are empty; retries the drop every 1.5 s, after 3 tries he
  fights instead. Stands (or crouches for "get down", walks after you for "follow me"),
  faces the captor.
  - Hit by anyone (`Player.BeingHitAction`: bullets, melee, explosions; never bleeding) ->
    fast arms down, Retreating; other held bots who see it run too.
  - Captor dead -> Retreating. Captor seen beyond 25 m -> Retreating. Captor out of his
    sight 6 s -> Retreating (no tracking through walls: only the last sighting is used).
  - Not covered (no gun in hand, reloading, inventory, looking > 30 deg away, no line of
    sight) for 1.5 s: a roll every 1 s at 0.15 x nerve. Success: knife within 4 m, else draw
    (holster first) within 30 m -> Rearmed; otherwise keeps waiting.
- **Retreating**: complete NavMesh path to a point ~35 m from your last-known position,
  preferring hidden ones; sprints. 4 s grace. Stuck 3 s without 0.5 m progress -> new route,
  3 retries, then Hiding. Arrived or 15 s -> Regrouping.
- **Regrouping**: watches your last position 4-12 s, then: draw a kept gun; own gun if still
  there, reachable and you were last seen > 20 m from it; else nearest reachable loose gun /
  gun on a body within 60 m, > 20 m from you, not claimed by another held bot; else knife if
  nerve >= 0.75; else Hiding.
- **Recovering / PickingUp**: walks; gives up if gone, stuck, unreachable or 30 s; pick-up the
  Looting Bots way; success only if the gun is then in one of his weapon slots, then draws ->
  Rearmed. 5 s pick-up time limit.
- **Hiding**: moves out of sight of your last position (cover search 20 m) if he's in view,
  crouches, looks again every 8 s.
- **Caught again** (any phase but HandsUp, after grace): a human within 12 m aiming within
  10 deg with a clear line for 0.4 s. With a gun on him, 0.5 x nerve chance he draws instead.
- **Rearmed**: arms down, reservation released, back to SAIN / vanilla.
- **Orders** (OrderMenu.cs): look at a held bot within 3 m, / opens the menu, . (and =)
  choose, / gives it. Drop your weapons / drop your kit / armour off (one item
  every 0.4 s, whole containers, then a "dropped n of m" report) / follow me / get down /
  stay there / get lost (Retreating). Re-checked when given.
- **Asleep** (ORBIT Ghost Mode or anything that deactivates the bot): claim on a gun dropped,
  decides again on waking. ORBIT itself is not patched.
- **Voice**: BotTalk.Say lines HoldFire (surrender), Roger (order), Negative (refused / partial),
  OnFight (break), OnBeingHurt (shot); 12 s cooldown per bot.

## In-game test plan (0.1.0)

Before raiding, F12 > Hands Are High > 14. Debug: turn on **Debug keys** and **Overlay**.
[ surrenders the bot nearest your crosshair, ] gives every held bot an opening, Backspace
acts as if you left (Joel has no numpad; these keys are free in EFT and all his mods). Log lines to grep in BepInEx\LogOutput.log: `Surrender:`,
`->` (phase changes, with reasons), `pressure` (surrender checks), `order`, `asleep`.

1. [ on a Scav holding a rifle with a pistol on him: rifle drops, pistol stays, hands
   rise only after the rifle is gone, he stands and faces you, never shoots (SAIN + ORBIT).
2. ] at 3 m (knife), 15 m (pistol), 45 m (keeps his hands up).
3. Look away, reload or open your inventory near him: after 1.5 s he may break (overlay shows
   nerve; set 14. Debug > Override nerve 1.0 for a sure thing).
4. Shoot him in the leg: he bolts. With two held bots in view of each other, shoot one: the
   other runs too.
5. Walk 30 m off or break line of sight 6 s: he retreats, regroups, then draws or walks back
   to his rifle; leave another gun near a body and pick up his rifle yourself.
6. Nothing to find: he hides out of sight; walk up and aim straight at him: hands go up again.
7. Orders: / up close, try each order; follow him through a door; "drop your kit".
8. Natural surrender: wound a lone Scav badly and empty his gun, or kill his squad; watch the
   `pressure` lines. Turn on 14. Debug > Verbose logs to see every check.
9. Pose: jitter, clipping, palms; tune in F12 section 12.
10. Leave a retreating bot 300 m behind (ORBIT Ghost Mode): log should show `asleep` /
    `woke up`.

## How it works (engine side, verified 2026-11-18)

The 0.16.9 client is de-obfuscated and the build treats warnings as errors, so every name
below compiled against H:\SPT4.1.X. Semantics read with ilspycmd.

- **Layer**: BigBrain `CustomLayer` on brains Assault, CursAssault, PMC, PmcBear, PmcUsec,
  priority 95. SAIN: combat 20-24, avoid threat 80, debug 99. ORBIT 3.1.1: `OrbitBrainLayer`
  at 19 on PMC/PmcUsec/PmcBear/Assault (not CursAssault); its agent only drives a bot while
  the current layer is `OrbitBrainLayer` (`_agent.IsActive`, OnLayerChangedTo); hard-stuck
  teleport resets when the bot isn't moving; Ghost Mode sleeps bots > 260 m from any human.
- **Drop**: `Player.SetEmptyHands`, then `PlayerInventoryController.ThrowItem(item, false, null)`.
- **Draw**: `BotWeaponSelector.TryChangeToSlot(slot, slot == FirstPrimaryWeapon)`, `ChangeToMelee()`.
- **Pressure**: `BotOwner.Memory.GoalEnemy` (EnemyInfo: IsVisible, Person, PersonalLastSeenTime =
  Time.time), `Memory.IsUnderFire`, `Memory.LastTimeHit` (Time.time), `EnemiesController.EnemyInfos`,
  `BotsGroup.MembersCount/Member(i)` (RemoveAlly shrinks it on death), `WeaponManager.HaveBullets`,
  `WeaponManager.Reload.CanReload(false)`, `HealthController.GetBodyPartHealth(Common)` Current/Maximum.
- **SAIN personality**: `SAIN.Interop.SAINExternal.GetPersonality(BotOwner)` (string of
  EPersonality: Wreckless, SnappingTurtle, GigaChad, Chad, Rat, Timmy, Coward, Normal,
  Custom1-5), by reflection.
- **Voice**: `BotOwner.BotTalk.Say(EPhraseTrigger, sayImmediately)`.
- **Finding guns**: `Physics.OverlapSphereNonAlloc` on Interactive/Loot/Deadbody,
  `LootItem.Item is Weapon` or `Corpse.ItemOwner.RootItem` slots. Pick-up: `FindSlotToPickUp`,
  `ItemManipulator.Move`, `TryRunNetworkTransaction`.
- **Arm pose** (`ArmPose.cs`): postfix on `Player.VisualPass` (after FBBIK, AdjustElbows,
  IkApply, HandsController.ManualLateUpdate); bones aimed in the body's yaw frame, slerped by
  `Weight * PoseStrength`; nothing persists between frames, so teardown only unpatches.
- Sight: `Physics.Linecast` with `LayersMaskController.HighPolyWithTerrainMask` (Terrain +
  HighPolyCollider only; hitboxes are on HitCollider/Player, so bodies never block it).
- Hits: `Player.BeingHitAction` (raised in ApplyDamageInfo, which ApplyShot calls). One handler
  per held bot, removed whenever a state leaves the registry, on a new raid and on teardown.
- Nerve override (14. Debug) applies live: states keep the rolled value, `Nerve` reads through it. Paths:
  `NavMesh.CalculatePath`, complete paths only.
- **Hot reload**: BigBrain can't unregister a custom layer; teardown empties the registry (old
  layer goes inert), clears the SAIN lookup, notes and error throttle, unpatches, destroys Runtime.

## Known unknowns

- Whether SAIN's own per-frame components (not layers) do anything to an unarmed held bot.
- Whether `BotTalk.Say` plays while our layer owns the bot, and which lines Scav voices have.
- / . = [ ] Backspace were free in Joel's EFT and mod binds on 2026-11-18 (all rebindable in F12 section 10).
- Whether ORBIT's sleep shows as `BotState != Active` or an inactive GameObject (both handled).
- Forearm twist bones untouched; wrists may look twisted (`Forearm twist`).
- Fika: decisions must run on the host; the arm pose needs a synced flag.
