using System;
using System.Collections.Generic;
using System.Linq;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard.Registration
{
    /// <summary>
    /// How the drinks are made: crafting recipes (station drinks, and the base of each fermented one) and the fermenter
    /// conversion that turns a base into drinks. Both are re-run on every registration pass and are idempotent.
    ///
    /// Two rules the first draft of this kind of code usually breaks, kept here on purpose:
    ///  - A recipe whose ingredient cannot be found is WITHHELD, never published without it. Dropping the missing line (as a
    ///    sloppier version would) makes the recipe cheaper, which is a silent balance bug that looks like a working mod.
    ///  - A station is resolved from the scene, and a recipe with no station is a free recipe, so a pass that cannot find
    ///    its station skips the recipe and tries again next pass. On a dedicated server ObjectDB wakes before ZNetScene.
    /// </summary>
    internal static class Brewing
    {
        // Prefab names of the vanilla stations and the fermenter. NOT verified against a running game: a miss logs a warning
        // naming the prefab, once per prefab, and the recipes that need it are withheld.
        private const string CauldronPrefab = "piece_cauldron";
        private const string MeadKetillPrefab = "piece_MeadCauldron";
        private const string FermenterPrefab = "fermenter";

        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static void WarnOnce(string key, string message)
        {
            if (Warned.Add(key)) Plugin.Log.LogWarning(message);
        }

        public static void EnsureAll()
        {
            if (!Plugin.Enabled.Value) return;
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_items.Count == 0 || db.GetItemPrefab("Wood") == null) return;
            if (ZNetScene.instance == null) return;

            foreach (RecipeSpec spec in Drinks.Recipes())
            {
                try { EnsureRecipe(db, spec); }
                catch (Exception ex) { Plugin.Log.LogError($"Could not build {spec.Name}; it will not exist this session. Reason: {ex}"); }
            }
            try { EnsureConversions(); }
            catch (Exception ex) { Plugin.Log.LogError($"Could not add the fermenter conversions. Reason: {ex}"); }
        }

        // ---- recipes ------------------------------------------------------------------------------------------

        private static CraftingStation FindStation(StationKind kind)
        {
            string prefabName = kind == StationKind.Cauldron ? CauldronPrefab : MeadKetillPrefab;
            GameObject go = ZNetScene.instance.GetPrefab(prefabName);
            CraftingStation station = go != null ? go.GetComponent<CraftingStation>() : null;
            if (station == null)
                WarnOnce("station:" + prefabName, $"Crafting station prefab '{prefabName}' not found, so the recipes that need it are withheld. Valheim's names may have moved.");
            return station;
        }

        private static void EnsureRecipe(ObjectDB db, RecipeSpec spec)
        {
            GameObject resultPrefab = Items.Get(spec.ResultPrefab);
            if (!Holder.IsAlive(resultPrefab)) return; // the item is not built yet: retry next pass
            ItemDrop resultDrop = resultPrefab.GetComponent<ItemDrop>();
            if (resultDrop == null) return;

            CraftingStation station = FindStation(spec.Station);
            if (station == null) return;

            var reqs = new List<Piece.Requirement>();
            foreach (Ingredient ing in spec.Ingredients)
            {
                GameObject res = db.GetItemPrefab(ing.Item);
                ItemDrop resDrop = Holder.IsAlive(res) ? res.GetComponent<ItemDrop>() : null;
                if (resDrop == null)
                {
                    WarnOnce("ingredient:" + spec.Name + ":" + ing.Item,
                        $"{spec.Name}: ingredient '{ing.Item}' is not in ObjectDB, so this recipe is withheld rather than published cheaper.");
                    return;
                }
                reqs.Add(new Piece.Requirement { m_resItem = resDrop, m_amount = ing.Amount, m_recover = true });
            }

            if (!Recipes.TryGetValue(spec.Name, out Recipe recipe) || !Holder.IsAlive(recipe))
            {
                recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = spec.Name;
                recipe.hideFlags = HideFlags.HideAndDontSave;
                Recipes[spec.Name] = recipe;
            }

            // Re-applied every pass: the ingredient and station objects can be different instances after a scene reload.
            recipe.m_item = resultDrop;
            recipe.m_amount = spec.Amount;
            recipe.m_enabled = true;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = station;
            recipe.m_repairStation = null;
            recipe.m_resources = reqs.ToArray();

            // m_recipes is a plain list with no register to rebuild, but CopyOtherDB swaps the whole list, so check each pass.
            if (!db.m_recipes.Contains(recipe))
            {
                db.m_recipes.Add(recipe);
                Plugin.Log.LogInfo($"Added {spec.Name}: {string.Join(" + ", spec.Ingredients.Select(i => i.Amount + " " + i.Item))} -> {spec.Amount} {spec.ResultPrefab} at {spec.Station}.");
            }
        }

        // ---- fermenter ----------------------------------------------------------------------------------------

        private static void EnsureConversions()
        {
            GameObject fermenterPrefab = ZNetScene.instance.GetPrefab(FermenterPrefab);
            Fermenter fermenter = fermenterPrefab != null ? fermenterPrefab.GetComponent<Fermenter>() : null;
            if (fermenter == null)
            {
                WarnOnce("fermenter", $"Fermenter prefab '{FermenterPrefab}' not found, so Tankard's fermented drinks cannot be made. Valheim's names may have moved.");
                return;
            }
            if (fermenter.m_conversion == null) fermenter.m_conversion = new List<Fermenter.ItemConversion>();

            foreach (ConversionSpec spec in Drinks.Conversions())
            {
                GameObject from = Items.Get(spec.FromPrefab);
                GameObject to = Items.Get(spec.ToPrefab);
                if (!Holder.IsAlive(from) || !Holder.IsAlive(to)) continue; // items not built yet: retry next pass
                ItemDrop fromDrop = from.GetComponent<ItemDrop>();
                ItemDrop toDrop = to.GetComponent<ItemDrop>();
                if (fromDrop == null || toDrop == null) continue;

                // Matched by the INPUT item's name, which is how Fermenter finds its conversion (IsItemAllowed and
                // GetItemConversion compare m_from.gameObject.name's hash; decompiled). If a conversion for this input is
                // already there, update it in place rather than adding a duplicate.
                Fermenter.ItemConversion existing = fermenter.m_conversion.FirstOrDefault(
                    c => c != null && c.m_from != null && c.m_from.gameObject.name == spec.FromPrefab);
                if (existing == null)
                {
                    fermenter.m_conversion.Add(new Fermenter.ItemConversion { m_from = fromDrop, m_to = toDrop, m_producedItems = spec.Yield });
                    Plugin.Log.LogInfo($"Fermenter: {spec.FromPrefab} -> {spec.Yield} {spec.ToPrefab}.");
                }
                else
                {
                    existing.m_from = fromDrop;
                    existing.m_to = toDrop;
                    existing.m_producedItems = spec.Yield;
                }
            }
        }
    }
}
