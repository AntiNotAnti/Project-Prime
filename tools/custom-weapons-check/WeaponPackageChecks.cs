using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

// Actual engine package API tests, no GL/audio device/scene/network activation.
// The small independent ZIP writer below is test fixture construction: it lets
// malformed semantic bytes retain correct CRC/ZIP structure and vice versa.
internal static class WeaponPackageChecks
{
    private const string ExpectedReferenceHash="5789962e4906689774acc14ab26118fe68df20b33b6ff9784e109cf375bf3631";
    private const int ExpectedReferenceBytes=44224;
    private sealed record Entry(string Name,byte[] Data);
    private sealed record ZipRow(string Name,int Local,int Data,int Central,int Length);
    private static byte[] Utf8(string text)=>new UTF8Encoding(false,true).GetBytes(text);
    private static string Sha(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {uint crc=uint.MaxValue;foreach(byte b in bytes){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}return ~crc;}
    private static byte[] Stored(IEnumerable<Entry> input,bool sorted=true,int gapBeforeEntry=-1,int gapBeforeDirectory=0)
    {
        var entries=(sorted?input.OrderBy(e=>e.Name,StringComparer.Ordinal):input).ToArray();
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream,Encoding.UTF8,true);var positions=new List<uint>();
        int ordinal=0;foreach(var entry in entries)
        {
            if(ordinal++==gapBeforeEntry)w.Write((byte)0);
            byte[] name=Utf8(entry.Name);positions.Add(checked((uint)stream.Position));w.Write(0x04034b50u);w.Write((ushort)20);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)33);
            w.Write(Crc(entry.Data));w.Write((uint)entry.Data.Length);w.Write((uint)entry.Data.Length);w.Write((ushort)name.Length);w.Write((ushort)0);w.Write(name);w.Write(entry.Data);
        }
        if(gapBeforeDirectory>0)w.Write(new byte[gapBeforeDirectory]);
        uint start=checked((uint)stream.Position);
        for(int i=0;i<entries.Length;i++)
        {
            var entry=entries[i];byte[] name=Utf8(entry.Name);w.Write(0x02014b50u);w.Write((ushort)20);w.Write((ushort)20);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)33);
            w.Write(Crc(entry.Data));w.Write((uint)entry.Data.Length);w.Write((uint)entry.Data.Length);w.Write((ushort)name.Length);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)0);w.Write(0u);w.Write(positions[i]);w.Write(name);
        }
        uint size=checked((uint)stream.Position-start);w.Write(0x06054b50u);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)entries.Length);w.Write((ushort)entries.Length);w.Write(size);w.Write(start);w.Write((ushort)0);return stream.ToArray();
    }
    private static Dictionary<string,byte[]> Unzip(byte[] bytes)
    {
        using var input=new MemoryStream(bytes);using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e=>e.FullName,e=>{using var s=e.Open();using var o=new MemoryStream();s.CopyTo(o);return o.ToArray();},StringComparer.Ordinal);
    }
    private static List<ZipRow> Rows(byte[] bytes)
    {
        int end=bytes.Length-22;int at=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end+16)));var rows=new List<ZipRow>();int count=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(end+10));
        for(int i=0;i<count;i++)
        {
            int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at+28)),extra=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at+30)),comment=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at+32));
            string name=Encoding.UTF8.GetString(bytes,at+46,nameLength);int local=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+42)));int n=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local+26));int x=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local+28));
            rows.Add(new(name,local,local+30+n+x,at,checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+24)))));at+=46+nameLength+extra+comment;
        }
        return rows;
    }
    private static byte[] Changed(byte[] bytes,Action<byte[]> edit){var result=bytes.ToArray();edit(result);return result;}
    private static byte[] JsonChanged(byte[] bytes,Action<JsonObject> edit){var root=JsonNode.Parse(bytes)!.AsObject();edit(root);return Utf8(root.ToJsonString());}
    private static byte[] Repack(Dictionary<string,byte[]> original,Action<Dictionary<string,byte[]>> edit,Action<JsonObject>? editManifest=null)
    {
        var files=original.ToDictionary(x=>x.Key,x=>x.Value.ToArray(),StringComparer.Ordinal);edit(files);var manifest=JsonNode.Parse(files["manifest.json"])!.AsObject();
        foreach(var row in manifest["resources"]!.AsArray())
        {string path=row!["path"]!.GetValue<string>();if(files.TryGetValue(path,out var bytes)){row["length"]=bytes.Length;row["sha256"]=Sha(bytes);}}
        editManifest?.Invoke(manifest);files["manifest.json"]=Utf8(manifest.ToJsonString());return Stored(files.Select(x=>new Entry(x.Key,x.Value)));
    }
    internal static int Run(string? output=null)
    {
        int checks=0;var failures=new List<string>();void Check(bool good,string label){checks++;if(!good){failures.Add(label);Console.Error.WriteLine("WEAPON PACKAGE FAIL "+label);}}
        void Reject(Action action,string label)
        {try{action();Check(false,"expected InvalidDataException: "+label);}catch(InvalidDataException){Check(true,label);}catch(Exception e){Check(false,label+" leaked "+e.GetType().Name+": "+e.Message);}}
        string native=JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P});var rng=Rng.Current;var rngBefore=(rng.Rng1,rng.Rng2);var watch=System.Diagnostics.Stopwatch.StartNew();
        string? referenceHash=null,contentHash=null,dependencyHash=null,mechanicsHash=null,contentId=null;byte[]? referenceBytes=null;int referenceLength=0;
        try
        {
            Headless.Enter();byte[] reference=WeaponPackageReference.BuildRiftlineM7();referenceHash=Sha(reference);referenceBytes=reference;referenceLength=reference.Length;var package=WeaponPackage.Parse(reference);var files=Unzip(reference);var rows=Rows(reference);
            contentHash=package.ContentHash;dependencyHash=package.DependencyHash;mechanicsHash=package.Definition.Mechanics.DefinitionHash;contentId=package.Definition.Identity.ContentId.ToString("D");
            Check(reference.Length==ExpectedReferenceBytes&&referenceHash==ExpectedReferenceHash,"schema1 historical reference44224-byte archive matches independently frozen SHA256");
            // Independently computed with Python stdlib ZIP/SHA256 from the original
            // reference: WPD1 + NUL + its canonical normalized resource manifest;
            // project/mechanics/empty-lock JSON in this vector are already canonical.
            Check(contentHash=="5a70fce558e7cdd0eebf28293d5258da0baaddc8b582cf73e9a8419340f6e24e"
                &&dependencyHash=="10ea078c19901f782285c3185985b5958232eb28b843042c31a23ff1c03e15a1"
                &&mechanicsHash=="3ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228"
                &&contentId=="9a776851-f39f-4a76-8c13-c5d88875ef61","schema1 full independent content/dependency/mechanics/lineage historical vector");
            Check(package.ContentHash==Sha("WPD1\0"u8.ToArray().Concat(files["manifest.json"]).ToArray()),"reference canonical content domain/framing matches independent SHA computation");
            Check(package.PackageHash==Sha(reference),"archive identity is exact full-byte SHA256");
            Check(package.Definition.Identity==RiftlineM7.Create().Identity&&package.Definition.Name=="Riftline M7"&&package.Definition.Mechanics.ToCanonicalBytes().AsSpan().SequenceEqual(RiftlineM7.Create().Mechanics.ToCanonicalBytes()),"reference uses exact existing M7 mechanics/identity/name");
            Check(WeaponIdentity.IsDigest(package.ContentHash)&&WeaponIdentity.IsDigest(package.DependencyHash)&&package.ContentHash!=package.PackageHash,"content/dependency hashes are distinct typed digest values");
            Check(files.Count==11&&files.Keys.Order(StringComparer.Ordinal).SequenceEqual(new[]{"manifest.json","weapon.json","mechanics.json","dependencies.lock.json","model/model-v1.json","audio/shot.wav","audio/bolt.wav","audio/reload.wav","audio/empty.wav","LICENSE.txt","README.txt"}.Order(StringComparer.Ordinal)),"reference contains exactly11 declared self-contained original resources/metadata");
            Check(rows.Select(x=>x.Name).SequenceEqual(files.Keys.Order(StringComparer.Ordinal)),"writer stores entries in ordinal order");
            Check(Stored(files.Select(kv=>new Entry(kv.Key,kv.Value))).AsSpan().SequenceEqual(reference),"independent canonical ZIP32 writer reproduces exact reference archive");
            var canonical=package.Project.ToCanonicalBytes();Check(WeaponProject.Parse(canonical).ToCanonicalBytes().AsSpan().SequenceEqual(canonical),"project strict parse/canonical roundtrip");
            Check(package.DependencyHash==Sha(files["dependencies.lock.json"]),"dependency hash binds actual canonical empty lock bytes");
            var mechanics=WeaponMechanics.Parse(files["mechanics.json"]);Check(mechanics.DefinitionHash==package.Definition.Identity.DefinitionHash,"package mechanics digest is existing engine canonical definition digest");
            Check(Sha(files["model/model-v1.json"])==CustomRifleModel.Digest,"original reference model exact independently pinned bytes");CustomRifleModel.DecodeVerified(files["model/model-v1.json"]);
            foreach(var cue in new[]{CustomRifleCue.Shot,CustomRifleCue.Bolt,CustomRifleCue.Reload,CustomRifleCue.Empty})Check(files["audio/"+cue.ToString().ToLowerInvariant()+".wav"].AsSpan().SequenceEqual(CustomRifleCues.BuildWave(cue)),"reference cue exactly equals original generated PCM: "+cue);
            var resources=files.Where(kv=>kv.Key is not("weapon.json" or "manifest.json")).ToDictionary(kv=>kv.Key,kv=>kv.Value.ToArray(),StringComparer.Ordinal);
            Check(WeaponPackageBuilder.Build(package.Project,resources).AsSpan().SequenceEqual(reference),"reference rebuild through ordinary project/resources path is exact");
            Check(WeaponPackageBuilder.Build(WeaponProject.Parse(canonical),resources.Reverse().ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal)).AsSpan().SequenceEqual(reference),"resource enumeration order cannot affect archive bytes");
            var savedCulture=CultureInfo.CurrentCulture;var savedUi=CultureInfo.CurrentUICulture;
            try{foreach(string culture in new[]{"tr-TR","fr-FR","ar-SA"}){CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);CultureInfo.CurrentUICulture=CultureInfo.CurrentCulture;Check(WeaponPackageReference.BuildRiftlineM7().AsSpan().SequenceEqual(reference)&&WeaponPackageBuilder.Build(package.Project,resources).AsSpan().SequenceEqual(reference),"deterministic archive/project under "+culture);}}
            finally{CultureInfo.CurrentCulture=savedCulture;CultureInfo.CurrentUICulture=savedUi;}
            foreach(bool mp in new[]{false,true})
            {
                using var bundled=new WeaponRegistry(mp,new[]{RiftlineM7.Create()});using var loaded=new WeaponRegistry(mp,new[]{package.Definition});Check(loaded.Count==10&&bundled.Count==10,"one package retains nine native runtime entries");
                for(ushort beam=1;beam<=9;beam++){Check(bundled.TryResolve(new WeaponRuntimeId(beam),out var a)&&loaded.TryResolve(new WeaponRuntimeId(beam),out var b)&&a!.Identity==b!.Identity&&a.Native!.BodyDamage==b.Native!.BodyDamage&&a.Native.HeadDamage==b.Native.HeadDamage&&a.Native.CooldownTicks==b.Native.CooldownTicks&&a.Native.AmmoCost==b.Native.AmmoCost&&a.NativeAffinity!.BodyDamage==b.NativeAffinity!.BodyDamage,"native registry slot parity "+mp+"/"+beam);}
                Check(loaded.TryResolve(new WeaponRuntimeId(10),out var entry)&&entry!.Custom!.Identity==package.Definition.Identity,"custom runtime10 remains registry-assigned exact M7");
            }
            Check(new CustomWeaponManifest(new[]{package.Definition}).Digest==new CustomWeaponManifest(new[]{RiftlineM7.Create()}).Digest,"local package leaves mechanics-only CWM1 identity unchanged");
            // Immutability is observed through actual engine API, not just file copies.
            var mutable=reference.ToArray();var retained=WeaponPackage.Parse(mutable);Array.Fill(mutable,(byte)0);Check(retained.PackageHash==package.PackageHash&&retained.Project.ToCanonicalBytes().AsSpan().SequenceEqual(canonical),"reader owns immutable archive/project state");
            foreach(string path in package.ResourcePaths){byte[] a=package.CopyResource(path),b=package.CopyResource(path);Check(!ReferenceEquals(a,b)&&a.AsSpan().SequenceEqual(b),"resource reads return detached exact bytes: "+path);if(a.Length>0)a[0]^=255;Check(package.CopyResource(path).AsSpan().SequenceEqual(b),"caller edits cannot change retained resource: "+path);}
            var listPackage=WeaponPackage.Parse(reference);var pathsBefore=listPackage.ResourcePaths.ToArray();
            if(listPackage.ResourcePaths is IList<string> pathList){try{pathList[0]="attacker.txt";}catch(NotSupportedException){}}
            Check(listPackage.ResourcePaths.SequenceEqual(pathsBefore),"public readonly resource list cannot mutate package inventory");
            var audioBefore=listPackage.Project.AudioPaths.ToArray();if(listPackage.Project.AudioPaths is IList<string> audioList){try{audioList[0]="attacker.wav";}catch(NotSupportedException){}}
            Check(listPackage.Project.AudioPaths.SequenceEqual(audioBefore),"public readonly cue list cannot mutate retained project");
            byte[] archiveCopy=listPackage.CopyArchive();archiveCopy[0]=0;Check(listPackage.CopyArchive().AsSpan().SequenceEqual(reference),"archive copy is detached from retained immutable bytes");
            Reject(()=>package.CopyResource("../missing.txt"),"resource retrieval is exact and cannot traverse");Reject(()=>package.CopyResource("audio/SHOT.wav"),"resource retrieval cannot normalize case");
            var alteredCanonical=package.Project.ToCanonicalBytes();if(alteredCanonical.Length>0)alteredCanonical[0]=0;Check(package.Project.ToCanonicalBytes().AsSpan().SequenceEqual(canonical),"canonical project arrays are detached");
            var ownedInput=resources.ToDictionary(x=>x.Key,x=>x.Value.ToArray(),StringComparer.Ordinal);byte[] built=WeaponPackageBuilder.Build(package.Project,ownedInput);foreach(var value in ownedInput.Values)Array.Fill(value,(byte)0);Check(WeaponPackage.Parse(built).Definition.Identity==package.Definition.Identity,"builder owns emitted resource bytes independent of mutable caller arrays");
            Check(package.ResourcePaths.Count==10&&package.ResourcePaths.Order(StringComparer.Ordinal).SequenceEqual(files.Keys.Where(p=>p!="manifest.json").Order(StringComparer.Ordinal)),"resource path inventory exposes exact ten immutable nonmanifest resources");
            ProjectCases(canonical,Check,Reject);ArchiveCases(reference,files,rows,Check,Reject);ManifestCases(package,reference,files,Check,Reject);ResourceCases(package,resources,Check,Reject);Fuzz(reference,Reject);DiagnosticCases(canonical,files,Check);
        }
        catch(Exception e){Check(false,"unexpected suite exception: "+e);}
        Check(native==JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P}),"all native SP/MP weapon table values remain exact");Check(ReferenceEquals(rng,Rng.Current)&&(rng.Rng1,rng.Rng2)==rngBefore,"package validation/build leaves gameplay RNG unchanged");
        var result=new{checks,failures,referenceHash,referenceLength,contentHash,dependencyHash,mechanicsHash,contentId,seconds=watch.Elapsed.TotalSeconds,scope="Actual engine CPU package/project/builder/reference APIs and independent malformed ZIP fixtures; no install/CLI scene/network/render/audio/device activation claim."};
        if(output!=null){Directory.CreateDirectory(Path.GetFullPath(output));File.WriteAllText(Path.Combine(Path.GetFullPath(output),"weapon-package-results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));if(referenceBytes!=null)File.WriteAllBytes(Path.Combine(Path.GetFullPath(output),"reference.ppweapon"),referenceBytes);}
        Console.WriteLine($"{(failures.Count==0?"PASS":"FAIL")} {checks} weapon package checks; failures={failures.Count}; reference={referenceHash}/{referenceLength}bytes");return failures.Count==0?0:1;
    }
    private static void ProjectCases(byte[] canonical,Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var prop in JsonNode.Parse(canonical)!.AsObject())reject(()=>WeaponProject.Parse(JsonChanged(canonical,r=>r.Remove(prop.Key))),"missing project property "+prop.Key);
        reject(()=>WeaponProject.Parse(JsonChanged(canonical,r=>r["futureUnsupported"]=true)),"unknown project capability");
        string text=Encoding.UTF8.GetString(canonical);string first=JsonNode.Parse(canonical)!.AsObject().First().Key;var value=JsonNode.Parse(canonical)![first]!.ToJsonString();
        reject(()=>WeaponProject.Parse(Utf8("{\""+first+"\":"+value+","+text[1..])),"duplicate project field");
        reject(()=>WeaponProject.Parse(Array.Empty<byte>()),"empty project");reject(()=>WeaponProject.Parse(Utf8("[]")),"project must be object");reject(()=>WeaponProject.Parse(Utf8("null")),"project cannot be null");
        reject(()=>WeaponProject.Parse(new byte[]{0xc0,0xaf}),"malformed UTF8 project");reject(()=>WeaponProject.Parse(Utf8(text.Replace("{","{/*comment*/",StringComparison.Ordinal))),"comments forbidden");reject(()=>WeaponProject.Parse(Utf8(text[..^1]+",}")),"trailing comma forbidden");
        reject(()=>WeaponProject.Parse(JsonChanged(canonical,r=>r["schema"]=2)),"future project schema");reject(()=>WeaponProject.Parse(JsonChanged(canonical,r=>r["name"]=new string('x',81))),"overlong display name");reject(()=>WeaponProject.Parse(JsonChanged(canonical,r=>r["name"]="bad\nname")),"control character display name");
        reject(()=>WeaponProject.Parse(new byte[WeaponPackageLimits.MaximumJsonBytes+1]),"project JSON over exact byte budget");
        check(WeaponProject.Parse(Utf8(text+new string(' ',WeaponPackageLimits.MaximumJsonBytes-canonical.Length))).ToCanonicalBytes().AsSpan().SequenceEqual(canonical),"exact65536 authoring JSON bytes accepted before canonicalization");
        void Bad(string label,Action<JsonObject> edit)=>reject(()=>WeaponProject.Parse(JsonChanged(canonical,edit)),label);
        void Good(string label,Action<JsonObject> edit){var bytes=JsonChanged(canonical,edit);check(WeaponProject.Parse(bytes).ToCanonicalBytes().Length>0,label);}
        foreach(string field in new[]{"contentId","productId","name","author","license","readme","mechanics","presentation","dependencies"})Bad("project null/wrong type "+field,r=>r[field]=null);
        foreach(string id in new[]{"",Guid.Empty.ToString("D"),"9A776851-F39F-4A76-8C13-C5D88875EF61","9a776851f39f4a768c13c5d88875ef61","not-a-guid"})Bad("noncanonical/invalid lineage "+id,r=>r["contentId"]=id);
        foreach(string id in new[]{"",new string('a',65),"1leadingdigit","Prime.Weapon","http://weapon","prime_weapon","prime weapon"})Bad("unsupported productId "+id,r=>r["productId"]=id);
        Good("inclusive author/name/product text bounds",r=>{r["name"]=new string('N',80);r["author"]=new string('A',120);r["productId"]="a"+new string('0',63);});
        Bad("overlong author",r=>r["author"]=new string('A',121));
        foreach(string spdx in new[]{"CC0-1.0","CC-BY-4.0","MIT","Apache-2.0","LicenseRef-Creator"})Good("explicit accepted license "+spdx,r=>r["license"]!["spdx"]=spdx);
        Bad("unknown license capability",r=>r["license"]!["spdx"]="unreviewed-license");
        foreach(string key in new[]{"spdx","path"})Bad("missing nested license "+key,r=>r["license"]!.AsObject().Remove(key));
        foreach(string key in new[]{"schema","mount","model","fallback","audio"})Bad("missing presentation "+key,r=>r["presentation"]!.AsObject().Remove(key));
        foreach(string key in new[]{"format","path"})Bad("missing model "+key,r=>r["presentation"]!["model"]!.AsObject().Remove(key));
        foreach(string key in new[]{"format","shot","bolt","reload","empty"})Bad("missing cue definition "+key,r=>r["presentation"]!["audio"]!.AsObject().Remove(key));
        Bad("future presentation schema",r=>r["presentation"]!["schema"]=2);Bad("custom mount script capability",r=>r["presentation"]!["mount"]="scripted-v1");Bad("unknown fallback capability",r=>r["presentation"]!["fallback"]="native-beam-10");
        foreach(string format in new[]{"model/gltf-binary","prime-rigid-glb-v1","gltf","script","shader"})Bad("unsupported creator model format "+format,r=>r["presentation"]!["model"]!["format"]=format);
        Bad("unsupported audio codec",r=>r["presentation"]!["audio"]!["format"]="audio/ogg");Bad("unknown dependency URI",r=>r["dependencies"]="https://invalid.example/lock.json");
        foreach(string path in new[]{"../model.json","/model.json","C:/model.json","dir\\model.json","dir//model.json","dir/./model.json","dir/../model.json","CON.json","com0.json","lpt9.json","model.glb","model.gltf","model.json.",".hidden.json",new string('a',65)+"/model.json",new string('a',64)+"/"+new string('b',59)+".json","模型.json"})Bad("unsafe model reference "+path,r=>r["presentation"]!["model"]!["path"]=path);
        Good("exact128-byte path with bounded components",r=>r["presentation"]!["model"]!["path"]=new string('a',64)+"/"+new string('b',58)+".json");
        Bad("resource path depth9 refused",r=>r["presentation"]!["model"]!["path"]="a/b/c/d/e/f/g/h/model.json");
        Good("resource path depth8 accepted",r=>r["presentation"]!["model"]!["path"]="a/b/c/d/e/f/g/model.json");
        Bad("two cues cannot alias exact path",r=>r["presentation"]!["audio"]!["bolt"]=r["presentation"]!["audio"]!["shot"]!.GetValue<string>());
        Bad("two cues cannot alias case path",r=>r["presentation"]!["audio"]!["bolt"]="audio/SHOT.wav");Bad("license/readme references cannot alias",r=>r["license"]!["path"]=r["readme"]!.GetValue<string>());
        Bad("mechanics/model references cannot alias",r=>r["mechanics"]=r["presentation"]!["model"]!["path"]!.GetValue<string>());Bad("reserved generated resource path",r=>r["mechanics"]="WEAPON.json");
        foreach(string pointer in new[]{"license","presentation","presentation/model","presentation/audio"})
        {
            Bad("unknown nested field "+pointer,r=>{JsonNode node=r;foreach(string component in pointer.Split('/'))node=node[component]!;node["unknown"]=true;});
            var duplicate=Encoding.UTF8.GetString(JsonChanged(canonical,r=>r["name"]="SENTINEL"));
            if(pointer=="license")reject(()=>WeaponProject.Parse(Utf8(duplicate.Replace("\"spdx\":", "\"spdx\":\"MIT\",\"spdx\":",StringComparison.Ordinal))),"duplicate nested license field");
        }
        string sentinel=Encoding.UTF8.GetString(JsonChanged(canonical,r=>r["name"]="SENTINEL"));
        foreach(string invalid in new[]{"\\uD800","\\uDC00","\\uD800text","\\uD801"})reject(()=>WeaponProject.Parse(Utf8(sentinel.Replace("SENTINEL",invalid,StringComparison.Ordinal))),"unpaired JSON escaped surrogate "+invalid);
        reject(()=>WeaponProject.Parse(Utf8(sentinel.Replace("\"schema\":1","\"schema\":1e9999",StringComparison.Ordinal))),"nonfinite numeric JSON");
        check(WeaponProject.Parse(Utf8(" \n"+text+"\t")).ToCanonicalBytes().AsSpan().SequenceEqual(canonical),"insignificant authoring JSON whitespace canonicalizes");
    }
    private static void ArchiveCases(byte[] reference,Dictionary<string,byte[]> files,List<ZipRow> rows,Action<bool,string> check,Action<Action,string> reject)
    {
        void Bad(string label,Action<byte[]> change)=>reject(()=>WeaponPackage.Parse(Changed(reference,change)),label);
        void U16(byte[] bytes,int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at),value);void U32(byte[] bytes,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at),value);
        int end=reference.Length-22;var first=rows[0];
        foreach(int delta in new[]{0,1,2,3})Bad("local signature byte "+delta,b=>b[first.Local+delta]^=1);
        foreach(var row in rows)
        {
            Bad("CRC mismatch "+row.Name,b=>b[row.Data]^=1);Bad("central CRC mismatch "+row.Name,b=>b[row.Central+16]^=1);
            foreach(int offset in new[]{4,6,8,10,12,28})Bad("unsupported local ZIP field "+row.Name+"/"+offset,b=>U16(b,row.Local+offset,offset==12?(ushort)34:(ushort)1));
            foreach(int offset in new[]{4,6,8,10,12,14,30,32,34,36})Bad("unsupported central ZIP field "+row.Name+"/"+offset,b=>U16(b,row.Central+offset,offset==14?(ushort)34:(ushort)1));
            Bad("central nonzero attributes "+row.Name,b=>U32(b,row.Central+38,0xa1ff0000));Bad("overlap/foreign local offset "+row.Name,b=>U32(b,row.Central+42,1));
            Bad("ZIP64 size sentinel "+row.Name,b=>U32(b,row.Local+18,uint.MaxValue));Bad("central declared huge expanded size "+row.Name,b=>U32(b,row.Central+24,uint.MaxValue));
        }
        foreach(int offset in new[]{4,6,8,10,20})Bad("EOCD canonical fields "+offset,b=>U16(b,end+offset,offset is 8 or 10?(ushort)65535:(ushort)1));
        Bad("EOCD invalid central size",b=>U32(b,end+12,uint.MaxValue));Bad("EOCD invalid central offset",b=>U32(b,end+16,uint.MaxValue));
        reject(()=>WeaponPackage.Parse(reference.Concat(new byte[]{0}).ToArray()),"trailing bytes");reject(()=>WeaponPackage.Parse(new byte[]{0}.Concat(reference).ToArray()),"leading prefix bytes");
        reject(()=>WeaponPackage.Parse(Stored(files.Reverse().Select(x=>new Entry(x.Key,x.Value)),sorted:false)),"unsorted entries even with valid CRC/directory");
        reject(()=>WeaponPackage.Parse(Stored(files.Select(x=>new Entry(x.Key,x.Value)).Concat(new[]{new Entry(first.Name,files[first.Name])}))),"duplicate ZIP path");
        foreach(string path in new[]{"../escape.txt","/absolute.txt","C:/drive.txt","dir\\back.txt","dir/../escape.txt","CON.txt","dir/name.","dir/name ","dir//empty.txt","./relative.txt","url:https.txt","file.glb","file.gltf","script.js","shader.glsl","nested.zip","plugin.dll","launch.exe"})
            reject(()=>WeaponPackage.Parse(Stored(files.Select(x=>new Entry(x.Key,x.Value)).Append(new Entry(path,Utf8("untrusted"))))),"unsafe/unsupported extra entry "+path);
        foreach(string path in files.Keys)reject(()=>WeaponPackage.Parse(Stored(files.Where(x=>x.Key!=path).Select(x=>new Entry(x.Key,x.Value)))),"missing required archive resource "+path);
        reject(()=>WeaponPackage.Parse(Stored(files.Select(x=>new Entry(x.Key,x.Value)).Append(new Entry("README.TXT",Utf8("case alias"))))),"case-colliding archive entry");
        reject(()=>WeaponPackage.Parse(Stored(files.Select(x=>new Entry(x.Key,x.Value)),gapBeforeEntry:1)),"valid ZIP local ranges cannot contain leading/inter-entry gaps");
        reject(()=>WeaponPackage.Parse(Stored(files.Select(x=>new Entry(x.Key,x.Value)),gapBeforeDirectory:1)),"valid ZIP cannot contain a gap before central directory");
        foreach(ushort flags in new ushort[]{1,8,0x800,0x8000})Bad("paired unsupported general flags "+flags,b=>{U16(b,first.Local+6,flags);U16(b,first.Central+8,flags);});
        Bad("paired unsupported compression",b=>{U16(b,first.Local+8,8);U16(b,first.Central+10,8);});
        Bad("Unix symlink external mode",b=>{U16(b,first.Central+4,0x0314);U32(b,first.Central+38,0xa1ff0000);});
        reject(()=>WeaponPackage.Parse(new byte[WeaponPackageLimits.MaximumArchiveBytes+1]),"archive byte budget checked before parse");
        reject(()=>WeaponPackage.Parse(Stored(Enumerable.Range(0,WeaponPackageLimits.MaximumEntries+1).Select(i=>new Entry("file"+i+".txt",Array.Empty<byte>())))),"archive entry budget rejects33 entries");
        // A valid CRC does not replace the separately declared SHA manifest.
        var changed=files.Select(x=>new Entry(x.Key,x.Key=="README.txt"?Utf8("changed without changing manifest"):x.Value));reject(()=>WeaponPackage.Parse(Stored(changed)),"valid CRC with manifest resource SHA mismatch");
        check(Crc("123456789"u8)==0xcbf43926,"independent fixture CRC standard vector");
    }
    private static void ManifestCases(WeaponPackage package,byte[] reference,Dictionary<string,byte[]> files,Action<bool,string> check,Action<Action,string> reject)
    {
        void Bad(string label,Action<JsonObject> edit)=>reject(()=>WeaponPackage.Parse(Repack(files,_=>{},edit)),label);
        foreach(string key in new[]{"schema","kind","contentId","mechanicsHash","resources"})Bad("missing manifest field "+key,r=>r.Remove(key));
        Bad("manifest unknown field",r=>r["unsupported"]=true);Bad("manifest future schema",r=>r["schema"]=2);Bad("manifest cannot claim generic model",r=>r["kind"]="model");
        Bad("manifest wrong lineage",r=>r["contentId"]=Guid.Empty.ToString("D"));Bad("manifest mechanics SHA substitution",r=>r["mechanicsHash"]=new string('a',64));
        Bad("manifest rows missing",r=>r["resources"]!.AsArray().RemoveAt(0));Bad("manifest extra row",r=>r["resources"]!.AsArray().Add(r["resources"]![0]!.DeepClone()));
        Bad("manifest rows order is canonical",r=>{var rows=r["resources"]!.AsArray();var a=rows[0]!.DeepClone();rows[0]=rows[1]!.DeepClone();rows[1]=a;});
        foreach(string key in new[]{"path","mediaType","length","sha256"})Bad("missing manifest resource field "+key,r=>r["resources"]![0]!.AsObject().Remove(key));
        Bad("manifest resource unknown field",r=>r["resources"]![0]!["url"]="https://invalid.example/resource");Bad("manifest resource length mismatch",r=>r["resources"]![0]!["length"]=int.MaxValue);
        Bad("manifest resource SHA mismatch",r=>r["resources"]![0]!["sha256"]=new string('0',64));Bad("manifest resource media mismatch",r=>r["resources"]![0]!["mediaType"]="application/octet-stream");
        Bad("manifest resource path exact",r=>r["resources"]![0]!["path"]="license.txt");
        var pretty=Repack(files,r=>
        {
            foreach(string path in new[]{"weapon.json","mechanics.json","dependencies.lock.json"})
            {var root=JsonNode.Parse(r[path])!.AsObject();var reversed=new JsonObject();foreach(var kv in root.Reverse())reversed[kv.Key]=kv.Value?.DeepClone();r[path]=Utf8(reversed.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));}
        });var formatted=WeaponPackage.Parse(pretty);
        check(formatted.PackageHash!=package.PackageHash&&formatted.ContentHash==package.ContentHash&&formatted.DependencyHash==package.DependencyHash&&formatted.Definition.Identity==package.Definition.Identity,"raw authoring JSON order/whitespace changes archive only; canonical content/mechanics/lock identities stable");
        var formattedFiles=Unzip(pretty);var inputs=formattedFiles.Where(x=>x.Key is not("weapon.json" or "manifest.json")).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
        check(WeaponPackageBuilder.Build(formatted.Project,inputs).AsSpan().SequenceEqual(reference),"ordinary builder normalizes equivalent project/mechanics/empty-lock JSON to exact reference bytes");
        reject(()=>WeaponPackage.Parse(Repack(files,r=>r["audio/shot.wav"]=Changed(r["audio/shot.wav"],b=>b[20]=3))),"reader decodes malformed actual WAV after independently valid ZIP/CRC/SHA");
        reject(()=>WeaponPackage.Parse(Repack(files,r=>r["model/model-v1.json"]=Utf8("{\"schema\":1,\"uri\":\"https://invalid.example/model.glb\"}"))),"reader rejects unsupported actual model after independently valid CRC/SHA");
        reject(()=>WeaponPackage.Parse(Repack(files,r=>r["dependencies.lock.json"]=Utf8("{\"schema\":1,\"dependencies\":[{}]}"))),"reader rejects actual unsupported dependencies after valid CRC/SHA");
    }
    private static void ResourceCases(WeaponPackage package,Dictionary<string,byte[]> resources,Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(string path in resources.Keys){var missing=resources.Where(x=>x.Key!=path).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);reject(()=>WeaponPackageBuilder.Build(package.Project,missing),"builder missing resource "+path);}
        foreach(string name in new[]{"weapon.json","manifest.json","extra.txt","shader.glsl","nested.ppweapon","../outside.txt"}){var extra=new Dictionary<string,byte[]>(resources,StringComparer.Ordinal){[name]=Utf8("bad")};reject(()=>WeaponPackageBuilder.Build(package.Project,extra),"builder refuses undeclared/generated/unsafe path "+name);}
        var broken=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);broken["dependencies.lock.json"]=Utf8("{\"schema\":1,\"dependencies\":[{\"id\":\"remote\",\"sha256\":\""+new string('a',64)+"\"}]}");reject(()=>WeaponPackageBuilder.Build(package.Project,broken),"nonempty dependency lock unsupported");
        broken=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);broken["model/model-v1.json"]=Utf8("{\"schema\":1,\"uri\":\"https://invalid.example/model.glb\"}");reject(()=>WeaponPackageBuilder.Build(package.Project,broken),"model cannot resolve external URI/unsupported schema");
        foreach(string audio in new[]{"audio/shot.wav","audio/bolt.wav","audio/reload.wav","audio/empty.wav"})
        {
            foreach(int at in new[]{0,4,8,12,16,20,22,24,28,32,34,36,40}){var copy=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);copy[audio]=Changed(copy[audio],b=>b[at]^=1);reject(()=>WeaponPackageBuilder.Build(package.Project,copy),"strict WAV header "+audio+"/"+at);}
            foreach(int length in new[]{0,1,43}){var copy=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);copy[audio]=copy[audio][..length];reject(()=>WeaponPackageBuilder.Build(package.Project,copy),"truncated WAV "+audio+"/"+length);}
        }
        foreach(int frames in new[]{0,WeaponPackageLimits.MaximumWaveFrames+1})
        {
            var copy=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);var wave=new byte[44+frames*2];resources["audio/shot.wav"].AsSpan(0,44).CopyTo(wave);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4),wave.Length-8);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40),frames*2);copy["audio/shot.wav"]=wave;
            reject(()=>WeaponPackageBuilder.Build(package.Project,copy),"WAV sample frames out of bounds "+frames);
        }
        {
            var copy=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);var wave=new byte[44+WeaponPackageLimits.MaximumWaveFrames*2];resources["audio/shot.wav"].AsSpan(0,44).CopyTo(wave);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4),wave.Length-8);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40),WeaponPackageLimits.MaximumWaveFrames*2);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44),short.MinValue);BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(46),short.MaxValue);copy["audio/shot.wav"]=wave;
            check(WeaponPackage.Parse(WeaponPackageBuilder.Build(package.Project,copy)).CopyResource("audio/shot.wav").Length==wave.Length,"inclusive11025-frame full-amplitude PCM wave is accepted");
        }
        foreach(string path in new[]{"LICENSE.txt","README.txt"})
        {
            foreach(byte[] text in new[]{Array.Empty<byte>(),new byte[WeaponPackageLimits.MaximumTextBytes+1],new byte[]{0xff,0xfe,0},new byte[]{65,0,66}})
            {var copy=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);copy[path]=text;reject(()=>WeaponPackageBuilder.Build(package.Project,copy),"invalid/empty/oversized UTF8 text "+path+"/"+text.Length);}
        }
        var metadata=WeaponProject.Parse(JsonChanged(package.Project.ToCanonicalBytes(),r=>r["name"]="Riftline package metadata test"));var renamed=WeaponPackage.Parse(WeaponPackageBuilder.Build(metadata,resources));
        check(renamed.Definition.Identity==package.Definition.Identity&&renamed.Definition.Name!=package.Definition.Name&&renamed.PackageHash!=package.PackageHash&&renamed.ContentHash!=package.ContentHash&&renamed.DependencyHash==package.DependencyHash,"name changes package/content but never mechanics/dependency identity");
        var edited=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);edited["README.txt"]=Utf8(Encoding.UTF8.GetString(edited["README.txt"])+"\nTest metadata revision.\n");var asset=WeaponPackage.Parse(WeaponPackageBuilder.Build(package.Project,edited));
        check(asset.Definition.Identity==package.Definition.Identity&&asset.ContentHash!=package.ContentHash&&asset.PackageHash!=package.PackageHash&&asset.DependencyHash==package.DependencyHash,"text asset changes package/content identity independently of mechanics");
        edited=resources.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);edited["mechanics.json"]=JsonChanged(edited["mechanics.json"],r=>r["damage"]!["body"]=66);var mechanics=WeaponPackage.Parse(WeaponPackageBuilder.Build(package.Project,edited));
        check(mechanics.Definition.Identity.ContentId==package.Definition.Identity.ContentId&&mechanics.Definition.Identity.DefinitionHash!=package.Definition.Identity.DefinitionHash&&mechanics.ContentHash!=package.ContentHash&&mechanics.PackageHash!=package.PackageHash,"valid mechanics edit changes canonical revision and package without changing lineage");
    }
    private static void DiagnosticCases(byte[] canonical,Dictionary<string,byte[]> files,Action<bool,string> check)
    {
        string hostile="\u001b[2J\n\u202e"+new string('x',1000);
        void Printable(Action parse,string label)
        {
            try{parse();check(false,label+": malformed unknown field was accepted");}
            catch(InvalidDataException error)
            {check(error.Message.Length<=600&&error.Message.All(c=>c is >= ' ' and <= '~'),label+": diagnostic is bounded printable ASCII without terminal controls or Unicode direction markers");}
        }
        Printable(()=>WeaponProject.Parse(JsonChanged(canonical,r=>r[hostile]=true)),"hostile unknown project field");
        Printable(()=>WeaponPackage.Parse(Repack(files,_=>{},r=>r[hostile]=true)),"hostile unknown manifest field");
    }
    private static void Fuzz(byte[] reference,Action<Action,string> reject)
    {
        // Deterministic bounded corruptions; no production/game RNG used.
        for(int i=0;i<256;i++){int index=(int)(((ulong)i*2654435761u)%((ulong)reference.Length));int bit=i&7;reject(()=>WeaponPackage.Parse(Changed(reference,b=>b[index]^=(byte)(1<<bit))),"single-bit archive corruption "+i);}
        for(int i=0;i<128;i++){int length=(int)((ulong)i*(ulong)reference.Length/128);reject(()=>WeaponPackage.Parse(reference.AsMemory(0,length)),"bounded archive truncation "+length);}
        foreach(byte[] bytes in new[]{Array.Empty<byte>(),new byte[22],Utf8("{\"schema\":1}"),Utf8("PK\x03\x04notzip"),new byte[]{0xff,0xfe,0xfd}})reject(()=>WeaponPackage.Parse(bytes),"nonarchive bounded input length "+bytes.Length);
    }
}
