# Changelog

## 0.1.0 (unreleased)

- Scaffold. The plugin loads, binds `Enabled` and `SoberPerSecond`, and logs the four-drink table. The intoxication
  meter (add, decay, tiers, symptom severity) is written and covered by the off-game harness.
- The four drinks (Ale, Blueberry wine, Honey mead, Skaldic mead) are registered as items, cloned from a vanilla mead,
  with English names. Drinking one adds its potency to the local player's meter, which sobers at `SoberPerSecond`; each
  drink and each tier change is logged. A drink does nothing else yet: no symptoms, no recipe to make one, and the
  meter is not saved across a relog. Use `spawn TK_Ale` (devcommands) to get one.
