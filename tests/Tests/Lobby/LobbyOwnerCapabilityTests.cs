using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class LobbyOwnerCapabilityTests
{
    [Fact]
    public void MissingCapabilityAdmitsPlayerWithoutHostPermission()
    {
        using var harness = new Harness(Guid.NewGuid(), IPAddress.Loopback);
        _ = harness.Join("MISSING");

        Assert.Equal(0UL, harness.Lobby.Runtime.HostConnectionId);
        Assert.False(harness.Lobby.Runtime.Players.Single().Host);
    }

    [Fact]
    public void WrongCapabilityDoesNotConsumeTheFirstOwnerClaim()
    {
        Guid expected = Guid.NewGuid();
        using var harness = new Harness(expected, IPAddress.Loopback);
        _ = harness.Join("WRONG", Guid.NewGuid());
        NetClient owner = harness.Join("OWNER", expected);

        Assert.Equal(owner.Connection!.Id, harness.Lobby.Runtime.HostConnectionId);
        Assert.False(harness.Lobby.Runtime.Players.Single(player => player.DisplayName == "WRONG").Host);
        Assert.True(harness.Lobby.Runtime.Players.Single(player => player.DisplayName == "OWNER").Host);
    }

    [Fact]
    public void ConsumedCapabilityCannotBeReplayedAfterOwnerLeaves()
    {
        Guid capability = Guid.NewGuid();
        using var harness = new Harness(capability, IPAddress.Loopback);
        NetClient owner = harness.Join("OWNER", capability);
        byte slot = owner.Accepted.Slot;
        harness.Server.Remove(slot, allowReconnect: false, ParticipantExitReason.ExplicitLeave);
        Assert.Equal(0UL, harness.Lobby.Runtime.HostConnectionId);

        _ = harness.Join("REPLAY", capability);

        Assert.Equal(0UL, harness.Lobby.Runtime.HostConnectionId);
        Assert.False(harness.Lobby.Runtime.Players.Single().Host);
    }

    [Fact]
    public void CapabilityFromWrongSourceAddressCannotClaimOwner()
    {
        Guid capability = Guid.NewGuid();
        using var harness = new Harness(capability, IPAddress.Parse("127.0.0.2"));
        _ = harness.Join("WRONG-ENDPOINT", capability);

        Assert.Equal(0UL, harness.Lobby.Runtime.HostConnectionId);
        Assert.False(harness.Lobby.Runtime.Players.Single().Host);
    }

    [Fact]
    public void NetworkLobbyWithoutConfiguredCapabilityDoesNotUseCompatibilityFallback()
    {
        using var harness = new Harness();
        _ = harness.Join("NETWORK-FIRST");

        Assert.Equal(0UL, harness.Lobby.Runtime.HostConnectionId);
        Assert.False(harness.Lobby.Runtime.Players.Single().Host);
    }

    private sealed class Harness : IDisposable
    {
        private readonly NetTransport _serverTransport = new(0);
        private readonly List<NetTransport> _clientTransports = new();
        private readonly List<NetClient> _clients = new();
        private uint _tick;

        public ServerNetwork Server { get; }
        public ServerLobby Lobby { get; }

        public Harness(Guid capability = default, IPAddress? ownerAddress = null)
        {
            MatchRules rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
            Server = new ServerNetwork(_serverTransport, rules, observers: null,
                lobbyOwnerCapability: capability, lobbyOwnerAddress: ownerAddress);
            Lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
            var bridge = new ServerLobbyNetwork(Lobby, Server);
            bridge.Open(0);
        }

        public NetClient Join(string name, Guid capability = default)
        {
            var transport = new NetTransport(0);
            var client = new NetClient(transport,
                new IPEndPoint(IPAddress.Loopback, _serverTransport.LocalPort),
                name, Hunter.Samus, ownerCapability: capability);
            _clientTransports.Add(transport);
            _clients.Add(client);
            PumpUntil(() => client.State == NetConnectionState.Lobby
                && Lobby.Runtime.Players.Any(player => player.DisplayName == name));
            Assert.Null(client.Failure);
            return client;
        }

        private void PumpUntil(Func<bool> condition)
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(3))
            {
                Server.Poll(++_tick);
                foreach (NetClient client in _clients) client.Poll();
                if (condition()) return;
                Thread.Sleep(2);
            }
            Assert.Fail("Lobby admission did not complete before its deadline.");
        }

        public void Dispose()
        {
            foreach (NetClient client in _clients) client.Dispose();
            foreach (NetTransport transport in _clientTransports) transport.Dispose();
            _serverTransport.Dispose();
        }
    }
}
