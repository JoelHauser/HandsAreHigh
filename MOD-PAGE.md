<!--
  The SPT Forge (sp-mod.com) mod-page description. Paste everything below this comment into the
  listing. Check it in the Forge editor's preview before saving.

  Forge extras used: a blockquote followed by "{.is-info}" / "{.is-warning}" becomes a coloured
  box; "## Title {.tabset}" turns the ### headings under it into tabs up to the next ## heading.
  Raw HTML is stripped. No images exist yet (add a hero image / thumbnail when there is one; the
  thumbnail is cropped square).

  Short-description field:
    "Scavs and PMCs can surrender: weapon down, hands up, and an eye on you for an opening. Give them orders, take them prisoner, or watch bots take each other."

  Release facts still to confirm before posting:
    - Version shown on the Forge (the code is 0.28.1; no GitHub release exists yet) and whether it
      goes up as a pre-release / beta.
    - SPT version string for the listing (built and tested on SPT 4.1.x, EFT 0.16.9.40743).
    - Links for SAIN, ORBIT, Looting Bots and Pit Fireteam (only BigBrain is linked below).
    - Pit Fireteam teammate detection: written for it, never run against the real mod.
    - Licence, and the Forge's "custom AI disclosure" field.
    - 0.28.x (ignored surrenders, truce, the seven-section F12 menu) has not been run in a raid.
      Do one raid before posting.
-->

> **Hands Are High lets bots give up.** A Scav or PMC who is losing badly can drop the weapon in his hands and put them up, and from then on he is a person in a bad spot, not a statue. He watches you, does what he is told (or most of it), and takes his chance the moment you give him one.
>
> It works both ways: bots take prisoners too, from you and from each other.

{.is-info}

## The surrender

Surrender is rare and has to be earned. A bot weighs how the fight is going for him: his wounds, the rounds cracking past him, an empty magazine, being outnumbered, a squad that is down or gone. His own nerve matters, and with SAIN installed so does his personality. A cornered Scav gives up far sooner than a PMC.

When he breaks, he calls it out, drops the weapon in his hands and raises them. Anything still slung or holstered stays on him until someone tells him otherwise.

## Holding him

Look at a held bot up close and open the order menu:

- **Drop weapons**, **Drop kit**, **Armour off**
- **Get down** (on his knees) and **Stand up**
- **Hands on head** and **Hands up**
- **Turn around**, **Stay**, **Follow me**
- **Let go**

He follows orders with the game's own animations and voice lines: he kneels, turns, laces his fingers behind his head, answers, pleads, mutters. He does not always comply completely. A prisoner told to drop his weapons may quietly keep a sidearm or knife, and the bold ones occasionally show you exactly what they think of you.

He also reads the room. Fire a warning shot past him and he freezes. Look away, reload, holster, or turn your back, and he notices a moment later, like a person would.

## The opening

A prisoner is always thinking about getting out. When the man covering him stops covering him, he weighs his chances: his weapons, the distance, his legs, his wounds, his squad, a way out. If he decides to go, he draws whatever he kept, slung gun first, then sidearm, and his knife only if he has no gun. Or he simply runs.

Walk away and he stays put until he is sure you are gone, then gets clear. He keeps his distance from where he was held, comes back for his own gun when it is safe, picks up another one, or hides. Catch him again and his hands go back up.

## Bots take prisoners

A Scav or PMC can give up to a hostile bot, not only to you. That bot makes his own call:

- **Take him prisoner.** He covers him, gives orders, sometimes walks up to control him, collects the weapons and gear he wants, and ends it his own way: lets him go, walks off, or leaves a squadmate behind on guard.
- **Spare him.** He holds fire and leaves the man be.
- **Ignore him.** Not worth the time. The two leave each other alone for a while after.
- **Keep fighting.**

His squad goes along with the decision, and one squadmate may cover the captor from a better angle. Other bots who come across a man with his hands up decide for themselves whether to spare him. Not every captor is merciful, and a prisoner who runs from a bot captor may be shot for it.

The hold-fire applies to that one man only. Every other enemy is fought as normal, and a prisoner who picks up a weapon is an enemy again to whoever sees it.

## Field manual {.tabset}

### Install

**Requires:**

- SPT 4.1.x
- [BigBrain](https://github.com/DrakiaXYZ/SPT-BigBrain/releases) 1.5.0 or newer

Unzip into your SPT folder, so that `HandsAreHigh.dll` lands in `BepInEx\plugins`. Client only, no server part.

### Controls

| Key | What it does |
|---|---|
| `/` | Opens the order menu while you look at a held bot up close; press again to give the highlighted order |
| `F` (your Interact key) | The same, within order range. The press is kept from the game, so it never opens a door or picks something up at the same time |
| `1`-`9` | Gives that numbered order while the menu is open; `0` closes it. Your hotbar ignores these keys meanwhile |
| Mouse wheel, `.` / `=` | Moves the highlight |

Every key can be rebound in F12 under **5. Commands and dialogue**.

### Settings

In the F12 menu, under **Hands Are High**, in seven sections:

1. **General** switches the main parts on and off: natural surrenders, PMCs, bots taking prisoners, the order menu, bosses (off by default), and the SAIN and ORBIT options.
2. **Surrender** controls how readily bots give up.
3. **Captors and squads** controls what bot captors do: take, spare or ignore, gear, orders, endings, and how ruthless they are.
4. **Prisoner escape and recovery** controls breaking free, running, hiding and rearming.
5. **Commands and dialogue** holds the keys, which orders the menu offers, and the voice lines.
6. **Animations** covers the poses and gestures.
7. **Debugging** is for testing and can be left alone.

The common settings are shown first. Tick **Advanced settings** to see the fine tuning.

### Compatibility

- **SAIN**: developed and tested alongside it. With SAIN, a bot's personality shapes his decisions, and SAIN steps aside while a bot is held.
- **ORBIT**: developed and tested alongside it. Encounters are kept awake, and a released prisoner isn't walked straight back to where he was caught.
- **Looting Bots, Pit Fireteam**: supported in the code, not yet tested in a raid.
- **Questing Bots** and other AI mods: not tested.
- **Fika**: not supported.

### Good to know

- **Surrenders are rare.** That's deliberate: most fights end the usual way. The odds are in **2. Surrender** if you want more.
- **Who can surrender:** Scavs and PMCs. Bosses and their followers only if you switch them on. Raiders, Rogues and cultists never. Your own teammates don't, unless you allow it.
- **Up to four bot-held prisoners at once** by default, so big fights stay readable.
- **The poses are fitted to EFT's own skeleton.** Bulky helmets, gloves or sleeves can still clip a little in some poses.
- **Most testing so far used set-up encounters.** Surrenders in an ordinary raid are rare by design and have had the least testing. Reports with your `BepInEx\LogOutput.log` are welcome.

### Uninstall

Delete `BepInEx\plugins\HandsAreHigh.dll`. To remove its settings too, delete `BepInEx\config\com.joelhauser.handsarehigh.cfg`.
