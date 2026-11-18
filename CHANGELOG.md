# Changelog

## 0.1.0 (unreleased)

- Scaffold. The plugin loads, binds `Enabled` and `SoberPerSecond`, and logs the four-drink table. The intoxication
  meter (add, decay, tiers, symptom severity) is written and covered by the off-game harness.
- The four drinks (Ale, Blueberry wine, Honey mead, Skaldic mead) are registered as items, cloned from a vanilla mead,
  with English names. Drinking one adds its potency to the local player's meter, which sobers at `SoberPerSecond`; each
  drink and each tier change is logged. A drink does nothing else yet: no symptoms, no recipe to make one, and the
  meter is not saved across a relog. Use `spawn TK_Ale` (devcommands) to get one.
- You can now make them. **Ale** (4 Barley, makes 2) and **Blueberry wine** (8 Blueberries and 1 Honey, makes 2) are
  crafted at the cauldron. **Honey mead** and **Skaldic mead** are fermented: craft a base at the Mead Ketill
  (Honey 6 and Raspberry 4, or Honey 8, Barley 4 and Cloudberry 4), put it in a fermenter under a roof, and tap four
  drinks out. A fermented drink hits harder than any cauldron one. All fermenter drinks take the barrel's normal time:
  the game keeps one fermentation time per barrel, not per drink. A recipe whose ingredient or station can't be found
  is left out and logged, never added cheaper.
- **Drunk chat.** Once you are properly Tipsy, what you say, shout or whisper comes out slurred: stretched vowels and
  hiccups at first, wrong keys and dropped letters next, and near full the line is gibberish with the shape of a
  sentence. Spaces, numbers, punctuation, rich-text tags and non-English letters are never touched, and no word
  vanishes. You see your own garbled line too. Two settings: `GarbleChat` (on/off) and `GarbleStrength` (0 to 2).
  They are read on the drinker's own game.
- **You feel it.** Once Tipsy, the camera leans, bobs, drifts off your aim and breathes in and out. Your walk
  wanders side to side, but only while you're moving. Once hammered, you sometimes trip with vanilla's stagger, never
  twice within 8 seconds, and never while swimming, steering, sitting or in the air. All of it is smooth (a slow lean,
  never a jolt) and scales with how drunk you are. Settings: `CameraSway`, `Wobble`, `Stumbles`, `SymptomStrength`.
- **Blackouts.** From the 12th drink of one binge (every drink since you were last fully sober), each drink has a chance
  to black you out: 10% on the 12th, 10% more on every drink after. You wake on dry land in a biome your world's boss
  kills have opened, 300 m away on the 12th and 250 m further for every drink after. A share of your things stays
  in a grave at the drinking spot: a quarter of your stacks on the 12th, 10% more per drink, everything by the 20th.
  Ore and anything else that can't go through a portal always stays. Your map is gone until you get back within
  50 m of the spot, so you'll need a friend to bring you home. That survives a relog, and each world tracks its own.
  A world set to keep inventory on death keeps it here too. Settings: `Blackouts`, `MinDrinks`, `LeavesGear`.
- **Remembering the map.** The big map opens under a fog that clears only while you look at it: you start
  remembering. It takes about 2½ minutes of looking when sober and about 3 once Tipsy, which is where it really
  starts; while Drunk or worse it barely moves. You come round from a blackout still Hammered. The minimap comes back when the fog has
  cleared, and walking back to the grave still restores everything at once. Setting: `MapRememberSeconds`.
- **Blackouts in a row.** Every earlier blackout in a streak (consecutive in-game days with at least one blackout,
  same-day ones included) sends you half as far again. On the third day in a row you lose everything and wake at
  least 5 km away. A day without a blackout ends the streak.
- `tankard status | map | sober` in the console: see the meter, blackout, memory and streak; end a stuck blackout;
  sober up.
- **Six more drinks, ten in all**, climbing the biomes. From the cauldron: Raspberry cider (8 Raspberry + 1 Honey),
  Ale, Forest grog (4 Carrot + 2 Thistle), Blueberry wine. From the fermenter, each stronger than the last: Honey
  mead, Bog wine (Turnip, Bloodbag, Honey), Frostmead (Onion, Freeze gland, Honey), Skaldic mead, Jotun's draught
  (Sap, Royal jelly, Jotun puffs), Fire wine (Vineberry, Honey). They clone different vanilla meads, so they look
  different in the inventory.
- `PreferExploredGround` (off by default): a blackout prefers ground the world has already generated, which avoids
  the hitch of building new land as you wake, at the cost of landing only where the world has been loaded.
