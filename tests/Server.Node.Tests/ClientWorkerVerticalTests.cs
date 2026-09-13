using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MphRead;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class ClientWorkerVerticalTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task TlsNodeAutomaticallyContinuesRealUdpMatchesAndReturnsOnSameSession()
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
                Maps = new[] { map, map with { MapKey = "MP4 HIGHGROUND" }, map with { MapKey = "MP2 HARVESTER" } },
                PostMatchVoteSeconds = 30,
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
        NodeMatchHandoff? priorHandoff = null;
        string expectedMap = map.MapKey;
        var observedMatches = new HashSet<MatchId>();
        try
        {
            for (int round = 0; round < 4; round++)
            {
                if (round == 0)
                {
                    revision = owner.Lobby!.Revision;
                    await owner.SendAsync("lobby.ready.set", new LobbySetReady(true, revision));
                    await Until(() => guest.Lobby!.Revision > revision);
                    revision = guest.Lobby!.Revision;
                    await guest.SendAsync("lobby.ready.set", new LobbySetReady(true, revision));
                    await Until(() => owner.Lobby!.Revision > revision);
                    await owner.SendAsync("lobby.start", new LobbyStart(owner.Lobby!.Revision));
                }
                await Until(() =>
                {
                    if (owner.Error != null || guest.Error != null) throw new InvalidOperationException($"Node handoff failed: {owner.Error}; {guest.Error}; workers=" + string.Join(";", host.App.Services.GetRequiredService<WorkerManager>().Snapshot().Select(x => $"{x.Status}/{x.FailureReason}/{x.Capacity}/{x.Matches.Count}")));
                    return owner.Handoff != null && owner.Handoff.MatchId != priorMatch && guest.Handoff?.MatchId == owner.Handoff.MatchId;
                });
                var handoff = owner.Handoff!; var guestHandoff = guest.Handoff!;
                Assert.NotEqual(priorMatch, handoff.MatchId);
                Assert.True(observedMatches.Add(new(handoff.MatchId)));
                Assert.Equal(expectedMap, owner.Lobby!.MapKey);
                Assert.True(host.App.Services.GetRequiredService<WorkerScheduler>().TryGetAssignment(new(handoff.MatchId), out var assignment));
                Assert.Equal(expectedMap, assignment!.Spec.Content.MapKey);
                if (priorHandoff != null)
                {
                    Assert.NotEqual(priorHandoff.Ticket, handoff.Ticket);
                    Assert.NotEqual(priorHandoff.Nonce, handoff.Nonce);
                }
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
                byte[]? guestAdmissionKey = guestHandoff.UdpAuthenticationEnabled
                    ? AdmissionKeyRules.Decode(guestHandoff.AdmissionKey) : null;
                NetClient second;
                try
                {
                    second = new NetClient(transport,
                        new IPEndPoint(IPAddress.Parse(guestHandoff.Host), guestHandoff.Port),
                        "Player", guestHandoff.Hunter, guestHandoff.Nonce, guestHandoff.Ticket,
                        false, guestHandoff.WireMatchId, guestHandoff.AdmissionId, guestAdmissionKey,
                        guestHandoff.UdpAuthenticationEnabled);
                }
                finally
                {
                    if (guestAdmissionKey is not null)
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(guestAdmissionKey);
                }
                using (second)
                {
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
                // WSS resume reconstructs the stable session/lobby state. The
                // explicit rejoin request rotates the proof and reissues a
                // fresh match handoff, while live UDP continues independently.
                if (round == 0)
                {
                    var previous = owner;
                    string resumeToken = previous.Session!.ResumeToken;
                    await previous.DisposeAsync();
                    await Until(() => host.App.Services.GetRequiredService<NodeSessionManager>().CanResume(resumeToken), Poll);
                    owner = Control();
                    await owner.ResumeAsync(previous, CancellationToken.None);
                    await Until(() => owner.Handoff == null && owner.Lobby?.CurrentMatchId == handoff.MatchId
                        && owner.Lobby.LobbyId == lobbyId, Poll);
                    Assert.Equal(session, owner.Session!.SessionId);
                    Assert.NotEqual(resumeToken, owner.Session.ResumeToken);
                    await owner.SendAsync("match.rejoin", new NodeMatchRejoin(handoff.MatchId));
                    await Until(() => owner.Handoff?.MatchId == handoff.MatchId
                        && owner.Handoff.Nonce != handoff.Nonce, Poll);
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
                priorHandoff = handoff;
                await Until(() => owner.Round is { Options.IsEmpty: false } && guest.Round?.BallotRevision == owner.Round.BallotRevision);
                LobbyVoteChoice choice = round switch
                {
                    0 => LobbyVoteChoice.Rematch,
                    1 => LobbyVoteChoice.NextMap,
                    2 => LobbyVoteChoice.Map,
                    _ => LobbyVoteChoice.ReturnToLobby
                };
                var ballot = owner.Round!;
                var selected = ballot.Options.First(option => option.Choice == choice
                    && (choice != LobbyVoteChoice.Map || option.MapKey == "MP1 SANCTORUS"));
                Assert.Equal(round == 1 ? "MP4 HIGHGROUND" : "MP1 SANCTORUS", selected.MapKey);
                expectedMap = selected.MapKey;
                await owner.SendAsync("lobby.vote.cast", new LobbyVoteCast(owner.Lobby!.Revision, ballot.BallotRevision, selected.Id));
                await Until(() => guest.Round?.Options.First(option => option.Id == selected.Id).Votes == 1);
                NodeControlClient? disconnectedOwner = null;
                if (round == 1)
                {
                    // The original host has already voted. The Node must keep
                    // the resolved continuation pending until that host resumes
                    // instead of freezing a host-only match.
                    disconnectedOwner = owner;
                    string proof = owner.Session!.ResumeToken;
                    await owner.DisposeAsync();
                    await Until(() => host.App.Services.GetRequiredService<NodeSessionManager>().CanResume(proof));
                }
                await guest.SendAsync("lobby.vote.cast", new LobbyVoteCast(guest.Lobby!.Revision, ballot.BallotRevision, selected.Id));
                if (disconnectedOwner != null)
                {
                    owner = Control();
                    await owner.ResumeAsync(disconnectedOwner, CancellationToken.None);
                    await Until(() => guest.Handoff is { } next && next.MatchId != priorMatch);
                    await Until(() => owner.Handoff?.MatchId == guest.Handoff!.MatchId);
                    Assert.Equal(session, owner.Session!.SessionId);
                    Assert.Equal(lobbyId, owner.Lobby!.LobbyId);
                }
                if (choice == LobbyVoteChoice.ReturnToLobby)
                {
                    await Until(() => owner.Lobby!.Phase == LobbyPhase.Open && guest.Lobby!.Phase == LobbyPhase.Open);
                    Assert.Null(owner.Lobby.CurrentMatchId);
                    Assert.Null(owner.Handoff);
                    // Completed placements are released after the coordinator
                    // consumes their lifecycle notice; ReturnToLobby must not
                    // allocate another or leave one active.
                    var retainedMatches = host.App.Services.GetRequiredService<WorkerManager>().Snapshot()
                        .SelectMany(worker => worker.Matches).ToArray();
                    Assert.Equal(4, observedMatches.Count);
                    Assert.Empty(retainedMatches);
                    Assert.Equal(session, owner.Session!.SessionId);
                    Assert.Equal(lobbyId, owner.Lobby.LobbyId);
                }
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
