# Validation of the 0.13.1 arm solve as implemented (ArmPose.PoseArm + MeasureAxes, the
# hands-up pose, Kneel.ApplyHands' curve/elbow/wrist). Same helpers as armsim.py.
import math, numpy as np
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from armsim_lib import *

def measure(side):
    # animator's hanging arm, elbow ~25 deg: the rig's local axes are whatever they are
    aL, hL = np.array([1., 0, 0]), np.array([0, 0, 1.])
    return aL, hL

def world(dirv, hinge, aL, hL): return look(dirv, hinge) @ look(aL, hL).T

def pose_arm(Wu, Wf, sh, el, pa, du, df, aL, hL, roll):
    # mirrors ArmPose.PoseArm with w = 1
    hn = n(cross(du, df)); hc = n(cross(el - sh, pa - el))
    relU = look(du, hn) @ look(el - sh, hc).T @ Wu
    Wu2 = slerp_rot(relU, world(du, hn, aL, hL), roll)
    M = Wu2 @ Wu.T                      # how the upper arm moved: the forearm (child) moves with it
    Wf1 = M @ Wf; el2 = sh + n(du) * A; pa2 = el2 + M @ (pa - el)
    cf = pa2 - el2; hc2 = n(cross(el2 - sh, cf))
    relF = look(df, hn) @ look(cf, hc2).T @ Wf1
    Wf2 = slerp_rot(relF, world(df, hn, aL, hL), roll)
    return Wu2, Wf2, el2, el2 + n(df) * C

def twist(Wu, Wf, aL, hL):
    hu = Wu @ hL; hf = Wf @ hL; z = n(Wf @ aL)
    return angle(n(hu - z * np.dot(hu, z)), hf)

def hands_up(side):
    sh = np.array([0.18 * side, 1.45, 0.0]); head = np.array([0, 1.62, 0.02]); skull = head + U * 0.08
    aL, hL = measure(side)
    ud0 = n(-U + F * 0.15 + R * side * 0.1); fd0 = n(-U + F * 0.45); h0 = n(cross(ud0, fd0))
    Wu = world(ud0, h0, aL, hL); Wf = world(fd0, h0, aL, hL)
    el0 = sh + ud0 * A; pa0 = el0 + fd0 * C
    raise_, ahead = math.radians(12), math.radians(25); out = R * side
    ud = n((out * math.cos(ahead) + F * math.sin(ahead)) * math.cos(raise_) + U * math.sin(raise_))
    fd = n(rot(F, 8 * side) @ (rot(R, 12) @ U))
    Wu, Wf, el, pa = pose_arm(Wu, Wf, sh, el0, pa0, ud, fd, aL, hL, 1.0)
    return sh, el, pa, Wu, Wf, aL, hL, skull

def behind(side, h, look_deg=0.0, helmet=False, idle_elbow=0.0):
    sh, el, pa, Wu, Wf, aL, hL, skull = hands_up(side)
    L = rot(U, look_deg); hx = 0.035 if helmet else 0.0
    upL, rL, fL = L @ U, L @ R, L @ F
    minR = HEADR + hx + 0.02
    palmAt = skull - fL * (0.11 + hx) + rL * side * 0.05
    face = skull - palmAt
    fing = n(-rL * side + upL * 0.25); fing = n(fing - n(face) * np.dot(fing, n(face)))
    wristAt = palmAt - fing * PALMC
    p0 = pa - skull; p2 = wristAt - skull
    lat = max(abs(np.dot(p0, rL)), 0.22); p1 = rL * side * lat + upL * 0.06 - fL * 0.02
    u = 1 - h; off = p0 * u * u + p1 * 2 * h * u + p2 * h * h
    if np.linalg.norm(off) < minR: off = n(off) * minR
    pos = skull + off
    bendNow = perp(el - sh, pa - sh, R * side)
    bendOut = n(rL * side * (1.0 + idle_elbow) + F * 0.35 + U * 0.25)
    elbow, wrist = twobone(sh, pos, A, C, slerpv(bendNow, bendOut, h))
    Wu2, Wf2, el2, wr2 = pose_arm(Wu, Wf, sh, el, pa, elbow - sh, wrist - elbow, aL, hL, ss(0, 0.4, h))
    # wrist (thumb-based, mirrored here as palm frame: z fingers, y palm normal)
    ww = ss(0.35, 1, h)
    faceN = n(skull - wr2); fw = n(-rL * side + upL * 0.25); fw = n(fw - faceN * np.dot(fw, faceN))
    fore = wr2 - el2; b = angle(fore, fw)
    if b > 80: fw = slerpv(fore, fw, 80 / b)
    Pw = look(fw, n(faceN - fw * np.dot(faceN, fw)))
    P0 = look(fore, n(F - n(fore) * np.dot(F, n(fore))))          # hands-up palm: fingers along forearm, palm forward
    Pm = slerp_rot(P0, Pw, ww)
    pcen = wr2 + Pm[:, 2] * PALMC; tip = wr2 + Pm[:, 2] * 0.17
    clear = min(np.linalg.norm(pcen - skull), np.linalg.norm(tip - skull)) - (HEADR + hx)
    return dict(elbow=el2, wrist=wr2, twist=twist(Wu2, Wf2, aL, hL), clear=clear,
                palm=angle(Pm[:, 1], skull - wr2), wristbend=angle(fore, Pm[:, 2]),
                flex=angle(el2 - sh, wr2 - el2), reach=np.linalg.norm(pos - sh) / (A + C),
                gap=np.linalg.norm(wr2 - pos), Pm=Pm, sh=sh)

