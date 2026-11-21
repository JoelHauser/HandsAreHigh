"""0.23.0 stand-point check (CaptorLoot.StandPoint), flat-ground model.

Ports the candidate order and the cheap filters of CaptorLoot.StandPoint (reach 0.5-0.85 m from
the item, >= 0.8 m from the prisoner's feet, the captor's own side first) and adds walls as
half-planes (a corridor of a given width) to stand in for Physics.CheckCapsule (radius 0.3 m).
NavMesh, lines of sight and paths are not modelled (open floor): this checks the geometry of the
choice, not the game. Reports, per case: whether a spot is found, its distance to the item and to
the prisoner, the look-down angle from the captor's eyes (1.55 m) to the item (the game's pick-up
lowers the body only past 60 deg), and how far the spot is from the line the 0.22 walk took (the
item itself).

Run: python -I tools/standsim/standsim.py
"""
import math

STAND_MIN, STAND_MAX, STAND_CLEAR = 0.5, 0.85, 0.8
ANGLES = [0, 35, -35, 70, -70, 105, -105, 140, -140, 180]
RADII = [0.65, 0.8]
CAPSULE = 0.3
EYE = 1.55


def rot(v, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return (v[0] * c - v[1] * s, v[0] * s + v[1] * c)


def dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def stand_point(me, item, man, half_width=None):
    to_me = (me[0] - item[0], me[1] - item[1])
    n = math.hypot(*to_me)
    to_me = (to_me[0] / n, to_me[1] / n)
    for a in ANGLES:
        for r in RADII:
            d = rot(to_me, a)
            p = (item[0] + d[0] * r, item[1] + d[1] * r)
            if not (STAND_MIN <= dist(p, item) <= STAND_MAX):
                continue
            if man is not None and dist(p, man) < STAND_CLEAR:
                continue
            # corridor along y: walls at x = +-half_width
            if half_width is not None and abs(p[0]) + CAPSULE > half_width:
                continue
            return p, a, r
    return None


def main():
    fails = 0
    cases = 0
    print("open ground: prisoner at origin facing +y, captor 3 m in front; gun dropped at his feet")
    for gx in (-0.3, 0.0, 0.3):
        for gy in (-0.2, 0.0, 0.2, 0.4, 0.6):
            cases += 1
            item, man, me = (gx, gy), (0.0, 0.0), (0.0, 3.0)
            res = stand_point(me, item, man)
            if res is None:
                fails += 1
                print(f"  item {item}: NO SPOT")
                continue
            p, a, r = res
            look = math.degrees(math.atan2(EYE, dist(p, item)))
            ok = dist(p, man) >= STAND_CLEAR and look > 60
            fails += 0 if ok else 1
            print(f"  item ({gx:+.1f},{gy:+.1f}): spot ({p[0]:+.2f},{p[1]:+.2f}) angle {a:+4d} r {r}: "
                  f"{dist(p, item):.2f} m to item, {dist(p, man):.2f} m to him, look down {look:.0f} deg"
                  f"{'' if ok else '  <-- FAIL'}")
    print("corridor (x walls), prisoner in the middle, captor 3 m along it")
    for w in (2.0, 1.6, 1.3, 1.1):
        cases += 1
        res = stand_point((0.0, 3.0), (0.0, 0.2), (0.0, 0.0), half_width=w / 2)
        if res is None:
            print(f"  width {w} m: no spot -> step-back order / after release (expected only when narrow)")
            if w >= 1.6:
                fails += 1
        else:
            p, a, r = res
            print(f"  width {w} m: spot ({p[0]:+.2f},{p[1]:+.2f}) {dist(p, (0, 0)):.2f} m from him, wall clearance {w / 2 - abs(p[0]):.2f} m")
    print("captor behind the prisoner (item between his feet, captor 3 m behind)")
    cases += 1
    res = stand_point((0.0, -3.0), (0.0, 0.1), (0.0, 0.0))
    print(f"  {res}")
    if res is None:
        fails += 1
    print(f"{cases - fails}/{cases} pass")


if __name__ == "__main__":
    main()
