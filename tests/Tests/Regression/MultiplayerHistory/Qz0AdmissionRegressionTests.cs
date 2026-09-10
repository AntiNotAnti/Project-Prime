using System;
using System.Collections.Immutable;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;
using SharedBotFillPolicy = ProjectPrime.Server.Shared.BotFillPolicy;

namespace MphRead.Tests;

/// <summary>QZ0 translated admission-ticket boundary regressions.</summary>
public sealed class Qz0AdmissionRegressionTests
{
    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorkerAdmissionTicketCannotReplayOrCrossWireMatchBoundaries()
    {
        // Prime invariant: a reconnect ticket is one-time, match-bound and
        // WireMatchId-bound; it cannot migrate a player into another match.
        long now = 1_000_000;
        MatchId matchId = new(Id("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        PlayerId playerId = new(Id("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var spec = new MatchSpec(matchId,
            new LobbyId(Id("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            new NodeId(Id("dddddddd-dddd-dddd-dddd-dddddddddddd")),
            Id("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1),
            new("MP1 SANCTORUS", "hash", "AMHE1", "test", NetHeader.Version),
            MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(0, playerId, null, "Player", Hunter.Samus, 0, SeatRole.Player, false)),
            SharedBotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var placement = new MatchPlacement(matchId, new WireMatchId(70001),
            new WorkerId(Id("ffffffff-ffff-ffff-ffff-ffffffffffff")),
            Id("11111111-2222-3333-4444-555555555555"), "127.0.0.1", 50001);
        var claims = new WorkerAdmissionClaims(spec.NodeId, spec.NodeIncarnation,
            placement.WorkerId, placement.WorkerIncarnation, spec.LobbyId, spec.MatchId,
            placement.WireMatchId, Id("66666666-6666-6666-6666-666666666666"), playerId, null,
            SeatRole.Player, 0, "Player", 77, now, now + 60,
            Id("77777777-7777-7777-7777-777777777777"));

        using var issuer = new WorkerAdmissionIssuer("qz0-admission");
        using var verifier = new WorkerAdmissionVerifier(spec, placement);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey(), DateTimeOffset.FromUnixTimeSeconds(now));
        string ticket = issuer.Issue(claims);
        var join = new JoinPacket(NetHeader.Version, claims.JoinNonce, Hunter.Samus, "Player",
            Ticket: ticket, WireMatchId: placement.WireMatchId.Value);

        Assert.True(verifier.TryConsume(ticket, join, DateTimeOffset.FromUnixTimeSeconds(now + 1), out _));
        string crossWireTicket = issuer.Issue(claims with
        {
            JoinNonce = 78,
            TicketId = Id("88888888-8888-8888-8888-888888888888")
        });
        var crossWireJoin = join with
        {
            Nonce = 78,
            Ticket = crossWireTicket,
            WireMatchId = placement.WireMatchId.Value + 1
        };
        Assert.False(verifier.TryConsume(crossWireTicket, crossWireJoin,
            DateTimeOffset.FromUnixTimeSeconds(now + 2), out _));
        Assert.False(verifier.TryConsume(ticket, join, DateTimeOffset.FromUnixTimeSeconds(now + 2), out _));
    }

    private static Guid Id(string value) => Guid.Parse(value);
}
