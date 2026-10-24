using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

/// <summary>Bounded latest-only crash recovery for committed editor documents. Preview never calls this owner.
/// Publication and retirement share the same generation gate, so a queued stale draft cannot return after Save.</summary>
internal static class ReplayCameraDraftStore
{
    private const uint Magic=0x44435046; // FPCD
    private const int MaximumBytes=ReplayCameraTrack.MaxStateBytes+8192;
    private const int MaximumOwners=32;
    private sealed record Draft(ReplayCameraTrackIdentity Identity,string CameraRevision,byte[] State);
    private sealed class Owner
    {
        internal long Generation;
        internal Draft? Pending;
        internal bool Requested,Running,Retired;
        internal Task Worker=Task.CompletedTask;
        internal string Error="";
    }
    private static readonly object Gate=new();
    private static string _overflowError="";
    private static readonly Dictionary<string,Owner> Owners=new(ReplayPathComparer.Comparer);
    internal static string Error(string logical){lock(Gate)return Owners.TryGetValue(logical,out var owner)?owner.Error:_overflowError;}
    internal static Task PendingWrites(string logical){lock(Gate)return Owners.TryGetValue(logical,out var owner)?owner.Worker:Task.CompletedTask;}
    internal static void Queue(string logical,ReplayCameraTrackIdentity identity,string revision,byte[] state)
    {
        if(state.Length>ReplayCameraTrack.MaxStateBytes)throw new InvalidDataException("Camera recovery document exceeds its limit.");
        Request(logical,new(identity with{SourceContentHash=(byte[])identity.SourceContentHash.Clone()},revision,(byte[])state.Clone()));
    }
    internal static void Retire(string logical)=>Request(logical,null);
    private static void Request(string logical,Draft? draft)
    {
        logical=Path.GetFullPath(logical);
        lock(Gate)
        {
            if(!Owners.TryGetValue(logical,out var owner))
            {
                if(Owners.Count>=MaximumOwners)
                {
                    string? evict=null;foreach(var item in Owners)if(item.Value.Worker.IsCompleted&&(!item.Value.Retired||item.Value.Error.Length==0)){evict=item.Key;break;}
                    if(evict==null){_overflowError="Camera recovery writers are busy. The in-memory document is retained; Save remains available.";return;}
                    Owners.Remove(evict);
                }
                Owners.Add(logical,owner=new());
            }
            owner.Generation++;owner.Retired=draft==null;owner.Pending=draft;owner.Requested=true;owner.Error="";
            if(!owner.Running){owner.Running=true;owner.Worker=Task.Run(()=>Drain(logical,owner));}
        }
    }
    private static void Drain(string logical,Owner owner)
    {
        while(true)
        {
            Draft? draft;long generation;
            lock(Gate)
            {
                if(!owner.Requested){owner.Running=false;return;}
                draft=owner.Pending;owner.Pending=null;owner.Requested=false;generation=owner.Generation;
            }
            string? temporary=null;
            try
            {
                if(draft!=null)
                {
                    byte[] bytes=Encode(draft);temporary=logical+".camera.draft."+Guid.NewGuid().ToString("N")+".tmp";
                    using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {file.Write(bytes);file.Flush(flushToDisk:true);}
                }
                lock(Gate)
                {
                    if(generation!=owner.Generation)continue;
                    if(temporary==null)File.Delete(logical+".camera.draft");
                    else File.Move(temporary,logical+".camera.draft",overwrite:true);
                    owner.Error="";
                }
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {lock(Gate)if(generation==owner.Generation)owner.Error="Camera recovery draft could not be saved. The document remains in memory; use Save. "+error.Message;}
            finally
            {
                if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
    private static byte[] Encode(Draft draft)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(Magic);writer.Write((byte)1);writer.Write(draft.Identity.LogicalReplayId);writer.Write(draft.Identity.SourceContentHash);
        writer.Write(draft.Identity.StartFrame);writer.Write(draft.Identity.EndFrame);writer.Write(draft.CameraRevision);
        writer.Write(draft.State.Length);writer.Write(draft.State);writer.Flush();byte[] body=stream.ToArray();
        writer.Write(SHA256.HashData(body));writer.Flush();return stream.ToArray();
    }
    internal static bool TryRecover(string logical,ReplayCameraTrackIdentity identity,string revision,out byte[]? state)
    {
        state=null;logical=Path.GetFullPath(logical);string path=logical+".camera.draft";
        Owner? observed;long generation;
        lock(Gate)
        {
            Owners.TryGetValue(logical,out observed);generation=observed?.Generation??0;
            if(observed is {Retired:true} or {Requested:true} or {Running:true})return false;
        }
        try
        {
            if(!File.Exists(path))return false;
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length<80||file.Length>MaximumBytes)return false;
            byte[] bytes=new byte[(int)file.Length];file.ReadExactly(bytes);
            if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0,bytes.Length-32)),bytes.AsSpan(bytes.Length-32)))return false;
            using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
            if(reader.ReadUInt32()!=Magic||reader.ReadByte()!=1||!ReplayPathComparer.Comparer.Equals(reader.ReadString(),identity.LogicalReplayId)
                ||!CryptographicOperations.FixedTimeEquals(reader.ReadBytes(32),identity.SourceContentHash)
                ||reader.ReadUInt32()!=identity.StartFrame||reader.ReadUInt32()!=identity.EndFrame||reader.ReadString()!=revision)return false;
            int length=reader.ReadInt32();if(length<40||length>ReplayCameraTrack.MaxStateBytes||stream.Position+length+32!=bytes.Length)return false;
            byte[] recovered=reader.ReadBytes(length);var validate=new ReplayCameraTrack();
            if(!validate.ImportState(recovered))return false;
            lock(Gate)
            {
                Owners.TryGetValue(logical,out var current);
                if(!ReferenceEquals(current,observed)||(current?.Generation??0)!=generation
                    ||current is {Retired:true} or {Requested:true} or {Running:true})return false;
                state=recovered;return true;
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException)
        {return false;}
    }
}
