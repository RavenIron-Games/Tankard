using System.Collections.Generic;

namespace RavenIron.Tankard.Core
{
    /// <summary>How a drink is made: crafted at a station in one go, or fermented over time like vanilla mead base.</summary>
    public enum BrewPath
    {
        Station,
        Fermenter,
    }

    /// <summary>The vanilla crafting stations the recipes use. Resolved to a prefab by name in the registration layer.</summary>
    public enum StationKind
    {
        Cauldron,
        MeadKetill,
    }

    /// <summary>One ingredient line: a vanilla item prefab name and how many.</summary>
    public sealed class Ingredient
    {
        public readonly string Item;
        public readonly int Amount;
        public Ingredient(string item, int amount) { Item = item; Amount = amount; }
    }

    /// <summary>One drink, as data. No game types, so the harness can check the table itself.</summary>
    public sealed class DrinkDef
    {
        public string PrefabName;   // the prefab and ObjectDB name, unique, and never changed once shipped (saves store it)
        public string DisplayName;  // English; the localisation hook maps the token onto this
        public string DonorPrefab;  // vanilla consumable the item is cloned from
        public BrewPath Path;
        public float Potency;       // meter units added per drink
        public string Blurb;

        // What it costs to make. Station drinks are crafted from these directly (CraftAmount per craft). Fermenter drinks
        // craft their BASE from these at the Mead Ketill (one base per craft, like vanilla), then the fermenter turns one
        // base into FermentYield drinks. The fermenter's duration is one value per barrel, not per drink (decompiled from
        // 1.0.x), so there is deliberately no per-drink ferment time.
        public Ingredient[] Ingredients = new Ingredient[0];
        public int CraftAmount = 1;
        public int FermentYield;

        public string NameToken => "$tk_" + PrefabName.ToLowerInvariant();
        public string DescriptionToken => NameToken + "_desc";

        // The intermediate item a fermented drink is made from. Null for station drinks.
        public string BasePrefabName => Path == BrewPath.Fermenter ? PrefabName + "Base" : null;
        public string BaseNameToken => BasePrefabName == null ? null : "$tk_" + BasePrefabName.ToLowerInvariant();
        public string BaseDescriptionToken => BaseNameToken == null ? null : BaseNameToken + "_desc";
        public string BaseDisplayName => DisplayName + " base";
    }

    /// <summary>Everything the registration layer must create as an item: a drink, or the base of a fermented one.</summary>
    public sealed class ItemSpec
    {
        public string PrefabName;
        public string DonorPrefab;
        public string NameToken;
        public string DescriptionToken;
        public bool IsDrink;      // drinks are consumable; bases are materials that only the fermenter takes
        public DrinkDef Drink;    // the drink this item is, or is the base of
    }

    /// <summary>One crafting recipe the registration layer must add.</summary>
    public sealed class RecipeSpec
    {
        public string Name;           // the Recipe asset name
        public string ResultPrefab;   // the item produced
        public int Amount;
        public StationKind Station;
        public Ingredient[] Ingredients;
    }

    /// <summary>One fermenter conversion: one base in, Yield drinks out.</summary>
    public sealed class ConversionSpec
    {
        public string FromPrefab;
        public string ToPrefab;
        public int Yield;
    }

