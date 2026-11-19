# Hands Are High

Bots in SPT can surrender. When a Scav or PMC is badly hurt, out of ammo, outnumbered or has lost his squad, he may drop the weapon in his hands and put his hands up.

He isn't a statue after that. He watches you, and if you look away, reload or holster your gun, he may go for his knife or a pistol he kept. Walk off and he runs, then comes back for his gun or picks up another one. Catch him again and his hands go back up.

> Work in progress: not released yet.

## Features

- **Bots surrender on their own:** it's rare, and it depends on how bad things are for the bot, his nerve, and his SAIN personality if SAIN is installed.
- **Openings:** a held bot breaks free if you stop covering him. Close up he uses his knife, further away a sidearm.
- **Escape and rearm:** he runs, regroups, then goes back for his own gun, finds another one, or hides.
- **Orders:** look at a held bot up close to make him drop his weapons, drop his kit, take his armour off, follow you, get down, stay, or leave.
- **Reactions:** shoot a surrendered bot and the others who see it run.
- **Settings:** everything is adjustable in the F12 menu.

## Requirements

- SPT 4.1.x
- [BigBrain](https://github.com/DrakiaXYZ/SPT-BigBrain/releases) 1.5.0 or newer

Works alongside SAIN and ORBIT.

## Install

Extract the zip into your SPT folder, so that `HandsAreHigh.dll` ends up in `BepInEx/plugins`.

## Controls

| Key | Action |
|---|---|
| `/` | Open the order menu while looking at a held bot; press again to give the order |
| `.` / `=` | Next / previous order |

You can rebind the keys in F12 > Hands Are High > 10. Orders.

## Building

```
scripts\pack.ps1 -SPTPath <your SPT folder> [-Install]
```

Run it from PowerShell. It builds against your SPT install and writes the release zip to `dist/`.
