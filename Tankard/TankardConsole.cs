using System;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard
{
    /// <summary>
    /// `tankard status | map | sober`. Registered from a Terminal.InitTerminal postfix with the public ConsoleCommand
    /// constructor, the way Fathom and Tarn do on 1.0.x, never through Terminal.commands (private in the shipping game).
    ///
    /// `map` is the way out of a blackout that went wrong: it ends the blackout on this character in this world and puts
    /// the minimap back, whatever state the save is in. Asked for during the first in-game test (2026-11-17).
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class TankardConsole
    {
        private static string StreakText(Player p)
        {
            if (p == null) return "streak: no player";
            p.m_customData.TryGetValue(Blackout.StreakKeyFor(BlackoutRunner.WorldId()), out string s);
            Blackout.Streak st = Blackout.ParseStreak(s);
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay() : -1;
            if (st.Days == 0) return $"no blackout streak (today is day {today})";
            bool live = today == st.LastDay || today == st.LastDay + 1;
            return $"streak {st.Days} day(s), {st.Count} blackout(s), last on day {st.LastDay}, today day {today}" +
                   (live ? (st.Days + (today == st.LastDay ? 0 : 1) >= Blackout.TotalLossDays ? " - the next one takes EVERYTHING" : "") : " (broken: a day was skipped)");
        }

        private static int CountKeys(Player p)
        {
            if (p == null) return 0;
            int n = 0;
            foreach (var k in p.m_customData.Keys) if (k.StartsWith("Tankard.", StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>
        /// `tankard find`: every lost tankard this game has loaded, nearest first, with which way to walk, how far, whose it
        /// is and how long is left to find it; and whether yours is already in your bag. Only the loaded area can be seen
        /// (a dropped item is a ZDO with no body until its zone loads), which always covers yours: it lands 10-20 m away.
        /// A one-off scene search, so it costs nothing until it is typed.
        /// </summary>
        private static System.Collections.Generic.List<string> FindLostTankards(Player p)
        {
            var lines = new System.Collections.Generic.List<string>();
            long me = p.GetPlayerID();
            long now = DateTime.UtcNow.Ticks;
            UnityEngine.Vector3 at = p.transform.position;

            foreach (ItemDrop.ItemData it in p.GetInventory().GetAllItems())
            {
                if (it?.m_shared == null || it.m_shared.m_name != LostTankard.NameToken) continue;
                it.m_customData.TryGetValue(LostTankard.OwnerKey, out string bagOwner);
                bool mine = LostTankard.IsMine(bagOwner, me);
                lines.Add(mine ? "tankard: your lost tankard is in your bag. Drink from it to end the blackout."
                               : "tankard: you are carrying someone else's lost tankard. It does nothing for you.");
            }

            var found = new System.Collections.Generic.List<(float d, string text)>();
            foreach (ItemDrop drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(UnityEngine.FindObjectsSortMode.None))
            {
                var data = drop != null ? drop.m_itemData : null;
                if (data?.m_shared == null || data.m_shared.m_name != LostTankard.NameToken) continue;
                data.m_customData.TryGetValue(LostTankard.OwnerKey, out string owner);
                data.m_customData.TryGetValue(LostTankard.DroppedKey, out string dropped);
                UnityEngine.Vector3 pos = drop.transform.position;
                float dx = pos.x - at.x, dz = pos.z - at.z;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                double left = LostTankard.SecondsLeft(dropped, now);
                string whose = LostTankard.IsMine(owner, me) ? "YOURS" : "someone else's";
                string time = left > 0 ? $"{left:0} s left to find it" : "too late: it crumbles when picked up";
                found.Add((d, $"tankard: {whose} lost tankard {d:0} m {LostTankard.Compass(dx, dz)}, {pos.y - at.y:+0;-0;0} m up/down, {time}"));
            }
            found.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var f in found) lines.Add(f.text);

            if (lines.Count == 0)
                lines.Add(BlackoutRunner.MapHidden
                    ? "tankard: no lost tankard nearby. It is only dropped when you die during a blackout, and it despawns after 5 minutes unfound. Walk back to the drinking spot instead."
                    : "tankard: no lost tankard nearby, and no blackout running.");
            Plugin.Log.LogInfo($"Console: tankard find listed {found.Count} lost tankard(s) nearby.");
            return lines;
        }

        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("tankard", "Tankard: tankard status | find (your lost tankard) | map (end a blackout, map back) | sober | tolerance [0-69]", args =>
                {
                    try
                    {
                        string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "status";
                        Player p = Player.m_localPlayer;
                        switch (sub)
                        {
                            case "find":
                                if (p == null) { args.Context.AddString("tankard: no local player"); return; }
                                foreach (string line in FindLostTankards(p)) args.Context.AddString(line);
                                return;
                            case "map":
                                if (p == null) { args.Context.AddString("tankard: no local player"); return; }
                                BlackoutRunner.EndBlackout(p, restoreMinimap: true);
                                Plugin.Log.LogInfo("Console: blackout ended by hand, map restored.");
                                args.Context.AddString("tankard: blackout ended, map back");
                                return;
                            case "tolerance":
                                if (p == null) { args.Context.AddString("tankard: no local player"); return; }
                                if (args.Args.Length > 2)
                                    p.m_customData[Tolerance.Key] = Tolerance.Format(Tolerance.Parse(args.Args[2]));
                                args.Context.AddString($"tankard: tolerance {Meter.ToleranceOf(p):0.00} of {Tolerance.Max:0} " +
                                                       $"({Tolerance.Describe(Meter.ToleranceOf(p))})");
                                return;
                            case "sober":
                                Meter.Reset();
                                args.Context.AddString("tankard: sober (meter and binge reset)");
                                return;
                            default:
                                args.Context.AddString(
                                    $"tankard: meter {Meter.Level:0.0} ({Meter.Tier}), drink {Meter.BingeDrinks} of this binge, " +
                                    $"tolerance {Meter.ToleranceOf(p):0.00} of {Tolerance.Max:0} ({Tolerance.Describe(Meter.ToleranceOf(p))}), " +
                                    $"binge peak {Meter.Peak:0.0}{(Meter.BlackedOut ? " (blacked out)" : "")}, hangover {Meter.HangoverLeft(p):0} s, " +
                                    $"blackout {(BlackoutRunner.MapHidden ? "ON" : "off")}, " +
                                    $"bed {(BlackoutRunner.OnLongWalk ? "NO (long walk home until the hangover is over)" : "yes")}, map remembered {MapMemory.Progress:P0}, " +
                                    $"minimap mode {(Minimap.instance != null ? Minimap.instance.m_mode.ToString() : "none")}, " +
                                    $"world no-map modifier {(Game.m_noMap ? "ON" : "off")}, " +
                                    $"saved blackout keys here: {CountKeys(p)}, " + StreakText(p) + ", " + ServerSyncSetup.Describe());
                                return;
                        }
                    }
                    catch (Exception ex) { args.Context.AddString($"tankard: failed ({ex.Message})"); }
                });
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"tankard console: register failed: {ex.Message}"); }
        }
    }
}
