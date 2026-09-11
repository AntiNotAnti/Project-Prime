using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Identity;
using MphRead.Backend.Nodes;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using ProjectPrime.Server.Node;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Backend.Tests;

/// <summary>
/// A control-plane vertical seam: Backend discovery/admission feeds a real
/// Kestrel Node, and raw WSS frames exercise the same Node session/lobby wire
/// contract used by the client. Worker/game-content startup is deliberately
/// outside this fixture.
/// </summary>
public sealed class BackendNodeVerticalTests
{
    private const string Issuer = "https://backend.example.test";
    private const string Secret = "vertical-node-credential-at-least-thirty-two";
    private const string ContentHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task BackendDiscoveryAdmissionAndNodeResumeSurviveBackendOutage()
    {
        Guid nodeId = Guid.NewGuid();
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var keys = new TemporaryKeys(signing);
        using var backend = new BackendFactory(requireConfirmation: true, configure: services =>
        {
            services.Configure<GameServerOptions>(options => options.Servers.Add(new GameServerRegistration
            {
                Id = nodeId,
                Enabled = true,
                ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
            }));
            services.Configure<TicketOptions>(options =>
            {
                options.Issuer = Issuer;
                options.KeyId = "vertical";
                options.SigningKeyPemPath = keys.PrivatePath;
            });
        });
        using HttpClient backendClient = backend.CreateDatabaseClient();
        await using var node = new VerticalNodeHost(nodeId, Issuer, keys.PublicPath);
        await node.App.StartAsync();
        string control = node.App.Urls.Single().Replace("https://", "wss://", StringComparison.Ordinal)
            + NodeEndpointContract.ControlPath;

        using (HttpResponseMessage registration = await SendNodeRegistrationAsync(backendClient, nodeId, control))
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        NodeDirectoryPage page = (await backendClient.GetFromJsonAsync<NodeDirectoryPage>(
            $"/v1/nodes?protocol=1&build=vertical&content={ContentHash}"))!;
        NodeDirectoryEntry listed = Assert.Single(page.Entries);
        Assert.Equal(nodeId, listed.NodeId);
        Assert.Equal(control, listed.PublicControlUri);

        Guid playerId = await RegisterAsync(backendClient);
        var confirmation = Assert.Single(backend.Email.Sent);
        using (HttpResponseMessage confirmed = await backendClient.PostAsJsonAsync("/v1/auth/confirm-email",
            new { playerId = confirmation.PlayerId, code = confirmation.Code }))
            Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        string accessToken = await LoginAsync(backendClient);
        string registeredTicket = await ReadTicketAsync(backendClient, "/v1/node-admissions", nodeId,
            accessToken);
        string guestTicket = await ReadTicketAsync(backendClient, "/v1/guest-node-admissions", nodeId,
            accessToken: null, displayName: "Guest");

        // Admissions are bearer grants, not a live dependency on Backend. Once
        // both grants exist the Backend can disappear without interrupting the
        // Node control session or its reconnect grace period.
        backend.Dispose();

        using var registered = await ConnectAsync(control, "Bearer " + registeredTicket,
            node.CertificateThumbprint);
        using JsonDocument registeredGreeting = await ReadAsync(registered.Socket, registered.Timeout.Token);
        JsonElement registeredPayload = AssertType(registeredGreeting, "node.session");
        Assert.Equal(playerId, registeredPayload.GetProperty("playerId").GetGuid());
        Assert.Equal(JsonValueKind.Null, registeredPayload.GetProperty("guestSessionId").ValueKind);
        string resumeToken = registeredPayload.GetProperty("resumeToken").GetString()!;
        Guid sessionId = registeredPayload.GetProperty("sessionId").GetGuid();

        await SendAsync(registered.Socket, "lobby.create",
            new { name = "Vertical", visibility = "Public", playerLimit = 2, observerLimit = 0 },
            registered.Timeout.Token);
        using JsonDocument lobby = await ReadAsync(registered.Socket, registered.Timeout.Token);
        JsonElement lobbyPayload = AssertType(lobby, "lobby.snapshot");
        Guid lobbyId = lobbyPayload.GetProperty("lobbyId").GetGuid();

        await registered.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test",
            registered.Timeout.Token);
        NodeSessionManager sessions = node.App.Services.GetRequiredService<NodeSessionManager>();
        await UntilAsync(() => sessions.CanResume(resumeToken), registered.Timeout.Token);

