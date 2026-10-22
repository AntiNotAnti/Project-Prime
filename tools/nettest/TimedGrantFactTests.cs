using System;
using System.Buffers.Binary;
using System.Net;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class TimedGrantFactTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Timed grant codec: " + label); }
    private static TimedGrantFact Fact(uint contact = 100) => new(new(9, 2, 1, 3, 4, 0),
        5, 6, TimedPowerUp.DoubleDamage, 7, 8, 9, contact, unchecked(contact + 1800), TimedGrantEnd.Active, contact);
    private static byte[] Encode(TimedGrantFact fact)
    { var bytes = new byte[TimedGrantFact.Size]; fact.Write(bytes); return bytes; }
    private static void Reject(TimedGrantFact fact, string label)
    {
        Check(!fact.IsValid, label + " invalid object");
        bool thrown = false; try { Encode(fact); } catch (ArgumentException) { thrown = true; }
        Check(thrown, label + " writer rejects");
    }
    private static void RoundTrip(TimedGrantFact fact, string label)
    { Check(TimedGrantFact.TryRead(Encode(fact), out var read) && read == fact, label); }
    internal static int Run()
    {
        _checks = 0;
        try
        {
            var original = Fact();
            Check(NetConfig.ProtocolVersion >= 54 && (byte)PacketType.TimedGrant == 77
                && (byte)PacketType.PickupPrediction == 78, "protocol 54 grant and replay prediction identities");
            Check(NetReliableChannel.IsReliable(PacketType.TimedGrant)
                && !NetReliableChannel.IsCritical(PacketType.TimedGrant), "grant uses ordinary reliable recovery");
            Check(NetPacketQueue.Priority(PacketType.TimedGrant) == NetPacketPriority.Realtime,
                "grant has explicit realtime inbox priority");
            Check(NetFactBatch.Stride(PacketType.TimedGrant) == TimedGrantFact.Size,
                "grant fact batching has exact stride");
            var batch = new byte[22 * TimedGrantFact.Size];
            for (int offset = 0; offset < batch.Length; offset += TimedGrantFact.Size)
                original.Write(batch.AsSpan(offset, TimedGrantFact.Size));
            Check(NetFactBatch.Validate(PacketType.TimedGrant, batch), "maximum grant batch validates");
            Check(!NetFactBatch.Validate(PacketType.TimedGrant, new byte[23 * TimedGrantFact.Size]),
                "oversized grant batch refuses");
            batch[48] = byte.MaxValue;
            Check(!NetFactBatch.Validate(PacketType.TimedGrant, batch), "malformed row rejects complete grant batch");
            Check(!NetReliableChannel.IsReliable(PacketType.PickupPrediction)
                && NetFactBatch.Stride(PacketType.PickupPrediction) == 0,
                "replay prediction has no reliable or semantic batching lane");
            // TimedGrantBootstrap is replay-only packet 79; its enum is added
            // independently alongside the private replay consumer.
            var bootstrap = (PacketType)79;
            Check(!NetReliableChannel.IsReliable(bootstrap) && NetFactBatch.Stride(bootstrap) == 0,
                "replay bootstrap has no reliable or fact batching lane");
            // A playback-only transport exercises admission without a socket or game.
            using (var transport = new NetTransport(0, playbackOnly: true))
            {
                var peer = new IPEndPoint(IPAddress.Loopback, 27991);
                var welcome = new byte[17];
                BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), 123);
                transport.Send(peer, PacketType.Welcome, welcome);
                Check(transport.TrySendSemantic(peer, PacketType.TimedGrant, Encode(original)),
                    "semantic transport admits timed grant on existing connection");
                Check(!transport.TrySendSemantic(peer, bootstrap, Encode(original)),
                    "replay bootstrap cannot enter semantic transport");
                Check(!transport.TrySendSemantic(peer, PacketType.PickupPrediction, Encode(original)),
                    "replay prediction cannot enter semantic transport");
            }

            foreach (var type in Enum.GetValues<TimedPowerUp>())
            foreach (var reason in Enum.GetValues<TimedGrantEnd>())
            {
                uint end = reason is TimedGrantEnd.Active or TimedGrantEnd.Expired ? 1900u : 150u;
                uint authority = reason == TimedGrantEnd.Active ? 100u : end;
                var value = original with { Type = type, Reason = reason, EndFrame = end, AuthorityFrame = authority };
                RoundTrip(value, "type/reason");
                RoundTrip(value with { Resume = true }, "quiet resume type/reason");
                Check(Encode(value)[48] == (byte)reason && Encode(value with { Resume = true })[48] == ((byte)reason | 0x80),
                    "resume occupies only reason high bit");
            }
            RoundTrip(original with { Reason = TimedGrantEnd.Death, EndFrame = 100, AuthorityFrame = 100 }, "zero-duration terminal");
            RoundTrip(original with { Life = new(ulong.MaxValue, ushort.MaxValue, 7, ushort.MaxValue, ushort.MaxValue, 0),
                Room = uint.MaxValue, Revision = uint.MaxValue, Sequence = ulong.MaxValue,
                SpawnId = int.MaxValue, SpawnGeneration = uint.MaxValue }, "identity upper bounds");
            RoundTrip(original with { Reason = TimedGrantEnd.Renewed, EndFrame = 151, AuthorityFrame = 150 }, "renewal next-input phase");
            RoundTrip(Fact(uint.MaxValue - 100), "active wrapped expiry");
            var wrapped = Fact(uint.MaxValue - 100);
            RoundTrip(wrapped with { Reason = TimedGrantEnd.Expired, AuthorityFrame = wrapped.EndFrame }, "expired wrapped authority");
            RoundTrip(wrapped with { Reason = TimedGrantEnd.Renewed, EndFrame = 0, AuthorityFrame = uint.MaxValue }, "renewal wrapped next frame");
            var absent = original with { Sequence = 0, SpawnId = -1, SpawnGeneration = 0,
                ContactFrame = 0, EndFrame = 0, Reason = TimedGrantEnd.Retired };
            RoundTrip(absent, "explicit full-life absence");
            RoundTrip(absent with { Resume = true }, "explicit resumed absence");
            Check(original.Spawn == new PowerUpSpawnKey(9, 2, 5, 8, 9), "derived spawn identity");
            Check(absent.Spawn.Generation == 0 && absent.Spawn.SpawnId == -1, "absence is not an incarnation");
            foreach (var invalid in new[] {
                original with { Life = original.Life with { AuthorityEpoch = 0 } },
                original with { Life = original.Life with { MatchId = 0 } },
                original with { Life = original.Life with { ShooterSlot = 8 } },
                original with { Life = original.Life with { ShooterSlot = -1 } },
                original with { Life = original.Life with { Generation = 0 } },
                original with { Life = original.Life with { LifeId = 0 } },
                original with { Life = original.Life with { ShotId = 1 } },
                original with { Room = 0 }, original with { Revision = 0 },
                original with { Type = (TimedPowerUp)3 }, original with { Reason = (TimedGrantEnd)7 },
                original with { SpawnId = -2 }, original with { SpawnGeneration = 0 },
                original with { EndFrame = 99 }, original with { EndFrame = 1901 },
                original with { EndFrame = 1899 }, original with { AuthorityFrame = 99 },
                original with { Reason = TimedGrantEnd.Expired, AuthorityFrame = 1899 },
                original with { Reason = TimedGrantEnd.Death, EndFrame = 150, AuthorityFrame = 149 },
                original with { Reason = TimedGrantEnd.Renewed, EndFrame = 150, AuthorityFrame = 148 },
                absent with { SpawnId = 0 }, absent with { SpawnGeneration = 1 }, absent with { ContactFrame = 1 },
                absent with { EndFrame = 1 }, absent with { Reason = TimedGrantEnd.Death },
                absent with { Life = absent.Life with { LifeId = 0 } }, absent with { Room = 0 },
                absent with { Revision = 0 } }) Reject(invalid, "invalid field");
            var bytes = Encode(original);
            for (int size = 0; size < TimedGrantFact.Size; size++)
                Check(!TimedGrantFact.TryRead(bytes.AsSpan(0, size), out var bad) && bad == default, "truncated exact length");
            Check(!TimedGrantFact.TryRead(new byte[TimedGrantFact.Size + 1], out var trailing) && trailing == default, "trailing byte");
            for (int size = 0; size <= TimedGrantFact.Size + 1; size++)
            {
                if (size == TimedGrantFact.Size) continue;
                bool thrown = false; try { original.Write(new byte[size]); } catch (ArgumentException) { thrown = true; }
                Check(thrown, "writer exact length");
            }
            foreach (int offset in new[] { 10, 23, 48 })
            {
                var malformed = (byte[])bytes.Clone(); malformed[offset] = byte.MaxValue;
                Check(!TimedGrantFact.TryRead(malformed, out var bad) && bad == default, "malformed wire enum/slot");
            }
            for (int reason = 7; reason < 128; reason++)
            foreach (int resume in new[] { 0, 0x80 })
            {
                var malformed = (byte[])bytes.Clone(); malformed[48] = (byte)(reason | resume);
                Check(!TimedGrantFact.TryRead(malformed, out var bad) && bad == default,
                    "forbidden low-seven reason bits with and without resume");
            }
            var noEpoch = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(noEpoch, 0);
            Check(!TimedGrantFact.TryRead(noEpoch, out _), "malformed wire epoch");
            var fakeAbsent = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(fakeAbsent.AsSpan(24), 0);
            Check(!TimedGrantFact.TryRead(fakeAbsent, out _), "wire absence must be explicit canonical shape");
            Check(TimedGrantFact.Size == 53 && 22 * TimedGrantFact.Size + 24 + 4 == 1194, "22 records fit reliable 1200-byte budget");
            Check(23 * TimedGrantFact.Size + 24 + 4 > 1200 && (24 + 21) / 22 == 2, "24 slot/type states require two packets");
            Console.WriteLine($"PASS: {_checks} timed grant codec assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
