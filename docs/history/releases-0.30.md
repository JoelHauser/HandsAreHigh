# Release 0.30.0 (Mercy)

## 0.30.0 (2026-11-21): renamed Mercy; boss last-survivor rule verified; Rogue and roll-cap fixes

Contains 0.29.0 (boss last-survivor rule, Raiders split from bosses), 0.29.1 (unset last-seen
position, trigger-log wording) and the rename (notes for all three: releases-0.29.md).

### Lighthouse raid on 0.29.1 (2026-11-21, Joel playing; verified in raid, log level)

- Boss gate: Zryachiy targeting Joel at 76-79 m, outnumbered 3 vs 1: "boss: 2 of 3 guard(s) not
  confirmed dead" -> no roll, every check.
- Dead vs gone: the game's group said "lost 2 of 2" (both cultists left his BotsGroup) while the roster
  said "1 of 3 not confirmed dead": a guard out of the group without a death did not count.
- Natural last-survivor roll: "last survivor (4 guard(s) confirmed dead), roll 1 of 3", stress 0.86
  (light bleeding, fire 0.61, out of ammo), chance 0.088, roll 0.664 -> refused, fought on. Then "no
  worse than at his last refusal" on later checks (no repeated rolls).
- Roster growth: a fifth member joined his group later -> "1 of 5 not confirmed dead" -> ineligible again.
- Debug-key surrenders of followerZryachiy (x3) and Zryachiy (x2): boss/follower layers live, prisoner
  behaviour ran (hide under fire, bolt when shot, wiggle/finger rolls, hand-back to CloseZrFight /
  ZryachiyFight). Cultist voice has no surrender line (silent, logged once). One "no stepping turn
  from the game's animator" warning on a cultist: fell back to pivoting (handled).
- After release Zryachiy sat crouched for minutes in his own layer: his rifle was on the ground (dropped
  at the debug surrender) and his sidearm read out of ammo; the game's Zryachiy layers have no
  no-ammo plan. Not held by us (ReleaseWatch: layer ZryachiyFight/CloseZrFight, capture forgotten at 150 s).

### Fixes from that raid

- Rogues (exUsec) and other "boss or follower" roles whose brain carries none of our layers (sectants,
  BTR gunner, arena fighters) were checked for natural surrender with bosses on; a surrender would have
  been registered with no layer to drive him. `ClassOf` now returns None for a Boss-class bot unless
  `MercyPlugin.HasOurLayers` (brain `BaseBrain.ShortName()` in our registered lists); the role is only
  cached once his brain exists. Pre-existing since bosses were first supported.
- Lone-boss re-roll threshold capped at full stress: a first roll at 0.86 required 1.01 (impossible), so he
  never rolled again. Now min(last + 0.15, 1.0) and strictly worse than last time.
- tools/bosstest 55/55 (3 new cap cases).

### Prone surrender (Joel's request)

- A bot prone when he gives up gets to his feet: `SurrenderLogic.GetUpFromProne` sets the game's
  `BotLay.IsLay = false` (DoProne(false), pose 0.4; verified in Assembly-CSharp 0.16.9), then our
  SetPose(1) stands him (cached pose reset). Retried at most once a second while still prone; logged
  "was prone when he gave up; gets to his feet". Our SetPose alone only sets PoseLevel, never prone.

### Install (2026-11-21)

- Packed dist/Mercy-0.30.0.zip and installed: HandsAreHigh.dll + com.joelhauser.handsarehigh.cfg backed up
  to that session's scratchpad (backup-0.29.1-handsarehigh), cfg copied to com.mybutthasarash.mercy.cfg
  (hash-identical; bind 439/439, stable), HandsAreHigh.dll removed (Joel approved), Mercy.dll hash-verified.
  Old cfg left in place. tools/bosstest 55/55 on the Release DLL.

### Still needs a raid

- Mercy.dll under the new GUID (cfg carried over), Rogues no longer checked, a boss's second roll at
  full stress, a natural boss surrender carried through to the prisoner behaviour, a prone bot standing on surrender.

## 0.30.1 (2026-11-21): packed + installed (hash-verified; 0.30.0 DLL + cfg in that session scratchpad backup-0.30.0); unrun

