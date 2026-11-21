"""0.24.0: escape destination choice against a captor the man can SEE walking.

Re-implements Perception.FleePoint's candidate generation and scoring (flat ground, straight-line
routes standing in for NavMesh paths, a few box occluders for the "hidden" test) twice:
  old  0.23.0: away from the threat and from where he was held, routes kept 5 m off the threat's
       position, scored on the threat's position only (no idea where the captor is going);
  new  0.24.0: routes kept 8 m off the threat and 6 m off the captor's path over the look-ahead
       (6 s, at most 20 m), the spot scored against where he will be too, and a spot within 35 deg
       of the captor's heading (while within 25 m of him) penalised.
Then both men walk (prisoner 1.4 m/s walk-off, captor 1.4 m/s) for 20 s and we measure:
  alongside  share of runs where, after 5 s, the two are within 12 m of each other while moving
             within 35 deg of the same heading (walking off together);
  close      share of runs whose closest approach from 4 s on is under 6 m (the captor starts 3 m
             away and, in one case, walks through the spot he starts from);
  out        median separation from where he was held at 20 s.
Cases: captor standing; walking the prisoner's natural escape line (through and past him); walking
away from him; walking across. Pass: new is never worse than old on any case, and on the
"walks the escape line" case "alongside" and "close" drop to <= 10 %.
Run: python -I escsim.py   (exit 1 on a failure)
"""
import math, random, sys

RADIUS = 40.0          # Mathf.Max(CaptivityRadius, SafeDistance)
TRIES = 18             # PathCandidates (12) x 1.5
LOOK = 6.0             # Allow for where the captor is walking (s)
BOXES = [((15, -6), (19, 6)), ((-25, 10), (-15, 14)), ((5, 20), (12, 26)), ((-12, -24), (-4, -18))]


def v(x, y): return (x, y)
def add(a, b): return (a[0] + b[0], a[1] + b[1])
def sub(a, b): return (a[0] - b[0], a[1] - b[1])
def mul(a, k): return (a[0] * k, a[1] * k)
def length(a): return math.hypot(a[0], a[1])
def norm(a):
    l = length(a); return (a[0] / l, a[1] / l) if l > 1e-6 else (0.0, 0.0)
def rot(a, deg):
    r = math.radians(deg); c, s = math.cos(r), math.sin(r); return (a[0] * c - a[1] * s, a[0] * s + a[1] * c)
def angle(a, b):
    la, lb = length(a), length(b)
    if la < 1e-6 or lb < 1e-6: return 0.0
    return math.degrees(math.acos(max(-1, min(1, (a[0] * b[0] + a[1] * b[1]) / (la * lb)))))


def seg_hits_box(p, q, box):
    (x0, y0), (x1, y1) = box
    for i in range(21):
        t = i / 20; x = p[0] + (q[0] - p[0]) * t; y = p[1] + (q[1] - p[1]) * t
        if x0 <= x <= x1 and y0 <= y <= y1: return True
    return False


def line_clear(a, b): return not any(seg_hits_box(a, b, bx) for bx in BOXES)


def passes_near(route, p, r):
    limit = min(r, length(sub(route[0], p)) - 0.5)
    if limit <= 0: return False
    for i in range(1, len(route)):
        a, b = route[i - 1], route[i]; ab = sub(b, a); l2 = ab[0] ** 2 + ab[1] ** 2
        t = 0 if l2 < 1e-4 else max(0, min(1, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2))
        c = add(a, mul(ab, t))
        if length(sub(c, p)) < limit: return True
    return False


def passes_near_seg(route, p0, p1, r):
    n = max(1, min(20, math.ceil(length(sub(p1, p0)))))
    return any(passes_near(route, add(p0, mul(sub(p1, p0), i / n)), r) for i in range(1, n + 1))


