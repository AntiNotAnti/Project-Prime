using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MphRead.Protocol.Generator;

string attributes = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NetPacketAttribute.cs.txt"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
int checks = 0;
(Compilation Output, GeneratorDriverRunResult Run) Generate(string body)
{
    var compilation = CSharpCompilation.Create("Fixture" + Guid.NewGuid().ToString("N"),
        [CSharpSyntaxTree.ParseText(attributes), CSharpSyntaxTree.ParseText(body)], references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    GeneratorDriver driver = CSharpGeneratorDriver.Create(new NetPacketGenerator().AsSourceGenerator());
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
    return (output, driver.GetRunResult());
}
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL " + description);
    checks++; Console.WriteLine("NETGEN PASS " + description);
}
string preamble = "using System; using MphRead.Mods.Network.Generated; ";
foreach (var test in new (string Code, string Diagnostic)[]
{
    ("[NetPacket(1, 34)] readonly partial record struct P(float Value);", "NETGEN001"),
    ("[NetPacket(1, 34)] readonly partial record struct P(string Value);", "NETGEN002"),
    ("[NetPacket(1, 34)] readonly partial record struct P([NetRange(10, 1)] byte Value);", "NETGEN003"),
    ("enum Empty {} [NetPacket(1, 34)] readonly partial record struct P(Empty Value);", "NETGEN004"),
    ("[Flags] enum Flags { A=1 } [NetPacket(1,34)] readonly partial record struct P(Flags Value);", "NETGEN004"),
    ("[NetPacket(1, 34)] readonly partial record struct P(byte[] Value);", "NETGEN005"),
    ("[NetPacket(1, 34)] readonly partial record struct P([NetString(1472)] string Value);", "NETGEN006"),
    ("[NetPacket(1, 34)] readonly partial record struct P(byte Value); [NetPacket(1, 34)] readonly partial record struct Q(byte Value);", "NETGEN007"),
    ("[NetPacket(1, 0)] readonly partial record struct P(byte Value);", "NETGEN008"),
    ("[NetPacket(1,34)] readonly partial record struct P([NetRange(-1,2)] uint Value);", "NETGEN003"),
    ("[NetPacket(1,34)] partial record struct P(byte Value);", "NETGEN001"),
    ("[NetPacket(\"1\",34)] readonly partial record struct P(byte Value);", "NETGEN001"),
    ("[NetPacket(1,34)] readonly partial record struct P(byte Size);", "NETGEN001"),
    ("[NetPacket(256,34)] readonly partial record struct P(byte Value);", "NETGEN001"),
    ("[NetPacket(1,34)] readonly partial record struct P([NetString(-1)] string Value);", "NETGEN002"),
    ("[NetPacket(1,34)] readonly partial record struct P([NetFixedArray(32)] byte[] Value);", "NETGEN005")
})
{
    var result = Generate(preamble + test.Code);
    Check(result.Run.Diagnostics.Any(d => d.Id == test.Diagnostic), test.Diagnostic + " " + test.Code);
    Check(result.Run.Results.All(r => r.Exception == null), "schema rejection has no generator exception");
}
// The consuming transport's lower bound must override the generator's fallback.
var bound = Generate(preamble + "namespace MphRead.Mods.Network { static class NetConfig { public const int MaxPacketSize=4; } } [NetPacket(1,34)] readonly partial record struct P(uint Value);");
Check(bound.Run.Diagnostics.Any(d => d.Id == "NETGEN006"), "transport MaxPacketSize controls schema bound");
var valid = Generate(preamble + """
namespace Fixtures {
    enum PacketIds : byte { Test = 201 }
    enum Mode : byte { First = 1, Last = 3 }
    enum Signed : long { Min = long.MinValue, Max = long.MaxValue }
    enum Unsigned : ulong { Max = ulong.MaxValue }
    [NetPacket(PacketIds.Test,34)] readonly partial record struct Packet(
        [NetRange(1,8)] byte Count, bool Ready, Mode Kind, ushort Life, uint Match,
        [NetString(8)] string Name);
    [NetPacket(202,34)] readonly partial record struct Fixed(sbyte A, short B, int C,
        long D, ulong E, Signed F, Unsigned G);
    [NetPacket(203,34)] readonly partial record struct Boundary([NetString(1469)] string Value);
    [NetPacket(204,34)] readonly partial record struct Names(int offset, int size,
        [NetString(4)] string Value, [NetString(4)] string ValueLength);
    public static class WireChecks {
        static int count;
        static void Assert(bool value) { if (!value) throw new Exception("Wire assertion " + count); count++; }
        public static int Run() {
            var packet = new Packet(2, true, Mode.Last, 0x1234, 0x78563412, "é");
            byte[] bytes = new byte[packet.EncodedSize]; packet.Write(bytes);
            byte[] golden = { 201, 2, 1, 3, 0x34, 0x12, 0x12, 0x34, 0x56, 0x78, 2, 0, 0xc3, 0xa9 };
            Assert(bytes.AsSpan().SequenceEqual(golden));
            Assert(Packet.TryRead(bytes, out var decoded) && decoded == packet);
            Assert(Packet.MinimumSize == 12 && Packet.MaximumSize == 20);
            for(int n=0;n<bytes.Length;n++) Assert(!Packet.TryRead(bytes.AsSpan(0,n),out _));
            Assert(!Packet.TryRead(bytes.Concat(new byte[]{0}).ToArray(),out _));
            foreach(var corruption in new[]{(0,(byte)200),(1,(byte)0),(2,(byte)2),(3,(byte)2),(10,(byte)9),(12,(byte)0xff)}) {
                var bad=(byte[])bytes.Clone(); bad[corruption.Item1]=corruption.Item2;
                Assert(!Packet.TryRead(bad,out var rejected) && rejected == default);
            }
            var maximum = new Packet(8,false,Mode.First,0,0,"éééé");
            byte[] max = new byte[maximum.EncodedSize]; maximum.Write(max);
            Assert(max.Length == Packet.MaximumSize && Packet.TryRead(max,out _));
            Assert(!new Packet(1,false,Mode.First,0,0,"ééééé").Validate());
            Assert(!new Packet(1,false,Mode.First,0,0,"\ud800").Validate());
            Assert(!new Packet(1,false,Mode.First,0,0,null!).Validate());
            byte[] untouched = Enumerable.Repeat((byte)77,20).ToArray();
            try { new Packet(0,false,Mode.First,0,0,"").Write(untouched); Assert(false); }
            catch(InvalidOperationException) { Assert(untouched.All(x=>x==77)); }
            byte[] shortBuffer=Enumerable.Repeat((byte)77,2).ToArray();
            try { packet.Write(shortBuffer); Assert(false); }
            catch(ArgumentException) { Assert(shortBuffer.All(x=>x==77)); }
            var fixedPacket = new Fixed(-1,-2,-3,long.MinValue,ulong.MaxValue,Signed.Min,Unsigned.Max);
            byte[] fixedBytes=new byte[Fixed.Size]; fixedPacket.Write(fixedBytes);
            Assert(fixedBytes.AsSpan().SequenceEqual(Convert.FromHexString("CAFFFEFFFDFFFFFF0000000000000080FFFFFFFFFFFFFFFF0000000000000080FFFFFFFFFFFFFFFF")));
            Assert(Fixed.TryRead(fixedBytes,out var fixedCopy) && fixedCopy==fixedPacket);
            for(int n=0;n<Fixed.Size;n++) Assert(!Fixed.TryRead(fixedBytes.AsSpan(0,n),out _));
            Assert(!Fixed.TryRead(fixedBytes.Concat(new byte[]{0}).ToArray(),out _));
            var boundary = new Boundary(new string('a',1469));
            byte[] boundaryBytes=new byte[boundary.EncodedSize]; boundary.Write(boundaryBytes);
            Assert(boundaryBytes.Length==1472 && Boundary.TryRead(boundaryBytes,out var boundaryCopy) && boundaryCopy==boundary);
            var names = new Names(12,34,"a","b"); byte[] namesBytes=new byte[names.EncodedSize]; names.Write(namesBytes);
            Assert(Names.TryRead(namesBytes,out var namesCopy) && namesCopy==names);
            // Any datagram up to and beyond MTU must fail safely or round-trip.
            var random=new Random(1234);
            for(int i=0;i<5000;i++) {
                byte[] fuzz=new byte[random.Next(0,1500)]; random.NextBytes(fuzz);
                if(Packet.TryRead(fuzz,out var accepted)) {
                    byte[] encoded=new byte[accepted.EncodedSize]; accepted.Write(encoded);
                    Assert(encoded.AsSpan().SequenceEqual(fuzz));
                }
            }
            return count;
        }
    }
}
""".Replace("namespace Fixtures", "using System.Linq; namespace Fixtures"));
var errors = valid.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
foreach(var d in errors) Console.Error.WriteLine(d);
foreach(var d in valid.Run.Diagnostics) Console.Error.WriteLine(d);
Check(errors.Length == 0 && valid.Run.Diagnostics.Length == 0, "generated fixtures compile without errors");
using var stream = new MemoryStream();
var emitted = valid.Output.Emit(stream);
Check(emitted.Success, "generated fixture assembly emits");
var assembly = Assembly.Load(stream.ToArray());
var assertions = (int)assembly.GetType("Fixtures.WireChecks")!.GetMethod("Run")!.Invoke(null,null)!;
Console.WriteLine($"NETGEN PASS {checks} generator checks, {assertions} wire assertions, 5000 malformed datagrams");
