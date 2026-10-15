using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Update;

internal static class LegacyProcessChecks
{
    internal static int? RunChild(string[] args)
    {
        if(args.Length!=2||args[0]!="--legacy-apphost-ready")return null;
        // Deliberately no shared use lease: this models an older installation.
        File.WriteAllText(args[1],"ready");Thread.Sleep(30000);return 0;
    }

    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-legacy-apphosts-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string assembly=Assembly.GetExecutingAssembly().Location;
        string apphost=Path.Combine(AppContext.BaseDirectory,Path.GetFileNameWithoutExtension(assembly)+(OperatingSystem.IsWindows()?".exe":""));
        if(!File.Exists(apphost))throw new IOException("Build the updater test apphost before running legacy process checks.");
        string flatGame=OperatingSystem.IsWindows()?"ProjectPrime.exe":"ProjectPrime";
        string flatStudio=OperatingSystem.IsWindows()?"ProjectPrimeStudio.exe":"ProjectPrimeStudio";
        string bundleGame="Project Prime.app/Contents/MacOS/ProjectPrime";
        string bundleStudio="Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio";

        void WithLegacy(string executable,Action<Process> action)
        {
            string directory=Path.GetDirectoryName(executable)!;Directory.CreateDirectory(directory);
            File.Copy(apphost,executable);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(executable,File.GetUnixFileMode(apphost));
            foreach(string path in new[]{assembly,Path.ChangeExtension(assembly,".deps.json"),Path.ChangeExtension(assembly,".runtimeconfig.json")})
                File.Copy(path,Path.Combine(directory,Path.GetFileName(path)));
            string ready=Path.Combine(root,Guid.NewGuid().ToString("N")+".ready");
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            start.ArgumentList.Add("--legacy-apphost-ready");start.ArgumentList.Add(ready);
            using Process child=Process.Start(start)??throw new IOException("Legacy apphost fixture did not start.");
            try
            {
                var deadline=Stopwatch.StartNew();
                while(!File.Exists(ready)&&!child.HasExited&&deadline.Elapsed<TimeSpan.FromSeconds(10))Thread.Sleep(20);
                if(!File.Exists(ready))throw new IOException("Legacy apphost fixture did not become ready: "+(child.HasExited?child.StandardError.ReadToEnd():"startup timeout"));
                action(child);
            }
            finally{if(!child.HasExited)child.Kill(entireProcessTree:true);child.WaitForExit();}
        }
        string Incoming(string name,bool bundles)
        {
            string source=Path.Combine(root,name+"-source");Directory.CreateDirectory(source);
            string game=bundles?bundleGame:flatGame,studio=bundles?bundleStudio:flatStudio;
            foreach(string relative in new[]{game,studio})
            {string path=Path.Combine(source,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"new "+Path.GetFileName(relative));}
            File.WriteAllText(Path.Combine(source,"a.dll"),"new A");
            File.WriteAllBytes(Path.Combine(source,ReleaseInstallation.PairName),JsonSerializer.SerializeToUtf8Bytes(new ReleaseInstallation.DesktopPair(
                2,"0.1.53","0.1.53",1,game,studio,ReleaseInstallation.Hash(Path.Combine(source,game)),ReleaseInstallation.Hash(Path.Combine(source,studio)))));
            ReleaseInstallation.EnsureManifest(source);return source;
        }
        string Target(string name)
        {
            string target=Path.Combine(root,name+"-target");Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target,"a.dll"),"old A");ReleaseInstallation.EnsureManifest(target);
            File.WriteAllText(Path.Combine(target,"settings.json"),"player settings");return target;
        }
        void BlocksApply(string name,string relative,bool bundles,bool alias,bool caseAlias=false)
        {
            string target=Target(name),source=Incoming(name,bundles),requested=target;
            if(alias){requested=target+"-alias";Directory.CreateSymbolicLink(requested,target);}
            if(caseAlias)
            {
                requested=target.ToUpperInvariant();
                if(!Directory.Exists(requested))
                {Console.WriteLine("SKIP legacy apphost case-alias assertions: this macOS volume is case-sensitive.");return;}
            }
            byte[] manifest=File.ReadAllBytes(Path.Combine(target,ReleaseInstallation.ManifestName));
            WithLegacy(Path.Combine(target,relative),child=>
            {
                bool rejected=false;
                try{ReleaseInstallation.Apply(source,requested);}
                catch(IOException error){rejected=error.Message.Contains("Close Game and Studio",StringComparison.Ordinal);}
                check(rejected&&!child.HasExited&&File.ReadAllText(Path.Combine(target,"a.dll"))=="old A"
                    &&Convert.ToHexString(File.ReadAllBytes(Path.Combine(target,ReleaseInstallation.ManifestName)))==Convert.ToHexString(manifest)
                    &&!Directory.Exists(Path.Combine(target,".project-prime-update-transaction")),
                    "live pre-lease "+name+" blocks update before any installed bytes change");
            });
            ReleaseInstallation.Apply(source,requested);
            check(File.ReadAllText(Path.Combine(target,"a.dll"))=="new A"&&File.ReadAllText(Path.Combine(target,"settings.json"))=="player settings",
                "update proceeds after pre-lease "+name+" exits and preserves user settings");
            if(alias)Directory.Delete(requested);
        }
        try
        {
            BlocksApply("flat-game",flatGame,false,false);
            BlocksApply("flat-studio",flatStudio,false,false);
            if(!OperatingSystem.IsWindows())
            {
                BlocksApply("bundled-game",bundleGame,true,false);
                BlocksApply("bundled-studio-alias",bundleStudio,true,true);
                if(OperatingSystem.IsMacOS())BlocksApply("bundled-studio-case-alias",bundleStudio,true,false,true);
                string target=Target("recovery");
                string transaction=Path.Combine(target,".project-prime-update-transaction");
                Directory.CreateDirectory(Path.Combine(transaction,"backup"));
                File.Move(Path.Combine(target,"a.dll"),Path.Combine(transaction,"backup","a.dll"));
                byte[] journal=JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Operations=new[]{new{Path="a.dll",HadOriginal=true,Install=true}},OriginalDirectories=Array.Empty<string>(),CreatedDirectories=Array.Empty<string>()});
                File.WriteAllBytes(Path.Combine(transaction,"journal.json"),journal);
                WithLegacy(Path.Combine(target,bundleStudio),child=>
                {
                    bool rejected=false;try{ReleaseInstallation.Recover(target);}catch(IOException error){rejected=error.Message.Contains("Close Game and Studio",StringComparison.Ordinal);}
                    check(rejected&&!child.HasExited&&!File.Exists(Path.Combine(target,"a.dll"))
                        &&File.ReadAllText(Path.Combine(transaction,"backup","a.dll"))=="old A"
                        &&Convert.ToHexString(File.ReadAllBytes(Path.Combine(transaction,"journal.json")))==Convert.ToHexString(journal),
                        "live pre-lease bundled Studio blocks interrupted-update recovery without consuming its journal");
                });
                ReleaseInstallation.Recover(target);
                check(File.ReadAllText(Path.Combine(target,"a.dll"))=="old A"&&!Directory.Exists(transaction),
                    "interrupted update recovers after pre-lease bundled Studio exits");
                string otherTarget=Target("other-app"),otherSource=Incoming("other-app",false);
                WithLegacy(Path.Combine(otherTarget,"Unrelated.app/Contents/MacOS/ProjectPrimeStudio"),child=>
                {
                    ReleaseInstallation.Apply(otherSource,otherTarget);
                    check(!child.HasExited&&File.ReadAllText(Path.Combine(otherTarget,"a.dll"))=="new A",
                        "same-named apphost inside an unrelated app bundle is neither blocked nor terminated");
                });
            }
            string independent=Target("independent"),incoming=Incoming("independent",false);
            WithLegacy(Path.Combine(independent+"-unrelated",flatStudio),child=>
            {
                ReleaseInstallation.Apply(incoming,independent);
                check(!child.HasExited&&File.ReadAllText(Path.Combine(independent,"a.dll"))=="new A",
                    "same-named apphost in a prefix sibling installation neither blocks update nor gets terminated");
            });
        }
        finally{Directory.Delete(root,recursive:true);}
    }
}
