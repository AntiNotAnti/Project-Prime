#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.StudioReplay;

public sealed partial class StudioReplayPlayer
{
    public static Task ValidateSourceAsync(string path, CancellationToken cancellation = default) => Task.Run(() =>
    {
        string source = Path.GetFullPath(path);
        if (source.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
        {
            if (!ReplayVirtualClips.TryLoad(source, out var clip) || clip == null)
                throw new InvalidDataException("The replay clip descriptor is invalid.");
            source = Path.GetFullPath(clip.SourceReplay);
            if (clip.SourceContentHash is { } bound) ReplaySourceHash.Verify(source, bound, cancellation);
            using var reader = DemoReader.Open(source, out var opened);
            if (reader == null || clip.EndFrame > reader.DurationFrames)
                throw new InvalidDataException("The replay clip range is outside its source recording: " + opened);
        }
        cancellation.ThrowIfCancellationRequested();
        ReplayOpenResult result = ReplayArchive.Validate(source, cancellation);
        if (result != ReplayOpenResult.Success) throw new InvalidDataException("Cannot open replay: " + result);
        using var prepared = PreparedReplaySource.File(source, cancellation: cancellation, prepareMap: false);
    }, cancellation);

    /// <summary>Save a canonical non-destructive clip descriptor and rebase its
    /// authored sidecars. The recording's bytes and existing sidecars remain unchanged.</summary>
    public Task<string> SaveClipProjectAsync(uint start, uint end, string destination, CancellationToken cancellation = default)
    {
        if (!Status.Ready || start >= end || end > Status.DurationFrames)
            throw new InvalidOperationException("Choose a valid range after replay preparation completes.");
        destination = Path.GetFullPath(destination);
        if (!destination.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Replay projects use the .ppclip extension.", nameof(destination));
        if (ReplayPathComparer.Comparer.Equals(destination, LogicalPath))
            throw new ArgumentException("Save As requires a different clip path.", nameof(destination));
        if (_preparedSourceContentHash == null || _preparedLogicalContentHash == null)
            throw new InvalidOperationException("The recording's immutable identity is not ready.");
        string source = Path.GetFullPath(_preparedClipDescriptor?.SourceReplay ?? LogicalPath);
        uint sourceStart = checked((_preparedClipDescriptor?.StartFrame ?? 0) + start);
        uint sourceEnd = checked((_preparedClipDescriptor?.StartFrame ?? 0) + end);
        string logicalHash = _preparedLogicalContentHash, sourceHash = _preparedSourceContentHash;
        var camera = _camera.Crop(start, end);
        var markers = Markers.ToArray();
        var tags = ReplayAnnotations.Tags(LogicalPath).ToArray(); var collections = ReplayAnnotations.Collections(LogicalPath).ToArray();
        return Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            // Authored clips retain the durable source reference. Refuse a replaced
            // descriptor or recording instead of silently adopting different bytes.
            ReplaySourceHash.Verify(LogicalPath, logicalHash, cancellation);
            if (!ReplayPathComparer.Comparer.Equals(source, LogicalPath)) ReplaySourceHash.Verify(source, sourceHash, cancellation);
            string directory = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(directory);
            string backup = Path.Combine(_cacheRoot, "save-backups", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
            string[] files = [destination, destination + ".camera", destination + ReplayAnnotations.Extension, destination + ReplayReels.Extension];
            bool[] existed = files.Select(File.Exists).ToArray();
            for (int i = 0; i < files.Length; i++) if (existed[i]) File.Copy(files[i], Path.Combine(backup, i.ToString()), true);
            try
            {
                var document = new ReplayVirtualClipDocument(1, source, sourceStart, sourceEnd, DateTime.UtcNow, Path.GetFileNameWithoutExtension(destination), sourceHash);
                string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(staging, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true })); cancellation.ThrowIfCancellationRequested(); File.Move(staging, destination, true); }
                finally { if (File.Exists(staging)) File.Delete(staging); }
                foreach (string file in files.Skip(1)) if (File.Exists(file)) File.Delete(file);
                if (camera.Keys.Count > 0 && !camera.SaveBound(destination,
                    new ReplayCameraTrackIdentity(destination, Convert.FromHexString(sourceHash), sourceStart, sourceEnd)))
                    throw new IOException(camera.LastError);
                ReplayAnnotations.SetOrganization(destination, tags, collections);
                foreach (var marker in markers)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (marker.Track == "Bookmarks")
                    { if (marker.StartFrame >= start && marker.StartFrame <= end) ReplayAnnotations.AddBookmark(destination, marker.StartFrame - start, marker.Name, end - start); }
                    else
                    {
                        uint left = Math.Max(start, marker.StartFrame), right = Math.Min(end, marker.EndFrame);
                        if (left >= right) continue;
                        if (marker.Track == "Reel") ReplayReels.Add(destination, left - start, right - start, marker.Name);
                        else ReplayAnnotations.AddHighlight(destination, left - start, right - start, marker.Name);
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                return destination;
            }
            catch
            {
                for (int i = 0; i < files.Length; i++)
                { if (existed[i]) File.Copy(Path.Combine(backup, i.ToString()), files[i], true); else if (File.Exists(files[i])) File.Delete(files[i]); }
                throw;
            }
            finally { Directory.Delete(backup, true); }
        }, cancellation);
    }
}
#endif
