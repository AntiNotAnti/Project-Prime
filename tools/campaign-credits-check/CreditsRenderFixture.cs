using System.Buffers.Binary;
using System.Text;

// Invented native records and command packets only. This is deliberately
// independent of production serializers and contains no copied asset payload.
internal static class CreditsRenderFixture
{
    internal sealed record Data(byte[] Bytes,Dictionary<string,int> At);
    internal sealed record Instruction(byte Code,params uint[] Arguments);
    internal static IReadOnlyList<Instruction> Quad()=>new Instruction[]{new(0x14,0),new(0x21,511),new(0x22,0),new(0x40,1),
        new(0x23,0,0),new(0x24,64),new(0x25,0x10001000),new(0x26,0),new(0x41)};
    internal static IReadOnlyList<Instruction> Strip()=>new Instruction[]{new(0x14,0),new(0x21,511),new(0x22,0),new(0x40,3),
        new(0x23,0,0),new(0x24,64),new(0x25,0x10001000),new(0x26,0),new(0x24,128),new(0x25,0x10002000),new(0x41)};
    internal static Data Model(int nodes=1,int materials=1,int textures=1,int paletteEntries=2,int format=0,int width=8,int height=8,
        bool chain=false,IReadOnlyList<Instruction>? instructions=null,int? declaredVertices=null,int? declaredPrimitives=null)
    {
        instructions??=Quad();var b=new Blob();var at=new Dictionary<string,int>();b.Reserve(100);b.Word(0,1);b.Word(4,4096);
        int mats=b.Reserve(materials*132),dlists=b.Reserve(materials*32),ns=b.Reserve(nodes*240),matrix=b.Reserve(4),meshes=b.Reserve(materials*4),
            tx=b.Reserve(textures*40),pal=b.Reserve(16),pos=b.Reserve(4);
        b.Word(16,(uint)mats);b.Word(20,(uint)dlists);b.Word(24,(uint)ns);b.Half(28,1);b.Word(32,(uint)matrix);b.Word(36,(uint)meshes);
        b.Half(40,(ushort)textures);b.Word(44,(uint)tx);b.Half(48,1);b.Word(52,(uint)pal);b.Word(56,(uint)pos);b.Word(pos,1);
        b.Half(72,(ushort)materials);b.Half(74,(ushort)nodes);b.Half(96,(ushort)materials);b.Half(98,0);
        at["materials"]=mats;at["dlists"]=dlists;at["nodes"]=ns;at["matrix"]=matrix;at["meshes"]=meshes;at["textures"]=tx;at["palette"]=pal;at["posCounts"]=pos;
        for(int i=0;i<nodes;i++)
        {
            int n=ns+i*240;at["node"+i]=n;b.Name(n,"InventedNode"+i);b.Half(n+64,i==0?ushort.MaxValue:(ushort)(chain?i-1:0));
            b.Half(n+66,(ushort)(chain?(i+1<nodes?i+1:65535):(i==0&&nodes>1?1:65535)));
            b.Half(n+68,(ushort)(!chain&&i>0&&i+1<nodes?i+1:65535));b.Word(n+72,1);b.Half(n+76,(ushort)(i==0?materials:0));b.Half(n+78,0);
            for(int axis=0;axis<3;axis++)b.Word(n+80+axis*4,4096);
        }
        int vertices=instructions.Count(i=>i.Code is >=0x23 and <=0x26);
        int mode=instructions.FirstOrDefault(i=>i.Code==0x40)?.Arguments[0] is uint value?(int)value:1;
        int primitives=mode==3?Math.Max(0,vertices/2-1):vertices/4;
        b.Word(8,(uint)(declaredPrimitives??primitives*materials));b.Word(12,(uint)(declaredVertices??vertices*materials));
        for(int i=0;i<materials;i++)
        {
            int m=mats+i*132;at["material"+i]=m;b.Name(m,"InventedMaterial"+i);b.Byte(m+64,1);b.Byte(m+65,2);b.Byte(m+66,31);
            b.Half(m+68,0);b.Half(m+70,0);b.Byte(m+72,1);b.Byte(m+73,0);
            for(int color=74;color<83;color++)b.Byte(m+color,31);b.Word(m+104,4096);b.Word(m+108,4096);
            b.Half(meshes+i*4,(ushort)i);b.Half(meshes+i*4+2,(ushort)i);
            int start=b.Data.Count;
            for(int packet=0;packet<instructions.Count;packet+=4)
            {
                int commands=b.Reserve(4);int count=Math.Min(4,instructions.Count-packet);
                for(int c=0;c<count;c++)b.Byte(commands+c,instructions[packet+c].Code);
                for(int c=0;c<count;c++)
                {
                    int ordinal=packet+c;if(i==0){at["command"+ordinal]=commands+c;at["argument"+ordinal]=b.Data.Count;}
                    foreach(uint argument in instructions[ordinal].Arguments){int a=b.Reserve(4);b.Word(a,argument);}
                }
            }
            int dl=dlists+i*32;b.Word(dl,(uint)start);b.Word(dl+4,(uint)(b.Data.Count-start));for(int axis=0;axis<3;axis++)b.Word(dl+20+axis*4,4096);
            if(i==0){at["commandData"]=start;at["commandEnd"]=b.Data.Count;}
        }
        int bytes=width*height/(format==0?4:format==1?2:1);
        for(int i=0;i<textures;i++)
        {
            int texture=tx+i*40,image=b.Reserve(bytes,(byte)(format==0?0x55:format==1?0x11:format==6?0xa1:1));
            at["image"+i]=image;b.Byte(texture,(byte)format);b.Half(texture+2,(ushort)width);b.Half(texture+4,(ushort)height);b.Word(texture+8,(uint)image);b.Word(texture+12,(uint)bytes);b.Word(texture+28,1);
        }
        int colors=b.Reserve(paletteEntries*2);for(int i=0;i<paletteEntries;i++)b.Half(colors+i*2,(ushort)(i&31));b.Word(pal,(uint)colors);b.Word(pal+4,(uint)(paletteEntries*2));at["paletteData"]=colors;
        return new(b.Data.ToArray(),at);
    }
    internal static Data Animation(int nodes=1,int materials=1,int frames=3,int pool=3,int slots=1)
    {
        var b=new Blob();var at=new Dictionary<string,int>();b.Reserve(24);b.Half(20,(ushort)slots);b.Half(22,0xbeef);
        for(int c=0;c<5;c++){int table=b.Reserve(slots*4);b.Word(c*4,(uint)table);at["table"+c]=table;}
        int scale=b.Reserve(pool*4),rotation=b.Reserve(pool*2),translation=b.Reserve(pool*4),nodeRecords=b.Reserve(nodes*48),nodeGroup=b.Reserve(20);
        for(int i=0;i<pool;i++)b.Word(scale+4*i,4096);
        b.Word(nodeGroup,(uint)frames);b.Word(nodeGroup+4,(uint)scale);b.Word(nodeGroup+8,(uint)rotation);b.Word(nodeGroup+12,(uint)translation);b.Word(nodeGroup+16,(uint)nodeRecords);
        for(int i=0;i<nodes;i++)for(int group=0;group<3;group++)for(int axis=0;axis<3;axis++)
        {int n=nodeRecords+i*48+group*16;b.Byte(n+axis,1);b.Half(n+4+axis*2,(ushort)frames);b.Half(n+10+axis*2,0);}
        int colors=b.Reserve(pool,15),materialRecords=b.Reserve(materials*140),materialGroup=b.Reserve(20);
        b.Word(materialGroup,(uint)frames);b.Word(materialGroup+4,(uint)colors);b.Word(materialGroup+8,(uint)materials);b.Word(materialGroup+12,(uint)materialRecords);
        for(int i=0;i<materials;i++)
        {
            int m=materialRecords+i*140;b.Name(m,"InventedMaterial"+i);b.Word(m+64,1);b.Byte(m+87,255);
            for(int group=0;group<3;group++)for(int axis=0;axis<3;axis++){int g=m+68+group*16;b.Byte(g+axis,1);b.Half(g+4+axis*2,(ushort)frames);}
            b.Byte(m+132,1);b.Half(m+134,(ushort)frames);
        }
        for(int i=0;i<slots;i++){b.Word(at["table0"]+i*4,(uint)nodeGroup);b.Word(at["table2"]+i*4,(uint)materialGroup);}
        at["nodeGroup"]=nodeGroup;at["nodeRecords"]=nodeRecords;at["materialGroup"]=materialGroup;at["materialRecords"]=materialRecords;
        at["scale"]=scale;at["rotation"]=rotation;at["translation"]=translation;at["colors"]=colors;
        return new(b.Data.ToArray(),at);
    }
    internal sealed class Blob
    {
        internal readonly List<byte> Data=new();
        internal int Reserve(int count,byte fill=0){int at=Data.Count;Data.AddRange(Enumerable.Repeat(fill,count));return at;}
        internal int Add(byte[] bytes){int at=Data.Count;Data.AddRange(bytes);return at;}
        internal void Word(int at,uint value){for(int i=0;i<4;i++)Data[at+i]=(byte)(value>>(i*8));}
        internal void Half(int at,ushort value){Data[at]=(byte)value;Data[at+1]=(byte)(value>>8);}
        internal void Byte(int at,byte value)=>Data[at]=value;
        internal void Name(int at,string name){byte[] bytes=Encoding.ASCII.GetBytes(name);for(int i=0;i<bytes.Length;i++)Data[at+i]=bytes[i];}
    }
    internal static void Word(byte[] bytes,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at),value);
    internal static void Half(byte[] bytes,int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at),value);
}
