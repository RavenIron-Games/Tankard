using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard.Patches
{
    /// <summary>Dying while blacked out ends the blackout: two graves are enough (owner, 2026-11-18). An observing postfix.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class Patch_Death
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            try { if (__instance != null && __instance == Player.m_localPlayer) BlackoutRunner.OnLocalDeath(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Death hook failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Each Tankard drink ferments for its own time (owner, 2026-11-18: "change the times to match the drinks").
    ///
    /// Vanilla keeps one m_fermentationDuration per barrel and reads it in exactly one place, the private
    /// Fermenter.GetStatus (decompiled 1.0.21: Ready once the ZDO's start time is older than it). So the prefix sets
    /// the field to our drink's time for the length of that one call and the finalizer puts the barrel's own value back,
    /// whatever happens in between. A vanilla mead base in the same barrel keeps vanilla's time. The content is the ZDO's
    /// int, the stable hash of the base's prefab name (RPC_AddItem stores m_dropPrefab.name.GetStableHashCode()).
    /// Read on whichever game looks at the barrel, so every player needs the mod (they do anyway: the items need it).
    /// </summary>
    [HarmonyPatch(typeof(Fermenter), "GetStatus")]
    internal static class Patch_FermentTime
    {
        private static Func<Fermenter, int> _getContent;
        private static bool _loggedMissing;
        private static Dictionary<int, float> _times;

        private static bool Resolve()
        {
            if (_getContent != null) return true;
            try
            {
                var m = AccessTools.Method(typeof(Fermenter), "GetContent");
                if (m != null) _getContent = AccessTools.MethodDelegate<Func<Fermenter, int>>(m);
            }
            catch (Exception) { _getContent = null; }
            if (_getContent == null && !_loggedMissing)
            {
                _loggedMissing = true;
                Plugin.Log.LogError("Fermenter.GetContent not found: Valheim's API moved. Tankard drinks ferment for the vanilla time.");
            }
            return _getContent != null;
        }

        private static Dictionary<int, float> Times()
        {
            if (_times != null) return _times;
            var t = new Dictionary<int, float>();
            foreach (var d in Drinks.All)
                if (d.Path == BrewPath.Fermenter && d.FermentSeconds > 0f) t[d.BasePrefabName.GetStableHashCode()] = d.FermentSeconds;
            return _times = t;
        }

        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static void Prefix(Fermenter __instance, bool __runOriginal, out float __state)
        {
            __state = -1f;
            if (!__runOriginal || !Plugin.Enabled.Value || !Plugin.PerDrinkFermentTimes.Value || __instance == null || !Resolve()) return;
            try
            {
                if (!Times().TryGetValue(_getContent(__instance), out float seconds)) return;
                __state = __instance.m_fermentationDuration;
                __instance.m_fermentationDuration = seconds;
            }
            catch (Exception ex) { Plugin.Report("Ferment time", ex); }
        }

        [HarmonyFinalizer]
        private static void Finalizer(Fermenter __instance, float __state)
        {
            if (__state >= 0f && __instance != null) __instance.m_fermentationDuration = __state;
        }
    }
}
