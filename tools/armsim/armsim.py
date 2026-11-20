# Offline check of Kneel.ApplyHands (hands behind the head): old swing-only solve vs new
# hinge-preserving solve with a wrist. Axes: x right, y up, z forward. One handedness is used
# throughout (numpy's); only relative consistency matters for these measurements.
# Bones are full frames (rotation matrices: columns = local x, y, z), so roll is measured.
import numpy as np, math

def n(v):
    l = np.linalg.norm(v)
    return v / l if l > 1e-9 else v

cross = np.cross

def rot(axis, deg):
    a = n(np.asarray(axis, float)); t = math.radians(deg); c, s = math.cos(t), math.sin(t)
    x, y, z = a; C = 1 - c
    return np.array([[c + x*x*C, x*y*C - z*s, x*z*C + y*s],
                     [y*x*C + z*s, c + y*y*C, y*z*C - x*s],
                     [z*x*C - y*s, z*y*C + x*s, c + z*z*C]])

def fromto(a, b):
    a = n(a); b = n(b); v = cross(a, b); c = float(np.dot(a, b))
    if np.linalg.norm(v) < 1e-9:
        if c > 0: return np.eye(3)
        p = n(cross(a, [1, 0, 0])) if abs(a[0]) < 0.9 else n(cross(a, [0, 1, 0]))
        return rot(p, 180)
    return rot(v, math.degrees(math.atan2(np.linalg.norm(v), c)))

def look(f, u):  # columns x, y, z with z = f, y = u made square (like LookRotation)
    f = n(f); r = cross(u, f)
    r = n(r) if np.linalg.norm(r) > 1e-9 else n(cross([0, 1, 0], f))
    return np.stack([r, cross(f, r), f], axis=1)

def slerpv(a, b, t):
    a = n(a); b = n(b); th = math.acos(np.clip(np.dot(a, b), -1, 1))
    if th < 1e-6: return a
    return n((math.sin((1 - t) * th) * a + math.sin(t * th) * b) / math.sin(th))

def angle(a, b): return math.degrees(math.acos(np.clip(np.dot(n(a), n(b)), -1, 1)))
def ss(a, b, t):
    x = min(1, max(0, (t - a) / (b - a))); return x * x * (3 - 2 * x)
def perp(v, axis, fb):
    u = n(axis); v = v - u * np.dot(v, u)
    return n(v) if np.linalg.norm(v) > 1e-6 else fb

def twobone(root, target, a, b, hint):
    ax = target - root; d = np.linalg.norm(ax)
    u = ax / d if d > 1e-4 else np.array([0, -1., 0])
    d = min(max(d, abs(a - b) + 1e-3), a + b - 1e-3)
    c = np.clip((a*a + d*d - b*b) / (2*a*d), -1, 1); s = math.sqrt(1 - c*c)
    v = perp(hint, u, n(cross(u, [0, 1, 0])))
    return root + u*a*c + v*a*s, root + u*d

def slerp_rot(cur, want, w):
    Rrel = want @ cur.T
    ang = math.degrees(math.acos(np.clip((np.trace(Rrel) - 1) / 2, -1, 1)))
    if ang < 1e-4: return cur
    ax = np.array([Rrel[2, 1] - Rrel[1, 2], Rrel[0, 2] - Rrel[2, 0], Rrel[1, 0] - Rrel[0, 1]])
    if np.linalg.norm(ax) < 1e-9: return cur
    return rot(ax, ang * w) @ cur

R = np.array([1., 0, 0]); U = np.array([0, 1., 0]); F = np.array([0, 0, 1.])
A, C = 0.29, 0.27; HEADR = 0.10; PALMC = 0.055

