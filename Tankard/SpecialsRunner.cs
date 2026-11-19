using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Tankard.Core;
using RavenIron.Tankard.Registration;
using UnityEngine;

namespace RavenIron.Tankard
{
    /// <summary>
    /// The Valhalla in the game (rules: Core\Specials.cs, Valhalla). Drinking one:
    ///   1. fills the meter to the cap (Meter.Drink);
    ///   2. asks the SERVER for somewhere dangerous to wake: only the server knows where the crypts and camps are
    ///      (ZoneSystem.m_locationInstances is loaded from the world file there; a client's is empty). The answer comes
    ///      back as a routed RPC. No answer within <see cref="AnswerSeconds"/> and you wake at an ordinary far spot;
    ///   3. blacks you out through BlackoutRunner, which takes everything into the grave, counts the streak, and here
    ///      also hands you things that are not yours;
    ///   4. starts the three-day hangover, kept as seconds on the character and ticked here while you play, so death and
    ///      relogs carry it. Its status effect is only the visible half: the numbers are written onto the live copy each
    ///      tick, which is how it fades.
    /// </summary>
    internal static class ValhallaRunner
    {
        private const float AnswerSeconds = 5f;
        private const string AskRpc = "TK_ValhallaAsk", AnswerRpc = "TK_ValhallaAnswer";
        private static readonly System.Random Rng = new System.Random();

        private static bool _waiting;
        private static float _waitFor;
        private static Valhalla.State _state;
        private static object _profile;
        private static float _saveTimer, _seTimer;

        /// <summary>Waiting for the server's answer: no other blackout may start in between.</summary>
        public static bool Pending => _waiting;
        public static bool HangoverActive => _state.Active && Plugin.Valhalla.Value && Plugin.Enabled.Value;
        public static float Strength => HangoverActive ? Valhalla.Strength(_state.Left, _state.Total) : 0f;
        public static float SecondsLeft => _state.Left;
        public static float ReliefLeft => _state.Relief;

        // ---- step 2: where you shouldn't be ---------------------------------------------------------------------

        public static void Drank(Player p)
        {
            if (!Plugin.Blackouts.Value)
            {
                Plugin.Log.LogInfo("The Valhalla: blackouts are off on this server, so you stay on your feet. The hangover still comes.");
                StartHangover(p);
                return;
            }
            int allowed = Blackout.AllowedBiomes(k => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(k));
            Vector3 at = p.transform.position;
            _waiting = true;
            _waitFor = AnswerSeconds;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(at.x); pkg.Write(at.z); pkg.Write(allowed);
                // The (string, params) overload targets the server; GetServerPeerID itself is PRIVATE (decompiled), and a
                // single-player game is its own server, so the ask and the answer are both handled here, at once.
                ZRoutedRpc.instance.InvokeRoutedRPC(AskRpc, pkg);
                Plugin.Log.LogInfo($"The Valhalla: asked the server for somewhere dangerous to wake (biome mask {allowed}).");
            }
            catch (Exception ex)
            {
                Plugin.Report("Valhalla ask", ex);
                Answer(false, Vector3.zero, null);
            }
        }

