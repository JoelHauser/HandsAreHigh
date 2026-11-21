# 0.25.0: hands behind the head, one resting over the other (a clean interlace is not possible on
# this rig: interlace.py). Bottom hand: palm on the back of the skull, a little to its own side,
# fingers towards the middle (as 0.24.0). Top hand: palm lifted off the bottom hand's back, its palm
# over the bottom hand's fingers, its own fingers the other way, over the bottom hand's back towards
# its wrist. Checked with helmet and without, either hand on top:
#   - top fingers / palm vs bottom fingers / palm: capsule clearance (fingers R_F, palm R_P)
#   - every capsule no closer to the skull than the 0.24.0 hands were (the skull model is a sphere:
#     the check is relative to the pose already seen in game)
#   - the top fingers rest on the bottom hand (each finger's closest gap <= 2 cm)
# Usage: python -I overhand.py <skeleton.bundle>
import sys
exec(open("interlace.py").read().rsplit(chr(10) + "if __name__", 1)[0])
R_P = 0.012   # half thickness of the gloved palm (metacarpals)


def hand_pts(palm_at, face, fing, mirror, rots):
    X = mul(fing, -1); Y = mul(face, -1); Z = cross(X, Y)
    pts = chain_local(rots, mirror)
    palm_bone = sub(palm_at, mul(fing, PALM_CENTRE))
    def to_w(p): return add(palm_bone, add(add(mul(X, p[0]), mul(Y, p[1])), mul(Z, p[2])))
    fingers = {d: [to_w(p) for p in seq] for d, seq in pts.items()}
    palm = [(palm_bone, fingers[d][0]) for d in range(2, 6)]   # metacarpal capsules
    return fingers, palm, palm_bone


def proj(v, n): return sub(v, mul(n, dot(v, n)))


def pose(bottom_side, wb, along, lift, helmet, shape_b, shape_t, depth=0.11):
    right, up, fwd = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    sb = bottom_side; st = -sb
    pb = add(mul(fwd, -(depth + helmet)), mul(right, sb * wb))
    face_b = unit(mul(pb, -1))
    fing_b = unit(proj(mul(right, -sb), face_b))
    out_b = mul(face_b, -1)
    # Same construction as Kneel.ApplyHands' top hand: along the bottom hand's fingers, lifted off its back.
    pt = add(add(pb, mul(fing_b, along)), mul(out_b, lift))
    face_t = face_b                       # palm parallel to the bottom hand's back
    fing_t = unit(proj(mul(right, -st), face_t))
    B = hand_pts(pb, face_b, fing_b, sb > 0, joint_rots(shape_b, NOSPREAD))
    T = hand_pts(pt, face_t, fing_t, st > 0, joint_rots(shape_t, NOSPREAD))
    return B, T, pb, pt


def caps(H):
    fingers, palm, _ = H
    c = [(a, b, R_P) for a, b in palm]
    for d in range(2, 6):
        for i in range(3): c.append((fingers[d][i], fingers[d][i+1], R_F))
    for i in range(1, 3): c.append((fingers[1][i], fingers[1][i+1], R_F))
    return c


def check(B, T, helmet):
    cb, ct = caps(B), caps(T)
    worst = 9
    for a1, b1, r1 in ct:
        for a2, b2, r2 in cb:
            worst = min(worst, seg_dist(a1, b1, a2, b2) - r1 - r2)
    sk = 9
    for H in (B, T):
        for a, b, r in caps(H):
            sk = min(sk, point_seg((0, 0, 0), a, b) - r - HEAD_R - helmet)
    rest = max(min(seg_dist(T[0][d][i], T[0][d][i+1], a2, b2) - R_F - r2 for i in range(3) for a2, b2, r2 in cb) for d in range(2, 6))
    return worst, sk, rest


BOTTOM = {1: (None, 10, 12), 2: (14, 24, 14), 3: (17, 27, 15), 4: (19, 29, 17), 5: (21, 31, 19)}


def top_shape(mcp, pip):
    t = {1: (None, 10, 12)}
    for d in range(2, 6): t[d] = (mcp + (d-2)*2, pip + (d-2)*2, round(pip*0.6))
    return t


if __name__ == "__main__":
    base = 9
    for helmet in (0.0, 0.035):
        B, _, _, _ = pose(-1, 0.05, 0.0, 0.04, helmet, BOTTOM, BOTTOM)
        base = min(base, min(point_seg((0, 0, 0), a, b) - r - HEAD_R - helmet for a, b, r in caps(B)))
    print("0.24.0 hand vs skull model: %+.2f cm (baseline)" % (base * 100))
    best = []
    for wb in (0.03, 0.04, 0.05, 0.06):
        for along in (0.03, 0.05, 0.07, 0.09, 0.11):
            for lift in (0.026, 0.03, 0.034, 0.038, 0.042):
                for mcp in (5, 15, 25):
                    for pip in (10, 20, 30):
                        res = []
                        for helmet in (0.0, 0.035):
                            for side in (-1, 1):
                                B, T, pb, pt = pose(side, wb, along, lift, helmet, BOTTOM, top_shape(mcp, pip))
                                res.append(check(B, T, helmet) + (abs(pt[0]),))
                        w = min(r[0] for r in res); sk = min(r[1] for r in res); rest = max(r[2] for r in res)
                        best.append((w, sk, rest, wb, along, lift, mcp, pip, max(r[3] for r in res)))
    # Resting: the top hand's furthest finger no more than 2 cm off the bottom hand (the bottom
    # fingers curve round the skull, the top hand lies flat over them: its edge fingers lift).
    ok = [b for b in best if b[0] >= 0.003 and b[1] >= base - 1e-4 and b[2] <= 0.020]
    ok.sort(key=lambda b: (b[2] - b[0], -b[1]))
    print(len(ok), "clean of", len(best))
    for b in ok[:12]:
        print("clear %+.2f skull %+.2f rest gap %.2f cm | bottom +-%.3f along %.3f lift %.3f top mcp %d pip %d (top palm %.3f off the middle)" % (b[0]*100, b[1]*100, b[2]*100, *b[3:]))
    pick = ok[0] if ok else None
    if pick:
        for side in (-1, 1):
            for helmet in (0.0, 0.035):
                B, T, pb, pt = pose(side, pick[3], pick[4], pick[5], helmet, BOTTOM, top_shape(pick[6], pick[7]))
                print("  bottom %s helmet %.3f: clear %+.2f skull %+.2f rest %.2f cm" % ("left" if side < 0 else "right", helmet, *[x*100 for x in check(B, T, helmet)]))
