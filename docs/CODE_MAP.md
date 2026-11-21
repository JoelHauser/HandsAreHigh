# Code map (src/HandsAreHigh, 0.28.6)

Files and the symbols to search for first. Verified against the tree on 2026-11-21; prefer
`grep -n "SymbolName"` over line numbers. Many files open with a header comment that explains the
design and its history: read it before changing the file.

## Spine: who owns a bot when

```
NaturalSurrender / debug key / scenario / Stop demand
        -> SurrenderRegistry.Surrender (one entry path; SurrenderState per prisoner)
SurrenderLayer (BigBrain, prio 95) -> SurrenderLogic.Run: phase machine per bot
   Phase: HandsUp -> Retreating | Recovering -> PickingUp | Hiding | Regrouping | Rearmed | Released
SurrenderRegistry.Tick (every frame, from Runtime): arms progress, Kneel/HandsPose/Facing/Presence/
   Gestures updates, deaths, and the HAND-BACK: Released/Rearmed + arms down + kneel pose off +
   no hands request open -> Finished -> ReleaseWatch.Start, Escape.Begin, Release(), Unhook,
   Captors.EndCustody, Restraint.Detach | ForgetPrisoner, States.Remove (layer goes inactive)
Captors (CaptorLayer prio 70) drives bot captors and idle squad watchers; Overwatch drives the guard
Escape.KeepAwayLayer (prio 72) after the hand-back while unarmed and drifting back
Pose: ArmPosePatch (postfix Player.VisualPass) -> Kneel.ApplyBody, Presence.ApplyUpper, ArmPose.Apply,
   Presence.ApplyHead, Kneel.ApplyHands, Kneel.ApplyBrace, Fingers.Apply
```

## By subsystem

**Core state and lifecycle**
- `HandsAreHighPlugin.cs`: Awake: Settings.Bind, layer registration (Brains / BossBrains lists,
  priorities), Harmony PatchAll, `HotReloadTeardown` list (every static cache/registry must be added
  here), `PrepareMethods` JIT warm-up. `PluginVersion` (must equal csproj `<Version>`).
- `HotReloadTeardown.cs`: ordered teardown + owned components; `Runtime.cs` (`Runtime`, `RuntimeGui`):
  per-frame driver (registry tick, natural checks, menu, debug keys via `Targeting.BotUnderCrosshair`).
- `SurrenderState.cs`: everything per prisoner (`Phase`, `Order`, kneel/pose/look fields, timeline
  stamps `T*`, `ResetEncounter`, `SetPhase`, `BeginRetreat`, `MarkFlight`, `LowerArms`).
- `SurrenderRegistry.cs`: `Surrender`, `Tick`, `HoldsBot`, `TryGet`, `HistoryOf` (`BotHistory`: nerve,
  reaction, cooldowns per bot per raid), `RequestDrop/StartDrop/AbandonRequest/CutReload`, `Witness`,
  `Discharge`, `Release`, `Unhook`, `Clear`, `EnsureRaid`.
- `Settings*.cs` (partial class `Settings`, 0.28.0): `Settings.cs` (helpers `Text`/`Range`/`IntRange` with
  the advanced flag and display order, `Bind`, `Migrate` + `CurrentConfigVersion`), one file per F12
  section `Settings.1General.cs` .. `Settings.7Debugging.cs`, `Settings.Layout.cs` (`Moves`: old ->
  new keys, `MigrateLayout`, the three merged settings). Names must avoid `= \ " ' [ ]`.
- `Dice.cs` (seedable rolls), `Perf.cs` (profiler sections; detail sections Kit/Start/Decide are not summed), `FastAccess.cs` (compiled SAIN readers).

**Natural surrender and decisions**
- `NaturalSurrender.cs` (`Tick`, `Eligible`, `Injury`, `Experience`, `ClassOf`; header has the model),
  `Suppression.cs` (`ShotPatch` near misses, `UnderFirePatch`, decay), `SurrenderStats.cs` (raid
  summaries by source), `StopDemand.cs` (`SayPatch`), `Targeting.cs`.
