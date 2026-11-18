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

        private static void SymptomTests()
        {
            // Sober means still: exactly zero, at every moment, for every bad input.
            bool still = true;
            foreach (float s in new[] { 0f, -1f, float.NaN, float.NegativeInfinity })
                for (float t = 0f; t < 60f; t += 0.37f)
                {
                    var c = Symptoms.Camera(t, s);
                    still &= c.Roll == 0f && c.Pitch == 0f && c.Yaw == 0f && c.Fov == 0f && Symptoms.MoveDrift(t, s) == 0f;
                }
            Check(still, "no sway and no drift when sober or on bad input");
            Check(Symptoms.Camera(float.NaN, 1f).Roll == 0f && Symptoms.MoveDrift(float.PositiveInfinity, 1f) == 0f, "a bad clock moves nothing");

            // Bounded by the ceilings, and scaled by severity, over ten simulated minutes at 60 fps.
            float maxRoll = 0, maxPitch = 0, maxYaw = 0, maxFov = 0, maxDrift = 0, halfRoll = 0;
            double maxRateRoll = 0, maxRateDrift = 0; // per second, measured over the ACTUAL float step (see below)
            var prev = Symptoms.Camera(0f, 1f); float prevDrift = Symptoms.MoveDrift(0f, 1f); float prevT = 0f;
            for (int f = 1; f < 36000; f++)
            {
                float t = f / 60f;
                double dtReal = (double)t - prevT; // float t quantises 1/60 s; divide by the step it really took
                var c = Symptoms.Camera(t, 1f);
                float d = Symptoms.MoveDrift(t, 1f);
                maxRoll = Math.Max(maxRoll, Math.Abs(c.Roll)); maxPitch = Math.Max(maxPitch, Math.Abs(c.Pitch));
                maxYaw = Math.Max(maxYaw, Math.Abs(c.Yaw)); maxFov = Math.Max(maxFov, Math.Abs(c.Fov));
                maxDrift = Math.Max(maxDrift, Math.Abs(d));
                halfRoll = Math.Max(halfRoll, Math.Abs(Symptoms.Camera(t, 0.5f).Roll));
                maxRateRoll = Math.Max(maxRateRoll, Math.Abs(c.Roll - prev.Roll) / dtReal);
                maxRateDrift = Math.Max(maxRateDrift, Math.Abs(d - prevDrift) / dtReal);
                prev = c; prevDrift = d; prevT = t;
            }
            const float slack = 1e-4f;
            Check(maxRoll <= Symptoms.MaxRollDegrees + slack, $"roll stays under its ceiling ({maxRoll:0.00})");
            Check(maxPitch <= Symptoms.MaxPitchDegrees + slack && maxYaw <= Symptoms.MaxYawDegrees + slack && maxFov <= Symptoms.MaxFovDegrees + slack,
                  "pitch, yaw and fov stay under their ceilings");
            Check(maxDrift <= Symptoms.MaxMoveDrift + slack, $"drift stays under its ceiling ({maxDrift:0.00})");
            Check(maxRoll > Symptoms.MaxRollDegrees * 0.8f, $"full drunk actually reaches a real lean ({maxRoll:0.00} deg)");
            Check(maxDrift > Symptoms.MaxMoveDrift * 0.8f, $"full drunk actually drifts ({maxDrift:0.00})");
            Check(Math.Abs(halfRoll - maxRoll * 0.5f) < 0.01f, $"half severity is half the lean ({halfRoll:0.00} vs {maxRoll:0.00})");
            // Smooth: the rate of change never exceeds the analytic bound on the derivative, so there is no jump.
            // Roll: 6 deg x (0.6 x 2pi/6.5 + 0.4 x 2pi/3.7) = 7.55 deg/s (a slow lean).
            // Drift: 0.45 x (0.65 x 2pi/2.9 + 0.35 x 2pi/1.7) = 1.22 /s (a stagger, not a twitch).
            // Measured as change / actual step: the loop's own float t is not exactly f/60 (it was 0.18% long near 600 s,
            // which a per-frame comparison read as the function breaking its bound).
            double rollBound = Symptoms.MaxRollDegrees * (0.60 * 2 * Math.PI / 6.5 + 0.40 * 2 * Math.PI / 3.7);
            double driftBound = Symptoms.MaxMoveDrift * (0.65 * 2 * Math.PI / 2.9 + 0.35 * 2 * Math.PI / 1.7);
            Check(maxRateRoll <= rollBound * 1.001, $"roll moves smoothly ({maxRateRoll:0.000} deg/s, bound {rollBound:0.000})");
            Check(maxRateDrift <= driftBound * 1.001, $"drift moves smoothly ({maxRateDrift:0.0000} /s, bound {driftBound:0.0000})");
            Check(rollBound < 15.0, $"the fastest lean is under 15 degrees a second ({rollBound:0.0})");

            // Late in a long session: Time.time is a float that never resets. At ten hours, step it the way the game does
            // and demand the same bound. The first version did its phase maths in float and would have jittered here.
            float late = 36000f; double lateRateMax = 0;
            float prevLateT = late, prevLate = Symptoms.Camera(late, 1f).Roll;
            for (int k = 1; k < 2000; k++)
            {
                float t = late + k * (1f / 60f);
                if (t == prevLateT) continue; // float time can stall for a frame; vanilla would see the same
                float r = Symptoms.Camera(t, 1f).Roll;
                lateRateMax = Math.Max(lateRateMax, Math.Abs(r - prevLate) / ((double)t - prevLateT));
                prevLate = r; prevLateT = t;
            }
            Check(lateRateMax <= rollBound * 1.01, $"still smooth after ten hours of play ({lateRateMax:0.000} deg/s)");
            // It wanders: both signs are reached, so the lean is not stuck to one side.
            bool left = false, right = false;
            for (float t = 0f; t < 60f; t += 0.1f) { float r = Symptoms.Camera(t, 1f).Roll; left |= r < -1f; right |= r > 1f; }
            Check(left && right, "the lean goes both ways");

            // Stumbles: none until properly hammered, more at blackout, and never chained.
            Check(Symptoms.StumbleChancePerSecond(0.3f, Tier.Drunk) == 0.0, "no stumbles while merely drunk");
            Check(Symptoms.StumbleChancePerSecond(Symptoms.StumbleStartSeverity, Tier.Drunk) == 0.0, "no stumble exactly at the threshold");
            Check(Symptoms.StumbleChancePerSecond(0.75f, Tier.Hammered) < Symptoms.StumbleChancePerSecond(1f, Tier.Hammered), "chance climbs");
            Check(Math.Abs(Symptoms.StumbleChancePerSecond(1f, Tier.Hammered) - Symptoms.MaxStumbleChancePerSecond) < 1e-9, "full chance is the ceiling");
            Check(Symptoms.StumbleChancePerSecond(1f, Tier.Blackout) > Symptoms.StumbleChancePerSecond(1f, Tier.Hammered), "blackout stumbles more");
            Check(!Symptoms.ShouldStumble(1f, Tier.Hammered, 0.016f, 0.0, Symptoms.StumbleCooldown - 0.01f), "the cooldown holds even on a certain roll");
            Check(Symptoms.ShouldStumble(1f, Tier.Hammered, 0.016f, 0.0, Symptoms.StumbleCooldown), "a zero roll stumbles once the cooldown is over");
            Check(!Symptoms.ShouldStumble(1f, Tier.Hammered, 0.016f, 0.999, 100f), "a high roll does not stumble");
            Check(!Symptoms.ShouldStumble(1f, Tier.Hammered, 0f, 0.0, 100f) && !Symptoms.ShouldStumble(1f, Tier.Hammered, float.NaN, 0.0, 100f),
                  "no time, no stumble");
            Check(!Symptoms.ShouldStumble(1f, Tier.Hammered, 0.016f, double.NaN, 100f) && !Symptoms.ShouldStumble(1f, Tier.Hammered, 0.016f, 0.0, float.NaN),
                  "a bad roll or a bad clock never stumbles");
            Check(Symptoms.ShouldStumble(1f, Tier.Blackout, 0.016f, 0.0, Symptoms.BlackoutStumbleCooldown), "blackout's shorter cooldown applies");
            // A ten-second frame hitch counts as MaxFrame, not as ten seconds of chances.
            Check(!Symptoms.ShouldStumble(1f, Tier.Hammered, 10f, Symptoms.MaxStumbleChancePerSecond * Symptoms.MaxFrame + 0.001, 100f),
                  "a long frame is capped");

            // Simulated: an hour at full hammered, 60 fps, real RNG. Rate near the expected value, and the cooldown never broken.
            var rng = new Random(42);
            float since = 100f; int stumbles = 0; float minGap = float.MaxValue;
            for (int f = 0; f < 60 * 3600; f++)
            {
                since += 1f / 60f;
                if (Symptoms.ShouldStumble(1f, Tier.Hammered, 1f / 60f, rng.NextDouble(), since))
                {
                    minGap = Math.Min(minGap, since); since = 0f; stumbles++;
                }
            }
            Check(minGap >= Symptoms.StumbleCooldown, $"no two stumbles closer than the cooldown ({minGap:0.0} s)");
            // Expected interval is about cooldown + 1/chance; derived from the constants so a retune cannot silently break it.
            double expected = 3600.0 / (Symptoms.StumbleCooldown + 1.0 / Symptoms.MaxStumbleChancePerSecond);
            Check(stumbles > expected * 0.8 && stumbles < expected * 1.2, $"stumble rate matches the constants ({stumbles} in an hour, expected about {expected:0})");
        }

        private static void EffectTests()
        {
            // Every drink does something besides get you drunk.
            Check(Drinks.All.All(d => d.Effect != null && d.Effect.Seconds >= 60f && !string.IsNullOrEmpty(d.Effect.Name)),
                  "every drink has an effect with a name and a duration");
            Check(Drinks.All.Select(d => d.Effect.Name).Distinct().Count() == Drinks.All.Count, "effect names are unique");
            Check(Drinks.All.Select(d => "SE_" + d.PrefabName).Distinct().Count() == Drinks.All.Count, "status effect asset names are unique");
            var by = Drinks.All.ToDictionary(d => d.PrefabName);
            Check(by["TK_Frostmead"].Effect.Resist == Resist.Frost && by["TK_FireWine"].Effect.Resist == Resist.Fire &&
                  by["TK_BogWine"].Effect.Resist == Resist.Poison, "the biome drinks resist their biome");
            Check(Drinks.All.Count(d => d.CuresHangover) == 1 && by["TK_BloodyIvar"].CuresHangover, "exactly one cure, the Bloody Ivar");
            Check(by["TK_BloodyIvar"].Path == BrewPath.Station && by["TK_BloodyIvar"].Abv > 0f, "the Bloody Ivar is hair of the dog: a station drink with a little alcohol");
            Check(Words.English()[by["TK_BloodyIvar"].DescriptionToken.Substring(1)].Contains("Cures a hangover"), "its tooltip says it cures");
            string d1 = by["TK_ForestGrog"].Effect.Describe();
            Check(d1 == "+50 carry weight, -15% stamina spent running for 10 minutes", $"effect text comes from the numbers ({d1})");
            Check(by["TK_SkaldicMead"].Effect.Describe() == "+25% health regen, +50% stamina regen for 15 minutes", by["TK_SkaldicMead"].Effect.Describe());
            Check(Words.English()[by["TK_Ale"].DescriptionToken.Substring(1)].Contains("+25% stamina regen for 10 minutes"), "the tooltip lists the effect");
            var saved = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Check(!by["TK_ForestGrog"].Effect.Describe().Contains(","+"0") && by["TK_ForestGrog"].Effect.Describe().Contains("+50 carry"), "effect text is culture-proof");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = saved; }

            // Liquid courage.
            Check(Effects.CourageDamage(Tier.Tipsy) == 1f && Effects.CourageDamage(Tier.Drunk) > 1f &&
                  Effects.CourageDamage(Tier.Hammered) > Effects.CourageDamage(Tier.Drunk), "courage starts at Drunk and grows");
            Check(Effects.CourageBlockCost(Tier.Tipsy) == 0f && Effects.CourageBlockCost(Tier.Hammered) > Effects.CourageBlockCost(Tier.Drunk),
                  "and so does the worse block");

            // Hangovers.
            Check(Effects.HangoverSeconds(49f, false) == 0f && Effects.HangoverSeconds(float.NaN, false) == 0f, "no hangover under Drunk");
            Near(Effects.HangoverSeconds(50f, false), 300f, "5 minutes at Drunk");
            Near(Effects.HangoverSeconds(100f, false), 900f, "15 minutes at a full meter");
            Check(Effects.HangoverSeconds(10f, true) == 1200f, "20 minutes after a blackout, whatever the peak");
            float lastH = 0f; bool rises = true;
            for (float p = 50f; p <= 100f; p += 1f) { float h = Effects.HangoverSeconds(p, false); rises &= h >= lastH; lastH = h; }
            Check(rises, "a harder night, a longer morning");
            // Dying is no way out (owner, 2026-11-18): it never cures a hangover, and in a blackout it gives the full one.
            Check(Effects.HangoverAfterDeath(400f, false) == 400f, "an ordinary death carries the hangover over unchanged");
            Check(Effects.HangoverAfterDeath(0f, false) == 0f, "and a sober death gives none");
            Check(Effects.HangoverAfterDeath(0f, true) == 1200f && Effects.HangoverAfterDeath(300f, true) == 1200f,
                  "a death in a blackout wakes with 20 minutes");
            Check(Effects.HangoverAfterDeath(1500f, true) == 1500f, "never less than what was already left");
            Check(Effects.HangoverAfterDeath(float.NaN, true) == 1200f && Effects.HangoverAfterDeath(-5f, false) == 0f, "bad input is no hangover");
            Check(!Effects.ShouldReplaceHangover(1100f, 300f), "the binge ending later cannot cut a 20-minute hangover to 5");
            Check(Effects.ShouldReplaceHangover(100f, 900f) && Effects.ShouldReplaceHangover(0f, 300f) && Effects.ShouldReplaceHangover(float.NaN, 300f),
                  "a longer one, or one with nothing running, does replace it");

            // Fermenting: every fermented drink has its own time, and stronger drinks take longer.
            var ferm = Drinks.All.Where(d => d.Path == BrewPath.Fermenter).ToList();
            Check(ferm.All(d => d.FermentSeconds >= 600f), "every fermented drink has a barrel time");
            bool longer = true;
            for (int i = 1; i < ferm.Count; i++) longer &= ferm[i].FermentSeconds > ferm[i - 1].FermentSeconds;
            Check(longer, "stronger drinks ferment longer");
            Check(ferm.First().FermentSeconds < Effects.VanillaFermentSeconds && ferm.Last().FermentSeconds > Effects.VanillaFermentSeconds,
                  "honey mead is quicker than vanilla mead, fire wine slower");
            Check(Drinks.All.Where(d => d.Path == BrewPath.Station).All(d => d.FermentSeconds == 0f), "station drinks have no barrel time");

            // The saved meter.
            var ms = new MeterState { Level = 63.25f, Binge = 9, Peak = 81.5f, BlackedOut = true, HangoverLeft = 412.5f };
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var back = MeterState.Parse(ms.Format());
                Check(back.Level == 63.25f && back.Binge == 9 && back.Peak == 81.5f && back.BlackedOut && back.HangoverLeft == 412.5f,
                      $"the meter round-trips under de-DE ({ms.Format()})");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
            var junk = MeterState.Parse("NaN;-4;1e9;x;-1");
            Check(junk.Level == 0f && junk.Binge == 0 && junk.Peak == Intoxication.Cap && !junk.BlackedOut && junk.HangoverLeft == 0f, "a damaged save clamps");
            Check(MeterState.Parse(null).Level == 0f && MeterState.Parse("12;3").Level == 0f, "a missing or short save is sober");
        }

        private static void FoodTests()
        {
            Near(Food.Sobering(30f, 10f, 0f), 4f, "cooked meat (30/10) takes 4 off");
            Near(Food.Sobering(7f, 20f, 0f), 2.7f, "raspberries (7/20) take 2.7");
            Near(Food.Sobering(5f, 10f, 0f), Food.MinPerMeal, "a snack takes at least the minimum");
            Near(Food.Sobering(85f, 28f, 0f), Food.MaxPerMeal, "a big meal is capped");
            Near(Food.Sobering(300f, 300f, 300f), Food.MaxPerMeal, "no meal ever takes more than the cap");
            Check(Food.Sobering(0f, 0f, 0f) == 0f && Food.Sobering(float.NaN, -5f, float.PositiveInfinity) == 0f, "not food, no sobering");
            Check(Food.Sobering(30f, 10f, 0f, 0f) == 0f && Food.Sobering(30f, 10f, 0f, float.NaN) == 0f, "strength 0 turns it off");
            Near(Food.Sobering(30f, 10f, 0f, 2f), 8f, "strength scales it");
            Check(Food.MaxPerMeal < Intoxication.TierFloor[(int)Tier.Hammered] - Intoxication.TierFloor[(int)Tier.Drunk],
                  "a feast is 'a little': less than one tier's width");
            float lvl = Food.Eat(80f, Food.Sobering(85f, 28f, 0f));
            Check(lvl == 70f && Intoxication.TierOf(lvl) == Tier.Drunk, "one feast pulls Hammered at 80 down to Drunk, not sober");
            Check(Food.Eat(1f, Food.MaxPerMeal) == 0f && Food.Eat(float.NaN, 3f) == 0f && Food.Eat(500f, 0f) == Intoxication.Cap && Food.Eat(50f, float.NaN) == 50f, "eating never drives the meter below zero or past full");
            // Repeats of one food (Fatty stacks them): 100/50/25/5%, the same ladder as Fatty's food bar.
            Check(Food.RepeatFactor(0) == 1f && Food.RepeatFactor(-3) == 1f, "a first serving sobers in full (and vanilla only ever has firsts)");
            Check(Food.RepeatFactor(1) == 0.5f && Food.RepeatFactor(2) == 0.25f && Food.RepeatFactor(3) == 0.05f && Food.RepeatFactor(40) == 0.05f,
                  "repeats sober 50%, 25%, then 5% for good");
            float four = 0f; for (int i = 0; i < 4; i++) four += Food.Sobering(30f, 10f, 0f) * Food.RepeatFactor(i);
            Check(four < 8f, $"four steaks in a row take under 8 off, not the 16 seen in game ({four:0.0})");
        }

        private static void ToleranceTests()
        {
            var fire = Drinks.All.First(d => d.PrefabName == "TK_FireWine");
            var ale = Drinks.All.First(d => d.PrefabName == "TK_Ale");

            // The owner's point: an end-game drink handles a new player, and a hardened drinker handles it.
            Check(Tolerance.Hit(fire.Abv, 0f) >= Intoxication.TierFloor[(int)Tier.Hammered],
                  $"one Fire wine puts a new player at Hammered or worse ({Tolerance.Hit(fire.Abv, 0f):0})");
            Check(Tolerance.Hit(fire.Abv, Tolerance.Max) < Intoxication.TierFloor[(int)Tier.Drunk],
                  $"at 69 tolerance one Fire wine is under Drunk ({Tolerance.Hit(fire.Abv, Tolerance.Max):0})");
            Check(Tolerance.Hit(ale.Abv, Tolerance.Max) < Intoxication.TierFloor[(int)Tier.Buzzed],
                  $"at 69 an ale barely registers ({Tolerance.Hit(ale.Abv, Tolerance.Max):0.0})");
            Near(Tolerance.Hit(10f, 0f), 20f, "the hit is ABV x 2 at tolerance 0");
            Near(Tolerance.Hit(10f, Tolerance.Max), 8f, "and 40% of that at 69");
            float lastHit = float.MaxValue; bool hitFalls = true;
            for (float t = 0f; t <= 69f; t += 0.5f) { float h = Tolerance.Hit(20f, t); hitFalls &= h <= lastHit; lastHit = h; }
            Check(hitFalls, "more tolerance never makes a drink hit harder");
            Check(Tolerance.Hit(0f, 0f) == 0f && Tolerance.Hit(-5f, 0f) == 0f && Tolerance.Hit(float.NaN, 0f) == 0f, "no alcohol, no hit");
            Check(Tolerance.Hit(10f, 500f) == Tolerance.Hit(10f, Tolerance.Max) && Tolerance.Hit(10f, -3f) == Tolerance.Hit(10f, 0f) &&
                  Tolerance.Hit(10f, float.NaN) == Tolerance.Hit(10f, 0f), "a bad tolerance clamps into 0..69");

            // Growing: never down, never past 69, strong drinks train faster, and it does reach 69.
            Check(Tolerance.AfterDrink(10f, 20f) > 10f && Tolerance.AfterDrink(10f, 0f) == 10f, "a drink raises tolerance; water does not");
            Check(Tolerance.AfterDrink(0f, fire.Abv) > Tolerance.AfterDrink(0f, ale.Abv), "strong drinks train you faster");
            Check(Tolerance.AfterDrink(68.9f, 40f) == Tolerance.Max && Tolerance.AfterDrink(Tolerance.Max, 40f) == Tolerance.Max, "it stops at 69");
            Check(Tolerance.AfterDrink(60f, 5f) - 60f < Tolerance.AfterDrink(0f, 5f), "the gains slow as you climb");
            float tol = 0f; int n = 0;
            while (tol < Tolerance.Max && n < 10000) { tol = Tolerance.AfterDrink(tol, fire.Abv); n++; }
            Check(tol == Tolerance.Max && n < 200, $"Fire wine alone gets you to 69 ({n} drinks)");
            tol = 0f; n = 0;
            while (Tolerance.Shown(tol) < 30 && n < 10000) { tol = Tolerance.AfterDrink(tol, ale.Abv); n++; }
            Check(n > 40 && n < 200, $"ale alone takes a good while to reach 30 ({n} drinks)");
            Check(Tolerance.Shown(68.99f) == 68 && Tolerance.Shown(69f) == 69 && Tolerance.Shown(-1f) == 0, "shown as a floored whole number, 0..69");
            Check(Tolerance.Describe(0f) == "paperweight" && Tolerance.Describe(69f) == "iron gut", "described at both ends");
            var savedTol = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Check(Tolerance.Parse(Tolerance.Format(23.75f)) == 23.75f && !Tolerance.Format(23.75f).Contains(","), "tolerance round-trips under de-DE");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = savedTol; }
            Check(Tolerance.Parse(null) == 0f && Tolerance.Parse("x") == 0f && Tolerance.Parse("1000") == Tolerance.Max && Tolerance.Parse("NaN") == 0f,
                  "a damaged tolerance clamps, never NaN");
            Check(Words.English()[fire.DescriptionToken.Substring(1)].Contains("40% alcohol"), "the tooltip shows the alcohol content");
        }

        private static void BlackoutTests()
        {
            // Chance: zero below 12, 10% on the 12th, climbing 10% a drink, certain by the 21st.
            // Blood alcohol decides it (owner, 2026-11-18), not a drink count.
            Check(Blackout.Over(100f) == 0f && Blackout.Over(60f) == 0f && Blackout.Over(float.NaN) == 0f, "at or under the limit is not over");
            Near(Blackout.Over(160f), 60f, "160 is 60 over");
            Near(Blackout.Over(160f, 150f), 10f, "the limit is configurable");
            Check(Blackout.Chance(0f) == 0.0 && Blackout.Chance(-5f) == 0.0 && Blackout.Chance(float.NaN) == 0.0, "no chance at or under the limit");
            Near((float)Blackout.Chance(0.001f), 0.10f, "10% just over");
            Near((float)Blackout.Chance(50f), 0.60f, "60% at 50 over");
            Check(Blackout.Chance(90f) == 1.0 && Blackout.Chance(100f) == 1.0, "certain from 90 over, and capped");
            for (int n = 0; n < 100; n++) Check(Blackout.Chance(n + 1) >= Blackout.Chance(n), $"chance never falls ({n})");
            Near(Blackout.Distance(0f), Blackout.FirstDistance, "just over lands nearby");
            Near(Blackout.Distance(40f), 300f + 40 * 25f, "40 over is 1.3 km");
            Check(Blackout.Distance(100000f) == Blackout.WorldRadius - Blackout.EdgeMargin && Blackout.Distance(float.NaN) == Blackout.FirstDistance,
                  "distance is capped by the world, and a bad number lands nearby");
            Check(Blackout.LossFraction(0f) == 0.0, "nothing lost at the limit");
            Near((float)Blackout.LossFraction(1f), 0.26f, "a quarter and a bit just over");
            Near((float)Blackout.LossFraction(50f), 0.75f, "three quarters at 50 over");
            Check(Blackout.LossFraction(75f) == 1.0 && Blackout.LossFraction(99f) == 1.0, "everything from 75 over, capped");
            for (int n = 0; n < 100; n++) Check(Blackout.LossFraction(n + 1) >= Blackout.LossFraction(n), $"loss never falls ({n})");

            // What that means at the bar, with the real drinks, sobering ignored (back to back).
            var fireB = Drinks.All.First(d => d.PrefabName == "TK_FireWine");
            var ciderB = Drinks.All.First(d => d.PrefabName == "TK_RaspberryCider");
            float lv = 0f; for (int i = 0; i < 2; i++) lv = Intoxication.Add(lv, Tolerance.Hit(fireB.Abv, 0f));
            Check(Blackout.Chance(Blackout.Over(lv)) >= 0.6, $"a new drinker on two fire wines is likely out ({lv:0}, {Blackout.Chance(Blackout.Over(lv)):P0})");
            lv = 0f; int cups = 0; while (Blackout.Over(lv) <= 0f && cups < 50) { lv = Intoxication.Add(lv, Tolerance.Hit(fireB.Abv, Tolerance.Max)); cups++; }
            Check(cups == 4, $"a hardened drinker needs four fire wines to get over the limit ({cups})");
            lv = 0f; for (int i = 0; i < 12; i++) lv = Intoxication.Decay(Intoxication.Add(lv, Tolerance.Hit(ciderB.Abv, 0f)), 60f, 0.25f);
            Check(Blackout.Over(lv) == 0f, $"twelve ciders, a minute apart, never get you over ({lv:0})");

            var rng = new Random(5);
            Check(Blackout.PickLost(0, 0.5, rng).Count == 0 && Blackout.PickLost(10, 0.0, rng).Count == 0 && Blackout.PickLost(10, double.NaN, rng).Count == 0,
                  "nothing picked from nothing, or at zero or NaN");
            Check(Blackout.PickLost(10, 1.0, rng).Count == 10 && Blackout.PickLost(10, 7.0, rng).Count == 10, "everything at 1, capped above");
            Check(Blackout.PickLost(10, 0.25, rng).Count == 3, "ceil(25% of 10) = 3");
            Check(Blackout.PickLost(1, 0.25, rng).Count == 1, "a single stack at 25% is lost: ceil rounds up");
            Check(Blackout.PickLost(20, 0.25, rng).Count == 5, "exactly 25% of 20 is 5, not 6 (no float overshoot)");
            bool distinctOk = true;
            for (int s = 0; s < 200; s++)
            {
                var picked = Blackout.PickLost(32, 0.55, new Random(s));
                distinctOk &= picked.Count == 18 && picked.Distinct().Count() == 18 && picked.All(i => i >= 0 && i < 32);
            }
            Check(distinctOk, "picks are distinct, in range, and the right count");
            var hits = new int[10];
            for (int s = 0; s < 20000; s++) foreach (int i in Blackout.PickLost(10, 0.3, new Random(s))) hits[i]++;
            Check(hits.All(h => Math.Abs(h - 6000) < 400), $"every stack is equally likely to be lost ({string.Join(",", hits)})");

            // Biomes: Meadows always, each boss opens the next, Ocean never.
            Check(Blackout.AllowedBiomes(k => false) == Blackout.Meadows, "a fresh world: Meadows only");
            Check(Blackout.AllowedBiomes(null) == Blackout.Meadows, "no key source: Meadows only");
            int afterElder = Blackout.AllowedBiomes(k => k == "defeated_eikthyr" || k == "defeated_gdking");
            Check(afterElder == (Blackout.Meadows | Blackout.BlackForest | Blackout.Swamp), "Eikthyr and the Elder open Black Forest and Swamp");
            int all = Blackout.AllowedBiomes(k => true);
            Check((all & Blackout.Ocean) == 0, "never the ocean, even with every boss down");
            Check(all == (Blackout.Meadows | Blackout.BlackForest | Blackout.Swamp | Blackout.Mountain | Blackout.Plains |
                          Blackout.Mistlands | Blackout.AshLands | Blackout.DeepNorth), "every boss opens every land biome");
            Check(Blackout.BossOpens.Count == 7 && Blackout.BossOpens.Select(kv => kv.Key).Distinct().Count() == 7, "seven distinct boss keys");
            Check(Blackout.BossOpens[0].Key == "defeated_eikthyr", "the one key vanilla's own code names (Player.AddKnownBiome) is first");
            // Pinned to Heightmap.Biome's numbers, decompiled from 1.0.x.
            Check(Blackout.Meadows == 1 && Blackout.Swamp == 2 && Blackout.Mountain == 4 && Blackout.BlackForest == 8 && Blackout.Plains == 16 &&
                  Blackout.AshLands == 32 && Blackout.DeepNorth == 64 && Blackout.Ocean == 256 && Blackout.Mistlands == 512, "biome numbers match the game");

            // Landing on a fake world: land is Meadows east of x = 0 and Black Forest west of it, the sea is beyond 3 km,
            // and height is 40 m on land, 10 m (under the 30 m sea level) in the water.
            Func<float, float, int> biome = (x, z) => x * x + z * z > 3000f * 3000f ? Blackout.Ocean : (x >= 0 ? Blackout.Meadows : Blackout.BlackForest);
            Func<float, float, float> height = (x, z) => biome(x, z) == Blackout.Ocean ? 10f : 40f;
            bool landOk = true; int found = 0;
            for (int s = 0; s < 300; s++)
            {
                if (!Blackout.PickLanding(0f, 0f, 1000f, new Random(s), biome, height, 30f, Blackout.Meadows, out float x, out float z, out float y)) continue;
                found++;
                float d = (float)Math.Sqrt(x * x + z * z);
                landOk &= x >= 0 && d >= 800f - 0.5f && d <= 1200f + 0.5f && y == 40f;
            }
            Check(found == 300, $"a landing is always found when there is room ({found}/300)");
            Check(landOk, "landings are in an allowed biome, on the 80-120% ring, on dry land");

            // An island too small for the ring: the picker shrinks towards the spot instead of drowning you.
            Func<float, float, int> island = (x, z) => x * x + z * z <= 200f * 200f ? Blackout.Meadows : Blackout.Ocean;
            Func<float, float, float> islandH = (x, z) => island(x, z) == Blackout.Ocean ? 0f : 35f;
            bool islandOk = true;
            for (int s = 0; s < 100; s++)
            {
                bool ok = Blackout.PickLanding(0f, 0f, 2000f, new Random(s), island, islandH, 30f, Blackout.Meadows, out float x, out float z, out _);
                islandOk &= ok && x * x + z * z <= 200f * 200f;
            }
            Check(islandOk, "on a small island you wake on the island, not in the sea");

            // Nowhere allowed: no landing, so the caller leaves you be.
            Check(!Blackout.PickLanding(0f, 0f, 1000f, new Random(1), (x, z) => Blackout.Mistlands, (x, z) => 50f, 30f, Blackout.Meadows, out _, out _, out _),
                  "no allowed ground anywhere: no landing");
            Check(!Blackout.PickLanding(0f, 0f, 1000f, new Random(1), (x, z) => Blackout.Meadows, (x, z) => 29.5f, 30f, Blackout.Meadows, out _, out _, out _),
                  "Meadows that are all under sea level count as water");
            Check(!Blackout.PickLanding(0f, 0f, 1000f, null, biome, height, 30f, Blackout.Meadows, out _, out _, out _) &&
                  !Blackout.PickLanding(float.NaN, 0f, 1000f, new Random(1), biome, height, 30f, Blackout.Meadows, out _, out _, out _) &&
                  !Blackout.PickLanding(0f, 0f, 0f, new Random(1), biome, height, 30f, Blackout.Meadows, out _, out _, out _),
                  "bad inputs give no landing");
            // Preferring already-generated ground (the lag fix): when some fits, every landing is on it...
            Func<float, float, bool> westOnly = (x, z) => x < 0f; // pretend only the west has been generated
            Func<float, float, int> allMeadows = (x, z) => Blackout.Meadows;
            Func<float, float, float> dry = (x, z) => 40f;
            bool prefOk = true;
            for (int s = 0; s < 200; s++)
            {
                bool ok = Blackout.PickLanding(0f, 0f, 1000f, new Random(s), allMeadows, dry, 30f, Blackout.Meadows, out float x, out float z, out _, westOnly);
                prefOk &= ok && x < 0f;
            }
            Check(prefOk, "landings prefer already-generated ground when any of it fits");
            // ...when none fits, you still go somewhere (fresh ground), never nowhere.
            bool fallOk = true;
            for (int s = 0; s < 50; s++)
                fallOk &= Blackout.PickLanding(0f, 0f, 1000f, new Random(s), allMeadows, dry, 30f, Blackout.Meadows, out _, out _, out _, (x, z) => false);
            Check(fallOk, "with no generated ground at all, a landing is still found");
            // A total-loss day lands at least 5 km out (the owner: "go far away"); in game the ring woke 4897 m and 4859 m.
            bool floorOk = true; float nearest = float.MaxValue;
            for (int s = 0; s < 400; s++)
            {
                bool ok = Blackout.PickLanding(0f, 0f, Blackout.FarDistance, new Random(s), allMeadows, dry, 30f, Blackout.Meadows,
                    out float x, out float z, out _, null, Blackout.FarDistance);
                float d = (float)Math.Sqrt(x * x + z * z);
                floorOk &= ok && d >= Blackout.FarDistance - 0.5f && d <= Blackout.FarDistance * 1.2f + 0.5f;
                if (d < nearest) nearest = d;
            }
            Check(floorOk, $"with a 5 km floor every landing is 5-6 km out (nearest {nearest:0} m)");
            bool floorPref = true;
            for (int s = 0; s < 200; s++)
            {
                bool ok = Blackout.PickLanding(0f, 0f, Blackout.FarDistance, new Random(s), allMeadows, dry, 30f, Blackout.Meadows,
                    out float x, out float z, out _, westOnly, Blackout.FarDistance);
                floorPref &= ok && x < 0f && x * x + z * z >= (Blackout.FarDistance - 0.5f) * (Blackout.FarDistance - 0.5f);
            }
            Check(floorPref, "the floor and the generated-ground preference hold together");
            // On a small island nothing lies beyond the floor: you still wake (on the island) rather than not at all.
            bool floorIsland = true;
            for (int s = 0; s < 50; s++)
                floorIsland &= Blackout.PickLanding(0f, 0f, Blackout.FarDistance, new Random(s), island, islandH, 30f, Blackout.Meadows,
                    out float x, out float z, out _, null, Blackout.FarDistance) && x * x + z * z <= 200f * 200f;
            Check(floorIsland, "with no ground past the floor, the plain search still lands you on dry land");
            // ...and the preference never overrides the rules: generated ground in a forbidden biome is not chosen.
            bool ruleOk = true;
            for (int s = 0; s < 100; s++)
                if (Blackout.PickLanding(0f, 0f, 1000f, new Random(s), (x, z) => x < 0f ? Blackout.Mistlands : Blackout.Meadows, dry, 30f,
                        Blackout.Meadows, out float x2, out _, out _, westOnly))
                    ruleOk &= x2 >= 0f;
            Check(ruleOk, "a generated zone in a forbidden biome loses to fresh allowed ground");

            // Near the world's edge: never outside it.
            bool edgeOk = true;
            for (int s = 0; s < 200; s++)
                if (Blackout.PickLanding(9400f, 0f, 2000f, new Random(s), (x, z) => Blackout.Meadows, (x, z) => 40f, 30f, Blackout.Meadows, out float x, out float z, out _))
                    edgeOk &= x * x + z * z <= (Blackout.WorldRadius - Blackout.EdgeMargin) * (Blackout.WorldRadius - Blackout.EdgeMargin) + 1f;
            Check(edgeOk, "never woken outside the world's edge margin");

            // Home: round-trips through InvariantCulture, even when the thread's culture uses comma decimals.
            var saved = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                string s = Blackout.FormatHome(-1234.5678f, 42.25f, 987.125f);
                Check(!s.Contains(","), $"home is written without comma decimals under de-DE ({s})");
                Check(Blackout.TryParseHome(s, out float hx, out float hy, out float hz) && hx == -1234.5678f && hy == 42.25f && hz == 987.125f,
                      "home round-trips exactly under de-DE");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
            Check(!Blackout.TryParseHome(null, out _, out _, out _) && !Blackout.TryParseHome("", out _, out _, out _) &&
                  !Blackout.TryParseHome("1;2", out _, out _, out _) && !Blackout.TryParseHome("a;b;c", out _, out _, out _) &&
                  !Blackout.TryParseHome("NaN;1;2", out _, out _, out _) && !Blackout.TryParseHome("1;Infinity;2", out _, out _, out _),
                  "a damaged home value is refused, never half-read");
            Check(Blackout.IsHome(10f, 10f, 40f, 40f) && !Blackout.IsHome(0f, 0f, 40f, 40f), "home is 50 m, flat");
            // Coming home: replays the first in-game run. Standing AT the spot during the teleport's 2 s wait, then carried
            // 1300 m away, then walking back. The buggy check restored the map on the very first tick.
            bool armed = false;
            Check(!Blackout.ShouldRestoreMap(ref armed, true, 0f, 0f, 0f, 0f), "mid-teleport at the spot: map stays off");
            Check(!Blackout.ShouldRestoreMap(ref armed, false, 0f, 0f, 0f, 0f) && !armed, "at the spot but never away yet: still off, not armed");
            Check(!Blackout.ShouldRestoreMap(ref armed, false, 1300f, 0f, 0f, 0f) && armed, "woken far away: armed, still off");
            Check(!Blackout.ShouldRestoreMap(ref armed, false, 60f, 0f, 0f, 0f), "60 m out: not home yet");
            Check(Blackout.ShouldRestoreMap(ref armed, false, 30f, 20f, 0f, 0f), "back within 50 m: map returns");
            bool a2 = false;
            Check(!Blackout.ShouldRestoreMap(ref a2, true, 5000f, 0f, 0f, 0f) && !a2, "far away but mid-teleport does not arm");
            bool a3 = false;
            Check(!Blackout.ShouldRestoreMap(ref a3, false, 90f, 0f, 0f, 0f) && !a3, "90 m is not far enough to arm (needs 100)");
            // Streaks: consecutive days escalate; day three takes everything and throws you far.
            var st = Blackout.Advance(Blackout.Streak.None, 10);
            Check(st.LastDay == 10 && st.Days == 1 && st.Count == 1 && !st.IsTotal, "first blackout starts a one-day streak");
            var same = Blackout.Advance(st, 10);
            Check(same.Days == 1 && same.Count == 2, "a second blackout the same day counts, but is still day one");
            var d2 = Blackout.Advance(same, 11);
            Check(d2.Days == 2 && d2.Count == 3 && !d2.IsTotal, "the next day is day two");
            var d3 = Blackout.Advance(d2, 12);
            Check(d3.Days == 3 && d3.IsTotal, "the third day in a row is total");
            Check(Blackout.Advance(d3, 13).IsTotal, "and it stays total while the streak runs");
            Check(Blackout.Advance(d2, 13).Days == 1 && Blackout.Advance(d2, 13).Count == 1, "a skipped day ends the streak");
            Check(Blackout.Advance(d2, 5).Days == 1, "time going backwards (another world, a restore) starts fresh");
            Check(Blackout.Advance(st, -7).LastDay >= 0, "a bad day number never goes negative");
            // Distance and loss with the streak.
            Near(Blackout.DistanceFor(0f, st), Blackout.FirstDistance, "a first blackout is the base distance");
            Near(Blackout.DistanceFor(0f, same), Blackout.FirstDistance * 1.5f, "each earlier blackout adds half again");
            Near(Blackout.DistanceFor(0f, d2), Blackout.FirstDistance * 2f, "the third blackout of a streak is double");
            Check(Blackout.DistanceFor(0f, d3) >= Blackout.FarDistance, "day three throws you at least 5 km");
            Check(Blackout.DistanceFor(100f, d3) <= Blackout.WorldRadius - Blackout.EdgeMargin, "but never off the world");
            Check(Blackout.LossFor(20f, d2) == Blackout.LossFraction(20f), "below day three how far over decides the loss");
            Check(Blackout.LossFor(0f, d3) == 1.0, "day three: everything");
            Check(Blackout.DistanceFor(40f, same) > Blackout.DistanceFor(0f, same), "further over still means further, inside a streak");
            // Saved state.
            var rt2 = Blackout.ParseStreak(Blackout.FormatStreak(d3));
            Check(rt2.LastDay == 12 && rt2.Days == 3 && rt2.Count == 4, "a streak round-trips");
            Check(Blackout.ParseStreak(null).Days == 0 && Blackout.ParseStreak("x").Days == 0 && Blackout.ParseStreak("1;2").Days == 0 &&
                  Blackout.ParseStreak("-1;1;1").Days == 0 && Blackout.ParseStreak("5;0;0").Days == 0 && Blackout.ParseStreak("5;3;1").Days == 0,
                  "a damaged streak reads as none, never as a phantom day three");
            Check(Blackout.StreakKeyFor("1") != Blackout.KeyFor("1") && Blackout.StreakKeyFor("1") != Blackout.MemoryKeyFor("1"), "three separate keys");

            // Remembering: only looking advances it, it never goes back, and 90 s of looking finishes it.
            float mem = 0f;
            for (int f = 0; f < 90 * 60; f++) mem = Blackout.Remember(mem, 1f / 60f, 90f);
            Check(mem > 0.999f, $"90 s of looking at 60 fps remembers it all when set to 90 ({mem:0.0000})");
            // Tuned twice by the owner: 90 s was too fast, 300 s at Tipsy too slow. 150 s sober, and Tipsy is where it starts.
            Check(Blackout.DefaultRememberSeconds >= 120f && Blackout.DefaultRememberSeconds <= 200f, "two to three minutes of looking when sober");
            float tipsy = Intoxication.TierFloor[(int)Tier.Tipsy] + 1f, drunk = Intoxication.TierFloor[(int)Tier.Drunk] + 1f;
            float hammered = Intoxication.TierFloor[(int)Tier.Hammered] + 1f;
            Check(Blackout.RememberRate(0f) == 1f && Blackout.RememberRate(10f) == 1f, "sober and buzzed remember at full speed");
            Check(Blackout.RememberRate(tipsy) >= 0.75f, "Tipsy is where you really start remembering (the owner's second note)");
            Check(Blackout.RememberRate(drunk) < 0.25f && Blackout.RememberRate(drunk) > Blackout.RememberRate(hammered), "Drunk barely, Hammered less");
            Check(Blackout.RememberRate(hammered) > 0f && Blackout.RememberRate(100f) > 0f, "even blind drunk, looking brings back a little");
            Check(Blackout.RememberRate(float.NaN) == 1f && Blackout.RememberRate(-5f) == 1f, "a bad level reads as sober");
            // At Tipsy, from nothing to all of it in about three minutes of looking, at 60 fps.
            float tip = 0f; int frames = 0;
            while (tip < 1f && frames < 60 * 600) { tip = Blackout.Remember(tip, 1f / 60f, Blackout.DefaultRememberSeconds, tipsy); frames++; }
            Check(frames / 60f > 150f && frames / 60f < 240f, $"Tipsy remembers it all in about three minutes ({frames / 60f:0} s)");
            Check(Blackout.Remember(0.5f, 10f, 90f) <= 0.5f + 0.25f / 90f + 1e-6f, "a 10 s frame hitch counts as a quarter second");
            Check(Blackout.Remember(0.5f, 0f, 90f) == 0.5f && Blackout.Remember(0.5f, -1f, 90f) == 0.5f && Blackout.Remember(0.5f, float.NaN, 90f) == 0.5f,
                  "no time, no memory; never backwards");
            Check(Blackout.Remember(0.99f, 0.25f, 1f) == 1f && Blackout.Remember(2f, 0.1f, 90f) == 1f, "capped at 1");
            Check(Blackout.Remember(float.NaN, 0.1f, 90f) > 0f && Blackout.Remember(-3f, 0.1f, 90f) > 0f, "a damaged progress restarts from 0");
            Check(Blackout.Remember(0.3f, 0.1f, 0f) == 0.3f, "a zero duration setting changes nothing (never divides by zero)");
            Check(Blackout.FogAlpha(0f) == 1f && Blackout.FogAlpha(1f) == 0f && Blackout.FogAlpha(float.NaN) == 1f, "fog: solid at 0, clear at 1");
            Check(Blackout.FogAlpha(0.1f) >= 0.97f, "a quick peek shows almost nothing");
            bool fogMono = true; float lastFog = 2f;
            for (float p = 0f; p <= 1f; p += 0.01f) { float a = Blackout.FogAlpha(p); fogMono &= a <= lastFog && a >= 0f && a <= 1f; lastFog = a; }
            Check(fogMono, "fog only ever clears");
            Check(Blackout.MilestoneCrossed(0.2f, 0.3f) == 0 && Blackout.MilestoneCrossed(0.49f, 0.5f) == 1 && Blackout.MilestoneCrossed(0.74f, 0.9f) == 2,
                  "each milestone is announced when crossed");
            Check(Blackout.MilestoneCrossed(0.3f, 0.4f) == -1 && Blackout.MilestoneCrossed(0.5f, 0.5f) == -1, "no line between milestones");
            Check(Blackout.MemoryLines.Length == Blackout.MemoryMilestones.Length, "a line per milestone");
            var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Check(Blackout.ParseProgress(Blackout.FormatProgress(0.625f)) == 0.625f && !Blackout.FormatProgress(0.625f).Contains(","),
                      "progress round-trips under de-DE");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = savedCulture; }
            Check(Blackout.ParseProgress(null) == 0f && Blackout.ParseProgress("x") == 0f && Blackout.ParseProgress("NaN") == 0f &&
                  Blackout.ParseProgress("7") == 1f && Blackout.ParseProgress("-2") == 0f, "damaged progress reads as a clamp, never NaN");
            Check(Blackout.MemoryKeyFor("1") != Blackout.KeyFor("1"), "memory and home are separate keys");
            Check(Blackout.KeyFor("123") != Blackout.KeyFor("456") && Blackout.KeyFor(null) == Blackout.KeyFor(""), "one key per world");
        }

        private static int Main()
        {
            // ---- Add ---------------------------------------------------------------------------------------------
            Near(Intoxication.Add(0f, 10f), 10f, "a drink adds its potency");
            Near(Intoxication.Add(95f, 30f), 125f, "the meter runs past full");
            Near(Intoxication.Add(190f, 30f), Intoxication.Cap, "and caps at the ceiling");
            Check(Intoxication.TierOf(Intoxication.Cap) == Tier.Blackout && Intoxication.Severity(Intoxication.Cap) == 1f, "past full is still Blackout, symptoms capped");
            Near(Intoxication.SecondsUntil(Intoxication.Cap, 0f, 0.25f), 800f, "a full ceiling is about 13 minutes from sober");
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
            Check(Drinks.All.Count == 11, "eleven drinks: five from the cauldron (the Bloody Ivar among them), six from the fermenter");

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
            // The fermenter list climbs the biomes: each drink is stronger than the one before it.
            var fermented = Drinks.All.Where(d => d.Path == BrewPath.Fermenter).ToList();
            bool climbs = true;
            for (int i = 1; i < fermented.Count; i++) climbs &= fermented[i].Potency > fermented[i - 1].Potency;
            Check(climbs, "fermented drinks get stronger in table order (one per biome)");
            Check(Drinks.All.Select(d => d.DonorPrefab).Distinct().Count() >= 4, "drinks clone several different meads, so they don't all look alike");

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
            Check(string.Join(",", Drinks.All.Select(d => d.PrefabName)) == "TK_RaspberryCider,TK_Ale,TK_ForestGrog,TK_BlueberryWine,TK_BloodyIvar,TK_HoneyMead,TK_BogWine,TK_Frostmead,TK_SkaldicMead,TK_JotunDraught,TK_FireWine",
                  "shipped prefab names are pinned (saves store them)");
            Check(string.Join(",", Drinks.Items().Select(i => i.PrefabName)) ==
                  "TK_RaspberryCider,TK_Ale,TK_ForestGrog,TK_BlueberryWine,TK_BloodyIvar,TK_HoneyMead,TK_HoneyMeadBase,TK_BogWine,TK_BogWineBase,TK_Frostmead,TK_FrostmeadBase,TK_SkaldicMead,TK_SkaldicMeadBase,TK_JotunDraught,TK_JotunDraughtBase,TK_FireWine,TK_FireWineBase",
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
                    // Vanilla items, or one of our CAULDRON drinks (the Bloody Ivar takes an Ale, owner 2026-11-18): always
                    // craftable first, so no recipe can depend on itself or on a fermenter chain.
                    var ours = Drinks.All.FirstOrDefault(d => d.PrefabName == ing.Item);
                    Check(!itemNames.Contains(ing.Item) || (ours != null && ours.Path == BrewPath.Station && ing.Item != r.ResultPrefab),
                          $"{r.Name}: {ing.Item} is vanilla, or one of our cauldron drinks that is not the result itself");
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
            SymptomTests();
            BlackoutTests();
            ToleranceTests();
            FoodTests();
            EffectTests();

            Console.WriteLine($"{_run - _failed}/{_run} passed" + (_failed > 0 ? $", {_failed} FAILED" : ""));
            return _failed == 0 ? 0 : 1;
        }
    }
}
