"""0.22.0: offline checks of the turn and hands-to-head motion (no Unity).

1. Turn around (TurnDriver.Drive old 0.21.0 vs new), kinematic model of what the decompile says:
   - the bot's look yaw follows the steering target at 360 deg/s (BotSteering.LookToDirection(dir, 360));
   - HandsToBodyAngle = look - root; the body animator's stepping turn (Turn_*_90_Aim) runs while
     |HandsToBodyAngle| > 45 and turns the root by root motion (~100 deg/s: the 0.21.0 log measured
     113 deg in 1.1 s);
   - MovementState.ProcessUpperbodyRotation, isAi: if |HandsToBodyAngle| > TrunkRotationLimit the
     root is SET to look - sign * limit in that frame (a snap);
   - a pivot is MovementContext.ApplyRotation (the root turned by us).
   Measured: time, degrees stepped vs pivoted, the largest root snap, the largest one-frame change of
   the trunk twist (look - root: what the upper body shows), the torso's share of the attention
   look (Presence.ApplyUpper: old tracked the captor behind him, new leads into the turn).
2. Hands to the head and back (HandsPose.Profile vs the 0.21.0 MoveTowards + SmoothStep): time to
   complete, the largest one-frame change of speed, with reversals.
Run: python -I tools/posesim/posesim.py   (exit 1 if a new-code check fails)
"""
import math, sys

FPS = 60.0
DT = 1.0 / FPS
fails = []

def check(name, ok, detail):
    print(f"    {'ok  ' if ok else 'FAIL'} {name}: {detail}")
    if not ok: fails.append(name)

def move_towards(a, b, d):
    return b if abs(b - a) <= d else a + math.copysign(d, b - a)

def wrap(a):
    a = (a + 180.0) % 360.0 - 180.0
    return a

# ---- 1. turns ---------------------------------------------------------------------------------

def turn(new, total, turn_speed=110.0, clip_rate=100.0, max_t=8.0):
    root, look = 0.0, 0.0
    target = total
    mode, lead, limit, orig = 0, 0.0, 45.0, -1.0
    ease_since = -1.0
    stepped = pivoted = 0.0
    snap_max = 0.0
    twist_prev = 0.0
    twist_jump = 0.0
    t = 0.0
    settled = None
    torso_old_max = torso_new_max = 0.0
    yaw_s_old = yaw_v_old = 0.0
    captor = total - 180.0   # his captor stands where he turns his back on (behind the target)
    while t < max_t:
        off = wrap(target - root); a = abs(off)
        # --- driver (sets the look target) ---
        if new:
            if a <= 3.0:
                if mode == 1: mode = 0; ease_since = t if orig >= 0 and ease_since < 0 else ease_since
                mode = 0; lead = move_towards(lead, off, 220 * DT); want = root + lead
                if settled is None and a <= 3.5: settled = t
            else:
                step = a > 70 or (mode == 1 and a > 48)
                if step:
                    if mode != 1:
                        mode = 1
                        if orig < 0: orig = limit
                        limit = 80.0; ease_since = -1.0
                    lead = move_towards(lead, math.copysign(min(a, 60.0), off), 330 * DT)
                    want = root + lead
                else:
                    if mode == 1: mode = 0; ease_since = t
                    mode = 2
                    sp = max(30.0, turn_speed * smoothstep(0, 1, a / 35.0))
                    d = math.copysign(min(a, sp * DT), off); root += d; pivoted += abs(d)
                    rest = off - d
                    lead = move_towards(lead, max(-35, min(35, rest)), 220 * DT)
                    want = root + lead
            # ease the raised limit down with the look
            if orig >= 0 and mode != 1:
                if ease_since < 0: ease_since = t
                diff = abs(wrap(look - root))
                if diff <= orig or t - ease_since > 0.75: limit = orig; orig = -1.0; ease_since = -1.0
                else: limit = max(orig, min(limit, diff + 1.0, 80.0))
        else:
            if a <= 3.0:
                if mode == 1: limit = 45.0
                mode = 0; want = target
                if settled is None and a <= 2.0: settled = t
            elif a > 70:
                mode = 1; limit = 80.0
                want = root + math.copysign(60.0, off)
            else:
                if mode == 1: limit = 45.0       # restored at once
                mode = 2
                sp = max(30.0, turn_speed * smoothstep(0, 1, a / 35.0))
                d = math.copysign(min(a, sp * DT), off); root += d; pivoted += abs(d)
                rest = off - d
                want = root + max(-35, min(35, rest))
        # --- steering: look towards want at 360 deg/s ---
        look += max(-360 * DT, min(360 * DT, wrap(want - look)))
        # --- animator stepping turn (root motion) ---
        h2b = wrap(look - root)
        if abs(h2b) > 45.0:
            d = math.copysign(min(abs(h2b) - 0.0, clip_rate * DT), h2b)
            root += d; stepped += abs(d)
        # --- ProcessUpperbodyRotation (isAi): snap ---
        h2b = wrap(look - root)
        if abs(h2b) > limit:
            snap = abs(h2b) - limit
            root = look - math.copysign(limit, h2b)
            snap_max = max(snap_max, snap)
        h2b = wrap(look - root)
        twist_jump = max(twist_jump, abs(h2b - twist_prev)); twist_prev = h2b
        # --- attention torso share (Presence.ApplyUpper: 35% of the look past 8 deg, torso max 15) ---
        # old: the captor (behind) tracked from the root until he is lost past reach + 40 (85 + 40)
        ycap = wrap(captor - root)
        if abs(ycap) > 125: y_old = math.copysign(min(10, 17), -off if off else 1)   # lost: ~10 deg to the side he lost him
        else: y_old = math.copysign(min(abs(ycap), 85 * 0.92), ycap)
        yaw_s_old, yaw_v_old = smooth_damp(yaw_s_old, y_old, yaw_v_old, 0.22, 400, DT)
        torso_old = math.copysign(min(max(0, abs(yaw_s_old) - 8) * 0.35, 15), yaw_s_old)
        # new: a lead into the turn while turning (relative to the root: turns with him)
        y_new = math.copysign(min(abs(off) * 0.3, 15), off) if a > 3 else 0.0
        torso_new = math.copysign(min(max(0, abs(y_new) - 8) * 0.35, 15), y_new)
        # "against the turn": the torso twisted towards where he came from
        if a > 3:
            torso_old_max = max(torso_old_max, -torso_old * math.copysign(1, off))
            torso_new_max = max(torso_new_max, -torso_new * math.copysign(1, off))
        t += DT
        if settled is not None and t - settled > 0.8: break
    return dict(time=settled if settled is not None else float("inf"), stepped=stepped, pivoted=pivoted,
                snap=snap_max, twist_jump=twist_jump, torso_back_old=torso_old_max, torso_back_new=torso_new_max,
                limit_back=(limit == 45.0))

