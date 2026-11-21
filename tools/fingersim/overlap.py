# 0.26.0: hands behind the head, fingers overlapping in the middle (replaces overhand.py's layout).
# Joel's 0.25.0 screenshot: the top hand lay along the bottom hand "towards its wrist", so its
# fingertips ended in the opposite sleeve cuff. Here each palm rests on its own side of the back of
# the skull (wrists out behind the ears), fingers towards the middle; the top hand sits a finger's
# thickness further out so only the FINGERS overlap, its fingers over the bottom hand's fingers.
# Same hand model as interlace.py / overhand.py (left hand from skeleton.bundle, right = mirror).
# Checked for both hands on top, helmet on and off:
#   - top vs bottom hand capsules: clearance (no finger through a finger)
#   - every capsule no closer to the skull than the 0.24.0 hands (relative skull check)
#   - each hand vs the OTHER arm's sleeve (cuff capsule from the wrist bone towards the elbow,
#     R_SLEEVE), and the top fingertips this far from the bottom wrist: the 0.25.0 failure
#   - resting: the top fingers lie on the bottom ones (closest gap <= 1.2 cm)
# Prints the 0.25.0 layout's numbers first (it must fail the sleeve check: that is the screenshot).
# Usage (from tools/fingersim): python -I overlap.py <skeleton.bundle>
import sys
exec(open("interlace.py").read().rsplit(chr(10) + "if __name__", 1)[0])
R_P = 0.012        # half thickness of the gloved palm
R_SLEEVE = 0.045   # jacket cuff radius round the forearm
CUFF_BACK = 0.015  # the cuff starts this far behind the wrist bone, towards the elbow
UPPER, FORE = 0.29, 0.27   # shoulder->elbow, elbow->wrist bone (approximate EFT rig)


def hand_pts(palm_at, face, fing, mirror, rots):
    X = mul(fing, -1); Y = mul(face, -1); Z = cross(X, Y)
    pts = chain_local(rots, mirror)
    palm_bone = sub(palm_at, mul(fing, PALM_CENTRE))
    def to_w(p): return add(palm_bone, add(add(mul(X, p[0]), mul(Y, p[1])), mul(Z, p[2])))
    fingers = {d: [to_w(p) for p in seq] for d, seq in pts.items()}
    palm = [(palm_bone, fingers[d][0]) for d in range(2, 6)]
    return fingers, palm, palm_bone


def proj(v, n): return sub(v, mul(n, dot(v, n)))


def caps(H):
    fingers, palm, _ = H
    c = [(a, b, R_P) for a, b in palm]
    for d in range(2, 6):
        for i in range(3): c.append((fingers[d][i], fingers[d][i+1], R_F))
    for i in range(1, 3): c.append((fingers[1][i], fingers[1][i+1], R_F))
    return c


ELBOW_UP = 0.10   # 0.26.0 (was 0.25): elbows out and a little forward, not lifted above the hands


def elbow_of(side, wrist):
    # Kneel.ApplyHands' bend hint: out to his side, a little forward and up (ElbowsOut 1).
    right, up, fwd = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    sh = (side * 0.17, -0.25, -0.02)
    d = sub(wrist, sh); L = length(d)
    L = min(L, UPPER + FORE - 1e-4)
    u = unit(d)
    hint = unit(add(add(mul(right, side * 1.0), mul(fwd, 0.35)), mul(up, ELBOW_UP)))
    v = unit(proj(hint, u))
    cos = (UPPER * UPPER + L * L - FORE * FORE) / (2 * UPPER * L)
    sin = math.sqrt(max(0.0, 1 - cos * cos))
    return add(sh, add(mul(u, UPPER * cos), mul(v, UPPER * sin)))


def sleeve(H, side):
    wrist = H[2]
    el = elbow_of(side, wrist)
    a = add(wrist, mul(unit(sub(el, wrist)), CUFF_BACK))
    return (a, el, R_SLEEVE)


def place(side, w, dy, lift, depth, helmet, up_tilt):
    right, up, fwd = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    p = add(add(mul(fwd, -(depth + helmet)), mul(right, side * w)), mul(up, dy))
    face = unit(mul(p, -1))
    p = add(p, mul(face, -lift))
    fing = unit(proj(add(mul(right, -side), mul(up, up_tilt)), face))
    return p, face, fing


def pose_new(bottom, wb, wt, dyt, lift, helmet, shape_t, depth=0.11, up_tilt=0.25):
    st = -bottom
    pb, fb, gb = place(bottom, wb, 0.0, 0.0, depth, helmet, up_tilt)
    pt, ft, gt = place(st, wt, dyt, lift, depth, helmet, up_tilt)
    B = hand_pts(pb, fb, gb, bottom > 0, joint_rots(BOTTOM, NOSPREAD))
    T = hand_pts(pt, ft, gt, st > 0, joint_rots(shape_t, NOSPREAD))
    return B, T


def pose_025(bottom, helmet, depth=0.11):
    # Kneel.ApplyHands 0.25.0: OverBottom 0.06, OverAlong 0.05, OverLift 0.038, top shape OverTop.
    right, up, fwd = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    sb = bottom
    pb = add(mul(fwd, -(depth + helmet)), mul(right, sb * 0.06))
    fb = unit(mul(pb, -1))
    gb = unit(proj(add(mul(right, -sb), mul(up, 0.25)), fb))
    pt = add(add(pb, mul(gb, 0.05)), mul(fb, -0.038))
    B = hand_pts(pb, fb, gb, sb > 0, joint_rots(BOTTOM, NOSPREAD))
    T = hand_pts(pt, fb, mul(gb, -1), -sb > 0, joint_rots(OVERTOP, NOSPREAD))
    return B, T


