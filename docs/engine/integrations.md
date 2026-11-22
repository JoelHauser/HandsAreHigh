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

## COMET 1.0.0 (Chazut, github.com/Chazut/COMET tag 1.0.0 = 9e85438; read 2026-11-22)

Installed in H:\SPT4.1.X (COMET.dll + Comet.Server.dll hash-identical to the release zip), not yet run
in a raid with Mercy. Requires SAIN 4.5.1 (`BepInDependency me.sol.sain 4.5.1`, refuses a modified SAIN)
and SPT ~4.1.0 (server mod range; README: 4.1.6). Installed: SPT 4.1.6, SAIN 4.5.1, ORBIT 3.2.1 (soft
dependency of COMET, its Ghost API = ORBIT sleep). Pit Fireteam is not installed: not checked.
Applies to PMCs and AI player scavs only (BotEligibility: pmcUSEC/pmcBEAR, or assault with
MainProfileNickname / " (" nickname); ordinary Scavs, bosses, Raiders keep plain SAIN. No surrender
feature of its own. [code]

- Layers unchanged from RC1: Crossfire 21, PostCombat 21, MagazineRecovery 20. Ours (Surrender 95,
  KeepAway 72, Captor 70) outrank them. COMET classifies our layers as owner "other" (ControlHandoff)
  and releases its own paths when one takes over. [code]
- Target choice changed since RC1: SelectEnemy is now a transpiler (A07 threat pick routed through
  SAIN's CheckShallDogFight) and CheckShallDogFight gets a close-fight retarget (CloseFightRetarget).
  Both run inside ChooseEnemy over the lists our SainTargetFilter prefix has already filtered; COMET's
  ThreatAccepted postfix on ChooseEnemy releases its pick whenever the result isn't it, so our
  Postfix nulling a protected result works in either postfix order. A held SAIN `_lastDogFightTarget`
  can still return the prisoner; the result check drops it (native SAIN has the same field). [code]
- Weapon logic (WeaponReturn prefixes on TryChangeToSlot/TryChangeWeapon/TryChangeToMain/ManualUpdate/
  ShallChangeIfNoAmmo, PistolSwap) is gated on SAINLayersActive (off while we hold a bot). For
  released men with SAIN back: AuthorizeWeaponSwitch only acts on COMET's own requests (Intent set);
  ChooseWeaponFallback returns to native when the hands hold no firearm (frame null), which is the
  only case WeaponState.MaybeChange calls TryChangeWeapon (empty hands or knife). [code]
- Shared patch targets: BotWeaponManager.HaveBullets (both postfixes only turn true into false:
  order-free); BotMemory.SetUnderFire (COMET's Priority.First prefix skips the original only for a
  destroyed/replaced bot or source; our postfix still runs). [code]
- Confirmed conflict, fixed in 0.31.7: movement recovery C06 (MovementRecoveryAdapter) samples every
  COMET bot incl. "external-mover" (BotMover, which our layers drive with GoToByWay) and after 0.6 m
  without progress asks vault (1 s), RecalcWay (3 s), jump (5 s), teleport (8 s, off by default),
  Mover.Stop (14 s). Its pauses (prone, meds, busy hands, doors, shooting) don't cover a walking
  prisoner/captor. CometAdapter (Compat.cs) postfixes `Plugin.MovementRecoveryScope(BotComponent)`
  to false while the bot's active layer starts with "Mercy_" (live read: every exit path hands back),
  gated on SurrenderRegistry/Captors/Escape/Scenarios activity. COMET's own out-of-scope branch
  (EndMovementRecovery) resets its tracking. Lookup replayed on the installed DLLs (reflection-only
  load): all members found. [code; verified in raid 2026-11-22: two COMET escape runs and two COMET captor
  walk-ups with no recovery action]
- COMET's own firing (TacticalSteeringFire.TryShoot) comes from its layers (outranked) or adapters
  gated on SAINLayersActive, and aims at SAIN's GoalEnemy (filtered); the trigger guard stays the last
  line. ImpactReaction/ExplosionAwareness add the shooter as an enemy on a hit, as the game's GetHit
  does. MagazineRecovery only picks up magazines the bot itself dropped. PostCombat 1.0 seeks cover
  after combat even without a remembered threat (NeedsCover = !Protected): a released man under it
  after KeepAway ends is COMET's normal AI. [code]
- Needs a raid with COMET: the "COMET 1.0.0 found" log line, prisoner hands while held, captor target
  switch after a surrender, a walking prisoner/captor never vaulting or stopped by COMET, released men
  under PostCombat, Raid Review events.
