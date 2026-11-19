using RavenIron.Tankard.Core;
using RavenIron.Tankard.Registration;

namespace RavenIron.Tankard
{
    /// <summary>
    /// The local player's intoxication level, the current binge, and what the body does about it: drink effects, liquid
    /// courage while drunk, and the hangover after. A plain number driven from Plugin.Update: no timers, no coroutine of
    /// its own (house rule 2). It lives on the drinker's own client, which is where the symptoms render and where the
    /// blackout decides.
    ///
    /// A binge is every drink since you were last fully sober. It ends when the meter reaches zero, and THAT is when the
    /// hangover lands, sized by how high the meter went (Core\Effects.HangoverSeconds).
    ///
    /// Saved on the character (Player.m_customData, MeterState) since 2026-11-18, so quitting does not sober you up (the
    /// owner: "hold the drunk ... so no rage quits happen"). No time passes while you are logged out. The in-memory copy is
    /// authoritative while the same character profile is loaded, so a respawn (a new Player object) carries it over.
    /// </summary>
    internal static class Meter
    {
        public static float Level { get; private set; }
        public static int BingeDrinks { get; private set; }
        public static float Peak { get; private set; }
        public static bool BlackedOut { get; private set; }
        public const float WakeLevel = 90f;
        public static Tier Tier => Intoxication.TierOf(Level);

        private static object _profile;       // the PlayerProfile this state belongs to
        private static Player _body;          // the Player object we last wrote to
        private static float _saveTimer, _courageTimer;
        private static float _pendingHangover; // restored from a save, applied once the body can take effects

        /// <summary>Symptom severity 0 to 1, with the SymptomStrength setting applied. Zero below Tipsy.</summary>
        public static float SymptomSeverity
        {
            get
            {
                float s = Intoxication.Severity(Level) * Plugin.SymptomStrength.Value;
                return s < 0f ? 0f : (s > 1f ? 1f : s);
            }
        }

        /// <summary>The character's alcohol tolerance, 0 to 69. Saved in Player.m_customData: your body, so it follows the
        /// character across worlds (unlike the blackout keys, which are per world).</summary>
        public static float ToleranceOf(Player p)
        {
            if (p == null) return 0f;
            p.m_customData.TryGetValue(Tolerance.Key, out string s);
            return Tolerance.Parse(s);
        }

        public static void Drink(DrinkDef drink, Player p)
        {
            Attach(p);
            float before = Level;
            float tol = ToleranceOf(p);
            float hit = Tolerance.Hit(drink.Abv, tol);
            Level = Intoxication.Add(Level, hit);
            // The Valhalla fills the meter to the cap whatever your tolerance (Core\Specials).
            if (drink.Special == Special.Valhalla && Plugin.Valhalla.Value) { hit = Intoxication.Cap - before; Level = Intoxication.Cap; }
            Peak = Effects.PeakAfterDrink(Peak, Level, drink.CuresHangover); // the cure never earns the next hangover
            BingeDrinks++;
            float tolAfter = Tolerance.AfterDrink(tol, drink.Abv);
            if (p != null) p.m_customData[Tolerance.Key] = Tolerance.Format(tolAfter);
            Plugin.Log.LogInfo($"Drank {drink.DisplayName} ({drink.Abv:0}%): +{hit:0.0} at tolerance {tol:0.00}, meter {before:0.0} -> {Level:0.0} " +
                               $"({Tier}), drink {BingeDrinks} of this binge. Tolerance now {tolAfter:0.00}.");
            int shownBefore = Tolerance.Shown(tol), shownAfter = Tolerance.Shown(tolAfter);
            if (shownAfter > shownBefore && p != null)
            {
                try
                {
                    string text = shownAfter >= (int)Tolerance.Max
                        ? $"Tolerance {shownAfter}: iron gut. You can't get any harder."
                        : $"Tolerance {shownAfter} ({Tolerance.Describe(tolAfter)})";
                    p.Message(MessageHud.MessageType.TopLeft, text);
                }
                catch (System.Exception ex) { Plugin.Report("Tolerance message", ex); }
            }
            ApplyDrinkEffect(drink, p);
            UpdateCourage(p);
            if (drink.Special != Special.Valhalla) ValhallaRunner.OnAnyDrink(p, drink.Abv);
            if (drink.Special == Special.SkadisNightmare) SkadiRunner.Drank(p);
            Save(p);
        }

