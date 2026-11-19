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
        public float Abv;           // alcohol by volume, percent. The hit on the meter comes from this and tolerance (Core/Tolerance.cs)
        public float Potency => Tolerance.Hit(Abv, 0f); // the hit for a brand-new drinker (tolerance 0)
        public string Blurb;

        // What it costs to make. Station drinks are crafted from these directly (CraftAmount per craft). Fermenter drinks
        // craft their BASE from these at the Mead Ketill (one base per craft, like vanilla), then the fermenter turns one
        // base into FermentYield drinks. Vanilla keeps ONE fermenting time per barrel; Patch_Fermenter swaps in
        // FermentSeconds for whichever of our bases is inside (owner, 2026-11-18: "change the times to match the drinks").
        public Ingredient[] Ingredients = new Ingredient[0];
        public int CraftAmount = 1;
        public int FermentYield;
        public float FermentSeconds;   // world seconds in the barrel; fermenter drinks only

        public DrinkEffect Effect;     // what it does for you besides the meter; null for none
        public bool CuresHangover;     // the Bloody Ivar
        public Special Special;        // rules of its own (Core\Specials.cs): the Valhalla, Skadi's Nightmare
        public string SpecialLine;     // the tooltip line that says what those rules are

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
        public DrinkDef Drink;    // the drink this item is, or is the base of (null for the lost tankard)
        public string FallbackDonorPrefab; // tried when DonorPrefab is missing; null for none
        public bool IsLostTankard;

        /// <summary>The lost tankard a death in a blackout leaves near you (CoreLostTankard). Not a drink and not in Items().</summary>
        public static ItemSpec LostTankardSpec() => new ItemSpec
        {
            PrefabName = LostTankard.PrefabName, DonorPrefab = LostTankard.DonorPrefab, FallbackDonorPrefab = LostTankard.FallbackDonorPrefab,
            NameToken = LostTankard.NameToken, DescriptionToken = LostTankard.DescriptionToken, IsDrink = false, IsLostTankard = true,
        };
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
            new DrinkDef { PrefabName = "TK_Brambleblush", Effect = new DrinkEffect { Name = "Bramble Glow", Seconds = 300f, HealthRegen = 1.25f }, DisplayName = "Brambleblush", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Abv = 4f, Blurb = "Barely counts as a drink. Everyone starts somewhere.",
                Ingredients = new[] { new Ingredient("Raspberry", 8), new Ingredient("Honey", 1) }, CraftAmount = 2 },
            new DrinkDef { PrefabName = "TK_LonghouseAle", Effect = new DrinkEffect { Name = "Hearth Belly", Seconds = 600f, StaminaRegen = 1.25f }, DisplayName = "Longhouse Ale", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Abv = 5f, Blurb = "Thin and cheap. A warm start to the evening.",
                Ingredients = new[] { new Ingredient("Barley", 4) }, CraftAmount = 2 },
            new DrinkDef { PrefabName = "TK_RootstumpGrog", Effect = new DrinkEffect { Name = "Packmule Stride", Seconds = 600f, CarryWeight = 50f, RunStaminaDrain = -0.15f }, DisplayName = "Rootstump Grog", DonorPrefab = "MeadEitrMinor",
                Path = BrewPath.Station, Abv = 7f, Blurb = "Carrots and thistle, boiled until it stops arguing.",
                Ingredients = new[] { new Ingredient("Carrot", 4), new Ingredient("Thistle", 2) }, CraftAmount = 2 },
            new DrinkDef { PrefabName = "TK_BilberryDusk", Effect = new DrinkEffect { Name = "Twilight Wits", Seconds = 600f, EitrRegen = 1.3f }, DisplayName = "Bilberry Dusk", DonorPrefab = "MeadPoisonResist",
                Path = BrewPath.Station, Abv = 11f, Blurb = "Sweet enough that you forget to count.",
                Ingredients = new[] { new Ingredient("Blueberries", 8), new Ingredient("Honey", 1) }, CraftAmount = 2 },
            // The hangover cure (owner, 2026-11-18: "its called a Bloody Ivar"). Hair of the dog: a little alcohol, and the
            // morning goes away.
            new DrinkDef { PrefabName = "TK_BloodyIvar", Effect = new DrinkEffect { Name = "Ivar's Mercy", Seconds = 300f, HealthRegen = 1.25f }, CuresHangover = true,
                DisplayName = "Bloody Ivar", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Station, Abv = 6f, Blurb = "Berries, a Longhouse Ale, dandelion and a strip of boar jerky stirred in. Cures a hangover. Starts the next one.",
                // TK_LonghouseAle is our own item: Brewing resolves ingredients through ObjectDB, where Items registers it first.
                Ingredients = new[] { new Ingredient("Raspberry", 5), new Ingredient("Blueberries", 4), new Ingredient("TK_LonghouseAle", 1),
                                      new Ingredient("Dandelion", 2), new Ingredient("BoarJerky", 1) }, CraftAmount = 1 },

            // ---- the fermenter: a base from the Mead Ketill, then time. One per biome, each stronger -----------------
            new DrinkDef { PrefabName = "TK_HiveGoldMead", Effect = new DrinkEffect { Name = "Hive Blessing", Seconds = 600f, HealthRegen = 1.5f }, FermentSeconds = 1200f, DisplayName = "Hive-Gold Mead", DonorPrefab = "MeadHealthMinor",
                Path = BrewPath.Fermenter, Abv = 12f, Blurb = "Aged in the fermenter. Goes down easy.",
                Ingredients = new[] { new Ingredient("Honey", 6), new Ingredient("Raspberry", 4) }, FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_SwampmothersWine", Effect = new DrinkEffect { Name = "Mire Blood", Seconds = 600f, Resist = Resist.Poison }, FermentSeconds = 2400f, DisplayName = "Swampmother's Wine", DonorPrefab = "MeadPoisonResist",
                Path = BrewPath.Fermenter, Abv = 14f, Blurb = "Turnip, honey and something from a leech. Don't ask which part.",
                Ingredients = new[] { new Ingredient("Turnip", 6), new Ingredient("Bloodbag", 2), new Ingredient("Honey", 4) }, FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_RimecaskMead", Effect = new DrinkEffect { Name = "Rime Ward", Seconds = 600f, Resist = Resist.Frost }, FermentSeconds = 3000f, DisplayName = "Rimecask Mead", DonorPrefab = "MeadFrostResist",
                Path = BrewPath.Fermenter, Abv = 17f, Blurb = "Cold going down, a fire once it lands.",
                Ingredients = new[] { new Ingredient("Onion", 4), new Ingredient("FreezeGland", 2), new Ingredient("Honey", 6) }, FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_BragisTongue", Effect = new DrinkEffect { Name = "Saga Breath", Seconds = 900f, StaminaRegen = 1.5f, HealthRegen = 1.25f }, FermentSeconds = 3600f, DisplayName = "Bragi's Tongue", DonorPrefab = "MeadHealthMedium",
                Path = BrewPath.Fermenter, Abv = 20f, Blurb = "The kind a skald sings after. Not for a short night.",
                Ingredients = new[] { new Ingredient("Honey", 8), new Ingredient("Barley", 4), new Ingredient("Cloudberry", 4) },
                FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_YmirsMarrow", Effect = new DrinkEffect { Name = "Ymir's Shoulders", Seconds = 900f, CarryWeight = 150f, StaminaRegen = 1.25f }, FermentSeconds = 4800f, DisplayName = "Ymir's Marrow", DonorPrefab = "MeadEitrMinor",
                Path = BrewPath.Fermenter, Abv = 28f, Blurb = "Brewed from the mist itself. Giants drink it to feel small.",
                Ingredients = new[] { new Ingredient("Sap", 6), new Ingredient("RoyalJelly", 4), new Ingredient("MushroomJotunPuffs", 4) },
                FermentYield = 4 },
            new DrinkDef { PrefabName = "TK_SurtrsKiss", Effect = new DrinkEffect { Name = "Ember Gut", Seconds = 900f, Resist = Resist.Fire }, FermentSeconds = 6000f, DisplayName = "Surtr's Kiss", DonorPrefab = "MeadHealthMedium",
                Path = BrewPath.Fermenter, Abv = 40f, Blurb = "Vineberries from the burning lands. One of these and the floor is lava.",
                Ingredients = new[] { new Ingredient("Vineberry", 8), new Ingredient("Honey", 6) }, FermentYield = 4 },

            // ---- the two with rules of their own (owner, 2026-11-18) ------------------------------------------------
            // Skadi's Nightmare: the gold-flake schnapps. Mid game (frost glands from the Mountains), expensive (coins are
            // the gold flakes), long in the barrel, and only two cups to a base.
            new DrinkDef { PrefabName = "TK_SkadisNightmare", Special = Special.SkadisNightmare, FermentSeconds = 5400f,
                DisplayName = "Skadi's Nightmare", DonorPrefab = "MeadFrostResist",
                Path = BrewPath.Fermenter, Abv = 35f, Blurb = "Ice-clear, with gold flakes turning in it. Cold going down, then it burns.",
                SpecialLine = "While you're drunk on it, nothing wild will touch you. Bosses and raids don't care, and anything you hit hits back. " +
                              "When it wears off you're Frozen for 10 minutes. A fire or a frost mead thaws you.",
                Ingredients = new[] { new Ingredient("Coins", 25), new Ingredient("FreezeGland", 4), new Ingredient("Honey", 8), new Ingredient("Thistle", 2) },
                FermentYield = 2 },
            // The Valhalla, the final boss of cocktails: the strong brewed drinks poured into one cup at the cauldron.
            new DrinkDef { PrefabName = "TK_Valhalla", Special = Special.Valhalla, DisplayName = "The Valhalla", DonorPrefab = "MeadStaminaMedium",
                Path = BrewPath.Station, Abv = 60f, Blurb = "Everything strong in the hall, in one cup. Nobody remembers the second half of the night.",
                SpecialLine = "Puts you on the floor. You wake somewhere you shouldn't be, with nothing of yours and things that aren't. " +
                              "Then three days of fury: triple damage, double stamina cost, half the stamina back, fading as the days pass. " +
                              "Only more drink takes the edge off. The Bloody Ivar can't help, and nobody drinks another until it's over.",
                Ingredients = new[] { new Ingredient("TK_SurtrsKiss", 1), new Ingredient("TK_YmirsMarrow", 1), new Ingredient("TK_BragisTongue", 1),
                                      new Ingredient("TK_SwampmothersWine", 1) }, CraftAmount = 1 },
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
                words[d.DescriptionToken.Substring(1)] = d.Blurb + " (" + d.Abv.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% alcohol)" +
                    (d.Effect != null ? "\n" + d.Effect.Describe() + "." : "") + (d.CuresHangover ? "\nCures a hangover." : "") +
                    (string.IsNullOrEmpty(d.SpecialLine) ? "" : "\n" + d.SpecialLine);
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
