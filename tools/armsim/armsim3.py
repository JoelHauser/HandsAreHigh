# 0.22.0: the hands-up pose (ArmPose.Apply) and the hands-behind-head move (Kneel.ApplyHands)
# from it, offline. Same conventions and helpers as armsim2.py (x right, y up, z forward).
#
# 1. Hands-up geometry, old defaults vs new, with the torso leaning forward 0..30 deg (EFT's idle
#    stance leans; kneeling leans more). Old: the arm frame's "up" is ribcage->head (tips with the
#    lean). New: "up" is blended towards gravity by Forearms upright (ArmPose.ArmFrame).
# 2. Hands-behind-head transitions from the new start: clearance round the skull (+ helmet),
#    elbow flips, per-frame steps, a reversal mid-move, head turned.
# Run: python -I tools/armsim/armsim3.py
import math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from armsim_lib import *

PIV = np.array([0, 1.0, 0])             # the lean pivots at the hips
SH0 = lambda side: np.array([0.18 * side, 1.45, 0.0])
HEAD0 = np.array([0, 1.62, 0.02])        # head bone (top of the neck)
ABOVE = 0.08                              # Hands above neck (skull centre above the head bone)

OLD = dict(raise_=12, ahead=25, ffwd=12, fin=8, upright=0.0)
NEW = dict(raise_=22, ahead=20, ffwd=4, fin=15, upright=0.8)

def lean_rot(deg): return rot(R, deg)

def arm_frame(Lr, upright):
    r = Lr @ R; ut = Lr @ U
    ug = n(U - r * np.dot(U, r))          # gravity's up, square to the shoulder line
    u = slerpv(ut, ug, upright)
    return r, u, cross(r, u)

def hands_up(side, P, lean=0.0):
    Lr = lean_rot(lean)
    sh = PIV + Lr @ (SH0(side) - PIV)
    head = PIV + Lr @ (HEAD0 - PIV)
    skull = head + (Lr @ U) * ABOVE
    r, u, f = arm_frame(Lr, P["upright"])
    out = r * side
    ra, ah = math.radians(P["raise_"]), math.radians(P["ahead"])
    ud = n((out * math.cos(ah) + f * math.sin(ah)) * math.cos(ra) + u * math.sin(ra))
    fd = n(rot(f, P["fin"] * side) @ (rot(r, P["ffwd"]) @ u))   # as armsim2: +side tips the hand in towards the head
    el = sh + ud * A; wr = el + fd * C
    palm = wr + fd * PALMC
    return dict(sh=sh, el=el, wr=wr, palm=palm, skull=skull, ud=ud, fd=fd, r=r, u=u, f=f, Lr=Lr)

def metrics(side, P, lean, helmet=False):
    q = hands_up(side, P, lean)
    top = q["skull"][1] + HEADR + (0.035 if helmet else 0)
    fore_v = angle(q["fd"], U)
    flex = 180 - angle(q["ud"], q["fd"])                 # 180 = straight arm
    lat = abs(q["palm"][0] - q["skull"][0])
    # closest approach of the forearm segment (wrist..palm tip) to the skull sphere
    tip = q["wr"] + q["fd"] * 0.17
    seg = [q["el"] + (tip - q["el"]) * t for t in np.linspace(0, 1, 40)]
    clear = min(np.linalg.norm(p - q["skull"]) for p in seg) - HEADR - (0.035 if helmet else 0)
    return dict(above_top=q["palm"][1] - top, above_sh=q["palm"][1] - q["sh"][1], fore_from_vertical=fore_v,
                elbow=flex, elbow_above_sh=q["el"][1] - q["sh"][1], lat=lat, clear=clear,
                fwd=q["palm"][2] - q["sh"][2])

print("1. Hands-up pose (right arm; left mirrors)")
for name, P in (("OLD", OLD), ("NEW", NEW)):
    for lean in (0, 10, 20, 30):
        for helm in (False, True):
            m = metrics(1, P, lean, helm)
            print(f"  {name} lean {lean:2d}{' helmet' if helm else '       '}: palm {m['above_top']*100:+5.1f} cm vs head top, "
                  f"{m['above_sh']*100:5.1f} cm above shoulder, {m['fwd']*100:+5.1f} cm fwd; forearm {m['fore_from_vertical']:4.1f} deg off vertical; "
                  f"elbow angle {m['elbow']:5.1f}; elbow {m['elbow_above_sh']*100:+5.1f} cm vs shoulder; hand {m['lat']*100:4.1f} cm from head centre; "
                  f"arm clear of head {m['clear']*100:4.1f} cm")

