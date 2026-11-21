# Release 0.26.0 (notes, root causes, test plan)

## 0.26.0 (2026-11-21): hands behind the head without sleeve clipping, released men no longer walked back by ORBIT, the gun at his feet first

From Joel's pasted 3-part spec, his screenshot (rear view, hands behind the head, the top glove in
the other sleeve) and the 0.25.0 log (LogOutput 2026-11-21 03:31, 3 player-vs-Scav scenarios).

### 1. Hands behind the head

Root cause: 0.25.0 laid the top hand along the bottom hand "towards its wrist" (palm 5 cm along the
bottom fingers, its fingers pointing back over the bottom hand). Its fingertips ended 5.2 cm from the
bottom wrist bone, i.e. inside the cuff. tools/fingersim/overlap.py (new; same hand model as
overhand.py plus a sleeve capsule, radius 4.5 cm, from the wrist towards the IK elbow) reproduces it:
0.25.0 = top hand 4.6 cm inside the other sleeve, either hand on top, helmet on/off.

Fix (Kneel.ApplyHands, Fingers.OverTop): each palm on its own side of the back of the skull
(bottom +-9.5 cm, top +-10.5 cm from the middle; wrists behind the ears), fingers towards the
middle; the top hand 3 cm higher and 2 cm (one gloved finger) further out, so only the fingers
overlap, the top ones lying on the bottom ones; top fingers lightly bent (10/8/5 deg ... 16/14/5).
Elbow bend hint up 0.25 -> 0.10 (out and a little forward, not lifted above the hands). Offline
(overlap.py CHOSEN, both hands on top, helmet on/off): hands +0.48 cm clear, other sleeve +8.1 cm,
top fingers rest <= 0.56 cm off the bottom ones, top tips >= 17 cm from the bottom wrist, every capsule
further from the skull than the 0.24 hands (+0.03 vs -1.56 cm baseline). Path, timing (bottom hand
leads, top follows; reversed on the way down), kneel and standing windows unchanged.

### 2. Lingering after release; what the "legs" are

Three different "legs":
- Flight legs (SurrenderLogic.Retreating / PickRetreat / LegDone): our own route out of the 40 m
  captivity area, up to 4 legs, then Regroup (a stop) and Decide. Since 0.25.0 a calm way out (let
  go, left behind) skips them: QuickClear hands back mid-stride once 8 m away / out of sight 4 m /
  5 s. Still used for a bolt or a flight under fire (the captor still there): kept.
- Keep-away legs (Escape.KeepAwayLayer): after the hand-back, if his own AI walks an unarmed man back
  within 25 m of the capture, our layer takes him one leg away and hands back; at most 3.
- The 0.25.0 log shows what actually made released men linger: #1 handed back at 3.0 s, 9 m away and
  walking off; 3 s later ORBIT ("drifting back ... layer OrbitBrainLayer, target none") had turned
  him round to his pre-capture objective. Keep-away legs 1-3 each took him 60 m away and each time
  ORBIT walked him back; at +46 s CIRCLED BACK, +61 s 14 m from the capture spot. The keep-away legs
  were fighting a stale objective: repeated destination changes, no progress.

Fixes:
- OrbitObjectives.RetireIfBack (Compat.cs), called at the hand-back (Escape.Begin) and every 2 s
  while remembered (<= 4 drops per release): an ORBIT objective within the keep-away radius of the
  capture spot or the ex-captor, or whose straight line passes within 10 m of him, is dropped the
  way ORBIT drops a stalled one (CompletedPoiIds, Location/SplinterParent null, Status None); ORBIT
  picks a new one near him. Leaders / solo bots only. Members verified by reflection on the installed
  ORBIT.dll; fails soft (one log line) on another ORBIT version. Logged: "ORBIT objective ... leads
  back to the capture, dropped" or "... leads away, kept".
- Left with "hide" chosen (captor gone, DecideOnLeave): QuickClear applies on the way to cover too
  (was: our layer until he arrived or 8 s; the 0.25.0 log had an 8.2 s hide).
- Kept as necessary: the first route (FleePoint: not past the ex-captor, not his heading), door
  handling, the kneel rise before walking, KeepAway as a safety net for SAIN/vanilla drift (with the
  ORBIT objective dropped at the source it should rarely fire), emergency flight legs.

### 3. The gun at his feet

Root cause: left by a bot captor, "get his gun" was one weighted option against "walk off" and only
offered with the captor > 20 m away or out of sight; once he walked off, Decide refused his own gun
for 150 s ("it lies where he was held (off limits ...)", seen in the 0.25.0 log for a let-go). The
captor-gone path (DecideOnLeave) rolled it against run/hide/draw.

Fix: SurrenderLogic.TryGunAtFeet runs first in LeftDecide and DecideOnLeave (captor has left; not
for a "Leave" order, where the captor is still standing there). It takes the real item where it lies
now (Arsenal.Locate), within 4 m and reachable (NavMesh path), not under fire, no hands request of
ours open, and nobody covering him (his ex-captor with a gun on him within 40 m or within 5 m of the
gun, one of his squad, a player). Then Recovering -> PickingUp -> Arsenal.PickUp (the normal
inventory move) -> Draw. Otherwise logged once ("not going for his X at his feet: why") and he goes on
as before. CalmRearm: no battle cry, no turn to his ex-captor while the draw plays; once handed back,
his own AI (SAIN personality, threats) decides cover, leaving or a fight. The captor side is
unchanged: going for a gun still shows as resistance to anyone who sees it (Restraint.OnArmed).

### Checks

- Debug build 0 warnings; Release pack (scripts/pack.ps1).
- tools/fingersim/overlap.py: CHOSEN PASS for all 4 cases; 0.25.0 printed as the sleeve failure.
- tools/releasesim: PASS (no new phase edges: HandsUp -> Recovering and Hiding -> Released existed).
- tools/abandonsim: PASS (decision timing unchanged).
- ORBIT members: reflection on H:\SPT4.1.X\BepInEx\plugins\ORBIT\ORBIT.dll, 16/16 found.
- No config keys or defaults changed (one description text): no cfg migration.

### Needs a raid

- Hands behind the head from behind, the side and the front: no glove in a sleeve, the top fingers
  on the bottom fingers, elbows out and relaxed; standing and kneeling; up and down transitions.
- A bot captor walks off without the gun: `picks up his X at his feet first`, then `back to normal
  AI` with no turn or shout at the captor; a captor still covering him: `not going for ... covers him`.
- After any release: `ORBIT objective ... dropped` lines, and no `drifting back ... OrbitBrainLayer`
  / CIRCLED BACK in the ReleaseWatch lines.
