"""tools/natsim (0.19.0): Monte Carlo of Mercy's natural-surrender model (NaturalSurrender.Evaluate) against a
crude model of an ordinary firefight with the player. Compares the 0.18.0 defaults with the 0.19.0
proposal. Not a game simulation: it answers "given the formula and its timing, how often can a bot
that is being fought ever give up before he dies or the fight ends?"
"""
import random, math, sys

PARTS = {"head": 35, "chest": 85, "stomach": 70, "larm": 60, "rarm": 60, "lleg": 65, "rleg": 65}
HITP = [("head", .08), ("chest", .32), ("stomach", .20), ("larm", .10), ("rarm", .10), ("lleg", .10), ("rleg", .10)]

def pick_part(r):
    x = r.random(); acc = 0
    for p, w in HITP:
        acc += w
        if x < acc: return p
    return "rleg"

class Cfg:
    def __init__(s, **kw):
        s.__dict__.update(kw)

OLD = Cfg(eval=5.0, prompt_delay=0.5, prompt_gap=1.5, hit_w=1.0, full_shots=6, half_life=4.0,
          inj_thr=0.35, vital=0.4, limb=0.45, stomach=0.6, heavy=0.5, light=0.25,
          w_inj=0.45, w_sup=0.40, req=2, scav_thr=0.4, pmc_thr=0.55, curve=0.3,
          scav_base=0.35, scav_max=0.5, pmc_base=0.12, pmc_max=0.25, nerve_res=0.85,
          severe=0.6, severe_p=0.6, severe_boost=2.0, pinned=0.8, pinned_p=0.5,
          scav_level_counts=True, exp_comp=0.35)

NEW = Cfg(eval=3.0, prompt_delay=0.25, prompt_gap=0.8, hit_w=2.0, full_shots=6, half_life=4.0,
          inj_thr=0.6, vital=0.6, limb=0.55, stomach=0.65, heavy=0.55, light=0.3,
          w_inj=0.45, w_sup=0.40, req=2, scav_thr=0.4, pmc_thr=0.5, curve=0.3,
          scav_base=0.35, scav_max=0.5, pmc_base=0.12, pmc_max=0.25, nerve_res=0.85,
          severe=0.6, severe_p=0.6, severe_boost=2.0, pinned=0.8, pinned_p=0.5,
          scav_level_counts=False, exp_comp=0.35)

def injury(c, hp, bleed):
    total = sum(hp.values()) / 440.0
    worst = 0.0
    if total < c.inj_thr: worst = max(worst, 0.3 + 0.7 * (c.inj_thr - total) / c.inj_thr)
    for p in ("head", "chest"):
        f = hp[p] / PARTS[p]
        if f < c.vital: worst = max(worst, 0.4 + 0.6 * (c.vital - f) / c.vital)
    for p in ("larm", "rarm", "lleg", "rleg"):
        if hp[p] <= 0: worst = max(worst, c.limb)
    if hp["stomach"] <= 0: worst = max(worst, c.stomach)
    if bleed == 2: worst = max(worst, c.heavy)
    elif bleed == 1: worst = max(worst, c.light)
    return min(1.0, worst)

