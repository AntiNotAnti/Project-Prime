using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;

internal static class CustomReplayBootstrapChecks
{
    const string Cwm="a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9";
    const string Cwpk="e58d66d03426c22db5b414ecddb11d33f4bdb8b4b19a8800563d372ba3cda7a4";
    const string ManifestHex="4357504b01000100"+Cwm+"0a009a776851f39f4a768c13c5d88875ef613ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228"+
        "5789962e4906689774acc14ab26118fe68df20b33b6ff9784e109cf375bf3631"+"5a70fce558e7cdd0eebf28293d5258da0baaddc8b582cf73e9a8419340f6e24e"+"10ea078c19901f782285c3185985b5958232eb28b843042c31a23ff1c03e15a1";
    const string Layout="c46969353b705e4548689c8d23095c4b2a71e2dc49084c04b2fde4d6c92d3e88";
    const string GoldenSha="1dbfc02097faa355dd49dc907b2fbc69bc029510da06a3027db3a66497a6c23a";
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12f-bootstrap-"+Guid.NewGuid().ToString("N")));
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;}
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or IOException or InvalidDataException or InvalidOperationException or OperationCanceledException){Check(true,label);return;}throw new InvalidOperationException("Expected rejection: "+label);}
        void Unavailable(Action action,string label){try{action();}catch(InvalidDataException error){Check(error.Message=="The exact historical weapon package is unavailable or invalid. Restore its recorded archive revision to view this replay.",label+" gives bounded exact-revision diagnostic");return;}throw new InvalidOperationException("Expected unavailable history: "+label);}
        void Bad(byte[] data,string label)=>Check(!ReplayCustomWeaponBootstrap.TryRead(data,out var result)&&result==null,label);
        string native=JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P});
        try
        {
            Directory.CreateDirectory(root);var package=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());var manifest=new CustomWeaponPackageManifest(new[]{package});
            var world=new WorldBootstrapIdentity(new(7,0x1122334455667788,9),11,13,17,19,23);const ulong config=0x0102030405060708;
            var bootstrap=new ReplayCustomWeaponBootstrap(world,config,WeaponDigest.FromHex(Layout),manifest);byte[] bytes=bootstrap.CopyBytes();
            // Independent header writer: no production world/header/digest writers.
            byte[] golden=new byte[332];"CWRB"u8.CopyTo(golden);golden[4]=1;golden[6]=1;
            BinaryPrimitives.WriteUInt16LittleEndian(golden.AsSpan(8),7);BinaryPrimitives.WriteUInt64LittleEndian(golden.AsSpan(10),0x1122334455667788);
            BinaryPrimitives.WriteUInt32LittleEndian(golden.AsSpan(18),9);BinaryPrimitives.WriteUInt32LittleEndian(golden.AsSpan(22),11);BinaryPrimitives.WriteUInt16LittleEndian(golden.AsSpan(26),13);
            BinaryPrimitives.WriteUInt32LittleEndian(golden.AsSpan(28),17);BinaryPrimitives.WriteUInt32LittleEndian(golden.AsSpan(32),19);BinaryPrimitives.WriteUInt32LittleEndian(golden.AsSpan(36),23);
            BinaryPrimitives.WriteUInt64LittleEndian(golden.AsSpan(40),config);Convert.FromHexString(Layout).CopyTo(golden,48);Convert.FromHexString(Cwm).CopyTo(golden,80);Convert.FromHexString(Cwpk).CopyTo(golden,112);golden[144]=186;Convert.FromHexString(ManifestHex).CopyTo(golden,146);
            Check(ReplayCustomWeaponBootstrap.Version==1&&ReplayCustomWeaponBootstrap.HeaderSize==146&&ReplayCustomWeaponBootstrap.MaximumBytes==16384,"frozen file-only CWRB bounds");
            Check(bytes.SequenceEqual(golden)&&Convert.ToHexStringLower(SHA256.HashData(golden))==GoldenSha,"independent complete332-byte golden and pinned SHA256");
            Check(ReplayCustomWeaponBootstrap.TryRead(bytes,out var parsed)&&parsed!.World==world&&parsed.PackageConfiguration==config&&parsed.PickupLayout.ToString()==Layout&&parsed.Packages.Digest==manifest.Digest&&parsed.Capabilities==ReplayCustomWeaponCapabilities.ExactPackages,"all detached bootstrap identities round trip");
            byte[] detached=bootstrap.CopyBytes();detached.AsSpan().Clear();Check(bootstrap.CopyBytes().SequenceEqual(golden),"CopyBytes cannot mutate bootstrap");
            var input=(byte[])golden.Clone();ReplayCustomWeaponBootstrap.TryRead(input,out var snap);input.AsSpan().Clear();Check(snap!.CopyBytes().SequenceEqual(golden),"parser defensively snapshots caller bytes");
            for(int length=0;length<bytes.Length;length++)Bad(bytes[..length],"truncated bootstrap "+length);
            for(int extra=1;extra<=16;extra++)Bad(bytes.Concat(new byte[extra]).ToArray(),"trailing bytes "+extra);Bad(new byte[16385],"over-limit bootstrap");
            foreach(int offset in new[]{0,1,2,3,4,5,6,7,80,111,112,143,144,145,146,150,152,186,188,204,236,268,300}){var changed=(byte[])bytes.Clone();changed[offset]^=0x80;Bad(changed,"header/manifest inconsistency "+offset);}
            foreach(var field in new[]{(8,2),(10,8),(18,4),(22,4),(26,2),(28,4),(32,4),(36,4),(40,8),(48,32),(80,32),(112,32)}){var changed=(byte[])bytes.Clone();changed.AsSpan(field.Item1,field.Item2).Clear();Bad(changed,"zero required identity "+field.Item1);}
            Reject(()=>new ReplayCustomWeaponBootstrap(default,config,WeaponDigest.FromHex(Layout),manifest),"invalid world constructor");Reject(()=>new ReplayCustomWeaponBootstrap(world,0,WeaponDigest.FromHex(Layout),manifest),"zero configuration constructor");Reject(()=>new ReplayCustomWeaponBootstrap(world,config,default,manifest),"zero layout constructor");Reject(()=>new ReplayCustomWeaponBootstrap(world,config,WeaponDigest.FromHex(Layout),null!),"null manifest constructor");
            var resources=package.ResourcePaths.Where(x=>x!="weapon.json").ToDictionary(x=>x,x=>package.CopyResource(x));var project=JsonNode.Parse(package.Project.ToCanonicalBytes())!;project["name"]="Newer metadata revision";
            var newer=WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));project["contentId"]="12f012f0-1122-3344-5566-778899aabbcc";var second=WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));var pair=new CustomWeaponPackageManifest(new[]{package,second});
            Reject(()=>new ReplayCustomWeaponBootstrap(world,config,WeaponDigest.FromHex(Layout),pair),"valid multipackage manifest excluded by single-runtime contract");
            byte[] multiple=new byte[146+pair.Size];bytes.AsSpan(0,146).CopyTo(multiple);BinaryPrimitives.WriteUInt16LittleEndian(multiple.AsSpan(144),(ushort)pair.Size);pair.MechanicsDigest.Write(multiple.AsSpan(80,32));pair.Digest.Write(multiple.AsSpan(112,32));pair.Write(multiple.AsSpan(146));Bad(multiple,"canonical multipackage envelope with consistent digests is still refused");
            string path=Path.Combine(root,"history");var store=new WeaponPackageStore(path);store.Install(package.CopyArchive());store.Uninstall(package.PackageHash);
            using(var prepared=bootstrap.PrepareHistorical(store))
            {
                Check(prepared.Package(0).PackageHash==package.PackageHash&&prepared.Resources(0).ArchiveHash==package.PackageHash,"historical preparation opens exact uninstalled retained archive");
                for(int cue=0;cue<4;cue++)Check(prepared.Resources(0).Wave(cue).SequenceEqual(WeaponPackage.DecodeWave(package.CopyResource(package.Project.AudioPaths[cue]))),"historical exact prewarmed PCM "+cue);
                WeaponMatchPackageBinding retained;using(var gate=new FileStream(Path.Combine(path,".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))retained=prepared.Retain();
                prepared.Dispose();Check(store.Prune()==0&&retained.Resources(0).Model!=null,"retained prepared resource owner survives original disposal without gate IO");retained.Dispose();
            }
            Check(store.Prune()==1,"last historical owner releases archive lease");store.Install(newer.CopyArchive());Unavailable(()=>bootstrap.PrepareHistorical(store).Dispose(),"newest metadata revision cannot substitute historical exact archive");
            string missing=Path.Combine(root,"missing");Unavailable(()=>bootstrap.PrepareHistorical(new WeaponPackageStore(missing)).Dispose(),"missing exact historical store refuses");Check(!Directory.Exists(missing),"missing history creates no root");
            string corruptPath=Path.Combine(root,"corrupt");var corrupt=new WeaponPackageStore(corruptPath);corrupt.Install(package.CopyArchive());corrupt.Uninstall(package.PackageHash);File.WriteAllBytes(Path.Combine(corruptPath,"archives",package.PackageHash+".ppweapon"),new byte[]{1,2,3});Unavailable(()=>bootstrap.PrepareHistorical(corrupt).Dispose(),"corrupt exact historical bytes refuse without repair");
            store.Install(package.CopyArchive());using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject(()=>bootstrap.PrepareHistorical(store,cancel.Token).Dispose(),"pre-cancelled historical preparation");}
            using(var gate=new FileStream(Path.Combine(path,".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            using(var cancel=new CancellationTokenSource())
            {
                using var started=new ManualResetEventSlim();Task<bool> task=Task.Run(()=>{started.Set();try{bootstrap.PrepareHistorical(store,cancel.Token).Dispose();return false;}catch(OperationCanceledException){return true;}});Check(started.Wait(2000),"real gate-contended historical worker entered");Check(!task.Wait(100),"historical worker remains blocked by owned gate before cancellation");cancel.Cancel();Check(task.Wait(3000)&&task.Result,"gate-contended cancellation completes while gate remains owned");
            }
            store.Uninstall(package.PackageHash);Check(store.Prune()==1,"cancelled historical preparation leaves no archive lease");
            string partialPath=Path.Combine(root,"partial");var partial=new WeaponPackageStore(partialPath);var first=pair.Rows[0].Archive.ToString()==package.PackageHash?package:second;partial.Install(first.CopyArchive());partial.Uninstall(first.PackageHash);Reject(()=>WeaponMatchPackageBinding.PrepareHistorical(partial,pair).Dispose(),"partial multipackage historical preparation releases acquired entries");Check(partial.Prune()==1,"partial historical bind leaks no earlier archive");
            CustomReplayBoundaryChecks.Run(Check);Check(JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P})==native,"native weapon tables remain byte-for-byte unchanged");
            File.WriteAllBytes(Path.Combine(root,"golden.cwrb"),golden);File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks,goldenSha256=GoldenSha,scope="Detached file bootstrap and exact historical prewarmed lease ownership. Production recording/killcam refusal remains tested. No custom replay record/reopen/seek or playback admission claim."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {checks} custom replay bootstrap/historical ownership checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY BOOTSTRAP FAIL "+error);return 1;}
    }
}
