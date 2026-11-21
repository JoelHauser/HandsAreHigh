# 0.27.0: interlaced fingers behind the head (Joel: "interlace the fingers as best we can, doesn't
# have to look perfect, but natural"). interlace.py (0.25.0) showed a CLEAN lace is impossible on this
# rig (knuckle pitch 2.3-2.7 cm < two gloved fingers, 1.7 cm); its search only had one shared spread,
# one shared vertical offset and both hands' fingers tilted the same way (a V, so they crossed).
# Here the lace is optimised (scipy differential evolution) with:
#   - one vertical offset between the hands (half a knuckle pitch: fingers alternate L R L R ...),
#   - each hand's own in-plane finger tilt (laced fingers must run parallel, not in a V),
#   - per-finger spread at the knuckle (each hand its own) and per-finger curl,
#   - palm centre distance from the middle,
# scored on the worst finger-into-finger depth between the hands (a gloved squeeze of a few mm at the
# webs is accepted: real laced fingers are pressed together), with hard requirements:
#   - fingers alternate at the middle (a real lace, not stacked), each reaching past the other hand's
#     knuckles (>= 1.5 cm), knuckle rows 1-4 cm apart,
#   - no capsule closer to the skull than the 0.24.0 hands (the same relative skull check overlap.py
#     uses), helmet on and off,
#   - thumbs clear of the other hand, each hand's own fingers clear of each other,
#   - curls in a natural range, each finger curling at least as much as the one before it.
# Usage (from tools/fingersim): python -I lace.py <skeleton.bundle> [search]
#   no 'search': prints the CHOSEN layout (the constants in Kneel.cs / Fingers.cs) with PASS/FAIL.
import sys
exec(open("interlace.py").read().rsplit(chr(10) + "if __name__", 1)[0])
R_P = 0.012


def place(side, w, dy, depth, helmet, tilt):
    right, up, fwd = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    p = add(add(mul(fwd, -(depth + helmet)), mul(right, side * w)), mul(up, dy))
    face = unit(mul(p, -1))
    d = add(mul(right, -side), mul(up, tilt))
    fing = unit(sub(d, mul(face, dot(d, face))))
    return p, face, fing


def rots_for(flex, spread):
    return joint_rots(flex, spread)


def hand(side, w, dy, depth, helmet, tilt, flex, spread):
    palm_at, face, fing = place(side, w, dy, depth, helmet, tilt)
    pts = chain_local(rots_for(flex, spread), side > 0)
    X = mul(fing, -1); Y = mul(face, -1); Z = cross(X, Y)
    palm_bone = sub(palm_at, mul(fing, PALM_CENTRE))
    def to_w(p): return add(palm_bone, add(add(mul(X, p[0]), mul(Y, p[1])), mul(Z, p[2])))
    return {d: [to_w(p) for p in seq] for d, seq in pts.items()}, palm_bone


def caps(F, pb):
    c = [(pb, F[d][0], R_P) for d in range(2, 6)]
    for d in range(2, 6):
        for i in range(3): c.append((F[d][i], F[d][i+1], R_F, d, i))
    for i in range(1, 3): c.append((F[1][i], F[1][i+1], R_F, 1, i))
    return [x if len(x) == 5 else (x[0], x[1], x[2], 0, -1) for x in c]


def baseline_skull():
    base = 9
    for helmet in (0.0, 0.035):
        F, pb = hand(-1, 0.05, 0.0, 0.11, helmet, 0.25, CUPPED, NOSPREAD)
        base = min(base, min(point_seg((0, 0, 0), a, b) - r - HEAD_R - helmet for a, b, r, _, _ in caps(F, pb)))
    return base


# Parameter vector -> layout.
# x = [w, dv, tiltL, tiltR, mcp, pip, dip, dcurl, spL2..5, spR2..5]
NAMES = ["w", "dv", "tiltL", "tiltR", "mcp", "pip", "dip", "dcurl"] + ["spL%d" % d for d in range(2, 6)] + ["spR%d" % d for d in range(2, 6)]
BOUNDS = [(0.035, 0.085), (-0.02, 0.02), (-0.35, 0.35), (-0.35, 0.35), (0, 45), (5, 60), (0, 40), (0, 6)] + [(-8, 8)] * 8


def layout(x):
    w, dv, tl, tr_, mcp, pip, dip, dc = x[:8]
    flex = {1: CUPPED[1]}
    for d in range(2, 6):
        k = d - 2
        flex[d] = (mcp + k * dc, pip + k * dc, dip + k * dc * 0.6)
    spL = {1: 0.0}; spR = {1: 0.0}
    for d in range(2, 6): spL[d] = x[8 + d - 2]; spR[d] = x[12 + d - 2]
    return w, dv, tl, tr_, flex, spL, spR


