// Closed facade permits source-only identity-fence tests, without sockets/worlds.
namespace MphRead.Mods.Network;
internal sealed class NetTransport;
internal enum NetRole { Offline,Client,Server }
internal static class NetSession
{
    internal static readonly object ClientLifecycleGate = new();
    internal static NetTransport? EvidenceTransport;
    internal static NetRole Role;
    internal static bool IsClient { get=>Role==NetRole.Client; set=>Role=value?NetRole.Client:NetRole.Offline; }
    internal static int Stops;
    internal static void Stop() { NetClientJoinOwnership.RetireIncarnation(); EvidenceTransport = null; IsClient = false; ++Stops; }
}
