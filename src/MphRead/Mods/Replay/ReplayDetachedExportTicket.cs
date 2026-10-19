#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal sealed record ReplayDetachedExportTicket(int Version,Guid Id,string WorkDirectory,
    ReplayVideoExportManifest Manifest,ReplayExportPresentation Presentation,string RuntimeHash,
    string MphKey,string FhKey,string MapDirectory,string UserMapDirectory,ulong GeometryHash,int Subject,
    IReadOnlyDictionary<string,string> InputHashes)
{
    internal string StatusPath=>Path.Combine(WorkDirectory,"status.json");
    internal string CancelPath=>Path.Combine(WorkDirectory,"cancel");
    internal string LogPath=>Path.Combine(WorkDirectory,"worker.log");
    internal static ReplayDetachedExportTicket Read(string path)
    {
        path=Path.GetFullPath(path);var info=new FileInfo(path);
        if(!info.Exists||info.LinkTarget!=null||info.Length is <1 or >16*1024*1024)
            throw new InvalidDataException("The export ticket is unavailable or exceeds its size limit.");
        var ticket=JsonSerializer.Deserialize<ReplayDetachedExportTicket>(File.ReadAllBytes(path),ReplayDetachedExportFiles.Json)
            ??throw new InvalidDataException("The export ticket is empty.");
        if(ticket.Version!=1||ticket.Id==Guid.Empty||ticket.WorkDirectory!=Path.GetDirectoryName(path)
            ||ticket.GeometryHash==0||ticket.Subject is <0 or >7||ticket.Manifest==null||ticket.Presentation==null
            ||ticket.InputHashes==null||ticket.InputHashes.Count is <1 or >66
            ||!ticket.InputHashes.ContainsKey(ticket.Manifest.Replay)
            ||!ticket.InputHashes.ContainsKey(Path.Combine(ticket.WorkDirectory,"paths.txt"))
            ||ReplayDetachedExportFiles.Hash(ReplayDetachedExportFiles.RuntimeImage)!=ticket.RuntimeHash)
            throw new InvalidDataException("The export ticket or its runtime identity is invalid.");
        string prefix=ticket.WorkDirectory+Path.DirectorySeparatorChar;
        foreach(var input in ticket.InputHashes)
        {
            if(!Path.IsPathFullyQualified(input.Key)||!input.Key.StartsWith(prefix,StringComparison.Ordinal)
                ||new FileInfo(input.Key).LinkTarget!=null||ReplayDetachedExportFiles.Hash(input.Key)!=input.Value)
                throw new IOException("A private export input changed before its worker opened it.");
        }
        return ticket;
    }
}

internal sealed record ReplayDetachedExportStatus(Guid Id,int ProcessId,ReplayExportState State,string Message,
    float Progress,int Frames,string? Output,string? Error,DateTime WrittenUtc);

internal static class ReplayDetachedExportFiles
{
    internal static string RuntimeImage
    {
        get
        {
            string managed=Path.Combine(AppContext.BaseDirectory,"ProjectPrime.dll");
            return File.Exists(managed)?managed:Environment.ProcessPath??throw new IOException("The export runtime image is unavailable.");
        }
    }
    internal static readonly JsonSerializerOptions Json=CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options=new JsonSerializerOptions{WriteIndented=true};
        // OpenTK exposes vector swizzle properties. Explicit components prevent
        // recursive swizzle serialization and retain every authored key/tangent.
        options.Converters.Add(new VectorConverter());options.Converters.Add(new RotationConverter());return options;
    }
    private sealed class VectorConverter:JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {using var value=JsonDocument.ParseValue(ref reader);var a=value.RootElement;if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=3)throw new JsonException("Invalid camera vector.");return new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());}
        public override void Write(Utf8JsonWriter writer,Vector3 value,JsonSerializerOptions options)
        {writer.WriteStartArray();writer.WriteNumberValue(value.X);writer.WriteNumberValue(value.Y);writer.WriteNumberValue(value.Z);writer.WriteEndArray();}
    }
    private sealed class RotationConverter:JsonConverter<Quaternion>
    {
        public override Quaternion Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {using var value=JsonDocument.ParseValue(ref reader);var a=value.RootElement;if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=4)throw new JsonException("Invalid camera rotation.");return new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle(),a[3].GetSingle());}
        public override void Write(Utf8JsonWriter writer,Quaternion value,JsonSerializerOptions options)
        {writer.WriteStartArray();writer.WriteNumberValue(value.X);writer.WriteNumberValue(value.Y);writer.WriteNumberValue(value.Z);writer.WriteNumberValue(value.W);writer.WriteEndArray();}
    }
    internal static string Hash(string path)
    {using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Hash(input,CancellationToken.None);}
    internal static string Hash(Stream input,CancellationToken token)
    {
        input.Position=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[65536];int read;
        while((read=input.Read(buffer))>0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal static void WriteStatus(string path,ReplayDetachedExportStatus status)=>Write(path,status);
    internal static void Write<T>(string path,T value)
    {
        string staging=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {JsonSerializer.Serialize(output,value,Json);output.Flush(true);}
            File.Move(staging,path,overwrite:true);
        }
        finally{if(File.Exists(staging))File.Delete(staging);}
    }
    internal static string PlaybackHash(string reviewed)
    {
        const string marker=":PLAYBACK-SHA:";int start=reviewed.LastIndexOf(marker,StringComparison.Ordinal);
        if(start<0||reviewed.Length<start+marker.Length+64)throw new InvalidDataException("The export review has no playback content identity.");
        string hash=reviewed.Substring(start+marker.Length,64);
        if(hash.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("The playback content identity is invalid.");
        return hash;
    }
}
#endif