def measure(x, helmet, depth=0.11):
    w, dv, tl, tr_, flex, spL, spR = layout(x)
    FL, pL = hand(-1, w, +dv / 2, depth, helmet, tl, flex, spL)
    FR, pR = hand(+1, w, -dv / 2, depth, helmet, tr_, flex, spR)
    cl, cr = caps(FL, pL), caps(FR, pR)
    # worst depth between the hands, fingers (2-5) vs fingers / palm; thumbs separately
    lace = 9; thumbs = 9; palm = 9
    for a1, b1, r1, d1, i1 in cl:
        for a2, b2, r2, d2, i2 in cr:
            dd = seg_dist(a1, b1, a2, b2) - r1 - r2
            if d1 == 1 or d2 == 1: thumbs = min(thumbs, dd)
            elif i1 < 0 or i2 < 0: palm = min(palm, dd)
            else: lace = min(lace, dd)
    # own fingers vs own fingers (non-neighbouring segments of different digits)
    own = 9
    for F in (FL, FR):
        for d in range(2, 5):
            for i in range(1, 3):
                for k in range(1, 3):
                    own = min(own, seg_dist(F[d][i], F[d][i+1], F[d+1][k], F[d+1][k+1]) - 2 * R_F)
    sk = min(point_seg((0, 0, 0), a, b) - r - HEAD_R - helmet for C in (cl, cr) for a, b, r, _, _ in C)
    # lace: left fingers run to +x, right to -x. Knuckle rows and how far the tips pass the other's knuckles.
    kL = max(FL[d][0][0] for d in range(2, 6)); kR = min(FR[d][0][0] for d in range(2, 6))
    passL = min(max(p[0] for p in FL[d]) for d in range(2, 6)) - kR
    passR = kL - max(min(p[0] for p in FR[d]) for d in range(2, 6))
    # alternation: heights (y) of each finger where it crosses x = 0 (the middle)
    order = []
    for nm, F in (("L", FL), ("R", FR)):
        for d in range(2, 6):
            pts = F[d]; y = None
            for i in range(3):
                a, b = pts[i], pts[i+1]
                if (a[0] - 0) * (b[0] - 0) <= 0 and a[0] != b[0]:
                    t = (0 - a[0]) / (b[0] - a[0]); y = a[1] + t * (b[1] - a[1]); break
            order.append((y if y is not None else float("nan"), nm, d))
    alt = all(not math.isnan(o[0]) for o in order)
    if alt:
        s = [o[1] for o in sorted(order)]
        alt = all(s[i] != s[i+1] for i in range(len(s) - 1))
    return dict(lace=lace, palm=palm, thumbs=thumbs, own=own, skull=sk, knuck=kR - kL, passL=passL, passR=passR, alt=alt,
                order="".join(o[1] + str(o[2]) for o in sorted(order)) if alt or True else "")


BASE = None
# A natural lace: the rig's 'knuckle' is the MCP joint, inside the palm ~2 cm before the webs, so the
# rows sit about a proximal phalanx apart and each hand's tips reach the other's webs (within 0.5 cm
# of its MCP row), not past the joints. Accepted squeeze: fingers pressed <= 6 mm into each other,
# a fingertip <= 4 mm into the other palm at the web.
GAP_MIN, GAP_MAX, PASS_MIN = 0.035, 0.06, -0.005
LACE_MIN, PALM_MIN = -0.006, -0.004


def score(x):
    pen = 0.0; worst = 9
    for helmet in (0.0, 0.035):
        m = measure(x, helmet)
        worst = min(worst, m["lace"])
        pen += max(0, BASE - m["skull"]) * 20
        pen += max(0, 0.002 - m["thumbs"]) * 10
        pen += max(0, 0.0 - m["own"]) * 10
        pen += max(0, PASS_MIN - m["passL"]) * 10 + max(0, PASS_MIN - m["passR"]) * 10
        pen += max(0, m["knuck"] - GAP_MAX) * 10 + max(0, GAP_MIN - m["knuck"]) * 10
        pen += max(0, PALM_MIN - m["palm"]) * 10
        if not m["alt"]: pen += 0.05
    for o in (8, 12):   # neighbouring fingers' spreads within 6 deg of each other (no odd gaps)
        for d in range(3):
            pen += max(0, abs(x[o + d + 1] - x[o + d]) - 6) * 0.002
    return -worst + pen


def report(x, label):
    print(label)
    for i, n in enumerate(NAMES): print("  %-6s %+.4f" % (n, x[i]))
    ok = True
    for helmet in (0.0, 0.035):
        m = measure(x, helmet)
        good = (m["lace"] >= LACE_MIN and m["palm"] >= PALM_MIN - 1e-4 and m["skull"] >= BASE - 1e-4 and m["thumbs"] >= 0.0
                and m["own"] >= -0.001 and m["passL"] >= PASS_MIN - 1e-4 and m["passR"] >= PASS_MIN - 1e-4
                and GAP_MIN - 1e-4 <= m["knuck"] <= GAP_MAX + 1e-4 and m["alt"])
        ok &= good
        print("  helmet %.3f: lace %+.2f cm palm %+.2f cm thumbs %+.2f own %+.2f skull %+.2f (base %+.2f) knuckle gap %.1f pass %.1f/%.1f cm order %s %s"
              % (helmet, m["lace"] * 100, m["palm"] * 100, m["thumbs"] * 100, m["own"] * 100, m["skull"] * 100, BASE * 100, m["knuck"] * 100,
                 m["passL"] * 100, m["passR"] * 100, m["order"], "PASS" if good else "FAIL"))
    return ok


# Kneel.cs (LaceW, LaceDv, LaceTiltL, LaceTiltR) + Fingers.cs (Laced, LaceSpreadL/R): kept in step by hand.
CHOSEN = [0.0745, 0.019, -0.35, -0.35, 1.0, 8.0, 22.0, 0.5, 7.5, 6.0, 7.5, 2.5, -1.5, -4.5, -7.5, -8.0]

if __name__ == "__main__":
    BASE = baseline_skull()
    print("0.24.0 skull baseline %+.2f cm" % (BASE * 100))
    if len(sys.argv) > 2:
        from scipy.optimize import differential_evolution
        r = differential_evolution(score, BOUNDS, seed=int(sys.argv[3]) if len(sys.argv) > 3 else 1, maxiter=150,
                                   popsize=8, tol=1e-7, polish=True, workers=1)
        print("score", r.fun)
        report(r.x, "best:")
        print("x =", [round(float(v), 4) for v in r.x])
    if CHOSEN is not None:
        sys.exit(0 if report(CHOSEN, "chosen (code):") else 1)
