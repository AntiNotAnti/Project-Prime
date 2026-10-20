using System.Buffers.Binary;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

// All graph, skeleton-identity and ANIM bytes below are invented fixtures.
internal static class RetroAnimationArchiveCheck
{
    private static int _checks;
    internal static void Run()
    {
        foreach (bool echoes in new[] { false, true }) Test(echoes);
        AggregateDecodeWork();
        Console.WriteLine($"PASS: immutable, part-bound full ANIM archive ({_checks} checks)");
    }

    private static void Test(bool echoes)
    {
        TinyTickArchive(echoes);
        var graph = Graph(); byte[] cinf = { 4, 8, 15, 16, 23, 42 };
        byte[] raw = Animation(echoes); var requests = new List<uint>();
        byte[] archive = RetroCharacterAnimationArchive.Build(graph, cinf, echoes, id =>
        { requests.Add(id); return raw; });
        Check(requests.SequenceEqual(new uint[] { 7, 9 }), "shared leaves are captured once in sorted resource order");
        var snapshot = RetroCharacterAnimationArchive.Read(archive, graph, cinf, echoes);
        Check(snapshot.ByteLength == archive.Length && snapshot.SourceSha256 == Sha(archive)
            && snapshot.Resources.Select(r => r.Id).SequenceEqual(new uint[] { 7, 9 })
            && snapshot.Resources.All(r => r.Bytes == raw.Length && r.Sha256 == Sha(raw)),
            "archive exposes exact byte, digest and resource identities");
        Check(snapshot.Roots.Select(r => r.Ordinal).SequenceEqual(new[] { 0, 1, 2, 3 })
            && snapshot.Roots.Select(r => r.StrictPlay).SequenceEqual(new[] { true, false, false, true })
            && snapshot.Roots.Select(r => r.CharacterDeclared).SequenceEqual(new[] { true, true, false, true })
            && snapshot.Roots[1].Kind == "seq" && snapshot.Roots[1].ResourceId == null,
            "composite sharing one resource and nonzero source time remain nonprimitive roots");
        Throws<NotSupportedException>(() => snapshot.OpenStrictPrimitive(1), "sequence cannot become its shared leaf");
        Throws<NotSupportedException>(() => snapshot.OpenStrictPrimitive(2), "nonzero play time is not a zero-time primitive");
        Throws<ArgumentOutOfRangeException>(() => snapshot.OpenStrictPrimitive(-1), "negative root ordinal");
        Throws<ArgumentOutOfRangeException>(() => snapshot.OpenStrictPrimitive(4), "root ordinal extent");
        Throws<InvalidDataException>(() => snapshot.OpenResource(8), "undeclared resource lookup");
        ReadOnlyList(snapshot.Resources, snapshot.Resources[0], "resource inventory immutable");
        ReadOnlyList(snapshot.Roots, snapshot.Roots[0], "root inventory immutable");

        var clip = snapshot.OpenStrictPrimitive(0);
        Check(clip.Source.Id == 7 && clip.KeyCount == 3 && clip.Duration == 1 && clip.Tick == .5
            && clip.RotationChannels == 1 && clip.TranslationChannels == 1
            && clip.ScaleChannels == (echoes ? 1 : 0) && clip.RigidScale == !echoes,
            "full temporal inventory retains explicit scale qualification");
        var zero = clip.Sample(0); var middle = clip.Sample(.25); var last = clip.Sample(10);
        Check(zero.Translations[0].SequenceEqual(new double[] { 0, 2, 4 })
            && Math.Abs(middle.Translations[0][0] - .9999996) < 1e-10
            && Math.Abs(last.Translations[0][0] - 3.9999996) < 1e-10
            && zero.Rotations[0].SequenceEqual(new double[] { 0, 0, 0, 1 }),
            "sample uses full keys, existing endpoint epsilon and duration clamp");
        if (echoes)
            Check(zero.Scales[0].SequenceEqual(new double[] { 1, 2, 3 })
                && Math.Abs(middle.Scales[0][0] - 1.4999998) < 1e-10
                && last.Scales[0].Select((v, i) => Math.Abs(v - (2.9999998 + i)) < 1e-10).All(v => v),
                "scale is fully retained and interpolates alongside translation");
        else Check(zero.Scales.Count == 0, "Prime does not acquire invented scale keys");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => clip.Sample(invalid), "nonfinite sample time");
        Check(clip.Sample(-10).Translations[0].SequenceEqual(zero.Translations[0]), "negative time clamps to initial key");
        ReadOnlyList(zero.Translations[0], 42d, "pose component values immutable");
        ReadOnlyDictionary(zero.Translations, "pose channel dictionary immutable");
        clip.Sample(.75); snapshot.OpenResource(9).Sample(.1);
        Check(zero.Translations[0].SequenceEqual(new double[] { 0, 2, 4 })
            && clip.Sample(0).Translations[0].SequenceEqual(zero.Translations[0]),
            "retained pose and A/B/A sampling do not share mutable temporal state");

        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Read(archive, Graph("ball"), cinf, echoes), "body bank cannot bind ball");
        byte[] ball = RetroCharacterAnimationArchive.Build(Graph("ball"), cinf, echoes, _ => raw);
        Check(RetroCharacterAnimationArchive.Read(ball, Graph("ball"), cinf, echoes).Roots.Count == 4, "ball has its own reproducible identity");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Read(ball, graph, cinf, echoes), "ball bank cannot bind body");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Read(archive, Graph(character: "other-fixture"), cinf, echoes), "another canonical character cannot reuse bank");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Read(archive, graph, new byte[] { 4, 8, 15, 16, 23, 43 }, echoes), "skeleton bytes bind independently of CINF resource ID");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Read(archive, graph, cinf, !echoes), "source ANIM format identity");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Build(graph, Array.Empty<byte>(), echoes, _ => raw), "empty skeleton identity");

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); int calls = 0;
            Throws<OperationCanceledException>(() => RetroCharacterAnimationArchive.Build(graph, cinf, echoes,
                _ => { calls++; return raw; }, cancellation.Token), "pre-cancellation");
            Check(calls == 0, "pre-cancel cannot touch source resource callback");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            int calls = 0;
            Throws<OperationCanceledException>(() => RetroCharacterAnimationArchive.Build(graph, cinf, echoes,
                _ => { calls++; cancellation.Cancel(); return raw; }, cancellation.Token), "cancellation cannot publish a partial archive");
            Check(calls == 1, "cancellation stops before another source resource");
        }
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Build(graph, cinf, echoes, _ => new byte[3]), "minimum resource extent");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Build(graph, cinf, echoes,
            _ => new byte[MphRead.Mods.Import.Retro.Anim.MaximumResourceBytes + 1]), "per-resource byte budget");
        Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Build(graph, cinf, echoes,
            _ => new byte[] { 0, 0, 0, 3 }), "unqualified resource cannot be published");

        int first = 16 + BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(8, 4));
        int second = first + 40 + raw.Length;
        foreach (int cut in new[] { 0, 7, 8, 15, first - 1, first, first + 39, first + 40, second - 1, second + 39, archive.Length - 1 })
            Bad(archive.AsSpan(0, cut).ToArray(), graph, cinf, echoes, "truncated metadata/header/resource");
        foreach (int length in new[] { -1, 0, int.MaxValue, 1024 * 1024 + 1 })
            Bad(Patch(archive, 8, length), graph, cinf, echoes, "bounded metadata extent");
        foreach (int count in new[] { -1, 0, 1, 3, RetroCharacterAnimationArchive.MaximumResources + 1, int.MaxValue })
            Bad(Patch(archive, 12, count), graph, cinf, echoes, "count must exactly reproduce graph resource inventory");
        foreach (int length in new[] { -1, 0, 3, raw.Length + 1, int.MaxValue })
            Bad(Patch(archive, first + 4, length), graph, cinf, echoes, "bounded raw extent");
        foreach (int offset in new[] { 0, 16, first + 8, first + 40 })
        { byte[] corrupt = (byte[])archive.Clone(); corrupt[offset] ^= 1; Bad(corrupt, graph, cinf, echoes, "signature/metadata/digest/payload tamper"); }
        Bad(Patch(archive, first, 9), graph, cinf, echoes, "resource ordering must reproduce graph");
        Bad(Patch(archive, second, 7), graph, cinf, echoes, "duplicate resource cannot replace required ID");
        Bad(archive.Concat(new byte[] { 0 }).ToArray(), graph, cinf, echoes, "unaccounted tail");
        byte[] reversed = (byte[])archive.Clone();
        archive.AsSpan(second, 40 + raw.Length).CopyTo(reversed.AsSpan(first));
        archive.AsSpan(first, 40 + raw.Length).CopyTo(reversed.AsSpan(second));
        Bad(reversed, graph, cinf, echoes, "same resources in a different encoded order");

        // Metadata is rederived from the exact verified source, rather than trusting this DTO.
        var forged = graph with { Metadata = graph.Metadata with { SourceCharacterId = "forged", SourcePart = "ball" } };
        Check(RetroCharacterAnimationArchive.Build(forged, cinf, echoes, _ => raw).SequenceEqual(archive),
            "caller metadata cannot override verified graph identity");
        Array.Fill(raw, (byte)0); Array.Fill(archive, (byte)0); Array.Fill(cinf, (byte)0);
        Array.Fill(graph.SourceSnapshot, (byte)0); Array.Fill(graph.Descriptor, (byte)0);
        Check(snapshot.OpenResource(7).Sample(0).Translations[0].SequenceEqual(zero.Translations[0])
            && clip.Sample(0).Translations[0].SequenceEqual(zero.Translations[0]),
            "archive, clip and retained poses survive caller mutation of every input");
    }

    private static RetroAnimationGraphProof.Capture Graph(string? part = null, string character = "fixture")
    {
        var w = new Writer(); w.U32(0x00010001); w.U32(1); w.U32(0); w.U16(6); w.Text("fixture"); w.U32(1); w.U32(2); w.U32(3);
        w.U32(2); w.U32(0); w.Text("idle"); w.Text("idle"); w.U32(1); w.Text("sequence"); w.Text("sequence");
        w.Raw("PAS4"u8); w.U32(0); w.U32(0);
        for (int i = 0; i < 9; i++) w.U32(0); // particles4, unused, AABB, effects, frozen2
        w.U32(1); w.U32(3); // used root 3, in addition to named roots 0/1
        w.U16(1); w.U32(4);
        w.Text("idle"); Play(w, 7);
        w.Text("sequence"); w.U32(4); w.U32(2); Play(w, 7); Play(w, 7);
        w.Text("offset"); Play(w, 7, 0x3f800000);
        w.Text("other"); Play(w, 9);
        w.U32(3); w.U32(0); // v1: snap default transition, empty resource list
        return RetroAnimationGraphProof.FromSource(w.Array(), "G2ME01", 16, character, "fixture", new RetroCharacterRefs(1, 2, 3), part);
    }
    private static void TinyTickArchive(bool echoes)
    {
        byte[] raw=Animation(echoes);BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(12,4),1);
        var graph=Graph();byte[] cinf={4,8,15,16,23,42};
        byte[] bytes=RetroCharacterAnimationArchive.Build(graph,cinf,echoes,_=>raw);
        var clip=RetroCharacterAnimationArchive.Read(bytes,graph,cinf,echoes).OpenResource(7);
        Check(clip.Tick==(double)float.Epsilon&&clip.KeyCount==3,"archive preserves smallest positive finite tick");
        var half=clip.Sample((double)float.Epsilon/2);
        Check(half.Translations[0].SequenceEqual(new double[]{1,3,5}),"tiny tick retains finite interior translation sampling");
        foreach(double time in new[]{(double)float.Epsilon*2,1e-6,.25,1,double.MaxValue})
        {
            var end=clip.Sample(time);
            Check(end.Translations[0].SequenceEqual(new double[]{4,6,8})&&end.Rotations[0].All(double.IsFinite),
                "archive endpoint uses finite double frame comparison before integer narrowing");
            if(echoes)Check(end.Scales[0].SequenceEqual(new double[]{3,4,5}),
                "scale sampler also clamps enormous finite frame quotient before integer narrowing");
        }
        Check(half.Translations[0].SequenceEqual(new double[]{1,3,5}),"clamped endpoint samples do not mutate retained tiny-tick interior pose");
    }
    private static void AggregateDecodeWork()
    {
        // Every resource has zero decoded scalar channels, but the compressed
        // decoder still visits256 channel descriptors across20000 key steps.
        // Twelve fit64M operations; a thirteenth must be refused independently
        // of raw-byte/scalar limits, before performing another decoder loop.
        byte[] raw=EmptyChannels();byte[] cinf={4,8,15,16,23,42};
        var twelve=GraphWithResources(12);var thirteen=GraphWithResources(13);
        var isolated=Anim.LoadBounded(raw,true);
        Check(isolated.Keys==20000&&isolated.Rot.Count==0&&isolated.Trans.Count==0&&isolated.Scale.Count==0,
            "maximum empty-channel resource has no invented scalar payload but real bounded decode work");
        int calls=0;
        byte[] accepted=RetroCharacterAnimationArchive.Build(twelve,cinf,true,_=>{calls++;return raw;});
        Check(calls==12&&accepted.Length<100000&&RetroCharacterAnimationArchive.Read(accepted,twelve,cinf,true).Resources.Count==12,
            "twelve empty-channel resources fit work budget while staying far below encoded-byte limits");
        calls=0;
        ThrowsBudget(()=>RetroCharacterAnimationArchive.Build(thirteen,cinf,true,_=>{calls++;return raw;}),"work",
            "Build refuses thirteenth resource after twelve admissible empty-channel decodes");
        Check(calls==13,"aggregate Build work refusal occurs at thirteenth requested resource");

        // Produce a valid source/metadata/header template with cheap complete
        // resources, then independently reframe every entry with the larger
        // legal raw payload AND its correct digest. This bypasses Build's new
        // guard to exercise Read; no count/hash/truncation defect can explain
        // the required aggregate-work refusal.
        byte[] cheap=Animation(true);
        byte[] template12=RetroCharacterAnimationArchive.Build(twelve,cinf,true,_=>cheap);
        byte[] template13=RetroCharacterAnimationArchive.Build(thirteen,cinf,true,_=>cheap);
        byte[] forged12=ReplaceAllResources(template12,raw),forged13=ReplaceAllResources(template13,raw);
        Check(RetroCharacterAnimationArchive.Read(forged12,twelve,cinf,true).Resources.Count==12,
            "independently reframed/rehashed twelve-resource archive passes all structural and work checks");
        ThrowsBudget(()=>RetroCharacterAnimationArchive.Read(forged13,thirteen,cinf,true),"aggregate",
            "Read refuses valid-header correctly rehashed archive whose combined decode work exceeds64M");
    }
    private static RetroAnimationGraphProof.Capture GraphWithResources(int count)
    {
        var w=new Writer();w.U32(0x00010001);w.U32(1);w.U32(0);w.U16(6);w.Text("fixture");w.U32(1);w.U32(2);w.U32(3);
        w.U32(2);w.U32(0);w.Text("idle");w.Text("idle");w.U32(1);w.Text("other");w.Text("other");
        w.Raw("PAS4"u8);w.U32(0);w.U32(0);for(int i=0;i<9;i++)w.U32(0);
        w.U32(1);w.U32(3);w.U16(1);w.U32((uint)count);
        for(int i=0;i<count;i++){w.Text("resource"+i);Play(w,(uint)i+1);}
        w.U32(3);w.U32(0);
        return RetroAnimationGraphProof.FromSource(w.Array(),"G2ME01",16,"work-fixture","fixture",new RetroCharacterRefs(1,2,3));
    }
    private static byte[] EmptyChannels()
    {
        var w=new Writer();w.U32(2);w.U32(0);w.U16(0);w.F32(1);w.F32(.5f);
        w.U32(0);w.U32(0);w.U32(16384);w.F32(.5f);w.F32(.25f);w.U32(256);w.U32(0);w.U32(20000);
        for(int i=0;i<(20000+31)/32;i++)w.U32(0);w.U32(0);
        for(int bone=0;bone<256;bone++){w.Raw(new[]{(byte)bone});w.U16(0);w.U16(0);w.U16(0);}
        return w.Array();
    }
    private static byte[] ReplaceAllResources(byte[] archive,byte[] replacement)
    {
        int first=16+BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(8,4));
        int count=BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(12,4)),at=first;
        using var output=new MemoryStream();output.Write(archive.AsSpan(0,first));
        using(var writer=new BinaryWriter(output,Encoding.UTF8,leaveOpen:true))
            for(int i=0;i<count;i++)
            {
                uint id=BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(at,4));int length=BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(at+4,4));
                writer.Write(id);writer.Write(replacement.Length);writer.Write(SHA256.HashData(replacement));writer.Write(replacement);at+=40+length;
            }
        Check(at==archive.Length,"forged-work archive template consumes every original entry exactly once");return output.ToArray();
    }
    private static void ThrowsBudget(Action action,string expected,string why)
    {
        try{action();}catch(InvalidDataException error)
        {Check(error.Message.Contains(expected,StringComparison.OrdinalIgnoreCase),why+" identifies decoded budget rather than unrelated integrity failure");return;}
        throw new InvalidOperationException("Expected aggregate ANIM budget refusal: "+why);
    }
    private static void Play(Writer w, uint id, uint time = 0)
    { w.U32(0); w.U32(id); w.U32(12); w.Text("primitive"); w.U32(time); w.U32(2); }
    private static byte[] Animation(bool echoes)
    {
        var w = new Writer(); w.U32(0); w.F32(1); w.U32(0); w.F32(.5f); w.U32(0); w.U32(3); w.U32(0); w.U32(1); w.Raw(new byte[] { 0 });
        if (echoes) { w.U32(1); w.Raw(new byte[] { 0 }); }
        w.U32(1); w.Raw(new byte[] { 0 });
        if (echoes)
        { w.U32(1); w.Raw(new byte[] { 0 }); w.U32(3); for (int key = 0; key < 3; key++) w.Vec(1 + key, 2 + key, 3 + key); }
        w.U32(3); for (int key = 0; key < 3; key++) { w.F32(1); w.F32(0); w.F32(0); w.F32(0); }
        w.U32(3); for (int key = 0; key < 3; key++) w.Vec(key * 2, 2 + key * 2, 4 + key * 2);
        return w.Array();
    }
    private static byte[] Patch(byte[] input, int offset, int value)
    { byte[] copy = (byte[])input.Clone(); BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset, 4), value); return copy; }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Bad(byte[] input, RetroAnimationGraphProof.Capture graph, byte[] cinf, bool echoes, string why)
        => Throws<InvalidDataException>(() => RetroCharacterAnimationArchive.Read(input, graph, cinf, echoes), why);
    private static void ReadOnlyList<T>(IReadOnlyList<T> values, T replacement, string why)
    { Check(values is IList<T>, why + " exposes a checked readonly list"); Throws<NotSupportedException>(() => ((IList<T>)values)[0] = replacement, why); }
    private static void ReadOnlyDictionary(IReadOnlyDictionary<uint, IReadOnlyList<double>> values, string why)
    { Check(values is IDictionary<uint, IReadOnlyList<double>>, why + " exposes a checked readonly dictionary"); Throws<NotSupportedException>(() => ((IDictionary<uint, IReadOnlyList<double>>)values).Remove(0), why); }
    private static void Throws<T>(Action action, string why) where T : Exception
    { try { action(); } catch (T) { _checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
    private static void Check(bool value, string why)
    { if (!value) throw new InvalidOperationException(why); _checks++; }
    private sealed class Writer
    {
        private readonly MemoryStream _bytes = new();
        internal void U32(uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); _bytes.Write(bytes); }
        internal void U16(ushort value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); _bytes.Write(bytes); }
        internal void F32(float value) => U32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        internal void Vec(float x, float y, float z) { F32(x); F32(y); F32(z); }
        internal void Text(string value) => Raw(Encoding.ASCII.GetBytes(value + "\0"));
        internal void Raw(ReadOnlySpan<byte> value) => _bytes.Write(value);
        internal byte[] Array() => _bytes.ToArray();
    }
}