def check(B, T, bottom, helmet):
    cb, ct = caps(B), caps(T)
    hands = min(seg_dist(a1, b1, a2, b2) - r1 - r2 for a1, b1, r1 in ct for a2, b2, r2 in cb)
    sk = min(point_seg((0, 0, 0), a, b) - r - HEAD_R - helmet for H in (B, T) for a, b, r in caps(H))
    sb_, st_ = sleeve(B, bottom), sleeve(T, -bottom)
    sl = min(min(seg_dist(a, b, sb_[0], sb_[1]) - r - sb_[2] for a, b, r in ct),
             min(seg_dist(a, b, st_[0], st_[1]) - r - st_[2] for a, b, r in cb))
    rest = min(seg_dist(T[0][d][i], T[0][d][i+1], a2, b2) - R_F - r2 for d in range(2, 6) for i in range(3) for a2, b2, r2 in cb)
    tips = min(length(sub(T[0][d][3], B[2])) for d in range(2, 6))
    return hands, sk, sl, rest, tips


BOTTOM = {1: (None, 10, 12), 2: (14, 24, 14), 3: (17, 27, 15), 4: (19, 29, 17), 5: (21, 31, 19)}
OVERTOP = {1: (None, 10, 12), 2: (15, 10, 6), 3: (17, 12, 7), 4: (19, 14, 8), 5: (21, 16, 10)}


def top_shape(mcp, pip):
    t = {1: (None, 10, 12)}
    for d in range(2, 6): t[d] = (mcp + (d-2)*2, pip + (d-2)*2, round(pip*0.6))
    return t


# Kneel.ApplyHands 0.26.0 constants (OverBottomW, OverTopW, OverTopUp, OverLift) and Fingers.OverTop.
CHOSEN = (0.095, 0.105, 0.03, 0.020, 10, 8)


if __name__ == "__main__":
    base = 9
    for helmet in (0.0, 0.035):
        pb, fb, gb = place(-1, 0.05, 0, 0, 0.11, helmet, 0.25)
        B = hand_pts(pb, fb, gb, False, joint_rots(BOTTOM, NOSPREAD))
        base = min(base, min(point_seg((0, 0, 0), a, b) - r - HEAD_R - helmet for a, b, r in caps(B)))
    print("0.24.0 hand vs skull model: %+.2f cm (baseline)" % (base * 100))
    for side in (-1, 1):
        for helmet in (0.0, 0.035):
            B, T = pose_025(side, helmet)
            print("0.25.0 bottom %-5s helmet %.3f: hands %+.2f skull %+.2f SLEEVE %+.2f rest %.2f top tips to bottom wrist %.1f cm"
                  % ("left" if side < 0 else "right", helmet, *[x * 100 for x in check(B, T, side, helmet)]))
    best = []
    for wb in (0.065, 0.075, 0.085, 0.095):
        for wt in (0.065, 0.075, 0.085, 0.095):
            for dyt in (0.0, 0.015, 0.03):
                for lift in (0.020, 0.026, 0.032, 0.038):
                    for mcp in (5, 10, 15):
                        for pip in (8, 15, 22):
                            res = [check(*pose_new(side, wb, wt, dyt, lift, helmet, top_shape(mcp, pip)), side, helmet)
                                   for helmet in (0.0, 0.035) for side in (-1, 1)]
                            best.append((min(r[0] for r in res), min(r[1] for r in res), min(r[2] for r in res),
                                         max(r[3] for r in res), min(r[4] for r in res), wb, wt, dyt, lift, mcp, pip))
    ok = [b for b in best if b[0] >= 0.003 and b[1] >= base - 1e-4 and b[2] >= 0.01 and b[3] <= 0.012 and b[4] >= 0.06]
    # Prefer: hands resting close, a clear sleeve margin, a palm near the skull (small lift), wrists apart.
    ok.sort(key=lambda b: (b[3] - b[0] - 0.3 * b[2], b[8]))
    print(len(ok), "clean of", len(best))
    for b in ok[:12]:
        print("hands %+.2f skull %+.2f sleeve %+.2f rest %.2f tips-wrist %.1f cm | bottom +-%.3f top +-%.3f top up %+.3f lift %.3f top mcp %d pip %d"
              % (b[0]*100, b[1]*100, b[2]*100, b[3]*100, b[4]*100, *b[5:]))
    wb, wt, dyt, lift, mcp, pip = CHOSEN
    print("chosen (code):")
    for side in (-1, 1):
        for helmet in (0.0, 0.035):
            r = check(*pose_new(side, wb, wt, dyt, lift, helmet, top_shape(mcp, pip)), side, helmet)
            print("  bottom %-5s helmet %.3f: hands %+.2f skull %+.2f sleeve %+.2f rest %.2f tips-wrist %.1f cm %s"
                  % ("left" if side < 0 else "right", helmet, *[x * 100 for x in r],
                     "PASS" if r[0] >= 0.003 and r[1] >= base - 1e-4 and r[2] >= 0.01 and r[3] <= 0.012 and r[4] >= 0.06 else "FAIL"))