# ---- 2. behind the head, from the new hands-up arm (mirrors Kneel.ApplyHands) ----------------
def behind(side, h, P, lean=0.0, look_deg=0.0, helmet=False):
    q = hands_up(side, P, lean)
    Lr = q["Lr"]; Lk = rot(Lr @ U, look_deg)
    upL, rL, fL = Lk @ (Lr @ U), Lk @ (Lr @ R), Lk @ (Lr @ F)
    skull = q["skull"]; hx = 0.035 if helmet else 0.0
    minR = HEADR + hx + 0.02
    sh, el, pa = q["sh"], q["el"], q["wr"]
    palmAt = skull - fL * (0.11 + hx) + rL * side * 0.05
    face = skull - palmAt
    fing = n(-rL * side + upL * 0.25); fing = n(fing - n(face) * np.dot(fing, n(face)))
    wristAt = palmAt - fing * PALMC
    p0 = pa - skull; p2 = wristAt - skull
    lat = max(abs(np.dot(p0, rL)), 0.22); p1 = rL * side * lat + upL * 0.06 - fL * 0.02
    u = 1 - h; off = p0 * u * u + p1 * 2 * h * u + p2 * h * h
    if np.linalg.norm(off) < minR: off = n(off) * minR
    pos = skull + off
    up_t, fwd_t = Lr @ U, Lr @ F
    bendNow = perp(el - sh, pa - sh, (Lr @ R) * side)
    bendOut = n(rL * side * 1.0 + fwd_t * 0.35 + up_t * 0.25)
    elbow, wrist = twobone(sh, pos, A, C, slerpv(bendNow, bendOut, h))
    # palm centre and fingertips along the forearm early, round the skull late (as armsim2)
    fore = wrist - elbow
    faceN = n(skull - wrist); fw = n(-rL * side + upL * 0.25); fw = n(fw - faceN * np.dot(fw, faceN))
    fw = slerpv(n(fore), fw, ss(0.35, 1, h))
    pcen = wrist + fw * PALMC; tip = wrist + fw * 0.17
    seg = [elbow + (wrist - elbow) * t for t in np.linspace(0, 1, 20)] + [pcen, tip]
    clear = min(np.linalg.norm(p - skull) for p in seg) - (HEADR + hx)
    # in front of the face? (a hand passing the face is what reads as clipping through it)
    face_pass = np.dot(pcen - skull, fL) > 0.02 and abs(np.dot(pcen - skull, rL)) < 0.12
    return dict(elbow=elbow, wrist=wrist, sh=sh, clear=clear, face=face_pass,
                flex=180 - angle(elbow - sh, wrist - elbow), reach=np.linalg.norm(pos - sh) / (A + C))

def sweep(side, hs, P, **kw):
    w = dict(jump=0, flips=0, clear=9, face=0, flexmin=180, flexmax=0, reach=0)
    prev = None
    for h in hs:
        r = behind(side, h, P, **kw)
        w["clear"] = min(w["clear"], r["clear"]); w["face"] += int(r["face"]); w["reach"] = max(w["reach"], r["reach"])
        w["flexmin"] = min(w["flexmin"], r["flex"]); w["flexmax"] = max(w["flexmax"], r["flex"])
        if prev is not None:
            w["jump"] = max(w["jump"], np.linalg.norm(r["wrist"] - prev["wrist"]), np.linalg.norm(r["elbow"] - prev["elbow"]))
            a = r["sh"]
            bp = perp(prev["elbow"] - a, prev["wrist"] - a, R); bn = perp(r["elbow"] - a, r["wrist"] - a, R)
            if angle(bp, bn) > 30: w["flips"] += 1
        prev = r
    return w

def row(label, w):
    print(f"  {label:46} clear >= {w['clear']*100:4.1f} cm  past the face {w['face']}  flips {w['flips']}  "
          f"step <= {w['jump']*100:4.2f} cm  elbow angle {w['flexmin']:5.1f}..{w['flexmax']:5.1f}  reach <= {w['reach']:.2f}")

print("2. Hands up -> behind the head (0.9 s at 60 fps = 54 frames; eased)")
frames = 54
ease = [ss(0, 1, k / frames) for k in range(frames + 1)]
for P, name in ((OLD, "OLD"), (NEW, "NEW")):
    for side in (-1, 1):
        for lean in (0, 25):
            for helm in (False, True):
                row(f"{name} {'L' if side < 0 else 'R'} lean {lean:2d} {'helmet' if helm else 'bare  '}", sweep(side, ease, P, lean=lean, helmet=helm))
print("3. Reversal at 0.5 and again at 0.2 (velocity-continuous profile is the code's job; here: same path back)")
hs = [k / 60 for k in range(31)] + [0.5 - k / 60 for k in range(1, 19)] + [0.2 + k / 60 for k in range(1, 49)]
for side in (-1, 1):
    row(f"NEW reversals {'L' if side < 0 else 'R'}", sweep(side, hs, NEW, helmet=True))
print("4. Head turned while the hands travel (look -40, +40)")
for side in (-1, 1):
    for lk in (-40, 40):
        row(f"NEW look {lk:+d} {'L' if side < 0 else 'R'} helmet", sweep(side, ease, NEW, look_deg=lk, helmet=True))
