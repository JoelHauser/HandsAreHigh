# 0.25.0: kneel variants (src/Mercy/KneelVariant.cs + Kneel.ApplyBody / SolveLeg end pose).
# Side view (forward x, up y), one leg, rig sizes from the log ("Kneel rig: thigh 0.456 m, shin
# 0.460 m, foot 0.155 m"), defaults KneeHeight 0.09, thigh angle 8 (+-2 per-bot), hips forward 0,
# ToeHeight 0.025, ToeTuck 15 deg. Checks per variant, on flat ground and on the knee/foot steps
# each allows (settled: 0.05 m, the others: the kneel's own 0.12 m):
#   - the knee lands on Layout's point (hip-to-knee = thigh length): no knee slides between variants
#   - knee flexion within 80-155 deg (deep kneeling; a knee allows ~155-160)
#   - hip flexion (trunk vs thigh) 0-100 deg
#   - settled: the hip joint above the heel, the buttock (0.10 m below/behind the hip joint) not
#     below the top of the heel (no sitting through the feet)
#   - blends (upright -> variant, variant -> rise-ready): the hip path is continuous (max step per
#     1/60 s at the blend's peak speed) and the knee point never moves
import math
THIGH, SHIN, FOOT = 0.456, 0.460, 0.155
KH, TOE_H, TUCK = 0.09, 0.025, math.radians(15)
ANKLE_H = TOE_H + FOOT * math.cos(TUCK)
BASE_LEAN = 4.0

def variant(kind, base_deg, settled_deg=46.0, tense_lean=10.0):
    if kind == "settled": return max(0.0, settled_deg - base_deg), 5.0
    if kind == "tense": return -min(6.0, max(0.0, base_deg - 2.0)), tense_lean
    if kind == "rise-ready": return max(-min(6.0, max(0.0, base_deg - 2.0)), -4.0), 12.0
    return 0.0, 0.0

def pose(base_deg, d_thigh, lean, gk=0.0, gf=0.0):
    tb = math.radians(base_deg); tv = math.radians(base_deg + d_thigh)
    knee = (THIGH * math.sin(tb), gk + KH)                      # Layout (base tilt)
    hip = (THIGH * (math.sin(tb) - math.sin(tv)), gk + KH + THIGH * math.cos(tv))
    dy = max(-0.8 * SHIN, min(0.8 * SHIN, (gf + ANKLE_H) - (gk + KH)))
    shin = (-math.sqrt(SHIN**2 - dy**2), dy)
    ankle = (knee[0] + shin[0], knee[1] + shin[1])
    heel_top = ankle[1] + 0.03                                  # heel pad above the ankle joint, tucked toes
    trunk = (math.sin(math.radians(BASE_LEAN + lean)), math.cos(math.radians(BASE_LEAN + lean)))
    th = (knee[0] - hip[0], knee[1] - hip[1]); L = math.hypot(*th)
    sv = (shin[0] / SHIN, shin[1] / SHIN); tv_ = (th[0] / L, th[1] / L)
    # knee flexion: the angle between the thigh (hip->knee) and shin (knee->ankle) directions (0 = straight)
    knee_flex = math.degrees(math.acos(max(-1, min(1, tv_[0] * sv[0] + tv_[1] * sv[1]))))
    # hip flexion: 180 - the angle between the trunk (up) and the thigh (down): 0 = standing straight
    hip_flex = 180 - math.degrees(math.acos(max(-1, min(1, tv_[0] * trunk[0] + tv_[1] * trunk[1]))))
    return dict(hip=hip, knee=knee, ankle=ankle, L=L, knee_flex=knee_flex, hip_flex=hip_flex, heel_top=heel_top)

ok = True
for base in (6.0, 8.0, 10.0):
    for kind, steps in (("upright", (0.0, 0.12, -0.12)), ("tense", (0.0, 0.12, -0.12)), ("settled", (0.0, 0.05, -0.05)), ("rise-ready", (0.0, 0.05))):
        d, lean = variant(kind, base)
        for st in steps:
            p = pose(base, d, lean, gk=0.0, gf=st)
            bad = []
            if abs(p["L"] - THIGH) > 1e-6: bad.append("knee off Layout")
            if not (80 <= p["knee_flex"] <= 155): bad.append("knee %.0f" % p["knee_flex"])
            if not (0 <= p["hip_flex"] <= 100): bad.append("hip %.0f" % p["hip_flex"])
            if kind == "settled":
                butt = p["hip"][1] - 0.10
                if butt < p["heel_top"] - 1e-6: bad.append("buttock %.2f below heel %.2f" % (butt, p["heel_top"]))
                if p["hip"][0] < p["ankle"][0]: bad.append("hip behind the heel")
            ok &= not bad
            print("base %4.1f %-10s step %+.2f: hip (%+.2f, %.2f) knee flex %5.1f hip flex %5.1f%s" % (
                base, kind, st, p["hip"][0], p["hip"][1], p["knee_flex"], p["hip_flex"],
                ("  heel top %.2f, buttock %.2f" % (p["heel_top"], p["hip"][1] - 0.10)) if kind == "settled" else "") + ("  FAIL " + ", ".join(bad) if bad else ""))

# Blends: smoothstep over the blend time; the hip's largest step per 1/60 s (and the knee never moves).
def blend(a, b, secs, base=8.0):
    da, la = variant(a, base); db, lb = variant(b, base)
    prev = None; worst = 0; knee = None
    n = int(secs * 60)
    for i in range(n + 1):
        t = i / n; e = t * t * (3 - 2 * t)
        p = pose(base, da + (db - da) * e, la + (lb - la) * e)
        if prev: worst = max(worst, math.hypot(p["hip"][0] - prev[0], p["hip"][1] - prev[1]))
        if knee and math.hypot(p["knee"][0] - knee[0], p["knee"][1] - knee[1]) > 1e-9: return None
        prev = p["hip"]; knee = p["knee"]
    return worst
#   (urgent rise-ready shift: <= 30 mm per frame, ~1.8 m/s at its peak; every other blend <= 20 mm)
for a, b, secs, lim in (("upright", "settled", 1.4, 0.02), ("upright", "tense", 0.7, 0.02), ("settled", "tense", 1.6, 0.02),
                        ("settled", "rise-ready", 0.6, 0.02), ("settled", "rise-ready", 0.4, 0.03), ("settled", "rise-ready", 0.3, 0.03)):
    w = blend(a, b, secs)
    flag = "" if w is not None and w < lim else "  FAIL"
    ok &= flag == ""
    print("blend %-8s -> %-10s in %.2f s: hip step <= %.1f mm per 1/60 s, knee fixed%s" % (a, b, secs, (w or 0) * 1000, flag))
print("PASS" if ok else "FAIL")