def encounter(c, r, pmc):
    hp = dict(PARTS)
    bleed = 0
    armour = r.random() < (0.95 if pmc else 0.3)
    nerve = r.random()
    pers = r.uniform(0.75, 1.25)
    kit = 1.0 + 0.25 * min(1.0, r.uniform(50e3, 600e3) / 250e3) if not pmc else 1.25
    if pmc: exp = 0.6 * r.uniform(0.2, 1.0) + 0.4 * 0.33
    else: exp = (0.6 * r.uniform(0.9, 1.0) + 0.4 * 0.33) if c.scav_level_counts else 0.4 * 0.33
    exp_mult = 1 - c.exp_comp * exp
    armour_dull = 1 - 0.3 * (0.66 if armour else 0.0)
    end = r.uniform(*getattr(c, "fight", (8, 40)))
    t = 0.0; dt = 0.05
    fire = 0.0; fire_at = -99
    next_eval = r.uniform(0, c.eval); prompt_at = -99
    exposed = True; window_end = r.uniform(1.5, 3.0)
    rolls = 0
    while t < end:
        t += dt
        if t >= window_end:
            exposed = not exposed
            window_end = t + (r.uniform(1.5, 3.0) if exposed else r.uniform(3, 8))
        hit = exposed and r.random() < getattr(c, "hit_rate", 1.0) * dt
        near = r.random() < (1.5 if exposed else 0.4) * dt
        def record(w):
            nonlocal fire, fire_at, next_eval, prompt_at
            if t - fire_at < 0.1: return
            fire = min(40, fire * 0.5 ** ((t - fire_at) / c.half_life) + w) if fire > 0 else w
            fire_at = t
            if t - prompt_at >= c.prompt_gap:
                prompt_at = t
                next_eval = min(next_eval, t + c.prompt_delay)
        if hit:
            p = pick_part(r)
            dmg = 55.0 * (0.35 if armour and p in ("chest", "stomach") else 1.0)
            if hp[p] > 0: hp[p] -= dmg
            else:
                others = [q for q in hp if hp[q] > 0]
                for q in others: hp[q] -= dmg * 0.7 / len(others)
            if p != "head":
                x = r.random()
                if x < 0.15: bleed = 2
                elif x < 0.45 and bleed == 0: bleed = 1
            if hp["head"] <= 0 or hp["chest"] <= 0: return "dead", rolls
            if exposed and r.random() < 0.5: exposed = False; window_end = t + r.uniform(3, 8)
            record(c.hit_w)
        elif near:
            record(r.uniform(0.5, 1.0))
        if bleed: hp["chest"] -= (0.6 if bleed == 2 else 0.25) * dt
        if t < next_eval: continue
        next_eval = t + c.eval * r.uniform(0.8, 1.2)
        inj = injury(c, hp, bleed)
        shots = fire * 0.5 ** ((t - fire_at) / c.half_life)
        raw = min(1.0, shots / c.full_shots)
        sup = raw * armour_dull
        reasons = (inj >= 0.2) + (sup >= 0.2)
        pressure = min(1.0, c.w_inj * inj + c.w_sup * sup)
        severe = inj >= c.severe; pinned = raw >= c.pinned
        if severe: pressure = max(pressure, c.severe_p)
        if pinned: pressure = max(pressure, c.pinned_p)
        thr = c.pmc_thr if pmc else c.scav_thr
        elig = severe or pinned or (reasons >= c.req and pressure >= thr)
        if not elig: continue
        base, mx = (c.pmc_base, c.pmc_max) if pmc else (c.scav_base, c.scav_max)
        curve = max(0.0, min(1.0, (pressure - c.curve) / max(0.05, 1 - c.curve)))
        chance = min(mx, base * curve * (1 - c.nerve_res * nerve) * pers * kit * exp_mult * (c.severe_boost if severe else 1.0))
        rolls += 1
        if r.random() < chance: return "surrender", rolls
    return "survived", rolls

def run(c, pmc, n=20000, seed=1):
    r = random.Random(seed)
    out = {"dead": 0, "surrender": 0, "survived": 0}
    rolled = 0
    for _ in range(n):
        o, k = encounter(c, r, pmc)
        out[o] += 1
        rolled += k > 0
    return {k: v / n for k, v in out.items()}, rolled / n

if __name__ == "__main__":
    import copy
    # slow: a drawn-out fight (8-40 s, 1 hit/s while exposed); fast: how the player usually fights
    # (3-15 s, 2 hits/s). Real raids sit between; the fast column is the honest one.
    for name, base in (("0.18.0", OLD), ("0.19.0", NEW)):
        for label, fight in (("slow", dict(fight=(8, 40), hit_rate=1.0)), ("fast", dict(fight=(3, 15), hit_rate=2.0))):
            c = copy.copy(base); c.__dict__.update(fight)
            sc, _ = run(c, False, n=10000); pm, _ = run(c, True, n=10000)
            print(f"{name} {label}: Scav surrender {sc['surrender']:.3f} (died {sc['dead']:.2f}), PMC surrender {pm['surrender']:.3f} (died {pm['dead']:.2f})")
