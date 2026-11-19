# Hands Are High

SPT 4.1 client mod (BepInEx + BigBrain): a bot can surrender, drop the weapon in his hands and
put his hands up, then act on his own: break when you give him an opening, retreat when you
leave, and come back armed later. Repo https://github.com/JoelHauser/HandsAreHigh.

## Status

**0.10.0 (debug scenarios), built against `C:\HUH` 2026-11-19 (dist/HandsAreHigh-0.10.0.zip),
0 warnings; committed and pushed to `main` 2026-11-19; not installed, never run in game.**
0.5.0-0.10.0 are all unverified in raid. No GitHub release.

Next: Joel runs the 0.10.0 scenario test plan first (it makes the 0.5.0-0.9.0 plans quick to
reproduce: most of their steps are presets now), then the older plans. Everything below 0.10.0 unchanged in behaviour
except: every roll goes through `Dice` (UnityEngine.Random unless a scenario seed is set),
scenario participants carry `Nickname#S1-Tag` in all log lines, and the restraint patches /
threat loops also consult `ScenarioShield` (one bool when idle).

### 0.10.0 (debug scenario sandbox, src/HandsAreHigh/Scenarios/)

F12 > 17. Debug Scenarios: "Scenario tools" (off) + "Scenario menu key" (unbound). Off = Runtime
never calls into it. Menu: arrows move/change, Enter activates (configurable); InputGuard claims
input while open (hotbar + wheel blocked, keys latched); order menu suppressed while open.
- Spawn (verified in the 0.16.9 decompile, never run): `BotCreationData.Create(GetProfileDataParams
  (side, role, normal, 0, BotSpawnParams{ShallBeGroup(false,false)}), spawner._botCreator, 1, spawner)`,
  `AddPosition(spot, nearest AICorePoint id)`, `_inSpawnProcess++`, `IBotCreator.ActivateBot(data,
  GetClosestZone, false, groupAction, bot => spawner.ActivateBotCallback(...))`. groupAction = a new
  BotsGroup built like GetGroupAndSetEnemies (enemies, SetBotAsEnemy, Groups.AddNoKey) for a
  squad's first member, the same group (+SetBotAsEnemy) after; members requested one at a time.
  Despawn: `BotLeaveData.RemoveFromMap` for living scenario bots only; corpses left.
- Placement before any spawn: NavMesh sample 1.5 m, same level (1.6 m), ground ray, standing
  capsule clear (0.35 m), 1.6 m from others/any player, complete path from the anchor (<= 3x+15 m);
  rings to 6 m; a launch places everyone or spawns nobody. Anchor ahead (distance) or crosshair.
- Stages Placing -> Spawning (setup truce between participants, observer override if on) ->
  Setup (preset steps with timeouts; Fatal ones mark SETUP INCOMPLETE) -> truce lifted (except
  pairs a preset keeps: FORCED) -> Running (scheduled events) -> cleanup.
- Forced transitions: `SurrenderRegistry.Surrender(..., decideNow)` (false skips the bot captor's
  roll), `Captors.ForceAccept` (the real Accept minus decision/perception checks; hard limits
  kept), `Captors.ForceGlance`, existing DebugOpening/ForceRun/ForceGrab/Leave, gesture pending
  windows. Synthetic suppression `Suppression.Synthetic` (labelled SIMULATED).
- Shield (ScenarioShield.cs): (shooter, target) pairs, Observer (participant -> you; also the
  EnemyInfo ignore flag) and Truce (no flag). Same GoalEnemy/ShootData/trigger hooks as restraint;
  excluded from captor threat, OtherThreat, CountHostiles. Hits through it: SCENARIO SHIELD LEAK.
- Timeline: a BepInEx ILogListener (only while a scenario runs) keeps our own log lines naming a
  participant, classified choice / no action / not eligible / EXECUTION FAILED / event; plus
  scenario notes (FORCED / SIMULATED / real). Participants' surrender checks are always logged.
- Logging: every scenario note goes to the log as it happens (`Scenario #N +12.3s [kind]: ...`;
  EXECUTION FAILED as warnings; exact repeats every 10th), except lines the timeline took from our
  own log. Logged: launch + params, refusals, spawn requested/arrived (time, distance from the
  validated spot, group, state), setup steps done/failed, truce/override changes, scheduled events
  firing, menu open/close and events fired, deaths, custody and recovery-target changes, cleanup
  report (items, despawns, shield blocks, full timeline), listener attach/detach. Verbose adds
  shield pairs, profile timing, facing. Listener and condition exceptions are reported, not swallowed.
- Cleanup: shield pairs lifted; tracked items (all kit slots at spawn + dropped gun) removed only
  if loose (`GameWorld.DestroyLoot`), reported if in your inventory or on anyone else; living
  scenario bots discharged from the registry then despawned; report + timeline to the log. Raid
  change forgets; teardown cleans up in raid; tools turned off cleans up.
- Seed: per-participant nerve/reaction/disposition from the seed (order independent); Dice
  seeded for the mod's other rolls while that scenario runs. The game is not deterministic.
- Presets (17): Player vs isolated Scav; Player vs PMC; Matched Scav and PMC; PMC captures Scav;
  PMC squad captures a bot; Captor comes under fire; Captor reloads or looks away; Nearby dropped
  weapon; Missing or blocked weapon; Armed vs unarmed prisoner; Captor dies; Third-party observer;
  Long-range surrender; Pit Fireteam teammate capture (needs an existing follower, refuses
  otherwise); Commands, speech and gestures; Death during surrender or recovery; Multiple
  simultaneous encounters. Not supported: relocating a dropped gun or blocking its path on demand.
- Offline checks done (DLL loaded in PowerShell 5.1): 17 presets build, every param wraps/clamps,
  log classification on sample lines. Can't run offline (Unity ECalls): Dice, timeline, all game code.

### In-game test plan (0.10.0)

Scenario tools on, bind a menu key, Log decisions/restraint on. Grep `Scenario #`, `#S1-`,
`SETUP INCOMPLETE`, `SCENARIO SHIELD LEAK`, `Scenario shield:`, `report`.
1. Each preset once on Customs/Factory: spawn line per participant, no `spawn failed`, nobody in
   a wall or on another floor, group ids shared within a squad only; Clean up: `removed
   (BotLeaveData.RemoveFromMap)`, gun `removed from the ground`.
2. Launch/clean up 5x: participant/ shield counts back to 0 (Profile line), no duplicate timeline
   lines, no errors; F12 off mid-scenario cleans up.
