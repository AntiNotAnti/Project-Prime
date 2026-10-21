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
            Check(refused, $"Historical protocol {protocol} accepted an unsupported newer rule bit.");
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
        foreach (int protocol in new[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81 })
            Check(ReplayIdentityCompatibility.Convert(current,protocol).SequenceEqual(current)
                && MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(current,protocol)[1..]).RuleBits == 65536u,
                "Protocol60/61/62/63/64/65/66/67/68/69/70/71/72/73/74/75/76/77 preserve exact native MatchState payload and Quake3 rules.");
        var sessionCurrent = new byte[1 + SessionStatePacket.Size]; sessionCurrent[0] = (byte)PacketType.SessionState;
        BinaryPrimitives.WriteUInt32LittleEndian(sessionCurrent.AsSpan(1 + SessionStatePacket.Protocol28Size),65536u);
        foreach (int protocol in new[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81 })
            Check(ReplayIdentityCompatibility.Convert(sessionCurrent,protocol).SequenceEqual(sessionCurrent),
                "Protocol60/61/62/63/64/65/66/67/68/69/70/71/72/73/74/75/76/77 preserve exact native SessionState payload and movement bit16.");
        BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(104), 131072u);
        for (int protocol = 4; protocol < 68; protocol++) Reject(current, protocol);
        Check(ReplayIdentityCompatibility.Convert(current, 68).SequenceEqual(current)
            && MovementModelRules.FromRules((MatchModifierFlags)MatchStatePacket.Read(current.AsSpan(1)).RuleBits) == MovementModel.Traversal,
            "Protocol68 retains Traversal bit17 without changing MatchState width.");
        BinaryPrimitives.WriteUInt32LittleEndian(sessionCurrent.AsSpan(1 + SessionStatePacket.Protocol28Size), 131072u);
        for (int protocol = 31; protocol < 68; protocol++) Reject(sessionCurrent, protocol);
        Check(ReplayIdentityCompatibility.Convert(sessionCurrent, 68).SequenceEqual(sessionCurrent),
            "Protocol68 retains Traversal bit17 without changing SessionState width.");
        BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(104), 262144u);
        for (int protocol = 4; protocol < 70; protocol++) Reject(current, protocol);
        Reject(current,70); Reject(current,71); Reject(current,72); Reject(current,73); Reject(current,74); Reject(current,75); Reject(current,76);
        BinaryPrimitives.WriteUInt32LittleEndian(sessionCurrent.AsSpan(1 + SessionStatePacket.Protocol28Size),262144u);
        for (int protocol = 31; protocol < 70; protocol++) Reject(sessionCurrent, protocol);
        Reject(sessionCurrent,70); Reject(sessionCurrent,71); Reject(sessionCurrent,72); Reject(sessionCurrent,73); Reject(sessionCurrent,74); Reject(sessionCurrent,75); Reject(sessionCurrent,76);
        BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(104), 32768u);
        Check(MovementModelRules.FromRules((MatchModifierFlags)
            MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(current, 59)[1..]).RuleBits) == MovementModel.Momentum,
            "Protocol 59 preserves MovementModel v1.");
        foreach (int size in new[] { ServerStatusPacket.Size, ServerStatusPacket.SizeWithFlags, ServerStatusPacket.SizeWithWaitlist })
        {
            var status = new ServerStatusPacket { Protocol = 60, MaxPlayers = 8, ServerName = "fixture",
                Match = new MatchStatePacket { RoomKey = "fixture", RuleBits = 65536u } };
            byte[] wire = new byte[size]; ServerStatusPacket decoded;
            foreach (byte protocol in new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81 })
            {
                status.Protocol = protocol; status.Write(wire);
                Check(ServerStatusPacket.TryRead(wire,out decoded) && decoded.Protocol == protocol
                    && decoded.Match.RuleBits == 65536u && decoded.ServerName == "fixture",
                    "Reviewed protocol60/61/62/63/64/65/66/67/68/69/70/71/72/73/74/75/76/77 discovery preserves native current-width status.");
            }
            foreach (byte unsupported in new byte[] { 0,59,82,255 })
            {
                status.Protocol = unsupported; status.Write(wire);
                Check(!ServerStatusPacket.TryRead(wire,out _),"Unreviewed or wrong-width protocol discovery refuses current-width status.");
            }
            status.Match.RuleBits = 131072u;
            foreach (byte historicalProtocol in new byte[] { 60,61,62,63,64,65,66,67 })
            {
                status.Protocol = historicalProtocol; status.Write(wire);
                Check(!ServerStatusPacket.TryRead(wire,out _), "Historical discovery cannot enable Traversal bit17.");
            }
            status.Protocol = 68; status.Write(wire);
            Check(ServerStatusPacket.TryRead(wire,out decoded) && decoded.Match.RuleBits == 131072u,
                "Protocol68 discovery preserves Traversal in the reviewed current width.");
            status.Match.RuleBits=262144u;
            for(byte protocol=60;protocol<70;protocol++)
            {
                status.Protocol=protocol;status.Write(wire);
                Check(!ServerStatusPacket.TryRead(wire,out _),"Pre70 status cannot advertise reserved availability bit18.");
            }
            foreach(byte availabilityProtocol in new byte[]{70,71,72,73,74,75,76})
            {
                status.Protocol=availabilityProtocol;status.Write(wire);
                Check(ServerStatusPacket.TryRead(wire,out decoded)&&decoded.Match.RuleBits==262144u,
                    $"Protocol{availabilityProtocol} status preserves explicit availability without changing widths or packet IDs.");
            }
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