From the 0.30.0 Lighthouse raid (Mercy loaded under the new GUID with Joel's cfg: verified in raid;
Rogue checks now blocked: "Role 1705" in the raid summary; prone get-up fired 3x).
- Loose guns: a held man with no gun of his own (or his gone/failed) adopts the nearest loose gun within
  the scramble range (ground or body, 2 s cadence, TakeHeavy) as the gun he could go for
  (`SurrenderLogic.LookForLooseGun`, `SurrenderState.GunNotHis`); openings, fire dashes, left-behind
  pick-ups, walk/pick-up/draw then apply. Cultists brought no guns and ignored guns Joel put down.
- 47 ms Layer frame on a released man's 60 m weapon sweep: `Arsenal.OwnerOfCollider` never served cached
  "no owner" answers (doors/props on Interactive re-ran GetComponentInParent every sweep). Now cached;
  new Perf detail section Scavenge (inside Layer). Unverified that this was the whole 47 ms.
- Prone: Zryachiy stayed prone through ~10 tries. Each try now logs whether the game refused at once
  (UsedSimplifiedSkeleton shown) or he was laid down again; max 4 tries, reset at hand-back.
- Boss gate line lists each unconfirmed guard (name, role, alive + distance, left his group, prisoner,
  or no longer in the raid): his roster grew 2 -> 5 on Lighthouse, unexplained.
- Released men walked past guns (Joel: "weapons on the ground and they're choosing not to pick them up"):
  the Hiding phase's calm endings (QuickClear, in cover) handed back directly, skipping Decide (draw a
  kept gun / fetch a safe gun / then hand back) that Retreating already used. They now go through Decide
  (cover-from-fire endings still hand back directly: no loop). Decide's capture-area lock now applies only
  after an escape, not a calm exit (CalmFlight); guns near where he last saw his captor stay refused.
- Open: Joel's "busy hands glitch" (nothing in the log; awaiting his description).

### 0.30.1 second Lighthouse raid (2026-11-21; verified in raid, log level) and the build installed after it

- Verified: a held PMC adopted a loose VPO-101 8 m away (not his own), scrambled for it under fire,
  picked it up, rearmed, handed back to SAIN; own-gun scramble (Tavor) and captor-dead pick-up (MP-43-1C)
  rearmed; a calm hide ending drew a kept SPEAR via Decide; prone get-up "try 1 of 4: up" on a Scav.
- Fixed after it (installed, hash-verified; previous DLL + cfg in that session scratchpad backup-0.30.1a; unrun):
  NoticeGunGone ran mid-drop (logged "gone" before "gun on the ground"): waits for TThrown + 1 s, own gun
  only; LookForLooseGun re-finds a gun believed gone (GunGone cleared). Every calm hide ending goes
  through Decide (SurrenderLogic.EndHide); fire hides hand back directly. The capture-area gun lock needs
  his ex-captor last seen near the capture spot (Scav refused his Saiga with the captor 168 m away).
  "the" instead of "his" for adopted guns (His(s)).
- Seen: 215 ms Natural frame right after an SPSave autosave (2nd case; our work trivial, only verbose log
  lines: log-write stall suspected, unverified); event frames 12-22 ms in Layer (bolt / hide end).

## 0.30.2 (2026-11-21): packed + installed (hash-verified; previous DLL + cfg in that session scratchpad backup-0.30.1b); unrun

From the third Lighthouse raid on 0.30.1 (Goons; 16 natural surrenders, 0 Mercy warnings/errors; verified:
own gun fetched at the capture spot once the captor left, kept sidearms drawn on release, many scramble
pick-ups) and the COMET read:
- Busy hands: Knight's first drop unanswered 2 s (hands empty 3.8 s): the drop is asked while SAIN still
  drives him (our layer starts next brain update) with the trigger held. RequestDrop now ends SAIN's shot
  and releases the trigger first; TriggerPatch refuses presses by a man whose DropPending is set.
  Миха (not fighting) also had an unanswered first drop: game code (Player.Process.Proceed) shows a
  skipped process does call back with an error, so "no answer" = accepted but queued behind a running
  hands operation. Arsenal.DescribeHands now names the firearm's CurrentOperation; fix pending that name.
- Released men fetch guns (Escape fetch legs, KeepAway layer): see Escape.cs header. Joel's rules: only
  when his ex-captor can see neither him nor the gun; reach decided by the bot (nerve + personality, 6-40 m).
- COMET: SainTargetFilter.Postfix drops a protected result (engine/integrations.md, COMET section).
