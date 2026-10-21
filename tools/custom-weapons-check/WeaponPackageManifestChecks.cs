using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

// Independent CWPK/CWM1 byte writers below intentionally do not call production
// row writers or digest helpers. Binding tests retain actual OS store leases.
internal static class WeaponPackageManifestChecks
{
    const string CwmHex="43574d310100010001000a009a776851f39f4a768c13c5d88875ef613ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228";
    const string CwmSha="a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9";
    const string GoldenSha="e58d66d03426c22db5b414ecddb11d33f4bdb8b4b19a8800563d372ba3cda7a4";
    const string GoldenHex="4357504b01000100"+CwmSha+"0a009a776851f39f4a768c13c5d88875ef613ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228"+
        "5789962e4906689774acc14ab26118fe68df20b33b6ff9784e109cf375bf3631"+"5a70fce558e7cdd0eebf28293d5258da0baaddc8b582cf73e9a8419340f6e24e"+"10ea078c19901f782285c3185985b5958232eb28b843042c31a23ff1c03e15a1";
    internal static int Run(string? output=null)
    {
        int checks=0;var failures=new List<string>();var watch=Stopwatch.StartNew();
        void Check(bool ok,string label){checks++;if(!ok){failures.Add(label);Console.Error.WriteLine("MANIFEST FAIL "+label);}}
        void Reject(Action action,string label){try{action();Check(false,label);}catch(Exception e)when(e is ArgumentException or IndexOutOfRangeException or InvalidDataException or IOException or InvalidOperationException or OperationCanceledException){Check(true,label);}}
        void Bad(byte[] bytes,string label){bool parsed=CustomWeaponPackageManifest.TryRead(bytes,out var value);Check(!parsed&&value==null,label);}
        string sandbox=Path.Combine(Path.GetTempPath(),"prime-cwpk-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sandbox);
        string native=JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P});
        try
        {
            var package=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
            var resources=package.ResourcePaths.Where(x=>x!="weapon.json").ToDictionary(x=>x,x=>package.CopyResource(x),StringComparer.Ordinal);
            WeaponPackage Variant(int index,bool metadataOnly=false)
            {
                var project=JsonNode.Parse(package.Project.ToCanonicalBytes())!;project["name"]="Manifest variant "+index;
                if(!metadataOnly)project["contentId"]=Guid.Parse(index.ToString("x8")+"-1122-3344-5566-778899aabbcc").ToString("D");
                return WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));
            }
            var manifest=new CustomWeaponPackageManifest(new[]{package});byte[] bytes=manifest.CopyBytes();
            Check(CustomWeaponPackageManifest.HeaderSize==40&&CustomWeaponPackageManifest.RowSize==146&&CustomWeaponPackageManifest.MaximumEntries==64&&CustomWeaponPackageManifest.MaximumBytes==9384&&CustomWeaponPackageManifest.Version==1,"frozen CWPK bounds and version");
            Check(bytes.AsSpan().SequenceEqual(Convert.FromHexString(GoldenHex))&&Sha(bytes)==GoldenSha&&manifest.Digest.ToString()==GoldenSha,"independently pinned full CWPK reference bytes and SHA256");
            Check(Encode(new[]{package}).AsSpan().SequenceEqual(bytes),"independent writer reproduces complete CWPK bytes");
            Check(Cwm(new[]{package}).AsSpan().SequenceEqual(Convert.FromHexString(CwmHex))&&Sha(Cwm(new[]{package}))==CwmSha&&new CustomWeaponManifest(new[]{package.Definition}).Digest.ToString()==CwmSha&&manifest.MechanicsDigest.ToString()==CwmSha,"full original CWM1 bytes and digest remain unchanged");
            var row=manifest.Rows.Single();Check(row.Weapon.RuntimeId.Value==10&&row.Weapon.ContentId==package.Project.ContentId&&row.Weapon.Mechanics.ToString()==package.Definition.Mechanics.DefinitionHash&&row.Archive.ToString()==package.PackageHash&&row.Content.ToString()==package.ContentHash&&row.Dependencies.ToString()==package.DependencyHash,"one row carries all four exact identities");
            Check(CustomWeaponPackageManifest.TryRead(bytes,out var decoded)&&decoded!.CopyBytes().AsSpan().SequenceEqual(bytes)&&decoded.Digest==manifest.Digest,"strict parser canonical roundtrip");
            var metadata=Variant(99,true);var metadataManifest=new CustomWeaponPackageManifest(new[]{metadata});
            Check(metadata.PackageHash!=package.PackageHash&&metadata.ContentHash!=package.ContentHash&&metadataManifest.Digest!=manifest.Digest&&metadataManifest.MechanicsDigest==manifest.MechanicsDigest,"metadata archive revision changes CWPK while preserving CWM1");
            var input=new[]{package};var snapshot=new CustomWeaponPackageManifest(input);input[0]=metadata;byte[] detached=snapshot.CopyBytes();detached[0]=0;
            Check(snapshot.CopyBytes().AsSpan().SequenceEqual(bytes),"constructor enumeration snapshot and detached byte copies are immutable");
            if(snapshot.Rows is IList<CustomPackageManifestRow> list){try{list[0]=default;}catch(NotSupportedException){}}
            Check(snapshot.Rows.Single()==row,"public row view cannot mutate manifest");
            Reject(()=>new CustomWeaponPackageManifest(Array.Empty<WeaponPackage>()),"empty constructor rejected");Reject(()=>new CustomWeaponPackageManifest(null!),"null enumeration rejected");Reject(()=>new CustomWeaponPackageManifest(new WeaponPackage[]{null!}),"null package rejected");
            Reject(()=>new CustomWeaponPackageManifest(new[]{package,metadata}),"duplicate content lineage revisions rejected");Reject(()=>new CustomWeaponPackageManifest(new[]{package,package}),"duplicate exact package rejected");
            var all=Enumerable.Range(1,65).Select(i=>Variant(i)).ToArray();var full=new CustomWeaponPackageManifest(all.Take(64));
            Check(full.Rows.Count==64&&full.CopyBytes().Length==9384&&full.Rows[63].Weapon.RuntimeId.Value==73,"exact64 rows and9384 bytes accepted");
            Check(full.CopyBytes().AsSpan().SequenceEqual(Encode(all.Take(64)))&&new CustomWeaponPackageManifest(all.Take(64).Reverse()).Digest==full.Digest,"64 rows independent writer and enumeration order canonical parity");
            Reject(()=>new CustomWeaponPackageManifest(all),"65 rows rejected");Check(CustomWeaponPackageManifest.TryRead(full.CopyBytes(),out _),"maximum-size parser accepted");
            for(int length=0;length<bytes.Length;length++)Bad(bytes[..length],"truncated reference "+length);
            Bad(bytes.Concat(new byte[]{0}).ToArray(),"trailing byte rejected");Bad(new byte[9385],"over-budget bytes rejected");
            foreach(int offset in new[]{0,1,2,3,4,5,6,7,8,39}){var changed=bytes.ToArray();changed[offset]^=0x80;Bad(changed,"header mutation "+offset);}
            foreach((int offset,int count) in new[]{(40,2),(42,16),(58,32),(90,32),(122,32),(154,32)})
            {var changed=bytes.ToArray();changed.AsSpan(offset,count).Clear();Bad(changed,"zero required row identity "+offset);}
            var two=new CustomWeaponPackageManifest(all.Take(2)).CopyBytes();
            var swapped=two.ToArray();two.AsSpan(40,146).CopyTo(swapped.AsSpan(186));two.AsSpan(186,146).CopyTo(swapped.AsSpan(40));BinaryPrimitives.WriteUInt16LittleEndian(swapped.AsSpan(40),10);BinaryPrimitives.WriteUInt16LittleEndian(swapped.AsSpan(186),11);RehashCwm(swapped);Bad(swapped,"reordered complete rows rejected despite recalculated CWM1");
            var duplicate=two.ToArray();duplicate.AsSpan(42,16).CopyTo(duplicate.AsSpan(188,16));RehashCwm(duplicate);Bad(duplicate,"duplicate contentId rejected despite recalculated CWM1");
            var hole=two.ToArray();BinaryPrimitives.WriteUInt16LittleEndian(hole.AsSpan(186),12);RehashCwm(hole);Bad(hole,"runtime ID hole rejected despite recalculated CWM1");
            var backward=two.ToArray();BinaryPrimitives.WriteUInt16LittleEndian(backward.AsSpan(186),10);RehashCwm(backward);Bad(backward,"repeated runtime ID rejected");
            var random=new Random(12012026);for(int i=0;i<400;i++){var fuzz=new byte[random.Next(0,9500)];random.NextBytes(fuzz);Bad(fuzz,"bounded random parser input "+i);}
            // Valid but mismatched row identities must be rejected by actual bindings.
            foreach(int offset in new[]{58,90,122,154})
            {
                string root=Path.Combine(sandbox,"mismatch-"+offset);var store=new WeaponPackageStore(root);store.Install(package.CopyArchive());
                var changed=bytes.ToArray();changed[offset]^=1;if(offset==58)RehashCwm(changed);
                Check(CustomWeaponPackageManifest.TryRead(changed,out var mismatch),"nonzero alternate digest remains syntactically valid "+offset);
                Reject(()=>WeaponMatchPackageBinding.BindInstalled(store,mismatch!).Dispose(),"binding refuses exact identity mismatch "+offset);
                store.Uninstall(package.PackageHash);Check(store.Prune()==1,"failed binding leaks no archive lease "+offset);
            }
            var installedStore=new WeaponPackageStore(Path.Combine(sandbox,"installed"));installedStore.Install(package.CopyArchive());
            var binding=WeaponMatchPackageBinding.BindInstalled(installedStore,manifest);
            Check(binding.Count==1&&binding.Manifest.Digest==manifest.Digest&&binding.Package(0).PackageHash==package.PackageHash&&binding.Definition(new(10)).Identity==package.Definition.Identity,"installed binding exposes exact pinned validated package and runtime definition");
            Reject(()=>binding.Definition(new(9)),"native runtime cannot resolve through package binding");Reject(()=>binding.Package(1),"package index is bounded");
            Check(installedStore.Uninstall(package.PackageHash)&&installedStore.Prune()==0,"binding retains actual lease after uninstall");
            Reject(()=>WeaponMatchPackageBinding.BindInstalled(installedStore,manifest).Dispose(),"installed binding refuses archive without marker");
            using(var historical=WeaponMatchPackageBinding.BindHistorical(installedStore,manifest))Check(historical.Package(0).PackageHash==package.PackageHash&&installedStore.Prune()==0,"historical binding resolves exact retained uninstalled revision");
            binding.Dispose();binding.Dispose();Reject(()=>binding.Package(0),"disposed binding cannot expose package");Reject(()=>binding.Definition(new(10)),"disposed binding cannot expose definition");Check(installedStore.Prune()==1,"idempotent dispose releases final retained archive lease");
            Reject(()=>WeaponMatchPackageBinding.BindHistorical(installedStore,manifest).Dispose(),"historical missing revision refuses without fallback");
            var partialStore=new WeaponPackageStore(Path.Combine(sandbox,"partial"));partialStore.Install(all[0].CopyArchive());var pair=new CustomWeaponPackageManifest(all.Take(2));
            Reject(()=>WeaponMatchPackageBinding.BindInstalled(partialStore,pair).Dispose(),"partial installed binding refuses missing second package");
            Reject(()=>WeaponMatchPackageBinding.BindHistorical(partialStore,pair).Dispose(),"partial historical binding refuses missing second package");
            partialStore.Uninstall(all[0].PackageHash);Check(partialStore.Prune()==1,"partial binding failure releases first acquired lease");
            var cancelledStore=new WeaponPackageStore(Path.Combine(sandbox,"cancelled"));cancelledStore.Install(package.CopyArchive());
            using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();Reject(()=>WeaponMatchPackageBinding.BindInstalled(cancelledStore,manifest,cancellation.Token).Dispose(),"pre-cancelled installed binding refuses");Reject(()=>WeaponMatchPackageBinding.BindHistorical(cancelledStore,manifest,cancellation.Token).Dispose(),"pre-cancelled historical binding refuses");}
            using(var locked=new FileStream(Path.Combine(sandbox,"cancelled",".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            using(var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(75)))
            {
                var bounded=Stopwatch.StartNew();Reject(()=>WeaponMatchPackageBinding.BindInstalled(cancelledStore,manifest,cancellation.Token).Dispose(),"installed binding cancellation interrupts actual store gate contention");
                Check(bounded.Elapsed<TimeSpan.FromSeconds(3),"cancelled gate acquisition is bounded independently of10second lock timeout");
            }
            cancelledStore.Uninstall(package.PackageHash);Check(cancelledStore.Prune()==1,"cancelled bindings leak no lease");
        }
        catch(Exception error){Check(false,"unexpected suite exception: "+error);}
        finally{try{Directory.Delete(sandbox,true);}catch(Exception error){Check(false,"sandbox cleanup: "+error.Message);}}
        Check(JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P})==native,"complete native weapon table values unchanged");
        var result=new{checks,failures,goldenSha256=GoldenSha,mechanicsSha256=CwmSha,seconds=watch.Elapsed.TotalSeconds,scope="CPU-only CWPK canonical parser/digests and exact installed/historical binding with real archive leases; no network transfer, activation or grant."};
        if(output!=null){Directory.CreateDirectory(Path.GetFullPath(output));File.WriteAllText(Path.Combine(Path.GetFullPath(output),"weapon-package-manifest-results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"{(failures.Count==0?"PASS":"FAIL")} {checks} package manifest/binding checks; failures={failures.Count}");return failures.Count==0?0:1;
    }
    static string Sha(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    static WeaponPackage[] Sorted(IEnumerable<WeaponPackage> source)=>source.OrderBy(x=>x.Definition.Identity.ToString(),StringComparer.Ordinal).ToArray();
    static byte[] Cwm(IEnumerable<WeaponPackage> source)
    {
        var packages=Sorted(source);var bytes=new byte[10+50*packages.Length];"CWM1"u8.CopyTo(bytes);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4),1);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6),1);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8),(ushort)packages.Length);
        for(int i=0;i<packages.Length;i++){int at=10+50*i;BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at),(ushort)(10+i));Convert.FromHexString(packages[i].Project.ContentId.ToString("N")).CopyTo(bytes,at+2);Convert.FromHexString(packages[i].Definition.Mechanics.DefinitionHash).CopyTo(bytes,at+18);}return bytes;
    }
    static byte[] Encode(IEnumerable<WeaponPackage> source)
    {
        var packages=Sorted(source);var bytes=new byte[40+146*packages.Length];"CWPK"u8.CopyTo(bytes);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4),1);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6),(ushort)packages.Length);SHA256.HashData(Cwm(packages)).CopyTo(bytes,8);
        var cwm=Cwm(packages);for(int i=0;i<packages.Length;i++){int at=40+146*i;cwm.AsSpan(10+50*i,50).CopyTo(bytes.AsSpan(at));Convert.FromHexString(packages[i].PackageHash).CopyTo(bytes,at+50);Convert.FromHexString(packages[i].ContentHash).CopyTo(bytes,at+82);Convert.FromHexString(packages[i].DependencyHash).CopyTo(bytes,at+114);}return bytes;
    }
    static void RehashCwm(byte[] envelope)
    {
        int count=BinaryPrimitives.ReadUInt16LittleEndian(envelope.AsSpan(6));var cwm=new byte[10+count*50];"CWM1"u8.CopyTo(cwm);BinaryPrimitives.WriteUInt16LittleEndian(cwm.AsSpan(4),1);BinaryPrimitives.WriteUInt16LittleEndian(cwm.AsSpan(6),1);BinaryPrimitives.WriteUInt16LittleEndian(cwm.AsSpan(8),(ushort)count);for(int i=0;i<count;i++)envelope.AsSpan(40+146*i,50).CopyTo(cwm.AsSpan(10+50*i));SHA256.HashData(cwm).CopyTo(envelope,8);
    }
}
