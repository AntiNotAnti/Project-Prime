using System.Buffers.Binary;
using System.Security.Cryptography;
using Duration=MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;

// Numeric, invented duration metadata only; these records contain no animation
// assets, LUT evaluation, model bindings, or copied source payloads.
internal static class AnimationDurationFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;
        var fixture=Build();var value=Duration.Read(fixture.Bytes);
        Check(value.SlotCount==2&&value.HeaderPadding==0xbeef&&value.ByteLength==fixture.Bytes.Length,
            "advertised slots and opaque header padding retained");
        Check(value.NodeFrames==4&&value.MaterialFrames==7&&value.TexcoordFrames==9&&value.TextureFrames==6&&value.WidgetFrames==9,
            "independent first-slot channel durations, never larger second-slot maximum");
        Check(value.Sha256==Hash(fixture.Bytes),"duration identity hashes complete admitted bytes");
        string hash=value.Sha256;Array.Fill(fixture.Bytes,(byte)0);
        Check(value.Sha256==hash&&value.TexcoordFrames==9,"duration snapshot detached from caller bytes");
        fixture=Build();Check(Duration.Read(fixture.Bytes).Sha256==hash,"numeric fixture deterministic");
        var absent=Build();foreach(string table in new[]{"nodeTable","materialTable","texcoordTable","textureTable"})
            Put(absent.Bytes,absent.At[table],0);
        var empty=Duration.Read(absent.Bytes);
        Check(empty.NodeFrames==0&&empty.MaterialFrames==0&&empty.TexcoordFrames==0&&empty.TextureFrames==0&&empty.WidgetFrames==1,
            "zero first-slot entries are absent despite populated second slots and retain minimum still length1");
        var allZero=Build(first:new[]{0,0,0,0},second:new[]{0,0,0,0});
        Check(Duration.Read(allZero.Bytes).WidgetFrames==1,"present all-empty channel headers retain minimum widget frame1");
        var boundary=Build(first:new[]{18000,1,2,3},second:new[]{18000,18000,18000,18000});
        Check(Duration.Read(boundary.Bytes).WidgetFrames==18000,"maximum admitted duration inclusive");
        var slots=Build(slots:64);Check(Duration.Read(slots.Bytes).SlotCount==64,"all64 advertised slots admitted with bounded empty record spans");
        foreach(int n in new[]{0,1,19,23})Reject(Build().Bytes[..n],"truncated24-byte animation header");
        Reject(new byte[4*1024*1024+1],"input cap precedes source hash/payload allocation");
        foreach(ushort count in new ushort[]{0,65,ushort.MaxValue})
        {var bad=Build();Half(bad.Bytes,20,count);Reject(bad.Bytes,"slot count cap");}
        foreach(string field in new[]{"nodeTable","unusedTable","materialTable","texcoordTable","textureTable"})
        {
            foreach(uint pointer in new[]{uint.MaxValue,(uint)Build().Bytes.Length-1})
            {var bad=Build();Put(bad.Bytes,bad.At[field+"Field"],pointer);Reject(bad.Bytes,"bounded advertised table extent");}
        }
        var unused=Build();Put(unused.Bytes,unused.At["unusedTable"]+4,(uint)unused.At["node0"]);
        Reject(unused.Bytes,"nonzero unused entry refused even outside duration slot0");
        foreach(string group in new[]{"node","material","texcoord","texture"})
        {
            var bad=Build();Put(bad.Bytes,bad.At[group+"Table"]+4,uint.MaxValue);Reject(bad.Bytes,"nonfirst-slot group range validated");
            bad=Build();Put(bad.Bytes,bad.At[group+"Table"],(uint)bad.Bytes.Length-1);Reject(bad.Bytes,"truncated group header");
            bad=Build();if(group=="texture")Half(bad.Bytes,bad.At[group+"1"],18001);else Put(bad.Bytes,bad.At[group+"1"],18001);
            Reject(bad.Bytes,"oversized channel frame count in nonfirst slot refused");
        }
        foreach(var field in new[]{("node0",4),("node0",8),("node0",12),("node0",16),
            ("material0",4),("material0",12),("texcoord0",4),("texcoord0",8),("texcoord0",12),("texcoord0",20),
            ("texture0",12),("texture0",16),("texture0",20),("texture0",24)})
        {var bad=Build();Put(bad.Bytes,bad.At[field.Item1]+field.Item2,uint.MaxValue);Reject(bad.Bytes,"LUT/record offset overflow cannot leave supplied input");}
        foreach(var field in new[]{("node0",8),("node0",12),("node0",16),("texcoord0",8),("texcoord0",12),("texcoord0",20)})
        {var bad=Build();Put(bad.Bytes,bad.At[field.Item1]+field.Item2,1);Reject(bad.Bytes,"nonmonotonic LUT ranges refused");}
        foreach(var field in new[]{("node0",4),("node0",8),("node0",12),("node0",16),
            ("texcoord0",4),("texcoord0",8),("texcoord0",12)})
        {var bad=Build();int p=bad.At[field.Item1]+field.Item2;Put(bad.Bytes,p,Get(bad.Bytes,p)+1);Reject(bad.Bytes,"partial LUT or derived record extent refused");}
        foreach(var field in new[]{("material0",8),("texcoord0",16)})
        {var bad=Build();Put(bad.Bytes,bad.At[field.Item1]+field.Item2,uint.MaxValue);Reject(bad.Bytes,"record multiplication/count overflow rejected");}
        foreach(int field in new[]{2,4,6,8})
        {var bad=Build();Half(bad.Bytes,bad.At["texture0"]+field,ushort.MaxValue);Reject(bad.Bytes,"texture count/range product bounded before records");}
        foreach(string channel in new[]{"node","material","texcoord","texture"})
        {
            var bad=Build();int header=bad.At[channel+"0"];
            // A table may point at any byte offset. Complete header range must
            // be validated before interpreting even its first duration field.
            int headerBytes=channel=="texture"?32:channel=="texcoord"?28:20;
            byte[] shortInput=bad.Bytes[..(header+headerBytes-1)];
            // Remove later advertised records/tables so this extent tests the
            // chosen group's last header byte, not unrelated later groups.
            foreach(string t in new[]{"nodeTable","materialTable","texcoordTable","textureTable"})
                for(int s=0;s<2;s++)Put(shortInput,bad.At[t]+s*4,0);
            Put(shortInput,bad.At[channel+"Table"],(uint)header);
            Reject(shortInput,"exact group header truncation for "+channel);
        }
        var tiny=new byte[24];Put(tiny,0,uint.MaxValue);Half(tiny,20,64);
        for(int i=0;i<8;i++)Reject(tiny,"warm impossible table range");
        long before=GC.GetAllocatedBytesForCurrentThread();Reject(tiny,"tiny oversized range refuses without declared group arrays");
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated<1024*1024,"malicious tiny header refusal stays below1MiB warmed allowance");
        Console.WriteLine($"CREDITS DURATION PASS {checks} numeric checks; tiny invalid allocation {allocated} bytes; no animation evaluation.");
    }

    internal sealed record Fixture(byte[] Bytes,Dictionary<string,int> At);
    internal static Fixture Build(int slots=2,int[]? first=null,int[]? second=null)
    {
        first??=new[]{4,7,9,6};second??=new[]{100,110,120,130};
        var b=new Bytes();var at=new Dictionary<string,int>();b.Reserve(24);Half(b.Data,20,(ushort)slots);Half(b.Data,22,0xbeef);
        string[] names={"node","unused","material","texcoord","texture"};
        for(int c=0;c<names.Length;c++){int table=b.Reserve(slots*4);at[names[c]+"Table"]=table;at[names[c]+"TableField"]=c*4;Put(b.Data,c*4,(uint)table);}
        for(int slot=0;slot<slots;slot++)
        {
            int[] frames=slot==0?first:second;
            // LUT and record extents precede group headers just as the reader's
            // format defines. Bytes are invented zeros, never decoded as media.
            int scale=b.Reserve(4),rotate=b.Reserve(2),translate=b.Reserve(4),anim=b.Reserve(48),node=b.Reserve(20);
            Put(b.Data,node,(uint)frames[0]);Put(b.Data,node+4,(uint)scale);Put(b.Data,node+8,(uint)rotate);Put(b.Data,node+12,(uint)translate);Put(b.Data,node+16,(uint)anim);
            int color=b.Reserve(4),ma=b.Reserve(140),material=b.Reserve(20);
            Put(b.Data,material,(uint)frames[1]);Put(b.Data,material+4,(uint)color);Put(b.Data,material+8,1);Put(b.Data,material+12,(uint)ma);Half(b.Data,material+16,0xbbbb);Half(b.Data,material+18,0xface);
            int us=b.Reserve(4),ur=b.Reserve(2),ut=b.Reserve(4),ua=b.Reserve(60),uv=b.Reserve(28);
            Put(b.Data,uv,(uint)frames[2]);Put(b.Data,uv+4,(uint)us);Put(b.Data,uv+8,(uint)ur);Put(b.Data,uv+12,(uint)ut);Put(b.Data,uv+16,1);Put(b.Data,uv+20,(uint)ua);Half(b.Data,uv+24,0xbbbb);Half(b.Data,uv+26,0xface);
            int fi=b.Reserve(2),ti=b.Reserve(2),pi=b.Reserve(2),ta=b.Reserve(44),texture=b.Reserve(32);
            Half(b.Data,texture,(ushort)frames[3]);foreach(int offset in new[]{2,4,6,8})Half(b.Data,texture+offset,1);
            Half(b.Data,texture+10,0xbbbb);Put(b.Data,texture+12,(uint)fi);Put(b.Data,texture+16,(uint)ti);Put(b.Data,texture+20,(uint)pi);Put(b.Data,texture+24,(uint)ta);Half(b.Data,texture+28,0xbbbb);Half(b.Data,texture+30,0xface);
            int[] groups={node,material,uv,texture};string[] channels={"node","material","texcoord","texture"};
            for(int c=0;c<4;c++){Put(b.Data,at[channels[c]+"Table"]+slot*4,(uint)groups[c]);at[channels[c]+slot]=groups[c];}
        }
        return new(b.Data.ToArray(),at);
    }
    private sealed class Bytes
    {
        internal List<byte> Data=new();
        internal int Reserve(int count){int offset=Data.Count;Data.AddRange(new byte[count]);return offset;}
    }
    private static uint Get(byte[] bytes,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Put(byte[] bytes,int offset,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset),value);
    private static void Put(List<byte> bytes,int offset,uint value){for(int i=0;i<4;i++)bytes[offset+i]=(byte)(value>>(i*8));}
    private static void Half(byte[] bytes,int offset,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset),value);
    private static void Half(List<byte> bytes,int offset,ushort value){bytes[offset]=(byte)value;bytes[offset+1]=(byte)(value>>8);}
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Check(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException("Duration fixture: "+label);}
    private static void Reject(byte[] bytes,string label)
    {checks++;try{Duration.Read(bytes);}catch(InvalidDataException){return;}throw new InvalidOperationException("Duration expected InvalidDataException: "+label);}
}
