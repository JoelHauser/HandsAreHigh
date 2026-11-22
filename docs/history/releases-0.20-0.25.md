# Releases 0.25.0 - 0.20.0 (notes, root causes, test plans)

> ARCHIVE, not loaded by default. Moved verbatim from the root CLAUDE.md on 2026-11-21 (last
> committed version: git f5f9180; the 0.25.0 section was uncommitted at the time). Nothing below was
> edited: statements here are as written for their release and some are SUPERSEDED. Current facts
> live in docs/engine/ (verified findings), docs/CODE_MAP.md and docs/CURRENT_STATUS.md. "NEVER RUN
> IN GAME" labels below were true when written; see docs/CURRENT_STATUS.md for what has run since.
> Original line range in CLAUDE.md: 7-680.

## Status

**0.25.0 (2026-11-21): hands one over the other behind the head, prisoners who notice being left
in ~2 s, a hand-off to their own AI mid-stride with a logged timeline, F opens and confirms the
order menu (debug: bot-held men too), three kneel variants. From Joel's pasted 4-part spec + a 5th
point (kneel variants), his screenshot (stacked fingers behind the head) and the 0.24.0 raid log
(LogOutput 2026-11-21 02:47, 8 scenarios). Release packed (dist/Mercy-0.25.0.zip) and
installed in H:\SPT4.1.X (0.24.0 DLL + cfg backed up in that session's scratchpad, backup-0.24.0).
UNCOMMITTED. NEVER RUN IN GAME.**

Root causes (evidence: the 0.24.0 log, code, skeleton.bundle, the 0.16.9 decompile):
- Stacked fingers: both palms sat +-5 cm from the middle with fingers pointing at each other
  (cupped), so both hands' fingers occupied the same space (tools/fingersim/interlace.py: the 0.24
  pose has index fingers 1.7 cm inside each other). A true interlace is NOT possible cleanly on this
  rig: knuckle pitch 2.3-2.7 cm (Digit21-51 local z) is less than two gloved fingers (~3.4 cm); the
  best of ~3,000 laced layouts (spacing, half-pitch offset, spread +-20, flex) still overlaps 0.7 cm
  at the webs. The interlace in the spec's fallback form was built instead: one hand over the other.
