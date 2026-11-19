using System.Collections.Generic;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard.Registration
{
    /// <summary>
    /// Builds Tankard's status effects as plain vanilla SE_Stats, so they tick, show an icon and a tooltip, and modify
    /// regen, carry weight, damage and resistances exactly the way a vanilla mead does.
    ///
    /// They are NOT hung on the item as m_consumeStatusEffect: Player.CanConsumeItem refuses an item whose effect you
    /// already have ("$msg_cantconsume", decompiled 1.0.21), which would make a second cup impossible. Patch_Consume
    /// applies them instead, through SEMan.AddStatusEffect(StatusEffect, resetTime: true), which on your own character
    /// clones the template without an ObjectDB lookup. They are registered in ObjectDB as well, so any path that resolves
    /// an effect by its hash finds them.
    /// </summary>
    internal static class Buffs
    {
        public const string CourageDrunk = "SE_TK_CourageDrunk";
        public const string CourageHammered = "SE_TK_CourageHammered";
        public const string Hangover = "SE_TK_Hangover";
        public const string ValhallaPrice = "SE_TK_Valhalla";      // the three-day hangover; ValhallaRunner writes its numbers
        public const string SkadiCalm = "SE_TK_SkadiCalm";         // shown while the wild leaves you alone
        public const string FrozenMorning = "SE_TK_FrozenMorning"; // a copy of vanilla's Freezing under our own name

        private static SE_Stats _frozen;

        /// <summary>
        /// The Frozen morning: vanilla's own Freezing effect, copied and renamed. Vanilla adds and removes Freezing BY NAME
        /// every frame from the weather (Player.UpdateEnvStatusEffects, decompiled), so the real one would vanish on a warm
        /// day; the copy keeps every number Freezing has and answers to none of that. Built lazily, from ObjectDB, the
        /// first time it is needed, because the original lives there.
        /// </summary>
        public static SE_Stats FrozenMorningEffect()
        {
            if (_frozen != null) return _frozen;
            try
            {
                var vanilla = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectFreezing) as SE_Stats : null;
                if (vanilla == null) { Plugin.Log.LogWarning("Skadi's Nightmare: vanilla's Freezing effect is missing, so there is no Frozen morning."); return null; }
                var se = UnityEngine.Object.Instantiate(vanilla);
                se.name = FrozenMorning;
                se.hideFlags = HideFlags.DontUnloadUnusedAsset;
                se.m_name = "Frozen Morning";
                se.m_tooltip = "Skadi wants her due. Frozen to the bone, whatever the weather. A fire or a frost mead thaws you.";
                se.m_ttl = Core.Skadi.FrozenSeconds;
                se.m_startMessage = "";
                se.m_stopMessage = "The cold lets go of you.";
                _frozen = se;
                return se;
            }
            catch (System.Exception ex) { Plugin.Report("Frozen morning", ex); return null; }
        }

        private static readonly Dictionary<string, SE_Stats> _all = new Dictionary<string, SE_Stats>();

        public static string AssetNameFor(DrinkDef d) => "SE_" + d.PrefabName;

        public static SE_Stats Get(string assetName)
        {
            if (_all.Count == 0) Build();
            _all.TryGetValue(assetName, out SE_Stats se);
            if (se != null && se.m_icon == null) se.m_icon = IconFor(assetName); // items may register after the first build
            return se;
        }

        public static SE_Stats ForDrink(DrinkDef d) => d?.Effect == null ? null : Get(AssetNameFor(d));

        public static int Hash(string assetName) => assetName.GetStableHashCode();

        /// <summary>Adds every Tankard effect to this ObjectDB that it does not already hold.</summary>
        public static void EnsureIn(ObjectDB db)
        {
            if (db == null || db.m_StatusEffects == null) return;
            if (_all.Count == 0) Build();
            int added = 0;
            foreach (var kv in _all)
            {
                if (kv.Value.m_icon == null) kv.Value.m_icon = IconFor(kv.Key);
                if (db.GetStatusEffect(Hash(kv.Key)) != null) continue;
                db.m_StatusEffects.Add(kv.Value);
                added++;
            }
            if (added > 0) Plugin.Log.LogInfo($"Registered {added} status effect(s).");
        }

        private static void Build()
        {
            foreach (var d in Drinks.All)
            {
                if (d.Effect == null) continue;
                var e = d.Effect;
                var se = Make(AssetNameFor(d), e.Name, e.Describe() + ".", e.Seconds);
                se.m_healthRegenMultiplier = e.HealthRegen;
                se.m_staminaRegenMultiplier = e.StaminaRegen;
                se.m_eitrRegenMultiplier = e.EitrRegen;
                se.m_addMaxCarryWeight = e.CarryWeight;
                se.m_runStaminaDrainModifier = e.RunStaminaDrain;
                if (e.Resist != Resist.None)
                    se.m_mods = new List<HitData.DamageModPair>
                    {
                        new HitData.DamageModPair { m_type = TypeOf(e.Resist), m_modifier = HitData.DamageModifier.Resistant },
                    };
            }

            foreach (var t in new[] { Tier.Drunk, Tier.Hammered })
            {
                float dmg = Effects.CourageDamage(t), block = Effects.CourageBlockCost(t);
                var se = Make(t == Tier.Drunk ? CourageDrunk : CourageHammered, t == Tier.Drunk ? "Mead Courage" : "Berserker's Swagger",
                    $"+{Mathf.RoundToInt((dmg - 1f) * 100f)}% damage, +{Mathf.RoundToInt(block * 100f)}% stamina to block. Lasts while you're this drunk.", 0f);
                se.m_modifyAttackSkill = Skills.SkillType.All;
                se.m_damageModifier = dmg;
                se.m_blockStaminaUseModifier = block;
            }

            var h = Make(Hangover, "Hangover",
                $"{Mathf.RoundToInt((Effects.HangoverStaminaRegen - 1f) * 100f)}% stamina regen, {Mathf.RoundToInt((Effects.HangoverHealthRegen - 1f) * 100f)}% health regen. A Bloody Ivar fixes it.",
                Effects.HangoverMaxSeconds);
            h.m_staminaRegenMultiplier = Effects.HangoverStaminaRegen;
            h.m_healthRegenMultiplier = Effects.HangoverHealthRegen;
            h.m_startMessage = "Your head is pounding.";
            h.m_stopMessage = "The hangover lifts.";

            // The Valhalla's price. Starts at full strength; ValhallaRunner rewrites the numbers on the live copy each second
            // as they fade, and ends it itself (m_ttl 0 never runs out on its own).
            var v = Make(ValhallaPrice, "Valhalla's Fury", "Three days of it. Only more drink takes the edge off.", 0f);
            v.m_staminaRegenMultiplier = Core.Valhalla.StartRegen;
            v.m_attackStaminaUseModifier = v.m_blockStaminaUseModifier = v.m_dodgeStaminaUseModifier = v.m_jumpStaminaUseModifier =
                v.m_runStaminaDrainModifier = Core.Valhalla.StartStaminaUse;
            v.m_startMessage = "The fury of Valhalla is in you. Your arms ache for it.";

            Make(SkadiCalm, "Skadi's Calm", "Nothing wild will touch you while you're drunk on it, short of the Blackout stage. Bosses and raids don't care, and anything you hit hits back.", 0f);
        }

        private static SE_Stats Make(string assetName, string display, string tooltip, float ttl)
        {
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = assetName; // NameHash() caches from the asset name on first call: set it before anything asks
            se.hideFlags = HideFlags.DontUnloadUnusedAsset;
            se.m_name = display;
            se.m_tooltip = tooltip;
            se.m_ttl = ttl;
            se.m_icon = IconFor(assetName);
            _all[assetName] = se;
            return se;
        }

        private static HitData.DamageType TypeOf(Resist r) =>
            r == Resist.Fire ? HitData.DamageType.Fire : r == Resist.Frost ? HitData.DamageType.Frost : HitData.DamageType.Poison;

        private static Sprite IconFor(string assetName)
        {
            string prefab = assetName == Hangover ? "TK_BloodyIvar"
                          : assetName == ValhallaPrice ? "TK_Valhalla"
                          : assetName == SkadiCalm ? "TK_SkadisNightmare"
                          : assetName == CourageDrunk || assetName == CourageHammered ? "TK_SurtrsKiss"
                          : assetName.StartsWith("SE_") ? assetName.Substring(3) : assetName;
            var go = Items.Get(prefab);
            var icons = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_icons : null;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }
    }
}
