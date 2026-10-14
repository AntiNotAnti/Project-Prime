using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.MapGen;

// Archive-I/O microbenchmark only. Compares the previous copy + byte[] path
// with the new streaming atomic copy, on the same machine and source bytes.
// It does not include compiler, network, rendering, or multiplayer Ready time.
string? output = null;
int megabytes = 32, repetitions = 3;
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg == "--output" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
    else if (arg == "--mib" && i + 1 < args.Length) megabytes = int.Parse(args[++i]);
    else if (arg == "--repeat" && i + 1 < args.Length) repetitions = int.Parse(args[++i]);
    else throw new ArgumentException("Usage: map-install-io-benchmark [--output FILE] [--mib N] [--repeat N]");
}
if (megabytes is < 1 or > 128 || repetitions is < 1 or > 10)
    throw new ArgumentOutOfRangeException("Microbenchmark budgets are 1-128 MiB and 1-10 repetitions.");

string root = Path.Combine(Path.GetTempPath(), "prime-install-io-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string source = Path.Combine(root, "input.bin");
    using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 65536, FileOptions.SequentialScan))
    {
        byte[] chunk = new byte[65536];
        var random = new Random(5142026);
        for (int i = 0; i < megabytes * 16; i++)
        {
            random.NextBytes(chunk);
            stream.Write(chunk);
        }
    }
    string expectedHash = MapBuildFingerprint.HashFile(source);
    var method = typeof(MapBuildFingerprint).Assembly
        .GetType("MphRead.Mods.MapGen.AtomicFile")!
        .GetMethod("Copy", BindingFlags.Public | BindingFlags.Static)!;
    var streaming = (Func<string, string, long>)method.CreateDelegate(
        typeof(Func<string, string, long>));

    var prior = new (double Ms, long Allocated)[repetitions];
    var optimized = new (double Ms, long Allocated)[repetitions];

    (double Ms, long Allocated) Measure(Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        return (watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // Alternate execution order to make obvious cache/GC biases visible.
    for (int i = 0; i < repetitions; i++)
    {
        string outputFile = Path.Combine(root, "output-" + i + ".bin");
        void Old()
        {
            string snapshot = Path.Combine(root, "old-snapshot-" + i + ".bin");
            string stage = outputFile + ".legacy-stage";
            try
            {
                File.Copy(source, snapshot);
                File.WriteAllBytes(stage, File.ReadAllBytes(snapshot));
                File.Move(stage, outputFile, overwrite: true);
                if (MapBuildFingerprint.HashFile(outputFile) != expectedHash)
                    throw new InvalidDataException("Historical copy simulation changed bytes.");
            }
            finally
            {
                if (File.Exists(snapshot)) File.Delete(snapshot);
                if (File.Exists(stage)) File.Delete(stage);
            }
        }
        void New()
        {
            long bytes = streaming(source, outputFile);
            if (bytes != (long)megabytes * 1024 * 1024
                || MapBuildFingerprint.HashFile(outputFile) != expectedHash)
                throw new InvalidDataException("Streamed archive copy changed bytes.");
        }
        if ((i & 1) == 0)
        {
            prior[i] = Measure(Old);
            optimized[i] = Measure(New);
        }
        else
        {
            optimized[i] = Measure(New);
            prior[i] = Measure(Old);
        }
        File.Delete(outputFile);
    }

    var report = new
    {
        schema = 1,
        name = "PR05 archive-I/O microbenchmark",
        sourceMiB = megabytes,
        repetitions,
        priorPath = "File.Copy(source,snapshot) + File.ReadAllBytes(snapshot) + atomic stage write",
        optimizedPath = "single streaming 64 KiB atomic destination copy",
        prior = new
        {
            milliseconds = prior.Select(x => x.Ms).ToArray(),
            threadAllocatedBytes = prior.Select(x => x.Allocated).ToArray(),
            medianMilliseconds = prior.Select(x => x.Ms).Order().ElementAt(repetitions / 2)
        },
        optimized = new
        {
            milliseconds = optimized.Select(x => x.Ms).ToArray(),
            threadAllocatedBytes = optimized.Select(x => x.Allocated).ToArray(),
            medianMilliseconds = optimized.Select(x => x.Ms).Order().ElementAt(repetitions / 2)
        },
        // Old preparation made a full snapshot and commit wrote a second
        // archive. New owned download path avoids snapshot; this I/O budget
        // does not include compiled runtime outputs or overwrite backups.
        logicalArchiveCopyBytesBefore = 2L * megabytes * 1024 * 1024,
        logicalArchiveCopyBytesAfter = 1L * megabytes * 1024 * 1024,
        caveats = new[]
        {
            "A local synthetic binary file is not a valid .ppmap; this is copy-stage evidence only.",
            "Warm filesystem cache, different OS disks and antivirus vary absolute timings.",
            "No claims about end-to-end joining/Ready, compiler cost or Android device speed."
        }
    };
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (output != null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, json + Environment.NewLine);
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}
