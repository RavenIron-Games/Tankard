using System;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard.Patches
{
    /// <summary>
    /// Feeds the meter when the local player drinks one of ours. A postfix that only OBSERVES: it never changes the
    /// result, so it cedes every fight and cannot disturb another mod's consumables (house rule 1).
    ///
    /// Player.ConsumeItem(Inventory, ItemData, bool) returns true only after it has applied the effect and removed one
    /// item (decompiled from 1.0.x), so __result is the "a drink was actually swallowed" signal. A refused attempt (full
    /// of food, wrong item type) returns false and adds nothing.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Patch_Consume
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            try
            {
                if (!__result || !Plugin.Enabled.Value || item == null || item.m_shared == null) return;
                if (__instance != Player.m_localPlayer) return; // the meter belongs to the drinker's own client

                DrinkDef drink = Words.ByNameToken(item.m_shared.m_name);
                if (drink != null) Meter.Drink(drink);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Consume hook failed (the drink was still drunk): {ex.Message}");
            }
        }
    }
}
