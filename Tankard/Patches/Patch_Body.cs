using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard.Patches
{
    /// <summary>Dying carries the hangover to the new body, and in a blackout keeps the map dark and gives the full
    /// post-blackout hangover (owner, 2026-11-18). An observing postfix.</summary>
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

    /// <summary>
    /// A death during a blackout respawns you on fresh ground near where you fell (BlackoutRunner.OnLocalDeath picks it),
    /// never at your bed. The owner, 2026-11-18: going back to the bed "make it to easy to get drunk and get home".
    ///
    /// Game.FindSpawnPoint (private, decompiled 1.0.21) runs every frame while a respawn is pending. After a death it tries
    /// the bed (custom spawn point), then the start temple: each waits m_respawnLoadDuration (8 s) and for the area to be
    /// ready, then takes the ground height. This prefix does the same steps for Tankard's spot instead, and only after a
    /// death (m_respawnAfterDeath; a log-in uses the logout point and is never touched). usedLogoutPoint = true keeps
    /// vanilla from making that spot your new home point. Nothing pending, or another prefix already decided:
    /// vanilla runs. If the spot has no ground (it should: it was picked as dry land), Tankard
    /// lets go and vanilla picks, with a warning, rather than leave you waiting forever.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
    internal static class Patch_BlackoutRespawn
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Game __instance, ref Vector3 point, ref bool usedLogoutPoint, float dt, ref bool __result,
            bool ___m_respawnAfterDeath, ref float ___m_respawnWait, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            try
            {
                if (!___m_respawnAfterDeath || !BlackoutRunner.PeekRespawn(out Vector3 at)) return true;
                ___m_respawnWait += dt;
                usedLogoutPoint = false;
                point = Vector3.zero;
                __result = false;
                ZNet.instance.SetReferencePosition(at);
                if (___m_respawnWait <= __instance.m_respawnLoadDuration || !ZNetScene.instance.IsAreaReady(at)) return false;
                if (!ZoneSystem.instance.GetGroundHeight(at, out float ground))
                {
                    Plugin.Log.LogWarning($"Blackout respawn: no ground at ({at.x:0}, {at.z:0}); letting the game choose.");
                    BlackoutRunner.RespawnUsed(spawnedThere: false);
                    ___m_respawnWait = 0f;
                    return false;
                }
                point = new Vector3(at.x, Mathf.Max(at.y, ground) + 0.25f, at.z);
                usedLogoutPoint = true;
                __result = true;
                BlackoutRunner.RespawnUsed(spawnedThere: true);
                Plugin.Log.LogInfo($"Blackout respawn: you come round at ({point.x:0}, {point.z:0}), after {___m_respawnWait:0.0} s.");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Blackout respawn failed, the game picks instead: {ex.Message}");
                BlackoutRunner.RespawnUsed(spawnedThere: false);
                return true;
            }
        }
    }
}
