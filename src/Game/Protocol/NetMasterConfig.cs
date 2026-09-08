namespace MphRead.Mods.Network
{
    public static class NetMasterConfig
    {
        /// <summary>
        /// The directory a server reports to unless told otherwise.
        ///
        /// The project's default directory endpoint. It can still be
        /// overridden by launcher preferences or command-line arguments.
        /// </summary>
        public const string DefaultHost = "51.161.113.128";

        /// <summary>
        /// Beside the game port rather than on it: a machine can then run
        /// both the directory and a server people play on, which is exactly
        /// the arrangement this was written for.
        /// </summary>
        public const ushort DefaultPort = 27889;

        /// <summary>Seconds between heartbeats.</summary>
        public const double HeartbeatSeconds = 15.0;

        /// <summary>
        /// How long a server stays listed after its last heartbeat. Several
        /// heartbeats' worth, so a server does not vanish from the list
        /// because one datagram went missing.
        /// </summary>
        public const double ExpirySeconds = 50.0;

        /// <summary>
        /// How many servers fit in one reply. The packet cap is 1024 bytes
        /// and an entry is 83, so this is what the datagram holds rather than
        /// a policy about how many servers may exist.
        /// </summary>
        public static int EntriesPerPacket =>
            (NetConfig.MaxPacketSize - 1 - 4) / MasterEntryPacket.Size;
    }

}
