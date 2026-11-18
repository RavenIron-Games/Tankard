using RavenIron.Tankard.Core;

namespace RavenIron.Tankard
{
    /// <summary>
    /// The local player's intoxication level. A plain number driven from Plugin.Update: no timers, no coroutine of its own
    /// (house rule 2). It lives on the drinker's own client, which is where the symptoms will render.
    ///
    /// Not persisted yet: it resets when the game closes, and starts at zero on a new session. Deciding how it survives a
    /// relog (player custom data vs a status effect that carries it) is still open (CLAUDE.md).
    /// </summary>
    internal static class Meter
    {
        public static float Level { get; private set; }
        public static Tier Tier => Intoxication.TierOf(Level);

        public static void Drink(DrinkDef drink)
        {
            float before = Level;
            Level = Intoxication.Add(Level, drink.Potency);
            Plugin.Log.LogInfo($"Drank {drink.DisplayName}: meter {before:0.0} -> {Level:0.0} ({Tier}).");
        }

        public static void Tick(float dt)
        {
            if (Level <= 0f) return;
            Tier before = Tier;
            Level = Intoxication.Decay(Level, dt, Plugin.SoberPerSecond.Value);
            if (Tier != before) Plugin.Log.LogInfo($"Meter {Level:0.0}: {before} -> {Tier}.");
        }

        public static void Reset() => Level = 0f;
    }
}
