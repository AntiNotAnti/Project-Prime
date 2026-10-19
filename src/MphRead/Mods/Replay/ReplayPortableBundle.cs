#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;
/// <summary>Old Prime Studio v1 fixed-member portable format; no arbitrary archive extraction or map installation.</summary>
internal static class ReplayPortableBundle
{
    internal static void Export(string output,string source,byte[]? annotations,ReplayReelDocument reel,byte[] cameraState,MapContentIdentity? map,string? package,CancellationToken cancellation)
    {
        using var archive=ZipFile.Open(output,ZipArchiveMode.Create);long total=0;
        void Budget(long bytes){total=checked(total+bytes);if(total>1024L*1024*1024)throw new InvalidDataException("Portable bundle exceeds 1 GiB.");}
        void Entry(string name,byte[] bytes){Budget(bytes.Length);var entry=archive.CreateEntry(name);using var stream=entry.Open();stream.Write(bytes);}
        string Copy(string name,string path)
        {
            cancellation.ThrowIfCancellationRequested();var info=new FileInfo(path);
            if(!info.Exists||info.LinkTarget!=null||info.Length>1024L*1024*1024)throw new IOException("Portable members must be regular files no larger than 1 GiB.");
            Budget(info.Length);var entry=archive.CreateEntry(name,CompressionLevel.NoCompression);using var stream=entry.Open();using var input=File.OpenRead(path);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[65536];int count;
            while((count=input.Read(buffer))>0){cancellation.ThrowIfCancellationRequested();hash.AppendData(buffer,0,count);stream.Write(buffer,0,count);}
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        string replayHash=Copy("replay.ppdemo",source);
        if(annotations!=null)Entry("replay.ppdemo"+ReplayAnnotations.Extension,annotations);
        reel.SourceReplay="replay.ppdemo";if(reel.Segments.Count>0)Entry("replay.ppdemo"+ReplayReels.Extension,JsonSerializer.SerializeToUtf8Bytes(reel));
        bool included=false;
        if(map is{} required&&package!=null)
        {
            if(!MapContentIdentity.FromPackage(package).Matches(required))throw new InvalidDataException("The exact recorded map package is unavailable.");
            if(!StringComparer.OrdinalIgnoreCase.Equals(Copy("map.ppmap",package),required.PackageHash.ToString()))throw new IOException("Custom map changed while copying.");
            included=true;
        }
        Entry("manifest.json",JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Replay="replay.ppdemo",ReplayHash=replayHash,Map=included?"map.ppmap":null,
            RequiredMapId=map?.MapId,RequiredRoom=map?.RoomKey,RequiredContentHash=map?.ContentHash.ToString(),RequiredPackageHash=map?.PackageHash.ToString(),CameraState=cameraState}));
        cancellation.ThrowIfCancellationRequested();
    }
    internal static string Import(string bundle,string directory,CancellationToken cancellation)
    {
        try{return ImportCore(bundle,directory,cancellation);}
        catch(Exception error)when(error is JsonException or System.Collections.Generic.KeyNotFoundException or FormatException or OverflowException)
        {throw new InvalidDataException("Invalid portable replay manifest.",error);}
    }
    private static string ImportCore(string bundle,string directory,CancellationToken cancellation)
    {
        ReplayPortableArchive.Extract(bundle,directory,cancellation);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));var data=manifest.RootElement;
        if(data.GetProperty("Version").GetInt32()!=1||data.GetProperty("Replay").GetString()!="replay.ppdemo")throw new InvalidDataException("Unsupported portable replay manifest.");
        string replay=Path.Combine(directory,"replay.ppdemo");
        using(var input=File.OpenRead(replay))if(!StringComparer.OrdinalIgnoreCase.Equals(data.GetProperty("ReplayHash").GetString(),Convert.ToHexString(SHA256.HashData(input))))throw new InvalidDataException("Portable replay content hash differs from the manifest.");
        using var parsed=new ReplayPlaybackSession(new PassiveReplaySessionHost());if(!parsed.JoinDetached(replay,cancellation))throw new InvalidDataException(parsed.LastError);
        var required=parsed.Metadata?.CustomMapIdentity;string? member=data.GetProperty("Map").GetString();
        if(required is{} identity)
        {
            if(!StringComparer.OrdinalIgnoreCase.Equals(data.GetProperty("RequiredPackageHash").GetString(),identity.PackageHash.ToString())
                ||!StringComparer.OrdinalIgnoreCase.Equals(data.GetProperty("RequiredContentHash").GetString(),identity.ContentHash.ToString())
                ||!StringComparer.OrdinalIgnoreCase.Equals(data.GetProperty("RequiredRoom").GetString(),identity.RoomKey)||data.GetProperty("RequiredMapId").GetGuid()!=identity.MapId)
                throw new InvalidDataException("Portable map reference differs from the recording.");
            if(member!=null&&(member!="map.ppmap"||!MapContentIdentity.FromPackage(Path.Combine(directory,member)).Matches(identity)))throw new InvalidDataException("Portable custom map bytes do not match the recording.");
        }
        else if(member!=null||data.GetProperty("RequiredPackageHash").ValueKind!=JsonValueKind.Null)throw new InvalidDataException("Portable replay declares an unrecorded map.");
        if(File.Exists(Path.Combine(directory,"map.ppmap"))&&member==null)throw new InvalidDataException("Portable replay carries an undeclared map.");
        if(data.TryGetProperty("CameraState",out var saved)&&saved.ValueKind!=JsonValueKind.Null)
        {
            var track=new ReplayCameraTrack();if(!track.ImportState(saved.Deserialize<byte[]>()??throw new InvalidDataException("Missing camera state.")))throw new InvalidDataException(track.LastError);
            if(track.WindowDuration is{} duration&&duration!=parsed.LastFrame)throw new InvalidDataException("Portable camera window differs from the recording.");
            if(!track.Save(replay))throw new IOException(track.LastError);
        }
        var reelFile=ReplayReviewedMetadata.Read(replay+ReplayReels.Extension);
        if(reelFile.Bytes!=null)
        {
            var document=JsonSerializer.Deserialize<ReplayReelDocument>(reelFile.Bytes)??throw new InvalidDataException("Invalid portable reel.");
            if(document.SourceReplay!="replay.ppdemo")throw new InvalidDataException("Portable reel refers outside the bundle.");
            document.SourceReplay=replay;File.WriteAllText(reelFile.Path,JsonSerializer.Serialize(document));
            _=ReplayReels.ReadReviewed(ReplayReviewedMetadata.Read(reelFile.Path),replay,parsed.LastFrame);
        }
        _=ReplayAnnotations.ReadReviewed(ReplayReviewedMetadata.Read(replay+ReplayAnnotations.Extension),parsed.LastFrame);
        cancellation.ThrowIfCancellationRequested();return replay;
    }
}
#endif