- Prisoner decisions in `SurrenderLayer.cs`: `HandsUp`, `DecideOnOpening`, `TakeOpening`,
  `ScrambleQuality`, `DecideOnLeave`, `Abandonment`/`LeftDecide` (left by a bot captor),
  `UnderFire`/`EscapeFire`, `ReassessEscape`, `NoticeGunGone`, `NoticeApproach`.
- `Decisions.cs` (`Reactions`: `Covered`, `Shows`, `SquadCovers`, `FreeAlliesNear`, `WarningShot`),
  `Compliance.cs` (answers to Drop your weapons), `Gestures.cs` (wiggle, middle finger, `TiltPatch`),
  `Presence.cs` (demeanor, attention, head/torso look, flinch, idle).

**Captors, custody, encounters, hostility**
- `Captors.cs`: `OnSurrender` -> `Due`/`TickDue` (0.28.5: decided a frame later) -> `Decide`/`Impractical`/`Accept`,
  walk-up `Approach` (`StuckWhy`), `Tick`/`Check` (plan steps), `Threat`,
  `HandTo`, `Interrupt`, `End`/`EndCustody`, `MaybeTakeOver`, `ForceAccept`, `WantsControl`,
  `CaptorLayer`/`CaptorLogic`. 0.28.0 ignored surrenders: `TryIgnore` (chance x personality/situation),
  `Ignore`, `ForceIgnore` (debug key); the prisoner side is `SurrenderState.IgnoredBy`/`Ignored`,
  `SurrenderLogic.Abandonment` (ignored cue, `ObserverCovers`), `LeftDecide` -> `IgnoredRecover`.
- `Truce.cs` (0.28.0): (holder, other) hold-fire pairs after an ignored surrender, both ways, started
  at the hand-back (`Truce.Begin`), ended by a hit between them, a re-hold or the time; read by the same
  patches as restraint (GoalEnemy, ShootData, trigger, SAIN filter) via `Truce.Active`/`Blocks`.
- `Encounter.cs`: endings (`WrapUp`, `WalkAway`), no-recapture memory (`Remember`, `Recently`,
  `EndedBy`), speech floor (`MaySay`, `FloorFree`, `TookFloor`), `AssessFactor`.
- `Judgement.cs`: lethal intent and kill decisions (`Intend`, `AtRelease`, `OnBolt`, `AfterDemand`,
  `TickKills` execution watch). `Restraint.cs`: per-(observer, prisoner) hold-fire decisions
  (`Set`, `InformSquad`, `Detach`/`TickDetached`, `ForgetPrisoner`, `OnArmed`, `OnPrisonerHit`) and
  the patches `GoalEnemyPatch`, `ShootDataPatch`, `TriggerPatch`, plus the per-round burst guard
  `ShotAtRestrained` (from `Suppression.ShotPatch`, on `Perception.OnLineOfFire`). `SainTargetFilter.cs`: SAIN
  ChooseEnemy filter/steer. `FireBreak.cs`: incoming fire breaks a hold-up.
- `Overwatch.cs`: squad guard (`CoverGuard`, `Visibly`, `Covered`, `HoldForCover`).
- `Orders.cs` (`CanGive`, `Give`: the only order path, human take-over included), `CaptorVoice.cs`,
  `Voice.cs`, `CommandVoice.cs`.

**Release, escape, recovery, hand-off**
- `SurrenderLayer.cs`: `Retreating`, `PickRetreat`, `LegDone`, `QuickClear` (0.25 mid-stride
  hand-off), `Regroup`/`Regrouping`/`Decide` (kept gun, own gun, scavenge, knife, else `HandBack`),
  `Recovering`, `PickingUp`, `AfterPickUp` (`InHands`), `GiveUpOn`, `GoHide`/`Hiding` (`SetHideTime`), `TakeCoverFromFire`,
  `Rearm`/`Rearmed`, `HandBack`, `Released`, `SpottedAgain`, `MoveTo`, `Stalled`, `Stamp`.
