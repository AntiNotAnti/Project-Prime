using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Constructed values exercise only production value-storage semantics. They do
// not authenticate a FireEvent, execute native emission, or prove grant funding.
internal static class TimedShotCertificateTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Timed certificate storage: " + message);
        _checks++;
    }

    private static TimedShotCertificate Fixture(int slot = 0, uint id = 1, uint source = 500,
        uint authority = 100, uint room = 3, ushort life = 1)
    {
        var key = new ShotKey(11, 7, slot, 2, life, id);
        var fire = new FireEvent(id, source, 90, 128, FireEventKind.PressFire,
            (byte)BeamType.Missile, 0, FireEvent.DoubleStateBit, FireEvent.FlagPose,
            Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, SourceLife: life);
        var funding = new TimedShotFunding(55, new PowerUpSpawnKey(11, 7, room, 10, 4),
            key with { ShotId = 0 }, TimedPowerUp.DoubleDamage, 80, 490, 1880, 99);
        return new(key, fire, room, authority, TimedShotSource.AcceptedFire,
            new(ShotProofReason.Verified, ShotProofReason.Verified, true, 1, 10, 20),
            new(TimedGrantKnowledge.Granted, 55), TimedGrantKnowledge.Granted,
            funding, TimedShotUnavailable.None, 1);
    }

    private static bool Read(TimedShotCertificateStore store, in TimedShotCertificate expected, uint now)
        => store.TryGet(expected.Key, expected.Fire.SourceFrame, expected.Room, now, out var actual)
            && actual == expected;

    private static void ExactIdentity()
    {
        var store = new TimedShotCertificateStore(); var value = Fixture();
        Check(store.Remember(value, 100) == TimedShotRetention.Stored, "first constructed value stores");
        Check(Read(store, value, 100), "all certificate fields round trip without reinterpretation");
        foreach (var wrong in new[] {
            value.Key with { AuthorityEpoch = 12 }, value.Key with { MatchId = 8 },
            value.Key with { ShooterSlot = 1 }, value.Key with { Generation = 3 },
            value.Key with { LifeId = 2 }, value.Key with { ShotId = 2 },
            value.Key with { ShooterSlot = -1 }, value.Key with { ShooterSlot = 8 } })
            Check(!store.TryGet(wrong, 500, 3, 100, out var missing) && missing == default,
                "lookup requires exact complete identity: " + wrong);
        Check(!store.TryGet(value.Key, 501, 3, 100, out _), "another source frame cannot borrow a certificate");
        Check(!store.TryGet(value.Key, 500, 4, 100, out _), "another room cannot borrow a certificate");
        var zeroSource = Fixture(id: 2, source: 0, authority: 0);
        var zeroClockStore = new TimedShotCertificateStore();
        Check(zeroClockStore.Remember(zeroSource, 0) == TimedShotRetention.Stored && Read(zeroClockStore, zeroSource, 0),
            "observed source/authority frame zero is valid data, independent of zero shot identity");
    }

    private static void ImmutableFirstAndAliases()
    {
        var store = new TimedShotCertificateStore(); var first = Fixture();
        Check(store.Remember(first, 100) == TimedShotRetention.Stored, "immutable fixture seeds");
        var later = first with { AuthorityFrame = 200, Lane = TimedShotSource.NativeInput,
            Source = first.Source with { Source = ShotProofReason.SourceChanged, DoubleHintAgrees = false },
            LedgerAnswer = new(TimedGrantKnowledge.NotGranted), Knowledge = TimedGrantKnowledge.NotGranted,
            Funding = null, Unavailable = TimedShotUnavailable.Source, Components = 3 };
        Check(store.Remember(later, 200) == TimedShotRetention.Duplicate,
            "same exact fire does not resnapshot later grant/source/component candidate state");
        Check(Read(store, first, 200) && first.AuthorizedDouble,
            "original funding, source, lane, component count and authority timestamp remain immutable");
        foreach (var fire in new[] {
            first.Fire with { SourceFrame = 501 }, first.Fire with { AckFrame = 91 },
            first.Fire with { AckSubFrame = 129 }, first.Fire with { Charge = 1 },
            first.Fire with { Weapon = (byte)BeamType.PowerBeam },
            first.Fire with { Direction = Vector3.UnitX },
            first.Fire with { ContinuousPhase = 0 } })
        {
            long aliases = store.Aliases;
            Check(store.Remember(first with { Fire = fire, AuthorityFrame = 201 }, 201) == TimedShotRetention.Alias,
                "conflicting immutable fire/source alias is refused");
            Check(store.Aliases == aliases + 1 && Read(store, first, 201),
                "alias is counted and preserves the original certificate");
        }
        Check(!store.TryGet(first.Key, 501, first.Room, 201, out _), "refused source alias is not readable");
        Check(store.Remember(later with { AuthorityFrame = 1124 }, 1124) == TimedShotRetention.Duplicate,
            "duplicate at inclusive retention boundary does not refresh timestamp");
        Check(!store.TryGet(first.Key, 500, 3, 1125, out _), "duplicate never extends original 1024-frame lifetime");
        Check(store.Remember(later with { AuthorityFrame = 1125 }, 1125) == TimedShotRetention.Stored,
            "expired identity can be reused only after original evidence becomes unavailable");
        Check(Read(store, later with { AuthorityFrame = 1125 }, 1125),
            "expired replacement records its own unavailable source and absent funding exactly");
    }

    private static void FullCapacityAndEightSlots()
    {
        Check(TimedShotCertificateStore.PerSlot == 256 && TimedShotCertificateStore.Retention == 1024,
            "contract has fixed 256 cells per owner and 1024 authority-frame retention");
        var store = new TimedShotCertificateStore();
        for (int slot = 0; slot < 8; slot++)
            for (uint id = 1; id <= 256; id++)
            {
                var value = Fixture(slot, id, source: id + 500);
                Check(store.Remember(value, 100) == TimedShotRetention.Stored,
                    $"all fixed cells admit distinct value slot={slot} id={id}");
            }
        for (int slot = 0; slot < 8; slot++)
        {
            long refused = store.CapacityRefused;
            Check(store.Remember(Fixture(slot, 257), 100) == TimedShotRetention.Capacity
                && store.CapacityRefused == refused + 1, "full owner refuses additional value explicitly");
            var duplicate = Fixture(slot, 256, source: 756);
            Check(store.Remember(duplicate, 100) == TimedShotRetention.Duplicate,
                "duplicate lookup succeeds even when every owner cell is occupied");
            Check(store.Remember(duplicate with { Fire = duplicate.Fire with { SourceFrame = 999 } }, 100)
                == TimedShotRetention.Alias, "full storage does not hide an alias behind capacity refusal");
            for (uint id = 1; id <= 256; id++)
                Check(Read(store, Fixture(slot, id, source: id + 500), 100),
                    "capacity refusal and alias never evict any live owner evidence");
        }
        Check(store.CapacityRefused == 8 && store.Aliases == 8, "owners have independent fixed capacity counters");
        for (int slot = 0; slot < 8; slot++)
            Check(store.Remember(Fixture(slot, 257, authority: 1125), 1125) == TimedShotRetention.Stored,
                "each owner recovers storage after exact evidence expiry");
    }

    private static void RetentionAndRollover()
    {
        foreach (uint at in new[] { 0u, 100u, uint.MaxValue - 500 })
        {
            var store = new TimedShotCertificateStore(); var value = Fixture(authority: at);
            Check(store.Remember(value, at) == TimedShotRetention.Stored, "retention fixture stores at its authority frame");
            Check(Read(store, value, unchecked(at + 1024)), "TTL includes age 1024 across authority rollover");
            Check(!store.TryGet(value.Key, value.Fire.SourceFrame, value.Room, unchecked(at + 1025), out var missing)
                && missing == default, "TTL excludes age 1025 across authority rollover");
            var invalidStore = new TimedShotCertificateStore();
            Check(invalidStore.Remember(value, unchecked(at + 1025)) == TimedShotRetention.Invalid,
                "already-expired candidate cannot enter empty storage");
            Check(invalidStore.Remember(value, unchecked(at - 1)) == TimedShotRetention.Invalid,
                "future authority timestamp is not retained through unsigned rollover");
        }
        var sourceStore = new TimedShotCertificateStore();
        foreach (uint source in new[] { uint.MaxValue, 0u, 1u })
        {
            var value = Fixture(id: source == uint.MaxValue ? 1u : source + 2, source: source);
            Check(sourceStore.Remember(value, 100) == TimedShotRetention.Stored && Read(sourceStore, value, 100),
                "source-frame rollover is exact metadata rather than an authority expiry clock");
        }
    }

    private static void LifeAndScopeReuse()
    {
        var store = new TimedShotCertificateStore(); var old = Fixture();
        var next = Fixture(life: 2, authority: 200);
        Check(store.Remember(old, 100) == TimedShotRetention.Stored
            && store.Remember(next, 200) == TimedShotRetention.Stored,
            "same shot id in a new life is a distinct immutable identity");
        Check(Read(store, old, 200) && Read(store, next, 200), "new life preserves original old-life flight");
        Check(Read(store, old, 1124) && !store.TryGet(old.Key, 500, 3, 1125, out _)
            && Read(store, next, 1125), "old and current life retain independent authority timestamps");

        foreach (string scope in new[] { "generation", "epoch", "match", "room" })
        {
            var full = new TimedShotCertificateStore();
            for (uint id = 1; id <= 256; id++)
                Check(full.Remember(Fixture(id: id), 100) == TimedShotRetention.Stored, "scope reuse starts with full owner");
            var replacement = scope switch {
                "generation" => old with { Key = old.Key with { Generation = 3 } },
                "epoch" => old with { Key = old.Key with { AuthorityEpoch = 12 } },
                "match" => old with { Key = old.Key with { MatchId = 8 } },
                _ => old with { Room = 4 } };
            Check(full.Remember(replacement, 100) == TimedShotRetention.Stored && Read(full, replacement, 100),
                "retired " + scope + " permits fixed-cell reuse without growth");
            Check(!full.TryGet(old.Key, old.Fire.SourceFrame, old.Room, 100, out _),
                "replaced old " + scope + " identity cannot answer the new cell");
            Check(Read(full, Fixture(id: 256), 100),
                "scope reuse replaces an eligible cell, not unrelated remaining immutable entries");
        }
        var fullLife = new TimedShotCertificateStore();
        for (uint id = 1; id <= 256; id++) fullLife.Remember(Fixture(id: id), 100);
        Check(fullLife.Remember(next, 200) == TimedShotRetention.Capacity && Read(fullLife, old, 200),
            "life change alone cannot evict still-retained old-life flight from a full owner");
    }

    private static void InvalidValues()
    {
        var value = Fixture(); var store = new TimedShotCertificateStore();
        var invalid = new[] {
            value with { Key = default }, value with { Key = value.Key with { AuthorityEpoch = 0 } },
            value with { Key = value.Key with { MatchId = 0 } },
            value with { Key = value.Key with { ShooterSlot = -1 } },
            value with { Key = value.Key with { ShooterSlot = 8 } },
            value with { Key = value.Key with { Generation = 0 } },
            value with { Key = value.Key with { LifeId = 0 } },
            value with { Key = value.Key with { ShotId = 0 } },
            value with { Fire = value.Fire with { ShotId = 2 } },
            value with { Fire = value.Fire with { SourceLife = 0 } },
            value with { Fire = value.Fire with { SourceLife = 2 } },
            value with { Components = 0 }, value with { Components = -1 } };
        Check(store.Remember(value, 100) == TimedShotRetention.Stored, "invalid input checks preserve seeded evidence");
        foreach (var candidate in invalid)
            Check(store.Remember(candidate, 100) == TimedShotRetention.Invalid && Read(store, value, 100),
                "malformed key/fire life or absent component count rejects without mutation");
        Check(store.CapacityRefused == 0 && store.Aliases == 0, "invalid values do not masquerade as alias/capacity failures");
    }

    public static int Run()
    {
        try
        {
            _checks = 0;
            ExactIdentity(); ImmutableFirstAndAliases(); FullCapacityAndEightSlots();
            RetentionAndRollover(); LifeAndScopeReuse(); InvalidValues();
            Console.WriteLine($"PASS: {_checks} pure constructed timed-shot certificate storage assertions; no authenticated/native admission claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private enum Workload { Record, Duplicate, Read, FullCapacity }
    private static TimedShotCertificate[] BenchValues()
    {
        var values = new TimedShotCertificate[8 * TimedShotCertificateStore.PerSlot];
        for (int i = 0; i < values.Length; i++)
            values[i] = Fixture(i % 8, (uint)(i / 8 + 1), (uint)(i + 500));
        return values;
    }
    private static TimedShotCertificateStore BenchStore(TimedShotCertificate[] values, bool full)
    {
        var store = new TimedShotCertificateStore();
        if (full) foreach (var value in values)
            if (store.Remember(value, 100) != TimedShotRetention.Stored)
                throw new InvalidOperationException("benchmark storage preparation refused");
        return store;
    }
    private static long Loop(Workload workload, TimedShotCertificateStore store,
        TimedShotCertificate[] values, int iterations)
    {
        long checksum = 0;
        for (int i = 0; i < iterations; i++)
        {
            var value = values[i % values.Length];
            if (workload == Workload.Record)
            {
                // Fill every owner, then advance beyond TTL. No per-operation
                // store allocation; value construction is included in timing.
                uint now = unchecked(100u + (uint)(i / values.Length) * 1025u);
                value = value with { AuthorityFrame = now };
                if (store.Remember(value, now) != TimedShotRetention.Stored)
                    throw new InvalidOperationException("record benchmark did not store");
                checksum += value.Key.ShotId;
            }
            else if (workload == Workload.Read)
            {
                if (!store.TryGet(value.Key, value.Fire.SourceFrame, value.Room, 100, out var retained)
                    || retained != value) throw new InvalidOperationException("read benchmark changed evidence");
                checksum += retained.Components;
            }
            else
            {
                TimedShotRetention expected = TimedShotRetention.Duplicate;
                if (workload == Workload.FullCapacity)
                {
                    value = value with { Key = value.Key with { ShotId = 257 },
                        Fire = value.Fire with { ShotId = 257 } };
                    expected = TimedShotRetention.Capacity;
                }
                if (store.Remember(value, 100) != expected)
                    throw new InvalidOperationException("benchmark retention disposition changed");
                checksum += (int)expected + 1;
            }
        }
        return checksum;
    }

    public static int RunBenchmark(string[] args)
    {
        try
        {
            const int samples = 5, warmup = 10000, iterations = 20000;
            var values = BenchValues(); var rows = new object[4 * samples]; int row = 0;
            using var process = Process.GetCurrentProcess();
            foreach (var workload in Enum.GetValues<Workload>())
            {
                Loop(workload, BenchStore(values, workload != Workload.Record), values, warmup);
                for (int sample = 0; sample < samples; sample++)
                {
                    var store = BenchStore(values, workload != Workload.Record);
                    process.Refresh(); long cpuBefore = process.TotalProcessorTime.Ticks;
                    long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    long checksum = Loop(workload, store, values, iterations);
                    long stopwatchTicks = Stopwatch.GetTimestamp() - start;
                    long threadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                    process.Refresh(); long processCpuTicks = process.TotalProcessorTime.Ticks - cpuBefore;
                    rows[row++] = new { workload = workload.ToString(), sample, iterations, stopwatchTicks,
                        elapsedMilliseconds = stopwatchTicks * 1000.0 / Stopwatch.Frequency,
                        processCpuTicks, processCpuMilliseconds = processCpuTicks / (double)TimeSpan.TicksPerMillisecond,
                        threadAllocatedBytes, checksum, store.CapacityRefused, store.Aliases };
                }
            }
            string json = JsonSerializer.Serialize(new {
                fixture = "pure constructed values; no authenticated/native admission proof",
                runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                os = RuntimeInformation.OSDescription, stopwatchFrequency = Stopwatch.Frequency,
                samples, warmupOperationsPerWorkload = warmup, iterationsPerSample = iterations,
                caveat = "Raw local samples only. Process CPU includes runtime/background threads and timer overhead; thread allocation excludes setup, reporting and CPU timer queries. No comparative optimization or zero-allocation claim is inferred.",
                recordWorkload = "All eight owners fill 256 cells; authority time advances 1025 ticks between batches. Struct candidate construction is measured.",
                results = rows }, new JsonSerializerOptions { WriteIndented = true });
            if (args.Length > 1)
            {
                string path = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
            }
            Console.WriteLine(json); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
