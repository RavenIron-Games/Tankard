using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.Tankard.Core
{
    /// <summary>
    /// The blackout, as pure rules. Blood alcohol decides it (owner, 2026-11-18: "12 drinks is a little unbalanced. It
    /// should be BAC related"), replacing the 2026-11-17 rule of "from the 12th drink of a binge":
    ///  - The meter runs past full (100) up to <see cref="Intoxication.Cap"/>. Everything over the LIMIT (100 by default)
    ///    is "over the limit", and only a drink that leaves you over it can black you out.
    ///  - The further over, the likelier: 10% just over, +1% per point (so 60% at 150, certain from 190).
    ///  - You wake 300 m away plus 25 m per point over, and leave 25% of your stacks plus 1% per point over (all at 175).
    ///  - Tolerance matters by itself: a hardened drinker takes less from each cup, so it takes them more to get over.
    ///  - You only ever wake in a biome your world's boss kills have opened, on dry land.
    ///  - Your gear stays where you drank (vanilla's tombstone), and your map is gone until you get back there.
    /// No game types here: the world is passed in as two functions, so the harness can run the picker on a fake map.
    /// </summary>
    public static class Blackout
    {
        public const float DefaultLimit = 100f;       // the meter level past which a drink can black you out
        public const double FirstChance = 0.10;      // a drink that leaves you only just over the limit
        public const double ChancePerPoint = 0.01;
        public const float FirstDistance = 300f;     // metres from the drinking spot, just over the limit
        public const float DistancePerPoint = 25f;
        public const float WorldRadius = 10000f;     // WorldGenerator.worldSize (decompiled)
        public const float EdgeMargin = 500f;         // never wake at the edge of the world
        public const float HomeRadius = 50f;          // how close to the drinking spot counts as home again

        /// <summary>Points over the limit at this meter level: 0 at or under it, never negative or NaN.</summary>
        public static float Over(float level, float limit = DefaultLimit)
        {
            if (float.IsNaN(level) || float.IsInfinity(level) || float.IsNaN(limit) || float.IsInfinity(limit)) return 0f;
            float o = level - limit;
            return o > 0f ? o : 0f;
        }

        /// <summary>Chance of blacking out on a drink that leaves you <paramref name="over"/> points over the limit.</summary>
        public static double Chance(float over)
        {
            if (float.IsNaN(over) || over <= 0f) return 0.0;
            double c = FirstChance + ChancePerPoint * over;
            return c > 1.0 ? 1.0 : c;
        }

        /// <summary>How far from the drinking spot you wake, <paramref name="over"/> points over. Capped by the world.</summary>
        public static float Distance(float over)
        {
            if (float.IsNaN(over) || over < 0f) over = 0f;
            float d = FirstDistance + DistancePerPoint * over;
            float max = WorldRadius - EdgeMargin;
            return d > max ? max : d;
        }

        // ---- blackouts in a row ------------------------------------------------------------------------------------
        // The owner, 2026-11-17: "if you have multiple blackouts you get farther away and the more drunk you are the more
        // shit you lose. 3 days in a row you lose everything and go far away."
        // A streak is a run of consecutive in-game days each with at least one blackout. Every earlier blackout in the
        // streak (same day included) adds half the base distance again; the third day in a row takes everything and
        // throws you at least 5 km. A day with no blackout ends it.

        public const int TotalLossDays = 3;
        public const float StreakDistanceStep = 0.5f;
        public const float FarDistance = 5000f;

        public struct Streak
        {
            public int LastDay;  // in-game day of the last blackout (EnvMan.GetDay), -1 for none
            public int Days;     // consecutive days with a blackout, ending on LastDay
            public int Count;    // blackouts in the streak, all days

            public static Streak None => new Streak { LastDay = -1, Days = 0, Count = 0 };
            public bool IsTotal => Days >= TotalLossDays;
        }

        /// <summary>The streak after a blackout on <paramref name="today"/>. Pure: call it, then save the result only once the blackout has really happened.</summary>
        public static Streak Advance(Streak s, int today)
        {
            if (today < 0) today = 0;
            if (s.Days <= 0 || s.LastDay < 0) return new Streak { LastDay = today, Days = 1, Count = 1 };
            if (today == s.LastDay) return new Streak { LastDay = today, Days = s.Days, Count = s.Count + 1 };
            if (today == s.LastDay + 1) return new Streak { LastDay = today, Days = s.Days + 1, Count = s.Count + 1 };
            return new Streak { LastDay = today, Days = 1, Count = 1 }; // a gap, or the clock went backwards: a fresh start
        }

        /// <summary>Where you wake, given how far over the limit you were and the streak this blackout belongs to (already advanced).</summary>
        public static float DistanceFor(float over, Streak s)
        {
            float d = Distance(over) * (1f + StreakDistanceStep * Math.Max(0, s.Count - 1));
            if (s.IsTotal && d < FarDistance) d = FarDistance;
            float max = WorldRadius - EdgeMargin;
            return d > max ? max : d;
        }

        /// <summary>The share of your stacks left behind, given how far over the limit you were and the streak (already advanced).</summary>
        public static double LossFor(float over, Streak s) =>
            s.IsTotal ? 1.0 : LossFraction(over);

        public static string FormatStreak(Streak s) =>
            string.Format(CultureInfo.InvariantCulture, "{0};{1};{2}", s.LastDay, s.Days, s.Count);

        public static Streak ParseStreak(string text)
        {
            if (string.IsNullOrEmpty(text)) return Streak.None;
            string[] p = text.Split(';');
            if (p.Length != 3 ||
                !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int last) ||
                !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) ||
                !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                last < 0 || days <= 0 || count < days)
                return Streak.None;
            return new Streak { LastDay = last, Days = days, Count = count };
        }

        public static string StreakKeyFor(string worldId) => "Tankard.BlackoutStreak." + (string.IsNullOrEmpty(worldId) ? "unknown" : worldId);

        // ---- what you leave behind -------------------------------------------------------------------------------

        public const double FirstLoss = 0.25;      // a quarter of your stacks just over the limit
        public const double LossPerPoint = 0.01;   // the owner, 2026-11-17: "the more you drink ... the more you lose"

        /// <summary>Fraction of your item stacks left in the pile at the drinking spot. 0 at or under the limit, 1 at most.</summary>
        public static double LossFraction(float over)
        {
            if (float.IsNaN(over) || over <= 0f) return 0.0;
            double f = FirstLoss + LossPerPoint * over;
            return f > 1.0 ? 1.0 : f;
        }

        /// <summary>
        /// Which of <paramref name="count"/> stacks are left behind: ceil(fraction x count) of them, chosen uniformly at
        /// random (a partial Fisher-Yates shuffle). Indices into the caller's list, distinct, in no particular order.
        /// Items that must be left anyway (ore cannot teleport) are the caller's business and do not count against this.
        /// </summary>
        public static List<int> PickLost(int count, double fraction, Random rng)
        {
            var result = new List<int>();
            if (count <= 0 || rng == null || double.IsNaN(fraction) || fraction <= 0.0) return result;
            if (fraction > 1.0) fraction = 1.0;
            int k = (int)Math.Ceiling(fraction * count - 1e-9);
            if (k > count) k = count;
            var idx = new int[count];
            for (int i = 0; i < count; i++) idx[i] = i;
            for (int i = 0; i < k; i++)
            {
                int j = i + rng.Next(count - i);
                int t = idx[i]; idx[i] = idx[j]; idx[j] = t;
                result.Add(idx[i]);
            }
            return result;
        }

        // ---- biomes opened by boss kills ------------------------------------------------------------------------

        // Heightmap.Biome's values (decompiled from 1.0.x). Kept as plain ints so Core has no game types; the
        // registration layer casts. The harness pins these numbers against the list here.
        public const int Meadows = 1, Swamp = 2, Mountain = 4, BlackForest = 8, Plains = 0x10, AshLands = 0x20,
                         DeepNorth = 0x40, Ocean = 0x100, Mistlands = 0x200;

        /// <summary>The world's boss keys, in progression order, and the biome each one opens.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, int>> BossOpens = new List<KeyValuePair<string, int>>
        {
            new KeyValuePair<string, int>("defeated_eikthyr", BlackForest),
            new KeyValuePair<string, int>("defeated_gdking", Swamp),
            new KeyValuePair<string, int>("defeated_bonemass", Mountain),
            new KeyValuePair<string, int>("defeated_dragon", Plains),
            new KeyValuePair<string, int>("defeated_goblinking", Mistlands),
            new KeyValuePair<string, int>("defeated_queen", AshLands),
            new KeyValuePair<string, int>("defeated_fader", DeepNorth),
        };

        /// <summary>Biome mask you may wake in. Meadows always; each boss kill adds the biome it opens. Never Ocean.</summary>
        public static int AllowedBiomes(Func<string, bool> hasKey)
        {
            int mask = Meadows;
            if (hasKey == null) return mask;
            foreach (var kv in BossOpens)
                if (hasKey(kv.Key)) mask |= kv.Value;
            return mask;
        }

        // ---- landing ---------------------------------------------------------------------------------------------

        public const int Attempts = 400;

        /// <summary>
        /// Picks where you wake: on a ring around the drinking spot at about <paramref name="distance"/> (80% to 120%),
        /// in an allowed biome, at least a metre above sea level, inside the world. If the ring has no such ground (an
        /// island in the open sea, say) it widens towards the spot itself rather than dropping you in the water.
        /// Returns false only if nothing anywhere on any ring qualifies; the caller then leaves you where you are.
        /// </summary>
        /// <param name="minDistance">
        /// Optional floor, in metres from the spot. A total-loss day must land at least <see cref="FarDistance"/> away (the
        /// owner: "go far away"), but the 80-120% ring around a 5 km aim landed 4897 m and 4859 m out in game (2026-11-18).
        /// With a floor every ring radius is at least the floor and the ring never shrinks towards the spot. Only when no
        /// ground beyond the floor qualifies at all does it fall back to the plain search, so you still wake somewhere.
        /// </param>
        /// <param name="preferred">
        /// Optional. Ground that should win if any of it fits (in the game: zones the world has already generated, so
        /// waking there builds no new land; the owner reported lag right after a blackout, and the log showed new
        /// locations and dungeons being generated at each landing). A full pass is tried with it first, then a full pass
        /// without it, so a world with little generated land still sends you somewhere. Checked BEFORE biome and height,
        /// because in the game it is a hash lookup and they are noise functions.
        /// </param>
        public static bool PickLanding(float homeX, float homeZ, float distance, Random rng,
            Func<float, float, int> biomeAt, Func<float, float, float> heightAt, float waterLevel, int allowedMask,
            out float x, out float z, out float y, Func<float, float, bool> preferred = null, float minDistance = 0f)
        {
            x = homeX; z = homeZ; y = 0f;
            if (rng == null || biomeAt == null || heightAt == null || float.IsNaN(distance) || distance <= 0f) return false;
            if (float.IsNaN(homeX) || float.IsNaN(homeZ)) return false;
            float floor = float.IsNaN(minDistance) || minDistance < 0f ? 0f : minDistance;
            if (floor > 0f)
            {
                if (preferred != null &&
                    PickOnRings(homeX, homeZ, distance, rng, biomeAt, heightAt, waterLevel, allowedMask, preferred, floor, out x, out z, out y))
                    return true;
                if (PickOnRings(homeX, homeZ, distance, rng, biomeAt, heightAt, waterLevel, allowedMask, null, floor, out x, out z, out y))
                    return true;
            }
            if (preferred != null &&
                PickOnRings(homeX, homeZ, distance, rng, biomeAt, heightAt, waterLevel, allowedMask, preferred, 0f, out x, out z, out y))
                return true;
            return PickOnRings(homeX, homeZ, distance, rng, biomeAt, heightAt, waterLevel, allowedMask, null, 0f, out x, out z, out y);
        }

        private static bool PickOnRings(float homeX, float homeZ, float distance, Random rng,
            Func<float, float, int> biomeAt, Func<float, float, float> heightAt, float waterLevel, int allowedMask,
            Func<float, float, bool> preferred, float floor, out float x, out float z, out float y)
        {
            x = homeX; z = homeZ; y = 0f;
            float limit = WorldRadius - EdgeMargin;

            for (int i = 0; i < Attempts; i++)
            {
                // The first half of the attempts honour the distance; after that the ring shrinks step by step towards
                // the spot, so a blackout on a small island still lands you on that island.
                // It shrinks to 2% of the distance, i.e. nearly the drinking spot itself, which is always dry land (you drank
                // there). Shrinking only to 10% left a 200 m island unreachable from a 2 km blackout (the harness caught it).
                // With a floor the ring never shrinks: it may only reach past the aim, never fall short of the floor.
                float shrink = floor > 0f || i < Attempts / 2 ? 1f : 1f - (i - Attempts / 2) / (float)(Attempts / 2) * 0.98f;
                double angle = rng.NextDouble() * Math.PI * 2.0;
                float r = distance * shrink * (0.8f + 0.4f * (float)rng.NextDouble());
                if (r < floor) r = floor + (float)rng.NextDouble() * floor * 0.2f;
                float cx = homeX + (float)Math.Cos(angle) * r;
                float cz = homeZ + (float)Math.Sin(angle) * r;
                if (cx * cx + cz * cz > limit * limit) continue;
                if (preferred != null && !preferred(cx, cz)) continue;

                int biome = biomeAt(cx, cz);
                if ((biome & allowedMask) == 0 || (biome & Ocean) != 0) continue;
                float h = heightAt(cx, cz);
                if (float.IsNaN(h) || h < waterLevel + 1f) continue;

                x = cx; z = cz; y = h;
                return true;
            }
            return false;
        }

        // ---- the saved state: where home is, which is also "your map is off" ------------------------------------------

        /// <summary>The drinking spot, written into the player's saved custom data. InvariantCulture: never a comma decimal.</summary>
        public static string FormatHome(float x, float y, float z) =>
            string.Format(CultureInfo.InvariantCulture, "{0:R};{1:R};{2:R}", x, y, z);

        public static bool TryParseHome(string s, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split(';');
            if (parts.Length != 3) return false;
            const NumberStyles style = NumberStyles.Float;
            if (!float.TryParse(parts[0], style, CultureInfo.InvariantCulture, out x)) return false;
            if (!float.TryParse(parts[1], style, CultureInfo.InvariantCulture, out y)) return false;
            if (!float.TryParse(parts[2], style, CultureInfo.InvariantCulture, out z)) return false;
            return !(float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z));
        }

        /// <summary>Back home: within <see cref="HomeRadius"/> of the drinking spot, measured flat (height ignored).</summary>
        public static bool IsHome(float px, float pz, float homeX, float homeZ)
        {
            float dx = px - homeX, dz = pz - homeZ;
            return dx * dx + dz * dz <= HomeRadius * HomeRadius;
        }

        /// <summary>How far you must have been from the drinking spot before reaching it again counts as coming home.</summary>
        public const float ArmRadius = HomeRadius * 2f;

        /// <summary>
        /// The coming-home check, as a two-state machine. It stays DISARMED until you have been seen more than
        /// <see cref="ArmRadius"/> from the spot while not mid-teleport, and only an armed check can give the map back.
        /// Found in the first in-game run (2026-11-17): vanilla's teleport waits 2 s before it moves you, the check ran
        /// in that window, saw you still standing at the drinking spot and restored the map at once.
        /// </summary>
        public static bool ShouldRestoreMap(ref bool armed, bool teleporting, float px, float pz, float homeX, float homeZ)
        {
            if (teleporting) return false;
            float dx = px - homeX, dz = pz - homeZ;
            float d2 = dx * dx + dz * dz;
            if (!armed)
            {
                if (d2 > ArmRadius * ArmRadius) armed = true;
                return false;
            }
            return d2 <= HomeRadius * HomeRadius;
        }

        // ---- remembering the map ----------------------------------------------------------------------------------
        // The owner, 2026-11-17: "the longer you look the more the map comes back, because you start remembering".
        // The big map opens under a fog; only time spent LOOKING at it (the large map open) clears the fog. When it is
        // clear you remember everything and the minimap returns too. Walking home still restores it at once.

        // The owner after the first fog run: "the map comes back too fast, slow it down, you don't immediately sober up
        // from a blackout". So: 5 minutes of looking when sober (was 90 s), and the drunker you still are, the slower
        // it comes: at full drunk a tenth of the speed. You remember as you sober.
        // Retuned the same evening: "the map comes back too slow at tipsy, you should start remembering". So the rate
        // steps by TIER rather than sliding with severity, and Tipsy is where it really starts. 150 s of looking when
        // sober (was 300), about three minutes at Tipsy, and next to nothing while still drunk.
        public const float DefaultRememberSeconds = 150f;

        /// <summary>How fast memory returns at a meter level: by tier, so crossing into Tipsy is the moment it comes back.</summary>
        public static float RememberRate(float level)
        {
            switch (Intoxication.TierOf(level)) // NaN and negatives read as Sober there
            {
                case Tier.Sober:
                case Tier.Buzzed: return 1f;
                case Tier.Tipsy: return 0.8f;
                case Tier.Drunk: return 0.15f;
                default: return 0.05f; // Hammered, Blackout: you are in no state to remember anything
            }
        }

        /// <summary>Progress after looking for <paramref name="dt"/> more seconds at meter <paramref name="level"/>. Never falls, never passes 1.</summary>
        public static float Remember(float progress, float dt, float secondsToRemember, float level = 0f)
        {
            if (float.IsNaN(progress) || progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;
            if (float.IsNaN(dt) || dt <= 0f || float.IsNaN(secondsToRemember) || secondsToRemember <= 0f) return progress;
            // A frame hitch is not a quarter-minute of memory.
            float p = progress + Math.Min(dt, 0.25f) * RememberRate(level) / secondsToRemember;
            return p > 1f ? 1f : p;
        }

        /// <summary>
        /// Fog opacity over the map for a progress. Fully opaque at 0, clear at 1. Ease-in: the first glances bring back
        /// little, then it comes in a rush, the way a memory does. Never below 0.97 opacity in the first 10%, so a quick
        /// peek cannot read your own marker off the map.
        /// </summary>
        public static float FogAlpha(float progress)
        {
            if (float.IsNaN(progress) || progress <= 0f) return 1f;
            if (progress >= 1f) return 0f;
            // Ease in, but gentler than the first p^2: at half remembered the fog is about two-thirds, not three-quarters,
            // so the land shows through while you are still working on it. p^1.6 keeps a 10% peek at 0.975.
            return 1f - (float)Math.Pow(progress, 1.6);
        }

        public static readonly float[] MemoryMilestones = { 0.25f, 0.5f, 0.75f };

        public static readonly string[] MemoryLines =
        {
            "Bits of the land are coming back to you...",
            "You start to remember where you've been.",
            "It's nearly all there now.",
        };

        /// <summary>Index of the milestone crossed between two progress values, or -1. At most one per call.</summary>
        public static int MilestoneCrossed(float before, float after)
        {
            for (int i = MemoryMilestones.Length - 1; i >= 0; i--)
                if (before < MemoryMilestones[i] && after >= MemoryMilestones[i]) return i;
            return -1;
        }

        public static string FormatProgress(float p) => p.ToString("R", CultureInfo.InvariantCulture);

        public static float ParseProgress(string s)
        {
            if (string.IsNullOrEmpty(s) || !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float p) || float.IsNaN(p))
                return 0f;
            return p < 0f ? 0f : (p > 1f ? 1f : p);
        }

        public static string MemoryKeyFor(string worldId) => "Tankard.BlackoutMemory." + (string.IsNullOrEmpty(worldId) ? "unknown" : worldId);

        /// <summary>The custom-data key for one world, so a blackout in one world never hides the map in another.</summary>
        public static string KeyFor(string worldId) => "Tankard.BlackoutHome." + (string.IsNullOrEmpty(worldId) ? "unknown" : worldId);

        // ---- the long walk home ---------------------------------------------------------------------------------
        // The owner, 2026-11-18, after dying a second time and waking at his bed: drinking the lost tankard ended the
        // blackout, so the next death was an ordinary one and vanilla sent him home. "Drink the tankard, then die" was a free
        // trip home. His choice: no bed until the blackout's hangover is over. A blackout starts the long walk; until it ends,
        // every death wakes you near where you fell (as in a blackout) but drops no lost tankard and leaves the map alone.

        public static string LongWalkKeyFor(string worldId) => "Tankard.LongWalk." + (string.IsNullOrEmpty(worldId) ? "unknown" : worldId);

        /// <summary>
        /// Whether the long walk is over: the blackout has ended, and its hangover has been and gone (run out, or cured by a
        /// Bloody Ivar). <paramref name="hangoverOwed"/> is a blacked-out binge still running: its 20-minute hangover arrives
        /// when the meter reaches zero, so the walk is not over before it. With hangovers off there is nothing to wait for,
        /// and the walk ends with the blackout.
        /// </summary>
        public static bool LongWalkOver(bool blackoutActive, bool hangoversOn, float hangoverLeft, bool hangoverOwed)
        {
            if (blackoutActive) return false;
            if (!hangoversOn) return true;
            return !(hangoverLeft > 0f) && !hangoverOwed; // NaN counts as none
        }
    }
}
