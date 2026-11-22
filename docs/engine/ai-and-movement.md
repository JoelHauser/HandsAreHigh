# AI brains, identity, targeting and movement (EFT 0.16.9 + BigBrain/SAIN as installed)

Provenance: release notes 0.2.0-0.25.0 (now `docs/history/`), re-checked against current code
2026-11-21 where marked. Verification labels: see [README.md](README.md).

## Layers and who drives a bot

- Our layers (BigBrain `CustomLayer`, registered in `MercyPlugin.Awake`): Surrender
  (`Settings.LayerPriority`, default 95), Captor (`CaptorPriority`, 70), KeepAway (Captor+2 = 72,
  clamped below Surrender), Staging (Surrender+3, scenario bots only). Brains: Assault, CursAssault,
  PMC, PmcBear, PmcUsec; boss/follower brains only when "Bosses and followers can surrender" is on at
  startup. [compiled; brain names from the brains' ShortName strings, code]
- BigBrain cannot unregister a layer; layers attach when a brain activates. A hot reload leaves old
  layers on existing bots: they must go inert (empty registries), never act. [code, BigBrain 1.5.0]
- `IsActive()` is asked for every bot on every brain update: start with one cheap count
  (`SurrenderRegistry.Any`, etc.). A layer must return false as soon as it has nothing to do, or it
  keeps priority while idle. [code]
- SAIN layers: combat solo 20 / squad 22 / extract 24, AvoidThreat 80, debug 99. ORBIT 3.1.1:
  `OrbitBrainLayer` 19. Pit Fireteam follower layers 65-101. [code: SAIN 4.5.1, ORBIT 3.1.1, Pit 1.1.2]
- SAIN only clears `BotComponent.ActiveLayer` from its own layer's IsActive; once our layer wins,
  the flag stays set and SAIN's mover patches skip the game's mover and SmoothTurn turns the bot. Fix
  in use: `SainAdapter.ReleaseLayer` sets it to None every 0.25 s while we hold a bot. [code + log:
  "SAIN still had its layer marked active; released" in the 0.24.0 raid log]

## Identity: bot enemies are BotOwners

- `BotOwner` implements `IPlayer`; `BotSpawner.SetBotAsEnemy` / `BotsGroup` ctor register BOT enemies
  as their BotOwner (humans as Player). `EnemyInfo.Person` and `EnemiesController.EnemyInfos` keys are
  a BotOwner for bot-vs-bot pairs. Always compare via `Perception.PlayerOf / Same / InfoAbout`. [code,
  0.14.0 root cause of "bots never surrender to bots"]
- `BotsGroup.IsEnemy` compares by Id but only knows players already in the group's Enemies list;
  an `EnemyInfo` exists only once he has noticed them. An unseen shooter is NOT "friendly" by that test
  (FireBreak.SameSide uses same group / `IsAlly` only). [code, BotsGroup ~line 832]
- `BotOwner.UpdateManual` runs the LookSensor regardless of brain, so a held bot's
  `EnemyInfo.IsVisible` keeps updating; `EnemyInfo.PersonalLastPos` = last known position;
  `EnemyInfo.LastGetHitTime` is set by `BotOwner.GetHit` for the enemy that hit him. [code]

## Targets and firing

- Vanilla target: `BotMemory.GoalEnemy` (we prefix its setter: `GoalEnemyPatch`). Restraint also uses
  `EnemyInfo.SetIgnoreState(true)`, a `ShootData.Shoot` prefix and a last-line
  `FirearmController.SetTriggerPressed` prefix (`TriggerPatch`), all in Restraint.cs. [compiled]
- SAIN keeps its OWN target (`SAINEnemyController._goalEnemy`, from VisibleEnemies/KnownEnemies) and
  only mirrors it to `BotMemory.GoalEnemy`. Filtering/steering is done on `ChooseEnemy` (not
  SelectEnemy: it indexes KnownEnemies[0]); SAIN's GoalEnemy setter is PRIVATE (0.18.0 asked for a
  public one and never steered). See `SainTargetFilter`. [code SAIN 4.5.1, log 0.18.0]
- `BotWeaponManager.ShootController` is only cleared in Dispose: after our drop `HaveBullets`
  (= `_currentWeaponInfo.Reload.BulletCount` of the stale controller) stayed TRUE for an empty-handed
  bot, so SAIN treated disarmed men as armed (rush, search, grenade throws). `WeaponState` patches
  `HaveBullets` (getter postfix) and `BotReload.TryReload` (prefix) for bots we disarmed. [code;
  log 0.24.0: "'has bullets' reads false for his AI" fires; whether SAIN then retreats: unverified]
