# Build, package and offline checks

Every command here was run successfully on the home PC on 2026-11-21 unless marked otherwise.
Passing these proves the code compiles, the config loads and a MODEL behaves; it never proves
in-raid behaviour (see [in-raid-regression.md](in-raid-regression.md)).

## Build and pack (PowerShell, not Bash)

Bash turns `H:\SPT4.1.X` into `H:SPT4.1.X`; run builds from PowerShell.

```powershell
# Release build + version check + config-name check + dist\HandsAreHigh-<ver>.zip
.\scripts\pack.ps1 -SPTPath H:\SPT4.1.X
# same, then copy the DLL into H:\SPT4.1.X\BepInEx\plugins
.\scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
# Debug compile only (fast check while editing)
dotnet build src\HandsAreHigh\HandsAreHigh.csproj -c Debug -p:SPTPath=H:\SPT4.1.X
```

- The csproj defaults `SPTPath` to `C:\HUH` (work laptop); on the home PC always pass it.
- Work laptop (no SAIN/ORBIT/BigBrain installed): `-SPTPath C:\HUH -BigBrainDll <path>\DrakiaXYZ-BigBrain.dll`
  (BigBrain 1.5.0, compile-only reference; never packed). Not re-run since 2026-11-19.
- Alternative used once (0.15.0): `dotnet build -c Release -p:DeployToSPT=true -p:SPTPath=...`.
- `TreatWarningsAsErrors` is on: 0 warnings is required, not a goal.
- pack.ps1 refuses: csproj `<Version>` != `HandsAreHighPlugin.PluginVersion` (bump BOTH); config
  section/key names containing `= \ " ' [ ]` or a tab (BepInEx throws in Awake and the whole plugin
  dies with nothing in LogOutput.log; it shipped in 0.0.2-0.1.0 and was caught twice since).
- Zip contents: `BepInEx/plugins/HandsAreHigh.dll` only (forward-slash entries). No README, no
  BigBrain, nothing else.

## Before installing into H:\SPT4.1.X

- Check the game is not running (`Get-Process EscapeFromTarkov`): a running game locks the DLL.
  The launcher alone is fine.
- Back up the installed DLL and `BepInEx\config\com.joelhauser.handsarehigh.cfg` to the session
  scratchpad (`backup-<old version>`) first.
- Never delete or overwrite other files in an SPT install. Never write Joel's cfg from a read that
  may have failed (it was blanked once). Let the mod migrate the cfg itself.
- After install, confirm with a hash compare against `src\HandsAreHigh\bin\Release\HandsAreHigh.dll`.

## Config check (whenever settings change)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\cfgbind\bind.ps1 `
  -Dll src\HandsAreHigh\bin\Release\HandsAreHigh.dll -Spt H:\SPT4.1.X `
  -Cfg <copy of Joel's cfg> -Work <scratchpad>\cfgbind
```

Expect: only the new keys added, only intended migrations changed, nothing dropped, a second load
changes nothing. Existing section/key names must not be renamed casually: Joel's cfg holds values for
them (renaming = his value silently lost). Changing a default: add a step to
`Settings.Migrate` (Settings.V19.cs, bump `CurrentConfigVersion`); it moves a value only if it is
still at the OLD default. `Migrate` runs at the end of `Settings.Bind`, after every section.

## Offline models (Python 3 + UnityPy where a bundle is read; run with `python -I`)

| Tool | What it checks (a model, not the game) | Last result |
|---|---|---|
| tools/armsim (armsim3.py) | arm solver, hands-up frame, behind-head paths, clearances, elbow flips | pass 0.22.0 |
| tools/headsim | head rig axes from skeleton.bundle, absolute head target | pass 0.18.0 |
| tools/fingersim/fingersim.py `<skeleton.bundle>` | open/cupped finger shapes | pass 0.24.0 |
| tools/fingersim/interlace.py `<bundle>` | whether fingers can interlace (they can't cleanly) | 0.25.0 |
| tools/fingersim/overhand.py `<bundle>` | hands one over the other: clearance, skull, resting | pass 0.25.0 |
| tools/fingersim/dumprig.py `<bundle>` | prints the finger rig | - |
| tools/posesim | turn driver, trunk limit, hands profile reversals | pass 0.22.0 |
| tools/kneelsim/variants.py | kneel variant geometry, blends, rise shift speed | pass 0.25.0 |
| tools/standsim | captor pick-up stand spots | 20/20 0.23.0 |
| tools/coversim | squad cover fire lines | pass 0.20.0 |
| tools/firesim | trigger guard line-of-fire rule | 12/12 0.21-0.24 |
| tools/escsim | escape route rules vs a walking captor | pass 0.24.0 |
| tools/releasesim | every post-captivity path ends in his own AI, bounded | pass 0.24.0 (not updated for 0.25 quick hand-back) |
| tools/abandonsim | abandonment reaction time, loot-step / reposition false positives | pass 0.25.0 |
| tools/natsim | natural-surrender odds in ordinary fights | 0.19.0 |
| tools/perftest | `dotnet run -c Release -- sain H:\SPT4.1.X`: FastAccess readers vs reflection on the installed SAIN.dll | 28/28 0.20.1 |

skeleton.bundle: `H:\SPT4.1.X\EscapeFromTarkov_Data\StreamingAssets\Windows\assets\content\characters\character\skeleton.bundle`.
Run the model of the subsystem you changed; rerun others only if they share the changed rule.
