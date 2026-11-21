# 0.25.0: can the two hands interlace behind the head on this rig? Rebuilds both hands (left from
# skeleton.bundle, right = its mirror in local z, as the bundle has it) in the pose Kneel.ApplyHands
# puts them in (palm centre on the back of the skull, palm to the skull, fingers towards the middle,
# thumb down), with a shape per finger (flexion about local +Z per joint, abduction about local +Y
# at the knuckle) and a lateral / vertical offset per hand, and measures:
#   - finger-to-finger clearance between the hands (capsules, gloved finger radius R_F)
#   - finger-to-skull clearance (skull sphere + helmet)
#   - where the crossing happens (proximal or middle phalanges: an interlace; tips only: stacked)
# Usage: python -I interlace.py <skeleton.bundle> [search]
import UnityPy, sys, math, itertools
env = UnityPy.load(sys.argv[1])
tr = {}; go = {}
for o in env.objects:
    if o.type.name == "GameObject": go[o.path_id] = o.read().m_Name
for o in env.objects:
    if o.type.name == "Transform": tr[o.path_id] = o.read()
byname = {}
for pid, t in tr.items(): byname.setdefault(go.get(t.m_GameObject.path_id), pid)

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx, aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz)
def rot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    ix = w*vx+y*vz-z*vy; iy = w*vy+z*vx-x*vz; iz = w*vz+x*vy-y*vx; iw = -x*vx-y*vy-z*vz
    return (ix*w+iw*-x+iy*-z-iz*-y, iy*w+iw*-y+iz*-x-ix*-z, iz*w+iw*-z+ix*-y-iy*-x)
def inv(q): return (-q[0], -q[1], -q[2], q[3])
def norm(q):
    m = math.sqrt(sum(c*c for c in q)); return tuple(c/m for c in q)
def aa(axis, deg):
    h = math.radians(deg)/2; s = math.sin(h); return (axis[0]*s, axis[1]*s, axis[2]*s, math.cos(h))
