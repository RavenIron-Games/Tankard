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
                // A frost mead thaws Skadi's Frozen morning: vanilla's, or our own Rimecask Mead.
                if (item.m_shared.m_name == FrostMeadToken() || drink?.PrefabName == "TK_RimecaskMead") SkadiRunner.Thaw(__instance, "a frost mead");
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
                if (drink.Special == Special.Valhalla && Plugin.Valhalla.Value) ValhallaRunner.Drank(__instance);
                else BlackoutRunner.AfterDrink(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Consume hook failed (the drink was still drunk): {ex.Message}");
            }
        }

        private static string _frostMeadToken;
        /// <summary>Vanilla's frost resistance mead, by the name token its item carries (read from ObjectDB, not guessed).</summary>
        private static string FrostMeadToken()
        {
            if (_frostMeadToken != null) return _frostMeadToken;
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("MeadFrostResist") : null;
            string token = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name : null;
            if (!string.IsNullOrEmpty(token)) _frostMeadToken = token;
            return token;
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

    /// <summary>
    /// Nobody drinks a second Valhalla while the first one's three days run (owner, 2026-11-18). A behaviour prefix:
    /// Priority.Low, honours __runOriginal, and has no opinion about anything else ("no opinion" is return true). Refusing
    /// returns false before vanilla takes the item, so the cup stays in the bag.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Patch_RefuseValhalla
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(Player __instance, ItemDrop.ItemData item, ref bool __result, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || !Plugin.Enabled.Value || !Plugin.Valhalla.Value) return true;
                if (__instance != Player.m_localPlayer || item?.m_shared == null) return true;
                DrinkDef drink = Words.ByNameToken(item.m_shared.m_name);
                if (drink == null || drink.Special != Special.Valhalla) return true;
                if (!ValhallaRunner.Refuses(__instance)) return true;
                Plugin.Log.LogInfo("The Valhalla refused: the last one's hangover is still running.");
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Valhalla refusal check failed (the drink is allowed): {ex.Message}");
                return true;
            }
        }
    }
}
