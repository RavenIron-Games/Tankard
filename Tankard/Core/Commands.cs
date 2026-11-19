namespace RavenIron.Tankard
{
    /// <summary>
    /// The `tankard` console command is admin-only, every subcommand. Added 2026-11-18 (owner: "all commands are admin").
    ///
    /// Valheim's F5 console is open to every player, and the command was registered with no gate, so anyone could type
    /// `tankard map` and walk out of a blackout, `tankard sober` to clear a binge, `tankard tolerance 69` to drink like a
    /// veteran from day one, or `tankard find` to be pointed at the lost tankard instead of looking for it. The readout
    /// (`tankard status`) goes with them: it is a tester's tool, not a player feature.
    ///
    /// Who counts as an admin is ServerSync's answer (TankardConsole asks it): the server itself, a host, single player,
    /// or a client the server finds on adminlist.txt.
    /// </summary>
    public static class Commands
    {
        /// <param name="isAdmin">ServerSync's IsAdmin; false when ServerSync is not up yet.</param>
        public static bool Allowed(bool isAdmin) => isAdmin;

        public static string Refusal(string sub) =>
            $"tankard{(string.IsNullOrEmpty(sub) ? "" : " " + sub)}: admins only. On a server, an admin on adminlist.txt can run it.";
    }
}
