using System.Text;
using System.Text.Json;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class MatchTransitionContractTests
{
    private static readonly Guid Lobby = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Match = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Transition = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void CurrentEnvelopeUsesVersionFourAndFlatTransitionState()
    {
        NodeMatchTransitionVoteSnapshot state = Snapshot();
        byte[] bytes = NodeControlCodec.Write("match.transition.state", 7, null, state);
        string json = Encoding.UTF8.GetString(bytes);

        Assert.Contains($"\"version\":{NodeControlCodec.Version}", json);
        Assert.Contains("\"proposerName\":\"Owner\"", json);
        Assert.Contains("\"yes\":1", json);
        Assert.Contains("\"no\":0", json);
        Assert.Contains("\"eligible\":3", json);
        Assert.Contains("\"needed\":3", json);
        Assert.Contains("\"ownVote\":true", json);
        Assert.Contains("\"state\":\"Pending\"", json);
        Assert.Contains("\"targetMapKey\"", json);
        Assert.Contains("\"proposerSessionId\"", json);

        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.Equal(NodeControlCodec.Version, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void ProposeAndVoteUseCanonicalMapKeyAndAcceptProperties()
    {
        Guid request = Guid.NewGuid();
        byte[] propose = Encoding.UTF8.GetBytes($"{{\"version\":{NodeControlCodec.Version},\"type\":\"match.transition.propose\",\"requestId\":\""
            + request.ToString("D") + "\",\"payload\":{\"expectedRevision\":4,\"matchId\":\""
            + Match.ToString("D") + "\",\"choice\":\"ChangeMap\",\"mapKey\":\"unit2\"}}");
        byte[] vote = Encoding.UTF8.GetBytes($"{{\"version\":{NodeControlCodec.Version},\"type\":\"match.transition.vote\",\"requestId\":\""
            + request.ToString("D") + "\",\"payload\":{\"expectedRevision\":5,\"matchId\":\""
            + Match.ToString("D") + "\",\"ballotRevision\":2,\"accept\":true}}");

        var proposal = Assert.IsType<LobbyMatchTransitionPropose>(NodeControlCodec.Read(propose).Command);
        var response = Assert.IsType<LobbyMatchTransitionVote>(NodeControlCodec.Read(vote).Command);
        Assert.Equal("unit2", proposal.MapKey);
        Assert.True(response.Accept);
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Encoding.UTF8.GetBytes($"{{\"version\":{NodeControlCodec.Version},\"type\":\"match.transition.vote\",\"requestId\":\""
            + request.ToString("D") + "\",\"payload\":{\"expectedRevision\":5,\"matchId\":\""
            + Match.ToString("D") + "\",\"ballotRevision\":2,\"approve\":true}}")));
    }

    [Fact]
    public void TransitionValidationRejectsIdentityCountDeadlineAndMapViolations()
    {
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("match.transition.state", 1, null,
            Snapshot() with { TransitionId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("match.transition.state", 1, null,
            Snapshot() with { Needed = 4 }));
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("match.transition.state", 1, null,
            Snapshot() with { Deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)) }));
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("match.transition.started", 1, null,
            new NodeMatchTransitionStarted(Lobby, Match, Transition, MatchTransitionChoice.ChangeMap, "", MatchMode.Battle)));
        string invalidRestart = Guid.NewGuid().ToString("D");
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Encoding.UTF8.GetBytes($"{{\"version\":{NodeControlCodec.Version},\"type\":\"match.transition.propose\",\"requestId\":\""
            + invalidRestart + "\",\"payload\":{\"expectedRevision\":4,\"matchId\":\""
            + Match.ToString("D") + "\",\"choice\":\"Restart\",\"mapKey\":\"unit2\"}}")));
    }

    [Fact]
    public void StartedUsesPreviousMatchIdAndDoesNotCarryReplacementIdentity()
    {
        var started = new NodeMatchTransitionStarted(Lobby, Match, Transition,
            MatchTransitionChoice.Restart, "unit", MatchMode.Battle);
        string json = Encoding.UTF8.GetString(NodeControlCodec.Write(
            "match.transition.started", 1, null, started));
        Assert.Contains("\"previousMatchId\"", json);
        Assert.Contains("\"targetMapKey\":\"unit\"", json);
        Assert.DoesNotContain("\"matchId\"", json);
        Assert.DoesNotContain("\"newMatchId\"", json);
        Assert.DoesNotContain("\"replacementMatchId\"", json);
    }

    private static NodeMatchTransitionVoteSnapshot Snapshot()
        => new(Lobby, Match, Transition, 1,
            Guid.Parse("44444444-4444-4444-4444-444444444444"), "Owner", MatchTransitionChoice.Restart,
            "unit", MatchMode.Battle, 3, 1, 0, 3,
            DateTimeOffset.UtcNow.AddSeconds(30), MatchTransitionVoteState.Pending, true);
}
