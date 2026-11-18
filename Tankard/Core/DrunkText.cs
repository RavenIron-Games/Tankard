using System;
using System.Text;

namespace RavenIron.Tankard.Core
{
    /// <summary>
    /// Turns what a drunk player types into what comes out of their mouth. Pure: the caller supplies the random source, so
    /// the harness can seed it and assert on exact behaviour.
    ///
    /// The effect climbs with <c>severity</c> (0 to 1, from <see cref="Intoxication.Severity"/>):
    /// a stretched vowel or a typo at the first sign of it, dropped letters and wrong keys in the middle, and at the top
    /// most letters are replaced outright, so the message is gibberish with the shape of a sentence.
    ///
    /// What it never touches, because chat is read by other people and some of it is machine-read:
    ///  - whitespace, so word boundaries (and the look of a sentence) survive;
    ///  - digits, punctuation, and anything that is not a plain ASCII letter;
    ///  - rich-text tags such as &lt;color=red&gt;;
    ///  - the FIRST letter of each word is only ever swapped for another letter, never dropped, so no word vanishes.
    /// </summary>
    public static class DrunkText
    {
        public const string Hiccup = " *hic*";

        // Row-neighbours on a QWERTY keyboard: what a fat finger hits instead.
        private static readonly string[] Neighbours =
        {
            /* a */ "qwsz", /* b */ "vghn", /* c */ "xdfv", /* d */ "serfcx", /* e */ "wsdr", /* f */ "drtgvc",
            /* g */ "ftyhbv", /* h */ "gyujnb", /* i */ "ujko", /* j */ "huikmn", /* k */ "jiolm", /* l */ "kop",
            /* m */ "njk", /* n */ "bhjm", /* o */ "iklp", /* p */ "ol", /* q */ "wa", /* r */ "edft",
            /* s */ "awedxz", /* t */ "rfgy", /* u */ "yhji", /* v */ "cfgb", /* w */ "qase", /* x */ "zsdc",
            /* y */ "tghu", /* z */ "asx",
        };

        private const string Vowels = "aeiouAEIOU";

        /// <summary>
        /// Garbles <paramref name="text"/>. <paramref name="severity"/> is clamped to [0, 1] and scaled by
        /// <paramref name="strength"/> (a config multiplier; 0 turns the effect off). Zero effective severity, null or
        /// empty text returns the input untouched, so a sober player's words are never rewritten.
        /// </summary>
        public static string Garble(string text, float severity, Random rng, float strength = 1f)
        {
            if (string.IsNullOrEmpty(text) || rng == null) return text;
            float p = Clamp01(severity) * (float.IsNaN(strength) || strength < 0f ? 0f : strength);
            p = Clamp01(p);
            if (p <= 0f) return text;

            int cap = text.Length + 16; // never let a short message balloon
            var sb = new StringBuilder(text.Length + 8);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // A rich-text tag passes through whole, or the chat window would show broken markup.
                if (c == '<')
                {
                    int close = text.IndexOf('>', i);
                    if (close > i)
                    {
                        sb.Append(text, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }

                if (!IsAsciiLetter(c))
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                // One word: a run of non-space characters. Letters inside are altered; everything else stays put.
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '<') i++;
                AppendWord(sb, text, start, i, p, rng);
            }

            if (sb.Length > cap) sb.Length = cap;
            if (p > 0.25f && rng.NextDouble() < Math.Min(1.0, p * 0.8)) sb.Append(Hiccup); // after the cap, so it always fits
            return sb.ToString();
        }

        private static void AppendWord(StringBuilder sb, string text, int start, int end, float p, Random rng)
        {
            // Pull the word's characters out, perhaps swapping two neighbouring letters first (a clumsy finger).
            var chars = new char[end - start];
            text.CopyTo(start, chars, 0, chars.Length);
            if (chars.Length > 2 && rng.NextDouble() < 0.10 * p)
            {
                int k = rng.Next(0, chars.Length - 1);
                if (IsAsciiLetter(chars[k]) && IsAsciiLetter(chars[k + 1]))
                {
                    char t = chars[k]; chars[k] = chars[k + 1]; chars[k + 1] = t;
                }
            }

            double corrupt = p * p * 0.9;  // outright replacement: this is what makes the top end gibberish
            double typo = 0.25 * p;        // a wrong key beside the right one
            double drop = 0.05 * p;        // a letter that never lands
            double stretch = 0.20 * p;     // a vowel held a beat too long
            double flip = 0.05 * p;        // shift key at the wrong moment

            for (int j = 0; j < chars.Length; j++)
            {
                char c = chars[j];
                if (!IsAsciiLetter(c)) { sb.Append(c); continue; }

                double r = rng.NextDouble();
                char outc = c;
                bool dropped = false;

                if (r < corrupt) outc = RandomLetter(rng, char.IsUpper(c));
                else if ((r -= corrupt) < typo) outc = NeighbourOf(c, rng);
                else if ((r -= typo) < drop && j > 0) dropped = true; // j > 0: the first letter of a word always survives

                if (dropped) continue;

                if (rng.NextDouble() < flip) outc = char.IsUpper(outc) ? char.ToLowerInvariant(outc) : char.ToUpperInvariant(outc);
                sb.Append(outc);

                if (Vowels.IndexOf(c) >= 0 && rng.NextDouble() < stretch)
                {
                    int extra = 1 + rng.Next(0, 3);
                    for (int e = 0; e < extra; e++) sb.Append(outc);
                }
            }
        }

        private static char NeighbourOf(char c, Random rng)
        {
            bool upper = char.IsUpper(c);
            char lower = char.ToLowerInvariant(c);
            string n = Neighbours[lower - 'a'];
            char pick = n[rng.Next(0, n.Length)];
            return upper ? char.ToUpperInvariant(pick) : pick;
        }

        private static char RandomLetter(Random rng, bool upper)
        {
            char c = (char)('a' + rng.Next(0, 26));
            return upper ? char.ToUpperInvariant(c) : c;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : (v < 0f ? 0f : (v > 1f ? 1f : v));
    }
}
