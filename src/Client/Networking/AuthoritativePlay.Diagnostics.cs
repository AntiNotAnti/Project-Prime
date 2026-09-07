using System.Diagnostics;
using MphRead.Hud.Network;
namespace MphRead.Mods.Network
{
    public sealed partial class AuthoritativePlay
    {
        public NetworkHealthReading? ReadNetworkHealth() => Client.Connection is { } connection
            ? NetworkHealthReading.Read(connection.Metrics, Prediction, _interpolation, Stopwatch.GetTimestamp()) : null;
    }
}
