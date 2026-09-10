using System.Collections.Immutable;
using System.Security.Cryptography;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class WorkerAdmissionTests
{
    private static (MatchSpec Spec, MatchPlacement Placement, WorkerAdmissionClaims Claims, JoinPacket Join) Fixture(bool guest = false)
    {
        var node = new NodeId(Guid.NewGuid()); var worker = new WorkerId(Guid.NewGuid()); var match = new MatchId(Guid.NewGuid());
        var lobby = new LobbyId(Guid.NewGuid()); Guid incarnation = Guid.NewGuid();
        PlayerId? player = guest ? null : new PlayerId(Guid.NewGuid()); Guid? guestId = guest ? Guid.NewGuid() : null;
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var spec = new MatchSpec(match, lobby, node, incarnation, rules,
            new("MP1 SANCTORUS", "hash", "AMHE1", "test", NetHeader.Version), MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(3, player, guestId, "ADMISSION", Hunter.Samus, 0, SeatRole.Player, false)),
            BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var placement = new MatchPlacement(match, new(77), worker, Guid.NewGuid(), "127.0.0.1", 27000);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new WorkerAdmissionClaims(node, incarnation, worker, placement.WorkerIncarnation, lobby, match,
            placement.WireMatchId, Guid.NewGuid(), player, guestId, SeatRole.Player, 3, "ADMISSION", 123, now, now + 60, Guid.NewGuid());
        return (spec, placement, claims, new(NetHeader.Version, 123, Hunter.Samus, "ADMISSION", WireMatchId: 77));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedAccountAndGuestReservationsAreSingleUse(bool guest)
    {
        var f = Fixture(guest);
        using var issuer = new WorkerAdmissionIssuer("node-1");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey());
        string ticket = issuer.Issue(f.Claims);
        Assert.True(verifier.TryConsume(ticket, f.Join, DateTimeOffset.UtcNow, out var claims));
        Assert.Equal(f.Claims, claims);
        Assert.False(verifier.TryConsume(ticket, f.Join, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public async Task ConcurrentReplayConsumesExactlyOnce()
    {
        var f = Fixture();
        using var issuer = new WorkerAdmissionIssuer("node-1");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey());
        string ticket = issuer.Issue(f.Claims);
        bool[] accepted = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            verifier.TryConsume(ticket, f.Join, DateTimeOffset.UtcNow, out _))));
        Assert.Equal(1, accepted.Count(value => value));
    }

    [Fact]
    public void RejectsEveryCrossScopeRoleIdentityAndJoinMismatchWithoutSpendingValidTicket()
    {
        var f = Fixture();
        using var issuer = new WorkerAdmissionIssuer("node-1");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey());
        WorkerAdmissionClaims[] mismatches =
        [
            f.Claims with { NodeId = new(Guid.NewGuid()) }, f.Claims with { NodeIncarnation = Guid.NewGuid() },
            f.Claims with { WorkerId = new(Guid.NewGuid()) }, f.Claims with { WorkerIncarnation = Guid.NewGuid() },
            f.Claims with { LobbyId = new(Guid.NewGuid()) }, f.Claims with { MatchId = new(Guid.NewGuid()) },
            f.Claims with { WireMatchId = new(78) }, f.Claims with { PlayerId = new PlayerId(Guid.NewGuid()) },
            f.Claims with { PlayerId = null, GuestSessionId = Guid.NewGuid() }, f.Claims with { Role = SeatRole.Observer },
            f.Claims with { SeatId = 2 }, f.Claims with { Name = "OTHER" }, f.Claims with { JoinNonce = 456 },
            f.Claims with { IssuedAt = f.Claims.IssuedAt + 30, ExpiresAt = f.Claims.ExpiresAt + 30 },
            f.Claims with { IssuedAt = f.Claims.IssuedAt - 120, ExpiresAt = f.Claims.IssuedAt - 1 }
        ];
        foreach (var invalid in mismatches)
            Assert.False(verifier.TryConsume(issuer.Issue(invalid), f.Join, DateTimeOffset.UtcNow, out _));
        string good = issuer.Issue(f.Claims);
        foreach (JoinPacket wrong in new[] { f.Join with { WireMatchId = 0 }, f.Join with { WireMatchId = 78 },
            f.Join with { Hunter = Hunter.Kanden }, f.Join with { Observer = true }, f.Join with { Protocol = 8 } })
            Assert.False(verifier.TryConsume(good, wrong, DateTimeOffset.UtcNow, out _));
        Assert.True(verifier.TryConsume(good, f.Join, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void MalformedWrongKeyAndTamperedTokensFailClosed()
    {
        var f = Fixture();
        using var issuer = new WorkerAdmissionIssuer("node-1");
        using var wrong = new WorkerAdmissionIssuer("node-1");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey());
        string good = issuer.Issue(f.Claims);
        string[] parts = good.Split('.');
        foreach (string bad in new[] { "", "A.B.C", good + "x", good[..^2], wrong.Issue(f.Claims),
            parts[0] + "." + parts[1][..^1] + (parts[1][^1] == 'A' ? "B" : "A") + "." + parts[2],
            new string('A', JoinPacket.MaxRoutedTicketBytes + 1) })
            Assert.False(verifier.TryConsume(bad, f.Join, DateTimeOffset.UtcNow, out _));
        Assert.True(verifier.TryConsume(good, f.Join, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void ReplayStoreBoundsFailClosedAndExpiredEntriesAreReclaimed()
    {
        var f = Fixture();
        using var issuer = new WorkerAdmissionIssuer("node-1");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement, replayCapacity: 1);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey());
        Assert.True(verifier.TryConsume(issuer.Issue(f.Claims), f.Join, DateTimeOffset.UtcNow, out _));
        Assert.False(verifier.TryConsume(issuer.Issue(f.Claims with { TicketId = Guid.NewGuid() }), f.Join, DateTimeOffset.UtcNow, out _));
        long later = f.Claims.ExpiresAt + 1;
        var next = f.Claims with { TicketId = Guid.NewGuid(), IssuedAt = later, ExpiresAt = later + 60 };
        Assert.True(verifier.TryConsume(issuer.Issue(next), f.Join, DateTimeOffset.FromUnixTimeSeconds(later), out _));
    }

    [Fact]
    public void KeyRotationOverlapsAndExplicitRetirementRevokesOldSignatures()
    {
        var f = Fixture();
        using var first = new WorkerAdmissionIssuer("old");
        using var second = new WorkerAdmissionIssuer("new");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement);
        verifier.UpdatePublicKey(first.KeyId, first.ExportPublicKey());
        verifier.UpdatePublicKey(second.KeyId, second.ExportPublicKey());
        Assert.True(verifier.TryConsume(first.Issue(f.Claims), f.Join, DateTimeOffset.UtcNow, out _));
        Assert.True(verifier.TryConsume(second.Issue(f.Claims with { TicketId = Guid.NewGuid() }), f.Join, DateTimeOffset.UtcNow, out _));
        Assert.True(verifier.RetirePublicKey(first.KeyId));
        Assert.False(verifier.TryConsume(first.Issue(f.Claims with { TicketId = Guid.NewGuid() }), f.Join, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void RotationOverlapExpiresAndLaterRotationsCannotExtendAnOldKey()
    {
        var f = Fixture();
        using var old = new WorkerAdmissionIssuer("old");
        using var current = new WorkerAdmissionIssuer("current");
        using var latest = new WorkerAdmissionIssuer("latest");
        using var verifier = new WorkerAdmissionVerifier(f.Spec, f.Placement);
        var now = DateTimeOffset.FromUnixTimeSeconds(f.Claims.IssuedAt);
        verifier.UpdatePublicKey(old.KeyId, old.ExportPublicKey(), now);
        verifier.UpdatePublicKey(current.KeyId, current.ExportPublicKey(), now);
        verifier.UpdatePublicKey(latest.KeyId, latest.ExportPublicKey(), now.AddSeconds(100));
        var fresh = f.Claims with { TicketId = Guid.NewGuid(), IssuedAt = f.Claims.IssuedAt + 126, ExpiresAt = f.Claims.IssuedAt + 180 };
        Assert.False(verifier.TryConsume(old.Issue(fresh), f.Join, now.AddSeconds(126), out _));
        Assert.True(verifier.TryConsume(current.Issue(fresh), f.Join, now.AddSeconds(126), out _));
        // Retired entries are reclaimed; rotations cannot grow the key store indefinitely.
        for (int index = 0; index < 40; index++)
            verifier.UpdatePublicKey("key-" + index, latest.ExportPublicKey(), now.AddSeconds(300 + index * 126));
    }

    [Fact]
    public void RoutedJoinIsBoundedStrictAndLegacyLayoutRemainsExplicit()
    {
        var f = Fixture();
        string ticket = new string('A', JoinPacket.MaxRoutedTicketBytes - 4) + ".B.C";
        JoinPacket routed = f.Join with { Ticket = ticket, Observer = true };
        byte[] bytes = new byte[routed.EncodedSize]; routed.Write(bytes);
        Assert.Equal(NetConfig.MaxPacketSize, bytes.Length + NetHeader.Size);
        Assert.True(JoinPacket.TryRead(bytes, out var parsed)); Assert.Equal(routed, parsed);
        Assert.False(JoinPacket.TryRead(bytes.AsSpan(0, bytes.Length - 1), out _));
        byte flags = bytes[JoinPacket.Size];
        bytes[JoinPacket.Size] = 4; Assert.False(JoinPacket.TryRead(bytes, out _)); bytes[JoinPacket.Size] = flags;
        bytes.AsSpan(JoinPacket.Size + 3, 4).Clear(); Assert.False(JoinPacket.TryRead(bytes, out _));
        Assert.Throws<ArgumentException>(() => (routed with { Ticket = ticket + "A" }).Write(new byte[1024]));
        JoinPacket legacy = f.Join with { WireMatchId = 0 };
        bytes = new byte[legacy.EncodedSize]; legacy.Write(bytes);
        Assert.Equal(JoinPacket.Size, bytes.Length);
        Assert.True(JoinPacket.TryRead(bytes, out parsed)); Assert.Equal(0u, parsed.WireMatchId);
        JoinPacket nullCredential = f.Join with { Ticket = null! };
        bytes = new byte[nullCredential.EncodedSize]; nullCredential.Write(bytes);
        Assert.True(JoinPacket.TryRead(bytes, out parsed)); Assert.Equal("", parsed.Ticket);
    }

    [Fact]
    public void LargestSignedClaimsAndRoutedJoinRemainWithinDatagramCap()
    {
        var f = Fixture();
        using var issuer = new WorkerAdmissionIssuer(new string('k', 32));
        string ticket = issuer.Issue(f.Claims with { Name = new string('<', 16), JoinNonce = ulong.MaxValue });
        var join = f.Join with { Name = new string('<', 16), Nonce = ulong.MaxValue, Ticket = ticket };
        Assert.True(ticket.Length <= JoinPacket.MaxRoutedTicketBytes);
        Assert.True(NetHeader.Size + join.EncodedSize <= NetConfig.MaxPacketSize);
        byte[] bytes = new byte[join.EncodedSize]; join.Write(bytes);
        Assert.True(JoinPacket.TryRead(bytes, out var parsed)); Assert.Equal(join, parsed);
    }
}
