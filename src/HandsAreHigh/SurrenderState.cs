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
        Hiding,       // 0.21.0: moving to cover (a hidden spot); on arrival his own AI has him
        Rearmed,      // weapon drawn: arms come down and the normal AI takes over
        Released      // 0.21.0: free, unarmed or not: arms down, legs free, then his own AI takes over
    }

    // A standing order from the captor. Separate from the phase: a bot following you is
    // still in HandsUp and still watching for an opening.
    // 0.23.0 StepBack: a captor asked him to step off the gear at his feet (one short walk,
    // hands up, facing his captor; CaptorLoot.StandPoint).
    internal enum Order { None, Follow, Kneel, StepBack }

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
        internal bool HideUrgent;           // 0.14.0: slipping into cover during an escape: sprints there
        internal float UrgentSince;         // 0.14.0: the current urgent move (MoveDiag)
        internal Vector3 UrgentFrom;
        internal bool UrgentLogged;
        internal int UrgentPath;
        internal bool Scrambling;       // ran for his gun during an opening: sprints, no regroup first
        internal float ScrambleCooldownUntil;

        // What he knows about the captor. After losing sight he only uses the last sighting.
        // At a surrender it is where he last saw his threat (EnemyInfo), never a position he
        // didn't know.
        internal Vector3 CaptorLastSeen;
        internal float CaptorHiddenSince = -1f;
        internal bool CaptorSeen;           // this check: a clear line to his captor (presence: he knows he's there)
        // 0.15.0: the captor inside what his eyes can reach (gaze limits from his planted body when
        // kneeling). False for a man behind a kneeling prisoner: present, heard, but not watched;
        // the prisoner can't tell where that gun points.
        internal bool CaptorInView = true;
        // 0.15.0: who gave the last order (logged; never a look target by itself). Captor is custody.
        internal Player OrderIssuer;

        // 0.23.0: the encounter's speech floor (Encounter.FloorFree / TookFloor): who spoke last
        // among the prisoner, his captor and the covering squadmate, and until when it is his.
        internal Player FloorBy;
        internal float FloorUntil;
        // 0.23.0: being left (SurrenderLogic.Abandonment), from what he can see only.
        internal float LeftEvidence;          // seconds' worth of "he is leaving" cues
        internal float LeftDecideAt = -1f;    // he believes it at this time (his assessment)
        internal string LeftWhy;              // the cue he took it from
        internal float LeftSince = -1f;       // custody (or the last cue reset) ended at
        internal bool LeftCoveredLogged, LeftCancelLogged;
        internal string CoverLogWho;          // Reactions.Covered: the squad cover last logged (no repeats)
        internal float LeftLastCheck = -1f;
        // 0.23.0: the captor's equipment demand (GearNeeds): the one item asked for, and where to step.
        internal EquipmentSlot DemandSlot;
        internal Item DemandItem;
        internal Vector3 StepBackTo;
        internal float StepBackUntil;
        // 0.15.0: resistance decided but not yet visible to the bots around him.
        internal string ArmedPending;
        internal float ArmedPendingSince;
        // 0.20.0: when his resistance last showed (Restraint.OnArmed): a covering squadmate who never
        // saw it takes his squad's word after a moment (Overwatch). Squadmates of his captor he has
        // seen (Reactions.SquadCovers): only those can be "behind him, assumed".
        internal float ArmedShownAt = -1f;
        internal readonly HashSet<Player> SeenSquad = new HashSet<Player>();

        // 0.16.0: where his hands are, an order of its own (posture is Order / Kneel). HeadHands is
        // the eased 0..1 the pose uses (HandsPose.Update); HandsOrdered: an explicit hands order was
        // given this surrender (a later Get down then keeps them where they are).
        internal bool HandsOnHead;
        internal bool HandsOrdered;
        internal float HeadHands;
        internal bool HeadHandsLogged;
        internal bool HeadHelmet;

        // 0.16.0: Turn around (Facing.cs). TurnDir is fixed when the order is taken (away from where
        // the issuer stood then); he turns to it, then stays facing it until a new facing order,
        // a follow, or the end of this surrender. TurnReKneel: he got up to turn and kneels again.
        internal bool Turning, TurnedAway, TurnReKneel;
        internal Vector3 TurnDir;
        internal float TurnSince = -1f;
        internal Player TurnBy;
        // 0.17.0: TurnDriver (the game's stepping turn or a pivot). TrunkLimitOrig >= 0 while his
        // TrunkRotationLimit is raised for a step turn (always restored: TurnDriver.Stop).
        internal int DriveMode, DriveFrame = -10;
        internal float DriveSince, DriveYaw0, TrunkLimitOrig = -1f;
        internal Vector3 DriveLook;
        internal string DriveWhat;
        internal bool NoStepTurn;
        // 0.22.0: the look's lead over the root (signed deg), eased (never jumps between step and
        // pivot); the signed degrees still to turn and what the turn is for, this frame (Presence
        // leads the head into a turn instead of holding it on the old direction); when the raised
        // trunk limit started coming back down (it ratchets down with the look, no root snap).
        internal float DriveLead, DriveOff, LimitEaseSince = -1f;
        internal string DriveFor;
        // 0.22.0: the hands-to-head move's speed (HandsPose: velocity carried through reversals).
        internal float HeadHandsVel;
        internal bool PoseLogged;   // the hands-up pose measured once per surrender (ArmPose)

        // 0.16.0: what the last Drop your weapons did (Compliance.cs): what he really dropped,
        // what he kept back on purpose (never anything he doesn't carry), and how many times the
        // demand was made. Disarmed only when his weapon slots really are empty (Compliance.Disarmed).
        internal readonly List<Item> KeptBack = new List<Item>(2);
        internal readonly List<Item> Dropped = new List<Item>(4);
        internal int Demands;
        internal bool Deceiving;            // kept a weapon on purpose this surrender (weighs his draw later)
        internal float EscalateAt;          // > 0: he goes for what he kept at this time (Compliance)
        internal float DemandDoneAt = -1f;  // a Drop your weapons answered (dropped, partly, refused): a captor judges it once

        // 0.17.0: a committed flight (BeginRetreat): where he was held (not where his captor was last
        // seen), legs taken to get out of that area, when the flight started, and until when he
        // won't go back for a gun lying in that area.
        internal Vector3 HeldAt, FleeFrom;
        internal bool HeldSet;
        internal int FleeLegs, FleeReplans;
        internal float FleeSince = -1f, LegSince, FleeLockUntil, NextFleeCheck;
        internal bool FleeLockLogged;

        // 0.24.0: release and escape. CalmFlight: left behind by his captor, he walks off (no
        // sprint; any fire ends it). CaptorLastVel: how the captor was moving when last SEEN (flat;
        // zero when not seen: no tracking). EquipLogged / LastRefused: one equipment line per
        // flight, each refused weapon recovery logged once. DefendWeighedUntil: one fight-or-cover
        // weighing per burst. FaceThreat: a draw under fire turns to the shooter, not the captor.
        internal bool CalmFlight;
        internal Vector3 CaptorLastVel;
        internal bool EquipLogged;
        internal string LastRefused;
        internal float DefendWeighedUntil;
        internal Vector3? FaceThreat;

        // 0.16.0: who hit him last (BeingHitAction), for the death report: decided or a leak.
        internal Player LastHitBy;
        internal float LastHitAt = -1000f;

        // The surrender as an offer (0.9.0). Accepted: the captor has acknowledged it (a human came
        // within AcknowledgeDistance in sight, aimed at him a moment, or gave an order; a bot took
        // custody). Until then it lasts until OfferUntil (longer while the captor closes in), then
        // he seeks safety or reassesses. Distance alone never cancels it.
        internal bool Accepted;
        internal SurrenderSource Source;     // 0.19.0: natural, recapture, debug, scenario (SurrenderStats)
        internal bool CountedAccept;         // counted once in SurrenderStats
        internal float OfferUntil;
        internal float ClosestDist = float.MaxValue;   // captor's closest approach seen this encounter
        internal float OfferRef = float.MaxValue;      // distance the next "he is coming for me" is measured from
        internal float CoveredSince = -1f;  // the captor has been aiming at him since (acknowledgement)

        // Follow: a failed path is retried at most once a second (not a path solve per frame).
        internal float FollowRetryAt;

        // 0.21.0: incoming fire that broke (or reached) this encounter (FireBreak.cs). FirePending:
        // the layer reacts on its next frame (SurrenderLogic.UnderFire). FireFrom: where it came
        // from as he can tell it (the shooter, or the hit's direction); FireBy when known.
        internal bool FirePending;
        internal Vector3 FireFrom;
        internal bool FireFromKnown;
        internal bool FireHit;
        internal float FireTime = -1000f;
        internal Player FireBy;
        internal string FireSense;
        internal bool FireRecover;          // going for his own gun under fire (abandoned if it turns unsafe)
        internal float FireRecoverUntil;
        internal Vector3 ThreatAt;          // what Hiding hides from (his captor's last sighting, or the fire)
        internal bool HideFromFire;         // the nearest spot hidden from the fire (not a far-off hiding place)
        internal float HideUntil;           // a move to cover ends here, wherever he is
        internal int RecoverTries;          // guns gone for since he got away (at most 2, then his own AI)
        // 0.21.0: leaving us (Phase.Released): why, and the hand-off log.
        internal string HandBackWhy;
        internal float LeftHeldAt = -1f;    // when he stopped holding his hands up (first time this surrender)
        // 0.25.0: a let-go or a walk-off is handed to his own AI mid-stride (no stop, no regroup):
        // QuickHandBack marks such a flight; HandBackMoving = he was still walking our last path
        // when the registry let go of him (the next layer's move replaces it).
        internal bool QuickHandBack, HandBackMoving, QuickEnded;
        // 0.25.0: the hand-off timeline (Time.time; -1 = didn't happen). Logged by ReleaseWatch once
        // his own AI is seen driving him, so "where does the delay come from" is a log fact.
        internal float TLeftCue = -1f, TLeftNoticed = -1f, TEscape = -1f, THandsDown = -1f, TOnFeet = -1f,
                       TFirstMove = -1f, TLayerOff = -1f;
        internal string EscapeKind;

        // 0.25.0: hands behind the head, one over the other (Kneel.ApplyHands): which hand lies on
        // top (0 left, 1 right; -1 not chosen yet). Chosen once per surrender, kept.
        internal int HeadTopHand = -1;
        // 0.25.0: kneel variants (KneelVariant.cs): the one he holds and the blend towards it.
        internal KneelVariantKind KVarTarget = KneelVariantKind.Upright;
        internal float KVarThigh, KVarLean;                      // current offsets on the base kneel (deg)
        internal float KVarFromThigh, KVarFromLean, KVarToThigh, KVarToLean;
        internal float KVarBlendT = 1f, KVarBlendSecs = 1f;
        internal float KVarChosenAt = -1000f, KVarCheckAt, KVarRoll = -1f;
        internal bool KVarShifting;                              // weight going forward before a rise
        internal string KVarWhy;

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

        // Kneeling (Kneel.cs). One progress value (0 standing .. 1 kneeling) and a phase; the
        // order only says what is wanted, Kneel.Update moves towards it, so repeating an order or
        // reversing half-way never restarts or stacks anything.
        internal KneelPhase Kneel;
        internal float KneelT;
        internal bool KneelUrgent;          // getting up for a real reason (escape, draw, hit): no reversal
        internal KneelStyle KneelStyle;     // timing in use (descent or rise), kept until the transition ends
        internal KneelStyle KneelDescent;   // how he went down
        internal float KneelSecs;           // duration of the current transition
        internal float KneelStep, KneelStepNow;   // front-foot step: room found at the start / this transition
        internal KneelRise KneelRise;       // the rise in progress
        internal KneelRise KneelIntent;     // what the layer's decision needs (Kneel.Urge)
        internal bool KneelIntentSet;
        internal float KneelVel;            // dt/dKneelT now; eased towards the target speed on a retime or reversal
        internal bool KneelBrace;           // front-side hand pushes on the front thigh this rise
        internal float KneelBraceGate = 1f; // 1 = brace as scheduled; falls to 0 once he lets go early
        internal bool KneelBraceLetGo;      // letting go early (draw, bolt, out of reach): the gate falls
        internal bool KneelBraceStretch;    // the pose found the knee beyond his reach while holding it
        internal float KneelBraceRatio;
        internal int KneelKneeFrame = -1;   // frame KneelKneePos was solved (the brace only uses this frame's)
        internal float KneelRefWait;        // waiting for an animated frame to capture the standing pose
        internal float KneelFade, KneelFadeTotal;   // hand-back blend to the animator after a rise
        internal float KneelThighOff, KneelLeanOff, KneelSpreadOff;   // small per-bot posture differences
        internal readonly Vector3[] KneelRef = new Vector3[8];   // standing pose at the start, body-local: hips, knees, feet, toes (L,R)
        internal Vector3 KneelRefSpine;
        internal bool KneelRefOk;
        internal readonly Vector3[] KneelKneePos = new Vector3[2];
        internal string KneelFallback;      // ordered down but kneeling isn't possible (reason); cleared by a new order
        internal float KneelWaitSince;
        internal float KneelCheckAt;
        internal KneelRig Rig;
        internal int KneelLead;             // 0 left knee first, 1 right
        internal bool KneelHelmet;
        internal float KneelHelmetAt;
        internal Vector3 KneelAnchor, KneelAnchorFwd;           // where the ground was checked
        // 0.23.0: the root held on the knees' direction (Kneel.HoldRoot) and the settle check before
        // a kneel is committed (Kneel.StillTurning).
        internal float KneelDrift, KneelYawPrev, KneelYawAt = -1f, KneelTurnWaitSince = -1f;
        internal bool KneelDriftLogged;
        internal readonly float[] KneelGround = new float[6];   // knee L/R, foot L/R, front-foot step L/R: ground height vs his feet
        internal float KneelClock, KneelDtMin, KneelDtMax;      // transition timeline (log)
        internal int KneelFrames;
        internal int KneelTestLeft;         // debug loop: kneels still to do
        internal float KneelTestNextAt;
        internal bool KneelTestArmed;       // debug interruption pending
        internal Player KneelTestBy;
        internal bool ForceDraw;            // debug: deterministic draw (kneel interruption test)
        internal bool SprintHeld;           // a sprint the layer wanted while the kneel still had his legs

        // 0.13.0: a better spot to kneel (bounded search, a short compliant walk, hands up).
        internal int KneelMoves;            // walks to a new spot for this order (max Kneel.MaxSpotMoves)
        internal int KneelSearchI = -1;     // next candidate to check (-1: no search running)
        internal Vector3 KneelSearchFrom;
        internal string KneelSearchWhy;     // why where he stands won't do (kept for the report)
        internal bool KneelSpotMoving;
        internal Vector3 KneelSpot;
        internal float KneelSpotSince;
        internal Vector3 KneelSpotProgress;
        internal float KneelSpotProgressAt;
        internal readonly float[] KneelScratch = new float[6];
        // Rise flowing into the first step.
        internal int KneelMoveFrame = -100; // last frame the layer wanted to walk while the kneel had his legs
        internal float KneelFreedAt = -1000f;   // legs handed back to the game (end of a rise)
        internal float KneelHoldUntil;      // t held still (a freeze while reconsidering an escape)
        // Interrupted escape from the knees: one reassessment per attempt.
        internal bool KneelEscape;          // the current escape started from his knees (an opening)
        internal string KneelEscapeKind;
        internal float KneelEscapeT;        // t when it started
        internal float KneelEscapeSeen = -1f;   // renewed coverage first seen
        internal float KneelEscapeNotice;   // ...noticed at
        // Limbs (Kneel.ReadLimbs): 0..1 per leg (0 left, 1 right), arms unusable for a brace.
        internal readonly float[] KneelLegHurt = new float[2];
        internal readonly bool[] KneelArmBad = new bool[2];
        internal float KneelLimbsAt = -1000f;
        // 0.15.0: the direction he turns to before kneeling, fixed when the order is taken (the
        // issuer's direction then), so a captor walking about never keeps him turning.
        internal Vector3 KneelFaceDir;
        internal bool KneelFaceSet;
        internal float KneelFaceSince = -1f;
        internal bool KneelFaceLogged;
        // 0.15.0: each leg bone's own axes (along, knee hinge), measured from the standing pose at
        // the first frame down: legs are posed as full rotations on their hinge, not swings.
        internal readonly Vector3[] KneelLegAlong = new Vector3[6], KneelLegHinge = new Vector3[6];
        internal bool KneelLegAxesOk;
        internal float KneelPoseLogAt;
        internal float KneelPelvisYaw, KneelChestYaw;   // last frame's corrections (diagnostics)

        // Each arm's own elbow hinge, in the upper arm's and the forearm's local space, measured
        // once from the animator's pose (ArmPose.MeasureAxes; index 0 left, 1 right). With it the
        // arm is posed by full rotations (direction and roll), not swings (ArmPose.PoseArm).
        internal readonly Vector3[] ArmUpAlong = new Vector3[2], ArmUpHinge = new Vector3[2], ArmForeAlong = new Vector3[2], ArmForeHinge = new Vector3[2];
        internal readonly bool[] ArmAxisOk = new bool[2];
        internal int ArmAxisTries;
        internal HandRig Hands;             // finger and twist bones (ArmPose.HandsOf, found once)
        internal bool HandsLooked;

        // Demeanor, head attention, kneeling idle, flinch (Presence.cs).
        internal readonly PresenceState Look = new PresenceState();
        internal readonly Vector3[] GizmoKnee = new Vector3[2], GizmoFoot = new Vector3[2], GizmoPalm = new Vector3[2];

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
            return r * Dice.Range(0.8f, 1.25f);
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
            KneelFallback = null;
            KneelEscape = false;
            KneelSpotMoving = false;
            KneelFaceSet = false;
            CaptorInView = true;
            KneelSearchI = -1;
            KneelTestLeft = 0;
            KneelTestArmed = false;
            ForceDraw = false;
            // 0.16.0: hands back up, facing free, demands start again (what he carries is real
            // inventory and is never reset: KeptBack is rebuilt from it at the next demand).
            HandsOnHead = false;
            HandsOrdered = false;
            HeadHandsLogged = false;
            Facing.Clear(this);
            Demands = 0;
            EscalateAt = 0f;
            // 0.21.0: nothing of the last escape carries into a new surrender.
            FirePending = false;
            FireRecover = false;
            HandBackWhy = null;
            LeftHeldAt = -1f;
            QuickHandBack = HandBackMoving = false;
            TLeftCue = TLeftNoticed = TEscape = THandsDown = TOnFeet = TFirstMove = TLayerOff = -1f;
            EscapeKind = null;
            OfferUntil =Time.time + Settings.Between(Settings.OfferMin, Settings.OfferMax) * (1.2f - 0.4f * Nerve);
            Gestures.Cancel(this, "new surrender");
        }

        // The captor acknowledged the surrender (logged once per encounter).
        internal void Accept(string how)
        {
            if (Accepted) return;
            Accepted = true;
            SurrenderStats.Acknowledged(this, botCustody: false);
            if (Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{Name}: surrender acknowledged by {Restraint.Name(Captor)} ({how})");
        }

        // quiet: routine repeats (hiding and looking again every few seconds) only go to the
        // log with Verbose logs on.
        internal void SetPhase(Phase p, string reason, bool quiet = false)
        {
            Phase old = Phase;
            Phase = p;
            if (p == Phase.HandsUp) { CalmFlight = false; FaceThreat = null; EquipLogged = false; LastRefused = null; }
            if (p != Phase.HandsUp)
            {
                Gestures.Cancel(this, $"now {p}");
                DecideAt = -1f;
                LeaveAt = -1f;
                DrawCommitUntil = 0f;
                // 0.16.0: acting for himself: his hands come off his head (the arms come down with
                // the phase), and nothing holds his facing any more.
                HandsOnHead = false;
                Facing.Clear(this);
            }
            PhaseSince = Time.time;
            NextMoveOrder = 0f;
            if (old == Phase.HandsUp && p != Phase.HandsUp && LeftHeldAt < 0f) LeftHeldAt = Time.time;
            // What the bots around him see him do: going for a weapon ends every restraint (and
            // is remembered by whoever saw it); running or giving up again is re-decided.
            if (old != p)
            {
                // 0.15.0: going for a weapon is told to the bots around him once it shows (arms
                // coming down, a draw in his hands, moving off: Reactions.Shows), not at the moment
                // he decides; SurrenderRegistry.Tick fires it (at most 1.5 s later).
                if ((p == Phase.Rearmed && reason != "mod disabled") || p == Phase.Recovering || p == Phase.PickingUp)
                {
                    ArmedPending = p == Phase.Rearmed ? "drew a weapon" : "went for a gun";
                    ArmedPendingSince = Time.time;
                }
                else
                {
                    ArmedPending = null;
                    Restraint.OnSituationChanged(this);
                }
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
            // 0.25.0: a calm way out (let go, left behind) is handed to his own AI as soon as he is
            // on his way (SurrenderLogic.QuickClear), not after legs of our flight and a regroup.
            QuickHandBack = !emergency && Settings.QuickHandBack.Value;
            QuickEnded = false;
            if (TEscape < 0f) { TEscape = Time.time; EscapeKind = emergency ? "flight" : CalmFlight ? "walk-off" : "let go"; }
            MarkFlight();
            // On his knees: a bolt needs the fastest rise he can manage; being let go, a calm one.
            HandsAreHigh.Kneel.Urge(this, emergency ? KneelRise.Escape : KneelRise.Compliant);
            LowerArms(emergency ? Settings.EmergencyLowerSeconds.Value : Settings.LowerSeconds.Value);
            GraceUntil = Time.time + Settings.Grace.Value;
            Order = Order.None;
            DropQueue.Clear();
            ReportAt = -1f;
            SetPhase(Phase.Retreating, reason);
        }

        // 0.17.0: leaving his captivity: remember where it was (once per flight from his hands up).
        internal void MarkFlight()
        {
            if (Phase != Phase.HandsUp || Bot == null) return;
            float now = Time.time;
            HeldAt = Bot.Position;
            HeldSet = true;
            FleeFrom = Bot.Position;
            FleeLegs = 0;
            FleeReplans = 0;
            FleeSince = now;
            LegSince = now;
            FleeLockUntil = now + Settings.FleeCommit.Value;
            FleeLockLogged = false;
            RecoverTries = 0;
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
