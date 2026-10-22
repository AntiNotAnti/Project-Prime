using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using Archive=MphRead.Mods.Import.RetroCharacterAnimationArchive;

// Metadata contracts run without source media. The explicit owned-source probe
// keeps every derived byte in one disposable root, never the real inventory.
internal static class CharacterBundleAcceptance
{
    private const string FinalId = "darksamus-overcharged-echoes";
    private const string NormalId = "darksamus-echoes";
    private static ImportBundleSpec PreviousNormalV6()
    {
        string[] body={".obj",".skeleton.json",".weights.json",".motions.json",".cinf.bin",".animations.bin",".source.glb",".source.json"};
        return new(NormalId,6,"c4a5604fe06270d7cc613423766f4912f6c36a834b05766cad840a995991b148",
            "e25524c84e498e3ee77d4717ac3a3c812fbc8bd7",body.Select(s=>NormalId+s)
                .Concat(new[]{NormalId+".materials.bin","characters.json",NormalId+".cannon-source.json"})
                .Concat(body.Select(s=>NormalId+".ball"+s)).Append(NormalId+".parts.json").ToArray());
    }
    private static RetroCharacterPreviewExporter.AuthoringPart HistoricalBallPart(RetroCharacterPreviewExporter.AuthoringPart part,string id)
        =>part.Id!="ball"?part:part with{Version=3,
            RequiredComponents=part.RequiredComponents.Where(c=>c.Role is not("animation-graph" or "animation-graph-source" or "source-materials" or "generated-composite-geometry" or "generated-composite-provenance")).ToArray(),
            OptionalComponents=new[]{id+".ball.ancs-graph.json",id+".ball.ancs.bin"}};
    private static byte[] HistoricalNormalManifest(byte[] current)
    {
        var manifest=System.Text.Json.Nodes.JsonNode.Parse(current)!.AsObject();manifest["id"]="generated-darksamus-echoes-diagnostic-v2";
        var models=manifest["models"]!.AsArray();
        for(int i=models.Count-1;i>=0;i--)if(models[i]!["part"]!.GetValue<string>()=="AlternateForm")models.RemoveAt(i);
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }
    private static ImportBundleSpec PreviousFinalV6()
    {
        string[] body = new[] { ".obj", ".skeleton.json", ".weights.json", ".motions.json", ".cinf.bin", ".animations.bin", ".source.glb", ".source.json" };
        return new(FinalId, 6, "a023e5c79a9ebdd3d6e654b756eeb47df227e39a9bb382410adac576f6c42a40",
            "e25524c84e498e3ee77d4717ac3a3c812fbc8bd7", body.Select(s => FinalId + s)
                .Concat(new[] { FinalId + ".materials.bin", "characters.json" })
                .Concat(body.Select(s => FinalId + ".ball" + s)).Append(FinalId + ".parts.json").ToArray());
    }
    // Literal prior production contract: independent of the new v8 component list.
    private static ImportBundleSpec PreviousFinalV7()
    {
        string[] body={".obj",".skeleton.json",".weights.json",".motions.json",".cinf.bin",".animations.bin",".source.glb",".source.json"};
        return new(FinalId,7,"4630d3cffd91885564b4b1a3c3cc51adcbb9b912e92a073d71c60f437bf7eaf2",
            "e25524c84e498e3ee77d4717ac3a3c812fbc8bd7",body.Select(s=>FinalId+s)
                .Concat(new[]{FinalId+".materials.bin","characters.json",FinalId+".cannon-source.json"})
                .Concat(body.Select(s=>FinalId+".ball"+s)).Append(FinalId+".parts.json").ToArray());
    }
    internal static void RunContract()
    {
        // The historical body/ball contract below is pinned to the four original
        // Troopers and two Echoes appearances. Scout Drones have a separate
        // revision-2-only authoring shape tested by external-import-check.
        var sourceIds = RetroCharacterPreviewExporter.Sources.Select(s => s.Id).ToArray();
        Check(sourceIds.Contains("trooper-drone-alpha") && sourceIds.Contains("trooper-drone-gamma")
            && sourceIds.Distinct(StringComparer.Ordinal).Count() == sourceIds.Length,
            "Scout Drone source identities remain distinct from legacy body bundles");
        var specs = RetroCharacterPreviewExporter.Sources
            .Where(s => s.Id is not ("trooper-drone-alpha" or "trooper-drone-gamma"))
            .Select(s => RetroCharacterPreviewExporter.BundleSpec(s.Id)).ToArray();
        foreach (var spec in specs)
        {
            var again = RetroCharacterPreviewExporter.BundleSpec(spec.Id);
            bool echoes=spec.Id.EndsWith("-echoes",StringComparison.Ordinal);
            Check(spec.Version == (echoes?(spec.Id==FinalId?8:7):1) && spec.RecipeHash.Length == 64 && spec.SourceFingerprint.Length == 40,
                "canonical version and digest contract " + spec.Id);
            string[] body={spec.Id+".obj",spec.Id+".skeleton.json",spec.Id+".weights.json",spec.Id+".motions.json"};
            string[] completeBody=body.Concat(new[]{spec.Id+".cinf.bin",spec.Id+".animations.bin",spec.Id+".source.glb",spec.Id+".source.json"}).ToArray();
            string[] required=echoes?completeBody.Append(spec.Id+".materials.bin").Concat(new[]{"characters.json",spec.Id+".cannon-source.json"})
                .Concat(completeBody.Select(f=>spec.Id+".ball"+f[spec.Id.Length..]))
                .Concat(new[]{spec.Id+".ball.ancs-graph.json",spec.Id+".ball.ancs.bin",spec.Id+".ball.materials.bin"})
                .Concat(spec.Id==FinalId?new[]{spec.Id+".ball.composite.glb",spec.Id+".ball.composite.json"}:Array.Empty<string>())
                .Append(spec.Id+".parts.json").ToArray():body;
            Check(spec.RequiredComponents.SequenceEqual(required),
                "complete required body/ball inventory in one appearance contract " + spec.Id);
            var parts=RetroCharacterPreviewExporter.PartsFor(spec.Id);
            Check(parts.Select(p=>p.Id).SequenceEqual(echoes?new[]{"body","ball"}:new[]{"body"})
                &&parts.All(p=>p.Version==(echoes?(p.Id=="body"?(spec.Id==FinalId?6:5):4):1))&&RetroCharacterPreviewExporter.PartStem(spec.Id,"body")==spec.Id,
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
        Check(normal.Version==7&&normal.RecipeHash=="e822d370a5d5d88aff978a901d82f9fe68c39673d7d5bdf2212e6f938447618f"
            &&overcharged.Version==8&&overcharged.RecipeHash=="3da58fd4a3fc2218d5fcfed5327aeec5735ecfecc40e976e2e2be5bf05932b32",
            "normal v7 remains frozen while Final composite v8 advances its independent recipe");
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
        InspectFinalCannonContract();
        InspectNormalBallContract();
        InspectFinalBallContract();
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
            using var resources=ids.Any(id=>RetroCharacterPreviewExporter.BundleSpec(id).Version>=4)?new OwnedResources(source):null;
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
                if(spec.Version>=4) ValidateOwnedParts(id,folder,spec,inspected,resources!.Read,fullAnimationProof:true);
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
                var materials=ids.Select(id=>RetroCharacterMaterialArchive.Read(File.ReadAllBytes(Path.Combine(
                    Path.GetDirectoryName(files.Single(f=>Path.GetFileNameWithoutExtension(f)==id))!,id+".materials.bin")))).ToArray();
                Check(materials.SelectMany(m=>m.Textures).Select(t=>t.ResourceId).Distinct().Count()==20,
                    "normal ten and Final twelve declared texture inventories retain the measured twenty-resource union");
            }
            foreach(string id in ids.Where(id=>RetroCharacterPreviewExporter.BundleSpec(id).Version>=4))
            {
                var spec=RetroCharacterPreviewExporter.BundleSpec(id);
                var before=LocalImportBundle.Inspect(root,id,spec);
                string folder=Path.GetDirectoryName(before.Files.Single(f=>Path.GetFileName(f)==id+".obj"))!;
                string missing=Path.Combine(folder,id+".ball.obj");
                var survivors=before.Files.Where(f=>f!=missing).ToDictionary(f=>f,f=>Convert.ToHexStringLower(HashFile(f)));
                string[] bodyNames={id+".obj",id+".skeleton.json",id+".weights.json",id+".motions.json",id+".cinf.bin",id+".animations.bin",id+".source.glb",id+".source.json",id+".materials.bin"};
                var bodyHashes=bodyNames.Concat(new[]{"characters.json",id+".cannon-source.json"}).ToDictionary(n=>n,n=>Convert.ToHexStringLower(HashFile(Path.Combine(folder,n))));
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
                    "missing ball OBJ leaves real body and every other required ball component Ready "+id);
                string description=RetroCharacterPreviewExporter.DescribeInspection(id,incomplete);
                Check(description.Contains("body v"+(id==FinalId?6:5)+": Ready",StringComparison.Ordinal)
                    &&description.Contains("ball v4"+": Incomplete (geometry v1 Incomplete)",StringComparison.Ordinal)
                    &&description.Contains("Choose an updated imported body in Hunters for local offline matches.",StringComparison.Ordinal)
                    &&description.Contains("Unsupported states and online matches use native presentation.",StringComparison.Ordinal),
                    "owned inspection identifies failed ball geometry version without hiding usable body authoring parts "+id);
                var repairedFiles=RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id});
                var repaired=LocalImportBundle.Inspect(root,id,spec);
                string repairedFolder=Path.GetDirectoryName(repairedFiles.Single())!;
                Check(repaired.State==ImportBundleState.Ready&&repaired.Receipt!.Version==spec.Version
                    &&repaired.Receipt.Generation!=before.Receipt!.Generation&&repaired.Files.Count==(id==NormalId?25:27),
                    "update repairs whole body/ball appearance into a fresh complete v"+spec.Version+" generation "+id);
                Check(Directory.Exists(folder)&&!File.Exists(missing)
                    &&survivors.All(p=>File.Exists(p.Key)&&Convert.ToHexStringLower(HashFile(p.Key))==p.Value),
                    "repair retains old generation and every surviving old file without overwrite "+id);
                Check(bodyHashes.All(p=>Convert.ToHexStringLower(HashFile(Path.Combine(repairedFolder,p.Key)))==p.Value),
                    "repair preserves original body geometry rig weights sparse motions raw CINF full archive and generated source GLB/provenance bytes "+id);
                ValidateOwnedParts(id,repairedFolder,spec,repaired,resources!.Read,fullAnimationProof:false);
            }
            foreach(string id in ids.Where(id=>RetroCharacterPreviewExporter.BundleSpec(id).Version>=4))
                RepairOwnedMaterials(source,root,id,resources!.Read);
            if(ids.Contains("darksamus-overcharged-echoes")) RepairOwnedFinalManifest(source,root,resources!.Read);
            if(ids.Contains(FinalId)) MigrateOwnedFinalCannon(source,root,resources!.Read);
            if(ids.Contains(NormalId)) MigrateOwnedNormalBall(source,root,resources!.Read);
            if(ids.Contains(FinalId)) MigrateOwnedFinalBall(source,root,resources!.Read);
            if(ids.Contains("darksamus-echoes")) RepairOwnedCannon(source,root,resources!.Read);
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
                string reinstalled=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single())!;
                var ready=LocalImportBundle.Inspect(root,id,RetroCharacterPreviewExporter.BundleSpec(id));
                Check(ready.State==ImportBundleState.Ready&&ready.Files.Count==generation.Count&&generation.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
                    &&ready.Files.All(f=>generation.Any(p=>Path.GetFileName(p.Key)==Path.GetFileName(f)&&p.Value==ShaFile(f))),
                    "reimport after remove retains old generation and exact complete source component bytes "+id);
                if(id.EndsWith("-echoes",StringComparison.Ordinal))
                    ValidateMaterialArchive(id,reinstalled,ReadParts(reinstalled,id).Parts.Single(p=>p.Id=="body"),RetroCharacterPreviewExporter.BundleSpec(id).SourceFingerprint,resources!.Read);
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
    private static void RepairOwnedMaterials(string source,string root,string id,Func<uint,byte[]> resource)
    {
        var spec=RetroCharacterPreviewExporter.BundleSpec(id);var before=LocalImportBundle.Inspect(root,id,spec);
        string material=id+".materials.bin",folder=Path.GetDirectoryName(before.Files.Single(f=>Path.GetFileName(f)==id+".obj"))!;
        byte[] bytes=File.ReadAllBytes(Path.Combine(folder,material));var captured=RetroCharacterMaterialArchive.Read(bytes);
        string originalHash=Sha(bytes);var survivors=before.Files.Where(f=>Path.GetFileName(f)!=material).ToDictionary(f=>f,ShaFile);
        File.Delete(Path.Combine(folder,material));var incomplete=LocalImportBundle.Inspect(root,id,spec);
        var status=RetroCharacterPreviewExporter.InspectParts(id,incomplete);
        Check(incomplete.State==ImportBundleState.Incomplete&&status.Single(p=>p.Id=="body").State==ImportBundleState.Incomplete
            &&status.Single(p=>p.Id=="body").Components.Single(c=>c.Role=="source-materials").State==ImportBundleState.Incomplete
            &&status.Single(p=>p.Id=="body").Components.Where(c=>c.Role!="source-materials").All(c=>c.State==ImportBundleState.Ready)
            &&status.Single(p=>p.Id=="ball").State==ImportBundleState.Ready,
            "missing required materials withdraws only complete body readiness while verified ball/siblings remain Ready "+id);
        Check(captured.ReadProgram(0).TexIds.SequenceEqual(captured.Programs[0].TextureIds),"detached material archive remains usable after its live file disappears "+id);
        string repairedFolder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single())!;
        var repaired=LocalImportBundle.Inspect(root,id,spec);
        Check(repaired.State==ImportBundleState.Ready&&repaired.Receipt!.Generation!=before.Receipt!.Generation
            &&ShaFile(Path.Combine(repairedFolder,material))==originalHash
            &&survivors.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value&&ShaFile(Path.Combine(repairedFolder,Path.GetFileName(p.Key)))==p.Value),
            "material repair creates a complete new generation and preserves all old/new geometry/provenance/cannon/ball sibling bytes "+id);

        int previousVersion=id=="darksamus-echoes"?5:4,previousBody=id=="darksamus-echoes"?4:3;
        string recipe=$"retro-character-preview-v{previousVersion}:{id}:Echoes:parts-index-v1:"
            +string.Join(":",RetroCharacterPreviewExporter.PartsFor(id).Select(p=>
                $"{p.Id}-v{(p.Id=="body"?previousBody:3)}:{p.Ancs:x8}:{p.NativeName}:geometry-v1-rig-v1-weights-v1-motion-samples-v1-cinf-v1-full-anim-bank-v1-generated-glb-v1-diagnostic-colors-v1-generated-provenance-v2-optional-part-graph"));
        if(id=="darksamus-echoes")recipe+=":normal-fixed-generated-cannon-v1-global-triangle-ids";
        var priorInventory=id==NormalId?PreviousNormalV6():PreviousFinalV7();
        var previous=priorInventory with{Version=previousVersion,RecipeHash=Sha(System.Text.Encoding.UTF8.GetBytes(recipe)),RequiredComponents=priorInventory.RequiredComponents.Where(n=>n!=material&&(id=="darksamus-echoes"||n!="characters.json"&&n!=id+".cannon-source.json")).ToArray()};
        var components=repaired.Files.Where(f=>Path.GetFileName(f)!=material&&Path.GetFileName(f)!=id+".ball.materials.bin"&&!Path.GetFileName(f).Contains(".ball.composite.",StringComparison.Ordinal)&&(id=="darksamus-echoes"||Path.GetFileName(f)!="characters.json"&&Path.GetFileName(f)!=id+".cannon-source.json")).ToDictionary(f=>Path.GetFileName(f)!,File.ReadAllBytes);
        if(id==NormalId)components["characters.json"]=HistoricalNormalManifest(components["characters.json"]);
        var index=ReadParts(repairedFolder,id);
        components[id+".parts.json"]=JsonSerializer.SerializeToUtf8Bytes(index with{BundleVersion=previousVersion,Parts=index.Parts.Select(p=>p.Id!="body"?HistoricalBallPart(p,id):p with{
            Version=previousBody,RequiredComponents=p.RequiredComponents.Where(c=>c.Role!="source-materials"&&(id=="darksamus-echoes"||c.Role!="source-preview-manifest"&&c.Role!="source-cannon-recipe"))
                .Select(c=>id==NormalId&&c.Role=="source-preview-manifest"?c with{Version=2}:c).ToArray()}).ToArray()});
        LocalImportBundle.Publish(root,previous,components,replaceAllComponents:true);
        var old=LocalImportBundle.Inspect(root,id,previous);var oldFiles=old.Files.ToDictionary(f=>f,ShaFile);
        Check(old.State==ImportBundleState.Ready&&old.Files.Count==(id=="darksamus-echoes"?23:21)
            &&RetroCharacterPreviewExporter.InspectParts(id,LocalImportBundle.Inspect(root,id,spec)).All(p=>p.State==ImportBundleState.Outdated),
            "previous complete normalv5/Finalv4 receipt is explicitly valid but Outdated for new material transport "+id);
        string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,id,"current.json"),pointerHash=ShaFile(pointer);bool stage=false;
        using(var cancellation=new CancellationTokenSource())
        {
            var progress=new SynchronousProgress(s=>{if(s==ImportPublishStage.BeforeCommit){stage=true;cancellation.Cancel();}});
            Expect<OperationCanceledException>(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,cancellation.Token,progress,new[]{id}),"material migration BeforeCommit cancellation "+id);
        }
        Check(stage&&ShaFile(pointer)==pointerHash&&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
            &&LocalImportBundle.Inspect(root,id,previous).State==ImportBundleState.Ready,
            "canceled new material migration retains prior complete receipt and every immutable source byte "+id);
        string migratedFolder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single())!;
        var migrated=LocalImportBundle.Inspect(root,id,spec);
        Check(migrated.State==ImportBundleState.Ready&&migrated.Receipt!.Version==spec.Version&&ShaFile(Path.Combine(migratedFolder,material))==originalHash
            &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),"material migration publishes current complete authoring bundle without deleting or overwriting old generation "+id);
        ValidateOwnedParts(id,migratedFolder,spec,migrated,resource,fullAnimationProof:false);
    }
    private static void RepairOwnedFinalManifest(string source,string root,Func<uint,byte[]> resource)
    {
        const string id="darksamus-overcharged-echoes";
        var spec=RetroCharacterPreviewExporter.BundleSpec(id);
        var current=LocalImportBundle.Inspect(root,id,spec);
        string folder=Path.GetDirectoryName(current.Files.Single(f=>Path.GetFileName(f)==id+".obj"))!;
        string manifestHash=ShaFile(Path.Combine(folder,"characters.json"));
        // Reconstruct the immediately preceding complete raw-material recipe:
        // Final v5/body4 had all body/ball source bytes, but no preview manifest.
        string recipe=$"retro-character-preview-v5:{id}:Echoes:parts-index-v1:"
            +string.Join(":",RetroCharacterPreviewExporter.PartsFor(id).Select(p=>
                $"{p.Id}-v{(p.Id=="body"?4:3)}:{p.Ancs:x8}:{p.NativeName}:geometry-v1-rig-v1-weights-v1-motion-samples-v1-cinf-v1-full-anim-bank-v1-generated-glb-v1-diagnostic-colors-v1-generated-provenance-v2-optional-part-graph"))
            +":body-raw-materials-txtr-authoring-v1-all-local-slots";
        var previous=spec with{Version=5,RecipeHash=Sha(System.Text.Encoding.UTF8.GetBytes(recipe)),
            RequiredComponents=PreviousFinalV7().RequiredComponents.Where(n=>n!="characters.json"&&n!=id+".cannon-source.json").ToArray()};
        var components=current.Files.Where(f=>Path.GetFileName(f)!="characters.json"&&Path.GetFileName(f)!=id+".cannon-source.json"&&Path.GetFileName(f)!=id+".ball.materials.bin"&&!Path.GetFileName(f).Contains(".ball.composite.",StringComparison.Ordinal))
            .ToDictionary(f=>Path.GetFileName(f)!,File.ReadAllBytes);
        var index=ReadParts(folder,id);
        components[id+".parts.json"]=JsonSerializer.SerializeToUtf8Bytes(index with{BundleVersion=5,Parts=index.Parts.Select(p=>p.Id!="body"?HistoricalBallPart(p,id):p with{
            Version=4,RequiredComponents=p.RequiredComponents.Where(c=>c.Role!="source-preview-manifest"&&c.Role!="source-cannon-recipe").ToArray()}).ToArray()});
        LocalImportBundle.Publish(root,previous,components,replaceAllComponents:true);
        var old=LocalImportBundle.Inspect(root,id,previous);
        var oldFiles=old.Files.ToDictionary(f=>f,ShaFile);
        Check(old.State==ImportBundleState.Ready&&previous.RequiredComponents.Length==18&&old.Files.Count==22
            &&LocalImportBundle.Inspect(root,id,spec).State==ImportBundleState.Outdated,
            "Final previous v5/body4 remains a complete exact eighteen-required/twenty-two-file material authoring inventory");
        string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,id,"current.json"),pointerHash=ShaFile(pointer);bool reached=false;
        using(var cancellation=new CancellationTokenSource())
        {
            var progress=new SynchronousProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit){reached=true;cancellation.Cancel();}});
            Expect<OperationCanceledException>(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,cancellation.Token,progress,new[]{id}),
                "Final manifest migration cancellation before publication");
        }
        Check(reached&&ShaFile(pointer)==pointerHash&&LocalImportBundle.Inspect(root,id,previous).State==ImportBundleState.Ready
            &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
            "canceled historical Final v5 to current v8 upgrade preserves old discovery and every old immutable byte");
        string migratedObj=RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single();
        string migratedFolder=Path.GetDirectoryName(migratedObj)!;
        var migrated=LocalImportBundle.Inspect(root,id,spec);
        Check(migrated.State==ImportBundleState.Ready&&migrated.Receipt!.Version==8&&spec.RequiredComponents.Length==25&&migrated.Files.Count==27
            &&migrated.Receipt.Generation!=old.Receipt!.Generation&&ShaFile(Path.Combine(migratedFolder,"characters.json"))==manifestHash
            &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
            &&oldFiles.Where(p=>Path.GetFileName(p.Key)!=id+".parts.json").All(p=>ShaFile(Path.Combine(migratedFolder,Path.GetFileName(p.Key)))==p.Value),
            "Final upgrade adds its own body/cannon manifest and recipe while retaining all source geometry rig animation material ball and graph bytes");
        ValidateOwnedParts(id,migratedFolder,spec,migrated,resource,fullAnimationProof:false);
        var stable=Snapshot(root);
        Check(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single()==migratedObj&&Same(stable,Snapshot(root)),
            "repeat Final v8 import reuses the same exact generation and complete manifest bytes");
    }
    private static void MigrateOwnedFinalCannon(string source,string root,Func<uint,byte[]> resource)
    {
        var spec=RetroCharacterPreviewExporter.BundleSpec(FinalId);
        string baseline=Environment.GetEnvironmentVariable("PRIME_FINAL_CANNON_V6_BASELINE_CORE")
            ??throw new InvalidDataException("Final migration needs an explicit frozen v6 core.");
        string baselineSha=ShaFile(baseline);
        Check(baselineSha=="9998e6e375f7b8de49aebeeb4a64bfe1fa4c5ab25e009e90a8bc1afafa121f4a",
            "Final migration uses exact independently frozen pre-cannon v6 exporter");
        var normal=LocalImportBundle.Inspect(root,"darksamus-echoes",RetroCharacterPreviewExporter.BundleSpec("darksamus-echoes"));
        var normalFiles=normal.Files.ToDictionary(f=>f,ShaFile);
        string normalPointer=Path.Combine(root,LocalImportBundle.StorageDirectory,"darksamus-echoes","current.json");
        string? normalPointerSha=File.Exists(normalPointer)?ShaFile(normalPointer):null;
        var context=new AssemblyLoadContext("final-cannon-v6-baseline-"+Guid.NewGuid().ToString("N"),true);
        try
        {
            var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(baseline));
            var exporter=assembly.GetType("MphRead.Mods.Import.RetroCharacterPreviewExporter",true)!;
            var outputs=(IReadOnlyList<string>)exporter.GetMethod("ExportFromOwnedGame",BindingFlags.Public|BindingFlags.Static)!
                .Invoke(null,new object?[]{source,root,CancellationToken.None,null,new[]{FinalId}})!;
            Check(outputs.Count==1,"frozen v6 exporter publishes only the actual Final body/ball generation");
        }
        finally{context.Unload();}
        var previous=PreviousFinalV6();var old=LocalImportBundle.Inspect(root,FinalId,previous);
        Check(old.State==ImportBundleState.Ready&&old.Receipt is{Version:6}&&old.Files.Count==23
            &&RetroCharacterPreviewExporter.InspectBundle(root,FinalId).State==ImportBundleState.Outdated,
            "actual frozen Final v6 remains explicitly valid while current v8 requires Update");
        string oldFolder=Path.GetDirectoryName(old.Files[0])!;
        var oldFiles=old.Files.ToDictionary(f=>f,ShaFile);
        string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,FinalId,"current.json"),pointerHash=ShaFile(pointer);
        using(var cancellation=new CancellationTokenSource())
        {
            bool reached=false;var progress=new SynchronousProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit){reached=true;cancellation.Cancel();}});
            Expect<OperationCanceledException>(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,cancellation.Token,progress,new[]{FinalId}),
                "actual historical Final v6 to current v8 BeforeCommit cancellation");
            Check(reached&&ShaFile(pointer)==pointerHash&&LocalImportBundle.Inspect(root,FinalId,previous).State==ImportBundleState.Ready
                &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
                "canceled Final cannon migration preserves old pointer and every immutable v6 component");
        }
        string folder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{FinalId}).Single())!;
        var next=LocalImportBundle.Inspect(root,FinalId,spec);
        var unchanged=old.Receipt!.Components.Where(c=>c.Id!="characters.json"&&c.Id!=FinalId+".parts.json").ToArray();
        Check(next.State==ImportBundleState.Ready&&next.Receipt is{Version:8}&&next.Files.Count==27&&unchanged.Length==21
            &&next.Receipt.Generation!=old.Receipt.Generation
            &&unchanged.All(c=>File.ReadAllBytes(Path.Combine(oldFolder,c.Id)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder,c.Id)))
                &&ShaFile(Path.Combine(folder,c.Id))==c.Sha256)
            &&new[]{"characters.json",FinalId+".parts.json"}.All(n=>ShaFile(Path.Combine(folder,n))!=old.Receipt.Components.Single(c=>c.Id==n).Sha256)
            &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
            "Final v8 retains all23 old v6 files, preserves21 raw components and adds cannon/composite authority");
        Check((normalPointerSha==null||ShaFile(normalPointer)==normalPointerSha)&&normalFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
            &&ShaFile(baseline)==baselineSha,"Final-only migration preserves normal discovery/components and frozen baseline binary");
        ValidateOwnedParts(FinalId,folder,spec,next,resource,fullAnimationProof:false);
    }
    private static void MigrateOwnedNormalBall(string source,string root,Func<uint,byte[]> resource)
    {
        var spec=RetroCharacterPreviewExporter.BundleSpec(NormalId);
        string baseline=Environment.GetEnvironmentVariable("PRIME_NORMAL_BALL_BASELINE_CORE")
            ??throw new InvalidDataException("Normal ball migration needs an explicit frozen pre-ball core.");
        string baselineSha=ShaFile(baseline);
        Check(baselineSha=="c49c53ddd0d5c10fd1a558b52a797b33410ad35ad8db02b3450f1fae2323e641",
            "normal ball migration uses exact independently frozen body/cannon exporter");
        var final=LocalImportBundle.Inspect(root,FinalId,RetroCharacterPreviewExporter.BundleSpec(FinalId));
        var finalFiles=final.Files.ToDictionary(f=>f,ShaFile);
        string finalPointer=Path.Combine(root,LocalImportBundle.StorageDirectory,FinalId,"current.json");
        string? finalPointerSha=File.Exists(finalPointer)?ShaFile(finalPointer):null;
        var context=new AssemblyLoadContext("normal-ball-v6-baseline-"+Guid.NewGuid().ToString("N"),true);
        try
        {
            var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(baseline));
            var exporter=assembly.GetType("MphRead.Mods.Import.RetroCharacterPreviewExporter",true)!;
            var outputs=(IReadOnlyList<string>)exporter.GetMethod("ExportFromOwnedGame",BindingFlags.Public|BindingFlags.Static)!
                .Invoke(null,new object?[]{source,root,CancellationToken.None,null,new[]{NormalId}})!;
            Check(outputs.Count==1,"frozen pre-ball exporter publishes only the actual normal v6 generation");
        }
        finally{context.Unload();}
        var previous=PreviousNormalV6();var old=LocalImportBundle.Inspect(root,NormalId,previous);
        Check(old.State==ImportBundleState.Ready&&old.Receipt is{Version:6}&&old.Files.Count==24
            &&RetroCharacterPreviewExporter.InspectBundle(root,NormalId).State==ImportBundleState.Outdated,
            "actual frozen normal v6 remains explicitly valid while ball4 requires Update");
        string oldFolder=Path.GetDirectoryName(old.Files[0])!;var oldFiles=old.Files.ToDictionary(f=>f,ShaFile);
        string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,NormalId,"current.json"),pointerHash=ShaFile(pointer);
        using(var cancellation=new CancellationTokenSource())
        {
            bool reached=false;var progress=new SynchronousProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit){reached=true;cancellation.Cancel();}});
            Expect<OperationCanceledException>(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,cancellation.Token,progress,new[]{NormalId}),
                "actual normal v6 to ball4 BeforeCommit cancellation");
            Check(reached&&ShaFile(pointer)==pointerHash&&LocalImportBundle.Inspect(root,NormalId,previous).State==ImportBundleState.Ready
                &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
                "canceled normal ball migration preserves old pointer and every immutable v6 component");
        }
        string folder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{NormalId}).Single())!;
        var next=LocalImportBundle.Inspect(root,NormalId,spec);
        var unchanged=old.Receipt!.Components.Where(c=>c.Id!="characters.json"&&c.Id!=NormalId+".parts.json").ToArray();
        Check(next.State==ImportBundleState.Ready&&next.Receipt is{Version:7}&&next.Files.Count==25&&spec.RequiredComponents.Length==23&&unchanged.Length==22
            &&next.Receipt.Generation!=old.Receipt.Generation
            &&unchanged.All(c=>File.ReadAllBytes(Path.Combine(oldFolder,c.Id)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder,c.Id)))&&ShaFile(Path.Combine(folder,c.Id))==c.Sha256)
            &&new[]{"characters.json",NormalId+".parts.json"}.All(n=>ShaFile(Path.Combine(folder,n))!=old.Receipt.Components.Single(c=>c.Id==n).Sha256)
            &&ShaFile(Path.Combine(folder,NormalId+".ball.materials.bin"))=="fadaf0787fa1c9df6b0cf5765db78547953908dc1b24c9b1cba76217c16746b5"
            &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
            "normal ball v7 retains all24 old files, preserves22 body/cannon/ball source components and changes only manifest/index plus new ball materials");
        Check((finalPointerSha==null||ShaFile(finalPointer)==finalPointerSha)&&finalFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
            &&ShaFile(baseline)==baselineSha,"normal ball-only migration preserves Final discovery/all components and frozen baseline binary");
        ValidateOwnedParts(NormalId,folder,spec,next,resource,fullAnimationProof:false);
    }
    private static void MigrateOwnedFinalBall(string source,string root,Func<uint,byte[]> resource)
    {
        var spec=RetroCharacterPreviewExporter.BundleSpec(FinalId);var previous=PreviousFinalV7();
        string baseline=Environment.GetEnvironmentVariable("PRIME_FINAL_BALL_BASELINE_CORE")
            ??throw new InvalidDataException("Final ball migration needs an explicit frozen v7 core.");
        string baselineSha=ShaFile(baseline);
        Check(baselineSha=="b43f8dd4bc065007a89a015d67fc902e1bc61c4b34984fe9d2503609348b3b70",
            "Final ball migration uses exact independently frozen actual Final7/normal7 exporter");
        var normalSpec=RetroCharacterPreviewExporter.BundleSpec(NormalId);var normal=LocalImportBundle.Inspect(root,NormalId,normalSpec);
        Check(normal.State==ImportBundleState.Ready&&normal.Receipt is{Version:7}&&normal.Files.Count==25,
            "Final-only migration starts with complete current normal7 all25 files");
        var normalFiles=normal.Files.ToDictionary(f=>f,ShaFile);string normalPointer=Path.Combine(root,LocalImportBundle.StorageDirectory,NormalId,"current.json"),normalPointerSha=ShaFile(normalPointer);
        var context=new AssemblyLoadContext("final-ball-v7-baseline-"+Guid.NewGuid().ToString("N"),true);
        try
        {
            var exporter=context.LoadFromAssemblyPath(Path.GetFullPath(baseline)).GetType("MphRead.Mods.Import.RetroCharacterPreviewExporter",true)!;
            var outputs=(IReadOnlyList<string>)exporter.GetMethod("ExportFromOwnedGame",BindingFlags.Public|BindingFlags.Static)!
                .Invoke(null,new object?[]{source,root,CancellationToken.None,null,new[]{FinalId}})!;
            Check(outputs.Count==1,"frozen b43 publishes only actual Final7 without a composite");
        }
        finally{context.Unload();}
        var old=LocalImportBundle.Inspect(root,FinalId,previous);
        Check(old.State==ImportBundleState.Ready&&old.Receipt is{Version:7}&&old.Files.Count==24
            &&old.Receipt.RecipeHash==previous.RecipeHash&&RetroCharacterPreviewExporter.InspectBundle(root,FinalId).State==ImportBundleState.Outdated,
            "actual frozen Final7 literal recipe remains explicitly Ready while composite8 requires Update");
        string oldFolder=Path.GetDirectoryName(old.Files[0])!;var oldFiles=old.Files.ToDictionary(f=>f,ShaFile);
        string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,FinalId,"current.json"),pointerHash=ShaFile(pointer);
        using(var cancellation=new CancellationTokenSource())
        {
            bool reached=false;var progress=new SynchronousProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit){reached=true;cancellation.Cancel();}});
            Expect<OperationCanceledException>(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,cancellation.Token,progress,new[]{FinalId}),
                "actual Final7 to composite8 BeforeCommit cancellation");
            Check(reached&&ShaFile(pointer)==pointerHash&&LocalImportBundle.Inspect(root,FinalId,previous).State==ImportBundleState.Ready
                &&oldFiles.Count==24&&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
                "canceled composite8 retains exact old Final7 pointer and all24 immutable files");
        }
        string folder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{FinalId}).Single())!;
        var next=LocalImportBundle.Inspect(root,FinalId,spec);var unchanged=old.Receipt!.Components.Where(c=>c.Id!="characters.json"&&c.Id!=FinalId+".parts.json").ToArray();
        Check(next.State==ImportBundleState.Ready&&next.Receipt is{Version:8}&&next.Files.Count==27&&spec.RequiredComponents.Length==25
            &&unchanged.Length==22&&next.Receipt.Generation!=old.Receipt.Generation
            &&unchanged.All(c=>ShaFile(Path.Combine(folder,c.Id))==c.Sha256&&File.ReadAllBytes(Path.Combine(oldFolder,c.Id)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder,c.Id))))
            &&new[]{"characters.json",FinalId+".parts.json"}.All(n=>ShaFile(Path.Combine(folder,n))!=old.Receipt.Components.Single(c=>c.Id==n).Sha256)
            &&oldFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value),
            "Final composite8 keeps all24 old files and22 exact old nonmanifest/index bytes while adding three owned components");
        var normalAfter=LocalImportBundle.Inspect(root,NormalId,normalSpec);
        Check(normalAfter.State==ImportBundleState.Ready&&normalAfter.Files.Count==25&&normalAfter.Receipt!.RecipeHash==normal.Receipt!.RecipeHash
            &&normalAfter.Receipt.Generation==normal.Receipt.Generation&&ShaFile(normalPointer)==normalPointerSha
            &&normalFiles.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)&&normalAfter.Files.All(f=>normalFiles.TryGetValue(f,out var sha)&&ShaFile(f)==sha)
            &&ShaFile(baseline)==baselineSha,"Final composite-only migration leaves normal7 recipe/generation/pointer/all25 bytes and frozen core unchanged");
        ValidateOwnedParts(FinalId,folder,spec,next,resource,fullAnimationProof:false);
    }
    private static void RepairOwnedCannon(string source,string root,Func<uint,byte[]> resource)
    {
        const string id="darksamus-echoes",cannonName=id+".cannon-source.json";
        var spec=RetroCharacterPreviewExporter.BundleSpec(id);
        var before=LocalImportBundle.Inspect(root,id,spec);
        string oldFolder=Path.GetDirectoryName(before.Files.Single(f=>Path.GetFileName(f)==id+".obj"))!;
        string missing=Path.Combine(oldFolder,cannonName);
        string cannonHash=Sha(File.ReadAllBytes(missing));
        var survivors=before.Files.Where(f=>f!=missing).ToDictionary(f=>f,ShaFile);
        File.Delete(missing);
        var incomplete=LocalImportBundle.Inspect(root,id,spec);
        var parts=RetroCharacterPreviewExporter.InspectParts(id,incomplete);
        Check(incomplete.State==ImportBundleState.Incomplete&&parts[0].State==ImportBundleState.Incomplete
            &&parts[0].Version==5&&parts[0].Components.Single(c=>c.Role=="source-cannon-recipe").State==ImportBundleState.Incomplete
            &&parts[0].Components.Where(c=>c.Role!="source-cannon-recipe").All(c=>c.State==ImportBundleState.Ready)
            &&parts[1].State==ImportBundleState.Ready
            &&RetroCharacterPreviewExporter.DescribeInspection(id,incomplete).Contains("body v5: Incomplete (source-cannon-recipe v1 Incomplete)",StringComparison.Ordinal),
            "owned missing cannon alone makes body Incomplete while verified body siblings and ball stay Ready");
        string repairedFolder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single())!;
        var repaired=LocalImportBundle.Inspect(root,id,spec);
        Check(repaired.State==ImportBundleState.Ready&&repaired.Receipt!.Version==7&&repaired.Files.Count==25
            &&spec.RequiredComponents.Length==23&&repaired.Receipt.RequiredComponents.Length==25&&repaired.Receipt.Generation!=before.Receipt!.Generation
            &&ShaFile(Path.Combine(repairedFolder,cannonName))==cannonHash,
            "owned missing cannon repair publishes fresh complete v7 generation with deterministic recipe and normal ball");
        Check(!File.Exists(missing)&&Directory.Exists(oldFolder)&&survivors.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
            &&survivors.All(p=>ShaFile(Path.Combine(repairedFolder,Path.GetFileName(p.Key)))==p.Value),
            "cannon repair retains damaged old generation and exact surviving old/new bytes");
        ValidateOwnedParts(id,repairedFolder,spec,repaired,resource,fullAnimationProof:false);

        // Reconstruct the prior body-only generated manifest/index in this
        // disposable inventory; no current/legacy production file is edited.
        var sourceCharacter=RetroCharacterPreviewExporter.Sources.Single(s=>s.Id==id);
        string legacyRecipe=$"retro-character-preview-v4:{id}:{sourceCharacter.Game}:parts-index-v1:"
            +string.Join(":",RetroCharacterPreviewExporter.PartsFor(id).Select(p=>
                $"{p.Id}-v3:{p.Ancs:x8}:{p.NativeName}:geometry-v1-rig-v1-weights-v1-motion-samples-v1-cinf-v1-full-anim-bank-v1-generated-glb-v1-diagnostic-colors-v1-generated-provenance-v2-optional-part-graph"));
        var historical=PreviousNormalV6();
        var oldSpec=historical with{Version=4,RecipeHash=Sha(System.Text.Encoding.UTF8.GetBytes(legacyRecipe)),RequiredComponents=historical.RequiredComponents.Where(n=>n!=cannonName&&n!=id+".materials.bin").ToArray()};
        var oldBytes=repaired.Files.Where(f=>Path.GetFileName(f)!=cannonName&&Path.GetFileName(f)!=id+".materials.bin"&&Path.GetFileName(f)!=id+".ball.materials.bin")
            .ToDictionary(f=>Path.GetFileName(f),File.ReadAllBytes);
        var manifest=System.Text.Json.Nodes.JsonNode.Parse(HistoricalNormalManifest(oldBytes["characters.json"]))!.AsObject();
        var models=manifest["models"]!.AsArray();models.RemoveAt(1);
        models[0]!.AsObject().Remove("sourceCannonReceipt");models[0]!.AsObject().Remove("sourceCannonSha256");
        manifest["id"]="generated-darksamus-echoes-diagnostic-v1";
        oldBytes["characters.json"]=JsonSerializer.SerializeToUtf8Bytes(manifest);
        var index=ReadParts(repairedFolder,id);
        oldBytes[id+".parts.json"]=JsonSerializer.SerializeToUtf8Bytes(index with{BundleVersion=4,Parts=index.Parts.Select(p=>p.Id!="body"?HistoricalBallPart(p,id):p with{
            Version=3,RequiredComponents=p.RequiredComponents.Where(c=>c.Role!="source-cannon-recipe"&&c.Role!="source-materials")
                .Select(c=>c.Role=="source-preview-manifest"?c with{Version=1}:c).ToArray()}).ToArray()});
        LocalImportBundle.Publish(root,oldSpec,oldBytes,replaceAllComponents:true);
        var old=LocalImportBundle.Inspect(root,id,oldSpec);
        Check(old.State==ImportBundleState.Ready&&old.Files.Count==22&&oldSpec.RequiredComponents.Length==18&&old.Receipt!.RequiredComponents.Length==22,
            "migration setup is a verified complete old body-only generated v4 inventory");
        var retained=old.Files.ToDictionary(f=>f,ShaFile);
        var outdated=LocalImportBundle.Inspect(root,id,spec);
        Check(outdated.State==ImportBundleState.Outdated&&RetroCharacterPreviewExporter.InspectParts(id,outdated).All(p=>p.State==ImportBundleState.Outdated)
            &&!RetroCharacterPreviewExporter.DescribeInspection(id,outdated).Contains(": Ready",StringComparison.Ordinal),
            "old generated receipt is Outdated before migration without current component-readiness claims");
        string migratedFolder=Path.GetDirectoryName(RetroCharacterPreviewExporter.ExportFromOwnedGame(source,root,selectedIds:new[]{id}).Single())!;
        var migrated=LocalImportBundle.Inspect(root,id,spec);
        Check(migrated.State==ImportBundleState.Ready&&migrated.Receipt!.Version==7&&migrated.Files.Count==25
            &&migrated.Receipt.Generation!=old.Receipt!.Generation&&retained.All(p=>File.Exists(p.Key)&&ShaFile(p.Key)==p.Value)
            &&ShaFile(Path.Combine(migratedFolder,cannonName))==cannonHash,
            "migration repairs old v4 into complete materials/cannon/normal-ball v7 without deleting or overwriting old generation");
        ValidateOwnedParts(id,migratedFolder,spec,migrated,resource,fullAnimationProof:false);
    }
    private static string ShaFile(string path)=>Convert.ToHexStringLower(HashFile(path));

    private static void InspectFinalCannonContract()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-final-cannon-contract-"+Guid.NewGuid().ToString("N"));
        try
        {
            var previous=PreviousFinalV6();var next=RetroCharacterPreviewExporter.BundleSpec(FinalId);
            Check(next.Version==8&&next.RequiredComponents.Length==25&&next.RecipeHash=="3da58fd4a3fc2218d5fcfed5327aeec5735ecfecc40e976e2e2be5bf05932b32"
                &&next.SourceFingerprint==previous.SourceFingerprint&&next.RecipeHash!=previous.RecipeHash,
                "Final-only v8 recipe has the exact new identity while retaining the original source fingerprint");
            var oldBytes=previous.RequiredComponents.ToDictionary(n=>n,n=>System.Text.Encoding.UTF8.GetBytes("invented old Final v6 "+n));
            LocalImportBundle.Publish(root,previous,oldBytes);
            var oldSnapshot=Snapshot(root);var old=RetroCharacterPreviewExporter.InspectBundle(root,FinalId);
            Check(old.State==ImportBundleState.Outdated&&old.Receipt is{Version:6}
                &&LocalImportBundle.Inspect(root,FinalId,previous).State==ImportBundleState.Ready&&Same(oldSnapshot,Snapshot(root)),
                "literal old Final v6 remains explicitly Ready and current inspection is nonmutating Outdated");
            string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,FinalId,"current.json"),pointerHash=ShaFile(pointer);
            var newBytes=next.RequiredComponents.ToDictionary(n=>n,n=>System.Text.Encoding.UTF8.GetBytes("invented new Final v8 "+n));
            using(var cancellation=new CancellationTokenSource())
            {
                bool reached=false;var progress=new SynchronousProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit){reached=true;cancellation.Cancel();}});
                Expect<OperationCanceledException>(()=>LocalImportBundle.Publish(root,next,newBytes,cancellation.Token,progress,replaceAllComponents:true),
                    "synthetic Final v8 publication honors BeforeCommit cancellation");
                Check(reached&&ShaFile(pointer)==pointerHash&&LocalImportBundle.Inspect(root,FinalId,previous).State==ImportBundleState.Ready
                    &&oldSnapshot.All(p=>File.Exists(Path.Combine(root,p.Key))&&ShaFile(Path.Combine(root,p.Key))==p.Value),
                    "synthetic canceled Final v8 preserves every old v6 component and pointer");
            }
            LocalImportBundle.Publish(root,next,newBytes,replaceAllComponents:true);
            var ready=RetroCharacterPreviewExporter.InspectBundle(root,FinalId);
            foreach(var missing in new[]{("source-cannon-recipe",FinalId+".cannon-source.json",1),("source-preview-manifest","characters.json",3)})
            {
                string path=ready.Files.Single(f=>Path.GetFileName(f)==missing.Item2);byte[] bytes=File.ReadAllBytes(path);File.Delete(path);
                try
                {
                    var incomplete=RetroCharacterPreviewExporter.InspectBundle(root,FinalId);var parts=RetroCharacterPreviewExporter.InspectParts(FinalId,incomplete);
                    var component=parts[0].Components.Single(c=>c.Role==missing.Item1);
                    Check(incomplete.State==ImportBundleState.Incomplete&&parts[0].Version==6&&parts[0].State==ImportBundleState.Incomplete
                        &&component.Version==missing.Item3&&component.State==ImportBundleState.Incomplete
                        &&parts[0].Components.Where(c=>c.Role!=missing.Item1).All(c=>c.State==ImportBundleState.Ready)
                        &&parts[1].Version==4&&parts[1].State==ImportBundleState.Ready,
                        "Final missing required companion affects only its exact versioned body role: "+missing.Item1);
                }
                finally{File.WriteAllBytes(path,bytes);}
                byte[] corrupt=(byte[])bytes.Clone();corrupt[0]^=1;File.WriteAllBytes(path,corrupt);
                Check(RetroCharacterPreviewExporter.InspectBundle(root,FinalId).State==ImportBundleState.Failed,
                    "Final changed required companion fails its digest: "+missing.Item1);File.WriteAllBytes(path,bytes);
            }
            Check(RetroCharacterPreviewExporter.InspectBundle(root,FinalId).State==ImportBundleState.Ready,"restored exact Final companions recover current Ready");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void InspectNormalBallContract()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-normal-ball-contract-"+Guid.NewGuid().ToString("N"));
        try
        {
            var previous=PreviousNormalV6();var next=RetroCharacterPreviewExporter.BundleSpec(NormalId);
            Check(previous.Version==6&&previous.RequiredComponents.Length==20&&next.Version==7&&next.RequiredComponents.Length==23
                &&next.SourceFingerprint==previous.SourceFingerprint&&next.RecipeHash!=previous.RecipeHash,
                "normal ball-only v7 keeps its frozen source and advances the exact previous v6 inventory");
            LocalImportBundle.Publish(root,previous,previous.RequiredComponents.ToDictionary(n=>n,n=>System.Text.Encoding.UTF8.GetBytes("invented normal v6 "+n)));
            var oldSnapshot=Snapshot(root);var old=RetroCharacterPreviewExporter.InspectBundle(root,NormalId);
            Check(old.State==ImportBundleState.Outdated&&old.Receipt is{Version:6}
                &&LocalImportBundle.Inspect(root,NormalId,previous).State==ImportBundleState.Ready&&Same(oldSnapshot,Snapshot(root)),
                "literal normal v6 stays explicitly Ready while current ball inspection is nonmutating Outdated");
            var bytes=next.RequiredComponents.ToDictionary(n=>n,n=>System.Text.Encoding.UTF8.GetBytes("invented normal ball4 "+n));
            LocalImportBundle.Publish(root,next,bytes,replaceAllComponents:true);
            var ready=RetroCharacterPreviewExporter.InspectBundle(root,NormalId);
            foreach(var missing in new[]{("source-materials",NormalId+".ball.materials.bin"),("animation-graph",NormalId+".ball.ancs-graph.json"),("animation-graph-source",NormalId+".ball.ancs.bin")})
            {
                string path=ready.Files.Single(f=>Path.GetFileName(f)==missing.Item2);byte[] original=File.ReadAllBytes(path);File.Delete(path);
                try
                {
                    var incomplete=RetroCharacterPreviewExporter.InspectBundle(root,NormalId);var parts=RetroCharacterPreviewExporter.InspectParts(NormalId,incomplete);
                    Check(incomplete.State==ImportBundleState.Incomplete&&parts[0].Version==5&&parts[0].State==ImportBundleState.Ready
                        &&parts[1].Version==4&&parts[1].State==ImportBundleState.Incomplete&&parts[1].Components.Count==11
                        &&parts[1].Components.Single(c=>c.Role==missing.Item1) is{Version:1,State:ImportBundleState.Incomplete}
                        &&parts[1].Components.Where(c=>c.Role!=missing.Item1).All(c=>c.State==ImportBundleState.Ready),
                        "missing required normal ball companion leaves body/cannon Ready and identifies ball4 role: "+missing.Item1);
                }
                finally{File.WriteAllBytes(path,original);}
                byte[] corrupt=(byte[])original.Clone();corrupt[0]^=1;File.WriteAllBytes(path,corrupt);
                Check(RetroCharacterPreviewExporter.InspectBundle(root,NormalId).State==ImportBundleState.Failed,
                    "changed required normal ball companion fails its exact digest: "+missing.Item1);File.WriteAllBytes(path,original);
            }
            Check(RetroCharacterPreviewExporter.InspectBundle(root,NormalId).State==ImportBundleState.Ready,
                "restoring all exact normal ball companions recovers complete Ready");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void InspectFinalBallContract()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-final-ball-contract-"+Guid.NewGuid().ToString("N"));
        try
        {
            var previous=PreviousFinalV7();var next=RetroCharacterPreviewExporter.BundleSpec(FinalId);
            Check(previous.Version==7&&previous.RequiredComponents.Length==20&&previous.RecipeHash=="4630d3cffd91885564b4b1a3c3cc51adcbb9b912e92a073d71c60f437bf7eaf2"
                &&next.Version==8&&next.RequiredComponents.Length==25&&next.SourceFingerprint==previous.SourceFingerprint&&next.RecipeHash!=previous.RecipeHash,
                "literal Final7 inventory and recipe remain frozen independently from Final composite8");
            LocalImportBundle.Publish(root,previous,previous.RequiredComponents.ToDictionary(n=>n,n=>System.Text.Encoding.UTF8.GetBytes("invented Final7 "+n)));
            var original=Snapshot(root);var old=RetroCharacterPreviewExporter.InspectBundle(root,FinalId);
            Check(old.State==ImportBundleState.Outdated&&old.Receipt is{Version:7}&&LocalImportBundle.Inspect(root,FinalId,previous).State==ImportBundleState.Ready
                &&Same(original,Snapshot(root)),"literal Final7 inspection is nonmutating Outdated while its historical contract stays Ready");
            string pointer=Path.Combine(root,LocalImportBundle.StorageDirectory,FinalId,"current.json"),pointerHash=ShaFile(pointer);
            var bytes=next.RequiredComponents.ToDictionary(n=>n,n=>System.Text.Encoding.UTF8.GetBytes("invented Final composite8 "+n));
            using(var cancellation=new CancellationTokenSource())
            {
                bool reached=false;var progress=new SynchronousProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit){reached=true;cancellation.Cancel();}});
                Expect<OperationCanceledException>(()=>LocalImportBundle.Publish(root,next,bytes,cancellation.Token,progress,replaceAllComponents:true),"synthetic Final7 to8 BeforeCommit cancellation");
                Check(reached&&ShaFile(pointer)==pointerHash&&LocalImportBundle.Inspect(root,FinalId,previous).State==ImportBundleState.Ready
                    &&original.All(p=>File.Exists(Path.Combine(root,p.Key))&&ShaFile(Path.Combine(root,p.Key))==p.Value),
                    "synthetic canceled composite8 retains literal Final7 bytes and pointer");
            }
            LocalImportBundle.Publish(root,next,bytes,replaceAllComponents:true);var ready=RetroCharacterPreviewExporter.InspectBundle(root,FinalId);
            foreach(var missing in new[]{("source-materials",FinalId+".ball.materials.bin"),("generated-composite-geometry",FinalId+".ball.composite.glb"),
                ("generated-composite-provenance",FinalId+".ball.composite.json"),("animation-graph",FinalId+".ball.ancs-graph.json"),("animation-graph-source",FinalId+".ball.ancs.bin")})
            {
                string path=ready.Files.Single(f=>Path.GetFileName(f)==missing.Item2);byte[] saved=File.ReadAllBytes(path);
                try
                {
                    File.Delete(path);var incomplete=RetroCharacterPreviewExporter.InspectBundle(root,FinalId);var parts=RetroCharacterPreviewExporter.InspectParts(FinalId,incomplete);
                    Check(incomplete.State==ImportBundleState.Incomplete&&parts[0].Version==6&&parts[0].State==ImportBundleState.Ready
                        &&parts[1].Version==4&&parts[1].State==ImportBundleState.Incomplete&&parts[1].Components.Count==13
                        &&parts[1].Components.Single(c=>c.Role==missing.Item1) is{Version:1,State:ImportBundleState.Incomplete}
                        &&parts[1].Components.Where(c=>c.Role!=missing.Item1).All(c=>c.State==ImportBundleState.Ready),
                        "missing Final composite companion isolates exact ball4 role while body/cannon stay Ready: "+missing.Item1);
                    var corrupt=saved.ToArray();corrupt[0]^=1;File.WriteAllBytes(path,corrupt);
                    Check(RetroCharacterPreviewExporter.InspectBundle(root,FinalId).State==ImportBundleState.Failed,"changed Final composite companion fails exact digest: "+missing.Item1);
                }
                finally{File.WriteAllBytes(path,saved);}
            }
            Check(RetroCharacterPreviewExporter.InspectBundle(root,FinalId).State==ImportBundleState.Ready,"all restored Final composite companions recover complete Ready");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void InspectPartsContract()
    {
        const string id="darksamus-echoes";
        var spec=RetroCharacterPreviewExporter.BundleSpec(id);
        var states=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,ImportBundleState.Ready,"verified fixture")).ToList();
        var ready=new ImportBundleInspection(ImportBundleState.Ready,"synthetic complete",null,Array.Empty<string>(),states);
        var parts=RetroCharacterPreviewExporter.InspectParts(id,ready);
        Check(parts.Select(p=>p.Id).SequenceEqual(new[]{"body","ball"})&&parts.All(p=>p.Version==(p.Id=="body"?5:4)&&p.State==ImportBundleState.Ready
            &&p.Components.Select(c=>c.Role).SequenceEqual(new[]{"geometry","rig","weights","motion-samples","skeleton-source","animation-resources","source-rig-geometry","generated-provenance"}
                .Concat(p.Id=="body"?new[]{"source-materials","source-preview-manifest","source-cannon-recipe"}:new[]{"animation-graph","animation-graph-source","source-materials"}))
            &&p.Components.All(c=>c.Version==(c.Role=="source-preview-manifest"?3:1)&&c.State==ImportBundleState.Ready)),
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
        int indexState=states.FindIndex(c=>c.Id==id+".parts.json");
        states[indexState]=states[indexState] with { State=ImportBundleState.Incomplete };
        var missingIndex=ready with { State=ImportBundleState.Incomplete,Message="parts index missing" };
        Check(RetroCharacterPreviewExporter.InspectParts(id,missingIndex).All(p=>p.State==ImportBundleState.Ready),
            "missing shared parts index keeps both independently verified complete authoring parts Ready");
        string indexDescription=RetroCharacterPreviewExporter.DescribeInspection(id,missingIndex);
        Check(indexDescription.Contains(id+": Incomplete.",StringComparison.Ordinal)
            &&indexDescription.Contains("body v5: Ready; ball v4: Ready",StringComparison.Ordinal),
            "description preserves global incomplete state while identifying verified logical parts");
        states[indexState]=states[indexState] with { State=ImportBundleState.Ready };
        states.Add(new(id+".ancs-graph.json",ImportBundleState.Failed,"optional proof corrupt"));
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
        foreach(var missing in new[]{("skeleton-source",".cinf.bin"),("animation-resources",".animations.bin"),("source-rig-geometry",".source.glb"),("generated-provenance",".source.json")})
        {
            var verified=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,
                file==id+".ball"+missing.Item2?ImportBundleState.Incomplete:ImportBundleState.Ready,"invented component state")).ToArray();
            var incomplete=ready with{State=ImportBundleState.Incomplete,ComponentStates=verified};
            var affected=RetroCharacterPreviewExporter.InspectParts(id,incomplete);
            Check(affected[0].State==ImportBundleState.Ready&&affected[1].Version==4&&affected[1].State==ImportBundleState.Incomplete
                &&affected[1].Components.Single(c=>c.Role==missing.Item1).State==ImportBundleState.Incomplete
                &&affected[1].Components.Where(c=>c.Role!=missing.Item1).All(c=>c.State==ImportBundleState.Ready),
                "missing required raw source component refuses complete part readiness while preserving verified siblings "+missing.Item1);
        }
        var historical=PreviousNormalV6();
        var oldV2=new ImportBundleReceipt(1,id,2,new string('c',64),spec.SourceFingerprint,new string('d',64),
            historical.RequiredComponents.Where(c=>!c.EndsWith(".cinf.bin",StringComparison.Ordinal)&&!c.EndsWith(".animations.bin",StringComparison.Ordinal)&&!c.EndsWith(".source.glb",StringComparison.Ordinal)&&!c.EndsWith(".source.json",StringComparison.Ordinal)&&c!="characters.json"&&c!=id+".cannon-source.json"&&c!=id+".materials.bin").ToArray(),Array.Empty<ImportBundleComponent>());
        Check(RetroCharacterPreviewExporter.InspectParts(id,new(ImportBundleState.Outdated,"old sparse-only v2",oldV2,Array.Empty<string>()))
            .All(p=>p.State==ImportBundleState.Outdated),"old body/ball v2 receipt cannot claim current generated-source readiness");
        var oldV3=new ImportBundleReceipt(1,id,3,new string('e',64),spec.SourceFingerprint,new string('f',64),
            historical.RequiredComponents.Where(c=>!c.EndsWith(".source.glb",StringComparison.Ordinal)&&!c.EndsWith(".source.json",StringComparison.Ordinal)&&c!="characters.json"&&c!=id+".cannon-source.json"&&c!=id+".materials.bin").ToArray(),Array.Empty<ImportBundleComponent>());
        Check(RetroCharacterPreviewExporter.InspectParts(id,new(ImportBundleState.Outdated,"old raw-source v3",oldV3,Array.Empty<string>()))
            .All(p=>p.State==ImportBundleState.Outdated),"old raw-source v3 receipt cannot claim current generated geometry/provenance readiness");
        var oldV4=oldV3 with{Version=4,RequiredComponents=historical.RequiredComponents.Where(c=>c!=id+".cannon-source.json"&&c!=id+".materials.bin").ToArray()};
        var oldGenerated=new ImportBundleInspection(ImportBundleState.Outdated,"old generated body-only v4",oldV4,Array.Empty<string>());
        Check(RetroCharacterPreviewExporter.InspectParts(id,oldGenerated).All(p=>p.State==ImportBundleState.Outdated
                &&p.Components.All(c=>c.State==ImportBundleState.Outdated))
            &&!RetroCharacterPreviewExporter.DescribeInspection(id,oldGenerated).Contains(": Ready",StringComparison.Ordinal),
            "old generated v4 receipt cannot claim current material/cannon/manifest readiness without component verification");
        var materialMissing=ready with{State=ImportBundleState.Incomplete,ComponentStates=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,
            file==id+".materials.bin"?ImportBundleState.Incomplete:ImportBundleState.Ready,"invented material archive state")).ToArray()};
        var materialParts=RetroCharacterPreviewExporter.InspectParts(id,materialMissing);
        Check(materialParts[0].Version==5&&materialParts[0].State==ImportBundleState.Incomplete
            &&materialParts[0].Components.Single(c=>c.Role=="source-materials").State==ImportBundleState.Incomplete
            &&materialParts[0].Components.Where(c=>c.Role!="source-materials").All(c=>c.State==ImportBundleState.Ready)
            &&materialParts[1].Version==4&&materialParts[1].State==ImportBundleState.Ready,
            "missing material authoring transport affects only its new required body role, not verified ball or runtime capabilities");
        var manifestMissing=ready with{State=ImportBundleState.Incomplete,ComponentStates=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,
            file=="characters.json"?ImportBundleState.Incomplete:ImportBundleState.Ready,"invented manifest state")).ToArray()};
        var manifestParts=RetroCharacterPreviewExporter.InspectParts(id,manifestMissing);
        Check(manifestParts[0].State==ImportBundleState.Incomplete&&manifestParts[0].Components.Single(c=>c.Role=="source-preview-manifest").Version==3
            &&manifestParts[0].Components.Single(c=>c.Role=="source-preview-manifest").State==ImportBundleState.Incomplete
            &&manifestParts[1].State==ImportBundleState.Ready,"missing normal preview manifest refuses complete body while ball remains independently Ready");
        var cannonMissing=ready with{State=ImportBundleState.Incomplete,ComponentStates=spec.RequiredComponents.Select(file=>new ImportBundleComponentInspection(file,
            file==id+".cannon-source.json"?ImportBundleState.Incomplete:ImportBundleState.Ready,"invented cannon state")).ToArray()};
        var cannonParts=RetroCharacterPreviewExporter.InspectParts(id,cannonMissing);
        Check(cannonParts[0].State==ImportBundleState.Incomplete&&cannonParts[0].Components.Single(c=>c.Role=="source-cannon-recipe").Version==1
            &&cannonParts[0].Components.Single(c=>c.Role=="source-cannon-recipe").State==ImportBundleState.Incomplete
            &&cannonParts[0].Components.Where(c=>c.Role!="source-cannon-recipe").All(c=>c.State==ImportBundleState.Ready)
            &&cannonParts[1].State==ImportBundleState.Ready
            &&RetroCharacterPreviewExporter.DescribeInspection(id,cannonMissing).Contains("body v5: Incomplete (source-cannon-recipe v1 Incomplete)",StringComparison.Ordinal),
            "missing cannon identifies only failed body recipe while manifest and ball remain independently Ready");
    }
    private static void ValidateSourceGlb(string id,RetroCharacterPreviewExporter.AuthoringPart part,string folder,Func<uint,byte[]> resource)
    {
        string stem=RetroCharacterPreviewExporter.PartStem(id,part.Id);
        byte[] cmdlBytes=resource(part.Refs.Cmdl),cskrBytes=resource(part.Refs.Cskr),cinfBytes=resource(part.Refs.Cinf);
        var model=Cmdl.Load(value=>value==part.Refs.Cmdl?cmdlBytes:value==part.Refs.Cskr?cskrBytes:resource(value),part.Refs.Cmdl,part.Refs.Cskr);
        var skeleton=RetroCharacterSource.ReadSkeleton(cinfBytes);
        byte[] glb=File.ReadAllBytes(Path.Combine(folder,stem+".source.glb"));
        Check(glb.AsSpan().SequenceEqual(RetroCharacterSourceGlbWriter.Write(model,skeleton))
            &&glb.AsSpan().SequenceEqual(RetroCharacterSourceGlbWriter.Write(model,skeleton)),
            "generated GLB byte-for-byte deterministic against independently reopened original CMDL/CSKR/CINF "+id+"/"+part.Id);
        using var doc=JsonDocument.Parse(glb.AsMemory(20,checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)))));
        var root=doc.RootElement;
        int binHeader=20+checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)));
        Check(BinaryPrimitives.ReadUInt32LittleEndian(glb)==0x46546c67&&BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4))==2
            &&BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8))==glb.Length
            &&BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16))==0x4e4f534a
            &&BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeader+4))==0x004e4942
            &&binHeader+8L+BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeader))==glb.Length,
            "GLB owns exact JSON/BIN chunks and no external source URI "+id+"/"+part.Id);
        byte[] binary=glb.AsSpan(binHeader+8).ToArray();
        var accessors=root.GetProperty("accessors");var views=root.GetProperty("bufferViews");
        (int Offset,int Count) Access(int ordinal,string type,int component,int elementBytes)
        {
            var a=accessors[ordinal];var view=views[a.GetProperty("bufferView").GetInt32()];
            int offset=view.GetProperty("byteOffset").GetInt32()+(a.TryGetProperty("byteOffset",out var skip)?skip.GetInt32():0);
            int count=a.GetProperty("count").GetInt32();
            Check(a.GetProperty("type").GetString()==type&&a.GetProperty("componentType").GetInt32()==component
                &&count>=0&&offset>=0&&offset+(long)count*elementBytes<=binary.Length
                &&view.GetProperty("byteLength").GetInt32()==count*elementBytes,
                "bounded exact accessor shape "+id+"/"+part.Id);
            return(offset,count);
        }
        bool FloatBits(int offset,float expected)=>BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(offset,4))==BitConverter.SingleToInt32Bits(expected);
        var bones=skeleton.Bones.Values.OrderBy(b=>b.Id).ToArray();
        var joints=bones.Select((b,i)=>(b.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        var skin=root.GetProperty("skins")[0];var nodes=root.GetProperty("nodes");
        Check(bones.Length==(part.Id=="body"?39:2)&&skin.GetProperty("joints").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(Enumerable.Range(0,bones.Length))
            &&nodes.GetArrayLength()==bones.Length+1,"complete source joint inventory retained without pruning "+id+"/"+part.Id);
        var inverse=Access(skin.GetProperty("inverseBindMatrices").GetInt32(),"MAT4",5126,64);
        Check(inverse.Count==bones.Length,"every source joint retains an inverse bind "+id+"/"+part.Id);
        bool exactRig=true;
        for(int i=0;i<bones.Length;i++)
        {
            var b=bones[i];float x=b.X,y=b.Y,z=b.Z;
            if(skeleton.Bones.TryGetValue(b.Parent,out var parent)){x-=parent.X;y-=parent.Y;z-=parent.Z;}
            float[] expected={-x,z,y};var node=nodes[i];
            exactRig&=node.GetProperty("name").GetString()=="source_bone_"+b.Id+"_"+b.Name
                &&node.GetProperty("children").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(bones.Where(c=>c.Parent==b.Id).Select(c=>joints[c.Id]));
            for(int axis=0;axis<3;axis++)exactRig&=BitConverter.SingleToInt32Bits(node.GetProperty("translation")[axis].GetSingle())==BitConverter.SingleToInt32Bits(expected[axis]);
            for(int k=0;k<16;k++)
            {
                float value=k is 0 or 5 or 10 or 15?1:k==12?b.X:k==13?-b.Z:k==14?-b.Y:0;
                exactRig&=FloatBits(inverse.Offset+i*64+k*4,value);
            }
        }
        Check(exactRig,"exact source hierarchy/local binds and inverse-bind bits survive source basis conversion "+id+"/"+part.Id);
        var materialDefinitions=root.GetProperty("materials");
        Check(materialDefinitions.GetArrayLength()==model.Materials.Length
            &&materialDefinitions.EnumerateArray().Select(m=>m.GetProperty("extras").GetProperty("sourceMaterialOrdinal").GetInt32()).SequenceEqual(Enumerable.Range(0,model.Materials.Length))
            &&materialDefinitions.EnumerateArray().All(m=>m.GetProperty("extras").GetProperty("materialPolicy").GetString()==RetroCharacterSourceGlbWriter.MaterialPolicy),
            "all original source material identities retained under explicit diagnostic-color policy "+id+"/"+part.Id);
        var seen=new HashSet<int>();bool cornersMatch=true;
        var primitives=root.GetProperty("meshes")[0].GetProperty("primitives");
        Check(primitives.GetArrayLength()==model.TriangleMaterial.Distinct().Count(),"one primitive per populated original source material "+id+"/"+part.Id);
        foreach(var primitive in primitives.EnumerateArray())
        {
            int material=primitive.GetProperty("material").GetInt32();
            int[] faces=primitive.GetProperty("extras").GetProperty("sourceTriangleIds").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            Check(material>=0&&material<model.Materials.Length&&primitive.GetProperty("extras").GetProperty("sourceMaterialOrdinal").GetInt32()==material
                &&faces.SequenceEqual(Enumerable.Range(0,model.TriangleCount).Where(f=>model.TriangleMaterial[f]==material))
                &&faces.All(f=>seen.Add(f)),"source material/triangle IDs cover original faces once in source order "+id+"/"+part.Id);
            var attrs=primitive.GetProperty("attributes");
            var p=Access(attrs.GetProperty("POSITION").GetInt32(),"VEC3",5126,12);var n=Access(attrs.GetProperty("NORMAL").GetInt32(),"VEC3",5126,12);
            var uv=Access(attrs.GetProperty("TEXCOORD_0").GetInt32(),"VEC2",5126,8);var j=Access(attrs.GetProperty("JOINTS_0").GetInt32(),"VEC4",5123,8);
            var weight=Access(attrs.GetProperty("WEIGHTS_0").GetInt32(),"VEC4",5126,16);var indices=Access(primitive.GetProperty("indices").GetInt32(),"SCALAR",5125,4);
            Check(new[]{p.Count,n.Count,uv.Count,j.Count,weight.Count,indices.Count}.All(c=>c==faces.Length*3),"all source corners retain complete per-corner attributes "+id+"/"+part.Id);
            float[]? sourceUv=(model.Materials[material].Flags&Cmdl.ShortUv)!=0?model.ShortUvs:model.Uv0;
            int index=0;
            foreach(int face in faces)for(int c=0;c<3;c++,index++)
            {
                var corner=model.Triangles[face*3+c];
                cornersMatch&=FloatBits(p.Offset+index*12,-model.Positions[corner.P*3])&&FloatBits(p.Offset+index*12+4,model.Positions[corner.P*3+2])&&FloatBits(p.Offset+index*12+8,model.Positions[corner.P*3+1])
                    &&FloatBits(n.Offset+index*12,-model.Normals[corner.N*3])&&FloatBits(n.Offset+index*12+4,model.Normals[corner.N*3+2])&&FloatBits(n.Offset+index*12+8,model.Normals[corner.N*3+1])
                    &&FloatBits(uv.Offset+index*8,corner.T0<0?0:sourceUv![corner.T0*2])&&FloatBits(uv.Offset+index*8+4,corner.T0<0?0:sourceUv![corner.T0*2+1])
                    &&BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(indices.Offset+index*4,4))==index;
                var influences=model.Skin![corner.P].Where(v=>v.Weight>0).ToArray();
                for(int k=0;k<4;k++)cornersMatch&=BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(j.Offset+index*8+k*2,2))==(k<influences.Length?joints[influences[k].Bone]:0)
                    &&FloatBits(weight.Offset+index*16+k*4,k<influences.Length?influences[k].Weight:0);
            }
        }
        Check(cornersMatch&&seen.Count==model.TriangleCount,"every source corner retains exact positions/normals/UV/weights/joints and original winding "+id+"/"+part.Id);
        using var provenance=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,stem+".source.json")));
        var proof=provenance.RootElement;
        Check(proof.GetProperty("format").GetInt32()==2&&proof.GetProperty("writerVersion").GetInt32()==RetroCharacterSourceGlbWriter.Version
            &&proof.GetProperty("sourceCharacterId").GetString()==id&&proof.GetProperty("sourceCharacter").GetString()==id
            &&proof.GetProperty("sourceNativeName").GetString()==part.NativeName&&proof.GetProperty("sourcePart").GetString()==part.Id
            &&proof.GetProperty("sourceCmdl").GetUInt32()==part.Refs.Cmdl&&proof.GetProperty("sourceCskr").GetUInt32()==part.Refs.Cskr&&proof.GetProperty("sourceCinf").GetUInt32()==part.Refs.Cinf
            &&proof.GetProperty("sourceCmdlSha256").GetString()==Sha(cmdlBytes)&&proof.GetProperty("sourceCskrSha256").GetString()==Sha(cskrBytes)&&proof.GetProperty("sourceCinfSha256").GetString()==Sha(cinfBytes)
            &&proof.GetProperty("sourceAncsSha256").GetString()==Sha(resource(part.Ancs))&&proof.GetProperty("sourceGlbSha256").GetString()==Sha(glb)
            &&proof.GetProperty("sourceAnimationArchiveSha256").GetString()==Sha(File.ReadAllBytes(Path.Combine(folder,stem+".animations.bin")))
            &&proof.GetProperty("materialPolicy").GetString()==RetroCharacterSourceGlbWriter.MaterialPolicy
            &&new[]{"approvedForGameplay","multiplayerEnabled","playerSelectionEnabled","firstPersonVerified","alternateFormVerified","sourceMotionRetargetVerified","devicePerformanceVerified"}.All(flag=>!proof.GetProperty(flag).GetBoolean()),
            "generated provenance binds exact source resources and GLB without gameplay/material-parity qualification "+id+"/"+part.Id);
        Console.WriteLine(JsonSerializer.Serialize(new{Kind="owned-source-glb-proof",Character=id,Part=part.Id,SourceCmdlSha256=Sha(cmdlBytes),SourceCskrSha256=Sha(cskrBytes),GlbSha256=Sha(glb),AllCornersVerified=true,FullJointInventoryVerified=true,SourceMaterialIdsVerified=true}));
    }
    private static RetroCharacterPreviewExporter.AuthoringPartsIndex ReadParts(string folder,string id)
        =>JsonSerializer.Deserialize<RetroCharacterPreviewExporter.AuthoringPartsIndex>(File.ReadAllBytes(Path.Combine(folder,id+".parts.json")))
            ??throw new InvalidDataException("Missing authoring parts index");
    private static void ValidateFinalComposite(string folder)
    {
        string stem=FinalId+".ball",geometry=Path.Combine(folder,stem+".composite.glb"),receiptPath=Path.Combine(folder,stem+".composite.json");
        Check(ShaFile(geometry)=="57158cb41d062bab7b93920016855c8fd7a8b899a48c8dde23e4838e4b95beab"
            &&ShaFile(receiptPath)=="8eba953c52a647a6e4c8bea6c66b289bd5f4bc95abe692782959248522bb4a55",
            "Final composite GLB/provenance reproduce independently bootstrapped whole-byte identities");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(receiptPath));var receipt=doc.RootElement;var selection=receipt.GetProperty("selection");
        Check(receipt.GetProperty("format").GetInt32()==2&&receipt.GetProperty("kind").GetString()=="generated-final-ball-composite-v1"
            &&receipt.GetProperty("sourceCharacter").GetString()==FinalId&&receipt.GetProperty("sourcePart").GetString()=="ball-composite"
            &&receipt.GetProperty("sourceTriangleCount").GetInt32()==8170&&receipt.GetProperty("sourceMaterialCount").GetInt32()==6
            &&receipt.GetProperty("sourceBoneCount").GetInt32()==2&&receipt.GetProperty("staticSourceBake").GetBoolean()
            &&receipt.GetProperty("nativeRollAndCollision").GetBoolean()&&receipt.GetProperty("visualOnly").GetBoolean()
            &&new[]{"gameplayChanged","online","replay"}.All(n=>!receipt.GetProperty(n).GetBoolean())
            &&receipt.GetProperty("sourceGlbSha256").GetString()==ShaFile(geometry)
            &&receipt.GetProperty("materialArchiveSha256").GetString()==ShaFile(Path.Combine(folder,stem+".materials.bin")),
            "Final composite explicitly declares derived static8170/6materials/2rig presentation, native motion and no gameplay/network claims");
        Check(selection.GetProperty("policy").GetString()=="final-vein-ball-v1-source52-time0-shell-vent-vessel-line-heart"
            &&selection.GetProperty("clipOrdinal").GetInt32()==52&&selection.GetProperty("clipResourceId").GetUInt32()==0xc2c4a405
            &&selection.GetProperty("clipSha256").GetString()=="c29325b7b5ae5eb0efc3ae336d9e5a1d260a3aeef57745fe2b299851f2c0a872"
            &&selection.GetProperty("clipSeconds").GetDouble()==0&&selection.GetProperty("foldedFromCompleteSourceRig").GetInt32()==39
            &&selection.GetProperty("staticCompositeBallRig").GetInt32()==2&&!selection.GetProperty("sourcePhysicsChanged").GetBoolean(),
            "Final composite binds independent original full39 source pose52 exactly at phase0 before declared static bake");
        foreach(var part in new[]{("shell",2220,"64b59658ea98061c2a8347c1e1fe12e3c40871b2e003b2b51b001518c2a4af70"),
            ("vent",2220,"64b59658ea98061c2a8347c1e1fe12e3c40871b2e003b2b51b001518c2a4af70"),
            ("vessel",822,"3a71a321cdc51f12a67c5384f23b1388b764ac99a3ce3ac7ca52a241787d56f1"),
            ("line",2820,"edccb1dbe70f626690e0d522c8f3ffcca4c7e9ebb1dca74fc623f182188c0d06"),
            ("heart",88,"628379186fd26df3f028424b5c8d2a25e08641a1fe238bf807ab0079a6c54fe8")})
            Check(selection.GetProperty(part.Item1).GetProperty("triangles").GetInt32()==part.Item2
                &&selection.GetProperty(part.Item1).GetProperty("sourceIdsSha256").GetString()==part.Item3,
                "Final composite whole source selection matches independent original resource oracle: "+part.Item1);
        string[] inputs={FinalId+".source.glb",FinalId+".source.json",FinalId+".materials.bin",FinalId+".animations.bin",FinalId+".cinf.bin",FinalId+".ancs-graph.json",FinalId+".ancs.bin",
            stem+".source.glb",stem+".source.json",stem+".cinf.bin",stem+".animations.bin",stem+".ancs-graph.json",stem+".ancs.bin",stem+".materials.bin"};
        var bound=receipt.GetProperty("sourceInputs");
        Check(bound.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(inputs.Order())
            &&inputs.All(n=>bound.GetProperty(n).GetString()==ShaFile(Path.Combine(folder,n))),
            "Final composite joins every exact original body/ball graph, animation, geometry, provenance and material input in one generation");
    }
    private static void ValidateOwnedParts(string id,string folder,ImportBundleSpec spec,ImportBundleInspection inspected,Func<uint,byte[]> resource,bool fullAnimationProof)
    {
        bool final=id==FinalId;int bodyVersion=final?6:5,ballVersion=4,manifestVersion=3,requiredCount=final?25:23,fileCount=final?27:25;
        Check(spec.RequiredComponents.Length==requiredCount&&inspected.Files.Count==fileCount&&inspected.Receipt!.Components.Length==fileCount
            &&inspected.Files.All(f=>Path.GetDirectoryName(f)==folder),"exact complete body/ball inventory and four graph files share one generation "+id);
        Check(RetroCharacterPreviewExporter.InspectParts(id,inspected).All(p=>p.State==ImportBundleState.Ready
            &&p.Version==(p.Id=="body"?bodyVersion:ballVersion)&&p.Components.Count==(p.Id=="body"||!final?11:13)
            &&p.Components.All(c=>c.State==ImportBundleState.Ready&&c.Version==(c.Role=="source-preview-manifest"?manifestVersion:1))),
            "actual verified body and ball report every versioned source component Ready "+id);
        var index=ReadParts(folder,id);
        Check(index.Schema==1&&index.CharacterId==id&&index.SourceGameId=="G2ME01"&&index.BundleVersion==spec.Version&&!index.RuntimeInstall
            &&index.Parts.Select(p=>p.Id).SequenceEqual(new[]{"body","ball"})
            &&index.UnqualifiedParts.SequenceEqual(new[]{"source-materials","first-person","runtime-animation-and-morph"}),
            "parts index binds original appearance and explicit unqualified runtime work "+id);
        foreach(var part in index.Parts)
        {
            string stem=RetroCharacterPreviewExporter.PartStem(id,part.Id);
            Check(part.Version==(part.Id=="body"?bodyVersion:ballVersion)&&part.RequiredComponents.All(c=>c.Version==(c.Role=="source-preview-manifest"?manifestVersion:1))
                &&part.RequiredComponents.Select(c=>c.Role).SequenceEqual(new[]{"geometry","rig","weights","motion-samples","skeleton-source","animation-resources","source-rig-geometry","generated-provenance"}
                    .Concat(part.Id=="body"?new[]{"source-materials"}:Array.Empty<string>())
                    .Concat(part.Id=="ball"?new[]{"animation-graph","animation-graph-source","source-materials"}:Array.Empty<string>())
                    .Concat(part.Id=="ball"&&final?new[]{"generated-composite-geometry","generated-composite-provenance"}:Array.Empty<string>())
                    .Concat(part.Id=="body"?new[]{"source-preview-manifest","source-cannon-recipe"}:Array.Empty<string>()))
                &&part.RequiredComponents.Select(c=>c.File).SequenceEqual(new[]{stem+".obj",stem+".skeleton.json",stem+".weights.json",stem+".motions.json",stem+".cinf.bin",stem+".animations.bin",stem+".source.glb",stem+".source.json"}
                    .Concat(part.Id=="body"?new[]{stem+".materials.bin"}:Array.Empty<string>())
                    .Concat(part.Id=="ball"?new[]{stem+".ancs-graph.json",stem+".ancs.bin",stem+".materials.bin"}:Array.Empty<string>())
                    .Concat(part.Id=="ball"&&final?new[]{stem+".composite.glb",stem+".composite.json"}:Array.Empty<string>())
                    .Concat(part.Id=="body"?new[]{"characters.json",id+".cannon-source.json"}:Array.Empty<string>()))
                &&part.OptionalComponents.SequenceEqual(part.Id=="ball"?Array.Empty<string>():new[]{stem+".ancs-graph.json",stem+".ancs.bin"})
                &&part.RequiredComponents.Select(c=>c.File).Concat(part.OptionalComponents).All(n=>inspected.Files.Contains(Path.Combine(folder,n))),
                "part component versions exact body/ball filenames and optional raw graph siblings "+id+"/"+part.Id);
            ValidateSourceGlb(id,part,folder,resource);
            ValidateAnimationArchive(id,part,folder,resource,fullAnimationProof);
        }
        ValidateMaterialArchive(id,folder,index.Parts.Single(p=>p.Id=="body"),spec.SourceFingerprint,resource);
        Check(inspected.Files.Any(f=>Path.GetFileName(f)==id+".ball.materials.bin"),
            "each independent ball4 profile owns its alternate-form raw material archive "+id);
        {
            var materials=RetroCharacterMaterialArchive.Read(File.ReadAllBytes(Path.Combine(folder,id+".ball.materials.bin")));
            var ballPart=index.Parts.Single(p=>p.Id=="ball");
            Check(materials.Sha256==(final?"28d5d167d48281c98234335eb8b6e766942d2832f6dbbcb35ffeb8854827e1eb":"fadaf0787fa1c9df6b0cf5765db78547953908dc1b24c9b1cba76217c16746b5")
                &&materials.Binding==new RetroCharacterMaterialArchive.Source(id+"-ball",ballPart.Refs.Cmdl,ballPart.Refs.Cskr,ballPart.Refs.Cinf,ballPart.Ancs,spec.SourceFingerprint)
                &&materials.GeometrySha256==ShaFile(Path.Combine(folder,id+".ball"+(final?".composite.glb":".source.glb")))
                &&materials.Programs.Count==1&&materials.Textures.Count==4,
                "ball material archive binds its own original source model/rig/ANCS and exact presentation geometry "+id);
            if(final)ValidateFinalComposite(folder);
        }
        {
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"characters.json")));
            var value=manifest.RootElement;var models=value.GetProperty("models");
            Check(value.GetProperty("format").GetInt32()==1&&models.GetArrayLength()==3
                &&models.EnumerateArray().Select(e=>e.GetProperty("part").GetString()).SequenceEqual(new[]{"Biped","ViewModel","AlternateForm"}),
                "generated manifest contains exactly its own body/cannon/ball entries "+id);
            {
                var alt=models[2];string stem=id+".ball",geometry=stem+(final?".composite.glb":".source.glb"),provenance=stem+(final?".composite.json":".source.json");
                Check(value.GetProperty("id").GetString()==(final?"generated-darksamus-final-parts-v3":"generated-darksamus-echoes-parts-v3")
                    &&alt.GetProperty("hunter").GetString()=="Samus"&&alt.GetProperty("lod").GetInt32()==0
                    &&alt.GetProperty("skinning").GetString()=="Weighted4"&&alt.GetProperty("poseSource").GetString()=="SourceRigPreview"
                    &&alt.GetProperty("characterId").GetString()=="darksamus"&&alt.GetProperty("skinSource").GetString()==(final?"MP2DarkSamusFinal":"MP2DarkSamus")
                    &&alt.GetProperty("model").GetString()==geometry&&alt.GetProperty("provenance").GetString()==provenance
                    &&alt.GetProperty("provenanceSha256").GetString()==ShaFile(Path.Combine(folder,provenance))
                    &&alt.GetProperty("sourceAnimationArchive").GetString()==stem+".animations.bin"
                    &&alt.GetProperty("sourceAnimationArchiveSha256").GetString()==ShaFile(Path.Combine(folder,stem+".animations.bin"))
                    &&!alt.GetProperty("boneMap").EnumerateObject().Any()&&!alt.TryGetProperty("sourceCannonReceipt",out _),
                    "manifest AlternateForm binds its own exact same-generation ball geometry/provenance/archive without body or cannon aliases "+id);
            }
            foreach(var entry in models.EnumerateArray().Take(2))
            Check(entry.GetProperty("hunter").GetString()=="Samus"&&entry.GetProperty("lod").GetInt32()==0
                &&entry.GetProperty("skinning").GetString()=="Weighted4"&&entry.GetProperty("poseSource").GetString()=="SourceRigPreview"
                &&entry.GetProperty("characterId").GetString()=="darksamus"&&entry.GetProperty("skinSource").GetString()==(final?"MP2DarkSamusFinal":"MP2DarkSamus")
                &&entry.GetProperty("model").GetString()==id+".source.glb"&&entry.GetProperty("provenance").GetString()==id+".source.json"
                &&entry.GetProperty("provenanceSha256").GetString()==Sha(File.ReadAllBytes(Path.Combine(folder,id+".source.json")))
                &&entry.GetProperty("sourceAnimationArchive").GetString()==id+".animations.bin"
                &&entry.GetProperty("sourceAnimationArchiveSha256").GetString()==Sha(File.ReadAllBytes(Path.Combine(folder,id+".animations.bin")))
                &&!entry.GetProperty("boneMap").EnumerateObject().Any(),"body/cannon manifest binds same-generation generated geometry/provenance/full archive without retarget mappings "+id);
            Check(models[0].GetProperty("sourceCannonReceipt").ValueKind==JsonValueKind.Null
                &&models[0].GetProperty("sourceCannonSha256").ValueKind==JsonValueKind.Null
                &&models[1].GetProperty("sourceCannonReceipt").GetString()==id+".cannon-source.json"
                &&models[1].GetProperty("sourceCannonSha256").GetString()==Sha(File.ReadAllBytes(Path.Combine(folder,id+".cannon-source.json"))),
                "only fixed ViewModel entry binds exact generated cannon recipe sibling and digest "+id);
            using var cannon=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,id+".cannon-source.json")));
            var recipe=cannon.RootElement;var groups=recipe.GetProperty("triangles");
            string[] sourceInputs={id+".source.glb",id+".source.json",id+".ancs-graph.json",id+".ancs.bin",id+".cinf.bin",id+".animations.bin"};
            Check(recipe.GetProperty("format").GetInt32()==2&&recipe.GetProperty("mode").GetString()=="generatedSourceCannonAuthoring"
                &&recipe.GetProperty("sourceCharacter").GetString()==id&&recipe.GetProperty("presentationPart").GetString()=="viewModel"
                &&recipe.GetProperty("sourceElbowId").GetInt32()==24&&recipe.GetProperty("sourceElbowName").GetString()=="R_elbow"
                &&recipe.GetProperty("sourceParticleLocatorId").GetInt32()==32&&recipe.GetProperty("sourceParticleLocatorName").GetString()=="GUN_Particle_LCTR"
                &&recipe.GetProperty("clipOrdinal").GetInt32()==32&&recipe.GetProperty("clipResourceId").GetUInt32()==0xd12fdfa3u
                &&recipe.GetProperty("clipSeconds").GetDouble()==0
                &&groups.EnumerateArray().Select(g=>g.GetArrayLength()).SequenceEqual(final?new[]{0,0,0,392,66,314,0,21,0}:new[]{0,0,66,0,0,326,0})
                &&groups.EnumerateArray().SelectMany(g=>g.EnumerateArray().Select(v=>v.GetInt32())).Distinct().Count()==(final?793:392)
                &&recipe.GetProperty("sourceInputs").EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(sourceInputs.Order())
                &&sourceInputs.All(n=>recipe.GetProperty("sourceInputs").GetProperty(n).GetString()==Sha(File.ReadAllBytes(Path.Combine(folder,n))))
                &&recipe.GetProperty("sourceUnitsUnscaled").GetBoolean()&&recipe.GetProperty("calibrationOnly").GetBoolean()
                &&new[]{"gameplayMuzzleChanged","publicSelectionEnabled","networkReplayEnabled","firstPersonVerified"}.All(f=>!recipe.GetProperty(f).GetBoolean()),
                "fixed source cannon receipt binds source IDs, exact zero-phase primitive, six original inputs and all false gameplay claims "+id);
            Check(ShaFile(Path.Combine(folder,id+".cannon-source.json"))==(final
                ?"46c828de8c0c7f6ec6102216400891c30475036bc2aa7da0f7b6b752abf0752c"
                :"5f1f479438b5efe1d482ff4565f7782e3175a2aed49b4a33a2953b82e4ac7521"),
                "complete generated cannon receipt reproduces its independently frozen appearance identity "+id);
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
    private static void ValidateMaterialArchive(string id,string folder,RetroCharacterPreviewExporter.AuthoringPart body,string fingerprint,Func<uint,byte[]> resource)
    {
        byte[] input=File.ReadAllBytes(Path.Combine(folder,id+".materials.bin"));
        var archive=RetroCharacterMaterialArchive.Read(input);
        byte[] cmdl=resource(body.Refs.Cmdl),cskr=resource(body.Refs.Cskr);
        var model=Cmdl.Load(value=>value==body.Refs.Cmdl?cmdl:value==body.Refs.Cskr?cskr:resource(value),body.Refs.Cmdl,body.Refs.Cskr);
        uint Be(int at)=>BinaryPrimitives.ReadUInt32BigEndian(cmdl.AsSpan(at,4));
        int materialAt=checked((0x2c+4*(int)Be(0x24)+31)&~31),materialLength=checked((int)Be(0x2c));
        var set=cmdl.AsSpan(materialAt,materialLength);
        int tableCount=checked((int)BinaryPrimitives.ReadUInt32BigEndian(set));
        uint[] table=Enumerable.Range(0,tableCount).Select(i=>BinaryPrimitives.ReadUInt32BigEndian(cmdl.AsSpan(materialAt+4+i*4,4))).ToArray();
        int countAt=4+4*tableCount,count=checked((int)BinaryPrimitives.ReadUInt32BigEndian(set[countAt..])),baseAt=countAt+4+4*count,previous=0;
        Check(archive.Binding==new RetroCharacterMaterialArchive.Source(id,body.Refs.Cmdl,body.Refs.Cskr,body.Refs.Cinf,body.Ancs,fingerprint)
            &&archive.CmdlSha256==Sha(cmdl)&&archive.CskrSha256==Sha(cskr)
            &&archive.GeometrySha256==ShaFile(Path.Combine(folder,id+".source.glb"))&&archive.MaterialSetSha256==Sha(set)
            &&archive.Programs.Count==count&&count==(id=="darksamus-echoes"?7:9)&&archive.Textures.Count==(id=="darksamus-echoes"?10:12),
            "material transport binds exact original body resources/full material set/generated geometry and all source slot counts "+id);
        var json=new JsonSerializerOptions{IncludeFields=true};
        for(int i=0;i<count;i++)
        {
            int end=checked((int)BinaryPrimitives.ReadUInt32BigEndian(set.Slice(countAt+4+i*4,4))),length=end-previous;
            var raw=set.Slice(baseAt+previous,length);var program=archive.ReadProgram(i);
            Check(archive.RawProgram(i).SequenceEqual(raw)&&archive.Programs[i].Ordinal==i&&archive.Programs[i].Length==length
                &&archive.Programs[i].RawSha256==Sha(raw)&&archive.Programs[i].TextureIds.SequenceEqual(program.Tex.Select(t=>table[t]))
                &&JsonSerializer.SerializeToUtf8Bytes(program,json).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(model.Materials[i],json)),
                "every raw program byte/opaque field and parsed selector matches its original CMDL material ordinal "+id+"/"+i);
            byte[] before=JsonSerializer.SerializeToUtf8Bytes(program,json);program.Flags^=1;
            if(program.TexIds.Length>0)program.TexIds[0]^=1;if(program.Channels.Length>0)program.Channels[0]^=1;
            if(program.Stages.Length>0)program.Stages[0].ColorIn[0]^=1;
            Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(archive.ReadProgram(i),json)),"mutable returned material parser is detached from captured archive "+id+"/"+i);
            previous=end;
        }
        uint[] needed=model.Materials.SelectMany(m=>m.TexIds).Distinct().Order().ToArray();
        Check(archive.Textures.Select(t=>t.ResourceId).SequenceEqual(needed),"archive retains exactly every declared texture slot including TEV-unused declarations "+id);
        var textureMetadata=new List<object>();long pixels=0;
        foreach(var texture in archive.Textures)
        {
            byte[] raw=resource(texture.ResourceId);var image=archive.DecodeTexture(texture.ResourceId);
            string oracle=MaterialTextureOracle(raw,image);
            Check(archive.RawTexture(texture.ResourceId).SequenceEqual(raw)&&texture.SourceSha256==Sha(raw)&&texture.Length==raw.Length
                &&image.Layout.Width==texture.Width&&image.Layout.Height==texture.Height&&image.Layout.Format==texture.Format
                &&texture.RgbaSha256==Sha(image.Pixels)&&oracle==texture.RgbaSha256,
                "every complete TXTR and every decoded RGBA texel agrees with original source and independent coordinate oracle "+id+"/"+texture.ResourceId.ToString("x8"));
            pixels+=(long)texture.Width*texture.Height;
            textureMetadata.Add(new{texture.ResourceId,texture.Format,texture.Width,texture.Height,texture.MipCount,texture.Length,texture.SourceSha256,texture.RgbaSha256,OracleSha256=oracle});
        }
        string firstProgram=archive.Programs[0].RawSha256,firstTexture=archive.Textures[0].SourceSha256;Array.Fill(input,(byte)0);
        Check(Sha(archive.RawProgram(0))==firstProgram&&Sha(archive.RawTexture(archive.Textures[0].ResourceId))==firstTexture,
            "archive owns detached bytes after caller destroys original input "+id);
        Expect<NotSupportedException>(()=>((IList<RetroCharacterMaterialArchive.Program>)archive.Programs)[0]=archive.Programs[0],"program directory is read-only");
        Expect<NotSupportedException>(()=>((IList<uint>)archive.Programs[0].TextureIds)[0]=0,"program texture bindings are read-only");
        Console.WriteLine(JsonSerializer.Serialize(new{Kind="owned-complete-material-authoring",CharacterId=id,ArchiveSha256=archive.Sha256,
            SourceCmdlSha256=archive.CmdlSha256,SourceCskrSha256=archive.CskrSha256,GeneratedGlbSha256=archive.GeometrySha256,
            MaterialSetSha256=archive.MaterialSetSha256,Programs=archive.Programs.Count,Textures=textureMetadata,ComparedPixels=pixels,
            archive.DecodedBytes,SourcePayloadsWritten=false,RenderingActivated=false,SourceMaterialsQualified=false}));
    }
    // Logical-coordinate oracle from the separately qualified texture probe:
    // no production tile loops or endpoint helpers are used here.
    private static string MaterialTextureOracle(ReadOnlySpan<byte> source,RetroTextureSource.Decoded image)
    {
        static int Expand(int v,int bits){int result=0;for(int b=0;b<8;b++)result=2*result+((v>>(bits-1-b%bits))&1);return result;}
        static int Word(ReadOnlySpan<byte> s,int at)=>256*s[at]+s[at+1];
        var layout=image.Layout;var row=new byte[layout.Width*4];using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Require(layout.Format is 8 or 10,"Owned material oracle only admits the measured RGB5A3/CMPR source profile.");
        for(int y=0;y<layout.Height;y++)
        {
            for(int x=0;x<layout.Width;x++)
            {
                int at;
                if(layout.Format==8)
                {
                    at=12+32*((y/4)*((layout.Width+3)/4)+x/4)+2*(4*(y%4)+x%4);int word=Word(source,at),bits=word>=0x8000?5:4;
                    for(int c=0;c<3;c++)row[x*4+c]=(byte)Expand((word>>((2-c)*bits))&((1<<bits)-1),bits);
                    row[x*4+3]=(byte)(word>=0x8000?255:Expand(word/4096,3));
                }
                else
                {
                    at=12+32*((y/8)*((layout.Width+7)/8)+x/8)+8*(2*((y%8)/4)+(x%8)/4);
                    int first=Word(source,at),second=Word(source,at+2),choice=(source[at+4+y%4]>>(6-2*(x%4)))&3;
                    for(int c=0;c<3;c++)
                    {
                        int shift=c==0?11:c==1?5:0,bits=c==1?6:5;
                        int a=Expand((first>>shift)&((1<<bits)-1),bits),b=Expand((second>>shift)&((1<<bits)-1),bits);
                        row[x*4+c]=(byte)(choice switch{0=>a,1=>b,2=>first>second?(5*a+3*b)/8:(a+b)/2,_=>first>second?(3*a+5*b)/8:(a+b)/2});
                    }
                    row[x*4+3]=(byte)(choice==3&&first<=second?0:255);
                }
            }
            Require(row.AsSpan().SequenceEqual(image.Pixels.Slice(y*row.Length,row.Length)),"Independent logical texel oracle differs from material archive decode.");
            digest.AppendData(row);
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
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
