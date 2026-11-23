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
            try { EnsureOnTray(db); }
            catch (Exception ex) { Plugin.Log.LogError($"Could not put the drinks on the serving tray: {ex}"); }
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
            GameObject held = db.GetItemPrefab(def.PrefabName.GetStableHashCode());
            if (held != null)
            {
                // An EARLIER Tankard build's clone (a hot reload under the Hotload dev tool: this assembly's statics start
                // empty, but ObjectDB still holds the old build's prefabs). It is ours: adopt it rather than refuse it or
                // build a second one, because every item in a bag, chest or fermenter already points at it. The caller
                // reapplies this build's stats to it.
                if (Holder.IsOurs(held))
                {
                    Holder.Adopt(held.transform.parent.gameObject);
                    Prefabs[def.PrefabName] = held;
                    Plugin.Log.LogInfo($"Adopted '{def.PrefabName}' from an earlier Tankard build.");
                    return held;
                }
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

            // Our own picture (Registration\Icons.cs). One icon, so m_variants stays 0: 1.0.17's ResetVariantIfInvalid
            // resets any variant index that is not below m_variants, and GetIcon bounds-checks against m_icons.
            Sprite icon = Icons.For(def.PrefabName);
            if (icon != null)
            {
                s.m_icons = new[] { icon };
                s.m_variants = 0;
            }

            s.m_setName = "";
            s.m_setSize = 0;
            s.m_setStatusEffect = null;
            s.m_equipStatusEffect = null;

            ApplyPiece(def, prefab, drop, icon);
            try { ApplyTint(def, prefab); }
            catch (Exception ex) { Plugin.Log.LogError($"Could not colour the bottle of '{def.PrefabName}'; it keeps the donor's look: {ex.Message}"); }
        }

        // ---- the bottle's colour -----------------------------------------------------------------------------------
        // A vanilla mead is ONE mesh with ONE painted texture, drawn twice (attach on the ground or a table, equipoffset        // in the hand), and some share that material with the WEAPONS atlas (weapons1). So the tint goes on a COPY of each
        // material, owned by this prefab alone; writing the donor's would recolour every vanilla mead and half the swords.
        // _Color multiplies the texture. An emissive bottle (the bog witch meads glow white) would wash the tint out, so
        // its emission takes the tint too, at the brightness it had.
        private static readonly Dictionary<Material, Dictionary<string, Material>> TintedCopies = new Dictionary<Material, Dictionary<string, Material>>();

        private static void ApplyTint(ItemSpec def, GameObject prefab)
        {
            string hex = Tints.For(def);
            if (hex == null) return;
            if (!Tints.TryParse(hex, out float r, out float g, out float b))
            {
                Plugin.Log.LogWarning($"Tint '{hex}' for '{def.PrefabName}' is not #rrggbb; it keeps the donor's look.");
                return;
            }
            var tint = new Color(r, g, b, 1f);
            int painted = 0;
            foreach (Renderer rend in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(rend is MeshRenderer) && !(rend is SkinnedMeshRenderer)) continue; // the pickup sparkle stays as it is
                Material[] mats = rend.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = mats[i];
                    if (src == null || !src.HasProperty("_Color")) continue;
                    mats[i] = TintedCopy(src, hex, tint);
                    changed = true;
                }
                if (changed) { rend.sharedMaterials = mats; painted++; }
            }
            Plugin.Log.LogDebug($"Tinted {painted} renderer(s) of '{def.PrefabName}' {hex}.");
        }

        private static Material TintedCopy(Material src, string hex, Color tint)
        {
            // Already this tint's copy: ApplyStats runs every pass, and an adopted prefab (Items.BuildOrGet) wears the
            // earlier build's copies. Copying a copy would only stack up materials.
            if (src.name.EndsWith("_tk" + hex.Substring(1), StringComparison.Ordinal)) return src;
            if (!TintedCopies.TryGetValue(src, out var byHex)) TintedCopies[src] = byHex = new Dictionary<string, Material>();
            if (byHex.TryGetValue(hex, out Material done) && done != null) return done;
            var m = new Material(src) { name = src.name + "_tk" + hex.Substring(1) };
            // A vanilla bottle's texture is already coloured (the frost pouch is brown, the poison bottle green), and _Color
            // MULTIPLIES it: ice blue on brown came out green in the robot's photo. So the copy draws a greyscale version of
            // the texture, and the tint is the only colour left.
            Texture grey = GreyCopy(src.mainTexture);
            if (grey != null) m.mainTexture = grey;
            m.SetColor("_Color", tint);
            if (m.HasProperty("_EmissionColor"))
            {
                Color e = m.GetColor("_EmissionColor");
                float glow = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                if (glow > 0f) m.SetColor("_EmissionColor", new Color(tint.r * glow, tint.g * glow, tint.b * glow, 1f));
            }
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            byHex[hex] = m;
            return m;
        }

        private static readonly Dictionary<Texture, Texture2D> GreyCopies = new Dictionary<Texture, Texture2D>();

        /// <summary>
        /// A greyscale copy of a texture, brightness stretched so its brightest parts are near white, or null where nothing is
        /// drawn (a dedicated server has no graphics device: it renders no bottle, so it needs no tint). Vanilla's textures are
        /// not CPU-readable, so the copy goes through the GPU: Blit into a render target, then ReadPixels back. One copy per
        /// source texture, shared by every drink that uses it.
        /// </summary>
        private static Texture2D GreyCopy(Texture src)
        {
            if (src == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            if (GreyCopies.TryGetValue(src, out Texture2D done) && done != null) return done;
            int w = src.width, h = src.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = src.name + "_tkgrey" };
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
            }
            Color32[] px = tex.GetPixels32();
            // Stretch by the 95th-percentile brightness, so a dark texture does not darken every tint.
            var hist = new int[256];
            var lum = new byte[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                lum[i] = (byte)((px[i].r * 54 + px[i].g * 183 + px[i].b * 19) >> 8);
                hist[lum[i]]++;
            }
            int top = 255, seen = 0, want = px.Length / 20;
            while (top > 1 && (seen += hist[top]) < want) top--;
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)Math.Min(255, lum[i] * 255 / top);
                px[i] = new Color32(v, v, v, px[i].a);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.filterMode = src.filterMode;
            tex.wrapMode = src.wrapMode;
            tex.anisoLevel = src.anisoLevel;
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            GreyCopies[src] = tex;
            Plugin.Log.LogDebug($"Grey copy of '{src.name}' ({w}x{h}), stretched by {255f / top:F2}.");
            return tex;
        }

        // ---- the serving tray ---------------------------------------------------------------------------------------
        // The serving tray is vanilla's Feaster (owner, 2026-11-22: "the drinks need to be recognized by the serving tray").
        // Read from 1.0.21 by the robot's survey (phase Y): its _FeasterPieceTable lists every vanilla food and mead as the
        // ITEM prefab itself, with a Piece whose one requirement is one of that same item, meads under Food. Placing one sets
        // the cup down; it is still an ItemDrop, so picking it up gives the cup back. A drink cloned from a mead already
        // carries the mead's Piece, which named, pictured and cost the MEAD, so it is rewritten here to be this drink.

        public const string TrayPrefab = "Feaster";

        private static void ApplyPiece(ItemSpec def, GameObject prefab, ItemDrop drop, Sprite icon)
        {
            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null) return;
            if (!def.IsDrink)
            {
                // A base or the lost tankard is never set on a table; a Piece left over from its donor would still cost the
                // donor if anything ever offered it, so it says nothing it does not mean.
                piece.m_resources = new Piece.Requirement[0];
                return;
            }
            piece.m_name = def.NameToken;
            piece.m_description = def.DescriptionToken;
            if (icon != null) piece.m_icon = icon;
            piece.m_category = Piece.PieceCategory.Food;
            piece.m_resources = new[] { new Piece.Requirement { m_resItem = drop, m_amount = 1, m_recover = true } };
        }

        /// <summary>Puts every drink on the serving tray's menu. Idempotent: run after every ObjectDB pass.</summary>
        public static void EnsureOnTray(ObjectDB db)
        {
            PieceTable table = db?.GetItemPrefab(TrayPrefab)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null || table.m_pieces == null)
            {
                if (!_loggedNoTray)
                {
                    _loggedNoTray = true;
                    Plugin.Log.LogWarning($"No serving tray ('{TrayPrefab}' with a piece table) in ObjectDB, so the drinks can't be set on a table.");
                }
                return;
            }
            int added = 0;
            foreach (ItemSpec def in AllSpecs())
            {
                if (!def.IsDrink || !Prefabs.TryGetValue(def.PrefabName, out GameObject prefab) || prefab == null) continue;
                if (prefab.GetComponent<Piece>() == null) continue;
                if (table.m_pieces.Contains(prefab)) continue;
                table.m_pieces.Add(prefab);
                added++;
            }
            if (added > 0) Plugin.Log.LogInfo($"Serving tray: {added} drink(s) can be set on a table.");
        }

        private static bool _loggedNoTray;

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
        // Found by this NAME, never by the static: the static belongs to one build, and a hot reload loads a new assembly
        // whose static is empty while the old build's container and clones live on.
        public const string RootName = "Tankard_PrefabContainer";

        private static GameObject _root;

        public static bool IsAlive(UnityEngine.Object o) => !ReferenceEquals(o, null) && o != null;

        /// <summary>
        /// True for a clone some Tankard build made: it hangs straight from an inactive, parentless container of ours.
        /// Another mod's item of the same name has no such parent, so it is still refused.
        /// </summary>
        public static bool IsOurs(GameObject go)
        {
            if (!IsAlive(go)) return false;
            Transform parent = go.transform.parent;
            return parent != null && parent.parent == null && parent.name == RootName && !parent.gameObject.activeSelf;
        }

        /// <summary>Takes an earlier build's container as this build's, so a reload never leaves two of them.</summary>
        public static void Adopt(GameObject container)
        {
            if (!IsAlive(_root) && IsAlive(container) && container.name == RootName) _root = container;
        }

        public static GameObject Root
        {
            get
            {
                if (IsAlive(_root)) return _root;
                _root = new GameObject(RootName);
                _root.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_root);
                return _root;
            }
        }
    }
}