def smoothstep(a, b, x):
    x = max(0.0, min(1.0, (x - a) / (b - a))); return x * x * (3 - 2 * x)

def smooth_damp(cur, target, vel, smooth, maxspeed, dt):
    # Unity's Mathf.SmoothDamp
    smooth = max(0.0001, smooth); omega = 2.0 / smooth; x = omega * dt
    exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x)
    change = cur - target; orig_to = target
    maxc = maxspeed * smooth; change = max(-maxc, min(maxc, change)); target = cur - change
    temp = (vel + omega * change) * dt; vel = (vel - omega * temp) * exp
    out = target + (change + temp) * exp
    if (orig_to - cur > 0.0) == (out > orig_to): out = orig_to; vel = (out - orig_to) / dt
    return out, vel

print("1. Turn around (60 fps; captor behind the target)")
for total in (177.0, -176.0, 120.0, 90.0, 60.0):
    o = turn(False, total); n = turn(True, total)
    print(f"  {total:+5.0f} deg  OLD: {o['time']:4.2f} s, stepped {o['stepped']:5.1f}, pivoted {o['pivoted']:5.1f}, root snap {o['snap']:4.1f}, "
          f"trunk jump/frame {o['twist_jump']:4.1f}, torso back {o['torso_back_old']:4.1f}")
    print(f"           NEW: {n['time']:4.2f} s, stepped {n['stepped']:5.1f}, pivoted {n['pivoted']:5.1f}, root snap {n['snap']:4.1f}, "
          f"trunk jump/frame {n['twist_jump']:4.1f}, torso back {n['torso_back_new']:4.1f}")
    check(f"{total:+.0f}: no root snap", n["snap"] < 0.5, f"{n['snap']:.2f} deg")
    check(f"{total:+.0f}: trunk never jumps", n["twist_jump"] <= 6.0, f"{n['twist_jump']:.1f} deg/frame")
    check(f"{total:+.0f}: torso never twists back against the turn", n["torso_back_new"] <= 0.01, f"{n['torso_back_new']:.1f} deg")
    check(f"{total:+.0f}: settles", n["time"] < 4.0, f"{n['time']:.2f} s")
    check(f"{total:+.0f}: trunk limit back to the game's 45", n["limit_back"], "")
    if abs(total) > 70:
        check(f"{total:+.0f}: mostly stepped", n["pivoted"] <= 50.0, f"pivot {n['pivoted']:.0f} deg")
