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

        private static string StripHiccup(string s) => s.Replace(DrunkText.Hiccup, "");

        private static double NonEFraction(float severity, int seeds)
        {
            long letters = 0, notE = 0;
            for (int seed = 0; seed < seeds; seed++)
            {
                string o = StripHiccup(DrunkText.Garble("eeeeeeeeee eeeeeeeeee eeeeeeeeee", severity, new Random(seed)));
                foreach (char c in o) if (char.IsLetter(c)) { letters++; if (c != 'e') notE++; }
            }
            return letters == 0 ? 0 : (double)notE / letters;
        }

        private static void DrunkTextTests()
        {
            string[] samples =
            {
                "hello there, friend", "Meet me at the stones at 10", "I did NOT take the last mead!", "a b c", "x",
                "can't stop, won't stop", "the Queen? the QUEEN. she is dead", "room 101 is free",
            };

            // Off means off: a sober player's words are never rewritten.
            foreach (string s in samples)
            {
                Check(DrunkText.Garble(s, 0f, new Random(1)) == s, $"severity 0 leaves \"{s}\" alone");
                Check(DrunkText.Garble(s, 1f, new Random(1), 0f) == s, $"strength 0 leaves \"{s}\" alone");
                Check(DrunkText.Garble(s, float.NaN, new Random(1)) == s, "NaN severity leaves text alone");
                Check(DrunkText.Garble(s, 1f, new Random(1), float.NaN) == s, "NaN strength leaves text alone");
                Check(DrunkText.Garble(s, -1f, new Random(1)) == s, "negative severity leaves text alone");
            }
            Check(DrunkText.Garble(null, 1f, new Random(1)) == null, "null text stays null");
            Check(DrunkText.Garble("", 1f, new Random(1)) == "", "empty text stays empty");
            Check(DrunkText.Garble("hello", 1f, null) == "hello", "no random source, no change");

            // Deterministic for a seed, and different seeds really do differ.
            Check(DrunkText.Garble("hello there, friend", 0.8f, new Random(7)) == DrunkText.Garble("hello there, friend", 0.8f, new Random(7)),
                  "same seed, same output");
            var distinct = new HashSet<string>();
            for (int seed = 0; seed < 30; seed++) distinct.Add(DrunkText.Garble("hello there, friend", 0.8f, new Random(seed)));
            Check(distinct.Count > 10, "different seeds give different output");

            // Shape survives at every strength: same number of words, none empty, digits and tags untouched, bounded growth.
            foreach (string s in samples)
            {
                int words = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                for (int step = 1; step <= 10; step++)
                    for (int seed = 0; seed < 40; seed++)
                    {
                        string o = DrunkText.Garble(s, step / 10f, new Random(seed * 31 + step));
                        string core = StripHiccup(o);
                        string[] toks = core.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        bool ok = toks.Length == words && o.Length <= s.Length + 16 + DrunkText.Hiccup.Length;
                        // spacing is preserved exactly: the same indices of the input that were spaces are spaces,
                        // checked as "same count of spaces" because stretches and drops shift positions.
                        ok &= core.Count(ch => ch == ' ') == s.Count(ch => ch == ' ');
                        if (!ok) { Check(false, $"shape broke for \"{s}\" at severity {step / 10f} seed {seed}: \"{o}\""); goto nextSample; }
                    }
                Check(true, $"shape holds for \"{s}\" at every severity");
                nextSample:;
            }
            for (int seed = 0; seed < 100; seed++)
            {
                string o = DrunkText.Garble("room 101 is free", 1f, new Random(seed));
                Check(o.Contains("101"), $"digits survive at full severity (seed {seed})");
                if (!o.Contains("101")) break;
            }
            for (int seed = 0; seed < 100; seed++)
            {
                string o = DrunkText.Garble("hello <color=red>world</color> bye", 1f, new Random(seed));
                bool ok = o.Contains("<color=red>") && o.Contains("</color>");
                Check(ok, $"rich-text tags survive whole (seed {seed}): \"{o}\"");
                if (!ok) break;
            }
            Check(StripHiccup(DrunkText.Garble("a < b and c > d", 1f, new Random(3))).Contains(" "), "a lone angle bracket does not eat the sentence");
            for (int seed = 0; seed < 100; seed++)
            {
                string o = DrunkText.Garble("héllo wörld ✓ ñ", 1f, new Random(seed));
                bool ok = o.Contains("é") && o.Contains("ö") && o.Contains("✓") && o.Contains("ñ");
                Check(ok, $"non-ASCII letters and symbols are never touched (seed {seed})");
                if (!ok) break;
            }

            // It really escalates: a trace of the effect low down, mostly gibberish at the top.
            double f2 = NonEFraction(0.2f, 300), f5 = NonEFraction(0.5f, 300), f10 = NonEFraction(1f, 300);
            Check(f2 < f5 && f5 < f10, $"corruption climbs with severity ({f2:0.00} < {f5:0.00} < {f10:0.00})");
            Check(f2 < 0.2, $"a little drunk is mostly readable ({f2:0.00} of letters wrong)");
            Check(f10 > 0.6, $"fully drunk is mostly gibberish ({f10:0.00} of letters wrong)");

            // Strength is a plain multiplier on severity, clamped at 1.
            Check(DrunkText.Garble("hello there", 0.5f, new Random(9), 2f) == DrunkText.Garble("hello there", 1f, new Random(9)),
                  "strength 2 at half severity equals full severity");

            // The hiccup only shows up once properly drunk, and always fits.
            int hicLow = 0, hicHigh = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                if (DrunkText.Garble("hello there", 0.2f, new Random(seed)).EndsWith(DrunkText.Hiccup)) hicLow++;
                if (DrunkText.Garble("hello there", 1f, new Random(seed)).EndsWith(DrunkText.Hiccup)) hicHigh++;
            }
            Check(hicLow == 0, "no hiccup while only slightly drunk");
            Check(hicHigh > 100, $"hiccups when very drunk ({hicHigh} of 200)");

            // Fuzz: arbitrary text at arbitrary severity never throws.
            var fz = new Random(12345);
            bool threw = false;
            try
            {
                for (int n = 0; n < 3000; n++)
                {
                    var buf = new char[fz.Next(0, 40)];
                    for (int k = 0; k < buf.Length; k++)
                    {
                        int kind = fz.Next(0, 6);
                        buf[k] = kind == 0 ? '<' : kind == 1 ? '>' : kind == 2 ? ' ' : kind == 3 ? (char)fz.Next(0xD800, 0xE000) : kind == 4 ? (char)fz.Next(32, 127) : (char)fz.Next(0, 0xFFFF);
                    }
                    DrunkText.Garble(new string(buf), (float)fz.NextDouble() * 1.5f - 0.2f, new Random(n), (float)fz.NextDouble() * 3f);
                }
            }
            catch (Exception ex) { threw = true; Console.WriteLine("fuzz threw: " + ex); }
            Check(!threw, "3000 random strings, including lone surrogates and stray brackets, never throw");
        }

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

            DrunkTextTests();

            Console.WriteLine($"{_run - _failed}/{_run} passed" + (_failed > 0 ? $", {_failed} FAILED" : ""));
            return _failed == 0 ? 0 : 1;
        }
    }
}
