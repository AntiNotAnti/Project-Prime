using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using FruityPrime.Server.Shared;
using MphRead.Identity;

namespace FruityPrime.Server.Worker.Reporting;

/// <summary>Runs after immutable completion capture, outside simulation. No Backend transport or credentials.</summary>
public static class WorkerReportArtifactWriter
{
    public static MatchReportReady Write(string root, WorkerId workerId, Guid workerIncarnation,
        MatchSpec spec, uint wireMatchId, MatchReportV1 snapshot)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Artifact root must be absolute.");
        // Bind launch metadata; preserve measured outcomes, schema and participant evidence unchanged.
        MatchReportV1 report = snapshot with { ContentHash = spec.Content.ContentHash, TrustClass = spec.TrustClass,
            TournamentId = spec.TournamentId?.ToString("D"), RoundId = spec.RoundId?.ToString("D") };
        MatchReportBinding.Validate(spec, wireMatchId, report);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report);
        Guid reportId = spec.MatchId.Value;
        var ready = new MatchReportReady(spec.MatchId, reportId, workerId, workerIncarnation,
            Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        ready.Validate();
        string directory = Path.Combine(root, "reports"); Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Artifact directory cannot be a link.");
        string path = Path.Combine(directory, reportId.ToString("N") + ".json");
        if (File.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != bytes.Length
                || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new IOException("Conflicting immutable report artifact.");
            return ready;
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            if (OperatingSystem.IsWindows())
            { if (!MoveFileEx(temporary, path, 8)) throw new IOException("Durable artifact rename failed."); }
            else
            {
                File.Move(temporary, path, overwrite: false);
                int descriptor = open(directory, 0);
                if (descriptor < 0) throw new IOException("Artifact directory synchronization failed.");
                try { if (fsync(descriptor) != 0) throw new IOException("Artifact directory synchronization failed."); }
                finally { close(descriptor); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return ready;
    }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int descriptor);
    [DllImport("libc")] private static extern int close(int descriptor);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string replacement, uint flags);
}
