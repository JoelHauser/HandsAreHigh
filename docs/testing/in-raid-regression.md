# In-raid checks and log reading

Only a raid proves behaviour. Each release's full test plan is in `docs/history/` (search
"In-game test plan (0.x.0)"); this file is the current, per-subsystem regression set. What has
actually been seen in a raid is tracked in [../CURRENT_STATUS.md](../CURRENT_STATUS.md).

## Setup

- F12 > Hands Are High. Section 14 Debug: "Debug keys" on for debug keys and debug menu authority.
  Logs (7. Debugging, advanced, most default on): Log: decisions, kneeling, escapes, encounter endings,
  hold-fire decisions, gear decisions, incoming fire, squad cover; Verbose only when a line says it needs it.
- Section 17: "Scenario tools" on + a "Scenario menu key" to launch presets (debug sandbox; scenario
  bots are NOT raid evidence: SurrenderStats separates Natural/Demand from Scenario/DebugKey).
- Log: `H:\SPT4.1.X\BepInEx\LogOutput.log` (overwritten each launch; no timestamps on lines). Our
  lines start `[Info   :Hands Are High]`. Unity-side exceptions and silent plugin deaths are in
  `%USERPROFILE%\AppData\LocalLow\Battlestate Games\EscapeFromTarkov\Player.log`.

## Reading a log efficiently

- First: version line (`Hands Are High 0.x.y loaded`), errors (`grep -a "Error\|Exception\|WARNING\|LEAK\|FAIL"`).
- Scenario index: `grep -a "Scenario #[0-9]* .*+0.0s \[info\]: launched"`.
- Follow one bot: every line about him contains his name and, for scenario bots, a tag like `#S3-PMC`
  (`grep -a "#S6-"`). Read his lines in order, not the whole log.
- Bots are named; "you" = the player (`lowlvltest` in Joel's logs).
- Useful anchors: `HandsUp`, `->` (phase changes), `notices`, `left by`, `back to his own AI`,
  `hand-off timeline`, `after release +`, `CIRCLED BACK`, `RENEWED HOSTILITY`, `RESTRAINT LEAK`,
  `PRISONER HIT WITHOUT A DECISION`, `KILL DECISION`, `INCOMING FIRE`, `kneel:`, `stepped round`,
  `fingers:`, `hands-up pose:`, `head rig checked`, `Natural surrender, so far this raid`.
- Lines carry no wall-clock time: order and the `+N s` fields are the timeline. ReleaseWatch and
  scenario lines give relative seconds.

## Regression set (run what your change touches; all for a release)

1. Surrender and hands: `[` (debug) a Scav and a PMC standing, then kneeling. Timeline line ("drop
   asked ... arms up"), gun on the ground, no busy hands, `hands-up pose: wrists ...` (wrists a few cm
   above the head top, forearms ~15-20 deg off vertical). Hands on head <-> Hands up repeatedly and
   mid-move, standing and kneeling, during a turn: no snap, top hand arrives last (0.25), fingers settle
   as each hand lands, no fingers through fingers, head turns freely, helmet on/off.
2. Turn around (standing, kneeling, twice in a row): `stepped round N deg with the game's turn
   animation`, shoulders turn with the chest, no `couldn't finish turning`, no twisted feet after a
   re-kneel.
3. Kneel: Get down / Stand up, each variant (0.25: `kneel: will kneel <variant>`), on slopes, stairs,
   against a wall, with a backpack; `can't kneel here (...)` where expected; rise from settled shows
   `weight forward off his heels first`; urgent rises (Leave, fire) no pause before the first step.
4. Order menu: `/` and F (0.25) open/confirm, digits 1-9, holding a key never repeats, the hotbar and
   doors/loot never react to a menu key; menu closes on look-away / Tab / Esc and the key works again.
5. Bot captor ("PMC captures Scav", "PMC squad captures a bot"): `takes him prisoner`, plan, orders,
   squad `holds fire (squad)`, zero `RESTRAINT LEAK`, gear pick-up animation (`took ... into his ...
   slot`), no frozen captor.
6. Endings and release: captor walks away -> prisoner `notices ... reacts in N s`, `left by`,
   `hand-off timeline` (0.25: total under ~4 s, longest step named), `after release +15..+120 s`
   distance growing, no CIRCLED BACK while unarmed; Leave by you: `on his way ... his own AI takes over`.
7. Incoming fire ("Incoming fire breaks the hold-up", "Captor comes under fire"): `INCOMING FIRE ...
   the hold-up breaks`, captor fights the shooter (not the prisoner), prisoner to cover or his gun,
   no recapture for 8 s.
8. Judgement ("Captor execution check", "Captor judgement"): `intent about`, `DECIDES TO KILL` ->
   `CARRIED OUT`; any `EXECUTION FAILURE` line is a bug report.
9. Squad cover ("Squad cover"): guard spot off the captor's line, `covering`, guard reacts to a draw.
10. Natural raid, no scenarios: fight normally for a raid; send the `Natural surrender, so far this
    raid` summaries. Bot-vs-bot fights should look the same with the mod on and Enabled off.
11. Performance: 14. Debug > Profile on, compare Enabled on/off (cases in the 0.20.1 profiling plan,
    `docs/history/releases-0.20-0.25.md`). No Profile run exists yet for any version.
12. Lifecycle: leave the raid mid-encounter, start another; F12 disable mid-encounter; hot reload if
    ScriptEngine is in use: no stuck poses, no blocked input, counts return to 0.