def sweep(side, hs, **kw):
    worst = dict(twist=0, jump=0, flips=0, clear=9, wristbend=0, reach=0, gap=0, rotjump=0)
    prev = None
    for h in hs:
        r = behind(side, h, **kw)
        worst["twist"] = max(worst["twist"], r["twist"]); worst["clear"] = min(worst["clear"], r["clear"])
        worst["wristbend"] = max(worst["wristbend"], r["wristbend"]); worst["reach"] = max(worst["reach"], r["reach"])
        worst["gap"] = max(worst["gap"], r["gap"])
        if prev is not None:
            worst["jump"] = max(worst["jump"], np.linalg.norm(r["wrist"] - prev["wrist"]), np.linalg.norm(r["elbow"] - prev["elbow"]))
            bp = n((prev["elbow"] - r["sh"]) - n(prev["wrist"] - r["sh"]) * np.dot(prev["elbow"] - r["sh"], n(prev["wrist"] - r["sh"])))
            bn = n((r["elbow"] - r["sh"]) - n(r["wrist"] - r["sh"]) * np.dot(r["elbow"] - r["sh"], n(r["wrist"] - r["sh"])))
            if angle(bp, bn) > 30: worst["flips"] += 1
            Rrel = r["Pm"] @ prev["Pm"].T
            worst["rotjump"] = max(worst["rotjump"], math.degrees(math.acos(np.clip((np.trace(Rrel) - 1) / 2, -1, 1))))
        prev = r
    return worst, prev

def row(label, w, end):
    print(f"{label:44} twist {w['twist']:5.1f}  flips {w['flips']}  step {w['jump']*100:5.2f} cm  palm-rot step {w['rotjump']:5.1f} deg  "
          f"clear >= {w['clear']*100:4.1f} cm  wrist <= {w['wristbend']:4.1f}  reach <= {w['reach']:.2f}  ik gap {w['gap']*100:.1f} cm | end palm {end['palm']:4.1f} flex {end['flex']:5.1f}")

# 1. Transitions at frame rates (hands window 0.76->1 of a 2.3 s steady descent = 0.55 s; frightened 0.38->0.82 of 1.3 s = 0.57 s)
for fps in (20, 30, 60, 144):
    frames = max(2, int(0.55 * fps))
    for side in (-1, 1):
        for helm in (False, True):
            hs = [ss(0, 1, k / frames) for k in range(frames + 1)]
            w, end = sweep(side, hs, helmet=helm)
            row(f"{fps:3d} fps {'L' if side < 0 else 'R'} {'helmet' if helm else 'no helmet'} down", w, end)
# 2. Reversal mid-move (rise ordered at h 0.5): back along the same path
hs = [k / 60 for k in range(31)] + [0.5 - k / 60 for k in range(1, 31)]
for side in (-1, 1):
    w, end = sweep(side, hs); row(f"reversal at 0.5 {'L' if side < 0 else 'R'}", w, end)
# 3. Holding the pose while the head turns (h = 1), look -38..38 deg in 1 deg steps (head speed 200 deg/s at 60 fps ~ 3 deg/frame)
for side in (-1, 1):
    for helm in (False, True):
        worst = dict(twist=0, jump=0, flips=0, clear=9, wristbend=0, reach=0, gap=0, rotjump=0); prev = None
        for lk in list(range(-38, 39, 1)):
            r = behind(side, 1.0, look_deg=lk, helmet=helm)
            worst["twist"] = max(worst["twist"], r["twist"]); worst["clear"] = min(worst["clear"], r["clear"])
            worst["wristbend"] = max(worst["wristbend"], r["wristbend"]); worst["reach"] = max(worst["reach"], r["reach"]); worst["gap"] = max(worst["gap"], r["gap"])
            if prev is not None:
                worst["jump"] = max(worst["jump"], np.linalg.norm(r["wrist"] - prev["wrist"]), np.linalg.norm(r["elbow"] - prev["elbow"]))
            prev = r
        row(f"head sweep -38..38 (1 deg) {'L' if side < 0 else 'R'} {'helmet' if helm else 'no helmet'}", worst, prev)
# 4. Idle elbow settle (-0.12..+0.08) while kneeling
for side in (-1, 1):
    for ie in (-0.12, 0.08):
        w, end = sweep(side, [1.0, 1.0], idle_elbow=ie); row(f"idle elbow {ie:+.2f} {'L' if side < 0 else 'R'}", w, end)
