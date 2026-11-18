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
        // Every new body starts disarmed. Found by the robot, 2026-11-18: after a death far out, the DEAD body stayed the
        // local player for ~14 s where it fell, was seen 2 km from the spot and armed the check, so the respawn beside
        // the drinking spot counted as walking home and gave the map back. A dead body is never checked either.
        private static Player _checkedBody;

        /// <summary>Read by the Minimap patch every frame; refreshed from the saved key twice a second.</summary>
        public static bool MapHidden => _mapHidden && Plugin.Enabled.Value;

        // ZoneSystem.m_generatedZones is PRIVATE (decompiled 1.0.x: `private readonly HashSet<Vector2s>`), and so is
        // IsZoneGenerated. Read through a cached FieldRefAccess (house rule 5), retried until it resolves; a miss only
        // costs the preference, never the blackout. On a client in multiplayer it holds the zones THIS machine has
        // generated, which is still the land it can load without building.
        private static AccessTools.FieldRef<ZoneSystem, HashSet<Vector2s>> _generatedZones;
        private static bool _warnedGenerated;

        private static HashSet<Vector2s> GeneratedZones()
        {
            try
            {
                if (_generatedZones == null) _generatedZones = AccessTools.FieldRefAccess<ZoneSystem, HashSet<Vector2s>>("m_generatedZones");
                return ZoneSystem.instance != null ? _generatedZones(ZoneSystem.instance) : null;
            }
            catch (Exception ex)
            {
                if (!_warnedGenerated)
                {
                    _warnedGenerated = true;
                    Plugin.Log.LogWarning($"ZoneSystem.m_generatedZones did not resolve, so blackouts may land on new ground (lag). {ex.Message}");
                }
                return null;
            }
        }

        internal static string WorldId()
        {
            long uid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0;
            return uid == 0 ? null : uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string WorldKey() => Blackout.KeyFor(WorldId());

        // A death in a blackout: where the next body comes round (taken by Patch_BlackoutRespawn), then the lost tankard to
        // drop beside it. Static because the dead body (and its m_customData) is destroyed before Game looks for a spawn.
        private static bool _respawnPending;
        private static Vector3 _respawnAt;
        private static bool _dropPending;
        private static Vector3 _dropNear;
        private static float _dropWait;
        // The respawn is a blackout's (drop the lost tankard after it) or the long walk's (no tankard, just no bed).
        private static bool _dropAfterRespawn;
        private static bool _longWalkNotice;
        private static bool _longWalk;
        private static string LongWalkKey() => Blackout.LongWalkKeyFor(WorldId());
        /// <summary>No bed yet: a blackout's long walk home is still on (Core\Blackout.LongWalkOver).</summary>
        public static bool OnLongWalk => _longWalk && Plugin.Enabled.Value;
        // Lost tankards this game dropped, with their real-time deadline, so it can take back any not found in time.
        private static readonly List<KeyValuePair<ItemDrop, DateTime>> _dropped = new List<KeyValuePair<ItemDrop, DateTime>>();

        /// <summary>Where the next body comes round after a death in a blackout. False means "let vanilla choose".</summary>
        public static bool PeekRespawn(out Vector3 at) { at = _respawnAt; return _respawnPending && Plugin.Enabled.Value; }

        /// <summary>The respawn happened (or was given up): drop the lost tankard once the new body is standing there.</summary>
        public static void RespawnUsed(bool spawnedThere)
        {
            _respawnPending = false;
            if (!spawnedThere) return;
            if (!_dropAfterRespawn) { _longWalkNotice = true; return; }
            _dropPending = true;
            _dropNear = _respawnAt;
            _dropWait = 0f;
        }

        /// <summary>
        /// Dying in a blackout is no way out (owner, 2026-11-18). You never respawn at your bed ("make it to easy to get
        /// drunk and get home"), and not where you fell either, so whatever killed you cannot keep killing you: you come
        /// round on fresh dry land about LostTankard.RespawnDistance from where you died, picked again on every death, with
        /// your lost tankard 10 to 20 m away. Picking it up ends the blackout. Walking back to the drinking spot still works.
        /// You also wake with the full post-blackout hangover, and any death carries a running hangover to the new body.
        /// The coming-home check is disarmed, so a respawn near the drinking spot never counts as walking home. From Patch_Death.
        /// </summary>
        public static void OnLocalDeath(Player p)
        {
            if (p == null) return;
            bool inBlackout = _mapHidden;
            // After the blackout, until its hangover is over: still no bed (the owner, 2026-11-18). Read from the save,
            // not the cached flag, so a death on the very first frame after a login still counts.
            bool longWalk = !inBlackout && Plugin.Enabled.Value && p.m_customData.ContainsKey(LongWalkKey());
            _respawnPending = false;
            _dropPending = false;
            Meter.OnDeath(p, inBlackout);
            if (!inBlackout && !longWalk) return;
            _homeArmed = false;
            _dropAfterRespawn = inBlackout;

            Vector3 died = p.transform.position;
            Vector3 at = died;
            WorldGenerator wg = WorldGenerator.instance;
            if (wg != null && ZoneSystem.instance != null)
            {
                int allowed = Blackout.AllowedBiomes(k => ZoneSystem.instance.GetGlobalKey(k));
                if (Blackout.PickLanding(died.x, died.z, LostTankard.RespawnDistance, Rng,
                        (x, z) => (int)wg.GetBiome(x, z), (x, z) => wg.GetHeight(x, z), ZoneSystem.instance.m_waterLevel, allowed,
                        out float lx, out float lz, out float ly))
                    at = new Vector3(lx, ly, lz);
                else
                    Plugin.Log.LogWarning("Died during a blackout: no allowed dry land near where you fell, so you come round there.");
            }
            _respawnAt = at;
            _respawnPending = true;
            float off = new Vector2(at.x - died.x, at.z - died.z).magnitude;
            if (!inBlackout)
            {
                Plugin.Log.LogInfo($"Died on the long walk home at ({died.x:0}, {died.z:0}): you will come round {off:0} m away at " +
                                   $"({at.x:0}, {at.z:0}), not at your bed, until the blackout's hangover is over.");
                return;
            }
            Plugin.Log.LogInfo($"Died during a blackout at ({died.x:0}, {died.z:0}): you will come round {off:0} m away at ({at.x:0}, {at.z:0}), " +
                               "not at your bed, with your lost tankard close by. The map stays dark until you pick it up or walk home.");
        }

        /// <summary>The blackout is over: by walking home, by finding the lost tankard, by remembering the whole map, or by a damaged save.</summary>
        internal static void EndBlackout(Player p, bool restoreMinimap)
        {
            if (p != null) p.m_customData.Remove(WorldKey());
            _respawnPending = false;
            _dropPending = false;
            _mapHidden = false;
            MapMemory.Clear(p);
            if (restoreMinimap) Minimap.instance?.SetMapMode(Minimap.MapMode.Small); // nothing else would bring it back
        }

        // ---- the lost tankard -----------------------------------------------------------------------------------

        /// <summary>Drops the lost tankard 10-20 m from the new body, on dry ground, marked with the owner's id.</summary>
        private static void DropLostTankard(Player p)
        {
            _dropPending = false;
            try
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(LostTankard.PrefabName) : null;
                ItemDrop template = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (template == null)
                {
                    Plugin.Log.LogError($"Lost tankard: the '{LostTankard.PrefabName}' prefab is missing, so none was dropped. Walk home instead.");
                    return;
                }
                Vector3 me = p.transform.position;
                Vector3 spot;
                if (LostTankard.PickDrop(me.x, me.z, Rng, (x, z) => ZoneSystem.instance.GetGroundHeight(new Vector3(x, me.y + 50f, z)),
                        ZoneSystem.instance.m_waterLevel, out float dx, out float dz, out float dy))
                    spot = new Vector3(dx, dy + 0.5f, dz);
                else
                    spot = me + p.transform.forward * 1.5f + Vector3.up * 0.5f; // nowhere dry nearby: at your feet
                ItemDrop.ItemData item = template.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = 1;
                item.m_customData[LostTankard.OwnerKey] = LostTankard.FormatOwner(p.GetPlayerID());
                DateTime now = DateTime.UtcNow;
                item.m_customData[LostTankard.DroppedKey] = LostTankard.FormatTicks(now.Ticks);
                ItemDrop dropped = ItemDrop.DropItem(item, 1, spot, Quaternion.Euler(0f, (float)(Rng.NextDouble() * 360.0), 0f));
                if (dropped != null) _dropped.Add(new KeyValuePair<ItemDrop, DateTime>(dropped, now.AddSeconds(LostTankard.FindSeconds)));
                float d = new Vector2(spot.x - me.x, spot.z - me.z).magnitude;
                Plugin.Log.LogInfo($"Lost tankard dropped {d:0} m from you at ({spot.x:0}, {spot.z:0}); {LostTankard.FindSeconds:0} s of real time to find it.");
                p.Message(MessageHud.MessageType.Center, "You had a tankard in your hand... it can't be far. Find it before it's gone.");
            }
            catch (Exception ex) { Plugin.Report("Lost tankard drop", ex); }
        }

        /// <summary>
        /// Takes back every lost tankard this game dropped that nobody found within LostTankard.FindSeconds of real time.
        /// A picked-up one is already gone from the world (Unity null) and is just forgotten.
        /// </summary>
        private static void ExpireDroppedTankards(Player p)
        {
            if (_dropped.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            for (int i = _dropped.Count - 1; i >= 0; i--)
            {
                ItemDrop drop = _dropped[i].Key;
                if (drop == null) { _dropped.RemoveAt(i); continue; }
                if (now < _dropped[i].Value) continue;
                _dropped.RemoveAt(i);
                try
                {
                    ZNetView nview = drop.GetComponent<ZNetView>();
                    if (nview == null || !nview.IsValid()) continue;
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    nview.Destroy();
                    Plugin.Log.LogInfo("Lost tankard despawned: not found in time.");
                    if (p != null && _mapHidden)
                        p.Message(MessageHud.MessageType.Center, "Your tankard is gone. You'll have to find your own way home.");
                }
                catch (Exception ex) { Plugin.Report("Lost tankard despawn", ex); }
            }
        }

        /// <summary>
        /// Your own lost tankard, picked up: found in time it is marked so the clock no longer applies, and you are told to
        /// drink it. Picked up too late (a copy that outlived the despawn, say across a relog) it crumbles. Anyone else's is
        /// left alone: it does nothing for them.
        /// </summary>
        private static void CheckPickedUpTankards(Player p)
        {
            Inventory inv = p.GetInventory();
            if (inv == null) return;
            long me = p.GetPlayerID();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                if (it?.m_shared == null || it.m_shared.m_name != LostTankard.NameToken) continue;
                if (it.m_customData.ContainsKey(LostTankard.FoundKey)) continue;
                it.m_customData.TryGetValue(LostTankard.OwnerKey, out string owner);
                if (!LostTankard.IsMine(owner, me)) continue;
                it.m_customData.TryGetValue(LostTankard.DroppedKey, out string dropped);
                if (LostTankard.IsExpired(dropped, DateTime.UtcNow.Ticks))
                {
                    inv.RemoveItem(it); // returning at once: the enumerator never advances after the removal
                    Plugin.Log.LogInfo("Lost tankard picked up too late: it crumbles.");
                    p.Message(MessageHud.MessageType.Center, "The tankard crumbles in your hand. Too late.");
                    return;
                }
                it.m_customData[LostTankard.FoundKey] = "1";
                Plugin.Log.LogInfo("Lost tankard found in time.");
                p.Message(MessageHud.MessageType.Center, "Your tankard! Drink from it.");
                return;
            }
        }

        /// <summary>
        /// Drinking the lost tankard (from Patch_Consume). Single use: the game has already taken it from the bag. Yours, found
        /// in time, during a blackout: the blackout ends and the map comes back. Anything else: gone, and nothing happens.
        /// </summary>
        public static void DrankLostTankard(Player p, ItemDrop.ItemData item)
        {
            item.m_customData.TryGetValue(LostTankard.OwnerKey, out string owner);
            if (!LostTankard.IsMine(owner, p.GetPlayerID()))
            {
                Plugin.Log.LogInfo("Drank someone else's lost tankard: nothing happens.");
                p.Message(MessageHud.MessageType.Center, "It isn't your tankard. Nothing comes back to you.");
                return;
            }
            item.m_customData.TryGetValue(LostTankard.DroppedKey, out string dropped);
            if (!item.m_customData.ContainsKey(LostTankard.FoundKey) && LostTankard.IsExpired(dropped, DateTime.UtcNow.Ticks))
            {
                Plugin.Log.LogInfo("Drank a lost tankard found too late: nothing happens.");
                p.Message(MessageHud.MessageType.Center, "Too late. It's empty.");
                return;
            }
            if (!_mapHidden)
            {
                Plugin.Log.LogInfo("Drank the lost tankard with no blackout running: nothing to end.");
                p.Message(MessageHud.MessageType.Center, "Empty. You already know where you are.");
                return;
            }
            Plugin.Log.LogInfo("Drank from the lost tankard: blackout over, map restored.");
            p.Message(MessageHud.MessageType.Center, "One more drink... and it all comes back to you.");
            EndBlackout(p, restoreMinimap: true);
        }

        // ---- the drink that might do it ---------------------------------------------------------------------------

        public static void AfterDrink(Player p)
        {
            if (!Plugin.Enabled.Value || !Plugin.Blackouts.Value || p == null) return;
            // Blood alcohol decides it (owner, 2026-11-18): only a drink that leaves you over the limit can do it.
            float over = Blackout.Over(Meter.Level, Plugin.BlackoutOver.Value);
            double chance = Blackout.Chance(over);
            if (chance <= 0.0) return;
            double roll = Rng.NextDouble();
            Plugin.Log.LogInfo($"Blackout roll at meter {Meter.Level:0.0} ({over:0.0} over the limit of {Plugin.BlackoutOver.Value:0}), " +
                               $"drink {Meter.BingeDrinks}: {roll:0.000} against {chance:0.00}.");
            if (roll >= chance) return;
            try { Execute(p, over); }
            catch (Exception ex) { Plugin.Log.LogError($"Blackout failed part-way: {ex}"); }
        }

        private static void Execute(Player p, float over)
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
            // The streak this blackout would join. Computed now, saved only after the teleport is accepted (step 4).
            string streakKey = Blackout.StreakKeyFor(WorldId());
            p.m_customData.TryGetValue(streakKey, out string savedStreak);
            Blackout.Streak before = Blackout.ParseStreak(savedStreak);
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay() : Math.Max(0, before.LastDay);
            Blackout.Streak streak = Blackout.Advance(before, today);
            float distance = Blackout.DistanceFor(over, streak);
            double loss = Blackout.LossFor(over, streak);
            int drinks = Meter.BingeDrinks;
            WorldGenerator wg = WorldGenerator.instance;
            float water = ZoneSystem.instance.m_waterLevel;

            // Prefer ground the world has already generated: waking there builds no new land, which is what lagged right
            // after a blackout (the owner, 2026-11-17; the log showed locations and dungeons generated at each landing).
            HashSet<Vector2s> generated = GeneratedZones();
            Func<float, float, bool> preferred = null;
            if (Plugin.PreferExploredGround.Value && generated != null && generated.Count > 0)
                preferred = (x, z) => generated.Contains(ZoneSystem.GetZone(new Vector3(x, 0f, z)));

            if (!Blackout.PickLanding(home.x, home.z, distance, Rng,
                    (x, z) => (int)wg.GetBiome(x, z), (x, z) => wg.GetHeight(x, z), water, allowed,
                    out float lx, out float lz, out float ly, preferred, streak.IsTotal ? Blackout.FarDistance : 0f))
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
            int left = LeaveGear(p, loss);
            p.m_customData[WorldKey()] = Blackout.FormatHome(home.x, home.y, home.z);
            p.m_customData[streakKey] = Blackout.FormatStreak(streak);
            p.m_customData[LongWalkKey()] = "1"; // no bed until this blackout's hangover is over
            _longWalk = true;
            _mapHidden = true;
            _homeArmed = false; // you are still standing at the spot for the teleport's first 2 s
            MapMemory.Start(p);
            Minimap.instance?.SetMapMode(Minimap.MapMode.None);

            _followUp = 0f;
            _followUpStep = 0;
            _followHome = home;
            _followTarget = landing;
            Meter.WakeFromBlackout();
            float actual = new Vector2(lx - home.x, lz - home.z).magnitude;
            bool onGenerated = generated != null && generated.Contains(ZoneSystem.GetZone(landing));
            Plugin.Log.LogInfo($"BLACKOUT {over:0.0} over the limit, after {drinks} drinks: {left} stack(s) left at {home}, aimed {distance:0} m, woke {actual:0} m away " +
                               $"at ({lx:0}, {lz:0}) on {(onGenerated ? "already-generated" : "NEW")} ground. " +
                               $"Streak: day {streak.Days} in a row (in-game day {streak.LastDay}), blackout {streak.Count} of the streak, loss {loss:P0}" +
                               (streak.IsTotal ? ", TOTAL." : "."));
            p.Message(MessageHud.MessageType.Center, streak.IsTotal ? "Three nights running. Everything goes black." : "Everything goes black.");
            p.Message(MessageHud.MessageType.TopLeft, streak.IsTotal
                ? "You've woken up a long way from anywhere you know, with nothing on you. It's all back where you were drinking."
                : left > 0
                    ? "You've woken up somewhere you don't recognise. Some of your things are back where you were drinking, and you can't read your map."
                    : "You've woken up somewhere you don't recognise, and you can't read your map.");
        }

        /// <summary>Moves a share of the inventory into a tombstone at the player's feet. Returns how many stacks moved.</summary>
        private static int LeaveGear(Player p, double lossFraction)
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
                foreach (int i in Blackout.PickLost(candidates.Count, lossFraction, Rng))
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

        /// <summary>
        /// The long walk home (Core\Blackout.LongWalkOver): reads it from the character, tells a player who has just come
        /// round on it why they are not at their bed, and ends it once the blackout and its hangover are both over. Only
        /// once Meter holds this body's saved state, or a login would read "no hangover" and end it on the first frame.
        /// </summary>
        private static void UpdateLongWalk(Player p, bool blackoutActive)
        {
            string key = LongWalkKey();
            _longWalk = p.m_customData.ContainsKey(key);
            if (!_longWalk || p.IsDead()) return;
            if (_longWalkNotice)
            {
                _longWalkNotice = false;
                p.Message(MessageHud.MessageType.Center, "No bed for you yet. Not until the hangover's gone.");
            }
            if (!Meter.AttachedTo(p)) return;
            bool hangoversOn = Plugin.Hangovers.Value && Plugin.Enabled.Value;
            if (!Blackout.LongWalkOver(blackoutActive, hangoversOn, Meter.HangoverLeft(p), Meter.BlackedOut)) return;
            p.m_customData.Remove(key);
            _longWalk = false;
            Plugin.Log.LogInfo("Long walk over: the blackout and its hangover are gone, so a death sends you to your bed again.");
            p.Message(MessageHud.MessageType.TopLeft, "Your head clears. Your bed will have you again.");
        }

        public static void Tick(float dt)
        {
            FollowUp(dt);
            _checkTimer -= dt;
            if (_checkTimer > 0f) return;
            _checkTimer = 0.5f;

            Player p = Player.m_localPlayer;
            ExpireDroppedTankards(p);
            if (p == null || ZNet.instance == null) { _mapHidden = false; _longWalk = false; return; }
            string key = WorldKey();
            UpdateLongWalk(p, p.m_customData.ContainsKey(key));
            if (!p.m_customData.TryGetValue(key, out string saved)) { _mapHidden = false; return; }
            if (!Blackout.TryParseHome(saved, out float hx, out float _, out float hz))
            {
                Plugin.Log.LogWarning($"Blackout: the saved drinking spot '{saved}' cannot be read; giving your map back.");
                EndBlackout(p, restoreMinimap: true);
                return;
            }
            _mapHidden = true;
            if (!ReferenceEquals(p, _checkedBody)) { _checkedBody = p; _homeArmed = false; }
            // The minimap stays off for the whole blackout. Patch_NoMap only turns REQUESTS for it away, and the game asks
            // for it only from None, so one that is already showing stays up for good. That happened after a relog: the
            // game showed it while loading the character, before this tick knew about the blackout (the robot's relog
            // test, 2026-11-18, read "blackout ON, minimap mode Small"). The big map (Large) is allowed: it is where you remember.
            Minimap mm = Minimap.instance;
            if (mm != null && mm.m_mode == Minimap.MapMode.Small)
            {
                mm.SetMapMode(Minimap.MapMode.None);
                Plugin.Log.LogInfo("Blackout: the minimap was showing; hidden again.");
            }
            if (p.IsDead()) return;

            // After a death in a blackout: drop the lost tankard once the new body stands where it came round and the
            // ground there has loaded (GetGroundHeight needs the area). Never later than a minute, wherever you are then.
            if (_dropPending)
            {
                _dropWait += 0.5f;
                Vector3 me = p.transform.position;
                bool there = new Vector2(me.x - _dropNear.x, me.z - _dropNear.z).magnitude < 60f;
                if ((there && !p.IsTeleporting() && ZoneSystem.instance.IsActiveAreaLoaded()) || _dropWait >= 60f) DropLostTankard(p);
            }
            CheckPickedUpTankards(p);

            Vector3 pos = p.transform.position;
            if (Blackout.ShouldRestoreMap(ref _homeArmed, p.IsTeleporting(), pos.x, pos.z, hx, hz))
            {
                Plugin.Log.LogInfo("Back at the drinking spot: map restored.");
                p.Message(MessageHud.MessageType.Center, "You find your way back. The world makes sense again.");
                EndBlackout(p, restoreMinimap: true);
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
        private static bool Prefix(Minimap __instance, ref Minimap.MapMode mode)
        {
            // The big map may open: that is where you remember it (MapMemory lays the fog over it). The minimap may not,
            // until the blackout is over, so the corner of the screen never gives your position away.
            //
            // Vanilla only ever reaches Large FROM Small (Minimap.Update, decompiled: in None, the Map key asks for Small,
            // and every frame in None it asks for Small too; only Small + Map asks for Large). With Small forbidden that
            // path is cut, so a Small request is read for its intent:
            //   from Large  -> you are closing the map    -> None
            //   from None, with the Map key down this frame -> you are opening it -> Large
            //   anything else (the every-frame "None? show Small") -> None
            if (!BlackoutRunner.MapHidden || mode != Minimap.MapMode.Small) return true;
            bool mapKey = ZInput.GetButtonDown("Map") || ZInput.GetButtonDown("JoyMap");
            mode = __instance.m_mode == Minimap.MapMode.None && mapKey ? Minimap.MapMode.Large : Minimap.MapMode.None;
            return true;
        }
    }
}
