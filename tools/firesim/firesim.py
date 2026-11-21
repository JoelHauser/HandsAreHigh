"""0.21.0: offline check of Perception.InLineOfFire (the trigger guard used by restraint and the
scenario observer override) against the 0.20 rule it replaces (a flat 6 deg cone round the
protected man's chest at any range; restraint: max(6 deg, atan(1.1 / d))).

The geometry is a line-for-line port of Perception.OnLine / InLineOfFire. Run: python firesim.py
Every case prints old -> new and whether the new result is the intended one; exit code 1 on any
miss.
"""
import math
import sys


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def norm(a): return math.sqrt(dot(a, a))
def unit(a): n = norm(a); return mul(a, 1.0 / n)
def angle(a, b): return math.degrees(math.acos(max(-1.0, min(1.0, dot(unit(a), unit(b))))))

UP = (0.0, 1.0, 0.0)
EYE_H = 1.6   # Perception.Eye ~ head height
CHEST_H = 1.2


def on_line(eye, look, feet, max_range):
    along = dot(sub(add(feet, UP), eye), look)
    if along <= 0.2 or along > max_range:
        return False, along
    closest = add(eye, mul(look, along))
    off = sub(feet, closest)
    up = -off[1]
    if up < -0.1 or up > 1.95:
        return False, along
    flat = (off[0], 0.0, off[2])
    return norm(flat) <= 0.8 + 0.015 * along, along   # 0.28.4 (was 0.55 + 0.01)


def in_line_of_fire(shooter_feet, look, man_feet, goal_feet, max_range):
    eye = add(shooter_feet, (0.0, EYE_H, 0.0))
    look = unit(look)
    hit, along = on_line(eye, look, man_feet, max_range)
    if not hit:
        return False
    if goal_feet is not None:
        ghit, galong = on_line(eye, look, goal_feet, max_range)
        if ghit and galong + 0.75 < along:
            return False
    return True


def old_scenario(shooter_feet, look, man_feet):
    eye = add(shooter_feet, (0.0, EYE_H, 0.0))
    to = sub(add(man_feet, (0.0, CHEST_H, 0.0)), eye)
    return norm(to) <= 200 and angle(look, to) <= 6.0


def old_restraint(shooter_feet, look, man_feet):
    eye = add(shooter_feet, (0.0, EYE_H, 0.0))
    to = sub(add(man_feet, (0.0, CHEST_H, 0.0)), eye)
    d = norm(to)
    if d > 150:
        return False
    cone = max(6.0, math.degrees(math.atan2(1.1, max(d, 0.5))))
    return angle(look, to) <= cone


def aim_at(shooter_feet, target_feet, height=CHEST_H):
    eye = add(shooter_feet, (0.0, EYE_H, 0.0))
    return sub(add(target_feet, (0.0, height, 0.0)), eye)


cases = []
S = (0.0, 0.0, 0.0)

