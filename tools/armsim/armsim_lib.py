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

