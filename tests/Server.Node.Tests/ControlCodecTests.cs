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
