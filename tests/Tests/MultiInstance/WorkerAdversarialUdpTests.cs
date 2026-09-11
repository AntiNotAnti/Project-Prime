using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker;
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
        var hub = new WorkerNetworkHub(physical, options.Incarnation, new RoutedMatchDatagramRouter(),
            matchLimit: 2, udpAuthenticationEnabled: true);
        await using var runtime = new WorkerRuntime(options, content.Content, hub);
        using var issuer = new WorkerAdmissionIssuer("udp-security");
        await runtime.UpdateSigningKeyAsync(new(issuer.KeyId, issuer.ExportPublicKey()));
        MatchSpec Spec(string name) => new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(),
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1),
            new("MP1 SANCTORUS", content.Content.ContentHash, content.Content.Version, options.BuildVersion, NetHeader.Version),
            MatchTrustClass.Private, null, null, ImmutableArray.Create(new RosterSeat(0, null, Guid.NewGuid(), name, Hunter.Samus, 0, SeatRole.Player, false)),
            ProjectPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
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
        async Task<(Guid Id, byte[] Key)> Install(MatchSpec spec, MatchPlacement placement,
            WorkerAdmissionClaims claims, byte fill)
        {
            Guid id = Guid.NewGuid();
            byte[] key = new byte[AdmissionKeyRules.ByteLength];
            Array.Fill(key, fill);
            var command = new InstallAdmissionKey(id, claims.TicketId, claims.NodeSessionId,
                spec.NodeId, spec.NodeIncarnation, spec.MatchId, placement.WireMatchId,
                placement.WorkerId, placement.WorkerIncarnation, claims.SeatId, claims.JoinNonce,
                claims.ExpiresAt, Convert.ToBase64String(key));
            Assert.IsType<AdmissionKeyInstalled>(await runtime.InstallAdmissionKeyAsync(command));
            return (id, key);
        }
        var admissionA = await Install(first, placeA, claimsA, 0xA1);
        var admissionB = await Install(second, placeB, claimsB, 0xB2);
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
        // Protected Workers silently reject legacy/unauthenticated joins at
        // the routing boundary and must not emit an unauthenticated Refused.
        await Until(() => hub.Metrics.PacketsRejected >= 4);
        Assert.Empty(attacker.Drain());
        Assert.Equal(0, await runtime.InvokeMatchAsync(first.MatchId, m => m.Network.Count));
        Assert.Equal(0, await runtime.InvokeMatchAsync(second.MatchId, m => m.Network.Count));
        using var wireA = new NetTransport(0); using var wireB = new NetTransport(0);
        using var clientA = new NetClient(wireA, endpoint, claimsA.Name, Hunter.Samus,
            claimsA.JoinNonce, issuer.Issue(claimsA), wireMatchId: placeA.WireMatchId.Value,
            admissionId: admissionA.Id, authKey: admissionA.Key, udpAuthenticationEnabled: true);
        using var clientB = new NetClient(wireB, endpoint, claimsB.Name, Hunter.Samus,
            claimsB.JoinNonce, issuer.Issue(claimsB), wireMatchId: placeB.WireMatchId.Value,
            admissionId: admissionB.Id, authKey: admissionB.Key, udpAuthenticationEnabled: true);
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
