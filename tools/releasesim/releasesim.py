"""0.21.0: the post-captivity phase graph of SurrenderLayer, as coded, with each edge's guard.

Checks that every way out of HandsUp ends in a hand-back to the bot's own AI (Released or
Rearmed -> SurrenderRegistry.Tick) within a bounded time, and shows the 0.20.1 graph's
unbounded loop (Hiding <-> Regrouping every 8 s for a bot with nothing to fight with).

Edges are (target, guard, max seconds spent in the source before taking it). A guard names the
counter that bounds a cycle through it; an unguarded cycle is a failure. Defaults are the
config defaults (Retreat time limit 15 s, retries 3, Move to cover at most 8 s, pick-up 5 s,
recovery give-up 30 s, scramble timeout 8 s, draw hand-off 3 s, kneel rise up to ~2 s).
Run: python releasesim.py   (exit 1 on a failure)
"""
import sys

RISE = 2.0          # longest kneel rise before legs are free (Kneel styles)
ARMS = 0.6          # arms down
DRAW = 3.0          # DrawHandoffTimeout

V21 = {
    # HandsUp: the ways out (an opening, the captor leaving, being let go, a hit, incoming fire)
    "HandsUp":   [("Retreating", None, 0), ("Hiding", None, 0), ("Recovering", "recover", 0), ("Rearmed", None, 0), ("Released", None, 0)],
    # Retreating: legs (<= 3, each <= 15 s), stuck retries (<= 3) -> Hiding; leg done -> Regroup -> Decide
    "Retreating": [("Retreating", "legs", 15 + RISE), ("Hiding", None, 15 * 3 + RISE), ("Regrouping", None, 15 + RISE)],
    # Regrouping decides on the next check (or waits a frame for path budget)
    "Regrouping": [("Rearmed", None, 0.3), ("Recovering", "recover", 0.3), ("Released", None, 0.3)],
    # Recovering: arrival -> PickingUp; failure -> Regrouping (counter) / cover under fire
    "Recovering": [("PickingUp", None, 30), ("Regrouping", "recover", 30), ("Hiding", None, 30), ("Retreating", "scramble", 8), ("Rearmed", None, 8)],
    "PickingUp":  [("Rearmed", None, 5), ("Regrouping", "recover", 5), ("Hiding", None, 5), ("Retreating", "scramble", 5)],
    # Hiding: a move to cover with an end (arrived, stuck, no route, no spot, 8 s) -> Released
    "Hiding":     [("Released", None, 8 + RISE)],
    "Rearmed":    [("AI", None, DRAW + ARMS + RISE)],
    "Released":   [("AI", None, ARMS + RISE + 2.0)],   # + an unanswered hands change given up
}

V20 = {
    "HandsUp":   [("Retreating", None, 0), ("Hiding", None, 0), ("Recovering", "recover", 0), ("Rearmed", None, 0)],
    "Retreating": [("Retreating", "legs", 15), ("Hiding", None, 60), ("Regrouping", None, 15)],
    "Regrouping": [("Rearmed", None, 12), ("Recovering", None, 12), ("Hiding", None, 12)],   # 4-12 s crouched first
    "Recovering": [("PickingUp", None, 30), ("Regrouping", None, 30), ("Retreating", "scramble", 8), ("Rearmed", None, 8)],
    "PickingUp":  [("Rearmed", None, 5), ("Regrouping", None, 5), ("Retreating", "scramble", 5)],
    "Hiding":     [("Regrouping", None, 8)],   # "looks again" every 8 s, back to Hiding: forever
    "Rearmed":    [("AI", None, DRAW + ARMS)],
}

LIMITS = {"legs": 2, "recover": 2, "scramble": 1}   # extra passes each counter allows


def worst(graph, node, used, depth=0):
    """Longest time to reach AI from node; None if an unguarded cycle exists."""
    if node == "AI":
        return 0.0
    if depth > 60:
        return None
    best = 0.0
    for target, guard, secs in graph[node]:
        u = dict(used)
        if guard:
            u[guard] = u.get(guard, 0) + 1
            if u[guard] > LIMITS[guard] + 1:
                continue        # the counter refuses this edge now
        t = worst(graph, target, u, depth + 1)
        if t is None:
            return None
        best = max(best, secs + t)
    return best


ok = True
t21 = worst(V21, "HandsUp", {})
print(f"0.21.0: every path from HandsUp ends with his own AI; worst case {t21:.0f} s" if t21 is not None else "0.21.0: UNBOUNDED loop")
ok &= t21 is not None
t20 = worst(V20, "HandsUp", {})
print("0.20.1: UNBOUNDED (Hiding -> Regrouping -> Hiding ... with nothing to fight with), as seen in the 18:06 log" if t20 is None else f"0.20.1: bounded {t20:.0f} s (unexpected)")
ok &= t20 is None
# Released without a gun is reachable from every post-captivity phase that can end without one.
reach = {n for n in V21 if any(t == "Released" for t, _, _ in V21[n])}
print("phases that hand him back without a draw:", ", ".join(sorted(reach)))
ok &= {"HandsUp", "Regrouping", "Hiding"} <= reach
sys.exit(0 if ok else 1)
