using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;

internal static class CustomReplayCheckpointReopenChecks
{
    internal static int Run(string glOutput,string output)
    {
        int checks=0;
        void Check(bool ok,string label){if(!ok)throw new InvalidDataException(label);checks++;}
        byte[] Read(string name,int maximum)
        {
            using var stream=new FileStream(Path.Combine(glOutput,name),FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length<=0||stream.Length>maximum)throw new InvalidDataException("Excessive diagnostic input: "+name);
            byte[] bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);
            if(stream.ReadByte()!=-1)throw new InvalidDataException("Diagnostic input changed.");return bytes;
        }
        try
        {
            byte[] descriptorBytes=Read("descriptor.bin",ReplayCustomWeaponDescriptor.MaximumBytes);
            Check(ReplayCustomWeaponDescriptor.TryRead(descriptorBytes,out var descriptor),"cold descriptor parses");
            using var binding=descriptor!.Bootstrap.PrepareHistorical(new WeaponPackageStore(Path.Combine(glOutput,"server-store")));
            var context=descriptor.Bind(binding.Definition(new WeaponRuntimeId(10)));
            byte[] finalBytes=Read("final-checkpoint.bin",ReplayCustomWeaponState.MaximumCheckpointBytes);
            var final=ReplayCustomWeaponState.RestoreCheckpointBytes(finalBytes,context);
            Check(final.CaptureCheckpointBytes().AsSpan().SequenceEqual(finalBytes),"cold final canonical byte parity");
            using var json=JsonDocument.Parse(Read("accepted-facts-results.json",4*1024*1024));
            var root=json.RootElement;var clocks=root.GetProperty("passiveRestore");
            Check(Convert.ToHexString(SHA256.HashData(finalBytes))==root.GetProperty("checkpointSha256").GetString(),"cold final SHA matches receipt");
            Check(final.Sequence==clocks.GetProperty("finalSequence").GetUInt64()&&final.World==clocks.GetProperty("finalWorld").GetUInt32()&&final.RecordingFrame==clocks.GetProperty("finalRecordingFrame").GetUInt32(),"cold exact clocks match receipt");
            Check(context.Digest.ToString()==root.GetProperty("context").GetString(),"cold exact context matches receipt");
            bool reloaded=false;var finalSlots=new List<object>();for(int i=0;i<8;i++)if(final.TrySlot(i,out var slot)&&slot.HasWeapon)
            {Console.WriteLine($"FINAL SLOT {i}: magazine={slot.State.Magazine} reserve={slot.State.Reserve} phase={slot.State.Phase} optic={slot.State.Optic}");finalSlots.Add(new{slot=i,slot.State.Magazine,slot.State.Reserve,phase=slot.State.Phase.ToString(),slot.State.Optic});if(slot.State.Magazine==6&&slot.State.Reserve==22&&slot.State.Phase==CustomWeaponPhase.Ready)reloaded=true;}
            Check(reloaded,"cold actual final reloaded six/twentytwo Ready");
            byte[] facts=Read("accepted-facts.bin",32*1024*1024);int offset=0,count=0;
            var rows=root.GetProperty("facts");Check(rows.GetArrayLength()<=8192,"bounded fact receipt count");
            bool historicalOptic4=false;var linear=new ReplayCustomWeaponState(context);
            var first=ReplayCustomWeaponState.RestoreCheckpointBytes(Read("first-complete-checkpoint.bin",ReplayCustomWeaponState.MaximumCheckpointBytes),context);
            ReplayReplicaState? inspectionLinear=null,inspectionRestored=null;byte[]? inspectionBytes=null;uint inspectionRng1=0,inspectionRng2=0;
            if(File.Exists(Path.Combine(glOutput,"inspection-descriptor.bin")))
            {
                Check(ReplayCustomWeaponDescriptor.TryRead(Read("inspection-descriptor.bin",ReplayCustomWeaponDescriptor.MaximumBytes),out var inspectionDescriptor),"cold V2 inspection descriptor parses");
                Check(inspectionDescriptor!.InspectContext().Digest==context.Digest,"cold pure inspection context exact digest");
                inspectionBytes=Read("final-replica-checkpoint.bin",ReplayReplicaCheckpoint.MaximumCustomBytes);var checkpoint=new ReplayReplicaCheckpoint(inspectionBytes);
                ReplayReplicaState.InspectCustomCheckpointLayout(checkpoint);inspectionRestored=new ReplayReplicaState();inspectionRestored.RestoreCustomCheckpoint(checkpoint);
                bool refused=false;try{new ReplayReplicaState().RestoreCheckpoint(checkpoint);}catch(InvalidDataException){refused=true;}Check(refused,"default native restore refuses custom version fifteen");
                inspectionLinear=new ReplayReplicaState();inspectionLinear.EnableCustomInspection(inspectionDescriptor);inspectionLinear.Accept(Read("inspection-match.bin",64*1024),root.GetProperty("inspectionMatchFrame").GetUInt32());inspectionRng1=inspectionLinear.Rng1;inspectionRng2=inspectionLinear.Rng2;
            }
            foreach(var row in rows.EnumerateArray())
            {
                Check(offset<=facts.Length-4,"fact length prefix present");int length=BinaryPrimitives.ReadInt32LittleEndian(facts.AsSpan(offset));offset+=4;
                Check(length>0&&length<=facts.Length-offset,"bounded fact payload");var bytes=facts.AsSpan(offset,length);offset+=length;
                Check(length==row.GetProperty("bytes").GetInt32()&&Convert.ToHexString(SHA256.HashData(bytes)).Equals(row.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase),"copied payload hash matches receipt");
                Check(ReplayCustomWeaponFact.TryRead(bytes,context,out var fact)&&fact!.Sequence==(ulong)count+1&&fact.Sequence==row.GetProperty("sequence").GetUInt64()&&fact.World==row.GetProperty("world").GetUInt32(),"cold contiguous exact fact");
                uint frame=row.GetProperty("RecordingFrame").GetUInt32();linear.Accept(fact!,frame);inspectionLinear?.Accept(bytes,frame);if(fact!.Sequence>first.Sequence)first.Accept(fact,frame);count++;
                for(int i=0;i<8;i++)if(linear.TrySlot(i,out var historical)&&historical.HasWeapon&&historical.State.Optic==4)historicalOptic4=true;
            }
            Check(historicalOptic4,"cold actual historical tables preserve optic four");
            Check(offset==facts.Length&&count==root.GetProperty("count").GetInt32(),"complete diagnostic stream consumed");
            linear.Advance(final.World,final.RecordingFrame);first.Advance(final.World,final.RecordingFrame);
            Check(linear.CaptureCheckpointBytes().AsSpan().SequenceEqual(finalBytes),"cold full facts reproduce final canonical slots pickups flights and history");
            Check(first.CaptureCheckpointBytes().AsSpan().SequenceEqual(finalBytes),"cold first checkpoint plus actual suffix reproduces final canonical state");
            if(inspectionLinear!=null)
            {
                Check(inspectionLinear.CaptureCheckpoint().Bytes.SequenceEqual(inspectionBytes!)&&inspectionRestored!.CaptureCheckpoint().Bytes.SequenceEqual(inspectionBytes!),"cold actual Match and full custom facts reproduce final replica fifteen");
                Check(inspectionLinear.CustomWeapons!.CaptureCheckpointBytes().AsSpan().SequenceEqual(finalBytes)&&inspectionRestored!.CustomWeapons!.CaptureCheckpointBytes().AsSpan().SequenceEqual(finalBytes),"cold replica embedded CWRS exact parity");
                Check(inspectionLinear.Rng1==inspectionRng1&&inspectionLinear.Rng2==inspectionRng2&&inspectionRestored!.Rng1==inspectionRng1&&inspectionRestored.Rng2==inspectionRng2,"cold inspection RNG unchanged");
            }
            Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"checkpoint-reopen-results.json"),JsonSerializer.Serialize(new{checks,count,customReplicaInspection=inspectionLinear!=null,finalSlots,historicalOptic4,final.Sequence,final.World,final.RecordingFrame,checkpointSha256=Convert.ToHexString(SHA256.HashData(finalBytes)),scope="Separate cold process, exact historical package binding and detached byte checkpoint/fact suffix parity. No Scene, live session, Demo archive or archive seek."},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} cold detached custom checkpoint reopen checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM CHECKPOINT REOPEN FAIL "+error);return 1;}
    }
}