        /// <summary>
        /// A cup of Kaffi (Core\Specials.cs, Kaffi): no alcohol, no tolerance, no binge count. It takes a chunk off the meter
        /// unless the jitters from the last cup are still on, and starts (or restarts) the jitters. The jitters go on even
        /// with DrinkEffects off, because they are the only thing that stops Kaffi from being an instant sober button.
        /// </summary>
        public static void DrinkKaffi(DrinkDef drink, Player p)
        {
            Attach(p);
            int hash = Buffs.Hash(Buffs.AssetNameFor(drink));
            bool jittery = false;
            try { jittery = p != null && p.GetSEMan().HaveStatusEffect(hash); } catch (System.Exception) { }
            float before = Level;
            Tier tierBefore = Tier;
            Level = Kaffi.After(Level, jittery);
            Plugin.Log.LogInfo(jittery
                ? $"Kaffi: still jittery from the last cup, so this one does nothing for your head (meter {Level:0.0})."
                : $"Kaffi: meter {before:0.0} -> {Level:0.0} ({tierBefore} -> {Tier}).");
            if (p != null)
            {
                try
                {
                    string text = jittery ? "Your hands are shaking. Another cup won't clear your head yet."
                                : before <= 0f ? "Strong and black. You were sober anyway."
                                : Tier != tierBefore ? $"The Kaffi clears your head. ({tierBefore} -> {Tier})" : "The Kaffi clears your head a little.";
                    p.Message(MessageHud.MessageType.TopLeft, text);
                    var se = Buffs.ForDrink(drink);
                    if (se != null) p.GetSEMan().AddStatusEffect(se, resetTime: true);
                }
                catch (System.Exception ex) { Plugin.Report("Kaffi", ex); }
            }
            UpdateCourage(p);
            Save(p);
        }

        /// <summary>The Valhalla: you come round still flattened, the meter at the cap.</summary>
        public static void FillToCap()
        {
            Level = Intoxication.Cap;
            if (Level > Peak) Peak = Level;
            Save(Player.m_localPlayer);
        }

        private static void ApplyDrinkEffect(DrinkDef drink, Player p)
        {
            if (p == null) return;
            try
            {
                var seman = p.GetSEMan();
                if (drink.CuresHangover && seman.RemoveStatusEffect(Buffs.Hash(Buffs.Hangover), quiet: true))
                {
                    _pendingHangover = 0f;
                    p.Message(MessageHud.MessageType.Center, "The Bloody Ivar does its work. Your head clears.");
                    Plugin.Log.LogInfo("Bloody Ivar: hangover cured.");
                }
                if (!Plugin.DrinkEffects.Value) return;
                var se = Buffs.ForDrink(drink);
                if (se != null) seman.AddStatusEffect(se, resetTime: true);
            }
            catch (System.Exception ex) { Plugin.Report("Drink effect", ex); }
        }

        /// <summary>Eating while drinking takes a little off the meter (Core\Food). Does nothing when sober. The binge
        /// count is untouched: food steadies you, it does not wipe the slate (the meter reaching 0 still does that).</summary>
        public static void Eat(string nameToken, float health, float stamina, float eitr, Player p, int alreadyEaten = 0)
        {
            Attach(p);
            if (Level <= 0f) return;
            float points = Food.Sobering(health, stamina, eitr, Plugin.FoodSobers.Value) * Food.RepeatFactor(alreadyEaten);
            if (points <= 0f) return;
            float before = Level;
            Tier tierBefore = Tier;
            Level = Food.Eat(Level, points);
            Plugin.Log.LogInfo($"Ate {nameToken} ({health:0}/{stamina:0}/{eitr:0}): -{points:0.0}{(alreadyEaten > 0 ? $" (repeat x{alreadyEaten + 1}, {Food.RepeatFactor(alreadyEaten):P0})" : "")}, meter {before:0.0} -> {Level:0.0} ({Tier}).");
            Save(p);
            if (p == null) return;
            try
            {
                string text = Tier != tierBefore ? $"The food steadies you. ({tierBefore} -> {Tier})" : "The food steadies you a little.";
                p.Message(MessageHud.MessageType.TopLeft, text);
            }
            catch (System.Exception ex) { Plugin.Report("Food message", ex); }
        }

