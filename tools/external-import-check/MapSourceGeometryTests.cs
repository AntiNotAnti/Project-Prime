using System.Buffers.Binary;
using System.Collections;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// Entirely invented authored geometry/UVs. No retail model, texture, coordinate
// or map recipe is stored. Transport integrity is not renderer qualification.
internal static class MapSourceGeometryTests
{
    private static int checks;
    private sealed record Generated(MapDefinition Definition, MapOutputSet Outputs, byte[] Model,
        byte[] Component, string Recipe, MapSourceGeometry.Snapshot Snapshot);

    internal static void Run()
    {
        checks = 0;
        string root = Path.Combine(Path.GetTempPath(), "prime-source-geometry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Generated primary = Generate(Fixture(), Path.Combine(root, "primary"));
            Emission(primary);
            Generated split = Splits(root);
            Codec(primary, split);
            AuthoringRefusal();
            EditingGuards();
            LegacyAndCache(primary, root);
            RuntimeGeneration(root);
            AllocationBounds(primary);
            TinyVisualTriangles();
            Console.WriteLine($"MAP SOURCE GEOMETRY PASS {checks} synthetic checks; final native emission/immutable transport only, no source media or renderer activation.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Emission(Generated generated)
    {
        var snapshot = generated.Snapshot;
        Check(generated.Outputs.Files.Count() == 6 && generated.Outputs.Complete
            && generated.Outputs.SourceGeometry != null && MapBuildManifest.IsCurrent(generated.Definition, generated.Outputs),
            "public Generate publishes six complete hashed outputs with companion as commit member");
        Check(snapshot.MapId == generated.Definition.MapId && snapshot.RecipeSha256 == generated.Recipe
            && snapshot.NativeModelSha256 == Hash(generated.Model) && snapshot.ComponentSha256 == Hash(generated.Component),
            "component captures exact map/recipe/native model/component identities");
        Check(snapshot.Entries.Count == 1 && snapshot.Entries[0].DlistId == 0 && snapshot.Entries[0].MaterialId == 0
            && snapshot.Entries[0].Triangles == 4 && snapshot.Entries[0].Quads == 1,
            "only source material list is transported; one triangle plus three fan triangles then one quad");
        // Authored quad precedes the triangle. Native emission first groups all
        // triangles; a5-gon expands (0,1,2),(0,2,3),(0,3,4), then the quad.
        int[] tags = { 20, 21, 22, 30, 31, 32, 30, 32, 33, 30, 33, 34, 10, 11, 12, 13 };
        Corners(snapshot.Entries[0].Corners, tags.Select(Uv).ToArray(), "independent triangle/fan/quad source corner order");
        Check(BitConverter.SingleToInt32Bits(snapshot.Entries[0].Corners[12].BaseV) == unchecked((int)0x80000000)
            && snapshot.Entries[0].Corners[0].BaseU == 200000.25f,
            "negative-zero and large unquantized source BASE float bits survive native fallback packing");
        var mesh = (MapMesh)generated.Definition.Geometry[0];
        Check(mesh.Faces[0][0] == mesh.Faces[1][0]
            && snapshot.Entries[0].Corners[0] != snapshot.Entries[0].Corners[12],
            "shared native position retains independent per-face source UV seam");
        var header = NativeHeader(generated.Model);
        var lists = Read.DoOffsets<DisplayList>(generated.Model, header.DlistOffset, header.MeshCount);
        int nativeCorners = 0;
        Read.VisitRenderInstructions(generated.Model, lists[0], (code, args) => {
            if (code == InstructionCode.TEXCOORD) { Check(args[0] == 0, "source native fallback UV remains explicitly zero"); nativeCorners++; }
        });
        Check(nativeCorners == tags.Length, "independent native command walk has exactly companion corner cardinality");
        int ordinaryCorners = 0;
        Read.VisitRenderInstructions(generated.Model, lists[1], (code, args) => {
            if (code == InstructionCode.TEXCOORD) { Check(args[0] == 0, "nullable unsupported face in source profile also has zero native fallback UV"); ordinaryCorners++; }
        });
        Check(ordinaryCorners == 3 && snapshot.Entries.All(e => e.DlistId != 1),
            "unsupported nullable face retains native geometry without inventing a source coordinate snapshot");
        var captured = MapBuildSnapshot.Capture(generated.Definition);
        var compiled = MapBuilder.Build(generated.Definition);
        mesh.SourceUvFaces![0]!.Base[0][0] = 999;
        Check(((MapMesh)captured.CreateDefinition().Geometry[0]).SourceUvFaces![0]!.Base[0][0] == Uv(10).BaseU
            && compiled.Faces.First(f => f.SourceUv != null).SourceUv!.Base[0].X == Uv(10).BaseU,
            "build snapshot and compiled source face independently detach mutable nested DTO arrays");
        mesh.SourceUvFaces[0]!.Base[0][0] = Uv(10).BaseU;
        var worker = (MapMesh)captured.CreateDefinition().Geometry[0]; worker.SourceUvFaces![0]!.Lightmap[0][0] = 12;
        Check(((MapMesh)captured.CreateDefinition().Geometry[0]).SourceUvFaces![0]!.Lightmap[0][0] == Uv(10).LightmapU,
            "each worker receives fresh source-coordinate nested arrays");
        Check(snapshot.Entries is IList<MapSourceGeometry.Entry> { IsReadOnly: true }
            && snapshot.Entries[0].Corners is IList<EchoesSourceCoordinates> { IsReadOnly: true },
            "decoded entry and coordinate collections refuse mutation through list interfaces");
        Expect<NotSupportedException>(() => ((IList<MapSourceGeometry.Entry>)snapshot.Entries).Clear(), "entry list immutable");
        Expect<NotSupportedException>(() => ((IList<EchoesSourceCoordinates>)snapshot.Entries[0].Corners)[0] = default, "corner list immutable");
    }

    private static Generated Splits(string root)
    {
        MapDefinition batched = Many(350, partition: false);
        Generated split = Generate(batched, Path.Combine(root, "batch-split"));
        Check(split.Snapshot.Entries.Count == 2 && split.Snapshot.Entries[0].Triangles == 341
            && split.Snapshot.Entries[1].Triangles == 9 && split.Snapshot.Entries.All(e => e.Quads == 0 && e.MaterialId == 0),
            "1024-vertex batch limit splits at complete triangle341 rather than a corner");
        Corners(split.Snapshot.Entries[0].Corners, Enumerable.Range(0, 341).SelectMany(i => TriangleUvs(i)).ToArray(), "first native batch source sequence");
        Corners(split.Snapshot.Entries[1].Corners, Enumerable.Range(341, 9).SelectMany(i => TriangleUvs(i)).ToArray(), "second native batch source sequence");
        Generated partitioned = Generate(Many(256, partition: true), Path.Combine(root, "partition-material"));
        Check(partitioned.Snapshot.Entries.Count == 4, "two spatial partitions each retain two material-specific display lists");
        for (int group = 0; group < 4; group++)
        {
            int side = group / 2, material = group % 2;
            var entry = partitioned.Snapshot.Entries[group];
            Check(entry.DlistId == group && entry.MaterialId == material && entry.Triangles == 64 && entry.Quads == 0,
                "spatial/material native list identity " + group);
            var expected = Enumerable.Range(side * 128, 128).Where(i => i % 2 == material).SelectMany(TriangleUvs).ToArray();
            Corners(entry.Corners, expected, "partition/material corner order " + group);
        }
        return split;
    }

    private static void Codec(Generated primary, Generated multiple)
    {
        var inputs = multiple.Snapshot.Entries.Select(e => new MapSourceGeometry.Input(e.DlistId, e.MaterialId, e.Corners)).Reverse().ToList();
        var originalFirst = inputs[0];
        byte[] encoded = MapSourceGeometry.Encode(multiple.Definition.MapId, multiple.Recipe.ToUpperInvariant(), multiple.Model, inputs);
        Check(encoded.SequenceEqual(multiple.Component) && inputs[0] == originalFirst,
            "codec sorts detached inventory deterministically without mutating caller order; hash case is canonical");
        Check(encoded.AsSpan(0, 4).SequenceEqual("PPSG"u8) && U32(encoded, 4) == 1 && U32(encoded, 8) == 192
            && U32(encoded, 12) == 2 && U32(encoded, 16) == 96 && U32(encoded, 20) == 384
            && U32(encoded, 24) == encoded.Length && U32(encoded, 28) == 1
            && new Guid(encoded.AsSpan(32, 16)) == multiple.Definition.MapId,
            "literal little-endian header/version/directory/payload contract");
        Check(encoded.AsSpan(80, 32).SequenceEqual(SHA256.HashData(multiple.Model))
            && encoded.AsSpan(144, 32).SequenceEqual(SHA256.HashData(encoded.AsSpan(192, 192))),
            "whole native bytes and exact entry directory digests are bound independently");
        byte[] good = primary.Component;
        void Reject(byte[] bytes, string why) => Expect<InvalidDataException>(() => MapSourceGeometry.Decode(bytes, primary.Model, primary.Definition.MapId, primary.Recipe), why);
        foreach (int at in new[] { 0, 4, 8, 16, 20, 24, 28, 32, 48, 80, 112, 144, 176, 191 })
        { byte[] bad = (byte[])good.Clone(); bad[at] ^= 1; Reject(bad, "header integrity/identity field " + at); }
        foreach (uint count in new uint[] { 0, 65536, uint.MaxValue })
        { byte[] bad = (byte[])good.Clone(); Put(bad, 12, count); Reject(bad, "entry-count admission before retained arrays " + count); }
        foreach (int length in new[] { 0, 191, 192, 200, 287, good.Length - 1 }) Reject(good[..length], "truncated component " + length);
        byte[] trailing = good.Concat(new byte[] { 0 }).ToArray(); Reject(trailing, "old total length rejects trailing byte");
        Put(trailing, 24, (uint)trailing.Length); Reject(trailing, "honest total length still rejects unexpected trailing data");
        foreach (var field in new (int At, uint Value)[] { (0, 1), (0, uint.MaxValue), (4, 1), (4, uint.MaxValue),
            (8, 0), (8, 65001), (8, uint.MaxValue), (12, 5), (16, 0), (20, 1), (20, uint.MaxValue),
            (24, 0), (24, 255), (24, uint.MaxValue), (28, 1) })
        {
            byte[] bad = (byte[])good.Clone(); Put(bad, 192 + field.At, field.Value); RehashDirectory(bad);
            Reject(bad, "rehash-valid entry structure/native identity refusal " + field);
        }
        foreach (int at in new[] { 192 + 32, 192 + 64, good.Length - 1 })
        { byte[] bad = (byte[])good.Clone(); bad[at] ^= 1; if (at < 288) RehashDirectory(bad); Reject(bad, "native-list or payload digest refusal " + at); }
        foreach (int channel in Enumerable.Range(0, 4))
        foreach (uint bits in new uint[] { 0x7fc00000, 0x7f800000, 0xff800000 })
        {
            byte[] bad = (byte[])good.Clone(); Put(bad, 288 + channel * 4, bits); RehashPayload(bad, 0); RehashDirectory(bad);
            Reject(bad, "rehash-valid nonfinite coordinate channel " + channel);
        }
        foreach (int modelChange in new[] { 0, primary.Model.Length - 1 })
        {
            byte[] model = (byte[])primary.Model.Clone(); model[modelChange] ^= 1;
            Expect<InvalidDataException>(() => MapSourceGeometry.Decode(good, model, primary.Definition.MapId, primary.Recipe), "same-size changed native bytes cannot reuse companion");
        }
        var h = NativeHeader(primary.Model);
        var lists = Read.DoOffsets<DisplayList>(primary.Model, h.DlistOffset, h.MeshCount);
        byte[] reordered = (byte[])primary.Model.Clone();
        // Change one native vertex position word without changing commands,
        // corner counts or byte extent. A same-count generation is still stale.
        int[] arguments = VertexArguments(primary.Model, lists[0]);
        Check(arguments.Length >= 2, "native same-count mutation fixture finds two emitted vertices");
        byte[] firstVertex = reordered.AsSpan(arguments[0], 8).ToArray();
        reordered.AsSpan(arguments[1], 8).CopyTo(reordered.AsSpan(arguments[0], 8));
        firstVertex.CopyTo(reordered, arguments[1]);
        Expect<InvalidDataException>(() => MapSourceGeometry.Decode(good, reordered, primary.Definition.MapId, primary.Recipe), "same-count reordered/changed native emission cannot retarget old companion");
        byte[] resignedModel = (byte[])good.Clone(); SHA256.HashData(reordered).CopyTo(resignedModel, 80);
        Expect<InvalidDataException>(() => MapSourceGeometry.Decode(resignedModel, reordered, primary.Definition.MapId, primary.Recipe), "even replaced whole-model header digest retains per-list integrity check");
        foreach (int offset in new[] { (int)h.DlistOffset, (int)h.MeshOffset })
        {
            byte[] model = (byte[])primary.Model.Clone(); Put(model, offset, uint.MaxValue);
            byte[] component = (byte[])good.Clone(); SHA256.HashData(model).CopyTo(component, 80);
            Expect<InvalidDataException>(() => MapSourceGeometry.Decode(component, model, primary.Definition.MapId, primary.Recipe), "bounded native directory/list references refuse overflow before traversal");
        }
        Expect<InvalidDataException>(() => MapSourceGeometry.Decode(good, primary.Model, Guid.NewGuid(), primary.Recipe), "different map identity cannot reuse a matching-size component");
        Expect<InvalidDataException>(() => MapSourceGeometry.Decode(good, primary.Model, primary.Definition.MapId, new string('0', 64)), "different recipe cannot reuse matching native bytes");
        byte[] inputCopy = (byte[])good.Clone(); byte[] nativeCopy = (byte[])primary.Model.Clone();
        var detached = MapSourceGeometry.Decode(inputCopy, nativeCopy, primary.Definition.MapId, primary.Recipe);
        Array.Fill(inputCopy, (byte)0); Array.Fill(nativeCopy, (byte)0);
        Corners(detached.Entries[0].Corners, primary.Snapshot.Entries[0].Corners, "decoded snapshot survives mutation of both caller buffers");
        foreach (var invalid in new[] { Array.Empty<MapSourceGeometry.Input>(), new[] { new MapSourceGeometry.Input(-1, 0, primary.Snapshot.Entries[0].Corners) },
            new[] { new MapSourceGeometry.Input(0, 1, primary.Snapshot.Entries[0].Corners) },
            new[] { new MapSourceGeometry.Input(0, 0, primary.Snapshot.Entries[0].Corners.Take(3).ToArray()) },
            new[] { new MapSourceGeometry.Input(0, 0, primary.Snapshot.Entries[0].Corners), new MapSourceGeometry.Input(0, 0, primary.Snapshot.Entries[0].Corners) } })
            Expect<InvalidDataException>(() => MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model, invalid), "encode refuses absent/duplicate/stale/count-mismatched entries");
        Expect<InvalidDataException>(() => MapSourceGeometry.Encode(Guid.Empty, primary.Recipe, primary.Model, inputs), "persistent map identity required");
        Expect<InvalidDataException>(() => MapSourceGeometry.Decode(good, primary.Model, primary.Definition.MapId, "bad"), "recipe digest shape required");
        var nonfinite = primary.Snapshot.Entries[0].Corners.ToArray(); nonfinite[0] = new(float.NaN, 0, 0, 0);
        Expect<InvalidDataException>(() => MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model,
            new[] { new MapSourceGeometry.Input(0, 0, nonfinite) }), "encode refuses nonfinite coordinates");
    }

    private static void AuthoringRefusal()
    {
        var mutations = new Action<MapDefinition>[] {
            d => d.MapId = Guid.Empty,
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces = new(),
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces = Enumerable.Repeat<MapSourceUvFace?>(null, 4).ToList(),
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces!.RemoveAt(0),
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces![0]!.Base[0][0] = float.NaN,
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces![0]!.Lightmap[0][1] = float.PositiveInfinity,
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces![0]!.Base = new[] { new[] { 0f, 0f } },
            d => ((MapMesh)d.Geometry[0]).SourceUvFaces![0]!.Contract = "unsupported",
            d => ((MapMesh)d.Geometry[0]).FaceTexcoords.Add(new[] { new[] { 0f, 0f } }),
            d => ((MapMesh)d.Geometry[0]).FaceUv[0] = new(),
            d => ((MapMesh)d.Geometry[0]).FaceMaterials[3] = 0,
            d => d.Brushes.Add(new MapBrush { Id = Guid.NewGuid(), Material = 0, Min = new[] { -1f,-1f,-1f }, Max = new[] { 1f,0f,1f } }),
            d => d.Materials[0].ProceduralWhite = false,
            d => d.Materials[0].Albedo = "texture.png",
            d => d.Materials[0].Normal = "normal.png",
            d => d.Materials[0].Animation = new(),
            d => d.Import = new(), d => d.NativeRoom = new(), d => d.ModelSources.Add(new()),
            d => ((MapMesh)d.Geometry[0]).CollisionOnly = true
        };
        for (int i = 0; i < mutations.Length; i++)
        {
            var definition = Fixture(); mutations[i](definition);
            Check(!MapValidator.Validate(definition).IsValid, "authoring refuses partial/ambiguous/hybrid source contract " + i);
        }
        var valid = Fixture();
        Check(MapValidator.Validate(valid).IsValid, "nullable ordinary face on a separate material is valid source coverage");
        var neutralOnly = Fixture(); neutralOnly.Materials[1].Animation = new();
        Check(MapValidator.Validate(neutralOnly, checkSources: false).Diagnostics.Any(d => d.Code == "FP-MAP-SOURCE-UV"
            && d.Severity == MapDiagnosticSeverity.Error),
            "declared source-profile null-pair faces also require a neutral fallback; no active channel can bind placeholder UVs");
        valid.LocalSourceOnly = false; valid.LocalSourceFingerprint = null;
        Check(valid.RequiresPrivateSource, "source UV declaration itself retains private-source fence after flags stripped");
    }

    private static void LegacyAndCache(Generated primary, string root)
    {
        var legacy = CreateLegacyFixture();
        Check(!legacy.HasSourceGeometry && !legacy.RequiresPrivateSource, "absent source coordinates preserve ordinary source-neutral classification");
        Check(!legacy.Serialize().Contains("sourceUvFaces", StringComparison.Ordinal)
            && !MapProjectFolder.Serialize(legacy).Contains("sourceUvFaces", StringComparison.Ordinal), "legacy serialization omits new null field in both serializers");
        string dir = Path.Combine(root, "legacy"); var outputs = MapOutputSet.Create(legacy, dir, dir, dir);
        MapPacker.Generate(MapBuilder.Build(legacy), dir, dir, dir, verbose: false);
        Check(outputs.SourceGeometry == null && outputs.Files.Count() == 5 && outputs.Complete && MapBuildManifest.IsCurrent(legacy, outputs),
            "legacy Generate retains five native outputs and a valid five-hash manifest");
        var manifest = JsonSerializer.Deserialize<MapBuildManifest>(File.ReadAllText(primary.Outputs.Manifest))!;
        Check(manifest.OutputHashes.Length == 6 && manifest.OutputHashes[5] == Hash(primary.Component), "manifest's sixth digest binds companion bytes");
        var scheduler = new MapBuildScheduler(Path.Combine(root, "scheduler"), concurrency: 1);
        var snapshot = MapBuildSnapshot.Capture(primary.Definition);
        var first = scheduler.BuildAsync(snapshot).GetAwaiter().GetResult();
        Check(first.Succeeded && first.Outputs!.Files.Count() == 6, "scheduler publishes source companion as sixth complete cached output: " + string.Join(";", first.Diagnostics.Select(d => d.Message)));
        var hit = scheduler.BuildAsync(snapshot).GetAwaiter().GetResult();
        Check(hit.Succeeded && hit.CacheHit, "unmodified six-output scheduler cache hit");
        string sourceFile = hit.Outputs!.SourceGeometry!; byte[] bytes = File.ReadAllBytes(sourceFile); bytes[^1] ^= 1; File.WriteAllBytes(sourceFile, bytes);
        Check(!MapBuildManifest.IsCurrent(primary.Definition, hit.Outputs), "companion corruption invalidates build manifest");
        var repaired = scheduler.BuildAsync(snapshot).GetAwaiter().GetResult();
        Check(repaired.Succeeded && !repaired.CacheHit && Hash(File.ReadAllBytes(repaired.Outputs!.SourceGeometry!)) == Hash(primary.Component),
            "sixth-file cache corruption forces deterministic rebuild rather than stale cache hit");
        File.Delete(repaired.Outputs!.SourceGeometry!);
        Check(!repaired.Outputs.Complete && !MapBuildManifest.IsCurrent(primary.Definition, repaired.Outputs), "missing companion makes whole output set incomplete");
        var restored = scheduler.BuildAsync(snapshot).GetAwaiter().GetResult();
        Check(restored.Succeeded && !restored.CacheHit && restored.Outputs!.Complete, "missing sixth file rebuilds complete cache entry");
        Expect<InvalidDataException>(() => MapPackageBuilder.Build(primary.Definition, Path.Combine(root, "forbidden.ppmap")), "private source geometry cannot be packaged after flags stripped");
        var edited = MapProjectSerializer.Clone(primary.Definition); ((MapMesh)edited.Geometry[0]).SourceUvFaces![0]!.Base[0][0] += .5f;
        Check(MapBuildFingerprint.Create(edited).RecipeHash != primary.Recipe, "source UV edits change recipe fingerprint even when legacy model UVs stay zero");
        Check(!MapBuildManifest.IsCurrent(edited, primary.Outputs), "changed source coordinates invalidate old native/companion generation together");
    }

    private static void RuntimeGeneration(string root)
    {
        string runtimeRoot = Path.Combine(root, "runtime");
        Generated generated = Generate(Fixture(), Path.Combine(runtimeRoot, "_archives", "fixture"));
        var definition = generated.Definition;
        var originalDefinition = MapProjectSerializer.Clone(definition);
        var spec = MapSourceGeometryReadSpec.Capture(definition, "fixture")!;
        Guid id = spec.MapId; string recipe = spec.RecipeSha256;
        var missing = MapSourceGeometryReadSpec.Capture(definition, "missing")!;
        Check(!Directory.Exists(Path.Combine(runtimeRoot, "_archives", "missing")), "runtime spec Capture performs no content I/O");
        Expect<DirectoryNotFoundException>(() => missing.Load(runtimeRoot, generated.Model), "missing publication directory refuses runtime source model load");
        definition.MapId = Guid.NewGuid(); definition.Name = "MUTATED_NAME";
        ((MapMesh)definition.Geometry[0]).SourceUvFaces![0]!.Base[0][0] = 99;
        Check(spec.MapId == id && spec.RecipeSha256 == recipe && spec.Load(runtimeRoot, generated.Model).MapId == id,
            "runtime read spec freezes map/name/recipe and nested source geometry before later DTO edits");
        string manifest = generated.Outputs.Manifest, component = generated.Outputs.SourceGeometry!;
        byte[] originalManifest = File.ReadAllBytes(manifest), originalComponent = File.ReadAllBytes(component);
        void Restore() { File.WriteAllBytes(manifest, originalManifest); File.WriteAllBytes(component, originalComponent); }
        File.Delete(component);
        Expect<FileNotFoundException>(() => spec.Load(runtimeRoot, generated.Model), "missing companion is not silently discarded"); Restore();
        File.Delete(manifest);
        Expect<FileNotFoundException>(() => spec.Load(runtimeRoot, generated.Model), "missing manifest is not silently discarded"); Restore();
        File.WriteAllText(manifest, "broken-json");
        Expect<JsonException>(() => spec.Load(runtimeRoot, generated.Model), "corrupt manifest prevents runtime generation adoption"); Restore();
        byte[] corrupted = (byte[])originalComponent.Clone(); corrupted[^1] ^= 1; File.WriteAllBytes(component, corrupted);
        Expect<InvalidDataException>(() => spec.Load(runtimeRoot, generated.Model), "manifest's component digest prevents pixel-coordinate corruption adoption"); Restore();
        var receipt = JsonSerializer.Deserialize<MapBuildManifest>(originalManifest)!;
        receipt.OutputHashes[0] = new string('0', 64); File.WriteAllBytes(manifest, JsonSerializer.SerializeToUtf8Bytes(receipt));
        Expect<InvalidDataException>(() => spec.Load(runtimeRoot, generated.Model), "manifest's model digest must match exact parser input bytes"); Restore();
        receipt = JsonSerializer.Deserialize<MapBuildManifest>(originalManifest)!;
        receipt.Fingerprint = receipt.Fingerprint! with { RecipeHash = new string('0',64) };
        File.WriteAllBytes(manifest, JsonSerializer.SerializeToUtf8Bytes(receipt));
        Expect<InvalidDataException>(() => spec.Load(runtimeRoot, generated.Model), "manifest recipe must match captured generation"); Restore();
        receipt = JsonSerializer.Deserialize<MapBuildManifest>(originalManifest)!;
        receipt.Fingerprint = receipt.Fingerprint! with { CompilerVersion = receipt.Fingerprint!.CompilerVersion - 1 };
        File.WriteAllBytes(manifest, JsonSerializer.SerializeToUtf8Bytes(receipt));
        Expect<InvalidDataException>(() => spec.Load(runtimeRoot, generated.Model), "old compiler receipt cannot reinterpret current source transport"); Restore();
        byte[] changedModel = (byte[])generated.Model.Clone(); changedModel[^1] ^= 1;
        Expect<InvalidDataException>(() => spec.Load(runtimeRoot, changedModel), "same-count old/different native model input refuses runtime load");
        using (var file = new FileStream(component, FileMode.Create, FileAccess.Write)) file.SetLength(64L*1024*1024+1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Expect<InvalidDataException>(() => spec.Load(runtimeRoot, generated.Model), "component length budget precedes runtime file allocation");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024*1024, "oversized sparse runtime component does not allocate its64MiB declared length"); Restore();
        string alias = Path.Combine(root, "runtime-ancestor-link"); SourceBankMaterialSnapshotTests.CreateDirectoryAlias(alias, runtimeRoot);
        try { Expect<InvalidDataException>(() => spec.Load(alias, generated.Model), "runtime read rejects linked ancestor above selected source root"); }
        finally { Directory.Delete(alias); }
        var snapshot = spec.Load(runtimeRoot, generated.Model);
        var model = new Model("synthetic-source-owner", false, default, Array.Empty<RawNode>(), Array.Empty<RawMesh>(),
            Array.Empty<RawMaterial>(), Array.Empty<DisplayList>(), Array.Empty<IReadOnlyList<RenderInstruction>>(),
            new AnimationResults(), Array.Empty<OpenTK.Mathematics.Matrix4>(), Array.Empty<Recolor>(), Array.Empty<int>(),
            Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>()) { SourceGeometry = snapshot };
        Check(ReferenceEquals(model.CreateSceneCopy().SourceGeometry, snapshot), "scene copy preserves immutable source snapshot identity without reloading/retargeting");
        string previousRoot = Paths.FileSystem, previousNamespace = CustomRooms.RuntimeNamespace;
        string archive = Path.Combine(runtimeRoot, "_archives", originalDefinition.Name.ToLowerInvariant());
        Directory.CreateDirectory(archive);
        foreach (string file in generated.Outputs.Files.Append(generated.Outputs.Manifest))
            File.Copy(file, Path.Combine(archive, Path.GetFileName(file)));
        try
        {
            CustomRooms.RuntimeNamespace = ""; Paths.SetPath(Paths.MphKey, runtimeRoot);
            var metadata = CustomRooms.MakeMetadata(originalDefinition, 9990);
            Model adopted = Read.PrepareRoomModel(metadata);
            Check(adopted.SourceGeometry is { } adoptedSource && adoptedSource.NativeModelSha256 == Hash(generated.Model)
                && adoptedSource.RecipeSha256 == generated.Recipe && adoptedSource.Entries.Count == 1,
                "actual room metadata/model parser adopts companion bound to the exact initially read native bytes without Scene/GPU");
            Check(ReferenceEquals(adopted.CreateSceneCopy().SourceGeometry, adopted.SourceGeometry),
                "actual parsed model's scene copy transfers immutable source snapshot");
            File.Delete(Path.Combine(archive, Path.GetFileName(component)));
            Expect<FileNotFoundException>(() => Read.PrepareRoomModel(metadata),
                "actual room model load fails closed when its declared companion is missing");
        }
        finally { Paths.SetPath(Paths.MphKey, previousRoot); CustomRooms.RuntimeNamespace = previousNamespace; }
    }

    private static void EditingGuards()
    {
        var operations = new Action<MapMesh>[] {
            m => MapMeshEditing.Triangulate(m, new[] { 0 }),
            m => MapMeshEditing.Join(new[] { m, new MapMesh() }),
            m => MapMeshEditing.Join(new[] { new MapMesh(), m }),
            m => MapMeshEditing.TransformUv(m, new[] { 0 }, new System.Numerics.Vector2(2), new System.Numerics.Vector2(1), 45, 16),
            m => MapMeshEditing.AssignMaterial(m, new[] { 0 }, 1)
        };
        foreach (var operation in operations)
        {
            var definition = Fixture(); var mesh = (MapMesh)definition.Geometry[0]; string before = definition.Serialize();
            Expect<InvalidOperationException>(() => operation(mesh), "source topology/UV/material edit is explicitly refused");
            Check(definition.Serialize() == before, "refused operation preserves every serialized source channel/identity before mutation");
        }
        var priorDefinition = Fixture(); var priorMesh = (MapMesh)priorDefinition.Geometry[0];
        var prior = new MapModelSource { Id = Guid.NewGuid(), Source = "synthetic-absent.obj" };
        prior.Objects.Add(new MapModelObject { Id = priorMesh.Id, Key = "Source#0" }); priorDefinition.ModelSources.Add(prior);
        string original = priorDefinition.Serialize();
        var emptyImport = new ImportedModel(Array.Empty<MapMesh>(), Array.Empty<MapMaterial>(), new Dictionary<string,byte[]>(), Array.Empty<string>());
        Expect<InvalidOperationException>(() => ModelReimport.Apply(priorDefinition, emptyImport, "never-read.obj", new string('b',64), new ModelImportSettings(), prior.Id),
            "reimport refuses removal of previously tracked source mesh before external I/O or mutation");
        Check(priorDefinition.Serialize() == original, "reimport refusal preserves exact existing document and model provenance");
        var document = new MapDocument(new MapProject(Fixture()));
        string documentBefore = document.Project.Definition.Serialize(); var historyBefore = document.CurrentStateId;
        Expect<InvalidOperationException>(() => document.PaintFaces(document.Project.Definition.Geometry[0].Id, new[] { 0 }, 1), "document material paint rejects source mesh before history transaction");
        Check(document.Project.Definition.Serialize() == documentBefore && document.CurrentStateId == historyBefore,
            "refused document paint leaves source data and undo history unchanged");
        var definitionMoved = Fixture(); var moved = (MapMesh)definitionMoved.Geometry[0];
        var beforeUv = JsonSerializer.Serialize(moved.SourceUvFaces);
        moved.Transform.Position = new[] { 1f,2f,3f };
        MapMeshEditing.MoveVertex(moved, 0, new System.Numerics.Vector3(.5f,0,0));
        moved.Vertices.Add(new[] { 99f,99f,99f });
        int previousVertices = moved.Vertices.Count; MapMeshEditing.Compact(moved);
        Check(moved.Vertices.Count == previousVertices-1 && JsonSerializer.Serialize(moved.SourceUvFaces) == beforeUv
            && moved.Transform.Position.SequenceEqual(new[] { 1f,2f,3f }), "allowed object/vertex transform and Compact preserve exact per-face source channels");
        Check(MapMeshEditing.Clone(moved).SourceUvFaces!.Count == moved.SourceUvFaces!.Count
            && JsonSerializer.Serialize(MapMeshEditing.Clone(moved).SourceUvFaces) == beforeUv,
            "whole mesh clone preserves paired coordinate channels");
    }

    private sealed class NeverList<T> : IReadOnlyList<T>
    {
        public int Count { get; }
        internal NeverList(int count) => Count = count;
        public T this[int index] => throw new Exception("Oversized inventory was indexed before admission");
        public IEnumerator<T> GetEnumerator() => throw new Exception("Oversized inventory was enumerated before admission");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static void AllocationBounds(Generated primary)
    {
        var impossible = new NeverList<MapSourceGeometry.Input>(65536);
        for (int i = 0; i < 8; i++) Expect<InvalidDataException>(() => MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model, impossible), "warm impossible entry-count rejection");
        long before = GC.GetAllocatedBytesForCurrentThread();
        Expect<InvalidDataException>(() => MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model, impossible), "impossible count rejected before inventory allocation");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024 * 1024, "oversized entry count allocates less than1MiB warmed rejection allowance");
        Expect<InvalidDataException>(() => MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model,
            new[] { new MapSourceGeometry.Input(0, 0, new NeverList<EchoesSourceCoordinates>(65001)) }), "per-list corner bound precedes coordinate enumeration");
        byte[] header = primary.Component[..192]; Put(header, 12, 65535); Put(header, 24, 192);
        Expect<InvalidDataException>(() => MapSourceGeometry.Decode(header, primary.Model, primary.Definition.MapId, primary.Recipe), "tiny component cannot allocate declared maximum directory or coordinate arrays");
        var hostile = new ContradictoryCoordinates(primary.Snapshot.Entries[0].Corners, invalid: true);
        Expect<InvalidDataException>(() => MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model,
            new[] { new MapSourceGeometry.Input(0, 0, hostile) }), "enumerator cannot vouch for different nonfinite indexed values");
        var expected = primary.Snapshot.Entries[0].Corners.Select((v,i) => new EchoesSourceCoordinates(i+.25f, i+.5f, -.125f, .875f)).ToArray();
        var changing = new ContradictoryCoordinates(expected, invalid: false);
        byte[] encoded = MapSourceGeometry.Encode(primary.Definition.MapId, primary.Recipe, primary.Model,
            new[] { new MapSourceGeometry.Input(0, 0, changing) });
        Corners(MapSourceGeometry.Decode(encoded, primary.Model, primary.Definition.MapId, primary.Recipe).Entries[0].Corners,
            expected, "encoder serializes its validated indexed snapshot, not caller enumeration or a later reread");
        Check(changing.IndexReads == expected.Length, "coordinate list indexers are read exactly once before serialization");
        Console.WriteLine($"MAP SOURCE GEOMETRY ALLOCATION PASS: impossible inventory allocated {allocated} bytes before refusal.");
    }

    private static void TinyVisualTriangles()
    {
        static MapMesh Tiny(float origin) => new() {
            Id = Guid.NewGuid(), Solid = false,
            Vertices = new() { new[] {origin,origin,origin}, new[] {origin+.0001f,origin,origin}, new[] {origin,origin+.0001f,origin} },
            Faces = new() { new[] {0,1,2} }, SourceUvFaces = new() { Pair(10,3) }
        };
        foreach (float origin in new[] { 0f,1000f })
        {
            MapMesh mesh = Tiny(origin); string before = JsonSerializer.Serialize(mesh);
            var sourcePoints = mesh.Vertices.Select(v => new OpenTK.Mathematics.Vector3(v[0],v[1],v[2])).ToArray();
            Check(MapSourceVisualTriangle.Applies(mesh,3)
                && MapSourceVisualTriangle.TryNormal(sourcePoints[0],sourcePoints[1],sourcePoints[2],out var normal)
                && float.IsFinite(normal.LengthSquared) && Math.Abs(normal.LengthSquared-1) < .000001f
                && normal == OpenTK.Mathematics.Vector3.UnitZ,
                "finite positive tiny source visual triangle has a stable unit fallback normal at origin " + origin);
            Check(!MapMeshValidator.Validate(mesh).Any(p => p.Severity == MapDiagnosticSeverity.Error),
                "mesh validator admits exact nonzero source triangle despite legacy area/Newell cancellation at " + origin);
            BuiltFace face = GeometryCompiler.Compile(mesh,16).Single();
            Check(face.Normal == OpenTK.Mathematics.Vector3.UnitZ && face.SourceUv != null
                && face.Texcoords.All(v => v == OpenTK.Mathematics.Vector2.Zero),
                "compiler retains positive tiny source face with zero native UV and detached paired coordinates");
            for (int i = 0; i < 3; i++)
            {
                Check(Bits(face.Points[i].X) == Bits(mesh.Vertices[i][0]) && Bits(face.Points[i].Y) == Bits(mesh.Vertices[i][1])
                    && Bits(face.Points[i].Z) == Bits(mesh.Vertices[i][2]), "tiny source position float bits/winding unchanged corner " + i);
                var expected = Uv(10+i);
                Check(Bits(face.SourceUv!.Base[i].X) == Bits(expected.BaseU) && Bits(face.SourceUv.Base[i].Y) == Bits(expected.BaseV)
                    && Bits(face.SourceUv.Lightmap[i].X) == Bits(expected.LightmapU) && Bits(face.SourceUv.Lightmap[i].Y) == Bits(expected.LightmapV),
                    "tiny source paired UV float bits unchanged corner " + i);
            }
            Check(JsonSerializer.Serialize(mesh) == before, "tiny visual normal admission does not repair or mutate authored positions/UV/winding");
            MapMesh reversed = Tiny(origin); reversed.Faces[0] = new[] {0,2,1};
            Check(GeometryCompiler.Compile(reversed,16).Single().Normal == -OpenTK.Mathematics.Vector3.UnitZ,
                "tiny source helper preserves authored reversed winding rather than orienting a surface automatically");
            foreach (string policy in new[] { "legacy","solid","collision" })
            {
                MapMesh refused = Tiny(origin);
                if (policy == "legacy") refused.SourceUvFaces = null;
                else if (policy == "solid") refused.Solid = true;
                else refused.CollisionOnly = true;
                Check(!MapSourceVisualTriangle.Applies(refused,3), "source tiny policy does not apply to " + policy);
                Check(MapMeshValidator.Validate(refused).Any(p => p.Severity == MapDiagnosticSeverity.Error),
                    "original mesh validator area threshold still refuses tiny " + policy + " triangle");
                Expect<MapAuthoringException>(() => GeometryCompiler.Compile(refused,16), "original compiler threshold still refuses tiny " + policy + " triangle");
            }
            MapMesh unbound = Tiny(origin); unbound.SourceUvFaces![0] = null;
            BuiltFace unsupported = GeometryCompiler.Compile(unbound,16).Single();
            Check(unsupported.SourceUv == null && unsupported.Normal == OpenTK.Mathematics.Vector3.UnitZ
                && unsupported.Texcoords.All(v => v == OpenTK.Mathematics.Vector2.Zero),
                "explicit visual source profile preserves unsupported null-pair triangle with normal/zero native UV but no source snapshot");
        }
        var a = OpenTK.Mathematics.Vector3.Zero; var b = OpenTK.Mathematics.Vector3.One;
        foreach (var c in new[] { new OpenTK.Mathematics.Vector3(2), a,
            new OpenTK.Mathematics.Vector3(float.NaN,1,0), new OpenTK.Mathematics.Vector3(1,float.PositiveInfinity,0),
            new OpenTK.Mathematics.Vector3(1,0,float.NegativeInfinity) })
            Check(!MapSourceVisualTriangle.TryNormal(a,b,c,out _), "exact collinear/zero/nonfinite source normal still refused");
        MapMesh collinear = Tiny(0); collinear.Vertices[2] = new[] {.0002f,0f,0f};
        Check(MapMeshValidator.Validate(collinear).Any(p => p.Severity == MapDiagnosticSeverity.Error), "collinear source profile remains a mesh validation error");
        Expect<MapAuthoringException>(() => GeometryCompiler.Compile(collinear,16), "collinear source profile cannot enter built geometry");
        var quad = new MapMesh {
            Id = Guid.NewGuid(), Solid = false,
            Vertices = new() { new[] {0f,0f,0f}, new[] {.0001f,0f,0f}, new[] {.0001f,.0001f,0f}, new[] {0f,.0001f,0f} },
            Faces = new() { new[] {0,1,2,3} }, SourceUvFaces = new() { Pair(10,4) }
        };
        Check(!MapSourceVisualTriangle.Applies(quad,4)
            && MapMeshValidator.Validate(quad).Any(p => p.Severity == MapDiagnosticSeverity.Error), "source declaration does not relax nontriangle area policy");
        Expect<MapAuthoringException>(() => GeometryCompiler.Compile(quad,16), "nontriangle compiler threshold remains unchanged");
    }

    private sealed class ContradictoryCoordinates : IReadOnlyList<EchoesSourceCoordinates>
    {
        private readonly EchoesSourceCoordinates[] _values;
        private readonly bool _invalid;
        public int Count => _values.Length;
        internal int IndexReads { get; private set; }
        internal ContradictoryCoordinates(IReadOnlyList<EchoesSourceCoordinates> values, bool invalid)
        { _values = values.ToArray(); _invalid = invalid; }
        public EchoesSourceCoordinates this[int index] {
            get {
                if (++IndexReads > Count) throw new InvalidOperationException("Caller source coordinates were reread after snapshot");
                var value = _invalid ? new EchoesSourceCoordinates(float.NaN,0,0,0) : _values[index];
                if (index == Count-1) Array.Fill(_values, new EchoesSourceCoordinates(float.NaN,0,0,0));
                return value;
            }
        }
        public IEnumerator<EchoesSourceCoordinates> GetEnumerator() => Enumerable.Repeat(default(EchoesSourceCoordinates), Count).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static Generated Generate(MapDefinition definition, string directory)
    {
        var validation = MapValidator.Validate(definition);
        Check(validation.IsValid, "synthetic authoring fixture is admitted: " + string.Join(";", validation.Diagnostics.Where(d => d.Severity == MapDiagnosticSeverity.Error).Select(d => d.Message)));
        MapPacker.Generate(MapBuilder.Build(definition), directory, directory, directory, verbose: false);
        var outputs = MapOutputSet.Create(definition, directory, directory, directory);
        byte[] model = File.ReadAllBytes(outputs.Model), component = File.ReadAllBytes(outputs.SourceGeometry!);
        string recipe = MapBuildFingerprint.Create(definition).RecipeHash;
        return new(definition, outputs, model, component, recipe, MapSourceGeometry.Decode(component, model, definition.MapId, recipe));
    }
    private static MapDefinition Fixture()
    {
        var mesh = new MapMesh {
            Id = Guid.NewGuid(), Solid = false,
            Vertices = new() { new[] { 0f,0f,0f }, new[] { 1f,0f,0f }, new[] { 1f,0f,1f }, new[] { 0f,0f,1f },
                new[] { 2f,0f,0f }, new[] { 2f,0f,1f },
                new[] { 4f,0f,0f }, new[] { 6f,0f,0f }, new[] { 7f,0f,1f }, new[] { 5f,0f,2f }, new[] { 4f,0f,1f } },
            Faces = new() { new[] { 0,1,2,3 }, new[] { 0,4,5 }, new[] { 6,7,8,9,10 }, new[] { 0,5,3 } },
            FaceMaterials = new() { 0,0,0,1 }, SourceUvFaces = new() { Pair(10,4), Pair(20,3), Pair(30,5), null }
        };
        return Definition(mesh);
    }
    private static MapDefinition Many(int count, bool partition)
    {
        var mesh = new MapMesh { Id = Guid.NewGuid(), Solid = false, SourceUvFaces = new() };
        for (int i = 0; i < count; i++)
        {
            int local = partition ? i % 128 : i;
            float x = (partition ? i < 128 ? -40 : 40 : 1) + local % 10 * .2f;
            float z = 1 + local / 10 * .2f;
            int at = mesh.Vertices.Count;
            mesh.Vertices.Add(new[] { x,0f,z }); mesh.Vertices.Add(new[] { x+.1f,0f,z }); mesh.Vertices.Add(new[] { x,0f,z+.1f });
            mesh.Faces.Add(new[] { at,at+1,at+2 }); mesh.FaceMaterials.Add(partition ? i % 2 : 0);
            mesh.SourceUvFaces.Add(Pair(1000 + i * 3, 3));
        }
        var definition = Definition(mesh);
        definition.Partitioning = new() { Enabled = partition, CellSize = 16, FaceThreshold = 256, MaxVerticesPerDisplayList = 1024 };
        return definition;
    }
    private static MapDefinition Definition(MapMesh mesh) => new() {
        FormatVersion = 2, MapId = Guid.NewGuid(), Name = "SYNTHETIC_SOURCE_UV", TextureSource = "NO_SUCH_ROM_ROOM",
        Materials = new() { new MapMaterial { Id = Guid.NewGuid(), Name = "source", ProceduralWhite = true },
            new MapMaterial { Id = Guid.NewGuid(), Name = "other", ProceduralWhite = true } },
        Geometry = new() { mesh, CollisionFloor() }, Partitioning = new() { Enabled = false },
        Spawns = new() { new MapSpawn { Id = Guid.NewGuid(), Position = new[] { 0f,2f,0f } } }
    };
    private static MapMesh CollisionFloor() => new() {
        Id = Guid.NewGuid(), CollisionOnly = true, Solid = true, Material = 1, Uv = new() { Scale = new[] {.1f,.1f} },
        Vertices = new() { new[] {-100f,-1f,-100f}, new[] {100f,-1f,-100f}, new[] {100f,-1f,100f}, new[] {-100f,-1f,100f},
            new[] {-100f,0f,-100f}, new[] {100f,0f,-100f}, new[] {100f,0f,100f}, new[] {-100f,0f,100f} },
        Faces = new() { new[] {0,1,2,3}, new[] {4,7,6,5}, new[] {0,3,7,4}, new[] {1,5,6,2}, new[] {0,4,5,1}, new[] {3,2,6,7} }
    };

    // Standalone setup uses only pre-companion public APIs. It can be copied
    // into a runner compiled against the frozen pre-change DLL for a truthful
    // independent five-native-output witness; no new transport types required.
    internal static MapDefinition CreateLegacyFixture() => new() {
        FormatVersion = 2, MapId = Guid.Parse("40000000-0000-0000-0000-000000000001"), Name = "LEGACY_SOURCE_UV_WITNESS",
        TextureSource = "NO_SUCH_ROM_ROOM", Partitioning = new() { Enabled = false },
        Materials = new() { new MapMaterial { Id = Guid.Parse("40000000-0000-0000-0000-000000000002"), Name = "white", ProceduralWhite = true } },
        Brushes = new() { new MapBrush { Id = Guid.Parse("40000000-0000-0000-0000-000000000003"), Min = new[] {-4f,-1f,-4f}, Max = new[] {4f,0f,4f} } },
        Geometry = new() { new MapMesh { Id = Guid.Parse("40000000-0000-0000-0000-000000000004"), Solid = false,
            Vertices = new() { new[] {-1f,1f,0f}, new[] {1f,1f,0f}, new[] {0f,1f,1f} }, Faces = new() { new[] {0,1,2} } } },
        Spawns = new() { new MapSpawn { Id = Guid.Parse("40000000-0000-0000-0000-000000000005"), Position = new[] {0f,2f,0f} } }
    };
    private static EchoesSourceCoordinates Uv(int tag) => new(tag == 20 ? 200000.25f : tag + .125f,
        tag == 10 ? BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) : -tag - .5f, tag / 32768f, -.25f - tag / 65536f);
    private static EchoesSourceCoordinates[] TriangleUvs(int face) => Enumerable.Range(1000 + face * 3, 3).Select(Uv).ToArray();
    private static MapSourceUvFace Pair(int first, int count)
    {
        var values = Enumerable.Range(first, count).Select(Uv).ToArray();
        return new() { Base = values.Select(v => new[] { v.BaseU,v.BaseV }).ToArray(), Lightmap = values.Select(v => new[] { v.LightmapU,v.LightmapV }).ToArray() };
    }
    private static Header NativeHeader(byte[] model) => Read.ReadStruct<Header>(model.AsSpan(0, Sizes.Header));
    private static int[] VertexArguments(byte[] model, DisplayList list)
    {
        var result = new List<int>(); int at = (int)list.Offset, end = at + (int)list.Size;
        while (at < end)
        {
            byte[] codes = model.AsSpan(at,4).ToArray(); at += 4;
            foreach (byte code in codes)
            {
                int words = code switch { 0x00 or 0x41 => 0, 0x20 or 0x21 or 0x22 or 0x40 => 1, 0x23 => 2,
                    _ => throw new InvalidOperationException("Unexpected opcode in generated static fixture") };
                if (code == 0x23) result.Add(at); at += words*4;
            }
        }
        return result.ToArray();
    }
    private static void Corners(IReadOnlyList<EchoesSourceCoordinates> actual, IReadOnlyList<EchoesSourceCoordinates> expected, string why)
    {
        Check(actual.Count == expected.Count, why + " count");
        for (int i = 0; i < expected.Count; i++)
            Check(Bits(actual[i].BaseU) == Bits(expected[i].BaseU) && Bits(actual[i].BaseV) == Bits(expected[i].BaseV)
                && Bits(actual[i].LightmapU) == Bits(expected[i].LightmapU) && Bits(actual[i].LightmapV) == Bits(expected[i].LightmapV), why + " corner " + i);
    }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static void Put(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    private static void RehashDirectory(byte[] bytes) => SHA256.HashData(bytes.AsSpan(192, checked((int)U32(bytes,12))*96)).CopyTo(bytes,144);
    private static void RehashPayload(byte[] bytes, int entry)
    {
        int row = 192 + entry*96; int at = checked((int)U32(bytes,20) + (int)U32(bytes,row+20));
        SHA256.HashData(bytes.AsSpan(at, checked((int)U32(bytes,row+24)))).CopyTo(bytes,row+64);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Check(bool value, string why)
    { checks++; if (!value) throw new InvalidOperationException("Map source geometry: " + why); }
    private static void Expect<T>(Action action, string why) where T : Exception
    {
        checks++; try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Map source geometry expected " + typeof(T).Name + ": " + why);
    }
}
