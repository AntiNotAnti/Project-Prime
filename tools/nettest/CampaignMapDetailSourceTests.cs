using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Campaign;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class CampaignMapDetailSourceTests
{
    // Own-generated wc01: one square, two layer-specific faces, one grid cell.
    private static byte[] Fixture()
    {
        var bytes=new byte[196];"wc01"u8.CopyTo(bytes);
        void U32(int offset,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset,4),value);
        void U16(int offset,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset,2),value);
        void I32(int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset,4),value);
        U32(4,4);U32(8,84);U32(12,1);U32(16,132);U32(20,4);U32(24,148);
        U32(28,2);U32(32,156);U32(36,2);U32(40,188);
        I32(44,1);I32(48,1);I32(52,1);U32(68,1);U32(72,192);
        // Four fixed-point positions; y=0, plane y=0.
        I32(96,8192);I32(108,8192);I32(116,8192);I32(128,8192);I32(136,4096);
        for(ushort i=0;i<4;i++)U16(148+i*2,i);
        U16(164,0x40);U16(168,4);U16(180,0x80);U16(184,4);
        U16(188,0);U16(190,1);U16(192,2);U16(194,0);
        return bytes;
    }
    internal static void Run(Action<bool,string> check)
    {
        byte[] valid=Fixture();
        var all=CampaignMapDetailSource.ReadCollision(valid,-1);
        check(all.Points.Count==4&&all.RuntimeData.Count==2
            &&CampaignMapDetail.Build(all,null,Vector3.Zero).Ready,"bounded wc01 parser produces actual valid detached collision tables");
        var first=CampaignMapDetailSource.ReadCollision(valid,0x40);
        var second=CampaignMapDetailSource.ReadCollision(valid,0x80);
        check(first.RuntimeData.Count==1&&first.RuntimeData[0].LayerMask==0x40
            &&second.RuntimeData.Count==1&&second.RuntimeData[0].LayerMask==0x80,
            "native layer masks retain only the matching room geometry");
        check(CampaignMapDetailSource.ReadCollision(valid,0x100).RuntimeData.Count==0,
            "unmatched layer has no detail geometry");
        void Refuse(Action<byte[]> change,string label)
        {
            byte[] malformed=(byte[])valid.Clone();change(malformed);bool refused=false;
            try{CampaignMapDetailSource.ReadCollision(malformed,-1);}catch(InvalidDataException){refused=true;}
            check(refused,label);
        }
        Refuse(b=>b[0]=(byte)'x',"unsupported collision magic rejected before native parser");
        bool shortHeader=false;
        try{CampaignMapDetailSource.ReadCollision(valid.AsSpan(0,83),-1);}catch(InvalidDataException){shortHeader=true;}
        check(shortHeader,"truncated wc01 header rejected");
        foreach(int offset in new[]{8,16,24,32,40,72})
        {
            int captured=offset;
            Refuse(b=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(captured,4),0),"nonempty section cannot overlap header: "+offset);
            Refuse(b=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(captured,4),UInt32.MaxValue),"section end cannot overflow file: "+offset);
        }
        foreach(var row in new[]{(4,65537u),(12,65537u),(20,1048577u),(28,65536u),(36,1048577u),(68,1048577u),(76,4097u)})
        {
            var captured=row;
            Refuse(b=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(captured.Item1,4),captured.Item2),"section count budget rejected before allocation: "+row.Item1);
        }
        Refuse(b=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(148,2),4),"source point index checked before layer filtering");
        Refuse(b=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(160,2),1),"source face plane index checked");
        Refuse(b=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(170,2),2),"source face span checked against point-index table");
        Refuse(b=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(188,2),2),"source grid face index checked");
        Refuse(b=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(194,2),1),"source grid span checked");
        byte[] hiddenMalformed=(byte[])valid.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(hiddenMalformed.AsSpan(176,2),1);
        bool hiddenRefused=false;
        try{CampaignMapDetailSource.ReadCollision(hiddenMalformed,0x40);}catch(InvalidDataException){hiddenRefused=true;}
        check(hiddenRefused,"malformed hidden-layer face cannot bypass structural validation");

        // Every cell span is individually valid, but overlapping cells make
        // the native layer filter expand far beyond the source index table.
        const int repeatedIndexCount=UInt16.MaxValue,repeatedEntryCount=17;
        int repeatedEntryOffset=188+repeatedIndexCount*2;
        byte[] repeatedGrid=new byte[repeatedEntryOffset+repeatedEntryCount*4];
        valid.AsSpan(0,188).CopyTo(repeatedGrid);
        BinaryPrimitives.WriteUInt32LittleEndian(repeatedGrid.AsSpan(36,4),repeatedIndexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(repeatedGrid.AsSpan(68,4),repeatedEntryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(repeatedGrid.AsSpan(72,4),(uint)repeatedEntryOffset);
        for(int i=0;i<repeatedEntryCount;i++)
            BinaryPrimitives.WriteUInt16LittleEndian(repeatedGrid.AsSpan(repeatedEntryOffset+i*4,2),repeatedIndexCount);
        check(repeatedGrid.Length<CampaignMapDetailSource.MaxFileBytes
            &&repeatedIndexCount<=CampaignMapDetailSource.MaxGridReferences
            &&(ulong)repeatedIndexCount*repeatedEntryCount>CampaignMapDetailSource.MaxGridReferences,
            "repeated grid fixture has bounded tables and valid individual spans but excessive aggregate references");
        bool expansionRefused=false;
        try{CampaignMapDetailSource.ReadCollision(repeatedGrid,0x40);}
        catch(InvalidDataException ex){expansionRefused=ex.Message.Contains("grid expansion",StringComparison.Ordinal);}
        check(expansionRefused,"overlapping valid grid spans are rejected before native layer-filter expansion");

        string temporary=Path.Combine(Path.GetTempPath(),"prime-map-detail-source-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var gate=(SemaphoreSlim)typeof(CampaignMapDetailLoad).GetField("_readGate",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            string source=Path.Combine(temporary,"synthetic-collision.bin"),bad=Path.Combine(temporary,"invalid.bin");
            File.WriteAllBytes(source,valid);File.WriteAllBytes(bad,new byte[20]);
            string oversizedPath=Path.Combine(temporary,"oversized.bin");
            using(var sparse=new FileStream(oversizedPath,FileMode.CreateNew,FileAccess.Write))
                sparse.SetLength(CampaignMapDetailSource.MaxFileBytes+1L);
            bool fileBudget=false;
            try{CampaignMapDetailSource.ReadCollision(oversizedPath,-1);}catch(InvalidDataException){fileBudget=true;}
            check(fileBudget,"path reader rejects oversized source before allocating or decoding it");
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();bool beforeIo=false;
                try{CampaignMapDetailSource.ReadCollision(Path.Combine(temporary,"does-not-exist"),-1,cancelled.Token);}
                catch(OperationCanceledException){beforeIo=true;}
                check(beforeIo,"pre-cancelled path reader performs no missing-file IO");
            }
            using(var loaded=new CampaignMapDetailLoad(new[]{
                new CampaignMapDetailRequest("ROOM-A",source,0x40,new Vector3(1,2,3)),
                new CampaignMapDetailRequest("MISSING",Path.Combine(temporary,"missing.bin"),-1,Vector3.Zero),
                new CampaignMapDetailRequest("BROKEN",bad,-1,Vector3.Zero),
                new CampaignMapDetailRequest("ROOM-B",source,0x80,new Vector3(4,5,6))}))
            {
                var result=loaded.Completion.GetAwaiter().GetResult();
                check(result.Count==2&&loaded.TryGetRoom("room-a",out var a)&&a!.Ready
                    &&loaded.TryGetRoom("ROOM-B",out var b)&&b!.Ready,
                    "missing or malformed source keeps only its NAV fallback while valid batch rooms complete");
                check(!loaded.TryGetRoom("MISSING",out _)&&!loaded.TryGetRoom("BROKEN",out _)
                    &&!loaded.TryGetRoom("NEVER REQUESTED",out _),"missing and unrequested room lookups fail closed");
                bool readOnly=false;
                try{((IDictionary<string,CampaignMapDetailResult>)result).Clear();}catch(NotSupportedException){readOnly=true;}
                check(readOnly,"completed room snapshot cannot be mutated by consumers");
                loaded.Dispose();loaded.Dispose();
                check(!loaded.TryGetRoom("ROOM-A",out _),"retirement hides an already completed older room snapshot");
            }
            // Hold the actual worker lane; retirement must cancel its waiter and
            // never publish later after a replacement opening has begun.
            gate.Wait();
            CampaignMapDetailLoad retired;
            try
            {
                retired=new(new[]{new CampaignMapDetailRequest("ROOM-A",source,-1,Vector3.Zero)});
                retired.Dispose();
            }
            finally{gate.Release();}
            using(retired)
            {
                bool wasCancelled=false;
                try{retired.Completion.GetAwaiter().GetResult();}catch(OperationCanceledException){wasCancelled=true;}
                check(wasCancelled&&!retired.TryGetRoom("ROOM-A",out _),"retired queued worker cancels without late publication");
            }
            var mutable=new[]{new CampaignMapDetailRequest("CAPTURED",source,0x40,new Vector3(8,9,10))};
            gate.Wait();CampaignMapDetailLoad snapshot;
            try
            {
                snapshot=new(mutable);
                mutable[0]=new("MUTATED",bad,-1,Vector3.Zero);
            }
            finally{gate.Release();}
            using(snapshot)
            {
                _=snapshot.Completion.GetAwaiter().GetResult();
                check(snapshot.TryGetRoom("CAPTURED",out var captured)&&captured!.Polygons[0].Vertices[0]==new Vector3(8,9,10)
                    &&!snapshot.TryGetRoom("MUTATED",out _),"loader owns a detached request snapshot across worker delay");
            }
            using(var empty=new CampaignMapDetailLoad(Array.Empty<CampaignMapDetailRequest>()))
                check(empty.Completion.GetAwaiter().GetResult().Count==0&&!empty.TryGetRoom("ANY",out _),"empty request batch is a bounded empty snapshot");
            bool tooMany=false;
            try{using var oversized=new CampaignMapDetailLoad(new CampaignMapDetailRequest[CampaignMapDetailLoad.MaxRooms+1]);}
            catch(ArgumentOutOfRangeException){tooMany=true;}
            check(tooMany,"loader rejects request count beyond native room budget");
        }
        finally{Directory.Delete(temporary,true);}
    }
}
