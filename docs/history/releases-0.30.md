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