def handsup(side):
    sh = np.array([0.18*side, 1.45, 0.0]); head = np.array([0, 1.62, 0.02]); skull = head + U*0.08
    raise_, ahead = math.radians(12), math.radians(25); out = R*side
    ud = n((out*math.cos(ahead) + F*math.sin(ahead))*math.cos(raise_) + U*math.sin(raise_))
    fd = n(rot(F, 8*side) @ (rot(R, 12) @ U))
    el = sh + ud*A; pa = el + fd*C
    hinge = n(cross(ud, fd))
    Mu = look(ud, hinge); Mf = look(fd, hinge)            # local y = hinge: a clean hinge
    Mp = look(fd, n(F - fd*np.dot(F, fd)))                 # palm: y = palm normal (to the captor)
    return sh, el, pa, Mu, Mf, Mp, skull

def elbow_twist(Mu, Mf):
    hu = Mu[:, 1]; hf = Mf[:, 1]; z = Mf[:, 2]
    return angle(n(hu - z*np.dot(hu, z)), hf)

def run(mode, side, look_deg=0, steps=120, helmet=False, wpath=False):
    L = rot(U, look_deg)
    hx = 0.035 if helmet else 0.0
    res = dict(maxtwist=0, maxjump=0, flips=0, minclear=9, maxwrist=0, maxreach=0)
    prev = None; prevbend = None
    for k in range(steps + 1):
        t = k / steps; h = ss(0, 1, t)
        sh, el, pa, Mu, Mf, Mp, skull = handsup(side)
        minR = HEADR + hx + 0.02
        bendNow = perp(el - sh, pa - sh, R*side)
        bendOut = n(L @ (R*side*1.0) + F*0.35 + U*0.25)
        if mode == "old":
            target = skull + L @ (-F*(0.11 + hx) + R*side*0.05)
            pos = pa + (target - pa)*h
            fs = pos - skull
            if np.linalg.norm(fs) < minR: pos = skull + n(fs)*minR
            elbow, hand = twobone(sh, pos, A, C, slerpv(bendNow, bendOut, h))
            Q = fromto(el - sh, elbow - sh); Mu = Q @ Mu; Mf = Q @ Mf; Mp = Q @ Mp
            Q2 = fromto(Q @ (pa - el), hand - elbow); Mf = Q2 @ Mf; Mp = Q2 @ Mp
        else:
            N_end = n(L @ F)
            fe = n(L @ (-R*side + U*0.25)); fe = n(fe - N_end*np.dot(fe, N_end))
            pc = skull + L @ (-F*(0.11 + hx) + R*side*0.05)
            wrist_t = pc - fe*PALMC
            P0 = pa - skull; P2 = wrist_t - skull
            lat = max(abs(np.dot(P0, L @ R)), 0.22)
            P1 = L @ (R*side*lat + U*0.06 - F*0.02)
            off = (1-h)**2*P0 + 2*h*(1-h)*P1 + h*h*P2
            if np.linalg.norm(off) < minR: off = n(off)*minR
            pos = skull + off
            elbow, hand = twobone(sh, pos, A, C, slerpv(bendNow, bendOut, h))
            hc = n(cross(el - sh, pa - el)); hn = n(cross(elbow - sh, hand - elbow))
            Q = look(elbow - sh, hn) @ look(el - sh, hc).T; Mu = Q @ Mu; Mf = Q @ Mf; Mp = Q @ Mp
            Q2 = look(hand - elbow, hn) @ look(Q @ (pa - el), Q @ hc).T; Mf = Q2 @ Mf; Mp = Q2 @ Mp
            ww = ss(0.35, 1, h)
            Nn = n(skull - hand); fw = n(L @ (-R*side + U*0.25)); fw = n(fw - Nn*np.dot(fw, Nn))
            fore = hand - elbow; b = angle(fore, fw)
            if b > 80: fw = slerpv(fore, fw, 80/b)
            Nn = n(Nn - fw*np.dot(Nn, fw))
            Mp = slerp_rot(Mp, look(fw, Nn), ww)
            res["maxwrist"] = max(res["maxwrist"], angle(fore, Mp[:, 2]))
        tw = elbow_twist(Mu, Mf); res["maxtwist"] = max(res["maxtwist"], tw)
        if prev is not None:
            res["maxjump"] = max(res["maxjump"], np.linalg.norm(hand - prev[0]), np.linalg.norm(elbow - prev[1]))
            bend = n((elbow - sh) - n(hand - sh)*np.dot(elbow - sh, n(hand - sh)))
            if prevbend is not None and angle(bend, prevbend) > 30: res["flips"] += 1
            prevbend = bend
        prev = (hand, elbow)
        pcen = hand + Mp[:, 2]*PALMC; tip = hand + Mp[:, 2]*0.17
        res["minclear"] = min(res["minclear"], np.linalg.norm(pcen - skull) - (HEADR + hx), np.linalg.norm(tip - skull) - (HEADR + hx))
        res["maxreach"] = max(res["maxreach"], np.linalg.norm(pos - sh)/(A + C))
    res["endpalm"] = angle(Mp[:, 1], skull - hand)
    res["endtwist"] = tw
    res["elbowdeg"] = angle(elbow - sh, hand - elbow)
    res["elbowout"] = np.dot(elbow - sh, R*side)
    return res