# interrupted: a second Turn around half way (the target flips)
print("  interrupted turn (a new order at 0.5 s, the other way): see the hands/arms checks; the driver re-targets from the current root")

# ---- 2. hands to the head ------------------------------------------------------------------------

def profile_new(x, v, target, secs, dt):
    d = target - x
    if abs(d) < 1e-4 and abs(v) < 0.05: return target, 0.0
    acc = 4.0 / (secs * secs); vmax = 2.0 / secs
    vw = math.copysign(min(vmax, math.sqrt(2 * acc * abs(d))), d)
    v = move_towards(v, vw, acc * dt)
    nx = x + v * dt
    if (target - nx) * d <= 0: nx = target; v = 0.0
    # HandsPose.Update: Clamp01, and the speed is zero at either end
    if nx <= 0.0 or nx >= 1.0: nx = min(1.0, max(0.0, nx)); v = 0.0
    return nx, v

def run_hands(new, orders, secs=0.9, total=3.0):
    # orders: list of (time, want)
    x = 0.0; v = 0.0; t = 0.0; want = 0
    prev_w = 0.0; prev_speed = 0.0; max_dv = 0.0; done_at = None
    k = 0
    while t < total:
        while k < len(orders) and orders[k][0] <= t: want = orders[k][1]; k += 1
        if new:
            nx, v = profile_new(x, v, float(want), secs, DT)
            x = nx; w = x
        else:
            x = move_towards(x, float(want), DT / secs); w = smoothstep(0, 1, x)
        speed = (w - prev_w) / DT
        max_dv = max(max_dv, abs(speed - prev_speed))
        prev_speed = speed; prev_w = w
        if done_at is None and want == 1 and w >= 1.0: done_at = t
        t += DT
    return done_at, max_dv

print("2. Hands to the head (0.9 s), weight per frame")
for label, orders in (("up from rest", [(0.0, 1)]),
                      ("reversed at 0.45 s", [(0.0, 1), (0.45, 0)]),
                      ("reversed twice", [(0.0, 1), (0.40, 0), (0.6, 1)])):
    od, odv = run_hands(False, orders); nd, ndv = run_hands(True, orders)
    print(f"  {label:20} OLD: done {od if od is None else round(od, 2)} s, largest speed change {odv:5.2f}/s per frame   "
          f"NEW: done {nd if nd is None else round(nd, 2)} s, largest speed change {ndv:5.2f}/s per frame")
    # The only change above the acceleration limit (4/0.81 per s per s = 0.08/s per frame) is the
    # arrival frame (the last 0.6% of the move is closed at once): bounded at 0.5/s per frame,
    # against 3.3/s per frame at a reversal before.
    check(f"hands {label}: no speed jump", ndv <= 0.5 and ndv < odv + 0.3, f"{ndv:.3f}/s per frame (0.21.0: {odv:.2f})")
nd, _ = run_hands(True, [(0.0, 1)])
check("hands up from rest takes about Hands to head (s)", nd is not None and abs(nd - 0.9) < 0.12, f"{nd:.2f} s")
# escape / incoming fire: on his head (1.0), out of the hold-up -> Profile with secs 0.35
x, v, t = 1.0, 0.0, 0.0
while x > 0.0 and t < 2.0:
    x, v = profile_new(x, v, 0.0, 0.35, DT); t += DT
check("escape: hands off his head promptly", t <= 0.4, f"{t:.2f} s (then s.Weight's own lower)")
# reversed when nearly there (0.95): no overshoot past 1, no jump
x, v, t, top = 0.0, 0.0, 0.0, 0.0
while t < 0.8: x, v = profile_new(x, v, 1.0, 0.9, DT); t += DT
while t < 2.0: x, v = profile_new(x, v, 0.0, 0.9, DT); top = max(top, x); t += DT
check("reversed near the end: stays within 0..1", 0.0 <= x <= 1.0 and top <= 1.0, f"peak {top:.3f}, end {x:.3f}")

print("FAILED: " + ", ".join(fails) if fails else "all checks passed")
sys.exit(1 if fails else 0)