def flee_point(me, threat, held, vel, new, rng):
    away = sub(me, threat)
    from_held = sub(me, held)
    d = add(norm(away), mul(norm(from_held), 0.5) if length(from_held) > 1 else (0, 0))
    walking = new and length(vel) > 0.5
    ahead = threat
    if walking:
        step = mul(vel, LOOK)
        if length(step) > 20: step = mul(norm(step), 20)
        ahead = add(threat, step)
        fa = sub(me, ahead)
        # Walking: away from where he WILL be counts double what "away from where he is" does.
        d = add(add(mul(norm(away), 0.5), mul(norm(from_held), 0.5) if length(from_held) > 1 else (0, 0)), mul(norm(fa), 1.0) if length(fa) > 1 else (0, 0))
    d = norm(d) if length(d) > 0.01 else norm(away)
    here = length(sub(me, held))
    near_him = length(sub(me, threat)) < 25
    best = -1e9; point = None; best_leg = -1e9; leg_pt = None
    for i in range(TRIES):
        ang = rng.uniform(-80, 80)
        far = i % 3 != 2
        dist = max(RADIUS - here, 0) + rng.uniform(6, 25) if far else rng.uniform(8, max(10, RADIUS * 0.6))
        c = add(me, mul(rot(d, ang), dist))
        route = [me, c]
        if passes_near(route, threat, 8.0 if new else 5.0): continue
        if walking and passes_near_seg(route, threat, ahead, 6.0): continue
        hidden = not line_clear(threat, c)
        sep = length(sub(c, held))
        ft = min(length(sub(c, threat)), length(sub(c, ahead))) if walking else length(sub(c, threat))
        score = (30 if hidden else 0) + sep + 0.5 * ft - 0.35 * length(sub(c, me))
        if walking and near_him and angle(sub(c, me), vel) < 35: score -= 25
        if sep >= RADIUS:
            if score > best: best, point = score, c
        elif sep >= here + 8:
            ls = score + (40 if hidden else 0)
            if ls > best_leg: best_leg, leg_pt = ls, c
    return point or leg_pt


def run(case, new, seed):
    rng = random.Random(seed)
    me = v(0, 0); held = v(0, 0)
    cap, cvel = {
        "standing":            (v(-3, 0), v(0, 0)),
        "walks the escape line": (v(-3, 0), v(1.4, 0)),   # from behind him, through and past, the way he'd flee
        "walks away":          (v(-3, 0), v(-1.4, 0)),
        "walks across":        (v(-3, 0), v(0, 1.4)),
    }[case]
    dest = flee_point(me, cap, held, cvel, new, rng)
    if dest is None: return None
    go = norm(sub(dest, me))
    closest, along = 1e9, False
    p, c = me, cap
    for k in range(1, 201):
        t = k * 0.1
        if length(sub(dest, p)) > 0.2: p = add(p, mul(go, 0.14))
        c = add(c, mul(cvel, 0.1))
        dd = length(sub(p, c))
        if t > 4: closest = min(closest, dd)   # the captor starts 3 m off and may walk through his spot: from 4 s on
        if t > 5 and dd < 12 and length(cvel) > 0 and length(sub(dest, p)) > 0.2 and angle(go, cvel) < 35: along = True
    return along, closest < 6, length(sub(p, held))


ok = True
for case in ["standing", "walks the escape line", "walks away", "walks across"]:
    res = {}
    for new in (False, True):
        rows = [r for r in (run(case, new, s) for s in range(300)) if r is not None]
        al = sum(r[0] for r in rows) / len(rows); cl = sum(r[1] for r in rows) / len(rows)
        out = sorted(r[2] for r in rows)[len(rows) // 2]
        res[new] = (al, cl, out, len(rows))
    (a0, c0, o0, n0), (a1, c1, o1, n1) = res[False], res[True]
    bad = a1 > a0 + 0.02 or c1 > c0 + 0.02
    if case == "walks the escape line" and (a1 > 0.10 or c1 > 0.10): bad = True
    ok &= not bad
    print(f"{'FAIL' if bad else 'ok  '} {case:22s} alongside {a0:4.0%} -> {a1:4.0%}   within 6 m {c0:4.0%} -> {c1:4.0%}   median out at 20 s {o0:4.0f} -> {o1:4.0f} m   (runs {n0}/{n1})")
print("RESULT", "PASS" if ok else "FAIL")
sys.exit(0 if ok else 1)
