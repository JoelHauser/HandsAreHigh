# Release 0.29.0 (notes, test plan)

## 0.29.0 (2026-11-21): bosses give up only as the last survivor; Raiders are not bosses

From Joel's pasted spec.

### Root cause found (Raiders)

The game's role table marks `pmcBot` isBoss + isFollower, so `ClassOf` (`IsBossOrFollower`) filed
Raiders as Boss: they could only surrender with "Bosses can surrender" on (Joel's cfg: off), at boss
odds. Their brain ("PMC") always had our layers. Now `RoleClass.Raider`, checked before the boss test:
always eligible, PMC odds (base 0.12, threshold 0.5, max 0.25) x experience x personality, ordinary
gates (no squad condition), and Raiders can be captors without boss layers (`Captors.CanGuard`).
Personality-less fallbacks in Captors/Judgement/Restraint now give Raiders the PMC factor (was Boss).

### Boss last-survivor rule

- Squad boss = `IsPureBoss()` minus `bossBoarSniper` (`BossSquad.IsSquadBoss`, flagged once per bot in
  `BotHistory.SquadBoss`). Followers, Rogues and the rest of the Boss class keep the old Boss odds.
- Roster: every man seen in his `BotsGroup` or `BotBoss.Followers` on any of his checks, kept for the raid.
  A guard counts as dead only via `Player.OnPlayerDeadStatic` (death only) or a still-existing corpse
  with a dead HealthController. Distant, asleep, missing, despawned = not confirmed dead. Empty roster
  (spawned alone: Killa, Tagilla) = never.
- With guards not confirmed dead, the ordinary rules (severe, pinned, demand, reasons) never apply to a
  boss. When all are confirmed dead: stress = 1 - (1-injury)(1-0.8 fire)(1-0.6 out of ammo) (fire is
  armour-dulled); rolls only at stress >= 0.3; chance = `Boss last survivor chance` (0.3) x (0.25..1 by
  stress) x (1 - Nerve resistance x nerve) x personality x experience x bot threat, capped at the setting.
  At most 3 rolls per raid, each needing stress >= last refusal + 0.15. Refusal: nothing happens (he
  fights on); surrender: the ordinary `SurrenderRegistry.Surrender` path.
- Settings (section 2, common): `Boss last survivor can surrender` (on), `Boss last survivor chance`
  (0.3). Both need section 1 `Bosses can surrender (restart)` on (boss brains get our layers only then).
- Log: the roll line adds `boss stress`, `last-survivor rolls n/3`; gate text names guards left. Stats:
  gates `BossNotLastSurvivor`, `BossNoRollDue`; rolls split Scav/PMC/Raider/boss-follower.

### Verification

- Build passed (Debug + Release, 0 warnings); cfg bind on a copy of Joel's cfg: 2 keys added, nothing
  changed/dropped, second load stable.
- tools/bosstest 52/52 (real WildSpawnType table + the built DLL): Raider class/eligibility with bosses
  off; bosses vs followers; Reshala with 4 guards (4 alive, 3 dead + 1 unconfirmed, final death ->
  eligible); spawned alone; despawned guard; toggle off; refusal escalation and the 3-roll cap; odds:
  typical escalating fight ~23% per raid, ceiling 65.7%.

### Still needs a raid

- `Bosses can surrender` on (restart): a boss's checks name his roster size; killing all guards makes
  the gate say "last survivor"; one roll per worsening, at most 3.
- A boss with a guard asleep (ORBIT) or despawned stays "not confirmed dead".
- Raiders (Reserve/Labs) roll natural surrenders with squadmates alive; a Raider surrender's pose/drop.
- A surrendered boss under the prisoner behaviour (boss brains have never held a prisoner in a log).

## 0.29.0 Labs raid (2026-11-21, LogOutput, Joel playing; verified in raid, log level)

- Raiders: 41 rolls under ordinary rules with squadmates alive (Raider base 0.12, max 0.25); high nerve /
  Wreckless kept chances 0.004-0.09. One NATURAL Raider surrender (Барракуда to Ястреб, another
  Raider's squad): HandsUp -> wiggle/finger rolls -> captor never took him -> Hiding -> no route ->
  Released, hand-off 0.18 s. Debug-key surrender on Raider Беркут: drop 1.0 s, voice, pose, warning-shot
  freeze 8.4 s, flinch, killed by Joel. Role gate 0 (previous raid: Role 269). 0 HAH warnings/errors;
  Raid Review shot-patch NREs (24) and a BotFirstAid NRE are other code.
- Perf: 0.08% of frame time; surrender frame 16 ms (Start 7.4, Layer 4.3, Restraint 2.2; first surrender
  of the session, not yet compared); one 43 ms Natural frame coincided with an SPSave autosave
  (our work that frame trivial; cause unverified).
- No bosses on Labs: boss rule not exercised in raid.

## 0.29.1 (2026-11-21, from that log): packed + installed (hash-verified; 0.29.0 DLL + cfg in that session scratchpad backup-0.29.0); unrun

- "believes him 334-458 m from where he is": `EnemyInfo.HaveSeenPersonal` is set on any sighting
  (SetVisible), `PersonalLastPos` only on a full one (EEnemyPartVisibleType.Visible), so after a
  partial sighting it is Vector3.zero (map origin). `Perception.KnownPosition` and `Captors` threat
  memory now skip unset positions (`Perception.PositionSet`). A surrender in that state would have
  faced the origin.
- Trigger-guard log: the stored spare reason ("has his hands up") now says its age, "renewed after his
  release", "no longer held". Log text only.
- Build passed (Release, 0 warnings), tools/bosstest 52/52.

## Rename to Mercy (2026-11-21, Joel)

- Mod renamed Hands Are High -> Mercy everywhere (code, namespace, MercyPlugin, assembly Mercy.dll,
  BigBrain layer names Mercy_*, scripts, tools, docs incl. history). GUID com.joelhauser.handsarehigh
  -> com.mybutthasarash.mercy, so the cfg file is now com.mybutthasarash.mercy.cfg (Joel's values copied
  from the old file at install; bind check on the renamed DLL: 439/439, nothing changed). No config key
  name contained the old name.
- GitHub repo renamed JoelHauser/HandsAreHigh -> JoelHauser/Mercy (old URL redirects); clone folder
  H:\SPTMods\HandsAreHigh -> H:\SPTMods\Mercy. Old HandsAreHigh.dll removed from the install with Joel's
  approval (two GUIDs would both load). Log source is now `[Info   :Mercy]`.
