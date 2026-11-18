using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    internal enum Phase
    {
        HandsUp,      // standing, hands up, watching the captor for a chance
        Retreating,   // captor left or shot him: moving out of sight
        Regrouping,   // out of sight, catching breath, deciding how to rearm
        Recovering,   // walking to a gun (his own or another one)
        PickingUp,    // waiting for the pick-up transaction
        Hiding,       // nothing to fight with: crouched in cover, waits to be found
        Rearmed       // weapon drawn: arms come down and the normal AI takes over
    }

    internal sealed class SurrenderState
    {
        internal Player Bot;
        internal Player Captor;
        internal Phase Phase = Phase.HandsUp;
        internal float PhaseSince;

        // Arm pose: Progress walks to ArmsTarget, Weight is the eased value the pose uses.
        internal bool ArmsUp = true;
        internal float Progress;
        internal float Weight;
        internal float LowerSeconds;    // speed of the next lowering (fast when he goes for a weapon)

        internal float Seed;            // per-bot tremble offset
        internal float Nerve;           // 0..1, how likely he is to try something

        internal Item DroppedWeapon;    // the gun he dropped, to come back for
        internal Vector3 DropPosition;
        internal readonly HashSet<Item> Skip = new HashSet<Item>();   // guns he failed to reach or take
        internal Item TargetWeapon;     // the gun he's walking to now
        internal Vector3 TargetPoint;
        internal Vector3 CaptorLastSeen;

        internal bool ForcedOpening;    // debug keys: act now, whatever the captor is doing
        internal bool ForcedLeave;

        internal float LastHealth;
        internal float UnwatchedSince = -1f;   // when the captor stopped covering him
        internal float CaptorGoneSince = -1f;  // when the captor left / went out of sight
        internal float NextCheck;
        internal float NextMoveOrder;
        internal float RegroupUntil;

        internal void SetPhase(Phase p)
        {
            Phase = p;
            PhaseSince = Time.time;
            NextMoveOrder = 0f;
            HandsAreHighPlugin.Log.LogInfo($"{Bot?.Profile?.Nickname}: {p}");
        }

        internal void LowerArms(float seconds)
        {
            ArmsUp = false;
            LowerSeconds = seconds;
        }

        internal void RaiseArms()
        {
            ArmsUp = true;
        }
    }
}
