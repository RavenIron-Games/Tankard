<!--
  README schema v3 SOURCE. Edit this file, never README.md: `node tools\readme\forge.js` writes README.md from it.
  Tokens are listed at the top of tools\readme\forge.js. Every setting name goes through {{key ...}}, and every number
  that lives in the code comes from {{gen ...}}, so the page cannot drift from the DLL.
-->
# 🍺 Tankard

{{rune ᚹ | TANKARD | ALE, MEAD AND REGRET | a0652a}} {{badge VERSION | {{version}} | 8b1e1e}} {{badge INSTALL ON | SERVER AND EVERY PLAYER | 1f4e79}}

| 🍺 **14 drinks** | 🧠 **Tolerance 0-69** | 🌑 **Blackouts** | 🤕 **Hangovers** |
|---|---|---|---|
| two of them legends | earned one cup at a time | wake far from home | a Bloody Ivar fixes most |

*"One more," said Ivar, and the hall agreed. Morning found him three valleys east, without his axe, without his map, and with no memory of the troll he had apparently insulted.*

> **⚠️ A word before the first round.** Tankard is a game about vikings drinking too much, played for laughs. Raven Iron does not condone the abuse of alcohol or drugs. In real life, please drink responsibly, never drink and drive, and if drinking is hurting you or someone you love, reach out for help.

### Drink with your crew. Wake up somewhere else.

Tankard adds real drinking to Valheim. Every cup goes onto a meter that climbs as you drink and falls as you sober. Climb it and your camera sways, your feet wander, your words slur, and you hit harder while blocking worse. Climb past the line and you black out: you wake far from the table, part of your pack still lying where you sat, and your map gone until you find your way back.

Strong drinks flatten a new viking and barely warm an old one. Tolerance is earned, a cup at a time, and the morning after always sends the bill.

{{rune ᛞ | NEW IN {{version}} | TWO LEGENDS AND A CUP | 8b1e1e}}

> **💀 The Valhalla**, the final boss of cocktails. What it does, we're not telling. **❄️ Skadi's Nightmare**, the gold-flake schnapps: while you're drunk on it, the wild leaves you alone. When it wears off, you freeze. **☕ Kaffi**, the way back: no alcohol, and it takes the edge off the night.

{{rune ᚾ | BEFORE YOU INSTALL | READ THIS | b8860b}}

| | |
|---|---|
| **Who needs it** | The server **and every player**, on exactly the same version. A player without it, or on another version, is turned away at the door with a message saying why. |
| **Blackouts are real** | You wake hundreds of metres away, some of your things stay behind in a grave, and your map goes dark. Three days in a row and you lose everything. If your group does not want that, set {{key Blackouts}} off before anyone drinks. |
| **Needs** | BepInEx only. No Jotunn, no asset bundles. |
| **Removing it** | Tankard's drinks and bases disappear from chests, barrels and inventories when the mod is gone. Drink them first. |

{{rune ᚲ | I | THE BREW | a0652a}}

Five drinks come out of the **cauldron** in one go. Six strong ones are **brewed**: make a base at the **Mead Ketill**, put it in a fermenter under a roof, wait, and tap four cups. Stronger drinks take longer in the barrel. Every drink also gives a short buff, listed in its tooltip, and every drink can be set on a table with the **serving tray**, like a mead.

{{gen drinks}}

{{rune ᚢ | II | THE METER | a0652a}}

- **Every drink adds to one meter**, by its strength and your tolerance. Time takes it back down. Quitting does not sober you: the meter is saved on your character.
- **Six stages**: Sober, Buzzed, Tipsy, Drunk, Hammered, Blackout.
  - **Tipsy and up:** the camera leans and drifts, and your walk wanders while you move.
  - **Drunk:** you hit 10% harder and blocking costs 25% more stamina. From Hammered, 20% and 50%. Your buff bar calls it **Mead Courage**, then **Berserker's Swagger**.
  - **Hammered:** now and then you trip over your own feet.
  - **Chat** slurs as you drink, and turns to gibberish near the top. Numbers, names and punctuation stay readable.
