using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Explicit byte fixtures for archival body layouts of protocols 56–63.
// No transport, gameplay simulation, rendering or scene replay is exercised.
internal static class ReplayProtocol58Tests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Refused(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { return true; }
    }

    internal static int Run()
    {
        try
        {
            _checks = 0;
            Check(NetConfig.ProtocolVersion == 63 && ReplayIdentityCompatibility.Supports(60)
                && ReplayIdentityCompatibility.Supports(61) && ReplayIdentityCompatibility.Supports(62)
                && ReplayIdentityCompatibility.Supports(63) && !ReplayIdentityCompatibility.Supports(64),
                "explicit reviewed current protocol boundary preserves protocol60/61/62 replay support");
            Checkpoints(); Metadata(); ConvertedRules();
            Console.WriteLine($"PROTOCOL58 REPLAY PASS {_checks} byte/archive checks; no gameplay or visual acceptance claim.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("PROTOCOL58 REPLAY FAIL " + error); return 1; }
    }

    private static void ConvertedRules()
    {
        foreach (PacketType type in new[] { PacketType.MatchState, PacketType.MapChange, PacketType.SessionState })
        foreach (int protocol in new[] { 56, 57, 58, 59 })
        {
            bool session = type == PacketType.SessionState;
            byte[] packet = new byte[1 + (session ? SessionStatePacket.Size : MatchStatePacket.Protocol59Size)];
            packet[0] = (byte)type;
            int offset = session ? 1 + SessionStatePacket.Protocol28Size : 104;
            ushort historicalRules = protocol >= 58 ? (ushort)MatchModifierFlags.ThirdPersonCamera : (ushort)0;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset), historicalRules);
            byte[] converted = ReplayIdentityCompatibility.Convert(packet, protocol).ToArray();
            Check(BinaryPrimitives.ReadUInt16LittleEndian(converted.AsSpan(offset)) == historicalRules
                && converted[0] == (byte)(type == PacketType.MapChange ? PacketType.MatchState : type),
                "historical rule defaults and camera bits survive conversion: " + type + "/" + protocol);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset), (ushort)(historicalRules | 32768));
            if (protocol < 59)
                Check(Refused(() => { _ = ReplayIdentityCompatibility.Convert(packet, protocol).Length; }),
                    "historical reserved movement bit fails closed: " + type + "/" + protocol);
            else
                Check((BinaryPrimitives.ReadUInt16LittleEndian(ReplayIdentityCompatibility.Convert(packet, protocol)[offset..]) & 32768) != 0,
                    "current MovementModel-v1 bit survives conversion: " + type);
        }
    }

    private static void Checkpoints()
    {
        foreach (bool timed in new[] { false, true })
        {
            byte[] current = Checkpoint(timed, thirdPerson: true, momentum: true);
            Check(current[6] == 63 && BinaryPrimitives.ReadUInt16LittleEndian(current.AsSpan(4)) == (timed ? 13 : 12), "current writer identifies reviewed layout");
            var destination = new ReplayReplicaState();
            foreach (byte protocol in new byte[] { 56, 57, 58, 59, 60, 61, 62, 63 })
            {
                // Published 56–59 bodies share a 105-byte match; protocol 60 widens it.
                // Camera starts at 58; fixed Momentum-v1 starts at 59.
                // Preserve earlier bodies without inventing either historical rule.
                byte[] expected = Checkpoint(timed, thirdPerson: protocol >= 58, momentum: protocol >= 59);
                byte[] historical = HistoricalCheckpoint(expected, protocol);
                var descriptor = ReplayReplicaState.InspectCheckpointLayout(new(historical));
                Check(descriptor.Protocol == protocol && descriptor.Version == (timed ? 13 : 12)
                    && descriptor.Features == (timed ? ReplayReplicaCheckpointLayout.CurrentFeatures | ReplayReplicaCheckpointFeatures.TimedShots
                        : ReplayReplicaCheckpointLayout.CurrentFeatures), "exact protocol/layout/features selected");
                destination.RestoreCheckpoint(new(historical));
                Check(destination.CaptureCheckpoint().Bytes.SequenceEqual(expected)
                    && destination.TimedShots.Enabled == timed
                    && destination.Match?.RuleBits == (ushort)((protocol >= 58 ? MatchModifierFlags.ThirdPersonCamera : 0)
                        | (protocol >= 59 ? MatchModifierFlags.MomentumMovement : 0)),
                    "56–63 body restores and recaptures without losing match bits");
                if (protocol is 60 or 61 or 62)
                    Check(historical.Length == expected.Length && historical[6] == protocol
                        && historical.AsSpan(0,6).SequenceEqual(expected.AsSpan(0,6))
                        && historical.AsSpan(7).SequenceEqual(expected.AsSpan(7)),
                        "published protocol60/61/62 checkpoint body and features remain byte-exact apart from explicit protocol header");
            }
            void Reject(byte[] malformed, string label)
            {
                Check(Refused(() => destination.RestoreCheckpoint(new(malformed)))
                    && destination.CaptureCheckpoint().Bytes.SequenceEqual(current), label + " is transactional");
            }
            foreach (int length in new[] { 0, 4, 6, 7, 8, current.Length - 1 })
                Reject(current.AsSpan(0, length).ToArray(), "truncated checkpoint " + length);
            var future = current.ToArray(); future[6] = 64; Reject(future, "unreviewed future protocol");
            var old = current.ToArray(); old[6] = 55; Reject(old, "wrong historical feature contract");
            var unknown = current.ToArray(); unknown[7] |= 0x80; Reject(unknown, "unknown feature bit");
            var mismatched = current.ToArray(); mismatched[7] ^= (byte)ReplayReplicaCheckpointFeatures.TimedShots;
            Reject(mismatched, "mismatched version/features");
            var version = current.ToArray(); version[4] = 14; Reject(version, "unknown checkpoint version");
            Reject(current.Concat(new byte[] { 0 }).ToArray(), "unexpected checkpoint tail");
        }
    }

    internal static byte[] HistoricalCheckpoint(byte[] current, byte protocol)
    {
        // v12/v13 header(8), clocks/counters(36), snapshot/roster/match flags(3).
        const int matchAt = 47;
        if (current[4] is not (12 or 13) || current[45] != 0 || current[46] != 1)
            throw new InvalidOperationException("Historical fixture requires an unrevisioned present match.");
        if (protocol is 60 or 61 or 62 or 63)
        {
            byte[] sameWidth = current.ToArray(); sameWidth[6] = protocol;
            return sameWidth;
        }
        int cut = matchAt + MatchStatePacket.Protocol59Size;
        byte[] historical = new byte[current.Length - 2];
        current.AsSpan(0, cut).CopyTo(historical);
        current.AsSpan(cut + 2).CopyTo(historical.AsSpan(cut));
        historical[6] = protocol;
        return historical;
    }

    private static byte[] Checkpoint(bool timed, bool thirdPerson, bool momentum)
    {
        var source = new ReplayReplicaState();
        var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = "protocol58-fixture", Mode = (byte)GameMode.Battle,
            RuleBits = (ushort)((thirdPerson ? MatchModifierFlags.ThirdPersonCamera : 0)
                | (momentum ? MatchModifierFlags.MomentumMovement : 0)) }.Write(packet.AsSpan(1));
        source.Accept(packet, 0);
        if (timed) Check(source.TimedShots.Begin(23, 7, 41, TimedGrantProofMode.Enforce), "timed fixture capability enabled");
        return source.CaptureCheckpoint().Bytes.ToArray();
    }

    private static ReplayMetadata Fixture(byte protocol, bool? continuation) => new()
    {
        FormatVersion = 6, ProtocolVersion = protocol, TimedShotCertificates = true,
        IndexedFireContinuation = continuation, TickRate = 60, RoomKey = "protocol58-fixture",
        Mode = GameMode.Battle, RecordedAtUtc = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc),
        BuildVersion = "fixture", BuildId = "fixture", Type = ReplayType.Clip
    };

    private static void Metadata()
    {
        foreach (byte protocol in new byte[] { 56, 57, 58, 59, 60, 61, 62, 63 })
        foreach (bool? continuation in new bool?[] { null, false, true })
        {
            ReplayMetadata metadata = Fixture(protocol, continuation);
            byte[] encoded = ReplayFormatV3.EncodeMetadata(metadata);
            if (protocol == 61)
                Check(encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(60,continuation))),
                    "protocol61 timed metadata body preserves protocol60 bytes without new appendices or feature bits");
            if (protocol == 62)
                Check(encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(60,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(61,continuation))),
                    "protocol62 timed metadata body preserves both protocol60/61 bytes without new appendices or feature bits");
            if (protocol == 63)
                Check(encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(60,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(61,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(62,continuation))),
                    "protocol63 timed metadata body preserves protocol60/61/62 bytes without new appendices or feature bits");
            var decoded = ReplayFormatV3.DecodeMetadata(protocol, encoded, 6);
            Check(decoded.ProtocolVersion == protocol && decoded.TimedShotCertificates
                && decoded.IndexedFireContinuation == continuation
                && ReplayFormatV3.EncodeMetadata(decoded).AsSpan().SequenceEqual(encoded), "exact v6 metadata roundtrip for each protocol/policy");
            string path = Path.Combine(Path.GetTempPath(), "prime-protocol58-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
            try
            {
                using (var writer = new ReplayWriterV3(path, metadata)) ReplayTimelineArchive.EndFrame(writer, 0);
                using var reader = DemoReader.Open(path);
                Check(reader is { FormatVersion: 6 } && reader.Metadata is { TimedShotCertificates: true } reopened
                    && reopened.ProtocolVersion == protocol && reopened.IndexedFireContinuation == continuation,
                    "actual archive writer/reader for each protocol/policy");
            }
            finally { File.Delete(path); File.Delete(path + ".part"); }
            void Reject(byte[] malformed, string label) => Check(Refused(() => ReplayFormatV3.DecodeMetadata(protocol, malformed, 6)), label);
            Reject(encoded[..^1], "truncated metadata capability");
            Reject(encoded.Concat(new byte[] { 0 }).ToArray(), "unexpected metadata tail");
            var magic = encoded.ToArray(); magic[^6] ^= 1; Reject(magic, "wrong capability magic");
            var capability = encoded.ToArray(); capability[^2] = 2; Reject(capability, "wrong capability revision");
            var policy = encoded.ToArray(); policy[^1] = 3; Reject(policy, "invalid continuation policy");
            foreach (byte unsupported in new byte[] { 55, 64 })
            {
                Check(Refused(() => ReplayFormatV3.EncodeMetadata(Fixture(unsupported, continuation))), "unreviewed metadata protocol refused by encoder");
                Check(Refused(() => ReplayFormatV3.DecodeMetadata(unsupported, encoded, 6)), "unreviewed metadata protocol refused by decoder");
            }
        }
    }
}