- SAIN's decision order: `!HaveBullets -> Retreat` first, then StandAndShoot / Rush / ThrowGrenade /
  MoveToEngage / Search. Grenade check: enemy not visible 3 s, last known 8-75 m, updated < 120 s,
  every 5-10 s. Melee: `MeleeAttack` whenever `WeaponManager.IsMelee`, any distance (dry gun -> melee
  -> knife charge). [code SAIN 4.5.1]
- Friendly fire: vanilla `ShootData.CheckFriendlyFire` and SAIN's `SAINFriendlyFireClass` hold a shot
  with a squadmate in the line. [code]
- Our trigger guard: `Perception.InLineOfFire` = the man's body on the line (0.55 m + 1 cm/m either
  side), not if the shooter's own GoalEnemy stands in front on that line. (0.20 used a flat 6 deg cone
  at any range: blocked legitimate shots.) [code, sim tools/firesim 12/12]

## Suppression and hearing

- Near misses: postfix on `Player.FirearmController.InitiateShot` (`ShotPatch`), measured
  geometrically; hits via `BotMemory.SetUnderFire` (`UnderFirePatch`, called by GetHitInner).
  SAIN's `HearingSensorPatch` disables `BotHearingSensor`, the only vanilla near-miss caller of
  SetUnderFire. Event gap 0.1 s (`Suppression.MinEventGap`; older notes say 0.05: superseded).
  [code, re-checked 2026-11-21]

## Movement and paths

- Never `BotOwner.GoToPoint`: it pathfinds over the cover-point graph and with `mustHaveWay = true`
  TELEPORTS the bot to a cover point on failure. Use `NavMesh.CalculatePath` -> `BotOwner.Mover.GoToByWay
  (corners, 0.5)`; re-issued only when the destination moves or every 3 s (`SurrenderLogic.RepathInterval`;
  older notes say 1 s: superseded). [code + 0.1.0 log]
- BotMover moves a bot by setting `transform.position` along the path (`CastFromPos`): the NavMesh is
  the only wall. Path start must be <= 0.5 m horizontal / 0.6 m vertical from the bot, end <= 1.5/1.2 m,
  complete paths only. `Relink()` resets the mover's stale `_prevSuccessLinkedFrom` before a new walk
  (BotMover snaps a walking bot back to it). [code]
- Doors: the game opens a door on a bot's path only if the driving logic calls
  `BotOwner.DoorOpener.UpdateDoorInteractionStatus` every moving frame (vanilla MoveNode.DoorOpen; SAIN
  has its own). A shut door doesn't cut the NavMesh (NavMeshDoorLink carves only for an open leaf).
  Our MoveTo / CaptorLogic call it and yield while it works; `Perception.PathTo` refuses legs that
  cross `PlayerStaticCollisionsMask` except openable doors. [code, 0.21.0]
- `Arrived` needs the same floor (<= 1.6 m vertical). [code]
- SAIN `SprintLookDirPatch` (prefix on `BotMover.Sprint`) turns a sprint into a walk when the bot
  looks > 20 deg off `RealDestPoint`, and refuses while SAIN layers are active: our sprints look along
  the move direction every frame. [code]
- Heavy searches (FleePoint, CoverPoint, weapon sweep) are budgeted: `Perception.TakeHeavy` (2 per
  frame across bots). [code]

## Rotation and turning

- Root vs look: a bot's root turns only when look yaw minus root yaw (`HandsToBodyAngle`) exceeds
  `TrunkRotationLimit` (EFTHardSettings.HANDS_TO_BODY_MAX_ANGLE 45). Below it
  `PlayerAnimator.SetDeltaRotation` twists the animated trunk toward the look; past it
  `MovementState.ProcessUpperbodyRotation` (AI branch) SNAPS the root to exactly look -/+ limit.
  `BotSteering` keeps steering to the last LookToPoint every fixed update. [code, 0.15/0.22]
- Stepping turn: player_anim_controller goes Idle_Aim -> Turn_Left/Right_90_Aim when DeltaRotation
  passes +-45; the clip's root motion turns the root. Bots are snapped at 45 so it never plays unless
  the bot's own TrunkRotationLimit is raised (TurnDriver: 80 during a step, ratcheted back, restored on
  every exit). [code + UnityPy; log 0.24.0: "stepped round 133 deg with the game's turn animation"]
- `MovementContext.ApplyRotation` is the game's own root call (used by TurnDriver, Facing.FaceRoot,
  Kneel.HoldRoot). [compiled]

## Head free-look

- `BotOwner.UpdateManual -> BotHeadData.ManualUpdate -> Player.Look(withReturn false)`, +-40 deg,
  towards BestObservedEnemy or +-25 patrol glances, applied in VisualPass by `PlayerBones.RotateHead`.
  `HeadFreeLookPatch` centres it while HandsUp; the head is then set absolutely (Presence). [code]

