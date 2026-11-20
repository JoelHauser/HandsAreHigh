# Hands Are High

SPT 4.1 client mod (BepInEx + BigBrain): a bot can surrender, drop the weapon in his hands and
put his hands up, then act on his own: break when you give him an opening, retreat when you
leave, and come back armed later. Repo https://github.com/JoelHauser/HandsAreHigh.

## Status

**0.20.1 (2026-11-20): performance pass (from Joel's pasted 9-part performance spec). No gameplay,
balance or config change. Release packed (dist/HandsAreHigh-0.20.1.zip, DLL only) and installed in H:\SPT4.1.X
(0.20.0 DLL backed up in that session's scratchpad, backup-0.20.0). Committed and pushed to main
(no tag/release). NEVER PROFILED IN GAME: no Profile run exists for any version, so every cost below is by inspection, not measurement.**

Audit (inspection; nothing measured in game):
- Already cheap: every patch on an all-players path exits on one count (`SurrenderRegistry.Any`,
  `Restraint.Any`); natural checks staggered (2 per frame, no catch-up); observer scan one prisoner
  per frame, >= 0.5 s apart; captors 4 Hz; heavy path/spot work behind `Perception.TakeHeavy` with
  backoff and move caps; rigs cached per surrender with validity checks; NonAlloc physics; no LINQ,
  scene searches or closures on tick paths; errors rate-limited; GUI component off when idle;
  scenario log listener attached only while a scenario runs.
- Fixed (confirmed by inspection): (1) hot-reload teardown left `SurrenderRegistry.ByPlayer` (Player
  keys) and the loot cache (`Arsenal`: scene InteractableObjects, Items) referenced forever (an old
  assembly never unloads); (2) runtime reflection + enum boxing on recurring SAIN paths: owner read
  in the `ChooseEnemy` prefix (SAIN calls it every 0.1 s per bot: BotDecisionManager.ManualUpdate),
  layer release (0.25 s per held bot/captor), suppression (captor/guard threat 4 Hz, natural
  checks); (3) both layers' IsActive did lookups for every bot on every brain update with nobody
  held / no custody: now one count first; (4) `Presence` built the glance message (7 boxed floats)
  every glance per prisoner with Log attention off (the default); (5) Overwatch's 4 Hz coverage
  category used culture-aware StartsWith/EndsWith; (6) the profiler's "% of frame time" was ~100x
  too small (divided by 1000/avg instead of 1000), and Captors/Restraint/Voice were booked under
  Natural.
- FastAccess.cs: DynamicMethod readers for SAIN members; the first 8 reads of each are also made by
  reflection and compared; any exception/disagreement drops the compiled reader for good (one
  warning) and reflection is used as before. tools/perftest (`dotnet run -c Release -- sain
  H:\SPT4.1.X`): 28 checks pass, including every reader resolved from the installed SAIN.dll and
  run on real SAIN types; .NET Framework cost 49-104 ns -> 2-3 ns per read, enum read no longer
  allocates (15 gen0 GCs per 2M reads -> 0). Mono numbers will differ; the direction won't.
- Left alone on purpose (cost negligible, change would touch decision logic): per-decision "why"
  strings (seconds apart per prisoner), HoldForCover's reason string (compared by value as state),
  the default-on Log switches (Joel's diagnostics; 90 lines in the first 0.20.0 session).
- Profiler sections now: Pose, Tilt, Shot, Hit, Registry, Natural, Captors, Restraint, Voice, Menu,
  Layer, Gui, LayerCheck, SainFilter, HeadLook, FireGuard, Scenarios. The report gives gameplay cost
  (without Scenarios) and with scenarios, heap change per span, squad-cover and live-Player counts.

### Profiling plan (0.20.1), not run yet
Profile on (14. Debug), report every 10 s, same map/time, 3+ reports per case. Compare frame lines
(avg, p99, p99.9, worst, 1% low) and the "gameplay ms/s" / per-section lines:
1. Mod Enabled off (baseline frame times). 2. Enabled, idle raid, no surrender (expect LayerCheck +
HeadLook + Natural only, all tiny). 3. Combat without surrender (Shot, Hit, Natural). 4. One natural
surrender, held 60 s. 5. Several prisoners + bot captors + squad cover (Captors, Restraint,
SainFilter, FireGuard, Pose). 6. Gear pick-up, kneel/rise, turn-around (Pose worst us; "Profile
spike" lines name the section). 7. Scenario spawns separately: Scenarios section only, not gameplay.
8. Repeated raids: held/history/live/loot-cache counts return to ~0 at each raid start. Watch the log
for "compiled reader disagreed" (should never appear).

### First 0.20.1 raid (2026-11-20, LogOutput 18:05): "bots forgot how to fight each other, knife at range"
Joel's report after a raid with 30 scenario launches (#1-#30, up to 6 bots each, 30-45 m ahead).
Diagnosis from the log + SAIN 4.5.1 decompile; no code change made:
- No ordinary raid bot was ever held: the raid summary says Natural 0/0, Demand 0/0; all 25 Scenario,
  2 DebugKey (scenario bots #S1/#S4) and 2 Recapture surrenders were test bots. 0 mod errors/warnings.
- Cleanup worked: 35 participants removed via BotLeaveData.RemoveFromMap ("left in the raid": 0);
  truce/observer pairs and restraint ignore flags are lifted on cleanup (ScenarioShield.Remove,
  Restraint.ForgetPrisoner). #29/#30 still running at log end (CleanupAll at raid end).
- Knife charges at range: the "PMC captures Scav" preset's Scav ammo = No spare ammo removed 21-22
  spare mags every run, and captors took prisoners' guns. A dry gun -> the game switches to melee;
  SAIN then decides MeleeAttack whenever WeaponManager.IsMelee (BotDecisionManager.getDecision, any
  distance) and its RunToEnemyUpdatePatch sprints him at his GoalEnemy. SAIN behaviour on a state
  our presets create; not a leak to ordinary bots.
- Not fighting: within scenarios by design (setup truce; "Multiple simultaneous encounters" keeps 24
  truce pairs for its whole run; captors/guards held by our layer; observers hold fire at prisoners).
- Open: Joel to compare a raid with no scenarios launched (and, if still odd, one with Enabled off)
  before anything is changed.

**0.20.0 (2026-11-20): coordinated squad cover during captivity (Overwatch.cs, from Joel's pasted
6-part spec). Release packed (dist/HandsAreHigh-0.20.0.zip) and installed in H:\SPT4.1.X (0.19.0 DLL
backed up in that session's scratchpad, backup-0.19.0). Committed and pushed to main (no tag/release). NEVER RUN IN GAME: a role line
in the log is not proof the guard protects anyone; only the in-raid checks below are.**

What it does (details in the Overwatch.cs header):
- Role: the captor stays the captor (CaptorState unchanged as owner); one squadmate gets cs.Guard.
  Squad = the game's BotsGroup (cs.Group). Eligible: alive, active, not the captor, not held, not a
  captor himself, not covering another captor, not fetching gear, not a player ally (your group / Pit
  Fireteam follower: Teammates.IsPlayerTeammate), holds fire at this prisoner (restraint entry Holds),
  not in a fight (Restraint.OtherThreat, BotMemory.IsUnderFire), not out of ammo, <= 40 m, complete
  path to the prisoner. Scored by distance, sight (Perception.Recognises), gun up, injury. Kept until
  invalid (no re-scoring). Nobody: cs.AloneWhy, logged once ("holds him alone"), gear risk x1.15.
- Spot: ~6 m ("Guard distance") from the prisoner, 50-125 deg round him from the captor, line from
  eye to chest clear, his line of fire (8 m past the prisoner) >= 0.9 m from the captor's chest and
  squadmates, himself >= 1 m from the captor's line (10 m past), not bunched (< 4 m and < 20 deg off
  the captor's bearing), within 15 m. Scored by angle, distance, walk, solid geometry beside/behind
  (3 rays), hidden from a recent enemy sighting. Current spot kept while valid; moves only after 1.2 s
  of a problem or the prisoner moving 3 m; 8 moves max, 3 s apart; walk (no sprint), gun on him.
  tools/coversim: the fire-line rules checked offline (all pass; caught the preset's "behind" start
  reading as "too far" and a bunched start being accepted, both fixed).
- Attention: outward look (behind the captor / towards a recent enemy) every 5-10 s for 0.9-1.6 s,
  never while the captor fetches or walks up or the prisoner moves. Fighting (Captors.Threat >= Fire)
  releases him to the combat AI; back after QuietBeforeGuarding of quiet; > 12 s: role dropped,
  replacement picked.
- Coverage measured 4x/s (not assumed): not fighting, not glancing, firearm up, not reloading/
  switching/running, look within "Covered within", clear line, captor not in his line. Changes logged
  by category (no number churn; glances only in verbose), summary at the end (% covered, gaps).
- Captor: closing in and fetching gear wait for cover (HoldForCover, "Captor waits for cover" 6 s),
  then go ahead and log "accepts the risk". During a fetch, a loss he can TELL (guard fighting,
  reloading, running, gone; never a glance or a blocked view) is judged once: pause (-> "later") or
  carry on (CaptorLoot.CoverLossJudgement, by caution, kept weapon, kneeling/turned, already bending).
- Resistance: Overwatch.Visible = what shows (draw, weapon in hands not being dropped, walking
  towards the dropped gun, moving off unordered, urgent rise, hands lowered unordered); never the
  prisoner's phase. The guard notices only if he sees it (within 100 deg + Recognises), reaction
  0.3 x 0.75-1.35 (+peripheral, walking, fighting). Armed: Reject (squad) + remembered + GoalEnemy set
  ("GUARD REACTS", logs whether the captor stood in his line: the game's ShootData.CheckFriendlyFire /
  SAIN's SAINFriendlyFireClass hold that shot, both verified in the decompiles). Unarmed: eyes on him,
  holds fire (the captor's Judgement decides). Restraint.OnArmed skips a guard (his own perception);
  one who never sees it takes his squad's word 2.5 s later (as a responder: custody ended, still
  watching for 3 s). The guard layer is NOT gated on the prisoner's phase (that was a leak of the
  hidden decision, caught in review).
- Prisoner (Decisions.SquadCovers): squadmates judged by Overwatch.Visibly (gun, reload, running,
  weapon change, firing elsewhere, where he looks, line); "behind him, assumed" only for a squadmate
  he has seen (SurrenderState.SeenSquad); one in sight but not on him makes the opening x0.85.
- Hand-overs: Captors.SuccessorFor (guard first, then any practical member) in CaptorDown and
  TryHandOver; every custody end releases the role (Captors.End, CaptorDown, TryHandOver); Clear on
  raid end / disable / teardown; ORBIT kept awake while covering (not unblocked if he became a captor).
- Config 23. Squad cover (7 keys, bound offline on a copy of Joel's cfg through the built DLL: OK
  twice). Preset "Squad cover" (squad 1-3, squadmate beside/behind, keeps pistol/slung/nothing,
  events: none / draw at the pick-up / hostile bot at the guard / captor killed / guard killed / you
  take over). Offline: installed DLL builds all 28 presets.

### In-game test plan (0.20.0)
Log squad cover, Log decisions, Log gear decisions on. Preset "Squad cover" unless noted.
1. Two-bot capture (event None, squad 2): `<squad>: PMC-2 covers him while he holds ...`, then
   `covers ... from where he stands` or `moves to cover ...` + `in position: N m from ..., N deg off his
   line`. LOOK: PMC-2 stands off to the side, gun on the prisoner, not behind PMC-1. `covering ...`.
2. Pick-up with cover (same run): `waits for PMC-2 to cover him before ...` (if he wasn't yet) then
   `..., PMC-2 covering him`; the prisoner's `his captor isn't covering him, but PMC-2 is` (verbose) and
   no opening line while PMC-2 aims at him. LOOK: PMC-2's gun stays on the prisoner during the pick-up.
3. Draw at the pick-up: `PMC-2: sees ... drawing a weapon (N deg ...); reacts in 0.2-0.5 s`,
   `GUARD REACTS: ... engages`, and in the game PMC-2 actually shoots him (the real test). Run it with
   Guard looks around on and off; a `doesn't see ...` line + `takes his squad's word` is the miss case.
4. Blocked: Squadmate starts Behind: `PMC-1 is in his line of fire; moves to cover ...`. Also a
   capture next to a wall/car: `his view of him is blocked; moves ...`, no circling (<= 8 moves).
5. Distracted: Hostile bot at the guard: `stops covering ... to fight`, `not covering: fighting ...`,
   then `quiet again` or `stops covering ... (fighting ... for 12 s)` + a replacement (squad 3).
6. Captor killed at the pick-up: `captor down; PMC-2 takes over ... (he was covering him)`.
7. No guard: squad 1: `nobody can cover him ... (no squad); he holds him alone`; gear risk `nobody
   can cover him`. Guard killed (squad 2): `stops covering (he is down)` then alone.
8. You take over: `you take over from PMC-1`, `stops covering ... (the player took over)`; PMC-2 goes
   back to normal AI (no stuck stance). Pit Fireteam: preset "Pit Fireteam teammate capture": the
   follower never appears as a guard (`under the player's orders`).
9. Natural raid with PMC squads: same lines on real captures; Profile on: Overwatch costs in the
   Captors section.

**0.19.0 (2026-11-20): natural surrender made reachable in ordinary fights, natural-pipeline
diagnostics, captor pick-up fixed at its source, deliberate decisions about distant dropped weapons,
SAIN kill steering actually applied, dead code removed. Release packed (dist/HandsAreHigh-0.19.0.zip,
DLL only) and installed in H:\SPT4.1.X (0.18.0 DLL backed up in that session's scratchpad).
Committed and pushed to main (ce5f21c, no tag/release). Superseded in the install by 0.20.0. NEVER RUN IN GAME: no natural surrender and no pick-up animation has been seen yet.**

Why natural surrender never happened (evidence):
- The only natural-raid log on disk is 0.3.0's (scratchpad hah-log3): rolls ran (the pipeline
  worked) but every chance was 0.011-0.046 per check, even a Scav at pressure 0.99 (out of ammo, leg
  destroyed, pinned, 3 of 5 squad dead): 0.046. Every log since 0.4.0 is scenarios only (the 13:24
  0.18.0 log: 0 natural bots).
- tools/natsim (a model of NaturalSurrender against ordinary firefights): with 0.18.0's defaults a
  Scav gave up in 3.5% of fast fights (the player's usual 3-15 s, 2 hits/s) and a PMC in 0.5%; 12% /
  4% even in drawn-out ones. Causes, all in the timing and scaling, none in registration: one hit was
  1/6 of full suppression (below the 0.2 a reason needs); injury counted only below 35% health (the
  man is usually dead first); a destroyed leg + bleeding was 0.45 + nothing (worst, not sum), so the
  Scav threshold 0.4 x weight 0.45 was out of reach; the re-check after a hit came 0.5 s later and at
  most every 1.5 s (the second hit of a burst got no check); checks every 5 s; Scav levels (rolled
  around the player's, 48-78 in that log) counted as experience (x0.79 on every roll).
- Everything else traced and verified equal for natural and scenario bots: brains (Assault,
  CursAssault, PmcBear, PmcUsec: decompile ShortName strings; SPT pmc.json/bot.json brain types are
  pmcBEAR/pmcUSEC/assault), role classes, registration (one SurrenderRegistry.Surrender path),
  SAIN mirrors its target into BotMemory.GoalEnemy (setGoalEnemy), BotsGroup.IsEnemy compares by Id
  (so bot-vs-bot isn't blocked by the BotOwner key), no distance gate, cooldown only after a surrender.
- Two things that hid a surrender that did happen: a hit in the first moments (the shooter can't have
  seen the hands yet) made him bolt at once; the surrender shout was dropped when his speaker was busy
  with combat callouts ("no chance to speak within 2 s", 13:24 log).

Changes (natural):
- Defaults: Check every 5 -> 3 s; Recheck after a hit 0.5 -> 0.25 s (gap 1.5 -> 0.8 s); Hurt below
  0.35 -> 0.6; Head or thorax below 0.4 -> 0.6; PMC pressure needed 0.55 -> 0.5. A hit counts as two
  near misses. Injury: destroyed limb 0.55 (was 0.45), stomach 0.65 (0.6), heavy/light bleeding
  0.55/0.3 (0.5/0.25). Scav experience = difficulty only. natsim with these: Scav 22% / PMC 3% of
  fast fights (40% / 10% drawn-out); Scavs ~7x PMCs.
- Migration: Settings.V19.cs, hidden "Config version (set by the mod)" (14. Debug): a value still at
  its 0.18.0 default moves to the new one once; values set by hand are kept (tested on a copy of
  Joel's cfg: all five moved, a hand-set 7 s kept, second load no-op). TurnLead (unused since 0.17.0)
  is no longer bound; its line stays orphaned in the cfg.
- Hits ignored right after giving up (06. While held, 2 s): he stays down and pleads.
- Voice: the Surrender line overwrites a busy speaker after 0.4 s (BaseSpeaker.Play with importance
  1000: the game replaces the current phrase when the new one's importance is higher; verified).
- SurrenderStats.cs: per raid, every 2 min while something changed and at raid end: bots checked,
  checks stopped per gate (Role, Teammate, Cooldown, NotActive, NoTarget, TargetNotLiving, BotTarget,
  NotPerceived, WeaponChange, TooFewReasons, LowPressure), rolls / gave up (Scav, PMC), activated,
  activation failures, hits ignored in the grace; surrenders started / acknowledged (bot custody) by
  source: Natural, Demand, Recapture, ScenarioNatural, DebugKey, Scenario. Activation line says
  NATURAL. Only Natural and Demand are raid evidence.

Captor pick-up (why it snapped; CaptorLoot header):
- IdlePlayerState.Pickup IGNORES its flag and enters PickUpState; only PickUpState.Pickup exits. Our
  Pickup(false) calls (End, Fallback, stale callback, transaction callback) on a man no longer in
  PickUpState started a second, action-less pick-up holding him up to 5 s with rotation clamped.
  Every exit is now LeavePickupState (only `is PickUpState`), as the player's own pick-up does
  (InteractionContextHelper). Raid teardown leaves the state too.
- He looked at the item from his FEET (BotItemTaker.LookAndTakeItem does too): pitch ~17 deg, and he
  stopped 1.45 m away: the reach went into the air and the item jumped into his slot. PickUpState.Enter
  only lowers the body (PoseLevel / 3, smoothed by SmoothPoseLevel) with the look over 60 deg down
  (pitch positive = down: MovementContext look = Euler(pitch, yaw)). Now: path to ~0.7 m, slowing for
  the last 2 m; the look eased down from his EYES at 150 deg/s; Pickup(true) once the look is within 6
  deg and FirearmsAnimator.IsIdling (SetInteractInHands skips the reach otherwise; aiming lowered),
  1.5 s at most. Then Recover (0.6 s): no walking, the look eased back to the man (140 deg/s).
- The item must still lie there right before the pick-up, and the planned move is refused if the item
  left its address since (IItemOperationResult.From, as the player's pick-up checks).

Distant weapons (CaptorLoot Track/Notices/Weigh/WeighDistant; 21. Captors taking surrendered gear):
- Each item is tracked (its real LootItem, sampled every 0.4 s): no decision while it moves. He knows
  where it lies only if he saw it there (in front of him, 65 deg, clear line, within "Sees where
  things land within" 40 m) or at the feet of the man he watches; unseen: kept an eye out for
  ("Searches for an item he never saw land for" 12 s), then forgotten. He walks to where he believes it
  is, re-acquires it by sight on the way (3 re-paths), stops if it isn't there.
- Close (walking distance <= "Close by within" 4 m) or at the man's feet: the 0.16-0.18 risk rule.
  Distant (a walk away from him, <= "Never walks farther than" 30 m): benefit = (denial 0.6 if within
  scramble range of the man, else 0.25, demanded weapons only) + (0.5 fills a gap, else value up to
  0.45) x appetite; cost = ("Distance weighs" x walk 0..1 + custody: time unwatched / 25 s, x1.8 a
  weapon still on him, x0.6 kneeling, x0.6 turned away, x0.85 hands on head; 0.1 if a squadmate covers)
  x caution x experience. take / postpone (another enemy about, under fire, or custody is the larger
  cost: weighed again every 4 s or on a situation change, at most 3 times, Turn around may be ordered)
  / after release / leave. Table in this session: gun 6-8 m away: take; 12 m standing: postpone, kneeling
  + turned: take; 20 m covered by a squadmate: take; with a pistol still on him: postpone.
- On a distant walk he looks where he goes, a look back every 2.2-3.5 s (0.6-1 s): a real opening
  (Reactions.Covered reads his LookDirection; Busy "walking off to"). The job stops for the man moving
  only when the captor sees it (Perception.Covering 70 deg); Captors.Check notices resistance by sight.
  Time limit + 1 s per metre of a distant walk.

Also: SAIN kill steering used SAIN's PRIVATE GoalEnemy setter now (0.18.0 asked for a public one and
never steered: "NO: GoalEnemy has no public setter" in the 13:24 log). "sees his X is gone (you took
it)" printed "you" for bot captors.

Dead code removed (references checked first, incl. reflection/Harmony/config): Kneel.TurnRoot and
FaceTurnSpeed (no caller since 0.17.0), TurnLead config entry, write-only fields
CaptorOutOfViewSince, ComplianceWhy, DrawOk, LastDemandAt, OrderIssuedAt (SurrenderState), EndWhy,
Validated (Scenario), ScenarioPreset.ShieldDefault (26 sites; the shield param carries the default).
Kept: SurrenderLayer.BuildDebugText (BigBrain), Scenarios LogEvent (BepInEx listener).

Tests done: Release build 0 warnings; Settings.Bind on a copy of Joel's cfg through the built DLL
(caught and fixed an apostrophe in a new key that would have killed Awake); natsim; distant-decision
table. NOT done: anything in a raid.

### In-game test plan (0.19.0)
Settings for the natural test: Verbose logs OFF (the summary lines are enough; ON for every check),
Log decisions on, Log gear decisions on, Scenario tools may stay on but launch nothing.
1. Natural, you vs Scavs: an ordinary raid (Customs/Woods/Shoreline). Fight Scavs normally; also
   shoot legs/arms, and keep a Scav behind cover under sustained fire for a few seconds. Every 2 min:
   `Natural surrender, so far this raid: N bot(s) checked ...; rolls N (Scav N, PMC N), gave up N`.
   A natural surrender: `-> surrenders`, `surrender activated (NATURAL ...)`, he shouts (Stop/Hold
   fire), drops his gun; hits in the first 2 s: `stays down`. Walk up: `surrender acknowledged`.
   rolls 0 with many checks: the gate counts say where they stop (NotPerceived / TooFewReasons /
   LowPressure); send the summary lines.
2. Natural, bot vs bot: watch PMC-vs-Scav fights (or a PMC squad clearing Scavs): `vs X (bot ...)`
   rolls, `surrenders to him`, `takes him prisoner` / `spares him`; summary `Natural n/m (k)`.
3. Pick-up (scenario "Weapon pickup opportunity", or any capture): the captor walks right up,
   stops, his eyes go down, he crouches to the floor, the gun goes into his slot, he straightens up and
   looks back at the prisoner. Log `took ... (pick-up animation; N s); straightens up`. Interruptions
   (preset options): no second pick-up animation, no frozen captor.
4. Distant: a prisoner's gun lying 8-25 m from him (e.g. let him walk off after the drop, or a capture
   where the surrender happened away from the gun): `(distant: N m walk)` and the decision line
   `benefit ... vs cost ...: goes for it / waits / not worth leaving his post for`; while walking off
   he looks ahead and back; the prisoner's `opening (his captor is walking off to ...)`.
5. Compare frame time with the mod on/off in one raid (14. Debug, Profile on: the section timings).

**0.18.0 (2026-11-20): head set from the rig's real axes (sideways gaze / cocked head), the game's
bot head free-look centred while held, captor lethal intent decided once at custody, SAIN steered
onto a kill decision, demanded weapons collected, turn-around order bug, pick-up window for the
prisoner. Release packed (dist/HandsAreHigh-0.18.0.zip, DLL only) and installed in H:\SPT4.1.X (0.17.0
DLL backed up in that session's scratchpad). UNCOMMITTED. NEVER RUN IN GAME: nothing below is
confirmed in a raid.**

From the 0.17.0 log (LogOutput.log 2026-11-20 12:13, 13 scenarios): 9 release rolls, all "lets him
go" at 2-4% (base 0.04 x aggression x "discipline" 0.86-0.95); no DECIDES TO KILL anywhere, so the
execution path never ran; "orders him to turn around first" 4x, Turn around never given by a bot;
#13 the knife dropped on Drop your weapons was never weighed; pick-ups that did run worked (3x
"took ... pick-up animation").

Root causes and fixes:
- Gaze (Presence.ApplyHead): the head's "forward" was measured ONCE per bot from one animated frame
  (HeadFwdLocal = body forward in head space). The game's own bot head free-look (BotOwner.UpdateManual
  -> BotHeadData.ManualUpdate -> Player.Look(withReturn false), +-40 deg clamp; towards
  BestObservedEnemy, or +-25 deg patrol glances; applied in VisualPass by PlayerBones.RotateHead as
  GetHeadRotation = (yaw, 0, -pitch) on neck+head local) was whatever it was on that frame: that angle
  became a permanent sideways gaze, and the pitch, applied about a "right" axis taken from the skewed
  forward, leaked into roll. Offline replay of the old code (tools/headsim/headmath.py; rig axes: tools/headsim/rig_axes.py <skeleton.bundle>): free-look 15/25/40
  at measurement -> gaze +15/+25/+40 with the captor dead ahead, and 2.6/4.3/6.5 deg roll at 10 deg
  pitch. Rig axes READ from the installed skeleton.bundle (UnityPy): Root_Joint +Z forward; Base
  HumanHead local +Y = face, local X = down, local Z = left (SAIN also uses head.up as the look). Now
  the head is set to an absolute target from those axes (LookRotation(face, world up) x rig inverse):
  yaw/pitch from his planted body, ZERO roll, blended by W, 40% neck / 60% head; whatever the animator
  or the free-look left in the head is replaced, not added to. Verified offline: rest pose reproduced
  exactly; yaw -70..70 x pitch -20..40 -> 0.0 deg error, 0.0 roll. RigCheck per bot logs "head rig
  checked: ... animated head N yaw/pitch/roll (game free-look N)" and switches the look off for a rig
  that doesn't match (none expected: one shared skeleton).
- HeadFreeLook.cs: prefix on BotHeadData.ManualUpdate: while HandsUp (not asleep), target 0 and the
  game's own UpdateHeadRotation returns the head; his head states aren't run. Released/rearmed: untouched.
- Torso with the head: chest share from 8 deg (kneeling 15), 35%, eased over 10 deg, to the limit
  (kneeling 12: planted knees). Hands on head now sit in the skull's own final frame
  (Presence.SkullFrame), never pull the head.
- Debug: "Head neutral test" (14.): only the captor, no glances/demeanor tilt/tremble/flinch/idle/
  gestures. "Log pose diagnostics" now also logs, standing or kneeling, once a second: `gaze: ...
  captor at N deg; animated head yaw/pitch/roll (game free-look, look-to-body); after: head yaw/pitch/
  roll, target; chest N from body (our torso turn)`. New preset "Head straight check" turns both on
  for its run (cleared at cleanup/teardown).
- Lethal: decision existed, reachability didn't (2-4%), and experience reduced every lethal chance
  ("Experience restrains" = mercy by level). Now: Judgement.Intend at Captors.Accept, ONCE: "Lethal
  intent toward a prisoner" 0.25 x aggression (Wreckless 2.5 .. Timmy 0.5) x "Weight: rival faction"
  1.4 (BEAR/USEC, Scav/PMC either way) x he-shot-me 2.5, cap "Lethal decision cap" 0.85 (new key names
  so the defaults apply; old keys orphaned in the cfg). Logged "intent about X (decided once, at
  custody) ... MEANS TO KILL HIM / means to let him go". Carried out at the release point (after the
  disarming and the gear); something new since (a weapon kept back, learned he shot them) adds only
  the extra chance, once. Refusal and bolt: a captor with lethal intent fires; else their own rolls
  (no experience factor). Level/skill: only noticing a kept weapon (unchanged). Discipline key removed.
- Execution: (1) SAIN keeps its own target; 0.17.0's retarget only set BotMemory.GoalEnemy, which SAIN
  overwrites: SainTargetFilter.Steer (postfix on ChooseEnemy, only while a kill is watched) sets SAIN's
  public GoalEnemy to the prisoner when he is in SAIN's VisibleEnemies and the captor isn't under fire
  (Suppression < 1), mirrors BotMemory.GoalEnemy like SAIN's setGoalEnemy. (2) TriggerPatch returned
  without NoteShot when a shield was active and nobody restrained: shots could go unseen by the watch.
  (3) Escalate logs the restraint transition read back (Reject final, still holding fire no, ignore
  flag clear, vanilla target). Watch logs: target acquired (SAIN/game), "FIRES at ... (trigger
  pressed)", 3 s interim status, 10 s "EXECUTION FAILURE: KILL DECISION NOT CARRIED OUT" (layer, game
  target, SAIN target, hands, sight/EnemyInfo visible/ignore flag, distance, aim angle, restraint,
  holding fire, shield, prisoner phase), CARRIED OUT. Verified not a blocker: BotStandBy (needs nearest
  enemy > 130 m); SAIN IsEnemyActive (standby only). Health is never touched.
- Debug key Ctrl+U (22., debug keys on): the captor under the crosshair, or the captor of the prisoner
  under it, makes the lethal decision now (decision only; U is unbound in EFT and in every installed
  mod cfg).
- Turn-around bug (Captors.Check): `next` was read before CaptorLoot.Step inserted TurnAround at
  cs.Step; the stale order was given again and Step++ skipped the turn. Re-read after Step.
- Demanded weapons (CaptorLoot): items dropped after the first weighing are weighed when they land
  (HasNew). A gun or knife he made him drop (surrender drop or Drop your weapons) is Demanded: always
  wanted (no appetite/value), fetched first, "Risk he accepts for those" 0.9 (21.; "Collects the
  weapons he made him drop" on). Room is still the game's own slot answer (bots never pack guns:
  no empty weapon slot -> "no room", logged, left; nothing of his dropped/deleted). Same pick-up path as
  0.16 (PickUpState + BotItemTaker transaction, read back from his equipment). Interrupted (threat,
  man moved/resisted): full cleanup (Pickup(false)), then "later" (weighed again when the situation
  changes, 2 tries max). "someone else took it (X has it)" names the holder. A transaction finishing
  after the job ended is logged. After a kill decision is carried out the killer fetches what he still
  wanted (AfterKill -> the after-release fetch).
- Pick-up window (prisoner): "walking over" with the captor's eyes off him is now a clear opening
  (reaction time only; was +1.5 s, longer than most pick-ups); DecideOnOpening x pick-up window: 1.6 with
  a weapon still on him, 0.8 with nothing, x 0.8-1.3 by his fight. Covering squadmates still close the
  opening (Reactions.SquadCovers). The captor still reacts only to what shows (Reactions.Shows), slower
  while bent over (0.35-0.8 s extra, unchanged). Concealed weapons: still only Judgement.Visible.
- New presets: "Head straight check" (you or a PMC in front, standing/kneeling, hands up/on head,
  variation off/on) and "Weapon pickup opportunity" (squad 1-2, keeps pistol/slung/nothing, draws
  FORCED or own decision, interruption: prisoner killed / captor killed at the pick-up, weapon removed
  before he gets there). Offline: installed DLL loaded in PowerShell, all 27 presets build.

### In-game test plan (0.18.0)
Log decisions, judgement, gear decisions on; Log pose diagnostics on for 1-2.
1. Head straight check, You, each posture x hands, Variation Off: `head rig checked` (note the
   animated yaw and free-look numbers: they are the old error), then `gaze:` with captor ~0 deg: after
   head yaw ~0, roll ~0, chest ~0. Walk to his left and right: yaw follows, roll stays ~0, chest grows
   with it; standing past 70 deg a stepping turn. Variation On: same, plus glances. Screenshot.
2. Same with PMC bot captor; Turn around and hands on head (no turn back; head not bowed by the hands).
3. Captor execution check, Kill now, squad 1 and 3, each posture: `restraint ... Reject (final);
   still holding fire: no`, `has ... as his target`, `FIRES at`, `CARRIED OUT`; any `EXECUTION FAILURE`
   line is the bug report. Also Ctrl+U on a natural captor.
4. Captor judgement, He complies, His own roll, ~8 runs (vary captor personality): `intent about`
   lines with both outcomes; the MEANS TO KILL ones end in `carries out ... DECIDES TO KILL` and a
   CARRIED OUT line. Natural bot-vs-bot captures in a raid: same lines.
5. Weapon pickup opportunity: keeps pistol / slung / nothing, squad 1 vs 2, each interruption:
   `goes to take ... (the weapon he made him drop)`, `took ... into his ... slot (pick-up animation)`,
   prisoner `pick-up window x`, draws or keeps still; with squad 2 `but PMC-2 is` (covered); interrupted
   lines + `will weigh ... again`; no stuck hands on either.
6. Any captor plan with "turned away": the bot now actually gives Turn around before fetching.

**0.17.0 (2026-11-20): facing/turning via the game's stepping turn, lethal path made reachable and
watched, committed retreats, compact menu. Packed (dist/HandsAreHigh-0.17.0.zip) and installed in
H:\SPT4.1.X. Committed and pushed to main with 0.16.0/0.16.1 (2026-11-20, no tag/release). NEVER RUN IN
GAME (0.16.1 was never installed; the 11:21 log is 0.16.0).**

From the 0.16.0 log (2026-11-20 11:21, 13 scenarios): every lethal roll was a compliant prisoner at
release (3-7%), none hit; resistance was fought ("shot ... as decided" x2); every release ended in a
short sprint and "got away", often followed by "going for <his own gun> 23-26 m away"; the gear
weighing, staging and kept-weapon presets ran (kit "Keeps sidearm" had no sidearm in that seed).

Root causes and what changed:
- Turning: player_anim_controller (read with UnityPy) goes Idle_Aim -> Turn_Left/Right_90_Aim when
  DeltaRotation < -45 / > 45; the clip steps the feet and its root motion turns the root
  (MovementState.ProcessAnimatorMovement applies AnimatorDeltaRotation). A bot's root is snapped to
  exactly TrunkRotationLimit 45 (ProcessUpperbodyRotation, AI branch), so DeltaRotation never passes
  45: bots spin. 0.16 rotated the root itself (ApplyRotation) with the look 25 deg ahead.
  TurnDriver.cs: turns over 70 deg raise the prisoner's own TrunkRotationLimit to 80 (the game sets it
  per player too, e.g. 25 prone) and lead the look by 60: the game's clip turns him. Restored when
  the step ends, on any stop, release, phase change, teardown (TurnDriver.Stop). No root turn seen
  within 0.9 s -> that bot pivots from then on (warning). Smaller turns: pivot (ApplyRotation) with
  the look <= 35 ahead (never a clip). Used for Turn around, facing the captor (hysteresis 15 deg),
  the pre-kneel turn (Kneel.TurnRoot no longer called). Setting 19 "Turn with stepping feet".
- Head/torso: glances capped at 0.75 of his reach; the way-out glance is 40-70 deg off his body
  (0.16 aimed 35-110 deg off "away from the captor": behind him, neck and spine at their stops);
  over-the-shoulder at 0.8 reach. Captor tracking keeps the full reach; beyond it a kneeling man loses
  him, a standing one turns his body.
- Lethal: (1) an unarmed bolt from custody had no decision (squad rule always LetGo for the captor
  himself): Judgement.OnBolt, once, if he saw it, "Fires at a prisoner who runs" 0.3 x aggression x
  he-shot-me x kept-back x discipline. (2) a kill decision could be undone: a squadmate taking custody
  afterwards ran InformSquad -> SquadDecision -> Set(Respect) over the killer's entry. Now Final
  entries can't be overwritten with hold-fire, and Captors.Impractical refuses custody for a squad
  that decided to kill (Restraint.SquadDecidedToKill). (3) execution watch (Judgement.TickKills,
  0.5 s): logs "opens fire ... s after deciding", "KILL DECISION CARRIED OUT", or after 10 s "KILL
  DECISION NOT CARRIED OUT: layer, target, hands, sight, restraint, shield"; re-sets the target at
  most 3 times if it drifted while he sees the man. No health is touched.
- Retreat: RetreatPoint scored any hidden spot +100 regardless of distance and ignored where he was
  held. Perception.FleePoint: destination outside "Leaves the captivity area (m)" 40 of HeldAt (set
  in MarkFlight when he leaves his hands-up), complete route that never gets closer to the threat
  than he is (PassesNear), scored by concealment, separation, route length; else a leg through cover
  gaining >= 8 m. Arrival inside the area -> next leg (max 5 legs); replan (<= 2/leg) if the captor
  he sees is within 10 m of the destination. Logged: destination, legs, "retreat ends (...): X m
  from where he was held, Y m covered in Z s". "No going back for a gun for (s)" 60: guns in the area
  he fled are not recovery targets until then (a gun he carries is still drawn).
- Menu: fixed number per order (1 Drop weapons, 2 Drop kit, 3 Armour off, 4 Follow me, 5 Get down /
  Stand up, 6 Hands on head / Hands up, 7 Turn around, 8 Stay, 9 Let go); config-disabled orders
  leave their number unused, unavailable ones stay greyed with the reason. One-line header (name,
  side, state), 23 px rows (was 30), 16 px badges, short footer. InputGuard unchanged (the closing
  press is latched, verified in code).
- New presets: Captor execution check (Kill now = FORCED decision only; or a forced bolt with the
  captor's own roll / forced lethal), Retreat from captivity (let go or bolt, mercy forced). Events:
  "His captor decides to kill him now", "His captor lets him go now".
- Config keys: two new names first had apostrophes (would have killed the plugin at load); caught by
  the offline check, renamed. pack.ps1 checks every Settings*.cs.

### In-game test plan (0.17.0)
1. Turn around standing, several angles: feet step (the game's turn clip), log "stepped round N deg
   with the game's turn animation"; a "no stepping turn ... pivots" warning means the clip didn't play.
   Walk round him afterwards: no turn back. Kneeling: up, step round, down.
2. Captor circling a standing prisoner (Captor closes in preset, or walk round yourself): small moves
   = head only, beyond ~15 deg a short pivot, beyond 70 a step turn; no wound-up trunk. Kneeling: head
   and a little torso, then "loses sight of his captor". Hands on head while doing it.
3. Captor execution check: Kill now (each posture, squad 1 and 3): "opens fire", "CARRIED OUT",
   squadmates still "holds fire"; zero RESTRAINT LEAK. He bolts (own roll) several times, Wreckless vs
   Timmy captor: some "lets him run", some "DECIDES TO KILL".
4. Retreat from captivity in the open, inside a building, in a dead end: "flees to ... m from where
   he was held", legs, "retreat ends ... >= 40 m", no "going for <his gun>" in the area within 60 s.
5. Menu at 1080p / 1440p and Menu size 0.6 / 1.5: readable, digits 1-9 fixed, greyed rows, the
   closing 0 or a submitting digit never switches your weapon.

**0.16.1 (2026-11-20): facing fix from Joel's screenshot (standing, hands on head, chest twisted,
head looking away from him). Packed (dist/HandsAreHigh-0.16.1.zip), NOT installed (game was
running); superseded by 0.17.0, which includes it. Unrun.**
- Standing facing set only the look (SurrenderLogic.Face); the game turns a bot's root only past 45
  deg and then to 45 deg short, so after any big turn he stood with his trunk twisted up to 45 deg
  (the 0.15.0 kneel finding, never fixed standing). Facing.FaceRoot turns the root to the look
  target (ApplyRotation, 3 deg dead zone, Turn speed eased) whenever he faces his captor standing
  still.
- Presence measured the head's forward once per bot on the first frame, against the root: during
  that 45 deg twist (or aiming up/down) the head stayed that far off for the raid. Now measured only
  when look and root agree within 4 deg and the look is level.

**0.16.0 (2026-11-20, home PC): realism and polish pass: captors take surrendered gear, Turn
around and Hands on head orders (player and bot captors), head pose fix, partial compliance with
Drop your weapons, captor judgement (warn / repeat / release / lethal) with explicit decision
points, staged scenario spawns. Release build packed (dist/HandsAreHigh-0.16.0.zip) and installed in
H:\SPT4.1.X 2026-11-20 (0.15.0 DLL backed up in that session's scratchpad). Ran in game 2026-11-20
(log 11:21, 13 scenarios; findings under 0.17.0).**

From Joel's 0.15.0 log (LogOutput.log 2026-11-20 08:53): 5 scenarios. #1 (PMC squad): Prisoner
active +4.7 s, PMC-1 +9.9 s, PMC-2 only requested then (squad members one after another), cleaned
up at +21 s with PMC-2's profile still on its way. Every bot captor's plan was "disarm and release"
or "hold and release" and every custody ended `ordered Leave`; nobody was ever killed by a captor.
A frightened kneeler picked the Down glance 4 times in a row (weight 2.4 vs <= 0.7 for the rest).

Root causes:
- Mercy: after custody there was NO lethal decision point at all: every plan ended with Leave,
  every custody ending (busy, lost sight, captor down) is non-lethal by design, and scenarios use
  ForceAccept (skips Decide), so the only lethal paths were the initial refusal ((1-0.65m)(1-0.75m)
  ~9% at mercy 1, never in a forced scenario), a squadmate's 2% refusal, and an observed draw.
- Head bowed between the hands: kneeling, the Down glance looked at the ground 1.1 m ahead (~44 deg
  below the eyes), clamped only by Head down max 35, and the frightened demeanor added 9 deg; the
  hands follow HeadDelta (correct), so the head hung forward with the hands on it. No IK feedback
  (the animator rewrites the bones every frame; hands follow the head, never the reverse), no
  spine/neck compensation, targets not low (palm centre at skull height, 0.11 m behind it).
- Scenario start: ScenarioSpawner requested squad members one at a time and every bot acted the
  moment it was active (no hold).

Engine facts used (0.16.9 decompile, scratchpad eftfull; BigBrain 1.5.0 decompiled too):
- Bot pick-up = BotItemTaker: claim check `ItemTaker._itemToTake` of squadmates (SomebodyTargetItem),
  room = GetItemAddress (InventoryController.FindSlotToPickUp: only an EMPTY equipment slot that
  takes it; else FindGridToPickUp, never for a Weapon), then `CurrentManagedState.Pickup(true, cb)`
  (only IdlePlayerState overrides it -> PickUpState fires cb after 0.6 s, exits by itself after 5 s),
  in cb ItemManipulator.QuickFindAppropriatePlace(simulate) + StrictCheckMagazine +
  InventoryController.RunNetworkTransaction, then Pickup(false). CaptorLoot mirrors it exactly; no
  idle state within 1.5 s or no callback in 4 s -> the same transaction without the animation.
- EnemyInfo.LastGetHitTime: set by BotOwner.GetHit for the enemy that hit him = "he shot me".
- MovementContext.ApplyRotation feeds DeltaRotation (look minus root) to the body animator; the
  game snaps a bot's root past TrunkRotationLimit 45. Human turn-in-place states exist
  (Turn_Left/Right_90_Aim) but no code drives them for bots: whether the animator steps on a
  root turn is UNVERIFIED (look at it in game).
- BigBrain CustomLayerWrapper.Name() = GetName(); Brain.ActiveLayerName() reports our layers.

What changed (files): HandsPose.cs, Facing.cs, Compliance.cs, Judgement.cs, CaptorLoot.cs,
Settings.Realism.cs (sections 19-22, two keys in 17), Scenarios/ScenarioStaging.cs; edits in
Orders, OrderMenu, Captors, SurrenderLayer, SurrenderState, SurrenderRegistry, Kneel, Presence,
ArmPose, Decisions, Restraint, Gestures, CommandVoice, CaptorVoice, Runtime, Compat (Looting Bots
adapter), Scenario*.cs; pack.ps1 checks every Settings*.cs.
- Hands on head (HandsPose): its own order (HandsOnHead / HandsUp; menu slot 9, label flips),
  posture untouched. Only empty hands move (drop still under way -> waits). Kneel.ApplyHands runs
  standing too, weight HeadHands x arms weight. Get down still puts the hands on the head unless
  Hands up was ordered this surrender (19. "Get down puts hands on head"); the descent's own hand
  window keeps its timing; a compliant rise with the order kept keeps them there and never braces.
  Arm IK target clamped to an elbow of 25-160 deg (Kneel.ClampReach): the hand stops short instead
  of locking the elbow. Gestures off while the hands are on the head or he is turned away.
- Head (Presence): with the hands on the head pitch is limited to 19. Head down/up max with hands on
  head (12 / 16), standing yaw to 50; demeanor pitch halved; Down glance 2.5 m ahead and x0.4 with
  hands on head. Head not frozen: glances, looking up, the over-the-shoulder glance all remain.
- Turn around (Facing): direction fixed at the order (back to where the issuer stood); root turned
  at 110 deg/s eased (ApplyRotation), look leading by up to 25 deg; room for raised hands checked
  (0.32 m sphere cast) or refused; from his knees: compliant rise, turn, kneel again facing away
  (19. "Turn around from his knees"; off = refused); refused while getting up/down. Afterwards the
  layer doesn't face the captor (SurrenderLogic asks Facing.Look first), Presence treats him as
  planted (captor behind = out of view, over-the-shoulder glances, no turn back). Ends on Follow,
  phase change, new surrender, another Turn around. Menu: 10th row (no number key; wheel / next).
- Partial compliance (Compliance): Drop your weapons covers slung/holstered guns and the knife
  (the gun in hand went at the surrender). One decision per accepted demand: first demand drop all
  or keep one back (holster > knife > slung; his only weapon only if pistol/knife), repeated demand
  comply / refuse / escalate (a real draw after his reaction time). Real items only; the report line
  reads back his equipment: "disarmed" only when his weapon slots are empty. On screen: what you can
  see, never what he kept. Kept on purpose: draw weight x1.3 at a later real opening (never certain).
- Captor notice (Judgement.AfterDemand, once per answered demand): a slung gun he can see, a
  holstered pistol within 4 m with a line, a gun he saw earlier that didn't land (SeenWeapons);
  never a knife or bag contents. Noticed -> demand again (at most 2, warning line), then Refusal:
  lethal roll, else last warning + let go soon (Unsafe: no close-in, no fetching, no second roll).
- Release decision (Judgement.AtRelease, once per custody): kill instead of letting go, chance
  0.04 x aggression x he-shot-me 2.5 x kept-back 1.8 x discipline, cap 0.6. Lethal = Reject on that
  captor only, entry Final (never re-rolled when the man runs), captor's target set, custody ended;
  his squad's restraint stays. Death line: `killed with his hands up by X: as decided (...)` /
  `RESTRAINT LEAK` / `no decision about him`. Decide: known violence x0.63 on mercy.
- Gear (CaptorLoot): per item once: value (handbook incl. contents), gap (empty slot), room (game's
  address), appetite roll (0.6 x greed: Rat 1.5 .. Coward 0.7), risk (0.5 x by his feet 1.6 x weapon
  seen on him 2.2 x kneeling 0.55 x turned away 0.5 x hands on head 0.8 x squadmate covering 0.35 x
  hurt x caution x experience) vs 21. Risk he accepts 0.5: take / later / leave. Later: re-weighed
  only when the situation changes (kneels, turns, cover), a Turn around ordered first (once), or
  fetched after the release once the man is > 15 m or out of sight (leaves it beyond 25 m). Fetch:
  walk 0.65, eyes on the prisoner until 3 m, then on the item; still and looking down for the
  pick-up. Prisoner sees `his captor is picking up his X` (clear, 0.9) or `walking over to` (0.7);
  behind him he hears it; other guards he can see still cover him. Captor sees resistance 0.35-0.8 s
  later while fetching. Ends on any threat, the man moving 1.5 m, item gone, 20 s, stuck.
- Staging (ScenarioStaging, BigBrain layer at priority +3 = 98, only bots in Held): every profile
  requested at launch in parallel; activation per squad from the scenario tick (first member makes
  the group); each bot held from the group action (before his first brain update) until all are
  active and their brain runs the hold; then Validate (spot <= 3 m, one group per squad, enemies,
  sight/distance per preset NeedSight/MaxStart) and all released together, stage timings logged.
  Failure / timeout / cancel: cleanup with each participant's stage; stale profiles and late
  arrivals create nothing / are removed; hard release limit spawn timeout + 15 s.
- Looting Bots (not installed here, API unverified): PreventBotFromLooting(BotOwner, float) called
  only on an exact signature match, for captors while they guard or fetch.
- Debug keys (14. Debug keys on): Ctrl+J hands on head / up, Ctrl+T turn around (Ctrl+H is
  EFT's headlight switch). Scenario events: your/captor Hands, Turn, Draw what he kept (FORCED),
  Captor was hit by him (SIMULATED EnemyInfo). Presets: Captor disarms and takes the gear,
  Prisoner keeps a weapon back, Turn around and hands on head, Captor judgement, Spawn staging test.

Offline checks done: Release 0 warnings; DLL loaded in PowerShell 5.1: 23 presets build, every
param wraps/clamps; ClampReach over 4000 random targets keeps the elbow 25-160 deg; 41 new config
keys clean; OrderKind order keeps menu digits 1-8.

### In-game test plan (0.16.0)

Log decisions, Log gear decisions, Log judgement, Log kneeling on (Verbose for staging details).
1. Staging: "Spawn staging test" each mode. Normal: `profile requested` for all at +0.0, `held
   until everyone is ready`, `everyone ready after ... released together; stage timings`; nobody
   moves before. Slow: same, 8 s later. Fails / cancel: `setup failed ... (stage: ...)`, report
   `spawn stages`, no bot left standing frozen, nothing spawns afterwards. Then "PMC squad captures
   a bot": all three PMCs present before the prisoner gives up.
2. Gear: "Captor disarms and takes the gear", each answer, squad 1 and 2, turn Yes/No: the weigh
   lines (value, slot/bag, risk), `goes to take`, pick-up animation, `took ... into his ... slot`
   (check his kit), or later / leaves; nothing of his own dropped; with squad 2 more "takes it now".
3. Kept weapon: "Prisoner keeps a weapon back": holster (captor notices only close), slung (notices),
   knife (never); `demands it again`, the told-again choice; during a fetch the prisoner's
   `opening (his captor is picking up ...)` and, sometimes, `draw ... (kept back for this)`.
4. Turn / hands: "Turn around and hands on head", You and PMC bot, standing and kneeling, each
   order sequence. Walk round him: no turn back; head glances over his shoulder; kneeling turn =
   up, turn, down. Hands on head standing and kneeling: head no longer bowed onto the hands; look
   up at a captor standing over him (up to 16 deg).
5. Judgement: "Captor judgement" each situation with Own roll, then Force mercy / lethal: decision
   lines with every input; lethal -> `DECIDES TO KILL`, `killed with his hands up by ...: as
   decided`, squadmates' restraint unchanged; zero RESTRAINT LEAK.
6. Natural play: bot-vs-bot surrenders over a raid: some prisoners let go, some gear taken, rare
   lethal endings (more with Wreckless/GigaChad captors or after being shot), none from custody
   simply ending.

**0.15.0 (2026-11-19, home PC): kneel twist at its source, facing/attention rules, bot captors
close in and kneel prisoners, observable resistance. Built (Release, 0 warnings) and installed in
H:\SPT4.1.X 2026-11-20 with `dotnet build -c Release -p:DeployToSPT=true` (no dist zip, no GitHub
release); committed + pushed to main. Never run in game; the pose fix is a code-level diagnosis.**

From Joel's 0.14.0 log (LogOutput.log 2026-11-19 20:56): TechHappy (PMC-3) gave `order Kneel ...
not given (out of range)` every second while walking up, then the prisoner logged `kneel: waits
(turning to face his captor)` ~40 times and `never settled`; Joel's own Kneel then worked.
- ENGINE (verified, 0.16.9 decompile): MovementContext.Rotation setter / MovementState.ProcessRotation:
  a bot's root turns only when look yaw minus root yaw (HandsToBodyAngle) exceeds TrunkRotationLimit
  (EFTHardSettings.HANDS_TO_BODY_MAX_ANGLE 45; AI snaps at once); below that
  PlayerAnimator.SetDeltaRotation(HandsToBodyAngle) makes the body animator twist the trunk toward
  the look. BotSteering keeps steering to the last LookToPoint every fixed update.
- Why the AI kneel never started: Kneel.FacingSettled wanted the ROOT within 15 deg of FaceTarget;
  the root stops up to 45 deg short (the trunk twists instead), and FaceTarget moved as the captor
  walked. Now: Kneel.Request(s, by) fixes KneelFaceDir toward the issuer once; the layer looks along
  it (Kneel.PreTurn) and Kneel.TurnRoot turns the root at 150 deg/s with MovementContext.ApplyRotation
  (the game's own root call); settled within 10 deg, or after 2.5 s he kneels the way he faces. Facing
  is never a fallback reason any more.
- Twist sources found (all fixed, each switchable in 14. Debug "Kneel pose isolate"):
  1. LookAlign: while kneeling the steering target stayed where the captor stood when he went down:
     animator trunk twist up to 45 deg vs planted hips. SurrenderLayer.KeepLookOnBody: while any part of
     the kneel has his legs (not the rise pivot), LookToDirection(KneelAnchorFwd) -> HandsToBodyAngle ~0.
  2. PelvisSquare: ApplyBody's pelvis correction was a swing (FromToRotation hips->head): pelvis yaw
     stayed the animator's; legs solved in the planted frame twisted against it. Pelvis yawed so the
     hip line = body right.
  3. TorsoSquare: shoulder line squared to the hip line (Spine1/Spine3 halves, spine axis), so no
     animator twist survives under our look.
  4. 0.14.0 Presence torso share (up to 35 deg from 8 deg) stacked on 1: now kneeling torso <= 12
     ("Kneeling torso turn max"), from 30 deg, head-from-chest <= 55 ("Kneeling head turn max").
  5. LegHinge: legs were swung (Aim) from the animated crouch, keeping its thigh/shin roll. Now each
     leg bone's (along, hinge) is measured from the standing ref (MeasureLegAxes; hinge = hip-knee-ankle
     plane if bent >= 4 deg and within 35 deg of right, else right) and posed as full rotations
     (PoseBone, like ArmPose.PoseArm); bones between hip/knee and knee/ankle (thigh twist) held at the
     ref's local rotation. Squaring eases in over t 0-0.12 (and out at the end of a rise).
  Nothing accumulates (animator rewrites every bone; ArmPosePatch skips frames without _bodyupdated).
- Attention: s.OrderIssuer/OrderIssuedAt recorded (log only); the look target is the captor only while
  in view. Presence.InView: kneeling, within Reach (head + torso) + 40 deg peripheral of the planted
  body; standing always. Out of view: `loses sight of his captor`, faces where he knelt, head 8 deg
  down, a glance over that shoulder (to reach) every 4-9 s; never stands or turns to keep eye contact.
  Layer: s.CaptorInView (logs `is behind him, out of view` / `in view again`); CaptorLastSeen updated
  only in view, or coarsely (>1.5 m moves = footsteps) when present behind. Reactions.Covered(inView):
  behind him = assumed covered unless heard (reload, shots elsewhere, sprinting, weapon switch).
  SquadCovers: an unseen squadmate within 8 m with a line counts as covering (assumed). ReassessEscape
  needs the captor actually in view.
- Bot captors (Captors.cs): orders not tried out of range/line (logged once per step `waits (...);
  walks up first`, walk-up triggered). Kneel only when KneelReady (hands up and empty, captor covering
  him; 10 s then Stay) and Kneel.Suitable (HardChecks + Fits; no fit but reposition on -> still ordered,
  noted); else Stay. Followed once (FollowKneel): `sees X on his knees` / `can't kneel ...; lets him
  stay standing` / 15 s timeout; never repeated. Captor line Kneel = GetInCover (the player's Get down
  trigger), fallback HoldPosition/Stop; the order applies without audio.
- Close-in (MaybeCloseIn/PickCloseSpot, once per custody, "Captors close in on a disarmed prisoner"
  0.55 x0.6 if the prisoner still carries a weapon, x(1+0.15/squadmate), x(1-0.6 hurt)): fixed spot at
  "Close-in distance (m)" 3.5 on the captor's side (0/+-30/+-60 deg), on NavMesh, same level, >= 1.3 m
  from the dropped gun, line to the prisoner, complete path <= 2x+2 m. Walk 0.6, look on chest (gun
  follows). Stops: in position, any threat (no retry), prisoner moved 1.5 m, 12 s, stuck 2.5 s; max 2
  tries. SetMode during it ends it for good.
- Resistance seen, not decided: Captors.Check waits for Reactions.Shows (hands < 0.75, moving, urgent
  rise, something in hands/draw) + 0.18-0.45 s, or 2.5 s if he can't see; the captor layer keeps him
  until then. Restraint.OnArmed (squad) deferred the same way (SurrenderRegistry, <= 1.5 s).
- Openings: chance x range (<= 2.5 m armed 1.3 / unarmed 0.75; <= 5 m 1.1 / 0.9) x posture (kneeling
  0.75, going down 0.5, getting up 0.6) x fire (1 - 0.4 suppression); logged with Log decisions.
  TakeOpening at <= 2.5 m: knife x1.5, draw x0.7. Firing still only after arms down and off the knees
  (SurrenderRegistry hand-off, unchanged).
- Debug: "Kneel pose isolate", "Log pose diagnostics" (1/s: look-to-body angle, root vs knelt
  direction, hips/chest squared by, torso/head yaw, attention, legs hinged/swung). Scenario preset
  "Captor closes in and kneels prisoner" (plan: close+kneel / kneel only / close only / real rolls;
  Captors.DebugPlan).

### In-game test plan (0.15.0)

Log decisions, Log kneeling, Log attention, Log pose diagnostics on.
1. Pose: Ctrl+K a held Scav, stand in front, beside, behind him; leave him 60 s. Pose lines: game
   look-to-body ~0, root ~0 from where he knelt, hips/chest squared by small numbers after the first
   second; no growth over time (accumulation). Then each "Kneel pose isolate" value: the one that brings
   the twist back is the culprit if it isn't gone. Both lead legs (several kneels), rise and walk off.
2. Facing: give Kneel from 30-40 deg off his facing: `turned to face where the order came from`; walk
   round him while he turns: he doesn't follow.
3. Behind: walk behind a kneeling man: `is behind him, out of view`, `loses sight of his captor`; head
   to the front, glance over the shoulder now and then; no torso twist after you; no opening line
   just for being behind (only reload/fire/sprint heard).
4. Scenario "Captor closes in and kneels prisoner", each plan, flat ground then stairs/clutter:
   `close in on ... chance`, `closes in`, `stops closing in: in position`, `puts ... on his knees` or
   `can't kneel there ...; stay`, `sees ... on his knees`; no repeated order lines.
5. Same with Prisoner keeps a sidearm/knife and a defiant personality: opening lines show `range x`
   and `posture x`; on resistance: `his resistance shows (...)` then the captor's `sees ... ` line.
6. Captor squad size 3: squadmates' coverage, a threat during the close-in (`something else needs his
   attention`, no retry). Kill the prisoner / captor mid-approach / mid-kneel; leave the raid.

**0.14.0 (2026-11-19, home PC): bot-vs-bot identity fix, SAIN target filter, captor walk-up, urgent
escapes, weapon-recovery preference, head/torso look split, scenario spawn mag check. Built against
H:\SPT4.1.X, 0 warnings, dist/HandsAreHigh-0.14.0.zip, installed; committed + pushed to main
2026-11-19 (no tag/release). Joel sent one screenshot of a kneeling Scav, hands behind head, head
turned (no comment); no 0.14.0 log reviewed yet, so none of the fixes below is confirmed in game.**

From Joel's 0.13.1 log (LogOutput.log 2026-11-19 19:13): 0 natural surrender rolls in the whole log,
175 checks skipped "his target isn't a living player"; every forced custody went Guard -> Engage
("an enemy") at once; RESTRAINT LEAK only "at trigger", plus hits.
- ROOT CAUSE (verified in the decompile): BotOwner implements IPlayer, and BotSpawner.SetBotAsEnemy /
  BotsGroup ctor register bot enemies as their BotOwner (humans as Player). So EnemyInfo.Person and
  EnemyInfos keys are a BotOwner for bot-vs-bot pairs. Everything comparing to a Player failed:
  natural bot-vs-bot surrender (never evaluated), Captors.Assess (prisoner = "another enemy in
  sight" -> Engage -> layer off -> SAIN fights him -> custody dropped after 12 s -> he goes for
  his gun -> shot), Restraint OtherThreat/Flag/scan/Apply/GoalEnemy+ShootData prefixes,
  Perception.Recognises/Sees/KnownPosition (no recognition beyond 6 m), ScenarioShield (truce
  pairs kept targeting each other, trigger-only), debug Ctrl+[ "isn't fighting a bot".
  Fix: Perception.PlayerOf / Same / InfoAbout (either key) everywhere.
- SAIN keeps its own target (SAINEnemyController._goalEnemy from VisibleEnemies/KnownEnemies) and
  only mirrors it to BotMemory.GoalEnemy: our setter prefix never stopped SAIN aiming at a prisoner.
  SainTargetFilter: prefix/postfix on ChooseEnemy (not SelectEnemy: it indexes KnownEnemies[0])
  takes held-fire prisoners/shielded targets out of both lists for the call, drops a matching
  _goalEnemy, puts the lists back. Only for bots in Restraint.HeldFireBy / shield pairs.
  Trigger guard cone now covers the whole body (max(6, atan(1.1/d))). Hits within 1.5 s of a
  decision whose shooter's last trigger press was before it log "in flight, not a new attack".
- Restraint decisions that only expired (same situation) are renewed, not re-rolled (Ignore is
  re-decided). Rolls happen on real changes only.
- Captor walk-up (Captors.Approach / CaptorLogic.WalkUp): beyond order range or without a line,
  only with ThreatLevel.None, complete NavMesh path shortened to "Guards from (m)" 8, walk 0.8, eyes
  on him; stops on any threat/stuck 2.5 s/25 s. Custody beyond "Guards from up to" 20 m is
  practical when a path exists within "Walks up to a prisoner from (m)" 60.
- Escapes walked: SAIN's SprintLookDirPatch (prefix on BotMover.Sprint) turns a sprint into a walk
  if the bot looks > 20 deg from RealDestPoint, and refuses while SAINLayersActive. Our moves kept
  facing the captor and asked for the sprint once per path. MoveTo(sprint): LookToMovingDirection
  every frame + re-asks while !Mover.Sprinting. Hide from an opening and walking back for a gun
  (> 3 m) now urgent too. MoveDiag logs one line per urgent move at 1.5 s (m/s, mover/body sprint,
  legs, stamina, facing off destination, arms, pose).
- Weapon recovery: scramble / "get his gun" x2.5 within 3 m, x1.6 within 5 m; kneeling penalty
  0.85 (not 0.6) when it's within reach; a squadmate (restraint observer, alive, <= 25 m, line to
  the gun, up to 3 checks) raises exposure to 0.6.
- Head (Presence): yaw measured in the kneel's planted frame (KneelAnchorFwd) while kneeling; with
  hands behind the head the torso carries a wide look (dead zone 25->8, share 0.4->0.75, limit
  TorsoAssist x2, max 35) about the spine's own axis, split Spine1 40% / Spine3 60%; neck share
  <= 25 deg; up 12 deg; captor beyond reach + 12: settles at 85% of reach, 6 deg down.
- Scenario spawn mag check (scenario bots ONLY, Joel 2026-11-19): vanilla BotMagazineChecker's
  _nextPosibleCheckMagazine starts 0 (first PatrolMoveSimple update checks at once); SAIN
  Firemode.CheckMagTimer/NextCheckTimer start 0 (FirearmsAnimator.CheckAmmo on its first no-enemy
  update). SpawnQuiet schedules both as if just checked (vanilla: GreateRandom(CHECK_MAGAZIN_PERIOD);
  SAIN: 360 x 0.5-1.5, next check >= 30 s), nothing blocked. 17. "No magazine check on spawn" (on).
- ScenarioOps.RemoveItem executed the removal (ItemManipulator.Remove without simulate) and then the
  transaction threw "Trying to get parent ..." (every ammo preset in the log): simulate: true.
- SAIN preset in use: "Default [Modified]" (BaseSAINDifficulty hard), Difficulty coefs
  VisibleDist 0.7, GainSight 0.95, Scattering 0.75, HearingDistance 0.55, Aggression 0.85,
  GlobalAggression 0.9, AI-vs-AI vision/hearing limits on. Not compared with SAIN's shipped preset;
  not changed. Scenario bots always spawn BotDifficulty.normal (raid PMCs: pmc.json AsOnline).

### In-game test plan (0.14.0)

Log decisions + restraint on, Verbose off. Grep `walks up`, `stops walking up`, `takes him prisoner`,
`spares`, `refuses`, `RESTRAINT LEAK`, `in flight`, `SAIN target filter`, `move after`,
`Scenario spawn:`, `-> surrenders`, `no surrender check`.
1. Startup: `SAIN target filter on`. Natural: PMC captures Scav preset in Natural mode (or Matched
   off), several runs: Scav check lines now show `vs <PMC> (bot pmcUSEC, armed)` rolls, no "isn't
   a living player"; on a surrender the PMC logs a decision; on take: no `Guard -> Engage (an
   enemy)`, `walks up` if > 14 m, orders follow, zero RESTRAINT LEAK.
2. PMC squad captures a bot (forced): squadmates `holds fire (squad)`, no squad volley, zero leaks.
3. Captor comes under fire: `Guard -> Cover` or Engage; a walk-up in progress `stops walking up ...
   something else`; prisoner not shot by the captor's squad.
4. Kneeling prisoner, circle him (and with a bot captor walking up): upper back turns with the head,
   hands stay on the head, no neck corkscrew; behind him: head settles short, no pinned stare.
5. Openings with the gun 1-3 m / 8 m / gone / unreachable, standing and kneeling: decision log shows
   `scramble (within reach)` weights; pick-up, draw, AI hand-off.
6. Escapes standing and kneeling: `move after 1.5 s: N m/s ... mover sprinting, body sprinting,
   facing ~0 deg`. If NOT sprinting: the line says why (leg, stamina, facing).
7. Spawn any scenario: `Scenario spawn: <bot>: no magazine check at spawn (vanilla: first check in
   N s; SAIN: first magazine check in N s ...)`; no mag-check animation; he can shoot, reload, drop
   on surrender at once. Raid bots: unchanged (they still check at spawn).
8. Combat comparison (Joel): same map/spot, F12 Hands Are High Enabled off vs on with no encounter,
   then after an encounter: bot-vs-bot fights should look the same. Matched Scav and PMC keeps its
   truce by design (both fight YOU); they now ignore each other instead of aiming without firing.

**0.13.1 (2026-11-19, home PC): PMC scenario spawn fix + arm solver rewrite. Built against
H:\SPT4.1.X, 0 warnings, dist/HandsAreHigh-0.13.1.zip, installed; uncommitted; never run in game.**

Scenario spawn NullReferenceException (Joel's log, scenarios #6, #18-#28, every PMC):
- Trace: `BotProfileClient.CreateProfile` IL_0232-023b `profile_0.GetAllPrefabPaths` on a null
  profile, from `BotCreationData.Create` <- `ScenarioSpawner.RequestAsync`. Scavs fine.
- Cause (verified in the decompiles): SPT's server sends every Bear/Usec bot with Info.Side
  "Savage" (`SPTarkov.Server.Core BotController.TryGenerateSingleBot`); the game's
  `GetProfileDataParams.ChooseProfile` keeps only profiles whose Side + Role + BotDifficulty all
  equal the request; SPT's `PmcBotSidePatch` (postfix on `BotCreationData.ChooseProfile`) sets
  Bear/Usec only on the profile already chosen. We asked for side Bear/Usec -> no match -> null.
  Vanilla `BotSpawner.DebugSpawnAnyway` asks for Savage too. pmc.json useDifficultyOverride is
  false here, so difficulty matches.
- Fix: `ScenarioSpawner.RequestSide` asks Savage for pmcBEAR/pmcUSEC; the spawned bot's side is
  checked and logged. Also: readiness re-checked per request (raid running, spawner, `_gameEnd`,
  core point, zone) with a clear message; `_inSpawnProcess` (feeds `AllBotsWithLoaded`, the
  raid's bot cap) is given back when `ActivateBot` finishes without calling back (cancel,
  SpawnStopped, no position, player not created) or throws before it; our callback can't throw
  into `ActivateBotCallback` (its `--` runs after our callback); a bot the creator made but never
  handed over is removed via LeaveData; one request per participant; `Scenarios.RaidSerial`
  makes answers from an ended raid stale; a second copy of a scenario that is still spawning is
  refused even with concurrent scenarios on (Joel's Enter presses launched #23-#28 together). A
  ChooseProfile miss still logs the stack and now says what didn't match.
- The game's own `_bundlesLoading` counter leaks +1 per such failure (its ++ is before the
  deref); only the debug overlay reads it. Not ours to touch.

Arms (twisted arms, hands not settling behind the head): offline `tools/armsim/` (frames with
roll, not just points). From a realistic start (animator's hanging arm -> ArmPose swing ->
hands-up -> behind the head) the old swing-only `Aim` ended with the forearm ~120 deg rolled
against the upper arm's hinge and the upper arm ~80 deg off its own (hands-up alone ~30); the
wrist was never set (palm 19-56 deg off the skull); the IK target was the wrist bone, so palm
centre/fingers ended 1-4 cm inside the head/helmet; straight-line path projected onto a sphere.
- `ArmPose.MeasureAxes`: each arm's elbow hinge in the upper arm's and forearm's local space,
  from the game's pose (elbow 20-160 deg, before any of our writes; ~10 s of tries, then swing
  fallback). `ArmPose.PoseArm`: full rotations from (direction, hinge), roll weight eased in
  (hands-up 1; behind the head over h 0-0.4; brace with the reach). Used by the hands-up pose,
  `Kneel.ApplyHands` and `Kneel.ApplyBrace`.
- `ArmPose.SetWrist` (the brace's wrist code, shared): fingers/thumb/palm from the live finger
  bones, bend <= 80, a third of the roll on each twist bone. Hands-up: palms to the captor,
  fingers along the forearm, thumbs to the head (new: the palm used to face wherever the swings
  left it). Behind the head: palm centre on the back of the skull, fingers round towards the
  middle, thumb down, eased over h 0.35-1. Finger bones found once per surrender
  (`ArmPose.HandsOf`), so this works standing too.
- Path: quadratic curve in skull space past the ear (>= 0.22 m out) to the back; min radius kept.
- Offline (armsim2.py, mirrors the C#): twist 0, no elbow flips, clear >= 3.7 cm (helmet) /
  4.4 cm, wrist <= 41 deg, reach <= 0.76, at 20/30/60/144 fps, reversal at h 0.5, head sweep
  -38..38 deg while holding, idle elbow settle. Not checkable offline: the real rig's local axes
  (measured live), which side of the palm the thumb bone marks (Kneel brace palm flip mirrors
  it), how it reads in game.

### In-game test plan (0.13.1)

Debug keys, Overlay, Log kneeling, Verbose logs on (14. Debug). Grep `spawn`, `hinge measured`,
`finger bones`, `Scenario`.
1. Scenario menu: Matched Scav and PMC, Captor reloads or looks away (PMC captor), Third party
   (BEAR/USEC): `PMC spawned after ... (..., pmcBEAR/pmcUSEC, Bear/Usec)`, no `(side ..., expected
   ...)`, no exception. Press Enter several times on one: second copy refused while spawning.
2. Launch one, clean it up (or leave the raid) before the bots arrive: `arrived after ... removed`
   or nothing at all after a raid change; no exception; later raid bots still spawn normally.
3. Each surrendered bot logs `left/right arm hinge measured`. Hands-up from front/side/rear:
   palms to you, no corkscrew at the elbows or wrists. Compare with 0.13.0 if anything looks
   worse (Forearm twist in 12. Arm pose is still applied).
4. Ctrl+K, watch both arms from front, side, rear at 60 fps and capped 30: hands go up past the
   ears and round to the back of the head, elbows out, no twist on the way; palms on the back of
   the head, fingers towards the middle, thumbs down, nothing inside the head; helmet on/off,
   plate carrier + backpack. If palms face outwards: Kneel brace palm flip (also mirrors brace).
5. While down: circle him (head turns), wait for idle settles: hands stay on the head, no twist.
6. Shift+K (brace Always), Ctrl+Shift+K interruptions, Ctrl+K while rising, `]` escape: arms leave
   the head the way they came, brace hand lands flat, no flick; kill him mid-move: vanilla ragdoll.

**0.13.0 (2026-11-19): code complete, reviewed, built against H:\SPT4.1.X with 0 warnings
(dist/HandsAreHigh-0.13.0.zip), installed in H:\SPT4.1.X; never run in game.** Eight-part request
(kneel spot, rise-to-walk, head attention, interrupted escape, injury-aware kneel, kneeling idle,
flinch, demeanor). Next: Joel runs the 0.13.0 plan below.

Finished on the home PC (2026-11-19), after the work-laptop commit:
- Review of Presence.cs, SearchSpot, ReassessEscape. Signs checked: SignedAngle(body, look, up)
  and AngleAxis(yaw, up) agree; +pitch about Cross(up, fwd) = right is head down; flinch turns
  away from the side the round passed; neck/head split leaves the head at exactly q x animated.
  SearchSpot: the 8 angles are distinct (90, 45, 135, 0, 180, -45, 225, 270), rings nearest first.
  Fixed: (a) HeadAxisOk was reset per encounter and skipped while kneeling: a man recaptured on his
  knees had no head look until he stood; now measured once per bot (it is a rig property);
  (b) stale Yaw/Pitch from the last encounter turned the torso (assist) on the measuring frame,
  skewing the axis for good: look state is zeroed while the look is off, and the axis is not
  measured on a frame with torso assist or a flinch; (c) a hit during an escape from the knees
  left KneelEscape set, so ReassessEscape could call off a bolt caused by a hit (design: hits are
  never reconsidered): the hit handler clears it.
- Offline check (scratchpad free_check.py; the kneel_sim2 / brace_sim scripts stayed on the work
  laptop): the hand-off points come from the windows alone. At each style's Free t the lead window
  is 0.26-0.27 (hips ~11-12 cm under standing with a ~0.44 m drop), the trailing knee window is 0
  (knee up), and Free is the first t where that holds (0.203/0.170/0.142/0.203/0.203/0.243). Brace
  at hand-off: defensive 0, escape 0 (moved out-windows end above Free); compliant 0.76, released
  by UpdateBrace's "about to walk" latch (0.10 s) as soon as MoveTo asks for a walk. The solver
  itself is unchanged since the 0.12.1 sims.
- "Crouch when kneeling is impossible" default is now false; Joel's cfg had no entry yet, so he
  gets false (stays standing).

0.13.0 contents:
- Kneel spot (Kneel.cs TryBegin/SearchSpot/SpotWalk/SpotFailed; layer HandsUp Order.Kneel):
  Request only refuses what no step fixes (HardChecks: rig, prone, simplified skeleton, both legs
  broken/destroyed). At start: Fits = ground + room (vest widens torso capsule, backpack capsule
  behind, helmet raises top) + front-step room for either leg. Not fitting: one search, 4 rings
  (0.25/0.45/0.7/1 x "Room search radius" 2 m) x 8 angles, 6 candidates/frame, heavy budget;
  candidate must be on NavMesh at his level, >= 1.5 m from captor, in captor's sight if seen,
  standing capsule clear, short complete path (<= 2.5x straight + 1 m). Walks there (MoveTo, hands
  up, no sprint, facing captor; Phase stays HandsUp so captors/Restraint see hands up), arrive
  0.3 m, stall 2 s / 6 s limit; max 2 walks per order; else Fallback with menu note. Waits for
  facing within 15 deg before starting.
- Rise into walk: KneelStyle.Free per style (hand-off t when walking: steady 0.20, frightened
  0.17, one-knee 0.14, compliant 0.20, defensive 0.20, escape 0.24; was 0.15 urgent-only).
  Compliant rises also hand off early when the layer wants to walk (Kneel.WantsToWalk from MoveTo;
  FadeWalk 0.18). BUG FIXED: MoveTo set NextMoveOrder = 0 while legs locked, so Retreating re-ran
  the retreat search every frame of the rise; now -1. MoveTo asks SetPose(1) while locked (was 0:
  crouch dip at hand-off). Pivot: last 0.12 t before hand-off, body turns toward travel via
  BotSteering.LookToDirection(dir, 140 deg/s), clamped 70 deg from the feet; legs solved in the
  kneel's frame (FeetFrame / KneelAnchorFwd) so feet stay planted. Rearmed uses the same pivot.
- Interrupted escape (SurrenderLayer.ReassessEscape, MarkKneelEscape): run/hide/scramble taken
  while kneeling (and debug force run/grab); captor covers again (Reactions.Covered, reaction
  delay) during the rise or 0.6 s after hand-off -> one decision: carry on / back down (first 55%
  of rise only) / stand hands up, weighted by progress, nerve, personality, kind, distance,
  injury; 60% freeze 0.35-0.8 s first (KneelHoldUntil). Clears walk, sprint, gun claim (scramble
  cooldown), hide, grace; ComplyUntil >= 4 s; Kneel.Recomply continues from current t. Draws and
  hits never reconsidered. Debug: "Kneel test escape outcome".
- Injury (Kneel.ReadLimbs): per-leg hurt from leg health, heavy bleed 0.4, broken 0.7 (game's
  IsBodyPartBroken, same as its LegDamaged limp), destroyed 1; arms broken/destroyed. Hurt leg
  leads (healthy front leg carries him), no side swap when favouring; leg >= 0.5 -> frightened
  x0.15, one-knee x2. Down/rise time use max(injury, front leg), not a product (old LegsBad x1.5
  removed). Brace: no brace with a bad front-side arm (also let go mid-rise), chance x(1+front
  hurt), >= 0.85 when front >= 0.5. AdaptToWounds every 0.5 s mid-transition: slower only.
- Presence.cs (new): demeanor Resigned/Frightened/Cautious/Defiant per encounter (weights logged;
  steps one worse on wound +0.25 / heavy fire / warning shot, back after 25 s calm; min 12 s
  between changes); affects descent weights/times, compliant rise time, brace chance, tremble,
  chatter (plead/defiant), glances, idle, flinch. Head: smoothed yaw/pitch to captor or glance
  (gun he believes is there, fire origin < 8 s, squadmate in sight, way out, ground), limits
  config, half yaw / 5 deg up with hands behind head, torso (Spine3) assist up to 15 deg; neck
  40% / head 60%; head forward measured once per surrender from the animated pose. Kneel hands
  follow HeadDelta round the skull. Idle while Down: hip shift + elbow settle on random timers;
  no extra breathing. Flinch: Suppression.OnShot -> Presence.OnShot for any shooter, near miss
  past the head, cooldown 0.7 s + habituation; cancelled by hits. Config section 18.
- Version bumped to 0.13.0 in csproj + plugin.

### In-game test plan (0.13.0)

F12: 14. Debug: Debug keys, Overlay, Log kneeling, Kneel gizmos, Log attention on. 18. Attention
and demeanor at defaults. Grep `kneel:`, `demeanor`, `glances`, `flinches`, `covered again`,
`steps`, `couldn't reach`, `lets go of his knee`.
1. Kneel spot: `[` a Scav with his back to a wall or in a tight doorway, Ctrl+K: `no room where he
   stands`, then `steps N m to a spot with room`; he walks there hands up, no sprint, facing you,
   then kneels. Menu row reads "stepping to room to kneel". In a closet / dense clutter: `no room
   to kneel within 2.0 m`, he stays standing, hands up (menu says why).
2. Rise into walk: kneel him, then menu Follow (or walk off with him following): he stands and
   walks off in one motion, body turns toward you as his knees leave the ground, feet don't slide,
   no crouch dip at the first step. Log: `legs to the walk at t 0.20` (0.14-0.24 by style).
   With Kneel test brace Always + rise Compliant + Follow: `lets go of his knee early (he's about
   to walk)`, no hand flick.
3. Interrupted escape: kneel him, `]` (opening) and look away; when he starts up, aim at him
   again. Log: `covered again NN% of the way up (...)` with carry on / back down / stand weights,
   sometimes `freezes N s first`. Force each outcome with Kneel test escape outcome: carry on (he
   keeps going), back down (reverses from where he is, no restart), stand (finishes rising, hands
   up). Shoot him while he flees from his knees: he keeps running, no `covered again` line.
4. Injury: shoot a bot's leg (blacked or broken), `[`, Ctrl+K: `favours his healthy ... leg`,
   slower down/up times; break the front-side arm: no brace. Both legs broken: `can't kneel (both
   legs too badly hurt)`.
5. Head and demeanor: several surrenders; each logs `demeanor X (weights ...)`. Head follows you
   as you circle him (standing and kneeling), turns at most ~70 deg, torso helps past 25 deg;
   glances at his dropped gun, his squad, the way out, the ground, back to you. Recapture a bot
   while he is kneeling: head still tracks (0.13.0 fix). Defiant: chin up, few glances;
   frightened: head down, quick glances.
6. Kneeling idle: leave him kneeling 30 s: small hip shifts and elbow settles, never in step with
   another kneeling bot; knees stay on their marks (K gizmo).
7. Flinch: fire past his head (within 2 m, missing): `flinches (... passed X m away, left/right)`,
   head ducks away from that side, shoulders hunch, settles in ~0.5 s; full-auto: weaker each
   time, no constant shaking. Hit him: no flinch line, he reacts to the hit.
8. Profile on with 4 held bots (2 kneeling): no `Profile spike:`.

**0.12.0 (kneel styles, rise intents, knee brace, no crouch look), built against `C:\HUH` 2026-11-19
(dist/HandsAreHigh-0.12.0.zip), 0 warnings; committed and pushed to `main` 2026-11-19 (0.11.0 in the same commit); not installed, never run in game.**
0.5.0-0.12.0 are all unverified in raid. No GitHub release. Preview of the motion (same solver,
stick figure): https://claude.ai/artifact/BmomFjtD7GVoPCYSgKN5Aj

**0.12.1 (brace rework + transition fixes), built against `C:\HUH` 2026-11-19
(dist/HandsAreHigh-0.12.1.zip), 0 warnings; committed and pushed to `main` 2026-11-19; not installed, never run in game.**
Preview updated to 0.12.1 (same link as above): brace 0.12.1 vs 0.12.0 toggle, speed 1x-1/10x, hand path,
fingers drawn, brace reach/elbow/wrist readouts. Its standing reference was wrong before (hips 0.93 m over
ankles 0.08 m = ~44 deg knee bend); now 0.993 m (~8 deg). In game nothing of ours bends a standing bot's
legs (Stance with hands up = 1); a compliant rise ends on the captured standing pose and blends to the
animator over 0.2 s; urgent rises hand the legs back bent at t 0.15 on purpose (the walk takes over).

Next: Joel runs the 0.12.1 plan (below; it replaces the 0.12.0 plan's steps 2-3), then 0.10.0
(scenarios), then the older plans.

### 0.12.1 (brace push-off, transition audit; Kneel.cs, ArmPose.cs, SurrenderRegistry.cs)

Bugs found by reading the code + offline port (scratchpad brace_sim.py on kneel_sim2's solver):
- Brace snapped: reach and let-go each 0.1 of t (70 ms escape), straight line hands-up -> knee;
  and a reach gate `(1.04 - ratio) / 0.06` multiplied the weight: the shoulder-knee distance
  crosses that band in 1-2 frames, so the hand jumped 36-66 cm in one frame. Contact point was
  offset by world up (slid along the thigh as it tilted); wrist straight: fingers 2-6 cm inside
  the thigh, forearm ~1 cm from it. Let go at ~10-26% of the push.
- Reversal while rising cleared KneelBrace (hand snapped off); end of rise cleared it too.
- ArmPosePatch posed on frames the body animator didn't run (`_bodyupdated` false, e.g. bots
  in the fixed-update queue at high fps): partial weights blended twice -> creep then jump.
- Changing urgency / reversing jumped t's speed (velocity step, up to 2.4x).
- t advanced before the standing pose was captured (capture allowed up to t 0.2: first posed
  frame could jump); never captured -> crouch look.
- Escape from the knees: arms from behind the head in the 0.08 s "Fast lower time" (~5 frames).
- A draw starting mid-brace would fight the draw animation for the arm (no let-go).
- Rig transforms destroyed -> exception spam; stale rig reused at the next kneel.
Fixes: brace = quadratic arc from the arm's current pose (out to the side, landing from above),
contact on the thigh's top surface rebuilt per frame from the solved leg (BraceUp 0.15 m,
ThighRadius 0.075), wrist laid flat (fingers to the knee, thumb in; finger bones Digit31/Digit11
found by name, none -> no brace), roll shared with Forearm2/3; windows: compliant in 0.88->0.50
out 0.28->0.03, defensive 0.78->0.44 / 0.28->0.17, escape 0.78->0.48 / 0.31->0.18 (contact at
0-3% hips risen, hold to ~45-50%, reach <= 0.92, elbow straightens 89->47 compliant). Early
let-go latch (`KneelBraceGate`, 0.10 s): draw/drop/hands pending, IK goal beyond the arm past
contact, urgent switch on compliant timing. Leftover brace fades with KneelFade. KneelVel eased
(0.06 s) on retime/reversal; fresh starts at full speed. t held at 0 until the capture (0.5 s
limit -> fallback). Arms lower >= 0.25 s while kneeling (`Kneel.MinLowerSeconds`). ArmPosePatch
returns on !_bodyupdated. `KneelRig.Valid()` checked per frame while posing.
Offline (brace_sim.py, approximations for shoulder/hands-up): compliant hand peak 4.0 m/s
(old 22), fingers >= +1.4 cm / forearm >= +4.5 cm clear of the thigh, torso >= 7 cm, wrist 59-63
deg; urgent peaks ~9 m/s are the arms coming down, not the brace. Not checkable offline: real
shoulder/arm pose, which side of the palm bone is the palm (thumb-root inference; config
14. Debug "Kneel brace palm flip" if wrong), thigh thickness with kit, how it reads in game.

### In-game test plan (0.12.1)

Debug keys, Overlay, Log kneeling, Kneel gizmos on. Grep `kneel:`, `lets go of his knee`.
1. Kneel test brace Always, rise Compliant, Shift+K; watch side, front, rear at 60 fps and capped
   30: hand sets off as the hands leave the head, arcs out, lands on top of the front thigh (H at
   the K mark's thigh) before the hips rise, stays put on the thigh while he pushes (elbow
   straightening), lifts off around half-way up, goes back up to hands-up. Palm down, fingers
   towards the knee, no fingers in the thigh, no twisted wrist. Palm up -> set palm flip, report.
2. Same with Defensive and Escape (Ctrl+Alt+K): no pause before the first step; brace gone
   before he walks; arms come down from the head in ~0.25 s, not a flick.
3. Ctrl+K while rising with the hand on the knee: hand comes back off the way it came, no snap.
4. Kneel test interruption Draw / Run at GettingUp 0.5: `lets go of his knee early (...)` once,
   no arm fighting the draw; Kill at 0.5: ragdoll from the pose, no further `kneel:` lines.
5. High fps (uncapped) vs capped 30: no shimmer of the raised arms on a held standing bot.
6. Different kit: plate carrier + backpack, rig, no armour; helmet on/off: hand on the thigh,
   not inside pouches or plates (report which kit clips).

### Notes after 0.10.0 (2026-11-19, nothing built)

- **Raids with bots off**: `BotsController.Init` builds `_botSpawner` unconditionally; bots off
  only clears `_canSpawn`, which gates the controller's own spawn calls (and its OnKill /
  OnBeingHit / grenade event hooks, so some vanilla group reactions are missing). The scenario
  spawner calls BotSpawner / IBotCreator directly and skips MaxBots, so it should work there;
  unverified whether SPT's server returns profiles in such a raid (log: `spawn failed: the
  server returned no ... profile`).
- **Hot reload**: Joel declined (2026-11-19). Teardown supports it, but BigBrain layers attach at
  brain activation and can't be removed: bots spawned before a reload keep the old (inert)
  layers. No ScriptEngine on C:\HUH.
- **Kneel + hands behind head**: built in 0.11.0 (below). The note that was here called
  LeftThigh2 the calf: wrong, it is a mid-thigh twist bone (see 0.11.0).

### 0.12.0 (kneel polish: Kneel.cs)

- No crouch look: 0.11.0 solved the legs from the animated crouch, so he squatted first. Now his
  standing pose is captured body-local at the first frame down (`CaptureRef`, `KneelRef*`) and
  pelvis/torso/legs are solved from it at full weight for the whole kneel; the game's crouch
  (capsule only) is requested at t >= 0.35 going down, the stand at t <= 0.50 rising. Hand-back
  after a rise: `KneelFade` (0.2 s compliant, 0.14 s urgent) from our pose to the animator's.
- Choreography: lead foot swings back, lead knee down; the other foot steps forward (lifted arc,
  `KneelStepNow`) into a half-kneel; front knee down beside. Rising: front foot planted, push up
  over it, foot steps back. Styles (`KneelStyle`, windows in t, chosen once, kept until the end
  or an interruption; reversal/urgency change only retimes): descents Steady 2.3 s, Frightened
  1.3 s (short step, knees nearly together, hands up early, hunched), One knee first 2.8 s (pause
  in the half-kneel); rises Compliant 1.7 s, Defensive 0.9 s, Escape 0.7 s. Descent weights:
  config x nerve x personality (Coward/Timmy frightened, Chad etc. steady, Rat one knee) x wounds
  x heavy kit (backpack + armoured vest). Lead leg: per-bot side 80%, swapped/shortened when the
  front step has no room (`StepRoom`). Small per-bot posture offsets (thigh angle, lean, spread)
  from the seed.
- Rise intent: `Kneel.Urge` from BeginRetreat (emergency -> Escape, let go -> Compliant),
  StartScramble/BeginHide (Escape), Rearm (Defensive); unspecified urgency -> Defensive. Speed x
  (1 + k x injury) x 1.5 bad leg x 1.08 heavy. Urgent rises hand the legs back at t = 0.15 (path
  starts at once, walking); `SprintAllowed` holds the sprint until the 0.14 s blend ends
  (`SprintHeld`).
- Knee brace (`ApplyBrace`, after ApplyHands): front-side palm on the front thigh 0.10 m above
  the knee (rides on the bone, no sliding), only while the front foot is planted and the hips
  start up; +16 deg lean while braced; eases off past 1.04 of arm reach (logged once). Offline
  reach at full contact 0.78-0.98. Hand on the GROUND not built: kneeling shoulder ~0.9-1.0 m up,
  arm+hand ~0.65 m, only reachable folded almost flat. Chance 0.55 compliant, x1.3 escape,
  x0.45 defensive, never while drawing. Bones only.
- Bot captors: "hold" plans order Kneel instead of Stay at "Captors put prisoners on their
  knees" (0.4); Leave later gets him up calmly.
- Config 12. Kneeling: style times/weights, Stand up / Defensive / Quick stand times, Brace on
  the knee when rising, Brace chance, Transition lean scale (replaces Lean while lowering or
  rising; Torso upright removed). 14. Debug: Kneel test style / rise / brace (force a variant),
  Ctrl+Alt+K urgent rise (Escape = forced run, Defensive = forced draw or SIMULATED hit). An
  opening while kneeling: the existing `]` key.
- Offline (scratchpad kneel_sim2.py): 3 descents x 3 rises x 60/20 fps: nothing under the ground,
  knee 44-129 deg, largest move/frame 4.5-9 cm at 60 fps (escape fastest), reversals at t 0.2/
  0.5/0.8 continuous.

### In-game test plan (0.12.0)

Debug keys, Overlay, Log kneeling, Kneel gizmos on. Grep `kneel:`.
1. Kneel test style = each style, Ctrl+K: no squat before the knees go down; front foot steps
   forward, back knee down, front knee beside; watch side and front, 60 fps and capped 30.
2. Shift+K with Kneel test rise = Compliant / brace Always, Never: hand lands on the front thigh
   (H mark at the K mark) and stays on it while he pushes up; ends in hands-up, no pop at the end.
3. Ctrl+Alt+K Escape and Defensive: rise in ~0.7 / ~0.9 s, first step without a pause, sprint
   ~0.14 s later, no sprint cycle on the knees; draw finishes, AI hand-off after the blend.
4. `]` while kneeling: decision log shows kneeling weights; if he bolts: `Escape rise`.
5. A wounded bot (thorax, bleeding): slower rises; a heavy PMC (backpack + plates): style log.
6. Bot captor with a Scav: `holds ... plan`, some `ordered Kneel by <PMC>`; on release he rises.
7. Interrupt tests (Ctrl+Shift+K) at 0.2/0.5/0.9 both ways; Kill mid-rise: ragdoll, no stand.

### 0.11.0 (kneel, hands behind the head; src/HandsAreHigh/Kneel.cs)

Engine facts checked first (decompile of C:\HUH Assembly-CSharp + skeleton.bundle via UnityPy):
- No kneel/sit/get-up clip in player_anim_controller (0.10.0 search). So procedural, on top of the
  game's own crouch: `BotOwner.SetPose(0)` -> BotMover eases PoseLevel (0.2 of the gap per frame);
  `MovementContext.SetPoseLevel` sizes the capsule `Lerp(1.2, 1.6, pose)`, so the kneeling bot has
  the game's 1.2 m crouch capsule (head ~1.3 m kneeling). No collider, capsule or position is ours.
- `Player.Grounder` (FinalIK GrounderFBBIK) is disabled in `InitVisuals`; FBBIK/LimbIK only work
  the hands. Nothing re-poses the legs after the animator, so the VisualPass postfix is the last
  writer; the animator rewrites every bone next frame (nothing accumulates). `_bodyupdated`
  (protected, FieldRefAccess) is false on frames the body animator didn't run: the body pass is
  skipped then (re-posing our own output would creep). `UsedSimplifiedSkeleton` is set at creation
  (infected), not a distance LOD.
- Skeleton: `Base HumanPelvis` (0.928 m bind) -> `HumanLThigh1` (hip) -> `HumanLThigh2` (mid-thigh
  twist, 0.228 m) -> `HumanLCalf` (knee, +0.228) -> `HumanLFoot` (ankle, +0.46) -> `HumanLToe`
  (+0.155). PlayerBones.LeftThigh2 is NOT the calf. Rig found per surrender by these names under
  PlayerBones.LeftThigh1/RightThigh1; lengths measured live; any missing bone -> fallback, logged.
- `MovementContext.CanStandAt(1f)` checked when he starts rising (logged if a ceiling will keep
  him crouched; the game's own ChangePose refuses to stand there anyway).

Pose (all in ArmPosePatch, one owner per bone, in order): `Kneel.ApplyBody` (pelvis turned upright
+ lean, pelvis to kneeling height, head keeps the game's gaze, each leg: FK slerp from the
planted-foot two-bone solution to knee-on-ground / shin back / toes tucked, with knee/foot/toe
never under the cached ground) -> `ArmPose.Apply` (hands up, torso frame) -> `Kneel.ApplyHands`
(two-bone arm IK from the hands-up palm along a path pushed out around the skull + helmet, elbows
out). One progress t: 0-0.12 take-over, 0.10-0.55 pelvis + lead knee (half-kneel), 0.45-0.82
trailing knee, 0.15-0.80 lean bump, 0.70-1 hands. Rising = same path backwards (reversal is
continuous). Toes tucked, not instep-down: instep-down turns the foot through straight down near
the ground and the offline check showed it flipping 30 cm in one frame.

State (SurrenderState.Kneel*, Kneel.Update from Registry.Tick): Standing / GoingDown / Down /
GettingUp. The order sets only `Order`; Update moves t towards it, so repeats don't restart and a
reversal continues from where he is. Begin waits for arms up, hands not busy, not moving, no
interaction (6 s limit), then static checks: legs not destroyed, rig, ground (4 raycasts: knee and
foot points, each within "Max ground step" and "Max slope"), room (shin capsules, elbow spheres,
kneeling body capsule; static mask only). Fail -> `KneelFallback` (reason logged and on screen)
-> crouch with hands up (or standing, config); cleared by a new order. Ground re-checked only if
he is moved > 0.15 m or turned > 10 deg (max 2x/s); unsuitable -> gets up. Urgent (any phase
but HandsUp: shot/bolt, run, scramble, draw, hide, let go, mod disabled): quick rise (0.6 s), no
reversal; `LegsLocked` (t > 0.14) blocks MoveTo (no step, no sprint, progress timers reset),
facing and Regroup/Hide turning; `Kneel.Pose` keeps the game crouch until t <= 0.28; Rearmed hands
to the AI only once Standing. Draws may start while he rises (hands animator only; nothing new on
the hands controller, so no new busy-hands path). Opening weights while kneeling: run/scramble/
knife x0.6, hide x0.8, draw x0.85 (logged). Gestures refuse while kneeling (and are cancelled at
its start). Death: the postfix checks Dead and the game stops VisualPass: no stand-up, no bone
written, the ragdoll starts from whatever pose he died in. Despawn/teardown: state removed, bones
are the animator's next frame (a snap only on hot reload).

Orders: Kneel (Get down) -> Kneel.Request; new `OrderKind.Stand` (On your feet, menu row only
available while down); Stay keeps a kneeling man down (before it stood him up). Bot captors
never order Kneel. Facing is frozen while any part of the kneel is on (turning slides knees).

Config 12. Kneeling: Kneel on order, Kneel down time 2 s, Stand up time 1.6 s, Quick stand time
0.6 s, Leg pose strength, Torso upright, Torso lean, Lean while lowering or rising, Knee height,
Thigh angle, Knee spread, Hips forward, Hands behind head strength / distance, Hands above neck,
Elbows out, Helmet extra, Max ground step, Max slope, Crouch when kneeling is impossible.
14. Debug: Log kneeling, Kneel gizmos (overlay marks K/F/H), keys (Debug keys on) Ctrl+K kneel,
Shift+K stand, Alt+K loop (Kneel test cycles), Ctrl+Shift+K interrupt test (Kneel test
interruption: Reverse / Hit (SIMULATED, no damage) / Run / Draw / Kill; Kneel test interrupts:
GoingDown / GettingUp; Kneel test interrupt at 0-1). Debug keys go through `Orders.Give(...,
debug: true)` (range/line check skipped, everything else the menu's path).

Offline checks (scratchpad kneel_sim.py, a Python port of the solver with the bundle's lengths):
crouch pelvis 0.40 / 0.55 / 0.70 m, 60 and 20 fps, uneven ground +-0.10/0.12 m, either lead knee:
segment lengths exact, knee/foot/toe never under the ground, knee flex 78-153 deg, largest
per-frame move 3 cm at 60 fps / 8 cm at 20 fps (no jumps); hands never inside the skull sphere
(with and without helmet), final elbows ~0.31 m out at head height, elbow flex ~118 deg. Not
checkable offline: the game's real crouch pose, how it looks, Unity-side state machine timing.

Limits: procedural only (no animation asset), so the look depends on config values that can only
be judged in game; feet are toes-tucked; facing is frozen while kneeling; a corpse or another
player where his shins go is not checked (static geometry only); hand/wrist orientation follows
the forearm (no finger interlock).

### In-game test plan (0.11.0)

Debug keys + Overlay on, Log kneeling on, Kneel gizmos on for 1-3. Grep `kneel:`, `kneel test`,
`ordered Kneel`, `ordered Stand`, `can't kneel`, `Kneel rig`.
1. `[` a Scav on flat ground, Ctrl+K: `kneel: gets down`, ~2 s, `on his knees ... (2.0x s, N
   frames)`. Walk round him (front, sides, behind, close, 30 m+): knees on the ground (K marks at
   the knees), no floating/sinking, toes tucked, hands behind the head, elbows out, no hand inside
   the head; helmet and no helmet; Scav, PMC with plate carrier + backpack.
2. Shift+K: `gets up ... (ordered up)`, foot planted, rises, ends in the normal hands-up pose.
   Repeat Ctrl+K while going down / down: nothing restarts. Ctrl+K while rising: `back down from`.
3. Alt+K (3 cycles): no snaps, no stuck pose, no busy hands; frame-time limit (or low settings):
   timelines show more frames/longer dt, same seconds.
4. Stairs, slope > 20 deg, against a wall (back to it), under a low table, a destroyed leg:
   `can't kneel here (...)`, crouches (or stands with the fallback off), menu says why.
5. Ctrl+Shift+K with Kneel test interruption = Hit / Run / Draw / Kill, during GoingDown and
   GettingUp at 0.2 / 0.5 / 0.9: quick rise (<= 0.6 s) before any step; no sprint while knees are
   down; draw goes out while rising, AI hand-off only after `back on his feet`; Kill: ragdoll from
   the kneel, no stand-up, no later `kneel:` lines for him; Reverse: smooth turnaround.
6. Real opening while kneeling (`]`): log shows `kneeling: on foot x0.6, draw x0.85`.
7. Menu: Get down / On your feet / Stay (stays down) / Follow (rises, then walks).
8. Profile on with 4 kneeling bots: no `Profile spike:`; Pose cost per frame a few us per bot.

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
  Get down = kneel with hands behind the head (Kneel.cs, 0.11.0); On your feet stands him up.
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
