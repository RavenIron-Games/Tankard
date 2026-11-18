using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard
{
    /// <summary>
    /// The blackout, in the game. Rules are Core\Blackout.cs; this does the deed in an order where nothing is lost if a
    /// step refuses (the driving-a-vanilla-piece rule: every irreversible step after every check):
    ///   1. roll the chance; 2. find a landing; 3. ASK vanilla to teleport (it can refuse: already teleporting, cooldown);
    ///   4. only then leave gear in a tombstone at the drinking spot and save the spot, which turns the map off;
    ///   5. come round still drunk.
    /// The teleport waits 2 s before it moves you (Player.UpdateTeleport, decompiled), so the tombstone made in step 4
    /// is at the drinking spot, not the landing.
    ///
    /// The map stays off until you are within 50 m of the drinking spot, saved in Player.m_customData (a public dictionary
    /// the player profile writes and reads; decompiled), keyed per world, so it survives a relog and never leaks into
    /// another world.
    /// </summary>
    internal static class BlackoutRunner
    {
        private static readonly System.Random Rng = new System.Random();
        private static bool _mapHidden;
        private static float _checkTimer;
        // Not saved: a fresh session starts disarmed and arms the moment you are seen away from the spot, which after a
        // blackout is immediately (you wake at least ~240 m off, unless the island fallback put you closer).
        private static bool _homeArmed;

        /// <summary>Read by the Minimap patch every frame; refreshed from the saved key twice a second.</summary>
        public static bool MapHidden => _mapHidden && Plugin.Enabled.Value;

        private static string WorldKey()
        {
            long uid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0;
            return Blackout.KeyFor(uid == 0 ? null : uid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ---- the drink that might do it ---------------------------------------------------------------------------

        public static void AfterDrink(Player p)
        {
            if (!Plugin.Enabled.Value || !Plugin.Blackouts.Value || p == null) return;
            int min = Plugin.BlackoutMinDrinks.Value;
            double chance = Blackout.Chance(Meter.BingeDrinks, min);
            if (chance <= 0.0) return;
            double roll = Rng.NextDouble();
            Plugin.Log.LogInfo($"Blackout roll on drink {Meter.BingeDrinks}: {roll:0.000} against {chance:0.00}.");
            if (roll >= chance) return;
            try { Execute(p, Meter.BingeDrinks, min); }
            catch (Exception ex) { Plugin.Log.LogError($"Blackout failed part-way: {ex}"); }
        }

        private static void Execute(Player p, int drinks, int min)
        {
            if (p.IsDead() || p.IsTeleporting() || p.InCutscene())
            {
                Plugin.Log.LogInfo("Blackout skipped: dead, teleporting or in a cutscene.");
                return;
            }
            if (WorldGenerator.instance == null || ZoneSystem.instance == null)
            {
                Plugin.Log.LogWarning("Blackout skipped: no world generator.");
                return;
            }

            Vector3 home = p.transform.position;
            int allowed = Blackout.AllowedBiomes(k => ZoneSystem.instance.GetGlobalKey(k));
            float distance = Blackout.Distance(drinks, min);
            WorldGenerator wg = WorldGenerator.instance;
            float water = ZoneSystem.instance.m_waterLevel;

            if (!Blackout.PickLanding(home.x, home.z, distance, Rng,
                    (x, z) => (int)wg.GetBiome(x, z), (x, z) => wg.GetHeight(x, z), water, allowed,
                    out float lx, out float lz, out float ly))
            {
                Plugin.Log.LogInfo("Blackout: nowhere allowed to wake up, so you stay put.");
                p.Message(MessageHud.MessageType.Center, "The world spins, but you stay on your feet.");
                return;
            }

            // Step 3: ask first. Vanilla's distant teleport finds the real floor itself once the area loads (FindFloor, then
            // GetSolidHeight after 15 s), so the generator's height only has to be close; aim a little above it.
            // Stand up first. While attached (a chair, a bench, a bed, a ship's helm) Player.UpdateAttach writes the seat's
            // position into the transform EVERY frame (decompiled), so a teleport from a seat is undone the next frame.
            // Found in the first in-game run: two blackouts from the same bench, both of which "happened" and moved no one.
            if (p.IsAttached()) p.AttachStop();

            Vector3 landing = new Vector3(lx, ly + 2f, lz);
            Quaternion facing = Quaternion.Euler(0f, (float)(Rng.NextDouble() * 360.0), 0f);
            if (!p.TeleportTo(landing, facing, distantTeleport: true))
            {
                Plugin.Log.LogInfo("Blackout: the game refused the teleport (cooldown or already moving); nothing taken.");
                return;
            }

            // Step 4: the irreversible part.
            int left = LeaveGear(p, drinks, min);
            p.m_customData[WorldKey()] = Blackout.FormatHome(home.x, home.y, home.z);
            _mapHidden = true;
            _homeArmed = false; // you are still standing at the spot for the teleport's first 2 s
            Minimap.instance?.SetMapMode(Minimap.MapMode.None);

            _followUp = 0f;
            _followUpStep = 0;
            _followHome = home;
            _followTarget = landing;
            Meter.WakeFromBlackout();
            Plugin.Log.LogInfo($"BLACKOUT after {drinks} drinks: {left} stack(s) left at {home}, waking {distance:0} m away at ({lx:0}, {lz:0}).");
            p.Message(MessageHud.MessageType.Center, "Everything goes black.");
            p.Message(MessageHud.MessageType.TopLeft, left > 0
                ? "You've woken up somewhere you don't recognise. Some of your things are back where you were drinking, and you can't read your map."
                : "You've woken up somewhere you don't recognise, and you can't read your map.");
        }

        /// <summary>Moves a share of the inventory into a tombstone at the player's feet. Returns how many stacks moved.</summary>
        private static int LeaveGear(Player p, int drinks, int min)
        {
            if (p.m_tombstone == null) return 0;
            // The world's own death rule wins, and so does the owner's switch; ore still never travels either way.
            bool keepAll = !Plugin.BlackoutLeavesGear.Value || ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepInventory);

            Inventory inv = p.GetInventory();
            var candidates = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
                if (it != null && !it.m_shared.m_questItem) candidates.Add(it);

            var lost = new HashSet<ItemDrop.ItemData>();
            if (!keepAll)
                foreach (int i in Blackout.PickLost(candidates.Count, Blackout.LossFraction(drinks, min), Rng))
                    lost.Add(candidates[i]);
            // Whatever cannot go through a portal never comes with you, or a blackout would be a free ore teleport.
            foreach (ItemDrop.ItemData it in candidates)
                if (!it.m_shared.m_teleportable) lost.Add(it);

            return lost.Count == 0 ? 0 : MoveToTomb(p, lost);
        }

        private static int MoveToTomb(Player p, HashSet<ItemDrop.ItemData> lost)
        {
            // The same tombstone prefab and Setup call vanilla's Player.CreateTombStone uses, so it shows the player's name
            // and behaves like the grave everyone knows.
            GameObject tomb = UnityEngine.Object.Instantiate(p.m_tombstone, p.GetCenterPoint(), p.transform.rotation);
            Inventory graveInv = tomb.GetComponent<Container>().GetInventory();
            Inventory inv = p.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData it in lost)
            {
                if (p.IsItemEquiped(it)) p.UnequipItem(it, triggerEquipEffects: false);
                int before = inv.GetAllItems().Count;
                graveInv.MoveItemToThis(inv, it);
                if (inv.GetAllItems().Count < before) moved++;
            }
            PlayerProfile profile = Game.instance.GetPlayerProfile();
            tomb.GetComponent<TombStone>().Setup(profile.GetName(), profile.GetPlayerID());
            if (moved < lost.Count) Plugin.Log.LogWarning($"Blackout: the pile was full; {lost.Count - moved} stack(s) stayed with you.");
            return moved;
        }

        // ---- coming home ---------------------------------------------------------------------------------------

        // ---- after a blackout: did it take? ------------------------------------------------------------------------
        // The first in-game run had two blackouts that both started from the same spot, with the player back at the grave
        // 77 s after the first: the teleport did not hold, and nothing in the log said so. Three lines now say where you
        // actually are, so a teleport that is undone is visible rather than inferred.
        private static readonly float[] FollowUpAt = { 3f, 10f, 30f };
        private static float _followUp = -1f;
        private static int _followUpStep;
        private static Vector3 _followHome, _followTarget;

        private static void FollowUp(float dt)
        {
            if (_followUp < 0f) return;
            _followUp += dt;
            if (_followUpStep >= FollowUpAt.Length) { _followUp = -1f; return; }
            if (_followUp < FollowUpAt[_followUpStep]) return;
            Player p = Player.m_localPlayer;
            if (p == null) { _followUp = -1f; return; }
            Vector3 pos = p.transform.position;
            float fromHome = new Vector2(pos.x - _followHome.x, pos.z - _followHome.z).magnitude;
            float fromTarget = new Vector2(pos.x - _followTarget.x, pos.z - _followTarget.z).magnitude;
            Plugin.Log.LogInfo($"Blackout +{FollowUpAt[_followUpStep]:0}s: at ({pos.x:0}, {pos.y:0}, {pos.z:0}), {fromHome:0} m from the drinking spot, " +
                               $"{fromTarget:0} m from the landing; teleporting={p.IsTeleporting()}, attached={p.IsAttached()}, dead={p.IsDead()}.");
            _followUpStep++;
        }

        public static void Tick(float dt)
        {
            FollowUp(dt);
            _checkTimer -= dt;
            if (_checkTimer > 0f) return;
            _checkTimer = 0.5f;

            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null) { _mapHidden = false; return; }
            string key = WorldKey();
            if (!p.m_customData.TryGetValue(key, out string saved)) { _mapHidden = false; return; }
            if (!Blackout.TryParseHome(saved, out float hx, out float _, out float hz))
            {
                Plugin.Log.LogWarning($"Blackout: the saved drinking spot '{saved}' cannot be read; giving your map back.");
                p.m_customData.Remove(key);
                _mapHidden = false;
                return;
            }
            _mapHidden = true;
            Vector3 pos = p.transform.position;
            if (Blackout.ShouldRestoreMap(ref _homeArmed, p.IsTeleporting(), pos.x, pos.z, hx, hz))
            {
                p.m_customData.Remove(key);
                _mapHidden = false;
                Plugin.Log.LogInfo("Back at the drinking spot: map restored.");
                p.Message(MessageHud.MessageType.Center, "You find your way back. The world makes sense again.");
                Minimap.instance?.SetMapMode(Minimap.MapMode.Small); // nothing else would bring the minimap back
            }
        }
    }

    /// <summary>
    /// While blacked out, every request to show the map is turned into "no map". Vanilla's own world modifier does the
    /// same thing in the same place (Minimap.SetMapMode forces MapMode.None when Game.m_noMap; decompiled), so the game
    /// already knows how to run without one. A behaviour prefix: Priority.Low, rewrites only the argument, returns true.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Patch_NoMap
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(ref Minimap.MapMode mode)
        {
            if (BlackoutRunner.MapHidden) mode = Minimap.MapMode.None;
            return true;
        }
    }
}
