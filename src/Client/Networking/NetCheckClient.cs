namespace MphRead.Mods.Network
{
    /// <summary>The public rendered network check uses the shipping authoritative client.</summary>
    public static class NetCheckClient
    {
        public static int Run(string host, int port, string name, Hunter hunter, double seconds,
            string? shotDirectory, int width, int height, bool recordReplay = false,
            double spectateAt = -1, double rejoinAt = -1)
        {
            return AuthoritativeCheck.Run(host, port, name, hunter, seconds,
                shotDirectory, width, height, recordReplay, spectateAt, rejoinAt);
        }
    }
}
