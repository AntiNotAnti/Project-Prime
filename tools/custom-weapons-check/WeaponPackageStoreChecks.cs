using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.CustomWeapons.Packages;

// CPU-only real store checks. Children use the same executable and OS handles;
// a task/thread imitation cannot establish the cross-process lease guarantee.
internal static class WeaponPackageStoreChecks
{
    public static int Child(string[] args)
    {
        try
        {
            if(args[1]=="install"){Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();new WeaponPackageStore(args[2]).Install(File.ReadAllBytes(args[3]));return 0;}
            if(args[1]=="gate"){using var gate=new FileStream(Path.Combine(args[2],".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();return 0;}
            if(args[1]=="lease")
            {
                using var lease=new WeaponPackageStore(args[2]).Acquire(args[3]);
                Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();
                return lease.Package.PackageHash==args[3]?0:2;
            }
            throw new ArgumentException("Unknown store child mode.");
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    public static int Run(string? output=null)
    {
        int checks=0;var failures=new List<string>();var watch=Stopwatch.StartNew();
        void Check(bool ok,string label){checks++;if(!ok){failures.Add(label);Console.Error.WriteLine("FAIL "+label);}}
        void Reject(Action action,string label){try{action();Check(false,label);}catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException or TimeoutException){Check(true,label);}}
        string sandbox=Path.Combine(Path.GetTempPath(),"prime-store-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sandbox);
        string Root(string name)=>Path.Combine(sandbox,name);
        try
        {
            byte[] archive=WeaponPackageReference.BuildRiftlineM7();var package=WeaponPackage.Parse(archive);
            var resources=package.ResourcePaths.Where(x=>x!="weapon.json").ToDictionary(x=>x,x=>package.CopyResource(x),StringComparer.Ordinal);
            byte[] Revision(string name){var node=JsonNode.Parse(package.Project.ToCanonicalBytes())!;node["name"]=name;return WeaponPackageBuilder.Build(WeaponProject.Parse(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())),resources);}
            byte[] second=Revision("Riftline alternate revision");var p2=WeaponPackage.Parse(second);
            var invalid=new WeaponPackageStore(Root("invalid"));
            Check(!Directory.Exists(Root("invalid")),"constructor does not create store");
            Reject(()=>invalid.Install(new byte[]{1,2,3}),"malformed archive rejected");
            Check(!Directory.Exists(Root("invalid")),"invalid intake creates no root");
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Reject(()=>invalid.Install(archive,cancelled.Token),"pre-cancelled install rejected");}
            Check(!Directory.Exists(Root("invalid")),"cancelled intake creates no root");
            foreach(var options in new[]{new WeaponPackageStoreOptions{MaximumArchives=0},new WeaponPackageStoreOptions{MaximumInstalled=0},new WeaponPackageStoreOptions{MaximumTotalArchiveBytes=0},new WeaponPackageStoreOptions{LockTimeout=TimeSpan.Zero}})
                Reject(()=>new WeaponPackageStore(Root("bad-options"),options),"invalid store options rejected before state creation");
            Check(!Directory.Exists(Root("bad-options")),"bad options create no disk state");
            var store=new WeaponPackageStore(Root("basic"));
            Check(store.List().Count==0&&!Directory.Exists(Root("basic")),"listing missing store is empty and read only");
            var installed=store.Install(archive);Check(installed.PackageHash==package.PackageHash,"install returns exact validated archive");
            var rows=store.List();Check(rows.Count==1&&rows[0].PackageHash==package.PackageHash&&rows[0].ContentHash==package.ContentHash&&rows[0].DependencyHash==package.DependencyHash&&rows[0].ContentId==package.Project.ContentId&&rows[0].Name==package.Project.Name,"listing carries all exact pinned identities and metadata");
            store.Install(archive);Check(store.List().Count==1,"identical install idempotent");
            store.Install(second);Check(store.List().Count==2&&package.Project.ContentId==p2.Project.ContentId,"same lineage revisions coexist immutably");
            using(var lease=store.Acquire(package.PackageHash))Check(lease.Package.CopyArchive().AsSpan().SequenceEqual(archive),"acquire resolves exact older archive without upgrading");
            Reject(()=>store.Acquire(new string('f',64)),"missing revision cannot fallback");
            foreach(string bad in new[]{"", "../escape",new string('A',64),new string('a',63)}){Reject(()=>store.Acquire(bad),"invalid acquire hash "+bad);Reject(()=>store.Uninstall(bad),"invalid uninstall hash "+bad);}
            Check(!store.Uninstall(new string('f',64)),"uninstall absent revision false");
            using(var lease=store.Acquire(package.PackageHash))
            {
                Check(store.Uninstall(package.PackageHash),"uninstall removes marker while lease lives");
                Check(store.List().Count==1,"uninstalled lease no longer listed");
                Check(store.Prune()==0&&lease.Package.CopyArchive().AsSpan().SequenceEqual(archive),"prune preserves active in-process archive lease");
            }
            Check(store.Prune()==1&&store.Prune()==0,"released orphan pruned exactly once");
            store.Install(archive);
            using(var child=Start("lease",Root("basic"),package.PackageHash))
            {
                Ready(child);Check(store.Uninstall(package.PackageHash),"uninstall while real child holds lease");
                Check(store.Prune()==0,"prune skips actual cross-process lease");
                child.StandardInput.WriteLine();child.StandardInput.Flush();Finish(child);Check(store.Prune()==1,"child disposal releases cross-process lease");
            }
            store.Install(archive);
            using(var child=Start("lease",Root("basic"),package.PackageHash))
            {
                Ready(child);store.Uninstall(package.PackageHash);child.Kill(true);child.WaitForExit();Check(store.Prune()==1,"OS releases lease after child process failure");
            }
            store.Install(archive);
            using(var first=Start("lease",Root("basic"),package.PackageHash))
            using(var last=Start("lease",Root("basic"),package.PackageHash))
            {
                Ready(first);Ready(last);store.Uninstall(package.PackageHash);Check(store.Prune()==0,"two simultaneous real processes share archive lease");
                first.StandardInput.WriteLine();first.StandardInput.Flush();Finish(first);Check(store.Prune()==0,"one released child cannot invalidate remaining shared lease");
                last.StandardInput.WriteLine();last.StandardInput.Flush();Finish(last);Check(store.Prune()==1,"archive removable only after both shared child leases release");
            }
            (int Code,string Text) Cli(params string[] arguments)
            {
                var stdout=Console.Out;var stderr=Console.Error;using var capture=new StringWriter();
                try{Console.SetOut(capture);Console.SetError(capture);int code=WeaponPackageCli.Run(arguments);string text=capture.ToString();if(text.Length>8192)throw new InvalidOperationException("CLI output exceeded fixture bound.");return(code,text);}
                finally{Console.SetOut(stdout);Console.SetError(stderr);}
            }
            string project=Root("cli-project"), builtArchive=Root("cli.ppweapon"),cliStore=Root("cli-store");
            Check(Cli("export",project).Code==0,"CLI export creates editable project");
            Check(Cli("build",project,builtArchive).Code==0&&File.ReadAllBytes(builtArchive).AsSpan().SequenceEqual(archive),"CLI ordinary export/build reproduces exact reference bytes");
            Check(Cli("validate",builtArchive).Text.Contains(package.PackageHash,StringComparison.Ordinal),"CLI validate reports archive identity");
            Check(Cli("build",project,builtArchive).Code!=0&&File.ReadAllBytes(builtArchive).AsSpan().SequenceEqual(archive),"CLI build refuses output overwrite");
            Check(Cli("export",project).Code!=0,"CLI export refuses existing project");
            Check(Cli("install",builtArchive,cliStore).Code==0&&Cli("list",cliStore).Text.Contains(package.PackageHash,StringComparison.Ordinal),"CLI install/list uses exact pinned archive");
            Check(Cli("uninstall",package.PackageHash,cliStore).Code==0&&Cli("list",cliStore).Text.Length==0,"CLI uninstall/list removes installed marker");
            Check(Cli("prune",cliStore).Code==0&&new WeaponPackageStore(cliStore).Prune()==0,"CLI prune deletes orphan archive");
            Check(Cli("uninstall",package.PackageHash,cliStore).Code!=0,"CLI missing uninstall reports failure");
            Check(Cli("validate",Root("missing.ppweapon")).Code!=0,"CLI missing input fails");
            File.Delete(Path.Combine(project,"audio","shot.wav"));string missingOutput=Root("missing-output.ppweapon");
            Check(Cli("build",project,missingOutput).Code!=0&&!File.Exists(missingOutput),"CLI missing resource creates no output");
            File.WriteAllBytes(Path.Combine(project,"audio","shot.wav"),package.CopyResource("audio/shot.wav"));
            File.WriteAllText(Path.Combine(project,"weapon.json"),"{\"schema\":999,\"mechanics\":\"../escape\"}");
            Check(Cli("build",project,missingOutput).Code!=0&&!File.Exists(missingOutput),"CLI hostile project fails without output");
            foreach(string path in new[]{Path.Combine(sandbox,"nul"),Path.Combine(sandbox,"con.txt"),Path.Combine(sandbox,"file:stream"),Path.Combine(sandbox,"trail."),Path.Combine(sandbox,"line\nfeed")})
            {
                Reject(()=>new WeaponPackageStore(path).Install(archive),"public store refuses device/ADS/control/trailing alias path");
                Check(Cli("export",path).Code!=0,"CLI refuses unsafe disk path");
            }
            var installCap=new WeaponPackageStore(Root("installed-cap"),new WeaponPackageStoreOptions{MaximumInstalled=1});installCap.Install(archive);
            Reject(()=>installCap.Install(second),"installed count quota blocks second marker");Check(installCap.List().Count==1,"quota failure leaves committed revision intact");
            var archiveCap=new WeaponPackageStore(Root("archive-cap"),new WeaponPackageStoreOptions{MaximumArchives=1});archiveCap.Install(archive);archiveCap.Uninstall(package.PackageHash);
            Reject(()=>archiveCap.Install(second),"archive quota counts uninstalled cached revisions");Check(archiveCap.Prune()==1,"explicit prune recovers archive quota");archiveCap.Install(second);Check(archiveCap.List().Single().PackageHash==p2.PackageHash,"install succeeds after prune quota recovery");
            string loweredRoot=Root("lowered-quota");var originalQuota=new WeaponPackageStore(loweredRoot);originalQuota.Install(archive);originalQuota.Install(second);
            var loweredInstalled=new WeaponPackageStore(loweredRoot,new WeaponPackageStoreOptions{MaximumInstalled=1});
            Check(loweredInstalled.List().Count==2,"lowering installed admission quota does not hide existing revisions");
            Check(loweredInstalled.Uninstall(package.PackageHash)&&loweredInstalled.Uninstall(p2.PackageHash),"lowered installed quota permits recovery by uninstall");
            var loweredArchives=new WeaponPackageStore(loweredRoot,new WeaponPackageStoreOptions{MaximumArchives=1});
            Check(loweredArchives.Prune()==2,"lowered archive admission quota permits pruning both retained orphans");
            var bytesCap=new WeaponPackageStore(Root("bytes-cap"),new WeaponPackageStoreOptions{MaximumTotalArchiveBytes=archive.Length});bytesCap.Install(archive);bytesCap.Install(archive);
            Reject(()=>bytesCap.Install(second),"total byte quota counts actual immutable archives");Check(bytesCap.List().Count==1,"idempotent install consumes no extra byte quota");
            foreach(var checkpoint in new[]{WeaponPackageStoreCheckpoint.ArchiveStaged,WeaponPackageStoreCheckpoint.ArchivePublished,WeaponPackageStoreCheckpoint.MarkerStaged,WeaponPackageStoreCheckpoint.BeforeMarkerPromotion})
            {
                string path=Root("fault-"+checkpoint);
                var faultStore=new WeaponPackageStore(path,new WeaponPackageStoreOptions{Checkpoint=point=>{if(point==checkpoint)throw new IOException("injected "+point);}});
                Reject(()=>faultStore.Install(archive),"injected fault at "+checkpoint);
                var recovered=new WeaponPackageStore(path);Check(recovered.List().Count==0,"fault never publishes partial marker at "+checkpoint);
                recovered.Prune();recovered.Install(archive);Check(recovered.List().Single().PackageHash==package.PackageHash,"recovery after "+checkpoint);
            }
            using(var cancellation=new CancellationTokenSource())
            {
                var commitStore=new WeaponPackageStore(Root("commit-cancel"),new WeaponPackageStoreOptions{Checkpoint=point=>{if(point==WeaponPackageStoreCheckpoint.MarkerPublished){cancellation.Cancel();throw new IOException("postcommit callback");}}});
                Check(commitStore.Install(archive,cancellation.Token).PackageHash==package.PackageHash&&commitStore.List().Count==1,"cancellation and callback fault after marker commit cannot roll back or report failure");
            }
            using(var cancellation=new CancellationTokenSource())
            {
                string path=Root("precommit-cancel");var cancelStore=new WeaponPackageStore(path,new WeaponPackageStoreOptions{Checkpoint=point=>{if(point==WeaponPackageStoreCheckpoint.BeforeMarkerPromotion)cancellation.Cancel();}});
                Reject(()=>cancelStore.Install(archive,cancellation.Token),"cancellation before marker promotion aborts install");Check(new WeaponPackageStore(path).List().Count==0,"precommit cancellation leaves no installed marker");
            }
            Check(File.Exists(Path.Combine(Root("basic"),".store.lock")),"global gate remains persistent after operations");
            using(var child=Start("gate",Root("basic")))
            {
                Ready(child);var bounded=new WeaponPackageStore(Root("basic"),new WeaponPackageStoreOptions{LockTimeout=TimeSpan.FromMilliseconds(150)});
                Reject(()=>bounded.List(),"real other process gate causes bounded lock failure");child.Kill(true);child.WaitForExit();Check(bounded.List().Count==1,"process failure releases persistent global gate");
            }
            string corrupt=Root("corrupt");var corruptStore=new WeaponPackageStore(corrupt);corruptStore.Install(archive);
            File.WriteAllText(Path.Combine(corrupt,"installed",package.PackageHash+".installed.json"),"{}");
            Reject(()=>corruptStore.List(),"corrupt marker fails closed");
            string corruptArchive=Root("corrupt-archive");var corruptArchiveStore=new WeaponPackageStore(corruptArchive);corruptArchiveStore.Install(archive);
            File.WriteAllBytes(Path.Combine(corruptArchive,"archives",package.PackageHash+".ppweapon"),second);
            Reject(()=>corruptArchiveStore.Acquire(package.PackageHash),"archive bytes must match exact filename digest");Reject(()=>corruptArchiveStore.List(),"listing validates referenced archive digest");
            string stages=Root("stages");var stageStore=new WeaponPackageStore(stages);stageStore.Install(archive);
            string recognizedStage=Path.Combine(stages,"archives",".cw11b-"+new string('a',32)+".tmp");string unknownStage=Path.Combine(stages,"archives","operator-notes.tmp");
            File.WriteAllText(recognizedStage,"partial");File.WriteAllText(unknownStage,"keep");
            Check(stageStore.Prune()==0&&!File.Exists(recognizedStage)&&File.Exists(unknownStage),"prune scavenges only recognized stages without counting them as archives");
            string outside=Root("outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"sentinel"),"untouched");
            string linked=Root("linked");
            bool linkCreated=false;
            linkCreated=TryDirectoryLink(linked,outside);
            if(linkCreated)
            {
                Reject(()=>new WeaponPackageStore(linked).Install(archive),"store refuses root symbolic link");Check(File.ReadAllText(Path.Combine(outside,"sentinel"))=="untouched"&&Directory.GetFileSystemEntries(outside).Length==1,"refused link never changes external target");Directory.Delete(linked);
            }
            foreach(string component in new[]{"archives","installed"})
            {
                string nested=Root("linked-"+component);Directory.CreateDirectory(nested);string link=Path.Combine(nested,component);
                if(TryDirectoryLink(link,outside))
                {
                    Reject(()=>new WeaponPackageStore(nested).Install(archive),"store refuses linked "+component+" directory");Check(Directory.GetFileSystemEntries(outside).Length==1,"linked "+component+" target remains unchanged");Directory.Delete(link);
                }
            }
            string gateLinkRoot=Root("linked-gate");Directory.CreateDirectory(gateLinkRoot);string gateTarget=Path.Combine(outside,"sentinel");string gateLink=Path.Combine(gateLinkRoot,".store.lock");bool gateLinked=false;
            try{File.CreateSymbolicLink(gateLink,gateTarget);gateLinked=true;}catch(Exception e) when(e is UnauthorizedAccessException or PlatformNotSupportedException or IOException){Console.WriteLine("SKIP file symbolic-link fixture unavailable.");}
            if(gateLinked){Reject(()=>new WeaponPackageStore(gateLinkRoot).Install(archive),"store refuses linked persistent gate");Check(File.ReadAllText(gateTarget)=="untouched","linked gate target remains intact");File.Delete(gateLink);}
            string childArchive=Root("child-input.ppweapon");File.WriteAllBytes(childArchive,second);
            using(var first=Start("install",Root("child-install"),builtArchive))
            using(var last=Start("install",Root("child-install"),childArchive))
            {
                Ready(first);Ready(last);first.StandardInput.WriteLine();first.StandardInput.Flush();last.StandardInput.WriteLine();last.StandardInput.Flush();Finish(first);Finish(last);
                Check(new WeaponPackageStore(Root("child-install")).List().Select(x=>x.PackageHash).Order(StringComparer.Ordinal).SequenceEqual(new[]{package.PackageHash,p2.PackageHash}.Order(StringComparer.Ordinal)),"actual concurrent processes preserve two distinct committed revisions");
            }
            Parallel.For(0,12,i=>new WeaponPackageStore(Root("parallel")).Install(i%2==0?archive:second));
            Check(new WeaponPackageStore(Root("parallel")).List().Count==2,"parallel independent store objects preserve both atomic installs");
        }
        catch(Exception e){Check(false,"unexpected suite exception: "+e);}
        finally{try{Directory.Delete(sandbox,true);}catch(Exception e){Check(false,"sandbox cleanup: "+e.Message);}}
        var result=new{checks,failures,seconds=watch.Elapsed.TotalSeconds,scope="Real CPU store APIs, immutable revisions, bounded quotas, concurrent installs and actual cross-process lease/disposal/crash; no gameplay activation."};
        if(output!=null){Directory.CreateDirectory(Path.GetFullPath(output));File.WriteAllText(Path.Combine(Path.GetFullPath(output),"weapon-package-store-results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"{(failures.Count==0?"PASS":"FAIL")} {checks} weapon package store checks; failures={failures.Count}");return failures.Count==0?0:1;
    }
    private static bool TryDirectoryLink(string link,string target)
    {
        try{Directory.CreateSymbolicLink(link,target);return true;}catch(Exception e) when(e is UnauthorizedAccessException or PlatformNotSupportedException or IOException){ }
        if(OperatingSystem.IsWindows())
        {
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("New-Item -ItemType Junction -Path '"+link.Replace("'","''")+"' -Target '"+target.Replace("'","''")+"' -ErrorAction Stop | Out-Null");
            using var process=Process.Start(start)!;if(!process.WaitForExit(20000)){process.Kill(true);throw new TimeoutException("Junction creation timeout.");}if(process.ExitCode==0)return true;
        }
        Console.WriteLine("SKIP directory symbolic-link/junction fixture unavailable.");return false;
    }
    private static Process Start(params string[] arguments)
    {
        string host=Environment.ProcessPath??throw new InvalidOperationException("No process host.");
        var start=new ProcessStartInfo(host){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(host).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--weapon-package-store-child");foreach(string arg in arguments)start.ArgumentList.Add(arg);
        return Process.Start(start)??throw new InvalidOperationException("Child did not start.");
    }
    private static void Ready(Process child)
    {
        var line=child.StandardOutput.ReadLineAsync();if(!line.Wait(TimeSpan.FromSeconds(20))||line.Result!="READY"){if(!child.HasExited)child.Kill(true);throw new InvalidOperationException("Lease child failed: "+child.StandardError.ReadToEnd());}
    }
    private static void Finish(Process child){if(!child.WaitForExit(20000)){child.Kill(true);throw new TimeoutException("Lease child did not stop.");}if(child.ExitCode!=0)throw new InvalidOperationException("Lease child: "+child.StandardError.ReadToEnd());}
}