- Abandoned prisoners standing: after ~1 s of cues, a 2.5-9 s x nature (0.4-2.2) "takes it in"
  wait (log: #3 8.4 s, #4 7.2 s, #8 10.1 s). No gear check: a captor stepping aside to bend for
  loot built the same evidence (abandonsim: 56% false "left" on a 2 m loot step).
- Slow hand-off: our layer drove every let-go / walk-off through up to 4 flight legs until 40 m
  away and unseen, then Regroup -> Decide -> HandBack (StopMoving, then wait for arms/kneel) -> the
  registry; the 0.24.0 log: hand-backs 11.2-13.6 s after leaving captivity, #3 via Hiding.
  The layer's own IsActive is honest (HoldsBot); no objective is stashed or restored by this mod
  (ORBIT keeps its own: the +84 s CIRCLED BACK in #6 was ORBIT's layer). Separate captor-side bug:
  a captor who had just walked away was pulled straight back onto the captor layer as an idle
  "watcher" (#7 PMC-2: "STILL ON OUR LAYER" 4 s after his hand-back).
- F: the menu only knew its own keys (/ . =); the game's Interact (EGameKey.Interact ->
  ToggleInputKeyCombination BeginInteracting/EndInteracting) was never filtered; bot-held men were
  excluded from LookedAt/Validate.

What changed:
- Kneel.ApplyHands / Fingers (setting "Hands behind head: one over the other"): bottom palm on the
  skull 6 cm to its side, fingers to the middle (cupped); top palm 5 cm along the bottom hand's
  fingers and 3.8 cm off its back, palm parallel, fingers the other way, OverTop shape 15/10/6 ..
  21/16/10. Top hand chosen once per man (60% right). Targets from the skull frame only. Each hand
  its own progress (Kneel.HandT: bottom leads to 0.85, top starts at 0.15; reversal: top leaves
  first), the top hand's path held outside the bottom hand's shell; fingers close over the last
  45% of each hand's move. overhand.py: 0.36 cm clear, skull no closer than 0.24, either hand on top,
  helmet on/off.
- SurrenderLogic.Abandonment: threshold 1.0, cue weights x2-ish scaled by how far he has actually
  gone (no trigger on a 1 m step), then AbandonAssess 0.3-1.2 s x nature (<= 2.5 s; cfg migrated
  from 2.5/9 at version 25), bound 20 s (unguarded time only). Holds: CaptorLoot.GatheringNear
  (gear within 6 m of him), a squadmate covering (as before), a player within 25 m aiming his way
  (PlayerCovers). abandonsim: median 6.5 -> 1.8 s, 90% 10.8 -> 2.5 s; loot-step 56% -> 0%.
- Quick hand-back (section 27): any non-emergency flight (walk-off, Leave order, captor gone) ends
  in QuickClear once arms are down, legs his own, walking, and 8 m out / unseen + 4 m / 5 s ->
  Decide (kept gun drawn first) -> HandBack without StopMove (HandBackMoving: Released, the layer's
  Stop and the registry all skip the stop; his AI's first move replaces our path). Escape memory /
  KeepAway unchanged. Emergency flights unchanged.
- ReleaseWatch hand-off timeline: per-frame layer read for <= 10 s after the hand-back; one line
  "hand-off timeline: left cues +0, noticed, walk-off/let go, arms down, on his feet, moving, our
  layer off (mid-stride), his AI (layer), moved by his AI; longest step X -> Y".
- Captors.WantsControl: a bot that ended an encounter himself isn't re-pulled as a watcher
  (Encounter.EndedBy).
- F (section 27 "Interact key opens the order menu"): InputGuard reads the Interact binding (F
  fallback), removes BeginInteracting/EndInteracting while the menu owns input AND while you look at
  a man it could open on (ClaimInteract, every frame: the game may dispatch before our Update), and
  latches the key (hold and release stay ours). OrderMenu opens on it; confirm on a later frame only
  (_openFrame); prompt shows the key. Debug keys on + "Debug: order prisoners held by bots": LookedAt
  and Validate accept bot-held men (header "DEBUG: X's"); the first order goes through Orders.Give's
  take-over (EndCustody first: guarding layer, squad cover, loot job released).
- KneelVariant.cs (Kneel is now partial): Upright / Settled (thighs 46 deg, hips back ~0.26 m and
  down 0.13 m, lean +5) / Tense (thighs ~2, hips +5 cm, lean +10). Knees and feet are Layout's for
  all (no slide, ground/room checks hold, feet frame untouched); hips on the arc that keeps the
  thigh length. Chosen in TryBegin (demeanor, nerve, hurt legs, weighing an opening; one roll per
  man), Settled needs level knee/foot ground (<= 5 cm) and room for torso/pack further back.
  Descent upright, then settle 1.4 s / lean 0.7 s; re-check every 2 s, change only on a mood change,
  at most every 20 s (1.6 s blend). Rise from settled: weight forward first (0.6 / 0.4 / 0.3 s
  compliant / defensive / escape; the rise clock waits 65% of it). kneelsim/variants.py: knee
  flexion 109 / 103 / 147 (140-153 on steps), buttock above the heels, knee fixed in every blend,
  hips <= 28 mm per frame on the urgent shift (0.22 s was 38: slowed).

Tests done (offline): Debug + Release builds 0 warnings; pack.ps1; Settings.Bind on a copy of
Joel's cfg through the built DLL, twice: 452 -> 464 entries (the 12 section-27 keys), only the two
abandonment values migrated (2.5 -> 0.3, 9 -> 1.2), second load stable. tools/fingersim interlace.py
+ overhand.py, tools/abandonsim, tools/kneelsim/variants.py: PASS. NOT done: anything in a raid.
Not verified in game: the over-hand look on real meshes (gloves, helmets, bulky sleeves), whether
the top hand's path ever brushes the bottom hand mid-move, F vs a door/loot right behind a
prisoner, the settled pose with a big backpack and on slopes, SAIN's first move after a mid-stride
hand-back.

### In-game test plan (0.25.0)
Log decisions, Log kneel, Log escapes, Log encounter endings on; debug keys on.
1. Hands on head (standing, kneeling, each variant; helmet and none; turn around; Interrupt
   reverse): one hand lies over the other, fingers settle as each lands, the top hand arrives last
   and leaves first, no fingers through fingers, head still turns freely. Look from behind and side.
2. "PMC captures Scav" x5 (walk away endings): "notices ... (N m, cues for X s); reacts in Y s",
   then "hand-off timeline: ..." - total under ~4 s from cues to moving, longest step named. A
   captor bending for gear by him: no "notices".
3. Leave order yourself x3: walks/runs off, "on his way, ... his own AI takes over", timeline line
   with "our layer off (mid-stride)" and his AI moving him within a second; no stop in between.
4. F: look at a held man next to a door or loot: F opens the menu (door untouched), F gives the
   highlighted order, number keys still work, holding F does nothing more, F after closing opens
   the door. A "PMC captures Scav" prisoner: menu says DEBUG, first order logs "you take over ...
   (DEBUG: X held him; his custody ends first)" and the PMC goes back to his own AI.
5. Kneel x10 on varied ground: "kneel: will kneel <variant> (...)", settled men sit back after
   landing, tense men lean in; Stand up / Leave / fire from settled: "weight forward off his heels
   first", no snap; a slope or a wall behind: "can't sit back here (...): upright".

### 0.24.0

**0.24.0 (2026-11-21): open hands, an optional dropped knife, escapes that don't circle back, a
grenade is not a weapon, self-defence after release, no hostility from timers. From Joel's pasted
6-part spec, his screenshot (curled fingers) and the 0.23.0 raid log (LogOutput 2026-11-21, 34
scenario launches). Release packed (dist/Mercy-0.24.0.zip) and installed in H:\SPT4.1.X
(0.23.0 DLL backed up in that session's scratchpad, backup-0.23.0). Committed and pushed to main 2026-11-21 (no tag/release). NEVER RUN IN GAME.**

Root causes (evidence: the 0.23.0 log, the 0.16.9 decompile, SAIN 4.5.1 decompile, skeleton.bundle):
- Curled fingers: nothing of ours ever wrote a finger bone (ArmPose.SetWrist only READS Digit31/
  Digit11 to lay the palm). Rig (tools/fingersim): Base Human{L,R}Digit{1..5}{1..3}, child along
  local -X, flexion about local +Z, right = left with x,y negated; the bind pose itself is curled
  (index ~72 deg over 3 joints). Not verified: whether the animator rewrites fingers every frame
  (Fingers.cs detects it per surrender and logs it).
- Captor always fetching the knife: CaptorLoot.IsDemanded counted IsMelee items as "the weapon he
  made him drop" (always wanted, Demanded risk tolerance, postponed "later"). The Scabbard item is
  EFT.InventoryLogic.Knife : Item (KnifeComponent), not a Weapon. Log: #29/#30/#32 bent down by the
  man for a 6Kh5 / a 3,560-rouble MPL-50.
- Circling back, grenade throws, "shared walk then aggression": BotWeaponManager.ShootController is
  only cleared in Dispose, so after our drop HaveBullets (= _currentWeaponInfo.Reload.BulletCount,
  read from ShootController.Item) stayed TRUE for an empty-handed man. SAIN's EnemyDecisionClass
  tests "!HaveBullets -> Retreat" first; past it: StandAndShoot / Rush / ThrowGrenade / MoveToEngage
  / Search (squad GroupSearch / Help come even earlier). SAIN's grenade check needs the enemy NOT
  visible for 3 s, last known 8-75 m, updated within 120 s, every 5-10 s: exactly a man who got
  away. Log: every released man went to "SAIN : Combat Layer" targeting his ex-captor; #2 walked from
  54 m back to 25 m and died; #19 had walked 32 m and was 8 m from where he was held; #2, #9, #20
  took a grenade into their hands seconds after the hand-back (which ended every hold-fire: a
  grenade in hand counts as a weapon), and the captors opened fire.
- Walk-off release: Abandonment's "walk off" was HandBack at 0 m from where he was held (#9, #18,
  #19, #26): his own AI started next to his ex-captor, with the stale gun above.
- Released men stopped 5-15 m away: LegDone ended a flight once 12 m away and out of sight.
- Timer as provocation: Restraint.TickDetached dropped every hold-fire decision at once after 30 s
  (ForgetPrisoner): ignore flag, GoalEnemy and SAIN guards off for every observer, whatever they
  perceived.
- Self-defence: under fire in Retreating/Regrouping/Hiding the only answer was cover, even with a
  loaded gun on him. GunOnBody counted any gun (empty, broken, flare gun) as "a gun to draw".

What changed:
- Fingers.cs (new): per-joint target = bind swing x flexion table (open: index 5/9/5 .. little
  11/17/10, thumb 4/6; cupped round the head ~15/25/15; flat on the thigh for the brace hand),
  blended open<->cupped by HandsPose's own eased s.HeadHands (fingers move with wrists/elbows,
  reversals included), laid over by s.Weight (fades with the arms), left hand x (1 - GestureArm).
  Applied last in ArmPosePatch. Animator detection: first posed frame keeps the hand (FingerBase);
  next frame, unchanged bones = not animated -> blend from base, Fingers.Restore on every registry
  exit (SurrenderRegistry.Release). Log once: "fingers: the game's animator writes them..." or
  "fingers: nothing else writes them...". Setting "Open hands" (0 = game's own).
- WeaponState.cs (new): for bots whose gun we threw (Arsenal.EmptyHands -> Track), with no
  FirearmController in hand: HaveBullets getter postfix -> false; BotReload.TryReload prefix -> false
  (no reload on the stale controller; instead, with an enemy and a usable gun in a slot and not held
  by us: Selector.TryChangeWeapon, <= every 2 s). Logged once per bot. Setting "Disarmed bots know
  they are unarmed".
- Arsenal: GearKind (Firearm / Melee / Throwable / Other) from the game's classes; Usable(gun):
  not a flare gun, durability > 0, rounds in it or Ammo of its calibre in rig/pockets/backpack/secure
  (cached 4 s); UsableGun(bot, why); Equipment(bot) line; DescribeArmed names grenades but never
  counts them; Draw prefers a usable gun. GunOnBody unchanged (what must be disarmed).
- SurrenderLayer: every fight/flee decision uses UsableGun; Decide logs equipment once, refuses
  recoveries with a logged reason (in the capture area for "Remembers the capture for", route passes
  the captor (8 m), under fire -> cover first), knife comeback only if the captor is dead; walk-off
  = a calm Retreating flight (CalmFlight: walk 0.85, any fire ends it); flights end only when clear
  ("Escape ends this far from the threat" 40 m from where held, 3/4 of it from the threat, unseen) or
  hidden and well away, up to 4 legs; replan when the captor he SEES is heading near his spot or the
  same way; DefendSelf: under fire with a usable gun and free hands, fight back vs cover (logged),
  draw faces the shooter (FaceThreat).
- Perception.FleePoint: optional threat velocity (only when seen): route refused within 8 m of the
  threat (was 5) or 6 m of the captor's path over the look-ahead, spot scored against where he will
  be, same-heading spots penalised, the search leans away from where he will be; rejection counts in
  the log line.
- Escape.cs (new): after every hand-back, a memory (capture spot, threat where last SEEN) for
  "Remembers the capture for" (150 s); unarmed + not under fire + within "Keep away radius" (25 m)
  and closing >= 3 m in 3 s -> KeepAwayLayer (priority CaptorPriority + 2 = 72: above SAIN combat and
  ORBIT, below SAIN avoid-threat 80) runs one leg away (FleePoint, <= 20 s, ends at once under fire or
  with a gun in hand), at most "Keep away legs" (3). Armed: logged, not prevented. Cleared on raid
  change / teardown (layer inert after hot reload).
- Restraint after the hand-back: a weapon in his hands -> "RENEWED HOSTILITY: ... takes a <gun/knife/
  grenade> ... (his AI: layer, target)"; at the 30 s expiry each observer separately: sees him -> ONE
  explicit decision per release (FleeingRespect x mercy) logged as "lets him go again" or "RENEWED
  HOSTILITY ... decides to engage"; doesn't see him -> the hold stays; at the memory's end all holds
  drop, and an unseen man's stale vanilla target is cleared. ForceAttack (scenarios).
- CaptorLoot: melee items not Demanded; MeleeWant (base "Captors pick up a dropped knife" 0.3 x greed
  x use (none of his own 1.4 / has one 0.35) x value 0.6-1.5 by 40k x 1.15 within the man's reach),
  risk tolerance LootRisk x 0.8, never "later"/"after release": leaving is final.
- ReleaseWatch: followed for max(watch, memory), lines at +15/30/60/90/120 s with furthest distance;
  "CIRCLED BACK" when back within the keep-away radius after being 15 m further out.
- Settings section 26 "Release and escape" (10 keys). Scenarios: "Open hands", "Dropped knife:
  captor's choice", "Release and escape" (keeps nothing / gun removed / loaded sidearm / empty
  sidearm / grenades only; walk away / Leave / his own), "Captor attacks after release" (while still
  ours / 5 s after the hand-back). Kit presets "Grenades only", captor "No knife".

Tests done (offline): Debug + Release builds 0 warnings; pack.ps1 name check; Settings.Bind on a copy
of Joel's cfg through the installed DLL, twice: 442 -> 452 entries, exactly the 10 new keys, no value
changed, second load stable. tools/fingersim (skeleton.bundle): open/cupped shapes, no joint bent back,
tips palm-side, neighbour tips 3.4-4.3 cm (bind 3.2-3.7), open<->cupped monotonic: PASS.
tools/escsim (FleePoint rules, 300 seeded runs x 4 cases): captor walking the escape line: walking off
alongside him 70% -> 0%, within 6 m after 4 s 100% -> 0%; walking across 30% -> 0%; standing / walking
away unchanged: PASS. tools/releasesim: 0.24.0 graph bounded (worst 241 s + <= 3 x 20 s keep-away legs).
firesim 12/12. NOT done: anything in a raid. Not verified in game: whether the animator writes the
finger bones (the log says), the finger look under gloves and helmets, that SAIN retreats (not
searches) once HaveBullets reads false, KeepAway vs ORBIT objectives, the knife comeback rule.

### In-game test plan (0.24.0)
Log decisions, Log loot, Log escapes, Log restraint, Log encounter endings on.
1. "Open hands" (Scav and PMC, standing and kneeling, Interrupt reverse, Turn): fingers open with hands
   up, curved round the head on it, never a grip during a switch or reversal, no snap; note which
   "fingers: ..." line appears; after the Leave the fingers loosen to the game's own.
2. "Dropped knife: captor's choice", knife as generated and removed, own roll x5: mostly
   "<knife>: leave (melee weapon (Knife, KnifeComponent ...) ... leaves it)" when he has his own,
   more takes when he has none; a left knife never brings him back, "done with ..." follows.
3. "Release and escape", each "He keeps", ending Walks away: "walks off to a spot ... (N spots tried
   ...)", "retreat ends (..., clear)", the equipment line, then draw (loaded) or refusals + hand-back;
   once "'has bullets' reads false"; +15..+120 s lines: furthest grows, never CIRCLED BACK, layer not
   chasing his captor; Grenades only: no "takes a grenade in his hands"; any "keeps away, leg ..."
   line names what pulled him back.
4. "Captor attacks after release", both timings x both kits: "RENEWED HOSTILITY ... DECIDES TO
   ATTACK", then fight back (sidearm) or cover; after the hand-back his AI reacts; no busy hands.
5. Natural raid: release a few prisoners yourself and watch them for 2 minutes.

### 0.23.0

**0.23.0 (2026-11-21): encounters end on their own; prisoners notice being left; one voice per
encounter and no silent squad kills; kneeling turns without twisted feet; captors demand gear they
lack; pick-ups from a spot beside the item. From Joel's pasted 7-part spec and the 0.22.0 raid log
(LogOutput 2026-11-21 04:58, 5 scenarios). Release packed (dist/Mercy-0.23.0.zip) and
installed in H:\SPT4.1.X (0.22.0 DLL backed up in that session's scratchpad, backup-0.22.0). Committed and pushed to main 2026-11-21 (no tag/release). NEVER RUN IN GAME.**

Root causes (evidence: the 04:58 log, code, the 0.16.9 decompile in an earlier scratchpad):
- Captors hanging around: Captors.Check waited for HoldUntil (25-70 s) before the Leave step of
  every plan except "disarm and release", and every plan ended in a Leave order. Log #7: plan done
  ~38 s in, "lets him go within 61 s", stood over a kneeling man until Joel intervened.
- No way to walk off: custody could only end by an order, fire, a kill or lost sight; a captor
  who just left made the prisoner run (DecideOnLeave: run / hide / draw, all flights).
- Squadmates shooting a prisoner (no case in this log; found in code): (1) Restraint.InformSquad
  told only squadmates within SquadNoticeRadius at custody; one further away got no decision, so
  his AI (and SAIN, which shares the group's enemies) fought the man on sight; the scan only
  decided for squadmates who could see him. (2) SquadDecision re-rolled SquadRejectChance at
  every re-decision (situation change, hand-over, expired entry): a squadmate who had held fire
  could later "decide" to shoot a compliant man, logged only as a restraint line. (3) A hit by a
  bot with no decision was not logged at all (OnPrisonerHit returned).
- Squad dialogue: no shared turn-taking; each bot only checked his own speaker and his own
  prisoner. (Log spam fixed too: "his captor isn't covering him, but X is" 4x a second, and
  "loses sight of his captor" on every Presence reset.)
- Twisted foot on a kneeling turn: Kneel.TryBegin committed the kneel's frame (KneelAnchorFwd,
  the feet's orientation) as soon as Facing was within 10 deg, while the game's stepping-turn clip
  and the pivot were still turning the root; nothing held the root while kneeling. Legs solved in
  the old direction, hips still turning; CheckMoved re-anchored at >10 deg (log: "kneel: moved
  while down" right after the re-kneel). The rise pivot allowed 70 deg over planted feet.
- Loot jitter / walking into the prisoner: the fetch path went to the item itself (shortened
  0.7 m along the path); an item at the man's feet put the path end in his capsule; the
  settle test was distance-to-item + speed; any threat level (even Alert, an enemy seen 5 s ago)
  stopped a 2 m pick-up and it restarted later.

What changed:
- Encounter.cs (new): once nothing is left to do (plan steps given, no gear to take now, no
  demand under way; a postponed demanded item keeps him 20 s at most), the captor lingers
  (Lingers min/max x caution; x0.35 with an enemy about) and decides ONCE (logged with inputs):
  lethal intent first (Judgement.AtRelease, unchanged), then Leave order vs walk away (Walk away
  chance x boldness, x1.8 enemy about, x1.5 squad moving on, x1.3 kneeling/turned away, x0.4 a
  weapon still on him), and with an effective covering squadmate maybe GuardBehind (Captors.HandTo:
  custody to the guard, leader's decision inherited, no new lethal roll, plan Stay+Leave, 8-20 s).
  Walk away = End(released) with no order, no deferred fetch, prisoner left Accepted. Every
  ending: "back to his own AI (...)" and a "4 s after the hand-back: layer <name>" line.
  No recapture: Encounter.Remember/Recently: that captor's group takes nobody of that man (Captors.
  Impractical) and he doesn't surrender to them (SurrenderRegistry.Surrender) for 60 s.
- Prisoner (SurrenderLogic.Abandonment): custody over, bot captor alive, Accepted: evidence from
  cues he can see/hear (walking off back turned 1.6/s, walking off 1, standing off + gun away 0.6,
  turned away 0.35, heard walking off behind 0.9, out of sight 1/s; covered again -3/s); at 1.2
  he takes it in (Assess min/max x AssessFactor: nerve, personality, kneeling), cancelled if the
  captor covers him again; a squadmate he sees covering him (Reactions.SquadCovers, now internal)
  keeps him in place. Decision once: walk off (HandBack -> Released -> his own AI), get his own gun
  (<=12 m, captor out of sight or >20 m, reachable) or draw a kept gun (same distance rule).
  Bounded 45 s. No openings weighed meanwhile. Replaces DecideOnLeave for this case only.
- Restraint: custody informs the whole squad (no radius); the scan gives a custody squadmate his
  decision without sight; a holding squad decision is carried over without a roll; a squad
  refusal is SquadKill: "DECIDES TO KILL ... against his squad (...)", Final; a hit by a bot with
  no decision: "PRISONER HIT WITHOUT A DECISION" (warning, custody squad: then the squad rule
  applies) or an info line for an outsider, once per shooter and man.
- Speech floor (Encounter.MaySay / FloorFree / TookFloor; CaptorVoice, Voice): orders only from
  the custody holder; a covering squadmate only Covering/Threat/UnderFire/Resist/TakeOver; a
  non-urgent line waits while another of the encounter speaks or within his clip + 1.2 s.
- Kneel: TryBegin waits "still turning" (TurnDriver.DriveMode, or yaw moving >1.2 deg between
  0.2 s checks; bounded 2.5 s); HoldRoot keeps the root yaw on KneelAnchorFwd (ApplyRotation)
  while going down, down and rising before the pivot (logs once if >2 deg was corrected); MaxPivot
  70 -> 40.
- GearNeeds.cs (new): captor with no ArmorVest / TacticalVest / Backpack; the prisoner's item in
  that slot must land in that very slot by the captor's own FindSlotToPickUp (decompile:
  InventoryEquipment.FindSlotToPickUp -> Slot.FindLocationForItem/CheckConditions), armour class
  >= 2 and >= 30% durability (worst ArmorComponent.Repairable), rig >= 6 cells or armoured,
  backpack >= 12 cells; want 0.8 x greed; one demand per slot per encounter (OrderKind.DropItem,
  captor only); the dropped item is a Needed loot candidate (wanted, demanded risk); after the
  transfer the slots are read again. The pick-up's QuickFindAppropriatePlace(PickUp) includes
  TryEquip (decompile: PickUp = 0x33, TryEquip = 0x20) so it equips into the empty slot.
- CaptorLoot.StandPoint: one spot beside the item (0.5-0.85 m, >= 0.8 m from the prisoner,
  capsule clear of PlayerStaticCollisionsMask, NavMesh, line to the item, <= 3 path solves, own
  side first); walked to exactly, arrival = the spot; re-chosen only if the prisoner steps into it
  (checked 2x/s, twice at most). No spot: the man is told once to step back 1.3 m
  (OrderKind.StepBack / Order.StepBack, a walk with hands up, 5 s max), else after the release.
  Near pick-ups are stopped only by Fire/Contact (Alert stops a distant walk only). The look
  target no longer flips at 3 m (Close latch).
- Settings section 25 "Encounter endings" (14 keys). Scenarios: "Encounter ending: captor walks
  away", "Encounter ending: squad leaves a guard", "Squad dialogue", "Squadmate and the prisoner",
  "Kneeling turns", "Captor demands gear", "Pick-up at the prisoner's feet"; captor kit presets
  No body armour / No rig / No backpack / all three.

Tests done (offline): Debug + Release builds 0 warnings; pack.ps1 config-name check; Settings.Bind
on a copy of Joel's cfg through the built DLL, twice: 411 entries, exactly the 14 new keys added,
no existing value changed; tools/standsim/standsim.py 20/20 (spot found for every gun position at
the feet, >= 0.80 m from him, look-down 63-67 deg; corridors to 1.1 m wide; captor behind);
firesim 12/12 and releasesim unchanged. NOT done: anything in a raid. Not verified in game: ORBIT
taking a captor back after a walk-away, SAIN's look/target right after the hand-back, whether
HoldRoot fights a clip's root motion visibly, the StepBack walk.

### In-game test plan (0.23.0)
Log decisions, Log loot, Log encounter endings, Log restraint, Log voice on (Verbose for floor waits).
1. "Encounter ending: captor walks away", Standing / Kneeling / Kneeling turned away, ending his
   own: `done with ... lingers`, `how his hold on ... ends: ... -> walks off|tells him to go`,
   `4 s after the hand-back: layer <SAIN/ORBIT layer>, moving`. Prisoner: `notices ... takes it in
   for N s`, `left by ...: -> walk off|get his gun`, `back to his own AI`, `after release +15/+30 s`,
   no STILL NEAR. Repeat with Walks away (FORCED) and gun removed.
2. "Encounter ending: squad leaves a guard" (3): `hands ... over to <guard> (stays behind ...)`,
   prisoner `... still covers him: stays put`, then the guard's own ending and the prisoner leaving.
3. "Squad dialogue": orders from PMC-1 only, one `says` at a time; hostile bot: UnderFire at once.
4. "Squadmate and the prisoner": `Restraint: <PMC-3> holds fire ... (squad)` at custody although
   45 m away; never PRISONER HIT WITHOUT A DECISION / RESTRAINT LEAK. Deliberate: `DECIDES TO KILL
   ... against his squad (FORCED ...)` then `shot ... as decided`.
5. "Kneeling turns" x3, each interrupt option, on flat ground, stairs and rubble: feet turn with
   him, no `kneel: moved while down` after a re-kneel; `kneel: waits (still turning)` (Verbose).
6. "Captor demands gear", each Captor lacks: `gear he lacks ... -> tells him to drop it` or
   `won't do (...)`, `took ... into his ArmorVest/TacticalVest/Backpack slot`, never asked twice.
7. "Pick-up at the prisoner's feet": open ground, a corridor, a small room, standing and kneeling:
   `stands to pick up the ... N m from it, N m from <prisoner>`, one smooth walk, no capsule push;
   confined: `tells him to step back off it` / `takes it after letting him go`.

**0.22.0 (2026-11-20): turns without shoulder lag, captors answer the real shooter (not the
prisoner), a clearer hands-up pose, seamless hands-up <-> hands-behind-head. From Joel's pasted
3-part spec + a 4th part (transitions) and the 0.21.0 raid log (LogOutput 22:49) and screenshot.
Release packed (dist/Mercy-0.22.0.zip, DLL only) and installed in H:\SPT4.1.X (0.21.0 DLL
backed up in that session's scratchpad, backup-0.21.0). Committed and pushed to main 2026-11-21 (no
tag/release). NEVER RUN IN GAME.**

Root causes (evidence: the 22:49 0.21.0 log, the 0.16.9 decompile, offline sims):
- Shoulders lagging a turn: while he turned his back, Presence kept his attention on the captor
  behind him, measured from the turning root. The torso's share of that look (ApplyUpper, which runs
  before the arms are posed in the shoulders' frame) wound the upper spine, shoulders and raised arms
  back towards the old direction until he "lost" the captor at ~125 deg ("loses sight of his captor
  (about 128 deg round ...)" on every Turn around), then it sprang round. Also TurnDriver: the step
  turn handed over to a pivot at 70 deg left (every log turn: "stepped round 108-113 deg ... the rest
  by a pivot": ~65 deg spun on planted feet), and at that hand-over the look's lead fell from 60 to
  <= 35 deg in one frame while TrunkRotationLimit went 80 -> 45 in the same frame. Decompile
  (MovementState.ProcessUpperbodyRotation, isAi): the root is SET to look -/+ limit when the look is
  past it, so that was a 15 deg root snap plus a 25 deg upper-body swing against the turn. And
  Facing.Settled (2) < TurnDriver.Dead (3): a turn resting 2-3 deg short never finished ("couldn't
  finish turning (2 deg off after 8 s)").
- Captor shooting the freed prisoner (#6 "Incoming fire breaks the hold-up"): the hand-back
  (Phase.Released -> registry exit) called Restraint.ForgetPrisoner, so the ignore flag and every
  guard came off; the unarmed man 2 m away was the captor's nearest known enemy, shot 1.2 s later.
- Captor not reacting to being shot (#7 "PMC captures Scav", Joel hit the captor): FireBreak.SameSide
  counted any shooter the captor wasn't hostile to as his side. BotsGroup.IsEnemy only knows players
  already in the group's Enemies (decompile BotsGroup.cs:832) and an EnemyInfo exists only once he has
  noticed them, so an unseen shooter, the very case of being shot, was "friendly": neither the hit
  hook nor Captors.FreshFire broke the hold-up. In scenarios the observer override then also kept the
  captor from ever targeting Joel (6 hits, no answer).
- Half-raised hands (screenshot): the arm frame's up was ribcage -> head, which leans forward with the
  idle stance (more kneeling). armsim3: at 20-30 deg lean the 0.21.0 defaults put the forearms 33-43
  deg off vertical and the wrists below the top of the head.
- Hands-to-head reversal: MoveTowards + SmoothStep flipped the hands' speed in one frame (posesim:
  3.3/s per frame).

What changed:
- TurnDriver: steps continue while > 48 deg is left (look on the target once < 60 away, still > 45);
  the look lead is rate-limited (220 deg/s, 330 while stepping); the raised trunk limit ratchets down
  with the look (never below |HandsToBodyAngle| + 1, back to the game's within 0.75 s); StopNow for
  leaving the registry/teardown (immediate restore). Facing.Settled = Dead + 0.5.
- Presence.Smooth: while TurnDriver turns him for anything but facing his captor, the look leads into
  the turn (30% of what is left, max 15 deg, relative to his body: no lag) and a Turn around lets go of
  the captor at its start (log "turns his back: stops watching his captor, looks the way he turns").
- ArmPose.ArmFrame: right = shoulder line (as before), up slerped from the torso's towards gravity by
  new "Forearms stay upright" (0.8). New defaults raise 22 / forward 20 / forearm forward 4 / inward 15
  (were 12/25/12/8), migrated once (Config version 22) only where still at the old defaults. One log line
  per surrender once the raise is done: "hands-up pose: wrists +X / +Y cm against the top of his head,
  forearms N deg off vertical, ... torso lean L deg" (Log decisions or Verbose).
- HandsPose: an acceleration-limited profile (HeadHandsVel) replaces MoveTowards+SmoothStep; the
  kneel windows are eased there instead; out of HandsUp the hands leave his head within 0.35 s.
  Kneel.ApplyHands is unchanged: it already starts every frame from the live hands-up arm (continuous
  at h = 0, same path back on a reversal); armsim3 re-checked it from the new start.
- FireBreak.SameSide: same BotsGroup or BotsGroup.IsAlly only; a non-hostile BOT is left out for a
  near miss only. Credible(s, shooter, hit). Hits always count (unless squad/ally).
- ScenarioShield.ObserverAttacked: you hitting a captor or prisoner lifts the Observer pairs that
  shielded you from him and his squad (they fight you with their own AI; nothing points them at you).
- Restraint.Detach/TickDetached: a man handed back with empty hands keeps the hold-fire decisions
  (Respect/Ignore/LetGo; Reject and Final dropped) for "Unarmed ex-prisoner spared for" (30 s, section
  24); a gun, knife or grenade in his hands (Player.FirearmController / BaseKnifeController /
  BaseGrenadeHandsController, decompile) ends them at once and witnesses Distrust him; dead, caught again
  or expired ends them. The trigger guard (no firing through him) keeps working in that window.

Tests done (offline): Debug + Release builds 0 warnings; tools/armsim/armsim3.py (pose under 0-30 deg
lean, helmet; behind-head paths from the new start: >= 2.6 cm clear with a helmet, never past the face,
0 elbow flips, <= 1.4 cm/frame, reversals, head turned +-40); tools/posesim/posesim.py (turns +-176/177,
120, 90, 60: no root snap, trunk <= 5.5 deg/frame, 0 torso twist against the turn, settles, limit back to
45, <= 45 deg pivoted; hands profile: reversal speed change 0.46 vs 3.33/s per frame, escape 0.30 s,
no overshoot); firesim 12/12 and releasesim unchanged; Settings.Bind on a copy of Joel's cfg through the
installed DLL, twice (397 entries; exactly the 4 pose keys migrated + 2 new keys). NOT done: anything in
a raid. posesim's body model (clip ~100 deg/s while |look - root| > 45) is a model of the decompiled
rules, not the animator itself.

### In-game test plan (0.22.0)
Log decisions, Log incoming fire, Log attention on.
1. Pose: any hands-up prisoner, front/side/rear, with and without helmet/backpack/plate carrier: hands
   clearly above the head, forearms near vertical, elbows bent ~100 deg, no hands in the head, no
   shoulders into the backpack straps. Log `hands-up pose: wrists ...`: expect wrists ~+2..+6 cm against
   the top of his head (palms ~5 cm higher), forearms ~15-20 deg off vertical. Kneeling: same look.
2. Turn around standing, hands up, both directions, 3 times in a row: shoulders and arms turn with the
   chest, head leads a little into the turn, no 15 deg body snap at the end of the steps; log
   `stepped round ~130 deg ... the rest by a pivot`, `turns his back: stops watching his captor`,
   `turned round in ~2 s` (never `couldn't finish turning`). Repeat with Hands on head.
3. Turn around, then Turn around again half way; Turn around from kneeling (gets up, turns, kneels).
4. Hands on head <-> Hands up, repeatedly and mid-move, standing and kneeling, during a turn: no
   snaps, the hands come back along the same path round the ear; helmet: no clipping.
5. Scenario "Incoming fire breaks the hold-up", near miss on the prisoner: expect `Restraint: <prisoner>
   handed back with empty hands (...): <captor> keep holding fire at him for up to 30 s` and NO shots
   by the captor at him; with a knife/gun in his hands later: `takes a gun/knife in his hands after his
   release: nobody holds fire at him any more`.
6. Scenario "PMC captures Scav", shoot the captor yourself (observer override on): expect `Scenario
   shield: you hit <captor>: the observer override is lifted ...` and `INCOMING FIRE: <captor> (the
   captor) hit by lowlvltest ...: the hold-up breaks`; he should fight you. Same with a real hostile
   third party ("Captor comes under fire"): from front, side and behind the captor.

**0.21.0 (2026-11-20): incoming fire breaks a hold-up; released bots go back to their own AI; door
and wall fixes on our paths; trigger guards fixed (the "friendly" scenarios); kneel look clamp.
From Joel's pasted 7-part spec. Release packed (dist/Mercy-0.21.0.zip, DLL only) and
installed in H:\SPT4.1.X (0.20.1 DLL backed up in that session's scratchpad, backup-0.20.1).
Committed and pushed to main (no tag/release). NEVER RUN IN GAME.**

Root causes (evidence: the 18:06 0.20.1 LogOutput, the 0.16.9 / SAIN decompiles):
- Released bots "run, stop, crouch": a bot left the registry (our layer) ONLY through Rearmed. A
  released man with nothing to draw ran the flight legs, then Regrouping held him crouched (pose 0.6)
  4-12 s, then Hiding crouched him (pose 0) and "looked again" every 8 s, forever (log: ModularQuill
  #S14 Hiding <-> Regrouping "nothing to fight with" for the rest of the scenario). His own AI never
  got him back.
- Walking through doors: the game opens a door on a bot's path only when the driving logic calls
  BotOwner.DoorOpener.UpdateDoorInteractionStatus every frame (vanilla MoveNode.DoorOpen; SAIN does
  doors in its own mover). Our layers never did, and a shut door doesn't cut the NavMesh
  (NavMeshDoorLink carves only for an open leaf), so BotMover carried bots through shut doors.
  Also: Arrived ignored height (a spot on the floor above counted as reached).
- "Everyone friendly" scenarios: ScenarioShield.AimedAtShielded blocked any trigger press with the
  observing player within a flat 6 deg of the aim at any range (4 m wide at 40 m) and never checked
  the shooter's real target in front: #17's hostile third party aimed at the captor for 30 s and every
  shot was "blocked at trigger" (44 logged blocks across 10 scenarios, rate-limited). Restraint's
  guard (AimedAtRestrained) had the same flaw for a captor's squad firing past a prisoner.
- Incoming fire never ended a hold-up: fire on a captor moved him to cover with the prisoner told to
  follow (Cover mode) or kept custody through the fight (Engage); fire on the prisoner only mattered as
  a hit in HandsUp.

What changed:
- FireBreak.cs: credible fire = a hit (prisoner's own BeingHitAction hook; one hook per captor,
  synced 4x/s, the boss layers' pattern) or a near miss within "Near miss counts within" (1.5 m) of
  either chest with a clear line, or the bot captor's own threat sense at Fire that began during this
  custody (Captors.FreshFire; BotMemory.SetUnderFire's `source` now recorded as BotHistory.FireBy).
  Not credible: the prisoner, the captor himself (warning shots, executions), his side (same
  BotsGroup / not hostile to him; your teammates or GroupId for a human captor). Queued, acted on in
  SurrenderRegistry.Tick (a hit at once, a near miss after 0.12-0.35 s): captor -> Captors.Interrupt
  (End(interrupted): no fetch-later, no "resisted" guard watch, no reopened offer; his layer lets go);
  prisoner -> SurrenderLogic.UnderFire. A human captor is never touched.
- Prisoner under fire (EscapeFire, weighed once, logged): cover (CoverPoint hidden from the fire,
  sprint), draw a kept gun (only with hands free: no draw over a pending drop/hands change), or a dash
  for his own gun (<= "Goes for his gun under fire within" 6 m, still lying there, reachable, not
  claimed, x0.35 if in the shooter's view or out towards him; budget 1.2 s + 1 s per 3.5 m; a hit or
  new exposed fire on the way -> leaves it for cover). Kneeling: escape rise (Kneel.Urge before any
  hand-back can overwrite it). Unknown direction: straight to his own AI. Already getting away when
  fire arrives: cover from it, then his AI. A pick-up transaction under way is never cut.
- No recapture loop: an ex-prisoner can't surrender (natural / demand / recapture) and an ex-captor
  can't take custody until "No new surrender while under fire for" (8 s) after the last round near him
  (BotHistory.FireBreakUntil / NoCustodyUntil, extended by every near miss or hit). Natural checks
  gate it as UnderBreakingFire. An ex-captor may still give up himself (a new encounter).
- Hand-back (Phase.Released): Regroup decides at once (kept gun / own gun / another gun near, at most
  2 tries / else hand back); Hiding is a move to cover with an end (arrived, stuck, no route, no spot,
  "Move to cover at most" 8 s) -> hand back; flight legs end once hidden from the captor and 12 m clear
  (max 3 legs, was 5). SurrenderRegistry.Tick hands him back when arms are down, the kneel pose is off
  and no hands request of ours is open (an unanswered one is abandoned after its limit), StopMove
  first (no stale destination for a non-SAIN brain), cooldown as before. ReleaseWatch logs where he is
  at +15/+30/+60 s (layer, moving, posture, target, armed; STILL NEAR THE CAPTURE SPOT flag).
- Walls/doors: SurrenderLogic.MoveTo and CaptorLogic call the game's door opener every moving frame and
  yield to it (no re-path, sprint or stall count) while it works a door; Perception.PathTo refuses a
  route whose legs (1 m up) cross anything the player capsule collides with
  (PlayerStaticCollisionsMask) except an openable door (a locked one refuses); Arrived needs the same
  floor (<= 1.6 m); both logics' Stop() clear our path.
- Trigger guards: Perception.InLineOfFire = the man's body on the line (0.55 m + 1 cm/m either side,
  feet to head), and not if the shooter's own GoalEnemy stands on the line in front of him. Used by
  restraint and the scenario observer override. tools/firesim: 12/12 cases as intended; the old rule
  got 4 of the 12 wrong, all "blocked a shot that should fire" (you 6 m behind the target, a prisoner
  1.2 m off the line at 20 m, a man on a balcony over the prisoner).
- Kneel: SteerTo clamps every look to MaxPivot from the knees while any part of the kneel is on the
  bones (incl. the 0.14 s hand-back blend); HandsUp facing waits for Kneel.Posing to end; the rise
  pivot unchanged. Turn requests beyond that range already stand him up (TurnReKneel); a captor walking
  behind a kneeling man is out of view (head only, within its limits), never a body turn.
- Removed: captor Cover mode (TryCover/UpdateCover/ArriveCover/MoveToCover, FollowToCover) and its 4
  keys (Captors take cover, Captor cover search, Prisoner follows into cover, Follows into cover
  within); Regroup min/max and Look again every (s). Their lines stay orphaned in Joel's cfg.
- Config 24. Incoming fire and release (8 keys). Scenarios: events "Near miss on him" / "Hit on him"
  (SIMULATED, through the same queue); preset "Incoming fire breaks the hold-up" (fire at prisoner or
  captor; near miss / hit / real hostile bot / you; standing, kneeling, captor collecting the gear,
  prisoner dropping his gear, being let go, prisoner going for his gun; squad 1-2); "Retreat from
  captivity" gains "His dropped gun: removed" and a kept-weapon kit; "Captor comes under fire" text.

Tests done (offline): Debug and Release builds 0 warnings; Settings.Bind on copies of Joel's cfg
through the built and the installed DLL (395 entries, section 24 at defaults, twice); tools/firesim
12/12; tools/releasesim (every post-captivity path ends with his own AI, worst case 223 s = two
30 s gun walks; the 0.20.1 graph is unbounded as the log showed). NOT done: anything in a raid.

### In-game test plan (0.21.0)
Log decisions, Log incoming fire on. Scenario "Incoming fire breaks the hold-up" unless noted.
1. Fire at the prisoner, Near miss, Standing: `INCOMING FIRE: ... (the prisoner) a round 0.6 m past
   him ...: the hold-up breaks`, `<captor>: stops holding ... (incoming fire: ...)`, `<prisoner> under
   fire (...): cover ...; draw ...; his gun ...; -> ...`, `cover N m away, hidden from the fire`, `back
   to his own AI (in cover from the fire ...)`. LOOK: the captor turns to fight (not to the prisoner),
   the prisoner sprints to cover, no crouch-and-wait.
2. Same, Fire at the captor; and Hit. Then Kneeling: escape rise (no sliding on the knees, no crossed
   legs), then the sprint. Captor collecting the gear: the pick-up ends (`stops going for ...`), no
   stuck hands, no `will take ... later`. Prisoner dropping his gear: drops stop, no busy hands.
3. Prisoner going for his gun + Near miss: `fire while going for his gun: keeps going (out of the
   line of fire)` or `leaves it`; the pick-up completes or he ends in cover; never stands still.
4. Hostile bot (real): the third party actually fires (no `blocked at trigger` while it aims at the
   captor with you nearby); the INCOMING FIRE line comes from a real near miss/hit.
5. Squad 2: the guard `stops covering ...`; nobody re-takes the prisoner for 8 s after the last round
   (`no surrender to ...: ... still under the fire`).
6. "Retreat from captivity", Let go, gun removed, kit none: `retreat ends`, `back to his own AI
   (away, nothing to fight with; unarmed, N m from where he was held ...)`, then `after release +15/+30/
   +60 s ...` with growing distance or a target; never STILL NEAR THE CAPTURE SPOT. Indoors (crosshair
   into a building), at a door (he stops and opens it), on stairs, in a dead end (no wall-walking).
7. Earlier "friendly" scenarios (PMC captures Scav, multiple encounters): third parties and squads
   fight; Scenario shield blocks only when you are really in the line.
8. Natural raid: SurrenderStats `hold-ups broken by incoming fire N`; Profile: FireBreak is inside the
   Shot / Registry sections.

**0.20.1 (2026-11-20): performance pass (from Joel's pasted 9-part performance spec). No gameplay,
balance or config change. Release packed (dist/Mercy-0.20.1.zip, DLL only) and installed in H:\SPT4.1.X
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
6-part spec). Release packed (dist/Mercy-0.20.0.zip) and installed in H:\SPT4.1.X (0.19.0 DLL
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

