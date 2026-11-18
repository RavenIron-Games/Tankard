using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using ServerSync;

namespace RavenIron.Tankard
{
    /// <summary>
    /// The server's settings, enforced on every player, through blaxxun's ServerSync (Vendor\ServerSync.cs, byte-identical
    /// to Ragnarok's Wrath's and Valkyrie's Cargo's copy; never edit it). Added 2026-11-18 (owner: "add server sync").
    ///
    /// WHY. Almost everything Tankard does runs on each drinker's own game: the meter, the blackout, the chat garble, the
    /// buffs. Without this a player could switch blackouts off, or set SoberPerSecond to 10, in their own file.
    ///
    /// WHAT. Every setting follows the server, except <see cref="Local"/>. A player's own file is left exactly as it was:
    /// ServerSync holds the local value aside and puts it back on disconnect, and never saves the server's into it. With
    /// LockConfiguration on (the default) only an admin on adminlist.txt can change a synced value from a client, and that
    /// change goes to the server and out to everyone. In single player the game is its own server and nothing changes.
    ///
    /// THE VERSION GATE, strict, as in Ragnarok's Wrath: ModRequired, minimum = this build. A player without Tankard, or
    /// on another version, is refused at the handshake with a message naming both. Every player needs the mod anyway: a
    /// game without it cannot resolve the drinks' prefabs.
    ///
    /// ORDER. Registered after every Bind. ServerSync's own Harmony patches arrive with Tankard's PatchAll (its nested
    /// classes carry the attributes).
    /// </summary>
    internal static class ServerSyncSetup
    {
        public static ConfigSync Sync { get; private set; }
        public static ConfigEntry<bool> LockConfiguration { get; private set; }
        public static int SyncedCount { get; private set; }
        public static int LocalCount { get; private set; }

        /// <summary>Settings each player keeps for themselves. CameraSway only: its own description says to turn it off if
        /// it makes you feel sick for real, and a server must never force motion sickness on anyone.</summary>
        private static readonly HashSet<string> Local = new HashSet<string> { "CameraSway" };

        public static void Register(ConfigFile config)
        {
            Sync = new ConfigSync(Plugin.Guid)
            {
                DisplayName = "Tankard",
                CurrentVersion = Plugin.Version,
                MinimumRequiredVersion = Plugin.Version,
                ModRequired = true,
            };

            LockConfiguration = config.Bind("1 - Core", "LockConfiguration", true,
                "Only admins (adminlist.txt) may change Tankard's settings from a player's game. Every player always gets the " +
                "server's settings either way (except CameraSway, which each player keeps for themselves).");
            Sync.AddLockingConfigEntry(LockConfiguration);

            // ConfigFile is an IDictionary; copy the entries first so a registration cannot disturb the walk.
            var entries = new List<ConfigEntryBase>();
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in config)
                if (kv.Value != null && !ReferenceEquals(kv.Value, LockConfiguration)) entries.Add(kv.Value);

            MethodInfo add = typeof(ConfigSync).GetMethod(nameof(ConfigSync.AddConfigEntry));
            foreach (ConfigEntryBase entry in entries)
            {
                try
                {
                    // AddConfigEntry is generic over the entry's type, and the file holds bools, ints and floats side by side.
                    var own = (OwnConfigEntryBase)add.MakeGenericMethod(entry.SettingType).Invoke(Sync, new object[] { entry });
                    if (Local.Contains(entry.Definition.Key)) { own.SynchronizedConfig = false; LocalCount++; }
                    else SyncedCount++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"ServerSync: could not register [{entry.Definition.Section}] {entry.Definition.Key}: " +
                                          $"{ex.Message}. It stays a local setting.");
                }
            }
            Plugin.Log.LogInfo($"ServerSync: {SyncedCount} setting(s) follow the server on every player's game, {LocalCount} stay " +
                               $"local (CameraSway). Every player needs exactly Tankard {Plugin.Version}.");
        }

        /// <summary>One line for `tankard status`: where this machine's settings come from.</summary>
        public static string Describe()
        {
            ConfigSync s = Sync;
            if (s == null) return "config sync: not set up";
            return "config sync: " + (s.IsSourceOfTruth ? "this machine's own file" : "following the server") +
                   $", locked {(s.IsLocked ? "yes" : "no")}, admin here {(s.IsAdmin ? "yes" : "no")}, {SyncedCount} synced / {LocalCount} local";
        }
    }
}
