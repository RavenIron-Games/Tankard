using System;

namespace RavenIron.Tankard.Core
{
    /// <summary>How drunk a player is, as a named band. Ordered: a higher value is always drunker.</summary>
    public enum Tier
    {
        Sober = 0,
        Buzzed = 1,
        Tipsy = 2,
        Drunk = 3,
        Hammered = 4,
        Blackout = 5,
    }

    /// <summary>
    /// The intoxication meter, as pure arithmetic with no game types in it, so the test harness compiles this exact file.
    /// A player carries one number in [0, Max]. Each drink adds its potency, time takes it away, and the tier (and so
    /// every symptom) is read off the number. Nothing here knows about Valheim, a clock or a config file: callers pass
    /// the delta-time and the rates in, which is also what keeps the maths testable at the real default rates.
    /// </summary>
    public static class Intoxication
    {
        /// <summary>A FULL meter: symptoms peak here, and it is what the Blackout tier and the 0-100 numbers refer to.</summary>
        public const float Max = 100f;
        /// <summary>The ceiling. Since 2026-11-18 the meter runs past full: the part over the blackout limit is what makes
        /// a blackout likelier (CoreBlackout), and it takes longer to sober from. 200 is about 13 minutes at the default rate.</summary>
        public const float Cap = 200f;

        /// <summary>Lowest level of each tier, indexed by <see cref="Tier"/>. Sober starts at zero by definition.</summary>
        public static readonly float[] TierFloor = { 0f, 8f, 25f, 50f, 75f, 95f };

        /// <summary>Adds a drink. Negative or non-finite potency adds nothing; the result never leaves [0, Cap].</summary>
        public static float Add(float level, float potency)
        {
            level = IsFinite(level) ? Clamp(level) : 0f; // clamp BEFORE adding: -50 + 10 is 10, not 0
            if (!IsFinite(potency) || potency <= 0f) return level;
            return Clamp(level + potency);
        }

        /// <summary>
        /// Sobers up over <paramref name="dt"/> seconds at <paramref name="perSecond"/> units a second. A non-positive
        /// delta or rate changes nothing, so a paused game or a zeroed config can never push the level UP.
        /// </summary>
        public static float Decay(float level, float dt, float perSecond)
        {
            if (!IsFinite(level)) return 0f;
            if (!IsFinite(dt) || !IsFinite(perSecond) || dt <= 0f || perSecond <= 0f) return Clamp(level);
            return Clamp(level - dt * perSecond);
        }

        public static Tier TierOf(float level)
        {
            if (!IsFinite(level)) return Tier.Sober;
            for (int i = TierFloor.Length - 1; i > 0; i--)
                if (level >= TierFloor[i]) return (Tier)i;
            return Tier.Sober;
        }

        /// <summary>
        /// How hard the symptoms (camera sway, stagger) bite, 0 to 1. Zero until the player is properly Tipsy, then a
        /// smooth ramp, so one beer never wobbles the screen and the last stretch is the steep one.
        /// </summary>
        public static float Severity(float level)
        {
            float start = TierFloor[(int)Tier.Tipsy];
            if (!IsFinite(level) || level <= start) return 0f;
            float t = (Math.Min(level, Max) - start) / (Max - start);
            return t * t * (3f - 2f * t); // smoothstep
        }

        /// <summary>Seconds until a level has sobered to <paramref name="target"/> at a given rate; 0 if already there.</summary>
        public static float SecondsUntil(float level, float target, float perSecond)
        {
            if (!IsFinite(level) || !IsFinite(target) || !IsFinite(perSecond) || perSecond <= 0f) return 0f;
            return level <= target ? 0f : (level - target) / perSecond;
        }

        private static float Clamp(float v) => v < 0f ? 0f : (v > Cap ? Cap : v);
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
