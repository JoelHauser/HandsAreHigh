# Hands Are High

An SPT 4.1 mod that lets bots give up. A Scav or PMC who is losing badly can drop the weapon in his hands and put them up, and from then on he is a person in a bad spot, not a statue: he watches you, follows orders (or most of them), and takes his chance the moment you give him one.

It works both ways: bots take prisoners too, from you and from each other.

> Pre-release. No GitHub release yet; the current build is 0.28.6.

## Features

- **Surrender is rare and earned.** A bot weighs his wounds, rounds cracking past him, an empty magazine, being outnumbered and a squad that is down or gone. His nerve matters, and with SAIN so does his personality. A cornered Scav gives up far sooner than a PMC.
- **Only the weapon in his hands goes down.** Anything slung or holstered stays on him until someone tells him otherwise.
- **Orders.** Look at a held bot up close and give orders: drop weapons, drop kit, armour off, get down on his knees and stand up, hands on head and hands up, turn around, stay, follow me, let go. He may quietly keep a sidearm or knife.
- **Openings.** Look away, reload, holster or turn your back, and he notices a moment later. He draws what he kept (slung gun first, then sidearm, his knife only with no gun), runs, or slips into cover.
- **Escape and rearm.** Walk away and he gets clear once he is sure you are gone, keeps away from whoever held him, comes back for his gun when it is safe, picks up another one, or hides.
- **Bots take prisoners.** A bot a man gives up to decides for himself: take him prisoner, spare him, ignore him, or keep fighting. A captor gives orders, collects the gear he wants, and ends it his own way; his squad holds fire at that man and one squadmate may cover him. Not every captor is merciful.
- **Hold-fire only for that man.** Every other enemy is fought as normal, a bot never fires through a man he has spared, and a prisoner who picks up a weapon is an enemy again.
- **Voice and gestures.** The game's own voice lines (only lines the bot's voice actually has), a nervous wiggle, and the occasional rude gesture.
- **Settings.** Seven sections in the F12 menu; the fine tuning is under Advanced settings.

## Requirements

- SPT 4.1.x
- [BigBrain](https://github.com/DrakiaXYZ/SPT-BigBrain/releases) 1.5.0 or newer

## Install

Extract the zip into your SPT folder, so that `HandsAreHigh.dll` ends up in `BepInEx/plugins`. Client only, no server part.

## Controls

| Key | Action |
|---|---|
| `/` | Open the order menu while looking at a held bot up close; press again to give the highlighted order |
| `F` (your Interact key) | The same, within order range; the press never also opens a door or picks something up |
| `1`-`9` | Give that numbered order while the menu is open; `0` closes it. Your hotbar ignores these keys meanwhile |
| Mouse wheel, `.` / `=` | Move the highlight |

Keys can be rebound in F12 > Hands Are High > 5. Commands and dialogue.

## Compatibility

- **SAIN** and **ORBIT**: developed and tested alongside them.
- **Looting Bots**, **Pit Fireteam**: supported in the code, not yet tested in a raid.
- **Fika**: not supported.

## Building

```
scripts\pack.ps1 -SPTPath <your SPT folder> [-Install]
```

Run it from PowerShell. It builds against your SPT install and writes the release zip to `dist/`. Developer notes are in [CLAUDE.md](CLAUDE.md) and [docs/](docs/).
