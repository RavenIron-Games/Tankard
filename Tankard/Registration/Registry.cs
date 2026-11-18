using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard.Registration
{
    /// <summary>
    /// The re-entrant registration spine: one TryRegister, called from every point that can populate ObjectDB or
    /// ZNetScene, with no assumption about which fires first or how often. The postfixes only observe; nothing here
    /// replaces vanilla logic.
    /// </summary>
    [HarmonyPatch]
    internal static class Registry
    {
        public static void TryRegister()
        {
            try
            {
                Items.EnsureItems();
                Items.EnsurePrefabs();
                Brewing.EnsureAll();
                Buffs.EnsureIn(ObjectDB.instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Registration pass failed (the game keeps working; drinks may be missing). Reason: {ex}");
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ObjectDB_Awake_Postfix() => TryRegister();

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void ObjectDB_CopyOtherDB_Postfix() => TryRegister();

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void ZNetScene_Awake_Postfix() => TryRegister();
    }

    /// <summary>
    /// Injects the English strings. A postfix on Localization.SetupLanguage re-runs on every language change for free.
    /// Localization.AddWord is PRIVATE, so it is called through a cached delegate (house rule 5).
    /// </summary>
    [HarmonyPatch]
    internal static class LocalizationHook
    {
        private static Action<global::Localization, string, string> _addWord;

        [HarmonyPatch(typeof(global::Localization), nameof(global::Localization.SetupLanguage))]
        [HarmonyPostfix]
        private static void SetupLanguage_Postfix(global::Localization __instance)
        {
            try
            {
                if (_addWord == null)
                {
                    var m = AccessTools.Method(typeof(global::Localization), "AddWord", new[] { typeof(string), typeof(string) });
                    if (m == null)
                    {
                        Plugin.Log.LogError("Localization.AddWord is missing, so drink names show as raw tokens. Valheim's API moved.");
                        return;
                    }
                    _addWord = AccessTools.MethodDelegate<Action<global::Localization, string, string>>(m);
                }
                foreach (KeyValuePair<string, string> kv in Words.English())
                    _addWord(__instance, kv.Key, kv.Value);
                foreach (KeyValuePair<string, string> kv in LostTankard.English())
                    _addWord(__instance, kv.Key, kv.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Localization injection failed (non-fatal: drink names show as raw tokens). Reason: {ex}");
            }
        }
    }
}
