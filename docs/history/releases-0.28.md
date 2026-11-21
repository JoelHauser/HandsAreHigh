# Release 0.28.0 (notes, test plan, config layout)

## 0.28.0 (2026-11-21): ignored surrenders, and the F12 menu reorganised

From Joel's pasted spec: a bot may ignore a surrender instead of becoming a captor; clean up the F12 menu.

### Ignored surrenders

- `Captors.TryIgnore`, rolled first in `Captors.Decide` (before take / spare / refuse): chance =
  `Ignore chance` x a factor blended in by `Ignore: personality and situation` (personality: Wreckless
  1.3, GigaChad 1.2, Chad 1.1, Coward 1.4, Timmy 1.2, SnappingTurtle 0.8, Rat 0.6; no SAIN: Scav 1.2,
  PMC 0.9, Boss 0.7; busy with another threat x1.6, else impractical to guard x1.3; beyond guard range
  x1.4, within 8 m x0.7; rival faction x0.7; the man's kit value x1.3 (nothing) .. x0.6 (full)), capped
  0.9. Never a player ally, a man who hurt him (Judgement.MercyFactor < 1), or one his squad decided to kill.
- `Captors.Ignore`: Restraint Respect (target dropped, finger lifted), squad near him informed (no
  custody), `Encounter.Remember` (no recapture / no re-surrender to him or his squad for
  `Ending: no recapture for`), `SurrenderState.IgnoredBy`. No custody, so no orders, approach or gear walk.
- Prisoner: `Abandonment` now also runs for an ignored man (whatever `Notices being left` says), with
  an "ignored" cue (2.2/s when the bot is busy elsewhere, 0.5/s while he still faces him with no orders,
  negative while he walks up), decides within 8 s at most. Still covered (stays put): the ignorer's
  squadmates at gunpoint (`SquadCovers`, capped at 30 s after the ignore), a player aiming his way, or
  any other bot holding fire at him with a gun on him (`ObserverCovers`, new, all abandonment).
- `LeftDecide` -> `IgnoredRecover`: his own dropped gun within `Fetch his gun after being ignored`
  (walking distance <= 1.5x), still there (`Arsenal.Locate`), reachable, not under fire, nobody covering
  (`Endangered`) -> Recovering -> PickingUp -> draw, calmly (`CalmRearm`); else a usable kept gun drawn
  calmly; else a calm walk-off with the quick hand-back. Reuses TryGunAtFeet / the pick-up path / QuickClear.
- `Restraint.OnArmed`: the ignorer and his squad don't turn to Reject when the ignored man calmly takes
  his gun back; anyone else who sees it reacts as before.
- `Truce.cs` at the hand-back (`Truce.Begin`, before Detach/ForgetPrisoner): (holder, other) pairs both
  ways between the man and the ignorer + squadmates holding fire, for `Truce after an ignored surrender`
  (30 s). Read by the restraint patches (GoalEnemy setter, ShootData.Shoot, trigger, SAIN filter),
  `Restraint.OtherThreat`, `Captors.Threat`, the outnumbered count. Ends: a hit between them (both
  ways, self-defence), either held again or dead, the time (a holder not seeing the other has a stale
  GoalEnemy on him cleared). Teardown: `Restraint.Clear` -> `Truce.Clear` (hooks removed).
- Debug: `Key: captor ignores him` (Ctrl+M, debug keys on; M is unbound in Joel's Control.ini),
  `Captors.ForceIgnore`. Raid summary counts "surrenders ignored by a bot".

### F12 menu

- 27 sections (06, 10 and 12 each used twice) -> 7: 1 General, 2 Surrender, 3 Captors and squads,
  4 Prisoner escape and recovery, 5 Commands and dialogue, 6 Animations, 7 Debugging. One file each.
- 437 settings, 99 shown by default; 338 marked `IsAdvanced` (tuning, pose numbers, logs, test tools),
  listed after the common ones of their section (Order). Checked in the installed menus' code:
  BetterBepInEx hides IsAdvanced rows unless the title-bar Show advanced switch is on (a search lists
  them anyway) and sorts by Order then name; stock ConfigurationManager does the same with its tick box.
  Neither can collapse a section.
- Labels shortened with group prefixes for advanced tuning (`Gear:`, `Judgement:`, `Opening:`, `Kneel:`,
  `Log:` ...); descriptions rewritten in plain language (no version numbers or code symbols).
- Consolidated (each pair only ever worked as one setting): Hands behind head: fingers laced + one over
  the other -> `Hands behind head` (Laced / OneOverTheOther / SideBySide); Number keys give orders +
  Numpad keys too -> `Number keys` (Off / TopRow / Numpad); Leaves the captivity area + Escape ends this
  far from the threat (only used as their max) -> `Flee distance from captivity (m)` (the larger value).
- Migration (`Settings.Layout.cs`): before any Bind, every old (section, key) orphan BepInEx kept is
  renamed to its new key (428 rows), merged pairs computed from both old values, and 31 lines of
  settings removed in earlier versions dropped. A new key that already has a value is never overwritten.
  Logged once: "Config layout: N saved value(s) moved ...". One save at the end of Bind instead of one per Bind.

### Checks done

- Debug + Release build, 0 warnings; pack.ps1 name check passed.
- tools/cfgbind/bind.ps1 on a copy of Joel's cfg (465 lines, 10 values he changed): 437 after, second
  load changes nothing; new tools/cfgbind/carry.py: 428/428 values carried, no old line left. Synthetic
  copy with non-default merged values: OneOverTheOther, TopRow, 55 m (max of 35/55). Empty cfg: defaults.
- Not run in game.

### In-raid test plan

1. Launch once; LogOutput: `Config layout: 428 saved value(s) moved ...`. F12: seven sections; your Stop
   demand (F5), Speak your orders, Debug keys, Verbose logs, Log: attention, Key range 40, Scenario tools
   (F5), Scenario: allow several at once still set.
2. Natural: bot-vs-bot surrenders; in the log `ignore chance ... -> IGNORES the surrender`.
3. Forced: debug keys on, `Key: surrender to his enemy` (Ctrl+[) on a bot fighting another bot, then
   `Key: captor ignores him` (Ctrl+M) on the prisoner. Expect: `notices X pays him no attention (ignored
   his surrender)`, `ignored by X: lowers his hands and goes back for his ...` (or `walks off to
   safety`), `Truce: ... leave each other be for up to 30 s`, no shots between them; `Truce: ... ends
   after 30 s`.
4. With the ignorer's squadmate aiming at him: `still covers him: stays put`, then after 30 s `stops waiting`.
5. Shoot-back check: if one hits the other, `Truce: ENDED: ...` and a normal fight.

### Old -> new keys

| Old section | Old key | New section | New key |
|---|---|---|---|
| 01. General | Enabled | 1. General | Enabled |
| 01. General | Bots surrender on their own | 1. General | Bots surrender on their own |
| 01. General | PMCs can surrender | 1. General | PMCs can surrender |
| 15. Bots taking prisoners | Bots can take prisoners | 1. General | Bots take prisoners |
| 13. Other mods | Your teammates can surrender | 1. General | Your teammates can surrender |
| 10. Orders | Orders | 1. General | Order menu |
| 01. General | Bosses and followers can surrender (restart) | 1. General | Bosses can surrender (restart) |
| 13. Other mods | Read SAIN personality | 1. General | SAIN: use personality |
| 13. Other mods | Read SAIN suppression | 1. General | SAIN: use suppression |
| 06. While held | Take control from SAIN | 1. General | SAIN: take control while held |
| 13. Other mods | Pause while a bot is asleep | 1. General | Pause while a bot is asleep |
| 13. Other mods | Keep a bot-held encounter awake (ORBIT) | 1. General | ORBIT: keep encounters awake |
| 26. Release and escape | Disarmed bots know they are unarmed | 1. General | Disarmed bots know they are unarmed |
| 01. General | Layer priority (restart) | 1. General | Surrender layer priority (restart) |
| 15. Bots taking prisoners | Captor layer priority (restart) | 1. General | Captor layer priority (restart) |
| 05. Role and personality | Scav base chance | 2. Surrender | Scav base chance |
| 05. Role and personality | PMC base chance | 2. Surrender | PMC base chance |
| 05. Role and personality | Boss and follower base chance | 2. Surrender | Boss base chance |
| 15. Bots taking prisoners | Surrender chance against a bot | 2. Surrender | Chance against bots |
| 03. Surrendering on their own | Reasons needed | 2. Surrender | Reasons needed |
| 03. Surrendering on their own | Cooldown after a surrender (s) | 2. Surrender | Cooldown after a surrender (s) |
| 06. While held | Bolt when shot | 2. Surrender | Bolt when shot |
| 06. While held | Hits ignored right after giving up (s) | 2. Surrender | Hits ignored after giving up (s) |
| 06. While held | Fast weapon drop | 2. Surrender | Fast weapon drop |
| 06. While held | Cut a reload short | 2. Surrender | Cut a reload short |
| 02. Nerve | Nerve min | 2. Surrender | Nerve min |
| 02. Nerve | Nerve max | 2. Surrender | Nerve max |
| 02. Nerve | High nerve from | 2. Surrender | High nerve from |
| 03. Surrendering on their own | Check every (s) | 2. Surrender | Check every (s) |
| 04. Pressure | Recheck after a hit within (s) | 2. Surrender | Recheck after a hit (s) |
| 03. Surrendering on their own | Seen within (s) | 2. Surrender | Threat seen within (s) |
| 03. Surrendering on their own | Nerve resistance | 2. Surrender | Nerve resistance |
| 03. Surrendering on their own | Chance starts at pressure | 2. Surrender | Chance starts at pressure |
| 05. Role and personality | Scav pressure needed | 2. Surrender | Scav pressure needed |
| 05. Role and personality | Scav max chance | 2. Surrender | Scav max chance |
| 05. Role and personality | PMC pressure needed | 2. Surrender | PMC pressure needed |
| 05. Role and personality | PMC max chance | 2. Surrender | PMC max chance |
| 05. Role and personality | Boss and follower pressure needed | 2. Surrender | Boss pressure needed |
| 05. Role and personality | Boss and follower max chance | 2. Surrender | Boss max chance |
| 05. Role and personality | Personality multiplier min | 2. Surrender | Personality multiplier min |
| 05. Role and personality | Personality multiplier max | 2. Surrender | Personality multiplier max |
| 05. Role and personality | Armour confidence | 2. Surrender | Armour confidence |
| 05. Role and personality | Kit value weight | 2. Surrender | Kit value weight |
| 05. Role and personality | Kit value full at (roubles) | 2. Surrender | Kit value full at (roubles) |
| 05. Role and personality | Experience composure | 2. Surrender | Experience composure |
| 05. Role and personality | Re-value kit every (s) | 2. Surrender | Re-value kit every (s) |
| 04. Pressure | Hurt below (health fraction) | 2. Surrender | Hurt below (health) |
| 04. Pressure | Head or thorax below (fraction) | 2. Surrender | Head or thorax below (health) |
| 04. Pressure | Severe injury from | 2. Surrender | Severe injury from |
| 04. Pressure | Severe injury pressure floor | 2. Surrender | Severe injury pressure floor |
| 04. Pressure | Severe injury chance boost | 2. Surrender | Severe injury chance boost |
| 04. Pressure | Weight: injury | 2. Surrender | Weight: injury |
| 04. Pressure | Weight: out of ammo | 2. Surrender | Weight: out of ammo |
| 04. Pressure | Weight: suppression | 2. Surrender | Weight: suppression |
| 04. Pressure | Suppression half-life (s) | 2. Surrender | Suppression half-life (s) |
| 04. Pressure | Full suppression at (shots) | 2. Surrender | Full suppression at (shots) |
| 04. Pressure | Pinned down from | 2. Surrender | Pinned down from |
| 04. Pressure | Pinned down pressure floor | 2. Surrender | Pinned down pressure floor |
| 04. Pressure | Near miss within (m) | 2. Surrender | Near miss within (m) |
| 04. Pressure | Near miss checked up to (m) | 2. Surrender | Near miss checked up to (m) |
| 04. Pressure | Weight: outnumbered | 2. Surrender | Weight: outnumbered |
| 04. Pressure | Fully outnumbered at | 2. Surrender | Fully outnumbered at |
| 04. Pressure | Enemy counts for (s) | 2. Surrender | Enemy counts for (s) |
| 04. Pressure | Weight: squad | 2. Surrender | Weight: squad |
| 04. Pressure | Squad loss counts for (s) | 2. Surrender | Squad loss counts for (s) |
| 04. Pressure | Squad too far away | 2. Surrender | Squad too far away |
| 04. Pressure | Allies within (m) | 2. Surrender | Allies within (m) |
| 06. While held | Surrender acknowledged within (m) | 2. Surrender | Acknowledged within (m) |
| 06. While held | Unanswered surrender lasts min (s) | 2. Surrender | Unanswered surrender min (s) |
| 06. While held | Unanswered surrender lasts max (s) | 2. Surrender | Unanswered surrender max (s) |
| 06. While held | Ask again after (s) | 2. Surrender | Drop retry after (s) |
| 15. Bots taking prisoners | Takes a prisoner | 3. Captors and squads | Take prisoner chance |
| 15. Bots taking prisoners | Spares if not taking him | 3. Captors and squads | Spare chance |
| 15. Bots taking prisoners | Bot-held prisoners at once | 3. Captors and squads | Bot-held prisoners at once |
| 15. Bots taking prisoners | Holds a prisoner min (s) | 3. Captors and squads | Hold time min (s) |
| 15. Bots taking prisoners | Holds a prisoner max (s) | 3. Captors and squads | Hold time max (s) |
| 15. Bots taking prisoners | Your allies can make bots surrender | 3. Captors and squads | Your allies make bots surrender |
| 25. Encounter endings | Captors end encounters on their own | 3. Captors and squads | Captors end encounters on their own |
| 25. Encounter endings | A squadmate may stay behind to watch him | 3. Captors and squads | Squadmate may stay behind |
| 21. Captors taking surrendered gear | Captors take dropped gear | 3. Captors and squads | Captors take dropped gear |
| 25. Encounter endings | Captors demand gear they lack | 3. Captors and squads | Captors demand gear |
| 15. Bots taking prisoners | Captors put prisoners on their knees | 3. Captors and squads | Kneel order chance |
| 19. Hands on head and turning | Captors order hands on head | 3. Captors and squads | Hands on head order chance |
| 19. Hands on head and turning | Captors order a prisoner to turn around | 3. Captors and squads | Turn around order chance |
| 15. Bots taking prisoners | Captors close in on a disarmed prisoner | 3. Captors and squads | Close in chance |
| 22. Captor judgement | Lethal intent toward a prisoner | 3. Captors and squads | Lethal intent chance |
| 22. Captor judgement | Fires at a prisoner who runs | 3. Captors and squads | Fire at a runner chance |
| 16. Sparing surrendered enemies | Captor squad holds fire | 3. Captors and squads | Squad holds fire |
| 16. Sparing surrendered enemies | Other bots may spare him | 3. Captors and squads | Other bots may spare him |
| 23. Squad cover | A squadmate covers the captor | 3. Captors and squads | Squadmate covers the captor |
| 15. Bots taking prisoners | Guards from up to (m) | 3. Captors and squads | Guard range (m) |
| 15. Bots taking prisoners | Walks up to a prisoner from (m) | 3. Captors and squads | Walk up from (m) |
| 15. Bots taking prisoners | Guards from (m) | 3. Captors and squads | Guard distance (m) |
| 15. Bots taking prisoners | Walk-up speed | 3. Captors and squads | Walk-up speed |
| 15. Bots taking prisoners | Close-in distance (m) | 3. Captors and squads | Close-in distance (m) |
| 15. Bots taking prisoners | Between his orders min (s) | 3. Captors and squads | Between orders min (s) |
| 15. Bots taking prisoners | Between his orders max (s) | 3. Captors and squads | Between orders max (s) |
| 15. Bots taking prisoners | Bot order range (m) | 3. Captors and squads | Bot order range (m) |
| 15. Bots taking prisoners | Gives up custody after busy (s) | 3. Captors and squads | Give up custody after busy (s) |
| 15. Bots taking prisoners | Back to guarding after quiet (s) | 3. Captors and squads | Back to guarding after quiet (s) |
| 15. Bots taking prisoners | Fights at least (s) | 3. Captors and squads | Fight at least (s) |
| 15. Bots taking prisoners | Captors look around | 3. Captors and squads | Captors look around |
| 25. Encounter endings | Lingers after he is done min (s) | 3. Captors and squads | Ending: linger min (s) |
| 25. Encounter endings | Lingers after he is done max (s) | 3. Captors and squads | Ending: linger max (s) |
| 25. Encounter endings | Walk away instead of a Leave order | 3. Captors and squads | Ending: walk away weight |
| 25. Encounter endings | No recapture after an ending for (s) | 3. Captors and squads | Ending: no recapture for (s) |
| 21. Captors taking surrendered gear | Wants gear | 3. Captors and squads | Gear: appetite |
| 21. Captors taking surrendered gear | Worth taking from (roubles) | 3. Captors and squads | Gear: worth taking from (roubles) |
| 21. Captors taking surrendered gear | Items per prisoner at most | 3. Captors and squads | Gear: items per prisoner |
| 21. Captors taking surrendered gear | Risk he accepts | 3. Captors and squads | Gear: risk accepted |
| 21. Captors taking surrendered gear | Collects the weapons he made him drop | 3. Captors and squads | Gear: collect demanded weapons |
| 21. Captors taking surrendered gear | Risk he accepts for those | 3. Captors and squads | Gear: risk accepted for weapons |
| 26. Release and escape | Captors pick up a dropped knife | 3. Captors and squads | Gear: knife pick-up chance |
| 21. Captors taking surrendered gear | Fetch it after letting him go | 3. Captors and squads | Gear: fetch after release |
| 21. Captors taking surrendered gear | Gives up fetching after (s) | 3. Captors and squads | Gear: give up fetching after (s) |
| 25. Encounter endings | Captors step beside dropped gear | 3. Captors and squads | Gear: step beside it |
| 21. Captors taking surrendered gear | Close by within (m) | 3. Captors and squads | Gear: close by within (m) |
| 21. Captors taking surrendered gear | Never walks farther than (m) | 3. Captors and squads | Gear: never farther than (m) |
| 21. Captors taking surrendered gear | Distance weighs | 3. Captors and squads | Gear: distance weight |
| 21. Captors taking surrendered gear | Weighs a postponed item again after (s) | 3. Captors and squads | Gear: reweigh after (s) |
| 21. Captors taking surrendered gear | Postpones at most | 3. Captors and squads | Gear: postpone at most |
| 21. Captors taking surrendered gear | Sees where things land within (m) | 3. Captors and squads | Gear: sees it land within (m) |
| 21. Captors taking surrendered gear | Searches for an item he never saw land for (s) | 3. Captors and squads | Gear: search for (s) |
| 22. Captor judgement | Weight: rival faction | 3. Captors and squads | Judgement: rival faction weight |
| 22. Captor judgement | Weight: he shot me | 3. Captors and squads | Judgement: he shot me weight |
| 22. Captor judgement | Remembers being shot for (s) | 3. Captors and squads | Judgement: remembers being shot (s) |
| 22. Captor judgement | Weight: kept a weapon back | 3. Captors and squads | Judgement: kept a weapon weight |
| 22. Captor judgement | Refusal turns lethal | 3. Captors and squads | Judgement: refusal turns lethal |
| 22. Captor judgement | Lethal decision cap | 3. Captors and squads | Judgement: lethal cap |
| 16. Sparing surrendered enemies | Squad hears it within (m) | 3. Captors and squads | Squad: hears it within (m) |
| 16. Sparing surrendered enemies | Squadmate refuses anyway | 3. Captors and squads | Squad: refuses anyway chance |
| 16. Sparing surrendered enemies | Squadmates watch him (s) | 3. Captors and squads | Squad: watch him for (s) |
| 16. Sparing surrendered enemies | Notices a surrender within (m) | 3. Captors and squads | Others: notice within (m) |
| 16. Sparing surrendered enemies | Holds fire at a compliant man | 3. Captors and squads | Others: hold fire chance |
| 16. Sparing surrendered enemies | Lets an unarmed man run | 3. Captors and squads | Others: let him run chance |
| 16. Sparing surrendered enemies | Keeps his decision (s) | 3. Captors and squads | Hold-fire decision kept for (s) |
| 24. Incoming fire and release | Unarmed ex-prisoner spared for (s) | 3. Captors and squads | Hold fire after an unarmed release (s) |
| 23. Squad cover | Guard distance from the prisoner (m) | 3. Captors and squads | Cover: distance from prisoner (m) |
| 23. Squad cover | Captor waits for cover (s) | 3. Captors and squads | Cover: captor waits (s) |
| 23. Squad cover | Guard reaction time (s) | 3. Captors and squads | Cover: reaction time (s) |
| 23. Squad cover | Guard looks around | 3. Captors and squads | Cover: guard looks around |
| 23. Squad cover | Guard leaves the role after fighting (s) | 3. Captors and squads | Cover: leave role after fighting (s) |
| 25. Encounter endings | Prisoners notice they were left | 4. Prisoner escape and recovery | Notices being left |
| 08. Regroup and rearm | Go back for his own gun | 4. Prisoner escape and recovery | Go back for his own gun |
| 08. Regroup and rearm | Look for another gun | 4. Prisoner escape and recovery | Look for another gun |
| 08. Regroup and rearm | Fight with a knife | 4. Prisoner escape and recovery | Fight with a knife |
| 06. Taking an opening | Break chance | 4. Prisoner escape and recovery | Break chance |
| 24. Incoming fire and release | Incoming fire breaks a hold-up | 4. Prisoner escape and recovery | Incoming fire breaks a hold-up |
| 26. Release and escape | Fleeing men may fight back | 4. Prisoner escape and recovery | Fight back when fleeing |
| 27. Hand-off, interact key, kneel variants | Hand back to his own AI on the way out | 4. Prisoner escape and recovery | Quick hand-back to his own AI |
| 11. Reactions and voice | Other held bots react | 4. Prisoner escape and recovery | Runs when another prisoner is shot |
| 06. Taking an opening | Opening must last (s) | 4. Prisoner escape and recovery | Opening: must last (s) |
| 06. Taking an opening | Roll every (s) | 4. Prisoner escape and recovery | Opening: roll every (s) |
| 06. Taking an opening | Gun nearby boost | 4. Prisoner escape and recovery | Opening: gun nearby boost |
| 06. Taking an opening | Squad nearby boost | 4. Prisoner escape and recovery | Opening: squad nearby boost |
| 06. Taking an opening | Knife range (m) | 4. Prisoner escape and recovery | Opening: knife range (m) |
| 06. Taking an opening | Draw range (m) | 4. Prisoner escape and recovery | Opening: draw range (m) |
| 06. Taking an opening | Scramble range (m) | 4. Prisoner escape and recovery | Opening: scramble range (m) |
| 06. Taking an opening | Gun safe from you beyond (m) | 4. Prisoner escape and recovery | Opening: gun safe from you beyond (m) |
| 06. Taking an opening | Run only beyond (m) | 4. Prisoner escape and recovery | Opening: run only beyond (m) |
| 06. Taking an opening | Weight: draw | 4. Prisoner escape and recovery | Opening: draw weight |
| 06. Taking an opening | Weight: knife | 4. Prisoner escape and recovery | Opening: knife weight |
| 06. Taking an opening | Weight: scramble for his gun | 4. Prisoner escape and recovery | Opening: scramble weight |
| 06. Taking an opening | Weight: run | 4. Prisoner escape and recovery | Opening: run weight |
| 06. Taking an opening | Weight: slip into cover | 4. Prisoner escape and recovery | Opening: slip into cover weight |
| 06. Taking an opening | Scramble time limit (s) | 4. Prisoner escape and recovery | Opening: scramble time limit (s) |
| 06. Taking an opening | No second scramble for (s) | 4. Prisoner escape and recovery | Opening: no second scramble for (s) |
| 06. Taking an opening | Reaction time min (s) | 4. Prisoner escape and recovery | Reaction time min (s) |
| 06. Taking an opening | Reaction time max (s) | 4. Prisoner escape and recovery | Reaction time max (s) |
| 06. Taking an opening | Warning shot holds him for (s) | 4. Prisoner escape and recovery | Warning shot holds him (s) |
| 06. Taking an opening | After an order he complies for (s) | 4. Prisoner escape and recovery | Order holds him (s) |
| 06. Taking an opening | Coming closer counts after (m) | 4. Prisoner escape and recovery | Coming closer counts after (m) |
| 06. Taking an opening | Quickdraw sidearm | 4. Prisoner escape and recovery | Quickdraw sidearm |
| 06. Taking an opening | Draw hand-off limit (s) | 4. Prisoner escape and recovery | Draw hand-off limit (s) |
| 06. Taking an opening | Retry a refused draw for (s) | 4. Prisoner escape and recovery | Retry a refused draw (s) |
| 06. While held | Covered within (deg) | 4. Prisoner escape and recovery | Covered within (deg) |
| 06. While held | Captor gone beyond (m) | 4. Prisoner escape and recovery | Captor gone beyond (m) |
| 06. While held | Captor walks away by (m) | 4. Prisoner escape and recovery | Captor walks away by (m) |
| 06. While held | Out of sight for (s) | 4. Prisoner escape and recovery | Captor out of sight for (s) |
| 25. Encounter endings | Prisoner takes in being left min (s) | 4. Prisoner escape and recovery | Left: reaction min (s) |
| 25. Encounter endings | Prisoner takes in being left max (s) | 4. Prisoner escape and recovery | Left: reaction max (s) |
| 11. Reactions and voice | Sees it within (m) | 4. Prisoner escape and recovery | Sees a prisoner shot within (m) |
| 11. Reactions and voice | Chance he runs | 4. Prisoner escape and recovery | Runs when he sees it chance |
| 07. Retreat and recapture | Retreat distance (m) | 4. Prisoner escape and recovery | Retreat distance (m) |
| 07. Retreat and recapture | Retreat time limit (s) | 4. Prisoner escape and recovery | Retreat time limit (s) |
| 07. Retreat and recapture | Arrived within (m) | 4. Prisoner escape and recovery | Arrived within (m) |
| 07. Retreat and recapture | Grace after running (s) | 4. Prisoner escape and recovery | Grace after running (s) |
| 07. Retreat and recapture | Caught again within (m) | 4. Prisoner escape and recovery | Catch: within (m) |
| 07. Retreat and recapture | Aim within (deg) | 4. Prisoner escape and recovery | Catch: aim within (deg) |
| 07. Retreat and recapture | Aim for (s) | 4. Prisoner escape and recovery | Catch: aim for (s) |
| 07. Retreat and recapture | Resist chance | 4. Prisoner escape and recovery | Catch: resist chance |
| 07. Retreat and recapture | No going back for a gun for (s) | 4. Prisoner escape and recovery | No going back for a gun (s) |
| 26. Release and escape | Remembers the capture for (s) | 4. Prisoner escape and recovery | Remembers the capture (s) |
| 26. Release and escape | Keep away radius (m) | 4. Prisoner escape and recovery | Keep away radius (m) |
| 26. Release and escape | Keep away legs at most | 4. Prisoner escape and recovery | Keep away legs at most |
| 26. Release and escape | Allow for where the captor is walking (s) | 4. Prisoner escape and recovery | Allow for the captor walking (s) |
| 27. Hand-off, interact key, kneel variants | Hand back once this far from where he was held (m) | 4. Prisoner escape and recovery | Hand back after (m) |
| 27. Hand-off, interact key, kneel variants | Hand back at most this long after he set off (s) | 4. Prisoner escape and recovery | Hand back within (s) |
| 08. Regroup and rearm | Own gun: keep away from you (m) | 4. Prisoner escape and recovery | Own gun: keep away from you (m) |
| 08. Regroup and rearm | Search radius (m) | 4. Prisoner escape and recovery | Other guns: search radius (m) |
| 08. Regroup and rearm | Other guns: keep away from you (m) | 4. Prisoner escape and recovery | Other guns: keep away from you (m) |
| 08. Regroup and rearm | Give up walking after (s) | 4. Prisoner escape and recovery | Give up walking after (s) |
| 08. Regroup and rearm | Pick-up range (m) | 4. Prisoner escape and recovery | Pick-up range (m) |
| 08. Regroup and rearm | Pick-up time limit (s) | 4. Prisoner escape and recovery | Pick-up time limit (s) |
| 24. Incoming fire and release | Near miss counts within (m) | 4. Prisoner escape and recovery | Fire: near miss within (m) |
| 24. Incoming fire and release | No new surrender while under fire for (s) | 4. Prisoner escape and recovery | Fire: no new surrender for (s) |
| 24. Incoming fire and release | Goes for his gun under fire within (m) | 4. Prisoner escape and recovery | Fire: go for his gun within (m) |
| 24. Incoming fire and release | Cover from fire within (m) | 4. Prisoner escape and recovery | Fire: cover within (m) |
| 24. Incoming fire and release | Move to cover at most (s) | 4. Prisoner escape and recovery | Fire: move to cover at most (s) |
| 09. Hiding and movement | Cover search radius (m) | 4. Prisoner escape and recovery | Hiding: cover search radius (m) |
| 09. Hiding and movement | Points tried | 4. Prisoner escape and recovery | Movement: points tried |
| 09. Hiding and movement | Retries when stuck | 4. Prisoner escape and recovery | Movement: retries when stuck |
| 09. Hiding and movement | Stuck after (s) | 4. Prisoner escape and recovery | Movement: stuck after (s) |
| 09. Hiding and movement | Progress (m) | 4. Prisoner escape and recovery | Movement: progress (m) |
| 10. Orders | Menu / confirm key | 5. Commands and dialogue | Menu key |
| 10. Orders | Next order | 5. Commands and dialogue | Next order key |
| 10. Orders | Previous order | 5. Commands and dialogue | Previous order key |
| 27. Hand-off, interact key, kneel variants | Interact key opens the order menu | 5. Commands and dialogue | Interact key opens the menu |
| 10. Orders | Mouse wheel selects | 5. Commands and dialogue | Mouse wheel selects |
| 10. Orders | Menu size | 5. Commands and dialogue | Menu size |
| 11. Reactions and voice | On-screen hints | 5. Commands and dialogue | On-screen hints |
| 10. Stop demand | Stop demand | 5. Commands and dialogue | Stop demand |
| 10. Stop demand | Shout Stop key | 5. Commands and dialogue | Shout Stop key |
| 12. Kneeling | Kneel on order | 5. Commands and dialogue | Kneel on Get down |
| 19. Hands on head and turning | Hands on head order | 5. Commands and dialogue | Hands on head order |
| 19. Hands on head and turning | Turn around order | 5. Commands and dialogue | Turn around order |
| 10. Orders | Allow kit order | 5. Commands and dialogue | Kit order |
| 10. Orders | Allow armour order | 5. Commands and dialogue | Armour order |
| 10. Orders | Allow follow order | 5. Commands and dialogue | Follow order |
| 20. Partial compliance | Prisoners may keep a weapon | 5. Commands and dialogue | Prisoners may keep a weapon |
| 11. Reactions and voice | Bot voice lines | 5. Commands and dialogue | Bot voice lines |
| 11. Reactions and voice | Talk while held | 5. Commands and dialogue | Talk while held |
| 11. Reactions and voice | Acknowledge orders | 5. Commands and dialogue | Answer orders out loud |
| 11. Reactions and voice | Defiant remarks | 5. Commands and dialogue | Defiant remarks |
| 15. Bots taking prisoners | Bots speak their orders | 5. Commands and dialogue | Captors speak |
| 11. Reactions and voice | Speak your orders | 5. Commands and dialogue | Speak your orders |
| 10. Orders | Order range (m) | 5. Commands and dialogue | Order range (m) |
| 10. Orders | Follow distance (m) | 5. Commands and dialogue | Follow distance (m) |
| 10. Orders | Follow slack (m) | 5. Commands and dialogue | Follow slack (m) |
| 10. Orders | Time per dropped item (s) | 5. Commands and dialogue | Time per dropped item (s) |
| 19. Hands on head and turning | Get down puts hands on head | 5. Commands and dialogue | Get down puts hands on head |
| 19. Hands on head and turning | Turn around from his knees | 5. Commands and dialogue | Turn around from his knees |
| 10. Stop demand | Demand range (m) | 5. Commands and dialogue | Stop: range (m) |
| 10. Stop demand | Demand cooldown (s) | 5. Commands and dialogue | Stop: cooldown (s) |
| 10. Stop demand | Demand lasts (s) | 5. Commands and dialogue | Stop: lasts (s) |
| 10. Stop demand | Demand pressure | 5. Commands and dialogue | Stop: pressure |
| 10. Stop demand | Demand chance boost | 5. Commands and dialogue | Stop: chance boost |
| 20. Partial compliance | Keeps one back | 5. Commands and dialogue | Keep one back chance |
| 20. Partial compliance | Keeps his only weapon | 5. Commands and dialogue | Keep his only weapon |
| 20. Partial compliance | Complies when told again | 5. Commands and dialogue | Comply when told again |
| 20. Partial compliance | Escalates when told again | 5. Commands and dialogue | Escalate when told again |
| 20. Partial compliance | Kept weapon draw weight | 5. Commands and dialogue | Kept weapon draw weight |
| 20. Partial compliance | Captor repeats a demand at most | 5. Commands and dialogue | Captor repeats a demand at most |
| 20. Partial compliance | Holstered pistol seen within (m) | 5. Commands and dialogue | Holstered pistol seen within (m) |
| 11. Reactions and voice | Voice cooldown (s) | 5. Commands and dialogue | Voice cooldown (s) |
| 11. Reactions and voice | Unprompted line every min (s) | 5. Commands and dialogue | Talk every min (s) |
| 11. Reactions and voice | Unprompted line every max (s) | 5. Commands and dialogue | Talk every max (s) |
| 11. Reactions and voice | Unprompted line chance | 5. Commands and dialogue | Talk chance |
| 11. Reactions and voice | Reply chance | 5. Commands and dialogue | Reply chance |
| 11. Reactions and voice | Take turns within (m) | 5. Commands and dialogue | Take turns within (m) |
| 11. Reactions and voice | Gap between speakers (s) | 5. Commands and dialogue | Gap between speakers (s) |
| 11. Reactions and voice | Answer delay min (s) | 5. Commands and dialogue | Answer delay min (s) |
| 11. Reactions and voice | Answer delay max (s) | 5. Commands and dialogue | Answer delay max (s) |
| 15. Bots taking prisoners | Captor line gap (s) | 5. Commands and dialogue | Captor line gap (s) |
| 25. Encounter endings | One voice per encounter | 5. Commands and dialogue | One voice per encounter |
| 25. Encounter endings | Gap between voices in an encounter (s) | 5. Commands and dialogue | Gap between voices in an encounter (s) |
| 12. Arm pose | Face the player | 6. Animations | Face the captor |
| 12. Arm pose | Stance with hands up | 6. Animations | Stance with hands up |
| 26. Release and escape | Open hands | 6. Animations | Open hands |
| 27. Hand-off, interact key, kneel variants | Kneel variants | 6. Animations | Kneel variants |
| 12. Kneeling | Brace on the knee when rising | 6. Animations | Brace on the knee when rising |
| 12. Kneeling | Step to a spot with room | 6. Animations | Step to a spot with room |
| 12. Kneeling | Crouch when kneeling is impossible | 6. Animations | Crouch when kneeling is impossible |
| 19. Hands on head and turning | Turn with stepping feet | 6. Animations | Turn with stepping feet |
| 12. Arm pose | Wiggle | 6. Animations | Wiggle |
| 12. Arm pose | Middle finger | 6. Animations | Middle finger |
| 18. Attention and demeanor | Demeanor styles | 6. Animations | Demeanor styles |
| 18. Attention and demeanor | Head movement | 6. Animations | Head movement |
| 18. Attention and demeanor | Kneeling idle movement | 6. Animations | Kneeling idle movement |
| 18. Attention and demeanor | Flinch at near misses | 6. Animations | Flinch at near misses |
| 12. Arm pose | Upper arm raise (deg) | 6. Animations | Arms: upper arm raise (deg) |
| 12. Arm pose | Upper arm forward (deg) | 6. Animations | Arms: upper arm forward (deg) |
| 12. Arm pose | Forearm forward (deg) | 6. Animations | Arms: forearm forward (deg) |
| 12. Arm pose | Forearm inward (deg) | 6. Animations | Arms: forearm inward (deg) |
| 12. Arm pose | Forearms stay upright | 6. Animations | Arms: stay upright |
| 12. Arm pose | Forearm twist (deg) | 6. Animations | Arms: forearm twist (deg) |
| 12. Arm pose | Pose strength | 6. Animations | Arms: pose strength |
| 12. Arm pose | Raise time (s) | 6. Animations | Arms: raise time (s) |
| 12. Arm pose | Lower time (s) | 6. Animations | Arms: lower time (s) |
| 12. Arm pose | Fast lower time (s) | 6. Animations | Arms: fast lower time (s) |
| 12. Arm pose | Draw lower time (s) | 6. Animations | Arms: draw lower time (s) |
| 12. Arm pose | Tremble (deg) | 6. Animations | Arms: tremble (deg) |
| 12. Arm pose | Tremble speed | 6. Animations | Arms: tremble speed |
| 06. While held | Turn to you after (deg) | 6. Animations | Turn to you after (deg) |
| 19. Hands on head and turning | Turn speed (deg per s) | 6. Animations | Turn speed (deg per s) |
| 19. Hands on head and turning | Hands to head (s) | 6. Animations | Hands on head: move time (s) |
| 19. Hands on head and turning | Head down max with hands on head (deg) | 6. Animations | Hands on head: head down max (deg) |
| 19. Hands on head and turning | Head up max with hands on head (deg) | 6. Animations | Hands on head: head up max (deg) |
| 19. Hands on head and turning | Head turn max with hands on head (deg) | 6. Animations | Hands on head: head turn max (deg) |
| 12. Arm pose | Wiggle chance | 6. Animations | Wiggle: chance |
| 12. Arm pose | Wiggle max chance | 6. Animations | Wiggle: max chance |
| 12. Arm pose | Wiggle cooldown (s) | 6. Animations | Wiggle: cooldown (s) |
| 12. Arm pose | Wiggle strength | 6. Animations | Wiggle: strength |
| 12. Arm pose | Wiggle swings | 6. Animations | Wiggle: swings |
| 12. Arm pose | Wiggle swing time (s) | 6. Animations | Wiggle: swing time (s) |
| 12. Arm pose | Wiggle after settling min (s) | 6. Animations | Wiggle: after settling min (s) |
| 12. Arm pose | Wiggle after settling max (s) | 6. Animations | Wiggle: after settling max (s) |
| 12. Arm pose | Wiggle window (s) | 6. Animations | Wiggle: window (s) |
| 12. Arm pose | Wiggle sample gap (s) | 6. Animations | Wiggle: sample gap (s) |
| 12. Arm pose | Wiggle clearance (m) | 6. Animations | Wiggle: clearance (m) |
| 12. Arm pose | Middle finger chance | 6. Animations | Middle finger: chance |
| 12. Arm pose | Middle finger personality influence | 6. Animations | Middle finger: personality influence |
| 12. Arm pose | Middle finger max chance | 6. Animations | Middle finger: max chance |
| 12. Arm pose | Middle finger cooldown (s) | 6. Animations | Middle finger: cooldown (s) |
| 12. Arm pose | Middle finger after settling min (s) | 6. Animations | Middle finger: after settling min (s) |
| 12. Arm pose | Middle finger after settling max (s) | 6. Animations | Middle finger: after settling max (s) |
| 12. Arm pose | Middle finger window (s) | 6. Animations | Middle finger: window (s) |
| 12. Arm pose | Gesture arm blend (s) | 6. Animations | Gesture arm blend (s) |
| 18. Attention and demeanor | Demeanor strength | 6. Animations | Demeanor: strength |
| 18. Attention and demeanor | Glance every min (s) | 6. Animations | Head: glance every min (s) |
| 18. Attention and demeanor | Glance every max (s) | 6. Animations | Head: glance every max (s) |
| 18. Attention and demeanor | Head turn max (deg) | 6. Animations | Head: turn max (deg) |
| 18. Attention and demeanor | Head down max (deg) | 6. Animations | Head: down max (deg) |
| 18. Attention and demeanor | Head up max (deg) | 6. Animations | Head: up max (deg) |
| 18. Attention and demeanor | Head speed (deg per s) | 6. Animations | Head: speed (deg per s) |
| 18. Attention and demeanor | Torso turn max (deg) | 6. Animations | Head: torso turn max (deg) |
| 18. Attention and demeanor | Kneeling head turn max (deg) | 6. Animations | Head: kneeling turn max (deg) |
| 18. Attention and demeanor | Kneeling torso turn max (deg) | 6. Animations | Head: kneeling torso turn max (deg) |
| 18. Attention and demeanor | Kneeling idle strength | 6. Animations | Kneeling idle strength |
| 18. Attention and demeanor | Flinch strength | 6. Animations | Flinch: strength |
| 18. Attention and demeanor | Flinch cooldown (s) | 6. Animations | Flinch: cooldown (s) |
| 12. Kneeling | Kneel down time (s) | 6. Animations | Kneel: steady time (s) |
| 12. Kneeling | Frightened kneel time (s) | 6. Animations | Kneel: frightened time (s) |
| 12. Kneeling | One knee first time (s) | 6. Animations | Kneel: one knee first time (s) |
| 12. Kneeling | Style weight steady | 6. Animations | Kneel: steady weight |
| 12. Kneeling | Style weight frightened | 6. Animations | Kneel: frightened weight |
| 12. Kneeling | Style weight one knee first | 6. Animations | Kneel: one knee first weight |
| 12. Kneeling | Stand up time (s) | 6. Animations | Kneel: stand up time (s) |
| 12. Kneeling | Defensive stand time (s) | 6. Animations | Kneel: defensive stand time (s) |
| 12. Kneeling | Quick stand time (s) | 6. Animations | Kneel: quick stand time (s) |
| 12. Kneeling | Brace chance | 6. Animations | Kneel: brace chance |
| 12. Kneeling | Leg pose strength | 6. Animations | Kneel: leg pose strength |
| 12. Kneeling | Torso lean (deg) | 6. Animations | Kneel: torso lean (deg) |
| 12. Kneeling | Transition lean scale | 6. Animations | Kneel: transition lean scale |
| 12. Kneeling | Knee height (m) | 6. Animations | Kneel: knee height (m) |
| 12. Kneeling | Thigh angle (deg) | 6. Animations | Kneel: thigh angle (deg) |
| 12. Kneeling | Knee spread (m) | 6. Animations | Kneel: knee spread (m) |
| 12. Kneeling | Hips forward (m) | 6. Animations | Kneel: hips forward (m) |
| 27. Hand-off, interact key, kneel variants | Settled back on the heels weight | 6. Animations | Kneel: settled weight |
| 27. Hand-off, interact key, kneel variants | Forward and tense weight | 6. Animations | Kneel: tense weight |
| 27. Hand-off, interact key, kneel variants | Settled thigh angle (deg) | 6. Animations | Kneel: settled thigh angle (deg) |
| 27. Hand-off, interact key, kneel variants | Tense forward lean (deg) | 6. Animations | Kneel: tense forward lean (deg) |
| 27. Hand-off, interact key, kneel variants | Keeps a variant at least (s) | 6. Animations | Kneel: keep a variant at least (s) |
| 12. Kneeling | Max ground step (m) | 6. Animations | Kneel: max ground step (m) |
| 12. Kneeling | Max slope (deg) | 6. Animations | Kneel: max slope (deg) |
| 12. Kneeling | Room search radius (m) | 6. Animations | Kneel: room search radius (m) |
| 12. Kneeling | Hands behind head strength | 6. Animations | Hands behind head: strength |
| 12. Kneeling | Hands behind head (m) | 6. Animations | Hands behind head: distance (m) |
| 12. Kneeling | Hands above neck (m) | 6. Animations | Hands behind head: height (m) |
| 12. Kneeling | Elbows out | 6. Animations | Hands behind head: elbows out |
| 12. Kneeling | Helmet extra (m) | 6. Animations | Hands behind head: helmet extra (m) |
| 14. Debug | Debug keys | 7. Debugging | Debug keys |
| 14. Debug | Surrender bot | 7. Debugging | Key: surrender |
| 14. Debug | Give them an opening | 7. Debugging | Key: give an opening |
| 14. Debug | Pretend you left | 7. Debugging | Key: pretend you left |
| 14. Debug | Surrender to his enemy | 7. Debugging | Key: surrender to his enemy |
| 14. Debug | Overlay | 7. Debugging | Overlay |
| 17. Debug Scenarios | Scenario tools | 7. Debugging | Scenario tools |
| 17. Debug Scenarios | Scenario menu key | 7. Debugging | Scenario menu key |
| 14. Debug | Verbose logs | 7. Debugging | Verbose logs |
| 14. Debug | Profile | 7. Debugging | Profile |
| 14. Debug | Force run | 7. Debugging | Key: force run |
| 14. Debug | Force gun grab | 7. Debugging | Key: force gun grab |
| 22. Captor judgement | Debug key: captor decides to kill | 7. Debugging | Key: captor decides to kill |
| 19. Hands on head and turning | Debug key: hands on head | 7. Debugging | Key: hands on head |
| 19. Hands on head and turning | Debug key: turn around | 7. Debugging | Key: turn around |
| 14. Debug | Surrender key range (m) | 7. Debugging | Key range (m) |
| 14. Debug | Opening lasts (s) | 7. Debugging | Opening lasts (s) |
| 27. Hand-off, interact key, kneel variants | Debug: order prisoners held by bots | 7. Debugging | Order prisoners held by bots |
| 14. Debug | Override nerve | 7. Debugging | Override nerve |
| 14. Debug | Nerve value | 7. Debugging | Nerve value |
| 20. Partial compliance | Test outcome | 7. Debugging | Test: Drop your weapons answer |
| 14. Debug | Repeat errors every (s) | 7. Debugging | Repeat errors every (s) |
| 14. Debug | Log decisions | 7. Debugging | Log: decisions |
| 16. Sparing surrendered enemies | Log restraint decisions | 7. Debugging | Log: hold-fire decisions |
| 22. Captor judgement | Log judgement | 7. Debugging | Log: captor judgement |
| 21. Captors taking surrendered gear | Log gear decisions | 7. Debugging | Log: gear decisions |
| 23. Squad cover | Log squad cover | 7. Debugging | Log: squad cover |
| 24. Incoming fire and release | Log incoming fire | 7. Debugging | Log: incoming fire |
| 25. Encounter endings | Log encounter endings | 7. Debugging | Log: encounter endings |
| 26. Release and escape | Log escapes | 7. Debugging | Log: escapes |
| 14. Debug | Log voice | 7. Debugging | Log: voice |
| 14. Debug | Log gestures | 7. Debugging | Log: gestures |
| 14. Debug | Log attention | 7. Debugging | Log: attention |
| 14. Debug | Log kneeling | 7. Debugging | Log: kneeling |
| 14. Debug | Log pose diagnostics | 7. Debugging | Log: pose diagnostics |
| 24. Incoming fire and release | Log a released bot for (s) | 7. Debugging | Log: released bot for (s) |
| 14. Debug | Profile report every (s) | 7. Debugging | Profile report every (s) |
| 17. Debug Scenarios | Menu up | 7. Debugging | Scenario: menu up |
| 17. Debug Scenarios | Menu down | 7. Debugging | Scenario: menu down |
| 17. Debug Scenarios | Menu previous value | 7. Debugging | Scenario: previous value |
| 17. Debug Scenarios | Menu next value | 7. Debugging | Scenario: next value |
| 17. Debug Scenarios | Menu activate | 7. Debugging | Scenario: activate |
| 17. Debug Scenarios | Scenario overlay | 7. Debugging | Scenario: overlay |
| 17. Debug Scenarios | Allow concurrent scenarios | 7. Debugging | Scenario: allow several at once |
| 17. Debug Scenarios | Scenario bots at most | 7. Debugging | Scenario: bots at most |
| 17. Debug Scenarios | Spawn timeout (s) | 7. Debugging | Scenario: spawn timeout (s) |
| 17. Debug Scenarios | No magazine check on spawn | 7. Debugging | Scenario: no magazine check on spawn |
| 17. Debug Scenarios | Hold bots until all are ready | 7. Debugging | Scenario: hold bots until all are ready |
| 17. Debug Scenarios | Test: delay one spawn (s) | 7. Debugging | Scenario: delay one spawn (s) |
| 14. Debug | Kneel gizmos | 7. Debugging | Kneel: gizmos |
| 14. Debug | Kneel test: kneel | 7. Debugging | Kneel test: kneel key |
| 14. Debug | Kneel test: stand | 7. Debugging | Kneel test: stand key |
| 14. Debug | Kneel test: loop | 7. Debugging | Kneel test: loop key |
| 14. Debug | Kneel test: interrupt | 7. Debugging | Kneel test: interrupt key |
| 14. Debug | Kneel test: urgent rise | 7. Debugging | Kneel test: urgent rise key |
| 14. Debug | Kneel test cycles | 7. Debugging | Kneel test: cycles |
| 14. Debug | Kneel test interruption | 7. Debugging | Kneel test: interruption |
| 14. Debug | Kneel test interrupts | 7. Debugging | Kneel test: interrupts |
| 14. Debug | Kneel test interrupt at | 7. Debugging | Kneel test: interrupt at |
| 14. Debug | Kneel test style | 7. Debugging | Kneel test: style |
| 14. Debug | Kneel test rise | 7. Debugging | Kneel test: rise |
| 14. Debug | Kneel test brace | 7. Debugging | Kneel test: brace |
| 14. Debug | Kneel test escape outcome | 7. Debugging | Kneel test: escape outcome |
| 14. Debug | Kneel pose isolate | 7. Debugging | Kneel test: pose isolate |
| 14. Debug | Kneel brace palm flip | 7. Debugging | Kneel test: brace palm flip |
| 14. Debug | Head neutral test | 7. Debugging | Head neutral test |
| 14. Debug | Config version (set by the mod) | 7. Debugging | Config version (set by the mod) |
| 27. Hand-off, interact key, kneel variants | Hands behind head: fingers laced | 6. Animations | Hands behind head (combined) |
| 27. Hand-off, interact key, kneel variants | Hands behind head: one over the other | 6. Animations | Hands behind head (combined) |
| 10. Orders | Number keys give orders | 5. Commands and dialogue | Number keys (combined) |
| 10. Orders | Numpad keys too | 5. Commands and dialogue | Number keys (combined) |
| 07. Retreat and recapture | Leaves the captivity area (m) | 4. Prisoner escape and recovery | Flee distance from captivity (m) (combined) |
| 26. Release and escape | Escape ends this far from the threat (m) | 4. Prisoner escape and recovery | Flee distance from captivity (m) (combined) |

## 0.28.1 (2026-11-21): release-quality pass

Review of the per-frame paths, lifecycle/cleanup and hand-back, by inspection (no Profile run exists).
Found no large cost: every all-player patch and layer `IsActive` already exits on a count; heavy
searches go through `Perception.TakeHeavy`; statics are cleared on raid change and teardown.

- Keep-away legs (Escape.KeepAwayLogic) sprinted almost always: the rule also fired on the ex-captor's
  last-seen velocity being non-zero (stale, and practically never exactly zero). Now the documented
  rule only (sprint while he sees the threat within 30 m, else a brisk walk), evaluated every 0.5 s
  instead of a sight line every frame.
- Removed `Overwatch.IsGuard` (no callers).
- MOD-PAGE.md: the Forge description (release facts still to confirm are in its header comment).
- README: the opening draws in Joel's order (slung gun, sidearm, knife only without a gun); keys
  live in section 5.

Build passed (Debug + Release, 0 warnings); packed `dist/HandsAreHigh-0.28.1.zip`; installed in H:/SPT4.1.X (hash-verified);
not run in game.

## 0.28.2 (2026-11-21): from the first 0.28.1 raid with Profile on (LogOutput 10:40)

The raid: config layout migrated (428 values), 3 NATURAL surrenders (first ever in a log; 2 Scav,
1 PMC; one to a bot captor, Chauleqix, lethal intent rolled), 0 mod errors.

Profile (70 reports, ~19 ms frames): our gameplay cost 0.05-0.07% of frame time with nobody held,
0.4-0.9% with one prisoner. The p99.9 ~70-80 ms frame every 10 s is there with the mod idle (0.5 ms/s
of ours), alongside one gen-0 GC per report: not ours. Single-frame spikes: raid start 20 ms
(Registry: EnsureRaid prewarm, once); the first surrender 23 ms + first pose frame 11 ms (first use;
the second prisoner's worst was 5 ms); every surrender frame 10-22 ms (Natural/Captors: drop, captor
decision, kit value, path check in one frame); layer decision frames 5-9 ms (opening, regroup).
Some spikes include sections that normally cost ~1 us (Menu 2 ms), i.e. a GC landed in them.

- Bug: a scramble pick-up the game put straight into his hands ("holding TOZ-106") was followed by a
  draw request, refused (hands busy), so AfterPickUp gave up on the gun he held and Decide handed
  him back as `couldn't draw his slung gun`, as an unarmed release (Restraint.Detach). Now: the
  picked-up gun already in hand -> Rearm; Decide rearms on a usable gun already in hand.
- Diagnostics: 7 `RESTRAINT LEAK (blocked at trigger)` lines from Cogy, who held fire at reokos
  while fighting a Scav: the trigger guard blocks any press with the prisoner on the line of fire,
  so these were probably shots at the Scav with reokos in the way (no prisoner hit was logged). The
  line now names the shooter's target; with a target other than the prisoner it is an info line.
- Profiler: spike lines say when a GC collection ran that frame; detail sections Kit
  (Arsenal.AssessKit) and Start (SurrenderRegistry.Surrender), shown but not added to totals, to
  find out what the surrender frame costs before changing it.

Build passed (Debug + Release, 0 warnings); packed `dist/HandsAreHigh-0.28.2.zip`; installed in H:/SPT4.1.X (hash-verified).

## 0.28.3 (2026-11-21): from the live 0.28.2 raid (LogOutput 11:00, 7 natural surrenders, 0 mod warnings)

- A Scav (Санька) gave up to Fantia, who never saw it, slipped into cover during an opening, and
  was shot by Fantia while hiding. Behaviour as designed (no decision to spare him existed), but the
  hit diagnosis called Fantia "an outsider's own fight": now it says he is the man he gave up to,
  who never answered the surrender.
- The opening's "run" option ran a full RetreatPoint search (up to 12 path solves) only to learn
  whether a way out exists, in the same frame as the "hide" search: a 16.8 ms frame. It now stops at
  the first reachable spot (`anyOnly`); the hide search and the flight's own route search are as before.
- Confirmed in raid: the new trigger-guard line ("falloutfreak10's shot at Fantia held back at
  trigger: Санька ... is in the line of fire"): the 0.28.1 "leaks" were this case.

- Opening hides used the FIRE move's time limit ("Fire: move to cover at most", 8 s) for a spot up to
  26 m away: both opening hides in the raid (Санька, Давид, hurt legs) ended "8 s on the way to
  cover", stopped short in the open. Now the limit is the time he needs (distance x 1.4 / speed by
  legs/urgency + 3 s, between the fire limit and 25 s); Stalled still ends a hide with no progress.
  The timeout line says how long and how far short.
- Keep-away "stuck" lines now give the mover state, speed, stamina, door and distance left: 3 of
  Давид's 6 legs ended stuck (badly hurt, hurt legs) with no reason logged.
- Observed (changed below): SAIN's Squad Layer walks an unarmed released Scav back to his squad at the
  capture spot (Давид CIRCLED BACK twice; 6 keep-away legs across two releases; his ex-captor was
  60-77 m away). Keep-away keys on the capture spot, not on where the ex-captor is; whether it
  should yield when the captor is far is a design question for Joel.
- Давид surrendered twice to Fantia, who never noticed either: allowed (30 s cooldown long over,
  nobody ever held him).

- Fantia was handed back from an opening hide "armed: a gun in his hands" (his slung AKS) with no
  draw of ours asked: something else (game weapon selector or SAIN; not verified) drew while our
  layer held him. Outcome self-corrects (Restraint.TickDetached ends the hold-fire on an armed man),
  but the source is unknown: a one-time warning `... is in his hands with no draw of ours (phase,
  arms, active layer)` now logs it when it happens.

- Keep-away (Joel's call): keys on the ex-captor where he last saw him; the capture spot counts only
  while that is within "Keep away radius" of it; a dead ex-captor: nothing to keep away from. The
  leg's flight then avoids the ex-captor only. SAIN taking an unarmed man back to his squad at the
  capture spot, with the captor gone, is left to SAIN.

Build passed (Debug + Release, 0 warnings); packed `dist/HandsAreHigh-0.28.3.zip`; installed in
H:/SPT4.1.X (hash-verified; 0.28.2 DLL + cfg in that session scratchpad `backup-0.28.2`); not run.

## 0.28.4 (2026-11-21): from the live 0.28.3 raid (9 natural surrenders, 2 to bot custody)

- The 0.28.3 hide-time fix missed the opening path: TakeOpening's hide brings its own spot and skips
  the cover search where the new limit was set, so DaddyAL's opening hide still ended "8 s on the
  way to cover, 12 m short". Now SetHideTime runs for both. Debug build passed; not packed (game running).
- Surrender-frame split (first data): DaddyAL's surrender frame 4.1 ms, of which Start
  (SurrenderRegistry.Surrender) 0.44 ms and Captors 3.47 ms: the cost is on the bot-captor side.
- Real restraint leaks (4 `RESTRAINT LEAK (hit)` warnings): DaddyAL took custody of Леха, then
  fought a third party (Клим) past him; a round passed Леха at 0.9 m, then three hits, the last
  trigger press 0.1-0.3 s before, 3 s after the decision; Bot-Scave hit him 5.3 s after "lets go"
  the same way. Two gaps: the trigger guard only runs per press (a burst after a clear press swept
  onto him), and the line box (0.55 m + 1 cm/m around the LOOK) was narrower than bots' scatter.
  Now: a burst guard (Suppression.ShotPatch -> Restraint.ShotAtRestrained along the round's own
  muzzle line; a hit ends the AI's shot via ShootData.EndShoot, logged "burst ... stopped"), and the
  box 0.8 m + 1.5 cm/m (Perception.OnLine; shared with truce/scenario shield). tools/firesim:
  13/13 (new case 13 is this raid's geometry: old fire -> new block).
- "Shot with his hands up" names the shooter (it said nobody when no one held fire at him).
- Perf detail section Decide (Captors.Decide): one surrender start cost 16 ms (Рустам: squad
  hand-off to DaddyAL, three squadmates informed, cover chosen) against 0.44 ms for an unanswered one;
  Decide says next time whether the captor decision is that cost. Other single frames: raid start
  18 ms (Registry prewarm, every raid), raid end 18 ms (Scenarios cleanup), an opening 10.7 ms (the
  old two-search code). Memory: heap flat at ~2.5 GB after the first minutes; our tracked state
  bounded (prisoners/captors/restraint back to 0, history 35, loot cache 47). The ~80-95 ms frame
  every 10 s is one GC each (not ours); Joel has Verbose logs on (58% of the log lines).
- Not changed (offered to Joel): bots surrendering mid close-range firefight often die before the
  shooter notices (Тимофей, Саша); a "lull" rule would trade fewer surrenders for more survivors.

Build passed (Debug + Release, 0 warnings); tools/firesim 13/13; packed `dist/HandsAreHigh-0.28.4.zip`
and installed in H:/SPT4.1.X (hash-verified; 0.28.3 DLL + cfg in that session scratchpad
`backup-0.28.3`); not run in game.

## 0.28.5 (2026-11-21): from the live 0.28.4 raid (3 surrenders, 0 leaks, 1 burst stopped, 0 warnings)

- Surrender frame split (Ngst, first surrender of the raid): 31.9 ms in one frame, Start 21.5 ms,
  of which Decide 8.6 ms; the rest of Start is the game's drop (weapon into the world), the voice
  line and state setup. The captor's decision now runs from the next frame (Captors.TickDue, one per
  frame, same validity rules as TickPending; cleared with Captors.Clear). Debug build passed; not
  packed (game running).
- Log typo: "take chance X, roll Ydeclines to guard him" now has its separator.
- Seen in the 0.28.4 raid: the burst guard's first stop (Echo55 at Лёма past Станислав, no hit);
  the shooter named on "shot with his hands up"; a captor blocked ~15 s behind a spared man in a
  crossfire, resolved by FireBreak (no change made). No opening hide occurred: the hide-time fix is
  still unseen in raid.

Build passed (Debug + Release, 0 warnings); packed `dist/HandsAreHigh-0.28.5.zip` and installed in
H:/SPT4.1.X (hash-verified; 0.28.4 DLL + cfg in that session scratchpad `backup-0.28.4`); not run.

## 0.28.6 (2026-11-21): from the live 0.28.5 raid (7 surrenders, 0 leaks, 0 warnings)

- Deferred decision confirmed: Кот's surrender frame 2.5 ms (Start 0.16 ms) vs 22-32 ms before; the
  decision landed in a later frame (Decide 6.6 ms in a 12.8 ms Captors frame), custody setup 7.2 ms.
- Captor walk-up: oGRiZe took custody at 22 m, his walk-up stopped "stuck" at once (under 0.5 m in
  2.5 s), he guarded blind from 22 m and retried only after 8 s; the prisoner walks off after 6 s
  unwatched, so Кот left. Now a captor who can't see his man retries after 2 s, and the stuck line
  gives the active layer, mover state and speed (cause of the stall still unknown).
- Cosmetic: the take-chance line printed "roll 0.31;  -> takes him prisoner" (double space): fixed.
- VERIFIED IN RAID (log): opening-hide timing. Толян slipped into cover during an opening and ended
  "Hiding -> Released (in cover)" after 16 s, 23 m away (the 8 s limit would have stopped him short).
Build passed (Debug + Release, 0 warnings); packed `dist/HandsAreHigh-0.28.6.zip` and installed in
H:/SPT4.1.X (hash-verified; 0.28.5 DLL + cfg in that session scratchpad `backup-0.28.5`); not run.
