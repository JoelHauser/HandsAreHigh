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

## 0.31.4 (2026-11-22): fixes from the 0.31.3 Customs test raid (log watched live)

Raid: 15 natural surrenders + 1 debug-key boss (Решала, orders, left, braced rise, rearm), 0 Mercy
errors, 0 restraint leaks. Verified in raid: opening frame 1.1-2.3 ms (scramble, run, hide; was 4-8 ms),
deferred hide search reaches cover (Гриша 23 m), empty-automatic drop (Danonek AKS-74UN from
AutomaticFireOperation: first request, hands empty +599 ms), Hide all Mercy UI + F (Joel), released-man
gun fetch started for the first time (Тимурка, Карлуша) and a scavenge pick-up 20 m away (hgj).

Fixed:
- A gun that comes into a prisoner's hands after he gave up is dropped (SurrenderRegistry.Tick, with the
  "no draw of ours" warning). StartDrop reads the hands once; Святополк gave up mid weapon-switch, read
  "empty", and the Vepr-KM finished arriving in his hands while held.
- CheckDrop waits out a running hide (the game's FirearmController.Remove) up to 3x DropRetrySeconds
  before asking again; hgj's SV-98 took 2.36 s, request 2 landed on the running hide.
- Released-man gun fetch (Escape RunFetch): at the gun with meds/grenade in hands, waits 3 s, then ends
  the leg and logs the hands. Before: silent wait to the 25 s leg limit (Тимурка 13 s, Карлуша 2x 25 s;
  both hurt, ContinuousHealing installed: probably healing; hands state was never logged).
- Recovery walk give-ups ("took too long" / "stuck on the way") log distance, height and hands (Карлуша
  gave up 2 m from his gun with no reason given).

Open (performance, single frames): FleePoint on a bolt / new leg 3.8-4.9 ms; Regroup Decide on arrival
2.6-4.6 ms; first GUI draw of a note/menu/scenario panel 3-7 ms; each order ~2.4 ms. Check: opening kind
"captor is running" during a bot captor's walk-up. Not yet seen in raid: healing busy-hands fix, a bot
taking custody (every bot captor was out of guard range).

Other mods' errors seen (for their authors): TransparentSights missing config.json; SAIN destroying itself
on a player-scav bot assigned Killa's brain; Raid Review NullReferenceException in
ClientGameWorld_ShotDelegatePatch.PatchPostFix and "WebSocket closed; raid recording incomplete".

## 0.31.5 (2026-11-22): performance batch + captors keep their gun on the prisoner

Performance (single frames from the 0.31.3 raid):
- Perception.FleePoint (bolt / new escape leg, 3.8-4.9 ms): candidates drawn as before (same Dice order),
  cheap tests first, an upper bound on each score with the route length at its floor (straight line,
  +0.5 for the path start snapping nearer), routes solved best bound first, skipped once they can't win.
  Same choice: simulation 20,000/20,000 identical picks, path solves 16.2 -> 1.7 on average
  (scratchpad fleeequiv.py; a model, not a raid measurement). Log stats gain "could not beat the best".
- Order menu first draw (3-7 ms in Gui): the font search (Resources.FindObjectsOfTypeAll<Font>) and the
  glyphs (Latin + Cyrillic at the menu's pixel sizes, Font.RequestCharactersInTexture) moved to raid start
  (OrderMenu.Prewarm from SurrenderRegistry.EnsureRaid, during loading).
- The Regroup "what now" frames (2.6-4.6 ms) were the game's own weapon change (selector.TryChangeToSlot
  builds the firearm in his hands): not ours to cut; now timed as detail section "Draw" to show it.
- Not changed: each menu order ~2.4 ms (one keypress); "captor is running" is the game's IsSprintEnabled
  (Денис's captor really sprinted; not a mislabel).

New (Joel): captors keep their gun on the prisoner (CaptorLogic.Aim / Threat / AimDown in Captors.cs).
Verified in 0.16.9: vanilla BotPeaceHardAim.ManualUpdate = ShootController.SetAim(true) each frame +
Steering.LookToPoint; EndAim = SetAim(false). Threat 0..1 from what he can see (base 0.3; within 3 m +0.25 /
6 m +0.12, past 15 m -0.1; moving +0.25; hands not up +0.2; kneeling -0.15; kept something back +0.25 or a
gun still on him +0.15; defiant +0.15; trouble nearby +0.15; a gun in hands or not holding still = 1).
Raised for Lerp(low 3 s, high 20 s, threat) (x0.85-1.15), always from 0.75; then the ready for
Lerp(6 s, 1 s, threat) (x0.8-1.2); up at once when the threat rises 0.2 over the moment he lowered. Down
(SetAim false) on a glance, a loot job, a guard's scan, reload, no firearm in hands, layer Stop.
Applies to the custody guard, the walk-up and the squad guard (Overwatch cover). Log (Log decisions /
Verbose): "raises his gun on X for N s (threat T: reasons)". Settings: section 3 "Captors keep their gun on
the prisoner" (visible, on) + 3 advanced keys; cfg bind 441 -> 445, 4 added, nothing changed or dropped.

Build passed (Debug + Release, 0 warnings). Installed 2026-11-22 (hash-verified; 0.31.4 DLL + cfg in that
session scratchpad backup-0.31.4). Not run in game.
In raid: a bot taking custody -> "raises his gun on ..." lines, sights up then the ready, up again when the
prisoner moves; no aim while he picks gear up; no stuck ADS after custody ends. A bolt's Layer frame
under ~1 ms; FleePoint stats with "could not beat the best". No 3-7 ms Gui frame on the first menu/note.

## 0.31.6 (2026-11-22): fixes from the 0.31.5 Customs raid (log watched live)

Raid: 15 natural surrenders, 15 clean hand-backs, 5 bot custodies, 0 Mercy warnings/errors, 0 leaks.
Verified in raid on 0.31.5: captor aim ("raises his gun on X for as long as that lasts (threat 1.00: a gun
in his hands)", then "for 10 s (threat 0.35: 18 m off, a gun still on him)"; Лева took an opening on
"his captor's gun isn't up"), ignored surrender end to end (___5 ignored Васян: noticed +0.53 s, gun at
his feet, fallback draw, 30 s truce ended with no hits), FleePoint pruning ("12 could not beat the
best"), menu prewarm (font "Jovanny Lemonad - Bender" found at raid start; raid-start frame 31.6 ms,
in loading), Draw detail section (1.8-2.7 ms of the regroup frames = the game's weapon change), second
custody frame 1.4 ms (the first, 16 ms, was first-use).

Fixed:
- Released-man gun fetch: the root cause of the freezes. Escape RunFetch read "an item in hands that isn't
  a knife" as busy, and the empty-hands controller reports a placeholder Item: every fetch waited at the
  gun ("his hands are busy (empty)", 0.31.5 log). Now Arsenal.HandsEmpty is free. (0.31.4's guess,
  healing, was wrong; its 3 s cap stays for real meds/grenades.)
- Meds/stims in use: no drop request (StartDrop/CheckDrop wait up to 8 s) and no recovery pick-up on top of
  them (Arsenal.UsingMeds). Паша's scramble pick-up made the game put a stim away mid-use and the game's
  BotStimulators TryApply callback threw (NRE in TasksExtensions_HandleFinishedTask_Patch1); Васян's drop
  asked twice over an AI-2 in use.
- Retreating searches its route from the frame after the flight was decided (SurrenderState.BoltFrame):
  decision + FleePoint together made 4 ms frames (Антич, 6 long route solves).
- TakeOpening's run/hide availability checks stop after 4 failed path solves (RetreatPoint maxSolves):
  1-4 ms opening frames when the first spots were unreachable.

Not changed: first-use costs each game session (first surrender ~15 ms, first custody 16 ms, a 10 ms first
shot near a prisoner): our methods are already pre-compiled (PrepareMethods); the rest is game/Unity code
run for the first time.

Other mods (for their authors): SAIN "Bot is a PlayerScav but does not have any of these BaseBrains ...
Current Brain Assignment: [Killa] : Destroying SAIN for this bot" (2 this raid, 1 last); the game's
BotStimulators callback NRE when meds are dropped mid-use (ContinuousHealing patches in the stack);
Raid Review "WebSocket closed; this raid recording is incomplete"; TransparentSights missing config.json.

Build passed (Release, 0 warnings); packed dist/Mercy-0.31.6.zip. No settings changed since 0.31.5.
