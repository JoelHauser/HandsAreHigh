# Releases 0.19.0 - 0.12.0 (notes, root causes, test plans)

> ARCHIVE, not loaded by default. Moved verbatim from the root CLAUDE.md on 2026-11-21 (last
> committed version: git f5f9180; the 0.25.0 section was uncommitted at the time). Nothing below was
> edited: statements here are as written for their release and some are SUPERSEDED. Current facts
> live in docs/engine/ (verified findings), docs/CODE_MAP.md and docs/CURRENT_STATUS.md. "NEVER RUN
> IN GAME" labels below were true when written; see docs/CURRENT_STATUS.md for what has run since.
> Original line range in CLAUDE.md: 681-1477.

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