# ---- realistic start: the animator's arm (hanging, elbow a little bent, a clean hinge with the
# rig's own local axes) swung to hands-up by ArmPose.Apply (FromToRotation per bone, as in game).
def rig_local(side):
    # Arbitrary local bone axes (the rig's, unknown to us): along = local x, hinge = local z.
    return np.array([1., 0, 0]), np.array([0, 0, 1.])
def idle_then_handsup(side):
    sh, el_h, pa_h, _, _, _, skull = handsup(side)
    aL, hL = rig_local(side)
    ud0 = n(-U + F*0.15 + R*side*0.1); fd0 = n(-U + F*0.45)          # hanging, elbow ~25 deg
    hinge0 = n(cross(ud0, fd0))
    def world(dirv, hinge):  # bone world rotation from (dir, hinge) and the rig's local axes
        return look(dirv, hinge) @ look(aL, hL).T
    Wu = world(ud0, hinge0); Wf = world(fd0, hinge0)
    el0 = sh + ud0*A; pa0 = el0 + fd0*C
    # ArmPose.Apply: swing upper, then forearm (forearm is the upper's child)
    Q = fromto(ud0, el_h - sh); Wu = Q @ Wu; Wf = Q @ Wf
    Q2 = fromto(Q @ fd0, pa_h - el_h); Wf = Q2 @ Wf
    return sh, el_h, pa_h, Wu, Wf, aL, hL, skull, hinge0
def twist_world(Wu, Wf, aL, hL):
    hu = Wu @ hL; hf = Wf @ hL; z = n(Wf @ aL)
    return angle(n(hu - z*np.dot(hu, z)), hf)
def hinge_vs_bend(W, aL, hL, dirv, other):
    # how far the bone's own hinge axis is from the real bend plane normal (0 = bends on its hinge)
    want = n(cross(dirv, other)); return angle(W @ hL, want)

