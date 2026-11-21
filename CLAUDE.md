# Hands Are High

SPT 4.1 client mod (BepInEx + BigBrain): a bot can surrender, drop the weapon in his hands and
put his hands up, then act on his own: break when you give him an opening, retreat when you
leave, and come back armed later. Bots take prisoners too. Repo https://github.com/JoelHauser/HandsAreHigh.

Stack: C# (LangVersion latest, net472, `TreatWarningsAsErrors`), Harmony, BepInEx 5, BigBrain 1.5.0
(only hard dependency), Unity IMGUI; EFT 0.16.9.40743 (SPT 4.1.x, de-obfuscated) at
`H:\SPT4.1.X`. Optional at runtime (reflection): SAIN 4.5.1, ORBIT 3.1.1, Looting Bots, Pit Fireteam.
Offline tools: Python 3 (+UnityPy) and one .NET test (`tools/`). The developer is Joel; he tests in
game and sends LogOutput.log and screenshots.

## Read first

1. This file, then [docs/CURRENT_STATUS.md](docs/CURRENT_STATUS.md) (what is in flight, what is
   verified in a raid, next steps).
2. The row for your task in the lookup table at the end of [docs/CODE_MAP.md](docs/CODE_MAP.md):
   it names the engine reference to read and the files/symbols to start from.
3. Engine facts: [docs/engine/](docs/engine/README.md). Build/checks:
   [docs/testing/build-and-checks.md](docs/testing/build-and-checks.md). In-raid checks and log
   reading: [docs/testing/in-raid-regression.md](docs/testing/in-raid-regression.md).
4. History (release notes, investigations, old test plans; some statements superseded):
   `docs/history/releases-*.md`. Open only when you need why something is the way it is
   (search it for the symbol or version; don't read it whole).

## Build and package (PowerShell; Bash mangles `H:\` paths)

```powershell
dotnet build src\HandsAreHigh\HandsAreHigh.csproj -c Debug -p:SPTPath=H:\SPT4.1.X   # compile check
.\scripts\pack.ps1 -SPTPath H:\SPT4.1.X            # Release + checks + dist\HandsAreHigh-<ver>.zip
.\scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install   # ...and copy the DLL into the install
```

- csproj default `SPTPath` is `C:\HUH` (work laptop): pass it. 0 warnings is required.
- Version lives twice: csproj `<Version>` and `HandsAreHighPlugin.PluginVersion` (pack.ps1 refuses a
  mismatch). Zip = `BepInEx/plugins/HandsAreHigh.dll` only: no README, no BigBrain, nothing else.
- Before `-Install`: the game must not be running (`Get-Process EscapeFromTarkov`); back up the
  installed DLL and Joel's cfg to the scratchpad; hash-compare after.
- Config changes: run `tools/cfgbind/bind.ps1` on a COPY of Joel's cfg (details in build-and-checks.md).

## Engineering rules (every task)

**Game and mod APIs**
- Verify every engine/SAIN/ORBIT/BigBrain symbol and its behaviour in the installed version's code
  (decompile on demand: docs/engine/README.md) before using it. Never invent a method or assume a
  finding from another version applies. Record new verified findings in docs/engine/ with version,
  symbol, evidence and verification level.
- Known traps (details in docs/engine/): `BotOwner.GoToPoint` teleports on failure (use our NavMesh
  path + `Mover.GoToByWay`); bot enemies are keyed by `BotOwner` (use `Perception.PlayerOf/Same`);
  SAIN keeps its own target and swallows `BotTalk`/`Player.Say` (use `Speaker.Play`); SAIN's
  `ActiveLayer` flag must be cleared while we hold a bot; `HaveBullets` stays true after a drop
  (WeaponState); `IdlePlayerState.Pickup` ignores its flag (exit only via `LeavePickupState`);
  a Knife is not a Weapon and grenades are never "armed"; the AI root snaps at TrunkRotationLimit 45;
  doors need `DoorOpener.UpdateDoorInteractionStatus` every moving frame; skip the body pose when
  `_bodyupdated` is false; the game may dispatch input before our Update runs.

**Hands and inventory**
- One hands request at a time per bot (token, stale callbacks ignored, abandon only after its
  limit). Never stack a drop, draw, reload cut or pick-up on an unanswered one; never cut a running
  transaction; nothing a bot carries is deleted or duplicated (scenario cleanup only removes tracked
  loose scenario items). Hand a bot to the AI only with no hands request of ours open.

**Releasing control (hand-back) must leave nothing of ours behind**
- The registry exit (`SurrenderRegistry.Tick` Finished loop + `Release`/`Unhook`) is the single exit:
  movement (StopMove unless handed back mid-stride on purpose; sprint off), look/turn (TurnDriver.StopNow
  restores TrunkRotationLimit), pose (arms down, kneel pose off first, Fingers.Restore, gestures cancelled),
  custody (Captors.EndCustody: captor layer, Overwatch guard, CaptorLoot job, ORBIT keep-awake),
  hostility (Restraint.Detach for an unarmed release, ForgetPrisoner otherwise; ignore flags and
  GoalEnemy/SAIN guards follow those), events (BeingHitAction / death hooks), ReleaseWatch/Escape start.
  Any new override you add (a patch flag, a layer, a cached decision) needs its removal here and in
  teardown. A layer's `IsActive` must go false as soon as it has nothing to do.
- Never restore stale objectives or targets onto a released bot; we stash none (ORBIT/SAIN keep their own).

**Lifecycle and hot reload**
- Every static registry, cache, Harmony patch, event hook, layer flag or component must be cleared in
  `HandsAreHighPlugin.Awake`'s `HotReloadTeardown` list (and on raid change via `EnsureRaid`/`Clear`).
  BigBrain layers can't be unregistered: after teardown old layers must be inert (empty registries).
  New client code ships hot-reload ready from its first commit.

**Dependencies and compatibility**
- BigBrain is the only hard dependency. Everything else optional, by reflection, failing soft with one
  log line. Never `BepInIncompatibility`; never tell Joel to remove a mod; step aside for installed mods.
- Joel runs SAIN + ORBIT 3.1.1 (not Looting Bots/Questing Bots). No Fika support.

**Performance**
- Patches on all-player paths and every layer `IsActive` exit on one cheap count first
  (`SurrenderRegistry.Any`, `Restraint.Any`, ...). No LINQ, closures, boxing, string building or
  `FindObjectsOfType` on per-frame paths; NonAlloc physics; heavy path/spot searches through
  `Perception.TakeHeavy`; logs only on events (never per frame), detail lines behind Log/Verbose
  switches. No Profile run exists yet: don't claim measured costs.

**Config**
- BepInEx section/key names must not contain `= \ " ' [ ]` (a throw in Awake kills the plugin
  silently; pack.ps1 checks). Don't rename existing keys (Joel's values live in them). Changed
  defaults migrate through `Settings.Migrate` (Settings.V19.cs) only where still at the old default.
