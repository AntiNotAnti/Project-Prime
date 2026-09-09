using FruityPrime.Server.Node.Lobbies;
using MphRead.Mods.Network;

namespace FruityPrime.Server.Node.Tests;

/// <summary>Seeded identities, clocks, and bounded bursts for control-plane flood regressions.</summary>
internal sealed class FloodFixture
{
    internal const int Seed = 0x514A_0005;
    private readonly Random _random = new(Seed);

    internal double Now { get; set; } = 10;

    internal LobbyIdentity Identity(string name)
        => new(NextGuid(), NextGuid(), name);

    internal int Consume(ref NetRateLimit limit, int attempts)
    {
        int accepted = 0;
        for (int i = 0; i < attempts; i++)
            if (limit.Take(Now)) accepted++;
        return accepted;
    }

    internal Guid NextGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        var value = new Guid(bytes);
        return value == Guid.Empty ? new Guid("00000000-0000-0000-0000-000000000001") : value;
    }
}