def run2(mode, side, steps=120):
    worst_tw = 0; worst_hinge = 0; start_tw = None
    for k in range(steps + 1):
        h = ss(0, 1, k/steps)
        sh, el, pa, Wu, Wf, aL, hL, skull, _ = idle_then_handsup(side)
        if start_tw is None: start_tw = twist_world(Wu, Wf, aL, hL); start_h = hinge_vs_bend(Wu, aL, hL, el - sh, pa - el)
        target = skull + (-F*0.11 + R*side*0.05)
        pos = pa + (target - pa)*h if mode == "old" else None
        if mode != "old":
            fe = n(-R*side + U*0.25); fe = n(fe - F*np.dot(fe, F))
            wrist_t = skull + (-F*0.11 + R*side*0.05) - fe*PALMC
            P0 = pa - skull; P2 = wrist_t - skull
            lat = max(abs(np.dot(P0, R)), 0.22); P1 = R*side*lat + U*0.06 - F*0.02
            pos = skull + (1-h)**2*P0 + 2*h*(1-h)*P1 + h*h*P2
        bendNow = perp(el - sh, pa - sh, R*side); bendOut = n(R*side + F*0.35 + U*0.25)
        elbow, hand = twobone(sh, pos, A, C, slerpv(bendNow, bendOut, h))
        if mode == "old":
            Q = fromto(el - sh, elbow - sh); Wu = Q @ Wu; Wf = Q @ Wf
            Q2 = fromto(Q @ (pa - el), hand - elbow); Wf = Q2 @ Wf
        else:
            hn = n(cross(elbow - sh, hand - elbow)); hc = n(cross(el - sh, pa - el))
            Q = look(elbow - sh, hn) @ look(el - sh, hc).T; Wu_rel = Q @ Wu; Wf_rel = Q @ Wf
            Q2 = look(hand - elbow, hn) @ look(Q @ (pa - el), Q @ hc).T; Wf_rel = Q2 @ Wf_rel
            # absolute from the measured local axes (aL, hL), eased in early in the transition
            wa = ss(0.0, 0.4, h)
            Wu_abs = look(elbow - sh, hn) @ look(aL, hL).T
            Wu = slerp_rot(Wu_rel, Wu_abs, wa)
            # forearm: its parent moved by (Wu * inv(Wu_rel)); then absolute
            Wf_now = Wu @ Wu_rel.T @ Wf_rel
            Wf_abs = look(hand - elbow, hn) @ look(aL, hL).T
            Wf = slerp_rot(Wf_now, Wf_abs, wa)
        worst_tw = max(worst_tw, twist_world(Wu, Wf, aL, hL))
        worst_hinge = max(worst_hinge, hinge_vs_bend(Wu, aL, hL, elbow - sh, hand - elbow))
    return start_tw, start_h, worst_tw, worst_hinge, twist_world(Wu, Wf, aL, hL), hinge_vs_bend(Wu, aL, hL, elbow - sh, hand - elbow)

print("Realistic start (animator idle -> ArmPose swing -> hands-up -> behind head):")
print(f"{'mode':4} {'arm':3} {'hands-up twist':>14} {'hands-up hinge off':>18} {'worst twist':>11} {'worst hinge off':>15} {'end twist':>9} {'end hinge off':>13}")
for mode in ("old", "new"):
    for side in (-1, 1):
        a = run2(mode, side)
        print(f"{mode:4} {'L' if side < 0 else 'R':3} {a[0]:14.1f} {a[1]:18.1f} {a[2]:11.1f} {a[3]:15.1f} {a[4]:9.1f} {a[5]:13.1f}")
print()

print(f"{'mode':4} {'arm':3} {'look':>4} {'helm':4} {'maxTwist':>8} {'endTwist':>8} {'flips':>5} {'jump cm':>7} {'palm->skull':>11} {'clear cm':>8} {'wristMax':>8} {'reach':>5} {'elbowFlex':>9} {'elbowOut':>8}")
for mode in ("old", "new"):
    for side in (-1, 1):
        for lk in (0, 25, -25, 38):
            for helm in (False, True):
                r = run(mode, side, lk, helmet=helm)
                print(f"{mode:4} {'L' if side < 0 else 'R':3} {lk:4d} {'y' if helm else 'n':4} {r['maxtwist']:8.1f} {r['endtwist']:8.1f} {r['flips']:5d} {r['maxjump']*100:7.2f} {r['endpalm']:11.1f} {r['minclear']*100:8.1f} {r['maxwrist']:8.1f} {r['maxreach']:5.2f} {r['elbowdeg']:9.1f} {r['elbowout']:8.2f}")
