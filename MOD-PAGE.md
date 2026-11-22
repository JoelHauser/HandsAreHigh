<!--
  The mod-page description. Paste everything below this comment into the listing.
  Check it in the Forge editor's preview before saving.

  Written to answer "what does this mod do?" in the first lines; the flavour comes after.
  The Forge shows the word "Tarkov" as "T*****", so say "EFT".

  Forge extras used (raw HTML is stripped):
    - a blockquote followed by a line "{.is-info}" / "{.is-warning}" becomes a coloured box;
    - "## Title {.tabset}" turns the ### headings under it into tabs, up to the next ## heading
      (no closing marker);
    - a YouTube link on a line of its own embeds a player.

  Art: scripts/make_art.py, from Joel's screenshots in art/screens (debug overlays cropped away).
    docs/images/logo.png       header: the MERCY wordmark, transparent (scripts/make_logo.py from
                               art/logo-source.png). Cream letters: made for the dark theme, faint on a light one.
    docs/images/hero.jpg       the sunset close-up with MERCY over it (off the page since the logo)
    docs/images/thumbnail.png  square, for the Forge thumbnail (cropped 1:1)
    Section images, Joel's own framed shots (art/screens/page), resized only: scripts/make_page_images.py
      lineup.jpg (four strips stacked), orders.jpg, prisoner.jpg, squad.jpg, roadblock.jpg
    Not on the page: docs/images/cover.jpg, the painted grid cover (scripts/make_cover.py), for
    socials or a gallery; holding.jpg / captors.jpg (make_art.py stills) are retired.
    dist/Mercy-teaser.mp4      32 s 1080p teaser, silent
  Images are served from the repository, so they show once pushed to main.
  Video: https://youtu.be/k9EUDdaMcZw ("See it in action"). The Forge shows YouTube's own thumbnail
  (i.ytimg.com/vi/<id>/hqdefault.jpg) with a play button and loads the player on click; set
  docs/images/hero.jpg (1280x720) as the video's custom thumbnail in YouTube Studio.

  Short-description field:
    "Bots can give up. A beaten Scav or PMC drops his gun and raises his hands, then waits for his chance. Give him orders, take him prisoner, or watch bots take each other."

  Confirm before posting:
    - Version on the Forge: 1.0.0 (GitHub release v1.0.0, Mercy-1.0.0.zip).
    - Links for SAIN, ORBIT, Looting Bots and Pit Fireteam (only BigBrain is linked).
    - Licence, and the Forge's "custom AI disclosure" field.
-->

