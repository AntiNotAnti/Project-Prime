using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

int checks=0;
void Check(bool value,string label) { if(!value)throw new Exception(label);checks++; }
string root=Path.Combine(Path.GetTempPath(),"prime-community-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string secret=Guid.NewGuid().ToString("N");
var portProbe=new TcpListener(IPAddress.Loopback,0);portProbe.Start();int port=((IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
string address=$"http://localhost:{port}/library/";
using var cancellation=new CancellationTokenSource();
Task? server=null;
try
{
    var definition=MapTemplates.Create("COMMUNITY CHECK",true).Definition;
    string package=MapPackageBuilder.Build(definition,Path.Combine(root,"test.ppmap"));
    if(args.Length==2&&args[0]=="--export-package")File.Copy(package,Path.GetFullPath(args[1]),true);
    server=MapCommunityServer.ServeAsync(address,Path.Combine(root,"store"),secret,cancellation.Token);
    using var client=new MapCommunityClient(address,secret);
    Check((await client.BrowseAsync(default)).Length==0,"empty library");
    var published=await client.UploadAsync(package,default);
    Check(published.Hash==MapBuildFingerprint.HashFile(package)&&published.Name==definition.Name,"upload identity");
    var duplicate=await client.UploadAsync(package,default);
    Check(duplicate.Hash==published.Hash&&(await client.BrowseAsync(default)).Length==1,"idempotent content-addressed upload");
    // All non-editor consumers now share the same exact-archive acquisition.
    // A warm cache hit must work without contacting a dead Community server.
    var required = MapContentIdentity.FromPackage(package);
    string acquisitionRoot = Path.Combine(root, "acquisition");
    using (var cold = await CommunityExactArchiveAcquisition.AcquireAsync(
        required, address, CancellationToken.None, cacheRoot: acquisitionRoot))
        Check(cold.Identity.Matches(required)
            && MapBuildFingerprint.HashFile(cold.Path) == published.Hash,
            "cold archive acquisition validates exact bytes before leasing");
    using (var warm = await CommunityExactArchiveAcquisition.AcquireAsync(
        required, "http://127.0.0.1:1/unused/", CancellationToken.None,
        cacheRoot: acquisitionRoot))
        Check(warm.Identity.Matches(required)
            && MapBuildFingerprint.HashFile(warm.Path) == published.Hash,
            "warm archived map is usable without another network request");
    bool wrongMapRefused = false;
    try
    {
        using var wrong = await CommunityExactArchiveAcquisition.AcquireAsync(
            required with { MapId = Guid.NewGuid() }, address, CancellationToken.None,
            cacheRoot: acquisitionRoot);
    }
    catch (InvalidDataException) { wrongMapRefused = true; }
    Check(wrongMapRefused, "cache never substitutes a different MapId for the same package hash");
    // The application facade must be callable by the game or Studio without
    // constructing an Avalonia screen. Verify anonymous browsing, short-lived
    // authenticated client ownership, one-time 401 refresh and cancellation.
    var publicOperations = new CommunityMapOperations(address);
    Check((await publicOperations.BrowseProjectsAsync(false, false, "name", default))
        .Single().MapId == definition.MapId, "UI-neutral Community catalog lists map");
    int issued = 0, renewed = 0;
    var credentials = new DelegateCommunityCredentialSource(
        cancellation => { issued++; return Task.FromResult("expired-local-ticket"); },
        cancellation => { renewed++; return Task.FromResult(secret); });
    var operations = new CommunityMapOperations(address, credentials);
    var mine = await operations.BrowseProjectsAsync(true, false, "name", default);
    Check(mine.Single().MapId == definition.MapId && issued == 1 && renewed == 1,
        "Community facade refreshes a rejected ticket once and preserves catalog");
    var repeated = await operations.UploadPackageAsync(package, default);
    Check(repeated.Hash == published.Hash && issued == 2 && renewed == 2,
        "Community facade retains existing idempotent upload and re-authentication");
    using (var alreadyCancelled = new CancellationTokenSource())
    {
        alreadyCancelled.Cancel();
        try
        {
            await operations.BrowseProjectsAsync(true, false, "name", alreadyCancelled.Token);
            throw new Exception("A cancelled Community request contacted the service.");
        }
        catch (OperationCanceledException) { checks++; }
    }
    Check(issued == 2 && renewed == 2, "cancelled operation did not mint credentials");
    // Pooled handlers must never pool bearer tokens: each short-lived client
    // owns only its own Authorization header, even when sockets are reused.
    for (int i = 0; i < 8; i++)
    {
        using var listed = new MapCommunityClient(address, secret);
        using var anonymous = new MapCommunityClient(address);
        Check((await listed.BrowseProjectsAsync(default, mine: true)).Length == 1,
            "pooled authenticated catalog read");
        bool denied = false;
        try { await anonymous.BrowseProjectsAsync(default, mine: true); }
        catch (HttpRequestException ex) { denied = ex.StatusCode == HttpStatusCode.Unauthorized; }
        Check(denied, "shared handler never forwards a prior client's bearer token");
    }
    using var http=new HttpClient();
    Check((await http.GetStringAsync(address+"health")).Contains("prime-maps"),"service health endpoint");
    byte[] downloaded=await http.GetByteArrayAsync(address+"maps/"+published.Hash);
    Check(downloaded.SequenceEqual(File.ReadAllBytes(package)),"exact download bytes");
    using(var wrong=await http.PostAsync(address+"maps",new ByteArrayContent(downloaded)))
        Check(wrong.StatusCode==HttpStatusCode.Unauthorized,"upload requires credentials");
    http.DefaultRequestHeaders.Authorization=new("Bearer",secret);
    using(var invalid=await http.PostAsync(address+"maps",new ByteArrayContent(new byte[]{1,2,3})))
        Check(invalid.StatusCode==HttpStatusCode.BadRequest,"malformed package rejected");
    using(var missing=await http.GetAsync(address+"maps/"+new string('0',64)))
        Check(missing.StatusCode==HttpStatusCode.NotFound,"unknown hash rejected");
    Check(!Directory.EnumerateFiles(Path.Combine(root,"store"),"*.upload").Any(),"upload staging cleaned");
    try { using var invalid=new MapCommunityClient("http://example.com/");throw new Exception("accepted insecure remote URL"); }
    catch(ArgumentException) {checks++;}
    try { await client.InstallAsync(published with {Hash="../outside"},root,default);throw new Exception("accepted traversal hash"); }
    catch(InvalidDataException) {checks++;}
    try { await client.InstallAsync(published with {Bytes=published.Bytes-1},root,default);throw new Exception("accepted wrong size"); }
    catch(InvalidDataException) {checks++;}
    try { await client.InstallAsync(published with {Name="SPOOF"},root,default);throw new Exception("accepted wrong identity"); }
    catch(InvalidDataException) {checks++;}
    try { await client.InstallAsync(published with {ContentHash=new string('0',64)},root,default);throw new Exception("accepted wrong content hash"); }
    catch(InvalidDataException) {checks++;}
    try { await client.InstallAsync(published with {MapId=Guid.NewGuid()},root,default);throw new Exception("accepted wrong map ID"); }
    catch(InvalidDataException) {checks++;}
    using(var source=new MemoryStream(new byte[8]))using(var target=new MemoryStream())
    {
        try { await MapCommunityClient.CopyBoundedAsync(source,target,7,default);throw new Exception("accepted oversized body"); }
        catch(InvalidDataException) {checks++;}
    }
    string oldMaps=CustomRooms.MapDirectory,oldUser=CustomRooms.UserMapDirectory;
    try
    {
        CustomRooms.MapDirectory=Path.Combine(root,"missing-resources");CustomRooms.UserMapDirectory=Path.Combine(root,"installed");
        Directory.CreateDirectory(CustomRooms.UserMapDirectory);File.Copy(package,Path.Combine(CustomRooms.UserMapDirectory,"test.ppmap"));
        Check(new MapCatalog(CustomRooms.MapDirectory).Refresh().Any(m=>m.Definition?.Name==definition.Name),"writable installed library found without bundled maps");
        Directory.CreateDirectory(CustomRooms.MapDirectory);File.Copy(package,Path.Combine(CustomRooms.MapDirectory,"test.ppmap"));
        var catalog=new MapCatalog(CustomRooms.MapDirectory).Refresh();
        Check(catalog.Count==1&&catalog[0].Validation.IsValid,"installed package wins same-identity bundled duplicate");
        definition.Save(Path.Combine(CustomRooms.UserMapDirectory,"000-authoring.json"));
        Check(MapBundle.Is(new MapCatalog(CustomRooms.MapDirectory).Refresh()[0].Path),"runtime catalog prefers installed package over authoring JSON");
        Check(!MapBundle.Is(new MapCatalog(CustomRooms.MapDirectory).Refresh(false)[0].Path),"editor catalog prefers authoring JSON");
    }
    finally {CustomRooms.MapDirectory=oldMaps;CustomRooms.UserMapDirectory=oldUser;}
    cancellation.Cancel();await server;
    using var restart=new CancellationTokenSource();
    server=MapCommunityServer.ServeAsync(address,Path.Combine(root,"store"),secret,restart.Token);
    try {Check((await client.BrowseAsync(default)).Single().Hash==published.Hash,"published maps survive service restart");}
    finally {restart.Cancel();await server;}
    Console.WriteLine($"Map community: {checks} checks passed.");
}
finally
{
    cancellation.Cancel();if(server!=null)await server;
    Directory.Delete(root,true);
}
