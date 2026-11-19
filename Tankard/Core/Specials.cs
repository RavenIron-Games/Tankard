using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.Tankard.Core
{
    /// <summary>A drink with rules of its own beyond the meter and its buff.</summary>
    public enum Special
    {
        None,
        Valhalla,       // the final boss of cocktails
        SkadisNightmare, // the gold-flake schnapps
        Kaffi            // the way back: roasted barley, no alcohol
    }

    /// <summary>
    /// Kaffi, the Norse coffee (owner, 2026-11-19: "we dont have a way to clear your head or sober up", then "Kaffi is
    /// perfect"). Roasted barley, no alcohol. One cup takes a chunk off the meter and leaves you jittery; while the jitters
    /// last, another cup does nothing for your head. It never touches a hangover, a blackout, the Valhalla or tolerance,
    /// and since it holds no alcohol it neither earns a hangover nor eases the Valhalla.
    /// </summary>
    public static class Kaffi
    {
        public const float SoberPoints = 40f;
        public const float JitterSeconds = 600f;

        /// <summary>The meter after a cup: <see cref="SoberPoints"/> off, never below 0. Jittery: no change.</summary>
        public static float After(float level, bool jittery)
        {
            float l = float.IsNaN(level) || float.IsInfinity(level) || level < 0f ? 0f : (level > Intoxication.Cap ? Intoxication.Cap : level);
            if (jittery) return l;
            float next = l - SoberPoints;
            return next < 0f ? 0f : next;
        }
    }

    /// <summary>
    /// The Valhalla, as pure rules (owner, 2026-11-18: "the final boss of cocktails"). Mixed at the cauldron from the strong
    /// brewed drinks. One cup:
    ///  - fills the meter to the cap and ALWAYS blacks you out; you lose everything, as on the third day of a streak, and the
    ///    blackout counts toward the streak like any other;
    ///  - you wake somewhere dangerous in unlocked land (a crypt, a troll cave, a Fuling camp...), at any distance, carrying
    ///    a random handful of things that are not yours: mostly junk, about one time in ten something worth keeping;
    ///  - then a hangover of three GAME days (counted while you play): triple damage against creatures (never players),
    ///    double stamina cost and half stamina regen, all fading to nothing over the three days. Any drink eases the regen
    ///    penalty for a while (it never removes it) and still fills the meter as normal. The Bloody Ivar does not cure it,
    ///    death does not end it, and nobody may drink another Valhalla until it is over.
    /// </summary>
    public static class Valhalla
    {
        public const float Days = 3f;
        public const float DefaultDayLengthSeconds = 1800f; // EnvMan.m_dayLengthSec in vanilla
        public const float StartDamage = 3f;                // triple damage against creatures
        public const float StartStaminaUse = 1f;            // +100% stamina cost
        public const float StartRegen = 0.5f;               // stamina regen halved
        public const float ReliefShare = 0.5f;              // a drink halves the regen penalty while it lasts
        public const float ReliefBaseSeconds = 120f;
        public const float ReliefPerAbvSecond = 15f;        // a 40% drink: 2 + 10 = 12 minutes of relief

        public static float TotalSeconds(float dayLengthSeconds)
        {
            float d = float.IsNaN(dayLengthSeconds) || dayLengthSeconds <= 0f ? DefaultDayLengthSeconds : dayLengthSeconds;
            return d * Days;
        }

        /// <summary>How strong the hangover still is, 1 at the start down to 0 when it is over. Linear.</summary>
        public static float Strength(float left, float total)
        {
            if (float.IsNaN(left) || float.IsNaN(total) || total <= 0f || left <= 0f) return 0f;
            float s = left / total;
            return s > 1f ? 1f : s;
        }

        public static float DamageMultiplier(float strength) => 1f + (StartDamage - 1f) * Clamp01(strength);
        public static float StaminaUseExtra(float strength) => StartStaminaUse * Clamp01(strength);

        /// <summary>Stamina regen multiplier: halved at full strength, eased by a drink's relief, 1 when it is over.</summary>
        public static float RegenMultiplier(float strength, bool relief)
        {
            float penalty = (1f - StartRegen) * Clamp01(strength);
            if (relief) penalty *= ReliefShare;
            return 1f - penalty;
        }

        /// <summary>How long one drink eases the hangover, by its strength.</summary>
        public static float ReliefSeconds(float abv)
        {
            if (float.IsNaN(abv) || abv <= 0f) return 0f;
            return ReliefBaseSeconds + ReliefPerAbvSecond * abv;
        }

        /// <summary>Relief after a drink: the longer of what is left and what the new drink gives. Never stacks up.</summary>
        public static float ReliefAfterDrink(float reliefLeft, float abv)
        {
            float r = float.IsNaN(reliefLeft) || reliefLeft < 0f ? 0f : reliefLeft;
            float n = ReliefSeconds(abv);
            return n > r ? n : r;
        }

        // ---- saved on the character: seconds left, the total, relief left -------------------------------------------
        public const string Key = "Tankard.Valhalla";

        public struct State
        {
            public float Left, Total, Relief;
            public bool Active => Left > 0f && Total > 0f;
        }

        public static string Format(State s) =>
            string.Format(CultureInfo.InvariantCulture, "{0:R};{1:R};{2:R}", s.Left, s.Total, s.Relief);

        public static State Parse(string text)
        {
            var s = new State();
            if (string.IsNullOrEmpty(text)) return s;
            string[] p = text.Split(';');
            if (p.Length != 3) return s;
            s.Total = Clean(P(p[1]), 1e7f);
            s.Left = Clean(P(p[0]), s.Total);
            s.Relief = Clean(P(p[2]), 1e5f);
            return s;
        }

        /// <summary>The hangover after <paramref name="dt"/> seconds of play.</summary>
        public static State Tick(State s, float dt)
        {
            if (float.IsNaN(dt) || dt <= 0f || !s.Active) return s;
            s.Left = Math.Max(0f, s.Left - dt);
            s.Relief = Math.Max(0f, s.Relief - dt);
            if (s.Left <= 0f) { s.Left = 0f; s.Relief = 0f; }
            return s;
        }

        // ---- what you wake up holding ---------------------------------------------------------------------------
        public const int MinJunk = 3, MaxJunk = 5;
        public const double LuckyChance = 0.10;

        public sealed class Loot
        {
            public readonly string Item; public readonly int Min, Max;
            public Loot(string item, int min = 1, int max = 1) { Item = item; Min = min; Max = max; }
        }

        /// <summary>Vanilla item prefab names. A name the game cannot find is skipped and logged, never replaced.</summary>
        public static readonly IReadOnlyList<Loot> Junk = new List<Loot>
        {
            new Loot("ArmorRagsLegs"), new Loot("ArmorRagsChest"), new Loot("Club"), new Loot("Torch"),
            new Loot("Coins", 1, 12), new Loot("Stone", 1, 6), new Loot("Feathers", 1, 4), new Loot("Resin", 1, 5),
            new Loot("Wood", 1, 4), new Loot("Flint", 1, 3), new Loot("Mushroom", 1, 3), new Loot("Dandelion", 1, 3),
            new Loot("BoneFragments", 1, 4), new Loot("LeatherScraps", 1, 3), new Loot("TrophyBoar"), new Loot("TrophyDeer"),
            new Loot("Fish1"), new Loot("Raspberry", 1, 5), new Loot("HelmetLeather"), new Loot("Shield_Wood"),
        };

        /// <summary>The one-in-ten find: things from later biomes.</summary>
        public static readonly IReadOnlyList<Loot> Lucky = new List<Loot>
        {
            new Loot("Ruby", 1, 2), new Loot("AmberPearl", 1, 3), new Loot("SilverNecklace"), new Loot("Amber", 1, 4),
            new Loot("Coins", 40, 120), new Loot("Thunderstone"), new Loot("Silver", 1, 3), new Loot("BlackMetal", 1, 3),
            new Loot("MeadHealthMajor", 1, 2), new Loot("Iron", 1, 3),
        };

        /// <summary>
        /// Three to five things that are not yours: distinct junk, and with <see cref="LuckyChance"/> one of them swapped for
        /// a lucky find. Returned as (prefab, amount).
        /// </summary>
        public static List<KeyValuePair<string, int>> PickLoot(Random rng, IReadOnlyList<Loot> junk = null, IReadOnlyList<Loot> lucky = null,
            double luckyChance = LuckyChance)
        {
            var result = new List<KeyValuePair<string, int>>();
            junk = junk ?? Junk; lucky = lucky ?? Lucky;
            if (rng == null || junk.Count == 0) return result;
            int n = MinJunk + rng.Next(MaxJunk - MinJunk + 1);
            if (n > junk.Count) n = junk.Count;
            var idx = new int[junk.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            for (int i = 0; i < n; i++)
            {
                int j = i + rng.Next(idx.Length - i);
                int t = idx[i]; idx[i] = idx[j]; idx[j] = t;
                result.Add(Roll(junk[idx[i]], rng));
            }
            if (lucky.Count > 0 && rng.NextDouble() < luckyChance)
                result[rng.Next(result.Count)] = Roll(lucky[rng.Next(lucky.Count)], rng);
            return result;
        }

        private static KeyValuePair<string, int> Roll(Loot l, Random rng)
        {
            int lo = Math.Max(1, l.Min), hi = Math.Max(lo, l.Max);
            return new KeyValuePair<string, int>(l.Item, lo + rng.Next(hi - lo + 1));
        }

        // ---- where you wake: somewhere you shouldn't be -----------------------------------------------------------
        /// <summary>
        /// Parts of vanilla location prefab names that mean trouble: burial chambers and sunken crypts, troll caves, frost
        /// caves, greydwarf and Fuling camps, swamp huts, drake nests, Dvergr towns and towers, infested mines, the Ashlands
        /// fortresses. Matched case-insensitively against ZoneSystem's location prefab names, server-side.
        /// </summary>
        public static readonly string[] DangerParts =
        {
            "Crypt", "TrollCave", "MountainCave", "Greydwarf_camp", "GoblinCamp", "SwampHut", "SwampRuin", "DrakeNest",
            "StoneTowerRuins", "Dvergr", "GuardTower", "Infested", "Charred", "Fortress", "MorgenHole",
        };

        public static bool IsDangerous(string locationName) => DangerKind(locationName) != null;

        /// <summary>The kind of dangerous place a location is (the DangerParts entry it matches, so Crypt2 and Crypt4 are
        /// both "Crypt"), or null when it is not one.</summary>
        public static string DangerKind(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return null;
            foreach (string part in DangerParts)
                if (locationName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return part;
            return null;
        }

        /// <summary>A kind first, then one place of that kind (owner, 2026-11-19). Picking straight from every place let
        /// the commonest kind win: a swamp full of infested trees sent three wakes in a row to one. False when empty.</summary>
        public static bool TryPickByKind<T>(IDictionary<string, List<T>> byKind, Random rng, out T pick)
        {
            pick = default(T);
            if (byKind == null || rng == null) return false;
            var kinds = new List<string>();
            foreach (var kv in byKind) if (kv.Value != null && kv.Value.Count > 0) kinds.Add(kv.Key);
            if (kinds.Count == 0) return false;
            kinds.Sort(StringComparer.Ordinal); // a fixed order, so a seeded rng picks the same way every time
            List<T> places = byKind[kinds[rng.Next(kinds.Count)]];
            pick = places[rng.Next(places.Count)];
            return true;
        }

        /// <summary>How far from the dangerous place itself you come round: close enough to be in trouble, not inside it.</summary>
        public const float WakeMin = 20f, WakeMax = 45f;

        private static float Clamp01(float f) => float.IsNaN(f) || f < 0f ? 0f : (f > 1f ? 1f : f);
        private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        private static float Clean(float f, float max) => float.IsNaN(f) || float.IsInfinity(f) || f < 0f ? 0f : (f > max ? max : f);
    }

    /// <summary>
    /// Skadi's Nightmare, as pure rules (owner, 2026-11-18: "supposed to mimic goldschlager ... animals are friendly while
    /// you are drunk, not blackout"). From the first cup until the meter drops below Buzzed, whatever else you drink, every
    /// creature but bosses and raid waves leaves you alone; one you hit fights back. Off during a blackout. The moment the
    /// calm ends you are Frozen for ten minutes ("Frozen morning"), whatever the weather, unless a fire or a warming mead
    /// thaws you first.
    /// </summary>
    public static class Skadi
    {
        public const string Key = "Tankard.Skadi";          // "1" while the calm is owed: drunk on it, not yet sober
        public const string FrozenKey = "Tankard.SkadiFrozen"; // seconds of Frozen morning left
        public const float FrozenSeconds = 600f;
        /// <summary>The player ZDO flag every creature's owner reads (a plain bool, synced with the player).</summary>
        public const string CalmZdoKey = "tk_skadi_calm";
        /// <summary>A creature's ZDO flag, per player, once that player has hit it.</summary>
        public static string ProvokedZdoKey(long playerId) =>
            "tk_skadi_prov_" + playerId.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Calm right now: drunk on it, from Buzzed up to (not into) the Blackout stage, and not passed out. The owner,
        /// Storm10 2026-11-19: "end it at the blackout stage". The first build kept it on through the Blackout stage
        /// (95+) and only dropped it in a real blackout, so a player at 98.9 was still left alone.
        /// </summary>
        public static bool CalmActive(bool owed, float level, bool blackout)
        {
            if (!owed || blackout) return false;
            Tier t = Intoxication.TierOf(level);
            return t >= Tier.Buzzed && t < Tier.Blackout;
        }

        /// <summary>The calm is spent: the meter has dropped below Buzzed. That is when the Frozen morning lands.</summary>
        public static bool CalmSpent(bool owed, float level) =>
            owed && Intoxication.TierOf(level) < Tier.Buzzed;

        /// <summary>
        /// Whether a creature leaves a calm player alone. Bosses never do, raid waves never do, and one the player has hit
        /// fights back. Everything else does.
        /// </summary>
        public static bool LeavesAlone(bool playerCalm, bool isBoss, bool isRaid, bool provoked) =>
            playerCalm && !isBoss && !isRaid && !provoked;

        public static float FrozenAfter(float left, float dt)
        {
            float l = float.IsNaN(left) || left < 0f ? 0f : left;
            if (float.IsNaN(dt) || dt <= 0f) return l;
            return Math.Max(0f, l - dt);
        }

        public static float ParseSeconds(string s)
        {
            if (string.IsNullOrEmpty(s) || !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || f < 0f)
                return 0f;
            return f > FrozenSeconds ? FrozenSeconds : f;
        }

        public static string FormatSeconds(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    }
}
