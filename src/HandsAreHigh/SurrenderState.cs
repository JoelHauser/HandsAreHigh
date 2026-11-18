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

    // A standing order from the captor. Separate from the phase: a bot following you is
    // still in HandsUp and still watching for an opening.
    internal enum Order { None, Follow, Kneel }

    internal sealed class SurrenderState
    {
        internal Player Bot;
        internal string Name;
        internal Player Captor;
        internal Phase Phase = Phase.HandsUp;
        internal float PhaseSince;
        internal Order Order;

        // Arm pose: Progress walks towards ArmsUp, Weight is the eased value the pose uses.
        internal bool ArmsUp = true;
        internal float Progress;
        internal float Weight;
        internal float LowerSeconds;

        internal float Seed;            // per-bot tremble offset
        internal float RolledNerve;     // 0..1, rolled once per raid (SurrenderRegistry history)
        internal float Nerve => Settings.EffectiveNerve(RolledNerve);   // debug override applies live

        // The drop when his hands go up.
        internal bool DropPending;
        internal int DropAttempts;
        internal float DropSince;
        internal Item DroppedWeapon;    // the gun he dropped, to come back for
        internal Vector3 DropPosition;

        // Rearming.
        internal readonly HashSet<Item> Skip = new HashSet<Item>();   // guns he failed to reach or take
        internal Item TargetWeapon;     // the gun he's walking to: other surrendered bots leave it alone
        internal Vector3 TargetPoint;
        internal bool HideMoving;

        // What he knows about the captor. After losing sight he only uses the last sighting.
        internal Vector3 CaptorLastSeen;
        internal float CaptorHiddenSince = -1f;
        internal float UnwatchedSince = -1f;
        internal float NextBreakRoll;

        // Set by Player.BeingHitAction (bullets, melee, explosions; never bleeding).
        internal bool WasHit;
        internal System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float> HitHandler;

        internal Vector3 LastPosition;
        internal float NextCheck;
        internal float NextMoveOrder;
        internal float RegroupUntil;
        internal float GraceUntil;

        // Being caught again: the same player must aim at him for a moment.
        internal Player AimedBy;
        internal float AimedSince;

        // Movement progress, so he never runs into a closed door forever.
        internal Vector3 ProgressFrom;
        internal float ProgressSince;
        internal int Retries;

        // Orders that drop items go one item at a time.
        internal readonly Queue<KeyValuePair<EquipmentSlot, Item>> DropQueue = new Queue<KeyValuePair<EquipmentSlot, Item>>();
        internal readonly List<KeyValuePair<EquipmentSlot, Item>> Ordered = new List<KeyValuePair<EquipmentSlot, Item>>();
        internal string OrderName;
        internal float NextDropAt;
        internal float ReportAt = -1f;

        internal bool Suspended;        // asleep (ORBIT Ghost Mode) or otherwise inactive
        internal bool ForcedOpening;    // debug keys
        internal bool ForcedLeave;
        internal float NextVoice;

        internal void SetPhase(Phase p, string reason)
        {
            Phase old = Phase;
            Phase = p;
            PhaseSince = Time.time;
            NextMoveOrder = 0f;
            Retries = 0;
            HideMoving = false;
            ProgressFrom = Bot != null ? Bot.Position : Vector3.zero;
            ProgressSince = Time.time;
            HandsAreHighPlugin.Log.LogInfo(old == p
                ? $"{Name}: {p} ({reason})"
                : $"{Name}: {old} -> {p} ({reason})");
        }

        // Runs: arms down (fast if hurt), grace before he can be caught, orders cancelled.
        internal void BeginRetreat(string reason, bool emergency)
        {
            LowerArms(emergency ? Settings.EmergencyLowerSeconds.Value : Settings.LowerSeconds.Value);
            GraceUntil = Time.time + Settings.Grace.Value;
            Order = Order.None;
            DropQueue.Clear();
            ReportAt = -1f;
            SetPhase(Phase.Retreating, reason);
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
