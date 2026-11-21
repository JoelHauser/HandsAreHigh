# Animation, skeleton and pose ordering (EFT 0.16.9)

Provenance: release notes 0.6.0-0.25.0 (`docs/history/`), UnityPy reads of skeleton.bundle and
player_anim_controller.bundle. Labels: see [README.md](README.md). Offline models: `tools/armsim`,
`tools/headsim`, `tools/fingersim`, `tools/posesim`, `tools/kneelsim`.

## Who writes the bones, and when

- Our whole pose runs in a postfix on `Player.VisualPass` (`ArmPosePatch`, ArmPose.cs). Nothing
  re-poses after it: `Player.Grounder` (FinalIK GrounderFBBIK) is disabled in InitVisuals; FBBIK/LimbIK
  only work the hands. The animator rewrites every bone next frame, so nothing accumulates. [code]
- `_bodyupdated` (protected, FieldRefAccess `Kneel.BodyUpdated`) is false on frames the body
  animator didn't run (bots in the fixed-update queue at high fps): skip the body pass then, or
  partial weights blend twice (creep, then jump). [code, 0.12.1]
- Order inside ArmPosePatch (one owner per bone): `Kneel.ApplyBody` (pelvis, torso, legs) ->
  `Presence.ApplyUpper` (torso turn, flinch) -> `ArmPose.Apply` (hands-up arms) -> `Presence.ApplyHead`
  (neck + head, absolute) -> `Kneel.ApplyHands` (hands to the head, follows the skull frame) ->
  `Kneel.ApplyBrace` -> `Fingers.Apply` (last). Hands follow the head, never the reverse. [code]
- Death: `Player.LateUpdate` only calls VisualPass for the living; OnDead disables the animators and
  CreateCorpse makes the ragdoll from whatever pose he died in. We hook `OnPlayerDeadOrUnspawn` and
  stop posing that moment. Nothing touches colliders, rigidbodies or corpse bones. [code]
- `UsedSimplifiedSkeleton` is set at creation (infected), not a distance LOD. [code]

## Skeleton (skeleton.bundle, shared by all humans)

- Legs: `Base HumanPelvis` (0.928 m bind) -> `HumanLThigh1` (hip) -> `HumanLThigh2` (mid-thigh twist,
  NOT the calf) -> `HumanLCalf` (knee) -> `HumanLFoot` (ankle) -> `HumanLToe`. Live measurements in the
  0.24.0 log: thigh 0.456, shin 0.460, foot 0.155, hips 0.182 apart, torso 0.69 m. [UnityPy + log]
- Head: `Base HumanHead` local +Y = face, local X = down, local Z = left; Root_Joint +Z forward.
  The head is set to an absolute target from these axes (zero roll). [UnityPy + sim tools/headsim;
  log "head rig checked: face = bone +Y, crown = bone -X"]
- Arms: elbow hinge measured live per surrender from the game's pose (`ArmPose.MeasureAxes`); arms
  posed as full rotations (`ArmPose.PoseArm`), never swing-only (swing-only twisted forearms ~120 deg).
  [sim tools/armsim; log "arm hinge measured ... arms posed with roll"]
- Fingers: `Base Human{L,R}Digit{1..5}{1..3}`; child along local -X, flexion about local +Z, palm side
  is the palm's -Y; the right hand = the left mirrored in local z (positions z negated, rotations x,y
  negated). The bind pose is curled (~72 deg index). Knuckle pitch 2.3-2.7 cm (Digit21-51 local z):
  less than two gloved fingers, so a clean finger INTERLACE is not possible on this rig
  (tools/fingersim/interlace.py: best of ~3,000 layouts still overlaps 0.7 cm). [UnityPy + sim]
- The game's animator writes the finger bones every frame: log 0.24.0 "fingers: the game's animator
  writes them every frame (open hands laid over it)". [log, 2026-11-21]

## Posture, capsule, kneeling

- No kneel/sit/get-up clip exists in player_anim_controller: kneeling is procedural on top of the
  game's crouch. `BotOwner.SetPose(0)` -> BotMover eases PoseLevel; `MovementContext.SetPoseLevel`
  sizes the capsule `Lerp(1.2, 1.6, pose)`. No collider, capsule or position is ours. [code]
- `MovementContext.CanStandAt(1f)`: a ceiling keeps him crouched (the game refuses to stand). [code]
- The standing reference is captured body-local on the first frame down (`Kneel.CaptureRef`); legs
  solved from it on their own hinge (`MeasureLegAxes`, `PoseBone`); feet in the kneel's frame
  (`FeetFrame`). Toes tucked, never instep-down (flips ~30 cm in a frame near the ground). [sim]
- Root yaw held on `KneelAnchorFwd` while kneeling (`Kneel.HoldRoot`); look kept on the knees'
  direction (`SurrenderLogic.KeepLookOnBody`), so HandsToBodyAngle ~0 and no animator trunk twist.
  [code; log "kneel: waits (still turning)" in the 0.24.0 raid]

## Lean, gestures, pick-up pose

- Lean: `MovementContext.SetTilt` stores `_tilt` (+-5); `SmoothTilt` eases it to the animator.
  Animation only. SAIN's LeanClass calls SetTilt every tick for bots with an enemy: a prefix on
  `SmoothTilt` (`TiltPatch`) holds held bots. Whether Mono inlines SmoothTilt: unverified. [code]
- Middle finger: `EInteraction.GetOffGesture` (clip `tp_gesture_04_getoff`, 1.23 s, left arm only),
  played via `MovementContext.SetInteractInHands`; preconditions FirearmsAnimator.IsIdling, not
  sprinting, not simplified skeleton; end on `IsInteractionPlaying` false; interrupt with
  `ForceStopInteractions`. [code + UnityPy]
- Pick-up body lowering: see inventory-and-hands.md (PickUpState, look > 60 deg down).
