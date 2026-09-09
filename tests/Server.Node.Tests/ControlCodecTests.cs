using System.Text;
using System.Text.Json;
using FruityPrime.Server.Shared;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class ControlCodecTests
{
    private static byte[] Frame(string type, string payload) => Encoding.UTF8.GetBytes($$"""{"version":1,"type":"{{type}}","requestId":"{{Guid.NewGuid():D}}","payload":{{payload}}} """);
    [Fact]
    public void KnownRequestDecodesWithRequiredFields()
    { Assert.Equal(new LobbySetReady(true, 1), NodeControlCodec.Read(Frame("lobby.ready.set", "{\"ready\":true,\"expectedRevision\":1}")).Command); }
    [Fact]
    public void LobbyConfigureAcceptsOlderPayloadAndPreservesPointGoalWhenPresent()
    {
        var older = Assert.IsType<LobbyConfigure>(NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":1,\"mapKey\":\"unit\",\"mode\":\"Battle\"}")).Command);
        Assert.Null(older.PointGoal);

        var current = Assert.IsType<LobbyConfigure>(NodeControlCodec.Read(Frame("lobby.configure",
            "{\"expectedRevision\":2,\"mapKey\":\"unit\",\"mode\":\"Battle\",\"pointGoal\":12}")).Command);
        Assert.Equal(12, current.PointGoal);
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
