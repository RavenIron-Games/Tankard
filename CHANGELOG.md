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
