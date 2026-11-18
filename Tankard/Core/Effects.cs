using System;
using System.Globalization;

namespace RavenIron.Tankard.Core
{
    /// <summary>A resistance a drink can grant. Mapped to vanilla's damage types in the registration layer.</summary>
    public enum Resist { None, Fire, Frost, Poison }

    /// <summary>
    /// What a drink does for you besides get you drunk (owner, 2026-11-18: "love all the ones in 1"). Plain data; the
    /// registration layer turns each into a vanilla SE_Stats, so the effect reads, ticks and shows like a vanilla mead's.
    /// Regen multipliers follow vanilla's SE_Stats: above 1 adds (1.5 is +50%), below 1 multiplies.
    /// </summary>
    public sealed class DrinkEffect
    {
        public string Name;              // shown on the status icon
        public float Seconds;
        public float HealthRegen = 1f;
        public float StaminaRegen = 1f;
        public float EitrRegen = 1f;
        public float CarryWeight;        // added to max carry weight
        public float RunStaminaDrain;    // added fraction: -0.15 is 15% less stamina spent running
        public Resist Resist;

        /// <summary>The tooltip line, in plain words, built from the numbers so it can never disagree with them.</summary>
        public string Describe()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (HealthRegen != 1f) parts.Add(Pct(HealthRegen) + " health regen");
            if (StaminaRegen != 1f) parts.Add(Pct(StaminaRegen) + " stamina regen");
            if (EitrRegen != 1f) parts.Add(Pct(EitrRegen) + " eitr regen");
            if (CarryWeight != 0f) parts.Add((CarryWeight > 0 ? "+" : "") + CarryWeight.ToString("0", CultureInfo.InvariantCulture) + " carry weight");
            if (RunStaminaDrain != 0f) parts.Add(Signed(RunStaminaDrain) + " stamina spent running");
            if (Resist != Resist.None) parts.Add(Resist.ToString().ToLowerInvariant() + " resistant");
            return string.Join(", ", parts) + " for " + Effects.Minutes(Seconds);
        }

