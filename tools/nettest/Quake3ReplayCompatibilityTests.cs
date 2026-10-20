using System;
using System.Buffers.Binary;
using System.IO;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class Quake3ReplayCompatibilityTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        void Reject(byte[] packet, int protocol)
        {
            bool refused = false;
            try { ReplayIdentityCompatibility.Convert(packet, protocol); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, $"Historical protocol {protocol} accepted a new movement bit.");
        }
        for (int protocol = 4; protocol <= 59; protocol++)
        {
            byte[] canonical = new byte[1 + MatchStatePacket.Size];
            canonical[0] = (byte)PacketType.MatchState;
            byte[] old = canonical.AsSpan(0, 1 + MatchStatePacket.Protocol59Size).ToArray();
            var expanded = ReplayIdentityCompatibility.Convert(old, protocol);
            Check(expanded.Length == canonical.Length, $"Protocol {protocol} MatchState width.");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(expanded[104..]) == 0,
                $"Protocol {protocol} invented movement rules.");
            BinaryPrimitives.WriteUInt32LittleEndian(canonical.AsSpan(104), 65536u);
            Reject(canonical, protocol);
            if (protocol < 59)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(canonical.AsSpan(104), 32768u);
                Reject(canonical, protocol);
            }
            if (protocol >= 31)
            {
                byte[] session = new byte[1 + SessionStatePacket.Size];
                session[0] = (byte)PacketType.SessionState;
                BinaryPrimitives.WriteUInt32LittleEndian(session.AsSpan(1 + SessionStatePacket.Protocol28Size), 65536u);
                Reject(session, protocol);
            }
        }
        byte[] current = new byte[1 + MatchStatePacket.Size]; current[0] = (byte)PacketType.MatchState;
        BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(104), 65536u);
        Check(MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(current, 60)[1..]).RuleBits == 65536u,
            "Protocol 60 preserves Quake 3 rules.");
        BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(104), 32768u);
        Check(MovementModelRules.FromRules((MatchModifierFlags)
            MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(current, 59)[1..]).RuleBits) == MovementModel.Momentum,
            "Protocol 59 preserves MovementModel v1.");
        foreach (int size in new[] { ServerStatusPacket.Size, ServerStatusPacket.SizeWithFlags, ServerStatusPacket.SizeWithWaitlist })
        {
            var status = new ServerStatusPacket { Protocol = 60, MaxPlayers = 8, ServerName = "fixture",
                Match = new MatchStatePacket { RoomKey = "fixture", RuleBits = 65536u } };
            byte[] wire = new byte[size]; status.Write(wire);
            Check(ServerStatusPacket.TryRead(wire, out var decoded) && decoded.Protocol == 60
                && decoded.Match.RuleBits == 65536u && decoded.ServerName == "fixture", "Current status width roundtrip.");
            status.Protocol = 59; status.Match.RuleBits = 32768u; status.Write(wire);
            byte[] old = new byte[size - 2];
            wire.AsSpan(0, MatchStatePacket.Protocol59Size).CopyTo(old);
            wire.AsSpan(MatchStatePacket.Size).CopyTo(old.AsSpan(MatchStatePacket.Protocol59Size));
            Check(ServerStatusPacket.TryRead(old, out decoded) && decoded.Protocol == 59
                && decoded.Match.RuleBits == 32768u && decoded.ServerName == "fixture", "Historical status width normalization.");
            Check(!ServerStatusPacket.TryRead(old.AsSpan(0, old.Length - 1), out _), "Truncated historical status refused.");
            Check(!ServerStatusPacket.TryRead(wire, out _), "Historical protocol in current status width refused.");
        }
        Console.WriteLine($"Quake 3 replay compatibility: {checks} assertions passed.");
        return 0;
    }
}
