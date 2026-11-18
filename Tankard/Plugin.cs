using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard
{
    /// <summary>
    /// SCAFFOLD (2026-11-17): booze and getting drunk. Drinks add to a per-player intoxication meter (Core\Intoxication),
    /// time sobers it, and the tier read off the meter drives the symptoms. Two ways to make a drink: a station recipe,
    /// or the vanilla Fermenter, which makes the strong stuff slowly.
    ///
    /// Nothing is wired into the game yet. This build loads, binds its config and logs the drink table, so the next
    /// step (registering the items, the status effect and the recipes) starts from a plugin that is known to load.
    /// Items are made the zero-Jotunn way Yggdrasil's Reckoning does it: clone a vanilla consumable as a donor, name the
    /// clone, add it to ObjectDB and rebuild its registers. That has to ship server-side as well, or ZNetScene destroys
    /// any hash it cannot resolve.
    /// </summary>
    [BepInPlugin(Guid, "Tankard", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.raveniron.tankard";
        public const string Version = "0.1.0";
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> SoberPerSecond;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("1 - Core", "Enabled", true,
                "Master switch. Off, Tankard adds nothing and changes nothing.");
            SoberPerSecond = Config.Bind("2 - Drunkenness", "SoberPerSecond", 0.25f,
                new ConfigDescription("How fast the intoxication meter falls, in meter units a second. The meter runs 0 to " +
                                      Intoxication.Max + ", so 0.25 is about six and a half minutes from a full meter to sober.",
                    new AcceptableValueRange<float>(0.01f, 10f)));
            try
            {
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"Tankard {Version} loaded (enabled: {Enabled.Value}), {Drinks.All.Count} drink(s) defined.");
                foreach (var d in Drinks.All)
                    Log.LogInfo($"  {d.PrefabName}: {d.DisplayName}, {d.Path}, +{d.Potency}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Harmony patching failed: {ex}");
            }
        }

        private void Update()
        {
            // The one clock. A player who is not in a world (menu, loading) has no meter.
            if (Player.m_localPlayer == null) { Meter.Reset(); return; }
            Meter.Tick(UnityEngine.Time.deltaTime);
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