![MERCY](https://raw.githubusercontent.com/JoelHauser/Mercy/main/docs/images/logo.png)

> **What this mod does:** it lets bots surrender.
>
> - **When the fight turns against him, a Scav or PMC can give up.** He drops the weapon in his hands, puts them up and calls it out.
> - **You decide what happens next.** Order him to his knees, strip his kit, march him, let him go, or don't.
> - **Bots take prisoners too**, from you and from each other.
>
> A prisoner is not a statue. He watches you, does what he's told (mostly), and the moment you look away, reload or turn your back, he starts thinking about the gun at his feet.

{.is-info}

## What happens

![Four bots with their hands up: a factory, a dark warehouse, a red door, a brick wall](https://raw.githubusercontent.com/JoelHauser/Mercy/main/docs/images/lineup.jpg)

- **Surrender is rare, and earned.** A bot weighs his own fight: his wounds, rounds cracking past his head, an empty magazine, being outnumbered, a squad that is down or gone. His nerve matters, and with SAIN so does his personality. A cornered Scav folds long before a PMC does, and a GigaChad holds out longest of all.
- **Only the gun in his hands goes down.** Whatever is slung or holstered stays on him until someone makes him drop it.
- **He takes orders.** On your knees. Hands on your head. Turn around. Drop your kit. Armour off. Follow me. He does it with the game's own animations and voice lines: kneels, laces his fingers behind his head, answers, pleads, mutters under his breath.
- **He doesn't always comply.** Told to drop his weapons, he may keep a pistol back. The bold ones will flip you off with one hand while the other stays up.
- **He reads the room.** A warning shot past his ear freezes him. Look away, reload or holster, and he notices a moment later, the way a person would.
- **He takes his chance.** Give him an opening and he weighs it: the gun at his feet, the one still on him, how far you are, his legs, a way out. He might scramble for it, draw, bolt, or slip quietly into cover.
- **Walk away and he gets clear.** He waits until he's sure you're gone, then keeps his distance, goes back for his own gun when it's safe, picks up one lying nearby, or hides.

![A bot with his hands behind his head in front of a burning warehouse](https://raw.githubusercontent.com/JoelHauser/Mercy/main/docs/images/orders.jpg)

## See it in action

https://youtu.be/k9EUDdaMcZw

## When a bot takes the surrender

![A bot captor covers his prisoner, kneeling with his hands behind his head](https://raw.githubusercontent.com/JoelHauser/Mercy/main/docs/images/prisoner.jpg)

A bot can give up to another bot, not only to you. That bot makes his own call:

| The bot... | What you'll see |
|---|---|
| **Takes him prisoner** | Covers him, gives orders, walks up to control him, collects the guns and gear he wants, then ends it his own way: lets him go, walks off, or leaves a squadmate on guard |
| **Spares him** | Holds his fire and leaves him be |
| **Ignores him** | Not worth the time. The prisoner soon notices, lowers his hands and gets his gun back if he safely can; the two leave each other alone for a while |
| **Keeps fighting** | The usual way a fight ends |

His squad goes along with the decision, and one squadmate may take an angle to cover the captor. Other bots who come across a bot with his hands up decide for themselves. Not every captor is merciful: some never meant to let him walk, and a prisoner who runs may be shot in the back.

![A bot squad closes in on a bot with his hands up in a field](https://raw.githubusercontent.com/JoelHauser/Mercy/main/docs/images/squad.jpg)

The hold-fire is for that one prisoner. Every other enemy is fought as normal, a bot won't fire through a prisoner he spared, and a prisoner who picks up a gun is fair game again.

## The idea

Nobody in Norvinsk wants to die for a backpack. Most fights still end the usual way, but now and then the guy across the room has had enough, and what happens next is up to you, or to whoever got there first.

![A Scav on his knees with his hands up at a roadblock](https://raw.githubusercontent.com/JoelHauser/Mercy/main/docs/images/roadblock.jpg)

## Field manual {.tabset}

### Install

**Requires:**

- SPT 4.1.x
- [BigBrain](https://github.com/DrakiaXYZ/SPT-BigBrain/releases) 1.5.0 or newer

Unzip into your SPT folder, so `Mercy.dll` lands in `BepInEx\plugins`. Client only, no server part.

### Controls

| Key | What it does |
|---|---|
| `/` | Opens the order menu while you look at a held bot up close; press again to give the highlighted order |
| `F` (your Interact key) | The same, within order range. The press never also opens a door or picks something up |
| `1`-`9` | Gives that numbered order while the menu is open; `0` closes it. Your hotbar ignores these keys meanwhile |
| Mouse wheel, `.` / `=` | Moves the highlight |

The orders: **Drop weapons, Drop kit, Armour off, Get down, Stand up, Hands on head, Hands up, Turn around, Stay, Follow me, Let go.**

Every key can be rebound in F12 under **5. Commands and dialogue**.

### Settings

In the F12 menu, under **Mercy**, in seven numbered sections:

1. **General**: natural surrenders, PMCs, bots taking prisoners, your teammates, the order menu, bosses (off by default).
2. **Surrender**: how readily Scavs and PMCs give up, and how long before they can again.
3. **Captors and squads**: how often bots take, spare or ignore a prisoner, whether they take his gear, and how ruthless they are.
4. **Prisoner escape and recovery**: how often a prisoner tries his luck, and whether he goes back for a gun or a knife.
5. **Commands and dialogue**: keys, menu size, hints and voice lines.
6. **Animations**: stance, hands behind the head, kneeling styles and gestures.
7. **Debugging**: for testing; leave it alone.

Only the everyday settings are shown. Tick **Advanced settings** for the fine tuning: timings, per-order chances, pose details, and the SAIN and ORBIT options.

### Compatibility

- **SAIN**: built and tested alongside it. A bot's SAIN personality shapes his choices, and SAIN steps aside while he is held.
- **ORBIT**: built and tested alongside it. Encounters stay awake, and a released prisoner isn't walked straight back to where he was caught.
- **COMET**: tested alongside it. Works together without issues.
- **Looting Bots, Pit Fireteam**: supported, not yet tested in a raid.
- **Questing Bots** and other AI mods: not tested.
- **Fika**: not supported.

### Good to know

- **Surrenders are rare on purpose.** Most fights end the usual way. Want more? Raise the chances in **2. Surrender**.
- **Who can give up:** Scavs, PMCs and Raiders. Boss followers, if you switch bosses on; a boss himself only once every one of his guards is dead. Rogues and cultists never.
- **Up to four bot-held prisoners at once** by default, so a big fight stays readable.
- **The poses are fitted to EFT's own skeleton.** Bulky helmets, gloves or sleeves can still clip a little in some poses.
- **Found something odd?** Reports with your `BepInEx\LogOutput.log` are welcome.

### Uninstall

Delete `BepInEx\plugins\Mercy.dll`. To remove its settings too, delete `BepInEx\config\com.mybutthasarash.mercy.cfg`.
