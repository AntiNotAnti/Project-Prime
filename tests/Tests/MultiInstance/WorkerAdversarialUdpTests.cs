using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using FruityPrime.Server.Worker;
using MphRead.Identity;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class WorkerAdversarialUdpTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RealUdpSignedGuestAdmissionsAndCrossMatchPacketsCannotChangeOtherWorld()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        var options = new WorkerOptions { MaxMatches = 2, SimulationLanes = 2, MaxMatchesPerLane = 1,
            PlacementP99Milliseconds = 10000, PlacementCpuPercent = 100, MinimumMemoryHeadroomBytes = 0 };
        using var physical = new NetTransport(0);
        var hub = new WorkerNetworkHub(physical, options.Incarnation, new RoutedMatchDatagramRouter(), 2);
        await using var runtime = new WorkerRuntime(options, content.Content, hub);
        using var issuer = new WorkerAdmissionIssuer("udp-security");
        await runtime.UpdateSigningKeyAsync(new(issuer.KeyId, issuer.ExportPublicKey()));
        MatchSpec Spec(string name) => new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(),
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1),
            new("MP1 SANCTORUS", content.Content.ContentHash, content.Content.Version, options.BuildVersion, NetHeader.Version),
            MatchTrustClass.Private, null, null, ImmutableArray.Create(new RosterSeat(0, null, Guid.NewGuid(), name, Hunter.Samus, 0, SeatRole.Player, false)),
            FruityPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        MatchSpec first = Spec("GuestA"), second = Spec("GuestB");
        var placeA = Assert.IsType<MatchReady>(await runtime.CreateAsync(first)).Placement;
        var placeB = Assert.IsType<MatchReady>(await runtime.CreateAsync(second)).Placement;
        WorkerAdmissionClaims Claims(MatchSpec spec, MatchPlacement placement, ulong nonce)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new(spec.NodeId, spec.NodeIncarnation, placement.WorkerId, placement.WorkerIncarnation, spec.LobbyId,
                spec.MatchId, placement.WireMatchId, Guid.NewGuid(), null, spec.Roster[0].GuestSessionId, SeatRole.Player, 0,
                spec.Roster[0].DisplayName, nonce, now, now + 60, Guid.NewGuid());
        }
        var claimsA = Claims(first, placeA, 111); var claimsB = Claims(second, placeB, 222);
        var endpoint = new IPEndPoint(IPAddress.Loopback, hub.LocalPort);
        using var attacker = new NetTransport(0);
        void AttackJoin(WorkerAdmissionClaims claims, uint route)
        {
            var join = new JoinPacket(NetHeader.Version, claims.JoinNonce, Hunter.Samus, claims.Name, Ticket: issuer.Issue(claims), WireMatchId: route);
            byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes); join.Write(bytes.AsSpan(NetHeader.Size));
            attacker.SendDatagram(endpoint, bytes);
        }
        AttackJoin(claimsB, placeA.WireMatchId.Value);
        AttackJoin(claimsA with { WorkerIncarnation = Guid.NewGuid(), JoinNonce = 333 }, placeA.WireMatchId.Value);
        AttackJoin(claimsA with { GuestSessionId = Guid.NewGuid(), JoinNonce = 444 }, placeA.WireMatchId.Value);
        AttackJoin(claimsA with { SeatId = 1, JoinNonce = 555 }, placeA.WireMatchId.Value);
        // Each rejected async validation produces a Refused datagram; wait for actual completion, not a sleep guess.
        int refused = 0;
        await Until(() => { foreach (var p in attacker.Drain()) if (NetHeader.TryRead(p.Data.AsSpan(0,p.Length), out var h) && h.Type == NetMessageType.Refused) refused++; return refused == 4; });
        Assert.Equal(0, await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.Count));
        Assert.Equal(0, await runtime.InvokeMatchAsync(second.MatchId, m => m.Network.Count));
        using var wireA = new NetTransport(0); using var wireB = new NetTransport(0);
        using var clientA = new NetClient(wireA, endpoint, claimsA.Name, Hunter.Samus, claimsA.JoinNonce, issuer.Issue(claimsA), wireMatchId: placeA.WireMatchId.Value);
        using var clientB = new NetClient(wireB, endpoint, claimsB.Name, Hunter.Samus, claimsB.JoinNonce, issuer.Issue(claimsB), wireMatchId: placeB.WireMatchId.Value);
        await Until(() => { clientA.Poll(); clientB.Poll(); return clientA.Connection != null && clientB.Connection != null; });
        Assert.True(clientA.Ready(clientA.Accepted.MatchId)); Assert.True(clientB.Ready(clientB.Accepted.MatchId));
        await UntilAsync(async () => { clientA.Poll(); clientB.Poll(); return await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.Peers[0]?.Connection.State == NetConnectionState.Playing)
            && await runtime.InvokeMatchAsync(second.MatchId, m => m.Network.Peers[0]?.Connection.State == NetConnectionState.Playing); });
        var beforeA = await runtime.InvokeMatchAsync(first.MatchId, m => (m.Network.Peers[0]!.Connection.Endpoint.Port, m.Network.Rejected, m.Network.Peers[0]!.GuestSessionId));
        uint phase = await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.PhaseRevision);
        byte[] cross = new byte[NetHeader.Size + InputBundle.HeaderSize + InputCommand.Size];
        new NetHeader(NetMessageType.Input, NetHeaderFlags.None, clientA.Connection!.Id, 0x70000000, 0, 0).Write(cross);
        InputBundle.Write(cross.AsSpan(NetHeader.Size), placeB.WireMatchId.Value,
            new[] { new InputCommand(0x70000000, 1, 1, InputButtons.Shoot, InputButtons.Shoot, Vector3.UnitX, 0) }, phase);
        attacker.SendDatagram(endpoint, cross);
        await UntilAsync(async () => await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.Rejected) > beforeA.Rejected);
        Assert.Equal(beforeA.Port, await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.Peers[0]!.Connection.Endpoint.Port));
        Assert.Equal(first.Roster[0].GuestSessionId, beforeA.GuestSessionId);
        Assert.Null(await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.Peers[0]!.PlayerId));
        Assert.Equal(second.Roster[0].GuestSessionId, await runtime.InvokeMatchAsync(second.MatchId, m => m.Network.Peers[0]!.GuestSessionId));
        Assert.NotEqual(clientA.Connection.Id, clientB.Connection!.Id);
        Assert.Equal(clientB.Connection.Id, await runtime.InvokeMatchAsync(second.MatchId, m => m.Network.Peers[0]!.Connection.Id));
    }
    private static async Task Until(Func<bool> predicate) => await UntilAsync(() => Task.FromResult(predicate()));
    private static async Task UntilAsync(Func<Task<bool>> predicate)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!await predicate()) await Task.Delay(5, timeout.Token); }
}
