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

        /// <summary>The character's alcohol tolerance, 0 to 69. Saved in Player.m_customData: your body, so it follows the
        /// character across worlds (unlike the blackout keys, which are per world).</summary>
        public static float ToleranceOf(Player p)
        {
            if (p == null) return 0f;
            p.m_customData.TryGetValue(Tolerance.Key, out string s);
            return Tolerance.Parse(s);
        }

        public static void Drink(DrinkDef drink, Player p)
        {
            float before = Level;
            float tol = ToleranceOf(p);
            float hit = Tolerance.Hit(drink.Abv, tol);
            Level = Intoxication.Add(Level, hit);
            BingeDrinks++;
            float tolAfter = Tolerance.AfterDrink(tol, drink.Abv);
            if (p != null) p.m_customData[Tolerance.Key] = Tolerance.Format(tolAfter);
            Plugin.Log.LogInfo($"Drank {drink.DisplayName} ({drink.Abv:0}%): +{hit:0.0} at tolerance {tol:0.00}, meter {before:0.0} -> {Level:0.0} " +
                               $"({Tier}), drink {BingeDrinks} of this binge. Tolerance now {tolAfter:0.00}.");
            int shownBefore = Tolerance.Shown(tol), shownAfter = Tolerance.Shown(tolAfter);
            if (shownAfter > shownBefore && p != null)
            {
                try
                {
                    string text = shownAfter >= (int)Tolerance.Max
                        ? $"Tolerance {shownAfter}: iron gut. You can't get any harder."
                        : $"Tolerance {shownAfter} ({Tolerance.Describe(tolAfter)})";
                    p.Message(MessageHud.MessageType.TopLeft, text);
                }
                catch (System.Exception ex) { Plugin.Report("Tolerance message", ex); }
            }
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
