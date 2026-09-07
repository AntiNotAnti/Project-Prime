using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Linq;
using System.Net;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>
    /// Black-box authoritative abuse checks against a running authoritative server.
    /// The tick-rate assertion measures published server ticks, not an invented
    /// character speed limit. Source ServerSimulation.Step advances physics once
    /// per such tick; command acknowledgements may skip flooded sequence numbers.
    /// </summary>
    internal static class AuthorityCheck
    {
        private static readonly Vector3 ForgedPosition = new(1_000_000, -1_000_000, 1_000_000);

        public static int Run(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: nettest --authority-check PORT,PORT,... (2 to 8 loopback ports, approximately 55 seconds)");
                return 2;
            }
            int[] ports;
            try { ports = args[1].Split(',').Select(Int32.Parse).ToArray(); }
            catch (Exception ex) when (ex is FormatException or OverflowException) { return 2; }
            if (ports.Length < 2 || ports.Length > 8 || ports.Any(p => p < 1 || p > UInt16.MaxValue)) return 2;
            try
            {
                using var fixture = new Fixture(ports);
                fixture.Run();
                Console.WriteLine("AUTHORITYCHECK PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("AUTHORITYCHECK FAIL " + ex);
                return 1;
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly NetTransport[] _transports;
            private readonly NetClient[] _clients;
            private readonly ClientWorldState[] _worlds;
            private readonly uint[] _sequences;
            private readonly InputCommand[] _single = new InputCommand[1];
            private readonly byte[] _payload = new byte[SnapshotPacket.MaxSize + 16];
            private readonly byte[] _datagram = new byte[NetHeader.Size + SnapshotPacket.MaxSize + 16];
            private readonly SnapshotPlayer[] _forgedPlayer = new SnapshotPlayer[1];
            private readonly FixedTickScheduler _scheduler = new();
            private uint _maliciousSequence = 0x70000000;
            private long _injected;
            private int _attackerSlot;
            private ulong _attackerIdentity;

            public Fixture(int[] ports)
            {
                _transports = new NetTransport[ports.Length];
                _clients = new NetClient[ports.Length];
                _worlds = new ClientWorldState[ports.Length];
                _sequences = new uint[ports.Length];
                for (int i = 0; i < ports.Length; i++)
                {
                    _transports[i] = new NetTransport(0);
                    _clients[i] = new NetClient(_transports[i], new IPEndPoint(IPAddress.Loopback, ports[i]),
                        "AUTH" + i, (Hunter)i);
                    var world = new ClientWorldState();
                    _worlds[i] = world;
                    _clients[i].WorldPacketValidator = WorldPacket.TryValidate;
                    _clients[i].WorldPacketReceived = payload => world.Receive(payload);
                }
            }

            public void Run()
            {
                Until("join", 15, () => _clients.All(c => c.State == NetConnectionState.Playing && c.HasSnapshot));
                _attackerSlot = _clients[0].Accepted.Slot;
                _attackerIdentity = _clients[0].Connection!.Id;
                Phase("baseline", 3, () => SendAllNeutral());

                SnapshotPlayer original = GetPlayer(_clients[1], _attackerSlot);
                _forgedPlayer[0] = original;
                _forgedPlayer[0].Position = ForgedPosition;
                _forgedPlayer[0].Health = UInt16.MaxValue;
                _forgedPlayer[0].AmmoUa = UInt16.MaxValue;
                _forgedPlayer[0].AmmoMissiles = UInt16.MaxValue;
                _forgedPlayer[0].AvailableWeapons = 0x1FF;
                _forgedPlayer[0].Weapon = 8;
                uint beforeAck = _clients[1].Snapshot.LastProcessedInput;
                Phase("input-flood-and-forged-state", 6, () =>
                {
                    for (int i = 1; i < _clients.Length; i++) SendInput(i);
                    for (int i = 0; i < 24; i++)
                    {
                        // Speed up the command stream, including its client
                        // clock, without advancing any server-owned timer.
                        uint estimated = (i & 1) == 0
                            ? unchecked(_clients[0].Snapshot.ServerTick + 100_000)
                            : unchecked(_clients[0].Snapshot.ServerTick - 100_000);
                        SendInput(0, viewTick: estimated, weapon: 8);
                    }
                    InjectForbiddenSnapshot();
                    InjectInvalidInput();
                });
                Require(Sequence32.IsNewer(_clients[1].Snapshot.LastProcessedInput, beforeAck),
                    "Independent peer stopped processing inputs during abuse.");
                SnapshotPlayer after = GetPlayer(_clients[1], _attackerSlot);
                Require(after.Health == original.Health && after.AmmoUa == original.AmmoUa
                    && after.AmmoMissiles == original.AmmoMissiles && after.AvailableWeapons == original.AvailableWeapons,
                    "Idle attacker's server-owned health, ammunition or inventory changed during forged-state injection.");
                Require((after.AvailableWeapons & (1 << after.Weapon)) != 0, "DesiredWeapon equipped an unavailable weapon.");
                Console.WriteLine($"AUTHORITY forgedDatagrams={_injected} ownerHealth={after.Health} ammo={after.AmmoUa}/{after.AmmoMissiles} observerAck={_clients[1].Snapshot.LastProcessedInput} PASS");

                // Clear flood backlog, then make an observable legal movement
                // request. Holding input without packets must not run forever.
                Phase("flood-recovery", 2, () => SendAllNeutral());
                Vector3 origin = GetPlayer(_clients[1], _attackerSlot).Position;
                Phase("legal-movement", 0.8, () =>
                {
                    SendInput(0, InputButtons.Forward);
                    for (int i = 1; i < _clients.Length; i++) SendInput(i);
                });
                Require((GetPlayer(_clients[1], _attackerSlot).Position - origin).LengthSquared > 0,
                    "Legal input did not recover after malformed high-sequence datagrams.");
                uint starvedLife = GetPlayer(_clients[1], _attackerSlot).Life;

                // Do not poll the attacker at all: only its transport worker
                // can transmit keepalives during this longer-than-timeout gap.
                long heldKeepalivesBefore = _transports[0].Metrics.PacketsSent;
                float finalHorizontalSpeed = Single.PositiveInfinity;
                uint observerAck = _clients[1].Snapshot.LastProcessedInput;
                Phase("input-starvation-with-loading-keepalive", NetConfig.TimeoutSeconds + 3, () =>
                {
                    for (int i = 1; i < _clients.Length; i++) SendInput(i);
                    SnapshotPlayer player = GetPlayer(_clients[1], _attackerSlot);
                    Require(player.ConnectionId == _attackerIdentity, "Starved connection expired despite transport keepalive.");
                    Require(player.Health > 0 && player.Life == starvedLife,
                        "Starvation fixture died or respawned; neutral movement cannot be established by this run.");
                    finalHorizontalSpeed = new Vector2(player.Speed.X, player.Speed.Z).Length;
                }, pollAttacker: false);
                // Use one original fixed-point unit as a negligible residual
                // speed tolerance. Biped neutral friction in
                // PlayerInput.ProcessMovement should settle below it.
                Require(finalHorizontalSpeed <= Fixed.ToFloat(1), "Starved input left the player moving.");
                long workerPackets = _transports[0].Metrics.PacketsSent - heldKeepalivesBefore;
                Require(workerPackets > 0, "Paused owner sent no worker keepalives.");
                Require(Sequence32.IsNewer(_clients[1].Snapshot.LastProcessedInput, observerAck),
                    "Independent peer stopped during paused owner's starvation interval.");
                Phase("resume-owner", 2, () => SendAllNeutral());
                Require(_clients[0].State == NetConnectionState.Playing && _clients[0].Connection!.Id == _attackerIdentity,
                    "Paused owner did not resume the same connection.");
                Console.WriteLine(FormattableString.Invariant($"AUTHORITY starvationSpeed={finalHorizontalSpeed:G6} workerPackets={workerPackets} PASS"));

                ulong staleIdentity = _attackerIdentity;
                uint staleMatch = _clients[0].Accepted.MatchId;
                _clients[0].Reconnect();
                _sequences[0] = 0;
                Until("reconnect", 15, () => _clients[0].State == NetConnectionState.Playing && _clients[0].HasSnapshot
                    && HasPlayer(_clients[1], _attackerSlot)
                    && GetPlayer(_clients[1], _attackerSlot).ConnectionId == _clients[0].Connection!.Id);
                Require(_clients[0].Connection!.Id != staleIdentity && _clients[0].Accepted.Slot == _attackerSlot,
                    "Reconnect did not issue a fresh identity in the original slot.");
                _attackerIdentity = _clients[0].Connection!.Id;
                Phase("stale-session-replay", 2, () =>
                {
                    SendAllNeutral();
                    _single[0] = new InputCommand(0x12345678, 0x12345678, 0,
                        InputButtons.Forward | InputButtons.Shoot, InputButtons.Jump, -Vector3.UnitZ, 8);
                    int length = InputBundle.Write(_payload, staleMatch, _single, _worlds[0].PhaseRevision);
                    Inject(NetMessageType.Input, staleIdentity, _payload.AsSpan(0, length));
                    length = ReliableEventPacket.Write(_payload, 0x12345678, ReliableEventType.Disconnect, default);
                    Inject(NetMessageType.Event, staleIdentity, _payload.AsSpan(0, length));
                    Require(GetPlayer(_clients[1], _attackerSlot).ConnectionId == _attackerIdentity,
                        "Stale session replaced or disconnected the reconnected player.");
                });
                Require(_clients[0].Snapshot.LastProcessedInput < _sequences[0], "Stale input sequence entered the new session.");
                Require(_clients[0].Disconnect(), "Disconnect request was not queued.");
                Until("disconnect", 5, () => !HasPlayer(_clients[1], _attackerSlot), sendAttacker: false);
                long remainingSnapshots = _clients[1].SnapshotsReceived;
                Phase("peer-after-disconnect", 2, () =>
                {
                    for (int i = 1; i < _clients.Length; i++) SendInput(i);
                }, pollAttacker: false);
                Require(_clients[1].SnapshotsReceived > remainingSnapshots, "Independent peer stopped after disconnect.");
                Console.WriteLine($"AUTHORITY reconnectOld={staleIdentity} reconnectNew={_attackerIdentity} disconnectedSlot={_attackerSlot} PASS");
            }

            private void InjectForbiddenSnapshot()
            {
                var snapshot = new SnapshotPacket(_clients[0].Snapshot.ServerTick, 0x12345678,
                    _clients[0].Accepted.MatchId, 0, false, 0, 0);
                int length = snapshot.Write(_payload, _forgedPlayer);
                Inject(NetMessageType.Snapshot, _attackerIdentity, _payload.AsSpan(0, length));
            }

            private void InjectInvalidInput()
            {
                _single[0] = new InputCommand(0x12345678, 0x12345678, 0, InputButtons.Forward,
                    InputButtons.Jump, -Vector3.UnitZ, InputCommand.NoWeapon);
                int length = InputBundle.Write(_payload, _clients[0].Accepted.MatchId, _single, _worlds[0].PhaseRevision);
                // A position appended to an otherwise valid input is forbidden.
                BinaryPrimitives.WriteSingleLittleEndian(_payload.AsSpan(length), ForgedPosition.X);
                Inject(NetMessageType.Input, _attackerIdentity, _payload.AsSpan(0, length + 4));
                BinaryPrimitives.WriteSingleLittleEndian(_payload.AsSpan(InputBundle.HeaderSize + 20), Single.NaN);
                Inject(NetMessageType.Input, _attackerIdentity, _payload.AsSpan(0, length));
            }

            private void Inject(NetMessageType type, ulong identity, ReadOnlySpan<byte> payload)
            {
                new NetHeader(type, NetHeaderFlags.None, identity, _maliciousSequence++, 0, 0).Write(_datagram);
                payload.CopyTo(_datagram.AsSpan(NetHeader.Size));
                _transports[0].SendDatagram(_clients[0].Connection!.Endpoint,
                    _datagram.AsSpan(0, NetHeader.Size + payload.Length));
                _injected++;
            }

            private void SendAllNeutral()
            {
                for (int i = 0; i < _clients.Length; i++) SendInput(i);
            }

            private void SendInput(int clientIndex, InputButtons buttons = InputButtons.None,
                uint? viewTick = null, byte weapon = InputCommand.NoWeapon)
            {
                NetClient client = _clients[clientIndex];
                if (client.State != NetConnectionState.Playing
                    || !_worlds[clientIndex].HasState || _worlds[clientIndex].Phase != MatchPhase.Playing) return;
                uint sequence = _sequences[clientIndex]++;
                _single[0] = new InputCommand(sequence, sequence, viewTick ?? client.Snapshot.ServerTick,
                    buttons, InputButtons.None, -Vector3.UnitZ, weapon);
                client.SendInputs(_single, _worlds[clientIndex].PhaseRevision);
            }

            private void Poll(bool attacker)
            {
                for (int i = attacker ? 0 : 1; i < _clients.Length; i++)
                {
                    NetClient client = _clients[i];
                    client.Poll();
                    if (client.Failure != null && !client.IsDisconnecting) throw new InvalidOperationException(client.Failure);
                    if (client.State == NetConnectionState.Loading)
                    {
                        _worlds[i].Reset(client.Accepted.MatchId);
                        client.Ready(client.Accepted.MatchId);
                    }
                    while (client.TryDequeueEvent(out _)) { }
                    foreach (SnapshotPlayer player in client.SnapshotPlayers)
                    {
                        Require(player.Position != ForgedPosition && player.Health != UInt16.MaxValue
                            && player.AmmoUa != UInt16.MaxValue && player.AmmoMissiles != UInt16.MaxValue,
                            "A client-authored position, health or ammo claim entered authoritative snapshots.");
                    }
                }
            }

            private void Phase(string name, double seconds, Action action, bool pollAttacker = true)
            {
                uint startTick = _clients[1].Snapshot.ServerTick;
                long startSnapshots = _clients[1].SnapshotsReceived;
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < seconds)
                {
                    if (_scheduler.TakeDue(Stopwatch.GetTimestamp()) == 0) { _scheduler.Wait(); continue; }
                    Poll(pollAttacker);
                    action();
                }
                uint ticks = unchecked(_clients[1].Snapshot.ServerTick - startTick);
                long snapshots = _clients[1].SnapshotsReceived - startSnapshots;
                // Twelve ticks of endpoint scheduling/arrival slack; never
                // derive a speed limit from client acknowledgement sequences.
                Require(ticks <= Math.Ceiling(watch.Elapsed.TotalSeconds * 60) + 12,
                    $"{name}: inputs advanced server ticks faster than its fixed60Hz schedule.");
                Require(ticks >= seconds * 40 && snapshots >= seconds * 10,
                    $"{name}: independent peer or simulation stalled under abuse.");
                Console.WriteLine(FormattableString.Invariant($"AUTHORITY phase={name} seconds={watch.Elapsed.TotalSeconds:F3} serverTicks={ticks} observerSnapshots={snapshots} PASS"));
            }

            private void Until(string name, double timeout, Func<bool> complete, bool sendAttacker = true)
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < timeout)
                {
                    Poll(attacker: true);
                    for (int i = sendAttacker ? 0 : 1; i < _clients.Length; i++) SendInput(i);
                    if (complete()) return;
                    System.Threading.Thread.Sleep(5);
                }
                throw new InvalidOperationException(name + " timed out.");
            }

            private static bool HasPlayer(NetClient client, int slot)
            {
                foreach (SnapshotPlayer player in client.SnapshotPlayers) if (player.Slot == slot) return true;
                return false;
            }

            private static SnapshotPlayer GetPlayer(NetClient client, int slot)
            {
                foreach (SnapshotPlayer player in client.SnapshotPlayers) if (player.Slot == slot) return player;
                throw new InvalidOperationException("Observer lost player slot " + slot);
            }

            private static void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }

            public void Dispose()
            {
                foreach (NetClient? client in _clients) client?.Dispose();
                foreach (NetTransport? transport in _transports) transport?.Dispose();
            }
        }
    }
}
