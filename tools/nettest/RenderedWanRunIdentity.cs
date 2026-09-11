using System;
using System.IO;

namespace MphRead.NetTest;

internal sealed record RenderedWanClientIdentity(
    Guid? SessionId, Guid? PlayerId, ulong? ConnectionId, int? Slot, int? UdpPort);

internal sealed record RenderedWanWorkerIdentity(
    Guid? WorkerId, Guid? Incarnation, int? ProcessId);

internal sealed record RenderedWanNodeIdentity(
    Guid? NodeId, Guid? Incarnation, int? Port);

internal sealed record RenderedWanRunIdentity(
    Guid RunId, string Scenario, DateTimeOffset StartedUtc,
    RenderedWanClientIdentity Client, RenderedWanWorkerIdentity Worker,
    RenderedWanNodeIdentity Node, int? WorkerPort, Guid? MatchId,
    uint? WireMatchId);

internal static class RenderedWanRunReservation
{
    internal static string ReserveNewDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        // A validation run is an artifact boundary. Reusing an empty folder
        // is unsafe because a delayed writer or stale capture can still make a
        // later report look like it belongs to this process.
        if (Directory.Exists(fullPath) || File.Exists(fullPath))
            throw new IOException("The validation output directory must not already exist.");
        string? parent = Path.GetDirectoryName(fullPath);
        if (parent is null) throw new ArgumentException("The validation output directory has no parent.");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(fullPath);
        string marker = Path.Combine(fullPath, ".run-reservation");
        try
        {
            using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.WriteThrough);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            // The directory was created by this reservation attempt. Leaving
            // it in place is deliberate: a subsequent run must refuse it as a
            // stale/contaminated output boundary.
            throw new IOException("The validation output directory could not be exclusively reserved.");
        }
        return fullPath;
    }
}
