# Hands Are High

SPT 4.1 client mod (BepInEx + BigBrain): a bot can surrender, drop the weapon in his hands and
put his hands up, then act on his own: break when you give him an opening, retreat when you
leave, and come back armed later. Repo https://github.com/JoelHauser/HandsAreHigh.

## Status

**0.2.0, built, installed in `H:\SPT4.1.X` and pushed to `main` 2026-11-18; not yet run in game.**
0.1.0 (pushed, 2efc082) was run once by Joel 2026-11-18: surrender, drop and pose worked;
the log showed the problems 0.2.0 fixes (below). No GitHub release.

Joel's direction (2026-11-18): standing, not crouched; drop **only** the weapon in hand; on
an opening a bot draws the gun slung on his back first, then his sidearm, and only uses a
knife when he carries no gun; he often scrambles for his own dropped gun when it's close;
left alone he retreats and later gets his own gun back or finds a new one; "extremely
dynamic". He runs SAIN + ORBIT 3.1.1, not Looting Bots or Questing Bots.

Not built (optional in the spec): reputation pressure, squadmates freeing a held bot, player
voice lines, unit tests. No Fika.

### What the first in-game log showed (0.1.0) and what 0.2.0 changed

- Debug opening / leave hit every held bot (they looped over the registry); the surrender
  key fell back to the nearest bot. Now one bot under the crosshair (Targeting.cs).
- Almost every Retreating / Recovering failed at once with "no path": `BotOwner.GoToPoint`
  pathfinds over the bot's cover-point graph (`BotPathFinderCorePoints.GoToPosition`), which
  failed for most targets, and with its default `mustHaveWay = true` it **teleports the bot to
  a cover point** on failure. Now our own NavMesh path through `BotMover.GoToByWay`.
- Draws refused ("the game refused the weapon change"): `TryChangeToSlot(slot, false)` for a
  holster only passes when `_canChangeToSupportWeapons`. Now `changeToMain: true`, falling
  back to `Player.SetSlotItem` + `selector.OnWeaponTaken` (what TryChangeToSlot itself calls).
- Drop took > 1.5 s (retries logged): full holster animation, polled every 0.25 s. Now the
  empty-hands `Player.Process` with `fastHide: true` and the arms react to its callback.
- "fire 0" always: suppression read only `Memory.IsUnderFire`. Now Suppression.cs.

