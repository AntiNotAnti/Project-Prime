using System.Diagnostics;
using MphRead.Hud.Network;
namespace MphRead.Mods.Network
{
    public sealed partial class AuthoritativePlay
    {
        /// <summary>Bound local UDP port for owned-run diagnostics only.</summary>
        internal int LocalUdpPort => _transport.LocalPort;
        public NetworkHealthReading? ReadNetworkHealth() => Client.Connection is { } connection
            ? NetworkHealthReading.Read(connection.Metrics, Prediction, _interpolation, Stopwatch.GetTimestamp()) : null;
    }
}
