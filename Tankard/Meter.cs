using RavenIron.Tankard.Core;

namespace RavenIron.Tankard
{
    /// <summary>
    /// The local player's intoxication level, and how many drinks the current binge holds. A plain number driven from
    /// Plugin.Update: no timers, no coroutine of its own (house rule 2). It lives on the drinker's own client, which is
    /// where the symptoms render and where the blackout decides.
    ///
    /// A binge is every drink since you were last fully sober: the count resets when the meter reaches zero.
    /// Not persisted: the meter and the binge reset when the game closes. (The blackout's map lock IS persisted; see
    /// BlackoutRunner.)
    /// </summary>
    internal static class Meter
    {
        public static float Level { get; private set; }
        public static int BingeDrinks { get; private set; }
        public const float WakeLevel = 90f;
        public static Tier Tier => Intoxication.TierOf(Level);

        /// <summary>Symptom severity 0 to 1, with the SymptomStrength setting applied. Zero below Tipsy.</summary>
        public static float SymptomSeverity
        {
            get
            {
                float s = Intoxication.Severity(Level) * Plugin.SymptomStrength.Value;
                return s < 0f ? 0f : (s > 1f ? 1f : s);
            }
        }

        public static void Drink(DrinkDef drink)
        {
            float before = Level;
            Level = Intoxication.Add(Level, drink.Potency);
            BingeDrinks++;
            Plugin.Log.LogInfo($"Drank {drink.DisplayName}: meter {before:0.0} -> {Level:0.0} ({Tier}), drink {BingeDrinks} of this binge.");
        }

        public static void Tick(float dt)
        {
            if (Level <= 0f) return;
            Tier before = Tier;
            Level = Intoxication.Decay(Level, dt, Plugin.SoberPerSecond.Value);
            if (Tier != before) Plugin.Log.LogInfo($"Meter {Level:0.0}: {before} -> {Tier}.");
            if (Level <= 0f && BingeDrinks > 0)
            {
                Plugin.Log.LogInfo($"Sober again; the binge of {BingeDrinks} drink(s) is over.");
                BingeDrinks = 0;
            }
        }

        /// <summary>
        /// After a blackout you come round still hammered (the owner: "you don't immediately sober up from a blackout"),
        /// about five minutes from sober at the default rate, but the binge that did it is spent.
        /// </summary>
        public static void WakeFromBlackout()
        {
            // 90, not the Hammered floor of 75: at exactly the floor the next tick dropped you to Drunk, so "wake
            // hammered" lasted one frame (seen in the log 2026-11-17). 90 is a minute of Hammered, then Drunk, then the
            // long way down: about six minutes to sober at the default rate.
            Level = WakeLevel;
            BingeDrinks = 0;
        }

        public static void Reset()
        {
            Level = 0f;
            BingeDrinks = 0;
        }
    }
}
