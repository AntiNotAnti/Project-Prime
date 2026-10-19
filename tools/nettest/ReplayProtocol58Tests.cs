using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Explicit byte fixtures for protocol58's unchanged archival body layouts.
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
            Check(NetConfig.ProtocolVersion == 58 && ReplayIdentityCompatibility.Supports(58)
                && !ReplayIdentityCompatibility.Supports(59), "explicit reviewed current protocol boundary");
            Checkpoints(); Metadata();
            Console.WriteLine($"PROTOCOL58 REPLAY PASS {_checks} byte/archive checks; no gameplay or visual acceptance claim.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("PROTOCOL58 REPLAY FAIL " + error); return 1; }
    }

    private static void Checkpoints()
    {
        foreach (bool timed in new[] { false, true })
        {
            byte[] current = Checkpoint(timed, thirdPerson: true);
            Check(current[6] == 58 && BinaryPrimitives.ReadUInt16LittleEndian(current.AsSpan(4)) == (timed ? 13 : 12), "current writer identifies reviewed layout");
            var destination = new ReplayReplicaState();
            foreach (byte protocol in new byte[] { 56, 57, 58 })
            {
                // Only the published protocol byte differs: these versions use
                // the same explicit body/feature contract. Do not alter old bodies.
                // Historical producers did not have the new camera bit.
                byte[] expected = Checkpoint(timed, thirdPerson: protocol == 58);
                byte[] historical = expected.ToArray(); historical[6] = protocol;
                var descriptor = ReplayReplicaState.InspectCheckpointLayout(new(historical));
                Check(descriptor.Protocol == protocol && descriptor.Version == (timed ? 13 : 12)
                    && descriptor.Features == (timed ? ReplayReplicaCheckpointLayout.CurrentFeatures | ReplayReplicaCheckpointFeatures.TimedShots
                        : ReplayReplicaCheckpointLayout.CurrentFeatures), "exact protocol/layout/features selected");
                destination.RestoreCheckpoint(new(historical));
                Check(destination.CaptureCheckpoint().Bytes.SequenceEqual(expected)
                    && destination.TimedShots.Enabled == timed
                    && destination.Match?.RuleBits == (protocol == 58 ? (ushort)MatchModifierFlags.ThirdPersonCamera : (ushort)0),
                    "56/57/58 body restores and recaptures without losing match bits");
            }
            void Reject(byte[] malformed, string label)
            {
                Check(Refused(() => destination.RestoreCheckpoint(new(malformed)))
                    && destination.CaptureCheckpoint().Bytes.SequenceEqual(current), label + " is transactional");
            }
            foreach (int length in new[] { 0, 4, 6, 7, 8, current.Length - 1 })
                Reject(current.AsSpan(0, length).ToArray(), "truncated checkpoint " + length);
            var future = current.ToArray(); future[6] = 59; Reject(future, "unreviewed future protocol");
            var old = current.ToArray(); old[6] = 55; Reject(old, "wrong historical feature contract");
            var unknown = current.ToArray(); unknown[7] |= 0x80; Reject(unknown, "unknown feature bit");
            var mismatched = current.ToArray(); mismatched[7] ^= (byte)ReplayReplicaCheckpointFeatures.TimedShots;
            Reject(mismatched, "mismatched version/features");
            var version = current.ToArray(); version[4] = 14; Reject(version, "unknown checkpoint version");
            Reject(current.Concat(new byte[] { 0 }).ToArray(), "unexpected checkpoint tail");
        }
    }

    private static byte[] Checkpoint(bool timed, bool thirdPerson)
    {
        var source = new ReplayReplicaState();
        var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = "protocol58-fixture", Mode = (byte)GameMode.Battle,
            RuleBits = thirdPerson ? (ushort)MatchModifierFlags.ThirdPersonCamera : (ushort)0 }.Write(packet.AsSpan(1));
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
        foreach (byte protocol in new byte[] { 56, 57, 58 })
        foreach (bool? continuation in new bool?[] { null, false, true })
        {
            ReplayMetadata metadata = Fixture(protocol, continuation);
            byte[] encoded = ReplayFormatV3.EncodeMetadata(metadata);
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
            foreach (byte unsupported in new byte[] { 55, 59 })
            {
                Check(Refused(() => ReplayFormatV3.EncodeMetadata(Fixture(unsupported, continuation))), "unreviewed metadata protocol refused by encoder");
                Check(Refused(() => ReplayFormatV3.DecodeMetadata(unsupported, encoded, 6)), "unreviewed metadata protocol refused by decoder");
            }
        }
    }
}
