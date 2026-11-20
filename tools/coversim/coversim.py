"""Offline check of Overwatch's guard-spot geometry (0.20.0).

Replicates Overwatch.Near / Problem (line-of-fire parts only: no walls, no NavMesh) and the
PickSpot candidate ring, for the scenario layouts and a sweep of captor distances. Checks:
  1. the 'Squad cover' preset starts: beside -> valid as is (no move); behind -> flagged
  2. every candidate PickSpot can accept keeps the guard's line (8 m past the prisoner) >= 0.9 m
     from the captor's chest and the guard >= 1 m from the captor's line (10 m past)
  3. at least one candidate is accepted for captor distances 2.5-12 m
Usage: python coversim.py
"""
import math

def v(x, z, y=0.0): return (x, y, z)
def add(a, b): return tuple(p + q for p, q in zip(a, b))
def sub(a, b): return tuple(p - q for p, q in zip(a, b))
def mul(a, k): return tuple(p * k for p in a)
def dot(a, b): return sum(p * q for p, q in zip(a, b))
def length(a): return math.sqrt(dot(a, a))
def flat(a, b): return math.hypot(a[0] - b[0], a[2] - b[2])
def up(a, h): return (a[0], a[1] + h, a[2])
def rot_y(d, deg):  # Unity AngleAxis(deg, up) on (x, z): clockwise seen from above
    r = math.radians(deg); c, s = math.cos(r), math.sin(r)
    return (d[0] * c + d[2] * s, d[1], -d[0] * s + d[2] * c)

def near(a, b, extend, p, r):
    ab = sub(b, a); L = length(ab)
    if L < 0.01: return False
    d = mul(ab, 1 / L); t = dot(sub(p, a), d)
    if t < 0.3 or t > L + extend: return False
    q = add(a, mul(d, t)); return length(sub(q, p)) < r

def problem(feet, prisoner, captor, want=6.0):
    d = flat(feet, prisoner)
    if d < 2.2: return "too close"
    if d > max(15, want * 2.5): return "too far"
    eye, chest = up(feet, 1.55), up(prisoner, 1.2)
    if flat(feet, captor) < 1.8: return "on top of captor"
    if near(eye, chest, 8, up(captor, 1.2), 0.9): return "captor in his line"
    if near(up(captor, 1.55), chest, 10, up(feet, 1.2), 1.0): return "in captor's line"
    gv, cv = sub(feet, prisoner), sub(captor, prisoner)
    ang = math.degrees(math.atan2(abs(gv[0]*cv[2]-gv[2]*cv[0]), gv[0]*cv[0]+gv[2]*cv[2]))
    if flat(feet, captor) < 4 and ang < 20: return "bunched up"
    return None

ANGLES, RADII = (70, 50, 100, 125), (1.0, 0.7, 1.35)

def candidates(prisoner, captor, guard, want=6.0):
    to_cap = sub(captor, prisoner); to_cap = mul(to_cap, 1 / length(to_cap))
    to_me = sub(guard, prisoner)
    side = 1 if (to_cap[2] * to_me[0] - to_cap[0] * to_me[2]) >= 0 else -1  # Cross(a,b).y
    out = []
    for a in ANGLES:
        for sg in (side, -side):
            for r in RADII:
                c = add(prisoner, mul(rot_y(to_cap, a * sg), want * r))
                out.append((a * sg, want * r, c, problem(c, prisoner, captor, want)))
    return out

def preset(sep, behind):
    h = sep / 2; cap = v(-h, 0); pr = v(h, 0)
    g = v(-h - 2.5, 0) if behind else v(-h - 1, 3)
    return pr, cap, g

fails = 0
for sep in (4, 6, 8, 10, 12):
    for behind in (False, True):
        pr, cap, g = preset(sep, behind)
        p = problem(g, pr, cap)
        tag = "behind" if behind else "beside"
        print(f"preset sep {sep:>2} m, squadmate {tag}: start spot -> {p or 'OK, covers from where he stands'}")
        if behind and p != "captor in his line": fails += 1; print("   FAIL: the blocked start must read as the captor in his line")
        if not behind and p not in (None, "bunched up"): fails += 1; print("   FAIL: unexpected reason for the beside start")
for dist in (2.5, 3, 4, 6, 8, 10, 12):
    pr, cap, g = v(0, 0), v(-dist, 0), v(-dist - 1, 3)
    cs = candidates(pr, cap, g)
    ok = [c for c in cs if c[3] is None]
    for a, r, c, why in ok:
        eye, chest = up(c, 1.55), up(pr, 1.2)
        line_gap = math.inf
        # min distance of captor chest to guard's line (to 8 m past the prisoner)
        ab = sub(chest, eye); L = length(ab); d = mul(ab, 1 / L)
        t = max(0.3, min(L + 8, dot(sub(up(cap, 1.2), eye), d)))
        line_gap = length(sub(add(eye, mul(d, t)), up(cap, 1.2)))
        if line_gap < 0.9: fails += 1; print(f"   FAIL: accepted spot {a} deg {r:.1f} m has the captor {line_gap:.2f} m from its line")
    best_sep = min(flat(c[2], cap) for c in ok) if ok else 0
    print(f"captor {dist:>4} m from him: {len(ok)}/{len(cs)} candidates pass the line rules (closest to the captor {best_sep:.1f} m)"
          + ("" if ok else "  <- FAIL: none"))
    if not ok: fails += 1
print("ALL CHECKS PASS" if fails == 0 else f"{fails} FAILURE(S)")
