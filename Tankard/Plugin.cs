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
        internal static ConfigEntry<bool> GarbleChat;
        internal static ConfigEntry<float> GarbleStrength;
        internal static ConfigEntry<bool> CameraSway;
        internal static ConfigEntry<bool> Wobble;
        internal static ConfigEntry<bool> Stumbles;
        internal static ConfigEntry<float> SymptomStrength;

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
            GarbleChat = Config.Bind("3 - Drunk chat", "GarbleChat", true,
                "While drunk, what you type in chat (say, shout and whisper) comes out slurred, then garbled. Read on YOUR game, " +
                "since it is your client that sends the line. Off, your words always come out as typed.");
            GarbleStrength = Config.Bind("3 - Drunk chat", "GarbleStrength", 1f,
                new ConfigDescription("How hard drunkenness garbles chat. 1 is normal, 0.5 is half as bad, 2 reaches full gibberish " +
                                      "at half the intoxication. 0 turns it off.",
                    new AcceptableValueRange<float>(0f, 2f)));
            CameraSway = Config.Bind("4 - Symptoms", "CameraSway", true,
                "Past Tipsy the camera leans, bobs, drifts off your aim and breathes in and out. Turn it off if it makes you feel " +
                "sick for real. Read on your own game.");
            Wobble = Config.Bind("4 - Symptoms", "Wobble", true,
                "Past Tipsy your walk drifts side to side. Only while moving; standing still never slides you. Read on your own game.");
            Stumbles = Config.Bind("4 - Symptoms", "Stumbles", true,
                "Once hammered you sometimes trip (vanilla's stagger). Never while swimming, steering, sitting, in the air or " +
                "already staggering, and never two within 8 seconds. Read on your own game.");
            SymptomStrength = Config.Bind("4 - Symptoms", "SymptomStrength", 1f,
                new ConfigDescription("Scales sway, wobble and stumbling together. 0 is off, 2 hits full strength at half the meter.",
                    new AcceptableValueRange<float>(0f, 2f)));
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
            float dt = UnityEngine.Time.deltaTime;
            Meter.Tick(dt);
            if (Enabled.Value) Stumbler.Tick(dt);
        }

        // ---- fault reporting from the hot path ----------------------------------------------------------------------
        // The camera and movement patches run every frame, so a fault that keeps happening would bury the log. Each site
        // reports its first fault in full, then once per interval with the number suppressed in between (Fathom's shape).
        private const float ReportEverySeconds = 30f;
        private static readonly System.Collections.Generic.Dictionary<string, float> _nextReport = new System.Collections.Generic.Dictionary<string, float>();
        private static readonly System.Collections.Generic.Dictionary<string, long> _suppressed = new System.Collections.Generic.Dictionary<string, long>();

        internal static void Report(string where, Exception ex)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_nextReport.TryGetValue(where, out float next) && now < next)
            {
                _suppressed[where] = (_suppressed.TryGetValue(where, out long n) ? n : 0) + 1;
                return;
            }
            _suppressed.TryGetValue(where, out long gone);
            _suppressed[where] = 0;
            _nextReport[where] = now + ReportEverySeconds;
            Log.LogWarning($"{where}: {ex}" + (gone > 0 ? $" (and {gone} more since the last line)" : ""));
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