- **Alcohol content and tolerance.** Every drink now has an ABV, shown in its tooltip: cider 4%, ale 5%, grog 7%,
  blueberry wine 11%, honey mead 12%, bog wine 14%, frostmead 17%, skaldic mead 20%, Jotun's draught 28%, fire wine
  40%. Each character also has an alcohol tolerance from 0 to 69. A drink's hit is its ABV x 2 for a new drinker, so one
  fire wine (80) puts a lightweight at Hammered, and it shrinks to 40% of that at 69 (fire wine 32, ale 4). Every drink
  raises your tolerance by a tenth of its ABV, more slowly the higher you are, and it never falls or passes 69. A message
  tells you when it goes up. It is saved with the character, not the world. `tankard status` shows it, and
  `tankard tolerance [0-69]` reads or sets it for testing.
- **Food steadies you.** Eating real food while you have anything on the meter takes a little off: a tenth of the
  food's health + stamina + eitr, at least 2 and at most 10 (raspberries about 3, cooked meat 4, a big meal 10). A
  feast pulls you down about one tier at most, never sober in one bite, and it doesn't reset the binge count. A
  message says so, with the tier change if there is one. Setting: `FoodSobers` (0 off, 1 normal, up to 3).
- **Drinks do something.** Every drink gives a buff for 5 to 15 minutes, listed in its tooltip: cider and honey mead
  heal faster, ale and skaldic mead restore stamina faster, forest grog and Jotun's draught carry more, blueberry wine
  restores eitr, and bog wine, frostmead and fire wine make you resistant to poison, frost and fire. You can keep
  drinking while one is running.
- **Liquid courage.** While Drunk you hit 10% harder and blocking costs 25% more stamina; from Hammered, 20% and 50%.
- **Hangovers.** When a binge that reached Drunk is over you get a hangover: 40% less stamina regen, 25% less health
  regen, 5 to 15 minutes by how drunk you got, 20 after a blackout.
- **The Bloody Ivar** (5 Raspberry, 4 Blueberries, 1 Ale, 2 Dandelion and 1 Boar jerky at the cauldron, 6% alcohol) cures it.
- **Each fermented drink has its own barrel time**, longer the stronger it is: honey mead 20 minutes, bog wine 40,
  frostmead 50, skaldic mead 60, Jotun's draught 80, fire wine 100 (vanilla mead is 40).
- **Quitting no longer sobers you up.** The meter, the binge and the hangover are saved with the character, and no time
  passes while you're logged out.
- **Dying ends a blackout** and gives your map back. You have two graves to fetch instead.
- Settings: `DrinkEffects`, `LiquidCourage`, `Hangovers`, `PerDrinkFermentTimes`.
- **Blackouts follow your blood alcohol, not a drink count.** The meter now runs past full, up to 200, and anything over
  100 is "over the limit" (sobering from 200 takes about 13 minutes). Only a drink that leaves you over the limit can
  black you out: 10% just over, 1% more per point, so 60% at 150 and certain from 190. You wake 300 m away plus 25 m per
  point over, and leave a quarter of your stacks plus 1% per point (everything from 175). Tolerance shrinks every hit,
  so a hardened drinker needs four fire wines just to get over, while two flatten a new one, and twelve ciders never
  will. The streak rules are unchanged. `MinDrinks` is replaced by `BlackoutOver` (default 100).
- **The server's settings apply to everyone** (ServerSync). Every Tankard setting follows the server on each player's
  game, so nobody can switch blackouts off or sober up faster in their own file. The one exception is `CameraSway`,
  which each player keeps, so nobody is forced to play with a swaying camera that makes them feel sick. With
  `LockConfiguration` on (the default) only an admin can change settings from a player's game, and the change goes to
  everyone. A player's own config file is never overwritten. Every player must run exactly this Tankard version, or
  the server refuses them at join with a message naming both versions. `tankard status` says where the settings come
  from.
- **A repeat meal sobers less.** With a mod that lets you eat the same food again (Fatty), each extra serving takes off
  half as much as the one before: 100%, 50%, 25%, then 5%. That matches what Fatty gives you for each extra serving. So four steaks in a row now
  take about 7.5 off the meter, not 16. Without such a mod nothing changes, because vanilla never lets you eat the same food twice at once.
- **The third day of a blackout streak always throws you at least 5 km.** Before, the landing could fall up to 20%
  short of the aim, and two test blackouts woke 4897 m and 4859 m away. Only if there is no allowed dry land beyond
  5 km at all (a small island, say) does it fall back to the nearest ground it can find, so you still wake somewhere.
