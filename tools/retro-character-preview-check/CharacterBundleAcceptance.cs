using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Import;

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
            Check(spec.Version == (echoes?2:1) && spec.RecipeHash.Length == 64 && spec.SourceFingerprint.Length == 40,
                "canonical version and digest contract " + spec.Id);
            string[] body={spec.Id+".obj",spec.Id+".skeleton.json",spec.Id+".weights.json",spec.Id+".motions.json"};
            string[] required=echoes?body.Concat(body.Select(f=>spec.Id+".ball"+f[spec.Id.Length..])).Append(spec.Id+".parts.json").ToArray():body;
            Check(spec.RequiredComponents.SequenceEqual(required),
                "complete required body/ball inventory in one appearance contract " + spec.Id);
            var parts=RetroCharacterPreviewExporter.PartsFor(spec.Id);
            Check(parts.Select(p=>p.Id).SequenceEqual(echoes?new[]{"body","ball"}:new[]{"body"})
                &&parts.All(p=>p.Version==1)&&RetroCharacterPreviewExporter.PartStem(spec.Id,"body")==spec.Id,
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
                if(spec.Version==2) ValidateOwnedParts(id,folder,spec,inspected);
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
            foreach(string id in ids.Where(id=>RetroCharacterPreviewExporter.BundleSpec(id).Version==2))
            {
                var spec=RetroCharacterPreviewExporter.BundleSpec(id);
                var before=LocalImportBundle.Inspect(root,id,spec);
                string folder=Path.GetDirectoryName(before.Files.Single(f=>Path.GetFileName(f)==id+".obj"))!;
                string missing=Path.Combine(folder,id+".ball.obj");
                var survivors=before.Files.Where(f=>f!=missing).ToDictionary(f=>f,f=>Convert.ToHexStringLower(HashFile(f)));
                string[] bodyNames={id+".obj",id+".skeleton.json",id+".weights.json",id+".motions.json"};
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
                    "missing ball OBJ leaves real body and other three ball components Ready "+id);
                string description=RetroCharacterPreviewExporter.DescribeInspection(id,incomplete);
                Check(description.Contains("body v1: Ready",StringComparison.Ordinal)
                    &&description.Contains("ball v1: Incomplete (geometry v1 Incomplete)",StringComparison.Ordinal)
                    &&description.Contains("Authoring only",StringComparison.Ordinal),
                    "owned inspection identifies failed ball geometry version without hiding usable body authoring parts "+id);
                var repairedFiles=RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id});
                var repaired=LocalImportBundle.Inspect(root,id,spec);
                string repairedFolder=Path.GetDirectoryName(repairedFiles.Single())!;
                Check(repaired.State==ImportBundleState.Ready&&repaired.Receipt!.Version==2
                    &&repaired.Receipt.Generation!=before.Receipt!.Generation&&repaired.Files.Count==13,
                    "update repairs whole body/ball appearance into a fresh complete v2 generation "+id);
                Check(Directory.Exists(folder)&&!File.Exists(missing)
                    &&survivors.All(p=>File.Exists(p.Key)&&Convert.ToHexStringLower(HashFile(p.Key))==p.Value),
                    "repair retains old generation and every surviving old file without overwrite "+id);
                Check(bodyHashes.All(p=>Convert.ToHexStringLower(HashFile(Path.Combine(repairedFolder,p.Key)))==p.Value),
                    "repair preserves original body geometry rig weights and motions bytes "+id);
                ValidateOwnedParts(id,repairedFolder,spec,repaired);
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
            Check(File.Exists(source) && sourceBefore.SequenceEqual(HashFile(source)), "owned source unchanged after update/cancel/remove");
            Console.WriteLine("Owned source SHA256 after: " + Convert.ToHexStringLower(HashFile(source)));
            Console.WriteLine("PASS: owned character bundles, immutable siblings, reuse, BeforeCommit cancellation, safe removal");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Check(!Directory.Exists(root), "all derived probe data cleaned");
            Console.WriteLine("Probe cleanup: all derived temporary data removed");
        }
    }
    private static void InspectPartsContract()
    {
        const string id="darksamus-echoes";
        var spec=RetroCharacterPreviewExporter.BundleSpec(id);
        var states=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,ImportBundleState.Ready,"verified fixture")).ToList();
        var ready=new ImportBundleInspection(ImportBundleState.Ready,"synthetic complete",null,Array.Empty<string>(),states);
        var parts=RetroCharacterPreviewExporter.InspectParts(id,ready);
        Check(parts.Select(p=>p.Id).SequenceEqual(new[]{"body","ball"})&&parts.All(p=>p.Version==1&&p.State==ImportBundleState.Ready
            &&p.Components.Select(c=>c.Role).SequenceEqual(new[]{"geometry","rig","weights","motion-samples"})
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
            "missing shared parts index keeps both independently verified four-component authoring parts Ready");
        string indexDescription=RetroCharacterPreviewExporter.DescribeInspection(id,missingIndex);
        Check(indexDescription.Contains(id+": Incomplete.",StringComparison.Ordinal)
            &&indexDescription.Contains("body v1: Ready; ball v1: Ready",StringComparison.Ordinal),
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
    }
    private static RetroCharacterPreviewExporter.AuthoringPartsIndex ReadParts(string folder,string id)
        =>JsonSerializer.Deserialize<RetroCharacterPreviewExporter.AuthoringPartsIndex>(File.ReadAllBytes(Path.Combine(folder,id+".parts.json")))
            ??throw new InvalidDataException("Missing authoring parts index");
    private static void ValidateOwnedParts(string id,string folder,ImportBundleSpec spec,ImportBundleInspection inspected)
    {
        Check(spec.RequiredComponents.Length==9&&inspected.Files.Count==13&&inspected.Receipt!.Components.Length==13
            &&inspected.Files.All(f=>Path.GetDirectoryName(f)==folder),"nine required body/ball components plus four graph files share one generation "+id);
        Check(RetroCharacterPreviewExporter.InspectParts(id,inspected).All(p=>p.State==ImportBundleState.Ready
            &&p.Components.Count==4&&p.Components.All(c=>c.State==ImportBundleState.Ready&&c.Version==1)),
            "actual verified body and ball report all four versioned authoring components Ready "+id);
        var index=ReadParts(folder,id);
        Check(index.Schema==1&&index.CharacterId==id&&index.SourceGameId=="G2ME01"&&index.BundleVersion==2&&!index.RuntimeInstall
            &&index.Parts.Select(p=>p.Id).SequenceEqual(new[]{"body","ball"})
            &&index.UnqualifiedParts.SequenceEqual(new[]{"source-materials","first-person","runtime-animation-and-morph"}),
            "parts index binds original appearance and explicit unqualified runtime work "+id);
        foreach(var part in index.Parts)
        {
            string stem=RetroCharacterPreviewExporter.PartStem(id,part.Id);
            Check(part.Version==1&&part.RequiredComponents.All(c=>c.Version==1)
                &&part.RequiredComponents.Select(c=>c.Role).SequenceEqual(new[]{"geometry","rig","weights","motion-samples"})
                &&part.RequiredComponents.Select(c=>c.File).SequenceEqual(new[]{stem+".obj",stem+".skeleton.json",stem+".weights.json",stem+".motions.json"})
                &&part.OptionalComponents.SequenceEqual(new[]{stem+".ancs-graph.json",stem+".ancs.bin"})
                &&part.RequiredComponents.Select(c=>c.File).Concat(part.OptionalComponents).All(n=>inspected.Files.Contains(Path.Combine(folder,n))),
                "part component versions exact body/ball filenames and optional raw graph siblings "+id+"/"+part.Id);
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
    private sealed class SynchronousProgress(Action<ImportPublishStage> report) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage value) => report(value); }
    private static byte[] HashFile(string file) { using var stream = File.OpenRead(file); return SHA256.HashData(stream); }
    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexStringLower(HashFile(f)));
    private static bool Same(Dictionary<string,string> a, Dictionary<string,string> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("Character bundle PASS: " + message); }
    private static void Expect<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
}