- Never write Joel's cfg yourself from a read that may have failed.

**Behaviour Joel has set (keep unless he changes it)**
- Standing hands-up (not crouched); drop ONLY the weapon in hand; a breaking bot draws the slung gun
  first, then the sidearm, a knife only with no gun. Debug keys off by default; Joel has no numpad
  (menu `/` `.` `=`, digits, F). Scenario-only scopes stay scenario-only (e.g. the spawn magazine-check
  suppression). Never delete files from an SPT install unless Joel names the file.

## Architecture in one screen

- Entry: `HandsAreHighPlugin.Awake` binds settings, registers layers (Surrender 95, Captor 70,
  KeepAway 72, Staging 98), patches, teardown. `Runtime.Update` drives the registry, natural checks,
  menu and debug keys every frame.
- Prisoner: `SurrenderRegistry.Surrender` -> `SurrenderState` -> `SurrenderLayer`/`SurrenderLogic`
  phase machine (HandsUp, Retreating, Recovering, PickingUp, Hiding, Regrouping, Rearmed, Released) ->
  hand-back in `SurrenderRegistry.Tick`.
- Captor side: `Captors` (custody, plan, threat), `Encounter` (endings, speech floor), `Judgement`
  (lethal decisions), `Restraint` (hold-fire per observer), `Overwatch` (squad guard), `CaptorLoot`/
  `GearNeeds` (gear), `FireBreak` (incoming fire ends hold-ups).
- Pose: `ArmPosePatch` (VisualPass postfix) runs Kneel/Presence/ArmPose/Fingers in a fixed order.
- Input: `OrderMenu` + `InputGuard`; all orders through `Orders.Give`.
- Debug: `Scenarios/` sandbox, `ReleaseWatch`, `SurrenderStats`, `Perf`.
  Full map: docs/CODE_MAP.md.

## Working protocol

- Start with targeted filename/symbol searches (CODE_MAP names them); read the method plus enough
  context to see its callers, the state it touches and its cleanup. Widen only when evidence or a
  shared dependency calls for it; no repository-wide audit for a small fix.
- Logs: search by error text, bot name / scenario tag (`#S3-`), phase arrows, the anchors in
  in-raid-regression.md; read one bot's sequence with enough surrounding lines, never dump the log.
- Don't load generated output (`bin/`, `obj/`, `dist/`, `__pycache__`), binaries, whole decompiles or
  unrelated history.
- Keep related fixes together; no unrelated cleanup or refactors; no behaviour change in a docs task.
- Validate in proportion: Debug build while editing, the tool for the subsystem you changed, cfg bind
  when settings changed, pack when shipping. Don't rerun passing checks without new changes.
- Status words are exact: implemented / build passed / simulation passed / verified in raid. Never
  say something works in game unless a raid log or Joel showed it.
- Report: root cause, changed files, verification done, what still needs a raid. Joel decides when to
  commit and push (use the identity already set in the clone's git config).
- Don't spawn agents for routine work; delegate only with a clear benefit (it costs usage too).
- At a task boundary update docs/CURRENT_STATUS.md (replace, don't append). Put release notes in
  `docs/history/` (newest file) and new verified engine facts in docs/engine/, not here.
