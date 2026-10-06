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
        string source = LogicalPath;
        uint sourceStart = start, sourceEnd = end;
        if (ReplayVirtualClips.TryLoad(source, out var parent) && parent != null)
        { sourceStart = checked(parent.StartFrame + start); sourceEnd = checked(parent.StartFrame + end); source = Path.GetFullPath(parent.SourceReplay); }
        var keys = _camera.Keys.Where(k => k.Frame >= start && k.Frame <= end).Select(k => k with { Frame = k.Frame - start }).ToList();
        if (_camera.Keys.Count > 1 && start > _camera.Keys[0].Frame && start < _camera.Keys[^1].Frame && _camera.Sample(start, out var first) && keys.All(k => k.Frame != 0)) keys.Add(first with { Frame = 0 });
        if (_camera.Keys.Count > 1 && end > _camera.Keys[0].Frame && end < _camera.Keys[^1].Frame && _camera.Sample(end, out var last) && keys.All(k => k.Frame != end - start)) keys.Add(last with { Frame = end - start });
        if (_camera.Keys.Count > 0 && keys.Count == 0 && _camera.Sample(start,out var held))keys.Add(held with { Frame=0 });
        var markers = Markers.ToArray();
        var tags = ReplayAnnotations.Tags(LogicalPath).ToArray(); var collections = ReplayAnnotations.Collections(LogicalPath).ToArray();
        return Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            string directory = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(directory);
            string backup = Path.Combine(_cacheRoot, "save-backups", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
            string[] files = [destination, destination + ".camera", destination + ReplayAnnotations.Extension, destination + ReplayReels.Extension];
            bool[] existed = files.Select(File.Exists).ToArray();
            for (int i = 0; i < files.Length; i++) if (existed[i]) File.Copy(files[i], Path.Combine(backup, i.ToString()), true);
            try
            {
                var document = new ReplayVirtualClipDocument(1, source, sourceStart, sourceEnd, DateTime.UtcNow, Path.GetFileNameWithoutExtension(destination));
                string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(staging, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true })); cancellation.ThrowIfCancellationRequested(); File.Move(staging, destination, true); }
                finally { if (File.Exists(staging)) File.Delete(staging); }
                foreach (string file in files.Skip(1)) if (File.Exists(file)) File.Delete(file);
                var camera = new ReplayCameraTrack(); foreach (var key in keys.OrderBy(k => k.Frame)) if(!camera.Put(key))throw new InvalidDataException(camera.LastError);
                if (keys.Count > 0 && !camera.Save(destination)) throw new IOException(camera.LastError);
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
