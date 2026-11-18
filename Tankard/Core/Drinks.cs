using System.Collections.Generic;

namespace RavenIron.Tankard.Core
{
    /// <summary>How a drink is made: crafted at a station in one go, or fermented over time like vanilla mead base.</summary>
    public enum BrewPath
    {
        Station,
        Fermenter,
    }

    /// <summary>One drink, as data. No game types, so the harness can check the table itself.</summary>
    public sealed class DrinkDef
    {
        public string PrefabName;   // the prefab and ObjectDB name, unique, and never changed once shipped (saves store it)
        public string DisplayName;  // English; the localisation hook maps the token onto this
        public string DonorPrefab;  // vanilla consumable the item is cloned from (see Registration, to be built)
        public BrewPath Path;
        public float Potency;       // meter units added per drink
        public int FermentSeconds;  // Fermenter path only; 0 for Station drinks
        public string Blurb;
    }

    public static class Drinks
    {
        // DonorPrefab names are the vanilla prefabs the drinks are cloned from. They are NOT verified against a running
        // game yet: the ObjectDB lookup logs an error naming any donor it cannot find, which is the check.
        public static readonly IReadOnlyList<DrinkDef> All = new List<DrinkDef>
        {
            new DrinkDef { PrefabName = "TK_Ale", DisplayName = "Ale", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Potency = 10f, Blurb = "Thin and cheap. A warm start to the evening." },
            new DrinkDef { PrefabName = "TK_BlueberryWine", DisplayName = "Blueberry wine", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Potency = 14f, Blurb = "Sweet enough that you forget to count." },
            new DrinkDef { PrefabName = "TK_HoneyMead", DisplayName = "Honey mead", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Fermenter, Potency = 18f, FermentSeconds = 1200, Blurb = "Aged in the fermenter. Goes down easy." },
            new DrinkDef { PrefabName = "TK_SkaldicMead", DisplayName = "Skaldic mead", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Fermenter, Potency = 30f, FermentSeconds = 2400, Blurb = "The kind a skald sings after. Not for a short night." },
        };
    }
}