        private static string Pct(float m) => Signed(m - 1f);
        private static string Signed(float f) => (f > 0 ? "+" : "") + Math.Round(f * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    public static class Effects
    {
        public static string Minutes(float seconds)
        {
            int m = (int)Math.Round(seconds / 60f);
            return m == 1 ? "1 minute" : m + " minutes";
        }

        // ---- liquid courage: while Drunk or worse you hit harder and block worse --------------------------------------
        /// <summary>Damage multiplier from being drunk. 1 below Drunk, 1.10 Drunk, 1.20 Hammered and Blackout.</summary>
        public static float CourageDamage(Tier t) => t >= Tier.Hammered ? 1.20f : t >= Tier.Drunk ? 1.10f : 1f;
        /// <summary>Extra stamina a block costs, as a fraction of vanilla's. 0 below Drunk, +25% Drunk, +50% Hammered.</summary>
        public static float CourageBlockCost(Tier t) => t >= Tier.Hammered ? 0.50f : t >= Tier.Drunk ? 0.25f : 0f;

        // ---- hangovers ----------------------------------------------------------------------------------------------
        /// <summary>A binge whose meter never reached this leaves no hangover.</summary>
        public const float HangoverFromPeak = 50f; // Drunk
        public const float HangoverMinSeconds = 300f, HangoverMaxSeconds = 900f, HangoverAfterBlackoutSeconds = 1200f;
        public const float HangoverHealthRegen = 0.75f, HangoverStaminaRegen = 0.6f;

        /// <summary>How long the hangover lasts, from the binge's peak: none under Drunk, 5 minutes at Drunk rising to 15
        /// at a full meter, and 20 after a blackout.</summary>
        public static float HangoverSeconds(float peak, bool blackedOut)
        {
            if (blackedOut) return HangoverAfterBlackoutSeconds;
            if (float.IsNaN(peak) || peak < HangoverFromPeak) return 0f;
            float p = Math.Min(1f, (peak - HangoverFromPeak) / (Intoxication.Max - HangoverFromPeak));
            return HangoverMinSeconds + (HangoverMaxSeconds - HangoverMinSeconds) * p;
        }

        /// <summary>
        /// The hangover the new body wakes with after a death. Dying never cures one (a fresh body has no status effects,
        /// so without this every death quietly did), and dying in a blackout gives the full post-blackout hangover: the
        /// owner, 2026-11-18, "dying in a black out is the easy way out". Whichever is longer wins.
        /// </summary>
        public static float HangoverAfterDeath(float leftNow, bool inBlackout)
        {
            float left = float.IsNaN(leftNow) || float.IsInfinity(leftNow) || leftNow < 0f ? 0f : leftNow;
            return inBlackout ? Math.Max(left, HangoverAfterBlackoutSeconds) : left;
        }

        /// <summary>A new hangover never shortens one already running (the binge ending after a death in a blackout,
        /// say, must not cut a 20-minute one to 5).</summary>
        public static bool ShouldReplaceHangover(float runningLeft, float offered) =>
            !(runningLeft > 0f) || offered > runningLeft;

        /// <summary>
        /// One binge, one hangover (owner, 2026-11-18). A death in a blackout hands out the binge's hangover there and then
        /// (HangoverAfterDeath), so it also SETTLES the binge: its peak and its blackout are cleared, and the binge ending
        /// later charges nothing more for them. Seen live on Storm10: the death gave 20 minutes, a Bloody Ivar cured it,
        /// and the meter reaching 0 gave the same binge another 20. Drinks after the death raise the peak again and can
        /// earn their own hangover. With hangovers off nothing was handed out, so nothing is settled.
        /// </summary>
        public static void SettleAfterDeath(bool inBlackout, bool hangoversOn, ref float peak, ref bool blackedOut)
        {
            if (!inBlackout || !hangoversOn) return;
            peak = 0f;
            blackedOut = false;
        }

        /// <summary>The peak a hangover is sized from, after a drink. A cure (the Bloody Ivar) never raises it: drinking the
        /// cure must not earn the next hangover.</summary>
        public static float PeakAfterDrink(float peak, float level, bool curesHangover)
        {
            if (curesHangover || float.IsNaN(level)) return peak;
            return level > peak ? level : peak;
        }

        // ---- fermenting -------------------------------------------------------------------------------------------
        /// <summary>Vanilla's barrel time (Fermenter.m_fermentationDuration, decompiled 1.0.21): 2400 s of world time.</summary>
        public const float VanillaFermentSeconds = 2400f;
    }

    /// <summary>
    /// The meter as it is saved on the character, so quitting does not sober you up (owner, 2026-11-18: "hold the drunk ...
    /// so no rage quits happen"). Time does not pass while you are logged out: you come back exactly as drunk as you left.
    /// </summary>
    public struct MeterState
    {
        public float Level;
        public int Binge;
        public float Peak;
        public bool BlackedOut;
        public float HangoverLeft;

        public const string Key = "Tankard.Meter";

        public string Format() => string.Join(";",
            F(Level), Binge.ToString(CultureInfo.InvariantCulture), F(Peak), BlackedOut ? "1" : "0", F(HangoverLeft));

        public static MeterState Parse(string s)
        {
            var st = new MeterState();
            if (string.IsNullOrEmpty(s)) return st;
            string[] p = s.Split(';');
            if (p.Length < 5) return st;
            st.Level = Clamp(P(p[0]), Intoxication.Cap);
            int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out st.Binge);
            if (st.Binge < 0) st.Binge = 0;
            st.Peak = Clamp(P(p[2]), Intoxication.Cap);
            st.BlackedOut = p[3] == "1";
            st.HangoverLeft = Clamp(P(p[4]), Effects.HangoverAfterBlackoutSeconds);
            return st;
        }

        private static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        private static float Clamp(float f, float max) => float.IsNaN(f) || float.IsInfinity(f) || f < 0f ? 0f : (f > max ? max : f);
    }
}
