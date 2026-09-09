using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Node.Sessions;
using FruityPrime.Server.Shared;
using FruityPrime.Server.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MphRead;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class ClientWorkerVerticalTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task TlsNodeLobbyHandsOffRealUdpThenReturnsAndRematchesOnSameSession()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? throw new InvalidOperationException("GAME_DATA_DIRECTORY is required.");
        using var artifacts = new ArtifactDirectory();
        const string hostAdminToken = "test-only-QZ1-host-administration-token-0001";
        string hostAdminTokenFile = Path.Combine(artifacts.Path, "host-admin.token");
        File.WriteAllText(hostAdminTokenFile, hostAdminToken + Environment.NewLine);
        var launch = new WorkerLaunchOptions
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [typeof(WorkerOptions).Assembly.Location, "--content-dir", Path.GetFullPath(data), "--content-version", "AMHE1", "--lanes", "1", "--max-matches", "2", "--max-matches-per-lane", "2", "--replay-dir", Path.Combine(artifacts.Path, "replays")],
            ArtifactDirectory = artifacts.Path,
            Capacity = new(2, 16, 0, 0), StartupTimeout = TimeSpan.FromSeconds(30), ShutdownTimeout = TimeSpan.FromSeconds(3)
        };
        WorkerContentIdentity content;
        await using (var warmup = new WorkerScheduler(new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid())))
        {
            content = (await warmup.StartWorkerAsync(launch)).Content!;
            await warmup.ShutdownAsync("identity captured");
        }
        var map = new ContentIdentity("MP1 SANCTORUS", content.ContentHash, content.ContentVersion, content.BuildVersion, content.ProtocolVersion);
        await using var host = new NodeHostFixture(configure: builder => builder.Configuration.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Node = new
            {
                Maps = new[] { map },
                HostAdmin = new { TokenFile = hostAdminTokenFile },
                Workers = new { Processes = new[] { launch }, DrainTimeout = "00:00:05", ForceAfterDrainDeadline = true }
            }
        }))));
        await host.App.StartAsync();
        using var adminHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate?.GetCertHashString() == host.CertificateThumbprint
        };
        using var admin = new HttpClient(adminHandler);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        string endpoint = host.App.Urls.Single().Replace("https://", "wss://") + "/v1/control";
        NodeControlClient Control()
        {
            var socket = new ClientWebSocket();
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == host.CertificateThumbprint;
            return new NodeControlClient(socket);
        }
        await using var initialOwner = Control();
        NodeControlClient owner = initialOwner;
        await using var guest = Control();
        await owner.ConnectAsync(new(host.Ticket(Guid.NewGuid()), DateTimeOffset.UtcNow.AddSeconds(120), host.NodeId, endpoint));
        await guest.ConnectAsync(new(host.Ticket(Guid.NewGuid()), DateTimeOffset.UtcNow.AddSeconds(120), host.NodeId, endpoint));
        Guid session = owner.Session!.SessionId;
        await owner.SendAsync("lobby.create", new LobbyCreate("Vertical", LobbyVisibility.Public, 2, 0));
        await Until(() => owner.Lobby != null);
        await guest.SendAsync("lobby.join", new LobbyJoin(owner.Lobby!.LobbyId, owner.Lobby.Revision));
        await Until(() => owner.Lobby!.Members.Length == 2 && guest.Lobby?.Members.Length == 2);
        long revision = owner.Lobby.Revision;
        await owner.SendAsync("lobby.configure", new LobbyConfigure(revision, map.MapKey, MatchMode.Battle, TimeLimitSeconds: 3));
        await Until(() => owner.Lobby!.Revision > revision && guest.Lobby!.Revision == owner.Lobby.Revision);
        Guid lobbyId = owner.Lobby!.LobbyId;
        Guid priorMatch = Guid.Empty;
        try
        {
            for (int round = 0; round < 2; round++)
            {
                revision = owner.Lobby!.Revision;
                await owner.SendAsync("lobby.ready.set", new LobbySetReady(true, revision));
                await Until(() => guest.Lobby!.Revision > revision);
                revision = guest.Lobby!.Revision;
                await guest.SendAsync("lobby.ready.set", new LobbySetReady(true, revision));
                await Until(() => owner.Lobby!.Revision > revision);
                await owner.SendAsync("lobby.start", new LobbyStart(owner.Lobby!.Revision));
                await Until(() =>
                {
                    if (owner.Error != null || guest.Error != null) throw new InvalidOperationException($"Node handoff failed: {owner.Error}; {guest.Error}; workers=" + string.Join(";", host.App.Services.GetRequiredService<WorkerManager>().Snapshot().Select(x => $"{x.Status}/{x.FailureReason}/{x.Capacity}/{x.Matches.Count}")));
                    return owner.Handoff != null && owner.Handoff.MatchId != priorMatch && guest.Handoff?.MatchId == owner.Handoff.MatchId;
                });
                var handoff = owner.Handoff!; var guestHandoff = guest.Handoff!;
                Assert.NotEqual(priorMatch, handoff.MatchId);
                if (round == 0)
                {
                    using var silent = new NetTransport(0);
                    Assert.False(await NetLaunch.JoinWorkerAsync(handoff with { Port = (ushort)silent.LocalPort }, "Player", timeoutMs: 250));
                    Assert.Null(AuthoritativePlay.Current);
                    Assert.True(owner.Connected); Assert.Equal(session, owner.Session!.SessionId); Assert.Equal(lobbyId, owner.Lobby!.LobbyId);
                    ulong failedNonce = handoff.Nonce;
                    await owner.SendAsync("match.rejoin", new NodeMatchRejoin(handoff.MatchId));
                    await Until(() => owner.Handoff!.Nonce != failedNonce);
                    handoff = owner.Handoff!;
                }
                Assert.True(await NetLaunch.JoinWorkerAsync(handoff, "Player"), NetLaunch.LastJoinError);
                var player = AuthoritativePlay.Current!.Client;
                owner.MarkGameplayJoined(handoff.MatchId);
                using var transport = new NetTransport(0);
                using var second = new NetClient(transport, new IPEndPoint(IPAddress.Parse(guestHandoff.Host), guestHandoff.Port),
                    "Player", guestHandoff.Hunter, guestHandoff.Nonce, guestHandoff.Ticket, false, guestHandoff.WireMatchId);
                MatchPhase observedPhase = MatchPhase.WaitingForPlayers;
                player.WorldPacketReceived = bytes =>
                {
                    for (int offset = WorldPacket.HeaderSize; offset < bytes.Length; offset += WorldRecord.Size)
                        if (WorldRecord.TryRead(bytes.Slice(offset, WorldRecord.Size), out var record) && record.Kind == WorldRecordKind.Match)
                            observedPhase = (MatchPhase)record.B;
                };
                void Poll()
                {
                    player.Poll(); second.Poll();
                    if (player.State == NetConnectionState.Loading) player.Ready(handoff.WireMatchId);
                    if (second.State == NetConnectionState.Loading) second.Ready(handoff.WireMatchId);
                    while (player.TryDequeueEvent(out _)) { }
                    while (second.TryDequeueEvent(out _)) { }
                }
                await Until(() => player.HasSnapshot && second.HasSnapshot && observedPhase == MatchPhase.Playing, Poll);
                Assert.Equal(handoff.WireMatchId, player.Snapshot.MatchId);
                Assert.Equal(session, owner.Session!.SessionId);
                if (round == 0)
                {
                    Uri debugEndpoint = new(new Uri(host.App.Urls.Single()),
                        $"/v1/host/matches/{handoff.MatchId:D}/lagcomp-debug");
                    await PostDebug(admin, debugEndpoint, "enable", "history", 0,
                        HttpStatusCode.Unauthorized, "wrong-test-only-host-administration-token-0001");
                    await PostDebug(admin, debugEndpoint, "enable", "history", 0);
                    await Until(() => player.HistoricalDebug?.Mode == HistoricalCollisionDebugMode.History, Poll);
                    HistoricalCollisionDebugPacket historyDebug = player.HistoricalDebug!;
                    Assert.True(historyDebug.Metrics.IsValid);
                    Assert.True(historyDebug.CurrentTick > 0);

                    await PostDebug(admin, debugEndpoint, "refresh", "dynamic", 0);
                    await Until(() => player.HistoricalDebug?.Mode == HistoricalCollisionDebugMode.Dynamic, Poll);
                    Assert.True(player.HistoricalDebug!.Metrics.IsValid);

                    await PostDebug(admin, debugEndpoint, "clear", null, 0);
                    await Until(() => player.HistoricalDebug == null, Poll);
                }
                // WSS resume rotates its proof and reissues a fresh match handoff,
                // while the live UDP transport continues independently.
                if (round == 0)
                {
                    var previous = owner;
                    string resumeToken = previous.Session!.ResumeToken;
                    await previous.DisposeAsync();
                    await Until(() => host.App.Services.GetRequiredService<NodeSessionManager>().CanResume(resumeToken), Poll);
                    owner = Control();
                    await owner.ResumeAsync(previous, CancellationToken.None);
                    await Until(() => owner.Handoff?.MatchId == handoff.MatchId && owner.Lobby?.LobbyId == lobbyId, Poll);
                    Assert.Equal(session, owner.Session!.SessionId);
                    Assert.NotEqual(resumeToken, owner.Session.ResumeToken);
                    Assert.NotEqual(handoff.Nonce, owner.Handoff!.Nonce);
                    Assert.True(await NetLaunch.JoinWorkerAsync(owner.Handoff, "Player"));
                    Assert.Same(player, AuthoritativePlay.Current!.Client);
                    owner.MarkGameplayJoined(handoff.MatchId);
                }
                // The real three-second match clock completes naturally in the Worker.
                await Until(() => owner.MatchEnded && owner.Lobby?.Phase == LobbyPhase.PostMatch, Poll);
                Assert.True(owner.ShouldReturnFromGameplay);
                NetSession.Stop();
                Assert.True(owner.Connected); Assert.Equal(session, owner.Session!.SessionId); Assert.Equal(lobbyId, owner.Lobby!.LobbyId);
                priorMatch = handoff.MatchId;
                if (round == 0)
                {
                    await owner.SendAsync("lobby.rematch", new LobbyRematch(owner.Lobby.Revision));
                    await Until(() => owner.Lobby!.Phase == LobbyPhase.Open && guest.Lobby!.Phase == LobbyPhase.Open);
                }
            }
        }
        catch (Exception error) { Console.WriteLine($"VERTICAL FAILURE: {error}; owner={owner.Error} phase={owner.Lobby?.Phase}; guest={guest.Error}"); throw; }
        finally { NetSession.Stop(); await owner.DisposeAsync(); await guest.DisposeAsync(); await host.App.StopAsync(); }
    }
    private static async Task Until(Func<bool> condition, Action? tick = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) { tick?.Invoke(); await Task.Delay(10, deadline.Token); }
    }
    private static async Task PostDebug(HttpClient client, Uri endpoint, string action, string? mode,
        byte seat, HttpStatusCode expected = HttpStatusCode.Accepted, string? bearer = null)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(mode == null
            ? new { action, seat } : (object)new { action, mode, seat });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(json)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (bearer != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
    private sealed class ArtifactDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "node-vertical-" + Guid.NewGuid().ToString("N"));
        public ArtifactDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
