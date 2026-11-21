# 0.25.0: how long a left prisoner stands before he acts, 0.24.0 rules vs 0.25.0 rules
# (SurrenderLogic.Abandonment), checked every 0.25 s like the layer. Captor scenarios:
#   back-turned   walks off at 1.2 m/s with his back to him, from 3 m (in view throughout)
#   backing-off   walks away facing him (gun off him) at 1.0 m/s
#   out-of-sight  walks off 4 m with his back turned, then is out of sight
#   behind        the prisoner kneels facing away; he hears him walk off at 1.2 m/s
#   loot-step     steps 2 m aside (back turned) to an item by the prisoner, 4 s bent over it, back
#   reposition    1 m step away (back turned), then stands covering him again
# Reaction = first cue to the decision. Nerve 0.2/0.56/0.9 x personality (Coward, Normal, Chad).
# Expect: new rules act within ~1-3 s on the real departures, never on loot-step or reposition.
import random
CHECK = 0.25
PERS = {"Coward": 1.4, "Normal": 1.0, "Chad": 0.8}

def assess_factor(nerve, pers, kneeling):
    k = (1.3 - 0.6 * nerve) * PERS[pers] * (1.15 if kneeling else 1.0)
    return min(2.2, max(0.4, k))

def scenario(name):
    # yields per check: (seen, inView, goingAway, lookAway(bool back turned), gained, gunOff, hiddenFor, gathering)
    t = 0.0; d0 = 3.0
    while t < 30:
        if name == "back-turned": yield (True, True, True, True, 1.2 * t, True, 0, False)
        elif name == "backing-off": yield (True, True, True, False, 1.0 * t, True, 0, False)
        elif name == "out-of-sight":
            if t < 4 / 1.2: yield (True, True, True, True, 1.2 * t, True, 0, False)
            else: yield (False, False, False, False, 4 + 1.2 * (t - 4 / 1.2), True, t - 4 / 1.2, False)
        elif name == "behind": yield (True, False, True, False, 1.2 * t, True, 0, False)
        elif name == "loot-step":
            if t < 2 / 1.2: yield (True, True, True, True, 1.2 * t, True, 0, True)
            elif t < 2 / 1.2 + 4: yield (True, True, False, True, 2.0, True, 0, True)
            elif t < 2 / 1.2 + 4 + 2 / 1.2: yield (True, True, False, False, 2.0 - 1.2 * (t - 2 / 1.2 - 4), True, 0, True)
            else: yield (True, True, False, False, 0, False, 0, False)
        elif name == "reposition":
            if t < 1 / 1.2: yield (True, True, True, True, 1.2 * t, True, 0, False)
            else: yield (True, True, False, False, 1.0, False, 0, False)
        t += CHECK

def run(rules, name, nerve, pers, kneeling, rng):
    ev = 0.0; decide_at = None; t = 0.0; first = None
    for seen, inview, away, back, gained, gunoff, hidden, gathering in scenario(name):
        if rules == "0.24":
            if seen and inview:
                if away and back: w = 1.6
                elif away: w = 1.0
                elif gained > 3 and gunoff: w = 0.6
                elif gunoff and back: w = 0.35
                else: w = -3
            elif seen: w = 0.9 if away else (0.5 if gained > 4 else 0)
            else: w = 1.0 if hidden > 2 else 0.3
            thr = 1.2; a = rng.uniform(2.5, 9.0) * assess_factor(nerve, pers, kneeling)
        else:
            sure = min(1, max(0, (gained - 0.5) / 2.5))
            if seen and gathering: w = -1.5
            elif seen and inview:
                if away and back: w = 2.4 * (0.35 + 0.65 * sure)
                elif away: w = 1.5 * (0.35 + 0.65 * sure)
                elif gained > 3 and gunoff: w = 0.8
                elif gunoff and back: w = 0.4
                else: w = -3
            elif seen: w = 1.4 * (0.35 + 0.65 * sure) if away else (0.9 if gained > 4 else 0)
            else: w = 1.8 if hidden > 1 else 0.4
            thr = 1.0; a = min(2.5, rng.uniform(0.3, 1.2) * assess_factor(nerve, pers, kneeling))
        if w > 0 and first is None: first = t
        ev = min(10, max(0, ev + w * CHECK))
        if decide_at is None and ev >= thr: decide_at = t + a
        elif decide_at is not None and ev <= 0: decide_at = None
        if decide_at is not None and t >= decide_at: return t - (first or 0)
        t += CHECK
    return None

rng = random.Random(7)
ok = True
for name in ("back-turned", "backing-off", "out-of-sight", "behind", "loot-step", "reposition"):
    for rules in ("0.24", "0.25"):
        res = []
        for nerve in (0.2, 0.56, 0.9):
            for pers in PERS:
                for kneel in (False, True):
                    for _ in range(20):
                        res.append(run(rules, name, nerve, pers, kneel, rng))
        acted = [r for r in res if r is not None]
        line = "%-13s %s: acted %3d/%d" % (name, rules, len(acted), len(res))
        if acted:
            acted.sort()
            line += ", reaction median %.1f s, 90%% %.1f s, max %.1f s" % (acted[len(acted) // 2], acted[int(len(acted) * 0.9)], acted[-1])
        print(line)
        if rules == "0.25":
            if name in ("loot-step", "reposition") and acted: ok = False; print("   FAIL: acted on a non-departure")
            if name not in ("loot-step", "reposition") and (len(acted) < len(res) or acted[int(len(acted) * 0.9)] > 3.5): ok = False; print("   FAIL: slow")
print("PASS" if ok else "FAIL")
