using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.Tankard.Core
{
    /// <summary>
    /// The colour each drink's bottle takes in the world, so a cup on a table or on the ground matches its icon (owner,
    /// 2026-11-22: "the icons need to match the drops ingame"). Every value is the LIQUID colour its icon is drawn with in
    /// tools\icons\make-icons.js; keep the two in step. A vanilla mead is one mesh with one painted texture (surveyed by the
    /// robot, phase Y), so there is no liquid part to colour on its own: the tint multiplies the whole bottle.
    /// A fermenter base wears its drink's colour, as its icon's band does.
    /// </summary>
    public static class Tints
    {
        public static readonly IReadOnlyDictionary<string, string> ByDrink = new Dictionary<string, string>
        {
            { "TK_Brambleblush", "#e0507a" },
            { "TK_LonghouseAle", "#c8892b" },
            { "TK_RootstumpGrog", "#7a6a2a" },
            { "TK_BilberryDusk", "#5b2f8f" },
            { "TK_BloodyIvar", "#a3122a" },
            { "TK_HiveGoldMead", "#e0a21c" },
            { "TK_SwampmothersWine", "#4a5a1a" },
            { "TK_RimecaskMead", "#9fd6f0" },
            { "TK_BragisTongue", "#d99b2b" },
            { "TK_YmirsMarrow", "#9a8acb" },
            { "TK_SurtrsKiss", "#ff5a14" },
            { "TK_SkadisNightmare", "#f6f2dc" },
            { "TK_Valhalla", "#170808" },
            { "TK_Kaffi", "#2a160c" },
        };

        /// <summary>The tint for an item (a drink, or a drink's base), or null when it keeps the donor's look.</summary>
        public static string For(ItemSpec spec)
        {
            if (spec?.Drink == null) return null;
            return ByDrink.TryGetValue(spec.Drink.PrefabName, out string hex) ? hex : null;
        }

        /// <summary>"#rrggbb" to 0..1 channels. False for anything else, so a typo keeps the donor's look.</summary>
        public static bool TryParse(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            if (!int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            r = ((v >> 16) & 0xff) / 255f;
            g = ((v >> 8) & 0xff) / 255f;
            b = (v & 0xff) / 255f;
            return true;
        }
    }
}
