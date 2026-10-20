using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using Archive=MphRead.Mods.Import.RetroCharacterAnimationArchive;

// Metadata contracts run without source media. The explicit owned-source probe
// keeps every derived byte in one disposable root, never the real inventory.
internal static class CharacterBundleAcceptance
{
    internal static void RunContract()
    {
        var specs = RetroCharacterPreviewExporter.Sources.Select(s => RetroCharacterPreviewExporter.BundleSpec(s.Id)).ToArray();
        foreach (var spec in specs)
        {
            var again = RetroCharacterPreviewExporter.BundleSpec(spec.Id);
            bool echoes=spec.Id.EndsWith("-echoes",StringComparison.Ordinal);
            Check(spec.Version == (echoes?3:1) && spec.RecipeHash.Length == 64 && spec.SourceFingerprint.Length == 40,
                "canonical version and digest contract " + spec.Id);
            string[] body={spec.Id+".obj",spec.Id+".skeleton.json",spec.Id+".weights.json",spec.Id+".motions.json"};
            string[] completeBody=body.Concat(new[]{spec.Id+".cinf.bin",spec.Id+".animations.bin"}).ToArray();
            string[] required=echoes?completeBody.Concat(completeBody.Select(f=>spec.Id+".ball"+f[spec.Id.Length..])).Append(spec.Id+".parts.json").ToArray():body;
            Check(spec.RequiredComponents.SequenceEqual(required),
                "complete required body/ball inventory in one appearance contract " + spec.Id);
            var parts=RetroCharacterPreviewExporter.PartsFor(spec.Id);
            Check(parts.Select(p=>p.Id).SequenceEqual(echoes?new[]{"body","ball"}:new[]{"body"})
                &&parts.All(p=>p.Version==(echoes?2:1))&&RetroCharacterPreviewExporter.PartStem(spec.Id,"body")==spec.Id,
                "canonical versioned authoring parts retain body namespace " + spec.Id);
            Check(!ReferenceEquals(parts,RetroCharacterPreviewExporter.PartsFor(spec.Id)),"part inventory detached between calls "+spec.Id);
            Expect<NotSupportedException>(()=>((IList<RetroCharacterPreviewExporter.SourcePart>)parts)[0]=new("tampered",0,"",99),
                "caller cannot replace source part metadata");
            Check(RetroCharacterPreviewExporter.PartsFor(spec.Id)[0].Id=="body","part metadata remains intact "+spec.Id);
            if(echoes)
                Check(parts[1].Ancs==0x654a3f97&&parts[1].NativeName=="Dark_Samus_MorphBall"
                    &&RetroCharacterPreviewExporter.PartStem(spec.Id,"ball")==spec.Id+".ball",
                    "related ball namespace keeps canonical appearance prefix "+spec.Id);
            else
            {
                string legacyHash=spec.Id switch
                {
                    "trooper-power"=>"cf04e8f85575877e15be49960c9190a9d428723846ffb4353bd91ab97ffc41d8",
                    "trooper-ice"=>"15a2e0fe73eb6983ef8baa88131576c7632e33dbaa13e266063c3511ab43b546",
                    "trooper-wave"=>"b2131444ade0323c04795ad3a2b6637b552b36ba584d32fb5ed08b92e411d204",
                    "trooper-plasma"=>"9ebb847f18b5c3930a7ceeb8ce46bed433cacdbef51f778f4a3979a13eb9add5",
                    _=>throw new InvalidOperationException("Unexpected legacy source")
                };
                Check(spec.RecipeHash==legacyHash,"Prime body v1 recipe hash remains byte-compatible "+spec.Id);
                Expect<InvalidDataException>(()=>RetroCharacterPreviewExporter.PartStem(spec.Id,"ball"),"unqualified Trooper ball refused");
            }
            Check(spec.RecipeHash == again.RecipeHash && spec.SourceFingerprint == again.SourceFingerprint && spec.RequiredComponents.SequenceEqual(again.RequiredComponents),
                "recipe stable across calls " + spec.Id);
            spec.RequiredComponents[0] = "tampered.obj";
            Check(RetroCharacterPreviewExporter.BundleSpec(spec.Id).RequiredComponents[0] == spec.Id + ".obj",
                "required inventory detached from caller " + spec.Id);
        }
        Check(specs.Select(s => s.RecipeHash).Distinct().Count() == 6, "appearance recipes remain independent");
        Check(specs.Where(s=>s.Id.StartsWith("trooper-",StringComparison.Ordinal)).Select(s => s.SourceFingerprint).Distinct().Count() == 1 && specs[0].SourceFingerprint != specs[1].SourceFingerprint,
            "four Troopers share pinned Prime source, Echoes has distinct source");
        var normal=specs.Single(s=>s.Id=="darksamus-echoes");
        var overcharged=specs.Single(s=>s.Id=="darksamus-overcharged-echoes");
        Check(normal.SourceFingerprint==overcharged.SourceFingerprint && normal.RecipeHash!=overcharged.RecipeHash,
            "distinct normal/Overcharged recipes bind the same verified Echoes edition");
        Check(RetroCharacterPreviewExporter.Sources.Single(s=>s.Id==overcharged.Id).NativeName=="Battle_3"
            && RetroCharacterPreviewExporter.Sources.Single(s=>s.Id==normal.Id).NativeName=="Dark_Samus",
            "Overcharged source name cannot silently alias the normal body");
        Check(RetroCharacterPreviewExporter.PartsFor(normal.Id)[1]==RetroCharacterPreviewExporter.PartsFor(overcharged.Id)[1]
            &&RetroCharacterPreviewExporter.PartStem(normal.Id,"ball")!=RetroCharacterPreviewExporter.PartStem(overcharged.Id,"ball"),
            "same verified alternate source has distinct normal/Overcharged artifact namespaces");
        foreach(string? part in new string?[]{null,"","Body","BALL","weapon","../ball","ball.obj","body "})
            Expect<InvalidDataException>(()=>RetroCharacterPreviewExporter.PartStem(normal.Id,part!),"noncanonical part refused");
        foreach (string? id in new string?[] { null, "", "DARKSAMUS-ECHOES", "darksamus", "../darksamus-echoes", "trooper-power.obj", "trooper-power " })
        {
            Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.BundleSpec(id!), "unknown/noncanonical identity refused");
            Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.PartsFor(id!), "unknown part-owner identity refused");
            Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.PartStem(id!,"body"), "unknown artifact-owner identity refused");
        }
        InspectPartsContract();
        string root = Path.Combine(Path.GetTempPath(), "prime-character-contract-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Expect<OperationCanceledException>(() => RetroCharacterPreviewExporter.ExportFromOwnedGame("missing-owned-source.iso", root, cancellation.Token), "pre-cancel precedes source read/output");
        Check(!Directory.Exists(root), "pre-cancel creates no output");
    }

    internal static void ProbeOwnedSource(string source)
    {
        string temporary = OperatingSystem.IsMacOS() && Directory.Exists("/private/tmp") ? "/private/tmp" : Path.GetTempPath();
        string root = Path.Combine(temporary, "prime-owned-character-bundle-" + Guid.NewGuid().ToString("N"));
        byte[] sourceBefore = HashFile(source);
        Console.WriteLine("Owned source SHA256 before: " + Convert.ToHexStringLower(sourceBefore));
        try
        {
            var files = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, root);
            Check(files.Count > 0, "owned source has supported authoring appearances");
            string[] ids = files.Select(Path.GetFileNameWithoutExtension).Select(s => s!).ToArray();
            using var resources=ids.Any(id=>RetroCharacterPreviewExporter.BundleSpec(id).Version==3)?new OwnedResources(source):null;
            foreach (string id in ids)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(id);
                var inspected = LocalImportBundle.Inspect(root, id, spec);
                Check(inspected.State == ImportBundleState.Ready && inspected.Receipt != null, "owned bundle Ready " + id);
                string obj = files.Single(f => Path.GetFileNameWithoutExtension(f) == id);
                string folder = Path.GetDirectoryName(obj)!;
                Check(Path.GetFileName(folder) == inspected.Receipt!.Generation && inspected.Files.Contains(obj)
                    && spec.RequiredComponents.All(c => inspected.Files.Contains(Path.Combine(folder, c)))
                    && inspected.Files.All(f => Path.GetDirectoryName(f) == folder), "same immutable generation body/rig/motions/optional graph " + id);
                // Optional ANCS graph must retain its exact raw snapshot sibling.
                string graph = Path.Combine(folder, id + ".ancs-graph.json");
                if (File.Exists(graph))
                {
                    string snapshot = Path.Combine(folder, id + ".ancs.bin");
                    Check(inspected.Files.Contains(snapshot), "graph retains raw ANCS snapshot " + id);
                    var proof = RetroAnimationGraphProof.Verify(File.ReadAllBytes(graph), File.ReadAllBytes(snapshot));
                    Check(proof.SourceCharacterId == id && proof.SourcePart==null && !proof.NamedGraphSemanticsAccepted && proof.UninterpretedSetBytesPreservedInSnapshot,
                        "exact raw graph proof retained without named-semantic claim " + id);
                }
                if(spec.Version==3) ValidateOwnedParts(id,folder,spec,inspected,resources!.Read,fullAnimationProof:true);
            }
            if(ids.Contains("darksamus-echoes") && ids.Contains("darksamus-overcharged-echoes"))
            {
                uint Body(string id)
                {
                    string obj=files.Single(f=>Path.GetFileNameWithoutExtension(f)==id);
                    using var skeleton=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(obj)!,id+".skeleton.json")));
                    return skeleton.RootElement.GetProperty("Refs").GetProperty("Cmdl").GetUInt32();
                }
                Check(Body("darksamus-echoes")!=Body("darksamus-overcharged-echoes"),"actual Overcharged source model differs from the normal body");
                var normalParts=ReadParts(Path.GetDirectoryName(files.Single(f=>Path.GetFileNameWithoutExtension(f)=="darksamus-echoes"))!,"darksamus-echoes");
                var chargedParts=ReadParts(Path.GetDirectoryName(files.Single(f=>Path.GetFileNameWithoutExtension(f)=="darksamus-overcharged-echoes"))!,"darksamus-overcharged-echoes");
                Check(normalParts.Parts.Single(p=>p.Id=="ball").Refs==chargedParts.Parts.Single(p=>p.Id=="ball").Refs
                    &&normalParts.Parts.Single(p=>p.Id=="body").Refs!=chargedParts.Parts.Single(p=>p.Id=="body").Refs,
                    "normal and Overcharged have different source bodies and the same exact source ball refs");
            }
            foreach(string id in ids.Where(id=>RetroCharacterPreviewExporter.BundleSpec(id).Version==3))
            {
                var spec=RetroCharacterPreviewExporter.BundleSpec(id);
                var before=LocalImportBundle.Inspect(root,id,spec);
                string folder=Path.GetDirectoryName(before.Files.Single(f=>Path.GetFileName(f)==id+".obj"))!;
                string missing=Path.Combine(folder,id+".ball.obj");
                var survivors=before.Files.Where(f=>f!=missing).ToDictionary(f=>f,f=>Convert.ToHexStringLower(HashFile(f)));
                string[] bodyNames={id+".obj",id+".skeleton.json",id+".weights.json",id+".motions.json",id+".cinf.bin",id+".animations.bin"};
                var bodyHashes=bodyNames.ToDictionary(n=>n,n=>Convert.ToHexStringLower(HashFile(Path.Combine(folder,n))));
                File.Delete(missing);
                var incomplete=LocalImportBundle.Inspect(root,id,spec);
                Check(incomplete.State==ImportBundleState.Incomplete,
                    "missing required ball file makes complete appearance Incomplete "+id);
                var partStatus=RetroCharacterPreviewExporter.InspectParts(id,incomplete);
                var bodyStatus=partStatus.Single(p=>p.Id=="body");var ballStatus=partStatus.Single(p=>p.Id=="ball");
                Check(bodyStatus.State==ImportBundleState.Ready&&bodyStatus.Components.All(c=>c.State==ImportBundleState.Ready)
                    &&ballStatus.State==ImportBundleState.Incomplete
                    &&ballStatus.Components.Single(c=>c.Role=="geometry").State==ImportBundleState.Incomplete
                    &&ballStatus.Components.Where(c=>c.Role!="geometry").All(c=>c.State==ImportBundleState.Ready),
                    "missing ball OBJ leaves real body and other five ball components Ready "+id);
                string description=RetroCharacterPreviewExporter.DescribeInspection(id,incomplete);
                Check(description.Contains("body v2: Ready",StringComparison.Ordinal)
                    &&description.Contains("ball v2: Incomplete (geometry v1 Incomplete)",StringComparison.Ordinal)
                    &&description.Contains("Authoring only",StringComparison.Ordinal),
                    "owned inspection identifies failed ball geometry version without hiding usable body authoring parts "+id);
                var repairedFiles=RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id});
                var repaired=LocalImportBundle.Inspect(root,id,spec);
                string repairedFolder=Path.GetDirectoryName(repairedFiles.Single())!;
                Check(repaired.State==ImportBundleState.Ready&&repaired.Receipt!.Version==3
                    &&repaired.Receipt.Generation!=before.Receipt!.Generation&&repaired.Files.Count==17,
                    "update repairs whole body/ball appearance into a fresh complete v3 generation "+id);
                Check(Directory.Exists(folder)&&!File.Exists(missing)
                    &&survivors.All(p=>File.Exists(p.Key)&&Convert.ToHexStringLower(HashFile(p.Key))==p.Value),
                    "repair retains old generation and every surviving old file without overwrite "+id);
                Check(bodyHashes.All(p=>Convert.ToHexStringLower(HashFile(Path.Combine(repairedFolder,p.Key)))==p.Value),
                    "repair preserves original body geometry rig weights sparse motions raw CINF and full archive bytes "+id);
                ValidateOwnedParts(id,repairedFolder,spec,repaired,resources!.Read,fullAnimationProof:false);
            }
            files=ids.Select(id=>LocalImportBundle.Inspect(root,id,RetroCharacterPreviewExporter.BundleSpec(id)).Files
                .Single(f=>Path.GetFileName(f)==id+".obj")).ToArray();
            var original = Snapshot(root);
            Check(RetroCharacterPreviewExporter.ExportFromOwnedGame(source, root).SequenceEqual(files), "repeat import reuses immutable generation paths");
            Check(Same(original, Snapshot(root)), "repeat import preserves receipt and generation bytes");
            using var cancellation = new CancellationTokenSource();
            bool beforeCommit = false;
            var progress = new SynchronousProgress(stage => { if (stage == ImportPublishStage.BeforeCommit) { beforeCommit = true; cancellation.Cancel(); } });
            Expect<OperationCanceledException>(() => RetroCharacterPreviewExporter.ExportFromOwnedGame(source, root, cancellation.Token, progress), "BeforeCommit cancellation");
            Check(beforeCommit && Same(original, Snapshot(root)), "BeforeCommit cancellation preserves all prior receipts/files");
            if(ids.Length>1)
            {
                using var partialCancellation=new CancellationTokenSource();
                int checks=0;
                var partialProgress=new SynchronousProgress(stage=>
                { if(stage==ImportPublishStage.Checking && ++checks==3)partialCancellation.Cancel(); });
                try
                {
                    RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,partialCancellation.Token,partialProgress);
                    throw new InvalidOperationException("Expected cancellation after first complete appearance");
                }
                catch(ImportBundleBatchCanceledException ex)
                { Check(ex.CommittedIds.SequenceEqual(new[]{ids[0]}) && ex.Message.Contains(ids[0]),"actual selected batch reports first complete appearance before cancellation"); }
                Check(Same(original,Snapshot(root)),"same-content partial update retains exact good generation bytes for both appearances");
            }
            foreach (string id in ids)
            {
                var inspected = LocalImportBundle.Inspect(root, id, RetroCharacterPreviewExporter.BundleSpec(id));
                Check(inspected.State == ImportBundleState.Ready, "cancel retains Ready " + id);
                var generation = inspected.Files.ToDictionary(f => f, f => Convert.ToHexStringLower(HashFile(f)));
                Check(LocalImportBundle.Remove(root, id) && LocalImportBundle.Inspect(root, id).State == ImportBundleState.NotInstalled,
                    "remove unpublishes canonical inventory " + id);
                Check(generation.All(p => File.Exists(p.Key) && Convert.ToHexStringLower(HashFile(p.Key)) == p.Value), "remove retains immutable generation bytes " + id);
                Check(!LocalImportBundle.Remove(root, id), "repeated remove is absent " + id);
            }
            Console.WriteLine("PASS: owned character bundles, immutable siblings, reuse, BeforeCommit cancellation, safe removal");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Check(!Directory.Exists(root), "all derived probe data cleaned");
            Console.WriteLine("Probe cleanup: all derived temporary data removed");
            byte[] sourceAfter=HashFile(source);
            Console.WriteLine("Owned source SHA256 after: "+Convert.ToHexStringLower(sourceAfter));
            Check(sourceBefore.SequenceEqual(sourceAfter),"owned source unchanged after update/cancel/remove or failed probe");
        }
    }
    private static void InspectPartsContract()
    {
        const string id="darksamus-echoes";
        var spec=RetroCharacterPreviewExporter.BundleSpec(id);
        var states=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,ImportBundleState.Ready,"verified fixture")).ToList();
        var ready=new ImportBundleInspection(ImportBundleState.Ready,"synthetic complete",null,Array.Empty<string>(),states);
        var parts=RetroCharacterPreviewExporter.InspectParts(id,ready);
        Check(parts.Select(p=>p.Id).SequenceEqual(new[]{"body","ball"})&&parts.All(p=>p.Version==2&&p.State==ImportBundleState.Ready
            &&p.Components.Select(c=>c.Role).SequenceEqual(new[]{"geometry","rig","weights","motion-samples","skeleton-source","animation-resources"})
            &&p.Components.All(c=>c.Version==1&&c.State==ImportBundleState.Ready)),
            "inspection exposes exact logical part and required role versions from verified component states");
        Expect<NotSupportedException>(()=>((IList<RetroCharacterPreviewExporter.AuthoringPartStatus>)parts)[0]=parts[1],
            "part inspection snapshot rejects caller replacement");
        Expect<NotSupportedException>(()=>((IList<RetroCharacterPreviewExporter.AuthoringComponentStatus>)parts[0].Components)[0]
            =new("tampered",99,ImportBundleState.Failed),"component inspection snapshot rejects caller replacement");
        states[0]=states[0] with { State=ImportBundleState.Failed };
        var changed=RetroCharacterPreviewExporter.InspectParts(id,ready);
        Check(parts[0].State==ImportBundleState.Ready&&parts[0].Components[0].State==ImportBundleState.Ready
            &&changed[0].State==ImportBundleState.Failed&&changed[0].Components[0].State==ImportBundleState.Failed,
            "published part and component states are detached from later source inventory edits");
        states[0]=states[0] with { State=ImportBundleState.Ready };
        states[^1]=states[^1] with { State=ImportBundleState.Incomplete };
        var missingIndex=ready with { State=ImportBundleState.Incomplete,Message="parts index missing" };
        Check(RetroCharacterPreviewExporter.InspectParts(id,missingIndex).All(p=>p.State==ImportBundleState.Ready),
            "missing shared parts index keeps both independently verified six-component authoring parts Ready");
        string indexDescription=RetroCharacterPreviewExporter.DescribeInspection(id,missingIndex);
        Check(indexDescription.Contains(id+": Incomplete.",StringComparison.Ordinal)
            &&indexDescription.Contains("body v2: Ready; ball v2: Ready",StringComparison.Ordinal),
            "description preserves global incomplete state while identifying verified logical parts");
        states[^1]=states[^1] with { State=ImportBundleState.Ready };
        states.Add(new(id+".ball.ancs-graph.json",ImportBundleState.Failed,"optional proof corrupt"));
        var optionalBad=ready with { State=ImportBundleState.Failed,Message="optional proof corrupt" };
        Check(RetroCharacterPreviewExporter.InspectParts(id,optionalBad).All(p=>p.State==ImportBundleState.Ready)
            &&RetroCharacterPreviewExporter.DescribeInspection(id,optionalBad).Contains(id+": Failed.",StringComparison.Ordinal),
            "optional proof integrity failure cannot claim global readiness or erase verified required part states");
        var legacyReceipt=new ImportBundleReceipt(1,id,1,new string('a',64),spec.SourceFingerprint,new string('b',64),
            spec.RequiredComponents.Take(4).ToArray(),Array.Empty<ImportBundleComponent>());
        var outdated=new ImportBundleInspection(ImportBundleState.Outdated,"legacy body recipe",legacyReceipt,Array.Empty<string>());
        var legacyParts=RetroCharacterPreviewExporter.InspectParts(id,outdated);
        Check(legacyParts.All(p=>p.State==ImportBundleState.Outdated&&p.Components.All(c=>c.State==ImportBundleState.Outdated))
            &&!RetroCharacterPreviewExporter.DescribeInspection(id,outdated).Contains(": Ready",StringComparison.Ordinal),
            "old v1 receipt without verified current component states never claims current body or ball Ready");
        Check(RetroCharacterPreviewExporter.InspectParts(id,ready with { ComponentStates=null })
            .All(p=>p.State==ImportBundleState.Incomplete&&p.Components.All(c=>c.State==ImportBundleState.Incomplete)),
            "aggregate Ready without component verification fails closed for logical authoring readiness");
        foreach(var missing in new[]{("skeleton-source",".cinf.bin"),("animation-resources",".animations.bin")})
        {
            var verified=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,
                file==id+".ball"+missing.Item2?ImportBundleState.Incomplete:ImportBundleState.Ready,"invented component state")).ToArray();
            var incomplete=ready with{State=ImportBundleState.Incomplete,ComponentStates=verified};
            var affected=RetroCharacterPreviewExporter.InspectParts(id,incomplete);
            Check(affected[0].State==ImportBundleState.Ready&&affected[1].Version==2&&affected[1].State==ImportBundleState.Incomplete
                &&affected[1].Components.Single(c=>c.Role==missing.Item1).State==ImportBundleState.Incomplete
                &&affected[1].Components.Where(c=>c.Role!=missing.Item1).All(c=>c.State==ImportBundleState.Ready),
                "missing required raw source component refuses complete part readiness while preserving verified siblings "+missing.Item1);
        }
        var oldV2=new ImportBundleReceipt(1,id,2,new string('c',64),spec.SourceFingerprint,new string('d',64),
            spec.RequiredComponents.Where(c=>!c.EndsWith(".cinf.bin",StringComparison.Ordinal)&&!c.EndsWith(".animations.bin",StringComparison.Ordinal)).ToArray(),Array.Empty<ImportBundleComponent>());
        Check(RetroCharacterPreviewExporter.InspectParts(id,new(ImportBundleState.Outdated,"old sparse-only v2",oldV2,Array.Empty<string>()))
            .All(p=>p.State==ImportBundleState.Outdated),"old body/ball v2 receipt cannot claim complete temporal-source v3 readiness");
    }
    private static RetroCharacterPreviewExporter.AuthoringPartsIndex ReadParts(string folder,string id)
        =>JsonSerializer.Deserialize<RetroCharacterPreviewExporter.AuthoringPartsIndex>(File.ReadAllBytes(Path.Combine(folder,id+".parts.json")))
            ??throw new InvalidDataException("Missing authoring parts index");
    private static void ValidateOwnedParts(string id,string folder,ImportBundleSpec spec,ImportBundleInspection inspected,Func<uint,byte[]> resource,bool fullAnimationProof)
    {
        Check(spec.RequiredComponents.Length==13&&inspected.Files.Count==17&&inspected.Receipt!.Components.Length==17
            &&inspected.Files.All(f=>Path.GetDirectoryName(f)==folder),"thirteen required body/ball components plus four graph files share one generation "+id);
        Check(RetroCharacterPreviewExporter.InspectParts(id,inspected).All(p=>p.State==ImportBundleState.Ready
            &&p.Version==2&&p.Components.Count==6&&p.Components.All(c=>c.State==ImportBundleState.Ready&&c.Version==1)),
            "actual verified body and ball report all six versioned source components Ready "+id);
        var index=ReadParts(folder,id);
        Check(index.Schema==1&&index.CharacterId==id&&index.SourceGameId=="G2ME01"&&index.BundleVersion==3&&!index.RuntimeInstall
            &&index.Parts.Select(p=>p.Id).SequenceEqual(new[]{"body","ball"})
            &&index.UnqualifiedParts.SequenceEqual(new[]{"source-materials","first-person","runtime-animation-and-morph"}),
            "parts index binds original appearance and explicit unqualified runtime work "+id);
        foreach(var part in index.Parts)
        {
            string stem=RetroCharacterPreviewExporter.PartStem(id,part.Id);
            Check(part.Version==2&&part.RequiredComponents.All(c=>c.Version==1)
                &&part.RequiredComponents.Select(c=>c.Role).SequenceEqual(new[]{"geometry","rig","weights","motion-samples","skeleton-source","animation-resources"})
                &&part.RequiredComponents.Select(c=>c.File).SequenceEqual(new[]{stem+".obj",stem+".skeleton.json",stem+".weights.json",stem+".motions.json",stem+".cinf.bin",stem+".animations.bin"})
                &&part.OptionalComponents.SequenceEqual(new[]{stem+".ancs-graph.json",stem+".ancs.bin"})
                &&part.RequiredComponents.Select(c=>c.File).Concat(part.OptionalComponents).All(n=>inspected.Files.Contains(Path.Combine(folder,n))),
                "part component versions exact body/ball filenames and optional raw graph siblings "+id+"/"+part.Id);
            ValidateAnimationArchive(id,part,folder,resource,fullAnimationProof);
        }
        var ball=index.Parts.Single(p=>p.Id=="ball");
        var exactRefs=new RetroCharacterRefs(0x2f2c1e6f,0x4403b1b9,0xc7dbc44a);
        Check(ball.Ancs==0x654a3f97&&ball.NativeName=="Dark_Samus_MorphBall"&&ball.Refs==exactRefs,
            "parts index keeps exact qualified alternate source refs "+id);
        using(var skeleton=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,id+".ball.skeleton.json"))))
        {
            var value=skeleton.RootElement;var refs=value.GetProperty("Refs");
            Check(value.GetProperty("Id").GetString()==id&&value.GetProperty("SourcePart").GetString()=="ball"
                &&value.GetProperty("SourceCharacter").GetString()==ball.NativeName
                &&value.GetProperty("BoneCount").GetInt32()==2&&value.GetProperty("Bones").GetArrayLength()==2
                &&refs.GetProperty("Cmdl").GetUInt32()==exactRefs.Cmdl&&refs.GetProperty("Cskr").GetUInt32()==exactRefs.Cskr
                &&refs.GetProperty("Cinf").GetUInt32()==exactRefs.Cinf,
                "ball skeleton binds canonical appearance own two bones and exact model rig refs "+id);
        }
        using(var weights=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,id+".ball.weights.json"))))
            Check(weights.RootElement.GetProperty("vertexCount").GetInt32()==1553
                &&weights.RootElement.GetProperty("vertexGroupIds").GetArrayLength()==1553,
                "ball skin retains all 1553 source positions "+id);
        string[] triangles=File.ReadLines(Path.Combine(folder,id+".ball.obj")).Where(line=>line.StartsWith("f ",StringComparison.Ordinal)).ToArray();
        Check(triangles.Length==2220&&triangles.All(line=>line.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length==4),
            "ball OBJ retains 2220 actual source triangles "+id);
        using(var motion=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,id+".ball.motions.json"))))
        {
            var value=motion.RootElement;var clips=value.GetProperty("Clips").EnumerateArray().ToArray();
            Check(value.GetProperty("TotalClips").GetInt32()==2&&value.GetProperty("SampledClips").GetInt32()==2&&clips.Length==2
                &&clips.All(c=>c.GetProperty("Status").GetString()=="sparse-source-pose-preview"
                    &&c.GetProperty("Samples").GetArrayLength()==5
                    &&c.GetProperty("Samples").EnumerateArray().All(p=>p.GetProperty("Bones").EnumerateArray()
                        .All(b=>b.EnumerateObject().All(property=>!property.Name.Contains("scale",StringComparison.OrdinalIgnoreCase)))))
                &&value.GetProperty("SourceGraphProof").GetProperty("SourceSnapshotFile").GetString()==id+".ball.ancs.bin",
                "ball keeps its own two sparse sampled clips raw proof reference and no invented scale channel "+id);
        }
        var proof=RetroAnimationGraphProof.Verify(File.ReadAllBytes(Path.Combine(folder,id+".ball.ancs-graph.json")),
            File.ReadAllBytes(Path.Combine(folder,id+".ball.ancs.bin")));
        Check(proof.SourcePart=="ball"&&proof.SourceCharacterId==id&&proof.SourceCharacterName==ball.NativeName
            &&proof.SourceAncsId==ball.Ancs&&proof.SourceSnapshotFile==id+".ball.ancs.bin"
            &&new RetroCharacterRefs(proof.Cmdl,proof.Cskr,proof.Cinf)==exactRefs
            &&!proof.NamedGraphSemanticsAccepted&&proof.UninterpretedSetBytesPreservedInSnapshot,
            "ball graph verifies exact canonical part source snapshot and model refs without runtime claim "+id);
    }
    private static RetroAnimationGraphProof.Capture ReadGraph(string folder,string stem)
    {
        byte[] raw=File.ReadAllBytes(Path.Combine(folder,stem+".ancs.bin"));
        byte[] descriptor=File.ReadAllBytes(Path.Combine(folder,stem+".ancs-graph.json"));
        return new(raw,descriptor,RetroAnimationGraphProof.Verify(descriptor,raw));
    }
    private static void ValidateAnimationArchive(string id,RetroCharacterPreviewExporter.AuthoringPart part,string folder,Func<uint,byte[]> resource,bool full)
    {
        string stem=RetroCharacterPreviewExporter.PartStem(id,part.Id),archivePath=Path.Combine(folder,stem+".animations.bin");
        var graph=ReadGraph(folder,stem);byte[] cinf=File.ReadAllBytes(Path.Combine(folder,stem+".cinf.bin")),bytes=File.ReadAllBytes(archivePath);
        string bankHash=Sha(bytes);var bank=Archive.Read(bytes,graph,cinf,echoes:true);
        uint[] expectedIds=graph.Metadata.Nodes.Where(n=>n.Type==0).Select(n=>n.AnimId).Distinct().Order().ToArray();
        Check(bank.SourceSha256==bankHash&&bank.ByteLength==bytes.Length&&bank.Resources.Select(r=>r.Id).SequenceEqual(expectedIds)
            &&bank.Roots.Count==graph.Metadata.Roots.Length&&bank.Roots.Count==(part.Id=="ball"?2:76),
            "raw archive retains all exact resource IDs and source root ordinals "+id+"/"+part.Id);
        foreach(var root in graph.Metadata.Roots)
        {
            var node=graph.Metadata.Nodes[root.Node];var actual=bank.Roots[root.Ordinal];
            bool strict=node.Type==0&&node.Kind=="play"&&node.Children.Length==0&&node.PlayTimeBits==0&&node.PlayTimeTypeRaw==2;
            Check(actual.Ordinal==root.Ordinal&&actual.Name==root.Name&&actual.Kind==node.Kind&&actual.StrictPlay==strict
                &&actual.ResourceId==(strict?node.AnimId:(uint?)null),"archive keeps exact source root kind and strict zero-time primitive semantics "+id+"/"+part.Id+"/"+root.Ordinal);
            if(strict)Check(bank.OpenStrictPrimitive(root.Ordinal).Source.Id==node.AnimId,"strict root resolves exact primitive resource identity");
            else Expect<NotSupportedException>(()=>bank.OpenStrictPrimitive(root.Ordinal),"composite or nonzero-time source root cannot become a primitive");
        }
        if(!full)return;
        Expect<NotSupportedException>(()=>((IList<Archive.Resource>)bank.Resources).Clear(),"archive resource metadata read-only");
        Expect<NotSupportedException>(()=>((IList<Archive.Root>)bank.Roots).Clear(),"archive root metadata read-only");
        // The snapshot owns bytes: subsequent caller mutations cannot retarget
        // full-resource loads, roots, raw graph identity or skeleton association.
        Array.Clear(bytes);Array.Clear(cinf);Array.Clear(graph.SourceSnapshot);Array.Clear(graph.Descriptor);
        Check(bank.SourceSha256==bankHash&&bank.Resources.Select(r=>r.Id).SequenceEqual(expectedIds),"archive snapshot detached from caller archive/graph/CINF arrays");
        long scalarChecks=0,keySamples=0,offGridSamples=0,rawKeys=0;int clips=0;
        using var poseDigest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var keyDigest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var entry in bank.Resources)
        {
            byte[] raw=resource(entry.Id);
            try
            {
                Require(raw.Length==entry.Bytes&&Sha(raw)==entry.Sha256,"Archive resource differs from original owned source bytes.");
                // Deliberately use the historical raw decoder, independently of
                // Archive.OpenResource's bounded decode/capture entry point.
                var expected=Anim.Load(raw,echoes:true);var clip=bank.OpenResource(entry.Id);clips++;
                Require(clip.Source==entry&&clip.KeyCount==expected.Keys&&clip.Duration==expected.Duration&&clip.Tick==expected.Tick
                    &&clip.RotationChannels==expected.Rot.Count&&clip.TranslationChannels==expected.Trans.Count&&clip.ScaleChannels==expected.Scale.Count,
                    "Complete temporal resource metadata differs from independent decoder.");
                AppendRawKeys(keyDigest,entry.Id,expected);rawKeys+=expected.Keys;
                var first=clip.Sample(0);string firstHash=PoseHash(first);var last=clip.Sample(clip.Duration);
                Require(PoseHash(first)==firstHash,"Later sample mutated captured first pose.");
                foreach(double t in Enumerable.Range(0,expected.Keys).Select(k=>k*expected.Tick))
                {ComparePose(clip.Sample(t),expected,t);keySamples++;}
                foreach(double t in new[]{expected.Tick*.375,expected.Duration*.413,expected.Duration+expected.Tick})
                {ComparePose(clip.Sample(t),expected,t);offGridSamples++;}
                Require(PoseHash(first)==firstHash&&PoseHash(last)==PoseHash(clip.Sample(clip.Duration)),"Full key/off-grid sampling mutated retained snapshots or endpoint determinism.");
                if(clips==1)
                {
                    Expect<ArgumentOutOfRangeException>(()=>clip.Sample(double.NaN),"nonfinite clip time refused");
                    Expect<NotSupportedException>(()=>((IDictionary<uint,IReadOnlyList<double>>)first.Rotations).Clear(),"pose channel map read-only");
                    var channel=first.Rotations.Values.Concat(first.Translations.Values).Concat(first.Scales.Values).FirstOrDefault();
                    if(channel!=null)Expect<NotSupportedException>(()=>((IList<double>)channel)[0]=123,"pose values read-only");
                }
                bool rigid=expected.Scale.Values.All(keys=>keys.All(k=>k.All(v=>Math.Abs(v-1)<=.00001)));
                Require(clip.RigidScale==rigid,"Nonidentity scale classification must not be silently discarded.");
            }
            finally{Array.Clear(raw);}
        }
        // A captured archive remains usable after its source component has
        // temporarily disappeared; the live bundle itself must fail closed.
        byte[] original=File.ReadAllBytes(archivePath);var pristineGraph=ReadGraph(folder,stem);byte[] pristineCinf=File.ReadAllBytes(Path.Combine(folder,stem+".cinf.bin"));
        try
        {
            var spec=RetroCharacterPreviewExporter.BundleSpec(id);File.Delete(archivePath);
            Require(LocalImportBundle.Inspect(FindBundleRoot(folder),id,spec).State==ImportBundleState.Incomplete,"Missing archive must withdraw complete bundle readiness.");
            Require(bank.OpenResource(expectedIds[0]).Source.Sha256==bank.Resources[0].Sha256,"Captured archive unexpectedly reread missing component.");
        }
        finally{File.WriteAllBytes(archivePath,original);}
        byte[] corrupt=original.ToArray();corrupt[^1]^=0x80;
        Expect<InvalidDataException>(()=>Archive.Read(corrupt,pristineGraph,pristineCinf,true),"changed raw resource byte fails archive digest");
        try
        {
            File.WriteAllBytes(archivePath,corrupt);
            Require(LocalImportBundle.Inspect(FindBundleRoot(folder),id,RetroCharacterPreviewExporter.BundleSpec(id)).State==ImportBundleState.Failed,
                "Same-length archive corruption must make published bundle Failed.");
            Require(bank.OpenResource(expectedIds[0]).Source.Sha256==bank.Resources[0].Sha256,"Captured immutable archive adopted later corrupted file.");
        }
        finally{File.WriteAllBytes(archivePath,original);}
        Require(LocalImportBundle.Inspect(FindBundleRoot(folder),id,RetroCharacterPreviewExporter.BundleSpec(id)).State==ImportBundleState.Ready,
            "Restoring exact private fixture bytes must reverify complete generation.");
        byte[] staleCinf=pristineCinf.ToArray();staleCinf[^1]^=1;
        Expect<InvalidDataException>(()=>Archive.Read(original,pristineGraph,staleCinf,true),"stale same-size CINF cannot retarget archive");
        string otherStem=RetroCharacterPreviewExporter.PartStem(id,part.Id=="body"?"ball":"body");
        var otherGraph=ReadGraph(folder,otherStem);byte[] otherCinf=File.ReadAllBytes(Path.Combine(folder,otherStem+".cinf.bin"));
        Expect<InvalidDataException>(()=>Archive.Read(original,otherGraph,otherCinf,true),"body/ball resource banks cannot cross part graph/skeleton identities");
        Expect<InvalidDataException>(()=>Archive.Read(original,pristineGraph,pristineCinf,false),"Echoes archive cannot adopt Prime decoder mode");
        int nonzeroTimeChecks=VerifyNonzeroPlayTime(id,part,pristineGraph,pristineCinf,resource);
        Console.WriteLine(JsonSerializer.Serialize(new{Kind="owned-complete-animation-source",CharacterId=id,Part=part.Id,
            ArchiveSha256=bank.SourceSha256,ArchiveBytes=bank.ByteLength,Resources=bank.Resources.Count,Roots=bank.Roots.Count,
            SourceCinfSha256=Sha(File.ReadAllBytes(Path.Combine(folder,stem+".cinf.bin"))), ResourceDigests=bank.Resources,
            LegacyInputs=new[]{".obj",".skeleton.json",".weights.json",".motions.json"}.ToDictionary(suffix=>stem+suffix,suffix=>Sha(File.ReadAllBytes(Path.Combine(folder,stem+suffix)))),
            CharacterDeclaredRoots=bank.Roots.Count(r=>r.CharacterDeclared),StrictPrimitiveRoots=bank.Roots.Count(r=>r.StrictPlay),
            UnsupportedRoots=bank.Roots.Count(r=>!r.StrictPlay),NonzeroPlayTimeRefusals=nonzeroTimeChecks,ClipsOpened=clips,
            RawKeyCount=rawKeys,KeyTimeSamples=keySamples,OffGridAndClampedSamples=offGridSamples,ScalarComparisons=scalarChecks,
            AllRawKeysSha256=Convert.ToHexStringLower(keyDigest.GetHashAndReset()),AllSampledPosesSha256=Convert.ToHexStringLower(poseDigest.GetHashAndReset()),
            SourceStringsExported=false,PoseArraysExported=false,PixelsOrModelPayloadsExported=false}));

        void ComparePose(Archive.Pose actual,Anim expected,double seconds)
        {
            Dictionary<uint,double[]> rotations,translations;
            if(expected.Keys==1){rotations=expected.Rot.ToDictionary(p=>p.Key,p=>p.Value[0]);translations=expected.Trans.ToDictionary(p=>p.Key,p=>p.Value[0]);}
            else expected.Sample(seconds,out rotations,out translations);
            CompareChannels(actual.Rotations,rotations);CompareChannels(actual.Translations,translations);
            // Scale remains explicit in source data. The raw decoder exposes
            // its full keys but intentionally has no public scale sampler.
            double time=Math.Clamp(seconds,0,expected.Duration);if(time>=1e-7)time-=1e-7;
            int lo=expected.Keys==1?0:Math.Min((int)(time/expected.Tick),expected.Keys-2);
            double amount=expected.Keys==1?0:time/expected.Tick>=expected.Keys-1?1:time%expected.Tick/expected.Tick;
            var scales=expected.Scale.ToDictionary(p=>p.Key,p=>p.Value[lo].Select((v,i)=>expected.Keys==1?v:v+(p.Value[lo+1][i]-v)*amount).ToArray());
            CompareChannels(actual.Scales,scales);poseDigest.AppendData(Convert.FromHexString(PoseHash(actual)));
        }
        void CompareChannels(IReadOnlyDictionary<uint,IReadOnlyList<double>> actual,Dictionary<uint,double[]> expected)
        {
            Require(actual.Keys.Order().SequenceEqual(expected.Keys.Order()),"Full pose channel IDs changed.");
            foreach(var (bone,values) in expected)
            {Require(actual[bone].Count==values.Length,"Pose component width changed.");for(int i=0;i<values.Length;i++)
                {scalarChecks++;Require(double.IsFinite(actual[bone][i])&&BitConverter.DoubleToInt64Bits(actual[bone][i])==BitConverter.DoubleToInt64Bits(values[i]),"Archive pose differs from original raw decoder.");}}
        }
    }
    private static int VerifyNonzeroPlayTime(string id,RetroCharacterPreviewExporter.AuthoringPart part,
        RetroAnimationGraphProof.Capture original,byte[] cinf,Func<uint,byte[]> resource)
    {
        var root=original.Metadata.Roots.First(r=>
        {var n=original.Metadata.Nodes[r.Node];return n.Type==0&&n.Children.Length==0&&n.PlayTimeBits==0&&n.PlayTimeTypeRaw==2;});
        var node=original.Metadata.Nodes[root.Node];byte[] changed=original.SourceSnapshot.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(changed.AsSpan(node.PlayParametersOffset,4),0x3f800000);
        var result=RetroAnimationGraphProof.CaptureOptional(changed,"G2ME01",part.Ancs,id,part.NativeName,part.Refs,part.Id=="body"?null:part.Id);
        var graph=result.Graph??throw new InvalidDataException("Modified raw play fixture lost animation source set.");
        byte[] archive=Archive.Build(graph,cinf,true,resource);var captured=Archive.Read(archive,graph,cinf,true);
        Check(!captured.Roots[root.Ordinal].StrictPlay,"raw nonzero-time play remains unqualified even with retained complete leaf resource");
        Expect<NotSupportedException>(()=>captured.OpenStrictPrimitive(root.Ordinal),"nonzero raw play-time cannot become zero-time primitive");
        Array.Clear(changed);Array.Clear(archive);return 1;
    }
    private static string FindBundleRoot(string generation)
    {
        var appearance=Directory.GetParent(generation)??throw new InvalidDataException("Missing appearance owner directory.");
        var imports=appearance.Parent??throw new InvalidDataException("Missing local-import owner directory.");
        return imports.Parent?.FullName??throw new InvalidDataException("Missing bundle root.");
    }
    private static void AppendRawKeys(IncrementalHash digest,uint resource,Anim anim)
    {
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
        {
            writer.Write(resource);writer.Write(anim.Keys);writer.Write(anim.Tick);writer.Write(anim.Duration);
            foreach(var table in new[]{anim.Rot,anim.Trans,anim.Scale})
            {writer.Write(table.Count);foreach(var (bone,keys) in table.OrderBy(p=>p.Key)){writer.Write(bone);writer.Write(keys.Count);foreach(var key in keys){writer.Write(key.Length);foreach(double value in key)writer.Write(value);}}}
        }
        digest.AppendData(bytes.GetBuffer().AsSpan(0,(int)bytes.Length));
    }
    private static string PoseHash(Archive.Pose pose)
    {
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
            foreach(var table in new[]{pose.Rotations,pose.Translations,pose.Scales})
            {writer.Write(table.Count);foreach(var (bone,values) in table.OrderBy(p=>p.Key)){writer.Write(bone);writer.Write(values.Count);foreach(double value in values)writer.Write(value);}}
        return Sha(bytes.GetBuffer().AsSpan(0,(int)bytes.Length));
    }
    private sealed class OwnedResources:IDisposable
    {
        private readonly GameCubeDiscReader disc;private readonly List<RetroPakReader> paks=new();
        internal OwnedResources(string path)
        {
            disc=GameCubeDiscReader.Open(path);
            try{foreach(var required in ExternalSourceRequirements.For(disc.Game))paks.Add(new(disc.OpenRead(required.Path),leaveOpen:false));}
            catch{Dispose();throw;}
        }
        internal byte[] Read(uint id)
        {foreach(var pak in paks)if(pak.Contains(id))return pak.ReadResource(id);throw new InvalidDataException("Original animation resource is absent from verified source archives.");}
        public void Dispose(){foreach(var pak in paks)pak.Dispose();disc.Dispose();}
    }
    private static void Require(bool value,string label){if(!value)throw new InvalidDataException(label);}
    private static string Sha(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class SynchronousProgress(Action<ImportPublishStage> report) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage value) => report(value); }
    private static byte[] HashFile(string file) { using var stream = File.OpenRead(file); return SHA256.HashData(stream); }
    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexStringLower(HashFile(f)));
    private static bool Same(Dictionary<string,string> a, Dictionary<string,string> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("Character bundle PASS: " + message); }
    private static void Expect<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
}