# 1. Scenario #17: the third party 40 m out aims at the captor; you (the observer) stand 6 m
#    behind the captor and 2 m to the side. Should fire (you are not on the line).
cap = (0.0, 0.0, 40.0); you = (2.0, 0.0, 46.0)
cases.append(("third party at captor, you 6 m behind + 2 m aside", in_line_of_fire(S, aim_at(S, cap), you, cap, 200), old_scenario(S, aim_at(S, cap), you), False))
# 2. Same, you straight behind the captor: the captor takes the round first. Should fire.
you2 = (0.0, 0.0, 46.0)
cases.append(("third party at captor, you straight behind him", in_line_of_fire(S, aim_at(S, cap), you2, cap, 200), old_scenario(S, aim_at(S, cap), you2), False))
# 3. You between the shooter and the captor, on the line: must block.
you3 = (0.2, 0.0, 25.0)
cases.append(("you on the line in front of the captor", in_line_of_fire(S, aim_at(S, cap), you3, cap, 200), old_scenario(S, aim_at(S, cap), you3), True))
# 4. A captor's squadmate fires at a third party 30 m away; the prisoner kneels 8 m away, 3 m
#    off the line. Should fire (0.20: 3 m at 8 m is 20 deg -> fired too; check no regression).
third = (0.0, 0.0, 30.0); pris = (3.0, 0.0, 8.0)
cases.append(("squadmate at third party, prisoner 3 m off the line at 8 m", in_line_of_fire(S, aim_at(S, third), pris, third, 150), old_restraint(S, aim_at(S, third), pris), False))
# 5. Same third party, prisoner 1.2 m off the line at 20 m: 3.4 deg, old cone blocked it.
pris5 = (1.2, 0.0, 20.0)
cases.append(("squadmate at third party, prisoner 1.2 m off the line at 20 m", in_line_of_fire(S, aim_at(S, third), pris5, third, 150), old_restraint(S, aim_at(S, third), pris5), False))
# 6. Prisoner directly in front on the line (8 m), third party behind him: must block.
pris6 = (0.1, 0.0, 8.0)
cases.append(("prisoner on the line in front of the third party", in_line_of_fire(S, aim_at(S, third), pris6, third, 150), old_restraint(S, aim_at(S, third), pris6), True))
# 7. Aimed at the prisoner's head at 3 m (restrained bot, a leak): must block.
p7 = (0.0, 0.0, 3.0)
cases.append(("aim at the prisoner's head at 3 m", in_line_of_fire(S, aim_at(S, p7, 1.7), p7, None, 150), old_restraint(S, aim_at(S, p7, 1.7), p7), True))
# 8. Aimed at his left arm / shoulder (0.45 m aside) at 3 m: must block.
shoulder = sub(add(p7, (0.45, 1.4, 0.0)), (0.0, EYE_H, 0.0))
cases.append(("aim at his shoulder 0.45 m aside at 3 m", in_line_of_fire(S, shoulder, p7, None, 150), old_restraint(S, shoulder, p7), True))
# 9. Aimed at his feet (kneeling man's knee height 0.3 m) at 5 m: must block.
p9 = (0.0, 0.0, 5.0)
cases.append(("aim at his knees at 5 m", in_line_of_fire(S, aim_at(S, p9, 0.3), p9, None, 150), old_restraint(S, aim_at(S, p9, 0.3), p9), True))
# 10. Aimed over his head at someone on a balcony above and behind (line 2.5 m over his feet at
#     his distance): should fire.
bal = (0.0, 4.0, 30.0)
cases.append(("aim at a man on a balcony over the prisoner", in_line_of_fire(S, aim_at(S, bal), p9, bal, 150), old_restraint(S, aim_at(S, bal), p9), False))
# 11. Protected man behind the shooter: never.
back = (0.0, 0.0, -5.0)
cases.append(("protected man behind the shooter", in_line_of_fire(S, (0, 0, 1), back, None, 150), False, False))
# 12. 120 m, 1.5 m off the line (0.7 deg): body width 0.8 + 1.8 = 2.6 m (0.28.4) -> blocks (spread).
far = (1.5, 0.0, 120.0)
cases.append(("120 m out, 1.5 m off the line (spread allowance)", in_line_of_fire(S, (0, 0, 1), far, None, 200), old_scenario(S, (0, 0, 1), far), True))
# 13. 0.28.4, the 0.28.3 raid (DaddyAL): a captor fights a third party 30 m out; his prisoner stands
#     10 m out, 0.75 m off the line (rounds passed him at 0.9 m, then hit him). Must block: the old
#     0.55 + 0.01/m box (0.65 m at 10 m) let it through.
p13 = (0.75, 0.0, 10.0)
def old_box(shooter_feet, look, man_feet, goal_feet):
    eye = add(shooter_feet, (0.0, EYE_H, 0.0)); look = unit(look)
    along = dot(sub(add(man_feet, UP), eye), look)
    off = sub(man_feet, add(eye, mul(look, along))); flat = (off[0], 0.0, off[2])
    return 0.2 < along <= 150 and norm(flat) <= 0.55 + 0.01 * along
cases.append(("captor at third party, prisoner 0.75 m off the line at 10 m (0.28.3 raid)", in_line_of_fire(S, aim_at(S, third), p13, third, 150), old_box(S, aim_at(S, third), p13, third), True))

bad = 0
for name, new, old, want in cases:
    ok = new == want
    bad += 0 if ok else 1
    print(f"{'ok  ' if ok else 'MISS'} {name}: old {'block' if old else 'fire '} -> new {'block' if new else 'fire '} (want {'block' if want else 'fire'})")
print(f"{len(cases) - bad}/{len(cases)} as intended")
sys.exit(1 if bad else 0)
