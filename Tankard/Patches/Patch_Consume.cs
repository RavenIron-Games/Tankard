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

                // The lost tankard: single use, drinking it is what ends a blackout after a death (BlackoutRunner).
                if (item.m_shared.m_name == LostTankard.NameToken) { BlackoutRunner.DrankLostTankard(__instance, item); return; }

                DrinkDef drink = Words.ByNameToken(item.m_shared.m_name);
                if (drink == null)
                {
                    // Not one of ours. Real food (vanilla's own test in ConsumeItem: m_food > 0) sobers you a little.
                    // Our drinks have their food zeroed, so they never land here.
                    if (item.m_shared.m_food > 0f)
                        Meter.Eat(item.m_shared.m_name, item.m_shared.m_food, item.m_shared.m_foodStamina, item.m_shared.m_foodEitr, __instance,
                            SameFoodBefore(__instance, item.m_shared.m_name));
                    return;
                }
                Meter.Drink(drink, __instance);
                BlackoutRunner.AfterDrink(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Consume hook failed (the drink was still drunk): {ex.Message}");
            }
        }

        /// <summary>
        /// How many of this food were in the stomach BEFORE this meal. The postfix runs after EatFood, so the meal itself
        /// is already in the list and is not counted. Vanilla refreshes an entry in place when you re-eat, so this is 0
        /// there; Fatty appends a new entry per stack (its EatFood prefix never clears the list), so it counts them.
        /// </summary>
        private static int SameFoodBefore(Player p, string nameToken)
        {
            int n = 0;
            var foods = p.GetFoods();
            if (foods == null) return 0;
            foreach (Player.Food f in foods)
                if (f?.m_item?.m_shared != null && f.m_item.m_shared.m_name == nameToken) n++;
            return n > 0 ? n - 1 : 0;
        }
    }
}
