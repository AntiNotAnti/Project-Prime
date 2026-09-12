using System.Collections.Immutable;
using System.Text.Json;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class PresenceContractTests
{
    [Fact]
    public void PresenceContractUsesBoundedPlayerFacingValues()
    {
        Assert.True(PresenceContract.IsValidDisplayName("Pilot"));
        Assert.False(PresenceContract.IsValidDisplayName(" Pilot"));
        Assert.False(PresenceContract.IsValidDisplayName(new string('x',
            PresenceContract.MaximumDisplayNameLength + 1)));
        Assert.True(PresenceContract.IsValidRegion("us-central"));
        Assert.False(PresenceContract.IsValidRegion("us\ncentral"));

        var report = new NodePresenceReport(Guid.NewGuid(), 4,
            ImmutableArray.Create(new NodePresenceEntry("Pilot",
                PlayerPresenceActivity.InLobby)));
        report.Validate();

        var page = new PresenceDirectoryPage(2, 3, 1, 0, 1,
            ImmutableArray.Create(new PublicPresenceEntry("Pilot",
                PlayerPresenceActivity.InLobby, "us-central")),
            DateTimeOffset.UnixEpoch);
        page.Validate();
    }

    [Fact]
    public void PublicPresenceSerializationContainsNoPrivateIdentityFields()
    {
        var page = new PresenceDirectoryPage(2, 3, 1, 0, 1,
            ImmutableArray.Create(new PublicPresenceEntry("Pilot",
                PlayerPresenceActivity.InLobby, "us-central")),
            DateTimeOffset.UnixEpoch);

        string json = JsonSerializer.Serialize(page);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.True(root.TryGetProperty("Revision", out _));
        Assert.True(root.TryGetProperty("TotalOnline", out _));
        Assert.True(root.TryGetProperty("VisibleOnline", out _));
        Assert.True(root.TryGetProperty("GeneratedAt", out _));
        JsonElement entry = Assert.Single(root.GetProperty("Entries").EnumerateArray().ToArray());
        Assert.Equal("Pilot", entry.GetProperty("DisplayName").GetString());
        Assert.Equal((byte)PlayerPresenceActivity.InLobby,
            entry.GetProperty("Activity").GetByte());
        Assert.Equal("us-central", entry.GetProperty("Region").GetString());
        foreach (string privateName in new[]
        {
            "AccountId", "PlayerId", "SessionId", "NodeId", "Incarnation", "LobbyId",
            "MatchId", "IpAddress", "ResumeToken", "Authorization", "DeviceIdentity"
        })
        {
            Assert.DoesNotContain(privateName, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void InvalidActivityAndNullEntriesFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new NodePresenceEntry("Pilot",
            (PlayerPresenceActivity)99).Validate());
        Assert.Throws<ArgumentException>(() => new NodePresenceReport(Guid.NewGuid(), 0,
            ImmutableArray<NodePresenceEntry>.Empty.Add(null!)).Validate());
    }
}
