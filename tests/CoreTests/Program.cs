using System;
using System.Collections.Generic;
using System.Linq;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard.Tests
{
    internal static class Program
    {
        private static int _run, _failed;

        private static void Check(bool ok, string name)
        {
            _run++;
            if (ok) return;
            _failed++;
            Console.WriteLine("FAIL: " + name);
        }

        private static void Near(float actual, float expected, string name, float eps = 1e-4f) =>
            Check(Math.Abs(actual - expected) <= eps, $"{name} (got {actual}, expected {expected})");

        private static int Main()
        {
            // ---- Add ---------------------------------------------------------------------------------------------
            Near(Intoxication.Add(0f, 10f), 10f, "a drink adds its potency");
            Near(Intoxication.Add(95f, 30f), Intoxication.Max, "the meter caps at Max");
            Near(Intoxication.Add(20f, -5f), 20f, "negative potency adds nothing");
            Near(Intoxication.Add(20f, 0f), 20f, "zero potency adds nothing");
            Near(Intoxication.Add(20f, float.NaN), 20f, "NaN potency adds nothing");
            Near(Intoxication.Add(20f, float.PositiveInfinity), 20f, "infinite potency adds nothing");
            Near(Intoxication.Add(float.NaN, 10f), 10f, "a corrupt level restarts from sober");
            Near(Intoxication.Add(-50f, 10f), 10f, "a negative level clamps before adding");

            // ---- Decay -------------------------------------------------------------------------------------------
            Near(Intoxication.Decay(50f, 10f, 0.5f), 45f, "decay is rate x time");
            Near(Intoxication.Decay(3f, 100f, 0.5f), 0f, "decay floors at zero");
            Near(Intoxication.Decay(50f, 0f, 0.5f), 50f, "no time passed, no change");
            Near(Intoxication.Decay(50f, -5f, 0.5f), 50f, "negative dt never raises the level");
            Near(Intoxication.Decay(50f, 5f, -1f), 50f, "a negative rate never raises the level");
            Near(Intoxication.Decay(50f, 5f, 0f), 50f, "a zeroed rate holds the level");
            Near(Intoxication.Decay(float.NaN, 5f, 1f), 0f, "a corrupt level decays to sober");

            // ---- Tiers -------------------------------------------------------------------------------------------
            Check(Intoxication.TierOf(0f) == Tier.Sober, "0 is sober");
            Check(Intoxication.TierOf(7.99f) == Tier.Sober, "just under Buzzed is sober");
            Check(Intoxication.TierOf(8f) == Tier.Buzzed, "Buzzed starts at its floor");
            Check(Intoxication.TierOf(25f) == Tier.Tipsy, "Tipsy starts at its floor");
            Check(Intoxication.TierOf(50f) == Tier.Drunk, "Drunk starts at its floor");
            Check(Intoxication.TierOf(75f) == Tier.Hammered, "Hammered starts at its floor");
            Check(Intoxication.TierOf(95f) == Tier.Blackout, "Blackout starts at its floor");
            Check(Intoxication.TierOf(Intoxication.Max) == Tier.Blackout, "Max is Blackout");
            Check(Intoxication.TierOf(-3f) == Tier.Sober, "a negative level is sober");
            Check(Intoxication.TierOf(float.NaN) == Tier.Sober, "NaN is sober");
            Check(Intoxication.TierFloor.Length == Enum.GetValues(typeof(Tier)).Length, "one floor per tier");
            for (int i = 1; i < Intoxication.TierFloor.Length; i++)
                Check(Intoxication.TierFloor[i] > Intoxication.TierFloor[i - 1], $"tier floors ascend at {i}");

            // ---- Severity ----------------------------------------------------------------------------------------
            Near(Intoxication.Severity(0f), 0f, "sober has no symptoms");
            Near(Intoxication.Severity(Intoxication.TierFloor[(int)Tier.Tipsy]), 0f, "no symptoms until past Tipsy's floor");
            Near(Intoxication.Severity(Intoxication.Max), 1f, "full symptoms at Max");
            Near(Intoxication.Severity(float.NaN), 0f, "NaN has no symptoms");
            float prev = -1f; bool mono = true;
            for (float l = 0f; l <= Intoxication.Max; l += 0.5f)
            {
                float s = Intoxication.Severity(l);
                if (s < prev - 1e-6f || s < 0f || s > 1f) mono = false;
                prev = s;
            }
            Check(mono, "severity is monotonic and stays within [0, 1]");

            // ---- SecondsUntil ------------------------------------------------------------------------------------
            Near(Intoxication.SecondsUntil(50f, 0f, 0.5f), 100f, "time to sober at the given rate");
            Near(Intoxication.SecondsUntil(5f, 8f, 0.5f), 0f, "already below the target");
            Near(Intoxication.SecondsUntil(50f, 0f, 0f), 0f, "a zero rate returns 0, never infinity");

            // ---- The drink table ---------------------------------------------------------------------------------
            var names = new HashSet<string>();
            foreach (var d in Drinks.All)
            {
                Check(!string.IsNullOrEmpty(d.PrefabName) && d.PrefabName.StartsWith("TK_"), $"{d.PrefabName}: prefixed name");
                Check(names.Add(d.PrefabName), $"{d.PrefabName}: prefab names are unique");
                Check(d.PrefabName.IndexOfAny(new[] { ' ', '(' }) < 0, $"{d.PrefabName}: no space or '(' (ItemDrop truncates there)");
                Check(d.Potency > 0f && d.Potency < Intoxication.Max, $"{d.PrefabName}: potency inside the meter");
                Check(!string.IsNullOrEmpty(d.DonorPrefab), $"{d.PrefabName}: names a donor");
                Check(d.Path == BrewPath.Fermenter ? (d.FermentYield > 0 && d.CraftAmount == 1) : (d.FermentYield == 0 && d.CraftAmount > 0),
                      $"{d.PrefabName}: only fermenter drinks yield from a barrel, and a base is crafted one at a time");
                Check(d.Path == BrewPath.Fermenter == (d.BasePrefabName != null), $"{d.PrefabName}: only fermenter drinks have a base");
            }
            Check(Drinks.All.Count == 4, "four drinks in the first cut");

            bool hasStation = false, hasFermenter = false;
            float maxStation = 0f, minFerment = float.MaxValue;
            foreach (var d in Drinks.All)
            {
                if (d.Path == BrewPath.Station) { hasStation = true; maxStation = Math.Max(maxStation, d.Potency); }
                else { hasFermenter = true; minFerment = Math.Min(minFerment, d.Potency); }
            }
            Check(hasStation && hasFermenter, "both brewing paths are represented");
            // A fermented drink should outpunch a station one, or there is no reason to wait.
            Check(minFerment > maxStation, "every fermented drink is stronger than every station drink");

            // ---- Tokens and the English table -------------------------------------------------------------------------
            var tokens = new HashSet<string>();
            var english = Words.English();
            foreach (var d in Drinks.All)
            {
                Check(d.NameToken.StartsWith("$tk_") && d.NameToken == d.NameToken.ToLowerInvariant(), $"{d.PrefabName}: lowercase $tk_ name token");
                Check(tokens.Add(d.NameToken) && tokens.Add(d.DescriptionToken), $"{d.PrefabName}: tokens are unique");
                Check(english.ContainsKey(d.NameToken.Substring(1)) && english[d.NameToken.Substring(1)] == d.DisplayName,
                      $"{d.PrefabName}: English table carries the display name");
                Check(english.ContainsKey(d.DescriptionToken.Substring(1)) && !string.IsNullOrEmpty(english[d.DescriptionToken.Substring(1)]),
                      $"{d.PrefabName}: English table carries a description");
                Check(ReferenceEquals(Words.ByNameToken(d.NameToken), d), $"{d.PrefabName}: a consumed item's name token finds its drink");
            }
            // 4 drinks + 2 bases, each with a name and a description.
            Check(english.Count == Drinks.Items().Count * 2, "the English table has exactly a name and a description per item");
            foreach (var spec in Drinks.Items())
            {
                Check(english.ContainsKey(spec.NameToken.Substring(1)) && english.ContainsKey(spec.DescriptionToken.Substring(1)),
                      $"{spec.PrefabName}: its tokens are in the English table");
                if (!spec.IsDrink)
                    Check(Words.ByNameToken(spec.NameToken) == null, $"{spec.PrefabName}: a base is not drinkable, so it finds no drink");
            }
            Check(Words.ByNameToken("$item_meadhealthminor") == null, "vanilla mead is not mistaken for a drink");
            Check(Words.ByNameToken(null) == null && Words.ByNameToken("") == null, "null or empty token finds nothing");
            // A name must hash the same everywhere, so it can never be renamed after shipping. Pin the four we have.
            Check(string.Join(",", Drinks.All.Select(d => d.PrefabName)) == "TK_Ale,TK_BlueberryWine,TK_HoneyMead,TK_SkaldicMead",
                  "shipped prefab names are pinned (saves store them)");
            Check(string.Join(",", Drinks.Items().Select(i => i.PrefabName)) ==
                  "TK_Ale,TK_BlueberryWine,TK_HoneyMead,TK_HoneyMeadBase,TK_SkaldicMead,TK_SkaldicMeadBase",
                  "shipped item names, bases included, are pinned");

            // ---- Items, recipes, conversions: the three tables must agree --------------------------------------------
            var items = Drinks.Items();
            var itemNames = new HashSet<string>();
            foreach (var i in items)
            {
                Check(itemNames.Add(i.PrefabName), $"{i.PrefabName}: item names are unique across drinks and bases");
                Check(i.PrefabName.IndexOfAny(new[] { ' ', '(' }) < 0, $"{i.PrefabName}: no space or '('");
                Check(i.IsDrink ? i.DonorPrefab != Drinks.BaseDonorPrefab : i.DonorPrefab == Drinks.BaseDonorPrefab,
                      $"{i.PrefabName}: drinks clone a consumable and bases clone a material");
            }
            var recipes = Drinks.Recipes();
            var recipeNames = new HashSet<string>();
            Check(recipes.Count == Drinks.All.Count, "one recipe per drink: itself, or its base");
            foreach (var r in recipes)
            {
                Check(recipeNames.Add(r.Name), $"{r.Name}: recipe names are unique");
                Check(itemNames.Contains(r.ResultPrefab), $"{r.Name}: produces a registered item");
                Check(r.Amount > 0, $"{r.Name}: produces at least one");
                Check(r.Ingredients != null && r.Ingredients.Length > 0, $"{r.Name}: a recipe with no ingredients would be free");
                var seen = new HashSet<string>();
                foreach (var ing in r.Ingredients)
                {
                    Check(ing.Amount > 0, $"{r.Name}: {ing.Item} amount is positive");
                    Check(!string.IsNullOrEmpty(ing.Item) && seen.Add(ing.Item), $"{r.Name}: {ing.Item} is named once");
                    Check(!itemNames.Contains(ing.Item), $"{r.Name}: {ing.Item} is a vanilla item, not one of ours");
                }
                var owner = items.Find(i => i.PrefabName == r.ResultPrefab);
                Check(owner.Drink.Path == BrewPath.Fermenter ? r.Station == StationKind.MeadKetill : r.Station == StationKind.Cauldron,
                      $"{r.Name}: bases are made at the Mead Ketill, station drinks at the cauldron");
            }
            var convs = Drinks.Conversions();
            Check(convs.Count == Drinks.All.Count(d => d.Path == BrewPath.Fermenter), "one conversion per fermented drink");
            foreach (var c in convs)
            {
                var from = items.Find(i => i.PrefabName == c.FromPrefab);
                var to = items.Find(i => i.PrefabName == c.ToPrefab);
                Check(from != null && !from.IsDrink && to != null && to.IsDrink && from.Drink == to.Drink,
                      $"{c.FromPrefab} -> {c.ToPrefab}: a base becomes its own drink");
                Check(c.Yield > 0, $"{c.FromPrefab}: yields at least one drink");
            }
            foreach (var r in recipes)
                foreach (var ing in r.Ingredients)
                    Check(!ing.Item.EndsWith("Base"), $"{r.Name}: no recipe takes a base as an ingredient");

            Console.WriteLine($"{_run - _failed}/{_run} passed" + (_failed > 0 ? $", {_failed} FAILED" : ""));
            return _failed == 0 ? 0 : 1;
        }
    }
}
