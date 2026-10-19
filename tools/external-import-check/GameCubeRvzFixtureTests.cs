using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Rvz;

// Own-data synthetic fixtures only. No actual source-disc path, importer, GL,
// source extraction, or product output. Invoked by the existing no-args harness.
internal static class GameCubeRvzFixtureTests
{
    private static int Checks;
    static void Check(bool yes,string name){Checks++;if(!yes)throw new Exception(name);}
    static void Reject(Action action,string name){bool rejected=false;try{action();}catch(Exception e)when(e is InvalidDataException or IOException or ArgumentException or NotSupportedException or OverflowException or ObjectDisposedException){rejected=true;}Check(rejected,name);}
    internal static void Run()
    {
        Checks=0;
        Synthetic(); AliasAndJunk();
        Console.WriteLine($"PASS: {Checks} GameCube RVZ synthetic bounds/cache/junk/ownership assertions");
    }
    static void Synthetic()
    {
        byte[] iso=Iso();
        foreach(bool zstd in new[]{false,true})foreach(bool packed in new[]{false,true})
        {
            byte[] rvz=Rvz(iso,zstd,packed);
            using var source=new GuardStream(rvz);source.Position=11;
            using var logical=GameCubeRvzStream.Open(source,leaveOpen:true);
            using var gc=new GameCubeDiscReader(logical,leaveOpen:true);
            using var oldSource=new MemoryStream(iso,false);using var old=new GameCubeDiscReader(oldSource);
            Check(gc.GameId==old.GameId&&gc.Revision==old.Revision&&gc.Title==old.Title&&gc.Game==old.Game,"Legacy edition/header unchanged");
            Check(gc.Files.SequenceEqual(old.Files),"Existing FST entry identity unchanged");
            Check(gc.Sha1("Metroid1.pak")==old.Sha1("Metroid1.pak"),"Exact SHA recipe unchanged across compressed/packed groups");
            var expected=new ExternalArchiveRequirement("Metroid1.pak",old.Sha1("Metroid1.pak"));Check(ExternalSourceRequirements.Verify(gc,expected).Status==ExternalArchiveStatus.Verified,"Existing pin verifier is owner");
            Check(ExternalSourceRequirements.Verify(gc,new("Missing.pak",new string('0',40))).Status==ExternalArchiveStatus.Missing,"Missing pinned PAK remains explicit");
            using var view=gc.OpenRead("metroid1.PAK");using var other=gc.OpenRead("Metroid1.pak");
            var random=new Random(1234);
            for(int n=0;n<80;n++)
            {
                int at=random.Next((int)view.Length),count=Math.Min(random.Next(1,1024),(int)view.Length-at);byte[] got=new byte[count];view.Position=at;view.ReadExactly(got);Check(got.AsSpan().SequenceEqual(iso.AsSpan(0x7ff9+at,count)),"Cross-group seek bytes exact");
                other.Position=other.Length;Check(other.ReadByte()==-1,"Independent EOF view");
            }
            view.Seek(-9,SeekOrigin.End);Check(view.Position==view.Length-9,"Seek End exact");Reject(()=>logical.Write(new byte[1],0,1),"Read-only wrapper");Reject(()=>logical.SetLength(0),"No resize");Reject(()=>logical.Position=-1,"Negative seek rejected");
            logical.Position=logical.Length+8;Check(logical.ReadByte()==-1,"Past-end stream EOF");logical.Position=0;
            byte[] whole=new byte[iso.Length];logical.ReadExactly(whole);Check(whole.SequenceEqual(iso),"Entire logical disc reconstructs ISO bytes");
            Check(source.Writes==0&&source.LargestRead<=4*1024*1024,"Opened source read-only and bounded");
            gc.Dispose();Reject(()=>view.ReadByte(),"Existing file view cannot outlive reader");logical.Dispose();logical.Dispose();
            var cache=((Array)typeof(WiaReader).GetField("_cache",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(typeof(GameCubeRvzStream).GetField("_reader",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(logical)!)!);
            Check(cache.Cast<object>().All(x=>((byte[])x.GetType().GetField("Data")!.GetValue(x)!).Length==0),"All decoded group rentals returned on dispose");
            Reject(()=>logical.ReadByte(),"Disposed logical stream rejects cached reuse");Check(source.CanRead&&source.Disposals==0,"leaveOpen true+idempotent logical disposal");
        }
        string directory=Path.Combine(Path.GetTempPath(),"prime-cpp08-rvz-synthetic-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            foreach(string ext in new[]{".iso",".gcm",".rvz"})
            {
                string path=Path.Combine(directory,"synthetic"+ext);File.WriteAllBytes(path,ext==".rvz"?Rvz(iso,true,true):iso);using var disc=GameCubeDiscReader.Open(path);Check(disc.GameId=="G2ME01"&&disc.Sha1("Metroid1.pak")==Convert.ToHexString(SHA1.HashData(iso.AsSpan(0x7ff9,0x8031))).ToLowerInvariant(),"Actual candidate factory for "+ext);
            }
            string bad=Path.Combine(directory,"not-an-ISO.rvz");File.WriteAllBytes(bad,iso);Reject(()=>GameCubeDiscReader.Open(bad),"RVZ extension cannot relabel ISO bytes");
        }
        finally{Directory.Delete(directory,true);}
        using(var s=new GuardStream(Rvz(iso))){var v=GameCubeRvzStream.Open(s,leaveOpen:false);v.Dispose();v.Dispose();Check(s.Disposals==1,"Exact owned-stream disposal");}
        using(var s=new GuardStream(Rvz(iso))){using var v=GameCubeRvzStream.Open(s);s.SetLength(s.Length+1);Reject(()=>v.ReadByte(),"Cached reads detect original opened-stream length growth");}
        using(var s=new GuardStream(Rvz(iso))){using var v=GameCubeRvzStream.Open(s);using var gc=new GameCubeDiscReader(v);s.GrowAfterRead=true;v.Position=0x8000;Reject(()=>v.ReadByte(),"Growth during opened-stream group read fails before publish");}
        using(var s=new OversizedStream(Rvz(iso))){Reject(()=>GameCubeRvzStream.Open(s),"Physical >2GiB fails before table allocation");Check(s.LargestRead<0x200,"Physical bound rejects after bounded prefix only");}
        void Bad(Action<byte[]> mutate,string name,bool rehash=true)
        {
            byte[] data=Rvz(iso);mutate(data);if(rehash)Rehash(data);
            using var source=new GuardStream(data);long allocated=GC.GetAllocatedBytesForCurrentThread();
            Reject(()=>{using var logical=GameCubeRvzStream.Open(source);using var gc=new GameCubeDiscReader(logical);},name);
            Check(source.Writes==0&&source.LargestRead<=4*1024*1024,"Malformed read budget: "+name);
            Check(GC.GetAllocatedBytesForCurrentThread()-allocated<12*1024*1024,"Malformed allocation budget: "+name);
            Check(source.CanRead,"Failure does not own caller stream: "+name);
        }
        Bad(b=>U32(b,0,0x57494101),"WIA fails closed");
        Bad(b=>U32(b,0x48,2),"Trilogy/Wii platform fails before decode");
        Bad(b=>U32(b,4,0),"Unsupported RVZ version");
        Bad(b=>b[0x34]^=1,"Corrupt file header hash",false);
        Bad(b=>b[0x48+0x10]^=1,"Corrupt disc header hash",false);
        Bad(b=>U64(b,0x24,ulong.MaxValue),"Logical size ulong overflow");
        Bad(b=>U64(b,0x24,2147483649),"Logical GC size bound");
        Bad(b=>U64(b,0x2c,(ulong)b.Length+1),"Declared physical size mismatch");
        Bad(b=>U32(b,0x48+0xc4,uint.MaxValue),"Group count signed overflow");
        Bad(b=>U32(b,0x48+0xc4,65537),"Group table allocation cap");
        Bad(b=>U32(b,0x48+0xb4,1025),"Raw table allocation cap");
        Bad(b=>U32(b,0x48+0x90,1),"No Wii partition table");
        Bad(b=>U32(b,0x48+0xc,0x400000),"Chunk allocation cap");
        Bad(b=>U32(b,0x48+4,3),"Unsupported compression stays explicit");
        Bad(b=>U64(b,0x48+0xb8,ulong.MaxValue),"Raw table offset overflow");
        Bad(b=>U32(b,0x48+0xd0,0x7fffffff),"Stored table budget before allocation");
        Bad(b=>U64(b,0x140+8,ulong.MaxValue),"Raw region range overflow");
        Bad(b=>U32(b,0x140+20,1),"Raw region must cover exact groups");
        Bad(b=>U32(b,0x158+4,4*1024*1024+1),"Stored group allocation cap");
        Bad(b=>U32(b,0x158+8,4*1024*1024+1),"Packed group allocation cap");
        Bad(b=>U32(b,0x158,uint.MaxValue),"Stored group range outside physical file");
        Bad(b=>{b[0x58]=(byte)'R';U32(b,0x400,0x52334d45);},"MP3/unsupported GC identity stays rejected");
        Bad(b=>U32(b,0x400+0x424,0xfffffff0),"Existing FST bounds retained");
        Bad(b=>{b[0x400+0x500+24]=(byte)'.';b[0x400+0x500+25]=(byte)'.';b[0x400+0x500+26]=0;},"Existing traversal rules retained");
        Check(ExternalSourceRequirements.For(ExternalGameKind.Echoes).Select(x=>x.ExpectedSha1).SequenceEqual(new[]{"6b9a47578f41b2c64a12f1a0bf4e5cd783692124","b3375e83c5290d35d6c83f35b04b7f60f6fb8ea7","b8b92af37dc772ecd8234fb1ce664be0493188f6"}),"All actual Echoes pins unchanged");
    }
    static void AliasAndJunk()
    {
        byte[] oracle=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"rvz-fixtures","junk-oracle.bin"));
        byte[] seed=Enumerable.Range(0,68).Select(i=>(byte)i).ToArray();
        using var proof=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"rvz-fixtures","junk-oracle.json")));
        Check(Convert.ToHexString(SHA256.HashData(oracle)).ToLowerInvariant()==proof.RootElement.GetProperty("sha256").GetString(),"Independent synthetic oracle hash");
        var generator=new JunkGenerator();generator.Seed(seed);byte[] generated=new byte[oracle.Length];generator.Generate(generated);
        Check(generated.SequenceEqual(oracle),"Known-seed junk matches independent ring oracle over 128KiB");
        foreach(int phase in new[]{0,2079,32760,32767})
        {
            byte[] packet=new byte[4+7+4+68];U32(packet,0,7);for(int i=0;i<7;i++)packet[4+i]=(byte)(i+91);U32(packet,11,0x80008031);seed.CopyTo(packet,15);
            byte[] output=new byte[7+0x8031];Check(RvzPacking.Measure(packet,0x10000)==output.Length,"Junk measure bounds complete output");
            RvzPacking.Unpack(packet,output,phase,new JunkGenerator());
            Check(output.AsSpan(0,7).SequenceEqual(packet.AsSpan(4,7))&&output.AsSpan(7).SequenceEqual(oracle.AsSpan((phase+7)%0x8000,0x8031)),"Junk phase/block/0x8000-boundary oracle "+phase);
        }
        foreach(bool compressed in new[]{false,true})foreach(int kind in new[]{0,1,2,3})foreach(int chunk in new[]{0x8000,0x10000})
        {
            byte[] data=AliasRvz(compressed,kind,chunk,oracle,out byte[] full);
            static byte[] ReadFresh(byte[] source,long at,int count){using var s=new MemoryStream(source,false);using var v=GameCubeRvzStream.Open(s);v.Position=at;byte[] result=new byte[count];v.ReadExactly(result);return result;}
            long shortAt=0x3000,longAt=chunk+0x6000;byte[] shortReference=ReadFresh(data,shortAt,0x800),longReference=ReadFresh(data,longAt,0x1000);
            Check(shortReference.AsSpan().SequenceEqual(full.AsSpan((int)shortAt,shortReference.Length)),"Fresh short alias exact oracle");
            Check(longReference.AsSpan().SequenceEqual(full.AsSpan(0x6000,longReference.Length)),"Fresh long alias exact oracle");
            foreach(bool shortFirst in new[]{true,false})
            {
                byte[] poison=System.Buffers.ArrayPool<byte>.Shared.Rent(chunk);poison.AsSpan().Fill(0xad);System.Buffers.ArrayPool<byte>.Shared.Return(poison);
                using var source=new MemoryStream(data,false);using var logical=GameCubeRvzStream.Open(source);
                foreach(bool readShort in new[]{shortFirst,!shortFirst,shortFirst,!shortFirst})
                {
                    long at=readShort?shortAt:longAt;byte[] expected=readShort?shortReference:longReference;logical.Position=at;byte[] actual=new byte[expected.Length];logical.ReadExactly(actual);
                    Check(actual.SequenceEqual(expected),"Contextual group cache short/long alias vs uncached reference");
                }
            }
            if(kind!=3)
            {
                using var source=new MemoryStream(data,false);using var logical=GameCubeRvzStream.Open(source);using var reader=new GameCubeDiscReader(logical);
                Check(reader.Sha1("Metroid1.pak")==Convert.ToHexString(SHA1.HashData(full.AsSpan(0x5000,0x1000))).ToLowerInvariant(),"Actual FST short cache followed by long-alias PAK SHA");
            }
        }
        byte[] truncatedJunk=new byte[4+67];U32(truncatedJunk,0,0x80000010);Reject(()=>RvzPacking.Measure(truncatedJunk,0x8000),"Truncated junk seed rejected before restore allocation");
        byte[] oversizedJunk=new byte[72];U32(oversizedJunk,0,0x80010001);Reject(()=>RvzPacking.Measure(oversizedJunk,0x10000),"Oversized restored group rejected before allocation");
        using(var source=new MemoryStream(Rvz(Iso()),false))using(var logical=GameCubeRvzStream.Open(source))
        {
            object gate=typeof(GameCubeRvzStream).GetField("_gate",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(logical)!;
            using var entered=new ManualResetEventSlim();Task task;
            Monitor.Enter(gate);
            try{task=Task.Run(()=>{entered.Set();logical.Position=42;_ = logical.Position;});Check(entered.Wait(1000),"Position lock worker entered");Check(!task.Wait(50),"Position accessor waits for same read/seek/dispose lock");}
            finally{Monitor.Exit(gate);}
            Check(task.Wait(1000)&&logical.Position==42,"Position resumes with exact synchronized value");
        }
    }
    // kind: unpacked, packed literal, packed seed junk, or zero-size stored group.
    static byte[] AliasRvz(bool compressed,int kind,int chunk,byte[] oracle,out byte[] expected)
    {
        expected=new byte[chunk];for(int i=0;i<chunk;i++)expected[i]=(byte)((i*17+i/31)%251);
        Iso().AsSpan(0,0x600).CopyTo(expected);U32(expected,0x510,(uint)(chunk+0x5000));U32(expected,0x514,0x1000);
        byte[] packed;
        if(kind==2)
        {
            const int literal=0x1000;oracle.AsSpan(literal,chunk-literal).CopyTo(expected.AsSpan(literal));
            packed=new byte[4+literal+4+68];U32(packed,0,literal);expected.AsSpan(0,literal).CopyTo(packed.AsSpan(4));U32(packed,4+literal,0x80000000u+(uint)(chunk-literal));Enumerable.Range(0,68).Select(i=>(byte)i).ToArray().CopyTo(packed,8+literal);
        }
        else if(kind==1){packed=new byte[chunk+4];U32(packed,0,(uint)chunk);expected.CopyTo(packed,4);}
        else packed=expected.ToArray();
        byte[] raw=new byte[48];U64(raw,0,0x80);U64(raw,8,0x4001-0x80);U32(raw,20,1);U64(raw,24,(ulong)chunk);U64(raw,32,(ulong)chunk);U32(raw,44,1);
        if(compressed)raw=ZstdRaw(raw);int groupAt=(0x140+raw.Length+3)&~3;
        byte[] payload=compressed?ZstdRaw(packed):packed;byte[] table=new byte[12];U32(table,0,0x100);U32(table,4,kind==3?0:(uint)payload.Length|(compressed?0x80000000u:0));U32(table,8,kind is 1 or 2?(uint)packed.Length:0);if(compressed)table=ZstdRaw(table);
        byte[] image=new byte[0x400+(kind==3?0:payload.Length)];U32(image,0,WiaReader.RvzMagic);U32(image,4,0x01000000);U32(image,8,0x00030000);U32(image,12,0xdc);U64(image,0x24,(ulong)chunk*2);U64(image,0x2c,(ulong)image.Length);U32(image,0x48,1);U32(image,0x4c,compressed?5u:0);U32(image,0x54,(uint)chunk);expected.AsSpan(0,0x80).CopyTo(image.AsSpan(0x58));U32(image,0x48+0xb4,2);U64(image,0x48+0xb8,0x140);U32(image,0x48+0xc0,(uint)raw.Length);U32(image,0x48+0xc4,1);U64(image,0x48+0xc8,(ulong)groupAt);U32(image,0x48+0xd0,(uint)table.Length);raw.CopyTo(image,0x140);table.CopyTo(image,groupAt);if(kind!=3)payload.CopyTo(image,0x400);else Array.Clear(expected);Rehash(image);return image;
    }
    static byte[] Iso()
    {
        byte[] b=new byte[0x18000];Encoding.ASCII.GetBytes("G2ME01").CopyTo(b,0);Encoding.ASCII.GetBytes("Synthetic own-data fixture").CopyTo(b,0x20);U32(b,0x1c,GameCubeDiscReader.Magic);U32(b,0x424,0x500);U32(b,0x428,64);U32(b,0x500,0x01000000);U32(b,0x508,2);U32(b,0x510,0x7ff9);U32(b,0x514,0x8031);Encoding.ASCII.GetBytes("Metroid1.pak\0").CopyTo(b,0x518);for(int i=0;i<0x8031;i++)b[0x7ff9+i]=(byte)((i*29+i/91)%251);return b;
    }
    static byte[] Rvz(byte[] iso,bool compressed=false,bool packed=false)
    {
        const int chunk=0x8000;int groups=(iso.Length+chunk-1)/chunk;
        byte[] raw=new byte[24];U64(raw,0,0x80);U64(raw,8,(ulong)iso.Length-0x80);U32(raw,20,(uint)groups);
        var payloads=new List<byte[]>();for(int i=0;i<groups;i++){byte[] data=iso.AsSpan(i*chunk,Math.Min(chunk,iso.Length-i*chunk)).ToArray();if(packed){byte[] p=new byte[data.Length+4];U32(p,0,(uint)data.Length);data.CopyTo(p,4);data=p;}payloads.Add(compressed?ZstdRaw(data):data);}
        if(compressed)raw=ZstdRaw(raw);int groupOff=(0x140+raw.Length+3)&~3;byte[] table=new byte[groups*12];int dataOff=0x400;
        for(int i=0;i<groups;i++){U32(table,i*12,(uint)dataOff/4);U32(table,i*12+4,(uint)payloads[i].Length|(compressed?0x80000000u:0));U32(table,i*12+8,packed?(uint)(chunk+4):0);dataOff=(dataOff+payloads[i].Length+3)&~3;}
        byte[] storedTable=compressed?ZstdRaw(table):table;
        byte[] b=new byte[dataOff];U32(b,0,WiaReader.RvzMagic);U32(b,4,0x01000000);U32(b,8,0x00030000);U32(b,12,0xdc);U64(b,0x24,(ulong)iso.Length);U64(b,0x2c,(ulong)b.Length);U32(b,0x48,1);U32(b,0x4c,compressed?5u:0);U32(b,0x54,chunk);iso.AsSpan(0,0x80).CopyTo(b.AsSpan(0x58));U32(b,0x48+0x94,48);U32(b,0x48+0xb4,1);U64(b,0x48+0xb8,0x140);U32(b,0x48+0xc0,(uint)raw.Length);U32(b,0x48+0xc4,(uint)groups);U64(b,0x48+0xc8,(ulong)groupOff);U32(b,0x48+0xd0,(uint)storedTable.Length);raw.CopyTo(b,0x140);storedTable.CopyTo(b,groupOff);for(int i=0;i<groups;i++)payloads[i].CopyTo(b,(int)BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i*12))*4);Rehash(b);return b;
    }
    static byte[] ZstdRaw(byte[] data){byte[] b=new byte[data.Length+12];BinaryPrimitives.WriteUInt32LittleEndian(b,0xfd2fb528);b[4]=0xa0;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5),(uint)data.Length);uint block=((uint)data.Length<<3)|1;b[9]=(byte)block;b[10]=(byte)(block>>8);b[11]=(byte)(block>>16);data.CopyTo(b,12);return b;}
    static void Rehash(byte[] b){SHA1.HashData(b.AsSpan(0x48,0xdc)).CopyTo(b,0x10);SHA1.HashData(b.AsSpan(0,0x34)).CopyTo(b,0x34);}
    static void U32(byte[] b,int o,uint v)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o),v);
    static void U64(byte[] b,int o,ulong v)=>BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(o),v);
    sealed class OversizedStream(byte[] data):GuardStream(data) { public override long Length=>2147483649; }
    class GuardStream:MemoryStream
    {
        public GuardStream(byte[] data):base(data.Length+16){base.Write(data,0,data.Length);Position=0;}
        public int Writes,Disposals,LargestRead; public bool GrowAfterRead;
        public override int Read(Span<byte> b){LargestRead=Math.Max(LargestRead,b.Length);int n=base.Read(b);if(GrowAfterRead){GrowAfterRead=false;SetLength(Length+1);}return n;}
        public override int Read(byte[] b,int o,int c){LargestRead=Math.Max(LargestRead,c);return base.Read(b,o,c);}
        public override void Write(byte[] b,int o,int c){Writes++;throw new Exception("Source writes forbidden");}
        protected override void Dispose(bool disposing){if(disposing)Disposals++;base.Dispose(disposing);}
    }
}
