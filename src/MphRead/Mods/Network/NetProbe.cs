namespace MphRead.Mods.Network
{
    /// <summary>Read-only discovery never claims a player slot.</summary>
    public static class NetProbe
    {
        public static (bool Ok, string Message) Probe(string address, int port, int timeoutMs = 3000)
        {
            ServerStatus status = NetStatus.Query(address, port, allowJoinProbe: false, timeoutMs);
            if (!status.Online) { return (false, status.Message); }
            if (!status.Compatible)
            {
                return (false, status.IncompatibilityReason);
            }
            return (true, $"Reached {address}:{port}: {status.Players}/{status.MaxPlayers} players, {status.RoomKey}.");
        }
    }
}
