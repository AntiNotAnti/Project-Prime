using System.Net;
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
using Microsoft.Extensions.Logging.Abstractions;
using MphRead.Backend.Identity;
using MphRead.Backend.Nodes;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using ProjectPrime.Server.Node;
using ProjectPrime.Server.Node.Discovery;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Backend.Tests;

/// <summary>
/// A control-plane vertical seam: the production directory reporter publishes
/// to the real Backend, production AccountSession/NodeControlClient consume
/// discovery/admission over a real Kestrel Node, and raw WSS frames exercise
/// resume/lobby details. Worker/game-content startup is deliberately outside
/// this fixture.
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

        using HttpClient reporterHttp = backend.CreateClient();
        var reporterRegistration = new NodeDirectoryRegistration(Guid.NewGuid(), "Vertical", "test", control,
            1, "vertical", ContentHash, 8);
        using var reporter = new NodeDirectoryReporter(
            new NodeDirectoryReporterOptions(nodeId, new Uri("http://localhost/"), reporterRegistration, Secret),
            () => new NodeDirectoryHeartbeat(reporterRegistration.Incarnation, 0, 0, 0),
            NullLogger<NodeDirectoryReporter>.Instance, reporterHttp);
        Assert.True(await reporter.PublishOnceAsync());
        NodeDirectoryPage page = (await backendClient.GetFromJsonAsync<NodeDirectoryPage>(
            $"/v1/nodes?protocol=1&build=vertical&content={ContentHash}"))!;
        NodeDirectoryEntry listed = Assert.Single(page.Entries);
        Assert.Equal(nodeId, listed.NodeId);
        Assert.Equal(control, listed.PublicControlUri);

        using var account = new AccountSession(new Uri("http://localhost/"), backend.Server.CreateHandler());
        AccountRegistration accountRegistration = await account.RegisterAsync("vertical@example.test",
            "Strong-Vertical-Password123!", "Vertical");
        PlayerId playerId = accountRegistration.PlayerId;
        Assert.True(accountRegistration.ConfirmationRequired);
        var confirmation = Assert.Single(backend.Email.Sent);
        await account.ConfirmEmailAsync(confirmation.PlayerId, confirmation.Code);
        await account.SignInAsync("vertical@example.test", "Strong-Vertical-Password123!");
        Assert.True(account.IsSignedIn);
        MphRead.Mods.Accounts.NodeListing discovered = Assert.Single(await account.GetNodesAsync(1, "vertical", ContentHash));
        Assert.Equal(nodeId, discovered.NodeId);
        Assert.Equal(control, discovered.PublicControlUri);
        NodeAdmissionTicket registeredGrant = await account.GetNodeTicketAsync(nodeId);
        // Keep two distinct guest grants so the production client can be
        // exercised alongside the raw-wire guest/resume assertions below.
        NodeAdmissionTicket productionGuestGrant = await account.GetGuestNodeTicketAsync(nodeId, "ProductionGuest");
        NodeAdmissionTicket guestGrant = await account.GetGuestNodeTicketAsync(nodeId, "Guest");

        // Admissions are bearer grants, not a live dependency on Backend. Once
        // both grants exist the Backend can disappear without interrupting the
        // Node control session or its reconnect grace period.
        backend.Dispose();

        using var productionSocket = new ClientWebSocket();
        productionSocket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate?.GetCertHashString() == node.CertificateThumbprint;
        await using var production = new NodeControlClient(productionSocket);
        using var productionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await production.ConnectAsync(productionGuestGrant, productionTimeout.Token);
        Assert.Equal(nodeId, production.Session!.NodeId);
        Assert.Equal("ProductionGuest", production.Session.DisplayName);
        Assert.Null(production.Session.PlayerId);
        Assert.NotEqual(Guid.Empty, production.Session.GuestSessionId);
        await production.SendAsync("lobby.list", new LobbyList(), productionTimeout.Token);
        await UntilAsync(() => production.Lobbies != null, productionTimeout.Token);

        using var registered = await ConnectAsync(control, "Bearer " + registeredGrant.Ticket,
            node.CertificateThumbprint);
        using JsonDocument registeredGreeting = await ReadAsync(registered.Socket, registered.Timeout.Token);
        JsonElement registeredPayload = AssertType(registeredGreeting, "node.session");
        Assert.Equal(playerId.Value, registeredPayload.GetProperty("playerId").GetGuid());
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

        using var guest = await ConnectAsync(control, "Bearer " + guestGrant.Ticket,
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
