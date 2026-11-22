# Release 0.31.0

## 0.31.0 (2026-11-22): release-quality pass (opening frame cost, log volume, F12 menu)

Driven by the 0.30.2 raid log (2026-11-22 02:00, Profile on, 6 natural surrenders, 0 Mercy warnings).

### Measured in that log (0.30.2, before these changes)

- Mercy cost per 10 s window (337 windows in raid): median 0.46 ms/s (0.05% of frame time), p90
  0.82 ms/s, worst 6.4 ms/s (0.64%, one prisoner posing + layer work).
- Single-frame spikes over 1 ms: 15 in the raid. The repeatable ones were the openings: every
  "takes an opening" frame cost 4.2-7.7 ms in Layer (3 of 3). One-offs: raid-start prewarm 17.5 ms
  (in the loading screen), the raid's first surrender 7.7 ms (Start; later surrenders ~0.5 ms, so
  first-use cost), first bot custody 6.4 ms (Restraint).
- Log volume: 6,848 Mercy lines; ~5,000 were Verbose "no surrender check" repeats (same bot, same
  reason, every 3 s check).
- Post-raid menu: heap +420 MB per 10 s with no GC (to 13 GB). Not Mercy: outside a raid Runtime only
  runs Perf.Frame, InputGuard.Tick (returns at once) and EnsureRaid (returns at once), and Mercy cost
  reads 0.000 ms/s there. Matches the earlier MemoryWatch finding (EFT holds more in the settled menu).
- Hand-offs: 3 timelines, 1.0-1.3 s from the escape start to our layer off (that span is the
  scramble walk and the pick-up animation, not a freeze); no CIRCLED BACK, no keep-away stuck lines.
