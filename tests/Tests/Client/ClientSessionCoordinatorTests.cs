using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[CollectionDefinition("Client session globals", DisableParallelization = true)]
public sealed class ClientSessionGlobalCollection;

[Collection("Client session globals")]
public sealed class ClientSessionCoordinatorTests
{
    private sealed class KeysHandler : HttpMessageHandler
    {
        private readonly string _keys;

        public KeysHandler(string keys) => _keys = keys;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Put ? "{}" : _keys)
            });
        }
    }

    [Fact]
    public void SessionStatesCoverTheCompleteShellMatchLifetime()
    {
        Assert.Equal(new[]
        {
            ClientSessionState.Disconnected,
            ClientSessionState.Connecting,
            ClientSessionState.Lobby,
            ClientSessionState.LoadingMatch,
            ClientSessionState.InMatch,
            ClientSessionState.PostMatch,
            ClientSessionState.Leaving,
            ClientSessionState.Failed
        }, Enum.GetValues<ClientSessionState>());
    }

    [Fact]
    public void PumpHasOneOwnerAndRejectsWrongOrReentrantPolling()
    {
        var pump = new SessionPump();
        int polls = 0;

        Assert.True(pump.TryAcquire(SessionPumpOwner.Shell));
        Assert.True(pump.TryAcquire(SessionPumpOwner.Shell));
        Assert.False(pump.TryAcquire(SessionPumpOwner.Match));
        Assert.False(pump.TryPoll(SessionPumpOwner.Match, () => polls++));
        Assert.True(pump.TryPoll(SessionPumpOwner.Shell, () =>
        {
            polls++;
            Assert.False(pump.TryPoll(SessionPumpOwner.Shell, () => polls++));
            Assert.False(pump.Transfer(SessionPumpOwner.Shell, SessionPumpOwner.Match));
            Assert.False(pump.Release(SessionPumpOwner.Shell));
        }));
        Assert.Equal(1, polls);
        Assert.True(pump.Transfer(SessionPumpOwner.Shell, SessionPumpOwner.Match));
        Assert.False(pump.TryPoll(SessionPumpOwner.Shell, () => polls++));
        Assert.True(pump.TryPoll(SessionPumpOwner.Match, () => polls++));
        Assert.Equal(2, polls);
        Assert.True(pump.Release(SessionPumpOwner.Match));
        Assert.Equal(SessionPumpOwner.None, pump.Owner);
    }

    [Fact]
    public void PumpReleasesItsReentrancyGuardWhenPollingThrows()
    {
        var pump = new SessionPump();
        Assert.True(pump.TryAcquire(SessionPumpOwner.Shell));
        Assert.Throws<InvalidOperationException>(() =>
            pump.TryPoll(SessionPumpOwner.Shell, () => throw new InvalidOperationException("expected")));
        Assert.True(pump.Transfer(SessionPumpOwner.Shell, SessionPumpOwner.Match));
    }

    [Fact]
    public async Task StopWaitsForPollThenRunsTeardownExclusively()
    {
        var pump = new SessionPump();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Assert.True(pump.TryAcquire(SessionPumpOwner.Shell));
        Task polling = Task.Run(() => pump.TryPoll(SessionPumpOwner.Shell, () =>
        {
            entered.Set();
            release.Wait();
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        bool tornDown = false;
        Task stopping = Task.Run(() => pump.Stop(() => tornDown = true));
        await Task.Delay(20);
        Assert.False(stopping.IsCompleted);
        Assert.False(tornDown);

        release.Set();
        await Task.WhenAll(polling, stopping);
        Assert.True(tornDown);
        Assert.Equal(SessionPumpOwner.None, pump.Owner);
    }

    [Fact]
    public void LobbyAdmissionDoesNotReadyOrLoadUntilServerTransition()
    {
        ClientSessionCoordinator coordinator = ClientSessionCoordinator.Shared;
        coordinator.Shutdown();
        NetSession.Stop();
        using var serverTransport = new NetTransport(0);
        MatchRules rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var server = new ServerNetwork(serverTransport, rules);
        server.EnterLobby(sessionId: 91, rules, tick: 10);
        var play = new AuthoritativePlay("127.0.0.1", serverTransport.LocalPort,
            "SESSION", Hunter.Samus);
        int transitions = 0;
        void Transitioned(MatchTransitionPacket _) => transitions++;
        coordinator.MatchTransitioned += Transitioned;
        try
        {
            Pump(server, play.PollSession, () => play.Client.Connection != null);
            coordinator.AttachSession(play);

            Assert.Equal(ClientSessionState.Lobby, coordinator.State);
            Assert.Equal(SessionPumpOwner.Shell, coordinator.PumpOwner);
            Assert.False(coordinator.MatchPending);
            Assert.Equal(NetConnectionState.Lobby, play.Client.State);
            Assert.Equal(0u, play.Client.Accepted.MatchId);
            Assert.False(play.Client.Ready(0));
            Assert.Equal(NetConnectionState.Lobby, server.Peers[0]!.Connection.State);
            var scene = new Scene();
            Assert.Throws<InvalidOperationException>(() => coordinator.AttachScene(scene));
            Assert.Null(coordinator.Scene);
            Assert.Equal(SessionPumpOwner.Shell, coordinator.PumpOwner);

            server.StartLobbyMatch(matchId: 2, rules, tick: 120);
            Pump(server, () => coordinator.Poll(),
                () => coordinator.MatchPending && play.Client.Accepted.MatchId == 2);

            Assert.Equal(1, transitions);
            Assert.Equal(ClientSessionState.LoadingMatch, coordinator.State);
            Assert.Equal(NetConnectionState.Loading, play.Client.State);
            Assert.Equal(NetConnectionState.Loading, server.Peers[0]!.Connection.State);

            coordinator.AttachScene(scene);
            Assert.Equal(SessionPumpOwner.Match, coordinator.PumpOwner);
            Assert.Equal(ClientSessionState.LoadingMatch, coordinator.State);
            Assert.Equal(NetConnectionState.Loading, server.Peers[0]!.Connection.State);

            coordinator.DetachScene(scene);
            Assert.Equal(ClientSessionState.PostMatch, coordinator.State);
            Assert.Equal(SessionPumpOwner.None, coordinator.PumpOwner);
            Assert.Same(play, coordinator.Session);
            Assert.Same(play, AuthoritativePlay.Current);
            Assert.NotNull(play.Client.Connection);

            coordinator.Leave();
            Pump(server, () => { }, () => server.Count == 0);
            Assert.Equal(ClientSessionState.Disconnected, coordinator.State);
            Assert.Null(coordinator.Session);
            Assert.Null(AuthoritativePlay.Current);
        }
        finally
        {
            coordinator.MatchTransitioned -= Transitioned;
            coordinator.Shutdown();
            NetSession.Stop();
        }
    }

    [Fact]
    public async Task AuthenticatedReconnectRequiresCallbackAndCarriesFreshIdentity()
    {
        ClientSessionCoordinator coordinator = ClientSessionCoordinator.Shared;
        coordinator.Shutdown();
        NetSession.Stop();
        using var issuer = new GameTicketTests.Issuer();
        using var http = new HttpClient(new KeysHandler(issuer.Keys));
        using var authority = new ServerTicketAuthority(new(
            new Uri(GameTicketTests.Issuer.Origin + "/"),
            GameTicketTests.Issuer.Origin, issuer.Server, "operator-key")
        {
            SessionId = issuer.Session
        }, http);
        using var serverTransport = new NetTransport(0);
        MatchRules rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var server = new ServerNetwork(serverTransport, rules) { TicketAuthority = authority };
        server.EnterLobby(sessionId: 92, rules, tick: 10);
        var play = new AuthoritativePlay("127.0.0.1", serverTransport.LocalPort,
            "TICKET", Hunter.Samus, joinNonce: 123, ticket: issuer.Ticket());
        NetClient client = play.Client;
        try
        {
            Pump(server, play.PollSession, () => client.Connection != null);
            ulong previousConnectionId = client.Connection!.Id;
            uint sessionId = client.Connection.SessionId;
            coordinator.AttachSession(play);

            MethodInfo fail = typeof(NetClient).GetMethod("Fail",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NetClient.Fail was not found.");
            fail.Invoke(client, new object[] { "Server connection timed out." });
            Assert.False(coordinator.Poll());
            Assert.Equal(ClientSessionState.Failed, coordinator.State);
            Assert.True(coordinator.CanReconnect);
            Assert.False(await coordinator.ReconnectAsync());
            Assert.Contains("fresh game ticket", coordinator.Failure);

            ClientReconnectRequest? captured = null;
            coordinator.AttachSession(play, (request, _) =>
            {
                captured = request;
                return ValueTask.FromResult(issuer.Ticket(request.Nonce));
            });
            Assert.True(await coordinator.ReconnectAsync());
            Assert.Equal(ClientSessionState.Connecting, coordinator.State);
            Assert.NotNull(captured);
            Assert.NotEqual(0ul, captured.Value.Nonce);
            Assert.NotEqual(123ul, captured.Value.Nonce);
            Assert.Equal(previousConnectionId, captured.Value.PreviousConnectionId);
            Assert.Equal(sessionId, captured.Value.SessionId);
            Assert.Null(client.Connection);
            Assert.Null(client.Failure);
        }
        finally
        {
            coordinator.Shutdown();
            NetSession.Stop();
        }
    }

    private static void Pump(ServerNetwork server, Action clientPoll, Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 5)
        {
            server.Poll((uint)(timer.Elapsed.TotalSeconds * 60));
            clientPoll();
            if (done()) return;
            Thread.Sleep(2);
        }
        Assert.Fail("Session lifecycle condition timed out.");
    }
}
