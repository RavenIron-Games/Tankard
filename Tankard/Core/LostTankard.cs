using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.Tankard.Core
{
    /// <summary>
    /// The way out of a death during a blackout, and the guard against a death loop (owner, 2026-11-18: "we need a saftey
    /// loop to make sure you dont end in a death loop ie troll kills you and keeps killing you. like dropping a tankard
    /// 'close' to the player to find it and thats what starts the map respawn", then "closer 10-20m away").
    ///
    /// A death in a blackout never respawns you at your bed (that made it "to easy to get drunk and get home"), and not
    /// where you died either, or whatever killed you is waiting. You come round on fresh dry land about
    /// <see cref="RespawnDistance"/> from where you fell, picked fresh on every death, and your lost tankard lies
    /// 10 to 20 m away. It is single-use: drink from it and the blackout ends, your map comes back and the tankard is
    /// gone (owner, 2026-11-18). It must be FOUND within <see cref="FindSeconds"/> of real time, or it despawns and you
    /// walk home ("put a timer on it where if you dont find it in 5 mins real time it despawns"). Once it is in your bag,
    /// it is yours to drink. Walking home still works too.
    /// Pure, like the rest of Core.
    /// </summary>
    public static class LostTankard
    {
        public const string PrefabName = "TK_LostTankard";
        public const string NameToken = "$tk_lost_tankard";
        public const string DescriptionToken = "$tk_lost_tankard_desc";
        public const string DisplayName = "Your lost tankard";
        public const string Blurb = "You had it in your hand when the world went dark. One more drink from it and it all comes back.";
        /// <summary>The vanilla tankard, so it looks like one. A mead bottle if a game build ever drops it.</summary>
        public const string DonorPrefab = "Tankard";
        public const string FallbackDonorPrefab = "MeadHealthMinor";

        /// <summary>Whose tankard it is: the player id, in the item's own custom data, so only its owner's blackout ends.</summary>
        public const string OwnerKey = "Tankard.LostOwner";

        /// <summary>When it was dropped (UTC ticks, real time), in the item's custom data. Survives a relog, unlike a timer.</summary>
        public const string DroppedKey = "Tankard.LostDropped";
        /// <summary>Set once it is in your bag in time: from then on the clock no longer applies.</summary>
        public const string FoundKey = "Tankard.LostFound";
        /// <summary>How long you have to find it, in real seconds.</summary>
        public const double FindSeconds = 300.0;

        public static string FormatTicks(long utcTicks) => utcTicks.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Too late to find it: more than <see cref="FindSeconds"/> of real time since it was dropped. An unreadable stamp
        /// counts as too late (no way to tell), and a stamp from the future (a clock set back) as just dropped.
        /// </summary>
        public static bool IsExpired(string droppedTicks, long nowUtcTicks)
        {
            if (string.IsNullOrEmpty(droppedTicks) ||
                !long.TryParse(droppedTicks, NumberStyles.Integer, CultureInfo.InvariantCulture, out long dropped) || dropped <= 0) return true;
            if (nowUtcTicks <= dropped) return false;
            return (nowUtcTicks - dropped) / (double)TimeSpan.TicksPerSecond > FindSeconds;
        }

        /// <summary>How far from where you died you come round: the landing ring is 80-120% of this, so 180 to 270 m.</summary>
        public const float RespawnDistance = 225f;
        public const float DropMin = 10f, DropMax = 20f;

        public static Dictionary<string, string> English() => new Dictionary<string, string>
        {
            [NameToken.Substring(1)] = DisplayName,
            [DescriptionToken.Substring(1)] = Blurb,
        };

        /// <summary>Where the tankard lands relative to you: a random direction, <see cref="DropMin"/> to <see cref="DropMax"/> m out.</summary>
        public static void DropOffset(Random rng, out float dx, out float dz)
        {
            double angle = rng.NextDouble() * Math.PI * 2.0;
            float r = DropMin + (DropMax - DropMin) * (float)rng.NextDouble();
            dx = (float)Math.Cos(angle) * r;
            dz = (float)Math.Sin(angle) * r;
        }

        /// <summary>
        /// Picks the tankard's spot: dry ground 10-20 m away, at least half a metre above the water. Tries a few directions;
        /// returns false if none is dry (a sandbar, say), and the caller then drops it at your feet.
        /// </summary>
        public static bool PickDrop(float px, float pz, Random rng, Func<float, float, float> groundAt, float waterLevel,
            out float x, out float z, out float y)
        {
            x = px; z = pz; y = 0f;
            if (rng == null || groundAt == null || float.IsNaN(px) || float.IsNaN(pz)) return false;
            for (int i = 0; i < 16; i++)
            {
                DropOffset(rng, out float dx, out float dz);
                float cx = px + dx, cz = pz + dz;
                float h = groundAt(cx, cz);
                if (float.IsNaN(h) || float.IsInfinity(h) || h < waterLevel + 0.5f) continue;
                x = cx; z = cz; y = h;
                return true;
            }
            return false;
        }

        /// <summary>Seconds of real time still left to find it (0 once too late or unreadable). For `tankard find`.</summary>
        public static double SecondsLeft(string droppedTicks, long nowUtcTicks)
        {
            if (IsExpired(droppedTicks, nowUtcTicks)) return 0.0;
            long dropped = long.Parse(droppedTicks, NumberStyles.Integer, CultureInfo.InvariantCulture);
            double gone = nowUtcTicks <= dropped ? 0.0 : (nowUtcTicks - dropped) / (double)TimeSpan.TicksPerSecond;
            return Math.Max(0.0, FindSeconds - gone);
        }

        static readonly string[] Points = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        /// <summary>Which way to walk, in Valheim's axes (+z north, +x east), as one of eight compass points.</summary>
        public static string Compass(float dx, float dz)
        {
            if (float.IsNaN(dx) || float.IsNaN(dz) || (dx == 0f && dz == 0f)) return "right here";
            double deg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            return Points[(int)Math.Round(deg / 45.0) % 8];
        }

        public static string FormatOwner(long playerId) => playerId.ToString(CultureInfo.InvariantCulture);

        /// <summary>True only for a tankard marked with this player's id. Anyone else's, or an unmarked one, ends nothing.</summary>
        public static bool IsMine(string owner, long playerId) =>
            playerId != 0 && !string.IsNullOrEmpty(owner) &&
            long.TryParse(owner, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id == playerId;
    }
}
