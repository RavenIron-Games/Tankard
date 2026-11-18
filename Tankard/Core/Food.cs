using System;

namespace RavenIron.Tankard.Core
{
    /// <summary>
    /// Eating while drinking sobers you up a little (the owner, 2026-11-18: "food sober you a little up if you eat while
    /// drinking"). A meal knocks points off the meter in proportion to how filling it is: a tenth of its health, stamina
    /// and eitr added up, at least <see cref="MinPerMeal"/> for anything that counts as food, and never more than
    /// <see cref="MaxPerMeal"/>. A handful of berries takes 2 off, cooked meat 4, a big end-game meal 10. That is "a
    /// little" on a 100-point meter: a feast pulls you down about one tier at most, never sober in one bite.
    /// Pure, like the rest of Core.
    /// </summary>
    public static class Food
    {
        public const float PerFoodPoint = 0.1f;
        public const float MinPerMeal = 2f;
        public const float MaxPerMeal = 10f;

        /// <summary>Meter points one meal takes off. 0 for something that is not food (no health, stamina or eitr).</summary>
        public static float Sobering(float health, float stamina, float eitr, float strength = 1f)
        {
            float total = Clean(health) + Clean(stamina) + Clean(eitr);
            if (total <= 0f) return 0f;
            float s = Clean(strength);
            if (s <= 0f) return 0f;
            float points = total * PerFoodPoint;
            if (points < MinPerMeal) points = MinPerMeal;
            if (points > MaxPerMeal) points = MaxPerMeal;
            return points * s;
        }

        /// <summary>The meter after a meal taking <paramref name="points"/> off. Never below 0 or above the meter's max.
        /// (Intoxication.Add refuses a negative amount by design, so eating has its own path.)</summary>
        public static float Eat(float level, float points)
        {
            float l = float.IsNaN(level) || float.IsInfinity(level) || level < 0f ? 0f : (level > Intoxication.Max ? Intoxication.Max : level);
            float next = l - Clean(points);
            return next < 0f ? 0f : next;
        }

        private static float Clean(float v) => float.IsNaN(v) || float.IsInfinity(v) || v < 0f ? 0f : v;
    }
}
