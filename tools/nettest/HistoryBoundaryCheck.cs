using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Run in its own process: the retail scene and player table are global.</summary>
    internal static class HistoryBoundaryCheck
    {
        public static int Run(string[] args)
        {
            if (args.Length is < 2 or > 3)
            {
                Console.Error.WriteLine("Usage: nettest --history-boundary DATA_DIRECTORY [VERSION]");
                return 2;
            }
            try
            {
                ServerContent.Open(args[1], args.Length == 3 ? args[2] : "AMHE1");
                using var fixture = new Fixture();
                fixture.Run();
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("HISTORYBOUNDARY FAIL " + error);
                return 1;
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly ServerSimulation _simulation = new(new RotationEntry
                { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle });
            private readonly NetTransport _server = new(0);
            private readonly NetTransport _transport = new(0);
            private readonly ServerNetwork _network;
            private NetClient _client;
            private uint _tick, _sequence;
            private int _comparisons;
            private string _phase = "join";
            private readonly byte[] _packet = new byte[SnapshotPacket.MaxSize];
            private readonly SnapshotPlayer[] _decoded = new SnapshotPlayer[8];

            public Fixture()
            {
                _network = new ServerNetwork(_server, "MP1 SANCTORUS", GameMode.Battle);
                // Only one client is admitted here, so the normal owner-side
                // eligibility transition cannot enter gameplay. This focused
                // history fixture drives the simulation directly.
                _simulation.Scene.Match.Phase = MatchPhase.Playing;
                _client = CreateClient();
            }

            private NetClient CreateClient() => new(_transport,
                new IPEndPoint(IPAddress.Loopback, _server.LocalPort), "HISTORY-CHECK", Hunter.Kanden);

            public void Run()
            {
                Join();
                int slot = _client.Accepted.Slot;
                PlayerEntity player = PlayerEntity.Players[slot];
                ulong oldConnection = player.ServerCombatIdentity.ConnectionId;
                _phase = "movement";
                Vector3 start = player.Position;
                for (int i = 0; i < 90; i++) Advance(InputButtons.Forward);
                Require((player.Position - start).Length > 0.1f, "Movement fixture did not move.");

                _phase = "alt-form";
                Advance(InputButtons.Morph, InputButtons.Morph);
                for (int i = 0; i < 90; i++) Advance(InputButtons.Forward);
                Require(player.IsAltForm, "Normal morph input never reached alt form.");

                _phase = "death";
                uint oldLife = player.ServerCombatIdentity.Life;
                player.TakeDamage(0, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, null);
                for (int i = 0; i < 10; i++) Advance();
                Require(player.Health == 0, "Death fixture did not remain dead.");
                _phase = "respawn";
                for (int i = 0; i < 600 && player.Health == 0; i++) Advance(InputButtons.Shoot);
                Require(player.Health > 0 && player.ServerCombatIdentity.Life > oldLife,
                    "Normal respawn did not produce a new life.");
                Require(!_simulation.Combat.History.TryGet(slot, _tick - 1, oldConnection, oldLife, out _),
                    "Previous life matched the respawn boundary.");

                _phase = "spectating";
                for (int i = 0; i < 30; i++) Advance(InputButtons.Spectate);
                Require(player.Flags2.TestFlag(PlayerFlags2.Spectating), "Spectate input did not change participation.");
                _phase = "spectator-rejoin";
                for (int i = 0; i < 600 && player.Health == 0; i++) Advance(InputButtons.Shoot);
                Require(player.Health > 0 && !player.Flags2.TestFlag(PlayerFlags2.Spectating), "Spectator did not rejoin.");

                _phase = "disconnect";
                uint departingLife = player.ServerCombatIdentity.Life;
                Require(_client.Disconnect(), "Disconnect request was not queued.");
                PumpUntil(() => _network.Count == 0);
                Require(_simulation.States.Length == 0, "Disconnected actor remains in snapshot.");
                Require(!_simulation.Combat.History.TryGet(slot, _tick - 1, oldConnection,
                    departingLife, out _), "Disconnected actor was recorded at current tick.");
                _phase = "replacement";
                _sequence = 0;
                // A fresh guest Join is a new participant and cannot reclaim
                // the reserved slot by display name. The reconnect API retains
                // the prior connection proof on this same guest endpoint.
                _client.Reconnect();
                Join();
                Require(_client.Accepted.Slot == slot && player.ServerCombatIdentity.ConnectionId != oldConnection,
                    "Reconnect did not replace the same slot with a new connection.");
                for (int i = 0; i < 20; i++) Advance(InputButtons.Forward);
                Require(!_simulation.Combat.History.TryGet(slot, _tick - 1, oldConnection,
                    player.ServerCombatIdentity.Life, out _), "Old connection matched replacement history.");
                Console.WriteLine($"HISTORYBOUNDARY PASS ticks={_tick} comparisons={_comparisons} "
                    + "movement,alt-form,death,respawn,spectating,rejoin,disconnect,replacement");
            }

            private void Join() => PumpUntil(() => _client.State == NetConnectionState.Playing);

            private void PumpUntil(Func<bool> done)
            {
                var timeout = Stopwatch.StartNew();
                do
                {
                    _client.Poll();
                    if (_client.State == NetConnectionState.Loading) _client.Ready(_client.Accepted.MatchId);
                    _network.Poll(_tick);
                    Step(sendSnapshot: true);
                    Thread.Sleep(5);
                    Require(timeout.Elapsed.TotalSeconds < 10, "Lifecycle timed out during " + _phase);
                } while (!done());
            }

            private void Advance(InputButtons buttons = 0, InputButtons pressed = 0)
            {
                // Deterministic owner input isolates tick ordering from UDP scheduling.
                ServerPeer peer = _network.Peers[_client.Accepted.Slot]!;
                Span<InputCommand> command = stackalloc InputCommand[1];
                command[0] = new(_sequence, _sequence, _tick, buttons, pressed, -Vector3.UnitZ, InputCommand.NoWeapon);
                _sequence++;
                peer.Inputs.Receive(command, _tick);
                Step();
            }

            private void Step(bool sendSnapshot = false)
            {
                _simulation.Step(_network, _tick);
                var packet = new SnapshotPacket(_tick, _tick, _network.MatchId, 0, false, Rng.Rng1, Rng.Rng2);
                int length = packet.Write(_packet, _simulation.States);
                Require(SnapshotPacket.TryRead(_packet.AsSpan(0, length), _decoded, out var decoded, out int count)
                    && decoded.ServerTick == _tick, "Snapshot round trip failed.");
                for (int i = 0; i < count; i++)
                {
                    SnapshotPlayer snapshot = _decoded[i];
                    PlayerEntity player = PlayerEntity.Players[snapshot.Slot];
                    Require(_simulation.Combat.History.TryGet(snapshot.Slot, _tick, snapshot.ConnectionId,
                        snapshot.Life, out var history), $"Missing history tick={_tick} phase={_phase} life={snapshot.Life}.");
                    Require(history.Position == snapshot.Position && history.Facing == snapshot.Facing
                        && history.Hunter == snapshot.Hunter && history.Alive == (snapshot.Health > 0)
                        && history.AltForm == snapshot.Flags.HasFlag(SnapshotPlayerFlags.AltForm)
                        && history.Spectating == snapshot.Flags.HasFlag(SnapshotPlayerFlags.Spectating),
                        $"History/snapshot disagreement tick={_tick} phase={_phase} position={history.Position}/{snapshot.Position} "
                        + $"facing={history.Facing}/{snapshot.Facing} alive={history.Alive}/{snapshot.Health > 0} "
                        + $"alt={history.AltForm}/{snapshot.Flags.HasFlag(SnapshotPlayerFlags.AltForm)} "
                        + $"spectating={history.Spectating}/{snapshot.Flags.HasFlag(SnapshotPlayerFlags.Spectating)}.");
                    Require(history == LagCompensationState.Capture(player, snapshot.ConnectionId, snapshot.Life),
                        $"Collider geometry differs from completed scene tick={_tick} phase={_phase}.");
                    _comparisons++;
                }
                if (sendSnapshot)
                    foreach (ServerPeer? peer in _network.Peers)
                        if (peer?.Connection.State == NetConnectionState.Playing)
                            peer.Connection.Send(_server, NetMessageType.Snapshot, _packet.AsSpan(0, length));
                _simulation.Combat.Consume(_simulation.Combat.Count);
                _tick++;
            }

            private static void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }

            public void Dispose()
            {
                _client.Dispose();
                _transport.Dispose();
                _server.Dispose();
                _simulation.Dispose();
            }
        }
    }
}
