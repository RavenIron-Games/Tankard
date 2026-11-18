using System;

namespace RavenIron.Tankard.Core
{
    /// <summary>How far the camera is nudged off true this frame: degrees of roll, pitch and yaw, and degrees of field of view.</summary>
    public struct CameraSway
    {
        public float Roll, Pitch, Yaw, Fov;
        public static readonly CameraSway None = new CameraSway();
    }

    /// <summary>
    /// What being drunk does to the body, as pure functions of time and severity (0 to 1, from <see cref="Intoxication.Severity"/>).
    /// Everything is a sum of slow sines with unrelated periods, so it never visibly repeats, never jumps, and is exactly
    /// zero at zero severity. Amplitudes scale linearly with severity, so a little drunk is a hint and full is a lurch.
    ///
    /// Time is passed in, not read, so the harness can walk it frame by frame and prove the motion is smooth and bounded.
    /// </summary>
    public static class Symptoms
    {
        // Largest value each effect reaches at severity 1. These are the ceilings the tests pin.
        public const float MaxRollDegrees = 10f;
        public const float MaxPitchDegrees = 3f;
        public const float MaxYawDegrees = 5f;
        public const float MaxFovDegrees = 7f;
        public const float MaxMoveDrift = 0.70f;   // fraction of full stick, sideways


        public static CameraSway Camera(float t, float severity)
        {
            float s = Clean(severity);
            if (s <= 0f || float.IsNaN(t) || float.IsInfinity(t)) return CameraSway.None;
            return new CameraSway
            {
                // Two incommensurate periods, so the lean wanders instead of ticking like a metronome.
                Roll = s * MaxRollDegrees * (0.60f * Sin((double)t / 6.5) + 0.40f * Sin((double)t / 3.7 + 1.3)),
                Pitch = s * MaxPitchDegrees * Sin((double)t / 4.9 + 0.5),
                Yaw = s * MaxYawDegrees * Sin((double)t / 8.3 + 2.0),
                Fov = s * MaxFovDegrees * Sin((double)t / 7.1 + 0.9),
            };
        }

        /// <summary>Sideways push added to the movement stick, in [-MaxMoveDrift, MaxMoveDrift] times severity. Right is positive.</summary>
        public static float MoveDrift(float t, float severity)
        {
            float s = Clean(severity);
            if (s <= 0f || float.IsNaN(t) || float.IsInfinity(t)) return 0f;
            return s * MaxMoveDrift * (0.65f * Sin((double)t / 2.9) + 0.35f * Sin((double)t / 1.7 + 0.8));
        }

        // ---- stumbling ------------------------------------------------------------------------------------------

        public const float StumbleStartSeverity = 0.35f;  // below this you wobble but you do not fall over
        public const float StumbleCooldown = 5f;           // seconds between stumbles when merely hammered
        public const float BlackoutStumbleCooldown = 2.5f;
        public const float MaxStumbleChancePerSecond = 0.12f; // the owner asked for more (2026-11-17): ~1 per 8 s at full severity, plus the cooldown
        public const float BlackoutChanceMultiplier = 3f;
        public const float MaxFrame = 0.25f;                // a hitch of a second must not count as a second of chances

        /// <summary>Chance per second of a stumble. Zero until <see cref="StumbleStartSeverity"/>, then a ramp to the maximum.</summary>
        public static double StumbleChancePerSecond(float severity, Tier tier)
        {
            float s = Clean(severity);
            if (s <= StumbleStartSeverity) return 0.0;
            double ramp = (s - StumbleStartSeverity) / (1f - StumbleStartSeverity);
            double chance = ramp * MaxStumbleChancePerSecond;
            return tier == Tier.Blackout ? chance * BlackoutChanceMultiplier : chance;
        }

        /// <summary>
        /// Whether a stumble happens this frame. <paramref name="roll"/> is a uniform draw in [0, 1) from the caller, and
        /// <paramref name="secondsSinceLast"/> enforces the cooldown so two stumbles never chain into a stunlock.
        /// </summary>
        public static bool ShouldStumble(float severity, Tier tier, float dt, double roll, float secondsSinceLast)
        {
            if (float.IsNaN(dt) || dt <= 0f || double.IsNaN(roll)) return false;
            float cooldown = tier == Tier.Blackout ? BlackoutStumbleCooldown : StumbleCooldown;
            if (!(secondsSinceLast >= cooldown)) return false;
            double chance = StumbleChancePerSecond(severity, tier) * Math.Min(dt, MaxFrame);
            return roll < chance;
        }

        // Phase in DOUBLE and wrapped to one cycle before the multiply. In float, t / period * 2pi passes 10^5 radians after
        // a few hours of play (Time.time never resets in a session), where float resolution is ~0.01 rad and the sway would
        // visibly jitter. The harness caught the float version exceeding its own derivative bound at only t = 600 s.
        private static float Sin(float cycles) => Sin((double)cycles);
        private static float Sin(double cycles)
        {
            double frac = cycles - Math.Floor(cycles);
            return (float)Math.Sin(frac * (2.0 * Math.PI));
        }
        private static float Clean(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : (v < 0f ? 0f : (v > 1f ? 1f : v));
    }
}
