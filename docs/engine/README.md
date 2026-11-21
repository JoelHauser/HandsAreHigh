# Engine and integration findings

Verified facts about the game and the installed AI mods that this mod depends on. Read the file
for the subsystem you touch; each entry says what was examined and how it was verified.

| File | Covers |
|---|---|
| [ai-and-movement.md](ai-and-movement.md) | BigBrain layers, bot identity (BotOwner as IPlayer), enemies/targets, BotMover paths, doors, root rotation and turn clips, head free-look, scenario bot spawning |
| [inventory-and-hands.md](inventory-and-hands.md) | Dropping, drawing, reloads, hands controllers, pick-up transactions, slots, item classes, handbook prices |
| [animation-and-pose.md](animation-and-pose.md) | VisualPass ordering, skeleton bone names and axes, fingers, kneel/crouch capsule, lean, gestures, death |
| [input-and-voice.md](input-and-voice.md) | InputManager command filtering (hotbar, wheel, interact), key bindings, bot speech and phrase banks |
| [integrations.md](integrations.md) | SAIN 4.5.1, ORBIT 3.1.1, BigBrain 1.5.0, Pit Fireteam 1.1.2, Looting Bots, Fika |

## Verification labels used

- **code**: read in a decompile (or the mod's source) of the version named.
- **compiled**: the symbol exists and compiles against H:\SPT4.1.X (the build treats warnings as errors).
- **sim**: an offline model in `tools/` (a model of the rules, not the game).
- **log**: seen in a LogOutput.log from a real raid (says what happened, not always how it looked).
- **raid (visual)**: Joel saw it in game (screenshot or report).
- **unverified**: a hypothesis or inference; do not build on it without checking.

## Game version and where to look

- Client: EFT 0.16.9.40743 (SPT 4.1.x), de-obfuscated, installed at `H:\SPT4.1.X` (home PC).
  `C:\HUH` is the same build on the work laptop (no SAIN/ORBIT/BigBrain there).
- Decompile on demand; do not copy decompiled code into this repo:
  `ilspycmd -p -o <scratchpad>\eftfull -r H:\SPT4.1.X\EscapeFromTarkov_Data\Managed H:\SPT4.1.X\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll`
  (~2 min). Earlier sessions left full decompiles in their scratchpads (e.g.
  `%LOCALAPPDATA%\Temp\claude\8dc038bd4ad7aa29\2f222762-...\scratchpad\eftfull`); these are
  temporary and may be gone. An older obfuscated decompile (`_E180` names) in other scratchpads is a
  DIFFERENT build: never use it for 0.16.9 symbols.
- Search a decompile by type/method (`grep -rn "class BotMover\b"`, `grep -rn "ECommand.BeginInteracting"`),
  then read only the method needed.
- Mod DLLs: `H:\SPT4.1.X\BepInEx\plugins\` (SAIN.dll, ORBIT, BigBrain); decompile the same way.
  SAIN source v4.5.1 matches the installed build (SPT 4.1.3, EFT 40743).
- Asset data (skeleton, animator, voice banks) read with UnityPy, e.g.
  `H:\SPT4.1.X\EscapeFromTarkov_Data\StreamingAssets\Windows\assets\content\characters\character\skeleton.bundle`
  (scripts: `tools/fingersim/dumprig.py`, `tools/headsim/rig_axes.py`).

## Known unknowns (from the 2026-11-18 list, updated 2026-11-21)

- Resolved: `BotMover.GoToByWay` does move a held bot under SAIN once SAIN's ActiveLayer is cleared
  (0.3.0 log); `BotTalk.Say` does NOT play for SAIN bots (swallowed; we use Speaker.Play).
- Open: whether `fastHide` shortens the drop for every weapon (the timeline line tells per surrender);
  whether ORBIT's sleep shows as `BotState != Active` or an inactive GameObject (both handled); whether
  the bot animator shows a lean at +-3 in the hands-up pose; whether Mono inlines
  `MovementContext.SmoothTilt` (would bypass TiltPatch); phrase clips never listened to; Fika (decisions
  would need the host, the arm pose a synced flag). Forearm twist bones: superseded by the 0.13.1 arm
  solver (twist shared via SetWrist), look not re-checked in game.
