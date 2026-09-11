using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeRequestIdTests
{
    [Fact]
    public void ResolveAcceptsOnlyCanonicalNonEmptyGuid()
    {
        Guid id = Guid.NewGuid();
        Assert.Equal(id.ToString("D"), NodeRequestId.Resolve(id.ToString("D")));
        Assert.NotEqual("not-a-guid", NodeRequestId.Resolve("not-a-guid"));
        Assert.NotEqual(Guid.Empty.ToString("D"), NodeRequestId.Resolve(Guid.Empty.ToString("D")));
    }

    [Fact]
    public async Task EarlyControlRejectStillCarriesCorrelationHeader()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(host.App.Urls.Single().Replace("http:", "https:", StringComparison.Ordinal))
        };
        string supplied = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/control");
        request.Headers.Add(NodeRequestId.Header, supplied);
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(supplied, response.Headers.GetValues(NodeRequestId.Header).Single());
    }
}
