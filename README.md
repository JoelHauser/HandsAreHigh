# Hands Are High

Bots in SPT can surrender. When a Scav or PMC is badly hurt, out of ammo, outnumbered or has lost his squad, he may drop the weapon in his hands and put his hands up.

He isn't a statue after that. He watches you, and if you look away, reload or holster your gun, he may go for his knife or a pistol he kept. Walk off and he runs, then comes back for his gun or picks up another one. Catch him again and his hands go back up.

> Work in progress: not released yet.

## Features

- **Bots surrender on their own:** it's rare, and it depends on how bad things are for the bot, his nerve, and his SAIN personality if SAIN is installed.
- **Openings:** a held bot breaks free if you stop covering him. Close up he uses his knife, further away a sidearm.
- **Escape and rearm:** he runs, regroups, then goes back for his own gun, finds another one, or hides.
- **Bots take prisoners too:** a Scav or PMC can give up to a hostile bot. That bot decides whether to guard him, spare him or keep fighting, its squad holds fire at that prisoner only, and other bots decide for themselves whether to spare a man with his hands up.
- **Orders:** look at a held bot up close to make him drop his weapons, drop his kit, take his armour off, follow you, get down on his knees with his hands behind his head (and back on his feet), stay, or leave.
- **Reactions:** shoot a surrendered bot and the others who see it run. A warning shot past him keeps him still; he notices when you reload, look away, walk up or take his gun, a moment later, like a person would.
- **Talking:** held bots plead, mutter, answer your orders and sometimes mouth off, using only lines their voice actually has. Optionally your own character speaks your orders.
- **The wiggle:** now and then a nervous bot leans left and right with his hands up.
- **Defiance:** once in a while a bold one flips you off with his free hand, the right one staying up.
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
| `/` | Open the order menu while looking at a held bot; press again to give the highlighted order |
| `1`-`9` (or numpad) | Give that numbered order while the menu is open; `0` closes it. Your hotbar ignores these keys while the menu is open |
| Mouse wheel, `.` / `=` | Move the highlight |

You can rebind the keys in F12 > Hands Are High > 10. Orders.

## Building

```
scripts\pack.ps1 -SPTPath <your SPT folder> [-Install]
```

Run it from PowerShell. It builds against your SPT install and writes the release zip to `dist/`.