        public static void Register()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(AskRpc, OnAsk);
            ZRoutedRpc.instance.Register<ZPackage>(AnswerRpc, OnAnswer);
            Plugin.Log.LogInfo("The Valhalla: wake-spot RPCs registered.");
        }

        /// <summary>On the server: a dangerous location in an allowed biome, and a dry spot 20-45 m from it.</summary>
        private static void OnAsk(long sender, ZPackage pkg)
        {
            var reply = new ZPackage();
            try
            {
                float x = pkg.ReadSingle(), z = pkg.ReadSingle();
                int allowed = pkg.ReadInt();
                bool found = TryPickDanger(allowed, out Vector3 spot, out string name);
                reply.Write(found);
                reply.Write(spot.x); reply.Write(spot.y); reply.Write(spot.z);
                reply.Write(name ?? "");
                Plugin.Log.LogInfo(found
                    ? $"The Valhalla: sending a drinker from ({x:0}, {z:0}) to wake by {name} at ({spot.x:0}, {spot.z:0})."
                    : "The Valhalla: no dangerous place in the opened biomes; the drinker wakes somewhere ordinary.");
            }
            catch (Exception ex)
            {
                Plugin.Report("Valhalla server pick", ex);
                reply = new ZPackage();
                reply.Write(false); reply.Write(0f); reply.Write(0f); reply.Write(0f); reply.Write("");
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, AnswerRpc, reply);
        }

        private static bool TryPickDanger(int allowed, out Vector3 spot, out string name)
        {
            spot = Vector3.zero; name = null;
            ZoneSystem zs = ZoneSystem.instance;
            WorldGenerator wg = WorldGenerator.instance;
            if (zs == null || wg == null) return false;
            var picks = new List<ZoneSystem.LocationInstance>();
            foreach (ZoneSystem.LocationInstance li in zs.m_locationInstances.Values)
            {
                string n = li.m_location?.m_prefabName;
                if (!Valhalla.IsDangerous(n)) continue;
                if (((int)wg.GetBiome(li.m_position) & allowed) == 0) continue;
                picks.Add(li);
            }
            if (picks.Count == 0) return false;
            float water = zs.m_waterLevel;
            for (int tries = 0; tries < 12; tries++)
            {
                ZoneSystem.LocationInstance li = picks[Rng.Next(picks.Count)];
                for (int a = 0; a < 24; a++)
                {
                    double ang = Rng.NextDouble() * Math.PI * 2.0;
                    float r = Valhalla.WakeMin + (float)Rng.NextDouble() * (Valhalla.WakeMax - Valhalla.WakeMin);
                    float cx = li.m_position.x + (float)Math.Cos(ang) * r, cz = li.m_position.z + (float)Math.Sin(ang) * r;
                    int biome = (int)wg.GetBiome(cx, cz);
                    if ((biome & allowed) == 0 || (biome & Blackout.Ocean) != 0) continue;
                    float h = wg.GetHeight(cx, cz);
                    if (float.IsNaN(h) || h < water + 1f) continue;
                    spot = new Vector3(cx, h, cz);
                    name = li.m_location.m_prefabName;
                    return true;
                }
            }
            return false;
        }

        private static void OnAnswer(long sender, ZPackage pkg)
        {
            try
            {
                bool found = pkg.ReadBool();
                var at = new Vector3(pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle());
                string name = pkg.ReadString();
                Answer(found, at, name);
            }
            catch (Exception ex) { Plugin.Report("Valhalla answer", ex); Answer(false, Vector3.zero, null); }
        }

        private static void Answer(bool found, Vector3 at, string name)
        {
            if (!_waiting) return; // a late answer after the timeout already sent you somewhere
            _waiting = false;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead()) { Plugin.Log.LogInfo("The Valhalla: the drinker is gone or dead; no blackout."); return; }
            if (!found) Plugin.Log.LogInfo("The Valhalla: no dangerous place to send you, so an ordinary far blackout.");
            bool done = BlackoutRunner.Valhalla(p, found ? (Vector3?)at : null, name);
            if (done) StartHangover(p);
            else Plugin.Log.LogInfo("The Valhalla: the blackout did not happen (see above), so neither does its hangover.");
        }

        // ---- step 3's extra: things that are not yours -------------------------------------------------------------

        public static void GiveLoot(Player p)
        {
            try
            {
                Inventory inv = p.GetInventory();
                var given = new List<string>();
                foreach (KeyValuePair<string, int> l in Valhalla.PickLoot(Rng))
                {
                    GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(l.Key) : null;
                    if (prefab == null) { Plugin.Log.LogWarning($"The Valhalla: no item '{l.Key}' in this game, so it is skipped."); continue; }
                    if (inv.AddItem(prefab, l.Value)) given.Add(l.Value + " " + l.Key);
                }
                Plugin.Log.LogInfo("The Valhalla: you wake holding " + (given.Count > 0 ? string.Join(", ", given.ToArray()) : "nothing at all") + ".");
            }
            catch (Exception ex) { Plugin.Report("Valhalla loot", ex); }
        }

        // ---- step 4: three days of fury -----------------------------------------------------------------------------

        private static void StartHangover(Player p)
        {
            float day = EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : Valhalla.DefaultDayLengthSeconds;
            float total = Valhalla.TotalSeconds(day);
            _state = new Valhalla.State { Left = total, Total = total, Relief = 0f };
            Save(p);
            Plugin.Log.LogInfo($"The Valhalla: hangover of {total:0} s of play (three game days). Triple damage against creatures, " +
                               "double stamina cost, half stamina regen, all fading. Only more drink eases it.");
        }

        /// <summary>Any drink at all, the Valhalla aside: eases the regen penalty for a while.</summary>
        public static void OnAnyDrink(Player p, float abv)
        {
            Attach(p);
            if (!HangoverActive) return;
            float before = _state.Relief;
            _state.Relief = Valhalla.ReliefAfterDrink(_state.Relief, abv);
            if (_state.Relief > before)
            {
                Plugin.Log.LogInfo($"The Valhalla: a {abv:0}% drink eases it for {_state.Relief:0} s.");
                try { p?.Message(MessageHud.MessageType.TopLeft, "The drink takes the edge off. For now."); } catch (Exception) { }
            }
            Save(p);
        }

        /// <summary>Refuse a second Valhalla while the first one's hangover runs.</summary>
        public static bool Refuses(Player p)
        {
            Attach(p);
            if (!HangoverActive) return false;
            try { p.Message(MessageHud.MessageType.Center, "You're not done paying for the last one."); } catch (Exception) { }
            return true;
        }

        public static void Tick(float dt, Player p)
        {
            if (_waiting)
            {
                _waitFor -= dt;
                if (_waitFor <= 0f)
                {
                    Plugin.Log.LogWarning($"The Valhalla: no answer from the server in {AnswerSeconds:0} s (is Tankard on the server?), so an ordinary far blackout.");
                    Answer(false, Vector3.zero, null);
                }
            }
            if (p == null) return;
            Attach(p);
            if (!_state.Active) { RemoveEffect(p); return; }
            if (!p.IsDead()) _state = Valhalla.Tick(_state, dt);
            if (!_state.Active)
            {
                Plugin.Log.LogInfo("The Valhalla: the three days are over. You're yourself again.");
                try { p.Message(MessageHud.MessageType.Center, "The fury of Valhalla leaves you."); } catch (Exception) { }
                RemoveEffect(p);
                Save(p);
                return;
            }
            _seTimer -= dt;
            if (_seTimer <= 0f) { _seTimer = 1f; UpdateEffect(p); }
            _saveTimer -= dt;
            if (_saveTimer <= 0f) { _saveTimer = 5f; Save(p); }
        }

        /// <summary>Writes the fading numbers onto the live status effect, adding it again after a death or a relog.</summary>
        private static void UpdateEffect(Player p)
        {
            if (p.IsDead()) return;
            try
            {
                SEMan seman = p.GetSEMan();
                bool on = HangoverActive;
                StatusEffect live = seman.GetStatusEffect(Buffs.Hash(Buffs.ValhallaPrice));
                if (!on) { if (live != null) seman.RemoveStatusEffect(live, quiet: true); return; }
                if (live == null)
                {
                    SE_Stats template = Buffs.Get(Buffs.ValhallaPrice);
                    if (template == null) return;
                    live = seman.AddStatusEffect(template, resetTime: true);
                }
                if (!(live is SE_Stats se)) return;
                float s = Strength, use = Valhalla.StaminaUseExtra(s);
                bool relief = _state.Relief > 0f;
                se.m_staminaRegenMultiplier = Valhalla.RegenMultiplier(s, relief);
                se.m_attackStaminaUseModifier = use;
                se.m_blockStaminaUseModifier = use;
                se.m_dodgeStaminaUseModifier = use;
                se.m_jumpStaminaUseModifier = use;
                se.m_runStaminaDrainModifier = use;
                se.m_ttl = 0f; // ours to end, not the effect's own clock
                int hours = (int)Math.Ceiling(_state.Left / 60f);
                se.m_tooltip = $"x{Valhalla.DamageMultiplier(s):0.0} damage against creatures, +{use * 100f:0}% stamina cost, " +
                               $"{(se.m_staminaRegenMultiplier - 1f) * 100f:0}% stamina regen{(relief ? " (eased by drink)" : "")}. " +
                               $"About {hours} minute(s) of play left. Only more drink takes the edge off.";
            }
            catch (Exception ex) { Plugin.Report("Valhalla effect", ex); }
        }

        private static void RemoveEffect(Player p)
        {
            try
            {
                if (p == null || p.IsDead()) return;
                SEMan seman = p.GetSEMan();
                if (seman.HaveStatusEffect(Buffs.Hash(Buffs.ValhallaPrice))) seman.RemoveStatusEffect(Buffs.Hash(Buffs.ValhallaPrice), quiet: true);
            }
            catch (Exception ex) { Plugin.Report("Valhalla effect off", ex); }
        }

        private static void Attach(Player p)
        {
            if (p == null) return;
            object profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile == null || ReferenceEquals(profile, _profile)) return;
            _profile = profile;
            p.m_customData.TryGetValue(Valhalla.Key, out string saved);
            _state = Valhalla.Parse(saved);
            if (_state.Active) Plugin.Log.LogInfo($"The Valhalla: hangover loaded from the character, {_state.Left:0} of {_state.Total:0} s left.");
        }

        private static void Save(Player p)
        {
            if (p == null) return;
            try
            {
                if (_state.Active) p.m_customData[Valhalla.Key] = Valhalla.Format(_state);
                else p.m_customData.Remove(Valhalla.Key);
            }
            catch (Exception ex) { Plugin.Report("Valhalla save", ex); }
        }

        /// <summary>`tankard status`.</summary>
        public static string Describe() => !_state.Active ? "valhalla off" :
            $"valhalla {_state.Left:0} of {_state.Total:0} s (x{Valhalla.DamageMultiplier(Strength):0.00} damage, relief {_state.Relief:0} s)";

        /// <summary>`tankard sober` clears it too.</summary>
        public static void Clear(Player p)
        {
            _state = new Valhalla.State();
            _waiting = false;
            Save(p);
            RemoveEffect(p);
        }
    }

    /// <summary>
    /// Skadi's Nightmare in the game (rules: Core\Specials.cs, Skadi). The calm is decided on the drinker's own client and
    /// published as a bool on the drinker's player ZDO, because the creature that wants to bite you may be run by another
    /// player's game or the server: whoever owns a creature runs its AI, and reads the flag from the synced player ZDO.
    /// The Frozen morning is a copy of vanilla's Freezing under its own name, so vanilla's weather code (which adds and
    /// removes "Freezing" by name every frame) never touches it.
    /// </summary>
    internal static class SkadiRunner
    {
        private static bool _owed;
        private static float _frozen;
        private static object _profile;
        private static bool _published;
        private static float _timer;
        internal static readonly int CalmHash = Skadi.CalmZdoKey.GetStableHashCode();

        public static bool Calm { get; private set; }
        public static float FrozenLeft => _frozen;

        // The creature's and the player's network views are protected on Character (decompiled). House rule 5.
        private static AccessTools.FieldRef<Character, ZNetView> _nview;
        internal static ZDO ZdoOf(Character c)
        {
            try
            {
                if (c == null) return null;
                if (_nview == null) _nview = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
                ZNetView v = _nview(c);
                return v != null && v.IsValid() ? v.GetZDO() : null;
            }
            catch (Exception) { return null; }
        }

        private static bool On => Plugin.Enabled.Value && Plugin.SkadisNightmare.Value;

        public static void Drank(Player p)
        {
            if (!On) return;
            Attach(p);
            if (!_owed) Plugin.Log.LogInfo("Skadi's Nightmare: the wild will leave you alone while you're drunk on it.");
            _owed = true;
            Save(p);
        }

        /// <summary>A fire or a frost mead thaws a Frozen morning.</summary>
        public static void Thaw(Player p, string why)
        {
            if (_frozen <= 0f) return;
            _frozen = 0f;
            Plugin.Log.LogInfo($"Skadi's Nightmare: thawed by {why}.");
            try
            {
                p.GetSEMan().RemoveStatusEffect(Buffs.Hash(Buffs.FrozenMorning), quiet: true);
                p.Message(MessageHud.MessageType.TopLeft, "The cold lets go of you.");
            }
            catch (Exception ex) { Plugin.Report("Skadi thaw", ex); }
            Save(p);
        }

        public static void Tick(float dt, Player p)
        {
            if (p == null) return;
            Attach(p);
            _timer -= dt;
            if (_timer > 0f) return;
            float step = 0.5f - _timer; // the time since the last pass
            _timer = 0.5f;
            if (p.IsDead()) return;

            bool blackout = BlackoutRunner.MapHidden;
            bool calm = On && Skadi.CalmActive(_owed, Meter.Level, blackout);
            if (calm != Calm)
            {
                Calm = calm;
                Plugin.Log.LogInfo(calm ? "Skadi's Nightmare: calm. Nothing wild will touch you." : "Skadi's Nightmare: the calm is off.");
            }
            Publish(p, calm);
            Show(p, Buffs.SkadiCalm, calm, null);

            if (On && Skadi.CalmSpent(_owed, Meter.Level))
            {
                _owed = false;
                _frozen = Skadi.FrozenSeconds;
                Plugin.Log.LogInfo("Skadi's Nightmare: you've sobered. Frozen morning, 600 s, unless a fire or a frost mead thaws you.");
                try { p.Message(MessageHud.MessageType.Center, "Skadi wants her due. You're frozen to the bone."); } catch (Exception) { }
                Save(p);
            }
            else if (!On && _owed) { _owed = false; Save(p); }

            if (_frozen > 0f)
            {
                if (IsNearFire(p)) { Thaw(p, "a fire"); return; }
                _frozen = Skadi.FrozenAfter(_frozen, step);
                if (_frozen <= 0f)
                {
                    Plugin.Log.LogInfo("Skadi's Nightmare: the Frozen morning is over.");
                    Show(p, Buffs.FrozenMorning, false, null);
                    Save(p);
                    return;
                }
                Show(p, Buffs.FrozenMorning, true, _frozen);
                if (((int)_frozen) % 10 == 0) Save(p);
            }
        }

        private static bool IsNearFire(Player p)
        {
            try { return p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectCampFire); }
            catch (Exception) { return false; }
        }

        private static void Publish(Player p, bool calm)
        {
            if (calm == _published && !calm) return;
            ZDO zdo = ZdoOf(p);
            if (zdo == null) return;
            if (zdo.GetBool(CalmHash) != calm) zdo.Set(CalmHash, calm);
            _published = calm;
        }

        private static void Show(Player p, string name, bool on, float? ttl)
        {
            try
            {
                SEMan seman = p.GetSEMan();
                int hash = Buffs.Hash(name);
                StatusEffect live = seman.GetStatusEffect(hash);
                if (!on) { if (live != null) seman.RemoveStatusEffect(hash, quiet: true); return; }
                if (live == null)
                {
                    StatusEffect template = name == Buffs.FrozenMorning ? Buffs.FrozenMorningEffect() : Buffs.Get(name);
                    if (template == null) return;
                    live = seman.AddStatusEffect(template, resetTime: true);
                    if (live == null) return;
                }
                if (ttl.HasValue) live.m_ttl = ttl.Value + live.GetDuration(); // remaining = m_ttl - elapsed
            }
            catch (Exception ex) { Plugin.Report("Skadi effect", ex); }
        }

        private static void Attach(Player p)
        {
            object profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile == null || ReferenceEquals(profile, _profile)) return;
            _profile = profile;
            p.m_customData.TryGetValue(Skadi.Key, out string owed);
            p.m_customData.TryGetValue(Skadi.FrozenKey, out string frozen);
            _owed = owed == "1";
            _frozen = Skadi.ParseSeconds(frozen);
            _published = true; // so the first Publish writes the real value, even "not calm" over a stale true
            if (_owed || _frozen > 0f) Plugin.Log.LogInfo($"Skadi's Nightmare: loaded from the character (owed {_owed}, frozen {_frozen:0} s).");
        }

        private static void Save(Player p)
        {
            try
            {
                if (_owed) p.m_customData[Skadi.Key] = "1"; else p.m_customData.Remove(Skadi.Key);
                if (_frozen > 0f) p.m_customData[Skadi.FrozenKey] = Skadi.FormatSeconds(_frozen); else p.m_customData.Remove(Skadi.FrozenKey);
            }
            catch (Exception ex) { Plugin.Report("Skadi save", ex); }
        }

        public static string Describe() =>
            $"skadi {(Calm ? "CALM" : _owed ? "owed" : "off")}{(_frozen > 0f ? $", frozen {_frozen:0} s" : "")}";

        public static void Clear(Player p)
        {
            _owed = false; _frozen = 0f; Calm = false;
            if (p == null) return;
            Save(p);
            Publish(p, false);
            Show(p, Buffs.SkadiCalm, false, null);
            Show(p, Buffs.FrozenMorning, false, null);
        }
    }

    /// <summary>The Valhalla's wake-spot RPCs, registered once routed RPC exists (it is made in ZNet.Awake).</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Start))]
    internal static class Patch_RegisterRpcs
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { ValhallaRunner.Register(); }
            catch (Exception ex) { Plugin.Report("Valhalla RPC registration", ex); }
        }
    }
}
