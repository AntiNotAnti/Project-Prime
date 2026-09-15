using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class InputEdgeStressTests
    {
        private static readonly InputButtons[] EdgeButtons =
        {
            InputButtons.Jump,
            InputButtons.Morph,
            InputButtons.AltAttack,
            InputButtons.NextWeapon,
            InputButtons.PreviousWeapon,
            InputButtons.Boost,
            InputButtons.Spectate
        };

        [Theory]
        [InlineData("clean")]
        [InlineData("loss2")]
        [InlineData("loss5")]
        [InlineData("burst")]
        [InlineData("jitter")]
        [InlineData("reorder")]
        [InlineData("clientstall")]
        public void RedundantHistoryPreservesPressReleasePressEdgesAcrossImpairment(string impairment)
        {
            InputCommand[] expected = BuildEdgeScript();
            List<Packet> packets = BuildPackets(expected, impairment);
            ServerInputStream stream = new();
            InputCommand[] accepted = Drive(packets, expected.Length, stream).ToArray();

            // Each command carries non-edge intent as well as Pressed. Equality here
            // verifies that playout does not rewrite the command while suppressing
            // duplicates or recovering a packet from the eight-command history.
            Assert.Equal(expected, accepted);
            foreach (InputButtons edge in EdgeButtons)
            {
                Assert.Equal(2, accepted.Count(command => (command.Pressed & edge) != 0));
            }
            Assert.True(stream.Duplicates > 0);
            Assert.Equal(0, stream.SkippedCommands);
        }

        [Fact]
        public void HeldFallbackDoesNotReplayThePreviousPressEdge()
        {
            var stream = new ServerInputStream();
            InputCommand first = Command(10, InputButtons.Jump, InputButtons.Jump);
            InputCommand secondPress = Command(12, InputButtons.Jump, InputButtons.Jump);

            stream.Receive(new[] { first }, 100);
            stream.Take(100);
            stream.Take(101);
            Assert.Equal(first, stream.Take(102));

            // Sequence 11 is intentionally absent. The held control may be used
            // for simulation, but its edge and weapon intent must be cleared.
            stream.Receive(new[] { secondPress }, 103);
            InputCommand held1 = stream.Take(103);
            InputCommand held2 = stream.Take(104);
            InputCommand acceptedSecondPress = stream.Take(105);

            Assert.Equal(InputButtons.Jump, held1.Buttons);
            Assert.Equal(InputButtons.None, held1.Pressed);
            Assert.Equal(InputCommand.NoWeapon, held1.DesiredWeapon);
            Assert.Equal(InputButtons.Jump, held2.Buttons);
            Assert.Equal(InputButtons.None, held2.Pressed);
            Assert.Equal(InputCommand.NoWeapon, held2.DesiredWeapon);
            Assert.Equal(secondPress, acceptedSecondPress);
        }

        [Fact]
        public void HardStarvationExpiresHeldGameplayControlsWithoutInventingAnEdge()
        {
            var stream = new ServerInputStream();
            stream.ConfigurePlayout(1);
            InputCommand held = Command(10,
                InputButtons.Forward | InputButtons.Shoot,
                InputButtons.Shoot);
            stream.Receive(new[] { held }, 0);
            _ = stream.Take(0);
            Assert.Equal(held, stream.Take(1));

            InputCommand fallback = default;
            for (uint tick = 2; tick < 20; tick++)
            {
                fallback = stream.Take(tick);
            }

            Assert.Equal(InputButtons.None, fallback.Buttons
                & (InputButtons.Shoot | InputButtons.Forward));
            Assert.Equal(InputButtons.None, fallback.Pressed);
            Assert.True(stream.StarvedTicks > 0);

            InputCommand released = Command(11, InputButtons.None,
                InputButtons.None);
            stream.Receive(new[] { released }, 20);
            Assert.Equal(released, stream.Take(20));
        }

        [Fact]
        public void LongClientStallAccountsForUnavailableHistoryAndResumes()
        {
            var stream = new ServerInputStream();
            InputCommand[] initial = Enumerable.Range(0, InputBundle.Capacity)
                .Select(sequence => Command((uint)sequence, InputButtons.Forward, InputButtons.None))
                .ToArray();
            stream.Receive(initial, 0);
            for (uint tick = 0; tick < 8; tick++)
            {
                stream.Take(tick);
            }

            // No packet arrives while the server advances well beyond the
            // bounded history. The next command must be accounted as skipped,
            // rather than leaving the stream waiting forever.
            for (uint tick = 8; tick < 40; tick++)
            {
                stream.Take(tick);
            }
            InputCommand resumed = Command(40, InputButtons.Morph, InputButtons.Morph);
            stream.Receive(new[] { resumed }, 40);
            InputCommand accepted = default;
            int morphEdges = 0;
            bool hasProcessed = false;
            uint lastProcessed = 0;
            for (uint tick = 40; tick < 48; tick++)
            {
                InputCommand value = stream.Take(tick);
                if (stream.HasProcessed && (!hasProcessed || stream.LastProcessed != lastProcessed))
                {
                    accepted = value;
                    lastProcessed = stream.LastProcessed;
                    hasProcessed = true;
                    if ((value.Pressed & InputButtons.Morph) != 0) { morphEdges++; }
                }
            }

            Assert.Equal(resumed, accepted);
            Assert.Equal(1, morphEdges);
            Assert.True(stream.SkippedCommands > 0);
            Assert.True(stream.StarvedTicks > 0);
        }

        private static InputCommand[] BuildEdgeScript()
        {
            var commands = new List<InputCommand>(EdgeButtons.Length * 3);
            uint sequence = 100;
            foreach (InputButtons edge in EdgeButtons)
            {
                commands.Add(Command(sequence++, InputButtons.Forward | InputButtons.Shoot | edge, edge));
                commands.Add(Command(sequence++, InputButtons.Forward | InputButtons.Shoot, InputButtons.None));
                commands.Add(Command(sequence++, InputButtons.Forward | InputButtons.Shoot | edge, edge));
            }
            return commands.ToArray();
        }

        private static InputCommand Command(uint sequence, InputButtons buttons, InputButtons pressed)
            => new(sequence, sequence, sequence + 1000, buttons, pressed,
                -Vector3.UnitZ, (byte)(sequence % 9));

        private static List<Packet> BuildPackets(InputCommand[] commands, string impairment)
        {
            var packets = new List<Packet>(commands.Length);
            for (int sendTick = 0; sendTick < commands.Length; sendTick++)
            {
                if (ShouldDrop(sendTick, impairment)) { continue; }
                int first = Math.Max(0, sendTick - (InputBundle.Capacity - 1));
                InputCommand[] source = commands[first..(sendTick + 1)];
                byte[] wire = new byte[InputBundle.MaxSize];
                int length = InputBundle.Write(wire, 7, source, phaseRevision: 29);
                var decoded = new InputCommand[InputBundle.Capacity];
                Assert.True(InputBundle.TryRead(wire.AsSpan(0, length), decoded,
                    out uint matchId, out uint phaseRevision, out int count));
                Assert.Equal(7u, matchId);
                Assert.Equal(29u, phaseRevision);
                Assert.Equal(source, decoded[..count].ToArray());
                packets.Add(new Packet(sendTick + Delay(sendTick, impairment), decoded[..count].ToArray()));
            }
            return packets;
        }

        private static bool ShouldDrop(int sendTick, string impairment)
        {
            // Keep the startup window intact. Later packets redundantly carry
            // every command from the preceding seven client ticks.
            if (sendTick < 8) { return false; }
            return impairment switch
            {
                "loss2" => (sendTick * 37 + 11) % 100 < 2,
                "loss5" => (sendTick * 37 + 11) % 100 < 5,
                "burst" => sendTick is >= 8 and <= 11,
                // Seven missing sends are still inside the eight-command
                // redundancy window; this models a bounded client stall.
                "clientstall" => sendTick is >= 8 and <= 14,
                _ => false
            };
        }

        private static int Delay(int sendTick, string impairment)
            => impairment switch
            {
                "jitter" => new[] { 0, 2, 0, 1, 0, 2 }[sendTick % 6],
                "reorder" => new[] { 2, 0, 1, 0, 2, 0 }[sendTick % 6],
                _ => 0
            };

        private static IReadOnlyList<InputCommand> Drive(
            List<Packet> packets, int expectedCount, ServerInputStream stream)
        {
            var byArrival = new Dictionary<int, List<Packet>>();
            foreach (Packet packet in packets)
            {
                if (!byArrival.TryGetValue(packet.ArrivalTick, out List<Packet>? arrivals))
                {
                    arrivals = new List<Packet>();
                    byArrival.Add(packet.ArrivalTick, arrivals);
                }
                arrivals.Add(packet);
            }

            int lastArrival = packets.Count == 0 ? 0 : packets.Max(packet => packet.ArrivalTick);
            var accepted = new List<InputCommand>(expectedCount);
            bool hasLast = false;
            uint lastProcessed = 0;
            for (uint serverTick = 0; serverTick <= lastArrival + (uint)expectedCount + 16; serverTick++)
            {
                if (byArrival.TryGetValue((int)serverTick, out List<Packet>? arrivals))
                {
                    foreach (Packet packet in arrivals)
                    {
                        stream.Receive(packet.Commands, serverTick);
                    }
                }

                InputCommand value = stream.Take(serverTick);
                if (stream.HasProcessed && (!hasLast || stream.LastProcessed != lastProcessed))
                {
                    accepted.Add(value);
                    lastProcessed = stream.LastProcessed;
                    hasLast = true;
                }
            }
            return accepted;
        }

        private readonly record struct Packet(int ArrivalTick, InputCommand[] Commands);
    }
}
