using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MphRead.Backend.Nodes;
using MphRead.Backend.Presence;
using MphRead.Backend.Tickets;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class PresenceDirectoryTests
{
    private const string Secret = "presence-test-node-secret-at-least-thirty-two";

    [Fact]
    public void EmptyDirectoryReportsFreshZeroPopulation()
    {
        var clock = new Clock();
        PresenceDirectory presence = CreateFixture(clock).Presence;

        PresenceDirectoryPage page = presence.BrowsePage();

        Assert.Equal(0, page.TotalOnline);
        Assert.Equal(0, page.VisibleOnline);
        Assert.Empty(page.Entries);
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public void TotalComesFromNodePopulationEvenWhenLowerThanVisibleNames()
    {
        var clock = new Clock();
        PresenceFixture fixture = CreateFixture(clock);
        PresenceDirectory presence = fixture.Presence;
        NodeDirectory nodes = fixture.Nodes;
        Guid nodeId = fixture.NodeIds[0];
        NodeRegistration registration = Registration(nodeId, Guid.NewGuid(), "us-central", 8);
        AddNode(nodes, nodeId, registration);
        Assert.True(nodes.Heartbeat(nodeId, Secret,
            new NodeHeartbeat(registration.Incarnation, 0, 0, 0)));

        presence.Report(nodeId, Report(registration.Incarnation, 1,
            new NodePresenceEntry("Pilot", PlayerPresenceActivity.Online)));
        PresenceDirectoryPage page = presence.BrowsePage();

        Assert.Equal(0, page.TotalOnline);
        Assert.Equal(1, page.VisibleOnline);
        Assert.Equal("Pilot", Assert.Single(page.Entries).DisplayName);
    }

    [Fact]
    public void EqualRevisionIdenticalPayloadRefreshesTtlWithoutRevisionChurn()
    {
        var clock = new Clock();
        PresenceFixture fixture = CreateFixture(clock);
        PresenceDirectory presence = fixture.Presence;
        NodeDirectory nodes = fixture.Nodes;
        Guid nodeId = fixture.NodeIds[0];
        NodeRegistration registration = Registration(nodeId, Guid.NewGuid(), "us", 8);
        AddNode(nodes, nodeId, registration);
        NodePresenceReport report = Report(registration.Incarnation, 3,
            new NodePresenceEntry("Pilot", PlayerPresenceActivity.Online));
        presence.Report(nodeId, report);
        long revision = presence.BrowsePage().Revision;

        clock.Now += TimeSpan.FromSeconds(40);
        presence.Report(nodeId, report);
        PresenceDirectoryPage refreshed = presence.BrowsePage(revision: revision);

        Assert.Equal(revision, refreshed.Revision);
        Assert.Single(refreshed.Entries);

        clock.Now += TimeSpan.FromSeconds(46);
        Assert.Empty(presence.BrowsePage().Entries);
        Assert.True(presence.Revision > revision);
    }

    [Fact]
    public void LowerOrEqualChangedRevisionIsRejectedAndDoesNotReplacePayload()
    {
        var clock = new Clock();
        PresenceFixture fixture = CreateFixture(clock);
        PresenceDirectory presence = fixture.Presence;
        NodeDirectory nodes = fixture.Nodes;
        Guid nodeId = fixture.NodeIds[0];
        NodeRegistration registration = Registration(nodeId, Guid.NewGuid(), "us", 8);
        AddNode(nodes, nodeId, registration);
        presence.Report(nodeId, Report(registration.Incarnation, 5,
            new NodePresenceEntry("Pilot", PlayerPresenceActivity.Online)));

        PresenceDirectoryException lower = Assert.Throws<PresenceDirectoryException>(() =>
            presence.Report(nodeId, Report(registration.Incarnation, 4,
                new NodePresenceEntry("Lower", PlayerPresenceActivity.InLobby))));
        Assert.Equal("stale_revision", lower.Code);
        PresenceDirectoryException changed = Assert.Throws<PresenceDirectoryException>(() =>
            presence.Report(nodeId, Report(registration.Incarnation, 5,
                new NodePresenceEntry("Changed", PlayerPresenceActivity.InLobby))));
        Assert.Equal("stale_revision", changed.Code);

        Assert.Equal("Pilot", Assert.Single(presence.BrowsePage().Entries).DisplayName);
    }

    [Fact]
    public void ReplacementDeregisterAndTtlRemoveVisibleNames()
    {
        var clock = new Clock();
        PresenceFixture fixture = CreateFixture(clock);
        PresenceDirectory presence = fixture.Presence;
        NodeDirectory nodes = fixture.Nodes;
        Guid nodeId = fixture.NodeIds[0];
        NodeRegistration first = Registration(nodeId, Guid.NewGuid(), "us", 8);
        AddNode(nodes, nodeId, first);
        presence.Report(nodeId, Report(first.Incarnation, 1,
            new NodePresenceEntry("Old", PlayerPresenceActivity.InLobby)));
        Assert.Single(presence.BrowsePage().Entries);

        NodeRegistration replacement = Registration(nodeId, Guid.NewGuid(), "eu", 8);
        AddNode(nodes, nodeId, replacement);
        Assert.Empty(presence.BrowsePage().Entries);
        Assert.Throws<PresenceDirectoryException>(() => presence.Report(nodeId,
            Report(first.Incarnation, 2,
                new NodePresenceEntry("Replay", PlayerPresenceActivity.Online))));

        presence.Report(nodeId, Report(replacement.Incarnation, 1,
            new NodePresenceEntry("New", PlayerPresenceActivity.Online)));
        Assert.True(nodes.Deregister(nodeId, Secret, replacement.Incarnation));
        Assert.Empty(presence.BrowsePage().Entries);

        NodeRegistration ttl = Registration(nodeId, Guid.NewGuid(), "us", 8);
        AddNode(nodes, nodeId, ttl);
        presence.Report(nodeId, Report(ttl.Incarnation, 1,
            new NodePresenceEntry("Expires", PlayerPresenceActivity.Online)));
        clock.Now += PresenceDirectory.ReportLifetime;
        Assert.Empty(presence.BrowsePage().Entries);
    }

    [Fact]
    public void DuplicateNamesAreValidAndOrderingAndPagingAreDeterministic()
    {
        var clock = new Clock();
        PresenceFixture fixture = CreateFixture(clock, additionalNodes: 1);
        PresenceDirectory presence = fixture.Presence;
        NodeDirectory nodes = fixture.Nodes;
        Guid nodeId = fixture.NodeIds[0];
        NodeRegistration registration = Registration(nodeId, Guid.NewGuid(), "us", 100);
        AddNode(nodes, nodeId, registration);
        presence.Report(nodeId, Report(registration.Incarnation, 1,
            new NodePresenceEntry("Zulu", PlayerPresenceActivity.InMatch),
            new NodePresenceEntry("Alpha", PlayerPresenceActivity.Online),
            new NodePresenceEntry("Alpha", PlayerPresenceActivity.InMatch),
            new NodePresenceEntry("Bravo", PlayerPresenceActivity.InLobby)));

        PresenceDirectoryPage first = presence.BrowsePage();
        Assert.Equal(new[] { "Alpha", "Bravo", "Alpha", "Zulu" },
            first.Entries.Select(entry => entry.DisplayName));
        Assert.Equal(new[] { PlayerPresenceActivity.Online, PlayerPresenceActivity.InLobby,
            PlayerPresenceActivity.InMatch, PlayerPresenceActivity.InMatch },
            first.Entries.Select(entry => entry.Activity));

        Guid manyNodeId = fixture.NodeIds[1];
        NodeRegistration many = Registration(manyNodeId, Guid.NewGuid(), "eu", 100);
        AddNode(nodes, manyNodeId, many);
        var players = Enumerable.Range(0, PresenceContract.PageSize + 1)
            .Select(index => new NodePresenceEntry($"P{index:000}", PlayerPresenceActivity.Online))
            .ToImmutableArray();
        presence.Report(manyNodeId, Report(many.Incarnation, 1, players.ToArray()));
        PresenceDirectoryPage page0 = presence.BrowsePage();
        Assert.Equal(2, page0.PageCount);
        PresenceDirectoryPage page1 = presence.BrowsePage(page: 1, revision: page0.Revision);
        Assert.Equal(page0.Revision, page1.Revision);
        Assert.Equal(5, page1.Entries.Length);
        Assert.Equal("P049", page1.Entries[0].DisplayName);
        Assert.Equal("P050", page1.Entries[1].DisplayName);
        Assert.Throws<PresenceDirectoryPageException>(() => presence.BrowsePage(
            page: 1, revision: page0.Revision + 1));
        Assert.Throws<PresenceDirectoryPageException>(() => presence.BrowsePage(
            page: PresenceContract.MaximumPages));
    }

    [Fact]
    public void ReportMustFitCurrentNodeCapacityAndRejectMalformedData()
    {
        var clock = new Clock();
        PresenceFixture fixture = CreateFixture(clock);
        PresenceDirectory presence = fixture.Presence;
        NodeDirectory nodes = fixture.Nodes;
        Guid nodeId = fixture.NodeIds[0];
        NodeRegistration registration = Registration(nodeId, Guid.NewGuid(), "us", 1);
        AddNode(nodes, nodeId, registration);

        PresenceDirectoryException capacity = Assert.Throws<PresenceDirectoryException>(() =>
            presence.Report(nodeId, Report(registration.Incarnation, 1,
                new NodePresenceEntry("One", PlayerPresenceActivity.Online),
                new NodePresenceEntry("Two", PlayerPresenceActivity.Online))));
        Assert.Equal("presence_capacity_exceeded", capacity.Code);
        Assert.Throws<PresenceDirectoryException>(() => presence.Report(nodeId,
            new NodePresenceReport(registration.Incarnation, 1,
                ImmutableArray.Create(new NodePresenceEntry(" bad", PlayerPresenceActivity.Online)))));
    }

    private sealed record PresenceFixture(PresenceDirectory Presence, NodeDirectory Nodes,
        Guid[] NodeIds);

    private static PresenceFixture CreateFixture(Clock clock, int additionalNodes = 0)
    {
        Guid[] nodeIds = Enumerable.Range(0, additionalNodes + 1)
            .Select(_ => Guid.NewGuid()).ToArray();
        NodeDirectory nodes = new(new GameServerRegistry(Options.Create(new GameServerOptions
        {
            Servers = nodeIds.Select(nodeId => new GameServerRegistration
            {
                Id = nodeId, Enabled = true,
                ApiKeySha256 = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(Secret)))
            }).ToList()
        }), new TestEnvironment()), clock);
        return new PresenceFixture(new PresenceDirectory(nodes, clock), nodes, nodeIds);
    }

    private static void AddNode(NodeDirectory nodes, Guid nodeId, NodeRegistration registration)
    {
        Assert.True(nodes.Register(nodeId, Secret, registration));
    }

    private static NodeRegistration Registration(Guid nodeId, Guid incarnation,
        string region, int capacity)
        => new(incarnation, $"Node-{nodeId:N}"[..Math.Min(16, 5 + nodeId.ToString("N").Length)],
            region, "wss://node.example/v1/control", 1, "build", new string('a', 64), capacity);

    private static NodePresenceReport Report(Guid incarnation, long revision,
        params NodePresenceEntry[] players)
        => new(incarnation, revision, players.ToImmutableArray());

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
