using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using Xunit;

namespace MphRead.Tests;

public sealed class NodeAccountSessionTests
{
    [Theory]
    [InlineData("wss://node.example/control", true)]
    [InlineData("ws://node.example/control", false)]
    [InlineData("wss://user:password@node.example/control", false)]
    [InlineData("wss://node.example/control?ticket=secret", false)]
    public async Task DirectoryPinsCompatibilityAndSecureControlEndpoint(string endpoint, bool valid)
    {
        var node = new NodeListing(Guid.NewGuid(), "Node", "us", endpoint, 9, "build", new string('a', 64), 10, 1, 1, 0, "community", DateTimeOffset.UtcNow);
        using var session = new AccountSession(new Uri("https://backend.example/"), new Handler(node));
        if (valid) Assert.Single(await session.GetNodesAsync(9, "build", new string('a', 64)));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetNodesAsync(9, "build", new string('a', 64)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetNodesAsync(8, "build", new string('a', 64)));
    }
    private sealed class Handler(NodeListing listing) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("backend.example", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { listing }) });
        }
    }
}
