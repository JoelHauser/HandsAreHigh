# Current status

Update this file at every task boundary (replace, don't append a transcript). Use the four levels
exactly: **implemented** / **build passed** / **simulation passed** / **verified in raid** (log or
Joel's eyes). Release history lives in `docs/history/`.

Last updated: 2026-11-21 (after the 0.25.0 work and the docs reorganisation).

## State of the code

| Version | Where | Level |
|---|---|---|
| 0.25.0 | working tree, UNCOMMITTED; installed in H:\SPT4.1.X (`dist/HandsAreHigh-0.25.0.zip`); 0.24.0 DLL + cfg backed up in that session's scratchpad (`backup-0.24.0`) | build passed (Debug + Release, 0 warnings), cfg bind passed, simulation passed (overhand, kneelsim, abandonsim); **never run in a raid** |
| 0.24.0 | main @ f5f9180 (pushed, no tag/release) | ran in one raid: LogOutput 2026-11-21 02:47, 8 scenarios + Joel's screenshot (stacked fingers behind the head) |

No GitHub release exists for any version. Joel decides commits/pushes.

## Objective in progress

Joel's 11th spec (0.25.0): hands behind the head (interlace -> one over the other), fast reaction
to being abandoned, a prompt hand-off to SAIN/normal AI with a logged timeline, F opens/confirms the
order menu (debug: bot-held prisoners), kneel variants. All implemented; waiting for a raid.

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

1. Joel runs the 0.25.0 checks (docs/testing/in-raid-regression.md items 1-6) and sends LogOutput.log
   + screenshots of hands behind the head and the settled kneel.
2. Read the `hand-off timeline` lines first: the longest step names where any remaining delay is.
3. If fine: commit 0.25.0 (+ this docs reorganisation) when Joel asks.