        public static void Tick(float dt, Player p)
        {
            Attach(p);
            if (Level > 0f)
            {
                Tier before = Tier;
                Level = Intoxication.Decay(Level, dt, Plugin.SoberPerSecond.Value);
                if (Tier != before) Plugin.Log.LogDebug($"Meter {Level:0.0}: {before} -> {Tier}.");
            }
            if (Level <= 0f && (BingeDrinks > 0 || Peak > 0f)) EndBinge(p);

            if (_pendingHangover > 0f && p != null && !p.IsDead()) StartHangover(p, _pendingHangover, restored: true);

            _courageTimer -= dt;
            if (_courageTimer <= 0f) { _courageTimer = 0.5f; UpdateCourage(p); }
            _saveTimer -= dt;
            if (_saveTimer <= 0f) { _saveTimer = 2f; Save(p); }
        }

        private static void EndBinge(Player p)
        {
            float seconds = Effects.HangoverSeconds(Peak, BlackedOut);
            Plugin.Log.LogInfo($"Sober again; the binge of {BingeDrinks} drink(s) is over (peak {Peak:0.0}{(BlackedOut ? ", blacked out" : "")}).");
            BingeDrinks = 0;
            Peak = 0f;
            BlackedOut = false;
            if (seconds > 0f && Plugin.Hangovers.Value && Plugin.Enabled.Value && p != null) StartHangover(p, seconds, restored: false);
            Save(p);
        }

        /// <summary>
        /// The local player died. The new body has no status effects, so the hangover is carried over by hand, and a death
        /// in a blackout wakes with the full post-blackout one (Effects.HangoverAfterDeath). Applied by Tick once the new
        /// body is alive. The meter itself is untouched: dying does not sober you.
        /// </summary>
        // The hangover left on the body just BEFORE vanilla's OnDeath strips every status effect (Patch_Death's prefix).
        // Negative means "not noted": nothing ran before this death, so fall back to reading the body.
        private static float _hangoverAtDeath = -1f;

        /// <summary>Called by Patch_Death's prefix, while the dying body still has its effects.</summary>
        public static void NoteHangoverBeforeDeath(Player p)
        {
            _hangoverAtDeath = HangoverLeft(p);
        }

        public static void OnDeath(Player p, bool inBlackout)
        {
            Attach(p);
            float left = _hangoverAtDeath >= 0f ? _hangoverAtDeath : HangoverLeft(p);
            _hangoverAtDeath = -1f;
            bool hangoversOn = Plugin.Hangovers.Value && Plugin.Enabled.Value;
            float next = hangoversOn ? Effects.HangoverAfterDeath(left, inBlackout) : left;
            // One binge, one hangover: the death just handed out this binge's, so the binge ending must not add another.
            float peak = Peak; bool blackedOut = BlackedOut;
            Effects.SettleAfterDeath(inBlackout, hangoversOn, ref peak, ref blackedOut);
            bool settled = peak != Peak || blackedOut != BlackedOut;
            Peak = peak; BlackedOut = blackedOut;
            if (next <= 0f) { if (settled) Save(p); return; }
            _pendingHangover = next;
            Plugin.Log.LogInfo($"Died{(inBlackout ? " in a blackout" : "")}: the new body wakes with {next:0} s of hangover (had {left:0} s)" +
                               (settled ? "; that is this binge's hangover, so the binge ending adds none." : "."));
            Save(p);
        }

        private static void StartHangover(Player p, float seconds, bool restored)
        {
            if (!restored)
            {
                float running = p.GetSEMan().GetStatusEffect(Buffs.Hash(Buffs.Hangover))?.GetRemaningTime() ?? 0f;
                if (!Effects.ShouldReplaceHangover(running, seconds))
                {
                    Plugin.Log.LogInfo($"Hangover of {seconds:0} s offered; the {running:0} s one already running is longer, so it stays.");
                    return;
                }
            }
            _pendingHangover = 0f;
            try
            {
                var template = Buffs.Get(Buffs.Hangover);
                if (template == null) return;
                var seman = p.GetSEMan();
                seman.RemoveStatusEffect(Buffs.Hash(Buffs.Hangover), quiet: true);
                var live = seman.AddStatusEffect(template, resetTime: true);
                if (live != null) live.m_ttl = seconds; // the clone's own clock: remaining = m_ttl - elapsed
                Plugin.Log.LogInfo(restored ? $"Hangover resumed: {seconds:0} s left."
                                            : $"Hangover: {seconds:0} s. A Bloody Ivar cures it.");
            }
            catch (System.Exception ex) { Plugin.Report("Hangover", ex); }
        }