## Spawning scenario bots (debug sandbox)

- `BotCreationData.Create(GetProfileDataParams(side, role, difficulty, ...), spawner._botCreator, 1,
  spawner)`, `IBotCreator.ActivateBot(...)`; `_inSpawnProcess` must be given back when ActivateBot
  finishes without calling back. [code; scenarios have spawned in every 2026-11-20/21 log]
- SPT's server sends Bear/Usec bots with Info.Side "Savage"; `GetProfileDataParams.ChooseProfile`
  needs side+role+difficulty equal, and SPT's `PmcBotSidePatch` sets Bear/Usec after the choice: ask
  for Savage for pmcBEAR/pmcUSEC (`ScenarioSpawner.RequestSide`). [code + log 0.13.1 fix]
- `BotsController.Init` builds `_botSpawner` even with bots off; whether SPT returns profiles in such
  a raid: unverified.
- Magazine checks at spawn: vanilla `BotMagazineChecker._nextPosibleCheckMagazine` starts 0, SAIN
  `CheckMagTimer/NextCheckTimer` start 0; `SpawnQuiet` schedules them as if just checked, for
  scenario bots ONLY (Joel's scope). [code]
- ORBIT Ghost Mode sleeps bots > 260 m away; it refuses sleep only while GoalEnemy != null or under
  fire, so a calm captor could be ghosted mid-custody (see integrations.md). [code ORBIT 3.1.1]

## Perception primitives and pressure inputs

- Sight: `Physics.Linecast` with `LayersMaskController.HighPolyWithTerrainMask` (hitboxes are not in
  it); beyond 25 m a held bot also needs his `EnemyInfo.IsVisible` (or seen < 1 s): `Perception.Sees`.
  Bot captors recognise via line + IsVisible, line alone <= 6 m (`Perception.Recognises`). [code]
- Crosshair targeting (debug keys, menu): `Physics.RaycastNonAlloc` from the eye along `LookDirection`,
  mask HighPolyWithTerrain | HitCollider | Player, sorted, own colliders skipped; world first = no
  target; 0.25 m sphere-cast fallback only with a clear line (`Targeting.cs`). [code]
- Natural-surrender inputs: `Memory.GoalEnemy` (IsVisible, PersonalLastSeenTime), `EnemiesController
  .EnemyInfos`, `BotsGroup.MembersCount/Member(i)`, `WeaponManager.HaveBullets`, `Reload.CanReload(false)`,
  `HealthController.GetBodyPartHealth(Common)`; experience `Profile.Info.Level` and
  `BotOwner.Settings._difficulty` (BotDifficulty; Scavs: difficulty only since 0.19.0). [compiled]

## Roles, squads and deaths (0.29.0, Assembly-CSharp 0.16.9)

- `WildSpawnTypeExtension._spawnTypeSettings` marks `pmcBot` (Raiders), `exUsec`, `arenaFighter`,
  `pmcBEAR/USEC`, `shooterBTR` isBoss AND isFollower, so `IsBossOrFollower()` is true for Raiders: any
  "boss" filter built on it swallows them. `IsPureBoss()` = isBoss && !isFollower && !sectant (real
  bosses, plus `bossBoarSniper` whose ScavRoleKey is "ScavRole/Follower", `gifter`, `infectedTagilla`,
  `bossTest`). Knight's BigPipe/BirdEye and followerZryachiy are isBoss+isFollower (not pure). [code;
  every value printed by tools/bosstest]
- Brains: `pmcBot` (and arenaFighterEvent) -> `PmcLayersStrategy`, ShortName "PMC" (in our always-on
  `Brains` list); `exUsec` -> "ExUsec" (no layer of ours). `StandartBotBrain` switch. [code]
- `BotsGroup.RemoveAlly` is called from `BotsList.Remove`, on death AND despawn; `MembersCount` can't
  tell them apart. `BotOwner.Boss` (BotBoss) `.Followers` lists followers; `OnBossDead` exists. [code]
- `EnemyInfo.HaveSeenPersonal` is set on any sighting (SetVisible, any visible type); `PersonalLastPos`
  only on `EEnemyPartVisibleType.Visible`, so it can be Vector3.zero while HaveSeenPersonal is true.
  `EnemyLastPosition` = group's last position (BotsGroup.LoseVision / SetEnemyPos). [code; log 0.29.0:
  'believes him 450 m' on Labs]
- `Player.OnDead` invokes `OnPlayerDead`, static `Player.OnPlayerDeadStatic(Player, IPlayer, DamageInfo,
  EBodyPart)`, then `OnPlayerDeadOrUnspawn`; the dispose/unspawn path invokes only
  `OnPlayerDeadOrUnspawn`. So `OnPlayerDeadStatic` = died, never "left". Used by `BossSquad`. [code]