## Build

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X [-Install]
```

Run it from PowerShell. H:\SPT4.1.X has BigBrain; on an install without it pass
`-BigBrainDll <path>\DrakiaXYZ-BigBrain.dll` (BigBrain 1.5.0, never packed in our zip).
`C:\HUH` is the work-laptop install. Zip = `BepInEx/plugins/HandsAreHigh.dll` only.
pack.ps1 refuses config names containing = \ " ' [ ] (BepInEx throws in Awake and the whole
plugin dies silently; 0.0.2-0.1.0 shipped that bug). Existing section/key names must not be
renamed casually: Joel's cfg already holds values for them.

## Behaviour (one phase machine per bot, SurrenderLayer.cs)

Nerve 0-1 is rolled once per bot per raid (SurrenderRegistry history) and never re-rolled.
Order (none / follow / kneel) is separate from the phase: a following bot is still HandsUp.

- **Surrendering on his own** (NaturalSurrender.cs, header comment has the model). Scav/PMC
  only. Every ~5 s per bot, no catch-up. A human enemy within 30 m seen now or in the last 3 s.
  Pressure (weights): injury (0.35), out of ammo (0.30), suppression (0.35), outnumbered
  (0.20), squad lost or squad too far (0.30); armour dulls suppression + outnumbered (class 6:
  x0.7). Needs >= 2 reasons (each >= 0.15) and pressure >= 0.5. Chance = min(0.20, 0.08 x
  pressure x (1 - 0.85 x nerve) x role (PMC 0.65) x SAIN personality (0.5-1.5) x kit value
  (up to 1.25 at 250k handbook roubles) x experience (down to 0.65 at level 40+/impossible)).
  Every gated check logs every input.
- **HandsUp**: stops shooting/sprinting, fast-empties his hands (gun thrown, knife sheathed);
  arms rise on the game's hands-empty callback; asks again after 1 s, 3 tries then he fights.
  Logs a timeline once the arms are up: `Timeline from surrender: drop asked, hands empty
  (game holster), gun on the ground, arms up`.
  - Hit by anyone (`Player.BeingHitAction`; never bleeding) -> Retreating; witnesses run too.
  - Captor dead, seen beyond 25 m, or out of sight 6 s -> Retreating.
  - Not covered for 1.5 s: a roll every 1 s at 0.15 x (0.3..1 by nerve) x (1 + 2 x how good a
    grab for his own gun would be). Success: **TakeOpening** weighs and logs every option:
    draw (slung gun first, then sidearm, within 30 m), knife (only with no gun, within 4 m),
    scramble for his own dropped gun (within 10 m, reachable, unclaimed, worse the closer you
    are to it; halved if he has a gun to draw), run (not within 6 m); scaled by nerve and
    SAIN personality (fight vs flee), picked at random by weight.
- **Scramble**: arms down at once, sprints (Recovering with Scrambling), real pick-up
  transaction, then draws. Gone / stuck / 8 s / pick-up failed -> no second scramble for
  15 s; draws what he has or runs. Caught mid-scramble (0.75 s grace) -> hands up again.
- **Retreating**: NavMesh path to a point ~35 m from your last-known position, preferring
  hidden ones; sprints. 4 s grace. Stuck 3 s -> new route, 3 retries, then Hiding. Arrived or
  15 s -> Regrouping.
- **Regrouping**: watches your last position 4-12 s, then: draw a kept gun (slung first); own
  gun if still there, reachable and you were last seen > 20 m from it; else nearest reachable
  loose gun / gun on a body within 60 m, not claimed; else knife if nerve >= 0.75; else Hiding.
- **Recovering / PickingUp**: walks; gives up if gone, stuck, unreachable or 30 s; success only
  if the gun is then in one of his weapon slots, then draws. 5 s pick-up limit.
- **Hiding**: out of sight of your last position, crouches, looks again every 8 s (only the
  first hide after being caught is logged).
- **Caught again** (after grace): a human within 12 m aiming within 10 deg for 0.4 s. With a
  gun on him, 0.5 x nerve chance he draws instead.
- **Orders** (OrderMenu.cs): look at a held bot within 3 m, `/` opens, `.`/`=` choose, `/` gives.
- **Debug keys** (Runtime.cs): `[` surrender, `]` opening (6 s not covered, his own rolls),
  Backspace leave; each only the bot under the crosshair, logged as `Debug <cmd>: target ...`
  and shown on screen. No target: nothing happens.
- **Asleep** (ORBIT Ghost Mode): claim on a gun dropped, decides again on waking.

## In-game test plan (0.2.0)

F12 > Hands Are High > 14. Debug: **Debug keys** and **Overlay** on. Grep LogOutput.log for
`Debug `, `Timeline`, `takes an opening`, `scramble`, `pressure`, `->`.

1. Two or three Scavs close together, crosshair on one, `[`: only that one surrenders
   (`Debug surrender: target <name> (<id>)`). Crosshair on a wall, `[`: "no bot under your
   crosshair", nobody changes.
2. `]` on one of two held bots: only he gets "(debug)" opening lines.
3. `]` with his rifle on the floor 2-5 m from him and you 8+ m from it, a few times on fresh
   bots: he should often scramble for it, sprint, pick it up and fight. Pick it up yourself
   while he runs: "scramble failed: it's gone", he draws or runs, no duplicate gun.
4. Scav with a slung rifle (or PMC with a pistol), `]` at 3 m: he draws the gun, not the knife.
5. `[` on a bot that's firing / sprinting / reloading: read the Timeline line for each.
6. Pin a bot down with sustained fire near him vs a single shot: compare the `fire` value in
   `pressure` lines (Verbose logs on shows every check).
7. Retreat / regroup / recover now actually move (no instant "no path"); no teleports.
8. Same checks with SAIN + ORBIT (Joel's normal install).

## How it works (engine side, verified 2026-11-18)

The 0.16.9 client is de-obfuscated and the build treats warnings as errors, so every name
below compiled against H:\SPT4.1.X. Semantics read with ilspycmd (full decompile of
Assembly-CSharp in the session scratchpad; regenerate with `ilspycmd -p`).

- **Layer**: BigBrain `CustomLayer` on brains Assault, CursAssault, PMC, PmcBear, PmcUsec,
  priority 95. SAIN: combat 20-24, avoid threat 80, debug 99. ORBIT 3.1.1: `OrbitBrainLayer`
  at 19; its agent only drives a bot while its layer is current; Ghost Mode sleeps bots > 260 m.
- **Drop**: `new Player.Process<EmptyHandsController, IEmptyHandsController>(bot, factory, null,
  fastHide: true).Proceed(null, cb, true)` + `SetInHands(false)` (= SetEmptyHands with the fast
  hide `Player.Proceed(Weapon)` uses for quick switches); then `ThrowItem` from his slot.
  Throwing the item still in hands is wrong: `SetControllerInsteadRemovedOne` equips his next
  weapon. Remaining latency is the game's hide animation and the throw transaction.
- **Draw**: `TryChangeToSlot(slot, changeToMain: true)`; fallback `Player.SetSlotItem` +
  `BotWeaponSelector.OnWeaponTaken`. Melee: `ChangeToMelee()`.
- **Movement**: `NavMesh.CalculatePath` (both ends sampled) -> `BotOwner.Mover.GoToByWay(corners,
  0.5)`, re-issued every 1 s; `MovementResume`, `Sprint`. Never `BotOwner.GoToPoint`.
- **Suppression**: postfix on `BotMemory.SetUnderFire` (called by `GetHitInner` on hits and by
  `BotHearingSensor` for gunfire within BULLET_FEEL_DIST aimed at him); one event per 0.05 s,
  half-life 4 s, full at 6. SAIN: `SAINExternal.GetBotComponent` (private) ->
  `BotComponent.Suppression.CurrentState` (ESuppressionState None..Extreme), by reflection;
  the higher of the two counts.
- **Kit**: handbook price via `Singleton<Handbook>.Instance.StructuredItems.TryGetValue(templateId)`
  -> `Data.Price` x stack (not `GetBasePrice`: it logs an error per missing item;
  `ItemTemplate.CreditsPrice` is empty in SPT's items.json). Protection: best
  `ArmorComponent.ArmorClass` in vest/rig/helmet incl. plates. Cached per bot, 30 s.
- **Experience**: `Profile.Info.Level`, `BotOwner.Settings._difficulty` (BotDifficulty).
- **Pressure inputs**: `Memory.GoalEnemy` (IsVisible, PersonalLastSeenTime = Time.time),
  `EnemiesController.EnemyInfos`, `BotsGroup.MembersCount/Member(i)`, `WeaponManager.HaveBullets`,
  `Reload.CanReload(false)`, `HealthController.GetBodyPartHealth(Common)`.
- **SAIN personality**: `SAINExternal.GetPersonality(BotOwner)` (EPersonality names).
- **Targeting**: `Physics.RaycastNonAlloc` from the eye along `LookDirection`, mask
  HighPolyWithTerrain | HitCollider | Player, sorted, own colliders skipped; world first = no
  target; 0.25 m sphere cast fallback only with a clear line.
- **Voice**: `BotOwner.BotTalk.Say(EPhraseTrigger, sayImmediately)`.
- **Finding guns / pick-up**: OverlapSphere on Interactive/Loot/Deadbody; `FindSlotToPickUp`,
  `ItemManipulator.Move`, `TryRunNetworkTransaction`.
- **Arm pose**: postfix on `Player.VisualPass`.
- Sight: `Physics.Linecast` with `HighPolyWithTerrainMask` (hitboxes are not in it).
- Hits: `Player.BeingHitAction`, one handler per held bot, removed on leave / new raid / teardown.
- **Hot reload**: BigBrain can't unregister a custom layer; teardown empties the registry (old
  layer goes inert), clears SAIN lookups, notes, error throttle, unpatches (incl. SetUnderFire).

## Known unknowns

- Whether `fastHide` visibly shortens the drop for every weapon (Timeline line will tell).
- Whether `BotMover.GoToByWay` keeps moving under SAIN while our layer owns the bot.
- Whether `BotTalk.Say` plays while our layer owns the bot, and which lines Scav voices have.
- Whether ORBIT's sleep shows as `BotState != Active` or an inactive GameObject (both handled).
- Forearm twist bones untouched; wrists may look twisted (`Forearm twist`).
- Fika: decisions must run on the host; the arm pose needs a synced flag.
