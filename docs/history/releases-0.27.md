# Release 0.27.0 (notes, root causes, test plan)

## 0.27.0 (2026-11-21): fingers laced behind the head

From Joel's request and his 0.26.0 screenshot (rear view through a scope: the top glove's fingers splayed
straight across the other hand, "the hands still look very off ... interlace the fingers as best we can,
doesn't have to look perfect, but natural").

### Why 0.25/0.26 did not lace

interlace.py (0.25.0) showed a clean lace is impossible on this rig: knuckle pitch 2.3-2.7 cm is less than
two gloved fingers (1.7 cm each). Its search was also too narrow: one shared spread, one vertical
offset, both hands' fingers tilted UP (a V, so the two rows crossed), and it required the tips to pass
the other hand's MCP joints, which on this rig sit inside the palm ~2 cm before the webs (tips into the
other palm by ~1 cm, whatever the layout).

### The lace (tools/fingersim/lace.py)

Differential evolution (4 seeds; two converged to the same layout) over palm spacing, the vertical
offset between the hands, each hand's finger tilt in the palm plane, per-finger spread per hand and the
curl. Requirements: fingers alternate at the middle (L R L R ...), knuckle rows 3.5-6 cm apart (about a
proximal phalanx), each hand's tips reaching the other's webs, no capsule nearer the skull than the 0.24
hands, thumbs clear, a hand's own fingers clear, neighbouring spreads within 6 deg.

Chosen (Kneel.LaceW/LaceDv/LaceTilt*, Fingers.Laced/SpreadHigh/SpreadLow):
- palms 7.45 cm either side of the middle; one hand 1.9 cm higher (half a knuckle pitch);
- both hands' fingers tilted down 0.35 in the palm plane (a shallow inverted V; with the spreads the
  two rows of fingers run parallel);
- fingers nearly straight at the knuckle, 8-9.5 deg at the middle joint, ~22 deg at the tips (lying
  across the other hand, tips curling onto it);
- spread: higher hand 7.5/6/7.5/2.5 deg, lower hand -1.5/-4.5/-7.5/-8 (index..little).
Offline, helmet off/on: fingers pressed 5.6-5.9 mm into each other at the worst pair (about 3 mm per
glove, the accepted squeeze), a fingertip 1.9 mm into the other palm at the web with a helmet, thumbs
+2.1-2.5 mm, skull +0.32 / -0.76 cm (0.24 baseline -1.56), knuckle rows 6.0 / 5.3 cm apart, tips
0.6-1.9 cm past the other hand's MCP row, order R2L2R3L3R4L4R5L5. PASS.

Which hand's fingers sit higher is chosen once per man (HeadTopHand, 60% right); the other is the
mirror. Both hands travel together (no top/bottom stagger); fingers open while the hands travel and
settle (curl and spread) over the last 45% of the move, opening first on the way down.

Config: new key "Hands behind head: fingers laced" (section 27, default on) takes precedence over
"one over the other" (still there for off). New key only: no migration.

### Checks

- Debug + Release builds, 0 warnings; packed dist/HandsAreHigh-0.27.0.zip, installed in H:\SPT4.1.X.
- tools/fingersim/lace.py: CHOSEN PASS (helmet on and off).

### Needs a raid

- Hands behind the head from behind, the side and the front, standing and kneeling, both hands high:
  fingers alternating and lying across each other, tips on the other hand, no finger visibly through
  another, thumbs not through each other; up/down transitions without a snag.