        using var resumed = await ConnectAsync(control, "Resume " + resumeToken,
            node.CertificateThumbprint);
        using JsonDocument resumedGreeting = await ReadAsync(resumed.Socket, resumed.Timeout.Token);
        JsonElement resumedPayload = AssertType(resumedGreeting, "node.session");
        Assert.Equal(sessionId, resumedPayload.GetProperty("sessionId").GetGuid());
        Assert.NotEqual(resumeToken, resumedPayload.GetProperty("resumeToken").GetString());
        using JsonDocument restoredLobby = await ReadAsync(resumed.Socket, resumed.Timeout.Token);
        Assert.Equal("lobby.snapshot", restoredLobby.RootElement.GetProperty("type").GetString());
        Assert.Equal(lobbyId, restoredLobby.RootElement.GetProperty("payload").GetProperty("lobbyId").GetGuid());

        using var guest = await ConnectAsync(control, "Bearer " + guestTicket,
            node.CertificateThumbprint);
        using JsonDocument guestGreeting = await ReadAsync(guest.Socket, guest.Timeout.Token);
        JsonElement guestPayload = AssertType(guestGreeting, "node.session");
        Assert.Equal(JsonValueKind.Null, guestPayload.GetProperty("playerId").ValueKind);
        Assert.NotEqual(Guid.Empty, guestPayload.GetProperty("guestSessionId").GetGuid());

        await resumed.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test",
            resumed.Timeout.Token);
        await guest.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test",
            guest.Timeout.Token);
    }

    private static async Task<HttpResponseMessage> SendNodeRegistrationAsync(HttpClient client, Guid nodeId,
        string control)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/v1/node/registration")
        {
            Content = JsonContent.Create(new NodeRegistration(Guid.NewGuid(), "Vertical", "test", control,
                1, "vertical", ContentHash, 8))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        request.Headers.Add("X-Server-Id", nodeId.ToString("D"));
        return await client.SendAsync(request);
    }

    private static async Task<Guid> RegisterAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = "vertical@example.test", password = "Strong-Vertical-Password123!", displayName = "Vertical"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = "vertical@example.test", password = "Strong-Vertical-Password123!"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> ReadTicketAsync(HttpClient client, string path, Guid nodeId,
        string? accessToken, string? displayName = null)
    {
        object payload = displayName == null ? (object)new { nodeId } : new { nodeId, displayName };
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        if (accessToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"{path} returned {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static JsonElement AssertType(JsonDocument document, string type)
    {
        Assert.Equal(type, document.RootElement.GetProperty("type").GetString());
        return document.RootElement.GetProperty("payload");
    }

    private static async Task<TrackedSocket> ConnectAsync(string endpoint, string authorization,
        string thumbprint)
    {
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate?.GetCertHashString() == thumbprint;
        socket.Options.SetRequestHeader("Authorization", authorization);
        try
        {
            await socket.ConnectAsync(new Uri(endpoint), timeout.Token);
            return new(socket, timeout);
        }
        catch
        {
            socket.Dispose();
            timeout.Dispose();
            throw;
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, string type, object payload,
        CancellationToken cancellationToken)
    {
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = NodeControlCodec.Version, requestId = Guid.NewGuid(), type, payload
        });
        await socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<JsonDocument> ReadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[NodeControlCodec.MaximumFrameBytes];
        int length = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length),
                cancellationToken);
            length += result.Count;
        }
        while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.AsMemory(0, length));
    }

    private static async Task UntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }

    private sealed class TrackedSocket(ClientWebSocket socket, CancellationTokenSource timeout) : IDisposable
    {
        public ClientWebSocket Socket { get; } = socket;
        public CancellationTokenSource Timeout { get; } = timeout;
        public void Dispose()
        {
            Socket.Dispose();
            Timeout.Dispose();
        }
    }

    private sealed class TemporaryKeys : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("prime-vertical-keys-").FullName;
        public string PrivatePath { get; }
        public string PublicPath { get; }
        public TemporaryKeys(ECDsa key)
        {
            PrivatePath = Path.Combine(_directory, "private.pem");
            PublicPath = Path.Combine(_directory, "public.pem");
            File.WriteAllText(PrivatePath, key.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(PublicPath, key.ExportSubjectPublicKeyInfoPem());
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }

    private sealed class VerticalNodeHost : IAsyncDisposable
    {
        private readonly X509Certificate2 _certificate;
        public WebApplication App { get; }
        public string CertificateThumbprint => _certificate.Thumbprint;

        public VerticalNodeHost(Guid nodeId, string issuer, string publicKeyPath)
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));
            App = NodeApplication.Build([], builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Node:Authentication:NodeId"] = nodeId.ToString("D"),
                    ["Node:Authentication:Issuer"] = issuer,
                    ["Node:Authentication:Keys:0:KeyId"] = "vertical",
                    ["Node:Authentication:Keys:0:PublicKeyPemPath"] = publicKeyPath
                });
                builder.WebHost.ConfigureKestrel(server =>
                    server.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate)));
            });
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            _certificate.Dispose();
        }
    }
}
