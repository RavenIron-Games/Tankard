using System;
using System.Globalization;

namespace RavenIron.Tankard.Core
{
    /// <summary>
    /// Alcohol tolerance, decided by the owner 2026-11-18: "a functioning alcoholic stat that balances drinks. More
    /// drinks, higher stat, that maxes out at 69. So we can make the drinks have alcohol content so end game drinks for a
    /// new player gets them handled."
    ///
    ///  - Every drink has an ABV. Its hit on the meter is ABV x 2 at tolerance 0: a new player's first Fire wine (40%)
    ///    is 80 points, Blackout tier from one cup.
    ///  - Tolerance runs 0 to 69 and scales the hit down to <see cref="FloorAtMax"/> (40%) at the top: the same Fire wine
    ///    is 32 for a hardened drinker, and ale barely registers.
    ///  - Every drink raises tolerance by ABV / 10, slowing as you climb (times 1 - tol/80, so it still reaches 69) and
    ///    never past 69. Strong drinks train you faster.
    /// Pure, like the rest of Core: the caller owns where the number is saved (per CHARACTER: it is your body, not the world).
    /// </summary>
    public static class Tolerance
    {
        public const float Max = 69f;
        public const float HitPerAbv = 2f;
        public const float FloorAtMax = 0.4f;
        public const float GainPerAbv = 0.1f;
        public const float GainSlowdownAt = 80f; // > Max, so the last points still come, slowly

        /// <summary>Meter points a drink of <paramref name="abv"/> percent adds at tolerance <paramref name="tolerance"/>.</summary>
        public static float Hit(float abv, float tolerance)
        {
            if (float.IsNaN(abv) || abv <= 0f) return 0f;
            float t = Clean(tolerance) / Max;
            return abv * HitPerAbv * (1f - (1f - FloorAtMax) * t);
        }

        /// <summary>Tolerance after one more drink of <paramref name="abv"/> percent. Never falls, never passes 69.</summary>
        public static float AfterDrink(float tolerance, float abv)
        {
            float t = Clean(tolerance);
            if (float.IsNaN(abv) || abv <= 0f) return t;
            float next = t + abv * GainPerAbv * (1f - t / GainSlowdownAt);
            return next > Max ? Max : next;
        }

        /// <summary>The whole number a player sees. Floors, so "69" means truly maxed.</summary>
        public static int Shown(float tolerance) => (int)Math.Floor(Clean(tolerance));

        public static string Describe(float tolerance)
        {
            int n = Shown(tolerance);
            if (n >= 69) return "iron gut";
            if (n >= 50) return "seasoned";
            if (n >= 30) return "regular";
            if (n >= 10) return "getting there";
            return "lightweight";
        }

        public const string Key = "Tankard.Tolerance";

        public static string Format(float t) => Clean(t).ToString("R", CultureInfo.InvariantCulture);

        public static float Parse(string s)
        {
            if (string.IsNullOrEmpty(s) || !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) return 0f;
            return Clean(t);
        }

        private static float Clean(float t) => float.IsNaN(t) || float.IsInfinity(t) || t < 0f ? 0f : (t > Max ? Max : t);
    }
}
