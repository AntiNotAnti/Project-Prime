using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Admin;
using Xunit;

namespace MphRead.Tests.Admin;

public sealed class AdminCommandTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void CommandsAreOwnerExecutedBoundedIdempotentAndExpireWithoutMutation()
    {
        var clock = new Clock(); var queue = new AdminCommandQueue(clock); int applied = 0;
        var command = new AdminCommand(Guid.NewGuid(), AdminCommandKind.LockRoster, clock.Now);
        Assert.Equal("queued", queue.Submit(command).State); Assert.Equal(0, applied);
        Assert.Equal("conflict", queue.Submit(command with { Kind = AdminCommandKind.UnlockRoster }).State);
        queue.Drain(7, c => { applied++; return new(c.RequestId, "applied", "locked"); });
        Assert.Equal(1, applied); Assert.Equal((uint)7, queue.Submit(command).AppliedTick);
        queue.Drain(8, _ => throw new InvalidOperationException()); Assert.Equal(1, applied);
        AdminCommand? last = null;
        for (int i = 0; i < 32; i++) Assert.Equal("queued", queue.Submit(last = command with { RequestId = Guid.NewGuid() }).State);
        Assert.Equal("unavailable", queue.Submit(command with { RequestId = Guid.NewGuid() }).State);
        clock.Now += TimeSpan.FromSeconds(31);
        for (int i = 0; i < 8; i++) queue.Drain(9, _ => throw new Exception("Expired command executed"));
        Assert.Equal("expired", queue.Find(last!.RequestId)!.State);
        Assert.Equal("expired", queue.Submit(command with { RequestId = Guid.NewGuid() }).State);
        queue.Close();
    }

    [Fact]
    public async Task LoopbackAdminRequiresCredentialAndOnlyEnqueuesValidBoundedJson()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        int number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        const string secret = "test-only-loopback-admin-secret32characters";
        var queue = new AdminCommandQueue();
        using var server = new AdminHttpServer(new(number, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))), queue, () => new { State = "waiting" });
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{number}") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/admin/status")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        (await client.GetAsync("/v1/admin/status")).EnsureSuccessStatusCode();
        Guid id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/v1/admin/commands", new { requestId = id, kind = "LockRoster", issuedAtUtc = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("queued", queue.Find(id)!.State);
        queue.Drain(5, c => new(c.RequestId, "applied", "locked"));
        var status = await client.GetFromJsonAsync<JsonElement>($"/v1/admin/commands/{id:D}");
        Assert.Equal("applied", status.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/admin/commands", new { kind = "Unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/v1/admin/commands", new StringContent(new string('x', 4097)))).StatusCode);
    }
}