- **Tolerance** is yours, per character, from 0 (*paperweight*) to 69 (*iron gut*). Every drink raises it a little, and it never falls. A Surtr's Kiss puts a new player on the floor. A veteran drinks three.
- **Food helps a little.** Eating while drunk takes a few points off the meter. Eating the same thing again helps less each time.

{{gen hits}}

{{rune ᛈ | III | THE BLACKOUT | a0652a}}

The meter keeps going past full. Any drink that leaves you past the line can black you out, and the further past, the more likely it gets.

- **You wake far away**, on dry land in a biome your world has already opened by beating its boss.
- **Some of your things stay behind**, in a grave where you were drinking. Ore stays too, as it would at a portal.
- **Your map is gone.** Open it and it starts to come back while you look, slowly if you are still drunk. Walk back to the drinking spot and it all returns at once.
- **Blackouts in a row** send you further each time. Three days in a row and you lose everything and wake at least 5 km out.
- **Dying is not a way out.** You do not wake at your bed. You wake a short walk from where you fell, still blacked out, with a twenty-minute hangover. Close by lies **your lost tankard**. Drink from it to remember everything and end the blackout. Leave it for five minutes and it is gone, and you walk home.
- **No bed until the hangover is gone.** Even after the blackout ends, a death wakes you near where you fell, not at your bed, until the blackout's hangover has run out or a Bloody Ivar has cured it.

{{rune ᛊ | IV | THE MORNING AFTER | a0652a}}

- **A binge that reached Drunk ends in a hangover**: less stamina and health regen for 5 to 15 minutes, by how far you went, and 20 after a blackout.
- **Dying does not cure it**, and a new hangover never shortens one that is already running.
- **The Bloody Ivar cures it**: raspberries, blueberries, a Brambleblush, dandelion and a strip of boar jerky, made at the cauldron. Hair of the dog. Drinking it never starts the next hangover.
- **☕ Kaffi clears your head**: mushrooms roasted over coal with a spoon of honey, made at the cauldron. No alcohol. One cup takes 40 off your meter, then you're jittery for 10 minutes and another cup does nothing for your head. It won't touch a hangover, a blackout or the Valhalla.

{{rune ᛏ | V | THE TWO LEGENDS | 8b1e1e}}

**💀 The Valhalla**, the final boss of cocktails. Late game: a Surtr's Kiss, a Ymir's Marrow, a Bragi's Tongue and a Swampmother's Wine, poured into one cup at the cauldron.

What it does is a surprise, and we'd like to keep it that way. Ask someone who's had one, if they remember.

- **Bring nothing you can't stand to lose.**
- **Don't make plans for a while.**
- **One is enough.** The hall won't pour you a second.

**❄️ Skadi's Nightmare**, the gold-flake schnapps. Mid game: coins for the gold, freeze glands, honey and thistle at the Mead Ketill, then a long wait in the fermenter, and only two cups to a base. 35%.

- **The wild leaves you alone** from the first cup until your meter drops below Buzzed, whatever else you drink. Not at the Blackout stage (95 and up): drink that far and the wild remembers you.
- **Bosses and raids don't care**, and anything you hit hits back.
- **When it wears off, you're Frozen for 10 minutes**, whatever the weather. A fire or a frost mead (vanilla's, or a Rimecask Mead) thaws you.

{{rune ᛃ | THE LEDGER | EVERY NUMBER | a0652a}}

The numbers below are read from the code when this page is built.

{{gen ledger}}

{{rune ᚠ | THE TOOLBOX | FIRST TEN MINUTES | b8860b}}

1. Build a **cauldron** and brew a few **Brambleblushes** from raspberries and honey.
2. Drink two and watch the world start to lean. Keep going and you find out where the line is.
3. Make a **Hive-Gold Mead** base at the Mead Ketill and put it in a fermenter under a roof.
4. Keep a **Bloody Ivar** for the morning.

**Admins only.** The `tankard` console command (F5) is for admins and testing: on a server, only a player on
`adminlist.txt` can run it. In single player you are your own admin.