- One captor walk-up stuck 2.5 s ("mover Default, 0.0 m/s", the pair couldn't see each other); the
  2 s retry walked him up and the encounter went on. Cause unknown (not a door wait: DoorBusy would
  have returned before WalkUp). Watch for a repeat.

### Changes

- Opening decision (SurrenderLogic.TakeOpening): "hide" is now an availability check (first hidden,
  reachable spot, `RetreatPoint(..., anyOnly: true)`) instead of the full cover search. When hide is
  chosen, the full search runs from Hiding on a frame with heavy budget (the path every other hide
  already uses), with the first-found spot as a fallback (`SurrenderState.HideFallback`), so
  the best spot is still chosen and a search with worse random candidates still sends him to cover.
  Expected: the opening frame loses up to PathCandidates (12) path solves. Not yet measured.
- Verbose "no surrender check" lines (NaturalSurrender.LogBlock): the same gate for the same bot is
  logged once a minute instead of every check. Scenario bots still log every check (timeline).
  BotHistory.LastBlockGate/LastBlockLogAt; reset with History on a raid change. Expected ~5,000 fewer
  lines per raid like this one (Joel has Verbose on).
- F12: 103 -> 50 settings visible by default (key names unchanged, so no migration; 53 got
  IsAdvanced). Visible now: 1 General 7, 2 Surrender 4 (Scav/PMC chance, cooldown, bolt when shot),
  3 Captors 10 (take prisoner, spare, ignore + chance, lethal intent, fire at a runner, allies, take
  gear, demand gear, squadmate covers), 4 Escape 5 (break chance, own gun, another gun, knife, fire
  breaks a hold-up), 5 Commands 12 (keys, number keys, interact, size, hints, Stop demand + key, voice
  lines, captors speak, speak your orders), 6 Animations 6 (stance, hands behind head, kneel variants,
  wiggle, middle finger, head movement), 7 Debugging 6 (debug keys, overlay, scenario tools + key,
  verbose, profile). Advanced now: boss tuning, chance against bots, reasons needed, hit grace, fast
  drop, reload cut, ignore influence, truce time, prisoner cap, hold times, endings, stay-behind,
  per-order chances and toggles, squad hold fire, other bots spare, notices being left, ignored-fetch
  range, fight back when fleeing, quick hand-back, runs when another prisoner is shot, mouse wheel,
  talk while held, answer out loud, defiant remarks, face the captor, open hands, brace, step to room,
  crouch fallback, stepping turn, demeanor, kneel idle, flinch, individual debug keys.
- Descriptions: boss follower chance no longer mentions Rogues (ineligible since 0.30.0); Debug keys
  and Bosses say where their settings now are; Break chance and Look for another gun say what the
  player sees.

### Verification

- Build passed (Debug + Release, 0 warnings); pack checks passed; cfg bind on a copy of Joel's cfg:
  439 -> 439 entries, no value changed, nothing dropped, second load unchanged.
- Installed in H:/SPT4.1.X 2026-11-22 (hash-verified); 0.30.2 DLL + cfg in that session's scratchpad
  `backup-0.30.2`.
- Not run in game.

### In-raid checks (in order)

1. F12: each section shows only the settings above; Show advanced brings back the rest with Joel's
   values (his changed values still set).
2. An opening that ends in "hide": `slips into cover during an opening`, then (LogDecisions)
   `cover N m away`; he reaches cover; no Profile spike over ~2 ms on the opening frame.
3. With Verbose on: "no surrender check" lines for one bot at most once a minute per reason.
4. Profile: openings and the median window cost vs the numbers above.

## 0.31.1 (2026-11-22): empty automatic wouldn't drop

Joel: a surrendered bot whose weapon has no ammo doesn't drop it. Log (2026-11-22 11:47): ILikeLooting's
empty RPK-16 stayed in `AutomaticFireOperation` through 3 drop requests, twice ("couldn't empty his
hands after 3 requests ... fights instead", "HandsUp -> Rearmed (drop failed)"). Decompile (0.16.9):
`FireOperationBase.HideWeapon` only stores the hide in `_plannedAction`, run at the end of the fire
cycle (`InternalOnFireEndEvent`); with the gun empty that end never came. Fix
(`SurrenderRegistry.EndAutoFire`, called in `RequestDrop` after the trigger release): an
`AutomaticFireOperation` is fast-forwarded with the game's own `FastForwardCurrentState`, which for
that class is `SwitchToIdling` (trigger up, idle, operation finished, no shot), so the hide runs from
Idling. Single-shot fire operations are left alone (`FireOperation.FastForward` fires an unfired shot).
Build passed (Release, 0 warnings); packed `dist/Mercy-0.31.1.zip`; NOT installed (game was running).
In raid: an empty automatic gives up -> `hands still in automatic fire ... ended so the gun can drop`,
then `drops X ... request 1` and hands up, no "drop failed".

### 0.31.1 also: busy hands on healing bots

Joel: bots get busy hands while healing. No Mercy drop in the 11:47 log involved meds, so not the
surrender drop. Cause by inspection: `WeaponState.MaybeChange` (from the `TryReloadPatch` prefix, for
bots Mercy disarmed) called `Selector.TryChangeWeapon` whenever the hands held no firearm, and meds
in hands count as "no firearm". A released man healing, whose AI asked for a reload (HaveBullets is
false for him), got a weapon change on top of the meds. Now only from empty hands
(`EmptyHandsController`) or a knife (`BaseKnifeController`). Installed 2026-11-22 (hash-verified; 0.31.0
DLL + cfg in that session scratchpad `backup-0.31.0`). Not seen in a log either way: needs a raid.
Joel also runs ContinuousHealing 1.6.2 and HandsAreNotBusy 1.7.0 (healing/hands mods): if busy hands
on healing bots continue on 0.31.1, note whether those bots were ever Mercy prisoners.

## 0.31.2 (2026-11-22): hide all Mercy UI

Joel: a way to hide every Mercy screen element for screenshots (the scenario panel on the right).
New in 5. Commands and dialogue: "Hide all Mercy UI" (default off) and "Hide all Mercy UI key"
(default Insert; free in the game's Control.ini and in every installed mod cfg). All Mercy drawing goes
through RuntimeGui, so while hidden that component is disabled (Runtime.ToggleGui): no OnGUI at all.
Keys and orders keep working. Cfg bind on a copy of Joel's cfg: 439 -> 441, the 2 keys added, nothing
changed or dropped. Installed 2026-11-22 (hash-verified; 0.31.1 DLL + cfg in scratchpad `backup-0.31.1`).

## 0.31.3 (2026-11-22): Hide all Mercy UI took the F key

Joel on 0.31.2: "I can't press F on anything". Log: "Order menu: N hotbar/wheel command(s) kept from the
game while it was open" x4 between "Mercy UI hidden" lines. With "Interact key opens the menu", looking
at a prisoner in order range claims the interact key (OrderMenu hover -> InputGuard.ClaimInteract) and F
opens the menu; 0.31.2 hid the prompt and the menu but kept both, so F near prisoners went to an
invisible menu. Fix: OrderMenu.Update treats HideUi like the menu being off (closes it, no hover
claim). Built + packed; installed only once the game was closed (see CURRENT_STATUS).
Workaround on 0.31.2: press Insert to show the UI again (F works as before then).
