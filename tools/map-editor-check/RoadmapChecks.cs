using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

internal static class RoadmapChecks
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static string Address(){using var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();return "http://127.0.0.1:"+((IPEndPoint)socket.LocalEndpoint).Port+"/";}
    public static async Task Run(Action<bool,string> check,string root)
    {
        await InstanceRoles(check);await LockChurn(check);CatalogBudget(check,Path.Combine(root,"catalog-budget"));
        await UploadReservations(check,Path.Combine(root,"upload-reservations"));
        await ControlIsolation(check,Path.Combine(root,"control-isolation"));
        await OutboxAuthentication(check);await CachePublication(check,root);CancelledPrivateCommit(check,root);ExportFlipbook(check,root);
        await IdentityCache(check);PrewarmRetry(check);ReaderDeclaredSizes(check,root);await Profile(check,root);
    }
    private static async Task InstanceRoles(Action<bool,string> check)
    {
        Process Child(bool editor)
        {
            var start=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--guard-child");if(editor)start.ArgumentList.Add("-mapstudio");
            // Framework-dependent tests may run under an apphost rather than dotnet.
            if(!Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.RemoveAt(0);
            return Process.Start(start)!;
        }
        using var client=Child(false);using var editor=Child(true);
        try
        {
            check(await client.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="READY","primary launcher role owns its instance guard");
            check(await editor.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="READY","explicit editor can coexist with launcher");
            foreach(bool role in new[]{false,true})
            {
                using var duplicate=Child(role);await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                check(duplicate.ExitCode!=0,"duplicate "+(role?"editor":"launcher")+" exits with refusal status");
            }
        }
        finally{client.StandardInput.WriteLine();editor.StandardInput.WriteLine();await Task.WhenAll(client.WaitForExitAsync(),editor.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(10));}
    }
    private static async Task LockChurn(Action<bool,string> check)
    {
        Type type=typeof(MapCreatorCatalog).Assembly.GetType("MphRead.Mods.MapGen.MapUploadLocks")!;
        object locks=Activator.CreateInstance(type,true)!;var acquire=type.GetMethod("AcquireAsync",Private|BindingFlags.Public)!;
        async Task<IDisposable> Acquire(string key,CancellationToken token=default)
        {var task=(Task)acquire.Invoke(locks,new object[]{key,token})!;await task;return (IDisposable)task.GetType().GetProperty("Result")!.GetValue(task)!;}
        for(int i=0;i<1000;i++)using(await Acquire(i.ToString())){}
        check((int)type.GetProperty("Count",Private)!.GetValue(locks)! == 0,"upload key churn returns to zero retained locks");
        using(var held=await Acquire("same"))
        {
            using var cancel=new CancellationTokenSource();var waiter=Acquire("same",cancel.Token);cancel.Cancel();
            try{await waiter;throw new Exception("key wait did not cancel");}catch(OperationCanceledException){}
            var next=Acquire("same");await Task.Delay(20);check(!next.IsCompleted,"canceled key waiter cannot bypass active owner");held.Dispose();using(await next){}
        }
        check((int)type.GetProperty("Count",Private)!.GetValue(locks)! == 0,"holders and canceled waiters reclaim their key exactly once");
    }
    private static void CatalogBudget(Action<bool,string> check,string root)
    {
        Directory.CreateDirectory(root);string path=Path.Combine(root,"map_reports.json");
        var rows=new List<CommunityMapReport>();
        var sample=new CommunityMapReport(Guid.NewGuid(),Guid.NewGuid().ToString(),Guid.NewGuid(),"1","Other",new string('a',4000),DateTimeOffset.UtcNow);
        int per=JsonSerializer.SerializeToUtf8Bytes(new[]{sample},MapPackageReader.JsonOptions).Length;
        int count=(MapCreatorCatalog.MaxCatalogBytes-16000)/(per-4);
        for(int i=0;i<count;i++)rows.Add(sample with{Id=Guid.NewGuid(),MapId=Guid.NewGuid(),ReporterId=Guid.NewGuid().ToString()});
        byte[] before=JsonSerializer.SerializeToUtf8Bytes(rows,MapPackageReader.JsonOptions);
        while(before.Length>MapCreatorCatalog.MaxCatalogBytes-12000){rows.RemoveAt(rows.Count-1);before=JsonSerializer.SerializeToUtf8Bytes(rows,MapPackageReader.JsonOptions);}
        while(before.Length<MapCreatorCatalog.MaxCatalogBytes-16000){rows.Add(sample with{Id=Guid.NewGuid(),MapId=Guid.NewGuid(),ReporterId=Guid.NewGuid().ToString()});before=JsonSerializer.SerializeToUtf8Bytes(rows,MapPackageReader.JsonOptions);}
        File.WriteAllBytes(path,before);var catalog=new MapCreatorCatalog(root,new string('s',32));bool refused=false;
        try{catalog.Report("new",Guid.NewGuid(),new("2","Other",new string('λ',4000)));}catch(InvalidDataException){refused=true;}
        check(refused&&File.ReadAllBytes(path).SequenceEqual(before),"UTF-8 escaped report over storage ceiling preserves accepted history");
        check(catalog.Reports().Length==rows.Count&&new MapCreatorCatalog(root,new string('s',32)).Reports().Length==rows.Count,"rejected mutation leaves memory and restarted catalog consistent");
        using(var file=new FileStream(path,FileMode.Create))file.SetLength(MapCreatorCatalog.MaxCatalogBytes+1L);
        var degraded=new MapCreatorCatalog(root,new string('s',32));bool blocked=false;try{degraded.Reports();}catch(IOException){blocked=true;}
        degraded.PurgeMapData(Guid.NewGuid());
        check(blocked&&new FileInfo(path).Length==MapCreatorCatalog.MaxCatalogBytes+1L,"oversized legacy moderation is preserved while other catalog operations remain available");
    }
    private static async Task UploadReservations(Action<bool,string> check,string root)
    {
        Directory.CreateDirectory(root);const string secret="roadmap-upload-local-service-token",ordinary="roadmap-upload-ordinary-local-token";string address=Address();
        File.WriteAllBytes(Path.Combine(root,"creators.json"),JsonSerializer.SerializeToUtf8Bytes(new[]{new MapCreatorCredential("ordinary",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ordinary))).ToLowerInvariant())},MapPackageReader.JsonOptions));
        using var stop=new CancellationTokenSource();var service=MapCommunityServer.ServeAsync(address,root,secret,stop.Token);
        using var http=new HttpClient{BaseAddress=new Uri(address)};http.DefaultRequestHeaders.Add("Authorization","Bearer "+secret);
        async Task<int> Start(int id,long bytes=1,string? credential=null)
        {using var body=new StringContent(JsonSerializer.Serialize(new MapUploadStartRequest(bytes,true,false),MapPackageReader.JsonOptions),Encoding.UTF8,"application/json");using var request=new HttpRequestMessage(HttpMethod.Post,"uploads/"+id.ToString("x64")){Content=body};request.Headers.TryAddWithoutValidation("Authorization","Bearer "+(credential??secret));using var response=await http.SendAsync(request);return (int)response.StatusCode;}
        try
        {
            for(int i=300;i<304;i++)check(await Start(i,1,ordinary)==200,"ordinary creator session reserved");
            check(await Start(304,1,ordinary)==429,"ordinary creator cannot monopolize global partial sessions");
            foreach(string path in Directory.EnumerateFiles(root,"upload-*"))File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-2));
            for(int i=0;i<15;i++)check(await Start(i)==200,"partial session admitted within global quota");
            var race=await Task.WhenAll(Enumerable.Range(15,8).Select(i=>Start(i)));
            check(race.Count(status=>status==200)==1&&Directory.EnumerateFiles(root,"upload-*.json").Count()==16,"concurrent admission reserves exactly the last session slot");
            foreach(string path in Directory.EnumerateFiles(root,"upload-*"))File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-2));
            check(await Start(100)==200&&Directory.EnumerateFiles(root,"upload-*.json").Count()==1,"expired sessions free quota without service restart");
            foreach(string path in Directory.EnumerateFiles(root,"upload-*"))File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-2));
            // Four 512 MiB declarations reserve 2 GiB even with no received bytes.
            for(int i=200;i<204;i++)check(await Start(i,512L*1024*1024)==200,"declared byte reservation admitted");
            check(await Start(204)==507,"empty partial files cannot evade declared aggregate byte quota");
        }
        finally{stop.Cancel();await service;}
        using var restartStop=new CancellationTokenSource();var restart=MapCommunityServer.ServeAsync(address,root,secret,restartStop.Token);
        try
        {
            check(await Start(205)==507,"restart reconstructs declared byte reservations from durable metadata");
            using var health=await http.GetAsync("health");check(health.IsSuccessStatusCode,"control health remains usable with exhausted upload byte quota");
        }
        finally{restartStop.Cancel();await restart;}
    }
    private static async Task ControlIsolation(Action<bool,string> check,string root)
    {
        Directory.CreateDirectory(root);var map=new MapDefinition{FormatVersion=2,MapId=Guid.NewGuid(),Name="TRANSFER_ISOLATION"};
        byte[] project=Encoding.UTF8.GetBytes(map.Serialize()),data=new byte[16*1024*1024];
        var manifest=new MapPackageManifest{MapId=map.MapId,Name=map.Name,ContentHash=MapPackageReader.ContentHash(new[]{"project.json","textures/payload.tex"},name=>name=="project.json"?project:data)};
        string temporary=Path.Combine(root,"fixture.tmp");
        using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Create))
        {
            using(var stream=zip.CreateEntry("project.json",CompressionLevel.NoCompression).Open())stream.Write(project);
            using(var stream=zip.CreateEntry("textures/payload.tex",CompressionLevel.NoCompression).Open())stream.Write(data);
            using(var stream=zip.CreateEntry("manifest.json").Open())stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest,MapPackageReader.JsonOptions));
        }
        string hash=MapBuildFingerprint.HashFile(temporary);File.Move(temporary,Path.Combine(root,hash+".ppmap"));
        string address=Address();using var stop=new CancellationTokenSource();var service=MapCommunityServer.ServeAsync(address,root,new string('s',32),stop.Token);
        using var http=new HttpClient{BaseAddress=new Uri(address),Timeout=TimeSpan.FromSeconds(5)};var held=new List<HttpResponseMessage>();
        try
        {
            for(int i=0;i<8;i++){var response=await http.GetAsync("packages/"+hash,HttpCompletionOption.ResponseHeadersRead);check(response.IsSuccessStatusCode,"bulk transfer occupies a bounded lane");held.Add(response);}
            var clock=Stopwatch.StartNew();using var health=await http.GetAsync("health");clock.Stop();
            check(health.IsSuccessStatusCode&&clock.Elapsed<TimeSpan.FromSeconds(2),"health control remains responsive with eight unread bulk downloads");
            using var excess=await http.GetAsync("packages/"+hash,HttpCompletionOption.ResponseHeadersRead);check((int)excess.StatusCode==429,"bulk saturation rejects excess download without consuming control lane");
        }
        finally{foreach(var response in held)response.Dispose();stop.Cancel();await service.WaitAsync(TimeSpan.FromSeconds(10));}
    }
    private static async Task OutboxAuthentication(Action<bool,string> check)
    {
        Type type=typeof(MapCreatorCatalog).Assembly.GetType("MphRead.Mods.Network.CareerReportOutbox")!;
        var drain=type.GetMethod("DrainAsync",Private)!;string address=Address();
        string? previousUrl=Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_REPORT_URL"),previousKey=Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY");
        string directory=(string)type.GetProperty("DirectoryPath",Private)!.GetValue(null)!;Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(path,"{}");
        using var listener=new HttpListener();listener.Prefixes.Add(address);listener.Start();
        try
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_CAREER_REPORT_URL",address);Environment.SetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY",new string('k',32));
            foreach(int status in new[]{401,403,200})
            {
                Task response=Task.Run(async()=>{var context=await listener.GetContextAsync();context.Response.StatusCode=status;context.Response.Close();});
                await (Task)drain.Invoke(null,null)!;await response;
                check(File.Exists(path)==(status!=200),"durable report retained on "+status+" and removed only after accepted retry");
                check(!Directory.EnumerateFiles(directory,"*.json.sending-*").Any(),"authentication retry restores claimed report filename");
            }
        }
        finally{Environment.SetEnvironmentVariable("PROJECT_PRIME_CAREER_REPORT_URL",previousUrl);Environment.SetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY",previousKey);if(File.Exists(path))File.Delete(path);}
    }
    private static async Task CachePublication(Action<bool,string> check,string root)
    {
        string cache=Path.Combine(root,"bounded-cache");
        var scheduler=new MapBuildScheduler(cache,build:(map,path)=>{foreach(string file in MapOutputSet.Create(map,path,path,path).Files)File.WriteAllBytes(file,new byte[512]);return new();},cacheBudgetBytes:5000);
        MapDefinition? latest=null;MapBuildResult? output=null;
        for(int i=0;i<30;i++){latest=new(){Name="CACHE_"+i};output=await scheduler.BuildAsync(MapBuildSnapshot.Capture(latest));check(output.Succeeded,"cache churn publishes complete output");}
        check(Directory.EnumerateDirectories(cache).Count()<=1&&Directory.EnumerateFiles(cache,"*.lock").Count()<=2,"disk cache budget reclaims inactive outputs and key files");
        check((await scheduler.BuildAsync(MapBuildSnapshot.Capture(latest!))).CacheHit,"protected most recent cache entry remains a warm hit");
        var acquire=typeof(MapRuntimeUsage).GetMethod("AcquirePreparation",Private)!;
        using(var lease=(IDisposable)acquire.Invoke(null,new[]{latest!.Name})!)
        {
            bool blocked=false;try{MapBuildScheduler.Publish(output!,latest,Path.Combine(root,"runtime"),root,root);}catch(IOException){blocked=true;}
            check(blocked&&!Directory.Exists(Path.Combine(root,"runtime")),"authored Forge publication refuses active preparation before changing outputs");
        }
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool canceled=false;
        try{MapBuildScheduler.Publish(output!,latest,Path.Combine(root,"runtime"),root,root,cancellation.Token);}catch(OperationCanceledException){canceled=true;}
        check(canceled&&!Directory.Exists(Path.Combine(root,"runtime")),"canceled authored publication leaves destination absent");
        string retained=Path.Combine(root,"leased-cache");var owner=new MapBuildScheduler(retained,build:(map,path)=>{foreach(string file in MapOutputSet.Create(map,path,path,path).Files)File.WriteAllText(file,"leased");return new();});
        var first=await owner.BuildAsync(MapBuildSnapshot.Capture(new MapDefinition{Name="LEASE_ONE"}));
        var second=await owner.BuildAsync(MapBuildSnapshot.Capture(new MapDefinition{Name="LEASE_TWO"}));
        Type disk=typeof(MapBuildScheduler).Assembly.GetType("MphRead.Mods.MapGen.MapDiskCache")!;
        using(var held=(IDisposable)disk.GetMethod("Acquire",Private)!.Invoke(null,new object[]{retained,first.Fingerprint,CancellationToken.None})!)
        {
            disk.GetMethod("Prune",Private)!.Invoke(null,new object[]{retained,1L,TimeSpan.FromDays(30),second.Fingerprint});
            check(File.Exists(first.Outputs!.Model),"budget eviction preserves an actively leased output directory");
        }
        disk.GetMethod("Prune",Private)!.Invoke(null,new object[]{retained,1L,TimeSpan.FromDays(30),second.Fingerprint});
        check(!File.Exists(first.Outputs!.Model)&&File.Exists(second.Outputs!.Model),"eviction reclaims released cache while preserving current publication");
        string orphanKey=new string('a',64),activeKey=new string('b',64);
        string orphan=Path.Combine(retained,".build-"+orphanKey+"-"+Guid.NewGuid().ToString("N")),active=Path.Combine(retained,".build-"+activeKey+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphan);Directory.CreateDirectory(active);File.WriteAllText(Path.Combine(orphan,"partial.bin"),"abandoned");
        using(var held=(IDisposable)disk.GetMethod("Acquire",Private)!.Invoke(null,new object[]{retained,activeKey,CancellationToken.None})!)
        {
            disk.GetMethod("Prune",Private)!.Invoke(null,new object[]{retained,1L,TimeSpan.FromDays(30),second.Fingerprint});
            check(!Directory.Exists(orphan)&&Directory.Exists(active),"abandoned tagged staging is reclaimed while active compiler staging remains protected");
        }
        disk.GetMethod("Prune",Private)!.Invoke(null,new object[]{retained,1L,TimeSpan.FromDays(30),second.Fingerprint});
        check(!Directory.Exists(active),"released incomplete compiler staging is reclaimed on the next sweep");
    }
    private static void CancelledPrivateCommit(Action<bool,string> check,string root)
    {
        string snapshot=Path.Combine(root,"private-cancel.ppmap");File.WriteAllText(snapshot,"private bytes");
        var identity=new MapContentIdentity(Guid.NewGuid(),"PRIVATE_CANCEL",MapHash256.Parse(new string('0',64)),MapHash256.Parse(new string('1',64)),true);
        var build=new MapBuildResult("",null,Array.Empty<MapDiagnostic>(),Array.Empty<MapBudget>(),false,0);
        using var prepared=(PreparedMapInstallation)Activator.CreateInstance(typeof(PreparedMapInstallation),Private,null,new object[]{snapshot,build,identity},null)!;
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool stopped=false;string library=Path.Combine(root,"cancel-library");
        try{prepared.Commit(library,cancellation:cancellation.Token);}catch(OperationCanceledException){stopped=true;}
        check(stopped&&!Directory.Exists(library),"canceled prepared package cannot create library or runtime outputs");
    }
    private static void ExportFlipbook(Action<bool,string> check,string root)
    {
        string source=Path.Combine(root,"export-source");Directory.CreateDirectory(source);
        string texture=Path.Combine(source,"frame.tex");
        using(var writer=new BinaryWriter(File.Create(texture)))
        {
            writer.Write(Encoding.ASCII.GetBytes("FPTX"));writer.Write((ushort)1);writer.Write((ushort)1);
            writer.Write((ushort)0);writer.Write((ushort)8);writer.Write((ushort)8);writer.Write((ushort)1);writer.Write((ushort)0);
            writer.Write((ushort)32767);writer.Write(new byte[64]);
        }
        var definition=new MapDefinition{Name="FLIPBOOK_EXPORT",BaseDirectory=source};definition.Assets.Add(new(){Path="frame.tex",Kind="texture"});definition.Materials.Add(new(){Texture="frame.tex",Animation=new(){FlipbookFrames=new(){"frame.tex"}}});
        string exported=MapProjectFolder.Export(definition,Path.Combine(root,"exported"));var reloaded=MapDefinition.Load(exported);
        var validation=new MapValidationResult();MapAssets.Validate(reloaded,validation,true);
        check(validation.IsValid&&reloaded.Materials[0].Animation!.FlipbookFrames[0]==reloaded.Materials[0].Texture,"portable authoring export remaps baked texture and all flipbook frames together");
    }
    private static async Task IdentityCache(Action<bool,string> check)
    {
        string address=Address();string? previous=Environment.GetEnvironmentVariable("PROJECT_PRIME_SUPABASE_URL");Environment.SetEnvironmentVariable("PROJECT_PRIME_SUPABASE_URL",address);
        using var listener=new HttpListener();listener.Prefixes.Add(address);listener.Start();using var stop=new CancellationTokenSource();int requests=0,active=0,peak=0;
        var responder=Task.Run(async()=>{var running=new List<Task>();while(!stop.IsCancellationRequested){HttpListenerContext context;try{context=await listener.GetContextAsync();}catch when(stop.IsCancellationRequested){break;}running.Add(Task.Run(async()=>{int now=Interlocked.Increment(ref active);int old;while(now>(old=Volatile.Read(ref peak)))if(Interlocked.CompareExchange(ref peak,now,old)==old)break;Interlocked.Increment(ref requests);try{await Task.Delay(5);byte[] body=JsonSerializer.SerializeToUtf8Bytes(new{creator_id="hunter:"+Guid.NewGuid(),moderator=false,expires_at=DateTimeOffset.UtcNow.AddMinutes(10)});await context.Response.OutputStream.WriteAsync(body);context.Response.Close();}finally{Interlocked.Decrement(ref active);}}));}await Task.WhenAll(running);});
        Type type=typeof(MapCreatorCatalog).Assembly.GetType("MphRead.Mods.MapGen.MapCommunityIdentityVerifier")!;var verifier=(IDisposable)Activator.CreateInstance(type,true)!;var auth=type.GetMethod("AuthenticateAsync")!;
        Task<MapCreatorCredential?> Authenticate(string ticket,CancellationToken token=default)=>(Task<MapCreatorCredential?>)auth.Invoke(verifier,new object[]{"Bearer ppm1."+ticket,token})!;
        try
        {
            await Task.WhenAll(Enumerable.Range(0,40).Select(_=>Authenticate("same")));check(requests==1,"same ticket verification is shared across concurrent callers");
            for(int batch=0;batch<17;batch++)await Task.WhenAll(Enumerable.Range(batch*64,64).Select(i=>Authenticate(i.ToString())));
            var cache=(IDictionary)type.GetField("_cache",Private)!.GetValue(verifier)!;
            check(cache.Count<=1024&&peak<=4,"verified ticket cache plateaus at capacity and external verification concurrency stays bounded");
            using var canceled=new CancellationTokenSource();var task=Authenticate("cancel",canceled.Token);var other=Authenticate("cancel");canceled.Cancel();try{await task;throw new Exception("identity wait did not cancel");}catch(OperationCanceledException){}
            check(await other!=null,"canceled ticket waiter preserves shared verification for live caller");
        }
        finally{verifier.Dispose();Environment.SetEnvironmentVariable("PROJECT_PRIME_SUPABASE_URL",previous);stop.Cancel();listener.Close();await responder;}
    }
    private static void ReaderDeclaredSizes(Action<bool,string> check,string root)
    {
        string path=Path.Combine(root,"declared-size.ppmap");const string asset="textures/declared.tex";
        byte[] payload=new byte[1024];var map=new MapDefinition{FormatVersion=2,MapId=Guid.NewGuid(),Name="DECLARED_SIZE"};byte[] project=Encoding.UTF8.GetBytes(map.Serialize());
        var manifest=new MapPackageManifest{MapId=map.MapId,Name=map.Name,ContentHash=MapPackageReader.ContentHash(new[]{"project.json",asset},name=>name=="project.json"?project:payload)};
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
        {
            using(var stream=zip.CreateEntry("project.json",CompressionLevel.NoCompression).Open())stream.Write(project);
            using(var stream=zip.CreateEntry(asset,CompressionLevel.NoCompression).Open())stream.Write(payload);
            using(var stream=zip.CreateEntry("manifest.json").Open())stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest,MapPackageReader.JsonOptions));
        }
        byte[] original=File.ReadAllBytes(path);int central=-1;
        for(int offset=0;offset<original.Length-46;offset++)
            if(BitConverter.ToUInt32(original,offset)==0x02014b50&&Encoding.UTF8.GetString(original,offset+46,BitConverter.ToUInt16(original,offset+28))==asset){central=offset;break;}
        if(central<0)throw new Exception("ZIP sizing fixture did not find central entry.");
        foreach(uint declared in new uint[]{0,2048})
        {
            byte[] changed=(byte[])original.Clone();System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(central+24,4),declared);File.WriteAllBytes(path,changed);
            bool strict=false;try{using var ignored=new MapPackageReader(path);}catch(InvalidDataException){strict=true;}
            check(strict,"streaming strict digest rejects actual payload different from declared ZIP size "+declared);
            bool direct=false;try{typeof(MapPackageReader).GetMethod("ReadValidatedEntry",Private)!.Invoke(null,new object[]{path,asset});}catch(TargetInvocationException ex)when(ex.InnerException is InvalidDataException){direct=true;}
            check(direct,"single-allocation entry read enforces declared ZIP size and final EOF "+declared);
        }
    }
    private static void PrewarmRetry(Action<bool,string> check)
    {
        var method=typeof(MphRead.Mods.RoomPrewarm).GetMethod("ReusePreparation",Private)!;var now=DateTimeOffset.UtcNow;
        bool Reuse(Task<bool> task,DateTimeOffset retry)=>(bool)method.Invoke(null,new object[]{task,retry,now})!;
        var pending=new TaskCompletionSource<bool>();
        check(Reuse(pending.Task,DateTimeOffset.MinValue)&&Reuse(Task.FromResult(true),DateTimeOffset.MinValue),"pending and completed ready prewarm are reused");
        check(Reuse(Task.FromResult(false),now.AddSeconds(1))&&!Reuse(Task.FromResult(false),now.AddSeconds(-1)),"busy or failed prewarm retries after bounded cooldown");
        check(!Reuse(Task.FromException<bool>(new IOException("failed")),now.AddSeconds(-1)),"faulted prewarm does not become permanent completed state");
    }
    private static async Task Profile(Action<bool,string> check,string root)
    {
        var map=new MapDefinition{Name="PREPARATION_PROFILE"};for(int i=0;i<1000;i++)map.Geometry.Add(new MapBox());var snapshot=MapBuildSnapshot.Capture(map);
        var scheduler=new MapBuildScheduler(Path.Combine(root,"profile-cache"),build:(definition,path)=>{foreach(string file in MapOutputSet.Create(definition,path,path,path).Files)File.WriteAllText(file,"profile");return new();});
        var clock=Stopwatch.StartNew();await Task.WhenAll(Enumerable.Range(0,64).Select(_=>scheduler.BuildAsync(snapshot)));clock.Stop();
        Console.WriteLine($"PROFILE preparation: 64 callers / 1000 boxes; peak {scheduler.PreparationPeak}; aggregate {scheduler.PreparationMilliseconds:0.0} ms; wall {clock.Elapsed.TotalMilliseconds:0.0} ms; shared {scheduler.SharedRequests}");check(scheduler.PendingCount==0,"preparation profile drains bounded compilation work");
        check(scheduler.PreparationPeak<=2,"preparation clone/hash concurrency shares the configured worker ceiling");
        var slots=(SemaphoreSlim)typeof(MapBuildScheduler).GetField("_preparationSlots",Private)!.GetValue(scheduler)!;
        await slots.WaitAsync();await slots.WaitAsync();
        try
        {
            using var cancellation=new CancellationTokenSource();var waiting=scheduler.BuildAsync(snapshot,cancellation.Token);cancellation.Cancel();
            try{await waiting;throw new Exception("preparation waiter ignored cancellation");}catch(OperationCanceledException){}
            check(scheduler.PendingCount==0,"canceled preparation admission does not queue compiler work");
        }
        finally{slots.Release(2);}
    }
}
