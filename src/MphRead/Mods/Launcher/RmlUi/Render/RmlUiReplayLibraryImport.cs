#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal sealed partial class RmlUiLiveMenuServices
{
    internal static string ImportStudioRecording(string source,CancellationToken token)
    {
        source=Path.GetFullPath(source);token.ThrowIfCancellationRequested();
        var info=new FileInfo(source);
        if(!source.EndsWith(DemoFile.Extension,StringComparison.OrdinalIgnoreCase)||!info.Exists||info.LinkTarget!=null)
            throw new IOException("Choose an existing regular .ppdemo recording.");
        for(var parent=info.Directory;parent!=null;parent=parent.Parent)
            if(parent.LinkTarget!=null)throw new IOException("Recording sources cannot use symbolic-link ancestors.");
        using var lease=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(ReplayPathComparer.Same(info.DirectoryName!,DemoLibrary.Directory))
        {
            token.ThrowIfCancellationRequested();
            ReplayOpenResult validation=ReplayArchive.Validate(source);
            token.ThrowIfCancellationRequested();
            if(validation!=ReplayOpenResult.Success)throw new InvalidDataException("Recording integrity validation failed.");
            using var session=new ReplayPlaybackSession(new PassiveReplaySessionHost());
            if(!session.JoinDetached(source,token))throw new InvalidDataException(session.LastError);
            _=ReplayAnnotations.ReadReviewed(ReplayReviewedMetadata.Read(source+ReplayAnnotations.Extension),session.LastFrame);
            _=ReplayReels.ReadReviewed(ReplayReviewedMetadata.Read(source+ReplayReels.Extension),source,session.LastFrame);
            var camera=new ReplayCameraTrack();if(!camera.Load(source))throw new InvalidDataException(camera.LastError);
            var identity=ReplayCameraTrackIdentity.Create(source);
            _=RmlUiReplayProjectPreferences.Capture(source,identity.SourceContentHash,identity.StartFrame,identity.EndFrame,token);
            return source;
        }
        Directory.CreateDirectory(DemoLibrary.Directory);
        if(new DirectoryInfo(DemoLibrary.Directory).LinkTarget!=null)throw new IOException("The replay library is a symbolic link.");
        string imported=Path.Combine(DemoLibrary.Directory,"opened_"+Guid.NewGuid().ToString("N")+DemoFile.Extension);
        ImportReplayWithSidecars(source,imported,token);
        return imported;
    }

    // Library publication owns fresh files only. Studio import remains private and never installs maps.
    private static void ImportReplayWithSidecars(string source, string destination, CancellationToken token)
    {
        string? staging = null;
        bool published = false;
        try
        {
            if (MphRead.Mods.Launcher.RmlUi.Core.RmlUiReplayFileNames.IsPortableBundle(source))
            {
                staging = Path.Combine(DemoLibrary.Directory, "exports", "import_" + Guid.NewGuid().ToString("N"));
                source = ReplayPortableBundle.Import(source, staging, token);
                string package = Path.Combine(staging, "map.ppmap");
                if (File.Exists(package))
                {
                    // Existing installation authority rejects an active replay/match/map reader.
                    // Hold configured files throughout preparation and exact package publication.
                    using var files = GameFiles.TryUseConfiguredFiles(GameFiles.Root)
                        ?? throw new InvalidOperationException("Game files are being changed.");
                    var identity = MapContentIdentity.FromPackage(package);
                    MapRuntimeUsage.RequireInstallationAllowed(identity.RoomKey);
                    using var prepared = MapPackageInstaller.PrepareAsync(package, identity, token).GetAwaiter().GetResult();
                    token.ThrowIfCancellationRequested();
                    var installed = prepared.Commit(CustomRooms.UserMapDirectory);
                    Metadata.RegisterDownloadedMap(installed);
                }
            }
            using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            if (!session.JoinDetached(source, token)) throw new InvalidDataException(session.LastError);
            var annotations = ReplayReviewedMetadata.Read(source + ReplayAnnotations.Extension);
            _ = ReplayAnnotations.ReadReviewed(annotations, session.LastFrame);
            var reelFile = ReplayReviewedMetadata.Read(source + ReplayReels.Extension);
            var reel = ReplayReels.ReadReviewed(reelFile, source, session.LastFrame);
            var cameraFile = ReplayReviewedMetadata.Read(source + ".camera");
            var camera = new ReplayCameraTrack();
            if (cameraFile.Bytes != null && !camera.Load(source)) throw new InvalidDataException(camera.LastError);
            var sourceIdentity=ReplayCameraTrackIdentity.Create(source);
            var preferences=RmlUiReplayProjectPreferences.Capture(source,sourceIdentity.SourceContentHash,sourceIdentity.StartFrame,sourceIdentity.EndFrame,token);
            CopyReplay(source, destination, token, validate: true);
            published = true;
            if (annotations.Bytes != null) WriteNew(destination + ReplayAnnotations.Extension, annotations.Bytes);
            if (reelFile.Bytes != null)
            {
                // The recording acquired a new library identity; its reel follows that exact recording.
                reel.SourceReplay = destination;
                WriteNew(destination + ReplayReels.Extension, JsonSerializer.SerializeToUtf8Bytes(reel));
            }
            if (cameraFile.Bytes != null && !camera.Save(destination)) throw new IOException(camera.LastError);
            RmlUiReplayProjectPreferences.Stage(destination,preferences,sourceIdentity.SourceContentHash,0,0,token);
            RmlUiReplayProjectPreferences.Validate(preferences);
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            if (published) DemoLibrary.Delete(destination);
            throw;
        }
        finally
        {
            if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        static void WriteNew(string path, byte[] bytes)
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(bytes); output.Flush(flushToDisk: true);
        }
    }
}
#endif