        /// <summary>Liquid courage while Drunk or worse: one effect for Drunk, a stronger one from Hammered.</summary>
        private static void UpdateCourage(Player p)
        {
            if (p == null || p.IsDead()) return;
            try
            {
                var seman = p.GetSEMan();
                string want = !Plugin.Enabled.Value || !Plugin.LiquidCourage.Value ? null
                            : Tier >= Tier.Hammered ? Buffs.CourageHammered : Tier >= Tier.Drunk ? Buffs.CourageDrunk : null;
                foreach (string name in new[] { Buffs.CourageDrunk, Buffs.CourageHammered })
                {
                    bool has = seman.HaveStatusEffect(Buffs.Hash(name));
                    if (name == want && !has)
                    {
                        var se = Buffs.Get(name);
                        if (se != null) seman.AddStatusEffect(se, resetTime: true);
                    }
                    else if (name != want && has) seman.RemoveStatusEffect(Buffs.Hash(name), quiet: true);
                }
            }
            catch (System.Exception ex) { Plugin.Report("Liquid courage", ex); }
        }

        /// <summary>
        /// After a blackout you come round still hammered (the owner: "you don't immediately sober up from a blackout"),
        /// about five minutes from sober at the default rate, but the binge that did it is spent. The hangover that follows
        /// is the blackout one.
        /// </summary>
        public static void WakeFromBlackout()
        {
            // 90, not the Hammered floor of 75: at exactly the floor the next tick dropped you to Drunk, so "wake
            // hammered" lasted one frame (seen in the log 2026-11-17). 90 is a minute of Hammered, then Drunk, then the
            // long way down: about six minutes to sober at the default rate.
            Level = WakeLevel;
            if (Level > Peak) Peak = Level;
            BingeDrinks = 0;
            BlackedOut = true;
            Save(Player.m_localPlayer);
        }

        /// <summary>`tankard sober`: meter, binge and hangover gone, as if the night never happened.</summary>
        public static void Reset()
        {
            Level = 0f;
            BingeDrinks = 0;
            Peak = 0f;
            BlackedOut = false;
            _pendingHangover = 0f;
            var p = Player.m_localPlayer;
            if (p != null)
            {
                try { p.GetSEMan().RemoveStatusEffect(Buffs.Hash(Buffs.Hangover), quiet: true); } catch (System.Exception) { }
                UpdateCourage(p);
                Save(p);
            }
        }

        // ---- persistence ----------------------------------------------------------------------------------------

        /// <summary>
        /// Binds the in-memory state to the character. A different profile (another character, or the first one this
        /// session) loads from the save; the same profile keeps what is in memory and simply writes it onto the new body.
        /// </summary>
        private static void Attach(Player p)
        {
            if (p == null) return;
            object profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile != null && !ReferenceEquals(profile, _profile))
            {
                _profile = profile;
                p.m_customData.TryGetValue(MeterState.Key, out string saved);
                var st = MeterState.Parse(saved);
                Level = st.Level; BingeDrinks = st.Binge; Peak = st.Peak; BlackedOut = st.BlackedOut;
                _pendingHangover = st.HangoverLeft;
                _body = p;
                if (saved != null)
                    Plugin.Log.LogInfo($"Meter loaded from the character: {Level:0.0} ({Tier}), binge {BingeDrinks}, peak {Peak:0.0}" +
                                       (_pendingHangover > 0f ? $", hangover {_pendingHangover:0} s left" : "") + ".");
                return;
            }
            if (!ReferenceEquals(p, _body)) { _body = p; Save(p); } // a respawn: same character, new body
        }

        /// <summary>True once this body's saved state is loaded, so its hangover and binge can be trusted (the long walk
        /// must not end on the first frames after a login, before the save is read).</summary>
        public static bool AttachedTo(Player p) => p != null && ReferenceEquals(p, _body);

        private static void Save(Player p)
        {
            if (p == null || !ReferenceEquals(p, _body)) return;
            try
            {
                float left = _pendingHangover;
                var live = p.GetSEMan().GetStatusEffect(Buffs.Hash(Buffs.Hangover));
                if (live != null) left = live.GetRemaningTime();
                var st = new MeterState { Level = Level, Binge = BingeDrinks, Peak = Peak, BlackedOut = BlackedOut, HangoverLeft = left };
                p.m_customData[MeterState.Key] = st.Format();
            }
            catch (System.Exception ex) { Plugin.Report("Meter save", ex); }
        }

        public static float HangoverLeft(Player p)
        {
            if (p == null) return 0f;
            var live = p.GetSEMan().GetStatusEffect(Buffs.Hash(Buffs.Hangover));
            return live != null ? live.GetRemaningTime() : _pendingHangover;
        }
    }
}
