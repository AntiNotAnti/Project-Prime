using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ShotInputTests
{
    private static void Check(bool ok, string label) => NetArchitectureTests.Check(ok, label);
    private static byte Sample(uint frame) => ShotInputHistory.Sample((byte)(frame % 9),
        (frame & 1) != 0, (frame & 2) != 0, (frame & 4) != 0);

    public static int Run()
    {
        try
        {
            var life = new ShotKey(7, 8, 2, 9, 10, 0);
            var sender = new NetShotInputSender();
            var intent = new IntentPacket { Frame = 139, Aim = Vector3.UnitZ, Target = NetTargetIdentity.None };
            sender.Fill(139, life, ref intent);
            Check(intent.ShotInputCount == 0 && intent.ShotInputs.Valid(0), "missing native capture stays unavailable");
            for (uint frame = 100; frame <= 139; frame++) sender.Record(life, frame, Sample(frame));
            sender.Fill(139, life, ref intent);
            bool chronological = intent.ShotInputCount == 32;
            for (int i = 0; i < 32; i++) chronological &= intent.ShotInputs[i] == Sample(139u - (uint)i);
            Check(chronological, "bounded history keeps exact newest-first physical samples");
            sender.Record(life with { ShotId = 99 }, 139, Sample(139));
            sender.Record(life, 138, 0);
            sender.Fill(139, life, ref intent);
            Check(intent.ShotInputCount == 32 && intent.ShotInputs[0] == Sample(139),
                "shot ID and stale capture cannot replace current life input");
            sender.Fill(140, life, ref intent);
            Check(intent.ShotInputCount == 0 && intent.ShotInputs.Valid(0), "unsampled carrier frame clears reused packet fields");
            sender.Record(life, 142, Sample(142));
            sender.Fill(142, life, ref intent);
            Check(intent.ShotInputCount == 1 && intent.ShotInputs[0] == Sample(142) && intent.ShotInputs.Valid(1),
                "capture gap starts a new contiguous history without filling held frames");
            sender.Record(life, 142, (byte)(Sample(142) ^ ShotInputHistory.ShootBit));
            sender.Record(life, 142, Sample(142));
            sender.Fill(142, life, ref intent);
            Check(intent.ShotInputCount == 0, "contradictory same-frame capture remains unavailable");
            sender.Record(life, 143, Sample(143));
            sender.Fill(143, life, ref intent);
            Check(intent.ShotInputCount == 1, "next frame recovers without bridging conflicting input");
            foreach (ShotKey changed in new[] { life with { AuthorityEpoch = 8 }, life with { MatchId = 9 },
                life with { ShooterSlot = 3 }, life with { Generation = 10 }, life with { LifeId = 11 } })
            {
                var isolated = new NetShotInputSender();
                isolated.Record(life, 100, Sample(100)); isolated.Record(life, 101, Sample(101));
                isolated.Fill(101, changed, ref intent);
                Check(intent.ShotInputCount == 0, $"full life identity fences fill: {changed}");
                isolated.Record(changed, 102, Sample(102)); isolated.Fill(102, changed, ref intent);
                Check(intent.ShotInputCount == 1 && intent.ShotInputs.Valid(1), $"full life identity resets sender: {changed}");
            }
            var wrapping = new NetShotInputSender();
            wrapping.Record(life, uint.MaxValue, Sample(uint.MaxValue)); wrapping.Record(life, 0, Sample(0));
            wrapping.Fill(0, life, ref intent);
            Check(intent.ShotInputCount == 2 && intent.ShotInputs[1] == Sample(uint.MaxValue), "contiguous frame wrap preserves chronology");
            var invalidLife = new NetShotInputSender();
            invalidLife.Record(life with { LifeId = 0 }, 1, Sample(1));
            invalidLife.Fill(1, life with { LifeId = 0 }, ref intent);
            Check(intent.ShotInputCount == 0, "absent source life cannot author samples");

            sender = new NetShotInputSender();
            for (uint frame = 100; frame <= 139; frame++) sender.Record(life, frame, Sample(frame));
            intent = new IntentPacket { Frame = 139, Aim = Vector3.UnitZ, Target = NetTargetIdentity.None };
            sender.Fill(139, life, ref intent);
            Span<byte> bytes = stackalloc byte[IntentPacket.FullSize];
            intent.GrappleRequestNonce = 0x1234;
            int length = CompactIntentCodec.Write(intent, bytes);
            Check(length == 138 && CompactIntentCodec.HeaderSize == 136 && CompactIntentCodec.ShotInputOffset == 103
                && bytes[136] == 0x34 && bytes[137] == 0x12
                && CompactIntentCodec.TryRead(bytes[..length], out var decoded)
                && decoded.GrappleRequestNonce == 0x1234
                && decoded.ShotInputCount == 32 && decoded.ShotInputs.Equals(intent.ShotInputs),
                "all 32 physical input bytes and appended nonce roundtrip at protocol82 literal offsets");
            Check(ShotInputHistory.Sample(8, true, true, true) == 0x78
                && ShotInputHistory.Sample(0, false, false, false) == 0,
                "sample bits carry actual weapon, held shoot, physical zoom and native eligibility only");
            int offset = CompactIntentCodec.ShotInputOffset;
            bytes[offset] = 33;
            Check(!CompactIntentCodec.TryRead(bytes[..length], out _), "count greater than 32 rejected");
            CompactIntentCodec.Write(intent, bytes); bytes[offset + 1] |= 0x80;
            Check(!CompactIntentCodec.TryRead(bytes[..length], out _), "reserved input bit rejected");
            CompactIntentCodec.Write(intent, bytes); bytes[offset + 1] = 9;
            Check(!CompactIntentCodec.TryRead(bytes[..length], out _), "unknown weapon input rejected");
            CompactIntentCodec.Write(intent, bytes); bytes[offset] = 31; bytes[offset + 32] = 1;
            Check(!CompactIntentCodec.TryRead(bytes[..length], out _), "unused sample tail must be zero");
            intent.ShotInputCount = 0; intent.ShotInputs = default;
            CompactIntentCodec.Write(intent, bytes);
            Check(CompactIntentCodec.TryRead(bytes[..length], out decoded) && decoded.ShotInputCount == 0,
                "explicit zero-count live carrier is valid unavailable evidence");
            bytes[offset + 1] = 1;
            Check(!CompactIntentCodec.TryRead(bytes[..length], out _), "zero count cannot hide nonzero samples");
            intent.ShotInputCount = 33;
            bool rejected = false;
            try { CompactIntentCodec.Write(intent, bytes); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "writer rejects invalid input count");
            intent.ShotInputCount = 1; intent.ShotInputs[0] = 0x80;
            rejected = false;
            try { CompactIntentCodec.Write(intent, bytes); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "writer rejects malformed input bits");

            Span<byte> canonicalBefore = stackalloc byte[IntentPacket.FullSize];
            Span<byte> canonicalAfter = stackalloc byte[IntentPacket.FullSize];
            intent.ShotInputCount = 0; intent.ShotInputs = default; intent.Write(canonicalBefore);
            sender.Fill(139, life, ref intent); intent.Write(canonicalAfter);
            var replay = IntentPacket.Read(canonicalAfter);
            Check(canonicalBefore.SequenceEqual(canonicalAfter) && replay.ShotInputCount == 0
                && replay.ShotInputs.Valid(0) && IntentPacket.FullSize == 1287,
                "ephemeral physical input history never changes canonical replay bytes or reader");
            Check(CompactIntentCodec.HeaderSize + NetFireEvents.LiveCapacity * FireEvent.Size + 2 + 24 + 1 == 1199,
                "maximum live intent including nonce, network header and slot fits 1200-byte datagram");
            Console.WriteLine("shot input codec and sender tests passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
