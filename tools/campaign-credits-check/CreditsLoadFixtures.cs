using System.Collections;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods.Campaign;
using Load=MphRead.Mods.Campaign.CampaignCreditsLoad;
using SourceFile=MphRead.Mods.Campaign.CampaignCreditsSourceFile;

// Filesystem fixtures contain only invented bytes. Retail manifest paths,
// lengths and hashes are metadata; none of those files is opened by this suite.
internal static class CreditsLoadFixtures
{
    private static int checks,linkCases,nonRegularCases;
    [DllImport("libc",EntryPoint="mkfifo",CallingConvention=CallingConvention.Cdecl,SetLastError=true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)]string path,uint mode);
    internal static void Run()
    {
        checks=0;linkCases=0;nonRegularCases=0;Manifest();Identities();
        string root=Path.Combine(Path.GetTempPath(),"credits-load-own-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{Reads(root);InvalidIdentity(root);Links(root);HandleBoundary(root);NonRegularFiles(root);}
        finally{Directory.Delete(root,recursive:true);}
        Check(!Directory.Exists(root),"all invented source files and link fixtures removed");
        Console.WriteLine($"CREDITS LOAD PASS {checks} invented filesystem/manifest checks; linkcases={linkCases}; nonregularcases={nonRegularCases}; no commercial bytes, assets, UI or GL.");
    }
    private static void Manifest()
    {
        var inputs=Load.Inputs;var expected=new HashSet<string>(StringComparer.Ordinal);
        string[] modelStems={"logo_screen/MAYA/flare2","logo_screen/MAYA/flare3","logo_screen/MAYA/logo2","logo_screen/MAYA/logo3",
            "main menu/toplogoR","tostart/logofinal","gameoptions/ok","models/hudfont"};
        foreach(string stem in modelStems)expected.Add(stem+"_Model.bin");
        void Animations(string stem,params string[] states){foreach(string state in states)expected.Add(stem+"_"+state+"_Anim.bin");}
        foreach(string stem in modelStems.Take(4))Animations(stem,"Hidden_Idle","Idle_Hidden");
        Animations("main menu/toplogoR","Hidden_Idle","Idle","Idle_Hidden");
        Animations("tostart/logofinal","Hidden_Idle","Idle");
        Animations("gameoptions/ok","Hidden_Idle","Idle","Idle_Hidden","Selected","Selected_Hidden");
        foreach(string path in new[]{"_bin/arm9.bin","frontend/metroidhunters.bin","frontend/metroidhunters_text_en.bin",
            "sourceimages/bg/good1.bin","sourceimages/bg/good2.bin","sourceimages/bg/bad1.bin","sourceimages/bg/bad2.bin"})expected.Add(path);
        Check(inputs.Count==33&&expected.Count==33&&expected.SetEquals(inputs.Select(i=>i.Path)),"exact33 declared source roles/IDs, not arbitrary same-count inventory");
        Check(inputs.Select(i=>i.Path).Distinct(StringComparer.Ordinal).Count()==33
            &&inputs.Select(i=>i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==33,"manifest contains no exact or case-alias duplicate paths");
        Check(inputs.Count(i=>i.Path.EndsWith("_Model.bin",StringComparison.Ordinal))==8
            &&inputs.Count(i=>i.Path.EndsWith("_Anim.bin",StringComparison.Ordinal))==18,"eight native models and eighteen animation identities retained");
        Check(inputs.All(i=>i.Bytes is>=1 and<=4194304&&i.Sha256.Length==64&&i.Sha256.All(c=>c is>='0'and<='9' or>='a'and<='f')),
            "all pinned metadata uses bounded positive bytes and canonical lower hexadecimal SHA256");
        Check(inputs.Sum(i=>(long)i.Bytes)==1847932,"independent retained manifest byte total is coherent across all33 roles");
        var byPath=inputs.ToDictionary(i=>i.Path,StringComparer.Ordinal);
        Check(byPath["frontend/metroidhunters.bin"].Bytes==512232&&byPath["frontend/metroidhunters_text_en.bin"].Bytes==30604
            &&byPath["_bin/arm9.bin"].Bytes==909912,"document/text/ARM9 source extents match their fixed manifest roles");
        Check(0xC020C+480==0xC03EC&&0xC03EC+480<0xC7F5C
            &&0xC7F5C+byPath["models/hudfont_Model.bin"].Bytes<=byPath["_bin/arm9.bin"].Bytes,
            "two480-byte metric spans and embedded font are bounded/disjoint within pinned ARM9 extent");
        Check(inputs.GroupBy(i=>i.Sha256,StringComparer.Ordinal).All(g=>g.Select(i=>i.Bytes).Distinct().Count()==1),"shared source hashes never disagree on exact byte extent");
        Check(inputs is IList{IsReadOnly:true},"manifest exposes readonly inventory");
        Expect<NotSupportedException>(()=>((IList)inputs).Clear(),"caller cannot remove manifest identities");
        var first=inputs[0];var modified=first with{Path="own/replacement.bin",Bytes=1,Sha256=new string('0',64)};
        Check(inputs[0]==first&&modified!=first,"record copy changes cannot retarget process manifest entries");
    }
    private static void Identities()
    {
        string missing=Path.Combine(Path.GetTempPath(),"credits-load-missing-"+Guid.NewGuid().ToString("N"));
        Check(Load.Supports(new(missing,"AMHE1",Language.English)),"identity support does not require path IO or pre-existing extraction");
        foreach(var identity in new[]{new CampaignCreditsSourceIdentity("\0","Other",Language.English),new("\0","amhe1",Language.English),
            new("\0","AMHE1",Language.French),new("\0","AMHE1",Language.Japanese),new("\0","AMHE1",(Language)999),
            new(" ","AMHE1",Language.English),new(null!,"AMHE1",Language.English)})
        {
            Check(!Load.Supports(identity),"unsupported edition/language/absent source identity refused");
            Expect<NotSupportedException>(()=>Load.Read(identity,CancellationToken.None),"unsupported profile rejected before invalid root can reach path IO");
        }
        Expect<DirectoryNotFoundException>(()=>Load.Read(new(missing,"AMHE1",Language.English),CancellationToken.None),"supported missing extraction is unavailable rather than implicit profile fallback");
        Check(!Directory.Exists(missing),"unsupported/missing identity checks create no directories");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Expect<OperationCanceledException>(()=>Load.Read(new("\0","Other",Language.English),canceled.Token),"cancellation observed before source identity/path work");
        Expect<OperationCanceledException>(()=>Load.ReadPinned("\0",null!,canceled.Token),"bounded read cancellation precedes metadata validation or file lookup");
    }
    private static void Reads(string root)
    {
        byte[] content={0,127,128,255,17,29,31};string path=Write(root,"own/payload.bin",content);var input=Pin("own/payload.bin",content);
        string before=Hash(File.ReadAllBytes(path));byte[] first=Load.ReadPinned(root,input,CancellationToken.None);
        Check(first.SequenceEqual(content)&&Hash(first)==input.Sha256,"regular bounded file returns byte-exact detached snapshot with independent known pattern");
        first[0]=99;byte[] second=Load.ReadPinned(root,input,CancellationToken.None);
        Check(!ReferenceEquals(first,second)&&second.SequenceEqual(content)&&Hash(File.ReadAllBytes(path))==before,
            "caller mutation cannot alter source file or later copied read");
        File.WriteAllBytes(path,new byte[]{1,2,3,4,5,6,7});
        Check(second.SequenceEqual(content),"already returned snapshot remains detached when source file later changes");
        Expect<InvalidDataException>(()=>Load.ReadPinned(root,input,CancellationToken.None),"same-size changed source bytes cannot satisfy pinned hash");
        foreach(int length in new[]{0,1,6,8,1024})
        {File.WriteAllBytes(path,new byte[length]);Expect<InvalidDataException>(()=>Load.ReadPinned(root,input,CancellationToken.None),"empty/truncated/grown source extent refused before returning partial data");}
        File.WriteAllBytes(path,content);
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Expect<OperationCanceledException>(()=>Load.ReadPinned(root,input,canceled.Token),"already canceled read does not open or expose existing source");
        Check(Hash(File.ReadAllBytes(path))==before,"failed/canceled read leaves original invented bytes unchanged");
        byte[] boundary=new byte[4194304];boundary[0]=17;boundary[^1]=239;Write(root,"own/boundary.bin",boundary);
        var copied=Load.ReadPinned(root,Pin("own/boundary.bin",boundary),CancellationToken.None);
        Check(copied.Length==4194304&&copied[0]==17&&copied[^1]==239,"exact4MiB extent admitted with first/final byte preserved");
        Directory.CreateDirectory(Path.Combine(root,"own","directory.bin"));
        Expect<InvalidDataException>(()=>Load.ReadPinned(root,Pin("own/directory.bin",content),CancellationToken.None),"directory cannot masquerade as regular pinned source");
    }
    private static void InvalidIdentity(string root)
    {
        byte[] content={17,19,23,29};Write(root,"own/identity.bin",content);var input=Pin("own/identity.bin",content);
        foreach(int length in new[]{-1,0,4194305,int.MaxValue})
            Expect<InvalidDataException>(()=>Load.ReadPinned("\0",input with{Bytes=length},CancellationToken.None),"unsupported declared extent refused before even invalid root lookup");
        foreach(string hash in new[]{"",new string('0',63),new string('0',65),new string('z',64),new string('0',64),input.Sha256.ToUpperInvariant()})
            Expect<InvalidDataException>(()=>Load.ReadPinned(root,input with{Sha256=hash},CancellationToken.None),"malformed/wrong/noncanonical expected hash cannot return source data");
        foreach(string path in new[]{"","../escape.bin","own/../identity.bin","./own/identity.bin","own/./identity.bin","own//identity.bin","own/",
            "/own/identity.bin",Path.Combine(root,"own","identity.bin"),"own\\identity.bin","C:/own/identity.bin","own:identity.bin"})
            Expect<InvalidDataException>(()=>Load.ReadPinned(root,input with{Path=path},CancellationToken.None),"traversal/rooted/backslash/drive/colon/empty segment identity refused");
        Expect<ArgumentNullException>(()=>Load.ReadPinned(root,null!,CancellationToken.None),"null input descriptor refused explicitly");
        var huge=input with{Bytes=int.MaxValue};Expect<InvalidDataException>(()=>Load.ReadPinned(root,huge,CancellationToken.None),"warm fake huge source declaration refusal");
        long before=GC.GetAllocatedBytesForCurrentThread();Expect<InvalidDataException>(()=>Load.ReadPinned(root,huge,CancellationToken.None),"huge input declaration deterministically refused");
        Check(GC.GetAllocatedBytesForCurrentThread()-before<1024*1024,"huge declared extent cannot allocate its claimed source buffer");
    }
    private static void Links(string root)
    {
        byte[] content={2,3,5,7};string physical=Path.Combine(root,"physical");Write(physical,"nested/payload.bin",content);
        string linkedFile=Path.Combine(root,"file-link.bin");
        try{File.CreateSymbolicLink(linkedFile,Path.Combine(physical,"nested","payload.bin"));}
        catch(Exception error)when(error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {Console.WriteLine("CREDITS LOAD LINK SKIP: symbolic-link creation unavailable on this test platform.");return;}
        try
        {
            Expect<InvalidDataException>(()=>Load.ReadPinned(root,Pin("file-link.bin",content),CancellationToken.None),"symbolic-link source file refused");linkCases++;
            string directory=Path.Combine(root,"directory-link"),aliasRoot=Path.Combine(root,"root-link");
            Directory.CreateSymbolicLink(directory,Path.Combine(physical,"nested"));Directory.CreateSymbolicLink(aliasRoot,physical);
            try
            {
                Expect<InvalidDataException>(()=>Load.ReadPinned(root,Pin("directory-link/payload.bin",content),CancellationToken.None),"source directory symlink refused");linkCases++;
                Expect<InvalidDataException>(()=>Load.ReadPinned(aliasRoot,Pin("nested/payload.bin",content),CancellationToken.None),"chosen root symlink refused");linkCases++;
                Expect<InvalidDataException>(()=>Load.ReadPinned(Path.Combine(aliasRoot,"nested"),Pin("payload.bin",content),CancellationToken.None),"symlink ancestor above chosen root refused");linkCases++;
            }
            finally{Directory.Delete(directory);Directory.Delete(aliasRoot);}
        }
        finally{File.Delete(linkedFile);}
        Check(File.ReadAllBytes(Path.Combine(physical,"nested","payload.bin")).SequenceEqual(content),"link refusal never modifies linked target bytes");
    }
    private static void HandleBoundary(string root)
    {
        byte[] bytes={0,1,127,128,254,255};string path=Write(root,"handles/ordinary.bin",bytes);
        using(var stream=SourceFile.Open(path,CancellationToken.None))
        {
            Check(stream.CanRead&&stream.CanSeek&&stream.Length==bytes.Length,"classified ordinary handle is a readable seekable exact file");
            var actual=new byte[bytes.Length];stream.ReadExactly(actual);
            Check(actual.SequenceEqual(bytes)&&stream.ReadByte()==-1,"direct regular handle yields exactly invented bytes and EOF");
        }
        Check(Load.ReadPinned(root,Pin("handles/ordinary.bin",bytes),CancellationToken.None).SequenceEqual(bytes),
            "same-open-handle boundary preserves independent ReadPinned length/hash proof");
        string empty=Write(root,"handles/empty.bin",Array.Empty<byte>());
        using(var stream=SourceFile.Open(empty,CancellationToken.None))
            Check(stream.Length==0&&stream.ReadByte()==-1,"regular-file classifier admits empty file while loader separately rejects zero-byte input profile");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Expect<OperationCanceledException>(()=>{using var ignored=SourceFile.Open("\0",canceled.Token);},"direct cancellation occurs before invalid path/open");
        Expect<InvalidDataException>(()=>{using var ignored=SourceFile.Open("\0",CancellationToken.None);},"direct malformed path refused without opening object");
        // A sparse file proves Open classifies an object rather than allocating
        // its payload. The loader remains the separate exact-size/budget owner.
        string sparse=Path.Combine(root,"handles","sparse.bin");
        using(var create=new FileStream(sparse,FileMode.CreateNew,FileAccess.Write))create.SetLength(4194305);
        using(var warm=SourceFile.Open(sparse,CancellationToken.None))Check(warm.Length==4194305,"direct classification does not silently apply the loader's4MiB profile cap");
        long before=GC.GetAllocatedBytesForCurrentThread();
        using(var stream=SourceFile.Open(sparse,CancellationToken.None))Check(stream.Length==4194305,"ordinary large extent can be inspected without payload read");
        Check(GC.GetAllocatedBytesForCurrentThread()-before<1024*1024,"classified regular open allocates no multi-megabyte source payload");
        Expect<InvalidDataException>(()=>Load.ReadPinned(root,new("handles/sparse.bin",4194304,new string('0',64)),CancellationToken.None),
            "ReadPinned still refuses a grown regular extent before its claimed payload allocation");
    }
    private static void NonRegularFiles(string root)
    {
        if(!(OperatingSystem.IsMacOS()||OperatingSystem.IsLinux()))
        {Console.WriteLine("CREDITS LOAD NONREGULAR SKIP: Unix FIFO/socket/device fixture is not enabled on this platform.");return;}
        string fifo=Path.Combine(root,"own-fifo");
        Check(MakeFifo(fifo,384)==0,"invented private FIFO created with0600 permissions and no writer");
        // Keep the Unix socket path below Darwin sockaddr_un's small limit;
        // macOS's normal per-user TMPDIR can itself exceed that limit.
        string socketRoot=Path.Combine(OperatingSystem.IsMacOS()?"/private/tmp":"/tmp","pccf-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(socketRoot);string socketPath=Path.Combine(socketRoot,"s");
        try
        {
            using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            string? device=File.Exists("/dev/null")?"/dev/null":null;
            void RefuseAll()
            {
                ExpectFileRefusal(()=>{using var ignored=SourceFile.Open(fifo,CancellationToken.None);},"FIFO with no writer promptly refused as nonregular without read/handshake");
                ExpectFileRefusal(()=>Load.ReadPinned(root,new("own-fifo",1,new string('0',64)),CancellationToken.None),"ReadPinned rejects no-writer FIFO through same classified handle boundary");
                ExpectFileRefusal(()=>{using var ignored=SourceFile.Open(socketPath,CancellationToken.None);},"bound Unix socket path cannot become source file");
                ExpectFileRefusal(()=>Load.ReadPinned(socketRoot,new("s",1,new string('0',64)),CancellationToken.None),"ReadPinned rejects bound Unix socket before payload allocation");
                if(device!=null)ExpectFileRefusal(()=>{using var ignored=SourceFile.Open(device,CancellationToken.None);},"read-only OS null character device refused before any payload read");
            }
            // A regression to blocking open must fail this diagnostic rather
            // than hang its process. There is no writer, close peer, polling or
            // race manufactured to rescue an incorrectly opened FIFO.
            Bounded(RefuseAll,"initial no-writer FIFO/socket/device refusal");
            string descriptors=OperatingSystem.IsMacOS()?"/dev/fd":"/proc/self/fd";
            if(!Directory.Exists(descriptors))
            {Console.WriteLine("CREDITS LOAD DESCRIPTOR SKIP: descriptor inventory directory unavailable.");return;}
            int before=Directory.GetFileSystemEntries(descriptors).Length;
            Bounded(()=>{for(int i=0;i<32;i++)RefuseAll();},"repeated nonregular classification/refusal");
            int after=Directory.GetFileSystemEntries(descriptors).Length;
            Check(after==before,"repeated classified nonregular failures close every opened descriptor");
            nonRegularCases=device==null?4:5;
            Console.WriteLine($"CREDITS LOAD HANDLE METADATA platform={RuntimeInformation.OSDescription} arch={RuntimeInformation.ProcessArchitecture} nonregular={nonRegularCases} descriptorBefore={before} descriptorAfter={after}; no original source IO.");
        }
        finally{File.Delete(socketPath);Directory.Delete(socketRoot);File.Delete(fifo);}
    }
    private static void ExpectFileRefusal(Action action,string label)
    {
        checks++;
        try{action();}
        catch(Exception error)when(error is IOException or InvalidDataException){return;}
        throw new InvalidOperationException("Credits load expected IO/nonregular refusal: "+label);
    }
    private static void Bounded(Action action,string label)
    {
        Exception? failure=null;var stopwatch=Stopwatch.StartNew();
        var worker=new Thread(()=>{try{action();}catch(Exception error){failure=error;}}){IsBackground=true};worker.Start();
        Check(worker.Join(TimeSpan.FromSeconds(5)),label+" must finish within safety watchdog, not wait for FIFO writer");
        if(failure!=null)throw new InvalidOperationException("Credits load bounded fixture failed: "+label,failure);
        Check(stopwatch.Elapsed<TimeSpan.FromSeconds(5),label+" completed before watchdog; this is not an IO latency benchmark");
    }
    private static string Write(string root,string relative,byte[] bytes)
    {string path=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);return path;}
    private static Load.Input Pin(string path,byte[] bytes)=>new(path,bytes.Length,Hash(bytes));
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Credits load: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Credits load expected "+typeof(T).Name+": "+label);}
}