    public static class Drinks
    {
        // Donor and ingredient names are vanilla prefab names, NOT verified against a running game yet: the registration layer
        // logs an error naming any it cannot find, and withholds a recipe rather than publishing a cheaper one.
        public static readonly IReadOnlyList<DrinkDef> All = new List<DrinkDef>
        {
            // ---- the cauldron: quick, weak, made in one go -----------------------------------------------------------
            new DrinkDef { PrefabName = "TK_RaspberryCider", DisplayName = "Raspberry cider", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Potency = 8f, Blurb = "Barely counts as a drink. Everyone starts somewhere.",
                Ingredients = new[] { new Ingredient("Raspberry", 8), new Ingredient("Honey", 1) }, CraftAmount = 2 },
            new DrinkDef { PrefabName = "TK_Ale", DisplayName = "Ale", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Potency = 10f, Blurb = "Thin and cheap. A warm start to the evening.",
                Ingredients = new[] { new Ingredient("Barley", 4) }, CraftAmount = 2 },
            new DrinkDef { PrefabName = "TK_ForestGrog", DisplayName = "Forest grog", DonorPrefab = "MeadEitrMinor",
                Path = BrewPath.Station, Potency = 12f, Blurb = "Carrots and thistle, boiled until it stops arguing.",
                Ingredients = new[] { new Ingredient("Carrot", 4), new Ingredient("Thistle", 2) }, CraftAmount = 2 },
            new DrinkDef { PrefabName = "TK_BlueberryWine", DisplayName = "Blueberry wine", DonorPrefab = "MeadPoisonResist",
                Path = BrewPath.Station, Potency = 14f, Blurb = "Sweet enough that you forget to count.",
                Ingredients = new[] { new Ingredient("Blueberries", 8), new Ingredient("Honey", 1) }, CraftAmount = 2 },

            // ---- the fermenter: a base from the Mead Ketill, then time. One per biome, each stronger -----------------
            new DrinkDef { PrefabName = "TK_HoneyMead", DisplayName = "Honey mead", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Fermenter, Potency = 18f, Blurb = "Aged in the fermenter. Goes down easy.",
                Ingredients = new[] { new Ingredient("Honey", 6), new Ingredient("Raspberry", 4) }, FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_BogWine", DisplayName = "Bog wine", DonorPrefab = "MeadPoisonResist",
                Path = BrewPath.Fermenter, Potency = 22f, Blurb = "Turnip, honey and something from a leech. Don't ask which part.",
                Ingredients = new[] { new Ingredient("Turnip", 6), new Ingredient("Bloodbag", 2), new Ingredient("Honey", 4) }, FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_Frostmead", DisplayName = "Frostmead", DonorPrefab = "MeadFrostResist",
                Path = BrewPath.Fermenter, Potency = 26f, Blurb = "Cold going down, a fire once it lands.",
                Ingredients = new[] { new Ingredient("Onion", 4), new Ingredient("FreezeGland", 2), new Ingredient("Honey", 6) }, FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_SkaldicMead", DisplayName = "Skaldic mead", DonorPrefab = "MeadHealthMedium",
                Path = BrewPath.Fermenter, Potency = 30f, Blurb = "The kind a skald sings after. Not for a short night.",
                Ingredients = new[] { new Ingredient("Honey", 8), new Ingredient("Barley", 4), new Ingredient("Cloudberry", 4) },
                FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_JotunDraught", DisplayName = "Jotun's draught", DonorPrefab = "MeadEitrMinor",
                Path = BrewPath.Fermenter, Potency = 36f, Blurb = "Brewed from the mist itself. Giants drink it to feel small.",
                Ingredients = new[] { new Ingredient("Sap", 6), new Ingredient("RoyalJelly", 4), new Ingredient("MushroomJotunPuffs", 4) },
                FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_FireWine", DisplayName = "Fire wine", DonorPrefab = "MeadHealthMedium",
                Path = BrewPath.Fermenter, Potency = 42f, Blurb = "Vineberries from the burning lands. Two of these and the floor is lava.",
                Ingredients = new[] { new Ingredient("Vineberry", 8), new Ingredient("Honey", 6) }, FermentYield = 4 },
        };

        /// <summary>The vanilla item a base is cloned from. A base must be a material the fermenter takes, not a consumable.</summary>
        public const string BaseDonorPrefab = "MeadBaseHealthMinor";

        public static List<ItemSpec> Items()
        {
            var list = new List<ItemSpec>();
            foreach (var d in All)
            {
                list.Add(new ItemSpec { PrefabName = d.PrefabName, DonorPrefab = d.DonorPrefab, NameToken = d.NameToken,
                    DescriptionToken = d.DescriptionToken, IsDrink = true, Drink = d });
                if (d.Path == BrewPath.Fermenter)
                    list.Add(new ItemSpec { PrefabName = d.BasePrefabName, DonorPrefab = BaseDonorPrefab, NameToken = d.BaseNameToken,
                        DescriptionToken = d.BaseDescriptionToken, IsDrink = false, Drink = d });
            }
            return list;
        }

        public static List<RecipeSpec> Recipes()
        {
            var list = new List<RecipeSpec>();
            foreach (var d in All)
            {
                bool station = d.Path == BrewPath.Station;
                list.Add(new RecipeSpec
                {
                    Name = "Recipe_" + (station ? d.PrefabName : d.BasePrefabName),
                    ResultPrefab = station ? d.PrefabName : d.BasePrefabName,
                    Amount = station ? d.CraftAmount : 1,
                    Station = station ? StationKind.Cauldron : StationKind.MeadKetill,
                    Ingredients = d.Ingredients,
                });
            }
            return list;
        }

        public static List<ConversionSpec> Conversions()
        {
            var list = new List<ConversionSpec>();
            foreach (var d in All)
                if (d.Path == BrewPath.Fermenter)
                    list.Add(new ConversionSpec { FromPrefab = d.BasePrefabName, ToPrefab = d.PrefabName, Yield = d.FermentYield });
            return list;
        }
    }

    public static class Words
    {
        /// <summary>The English strings for every item, keyed by localisation token (without the leading dollar sign).</summary>
        public static Dictionary<string, string> English()
        {
            var words = new Dictionary<string, string>();
            foreach (var d in Drinks.All)
            {
                words[d.NameToken.Substring(1)] = d.DisplayName;
                words[d.DescriptionToken.Substring(1)] = d.Blurb;
                if (d.Path == BrewPath.Fermenter)
                {
                    words[d.BaseNameToken.Substring(1)] = d.BaseDisplayName;
                    words[d.BaseDescriptionToken.Substring(1)] = "Put it in a fermenter under a roof and wait. It makes " + d.FermentYield + " " + d.DisplayName.ToLowerInvariant() + ".";
                }
            }
            return words;
        }

        /// <summary>The drink a name token belongs to, or null. A BASE's token finds nothing: bases are not drinkable.</summary>
        public static DrinkDef ByNameToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            foreach (var d in Drinks.All)
                if (d.NameToken == token) return d;
            return null;
        }
    }
}