| Console command | What it does |
|---|---|
| `tankard` or `tankard status` | Your meter, stage, tolerance, hangover, blackout and where your settings come from |
| `tankard find` | Where your lost tankard lies, how far and in which direction, and how long you have |
| `tankard map` | Ends a blackout and gives the map back |
| `tankard sober` | Clears the meter and the binge |
| `tankard tolerance 30` | Sets your tolerance (0-69). Without a number it shows it |

{{rune ᚷ | MULTIPLAYER | SHARE A ROUND | 1f4e79}}

| The server | Each player's game | An admin |
|---|---|---|
| Holds the settings and sends them to everyone | Runs that player's own meter, symptoms, chat and blackout | Can change any setting from a config manager in game, and it reaches everyone |
| Needs Tankard installed, or it deletes the drinks | Keeps only {{key CameraSway}} for itself | Is anyone on `adminlist.txt` while {{key LockConfiguration}} is on |

**Versions must match exactly.** Every player runs the same Tankard as the server, or is refused at the door with a message naming both versions. In single player your game is its own server and nothing changes.

{{rune ᛗ | COMPATIBILITY | LIVING TOGETHER | 2e5e3a}}

| Mod | |
|---|---|
| **Fatty** (Wubarrk) | ✅ Checked with Fatty 1.2.0. Every drink still works, and Fatty's repeat-meal rule also slows how much food sobers you. |
| **Mods that move your respawn point** | ⚠️ Tankard only moves a respawn that happens during a blackout. Another mod that also picks the spot may win or lose that fight. |
| **Map and minimap mods** | ⚠️ A blackout hides the map through vanilla's own switch. A mod that draws its own map may still show it. |
| **Other drinking mods** | ⚠️ Not tested. Two drunk meters at once will stack their symptoms. |

{{rune ᛚ | SETTINGS | TUNE THE FLOW | b8860b}}

🔒 **Follow the server** (everything below except {{key CameraSway}}):

| Setting | Default | What it does |
|---|---|---|
| {{key Enabled}} | on | Master switch |
| {{key LockConfiguration}} | on | Only admins may change settings from a player's game |
| {{key SoberPerSecond}} | 0.25 | How fast the meter falls |
| {{key FoodSobers}} | 1 | How much eating sobers you (0 off, 2 double) |
| {{key GarbleChat}} | on | Drunk chat |
| {{key GarbleStrength}} | 1 | How hard chat slurs (0-2) |
| {{key Wobble}} | on | Drunk walking |
| {{key Stumbles}} | on | Tripping when Hammered |
| {{key SymptomStrength}} | 1 | Scales sway, wobble and stumbles together (0-2) |
| {{key Blackouts}} | on | Blackouts at all |
| {{key BlackoutOver}} | 100 | Where the blackout line sits on the meter (25-195) |
| {{key LeavesGear}} | on | Whether a blackout leaves things behind |
| {{key MapRememberSeconds}} | 150 | How long it takes to remember the map, sober |
| {{key PreferExploredGround}} | off | Wake only on ground the world already built, to skip the hitch of new land |
| {{key DrinkEffects}} | on | Each drink's buff |
| {{key LiquidCourage}} | on | Hit harder, block worse when drunk |
| {{key Hangovers}} | on | Hangovers |
| {{key Valhalla}} | on | Everything the Valhalla does (off: just a 60% drink) |
| {{key SkadisNightmare}} | on | Skadi's calm and the Frozen morning (off: just a 35% drink) |
| {{key PerDrinkFermentTimes}} | on | Stronger drinks ferment longer (off: vanilla's 40 minutes) |

🖥️ **Your own** (each player keeps it, whatever the server says):

| Setting | Default | What it does |
|---|---|---|
| {{key CameraSway}} | on | The swaying camera. Turn it off if it makes you feel sick for real. |

{{rune ᛟ | CREDITS | THE HALL | c9a227}}

- Made by **Raven Iron**. Every Raven Iron mod is free, and stays free.
  - Website: <https://ravenirongames.com>
  - Patreon: <https://www.patreon.com/cw/RavenIronGames>
  - Discord: <https://discord.gg/AGKDEurAVa>
- **ServerSync** by blaxxun, used as published.
- **Special thanks** to **Sergeant StretchyNuts**, for his priceless expertise and the countless hours we spent testing the effects in real life.
