using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Explicit byte fixtures for archival body layouts of protocols56–80 and current81.
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
            Check(NetConfig.ProtocolVersion == 81 && ReplayIdentityCompatibility.Supports(60)
                && ReplayIdentityCompatibility.Supports(61) && ReplayIdentityCompatibility.Supports(62)
                && ReplayIdentityCompatibility.Supports(63) && ReplayIdentityCompatibility.Supports(64) && ReplayIdentityCompatibility.Supports(65) && ReplayIdentityCompatibility.Supports(66) && ReplayIdentityCompatibility.Supports(67) && ReplayIdentityCompatibility.Supports(68) && ReplayIdentityCompatibility.Supports(69) && ReplayIdentityCompatibility.Supports(70) && ReplayIdentityCompatibility.Supports(71) && ReplayIdentityCompatibility.Supports(72) && ReplayIdentityCompatibility.Supports(73) && ReplayIdentityCompatibility.Supports(74) && ReplayIdentityCompatibility.Supports(75) && ReplayIdentityCompatibility.Supports(76) && ReplayIdentityCompatibility.Supports(77) && ReplayIdentityCompatibility.Supports(78) && ReplayIdentityCompatibility.Supports(79) && ReplayIdentityCompatibility.Supports(80) && ReplayIdentityCompatibility.Supports(81) && !ReplayIdentityCompatibility.Supports(82),
                "explicit reviewed current protocol boundary preserves protocol60–81 replay support");
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
            Check(current[6] == 81 && BinaryPrimitives.ReadUInt16LittleEndian(current.AsSpan(4)) == 14
                && current[7] == (timed ? 31 : 23), "current writer identifies protocol81/v14 grapple layout");
            var currentRoundtrip = new ReplayReplicaState(); currentRoundtrip.RestoreCheckpoint(new(current));
            Check(currentRoundtrip.CaptureCheckpoint().Bytes.SequenceEqual(current), "protocol81/v14 exact current checkpoint roundtrip");
            var destination = new ReplayReplicaState();
            foreach (byte protocol in new byte[] { 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80 })
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
                    "56–80 body restores and recaptures without losing match bits");
                if (protocol is >= 60 and <= 80)
                {
                    byte[] frozen80 = ReplicaCheckpointCompatibilityTests.Protocol80Rows(expected);
                    Check(historical.Length == expected.Length - 8 * (19 + 2) && historical[6] == protocol
                        && historical.AsSpan(0,6).SequenceEqual(frozen80.AsSpan(0,6))
                        && historical.AsSpan(7).SequenceEqual(frozen80.AsSpan(7)),
                        "published protocol60–80 checkpoint uses frozen173/1296 rows and original12/13 features");
                }
            }
            void Reject(byte[] malformed, string label)
            {
                Check(Refused(() => destination.RestoreCheckpoint(new(malformed)))
                    && destination.CaptureCheckpoint().Bytes.SequenceEqual(current), label + " is transactional");
            }
            foreach (int length in new[] { 0, 4, 6, 7, 8, current.Length - 1 })
                Reject(current.AsSpan(0, length).ToArray(), "truncated checkpoint " + length);
            var future = current.ToArray(); future[6] = 82; Reject(future, "unreviewed future protocol");
            var old = current.ToArray(); old[6] = 55; Reject(old, "wrong historical feature contract");
            var unknown = current.ToArray(); unknown[7] |= 0x80; Reject(unknown, "unknown feature bit");
            var mismatched = current.ToArray(); mismatched[7] ^= (byte)ReplayReplicaCheckpointFeatures.TimedShots;
            Reject(mismatched, "mismatched version/features");
            var version = current.ToArray(); version[4] = 15; Reject(version, "unknown checkpoint version");
            Reject(current.Concat(new byte[] { 0 }).ToArray(), "unexpected checkpoint tail");
        }
    }

    internal static byte[] HistoricalCheckpoint(byte[] current, byte protocol)
    {
        // v12/v13 header(8), clocks/counters(36), snapshot/roster/match flags(3).
        const int matchAt = 47;
        current = ReplicaCheckpointCompatibilityTests.Protocol80Rows(current);
        if (current[4] is not (12 or 13) || current[45] != 0 || current[46] != 1)
            throw new InvalidOperationException("Historical fixture requires an unrevisioned present match.");
        if (protocol is 60 or 61 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 73 or 74 or 75 or 76 or 77 or 78 or 79 or 80)
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
        foreach (byte protocol in new byte[] { 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81 })
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
            if (protocol == 64)
                Check(encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(60,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(61,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(62,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(63,continuation))),
                    "protocol64 timed metadata body preserves protocol60/61/62/63 bytes without new appendices or feature bits");
            if (protocol == 65)
                Check(encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(60,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(61,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(62,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(63,continuation)))
                    && encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(64,continuation))),
                    "protocol65 timed metadata body preserves protocol60/61/62/63/64 bytes without new appendices or feature bits");
            if (protocol == 66)
                Check(new byte[] { 60,61,62,63,64,65 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol66 timed metadata body preserves protocol60/61/62/63/64/65 bytes without new appendices or feature bits");
            if (protocol == 67)
                Check(new byte[] { 60,61,62,63,64,65,66 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol67 timed metadata body preserves protocol60/61/62/63/64/65/66 bytes without new appendices or feature bits");
            if (protocol == 68)
                Check(new byte[] { 60,61,62,63,64,65,66,67 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol68 timed metadata body retains reviewed historical bytes");
            if (protocol == 69)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol69 timed metadata body retains reviewed historical bytes");
            if (protocol == 70)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol70 native timed metadata body retains protocol60–69 bytes");
            if (protocol == 71)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol71 native timed metadata body retains protocol60–70 bytes");
            if (protocol == 72)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol72 native timed metadata body retains protocol60–71 bytes");
            if (protocol == 73)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol73 native timed metadata body retains protocol60–72 bytes");
            if (protocol == 74)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol74 native timed metadata body retains protocol60–73 bytes");
            if (protocol == 75)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol75 native timed metadata body retains protocol60–74 bytes");
            if (protocol == 76)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75 }.All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol76 native timed metadata body retains protocol60–75 bytes");
            if (protocol == 77)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76 }
                    .All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol77 native timed metadata body preserves every reviewed historical payload");
            if (protocol == 78)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77 }
                    .All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol78 native timed metadata body preserves every reviewed historical payload");
            if (protocol == 79)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78 }
                    .All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol79 native timed metadata body preserves every reviewed historical payload");
            if (protocol == 80)
                Check(new byte[] { 60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79 }
                    .All(old => encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(old,continuation)))),
                    "protocol80 native timed metadata body preserves every reviewed historical payload");
            if (protocol == 81)
                Check(encoded.AsSpan().SequenceEqual(ReplayFormatV3.EncodeMetadata(Fixture(80,continuation))),
                    "protocol81 native timed metadata body preserves protocol80 metadata fields");
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
            foreach (byte unsupported in new byte[] { 55, 82 })
            {
                Check(Refused(() => ReplayFormatV3.EncodeMetadata(Fixture(unsupported, continuation))), "unreviewed metadata protocol refused by encoder");
                Check(Refused(() => ReplayFormatV3.DecodeMetadata(unsupported, encoded, 6)), "unreviewed metadata protocol refused by decoder");
            }
        }
    }
}