- `Perception.cs`: `FleePoint`, `CoverPoint`, `RetreatPoint`, `PathTo`, `Reachable`, `RoutePassesNear`,
  `Sees`, `Recognises`, `CanSee`, `LineClear`, `InLineOfFire`, `KnownPosition`, `PlayerOf/Same/InfoAbout`,
  `TakeHeavy` (search budget).
- `Escape.cs` (capture memory, `KeepAwayLayer` keyed on the ex-captor via `CaptorAtSpot`, `StuckWhy`, `CheckObjective` -> `OrbitObjectives.RetireIfBack` in
  Compat.cs: drops an ORBIT objective that leads back to the capture), `ReleaseWatch.cs` (+15..+120 s
  lines, hand-off timeline), `WeaponState.cs` (`HaveBulletsPatch`, `TryReloadPatch`).
- 0.26.0 gun at his feet: `SurrenderLayer.TryGunAtFeet` / `Endangered` (first thing in `LeftDecide`
  and `DecideOnLeave`), `Arsenal.Locate` (the item where it lies now), `SurrenderState.CalmRearm`
  (no battle cry / no turn to the ex-captor in `Rearm`/`Rearmed`).

**Pose: hands, fingers, head, torso, kneeling, turning**
- `ArmPose.cs`: `ArmPosePatch` (the order of everything), `Apply`, `ArmFrame`, `TorsoFrame`,
  `MeasureAxes`, `PoseArm`, `SetWrist`, `HandsOf` (`HandRig`).
- `HandsPose.cs` (hands on head order + eased `HeadHands` profile), `Fingers.cs` (finger tables, animator
  detection, `Restore`), `Kneel.cs` (partial: `Request`, `Update`, `TryBegin`, `StartRise`, `ApplyBody`,
  `SolveLeg`, `ApplyHands` + `OverHands`/`HandT`, `ApplyBrace`, styles `KneelStyle`, ground/room checks,
  `HoldRoot`, debug kneel tests), `KneelVariant.cs` (partial Kneel: upright/settled/tense),
  `Presence.cs` (head/torso), `HeadFreeLook.cs`, `Facing.cs` (facing, Turn around), `TurnDriver.cs`
  (stepping turns via TrunkRotationLimit), `Gestures.cs`.

**Inventory, equipment, looting**
- `Arsenal.cs`: drop (`EmptyHands`, `Drop`), draw (`Draw`), `PickUp`, `FindWeapon`, gear classes
  (`Kind`, `Usable`, `UsableGun`, `GunOnBody`, `HasMelee`), kit value (`AssessKit`), descriptions.
- `CaptorLoot.cs`: captor gear jobs (`LootJob`, `Weigh`, `StandPoint`, `LeavePickupState`, `Busy`,
  `GatheringNear`, after-release fetch), `GearNeeds.cs` (demand gear the captor lacks), `Compliance.cs`.

**Player input and UI**
- `OrderMenu.cs` (menu state, `LookedAt`, `Validate`, `HandleInput`, `Submit`, IMGUI view),
  `InputGuard.cs` (`DispatchInputPatch`, `Claim`, `ClaimInteract`, latches), `Scenarios/ScenarioUi.cs`.

**Debug scenarios and diagnostics** (`Scenarios/`)
- `Scenarios.cs` (lifecycle, timeline listener), `ScenarioPresets.cs` (presets + params),
  `ScenarioSpawner.cs` (spawning), `ScenarioStaging.cs` (`StagingLayer`, hold until all ready),
  `ScenarioOps.cs` (kit/items), `ScenarioEvents.cs`, `ScenarioShield.cs` (truce/observer pairs),
  `SpawnQuiet.cs` (no mag check at spawn). Diagnostics: `ReleaseWatch`, `SurrenderStats`, `Perf`,
  "Log: pose diagnostics" (Kneel.PoseDiag / Presence), overlay `SurrenderLayer.BuildDebugText`.

