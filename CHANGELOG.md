# Changelog

## 0.2.0

Two legends.

- **The Valhalla**, the final boss of cocktails. Late game, mixed at the cauldron from a Surtr's Kiss, a Ymir's Marrow, a Bragi's Tongue and a Swampmother's Wine.
  - One cup always blacks you out and takes everything into the grave. It counts toward your streak.
  - You wake somewhere dangerous in the lands your world has opened, carrying a few things that aren't yours: mostly junk, about one time in ten something good.
  - Then three game days of fury: triple damage against creatures (not players), double stamina cost and half stamina regen, fading as the days pass.
  - Any drink eases it for a while. The Bloody Ivar doesn't, death doesn't end it, and you can't drink another until it's over.
- **Skadi's Nightmare**, the gold-flake schnapps. Mid game, brewed from coins, freeze glands, honey and thistle; 35%, two cups to a base.
  - While you're drunk on it, every creature but bosses and raids leaves you alone. Anything you hit hits back. It stops at the Blackout stage.
  - When it wears off you're Frozen for 10 minutes, unless a fire or a frost mead thaws you.
- **Kaffi**, the Norse coffee: barley roasted over coal with honey, at the cauldron. No alcohol. One cup takes 40 off your meter, then 10 minutes of jitters during which another cup does nothing for your head. It leaves hangovers, blackouts and the Valhalla alone.
- A word before the first round, at the top of the README: Raven Iron does not condone the abuse of alcohol or drugs. Please drink responsibly.
- Two new settings, both on: **Valhalla** and **SkadisNightmare**.
- `tankard status` shows both, and `tankard sober` clears both (admins only).

## 0.1.0

The first release.

- **Eleven drinks.** Five from the cauldron (Brambleblush, Longhouse Ale, Rootstump Grog, Bilberry Dusk and the Bloody Ivar) and six brewed: a base from the Mead Ketill, aged in the fermenter (Hive-Gold Mead, Swampmother's Wine, Rimecask Mead, Bragi's Tongue, Ymir's Marrow, Surtr's Kiss). Each has its own icon and a short buff, and stronger drinks take longer in the barrel.
- **A drunk meter.** Every drink adds to it by its strength, time takes it back down, and it is saved on your character.
- **Tolerance, 0 to 69 per character.** It grows with every drink and never falls, so strong drinks flatten new players and barely warm veterans.
- **Being drunk:**
  - the camera sways and your walk wanders;
  - when Hammered you sometimes trip;
  - chat slurs into gibberish;
  - while drunk you hit harder and block worse.
- **Food sobers you a little**, and the same food again helps less each time.
- **Hangovers** after a binge that reached Drunk. The Bloody Ivar cures one. Dying never does.
- **Blackouts.** A drink past the line can knock you out:
  - You wake far away, in a biome your world has opened.
  - Part of your pack stays in a grave where you drank.
  - Your map is dark until you remember it or walk back.
  - Three days in a row costs everything.
  - Dying in a blackout wakes you nearby, never at your bed. Your lost tankard lies close by for five minutes, and drinking it ends the blackout.
  - Your bed waits until the blackout's hangover is gone.
- **The `tankard` console command, admins only:** status, find, map, sober, tolerance. On a server only a player on `adminlist.txt` can run it.
- **Server-synced settings**, locked to admins. A player can't join on a different Tankard version.
