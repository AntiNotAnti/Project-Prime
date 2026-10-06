using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using MphRead.Mods.MapGen;
using MphRead.Mods.MapEditor;

const string built = "/Users/jarrett/.codex/worktrees/engineering-audit/Prime Hunters Online/tools/map-editor-check/bin/Release/net10.0";
AssemblyLoadContext.Default.Resolving += (_, n) => { string p=Path.Combine(built,n.Name+".dll"); return File.Exists(p)?Assembly.LoadFrom(p):null; };
string root=Path.Combine(Path.GetTempPath(),"prime-confirmed-repros-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    string textures=Path.Combine(root,"source");Directory.CreateDirectory(textures);
    foreach(string name in new[]{"base.tex","frame.tex"})
    {
        using var w=new BinaryWriter(File.Create(Path.Combine(textures,name)));
        w.Write(System.Text.Encoding.ASCII.GetBytes("FPTX"));w.Write((ushort)1);w.Write((ushort)1);
        w.Write((ushort)0);w.Write((ushort)8);w.Write((ushort)8);w.Write((ushort)1);w.Write((ushort)0);
        w.Write((ushort)(name=="base.tex"?32767:31));w.Write(new byte[64]);
    }
    var project=MapTemplates.Create("FLIPBOOK EXPORT AUDIT","basic-ffa");var source=project.Definition;
    source.BaseDirectory=textures;
    source.Assets.Add(new(){Path="base.tex",Kind="texture"});source.Assets.Add(new(){Path="frame.tex",Kind="texture"});
    foreach(var m in source.Materials)m.Texture="base.tex";
    source.Materials[0].Animation=new(){FlipbookFrames=new(){"frame.tex"}};
    var initial=MapValidator.Validate(source);if(!initial.IsValid)throw new Exception("Source invalid: "+string.Join(" | ",initial.Diagnostics.Select(d=>d.Message)));
    string exported=MapProjectFolder.Export(source,Path.Combine(root,"exported"));var after=MapDefinition.Load(exported);
    var result=MapValidator.Validate(after);
    Console.WriteLine("FLIPBOOK source-valid="+initial.IsValid+", exported-valid="+result.IsValid+", frame="+after.Materials.Single(m=>m.Animation!=null).Animation!.FlipbookFrames[0]);
    foreach(var d in result.Diagnostics.Where(d=>d.Severity==MapDiagnosticSeverity.Error))Console.WriteLine(d.Code+": "+d.Message);
    if(result.IsValid)throw new Exception("Export regression did not reproduce");

    string storage=Path.Combine(root,"catalog");Directory.CreateDirectory(storage);const int limit=32*1024*1024;
    var reports=Enumerable.Range(0,10000).Select(i=>new CommunityMapReport(Guid.NewGuid(),"hunter:"+i,Guid.NewGuid(),null,"Other",new string('X',4000),DateTimeOffset.UtcNow)).ToArray();
    byte[] Serialize(int count)=>JsonSerializer.SerializeToUtf8Bytes(reports.Take(count).ToArray(),MapPackageReader.JsonOptions);
    int lo=0,hi=10000;while(lo<hi){int mid=(lo+hi+1)/2;if(Serialize(mid).Length<=limit)lo=mid;else hi=mid-1;}
    byte[] previous=Serialize(lo);File.WriteAllBytes(Path.Combine(storage,"map_reports.json"),previous);
    var catalog=new MapCreatorCatalog(storage,new string('S',24));catalog.Report("hunter:new",Guid.NewGuid(),new(null,"Other",new string('Y',4000)));
    long saved=new FileInfo(Path.Combine(storage,"map_reports.json")).Length;bool refused=false;
    try{_ = new MapCreatorCatalog(storage,new string('S',24));}catch(InvalidDataException ex){refused=true;Console.WriteLine("CATALOG reload-error="+ex.Message);}
    Console.WriteLine($"CATALOG existing-reports={lo}, before={previous.Length}, after={saved}, load-limit={limit}, reload-refused={refused}");
    if(!refused)throw new Exception("Catalog regression did not reproduce");

    var runtimeMap=new MapDefinition(){Name="AUDIT LEASE",FormatVersion=2,MapId=Guid.NewGuid()};
    var scheduler=new MapBuildScheduler(Path.Combine(root,"cache"),build:(map,directory)=>{
        foreach(var f in MapOutputSet.Create(map,directory,directory,directory).Files)File.WriteAllText(f,"audit");return new MapValidationResult();
    });
    var build=await scheduler.BuildAsync(MapBuildSnapshot.Capture(runtimeMap));if(!build.Succeeded)throw new Exception("Fixture build failed");
    using(var preparation=(IDisposable)typeof(MapRuntimeUsage).GetMethod("AcquirePreparation",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{runtimeMap.Name})!)
    {
        bool fence=false;try{MapRuntimeUsage.RequireInstallationAllowed(runtimeMap.Name);}catch(IOException){fence=true;}
        string runtime=Path.Combine(root,"runtime");Directory.CreateDirectory(runtime);
        MapBuildScheduler.Install(build,runtimeMap,runtime,runtime,runtime);
        Console.WriteLine("INSTALL preparation-fence-refused="+fence+", raw-install-published="+MapOutputSet.Create(runtimeMap,runtime,runtime,runtime).Complete);
    }

    string uploads=Path.Combine(root,"uploads");Directory.CreateDirectory(uploads);
    using var port=new TcpListener(IPAddress.Loopback,0);port.Start();string address="http://127.0.0.1:"+((IPEndPoint)port.LocalEndpoint).Port+"/";port.Stop();
    string secret=new string('T',32);using var http=new HttpClient(){BaseAddress=new Uri(address)};
    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization","Bearer "+secret);
    async Task<HttpStatusCode> StartUpload(int i){string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("session"+i))).ToLowerInvariant();using var r=await http.PostAsync("uploads/"+hash,new StringContent(JsonSerializer.Serialize(new MapUploadStartRequest(128,false,false),MapPackageReader.JsonOptions),Encoding.UTF8,"application/json"));return r.StatusCode;}
    using(var stop=new CancellationTokenSource())
    {
        var service=MapCommunityServer.ServeAsync(address,uploads,secret,stop.Token);
        try
        {
            for(int i=0;i<16;i++){var status=await StartUpload(i);if(status!=HttpStatusCode.OK)throw new Exception("Start upload "+status);}
            foreach(string f in Directory.EnumerateFiles(uploads,"upload-*.json"))File.SetLastWriteTimeUtc(f,DateTime.UtcNow.AddDays(-2));
            Console.WriteLine("UPLOAD expired-sessions="+Directory.EnumerateFiles(uploads,"upload-*.json").Count()+", seventeenth="+(int)await StartUpload(16));
        }
        finally{stop.Cancel();await service;}
    }
    using(var stop=new CancellationTokenSource())
    {
        var service=MapCommunityServer.ServeAsync(address,uploads,secret,stop.Token);
        try{Console.WriteLine("UPLOAD after-restart-seventeenth="+(int)await StartUpload(16));}
        finally{stop.Cancel();await service;}
    }
}
finally{Directory.Delete(root,true);}