**Optional integrations**
- `SainAdapter.cs` (`ReleaseLayer`, personality, suppression), `SainTargetFilter.cs`, `FastAccess.cs`,
  `Compat.cs` (`OrbitAdapter`, `LootingBotsAdapter`, `Teammates` = player allies / Pit Fireteam).

## Task lookup: read these first

| Task | Docs | Start in | Also check |
|---|---|---|---|
| Hands, fingers, head, torso pose | engine/animation-and-pose.md | ArmPose, HandsPose, Fingers, Kneel.ApplyHands, Presence | ArmPosePatch order; tools/armsim, fingersim, headsim; Fingers.Restore on exit |
| Kneel / rise / variants | engine/animation-and-pose.md, engine/ai-and-movement.md (rotation) | Kneel.cs, KneelVariant.cs | HandsPose windows, Facing/TurnDriver, SurrenderLogic.KeepLookOnBody/SteerTo, tools/kneelsim |
| Turning / facing | engine/ai-and-movement.md (rotation) | Facing, TurnDriver | Presence.Smooth look lead, Kneel.PreTurn, TurnDriver.StopNow on exit |
| Natural surrender odds | engine/ai-and-movement.md (identity, suppression) | NaturalSurrender, Suppression | SurrenderStats gates, tools/natsim, cfg migration |
| Prisoner openings / decisions | (code headers) | SurrenderLayer HandsUp..TakeOpening, Decisions | Presence demeanor, Compliance, Kneel posture factor |
| Release, escape, recovery, hand-off | engine/ai-and-movement.md, engine/inventory-and-hands.md | SurrenderLayer flight/Decide/HandBack, Perception.FleePoint, Escape, ReleaseWatch | Registry hand-back conditions, Restraint.Detach (hostility after release), WeaponState, KeepAway priority, SAIN layer release, mover StopMove ownership, tools/releasesim, escsim, abandonsim |
| Captor behaviour, endings | engine/ai-and-movement.md | Captors, Encounter, Judgement | Restraint squad rules, Overwatch, CaptorLoot, Orders, ORBIT KeepAwake |
| Hostility / hold fire / kills | engine/ai-and-movement.md (targets) | Restraint, SainTargetFilter, Judgement, FireBreak | Perception.InLineOfFire, ScenarioShield, tools/firesim |
| Inventory, drop/draw, pick-up | engine/inventory-and-hands.md | Arsenal, CaptorLoot, GearNeeds | one-hands-request rule, LeavePickupState, WeaponState |
| Squad cover | engine/ai-and-movement.md | Overwatch | Captors.SuccessorFor/End, tools/coversim |
| Speech | engine/input-and-voice.md | Voice, CaptorVoice, CommandVoice | Encounter floor, SAIN talk patches |
| Order menu / keys | engine/input-and-voice.md | OrderMenu, InputGuard, Orders | Runtime debug keys, ScenarioUi claims input too |
| Debug scenarios | engine/ai-and-movement.md (spawning) | Scenarios/*.cs | ScenarioShield vs Restraint, cleanup report |
| Settings / config | testing/build-and-checks.md (config check) | Settings*.cs | Migrate, pack.ps1 name check, Joel's cfg values |
| SAIN / ORBIT / other mods | engine/integrations.md | SainAdapter, SainTargetFilter, FastAccess, Compat | teardown, version of the installed mod |
| Performance | (this file: Perf) | Perf.cs, any IsActive / per-frame path | CLAUDE.md "Performance" (measured 0.28.x; GC frames are not ours) |
| Lifecycle / hot reload | engine/integrations.md (BigBrain) | HotReloadTeardown, HandsAreHighPlugin.Awake, *.Clear/Reset | every new static cache, patch, event hook or layer |
