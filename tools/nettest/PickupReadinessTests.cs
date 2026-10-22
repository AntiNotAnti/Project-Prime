using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class PickupReadinessTests
{
    private static int _checks;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool value, string text)
    { _checks++; if (!value) throw new InvalidOperationException("Pickup readiness: " + text); }
    private static void Set(string name, object? value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static void Call(string name, params object[] args)
        => typeof(NetSession).GetMethod(name, StaticPrivate)!.Invoke(null, args);
    private static PickupReady? ClientReady => (PickupReady?)typeof(NetSession).GetField("_pickupReadiness", StaticPrivate)!.GetValue(null);
    private static void Receive(IPEndPoint address, PickupReady ready)
    {
        byte[] bytes = new byte[1 + PickupReady.Size]; bytes[0] = (byte)PacketType.PickupReady;
        ready.Write(bytes.AsSpan(1)); Call("HandlePickupReady", new ReceivedPacket(address, bytes, bytes.Length));
    }
    internal static int Run()
    {
        _checks = 0;
        var role = NetSession.Role; bool authority = NetSession.IsAuthority;
        var match = NetSession.ServerMatch; int local = NetSession.LocalSlot;
        ushort generation = NetPlayerLifecycle.Generation(1);
        var peers = (IList)typeof(NetSession).GetField("_peers", StaticPrivate)!.GetValue(null)!;
        var endpointField = typeof(NetSession).GetField("_hostEndPoint", StaticPrivate)!;
        var transportField = typeof(NetSession).GetField("_transport", StaticPrivate)!;
        object? oldEndpoint = endpointField.GetValue(null), oldTransport = transportField.GetValue(null);
        using var clientTransport = new NetTransport(0);
        using var hostTransport = new NetTransport(0);
        var hostAddress = new IPEndPoint(IPAddress.Loopback, hostTransport.LocalPort);
        var clientAddress = new IPEndPoint(IPAddress.Loopback, clientTransport.LocalPort);
        var address = new IPEndPoint(IPAddress.Loopback, 34991);
        var peer = new RemotePeer { EndPoint = address, SlotIndex = 1 };
        try
        {
            var ready = new PickupReady(9, 2, 3, 1);
            byte[] wire = new byte[PickupReady.Size]; ready.Write(wire);
            Check(PickupReady.Size == 16 && PickupReady.TryRead(wire, out var decoded) && decoded == ready,
                "readiness codec preserves full admission and process sequence");
            Check(!PickupReady.TryRead(wire.AsSpan(1), out _) && !PickupReady.TryRead(new byte[16], out _),
                "truncation and zero identity are rejected");
            Check(NetPacketQueue.Priority(PacketType.PickupReady) == NetPacketPriority.Realtime,
                "readiness uses bounded realtime queue class");
            Set(nameof(NetSession.Role), NetRole.Host); Set(nameof(NetSession.IsAuthority), true);
            Set(nameof(NetSession.ServerMatch), new MatchStatePacket
            { AuthorityEpoch = 9, MatchId = 2, RoomKey = "unit-ready", Mode = (byte)GameMode.Battle });
            NetPlayerLifecycle.SetOccupant(1, 3); NetPickupNetwork.Reset(); peers.Add(peer);
            // Asset-free item record: populate only the native CaptureItems inputs.
            // This fixture does not invoke item initialization or simulate a pickup.
            var item = (ItemInstanceEntity)RuntimeHelpers.GetUninitializedObject(typeof(ItemInstanceEntity));
            typeof(ItemInstanceEntity).GetField("<ItemType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(item, ItemType.DoubleDamage);
            item.DespawnTimer = -1; item.TokenId = 0; item.Position = new Vector3(1, 2, 3);
            var items = (Dictionary<ItemInstanceEntity, PowerUpSpawnKey>)typeof(NetPowerUpLedger)
                .GetField("Items", StaticPrivate)!.GetValue(null)!;
            var spawn = new PowerUpSpawnKey(9, 2, 7, 12, 1); items.Add(item, spawn);
            Call("PumpHostPickups");
            Check(peer.Pickups == null, "host does not discover or send baseline before post-load readiness");
            foreach (var invalid in new[] { ready with { Epoch = 10 }, ready with { Match = 3 }, ready with { Generation = 4 } })
                Receive(address, invalid);
            Receive(new IPEndPoint(IPAddress.Loopback, 34992), ready);
            Check(peer.Pickups == null, "epoch match generation and sender fence discovery");
            Receive(address, ready);
            var first = peer.Pickups ?? throw new InvalidOperationException("Readiness did not create baseline.");
            Check(first.Baseline.Count == 1, "actual host handler joins exact item baseline after readiness");
            int packets = 0;
            Check(NetPickupNetwork.Pump(first, 1, (type, payload) =>
            {
                Check(type == PacketType.PickupItem && PickupItemFact.TryRead(payload, out var fact)
                    && fact.Spawn == spawn && fact.Position == item.Position && fact.Available,
                    "actual baseline pump carries native capture identity and position");
                packets++; return true;
            }) && packets == 1 && first.Baseline.Count == 0, "baseline drains exactly once");
            Receive(address, ready);
            Check(ReferenceEquals(peer.Pickups, first) && first.Baseline.Count == 0,
                "duplicate readiness cannot reset drained baseline cursor");
            // The real scene-loaded hook creates a process-monotonic readmission.
            Set(nameof(NetSession.Role), NetRole.Client); Set(nameof(NetSession.IsAuthority), false);
            endpointField.SetValue(null, hostAddress); transportField.SetValue(null, clientTransport);
            Set(nameof(NetSession.LocalSlot), 1);
            byte[] hello = new byte[6]; hello[0] = (byte)NetConfig.ProtocolVersion;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), 123);
            clientTransport.Send(hostAddress, PacketType.Hello, hello);
            byte[] welcome = new byte[17];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), 123);
            hostTransport.Send(clientAddress, PacketType.Welcome, welcome);
            Check(SpinWait.SpinUntil(() => clientTransport.ConnectionStats(hostAddress).HasValue, 2000),
                "real loopback transport establishes hosted readiness route");
            foreach (var ignored in clientTransport.Drain()) { }
            foreach (var ignored in hostTransport.Drain()) { }
            NetPickupNetwork.Reset(); NetSession.MarkMatchLoaded();
            var clientFirst = ClientReady;
            Check(clientFirst.HasValue && clientFirst.Value.Epoch == 9 && clientFirst.Value.Match == 2,
                "actual MarkMatchLoaded creates client readiness even without lobby session");
            Call("PumpPickupReadiness");
            PickupReady? delivered = null;
            Check(SpinWait.SpinUntil(() =>
            {
                foreach (var received in hostTransport.Drain())
                    if (received.Type == PacketType.PickupReady && PickupReady.TryRead(received.Payload, out var value)) delivered = value;
                return delivered.HasValue;
            }, 2000), "actual client readiness pump arrives over reliable loopback transport");
            Check(delivered!.Value.Generation == 3 && delivered.Value.Sequence == clientFirst!.Value.Sequence,
                "client pump binds readiness to current occupant without changing load sequence");
            clientFirst = ClientReady;
            NetSession.MarkMatchLoaded();
            Check(ClientReady == clientFirst, "duplicate scene-ready hook preserves admission sequence");
            NetPickupNetwork.Reset(); Check(ClientReady == null, "actual network scene reset retires local readiness");
            NetSession.MarkMatchLoaded();
            Check(ClientReady is { } second && second.Sequence > clientFirst!.Value.Sequence,
                "new scene load creates strictly newer process readmission");
            Set(nameof(NetSession.Role), NetRole.Host); Set(nameof(NetSession.IsAuthority), true);
            items.Clear(); items.Add(item, spawn);
            Receive(address, ready with { Sequence = 2 });
            Check(!ReferenceEquals(peer.Pickups, first) && peer.Pickups!.Baseline.Count == 1,
                "newer admission rebuilds discovery baseline after reset");
            var secondPeer = peer.Pickups;
            Receive(address, ready);
            Check(ReferenceEquals(secondPeer, peer.Pickups), "delayed old admission cannot replace current baseline");
            Set(nameof(NetSession.Role), NetRole.Server);
            Receive(address, ready with { Sequence = 3 });
            Check(ReferenceEquals(secondPeer, peer.Pickups), "dedicated authority does not consume hosted readiness");
            Console.WriteLine($"PICKUP READINESS PASS: {_checks} checks"); return 0;
        }
        catch (Exception ex) { Console.WriteLine("PICKUP READINESS FAIL: " + ex); return 1; }
        finally
        {
            endpointField.SetValue(null, oldEndpoint); transportField.SetValue(null, oldTransport);
            peers.Remove(peer); NetPowerUpLedger.Reset(); NetPlayerLifecycle.SetOccupant(1, generation);
            Set(nameof(NetSession.Role), role); Set(nameof(NetSession.IsAuthority), authority);
            Set(nameof(NetSession.ServerMatch), match); Set(nameof(NetSession.LocalSlot), local);
        }
    }
}
