using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // Which bot is under the player's crosshair, resolved once per debug command.
    // A ray from the eye along the look direction through world geometry and hitboxes:
    // the first thing it hits that isn't the player himself decides. A wall in front means
    // no target. A thin sphere cast (0.25 m) catches a bot the ray just misses at range,
    // but only if nothing solid is in between. Never falls back to "the nearest bot".
    internal static class Targeting
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private const float SphereRadius = 0.25f;

        private static int Mask =>
            LayersMaskController.HighPolyWithTerrainMask | (1 << LayersMaskController.HitColliderLayer) | LayersMaskController.PlayerMask;

        internal static Player BotUnderCrosshair(Player me, float range)
        {
            if (me == null) return null;
            Vector3 eye = Perception.Eye(me);
            Vector3 dir = me.LookDirection.normalized;

            Player hit = FirstAlong(me, Physics.RaycastNonAlloc(eye, dir, Hits, range, Mask, QueryTriggerInteraction.Ignore), out bool blocked);
            if (hit != null || blocked) return hit;

            hit = FirstAlong(me, Physics.SphereCastNonAlloc(eye, SphereRadius, dir, Hits, range, Mask, QueryTriggerInteraction.Ignore), out _);
            if (hit != null && !Perception.LineClear(eye, Perception.Chest(hit))) return null;
            return hit;
        }

        // Nearest hit that isn't the player himself. blocked: the nearest hit was the world.
        private static Player FirstAlong(Player me, int n, out bool blocked)
        {
            blocked = false;
            Array.Sort(Hits, 0, n, HitDistance.Instance);
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null) continue;
                Player p = c.GetComponentInParent<Player>();
                if (p == me) continue;
                if (p == null)
                {
                    blocked = true;
                    return null;
                }
                return p.IsAI ? p : null;
            }
            return null;
        }

        private sealed class HitDistance : System.Collections.Generic.IComparer<RaycastHit>
        {
            internal static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        internal static string Describe(Player p) =>
            p == null ? "none" : $"{p.Profile?.Nickname} ({p.ProfileId})";

        internal static Player Me => Singleton<GameWorld>.Instance?.MainPlayer;
    }
}
