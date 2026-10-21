using System.Text.Json;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

// Asset-free admission of actual world13 capsules exported by the GL history source.
internal static class CustomReplayHeadHistoryChecks
{
    internal static int Run(string input,string output)
    {
        int checks=0;
        try
        {
            input=Path.GetFullPath(input);output=Path.GetFullPath(output);Directory.CreateDirectory(output);string loaded=typeof(ReplayWorldCheckpoint).Assembly.Location;string loadedSha=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loaded)));Console.WriteLine("Loaded ProjectPrime: "+loaded+" SHA256="+loadedSha);
            void Check(bool ok,string label){if(!ok)throw new InvalidDataException(label);checks++;}
            void Reject(byte[] bytes,bool native,string label){try{using var world=native?ReplayWorldCheckpoint.FromBytes(bytes):ReplayWorldCheckpoint.FromCustomBytes(bytes);}catch(InvalidDataException){Check(true,label);return;}throw new InvalidDataException("Expected parser refusal: "+label);}
            void RejectTruncated(byte[] bytes,string label){try{using var world=ReplayWorldCheckpoint.FromCustomBytes(bytes);}catch(Exception error)when(error is InvalidDataException or EndOfStreamException){Check(true,label);return;}throw new InvalidDataException("Expected truncated outer-capsule refusal: "+label);}
            string manifest=Path.Combine(input,"file-expected-worlds.json");if(new FileInfo(manifest).Length is <=0 or >65536)throw new InvalidDataException("Bounded world manifest required.");using var json=JsonDocument.Parse(File.ReadAllBytes(manifest));var rows=json.RootElement.EnumerateArray().Select(row=>(Name:row.GetProperty("name").GetString()!,Frame:row.GetProperty("frame").GetUInt32(),File:row.GetProperty("filename").GetString()!)).ToArray();Check(rows.Length is >0 and <=8,"at most eight actual world13 targets");var baseline=rows.Single(row=>row.Name=="baseline");long total=0;byte[]? originalConstruction=null,descriptor=null;var results=new List<object>();
            foreach(var row in rows)
            {
                if(Path.GetFileName(row.File)!=row.File)throw new InvalidDataException("World input must be a local basename.");string path=Path.Combine(input,row.File);long length=new FileInfo(path).Length;if(length<=0||length>Math.Min(ReplayWorldCheckpoint.MaximumBytes,8*1024*1024)||(total+=length)>8*1024*1024)throw new InvalidDataException("Eight MiB total world budget.");byte[] bytes=File.ReadAllBytes(path);using var world=ReplayWorldCheckpoint.FromCustomBytes(bytes);Check(world.HasHeadHistoryContract&&bytes[4]==13,"actual capsule declares world13 head-history capability "+row.Name);Check(world.HeadHistoryOrigin==baseline.Frame&&world.Frame==row.Frame&&world.Frame>=baseline.Frame,"actual origin and captured recording frame consistent "+row.Name);var initial=new ReplayReplicaState();initial.RestoreCustomCheckpoint(world.ConstructionState());var current=new ReplayReplicaState();current.RestoreCustomCheckpoint(world.DecoderState());Check(initial.CustomWeaponDescriptor!=null&&current.CustomWeaponDescriptor!=null&&initial.CustomWeaponDescriptor.CopyBytes().SequenceEqual(current.CustomWeaponDescriptor.CopyBytes())&&current.RecordingFrame==row.Frame,"construction/current custom contexts and clock agree "+row.Name);originalConstruction??=world.ConstructionState().Bytes.ToArray();descriptor??=current.CustomWeaponDescriptor!.CopyBytes();Check(originalConstruction.SequenceEqual(world.ConstructionState().Bytes)&&descriptor.SequenceEqual(current.CustomWeaponDescriptor!.CopyBytes()),"all actual capsules preserve original construction and exact descriptor "+row.Name);Reject(bytes,native:true,"native FromBytes refuses custom world13 "+row.Name);
                foreach(var mutation in new[]{(At:4,Value:(byte)14,Name:"future version14"),(At:6,Value:(byte)(bytes[6]&~16),Name:"missing head flag16"),(At:6,Value:(byte)(bytes[6]|32),Name:"unknown flag32")}){byte[] bad=(byte[])bytes.Clone();bad[mutation.At]=mutation.Value;Reject(bad,native:false,mutation.Name+" "+row.Name);}RejectTruncated(bytes[..^1],"truncated actual capsule "+row.Name);Reject(bytes.Concat(new byte[]{0}).ToArray(),native:false,"trailing actual capsule "+row.Name);results.Add(new{row.Name,row.Frame,origin=world.HeadHistoryOrigin,bytes=bytes.Length,sha256=Convert.ToHexStringLower(SHA256.HashData(bytes))});
            }
            Reject(new byte[ReplayWorldCheckpoint.MaximumBytes+1],native:false,"maximum capsule budget");File.WriteAllText(Path.Combine(output,"head-history-parser-results.json"),JsonSerializer.Serialize(new{checks,loadedAssembly=loaded,loadedSha256=loadedSha,totalBytes=total,results,scope="Asset-free parsing of actual supplied world13 history capsules on the loaded assembly. Exact admission/context/origin/header bounds; no Scene, GL, audio, head animation or authority execution claim."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {checks} actual world13 head-history parser checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("HEAD HISTORY PARSER FAIL "+error);return 1;}
    }
}
