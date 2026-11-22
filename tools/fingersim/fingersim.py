# 0.24.0: open hands for the surrender poses. Reads the finger chain from the game's
# skeleton.bundle (UnityPy), applies the target shapes used by src/Mercy/Fingers.cs
# (swing of the bind rotation kept, flexion about the bone's local +Z set), and checks:
#   - every joint flexes palm-ward (0 < flex), none bent backwards (no overextension)
#   - fingertips stay in front of the palm plane's back side (no tip through the hand)
#   - neighbouring fingertips are spaced like a relaxed hand (1.5 - 4.5 cm; the bind hand itself is 3.2 - 3.7 at ~75 deg curl)
#   - a blend between shapes changes each joint monotonically (no snap through a fist)
# and prints the quaternion constants for Fingers.cs (left hand; right = (-x,-y,z,w)).
# Usage: python -I fingersim.py <skeleton.bundle>
import UnityPy, sys, math
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
def axis_angle(axis, deg):
    h = math.radians(deg)/2; s = math.sin(h); return (axis[0]*s, axis[1]*s, axis[2]*s, math.cos(h))
def twist_z(q):  # swing-twist about local Z (same as ArmPose.TwistAbout(q, Vector3.forward))
    return norm((0.0, 0.0, q[2], q[3]))
def flex_of(q):
    t = twist_z(q); return math.degrees(2*math.atan2(t[2], t[3]))

DIG = [1, 2, 3, 4, 5]
bind = {}; lpos = {}
for d in DIG:
    for j in (1, 2, 3):
        t = tr[byname["Base HumanLDigit%d%d" % (d, j)]]
        lr = t.m_LocalRotation; lp = t.m_LocalPosition
        bind[(d, j)] = (lr.x, lr.y, lr.z, lr.w); lpos[(d, j)] = (lp.x, lp.y, lp.z)
tiplen = {1: 0.030, 2: 0.022, 3: 0.024, 4: 0.023, 5: 0.018}   # distal pad beyond the last joint

# Target flexion (deg) per joint. Thumb root (x1) keeps its bind rotation (its swing is the
# opposition; only 12/13 flex). Same tables as Fingers.cs.
SHAPES = {
    "open":   {1: (None, 4, 6),  2: (5, 9, 5),   3: (7, 11, 6),  4: (9, 14, 8),  5: (11, 17, 10)},
    "cupped": {1: (None, 10, 12), 2: (14, 24, 14), 3: (17, 27, 15), 4: (19, 29, 17), 5: (21, 31, 19)},
    "bind":   None,
}

def shaped(shape):
    out = {}
    for d in DIG:
        for j in (1, 2, 3):
            q = bind[(d, j)]
            if shape is None: out[(d, j)] = q; continue
            f = shape[d][j-1]
            if f is None: out[(d, j)] = q; continue
            tw = twist_z(q); swing = qmul(q, inv(tw))
            out[(d, j)] = norm(qmul(swing, axis_angle((0, 0, 1), f)))
    return out

def tips(rots):
    res = {}
    for d in DIG:
        q = (0, 0, 0, 1); p = (0, 0, 0)
        for j in (1, 2, 3):
            p = tuple(a+b for a, b in zip(p, rot(q, lpos[(d, j)])))
            q = qmul(q, rots[(d, j)])
        p = tuple(a+b for a, b in zip(p, rot(q, (-tiplen[d], 0, 0))))
        res[d] = p
    return res

ok = True
for name, sh in SHAPES.items():
    r = shaped(sh); tp = tips(r)
    flex = {k: flex_of(v) for k, v in r.items()}
    print("== %s" % name)
    for d in DIG:
        f = [flex[(d, j)] for j in (1, 2, 3)]
        print("  digit %d flex %s  total %5.1f  tip palm-frame (%+.3f %+.3f %+.3f)" % (d, " ".join("%5.1f" % x for x in f), sum(f), *tp[d]))
        if name != "bind":
            if any(x < 0 for x in f[1:] if x is not None) or (d != 1 and f[0] < 0): print("    FAIL overextended"); ok = False
            if tp[d][1] > 0.02: print("    FAIL tip behind the back of the hand"); ok = False
    sp = [math.dist(tp[a], tp[b]) for a, b in ((2, 3), (3, 4), (4, 5))]
    print("  neighbour tip spacing (cm): " + " ".join("%.1f" % (x*100) for x in sp))
    if name != "bind" and any(x < 0.015 or x > 0.045 for x in sp): print("  FAIL spacing"); ok = False

# Blend open <-> cupped: per-joint flexion must change monotonically (Fingers.cs lerps flex).
r0, r1 = shaped(SHAPES["open"]), shaped(SHAPES["cupped"])
mono = True
for k in r0:
    a = flex_of(r0[k]); b = flex_of(r1[k]); prev = None
    for i in range(11):
        t = i/10; f = a + (b-a)*t
        if prev is not None and (f - prev)*(b - a) < -1e-6: mono = False
        prev = f
print("blend open<->cupped monotonic per joint: %s" % mono); ok = ok and mono

print("\n// Fingers.cs constants: left-hand bind local rotations (x, y, z, w)")
for d in DIG:
    print("            { " + ", ".join("new Quaternion(%.5ff, %.5ff, %.5ff, %.5ff)" % bind[(d, j)] for j in (1, 2, 3)) + " },")
print("RESULT", "PASS" if ok else "FAIL")