def add(a, b): return tuple(x+y for x, y in zip(a, b))
def sub(a, b): return tuple(x-y for x, y in zip(a, b))
def mul(a, k): return tuple(x*k for x in a)
def dot(a, b): return sum(x*y for x, y in zip(a, b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def length(a): return math.sqrt(dot(a, a))
def unit(a): l = length(a); return mul(a, 1/l)
def twist_z(q): return norm((0.0, 0.0, q[2], q[3]))

bind = {}; lpos = {}
for d in range(1, 6):
    for j in (1, 2, 3):
        t = tr[byname["Base HumanLDigit%d%d" % (d, j)]]
        r = t.m_LocalRotation; p = t.m_LocalPosition
        bind[(d, j)] = (r.x, r.y, r.z, r.w); lpos[(d, j)] = (p.x, p.y, p.z)
TIP = {1: 0.030, 2: 0.022, 3: 0.024, 4: 0.023, 5: 0.018}
R_F = 0.0085          # gloved finger radius
HEAD_R = 0.10         # Kneel.HeadRadius
PALM_CENTRE = 0.055   # Kneel.PalmCentre (palm bone -> palm centre, along the fingers)

def joint_rots(flex, spread):
    out = {}
    for d in range(1, 6):
        for j in (1, 2, 3):
            q = bind[(d, j)]
            f = flex[d][j-1]
            if f is None: out[(d, j)] = q; continue
            swing = qmul(q, inv(twist_z(q)))
            a = spread[d] if j == 1 else 0.0
            out[(d, j)] = norm(qmul(qmul(swing, aa((0, 1, 0), a)), aa((0, 0, 1), f)))
    return out

def chain_local(rots, mirror):
    # palm-local points per digit: knuckle, j2, j3, tip. mirror: right hand (z -> -z).
    pts = {}
    for d in range(1, 6):
        q = (0, 0, 0, 1); p = (0, 0, 0); seq = []
        for j in (1, 2, 3):
            p = add(p, rot(q, lpos[(d, j)])); seq.append(p)
            q = qmul(q, rots[(d, j)])
        seq.append(add(p, rot(q, (-TIP[d], 0, 0))))
        if mirror: seq = [(x, y, -z) for x, y, z in seq]
        pts[d] = seq
    return pts

def place(side, w, dv, depth, helmet):
    # World: right +x, up +y, forward +z; skull centre at origin.
    right, up, fwd = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    palm_at = add(add(mul(fwd, -(depth + helmet)), mul(right, side * w)), mul(up, dv))
    face = unit(mul(palm_at, -1))
    fing = unit(sub(add(mul(right, -side), mul(up, 0.0)), mul(face, dot(add(mul(right, -side), mul(up, 0.0)), face))))
    return palm_at, face, fing

def world_hand(side, w, dv, depth, helmet, rots):
    palm_at, face, fing = place(side, w, dv, depth, helmet)
    mirror = side > 0
    pts = chain_local(rots, mirror)
    # local axes -> world: fingers = -X, palm side = -Y (faces the skull), z from the proper rotation.
    X = mul(fing, -1); Y = mul(face, -1); Z = cross(X, Y)
    if mirror:  # right hand: local frame mirrored in z (positions above are already mirrored), X,Y same rule
        Z = cross(X, Y)
    palm_bone = sub(palm_at, mul(fing, PALM_CENTRE))
    def to_w(p): return add(palm_bone, add(add(mul(X, p[0]), mul(Y, p[1])), mul(Z, p[2])))
    return {d: [palm_bone if False else to_w(p) for p in seq] for d, seq in pts.items()}, palm_bone, Z

def seg_dist(p1, q1, p2, q2):
    d1 = sub(q1, p1); d2 = sub(q2, p2); r = sub(p1, p2)
    a = dot(d1, d1); e = dot(d2, d2); f = dot(d2, r)
    c = dot(d1, r); b = dot(d1, d2); den = a*e - b*b
    s = min(max((b*f - c*e)/den, 0), 1) if den > 1e-12 else 0
    t = (b*s + f)/e if e > 1e-12 else 0
    if t < 0: t = 0; s = min(max(-c/a, 0), 1)
    elif t > 1: t = 1; s = min(max((b - c)/a, 0), 1)
    return length(sub(add(p1, mul(d1, s)), add(p2, mul(d2, t))))

def point_seg(p, a, b):
    ab = sub(b, a); t = max(0, min(1, dot(sub(p, a), ab)/dot(ab, ab)))
    return length(sub(p, add(a, mul(ab, t))))

def evaluate(shape, spread, w, dv, helmet, depth=0.11, verbose=False):
    rots = joint_rots(shape, spread)
    L, lp, LZ = world_hand(-1, w, +dv, depth, helmet, rots)
    Rr, rp, RZ = world_hand(+1, w, -dv, depth, helmet, rots)
    worst = 9; where = None; cross_pairs = 0
    for dl in range(2, 6):
        for dr in range(2, 6):
            for i in range(3):
                for k in range(3):
                    dd = seg_dist(L[dl][i], L[dl][i+1], Rr[dr][k], Rr[dr][k+1])
                    if dd < worst: worst = dd; where = (dl, i, dr, k)
    skull = HEAD_R + helmet
    skull_min = min(length(p) for hand in (L, Rr) for d in range(2, 6) for p in hand[d]) - skull
    # do the fingers reach past the midline (interlace) and how deep?
    # left fingers run to +x, right to -x: how far past each other the fingertips go, and the gap
    # between the two rows of knuckles (an interlace: knuckles 2-6 cm apart, tips past the other's knuckles)
    reach = (max(max(p[0] for p in L[d]) for d in range(2, 6)), min(min(p[0] for p in Rr[d]) for d in range(2, 6)))
    knuck = (max(L[d][0][0] for d in range(2, 6)), min(Rr[d][0][0] for d in range(2, 6)))
    if verbose:
        for nm, H in (("L", L), ("R", Rr)):
            for d in range(2, 6):
                print(nm, d, " ".join("(%+.3f %+.3f %+.3f)" % p for p in H[d]))
    return worst - 2*R_F, skull_min, where, reach, knuck, L, Rr

OPEN = {1: (None, 4, 6), 2: (5, 9, 5), 3: (7, 11, 6), 4: (9, 14, 8), 5: (11, 17, 10)}
CUPPED = {1: (None, 10, 12), 2: (14, 24, 14), 3: (17, 27, 15), 4: (19, 29, 17), 5: (21, 31, 19)}
NOSPREAD = {d: 0.0 for d in range(1, 6)}

if __name__ == "__main__":
    print("current 0.24.0 (cupped, palms +-5 cm, no vertical offset):")
    for helmet in (0.0, 0.035):
        c, sk, wh, reach, kn, _, _ = evaluate(CUPPED, NOSPREAD, 0.05, 0.0, helmet)
        print("  helmet %.3f: finger clearance %+.1f cm at %s, skull %+.1f cm, left tips reach x=%+.3f, right x=%+.3f, knuckle rows %+.3f/%+.3f (gap %.1f cm)" % (helmet, c*100, wh, sk*100, reach[0], reach[1], kn[0], kn[1], (kn[1]-kn[0])*100))
    if len(sys.argv) > 2:
        best = []
        for w in (0.055, 0.06, 0.065, 0.07, 0.075, 0.08):
            for dv in (0.004, 0.006, 0.008, 0.010, 0.012, 0.014):
                for sp in (0, 4, 8, 12, 16):
                    for mcp in (10, 20, 30, 40):
                        for pip in (15, 25, 35, 45):
                            shape = {1: CUPPED[1]}
                            for d in range(2, 6): shape[d] = (mcp + (d-2)*2, pip + (d-2)*2, pip*0.6)
                            spread = {1: 0, 2: sp, 3: sp*0.25, 4: -sp*0.6, 5: -sp*1.2}
                            worst = 9; skw = 9
                            for helmet in (0.0, 0.035):
                                c, sk, wh, reach, kn, _, _ = evaluate(shape, spread, w, dv, helmet)
                                worst = min(worst, c); skw = min(skw, sk)
                                gap = kn[1] - kn[0]
                                interlaced = 0.015 <= gap <= 0.06 and reach[0] > kn[1] + 0.01 and reach[1] < kn[0] - 0.01
                            if not interlaced: continue
                            best.append((worst, skw, w, dv, sp, mcp, pip))
        best.sort(reverse=True)
        for b in best[:15]: print("clear %+.2f cm skull %+.2f cm  w %.3f dv %.3f spread %d mcp %d pip %d" % (b[0]*100, b[1]*100, *b[2:]))
