using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Private files already copied/validated on the worker. Publication only renames.
/// Staging is on each destination volume, so committing never falls back to a large copy.</summary>
internal sealed class MapFilePublication : IDisposable
{
    private sealed record Entry(string Stage, string Destination, FileStream Guard, long Length, DateTime Stamp);
    private readonly List<Entry> _files = new();
    private readonly List<string> _directories = new();
    private bool _committed, _preserveRecovery;

    internal string Stage(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string directory = Path.Combine(parent, ".map-stage-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _directories.Add(directory);
        string stage = Path.Combine(directory, "payload" + Path.GetExtension(destination));
        // No caller/editor path remains authoritative after this private copy.
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[65536];
            int count;
            while ((count = input.Read(buffer)) != 0)
            { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
            output.Flush(flushToDisk: true);
        }
        var guard = new FileStream(stage, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        _files.Add(new(stage, destination, guard, guard.Length, File.GetLastWriteTimeUtc(stage)));
        return stage;
    }

    internal void Commit(CancellationToken token)
    {
        if (_committed) throw new InvalidOperationException("Map publication was already committed.");
        token.ThrowIfCancellationRequested();
        foreach (var file in _files)
        {
            var info = new FileInfo(file.Stage);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.Length != file.Length || info.LastWriteTimeUtc != file.Stamp
                || !MapPublicationFileIdentity.Same(file.Guard, file.Stage))
                throw new IOException("Private prepared map output changed before publication.");
        }
        var published = new List<(Entry File, string? Backup, bool Installed)>();
        try
        {
            foreach (var file in _files)
            {
                token.ThrowIfCancellationRequested();
                string? backup = File.Exists(file.Destination)
                    ? Path.Combine(Path.GetDirectoryName(file.Stage)!, "previous") : null;
                if (backup != null) File.Move(file.Destination, backup);
                published.Add((file, backup, false));
                File.Move(file.Stage, file.Destination);
                published[^1] = (file, backup, true);
            }
            _committed = true;
        }
        catch
        {
            try
            {
                foreach (var previous in published.AsEnumerable().Reverse())
                {
                    if (previous.Installed) File.Move(previous.File.Destination, previous.File.Stage);
                    if (previous.Backup != null) File.Move(previous.Backup, previous.File.Destination);
                }
            }
            catch (Exception rollback)
            {
                // Keep prior files for recovery when an external file owner blocks
                // restoration. Disposing a failed transaction must not erase them.
                _preserveRecovery = true;
                throw new IOException("Map publication rollback was blocked. Recovery files retained in "
                    + string.Join(", ", _directories), rollback);
            }
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var file in _files) file.Guard.Dispose();
        foreach (string directory in _directories)
            if (!_preserveRecovery && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        _files.Clear(); _directories.Clear();
    }
}
