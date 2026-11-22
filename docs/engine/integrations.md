# Installed AI mods and optional integrations

Provenance: source/decompile reads 2026-11-18..21 (`docs/history/`). Labels: see [README.md](README.md).
Code: `SainAdapter.cs`, `SainTargetFilter.cs`, `FastAccess.cs`, `Compat.cs` (OrbitAdapter,
LootingBotsAdapter, Teammates), `HeadFreeLook.cs`, `WeaponState.cs`.

## Dependency policy

- Only hard dependency: BigBrain (`[BepInDependency("xyz.drakia.bigbrain", "1.5.0")]`). Everything
  else (SAIN, ORBIT, Looting Bots, Pit Fireteam) is found by reflection at runtime and optional.
- Never add `BepInIncompatibility`; never require removing another mod. The mod steps aside for
  installed ones (Joel's standing rule).
- Joel runs SAIN and ORBIT 3.1.1, NOT Looting Bots or Questing Bots. No Fika support: decisions would
  have to run on the host and the arm pose would need a synced flag (unbuilt, unverified).

## BigBrain 1.5.0

- Custom layers cannot be unregistered; attach at brain activation. Hot reload: empty the
  registries so old layers go inert (see `HotReloadTeardown`). `CustomLayerWrapper.Name()` =
  `GetName()`; `BrainManager.GetActiveLayerName(BotOwner)` reports our layers ("Mercy_Surrender",
  "Mercy_Captor", "Mercy_KeepAway"). [code + log]

## SAIN 4.5.1 (SPT 4.1.3, EFT 40743; source = installed build)

- Layers: combat solo 20 / squad 22 / extract 24, AvoidThreat 80, debug 99.
- `BotComponent.ActiveLayer` public setter (-> SAINActivationClass.SetActiveLayer): we clear it while
  holding a bot (see ai-and-movement.md). [code + log]
- Own target `SAINEnemyController._goalEnemy`; `ChooseEnemy` patched late (SAIN found at raid start)
  through the plugin's Harmony instance; GoalEnemy setter is private. [code + log "SAIN target filter on"]
- `SAINExternal.GetPersonality(BotOwner)` (EPersonality), `GetBotComponent` (private),
  `BotComponent.Suppression.CurrentState` (ESuppressionState). `TimeSinceSenseEnemy` uses SAIN's own
  GoalEnemy (may be the prisoner): not used. Reads go through FastAccess compiled readers, verified
  against reflection on the first 8 reads (fallback on mismatch). tools/perftest: 28 checks on the
  installed SAIN.dll. [code + .NET test; Mono cost unmeasured]
- Disables vanilla hearing (`HearingSensorPatch`); swallows BotTalk/Player.Say; sprint->walk patch;
  melee charge on IsMelee; grenade rules; decision order (see ai-and-movement.md).
- Preset in use: "Default [Modified]" (BaseSAINDifficulty hard; VisibleDist 0.7, GainSight 0.95,
  Scattering 0.75, HearingDistance 0.55, Aggression 0.85, GlobalAggression 0.9, AI-vs-AI limits on).
  Not changed by us. Scenario bots spawn BotDifficulty.normal.

## ORBIT 3.1.1 (Joel's; main is 3.2.1)

- Layer `OrbitBrainLayer` 19; its agent drives a bot only while its layer is current. Ghost Mode
  sleeps bots > 260 m. `OrbitBotControl.SetBotSleepAllowed(BotOwner, bool, string)` (from 3.1.1)
  blocks sleep for the bot's ORBIT squad too; false on a ghost wakes it. We keep captor + prisoner
  awake during custody (`OrbitAdapter.KeepAwake`), released on end/raid change/teardown. [code; log
  "ORBIT found: bot-held encounters can be kept awake"]
- ORBIT keeps its own objectives; this mod never stashes or restores them. A released bot was
  walked straight back to his pre-capture objective (0.24.0 log #6 +84 s; 0.25.0 log #1: handed back
  9 m away walking off, 3 s later "drifting back ... OrbitBrainLayer", three keep-away legs each
  undone, CIRCLED BACK at +46 s). [log]
- 0.26.0, ORBIT 3.1.1 objective internals (decompiled from the installed ORBIT.dll; every member
  checked by reflection on that DLL, 2026-11-21): no public API drops an objective
  (`OrbitBotControl` = Exclude/Enable/SleepAllowed only). `Singleton<Orbit.Core.OrbitManager>.Instance`
  (Comfort `Singleton<T>.Instance` is a static property) `.AgentData.GetByProfileId(string)` -> `Agent`
  (`IsActive`, `Squad`, readonly `Objective`). `Objective.Location` / `SplinterParent` (`Waypoint`:
  field `Position`, property `Id`), `Status` (`ObjectiveStatus.None`). `Squad.Objective`
  (`SquadObjective.Location`), `CompletedPoiIds` (HashSet<int>, skipped by every waypoint pick),
  `Leader`, `Size`. ORBIT's own `GotoObjectiveAction.AbandonStalledObjective` drops a stuck objective
  exactly so (CompletedPoiIds.Add, matching squad Location = null, agent Location/SplinterParent =
  null, Status None); `GotoObjectiveStrategy` then calls `AssignNewObjective` (RequestNear his own
  position) for a squad whose Location is null. `OrbitObjectives.RetireIfBack` mirrors it for a
  released man whose objective leads back to the capture (leaders / solo only). [decompile +
  reflection; effect in game unverified]
- Whether ORBIT's sleep shows as `BotState != Active` or an inactive GameObject: unverified (both
  handled).

## Pit Fireteam 1.1.2 (source read 2026-11-19; not in H:\SPT4.1.X\BepInEx\plugins as of 2026-11-21)

- Follower = `BotFollower.BossToFollow` is `pitAIBossPlayer`; its group `AddAlly(real player)`.
  Talks via `BotTalk.TrySay`. Followers never surrender and never become guards
  (`Teammates.IsPlayerTeammate`). [code]

## Looting Bots (not in H:\SPT4.1.X\BepInEx\plugins as of 2026-11-21; API unverified)

- `PreventBotFromLooting(BotOwner, float)` is called only on an exact signature match, for captors
  while they guard or fetch (`LootingBotsAdapter`). Never run against the real mod.

## Other installed mods that mattered once

- `HandsAreNotBusy.dll` is also installed in H:\SPT4.1.X (another author's; not referenced by us).
