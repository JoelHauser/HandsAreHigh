# Current status

Update this file at every task boundary (replace, don't append a transcript). Use the four levels
exactly: **implemented** / **build passed** / **simulation passed** / **verified in raid** (log or
Joel's eyes). Release history lives in `docs/history/`.

Last updated: 2026-11-21 (0.27.0: fingers laced behind the head).

## State of the code

| Version | Where | Level |
|---|---|---|
| 0.27.0 | main @ b84c866 (pushed 2026-11-21, no tag/release); packed (`dist/HandsAreHigh-0.27.0.zip`) and installed in H:\SPT4.1.X; 0.26.0 DLL + cfg in that session's scratchpad (`backup-0.26.0`) | build passed (Debug + Release, 0 warnings), simulation passed (tools/fingersim/lace.py); Joel: "looks better" in game (2026-11-21) |
| 0.26.0 | main @ 0d1d50e (pushed 2026-11-21, no tag/release); packed (`dist/HandsAreHigh-0.26.0.zip`) and installed in H:\SPT4.1.X; 0.25.0 DLL + cfg backed up in that session's scratchpad (`backup-0.25.0`) | build passed (Debug + Release, 0 warnings), simulation passed (overlap.py, releasesim, abandonsim), ORBIT members checked by reflection on the installed DLL; Joel: "looks better" in game (2026-11-21) |
| 0.25.0 | main @ f2a4d51 (pushed, no tag/release) | ran in one raid: LogOutput 2026-11-21 03:31 (3 player-vs-Scav scenarios) + Joel's screenshot (top glove inside the other sleeve) |

No GitHub release exists for any version. Joel decides commits/pushes.

## Objective in progress

Joel's 12th spec (0.26.0, notes in docs/history/releases-0.26.md): hands behind the head without
clipping (fingers overlap in the middle, palms on their own sides), released men not walked back by a
stale ORBIT objective (dropped at the hand-back), the gun his captor left at his feet picked up first.
All implemented; waiting for a raid.

## What the 0.25.0 raid log showed (verified in raid, log level)

- Hand-off timelines 3.0 s (#1 let go) and 5.2 s (#2 let go, braced rise + hurt leg); an opening
  slip into cover 8.2 s (#2, captor present).
- #1: ORBIT turned him back to his pre-capture objective 3 s after the hand-back; 3 keep-away legs
  each undone by ORBIT; CIRCLED BACK at +46 s (fixed in 0.26.0 at the source).
- Let-go men refuse their own gun ("lies where he was held, off limits"): by design for a Leave
  order (the captor is still there).
- No bot-captor abandonment and no natural surrender in that log.

## What the 0.24.0 raid log showed (verified in raid, log level)

- Working: SAIN layer release, drop timeline (~0.6 s to gun on the ground), "has bullets reads false",
  head rig check, stepping turn ("stepped round 133 deg with the game's turn animation"), kneel and
  rise, hands-up pose numbers, scenario staging/cleanup, ORBIT keep-awake found, SAIN target filter on.
- Fingers: the game's animator writes finger bones every frame (first raid evidence).
- Problems that led to 0.25.0: abandoned prisoners "took it in" 7-10 s (#3, #4, #8); hand-backs came
  11-14 s after leaving captivity; #7 a captor that had walked away was pulled back onto the captor
  layer as a watcher; #6 ORBIT walked a released man back near the capture spot (+84 s, CIRCLED BACK),
  KeepAway then steered him away; released prisoners sat in "SAIN : Combat Layer" targeting the
  ex-captor, sometimes still for 5-14 s (SAIN's own choice; not changed).
- No natural (non-scenario) surrender in that log.

## Needs in-raid verification (nothing below has been seen in game)

0.27.0 (notes in docs/history/releases-0.27.md):
- Laced fingers behind the head (rear/side/front, standing and kneeling, either hand high): fingers
  alternating and across each other, no finger visibly through another, thumbs clear, clean transitions.
0.26.0:
- Hands behind the head (front/side/rear, standing and kneeling, both transitions): no glove in a
  sleeve or through the other hand, top fingers resting on the bottom ones, elbows out and relaxed.
- `ORBIT objective ... dropped` after releases; no `drifting back ... OrbitBrainLayer` / CIRCLED BACK.
- Bot captor walks off leaving the gun: `picks up his X at his feet first`, no shout/turn at the captor,
  then his own AI; covered: `not going for his X at his feet: ... covers him`.
0.25.0:
- Hands one over the other: look on real gloves/sleeves/helmets, front/side/rear; top hand path never
  brushing the bottom hand mid-move; fingers settling as each lands; head still free.
- Abandonment: `notices ... reacts in N s` within ~1-3 s of the captor clearly leaving; no reaction
  when a captor bends for gear beside him; a player aiming at him keeps him put.
- Hand-off: `hand-off timeline` lines; "our layer off (mid-stride)" followed by his AI moving him
  without a stop; Leave by you -> `on his way ... his own AI takes over`.
- F key: opens/confirms; doors/loot/pick-ups never triggered by the same press; holding F doesn't
  repeat; DEBUG take-over of a bot-held prisoner ends the bot's custody cleanly.
- Kneel variants: settled/tense/upright chosen and held; settled refused on slopes/near walls with
  the reason; weight shift before rising; no twisted feet; with hands up and on head.
Older, still open:
- Natural surrender in an ordinary raid: never seen in any log (the only natural-raid log, 0.3.0, had
  rolls at 1-5% that never fired; every later log is scenarios). tools/natsim predicts Scav ~22% /
  PMC ~3% of fast fights with the 0.19.0 defaults.
- Whether SAIN retreats (rather than searches) once HaveBullets reads false; KeepAway vs ORBIT.
- No Profile run exists for any version (all performance claims are by inspection).
- Joel's 0.20.1 report "bots forgot how to fight each other": diagnosed as scenario presets (dry guns
  -> SAIN melee charge, truces by design); open until he compares a raid with no scenarios.
- Looting Bots adapter never run against the real mod; Fika unsupported.

## Next steps

0. Joel looks at the laced hands (0.27.0) from behind and the side; screenshot if anything clips.
1. Joel runs a raid with 0.26.0: a bot-captor scenario that leaves the gun, a let-go, and hands behind
   the head (screenshots from behind and the side); sends LogOutput.log.
2. Read the `hand-off timeline` lines first: the longest step names where any remaining delay is.
3. If fine: a GitHub release only when Joel asks (none exists for any version).
