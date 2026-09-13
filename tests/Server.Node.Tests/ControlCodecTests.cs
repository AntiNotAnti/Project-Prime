using System.Text;
using System.Text.Json;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class ControlCodecTests
{
    private static byte[] Frame(string type, string payload, int version = NodeControlCodec.Version)
        => Encoding.UTF8.GetBytes($$"""{"version":{{version}},"type":"{{type}}","requestId":"{{Guid.NewGuid():D}}","payload":{{payload}}} """);
    [Fact]
    public void KnownRequestDecodesWithRequiredFields()
    { Assert.Equal(new LobbySetReady(true, 1), NodeControlCodec.Read(Frame("lobby.ready.set", "{\"ready\":true,\"expectedRevision\":1}")).Command); }
    [Fact]
    public void QuickPlayRequestIsStrictAndSupportsBoundedPreferences()
    {
        Assert.Equal(new QuickPlayJoin(MatchMode.Survival, false), NodeControlCodec.Read(Frame("quickplay.join",
            "{\"mode\":\"Survival\",\"allowBots\":false}")).Command);
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("quickplay.join", "{\"observer\":true}")));
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("quickplay.join", "{\"mode\":\"FutureMode\"}")));
    }
    [Fact]
    public void LobbyConfigureAcceptsOlderPayloadAndPreservesPointGoalWhenPresent()
    {
        var older = Assert.IsType<LobbyConfigure>(NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":1,\"mapKey\":\"unit\",\"mode\":\"Battle\"}")).Command);
        Assert.Null(older.PointGoal);
        Assert.Equal(BotDifficulty.Normal, older.BotDifficulty);

        var current = Assert.IsType<LobbyConfigure>(NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":2,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"pointGoal\":12}")).Command);
        Assert.Equal(12, current.PointGoal);
    }

    [Fact]
    public void LobbyConfigureCarriesFiveLevelBotDifficultyAndRejectsUnknownValues()
    {
        var expert = Assert.IsType<LobbyConfigure>(NodeControlCodec.Read(Frame(
            "lobby.configure",
            "{\"expectedRevision\":1,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"botCount\":2,\"botDifficulty\":\"Expert\"}"))
            .Command);
        Assert.Equal(BotDifficulty.Expert, expert.BotDifficulty);

        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame(
            "lobby.configure",
            "{\"expectedRevision\":1,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"botCount\":2,\"botDifficulty\":9}")));
    }
    [Fact]
    public void LobbyConfigureReadsStructuredRulesWhileMissingRulesRemainsCompatible()
    {
        NodeControlRequest currentRequest = NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":2,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"rules\":{\"timeLimitSeconds\":600,\"scoreGoal\":12,\"damageLevel\":2,\"killcamPolicy\":\"PostRound\"}}"));
        var current = Assert.IsType<LobbyConfigure>(currentRequest.Command);
        Assert.Equal(new LobbyRulesOptions(TimeLimitSeconds: 600, ScoreGoal: 12, DamageLevel: 2,
            KillcamPolicy: KillcamPolicy.PostRound), current.Rules);

        NodeControlRequest olderRequest = NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":3,\"mapKey\":\"unit\",\"mode\":\"Battle\"}"));
        var older = Assert.IsType<LobbyConfigure>(olderRequest.Command);
        Assert.Null(older.Rules);
    }

    [Fact]
    public void LobbyConfigureWritesKillcamPolicyOnlyWhenHostSelectedIt()
    {
        byte[] unspecified = NodeControlCodec.Write("lobby.configure", 1, null,
            new LobbyConfigure(1, "unit", MatchMode.Battle,
                new LobbyRulesOptions(TimeLimitSeconds: 600, ScoreGoal: 7)));
        byte[] selected = NodeControlCodec.Write("lobby.configure", 2, null,
            new LobbyConfigure(1, "unit", MatchMode.Battle,
                new LobbyRulesOptions(TimeLimitSeconds: 600, ScoreGoal: 7,
                    KillcamPolicy: KillcamPolicy.Disabled)));

        Assert.DoesNotContain("killcamPolicy", Encoding.UTF8.GetString(unspecified));
        Assert.Contains("\"killcamPolicy\":\"Disabled\"", Encoding.UTF8.GetString(selected));
    }
    [Fact]
    public void LegacyConfigureWriteOmitsAbsentRulesForOlderNodes()
    {
        byte[] bytes = NodeControlCodec.Write("lobby.configure", 1, null,
            new LobbyConfigure(1, "unit", MatchMode.Battle));
        Assert.DoesNotContain("\"rules\"", Encoding.UTF8.GetString(bytes));
    }
    [Fact]
    public void AdvancedFieldsDoNotFallThroughAsUnknownLegacyProperties()
    {
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":1,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"damageLevel\":2}")));
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":1,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"rules\":{\"futureRule\":true}}")));
    }
    [Fact]
    public void LegacyEnvelopeIsRejectedBeforePayloadDecoding()
    {
        var error = Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("lobby.configure",
            "{\"futurePayload\":true}", version: 1)));
        Assert.Equal("Unsupported control envelope version. Received 1; expected 3.", error.Message);
    }
    [Fact]
    public void CurrentEnvelopeWritesAndReadsAtVersionThree()
    {
        byte[] bytes = NodeControlCodec.Write("node.ping", 1, null, new NodePing());
        using JsonDocument eventDocument = JsonDocument.Parse(bytes);
        Assert.Equal(NodeControlCodec.Version, eventDocument.RootElement.GetProperty("version").GetInt32());
        Assert.IsType<NodePing>(NodeControlCodec.Read(Frame("node.ping", "{}")).Command);
    }
    [Theory]
    [InlineData("{\"ready\":true,\"ready\":false}")]
    [InlineData("{\"ready\":true,\"admin\":true}")]
    [InlineData("{}")]
    public void AmbiguousOrUnknownOrMissingPropertiesAreRejected(string payload)
    { Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("lobby.ready.set", payload))); }
    [Fact]
    public void UnknownVersionTypeAndOversizeAreRejected()
    {
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Frame("future.command", "{}")));
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(new byte[NodeControlCodec.MaximumFrameBytes + 1]));
    }
}
