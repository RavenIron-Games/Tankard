using System;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard.Patches
{
    /// <summary>
    /// The Valhalla's fury: while its hangover runs, the local player's hits on CREATURES land harder, never on players
    /// (owner, 2026-11-18: "creatures only"). Character.Damage is called on the ATTACKER's machine, before the hit is sent
    /// to whoever owns the target (it only fills in the weak spot and invokes RPC_Damage; decompiled 1.0.21), so the
    /// attacker can scale its own hit here, and nobody else's game has to know. Melee and projectiles both arrive through it.
    /// A behaviour prefix that only scales the hit in place: Priority.Low, honours __runOriginal, always returns true.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Patch_ValhallaFury
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, HitData hit, bool __runOriginal)
        {
            if (!__runOriginal || hit == null) return true;
            try
            {
                if (__instance is Player) return true;
                float s = ValhallaRunner.Strength;
                if (s <= 0f) return true;
                Player me = Player.m_localPlayer;
                if (me == null || !ReferenceEquals(hit.GetAttacker(), me)) return true;
                hit.ApplyModifier(Valhalla.DamageMultiplier(s));
            }
            catch (Exception ex) { Plugin.Report("Valhalla fury", ex); }
            return true;
        }
    }

    /// <summary>
    /// Skadi's calm: a creature does not count a calm player as an enemy (owner, 2026-11-18: "every creature but bosses";
    /// raids stay hostile; one you hit fights back). BaseAI.IsEnemy(a, b) is the one test every creature's AI uses to pick a
    /// target and to decide what to flee, and it runs on whichever machine owns the creature; the calm is read from the
    /// player's synced ZDO, so it holds whoever that is. A behaviour prefix: Priority.Low, honours __runOriginal, and has no
    /// opinion (return true) about everything except a calm player and a creature that should leave them alone.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    internal static class Patch_SkadiCalm
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(Character a, Character b, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.SkadisNightmare.Value) return true;
                Player p = a as Player ?? b as Player;
                if (p == null) return true;
                Character c = ReferenceEquals(p, a) ? b : a;
                if (c == null || c is Player) return true;
                ZDO pz = SkadiRunner.ZdoOf(p);
                if (pz == null || !pz.GetBool(SkadiRunner.CalmHash)) return true;
                bool raid = c.GetBaseAI() is MonsterAI m && m.IsEventCreature();
                ZDO cz = SkadiRunner.ZdoOf(c);
                bool provoked = cz != null && cz.GetBool(Skadi.ProvokedZdoKey(p.GetPlayerID()));
                if (!Skadi.LeavesAlone(true, c.IsBoss(), raid, provoked)) return true;
                __result = false;
                return false;
            }
            catch (Exception) { return true; } // hot path: never spam, never break targeting
        }
    }

    /// <summary>
    /// One you hit fights back: a creature hurt by a player remembers that player on its own ZDO, and Patch_SkadiCalm reads it.
    /// MonsterAI.OnDamaged runs where the damage is applied, which is the creature's owner, the only machine that may
    /// write its ZDO. Observes only; always returns true.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), "OnDamaged")]
    internal static class Patch_SkadiProvoked
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(MonsterAI __instance, Character attacker, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                if (!(attacker is Player p) || !Plugin.Enabled.Value || !Plugin.SkadisNightmare.Value) return true;
                Character self = __instance.GetComponent<Character>();
                ZDO cz = SkadiRunner.ZdoOf(self);
                if (cz == null || !cz.IsOwner()) return true;
                string key = Skadi.ProvokedZdoKey(p.GetPlayerID());
                if (!cz.GetBool(key)) cz.Set(key, true);
            }
            catch (Exception ex) { Plugin.Report("Skadi provoked", ex); }
            return true;
        }
    }
}
