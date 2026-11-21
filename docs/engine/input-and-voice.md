# Player input and bot speech (EFT 0.16.9)

Provenance: release notes 0.4.0-0.25.0 (`docs/history/`). Labels: see [README.md](README.md).
Code: `InputGuard.cs`, `OrderMenu.cs`, `Runtime.cs`, `Voice.cs`, `CaptorVoice.cs`, `CommandVoice.cs`,
`StopDemand.cs`, `Scenarios/ScenarioUi.cs`.

## Input routing and filtering

- `InputManager.Update` turns bound keys into a `List<ECommand>` (`InputPreset.UpdateInput`);
  `InputManager.DispatchInput` hands that list to `InputTree.TranslateInput`, which walks the nodes
  (UI screens first, GamePlayerOwner last); each node may remove commands. We prefix
  `DispatchInput` (`DispatchInputPatch`, 39-byte IL, not inlined) and remove commands before any node
  sees them. Input may be delivered from Update or FixedUpdate: never assume our MonoBehaviour Update
  runs before the game's dispatch in a frame. [code]
- Hotbar: `PlayerInputTranslator.TranslateCommand -> SetSlotItem` for SelectKnife /
  SelectSecondaryWeapon / QuickSelectSecondaryWeapon / SelectFirst/SecondPrimaryWeapon /
  SelectFastSlot4..0; the quick-use radial reacts to PressSlot4..0; slots fire on press OR release
  by the player's press type. Wheel: ScrollNext/ScrollPrevious. [code]
- Interact: `EGameKey.Interact` -> `ToggleInputKeyCombination(BeginInteracting, EndInteracting)`
  (InputManager.Init). The menu removes both while it owns input and while you look at a man it can
  open on (`InputGuard.ClaimInteract`, renewed every frame). [code; F in a raid: unverified]
- Bindings read (never written) from `Singleton<SettingsManager>.Instance.Control.Settings
  .GetAllKeyBindings()` (KeyGroup.keyName = EGameKey, variants -> KeyCode). [compiled]
- Latching: a key pressed while we own input keeps its own commands removed until it has been up 2
  frames and max(0.06 s, 2 fixed steps), 30 s at most. Ownership lapses 2 frames after the last claim
  (a stopped menu can't leave input blocked). [code]
- A game screen over the raid shows the cursor (`Cursor.visible`): the menu yields. [code]
- BepInEx `KeyboardShortcut.IsDown` refuses any other held key (W while walking): the menu reads
  `Input.GetKeyDown` + its own modifiers instead. [code]
- Joel's keys (no numpad): order menu `/` open/confirm, `.` next, `=` previous, digits 1-9; F with
  "Interact key opens the menu" (section 5). Debug keys only with "Debug keys" on (section 7). EFT's
  Ctrl+H is the headlight; U is unbound in EFT and every installed mod cfg (checked 2026-11-20).

## Bot speech

- Play with `Player.Speaker.Play(trigger, BotTalk.GetMask(null), true)`; never `BotTalk.Say` /
  `Player.Say` for held bots: SAIN's `BotTalkPatch` / `PlayerTalkPatch` swallow every phrase but
  hurt/agony/death/breath. [code SAIN 4.5.1 + 0.3.0 log]
- `BaseSpeaker` returns a TagBank only once a clip matched; `OnPhraseTold -> Player.PlaySpeechFromTime`
  plays on `_speechSource` unless `IsOutOfProtagonistsRange`. The `SpeechSource` getter CREATES a
  source: read `_speechSource` via FieldRefAccess. `SpeakerManager.FreeToSpeak`: group blockers. [code]
- A higher importance replaces the current phrase (Surrender uses 1000 after 0.4 s on a busy
  speaker). [code, 0.19.0]
- The player's Stop phrase: `GamePlayerOwner.PlayPhraseOrGesture -> Player.Say` (postfix `SayPatch`
  in StopDemand.cs). Our own spoken orders use `Speaker.Play` so friendly bots don't react via
  `GlobalEventDispatcher.SayPhrase`. [code]
- Phrase banks (UnityPy, 2026-11-19): all scav_1-6, bear_1-3, bear_1_eng/2_eng, bear_vitaly,
  usec_1-6, usec_rob have Stop, Negative, Roger, Going, DontKnow, NeedHelp, OnMutter, OnBeingHurt,
  HurtHeavy, HurtNearDeath, OnFight, Fire, FollowMe, Look, Silence, Repeat. BEAR/USEC only: HoldFire,
  Cooperation, Gogogo, HoldPosition, GetBack, OnYourOwn, Regroup, GoForward. Scav only: Toxic.
  GetInCover: all but scav_6. None: Provocation, Greetings, Warning, MumblePhrase. Bank "Attention" is
  `EPhraseTrigger.Look` (35); Gogogo has no bank in any voice. Clip mask 0 matches any tags. The
  clips were never listened to (choice is by trigger name). [UnityPy]
- SAIN `PlayerComponent.PlayVoiceLine` = Speaker.Play, refuses while Speaking/Busy; SAIN's CanTalk
  needs a human within 150 m. [code]
- In-raid: "says ...: audible (clip, s)" lines appear (0.24.0 log); "no chance to speak within 2 s"
  when speakers are busy. [log]
