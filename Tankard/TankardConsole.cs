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

        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("tankard", "Tankard: tankard status | map (end a blackout, map back) | sober | tolerance [0-69]", args =>
                {
                    try
                    {
                        string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "status";
                        Player p = Player.m_localPlayer;
                        switch (sub)
                        {
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
                                    $"blackout {(BlackoutRunner.MapHidden ? "ON" : "off")}, map remembered {MapMemory.Progress:P0}, " +
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