3. Menu open, press 1-0 and wheel: no hotbar use; close, hotbar works.
4. Natural Scav preset: overlay `last check` updates every ~5 s; shoot past him: fire rises.
5. Observer override on: participants never target you; off: they do.
6. Take a scenario gun into your inventory, clean up: reported `IN YOUR INVENTORY`, still yours.

**0.9.0, built on the work laptop against `C:\HUH` 2026-11-19 (dist/HandsAreHigh-0.9.0.zip),
0 warnings; committed and pushed to `main` 2026-11-19; not installed, never run in game.**
(0.7.0 + 0.8.0 were one commit before it.) 0.5.0-0.9.0 are all unverified in raid. No GitHub release.
`C:\HUH` (EFT 0.16.9.40743) has no SAIN, ORBIT, Pit Fireteam or BigBrain (compiled against the
scratchpad BigBrain 1.5.0).

Next: Joel runs the 0.9.0 plan, then 0.8.0, 0.7.0, 0.5.0 + 0.6.0 with Profile on. Open ideas (not
built): a "converted" bot that stays friendly for the raid; a bot captor's own Stop demand
before a surrender (today the Demand line plays once he takes custody).

### 0.9.0 (custody that weakens, distant surrender, encounter speech)

Sources read 2026-11-19 (scratchpad clones): SAIN master = v4.5.1 (SPT 4.1.3, EFT 40743 = C:\HUH);
Pit Fireteam main = 1.1.2 (SPT 4.1.x, addon needs SAIN 4.5.1); ORBIT tag 3.1.1 (Joel's), main 3.2.1.
- SAIN 4.5.1: layers combat solo 20 / squad 22 / extract 24, AvoidThreat 80. BotComponent.ActiveLayer
  still has a public setter (-> SAINActivationClass.SetActiveLayer): ReleaseLayer still works.
  Suppression.CurrentState (ESuppressionState) unchanged. SAINExternal: GetPersonality,
  TimeSinceSenseEnemy (SAIN's own GoalEnemy, may be the prisoner: not used), IsBotInCombat private.
  Talk: PlayerComponent.PlayVoiceLine = Speaker.Play, refuses while Speaking/Busy; CanTalk needs a
  human within 150 m. Bot/Player talk patches swallow all but hurt/death/breath.
- Pit Fireteam 1.1.2: follower layers 71 patrol, 72 combat, 73 request, 74/75 SAIN addon, 76 wire,
  101 tripwire, 65 friendly encounter, on PMC + Scav brains. Follower = BotFollower.BossToFollow is
  pitAIBossPlayer; its group AddAlly(real player). Talks via BotTalk.TrySay. Knows nothing of us.
- ORBIT 3.1.1: layer 19. Ghost sleep refused only while GoalEnemy != null or IsUnderFire, so a calm
  captor could be ghosted mid-custody. OrbitBotControl.SetBotSleepAllowed(BotOwner, bool, string)
  exists from 3.1.1 (blocks the bot's ORBIT squad too; false on a ghost wakes it).
- Game: BotOwner.UpdateManual runs LookSensor regardless of brain, so a held bot's EnemyInfo
  IsVisible keeps updating. EnemyInfo.PersonalLastPos / EnemyLastPosition = last known position.
  The bundle bank "Attention" is EPhraseTrigger.Look (35); Gogogo has no bank in any voice.

Changes:
- Range audit. Removed: "Within (m)" (30 m to a player) and "Gives up to a bot within (m)" (25 m).
  The killer was "Captor gone beyond (m)" 25: a surrender at 26-30 m was cancelled after the
  reaction time. Now leaving = seen beyond max(25, closest approach + 12 m "walks away by").
  Kept (justified): order ranges 3.5 m / 15 m, Stop demand 20 m, recapture 12 m, squad covers
  25 m, observer scan 50 m, AcceptRange (custody only, max now 80).
- Natural surrender: any range; threat perceived = in sight, seen <= SeenWithin, or under fire
  (our near misses / IsUnderFire) while he saw him within EnemyMemory. Gives up towards
  Perception.KnownPosition (exact if visible, else PersonalLastPos), logged. Player teammates
  (IsAlly or Pit Fireteam follower) skip checks unless "Your teammates can surrender".
- Offer: SurrenderState.Accepted. Human: within 25 m in sight at the surrender (as before), later
  within "acknowledged within", covering him ~0.8 s, or an order. Bot: custody. Unanswered lasts
  15-35 s x (1.2 - 0.4 nerve), +8 s each time the captor closes 3 m, then DecideOnLeave ("nobody
  came" / "never took him prisoner"). Out of sight while unanswered: 2.5 x LeaveSeconds.
- Held bot sight: Perception.Sees = clear line and, beyond 25 m, his EnemyInfo IsVisible (or seen
  < 1 s). Faces / flees / hides relative to the known position only.
- Bot captor recognition: Perception.Recognises (line + EnemyInfo IsVisible, line alone <= 6 m).
  Unseen: Captors.Pending, checked every 0.5 s, decides when he sees it.
- Captor modes (Captors.cs header): Assess every 0.25 s -> Contact (another enemy visible) /
  Fire (IsUnderFire, near misses < 2.5 s, SAIN suppression >= Medium; direction = last near-miss
  origin blurred 10% of range, else last seen enemy) / Alert (enemy seen < 5 s). Guard -> Cover
  (Fire with a known direction: spot within 12 m hidden from it, complete path, prefers keeping
  the prisoner in sight; prisoner within 10 m with a path is ordered Follow, Stay on arrival; max
  2 moves, 10 s apart; 6 s limit; stuck 2 s -> Engage) -> Engage (Contact, or Fire with no cover;
  layer off, dialogue interrupted, follow cancelled, one hand-over try to a practical squadmate;
  >= 3 s, back to Guard after 2.5 s quiet; > BusyRelease 12 s: custody given up). Glances every
  6-14 s (2-4 s when alert) for 0.8-1.8 s; no order during a glance. Custody ending (not a
  release) makes the offer unanswered again (0.6 x offer). Restraint never lifted by custody loss.
- Prisoner openings: removed the captor's internal Busy and "nobody guards him"; added gunfire
  heard (captor shot < 1.5 s, aim off him) and a sprinting captor; aim angle/line as before.
- Voice: CaptorVoice.cs (Demand, Stay, Disarm, Follow, Leave, Warn, Resist, Threat/OnSix,
  UnderFire, TakeCover, HandOver, TakeOver, Abandon, LostSight), one pending line per bot, urgent
  lines skip the gap (3.5 s) and drop queued ones, waits for Speaking/Busy and for his prisoner;
  prisoner waits for his captor. Voice.VerifyLater logs audible / out of earshot / no audio.
- ORBIT: OrbitAdapter.KeepAwake for captor + prisoner on Accept, lifted on End/CaptorDown/hand-over,
  ReleaseAll on raid change and teardown. Follow path failures retried 1/s (was a solve per frame).
- New cfg: 06 "Captor walks away by (m)", "Surrender acknowledged within (m)", "Unanswered
  surrender lasts min/max (s)"; 15 "Captor line gap", "Captors take cover", "Captor cover search
  (m)", "Prisoner follows into cover", "Follows into cover within (m)", "Back to guarding after
  quiet (s)", "Fights at least (s)", "Captors look around"; 13 "Keep a bot-held encounter awake
  (ORBIT)", "Your teammates can surrender". Profile line adds unrecognised / ORBIT blocks.

### In-game test plan (0.9.0)

Debug keys, Log decisions/voice/restraint on. Grep `->` (captor modes), `acknowledged`,
`unanswered`, `never took`, `nobody came`, `hands`, `takes over`, `captor voice`, `says`.
1. PMC holds a Scav (Ctrl+`[`), then shoot near the PMC from 40 m: `Guard -> Cover` (or Engage)
   within ~0.5 s, he moves (no wall clipping), the Scav follows if close, `Cover -> Guard`, Stay.
2. Show yourself to the PMC: `-> Engage`, a free squadmate `takes over`; the Scav's log shows an
   opening (`shooting at someone else` / `looks .. away`); zero `RESTRAINT LEAK`.
3. Stay hidden 15 s: `Engage -> Guard (quiet ...)` after >= 3 s; no flip-flop lines.
4. Natural surrender at 10 m, 40 m, 120 m+ (pin a Scav with a DMR): `surrenders`, no `walked
   away` while you stand still; aim at him: `acknowledged (aims at him...)`; or wait:
   `nobody came for his surrender` after 15-35 s, then run/hide.
5. Fire at a Scav from cover he can't see: he gives up towards where he last saw you (log
   `believes him N m from where he is`), faces that spot, not you.
6. Bot-vs-bot at 60 m+: `hasn't seen X give up`, then `now sees` or the offer ends.
7. ORBIT installed: walk > 260 m from a bot-held encounter: both stay awake until custody ends;
   Verbose shows `ORBIT: sleep blocked / allowed again`.
8. Pit Fireteam: teammates never surrender; they obey orders next to a prisoner; no extra lines.
9. Profile on during 1-6: no `Profile spike:` from captors; unrecognised/ORBIT counts return to 0.

### 0.8.0 (bots take prisoners, restraint)

Before: surrender only to a human (`NaturalSurrender.Evaluate`: "his target isn't a human
player"); prisoner logic already took any `Player` captor.

- Trigger: a Scav/PMC may give up to the bot he's fighting (his GoalEnemy, seen or just seen,
  <= 25 m) if that bot is active, not held, and its group `IsEnemy(him)`; chance x 0.85 x
  (1 if the foe has a gun with rounds, else 0.35); role odds unchanged (Scav >> PMC, bosses
  off). Debug: Ctrl+`[` = the bot under the crosshair gives up to his current bot enemy.
- Captor decision (Captors.cs, once): practical = firearm in hands, rounds, no other enemy
  in sight, no incoming fire, prisoner in sight <= 20 m, < 4 bot encounters, not already
  guarding, not held, a Scav/PMC brain, not a player ally. Take (0.65 x mercy) / spare
  (0.75 x mercy, restraint without custody) / refuse. Busy or at a limit: a free squadmate
  may take him. Plans: disarm+release, hold+release, disarm+hold; never follow; hold 25-70 s.
  Busy (other enemy visible, fire) -> layer off at once, custody dropped after 12 s. Captor
  down -> a free squadmate in sight takes over, else custody ends and restraint stays.
- CaptorLayer (BigBrain, priority 70 on the same brains; below SAIN avoid-threat 80): stands,
  `Steering.LookToPoint(prisoner chest)`, SAIN layer flag released as for prisoners. Also
  drives idle squadmates watching (WatchUntil, 15 s). Player allies (`BotsGroup.IsAlly(main
  player)`) never get it.
- Orders.cs: one path for you and bots (`CanGive`: alive, HandsUp, range 3.5 m / 15 m, line
  clear, bot = his custody). Bot speech `CommandVoice.SpeakAs` is feedback only.
- Restraint.cs: (observer, prisoner) decision Respect / Ignore / LetGo / Reject, kept 120 s
  or until a change (runs -> re-decided once; goes for a gun / draws -> all Reject + Distrust
  for whoever saw). Made to hold by `EnemyInfo.SetIgnoreState(true)` (vanilla; BetterEnemy
  skips it; Khorovod event uses it), prefix on `BotMemory.GoalEnemy` setter, prefix on
  `ShootData.Shoot`, last-line prefix on `FirearmController.SetTriggerPressed` (aim within 6
  deg of a restrained prisoner). Blocks there and hits by a restrained bot log `RESTRAINT
  LEAK`; intentional refusals log `refuses` / `shot ... as decided`. Squad = captor's
  BotsGroup within 80 m, same frame as the decision; squadmate refusal 2% x (2 - mercy).
  Observers: one prisoner per frame, each every 0.5 s, bots within 50 m with an EnemyInfo
  for him that is IsVisible; busy -> Ignore; else 0.8 x mercy (0.7 when running). Mercy =
  personality table (Wreckless 0.55 .. Timmy 1.15; no SAIN: Scav 0.9, PMC 1, boss 0.6) x
  per-raid 0.75-1.25; not nerve. Your allies treat your prisoner as their squad's.
- Prisoner vs a bot captor (Decisions.cs): openings = no gun up, reloading, switching
  weapons, captor busy fighting, captor inactive (asleep), nobody guarding (spared),
  looking away, blocked line; no inventory check for bots; a squadmate (squad restraint,
  <= 25 m) covering him and seen by him counts as covered.
- Menu: only prisoners nobody guards (custody null); ordering one takes him over (logged).
  Menu keys now work while walking (BepInEx IsDown refused any other held key).
- Cleanup: prisoner leaves registry -> flags cleared, custody ended; observers pruned every
  30 s; raid end / teardown clear all. Pit Fireteam: not found anywhere; closest public
  match is pitAlex's Friendly PMC (no source); nothing integrated beyond IsAlly.

### In-game test plan (0.8.0)

Debug keys on, Log restraint decisions on. Grep `surrenders to him`, `Restraint:`, `holds`,
`stops holding`, `ordered`, `RESTRAINT LEAK`, `refuses`.
1. PMC vs Scav fight: Ctrl+`[` on the Scav. Log the PMC's decision; on take: `holds`, plan,
   `ordered Stay/DropWeapons by <PMC>` once each, `Restraint: <squadmates> holds fire (squad)`
   in the same second, nobody shoots him, then `let go by` and he runs.
2. Same with a PMC prisoner (any role), and with SAIN on: zero `RESTRAINT LEAK` lines.
3. Walk into the scene: the PMC turns on you (`busy with <you>`), the Scav gets an opening.
4. Kill the captor: `takes over` or `nobody takes over, restraint stays` - no squad volley.
5. `]` (opening) on the prisoner while held by a bot: he draws -> `refuses`/Reject for all,
   squad fights him; flight unarmed -> `lets go` mostly.
6. Natural: no debug keys, long PMC-vs-Scav fights; the Scav's check lines show `bot threat`.
7. Profile on: restraint entries / bot captors counts bounded; no spikes.

### 0.7.0 (order menu, hotbar separation, faster draws)

Built 2026-11-19, never committed on its own; in the 0.8.0 commit.

- Menu (OrderMenu.cs): bound to the bot it opened on (never redirected; closes with a note
  when he's down / not hands up / asleep / > range + 0.75 m / out of sight 0.35 s; quietly
  when you look > 40 deg away or a game screen shows the cursor). Numbered rows (1-9, 0
  closes), wheel / `.` `=` move, `/` confirms; one `Submit` path, once a frame, re-checks
  target + availability (nothing to drop, still dropping, already following / down).
  IMGUI on Repaint only; layout rebuilt only on change; Bender font if a legacy Font of it is
  loaded (Verbose logs which). Scale = height/1080 x Menu size, floor 0.8; right of the
  crosshair, else edge, else left, else shrunk, else under the bot (checked for 15
  resolutions x 4 sizes in a script, none covers the bot or clips).
- Hotbar (InputGuard.cs): prefix on `InputManager.DispatchInput` (39-byte IL, not inlined)
  removes commands before InputTree reaches any node. While the menu owns input: all of
  SelectKnife / SelectSecondaryWeapon / QuickSelectSecondaryWeapon / SelectFirst/SecondPrimary /
  SelectFastSlot4-0 / PressSlot4-0 + ScrollNext/Previous. Keys pressed while it owns input are
  latched; after close only the hotbar commands bound to that key (read from
  `SettingsManager.Control.Settings.GetAllKeyBindings()` at open) stay blocked until it's up
  2 frames + max(0.06 s, 2 fixed steps). Ownership re-claimed per frame, lapses after 2.
  Bindings never written.
- Draw (Arsenal.Draw): one request at a time (DrawPending + token), none while the drop is
  pending; refused draw retried every check for 1.5 s without a new roll; arms lower over
  0.22 s while the draw animation starts (was a 0.08 s snap); in Rearmed he faces you and the
  layer hands him to the AI when the game's draw callback says the weapon is ready (limit 3 s),
  so the first shot is the AI's own with a ready gun. Sidearm: the game's quickdraw
  (`QuickdrawWeaponFast/QuickdrawTime`, 1.5-2x draw speed) which the game only sets when a
  gun in hand is swapped for the holster. Primaries have no fast draw: their draw animation
  is the remaining delay. Decisions due between 0.25 s checks run that frame. Log:
  `draw timeline (...)`. Re-surrender refused while a draw is pending.

### In-game test plan (0.7.0)

1. Menu at 1080p, 1440p, ultrawide, Menu size 0.6 / 2: readable, beside the bot, not clipped.
2. Each number gives its order once (`ordered X` once); holding the key doesn't repeat.
3. With a weapon / meds on 1-0: pressing a menu number never switches/uses them; hold the
   number past the menu closing, release: nothing; press again: hotbar works. Unused numbers
   (8, 9) and unavailable rows do nothing in the hotbar. Release-type slot binding too.
4. Wheel moves the highlight without changing walk speed; `/` gives it.
5. Look away, walk off, shoot him, Tab / Esc while open: menu closes, hotbar works at once.
6. `]` on a bot with a sidearm only, then with a slung rifle: `draw timeline` (game quickdraw
   on the sidearm), no snap, he turns to you, no shot before `weapon ready`, no busy hands.

### 0.6.0 (middle finger, Scav babble, performance audit)

- Middle finger: **exists** (an earlier note said it didn't). `EInteraction.GetOffGesture`; the
  game calls it that itself (BotGesture: GESTUS_FUCK_TO_SHOOT, EBotEnemyCause.fuckGestus). TP
  clip `tp_gesture_04_getoff` (player_anim_controller.bundle, 1.23 s), decoded with UnityPy:
  left upper arm 44 deg, left palm 86 deg, left fingers 60-125 deg; no right finger moves,
  right arm <= 20 deg. Played via `MovementContext.SetInteractInHands` (the bot path:
  body animator `AnimatedInteractions`; no GlobalEventDispatcher.ShowGesture, so no bot
  receives it; LocalPlayer.OnAnimatedInteraction is empty). Game preconditions checked first:
  FirearmsAnimator.IsIdling, not sprinting, not UsedSimplifiedSkeleton (that branch plays a
  door push). ArmPose hands the left arm over (`GestureArm`, 0.15 s blend); end on
  `IsInteractionPlaying` false; interrupt with `ForceStopInteractions`. One roll per surrender
  3-10 s after settling, 5% x (1 + influence x (personality x (0.5 + nerve) - 1)), max 15%,
  cooldown 600 s in BotHistory, 8 s window, needs you watching him.
- Scav babble: non-urgent Scav lines draw from category + a babble pool (Joel: "the scavs can
  just babble").
- Performance (no profiling possible here; all from code reading):
  - Per-frame / per-shot allocations removed: closures in Runtime.Update, Registry.Tick
    (voice/gestures), SurrenderLogic.Update (also a `$"phase ..."` string per bot per frame),
    ShotPatch (per shot by anyone), UnderFirePatch; Suppression.Record built its message per
    near miss; `SurrenderRegistry.All` was IEnumerable (boxed enumerator per foreach);
    Voice line arrays and RemoveAll lambda; Gestures sphere-cast height array; Injury vitals
    array; NavMeshPath.corners read for reachability-only checks; FindLying two HashSets and
    lambdas per call.
  - NaturalSurrender.Tick: per frame per AI was a ProfileId-string dictionary lookup + role
    chain; now a Player-keyed lookup first, role cached, at most 2 full checks per frame.
    Non-roll check logs and "no surrender check" logs only with Verbose (they repeated every
    few seconds per bot in bot-vs-bot fights).
  - Reflection: SAIN component cached per bot (ReleaseLayer every 0.25 s per held bot,
    suppression per check), shared args array, personality read once per bot per raid,
    assembly scan at raid start (Prewarm), not at the first check.
  - Heavy searches (retreat/cover point = up to 12 path solves, 60 m weapon sweep) limited to
    2 per frame across bots (`Perception.TakeHeavy`): a witnessed shooting makes every held
    bot flee at once. Empty weapon sweep not repeated for 20 s (hiding bots swept every 8 s).
    Drop-gun reachability cached 3 s. Following re-paths at most 2x/s.
  - Loot: collider -> InteractableObject cached per raid (GetComponentInParent per collider
    per sweep before), buffer 256 -> 1024 (bodies have a dozen-plus colliders each; 256 could
    silently drop guns).
  - IMGUI: OnGUI moved to `RuntimeGui`, enabled only while a bot is held or a note shows;
    useGUILayout off. Nothing runs outside a raid (no GameWorld, or HideoutGameWorld).
  - JIT: all our methods pre-compiled in Awake (RuntimeHelpers.PrepareMethod; log line
    `Pre-compiled N methods`), so the first surrender/gesture/pick-up doesn't compile.
  - Death: `Player.LateUpdate` only calls VisualPass for the living, so the pose never ran on
    corpses; OnDead disables the animators then CreateCorpse makes the ragdoll. We now hook
    `Player.OnPlayerDeadOrUnspawn` per held bot (unhooked on leave) and flag the state Dead:
    pose, lean, gestures, speech, callbacks (`Current()`), claims stop that moment. No ragdoll
    code existed to remove; nothing touches colliders, rigidbodies or corpse bones. Dead bots'
    Player keys pruned every 30 s.
  - Profiler: F12 14. Debug > Profile (off; one bool when off). Logs every 10 s frame-time
    stats (avg/p50/p99/p99.9/worst/1% low), GC gen0 count, heap, per-hook cost, tracked counts;
    `Profile spike:` when our code takes > 1 ms in a frame.

### Measurement plan (Profile on, same map/time/settings; Enabled off = baseline)

Compare `Profile` lines with Enabled on vs off: (1) no held bots, 5 min; (2) one held;
(3) four held; (4) debug openings + scrambles/pick-ups; (5) wiggle/finger chance 1 + chatter;
(6) kill a held bot; (7) kill 3-4 close together; (8) stand by 10+ corpses with a hiding bot
nearby; (9) ORBIT ghost sleep/wake (walk > 260 m away and back); (10) three raids in a row and
a hot reload. Watch `Profile spike:` at first surrender, first gesture, first pick-up, deaths;
`history` / `loot cache` counts must stay bounded; GC gen0 rate should match the baseline.

### 0.5.0

### 0.5.0 (decisions, wiggle, speech, spoken orders)

- Decisions react to events after a per-bot reaction time (BotHistory.Reaction, 0.35-0.9 s,
  x nerve/injury, varied): opening appears (clear: gun down/reload/inventory = reaction only;
  gradual: look away/no line = + OpeningDelay), gets clearer, you come closer (ApproachStep),
  his dropped gun seen gone (only if he can see the spot), warning shot (near miss by a
  player: freezes IntimidationSeconds x nerve), order (complies OrderCompliance), you leave or
  die (DecideOnLeave: run / draw kept gun / get his gun / hide). While an opening lasts he
  reassesses every BreakInterval +-30%. Act chance per decision = BreakChance x nerve scale x
  opening strength x (1 + boost x own-gun quality) x squad (free allies near) x (1 - 0.5
  injury). Options weigh legs (destroyed leg), injury, allies, a reachable way out
  (RetreatPoint) and a hidden spot (new "hide" option). Logs: `takes an opening (...)`,
  `reacts`, `warning shot`, `sees his ... is gone`.
- Wiggle (Gestures.cs): game lean via MovementContext; a prefix on `MovementContext.SmoothTilt`
  sets `_tilt` for HandsUp bots (0, or the wiggle), because SAIN's LeanClass calls SetTilt
  every tick for bots with an enemy, layer or not. Sampled at two opportunities only
  (settled after surrender; you look back after an opening), max one roll per 20 s, cooldown
  90 s in BotHistory. Shoulder/head sphere casts both sides before starting.
- Middle finger: wrongly reported missing here; see 0.6.0 (it is GetOffGesture).
- Speech (Voice.cs): categories Surrender/Plead/Threatened/Approached/Acknowledge/Refuse/
  Released/Idle/Defiant/Break/Hurt, priorities, per-bot cooldowns, unprompted chatter every
  18-45 s (50%), nearby held bots take turns (25 m, 1.5 s gap). Availability per voice from
  `Speaker.PhrasesBanks`. After a line, the speech source is checked for the clip playing
  (`says ...: audible` / out of earshot / no audio).
- Spoken orders (CommandVoice.cs, off by default): `Player.Speaker.Play` (never Player.Say,
  so no SayPatch Stop demand and no GlobalEventDispatcher.SayPhrase to friendly bots).
- SainAdapter moved to its own file. Reference UnityEngine.AudioModule added.

### Voice lines (read from the phrase bundles with UnityPy, 2026-11-19)

All 19 scav_1-6 / bear_1-3, bear_1_eng, bear_2_eng, bear_vitaly / usec_1-6, usec_rob voices
have Stop, Negative, Roger, Going, DontKnow, NeedHelp, OnMutter, OnBeingHurt, HurtHeavy,
HurtNearDeath, OnFight, Fire, FollowMe, Look, Silence, Repeat. BEAR/USEC only: HoldFire,
Cooperation, Gogogo, HoldPosition, GetBack, OnYourOwn, Regroup, GoForward. Scav only: Toxic.
GetInCover: all but scav_6. None have Provocation, Greetings, Warning, MumblePhrase. A clip
mask 0 matches any tags (TagBank.Compare). The old Surrender line relied on its fallback for
Scavs (no HoldFire).

### 0.4.0

**0.4.0, built, installed in `H:\SPT4.1.X` and pushed to `main` 2026-11-18; not yet run in game.**
0.3.0 was never committed on its own; its changes are in the 0.4.0 commit.
0.3.0 was run by Joel 2026-11-18 (voice, reload cut, SAIN release, near misses all worked in
the log); 0.4.0 fixes what he reported after it. No GitHub release.

### 0.4.0 (after the 0.3.0 run)

- Walking through walls: BotMover moves a bot by setting `transform.position` along the path
  (`CastFromPos`), so the NavMesh is the only wall. `Perception.PathTo` snapped the path start
  up to 2 m away (could be past a thin wall or on another floor). Now start <= 0.5 m
  horizontal / 0.6 m vertical, end <= 1.5 m / 1.2 m, complete paths only, else no move
  (reassess). `Relink()` resets the mover's last-good-link fields to his real position before
  a new walk (BotMover.method_12 snaps a walking bot back to a stale `_prevSuccessLinkedFrom`).
  No teleport anywhere in our code.
- Arms "coming loose" while walking: the pose frame was world-up/body-yaw; now torso frame
  (shoulder line + ribcage->head) in `ArmPose.TorsoFrame`.
- Natural surrender never seen: the pipeline worked (checks, near misses, pinned/severe all in
  the log) but per-check chance was 0.01-0.09. Now per-role base/threshold/max and a pressure
  curve; replaying the 0.3.0 log: a pinned, bleeding, ammo-less Scav ~95% over his checks, a
  severely hurt Scav ~55%, a pinned PMC ~2%, a lightly pressed PMC 0%.
- Roles: Scav base 0.35 / needs 0.40 / max 0.50; PMC 0.12 / 0.55 / 0.25; bosses+followers off by
  default, else 0.03 / 0.75 / 0.08 (their brains registered only when enabled, restart).
- Stop demand (off by default): postfix on `Player.Say` for the player's own `Stop` (the
  gesture/phrase menu path: GamePlayerOwner.PlayPhraseOrGesture -> Player.Say), optional
  unbound key that calls the same Say. One bot under the crosshair, 20 m, hostile, clear line;
  one immediate check with +0.2 pressure (counts as a reason) and x1.5 chance; 8 s per-bot cooldown.
- Pressure weights renamed (`Weight: ...`) so new defaults apply over old cfg values.

### What the 0.2.0 log showed and what 0.3.0 changed (all confirmed in SAIN / EFT code)

- Retreats always "stuck" and bots jittery: SAIN only clears `BotComponent.ActiveLayer` from
  its own layer's IsActive(); once our layer wins BigBrain stops asking SAIN's layers, so the
  flag stays set. While set, SAIN's `BotMoverManualUpdatePatch`/`ManualFixedUpdatePatch` skip
  the game's mover (our path never walked) and `SmoothTurnPatch` keeps turning the bot itself
  (fighting our facing). Now `SainAdapter.ReleaseLayer` sets it to None (what
  `SAINLayer.SetLayer(false)` does), checked every 0.25 s while held. Also: facing target only
  moves past a 4 deg hysteresis and uses the captor's body, not his bobbing head bone; mover
  stopped once (not every frame); pose only set when it changes; path re-issued only when the
  destination moves or every 3 s.
- Reload before the drop: `BaseReloadOperation.HideWeapon` only stores the callback, the hide
  waits for the reload. Now the reload is finished instantly with the game's own
  `HandsController.FastForwardCurrentState()` (used by `BotWeaponManager.PreActivate`), whose
  `SwitchToIdlingState` then runs the stored hide. A magazine swap can't be aborted midway.
- Busy hands: drop retries stacked new hands changes on unanswered ones. Now one request at a
  time with a token (stale callbacks ignored), failure callback handled, a request abandoned
  only after 2x the retry time, 3 requests max; draw fallback only with empty hands.
- Suppression stuck near 0: SAIN's `HearingSensorPatch` disables `BotHearingSensor`, the only
  near-miss caller of `BotMemory.SetUnderFire`; only hits counted. Now near misses are measured
  geometrically from every shot (postfix on `Player.FirearmController.InitiateShot`).
- Severe injury couldn't qualify alone ("hurt 0.83" -> pressure 0.44, needs 2 reasons). Now an
  injury model (worst of health / head-thorax / destroyed limb / bleeding) and a severe-injury
  path, plus a pinned-down path.
- Kit value always 0k: `HandbookNodes`' dictionary base only holds top-level categories; items
  are in its node cache, read by the indexer. Now the indexer.
- Silent surrenders: SAIN's `BotTalkPatch` / `PlayerTalkPatch` swallow every phrase but
  hurt/agony/death/breath for SAIN bots. Now `Player.Speaker.Play` (what SAIN itself uses),
  with fallback lines and the result logged.
- Debug opening "stands still": the scramble was cancelled within 0.75 s because you kept
  aiming. Debug openings now ignore your aim until they end; real scrambles need a steadier aim
  to stop; force run / force grab keys added.

### 0.2.0 notes

Joel's direction (2026-11-18): standing, not crouched; drop **only** the weapon in hand; on
an opening a bot draws the gun slung on his back first, then his sidearm, and only uses a
knife when he carries no gun; he often scrambles for his own dropped gun when it's close;
left alone he retreats and later gets his own gun back or finds a new one; "extremely
dynamic". He runs SAIN + ORBIT 3.1.1, not Looting Bots or Questing Bots.

Not built (optional in the spec): reputation pressure, squadmates freeing a held bot, player
voice lines, unit tests. No Fika.

### What the first in-game log showed (0.1.0) and what 0.2.0 changed

- Debug opening / leave hit every held bot (they looped over the registry); the surrender
  key fell back to the nearest bot. Now one bot under the crosshair (Targeting.cs).
- Almost every Retreating / Recovering failed at once with "no path": `BotOwner.GoToPoint`
  pathfinds over the bot's cover-point graph (`BotPathFinderCorePoints.GoToPosition`), which
  failed for most targets, and with its default `mustHaveWay = true` it **teleports the bot to
  a cover point** on failure. Now our own NavMesh path through `BotMover.GoToByWay`.
- Draws refused ("the game refused the weapon change"): `TryChangeToSlot(slot, false)` for a
  holster only passes when `_canChangeToSupportWeapons`. Now `changeToMain: true`, falling
  back to `Player.SetSlotItem` + `selector.OnWeaponTaken` (what TryChangeToSlot itself calls).
- Drop took > 1.5 s (retries logged): full holster animation, polled every 0.25 s. Now the
  empty-hands `Player.Process` with `fastHide: true` and the arms react to its callback.
- "fire 0" always: suppression read only `Memory.IsUnderFire`. Now Suppression.cs.

## Build

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X [-Install]
```

On the work laptop (no H:): `-SPTPath C:\HUH -BigBrainDll <scratchpad>\bigbrain\DrakiaXYZ-BigBrain.dll`
(1.5.0, copied from an earlier session's scratchpad). Decompile with
`ilspycmd -p -o <dir> -r C:\HUH\EscapeFromTarkov_Data\Managed ...\Assembly-CSharp.dll` (~2 min);
an older obfuscated decompile (`_E180` names) in other scratchpads is a different build.

Run it from PowerShell. H:\SPT4.1.X has BigBrain; on an install without it pass
`-BigBrainDll <path>\DrakiaXYZ-BigBrain.dll` (BigBrain 1.5.0, never packed in our zip).
`C:\HUH` is the work-laptop install. Zip = `BepInEx/plugins/HandsAreHigh.dll` only.
pack.ps1 refuses config names containing = \ " ' [ ] (BepInEx throws in Awake and the whole
plugin dies silently; 0.0.2-0.1.0 shipped that bug). Existing section/key names must not be
renamed casually: Joel's cfg already holds values for them.

## Behaviour (one phase machine per bot, SurrenderLayer.cs)

Nerve 0-1 is rolled once per bot per raid (SurrenderRegistry history) and never re-rolled.
Order (none / follow / kneel) is separate from the phase: a following bot is still HandsUp.

- **Surrendering on his own** (NaturalSurrender.cs, header comment has the model). Scav/PMC
  only. Every ~5 s per bot, no catch-up. A human enemy within 30 m seen now or in the last 3 s.
  Pressure (weights): injury (0.35), out of ammo (0.30), suppression (0.35), outnumbered
  (0.20), squad lost or squad too far (0.30); armour dulls suppression + outnumbered (class 6:
  x0.7). Needs >= 2 reasons (each >= 0.15) and pressure >= 0.5. Chance = min(0.20, 0.08 x
  pressure x (1 - 0.85 x nerve) x role (PMC 0.65) x SAIN personality (0.5-1.5) x kit value
  (up to 1.25 at 250k handbook roubles) x experience (down to 0.65 at level 40+/impossible)).
  Every gated check logs every input.
- **HandsUp**: stops shooting/sprinting, fast-empties his hands (gun thrown, knife sheathed);
  arms rise on the game's hands-empty callback; asks again after 1 s, 3 tries then he fights.
  Logs a timeline once the arms are up: `Timeline from surrender: drop asked, hands empty
  (game holster), gun on the ground, arms up`.
  - Hit by anyone (`Player.BeingHitAction`; never bleeding) -> Retreating; witnesses run too.
  - Captor dead, seen beyond 25 m, or out of sight 6 s -> Retreating.
  - Not covered: noticed after his reaction time (+1.5 s if gradual), then a decision every
    ~1 s while it lasts (0.5.0 formula above). Success: **TakeOpening** weighs and logs every option
    (0.5.0 adds hide, legs, injury, allies, way-out check):
    draw (slung gun first, then sidearm, within 30 m), knife (only with no gun, within 4 m),
    scramble for his own dropped gun (within 10 m, reachable, unclaimed, worse the closer you
    are to it; halved if he has a gun to draw), run (not within 6 m); scaled by nerve and
    SAIN personality (fight vs flee), picked at random by weight.
- **Scramble**: arms down at once, sprints (Recovering with Scrambling), real pick-up
  transaction, then draws. Gone / stuck / 8 s / pick-up failed -> no second scramble for
  15 s; draws what he has or runs. Caught mid-scramble (0.75 s grace) -> hands up again.
- **Retreating**: NavMesh path to a point ~35 m from your last-known position, preferring
  hidden ones; sprints. 4 s grace. Stuck 3 s -> new route, 3 retries, then Hiding. Arrived or
  15 s -> Regrouping.
- **Regrouping**: watches your last position 4-12 s, then: draw a kept gun (slung first); own
  gun if still there, reachable and you were last seen > 20 m from it; else nearest reachable
  loose gun / gun on a body within 60 m, not claimed; else knife if nerve >= 0.75; else Hiding.
- **Recovering / PickingUp**: walks; gives up if gone, stuck, unreachable or 30 s; success only
  if the gun is then in one of his weapon slots, then draws. 5 s pick-up limit.
- **Hiding**: out of sight of your last position, crouches, looks again every 8 s (only the
  first hide after being caught is logged).
- **Caught again** (after grace): a human within 12 m aiming within 10 deg for 0.4 s. With a
  gun on him, 0.5 x nerve chance he draws instead.
- **Orders** (OrderMenu.cs): look at a held bot within 3 m, `/` opens, `.`/`=` choose, `/` gives.
- **Debug keys** (Runtime.cs): `[` surrender, `]` opening (6 s not covered, his own rolls),
  Backspace leave; each only the bot under the crosshair, logged as `Debug <cmd>: target ...`
  and shown on screen. No target: nothing happens.
- **Asleep** (ORBIT Ghost Mode): claim on a gun dropped, decides again on waking.

## In-game test plan (0.5.0)

Debug keys + Overlay on; Log decisions/gestures/voice on (default). Grep `takes an opening`,
`reacts`, `warning shot`, `wiggle`, `says`, `order `.

1. Two or three held bots (Override nerve off): reload, open inventory, look away, walk up,
   walk off. Choices differ by nerve/personality/kit; reactions come ~0.3-1 s after the change.
2. Fire past a held bot (miss): `warning shot ... freezes`; no opening taken until it ends.
3. Take his rifle while he watches: `sees his ... is gone`; scramble no longer offered.
4. Wiggle: Wiggle chance 1, cooldown 0 for the test: smooth lean L/R with hands up, back
   upright; never next to a wall (`skipped: ... wall`); shoot him mid-wiggle: `interrupted (hit)`.
   Then defaults: rare. Recapture straight away: no second wiggle (cooldown).
5. Speech: `says X: audible` lines; Scav and PMC; two held bots side by side take turns.
6. Speak your orders on: each order gives one `ordered X` line and one `order X: you say ...`
   (or silent); nearby friendly bots don't react; Stop demand not triggered by Drop/Stay.
7. SAIN: held bot stands upright (no SAIN lean); after rearm SAIN leans again.
8. F12 disable / hot reload while a bot wiggles: lean returns upright.

## In-game test plan (0.4.0)

No debug keys for 1-4 (debug keys off), Verbose logs on for 3.

1. Walls: surrender bots next to walls, doorways, stairs; give openings / force run / leave.
   Nobody passes through geometry; unreachable spots log `no route (...)` with Verbose on.
2. Arms: order "follow me" and walk; arms stay attached to the shoulders while he walks.
3. Natural: pin a lone Scav down with sustained fire near him, or wound him badly; within a few
   checks he should often give up (`-> surrenders`, then `surrender activated`). Same with a
   PMC: far rarer. Bosses never (default).
4. Stop demand: off by default (saying Stop does nothing extra). Turn on 10. Stop demand, aim at
   a hostile Scav within 20 m, say Stop from the phrase menu: `Stop demand: target ...:
   accepted, evaluating now`, then one `told to stop` check. Again within 8 s: cooldown.

## In-game test plan (0.3.0)

F12 > Hands Are High > 14. Debug: **Debug keys** and **Overlay** on, **Override nerve** off
(the 0.2.0 log had every bot at 0.50). Keys: `[` surrender, `]` opening, Backspace leave,
Ctrl+Backspace force run, Ctrl+`]` force gun grab. Grep LogOutput.log for `SAIN still had`,
`Timeline`, `voice`, `opening`, `scramble`, `pressure`, `fire +` (Verbose), `->`.

1. Surrender while he fires, sprints, reloads, heals, idles: Timeline line each time; when
   reloading, "mid-reload: the reload is finished instantly". No pose with a gun in hand.
2. A surrender plays a voice line (`voice Surrender: played ...`) for Scavs and PMCs.
3. Leave / force run: he actually runs (no "stuck" x3), no teleport, no jitter while held.
4. `]` with his gun 2-5 m away: scrambles often; keep aiming, he's not stopped by it.
   Ctrl+`]`: always goes for it (or says why not). Grab it yourself first: clean fallback.
5. Repeated surrender / escape / pickup / recapture on one bot: no "hands busy" lockups.
6. Sustained fire close to a bot vs one shot and vs shooting elsewhere (Verbose logs on).
7. Badly wound a bot (thorax, leg blacked, bleeding): `severely injured` checks appear.

## How it works (engine side, verified 2026-11-18)

The 0.16.9 client is de-obfuscated and the build treats warnings as errors, so every name
below compiled against H:\SPT4.1.X. Semantics read with ilspycmd (full decompile of
Assembly-CSharp in the session scratchpad; regenerate with `ilspycmd -p`).

- **Layer**: BigBrain `CustomLayer` on brains Assault, CursAssault, PMC, PmcBear, PmcUsec,
  priority 95. SAIN: combat 20-24, avoid threat 80, debug 99. ORBIT 3.1.1: `OrbitBrainLayer`
  at 19; its agent only drives a bot while its layer is current; Ghost Mode sleeps bots > 260 m.
- **Drop**: `new Player.Process<EmptyHandsController, IEmptyHandsController>(bot, factory, null,
  fastHide: true).Proceed(null, cb, true)` + `SetInHands(false)` (= SetEmptyHands with the fast
  hide `Player.Proceed(Weapon)` uses for quick switches); then `ThrowItem` from his slot.
  Throwing the item still in hands is wrong: `SetControllerInsteadRemovedOne` equips his next
  weapon. Remaining latency is the game's hide animation and the throw transaction.
- **Draw**: `TryChangeToSlot(slot, changeToMain: true)`; fallback `Player.SetSlotItem` +
  `BotWeaponSelector.OnWeaponTaken`. Melee: `ChangeToMelee()`.
- **Movement**: `NavMesh.CalculatePath` (both ends sampled) -> `BotOwner.Mover.GoToByWay(corners,
  0.5)`, re-issued every 1 s; `MovementResume`, `Sprint`. Never `BotOwner.GoToPoint`.
- **Suppression**: postfix on `BotMemory.SetUnderFire` (called by `GetHitInner` on hits and by
  `BotHearingSensor` for gunfire within BULLET_FEEL_DIST aimed at him); one event per 0.05 s,
  half-life 4 s, full at 6. SAIN: `SAINExternal.GetBotComponent` (private) ->
  `BotComponent.Suppression.CurrentState` (ESuppressionState None..Extreme), by reflection;
  the higher of the two counts.
- **Kit**: handbook price via `Singleton<Handbook>.Instance.StructuredItems.TryGetValue(templateId)`
  -> `Data.Price` x stack (not `GetBasePrice`: it logs an error per missing item;
  `ItemTemplate.CreditsPrice` is empty in SPT's items.json). Protection: best
  `ArmorComponent.ArmorClass` in vest/rig/helmet incl. plates. Cached per bot, 30 s.
- **Experience**: `Profile.Info.Level`, `BotOwner.Settings._difficulty` (BotDifficulty).
- **Pressure inputs**: `Memory.GoalEnemy` (IsVisible, PersonalLastSeenTime = Time.time),
  `EnemiesController.EnemyInfos`, `BotsGroup.MembersCount/Member(i)`, `WeaponManager.HaveBullets`,
  `Reload.CanReload(false)`, `HealthController.GetBodyPartHealth(Common)`.
- **SAIN personality**: `SAINExternal.GetPersonality(BotOwner)` (EPersonality names).
- **Targeting**: `Physics.RaycastNonAlloc` from the eye along `LookDirection`, mask
  HighPolyWithTerrain | HitCollider | Player, sorted, own colliders skipped; world first = no
  target; 0.25 m sphere cast fallback only with a clear line.
- **Voice**: `Player.Speaker.Play(trigger, BotTalk.GetMask(null), true)` (BaseSpeaker: returns the
  TagBank only once a clip was matched; OnPhraseTold -> Player.PlaySpeechFromTime plays it on
  `_speechSource` unless IsOutOfProtagonistsRange). `SpeechSource` getter creates a source, so
  the check reads `_speechSource` via FieldRefAccess. SpeakerManager.FreeToSpeak: group blockers.
- **Lean**: MovementContext.SetTilt stores `_tilt` (+-5); SmoothTilt (from ManualUpdate) eases
  SmoothedTilt -> animator. Animation only, no position or capsule change. Vanilla BotTilt +-5.
- **Suppression gap**: one event per 0.1 s per bot (not 0.05).
- **Finding guns / pick-up**: OverlapSphere on Interactive/Loot/Deadbody; `FindSlotToPickUp`,
  `ItemManipulator.Move`, `TryRunNetworkTransaction`.
- **Arm pose**: postfix on `Player.VisualPass`.
- Sight: `Physics.Linecast` with `HighPolyWithTerrainMask` (hitboxes are not in it).
- Hits: `Player.BeingHitAction`, one handler per held bot, removed on leave / new raid / teardown.
- **Hot reload**: BigBrain can't unregister a custom layer; teardown empties the registry (old
  layer goes inert), clears SAIN lookups, notes, error throttle, unpatches (incl. SetUnderFire).

## Known unknowns

- Whether `fastHide` visibly shortens the drop for every weapon (Timeline line will tell).
- Whether `BotMover.GoToByWay` keeps moving under SAIN while our layer owns the bot.
- Whether `BotTalk.Say` plays while our layer owns the bot, and which lines Scav voices have.
- Whether ORBIT's sleep shows as `BotState != Active` or an inactive GameObject (both handled).
- Forearm twist bones untouched; wrists may look twisted (`Forearm twist`).
- Whether the bot animator shows a lean at +-3 in the standing hands-up pose (BotTilt uses it).
- Whether Mono inlines MovementContext.SmoothTilt (would bypass the prefix; then SAIN can
  still lean held bots and the wiggle fights it).
- Phrase choice is by trigger name; the clips were not listened to.
- Fika: decisions must run on the host; the arm pose needs a synced flag.
