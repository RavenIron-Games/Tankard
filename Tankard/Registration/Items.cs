using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard.Registration
{
    /// <summary>
    /// Builds and registers the drinks without Jotunn: clone a vanilla consumable, name the clone, add it to ObjectDB,
    /// rebuild ObjectDB's registers, then add the same prefab to ZNetScene. Shape and traps are Yggdrasil's Reckoning's
    /// Registration\Items.cs (see CLAUDE.md), re-read against the 1.0.x assembly for this mod.
    ///
    /// Two passes, both idempotent and re-entrant, because ObjectDB and ZNetScene wake in no guaranteed order:
    /// EnsureItems needs ObjectDB; EnsurePrefabs needs ZNetScene.
    /// </summary>
    internal static class Items
    {
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        // ObjectDB.UpdateRegisters is PRIVATE (decompiled from 1.0.x: it clears and refills m_itemByHash / m_itemByData).
        // Without it ItemDrop.Awake cannot resolve a clone's m_dropPrefab and the item saves as an empty string. The
        // publicized assembly would let this compile as a plain call and Mono would refuse it in game (house rule 5), so
        // it goes through a cached delegate. Resolution retries until it succeeds rather than latching a failure.
        private static Action<ObjectDB> _updateRegisters;
        private static bool _loggedMissingRegisters;

        private static bool ResolveUpdateRegisters()
        {
            if (_updateRegisters != null) return true;
            try
            {
                var m = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
                if (m != null) _updateRegisters = AccessTools.MethodDelegate<Action<ObjectDB>>(m);
            }
            catch (Exception ex)
            {
                if (!_loggedMissingRegisters) Plugin.Log.LogError($"ObjectDB.UpdateRegisters did not resolve: {ex.Message}");
            }
            if (_updateRegisters == null && !_loggedMissingRegisters)
            {
                _loggedMissingRegisters = true;
                Plugin.Log.LogError("ObjectDB.UpdateRegisters is missing, so the drinks cannot be indexed. Valheim's API moved.");
            }
            return _updateRegisters != null;
        }

        /// <summary>Every item Tankard registers: the drinks and their bases, then the lost tankard.</summary>
        private static List<ItemSpec> AllSpecs()
        {
            var list = Drinks.Items();
            list.Add(ItemSpec.LostTankardSpec());
            return list;
        }

        public static GameObject Get(string prefabName) =>
            Prefabs.TryGetValue(prefabName, out GameObject go) ? go : null;

        // ---- ObjectDB pass ------------------------------------------------------------------------------------

        public static void EnsureItems()
        {
            if (!Plugin.Enabled.Value) return;
            ObjectDB db = ObjectDB.instance;

            // FejdStartup's ObjectDB.Awake fires with an empty list, before CopyOtherDB fills it. Building against that DB
            // finds no donors and registers into a list the real one is about to replace.
            if (db == null || db.m_items == null || db.m_items.Count == 0 || db.GetItemPrefab("Wood") == null) return;

            bool added = false;
            foreach (ItemSpec def in AllSpecs())
            {
                try
                {
                    GameObject prefab = BuildOrGet(db, def);
                    if (prefab == null) continue;
                    ApplyStats(def, prefab); // every pass: CopyOtherDB can hand us a different list
                    if (!db.m_items.Contains(prefab))
                    {
                        db.m_items.Add(prefab);
                        added = true;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Could not build '{def.PrefabName}'; it will not exist this session. Reason: {ex}");
                }
            }

            if (added && ResolveUpdateRegisters())
            {
                _updateRegisters(db);
                Plugin.Log.LogInfo($"Registered {Prefabs.Count} drink item(s) in ObjectDB.");
            }
        }

        private static GameObject BuildOrGet(ObjectDB db, ItemSpec def)
        {
            if (Prefabs.TryGetValue(def.PrefabName, out GameObject existing) && Holder.IsAlive(existing))
                return existing;

            GameObject donor = db.GetItemPrefab(def.DonorPrefab);
            if (donor == null && !string.IsNullOrEmpty(def.FallbackDonorPrefab))
            {
                donor = db.GetItemPrefab(def.FallbackDonorPrefab);
                if (donor != null) Plugin.Log.LogWarning($"Donor '{def.DonorPrefab}' is missing; '{def.PrefabName}' is built from '{def.FallbackDonorPrefab}' instead.");
            }
            if (donor == null)
            {
                Plugin.Log.LogError($"Donor prefab '{def.DonorPrefab}' is missing from ObjectDB, so '{def.PrefabName}' cannot be built.");
                return null;
            }

            // Never take over somebody else's hash. ObjectDB.UpdateRegisters and ZNetScene.Awake both use Dictionary.Add, so
            // a collision there is an ArgumentException that aborts Awake: a bricked game, not a broken mod.
            if (db.GetItemPrefab(def.PrefabName.GetStableHashCode()) != null)
            {
                Plugin.Log.LogError($"Something else already owns the item name '{def.PrefabName}'. Refusing to replace it.");
                return null;
            }

            GameObject clone = UnityEngine.Object.Instantiate(donor, Holder.Root.transform, worldPositionStays: false);

            // Strips "(Clone)". ItemDrop truncates a prefab name at the first '(' or ' ' while ZNetScene and ObjectDB hash it
            // in full, so the two would disagree and m_dropPrefab would silently end up null.
            clone.name = def.PrefabName;

            Prefabs[def.PrefabName] = clone;
            Plugin.Log.LogInfo($"Forged '{def.PrefabName}' from '{def.DonorPrefab}'.");
            return clone;
        }

        /// <summary>
        /// Writes every stat the item has rather than inheriting the donor's. The clone owns its SharedData outright
        /// (Instantiate deep-copies the serializable ItemData), so nothing written here reaches the vanilla donor.
        /// </summary>
        private static void ApplyStats(ItemSpec def, GameObject prefab)
        {
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) return;

            // ItemDrop.Awake never runs on a template under the inactive holder, so m_dropPrefab is never filled in the
            // normal way, and a trader's BuyItem needs it non-null.
            drop.m_itemData.m_dropPrefab = prefab;

            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = def.NameToken;
            s.m_description = def.DescriptionToken;
            // A drink is a consumable. A base is a MATERIAL: Humanoid.CanConsumeItem refuses anything that is not
            // ItemType.Consumable (decompiled), so a base can never be swallowed for nothing; only the fermenter takes it.
            // The lost tankard is drunk too (single use: drinking it ends the blackout, Patch_Consume), so it is a consumable.
            s.m_itemType = def.IsDrink || def.IsLostTankard ? ItemDrop.ItemData.ItemType.Consumable : ItemDrop.ItemData.ItemType.Material;
            s.m_maxStackSize = def.IsLostTankard ? 1 : def.IsDrink ? 10 : 20;
            s.m_weight = 0.5f;
            s.m_teleportable = true;
            s.m_questItem = false;
            s.m_dlc = "";

            // The donor is a health mead. Player.ConsumeItem applies m_consumeStatusEffect (decompiled: it is the ONLY
            // thing a plain consumable does besides remove one item), so leaving the donor's in place would make every
            // drink heal like vanilla mead. A drink does nothing by itself here; the drunk meter is fed by a postfix on
            // Player.ConsumeItem (Patches\Patch_Consume.cs). Food stays zero, so CanEat never gates a drink.
            s.m_consumeStatusEffect = null;
            s.m_food = 0f;
            s.m_foodStamina = 0f;
            s.m_foodEitr = 0f;
            s.m_foodBurnTime = 0f;
            s.m_foodRegen = 0f;

            if (def.IsLostTankard)
            {
                // Vanilla's own despawn is off (ItemDrop.TimedDestruction: an hour, and only with no player within 25 m;
                // decompiled 1.0.21). Tankard runs its own 5-minute clock instead (LostTankard.FindSeconds): the dropper's
                // game removes it from the world, and a copy that outlived that (a relog) is refused when picked up.
                drop.m_autoDestroy = false;
                // The vanilla tankard is equipment; ours is only ever picked up, so strip what it does when equipped.
                s.m_attackStatusEffect = null;
            }

            s.m_setName = "";
            s.m_setSize = 0;
            s.m_setStatusEffect = null;
            s.m_equipStatusEffect = null;
        }

        // ---- ZNetScene pass -----------------------------------------------------------------------------------

        // ZNetScene.m_namedPrefabs is PRIVATE (decompiled from 1.0.x: `private readonly Dictionary<int, GameObject>`), the
        // table GetPrefab answers from. Reached through a cached FieldRefAccess, retried until it resolves.
        private static AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> _namedPrefabs;
        private static bool _loggedMissingNamed;

        private static bool ResolveNamedPrefabs()
        {
            if (_namedPrefabs != null) return true;
            try { _namedPrefabs = AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs"); }
            catch (Exception ex)
            {
                if (!_loggedMissingNamed)
                {
                    _loggedMissingNamed = true;
                    Plugin.Log.LogError($"ZNetScene.m_namedPrefabs did not resolve, so dropped drinks cannot spawn. Valheim's API moved. {ex.Message}");
                }
            }
            return _namedPrefabs != null;
        }

        public static void EnsurePrefabs()
        {
            if (!Plugin.Enabled.Value) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || Prefabs.Count == 0 || !ResolveNamedPrefabs()) return;
            Dictionary<int, GameObject> named = _namedPrefabs(scene);

            foreach (ItemSpec def in AllSpecs())
            {
                if (!Prefabs.TryGetValue(def.PrefabName, out GameObject prefab) || prefab == null) continue;
                int hash = def.PrefabName.GetStableHashCode();

                if (named.TryGetValue(hash, out GameObject existing))
                {
                    if (Holder.IsAlive(existing) && existing != prefab)
                        Plugin.Log.LogError($"Prefab hash collision on '{def.PrefabName}'. Refusing to overwrite.");
                    continue;
                }

                // m_prefabs is only read inside ZNetScene.Awake, so adding to it registers nothing at runtime. It is kept in
                // step because other mods enumerate it.
                if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                named[hash] = prefab;
            }
        }
    }

    /// <summary>
    /// The one inactive, DontDestroyOnLoad parent every runtime clone hangs from. It is deactivated BEFORE anything is
    /// parented to it: a clone that never becomes active never runs ZNetView.Awake, so it never creates the world-origin
    /// ZDO a plain runtime Instantiate would.
    /// </summary>
    internal static class Holder
    {
        private static GameObject _root;

        public static bool IsAlive(UnityEngine.Object o) => !ReferenceEquals(o, null) && o != null;

        public static GameObject Root
        {
            get
            {
                if (IsAlive(_root)) return _root;
                _root = new GameObject("Tankard_PrefabContainer");
                _root.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_root);
                return _root;
            }
        }
    }
}
