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
        internal Player Captor;           // whom he gave up to (you or a bot); the threat he watches
        internal CaptorState Custody;     // a bot actively guarding him (Captors.cs), or null
        internal bool OrderByHuman = true;  // the last order came from you (notes on screen)
        internal float NextObserverScan;  // Restraint.Tick
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

        // Drop timeline (Time.realtimeSinceStartup), logged once his arms are fully up, so the
        // game's own holster/throw latency can be told apart from our timers.
        internal float TStart = -1f, TDropRequested = -1f, THandsEmpty = -1f, TThrown = -1f;
        internal float TInterrupt = -1f;        // reload fast-forwarded
        internal string HandsAtStart;           // what his hands were doing when he gave up
        internal bool TimingLogged;

        // One hands request at a time. Callbacks carry the token they were issued with and
        // do nothing if a newer request (or a new surrender) replaced it.
        internal int HandsToken;
        internal bool HandsPending;
        internal float HandsSince;

        // Ownership while held: facing only re-aimed past a hysteresis; the mover is stopped
        // once when he stands still, not every frame.
        internal Vector3 FaceTarget;
        internal bool FaceSet;
        internal bool Moving;
        internal Vector3 PathTarget;
        internal bool SainReleased;

        // Voice: one pending event, played when the speaker is free.
        internal VoiceEvent? PendingVoice;
        internal float PendingVoiceUntil;
        internal float NextSurrenderVoice;

        // Rearming.
        internal readonly HashSet<Item> Skip = new HashSet<Item>();   // guns he failed to reach or take
        internal Item TargetWeapon;     // the gun he's walking to: other surrendered bots leave it alone
        internal Vector3 TargetPoint;
        internal bool HideMoving;
        internal bool Scrambling;       // ran for his gun during an opening: sprints, no regroup first
        internal float ScrambleCooldownUntil;
        internal int HidCount;          // hide-and-look-again cycles since last caught: only the first is logged

        // What he knows about the captor. After losing sight he only uses the last sighting.
        // At a surrender it is where he last saw his threat (EnemyInfo), never a position he
        // didn't know.
        internal Vector3 CaptorLastSeen;
        internal float CaptorHiddenSince = -1f;
        internal bool CaptorSeen;           // this check: he sees his captor (Perception.SeesCaptor)

        // The surrender as an offer (0.9.0). Accepted: the captor has acknowledged it (a human came
        // within AcknowledgeDistance in sight, aimed at him a moment, or gave an order; a bot took
        // custody). Until then it lasts until OfferUntil (longer while the captor closes in), then
        // he seeks safety or reassesses. Distance alone never cancels it.
        internal bool Accepted;
        internal float OfferUntil;
        internal float ClosestDist = float.MaxValue;   // captor's closest approach seen this encounter
        internal float OfferRef = float.MaxValue;      // distance the next "he is coming for me" is measured from
        internal float CoveredSince = -1f;  // the captor has been aiming at him since (acknowledgement)

        // Follow: a failed path is retried at most once a second (not a path solve per frame).
        internal float FollowRetryAt;
        internal bool FollowToCover;        // a bot captor ordered him along into cover

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
        internal float DebugOpeningUntil;   // debug key: treated as not covered until then (this bot only)
        internal bool ForcedLeave;          // debug key: this bot only
        internal bool ForceRun;             // debug key: deterministic run
        internal bool ForceGrab;            // debug key: deterministic scramble
        internal float NextVoice;

        // Drawing a weapon he kept (Arsenal.Draw). One draw request at a time, with a token so a
        // late callback from an abandoned one is ignored. The layer keeps him (facing you, not
        // firing) until the game reports the weapon ready, then hands him to the normal AI.
        internal bool DrawPending;
        internal int DrawToken;
        internal float DrawSince;
        internal EquipmentSlot DrawSlot;
        internal bool DrawQuick;
        internal bool DrawOk;
        internal float DrawCommitUntil;     // decided to draw but the game refused: retried until then
        // Draw timeline (Time.realtimeSinceStartup), logged at the hand-off.
        internal float TDecided = -1f, TDrawAsked = -1f, TArmsDown = -1f, TDrawReady = -1f;

        // Off-cadence decisions: a decision or leave reaction that comes due between the
        // 0.25 s checks runs on that frame, once (these hold the due time already run).
        internal float RanDecideAt = -2f;
        internal float RanLeaveAt = -2f;

        // Who he is (read once per surrender: SAIN personality is a reflection call).
        internal float Reaction;            // his own reaction time, s (BotHistory, rolled once per raid)
        internal string Personality;
        internal float Fight = 1f, Flee = 1f;

        // What he has noticed. Changes in what the captor does reach his decisions only after
        // his reaction delay; periodic reassessment while an opening lasts.
        internal bool LastCovered = true;
        internal string OpeningKind;
        internal float OpeningStrength;
        internal float OpenSince = -1f;
        internal float DecideAt = -1f;      // next decision about an opening (-1: none pending)
        internal float ComplyUntil;         // warning shot or fresh order: he stays still until then
        internal float LeaveAt = -1f;       // captor gone: he reacts at this time
        internal string LeaveWhy;
        internal float ApproachRef = -1f;   // captor distance the next "coming closer" is measured from
        internal bool GunGone;              // noticed his dropped gun is no longer there
        internal float NextSlowCheck;
        internal float Injury;              // 0..1, NaturalSurrender's injury model, refreshed every second
        internal bool LegsBad;

        // Body gestures (Gestures.cs). Only one at a time; per-encounter sampling.
        internal GestureKind Gesture;
        internal float GestureStart;
        internal float GestureSign = 1f;
        internal float WigglePendingUntil = -1f;
        internal string GestureBlock;       // why a pending gesture hasn't started yet (for the skip log)
        internal float GestureCheckAt;      // start conditions (casts) are checked at most 5x a second
        internal float GestureArm;          // 0..1: how far the left arm is handed to the game's gesture
        internal bool GestureConfirmed;     // the game reported the middle finger playing
        internal float DefiantAt = -1f;     // when the one middle-finger roll of this surrender happens
        internal bool DefiantSampled;
        internal float DefiantPendingUntil = -1f;

        // Lifecycle: set by the game's death/unspawn event; nothing of ours acts on him after.
        internal bool Dead;
        internal OnPlayerDeadOrUnspawn DeathHandler;

        // Cheap repeats: cached results with a short life.
        internal bool DropReachable;
        internal float DropReachableAt = -1000f;
        internal Vector3 DropReachableFrom;
        internal float NextScavengeScan;    // after an empty weapon search, don't search again before
        internal bool HideSearchPending;    // GoHide deferred its cover search to a frame with budget
        internal object SainComponent;      // SAIN BotComponent, cached for ReleaseLayer
        internal float SettledAt = -1f;
        internal bool SettledSampled;
        internal float NextWiggleSample;

        // Speech (Voice.cs).
        internal VoicePriority PendingPriority;
        internal float PendingVoiceAt;
        internal float NextChatter;
        internal float SpokeAt = -1000f;
        internal EPhraseTrigger LastLine = EPhraseTrigger.None;
        internal float VerifyAt = -1f;
        internal TaggedClip VerifyClip;
        internal string VerifyWhat;

        // Reaction delay for one noticed change: his own reaction time, quicker with nerve,
        // slower when hurt, varied each time.
        internal float NoticeDelay()
        {
            float r = Reaction * (1.15f - 0.3f * Nerve) * (1f + 0.5f * Injury);
            return r * Random.Range(0.8f, 1.25f);
        }

        // A new surrender (first or recapture): what he noticed last time no longer applies.
        internal void ResetEncounter()
        {
            LastCovered = true;
            OpeningKind = null;
            OpenSince = -1f;
            DecideAt = -1f;
            ComplyUntil = 0f;
            LeaveAt = -1f;
            ApproachRef = -1f;
            SettledAt = -1f;
            SettledSampled = false;
            WigglePendingUntil = -1f;
            DefiantAt = -1f;
            DefiantSampled = false;
            DefiantPendingUntil = -1f;
            DrawCommitUntil = 0f;
            TDecided = -1f;
            Accepted = false;
            ClosestDist = float.MaxValue;
            OfferRef = float.MaxValue;
            CoveredSince = -1f;
            FollowToCover = false;
            OfferUntil = Time.time + Settings.Between(Settings.OfferMin, Settings.OfferMax) * (1.2f - 0.4f * Nerve);
            Gestures.Cancel(this, "new surrender");
        }

        // The captor acknowledged the surrender (logged once per encounter).
        internal void Accept(string how)
        {
            if (Accepted) return;
            Accepted = true;
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{Name}: surrender acknowledged by {Restraint.Name(Captor)} ({how})");
        }

        // quiet: routine repeats (hiding and looking again every few seconds) only go to the
        // log with Verbose logs on.
        internal void SetPhase(Phase p, string reason, bool quiet = false)
        {
            Phase old = Phase;
            Phase = p;
            if (p != Phase.HandsUp)
            {
                Gestures.Cancel(this, $"now {p}");
                DecideAt = -1f;
                LeaveAt = -1f;
                DrawCommitUntil = 0f;
            }
            PhaseSince = Time.time;
            NextMoveOrder = 0f;
            // What the bots around him see him do: going for a weapon ends every restraint (and
            // is remembered by whoever saw it); running or giving up again is re-decided.
            if (old != p)
            {
                if ((p == Phase.Rearmed && reason != "mod disabled") || p == Phase.Recovering || p == Phase.PickingUp)
                    Restraint.OnArmed(this, p == Phase.Rearmed ? "drew a weapon" : "went for a gun");
                else Restraint.OnSituationChanged(this);
            }
            Retries = 0;
            HideMoving = false;
            if (p != Phase.Recovering && p != Phase.PickingUp) Scrambling = false;
            ProgressFrom = Bot != null ? Bot.Position : Vector3.zero;
            ProgressSince = Time.time;
            if (quiet && !Settings.VerboseLogs.Value) return;
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
            Gestures.Cancel(this, "arms coming down");
            ArmsUp = false;
            LowerSeconds = seconds;
        }

        internal void RaiseArms()
        {
            ArmsUp = true;
        }
    }
}
