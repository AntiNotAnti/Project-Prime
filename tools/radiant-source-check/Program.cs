using ReFuel.Stb;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    checks++; Console.WriteLine("PASS " + label);
}
void Refused(Action action, string label)
{
    bool refused = false;
    try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { refused = true; }
    Check(refused, label);
}
byte[] Identity(byte[] source) => JsonSerializer.SerializeToUtf8Bytes(new QuakeMapSidecar(
    1, Guid.NewGuid(), "TRANSACTION", 16, Convert.ToHexString(SHA256.HashData(source))));
MapDefinition Fixture(string name) => new()
{
    MapId = Guid.NewGuid(), FormatVersion = 3, Name = name,
    Author = "Native owner", Description = "Unchanged metadata", Version = "19",
    Materials = new() { new() { Id = Guid.NewGuid(), Name = "textures/prime/metal", Texture = "assets/metal.png",
        Albedo = "assets/metal-hd.png", Alpha = 24, TwoSided = true,
        Animation = new() { UvScroll = new[] { .1f, .2f } } } },
    Geometry = new() { new MapBox { Id = Guid.NewGuid(), Label = "Author label", Layer = "Floor", Shade = .7f, Transform = new() { Scale = new[] { 8f, 2f, 8f } } } },
    Spawns = new() { new() { Id = Guid.NewGuid(), Team = 0, Position = new[] { 0f, 4f, 0f } } },
    Items = new() { new() { Id = Guid.NewGuid(), SpawnInterval = 721, HasBase = false } },
    JumpPads = new() { new() { Id = Guid.NewGuid(), Speed = 22, Target = new[] { 4f, 6f, 8f } } },
    Audio = new() { Volume = .6f }, KillHeight = -91,
    Collision = new() { Source = "collision.obj", ZUp = true },
    Brushes = new() { new() { Id = Guid.NewGuid(), Label = "Legacy brush", Damaging = true, Min = new[] { -2f, -1f, -2f }, Max = new[] { 2f, 0f, 2f } } },
    NavigationLinks = new() { new() { Kind = MapNavigationLinkKind.Teleporter, To = new[] { 1f, 2f, 3f } } },
    Q3Triggers = new() { new() { Kind = "teleport", Min = new[] { -1f, -1f, -1f }, Max = new[] { 1f, 1f, 1f }, Gravity = 50, Destination = new[] { 4f, 5f, 6f } } }
};

string StockRadiantRewrite(string text)
{
    // A serialization regression fixture, not evidence of an actual GUI save.
    // Radiant drops ordinary comments and writes its own primitive numbering.
    int number = 0;
    return string.Join("\n", text.Replace("\r\n", "\n").Split('\n')
        .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
        .Select(line => line.Trim() == "{" ? "// brush " + number++ + "\n" + line : line));
}

if (args is ["--verify-mesh-gui", var meshGuiRoot])
{
    const string nativeAssets = "/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1";
    const string originalProject = "/Users/jarrett/Downloads/map";
    if (!Path.IsPathFullyQualified(meshGuiRoot)) throw new ArgumentException("Mesh GUI artifacts must use an absolute directory.");
    string directory = Path.GetFullPath(meshGuiRoot);
    string[] originalRoots = { originalProject, nativeAssets };
    if (originalRoots.Any(owner => directory == owner || directory.StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        throw new InvalidDataException("Mesh GUI verification must use private artifacts outside the read-only input directories.");
    var originalHashes = originalRoots.SelectMany(owner => Directory.EnumerateFiles(owner, "*", SearchOption.AllDirectories))
        .Order(StringComparer.Ordinal).ToDictionary(path => path, MapBuildFingerprint.HashFile);
    string baselinePath = Path.Combine(directory, "baseline.json"), savedPath = Path.Combine(directory, "native.prime-map.json");
    var baseline = MapDefinition.Load(baselinePath);
    var saved = MapDefinition.Load(savedPath);
    string source = Path.Combine(directory, "studio-user", "user-maps", "netradiant-sources", baseline.MapId.ToString("N"), "world.map");
    string extrudedPath = Path.Combine(directory, "gui-extruded-saved.map");
    string undoPath = Path.Combine(directory, "gui-undo-saved.map"), redoPath = Path.Combine(directory, "gui-redo-saved.map");
    string texturePath = Path.Combine(directory, "gui-fixture.tex");
    string[] suppliedPaths = { baselinePath, savedPath, source, RadiantSourceWorkspace.SidecarPath(source),
        source + ".prime-source.json", extrudedPath, undoPath, redoPath, texturePath };
    var protectedHashes = suppliedPaths.ToDictionary(path => path, MapBuildFingerprint.HashFile);
    var optionalEvidence = new[] { "process.json", "gui-extruded-saved.json", "gui-cube-extruded-saved.map", "gui-cube-extruded-saved.json" }
        .Select(name => Path.Combine(directory, name)).Where(File.Exists).ToDictionary(path => path, MapBuildFingerprint.HashFile);
    MphRead.Paths.MphKey = "AMHE1"; MphRead.Paths.SetPath("AMHE1", nativeAssets);
    string reviewRoot = Path.Combine(directory, "mesh-gui-review-" + Guid.NewGuid().ToString("N"));
    MphRead.Paths.SetPath("Export", Path.Combine(reviewRoot, "runtime", "exports"));
    CustomRooms.UserMapDirectory = Path.Combine(reviewRoot, "runtime", "user-maps");
    CustomRooms.MapDirectory = Path.Combine(reviewRoot, "runtime", "maps");
    string GeometryJson(MapGeometry geometry) => JsonSerializer.Serialize<MapGeometry>(geometry, MapPackageReader.JsonOptions);
    string MeshMetadata(MapMesh mesh)
    {
        var node = JsonSerializer.SerializeToNode<MapGeometry>(mesh, MapPackageReader.JsonOptions)!.AsObject();
        foreach (string key in new[] { "vertices", "faces", "faceMaterials", "faceTexcoords", "transform" }) node.Remove(key);
        return node.ToJsonString();
    }
    MapMesh Mesh(MapDefinition definition, string label) => definition.Geometry.OfType<MapMesh>().Single(mesh => mesh.Label == label);
    void PreservedFields(MapDefinition definition, string label)
    {
        var expected = MapProjectSerializer.Clone(baseline); expected.Geometry = definition.Geometry;
        Check(expected.Serialize() == definition.Serialize(), label + " retains every nongeometry project field, material and gameplay object");
        Check(definition.Geometry.Count == 2 && definition.Geometry.All(g => g is MapMesh)
            && definition.Geometry.Select(g => g.Id).ToHashSet().SetEquals(baseline.Geometry.Select(g => g.Id)),
            label + " retains both original native mesh identities and authoring kinds");
        foreach (var before in baseline.Geometry.Cast<MapMesh>())
        {
            var after = (MapMesh)definition.Geometry.Single(g => g.Id == before.Id);
            Check(MeshMetadata(after) == MeshMetadata(before), label + " preserves flags, label, layer, terrain, shade and native UV settings for " + before.Label);
            Check(after.FaceMaterials.Count == after.Faces.Count
                && after.FaceMaterials.Take(before.FaceMaterials.Count).SequenceEqual(before.FaceMaterials)
                && after.FaceMaterials.Skip(before.FaceMaterials.Count).All(material => material == before.Material)
                && after.FaceTexcoords.Count == after.Faces.Count
                && JsonSerializer.Serialize(after.FaceTexcoords.Take(before.FaceTexcoords.Count), MapPackageReader.JsonOptions)
                    == JsonSerializer.Serialize(before.FaceTexcoords, MapPackageReader.JsonOptions),
                label + " preserves original UV/material face bindings and binds new side faces for " + before.Label);
        }
    }
    MapDefinition SuppliedSave(string path, string label)
    {
        string copiedSource = RadiantSourceWorkspace.Prepare(baseline, Path.Combine(reviewRoot, label));
        File.WriteAllBytes(copiedSource, File.ReadAllBytes(path));
        var result = RadiantSourceWorkspace.Preview(baseline, copiedSource);
        PreservedFields(result.Proposed, label);
        return result.Proposed;
    }
    Check(baseline.Geometry.Count == 2 && baseline.Geometry.All(g => g is MapMesh)
        && Mesh(baseline, "Ground").Vertices.Count == 8 && Mesh(baseline, "Ground").Faces.Count == 6
        && Mesh(baseline, "Edit this cube").Vertices.Count == 8 && Mesh(baseline, "Edit this cube").Faces.Count == 6,
        "protected GUI baseline contains exactly the original ground and cube meshes with eight shared vertices and six faces each");
    var extruded = SuppliedSave(extrudedPath, "supplied-extrude-save");
    Check(Mesh(extruded, "Ground").Vertices.Count == 12 && Mesh(extruded, "Ground").Faces.Count == 10
        && Mesh(extruded, "Edit this cube").Vertices.Count == 8 && Mesh(extruded, "Edit this cube").Faces.Count == 6,
        "supplied earlier GUI extrude save records the ground extrusion and untouched cube topology");
    var undone = SuppliedSave(undoPath, "supplied-undo-save");
    Check(undone.Geometry.Cast<MapMesh>().All(mesh => mesh.Vertices.Count == 8 && mesh.Faces.Count == 6),
        "supplied GUI Undo save restores both original mesh topology counts");
    var redone = SuppliedSave(redoPath, "supplied-redo-save");
    Check(protectedHashes[extrudedPath] == protectedHashes[redoPath] && redone.Serialize() == extruded.Serialize(),
        "supplied GUI Redo save reproduces the extrusion bytes and full reviewed project exactly");
    PreservedFields(saved, "Studio saved GUI project");
    Check(Mesh(saved, "Ground").Vertices.Count == 8 && Mesh(saved, "Ground").Faces.Count == 6
        && Mesh(saved, "Edit this cube").Vertices.Count == 12 && Mesh(saved, "Edit this cube").Faces.Count == 10,
        "final Studio saved GUI project has the intended cube extrusion and restored ground topology");
    Check(GeometryJson(Mesh(saved, "Ground")) == GeometryJson(Mesh(baseline, "Ground")),
        "final GUI project preserves the ground exactly while cube geometry and transform may change");
    var review = RadiantSourceWorkspace.Preview(saved, source);
    Check(review.Proposed.Serialize() == saved.Serialize(), "final actual GUI source Preview equals the entire Studio saved project exactly");
    RadiantSourceWorkspace.RequireFresh(saved, review);
    string baselineJson = baseline.Serialize(), savedJson = saved.Serialize();
    var document = new MapDocument(new(MapProjectSerializer.Clone(baseline)));
    document.Edit("Apply supplied actual mesh GUI save", definition => RadiantSourceWorkspace.ApplyReviewed(definition, review));
    Check(document.Project.Definition.Serialize() == savedJson && document.History.CanUndo,
        "reviewed actual GUI geometry applies as one complete undoable native edit");
    document.History.Undo();
    Check(document.Project.Definition.Serialize() == baselineJson, "one native undo restores the entire protected pre-GUI project exactly");
    string package = MapPackageBuilder.Build(saved, Path.Combine(reviewRoot, "actual-mesh-gui.ppmap"));
    var packaged = MapDefinition.Load(package);
    Check(packaged.MapId == saved.MapId && packaged.Name == saved.Name
        && JsonSerializer.Serialize(packaged.Geometry, MapPackageReader.JsonOptions) == JsonSerializer.Serialize(saved.Geometry, MapPackageReader.JsonOptions)
        && JsonSerializer.Serialize(packaged.Spawns, MapPackageReader.JsonOptions) == JsonSerializer.Serialize(saved.Spawns, MapPackageReader.JsonOptions),
        "actual Studio GUI saved snapshot builds and reloads a valid package with exact native geometry and spawn fields");
    Check(protectedHashes.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value)
        && optionalEvidence.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value),
        "GUI verification preserves every supplied save, protected project, local texture and source ownership artifact byte exact");
    Check(originalRoots.SelectMany(owner => Directory.EnumerateFiles(owner, "*", SearchOption.AllDirectories)).Order(StringComparer.Ordinal).SequenceEqual(originalHashes.Keys)
        && originalHashes.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value),
        "GUI package verification leaves the original user project/assets and every original cartridge file and input file set unchanged");
    File.WriteAllText(Path.Combine(directory, "mesh-gui-verification.json"), JsonSerializer.Serialize(new
    {
        Evidence = "Readback of supplied actual GUI saves and Studio saved project; this CLI performs no UI interaction.",
        Baseline = baselinePath, SavedProject = savedPath, Source = source, review.SourceHash, review.SidecarHash,
        baseline.MapId, baseline.Name, GroundId = Mesh(saved, "Ground").Id, CubeId = Mesh(saved, "Edit this cube").Id,
        GroundVertices = 8, GroundFaces = 6, CubeVertices = 12, CubeFaces = 10,
        ExactSourcePreview = true, ExactOneUndo = true, Package = package, PackageSha256 = MapBuildFingerprint.HashFile(package),
        SuppliedArtifactHashes = protectedHashes, OptionalEvidenceHashes = optionalEvidence, OriginalInputHashes = originalHashes, Checks = checks
    }));
    Console.WriteLine($"Supplied actual mesh GUI save verification: {checks} checks passed; {package}");
    return;
}

if (args is ["--verify-closequarters-native-save", var closequartersSaveRoot])
{
    const string input = "/Users/jarrett/Downloads/map/map.json";
    const string nativeAssets = "/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1";
    if (!Path.IsPathFullyQualified(closequartersSaveRoot)) throw new ArgumentException("Native save artifact directory must be absolute.");
    string directory = Path.GetFullPath(closequartersSaveRoot);
    string inputOwner = Path.GetDirectoryName(input)!;
    if (directory == inputOwner || directory.StartsWith(inputOwner + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || directory == nativeAssets || directory.StartsWith(nativeAssets + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidDataException("Native save verification must use private artifacts outside the read-only input directories.");
    using var prepared = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "closequarters-prepared.json")));
    var metadata = prepared.RootElement;
    var inputHashes = metadata.GetProperty("InputHashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    Check(metadata.GetProperty("Input").GetString() == input && inputHashes.All(value => File.Exists(value.Key) && MapBuildFingerprint.HashFile(value.Key) == value.Value),
        "actual native save verification starts with the original CLOSE QUARTERS project and assets byte exact");
    string originalSource = metadata.GetProperty("Source").GetString()!;
    string originalSourceHash = metadata.GetProperty("SourceHash").GetString()!;
    string actualSave = Path.Combine(Path.GetDirectoryName(originalSource)!, "native-saved.map");
    if (!File.Exists(actualSave) || File.GetAttributes(actualSave).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(actualSave).Length is <= 0 or > 64L * 1024 * 1024)
        throw new InvalidDataException("The actual native writer must save the prepared map's sibling native-saved.map first.");
    Check(MapBuildFingerprint.HashFile(originalSource) == originalSourceHash, "the prepared authoritative world.map remains unchanged by native save-copy");
    string actualHash = MapBuildFingerprint.HashFile(actualSave);
    string recipe = Path.Combine(directory, "native.prime-map.json");
    var protectedArtifacts = new[] { originalSource, RadiantSourceWorkspace.SidecarPath(originalSource), originalSource + ".prime-source.json", recipe, actualSave }
        .ToDictionary(path => path, MapBuildFingerprint.HashFile);
    MphRead.Paths.MphKey = "AMHE1"; MphRead.Paths.SetPath("AMHE1", nativeAssets);
    MphRead.Paths.SetPath("Export", Path.Combine(directory, "runtime", "exports"));
    CustomRooms.UserMapDirectory = Path.Combine(directory, "runtime", "user-maps");
    CustomRooms.MapDirectory = Path.Combine(directory, "runtime", "maps");
    var current = MapDefinition.Load(recipe); current.BaseDirectory = inputOwner;
    string before = current.Serialize();
    Check(current.MapId == metadata.GetProperty("MapId").GetGuid() && current.Geometry.Count == 5
        && current.Geometry.OfType<MapMesh>().Sum(mesh => mesh.Vertices.Count) == 41852
        && current.Geometry.OfType<MapMesh>().Sum(mesh => mesh.Faces.Count) == 20462,
        "protected snapshot has all five native meshes, 41,852 shared vertices and 20,462 polygons");
    string reviewRoot = Path.Combine(directory, "native-save-review-" + Guid.NewGuid().ToString("N"));
    string source = RadiantSourceWorkspace.Prepare(current, reviewRoot);
    string freshPreparedHash = MapBuildFingerprint.HashFile(source);
    File.WriteAllBytes(source, File.ReadAllBytes(actualSave));
    var review = RadiantSourceWorkspace.Preview(current, source);
    Check(review.SourceHash == actualHash && review.Proposed.Serialize() == before,
        "actual native map writer preserves the entire project exactly, including all shared geometry, UVs, materials, flags and spawn fields");
    RadiantSourceWorkspace.RequireFresh(current, review);
    var document = new MapDocument(new(current));
    document.Edit("Apply exact native map save", definition => RadiantSourceWorkspace.ApplyReviewed(definition, review));
    bool undoAvailable = document.History.CanUndo;
    Check(document.Project.Definition.Serialize() == before, "applying an unchanged native save is an exact project no-op");
    if (undoAvailable) document.History.Undo();
    Check(document.Project.Definition.Serialize() == before, "native save no-op apply and optional undo retain the exact protected snapshot");
    Check(current.Brushes.Count == 0 && current.Geometry.All(geometry => !geometry.Solid),
        "original all-nonsolid authoring state is preserved without changing collision flags");
    string package = Path.Combine(reviewRoot, "unchanged-native-save.ppmap");
    bool expectedNonSolidFailure = false;
    string? expectedValidationMessage = null;
    string? packageExceptionCode = null;
    try { MapPackageBuilder.Build(review.Proposed, package); }
    catch (MapAuthoringException ex) when (
        (ex.Code == "FP-MAP-013" && ex.Message == "At least one solid face is required.")
        || (ex.Code == "FP-MAP-019" && ex.Message == "FP-MAP-013: At least one solid face is required."))
    { expectedNonSolidFailure = true; expectedValidationMessage = ex.Message; packageExceptionCode = ex.Code; }
    Check(expectedNonSolidFailure && !File.Exists(package),
        "canonical package validation retains the expected pre-existing FP-MAP-013 nonsolid-source rejection; no playable package is claimed");
    Check(inputHashes.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value)
        && protectedArtifacts.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value),
        "original project/assets, actual native save-copy, prepared source, sidecar, adjunct and protected snapshot remain byte exact");
    string receipt = Path.Combine(directory, "closequarters-native-save-verification.json");
    File.WriteAllText(receipt, JsonSerializer.Serialize(new
    {
        ActualNativeSavedCopy = actualSave, ActualNativeSavedCopySha256 = actualHash,
        PreparedSource = originalSource, PreparedSourceSha256 = originalSourceHash,
        FreshPreparedSourceSha256 = freshPreparedHash, ReviewSource = source, review.SourceHash,
        CurrentProjectSha256 = MapBuildFingerprint.HashText(before), current.MapId, current.Name,
        Meshes = 5, Vertices = 41852, Faces = 20462, Spawns = current.Spawns.Count,
        ExactWholeProjectRoundtrip = true, NoOpApply = true, UndoAvailableAfterNoOp = undoAvailable,
        ExpectedValidationCode = "FP-MAP-013", PackageExceptionCode = packageExceptionCode,
        ExpectedValidationMessage = expectedValidationMessage,
        PlayablePackageBuilt = false, InputHashes = inputHashes, ProtectedArtifactHashes = protectedArtifacts, Checks = checks
    }));
    Console.WriteLine($"Actual CLOSE QUARTERS native save-copy: {checks} checks passed; {receipt}");
    return;
}

if (args is ["--verify-native-reference", var savedReferenceRoot])
{
    const string nativeAssets = "/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1";
    if (!Path.IsPathFullyQualified(savedReferenceRoot)) throw new ArgumentException("Native reference artifact directory must be absolute.");
    string directory = Path.GetFullPath(savedReferenceRoot);
    if (directory == nativeAssets || directory.StartsWith(nativeAssets + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidDataException("Native reference verification artifacts must stay outside the read-only extraction.");
    using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "native-reference-verification.json")));
    string recipe = receipt.RootElement.GetProperty("Recipe").GetString()!;
    string recipeHash = receipt.RootElement.GetProperty("RecipeSha256").GetString()!;
    var cartridgeHashes = receipt.RootElement.GetProperty("CartridgeHashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    string actualSave = Path.Combine(directory, "native-saved.map");
    if (!File.Exists(actualSave) || File.GetAttributes(actualSave).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(actualSave).Length is <= 0 or > 64L * 1024 * 1024)
        throw new InvalidDataException("Save the real native writer output to this private artifact directory's native-saved.map first.");
    string actualHash = MapBuildFingerprint.HashFile(actualSave);
    MphRead.Paths.MphKey = "AMHE1"; MphRead.Paths.SetPath("AMHE1", nativeAssets);
    MphRead.Paths.SetPath("Export", Path.Combine(directory, "runtime", "exports"));
    CustomRooms.UserMapDirectory = Path.Combine(directory, "runtime", "user-maps");
    CustomRooms.MapDirectory = Path.Combine(directory, "runtime", "maps");
    var definition = MapDefinition.Load(recipe);
    string before = definition.Serialize();
    Check(MapBuildFingerprint.HashFile(recipe) == recipeHash && definition.NativeRoom?.PreserveEntities == false && definition.Spawns.Count == 1,
        "native writer verification uses the protected one-spawn recipe with native entity preservation disabled");
    var identities = receipt.RootElement.GetProperty("References").Deserialize<RadiantReferenceScene.Identity[]>()!
        .ToDictionary(value => value.Id, value => value.PayloadHash);
    string reviewRoot = Path.Combine(directory, "native-save-review-" + Guid.NewGuid().ToString("N"));
    string source = RadiantSourceWorkspace.Prepare(definition, reviewRoot);
    File.WriteAllBytes(source, File.ReadAllBytes(actualSave));
    var direct = QuakeMapInterchange.Import(source, RadiantSourceWorkspace.SidecarPath(source),
        requireDurableGeometryContainers: true, expectedReferences: identities);
    Check(direct.Definition.Geometry.Count == 0 && direct.Definition.Brushes.Count == 0 && direct.Definition.Materials.Count == 0
        && direct.Definition.Spawns.Count == 1 && direct.Definition.Spawns[0].Id == definition.Spawns[0].Id,
        "actual native writer preserves every retained identity and excludes immutable reference materials and geometry from owned lists");
    var review = RadiantSourceWorkspace.Preview(definition, source);
    Check(review.Proposed.Serialize() == before, "actual native writer roundtrip preserves the entire native recipe and exact authored spawn fields");
    RadiantSourceWorkspace.RequireFresh(definition, review);
    Check(MapBuildFingerprint.HashFile(actualSave) == actualHash && MapBuildFingerprint.HashFile(recipe) == recipeHash,
        "native save verification preserves actual writer output and protected recipe bytes");
    Check(Directory.EnumerateFiles(nativeAssets, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).SequenceEqual(cartridgeHashes.Keys)
        && cartridgeHashes.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value),
        "actual native writer roundtrip leaves every original cartridge file and input file set unchanged");
    File.WriteAllText(Path.Combine(directory, "native-save-verification.json"), JsonSerializer.Serialize(new
    {
        ActualSource = actualSave, ActualSourceSha256 = actualHash, ReviewSource = source, review.SourceHash,
        Recipe = recipe, RecipeSha256 = recipeHash, References = identities, Checks = checks
    }));
    Console.WriteLine($"Actual native reference save: {checks} checks passed.");
    return;
}

if (args is ["--prepare-native-reference", var referenceArtifactRoot])
{
    const string nativeAssets = "/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1";
    if (!Path.IsPathFullyQualified(referenceArtifactRoot)) throw new ArgumentException("Native reference artifacts must use an absolute directory.");
    string directory = Path.GetFullPath(referenceArtifactRoot);
    if (directory == nativeAssets || directory.StartsWith(nativeAssets + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || Directory.Exists(directory) || File.Exists(directory))
        throw new IOException("Native reference preparation requires a new private directory outside the cartridge extraction.");
    if (!Directory.Exists(nativeAssets)) throw new DirectoryNotFoundException("The read-only local AMHE1 extraction is required.");
    MphRead.Paths.MphKey = "AMHE1"; MphRead.Paths.SetPath("AMHE1", nativeAssets);
    MphRead.Paths.SetPath("Export", Path.Combine(directory, "runtime", "exports"));
    CustomRooms.UserMapDirectory = Path.Combine(directory, "runtime", "user-maps");
    CustomRooms.MapDirectory = Path.Combine(directory, "runtime", "maps");
    var cartridgeHashes = Directory.EnumerateFiles(nativeAssets, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(path => path, MapBuildFingerprint.HashFile);
    Console.WriteLine("Protected read-only cartridge inputs: " + cartridgeHashes.Count);
    try
    {
        var definition = NativeRoomProject.Create("MP3 PROVING GROUND", "REFERENCE_NATIVE").Definition;
        definition.NativeRoom!.PreserveEntities = false;
        definition.Items.Clear(); definition.JumpPads.Clear(); definition.NavigationLinks.Clear(); definition.Q3Triggers = null;
        if (definition.Spawns.Count == 0) throw new InvalidDataException("Native project creation must expose at least one authored spawn.");
        definition.Spawns = new() { definition.Spawns[0] };
        definition.Spawns[0].Label = "Authored spawn with native entity preservation disabled";
        Directory.CreateDirectory(directory); definition.BaseDirectory = directory;
        string before = definition.Serialize();
        string recipe = Path.Combine(directory, "native.prime-map.json"); File.WriteAllText(recipe, before);
        string recipeHash = MapBuildFingerprint.HashFile(recipe);
        string source = RadiantSourceWorkspace.Prepare(definition, directory);
        byte[] sourceBytes = File.ReadAllBytes(source);
        string sourceText = Encoding.UTF8.GetString(sourceBytes).Replace("\r\n", "\n");
        var expected = RadiantReferenceScene.Prepare(definition, source);
        var identities = expected.Identities.ToDictionary(value => value.Id, value => value.PayloadHash);
        Check(identities.Count > 0 && expected.Images.Length > 0 && sourceText.Contains("\"prime_reference\" \"1\""),
            "real native room with PreserveEntities=false and one authored spawn exports visible reference architecture");
        var direct = QuakeMapInterchange.Import(source, RadiantSourceWorkspace.SidecarPath(source), requireDurableGeometryContainers: true, expectedReferences: identities);
        Check(direct.Definition.Geometry.Count == 0 && direct.Definition.Brushes.Count == 0 && direct.Definition.Materials.Count == 0
            && direct.Definition.Spawns.Count == 1 && direct.Definition.Spawns[0].Id == definition.Spawns[0].Id,
            "real retained polygons and material tables stay outside owned geometry while the authored spawn survives");
        Check(RadiantSourceWorkspace.Preview(definition, source).Proposed.Serialize() == before,
            "real native reference workspace preserves every canonical recipe field exactly");
        string textureRoot = RadiantTexturePreview.Prepare(definition, source, out var unavailable);
        Check(unavailable.Count == 0, "all real native material previews decode from the read-only extraction");
        Check(Directory.GetFiles(textureRoot, "*.png", SearchOption.AllDirectories).Length == definition.Materials.Count + expected.Images.Length,
            "real native preview has one authored-material image and one reference image for every expected binding");
        foreach (var image in expected.Images)
        {
            string pngPath = Path.Combine(textureRoot, "textures", image.Shader.Replace('/', Path.DirectorySeparatorChar) + ".png");
            using var pngStream = File.OpenRead(pngPath);
            using var png = ReFuel.Stb.StbImage.Load(pngStream, StbiImageFormat.Rgba);
            int width = image.Material.CoordinateWidth > 0 ? image.Material.CoordinateWidth : image.Material.Width;
            int height = image.Material.CoordinateHeight > 0 ? image.Material.CoordinateHeight : image.Material.Height;
            Check(png.Width == width && png.Height == height, "retained PNG keeps native UV coordinate dimensions for " + image.Shader);
            byte[] rgba = new byte[checked(width * height * 4)];
            System.Runtime.InteropServices.Marshal.Copy(png.ImagePointer, rgba, 0, rgba.Length);
            bool exactPixels = true;
            for (int y = 0; y < height && exactPixels; y++) for (int x = 0; x < width; x++)
            {
                var pixel = image.Material.Pixels[(int)((long)y * image.Material.Height / height) * image.Material.Width + (int)((long)x * image.Material.Width / width)];
                int offset = (y * width + x) * 4;
                if (rgba[offset] != pixel.Red || rgba[offset + 1] != pixel.Green || rgba[offset + 2] != pixel.Blue || rgba[offset + 3] != pixel.Alpha)
                { exactPixels = false; break; }
            }
            Check(exactPixels, "retained PNG RGBA matches the native palette and alpha for " + image.Shader);
        }
        string normalized = StockRadiantRewrite(sourceText);
        File.WriteAllText(source, normalized);
        var normalizedReview = RadiantSourceWorkspace.Preview(definition, source);
        Check(normalizedReview.Proposed.Serialize() == before && normalizedReview.Diagnostics.All(d => !d.Error),
            "real typed reference hashes survive source comment removal and primitive numbering without recipe changes");
        File.WriteAllText(Path.Combine(directory, "normalized-native-save.map"), normalized);
        Guid firstReference = expected.Identities[0].Id;
        int property = sourceText.IndexOf("\"prime_id\" \"" + firstReference + "\"", StringComparison.Ordinal);
        int primitiveStart = sourceText.IndexOf("primeMeshDef", property, StringComparison.Ordinal);
        int primitiveEnd = sourceText.IndexOf("\n}", primitiveStart, StringComparison.Ordinal) + 3;
        string primitive = sourceText[primitiveStart..primitiveEnd];
        string encoded = string.Concat(primitive.Split('\n').Skip(4).TakeWhile(line => line.Trim() != "}").Select(line => line.Trim().Trim('\"')));
        var json = System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String(encoded))!.AsObject();
        json["mesh"]!["locked"] = false;
        string unlockedData = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(json));
        string unlockedPrimitive = "primeMeshDef\n{\nversion 1\ndata " + ((unlockedData.Length + 767) / 768) + "\n"
            + string.Join("\n", Enumerable.Range(0, (unlockedData.Length + 767) / 768).Select(i => "\"" + unlockedData.Substring(i * 768, Math.Min(768, unlockedData.Length - i * 768)) + "\"")) + "\n}\n";
        File.WriteAllText(source, sourceText.Replace(primitive, unlockedPrimitive));
        Refused(() => RadiantSourceWorkspace.Preview(definition, source), "unlocking actual retained cartridge architecture fails strict source review");
        File.WriteAllBytes(source, sourceBytes);
        Check(definition.Serialize() == before && MapBuildFingerprint.HashFile(recipe) == recipeHash,
            "reference preparation, preview and rejected source edits leave the native recipe bytes unchanged");
        var imageHashes = Directory.GetFiles(textureRoot, "*.png", SearchOption.AllDirectories).ToDictionary(path => path, MapBuildFingerprint.HashFile);
        File.WriteAllText(Path.Combine(directory, "native-reference-verification.json"), JsonSerializer.Serialize(new
        {
            SourceRoom = "MP3 PROVING GROUND", definition.NativeRoom!.PreserveEntities, AuthoredSpawns = definition.Spawns.Count,
            Source = source, SourceSha256 = MapBuildFingerprint.HashFile(source), Recipe = recipe, RecipeSha256 = recipeHash,
            References = expected.Identities, ReferenceImages = expected.Images.Select(i => i.Shader).ToArray(),
            expected.DependencyHash, ImageHashes = imageHashes, CartridgeHashes = cartridgeHashes, Checks = checks + 1
        }));
        File.WriteAllText(Path.Combine(directory, "source-path.txt"), source);
        Console.WriteLine("Real retained native source: " + source);
    }
    finally
    {
        Check(Directory.EnumerateFiles(nativeAssets, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).SequenceEqual(cartridgeHashes.Keys)
            && cartridgeHashes.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value),
            "every original cartridge file and the complete input file set remain byte exact");
    }
    Console.WriteLine($"Real native reference preparation: {checks} checks passed.");
    return;
}

// The author-provided CLOSE QUARTERS source and assets are inputs only.
// Every generated source, receipt and package belongs to the new artifact root.
if (args.Length == 2 && args[0] is "--prepare-closequarters" or "--verify-closequarters")
{
    const string input = "/Users/jarrett/Downloads/map/map.json";
    if (!Path.IsPathFullyQualified(args[1])) throw new ArgumentException("Artifact directory must be absolute.");
    string directory = Path.GetFullPath(args[1]);
    string owner = Path.GetDirectoryName(input)!;
    if (directory == owner || directory.StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidDataException("CLOSE QUARTERS artifacts must be outside the read-only input directory.");
    CustomRooms.UserMapDirectory = Path.Combine(directory, "runtime", "user-maps");
    CustomRooms.MapDirectory = Path.Combine(directory, "runtime", "maps");
    string receiptPath = Path.Combine(directory, "closequarters-prepared.json");
    // Native room materials are borrowed read-only from the existing local
    // extraction. All writable compiler/runtime destinations stay private.
    MphRead.Paths.MphKey = "AMHE1";
    MphRead.Paths.SetPath("AMHE1", "/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1");
    MphRead.Paths.SetPath("Export", Path.Combine(directory, "runtime", "exports"));
    if (args[0] == "--prepare-closequarters")
    {
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Preparation requires a new private artifact directory.");
        var inputHashes = Directory.EnumerateFiles(owner, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, MapBuildFingerprint.HashFile);
        var current = MapDefinition.Load(input);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "native.prime-map.json"), current.Serialize());
        string source = RadiantSourceWorkspace.Prepare(current, directory);
        Check(current.Geometry.All(g => g is MapMesh), "actual CLOSE QUARTERS source contains native shared polygon meshes");
        Check(RadiantSourceWorkspace.Preview(current, source).Proposed.Serialize() == current.Serialize(),
            "actual CLOSE QUARTERS unchanged workspace preserves every canonical project field");
        var imported = QuakeMapInterchange.Import(source, RadiantSourceWorkspace.SidecarPath(source), requireDurableGeometryContainers: true);
        Check(imported.PrimeMeshIds.Count == current.Geometry.Count, "all actual CLOSE QUARTERS meshes use native primeMeshDef payloads");
        string textureRoot = RadiantTexturePreview.Prepare(current, source);
        string[] images = Directory.GetFiles(textureRoot, "*.png", SearchOption.AllDirectories);
        Check(images.Length == 7 && images.Length == current.Materials.Count, "private CLOSE QUARTERS texture preview contains all seven original native material bindings");
        var viewportMaterials = MapViewportMaterials.Resolve(current);
        var descriptions = PrimeMeshInterchange.DescribeMaterials(current);
        for (int i = 0; i < current.Materials.Count; i++)
        {
            string name = current.Materials[i].Name;
            if (name.StartsWith("textures/", StringComparison.Ordinal)) name = name[9..];
            byte[] png = File.ReadAllBytes(Path.Combine(textureRoot, "textures", name + ".png"));
            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
            var material = viewportMaterials[(false, i)];
            int expectedWidth = material.CoordinateWidth > 0 ? material.CoordinateWidth : material.Width;
            int expectedHeight = material.CoordinateHeight > 0 ? material.CoordinateHeight : material.Height;
            Check(width == expectedWidth && height == expectedHeight && descriptions[i].Width == width && descriptions[i].Height == height,
                "preview PNG and mesh UV metadata retain native coordinate dimensions for " + current.Materials[i].Name);
        }
        Check(inputHashes.All(value => MapBuildFingerprint.HashFile(value.Key) == value.Value), "preparation leaves original project and every input asset byte exact");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            Input = input, InputHashes = inputHashes, TextureRoot = textureRoot, Source = source, SourceHash = MapBuildFingerprint.HashFile(source),
            current.MapId, current.Name, GeometryIds = current.Geometry.Select(g => g.Id).ToArray(),
            Vertices = current.Geometry.OfType<MapMesh>().Sum(g => g.Vertices.Count),
            Faces = current.Geometry.OfType<MapMesh>().Sum(g => g.Faces.Count), Checks = checks
        }));
        File.WriteAllText(Path.Combine(directory, "source-path.txt"), source);
        Console.WriteLine("Actual CLOSE QUARTERS source: " + source);
        Console.WriteLine("Read-only original: " + input);
    }
    else
    {
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        var values = receipt.RootElement;
        string source = values.GetProperty("Source").GetString()!;
        var inputHashes = values.GetProperty("InputHashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        Check(values.GetProperty("Input").GetString() == input && inputHashes.All(v => File.Exists(v.Key) && MapBuildFingerprint.HashFile(v.Key) == v.Value),
            "actual project and all original assets still match the protected preparation hashes");
        var current = MapDefinition.Load(Path.Combine(directory, "native.prime-map.json"));
        current.BaseDirectory = owner;
        Check(current.MapId == values.GetProperty("MapId").GetGuid(), "private native snapshot retains original MapId");
        var review = RadiantSourceWorkspace.Preview(current, source);
        var originalIds = current.Geometry.Select(g => g.Id).ToHashSet();
        Check(originalIds.IsSubsetOf(review.Proposed.Geometry.Select(g => g.Id).ToHashSet()), "actual editor save retains all original CLOSE QUARTERS objects");
        Check(review.Proposed.Geometry.All(g => g is MapMesh), "actual editor save retains native polygon mesh authoring");
        var expected = MapProjectSerializer.Clone(current);
        expected.Geometry = review.Proposed.Geometry; expected.Materials = review.Proposed.Materials;
        Check(expected.Serialize() == review.Proposed.Serialize(), "actual editor review retains every project and gameplay domain");
        RadiantSourceWorkspace.RequireFresh(current, review);
        var document = new MapDocument(new(current));
        string before = current.Serialize();
        document.Edit("Apply actual CLOSE QUARTERS source", d => { RadiantSourceWorkspace.ApplyReviewed(d, review); });
        if (document.History.CanUndo) document.History.Undo();
        Check(document.Project.Definition.Serialize() == before, "one undo restores the exact original CLOSE QUARTERS project");
        string package = MapPackageBuilder.Build(review.Proposed, Path.Combine(directory, "closequarters-reviewed.ppmap"));
        var loaded = MapDefinition.Load(package);
        Check(loaded.MapId == current.MapId && loaded.Geometry.Select(g => g.Id).ToHashSet().SetEquals(review.Proposed.Geometry.Select(g => g.Id)),
            "actual reviewed CLOSE QUARTERS builds and reloads with every mesh identity");
        Check(inputHashes.All(v => MapBuildFingerprint.HashFile(v.Key) == v.Value), "verification leaves original project and every input asset byte exact");
        File.WriteAllText(Path.Combine(directory, "closequarters-verification.json"), JsonSerializer.Serialize(new
        {
            Input = input, Source = source, review.SourceHash, review.SidecarHash, current.MapId, current.Name,
            SourceChanged = review.SourceHash != values.GetProperty("SourceHash").GetString(),
            GeometryIds = review.Proposed.Geometry.Select(g => g.Id).ToArray(),
            ChangedGeometryIds = current.Geometry.Where(g => JsonSerializer.Serialize<MapGeometry>(g, MapPackageReader.JsonOptions)
                != JsonSerializer.Serialize<MapGeometry>(review.Proposed.Geometry.Single(p => p.Id == g.Id), MapPackageReader.JsonOptions)).Select(g => g.Id).ToArray(),
            Package = package, PackageSha256 = MapBuildFingerprint.HashFile(package), InputHashes = inputHashes, Checks = checks
        }));
        Console.WriteLine($"Actual CLOSE QUARTERS verification: {checks} checks passed; {package}");
    }
    return;
}

if (args is ["--verify-authoring", var fixtureRoot])
{
    if (!Path.IsPathFullyQualified(fixtureRoot)) throw new ArgumentException("GUI fixture directory must be absolute.");
    string directory = Path.GetFullPath(fixtureRoot);
    CustomRooms.UserMapDirectory = Path.Combine(directory,"runtime","user-maps");
    CustomRooms.MapDirectory = Path.Combine(directory,"runtime","maps");
    string native = Path.Combine(directory,"native.prime-map.json");
    var current = MapDefinition.Load(native);
    string originalSource = RadiantSourceWorkspace.SourcePath(directory,current.MapId);
    string authored = Path.Combine(Path.GetDirectoryName(originalSource)!,"authoring.map");
    if (!File.Exists(authored) || File.GetAttributes(authored).HasFlag(FileAttributes.ReparsePoint)
        || new FileInfo(authored).Length is <= 0 or > 32L*1024*1024)
        throw new InvalidDataException("Save the actual Radiant Duplicate and newly drawn brush to the fixture's authoring.map first.");
    var inputs = new[] { native, originalSource, RadiantSourceWorkspace.SidecarPath(originalSource),
        originalSource+".prime-source.json", authored }.ToDictionary(path=>path,MapBuildFingerprint.HashFile);
    byte[] actualSave = File.ReadAllBytes(authored);
    // Recreate the canonical baseline privately, then replace only this synthetic
    // source with the exact GUI-authored bytes. Never rewrite the actual save or
    // the previously verified source's native adjunct.
    string reviewRoot = Path.Combine(directory,"authoring-review-"+Guid.NewGuid().ToString("N"));
    string source = RadiantSourceWorkspace.Prepare(current,reviewRoot);
    File.WriteAllBytes(source,actualSave);
    var review = RadiantSourceWorkspace.Preview(current,source);
    Check(review.Proposed.MapId==current.MapId && review.Proposed.Name==current.Name,
        "actual authoring save retains MapId and runtime name through strict v2 review");
    var originalIds = current.Geometry.Select(g=>g.Id).ToHashSet();
    var ids = review.Proposed.Geometry.Select(g=>g.Id).ToArray();
    var added = review.Proposed.Geometry.Where(g=>!originalIds.Contains(g.Id)).ToArray();
    Check(originalIds.IsSubsetOf(ids.ToHashSet()),"actual authoring save retains every original geometry ID");
    Check(added.Length>=2 && ids.All(id=>id!=Guid.Empty) && ids.Distinct().Count()==ids.Length,
        "actual Duplicate plus newly drawn geometry have at least two distinct new durable identities");
    foreach(var before in current.Geometry)
    {
        var after = review.Proposed.Geometry.Single(g=>g.Id==before.Id);
        Check(after.Label==before.Label && after.Layer==before.Layer && after.Locked==before.Locked
            && after.Terrain==before.Terrain && after.Shade==before.Shade,
            "actual authoring save preserves native metadata for "+before.Label);
        if(before is MapBezierPatch beforePatch)
            Check(after is MapBezierPatch afterPatch && afterPatch.Solid==beforePatch.Solid
                && afterPatch.Tessellation==beforePatch.Tessellation,"actual authoring save preserves original patch intent");
    }
    Check(added.All(g=>GeometryCompiler.Compile(g,review.Proposed.Materials[g.Material].TexScale).Count>0),
        "actual added objects contain compilable geometry rather than identity-only containers");
    var expected = MapProjectSerializer.Clone(current);
    expected.Geometry=review.Proposed.Geometry;expected.Materials=review.Proposed.Materials;
    Check(expected.Serialize()==review.Proposed.Serialize(),"actual additions retain all native gameplay and project domains");
    var validation = MapValidator.Validate(review.Proposed);
    MapCompiler.ThrowIfInvalid(validation);
    Check(validation.IsValid,"actual authored geometry passes canonical source validation");
    string package = MapPackageBuilder.Build(review.Proposed,Path.Combine(reviewRoot,"authoring-reviewed.ppmap"));
    var reloaded = MapDefinition.Load(package);
    Check(reloaded.MapId==current.MapId && reloaded.Name==current.Name
        && reloaded.Geometry.Select(g=>g.Id).ToHashSet().SetEquals(ids),
        "actual authoring additions build and reload with every original and added identity");
    Check(inputs.All(input=>MapBuildFingerprint.HashFile(input.Key)==input.Value)
        && MapBuildFingerprint.HashFile(source)==inputs[authored],
        "authoring verification preserves actual save, prior source, sidecar, native adjunct and project bytes");
    string receipt = Path.Combine(reviewRoot,"authoring-verification.json");
    File.WriteAllText(receipt,JsonSerializer.Serialize(new
    {
        ActualSource=authored,ActualSourceSha256=inputs[authored],ReviewSource=source,review.SourceHash,
        current.MapId,current.Name,OriginalGeometryIds=originalIds,AddedGeometryIds=added.Select(g=>g.Id).ToArray(),
        Package=package,PackageHash=MapBuildFingerprint.HashFile(package),InputHashes=inputs,Checks=checks
    }));
    Console.WriteLine($"Actual GUI authoring verification: {checks} checks passed; {receipt}");
    return;
}

if (args.Length == 2 && args[0] is "--prepare-gui" or "--verify-gui")
{
    if (!Path.IsPathFullyQualified(args[1])) throw new ArgumentException("GUI fixture directory must be absolute.");
    string directory = Path.GetFullPath(args[1]);
    string native = Path.Combine(directory, "native.prime-map.json");
    // The GUI oracle builds packages, but never writes a user's generated maps.
    CustomRooms.UserMapDirectory = Path.Combine(directory,"runtime","user-maps");
    CustomRooms.MapDirectory = Path.Combine(directory,"runtime","maps");
    if (args[0] == "--prepare-gui")
    {
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("GUI fixture preparation requires a new directory; existing content is never replaced.");
        Directory.CreateDirectory(directory);
        var fixture = MapTemplates.Create("RADIANT_GUI_SAVE", true).Definition;
        fixture.BaseDirectory = directory;
        fixture.TextureSource = "";
        const string textureName = "gui-fixture.tex";
        using (var output = new BinaryWriter(File.Create(Path.Combine(directory,textureName))))
        {
            output.Write(Encoding.ASCII.GetBytes("FPTX"));output.Write((ushort)1);output.Write((ushort)1);
            output.Write((ushort)0);output.Write((ushort)8);output.Write((ushort)8);output.Write((ushort)1);output.Write((ushort)0);
            output.Write((ushort)32767);output.Write(new byte[64]);
        }
        foreach (var material in fixture.Materials) material.Texture = textureName;
        fixture.Assets.Add(new(){Path=textureName});
        fixture.Author = "GUI save native owner"; fixture.Description = "Retain native gameplay and authoring metadata";
        fixture.Version = "19";
        foreach (var geometry in fixture.Geometry)
        {
            geometry.Label = "Native " + geometry.Label; geometry.Layer = "Native architecture";
            geometry.Locked = true; geometry.Terrain = "Sand"; geometry.Shade = .7f;
        }
        var patch = new MapBezierPatch
        {
            Label = "Native patch", Layer = "Native surfaces", Locked = true,
            Terrain = "Rock", Shade = .8f, Solid = false, Tessellation = 2
        };
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                patch.ControlPoints.Add(new[] { (float)col, col == 1 && row == 1 ? 1f : 0f, (float)row, col / 2f, row / 2f });
        fixture.Geometry.Add(patch);
        // Catch invalid fixture content before asking a human to repeat a GUI
        // edit. The edited-source oracle still builds its own package later.
        MapPackageBuilder.Build(fixture, Path.Combine(directory,"prepared-fixture.ppmap"));
        File.WriteAllText(native, fixture.Serialize());
        string source = RadiantSourceWorkspace.Prepare(fixture, directory);
        File.WriteAllText(Path.Combine(directory, "source-path.txt"), source);
        Console.WriteLine("Native fixture: " + native);
        Console.WriteLine("Open in Radiant, move an existing box, then Save: " + source);
        Console.WriteLine("Verify the actual GUI save with --verify-gui " + directory);
    }
    else
    {
        var current = MapDefinition.Load(native);
        string source = RadiantSourceWorkspace.SourcePath(directory, current.MapId);
        Check(RadiantSourceWorkspace.State(current, source) == RadiantSourceState.ExternalChanges,
            "actual GUI source differs from the protected export");
        var review = RadiantSourceWorkspace.Preview(current, source);
        Check(review.Proposed.MapId == current.MapId && review.Proposed.Name == current.Name,
            "GUI save retains MapId and runtime name");
        Check(current.Geometry.Select(g => g.Id).ToHashSet().SetEquals(review.Proposed.Geometry.Select(g => g.Id)),
            "GUI save retains the exact geometry ID set");
        foreach (var original in current.Geometry)
        {
            var geometry = review.Proposed.Geometry.Single(g => g.Id == original.Id);
            Check(geometry.Label == original.Label && geometry.Layer == original.Layer
                && geometry.Locked == original.Locked && geometry.Terrain == original.Terrain && geometry.Shade == original.Shade,
                "GUI save retains label, layer, locked, terrain and shade for " + original.Label);
            if (original is MapBezierPatch beforePatch)
                Check(geometry is MapBezierPatch afterPatch && afterPatch.Solid == beforePatch.Solid
                    && afterPatch.Tessellation == beforePatch.Tessellation, "GUI save retains patch collision and tessellation");
        }
        bool moved = current.Geometry.Any(before => JsonSerializer.Serialize<MapGeometry>(before, MapPackageReader.JsonOptions)
            != JsonSerializer.Serialize<MapGeometry>(review.Proposed.Geometry.Single(g => g.Id == before.Id), MapPackageReader.JsonOptions));
        Check(moved, "actual GUI save contains a box position or size edit, beyond serialization changes");
        var expected = MapProjectSerializer.Clone(current);
        expected.Geometry = review.Proposed.Geometry; expected.Materials = review.Proposed.Materials;
        Check(expected.Serialize() == review.Proposed.Serialize(), "GUI review retains every native gameplay and project domain");
        RadiantSourceWorkspace.RequireFresh(current, review);
        var document = new MapDocument(new(current));
        string before = document.Project.Definition.Serialize();
        document.Edit("Apply actual Radiant GUI geometry", d =>
        { RadiantSourceWorkspace.ApplyReviewed(d, review); });
        Check(document.History.CanUndo, "actual GUI review applies as an undoable native edit");
        document.History.Undo();
        Check(document.Project.Definition.Serialize() == before, "one undo restores the exact native GUI fixture");
        string package = MapPackageBuilder.Build(review.Proposed, Path.Combine(directory, "gui-reviewed.ppmap"));
        var packaged = MapDefinition.Load(package);
        Check(packaged.MapId == current.MapId && packaged.Name == current.Name
            && packaged.Geometry.Select(g => g.Id).ToHashSet().SetEquals(current.Geometry.Select(g => g.Id)),
            "reviewed actual GUI edit builds and reloads a package with the same identities");
        File.WriteAllText(Path.Combine(directory, "reviewed.prime-map.json"), review.Proposed.Serialize());
        File.WriteAllText(Path.Combine(directory, "gui-verification.json"), JsonSerializer.Serialize(new
        {
            Source = source, review.SourceHash, review.SidecarHash, current.MapId, current.Name,
            GeometryIds = current.Geometry.Select(g => g.Id).ToArray(), Package = package,
            PackageHash = MapBuildFingerprint.HashFile(package), Checks = checks
        }));
        Console.WriteLine($"Actual GUI save verification: {checks} checks passed; {package}");
    }
    return;
}
string root = Path.Combine(Path.GetTempPath(), "prime-radiant-源-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
CustomRooms.UserMapDirectory = Path.Combine(root, "runtime", "user-maps");
CustomRooms.MapDirectory = Path.Combine(root, "runtime", "maps");
try
{
    string GeometryJson(MapGeometry geometry) => JsonSerializer.Serialize<MapGeometry>(geometry, MapPackageReader.JsonOptions);
    var meshes = Fixture("NATIVE_MESH");
    meshes.Materials.Add(new() { Id = Guid.NewGuid(), Name = "textures/prime/second", TexScale = 7.5f });
    var nativeMesh = new MapMesh
    {
        Label = "Open concave nonplanar mesh \"label\"", Layer = "Mesh layer", Hidden = true, Locked = true,
        Solid = false, Damaging = true, Terrain = "Rock", Shade = .625f, CollisionOnly = true,
        Slipperiness = 3, ReflectBeams = true, IgnorePlayers = true, IgnoreBeams = true, IgnoreScan = true,
        Transform = new() { Position = new[] { .000000000001f, 2f, -3f }, Rotation = new[] { 0f, 0f, .6f, .8f }, Scale = new[] { 2f, .5f, 3f } },
        Uv = new() { Rotation = 37, Offset = new[] { .125f, -.75f }, Scale = new[] { .5f, 2f } },
        Vertices = new() { new[] { 0f, 0f, 0f }, new[] { 2f, 0f, 0f }, new[] { 1f, .125f, .5f }, new[] { 2f, 0f, 2f }, new[] { 0f, 0f, 2f }, new[] { 1e-12f, -1e-20f, float.Epsilon } },
        Faces = new() { new[] { 0, 1, 2, 3, 4 }, new[] { 0, 4, 2 } },
        FaceMaterials = new() { 1, 0 }, FaceTexcoords = new() { new[] { new[] { 0f, 0f }, new[] { 8f, 0f }, new[] { 4f, 2f }, new[] { 8f, 8f }, new[] { 0f, 8f } }, null },
        FaceUv = new() { [1] = new() { Rotation = 11, Offset = new[] { 1f, 2f } } }
    };
    meshes.Geometry.Add(nativeMesh);
    string meshBefore = GeometryJson(nativeMesh);
    string meshSource = RadiantSourceWorkspace.Prepare(meshes, root);
    string meshText = File.ReadAllText(meshSource);
    Check(meshText.Contains("primeMeshDef") && !meshText.Contains("// PRIME_ID="), "native polygon mesh exports as a typed durable editor primitive");
    File.WriteAllText(meshSource, StockRadiantRewrite(meshText));
    var meshReview = RadiantSourceWorkspace.Preview(meshes, meshSource);
    Check(GeometryJson(meshReview.Proposed.Geometry.Single(g => g.Id == nativeMesh.Id)) == meshBefore,
        "open concave nonplanar mesh roundtrip preserves shared indices, tiny float bits, UVs, materials, transform and every flag");
    var materialOrder = MapProjectSerializer.Clone(meshes);
    materialOrder.MapId = Guid.NewGuid(); materialOrder.Geometry = new() { new MapBox { Material = 1 }, nativeMesh };
    string remappedSource = RadiantSourceWorkspace.Prepare(materialOrder, root);
    File.WriteAllText(remappedSource, StockRadiantRewrite(File.ReadAllText(remappedSource)));
    Check(GeometryJson(RadiantSourceWorkspace.Preview(materialOrder, remappedSource).Proposed.Geometry[1]) == meshBefore,
        "workspace remaps every native mesh FaceMaterials channel through imported shader bindings");
    string MeshPrimitive(MapGeometry geometry)
    {
        var output = new StringBuilder(); PrimeMeshInterchange.Write(geometry, meshes, 16, output); return output.ToString();
    }
    string[] MeshBody(string primitive) => primitive.Replace("\r\n", "\n").Split('\n').Skip(2).TakeWhile(line => line.Trim() != "}").ToArray();
    string primitive = MeshPrimitive(nativeMesh);
    Guid duplicatedId = Guid.NewGuid();
    Check(PrimeMeshInterchange.Parse(MeshBody(primitive), 16, duplicatedId).Geometry.Id == duplicatedId,
        "duplicate geometry container identity overrides the copied payload mesh ID");
    string normalizedMeshText = meshText.Replace("\r\n", "\n");
    int duplicateIdIndex = normalizedMeshText.IndexOf("\"prime_id\" \"" + nativeMesh.Id + "\"", StringComparison.Ordinal);
    int duplicateGroupStart = normalizedMeshText.LastIndexOf("\n{", duplicateIdIndex, StringComparison.Ordinal) + 1;
    int duplicateGroupDepth = 0, duplicateGroupEnd = duplicateGroupStart;
    do
    {
        char c = normalizedMeshText[duplicateGroupEnd++];
        if (c == '{') duplicateGroupDepth++;
        else if (c == '}') duplicateGroupDepth--;
    } while (duplicateGroupDepth > 0);
    string duplicateContainer = normalizedMeshText[duplicateGroupStart..duplicateGroupEnd].Replace("\"prime_id\" \"" + nativeMesh.Id + "\"", "\"prime_id\" \"" + duplicatedId + "\"");
    File.WriteAllText(meshSource, normalizedMeshText + duplicateContainer);
    Check(RadiantSourceWorkspace.Preview(meshes, meshSource).Proposed.Geometry.Single(g => g.Id == duplicatedId) is MapMesh,
        "full source review accepts native Duplicate's fresh container ID with the unchanged copied mesh payload");
    var parsedNative = (MapMesh)PrimeMeshInterchange.Parse(MeshBody(primitive), 16, nativeMesh.Id).Geometry;
    Check(BitConverter.SingleToInt32Bits(parsedNative.Vertices[5][0]) == BitConverter.SingleToInt32Bits(1e-12f)
        && BitConverter.SingleToInt32Bits(parsedNative.Vertices[5][1]) == BitConverter.SingleToInt32Bits(-1e-20f)
        && BitConverter.SingleToInt32Bits(parsedNative.Vertices[5][2]) == BitConverter.SingleToInt32Bits(float.Epsilon),
        "base64 JSON preserves tiny and subnormal native coordinates exactly");
    Refused(() => PrimeMeshInterchange.Parse(new[] { "version 1", "data " + (PrimeMeshInterchange.MaximumChunks + 1) }, 16, nativeMesh.Id), "native mesh chunk count is bounded before decode");
    Refused(() => PrimeMeshInterchange.Parse(new[] { "version 1", "data 1", "\"" + new string('A', 772) + "\"" }, 16, nativeMesh.Id), "native mesh tokens stay within the 768 character Radiant budget");
    Refused(() => PrimeMeshInterchange.Parse(new[] { "version 1", "data 1", "\"A===\"" }, 16, nativeMesh.Id), "malformed base64 mesh data is rejected");
    int overBudgetEncodedLength = (PrimeMeshInterchange.MaximumDecodedBytes + 2) / 3 * 4;
    var overBudgetBody = new List<string> { "version 1", "data " + PrimeMeshInterchange.MaximumChunks };
    for (int offset = 0; offset < overBudgetEncodedLength; offset += 768)
        overBudgetBody.Add("\"" + new string('A', Math.Min(768, overBudgetEncodedLength - offset)) + "\"");
    Refused(() => PrimeMeshInterchange.Parse(overBudgetBody, 16, nativeMesh.Id), "native mesh decoded byte budget is checked before base64 allocation");
    Refused(() => PrimeMeshInterchange.Parse(MeshBody(primitive), 32, nativeMesh.Id), "native mesh payload units must match the protected source convention");
    string MutatedPayload(string original, Action<System.Text.Json.Nodes.JsonObject> mutate)
    {
        string[] body = MeshBody(original);
        string encoded = string.Concat(body.Skip(2).Select(line => line.Trim().Trim('\"')));
        var json = System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String(encoded))!.AsObject(); mutate(json);
        string next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(json));
        return "primeMeshDef\n{\nversion 1\ndata " + ((next.Length + 767) / 768) + "\n"
            + string.Join("\n", Enumerable.Range(0, (next.Length + 767) / 768).Select(i => "\"" + next.Substring(i * 768, Math.Min(768, next.Length - i * 768)) + "\"")) + "\n}\n";
    }
    Refused(() => PrimeMeshInterchange.Parse(MeshBody(MutatedPayload(primitive, json => json["mesh"]!["faces"]![0]![0] = 999999)), 16, nativeMesh.Id),
        "out of range native polygon indices fail before geometry can be applied");
    Refused(() => PrimeMeshInterchange.Parse(MeshBody(MutatedPayload(primitive, json => json["mesh"]!["faceMaterials"]![0] = 99)), 16, nativeMesh.Id),
        "out of range native face material binding is rejected");
    string metadataSource = meshText.Replace(primitive, MutatedPayload(primitive, json =>
    { json["mesh"]!["label"] = "Edited mesh label"; json["mesh"]!["hidden"] = false; json["mesh"]!["reflectBeams"] = false; }));
    Check(metadataSource != meshText, "native metadata edit fixture modifies the actual primitive payload");
    File.WriteAllText(meshSource, metadataSource);
    var metadataReview = RadiantSourceWorkspace.Preview(meshes, meshSource);
    var metadataMesh = (MapMesh)metadataReview.Proposed.Geometry.Single(g => g.Id == nativeMesh.Id);
    Check(metadataMesh.Label == "Edited mesh label" && !metadataMesh.Hidden && !metadataMesh.ReflectBeams,
        "mesh metadata comes from the edited payload rather than being overwritten by stale native metadata");
    var wedge = new MapWedge { Label = "Original wedge", Damaging = true, Transform = new() { Position = new[] { 1f, 2f, 3f } } };
    string wedgePrimitive = MeshPrimitive(wedge);
    Check(GeometryJson(PrimeMeshInterchange.Parse(MeshBody(wedgePrimitive), 16, wedge.Id).Geometry) == GeometryJson(wedge),
        "unchanged parameterized wedge restores its complete original geometry kind and bytes");
    string editedWedge = MutatedPayload(wedgePrimitive, json => json["mesh"]!["vertices"]![0]![0] = .375f);
    var convertedWedge = PrimeMeshInterchange.Parse(MeshBody(editedWedge), 16, wedge.Id);
    Check(convertedWedge.ConvertedToMesh && convertedWedge.Geometry is MapMesh,
        "an edited parameterized primitive becomes an explicit reviewed mesh rather than silently losing geometry");
    var prism = new MapPrism { Label = "Original prism", Sides = 7, Hidden = true };
    Check(GeometryJson(PrimeMeshInterchange.Parse(MeshBody(MeshPrimitive(prism)), 16, prism.Id).Geometry) == GeometryJson(prism),
        "unchanged parameterized prism retains its original side count and source metadata");
    var changedBaseline = MapProjectSerializer.Clone(meshes); ((MapMesh)changedBaseline.Geometry[1]).Vertices[0][0] = .25f;
    Refused(() => RadiantSourceWorkspace.Preview(changedBaseline, meshSource), "native mesh source keeps the existing strict geometry conflict baseline");

    var typed = Fixture("PRIME_POINTS");
    typed.Spawns[0].Label = "Quoted \"spawn\" with literal \\ path\nand newline";
    typed.Spawns[0].Position = new[] { 1.25f, 2.5f, -3.75f }; typed.Spawns[0].Yaw = 213.125f;
    typed.Items[0].Label = ""; typed.Items[0].Type = "MissileBig";
    typed.JumpPads[0].Target = new[] { 10f, 20f, 30f }; typed.JumpPads[0].Vector = null;
    typed.JumpPads[0].Size = new[] { 1f, 2f, 3f }; typed.JumpPads[0].ModelId = 3;
    typed.JumpPads[0].CooldownTime = 17; typed.JumpPads[0].ControlLockTime = 23;
    typed.JumpPads.Add(new() { Id = Guid.NewGuid(), Label = null, Target = null, Vector = new[] { .25f, .5f, -.75f }, Speed = 3 });
    typed.NavigationLinks[0].Bidirectional = true;
    typed.Q3Triggers![0].SourceEntity = 987;
    string typedBefore = typed.Serialize();
    string typedSource = RadiantSourceWorkspace.Prepare(typed, root);
    string typedText = File.ReadAllText(typedSource);
    Check(typedText.Contains("\"classname\" \"prime_spawn\"") && typedText.Contains("\"classname\" \"prime_pickup\"")
        && typedText.Contains("\"classname\" \"prime_jumppad\"") && typedText.Contains("\"classname\" \"prime_navigation_link\"")
        && typedText.Contains("\"classname\" \"prime_trigger\""), "full source exports every typed native gameplay list");
    File.WriteAllText(typedSource, StockRadiantRewrite(typedText));
    var typedReview = RadiantSourceWorkspace.Preview(typed, typedSource);
    Check(typedReview.Proposed.Serialize() == typedBefore, "source save preserves all native point fields, team, yaw, labels, pickup timing, pad vectors and traversal provenance exactly");
    Check(typedReview.TriggerIds.Length == 1 && typedReview.TriggerIds[0] != Guid.Empty,
        "traversal source identity is durable independently of numeric SourceEntity provenance");
    var dirtyPoint = MapProjectSerializer.Clone(typed); dirtyPoint.Items[0].SpawnInterval++;
    Refused(() => RadiantSourceWorkspace.Preview(dirtyPoint, typedSource), "source conflict baseline includes native gameplay edits");
    string movedPoint = typedText.Replace("\"prime_team\" \"0\"", "\"prime_team\" \"2\"")
        .Replace("\"prime_spawn_interval\" \"721\"", "\"prime_spawn_interval\" \"123\"");
    Check(movedPoint != typedText, "gameplay property fixture changes actual typed source");
    File.WriteAllText(typedSource, movedPoint);
    var typedEdit = RadiantSourceWorkspace.Preview(typed, typedSource);
    Check(typedEdit.Proposed.Spawns[0].Team == 2 && typedEdit.Proposed.Items[0].SpawnInterval == 123,
        "typed source property changes replace the native gameplay lists during review");
    var typedDocument = new MapDocument(new(typed));
    typedDocument.Edit("Apply typed source", d => RadiantSourceWorkspace.ApplyReviewed(d, typedEdit));
    Check(typedDocument.Project.Definition.Items[0].SpawnInterval == 123 && typedDocument.Project.Definition.Spawns[0].Team == 2,
        "shared apply helper installs reviewed geometry and every owned gameplay list");
    typedDocument.History.Undo();
    Check(typedDocument.Project.Definition.Serialize() == typedBefore, "one undo restores the entire source-owned native project");
    string noItems = System.Text.RegularExpressions.Regex.Replace(typedText,
        "\\{\\s*\"classname\" \"prime_pickup\"[^{}]*\\}\\s*", "", System.Text.RegularExpressions.RegexOptions.Singleline);
    Check(noItems != typedText, "last pickup deletion fixture removes the actual typed entity");
    File.WriteAllText(typedSource, noItems);
    Check(RadiantSourceWorkspace.Preview(typed, typedSource).Proposed.Items.Count == 0,
        "deleting the last source pickup replaces the list with empty rather than resurrecting stale native objects");
    string duplicatePoint = typedText.Replace(typed.Items[0].Id.ToString(), typed.Geometry[0].Id.ToString());
    File.WriteAllText(typedSource, duplicatePoint);
    Refused(() => RadiantSourceWorkspace.Preview(typed, typedSource), "point identities cannot collide with geometry identities");
    File.WriteAllText(typedSource, movedPoint);
    RadiantSourceWorkspace.Accept(typedEdit.Proposed, typedEdit);
    Check(RadiantSourceWorkspace.State(typedEdit.Proposed, typedSource) == RadiantSourceState.Saved,
        "accept advances the complete gameplay and geometry baseline");

    string GroupFor(string sourceText, Guid id)
    {
        string normalized = sourceText.Replace("\r\n", "\n");
        int property = normalized.IndexOf("\"prime_id\" \"" + id + "\"", StringComparison.Ordinal);
        if (property < 0) throw new InvalidOperationException("Fixture object group is missing.");
        int start = normalized.LastIndexOf("\n{", property, StringComparison.Ordinal) + 1;
        int depth = 0, end = start;
        do { char c = normalized[end++]; if (c == '{') depth++; else if (c == '}') depth--; } while (depth > 0);
        return normalized[start..end];
    }
    string PrimitiveFor(string group)
    {
        int start = group.IndexOf("primeMeshDef", StringComparison.Ordinal);
        return group[start..(group.IndexOf("\n}", start, StringComparison.Ordinal) + 3)];
    }
    var legacyOnly = Fixture("LEGACY_ONLY"); legacyOnly.Geometry.Clear();
    legacyOnly.Brushes[0].Label = null; legacyOnly.Brushes[0].Terrain = null;
    string legacyOnlyBefore = legacyOnly.Serialize();
    string legacyOnlySource = RadiantSourceWorkspace.Prepare(legacyOnly, root);
    File.WriteAllText(legacyOnlySource, StockRadiantRewrite(File.ReadAllText(legacyOnlySource)));
    var legacyOnlyReview = RadiantSourceWorkspace.Preview(legacyOnly, legacyOnlySource);
    Check(legacyOnlyReview.Proposed.Serialize() == legacyOnlyBefore && legacyOnlyReview.Proposed.Geometry.Count == 0,
        "legacy-only editor save restores the exact original brush fields, null metadata and original authoring kind");
    var legacyEdit = Fixture("LEGACY_EDIT");
    legacyEdit.Brushes[0].Shade = .625f; legacyEdit.Brushes[0].Terrain = "Rock";
    string legacyEditSource = RadiantSourceWorkspace.Prepare(legacyEdit, root);
    string legacyEditText = File.ReadAllText(legacyEditSource).Replace("\r\n", "\n");
    string legacyGroup = GroupFor(legacyEditText, legacyEdit.Brushes[0].Id);
    string legacyPrimitive = PrimitiveFor(legacyGroup);
    string changedLegacyPrimitive = MutatedPayload(legacyPrimitive, json => json["mesh"]!["vertices"]![0]![0] = -2.25f);
    File.WriteAllText(legacyEditSource, legacyEditText.Replace(legacyPrimitive, changedLegacyPrimitive));
    var legacyMeshReview = RadiantSourceWorkspace.Preview(legacyEdit, legacyEditSource);
    Check(legacyMeshReview.Proposed.Brushes.Count == 0 && legacyMeshReview.Proposed.Geometry.Single(g => g.Id == legacyEdit.Brushes[0].Id) is MapMesh,
        "edited legacy brush becomes one explicit mesh with its original durable identity");
    Check(legacyMeshReview.ConvertedGeometryIds.Contains(legacyEdit.Brushes[0].Id)
        && legacyMeshReview.Diagnostics.Any(d => !d.Error && d.Code == "FP-RADIANT-MESH-CONVERSION"),
        "legacy source kind conversion is visible in the review IDs and conversion diagnostic");
    var legacyDocument = new MapDocument(new(legacyEdit));
    string beforeLegacyApply = legacyEdit.Serialize();
    legacyDocument.Edit("Convert reviewed legacy brush", definition => RadiantSourceWorkspace.ApplyReviewed(definition, legacyMeshReview));
    Check(legacyDocument.Project.Definition.Brushes.Count == 0 && legacyDocument.Project.Definition.Geometry.Any(g => g.Id == legacyEdit.Brushes[0].Id),
        "v4 apply replaces both legacy and mesh lists during source kind conversion");
    legacyDocument.History.Undo();
    Check(legacyDocument.Project.Definition.Serialize() == beforeLegacyApply, "one undo restores the exact pre-conversion legacy source");
    File.WriteAllText(legacyEditSource, legacyEditText.Replace(legacyGroup, ""));
    Check(RadiantSourceWorkspace.Preview(legacyEdit, legacyEditSource).Proposed.Brushes.Count == 0,
        "deleting a legacy source object removes its original native brush");
    var changedLegacyNative = MapProjectSerializer.Clone(legacyEdit); changedLegacyNative.Brushes[0].Min[0] -= .5f;
    Refused(() => RadiantSourceWorkspace.Preview(changedLegacyNative, legacyEditSource), "v4 source conflict baseline includes legacy brush field edits");
    var farLegacy = Fixture("LEGACY_RANGE"); farLegacy.Geometry.Clear();
    farLegacy.Brushes[0].Min = new[] { 256f, 0f, 0f }; farLegacy.Brushes[0].Max = new[] { 257f, 1f, 1f };
    string farLegacySource = RadiantSourceWorkspace.Prepare(farLegacy, root);
    Check(QuakeMapInterchange.Import(farLegacySource, RadiantSourceWorkspace.SidecarPath(farLegacySource), requireDurableGeometryContainers: true).Definition.ScaleFactor >= 6,
        "legacy brush bounds participate in native fixed-point scale selection");

    var referenceOwner = Fixture("REFERENCE_GUARDS"); referenceOwner.Brushes.Clear();
    referenceOwner.Geometry[0].Label = "Geometry"; referenceOwner.Geometry[0].Shade = 1;
    string referenceSource = RadiantSourceWorkspace.Prepare(referenceOwner, root);
    string ownedSourceText = File.ReadAllText(referenceSource).Replace("\r\n", "\n");
    var referenceMesh = new MapMesh
    {
        Label = "Synthetic retained architecture", Locked = true, Solid = false,
        Vertices = new() { new[] { 0f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 0f, 0f, 1f } },
        Faces = new() { new[] { 0, 1, 2 } }, FaceTexcoords = new() { new[] { new[] { 0f, 0f }, new[] { 16f, 0f }, new[] { 0f, 16f } } }
    };
    string referenceShader = RadiantReferenceScene.ShaderPrefix + referenceOwner.MapId.ToString("N") + "/0";
    var referenceMaterials = new[] { new PrimeMeshInterchange.Material(referenceShader, 1, 64, 64) };
    var referenceDefinition = new MapDefinition { Materials = new() { new() { Name = referenceShader, TexScale = 1 } } };
    var referencePrimitiveBuilder = new StringBuilder();
    PrimeMeshInterchange.Write(referenceMesh, referenceDefinition, 16, referencePrimitiveBuilder, referenceMaterials, reference: true);
    string referencePrimitive = referencePrimitiveBuilder.ToString();
    string referenceGroup = "{\n\"classname\" \"func_group\"\n\"prime_geometry\" \"1\"\n\"prime_reference\" \"1\"\n\"prime_id\" \"" + referenceMesh.Id + "\"\n{\n" + referencePrimitive + "}\n}\n";
    // The synthetic adjunct is the trusted oracle for parser guard fixtures;
    // no real retained architecture or input asset is modified here.
    string referenceManifest = referenceSource + ".prime-source.json";
    var referenceMetadata = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(referenceManifest))!.AsObject();
    referenceMetadata["References"] = JsonSerializer.SerializeToNode(new[] { new RadiantReferenceScene.Identity(referenceMesh.Id,
        RadiantReferenceScene.PayloadHash(referenceMesh, referenceMaterials, 16)) });
    File.WriteAllText(referenceManifest, referenceMetadata.ToJsonString());
    File.WriteAllText(referenceSource, ownedSourceText + referenceGroup);
    var referenceReview = RadiantSourceWorkspace.Preview(referenceOwner, referenceSource);
    Check(referenceReview.Proposed.Geometry.Select(g => g.Id).SequenceEqual(referenceOwner.Geometry.Select(g => g.Id))
        && referenceReview.Proposed.Materials.Count == referenceOwner.Materials.Count
        && !referenceReview.Proposed.Materials.Any(m => RadiantReferenceScene.IsReferenceShader(m.Name)),
        "verified reference geometry and material descriptors never enter owned project lists");
    void ReferenceRefused(string candidate, string label)
    {
        File.WriteAllText(referenceSource, candidate);
        Refused(() => RadiantSourceWorkspace.Preview(referenceOwner, referenceSource), label);
    }
    ReferenceRefused(ownedSourceText, "removing a retained reference is rejected");
    ReferenceRefused(ownedSourceText + referenceGroup + referenceGroup.Replace(referenceMesh.Id.ToString(), Guid.NewGuid().ToString()),
        "adding an unrecognized retained reference is rejected");
    ReferenceRefused(ownedSourceText + referenceGroup.Replace(referencePrimitive, MutatedPayload(referencePrimitive, json => json["mesh"]!["vertices"]![0]![0] = .25f)),
        "changing retained architecture coordinates is rejected");
    ReferenceRefused(ownedSourceText + referenceGroup.Replace(referencePrimitive, MutatedPayload(referencePrimitive, json => json["mesh"]!["locked"] = false)),
        "unlocking retained architecture is rejected");
    ReferenceRefused(ownedSourceText + referenceGroup.Replace("\"prime_reference\" \"1\"\n", ""), "stripping the retained reference container marker is rejected");
    string unmarkedReference = referenceGroup.Replace("\"prime_reference\" \"1\"\n", "")
        .Replace(referencePrimitive, MutatedPayload(referencePrimitive, json => json["reference"] = false));
    ReferenceRefused(ownedSourceText + unmarkedReference, "stripping both retained architecture markers cannot convert a protected identity into owned geometry");
    ReferenceRefused(ownedSourceText + referenceGroup + unmarkedReference.Replace(referenceMesh.Id.ToString(), Guid.NewGuid().ToString()),
        "stripped and reidentified reference copy cannot leak reserved material names into owned geometry");
    ReferenceRefused(ownedSourceText.Replace(referenceOwner.Materials[0].Name, referenceShader) + referenceGroup,
        "ordinary brush projections cannot claim reserved reference material names");
    File.WriteAllText(referenceSource, "{\n\"classname\" \"worldspawn\"\n}\n" + referenceGroup);
    Check(RadiantSourceWorkspace.Preview(referenceOwner, referenceSource).Proposed.Geometry.Count == 0,
        "verified reference-only source can review the intentional deletion of every owned geometry object");

    var solidMeshes = MapTemplates.Create("SOLID_MESH_FIXTURE", true).Definition;
    solidMeshes.BaseDirectory = root; solidMeshes.TextureSource = "";
    const string solidTexture = "solid-mesh-fixture.tex";
    using (var output = new BinaryWriter(File.Create(Path.Combine(root, solidTexture))))
    {
        output.Write(Encoding.ASCII.GetBytes("FPTX")); output.Write((ushort)1); output.Write((ushort)1);
        output.Write((ushort)0); output.Write((ushort)8); output.Write((ushort)8); output.Write((ushort)1); output.Write((ushort)0);
        output.Write((ushort)32767); output.Write(new byte[64]);
    }
    foreach (var material in solidMeshes.Materials) material.Texture = solidTexture;
    solidMeshes.Assets.Add(new() { Path = solidTexture });
    solidMeshes.Geometry = solidMeshes.Geometry.Select(g => (MapGeometry)MapMeshEditing.Convert(g, solidMeshes.Materials[g.Material].TexScale)).ToList();
    string solidSource = RadiantSourceWorkspace.Prepare(solidMeshes, root);
    File.WriteAllText(solidSource, StockRadiantRewrite(File.ReadAllText(solidSource)));
    var solidReview = RadiantSourceWorkspace.Preview(solidMeshes, solidSource);
    string solidPackage = MapPackageBuilder.Build(solidReview.Proposed, Path.Combine(root, "solid-mesh-fixture.ppmap"));
    var loadedSolid = MapDefinition.Load(solidPackage);
    Check(loadedSolid.Geometry.OfType<MapMesh>().Any(g => g.Solid) && loadedSolid.MapId == solidMeshes.MapId,
        "self-contained solid native mesh source builds and reloads a playable package without original project assets");

    foreach (var crashPoint in Enum.GetValues<RadiantSourceCommitPoint>())
    {
        var crashMap = Fixture("WORKSPACE_CRASH");
        string crashSource = RadiantSourceWorkspace.SourcePath(root, crashMap.MapId);
        Refused(() => RadiantSourceWorkspace.Prepare(crashMap, root, point =>
        {
            if (point == crashPoint) throw new IOException("injected workspace crash");
        }), "workspace crash injected at " + crashPoint);
        string recoveredSource = RadiantSourceWorkspace.Prepare(crashMap, root);
        Check(recoveredSource == crashSource && File.Exists(crashSource + ".prime-source.json")
            && RadiantSourceWorkspace.Preview(crashMap, recoveredSource).Proposed.Serialize() == crashMap.Serialize(),
            "first workspace recovers source, sidecar and native adjunct after " + crashPoint);
        Check(!File.Exists(RadiantSourceWorkspace.SidecarPath(crashSource) + ".prime-transaction"),
            "workspace journal retires only after native adjunct commit at " + crashPoint);
    }
    var interrupted = Fixture("WORKSPACE_EXTERNAL");
    string interruptedSource = RadiantSourceWorkspace.SourcePath(root, interrupted.MapId);
    Refused(() => RadiantSourceWorkspace.Prepare(interrupted, root, point =>
    {
        if (point == RadiantSourceCommitPoint.SidecarCommitted) throw new IOException("crash before adjunct");
    }), "workspace can stop in the former source-to-adjunct crash gap");
    File.AppendAllText(interruptedSource, "\n// author save after crash\n");
    string savedExternal = File.ReadAllText(interruptedSource);
    Refused(() => RadiantSourceWorkspace.Prepare(interrupted, root),
        "workspace recovery preserves external save before the adjunct was committed");
    Check(File.ReadAllText(interruptedSource) == savedExternal,
        "failed workspace recovery never erases post-crash author content");
    var first = Fixture("FIRST"); var second = Fixture("SECOND");
    string firstJson = first.Serialize();
    string one = RadiantSourceWorkspace.Prepare(first, root);
    string two = RadiantSourceWorkspace.Prepare(second, root);
    Check(one != two && File.Exists(one) && File.Exists(two), "two MapIds have independent Unicode source directories");
    Check(QuakeMapInterchange.ReadIdentity(RadiantSourceWorkspace.SidecarPath(one)).MapId == first.MapId
        && QuakeMapInterchange.ReadIdentity(RadiantSourceWorkspace.SidecarPath(two)).MapId == second.MapId,
        "each source sidecar binds the correct identity");
    Check(first.Serialize() == firstJson, "geometry export leaves native project serialization byte-exact");
    var unchanged = RadiantSourceWorkspace.Preview(first, one);
    Check(unchanged.Proposed.Serialize() == firstJson, "unchanged roundtrip preserves all canonical project bytes");
    Check(unchanged.Proposed.Spawns[0].Team == 0 && unchanged.Proposed.Items[0].SpawnInterval == 721
        && !unchanged.Proposed.Items[0].HasBase && unchanged.Proposed.JumpPads[0].Speed == 22,
        "team spawns, pickup timing and launch pads survive geometry-only workflow");
    File.AppendAllText(one, "\n// external author edit\n");
    string external = File.ReadAllText(one);
    RadiantSourceWorkspace.Prepare(first, root);
    Check(File.ReadAllText(one) == external, "reopening preserves external edits across workspace recreation");
    Check(RadiantSourceWorkspace.State(first, one) == RadiantSourceState.ExternalChanges,
        "source state detects external changes");
    var review = RadiantSourceWorkspace.Preview(first, one);
    var document = new MapDocument(new(first));
    string before = document.Project.Definition.Serialize();
    document.Edit("Apply Radiant geometry", d => { RadiantSourceWorkspace.ApplyReviewed(d, review); });
    Check(document.Project.Definition.Author == first.Author && document.Project.Definition.KillHeight == -91
        && document.Project.Definition.Geometry[0].Layer == "Floor" && document.Project.Definition.Geometry[0].Shade == .7f,
        "reviewed source preserves metadata, author labels, layer and shade");
    if (document.History.CanUndo) document.History.Undo();
    Check(document.Project.Definition.Serialize() == before, "one undo restores exact prior native project");
    File.AppendAllText(one, "// newer edit\n");
    Refused(() => RadiantSourceWorkspace.RequireFresh(first, review), "review rejects a source revision changed before apply");
    var dirtyGeometry = MapProjectSerializer.Clone(first); dirtyGeometry.Geometry[0].Transform.Position[0] = 3;
    Refused(() => RadiantSourceWorkspace.Preview(dirtyGeometry, one), "dirty Studio geometry requires conflict reconciliation");
    var metadataEdit = MapProjectSerializer.Clone(first); metadataEdit.Description = "Concurrent metadata edit";
    Check(RadiantSourceWorkspace.Preview(metadataEdit, one).Proposed.Description == metadataEdit.Description,
        "independent native metadata edits are retained during review");
    var geometryEdit = Fixture("EDITED");
    // Keep this plane-edit fixture on the classic brush path. Custom metadata
    // uses a native payload so a fresh-ID editor Duplicate retains it too.
    geometryEdit.Geometry[0].Label = "Geometry"; geometryEdit.Geometry[0].Shade = 1;
    string geometrySource = RadiantSourceWorkspace.Prepare(geometryEdit, root);
    string geometryText = File.ReadAllText(geometrySource);
    Check(geometryText.Contains("\"classname\" \"func_group\"")
        && geometryText.Contains("\"prime_geometry\" \"1\"") && !geometryText.Contains("// PRIME_ID="),
        "workspace exports durable marked geometry containers instead of comment identities");
    string containerText = geometryText.Replace("\r\n", "\n");
    void ContainerRefused(string candidate, string label)
    {
        string malformed = Path.Combine(root, "malformed-container.map");
        File.WriteAllText(malformed, candidate);
        Refused(() => QuakeMapInterchange.Import(malformed, RadiantSourceWorkspace.SidecarPath(geometrySource)), label);
    }
    ContainerRefused(containerText.Replace("\"prime_geometry\" \"1\"", "\"prime_geometry\" \"2\""),
        "unknown geometry container marker is refused");
    ContainerRefused(containerText.Replace("\"prime_geometry\" \"1\"", ""),
        "unmarked func_group is refused instead of treating arbitrary entity geometry as world geometry");
    ContainerRefused(containerText.Replace(geometryEdit.Geometry[0].Id.ToString(), Guid.Empty.ToString()),
        "empty geometry container identity is refused");
    ContainerRefused(containerText.Replace("\"prime_geometry\" \"1\"", "\"prime_geometry\" \"1\"\n\"origin\" \"16 0 0\""),
        "geometry container transforms and unknown epairs are refused");
    int groupStart = containerText.IndexOf("{\n\"classname\" \"func_group\"", StringComparison.Ordinal);
    int primitiveStart = containerText.IndexOf("\n{\n", groupStart, StringComparison.Ordinal) + 1;
    int groupEnd = containerText.LastIndexOf("}\n", StringComparison.Ordinal);
    string primitiveText = containerText[primitiveStart..groupEnd];
    ContainerRefused(containerText.Remove(primitiveStart, groupEnd - primitiveStart),
        "empty geometry containers are refused");
    ContainerRefused(containerText.Insert(groupEnd, primitiveText),
        "split geometry containers with multiple primitives are refused");
    ContainerRefused(containerText + containerText[groupStart..],
        "duplicated geometry containers cannot silently reuse an existing GUID");
    ContainerRefused(containerText.Insert(groupEnd, "\"prime_patch_solid\" \"false\"\n"),
        "container properties appearing after geometry are refused");
    // Move every Quake X plane together: an actual geometry revision, rather
    // than only a source comment, must keep all native adjunct domains.
    geometryText = System.Text.RegularExpressions.Regex.Replace(geometryText,
        @"\( (?<x>-?\d+(?:\.\d+)?) ", match => "( "
            + (float.Parse(match.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture) + 16)
                .ToString(System.Globalization.CultureInfo.InvariantCulture) + " ");
    File.WriteAllText(geometrySource, StockRadiantRewrite(geometryText));
    var geometryReview = RadiantSourceWorkspace.Preview(geometryEdit, geometrySource);
    Check(geometryReview.Proposed.Geometry[0].Transform.Position[0] == 1f,
        "source geometry edits replace native geometry positions");
    Check(geometryReview.Proposed.Geometry[0].Id == geometryEdit.Geometry[0].Id
        && geometryReview.Proposed.Geometry[0].Label == geometryEdit.Geometry[0].Label
        && geometryReview.Proposed.Geometry[0].Layer == geometryEdit.Geometry[0].Layer
        && geometryReview.Proposed.Geometry[0].Locked == geometryEdit.Geometry[0].Locked
        && geometryReview.Proposed.Geometry[0].Terrain == geometryEdit.Geometry[0].Terrain
        && geometryReview.Proposed.Geometry[0].Shade == geometryEdit.Geometry[0].Shade,
        "stock-Radiant-style save keeps geometry IDs, labels, layers and shade after comments disappear");
    var expectedHybrid = MapProjectSerializer.Clone(geometryEdit);
    expectedHybrid.Geometry = geometryReview.Proposed.Geometry; expectedHybrid.Brushes = geometryReview.Proposed.Brushes;
    Check(expectedHybrid.Serialize() == geometryReview.Proposed.Serialize(),
        "changed geometry preserves native architecture, collision, triggers, navigation, gameplay and material bytes");
    RadiantSourceWorkspace.RequireFresh(geometryEdit, geometryReview);
    RadiantSourceWorkspace.Accept(geometryReview.Proposed, geometryReview);
    Check(RadiantSourceWorkspace.State(geometryReview.Proposed, geometrySource) == RadiantSourceState.Saved,
        "accepted review advances the source and native geometry baseline");
    string identityPath = RadiantSourceWorkspace.SidecarPath(geometrySource);
    string identityBytes = File.ReadAllText(identityPath);
    File.AppendAllText(identityPath, "\n");
    Refused(() => RadiantSourceWorkspace.RequireFresh(geometryEdit, geometryReview),
        "apply refuses identity sidecar changes after preview");
    File.WriteAllText(identityPath, identityBytes);

    var surfaces = Fixture("SURFACES");
    surfaces.Geometry.Add(new MapConvexBrush
    {
        Label = "Native ramp", Layer = "Ramps", Shade = .8f,
        Vertices = new() { new[] { 0f, 0f, 0f }, new[] { 8f, 0f, 0f }, new[] { 0f, 0f, -8f },
            new[] { 8f, 0f, -8f }, new[] { 0f, 4f, -8f }, new[] { 8f, 4f, -8f } },
        Faces = new() { new[] { 0, 2, 3, 1 }, new[] { 0, 4, 2 }, new[] { 1, 3, 5 },
            new[] { 2, 4, 5, 3 }, new[] { 0, 1, 5, 4 } }
    });
    // Exercise classic patch metadata separately from the custom native mesh
    // payload path covered by the Duplicate regression below.
    var patch = new MapBezierPatch { Solid = false, Tessellation = 2 };
    for (int col = 0; col < 3; col++)
        for (int row = 0; row < 3; row++)
            patch.ControlPoints.Add(new[] { (float)col, col == 1 && row == 1 ? 1f : 0f,
                (float)row, col / 2f, row / 2f });
    surfaces.Geometry.Add(patch);
    string surfacesSource = RadiantSourceWorkspace.Prepare(surfaces, root);
    File.WriteAllText(surfacesSource, StockRadiantRewrite(File.ReadAllText(surfacesSource)));
    var surfacesReview = RadiantSourceWorkspace.Preview(surfaces, surfacesSource);
    Check(surfacesReview.Proposed.Geometry.Any(g => g is MapConvexBrush)
        && surfacesReview.Proposed.Geometry.OfType<MapBezierPatch>().Single().Solid == false
        && surfacesReview.Proposed.Geometry.OfType<MapBezierPatch>().Single().Tessellation == 2,
        "hybrid workflow retains editable convex geometry and patch collision/tessellation intent");
    Check(surfacesReview.Proposed.Geometry.Single(g => g.Label == "Native ramp").Layer == "Ramps"
        && surfacesReview.Proposed.Materials[0].Texture == surfaces.Materials[0].Texture
        && surfacesReview.Proposed.Materials[0].Albedo == surfaces.Materials[0].Albedo,
        "supported surfaces preserve native authoring metadata and material assets");
    string patchSource = File.ReadAllText(surfacesSource);
    ContainerRefused(patchSource.Replace("\"prime_patch_solid\" \"false\"", ""),
        "marked patches refuse missing collision intent after an editor save");
    ContainerRefused(patchSource.Replace("\"prime_patch_tessellation\" \"2\"", "\"prime_patch_tessellation\" \"0\""),
        "marked patches refuse invalid tessellation intent");

    foreach (var original in surfaces.Geometry)
    {
        var customCopyOwner = MapProjectSerializer.Clone(surfaces); customCopyOwner.MapId = Guid.NewGuid();
        var custom = customCopyOwner.Geometry.Single(g => g.Id == original.Id);
        custom.Label = "Custom \"" + original.GetType().Name + "\""; custom.Terrain = "Sand"; custom.Shade = .375f;
        customCopyOwner.Geometry = new() { custom }; customCopyOwner.Brushes.Clear();
        string duplicateSource = RadiantSourceWorkspace.Prepare(customCopyOwner, root);
        string duplicateText = File.ReadAllText(duplicateSource).Replace("\r\n", "\n");
        int idIndex = duplicateText.IndexOf("\"prime_id\" \"" + custom.Id + "\"", StringComparison.Ordinal);
        int start = duplicateText.LastIndexOf("\n{", idIndex, StringComparison.Ordinal) + 1;
        int depth = 0, end = start;
        do
        {
            char c = duplicateText[end++];
            if (c == '{') depth++;
            else if (c == '}') depth--;
        } while (depth > 0);
        Guid copyId = Guid.NewGuid();
        string group = duplicateText[start..end];
        Check(group.Contains("primeMeshDef"), "custom " + original.GetType().Name + " metadata selects a complete native payload");
        group = group.Replace("\"prime_id\" \"" + custom.Id + "\"", "\"prime_id\" \"" + copyId + "\"");
        File.WriteAllText(duplicateSource, duplicateText + group);
        var copied = RadiantSourceWorkspace.Preview(customCopyOwner, duplicateSource).Proposed.Geometry.Single(g => g.Id == copyId);
        custom.Id = copyId;
        Check(copied.GetType() == custom.GetType() && GeometryJson(copied) == GeometryJson(custom),
            "fresh-ID native Duplicate preserves custom label, terrain, shade and unchanged " + original.GetType().Name + " source kind");
    }

    void UnmarkedWorkspaceRefused(MapDefinition fixture, string label, bool retainComments, bool mixed = false)
    {
        var native = MapProjectSerializer.Clone(fixture);
        native.MapId = Guid.NewGuid();
        string source = RadiantSourceWorkspace.Prepare(native, root);
        string original = File.ReadAllText(source).Replace("\r\n", "\n");
        string directory = Path.GetDirectoryName(source)!;
        string nativeFile = Path.Combine(directory, "native.json");
        File.WriteAllText(nativeFile, native.Serialize());
        var editable = MapProjectSerializer.Clone(native);
        editable.Import = null; editable.NativeRoom = null; editable.Collision = null;
        editable.Brushes.Clear(); editable.Spawns.Clear(); editable.Items.Clear();
        editable.JumpPads.Clear(); editable.NavigationLinks.Clear(); editable.Q3Triggers = null;
        string generic = Path.Combine(directory, "generic.map");
        QuakeMapInterchange.Export(editable, generic, Path.Combine(directory, "generic-sidecar.json"));
        string candidate = File.ReadAllText(generic);
        if (!retainComments) candidate = StockRadiantRewrite(candidate);
        if (mixed)
        {
            string layerDefinitions = string.Join("\n", original.Split('\n').Where(line => line.StartsWith("//@$& layerdef ", StringComparison.Ordinal)));
            candidate = layerDefinitions + "\n" + candidate + "\n"
                + original[original.IndexOf("{\n\"classname\" \"func_group\"", StringComparison.Ordinal)..];
        }
        File.WriteAllText(source, candidate); // An external editor's saved revision.
        Check(QuakeMapInterchange.Import(source, RadiantSourceWorkspace.SidecarPath(source)).Definition.Geometry.Count
            == native.Geometry.Count * (mixed ? 2 : 1), label + " remains compatible with generic editable-map import");
        string nativeJson = native.Serialize();
        var protectedFiles = new[] { source, RadiantSourceWorkspace.SidecarPath(source), source + ".prime-source.json", nativeFile }
            .ToDictionary(path => path, File.ReadAllBytes);
        bool refused = false;
        try { RadiantSourceWorkspace.Preview(native, source); }
        catch (QuakeMapInterchangeException ex)
        { refused = ex.Diagnostics.Any(d => d.Error && d.Code == "FP-RADIANT-DURABLE-GEOMETRY"); }
        Check(refused, label + " is refused by v2 workspace review before unstable identities can be applied");
        Check(native.Serialize() == nativeJson && protectedFiles.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)),
            label + " rejection leaves source, sidecar, native adjunct and native project bytes unchanged");
    }
    UnmarkedWorkspaceRefused(Fixture("STRICT_BRUSH"), "unmarked brush", retainComments: false);
    UnmarkedWorkspaceRefused(Fixture("STRICT_COMMENTS"), "comment-only brush", retainComments: true);
    var patchOnly = MapProjectSerializer.Clone(surfaces);
    patchOnly.Geometry = patchOnly.Geometry.OfType<MapBezierPatch>().Cast<MapGeometry>().ToList();
    UnmarkedWorkspaceRefused(patchOnly, "unmarked patch", retainComments: false);
    UnmarkedWorkspaceRefused(patchOnly, "comment-only patch", retainComments: true);
    UnmarkedWorkspaceRefused(Fixture("STRICT_MIXED"), "mixed marked and unmarked geometry", retainComments: false, mixed: true);

    string LegacyWorkspace(MapDefinition definition)
    {
        string source = RadiantSourceWorkspace.SourcePath(root, definition.MapId);
        var editable = MapProjectSerializer.Clone(definition);
        editable.Import = null; editable.NativeRoom = null; editable.Collision = null;
        editable.Brushes.Clear(); editable.Spawns.Clear(); editable.Items.Clear();
        editable.JumpPads.Clear(); editable.NavigationLinks.Clear(); editable.Q3Triggers = null;
        QuakeMapInterchange.Export(editable, source, RadiantSourceWorkspace.SidecarPath(source));
        File.WriteAllText(source + ".prime-source.json", JsonSerializer.Serialize(new
        {
            Version = 1, definition.MapId, SourceHash = MapBuildFingerprint.HashFile(source),
            GeometryHash = RadiantSourceWorkspace.GeometryHash(definition), NativeProjectJson = definition.Serialize()
        }));
        return source;
    }
    var legacy = Fixture("LEGACY_SAFE");
    string legacySource = LegacyWorkspace(legacy);
    Check(File.ReadAllText(legacySource).Contains("// PRIME_ID="), "legacy migration fixture uses historical comment identities");
    RadiantSourceWorkspace.Prepare(legacy, root);
    Check(File.ReadAllText(legacySource).Contains("\"prime_geometry\" \"1\"")
        && RadiantSourceWorkspace.Preview(legacy, legacySource).Proposed.Serialize() == legacy.Serialize(),
        "unchanged owned legacy source upgrades atomically to durable containers without changing native bytes");
    using (var manifest = JsonDocument.Parse(File.ReadAllBytes(legacySource + ".prime-source.json")))
        Check(manifest.RootElement.GetProperty("Version").GetInt32() == 4, "legacy upgrade advances the native adjunct format");
    var changedLegacy = Fixture("LEGACY_EXTERNAL");
    string changedLegacySource = LegacyWorkspace(changedLegacy);
    File.AppendAllText(changedLegacySource, "\n// saved outside Studio\n");
    string[] legacyFiles = { changedLegacySource, RadiantSourceWorkspace.SidecarPath(changedLegacySource), changedLegacySource + ".prime-source.json" };
    byte[][] legacyBytes = legacyFiles.Select(File.ReadAllBytes).ToArray();
    Refused(() => RadiantSourceWorkspace.Prepare(changedLegacy, root), "legacy upgrade refuses changed external source");
    Refused(() => RadiantSourceWorkspace.Preview(changedLegacy, changedLegacySource), "legacy changed source cannot silently lose metadata during preview");
    Check(legacyFiles.Select((path, i) => File.ReadAllBytes(path).SequenceEqual(legacyBytes[i])).All(x => x),
        "refused legacy migration preserves source, sidecar and adjunct bytes");
    var nativeChangedLegacy = Fixture("LEGACY_NATIVE_CHANGED");
    string nativeChangedSource = LegacyWorkspace(nativeChangedLegacy);
    string nativeChangedBytes = File.ReadAllText(nativeChangedSource);
    nativeChangedLegacy.Geometry[0].Transform.Position[0] = 1;
    Refused(() => RadiantSourceWorkspace.Prepare(nativeChangedLegacy, root), "legacy upgrade refuses a changed native geometry baseline");
    Check(File.ReadAllText(nativeChangedSource) == nativeChangedBytes, "native conflict never replaces the legacy author source");

    foreach (var racePoint in Enum.GetValues<RadiantSourceCommitPoint>().Where(p => p != RadiantSourceCommitPoint.AdjunctCommitted))
    foreach (bool sidecarEdit in new[] { false, true })
    {
        string raceDirectory = Path.Combine(root, "commit-race-" + racePoint + "-" + sidecarEdit); Directory.CreateDirectory(raceDirectory);
        string raceSource = Path.Combine(raceDirectory, "world.map"), raceSidecar = Path.Combine(raceDirectory, "project-prime.json");
        byte[] exported = Encoding.UTF8.GetBytes("exported"), author = Encoding.UTF8.GetBytes("concurrent author save");
        using (var lease = RadiantSourceTransaction.Acquire(raceSource))
            Refused(() => RadiantSourceTransaction.CommitUnderLease(raceSource, raceSidecar,
                exported, Identity(exported), null, null, stage =>
                {
                    if (stage == racePoint)
                        File.WriteAllBytes(sidecarEdit ? raceSidecar : raceSource, author);
                }), "commit detects concurrent " + (sidecarEdit ? "sidecar" : "source") + " save at " + racePoint);
        Check(File.ReadAllBytes(sidecarEdit ? raceSidecar : raceSource).SequenceEqual(author)
            && File.Exists(raceSidecar + ".prime-transaction"), "concurrent save and recovery journal survive conflict");
    }
    var unsupported = Fixture("UNSUPPORTED"); unsupported.Geometry.Add(new MapMesh());
    Refused(() => RadiantSourceWorkspace.Prepare(unsupported, root), "invalid empty meshes refuse export instead of producing misleading geometry");
    Check(!File.Exists(RadiantSourceWorkspace.SourcePath(root, unsupported.MapId)), "failed export leaves no misleading editable source");

    foreach (var point in Enum.GetValues<RadiantSourceCommitPoint>().Where(p => p != RadiantSourceCommitPoint.AdjunctCommitted))
    {
        string directory = Path.Combine(root, point.ToString()); Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "world.map"), sidecar = Path.Combine(directory, "project-prime.json");
        byte[] nextSource = Encoding.UTF8.GetBytes("new source"), nextSidecar = Identity(nextSource);
        try
        {
            using var lease = RadiantSourceTransaction.Acquire(source);
            RadiantSourceTransaction.CommitUnderLease(source, sidecar, nextSource, nextSidecar, null, null,
                stage => { if (stage == point) throw new IOException("injected crash"); });
        }
        catch (IOException) { }
        RadiantSourceTransaction.Recover(source, sidecar);
        Check(File.ReadAllBytes(source).SequenceEqual(nextSource) && File.ReadAllBytes(sidecar).SequenceEqual(nextSidecar),
            "recover consistent pair after " + point);
        Check(!File.Exists(sidecar + ".prime-transaction"), "completed recovery removes journal after " + point);

        byte[] replacement = Encoding.UTF8.GetBytes("replacement source"), replacementIdentity = Identity(replacement);
        using (var lease = RadiantSourceTransaction.Acquire(source))
        {
            try
            {
                RadiantSourceTransaction.CommitUnderLease(source, sidecar, replacement, replacementIdentity,
                    Convert.ToHexString(SHA256.HashData(nextSource)), Convert.ToHexString(SHA256.HashData(nextSidecar)),
                    stage => { if (stage == point) throw new IOException("injected replacement crash"); });
            }
            catch (IOException) { }
        }
        RadiantSourceTransaction.Recover(source, sidecar);
        Check(File.ReadAllBytes(source).SequenceEqual(replacement) && File.ReadAllBytes(sidecar).SequenceEqual(replacementIdentity),
            "recover replacement of existing source pair after " + point);
    }
    string conflictDirectory = Path.Combine(root, "recovery-conflict"); Directory.CreateDirectory(conflictDirectory);
    string conflictSource = Path.Combine(conflictDirectory, "world.map"), conflictSidecar = Path.Combine(conflictDirectory, "project-prime.json");
    try
    {
        using var lease = RadiantSourceTransaction.Acquire(conflictSource);
        RadiantSourceTransaction.CommitUnderLease(conflictSource, conflictSidecar,
            Encoding.UTF8.GetBytes("exported"), Identity(Encoding.UTF8.GetBytes("exported")), null, null,
            stage => { if (stage == RadiantSourceCommitPoint.SourceCommitted) throw new IOException("crash"); });
    }
    catch (IOException) { }
    File.WriteAllText(conflictSource, "saved in Radiant after crash");
    Refused(() => RadiantSourceTransaction.Recover(conflictSource, conflictSidecar), "recovery refuses to erase post-crash author edits");
    Check(File.ReadAllText(conflictSource) == "saved in Radiant after crash" && File.Exists(conflictSidecar + ".prime-transaction"),
        "conflicted recovery retains author bytes and journal for reconciliation");
    Refused(() => RadiantSourceTransaction.Recover(conflictSource, Path.Combine(root, "other-sidecar.json")),
        "transaction rejects a sidecar outside the source directory");

    string strict = Path.Combine(root, "strict"); Directory.CreateDirectory(strict);
    string strictSource = Path.Combine(strict, "legacy.map");
    var native = Fixture("LEGACY"); native.Spawns.Clear(); native.Items.Clear(); native.JumpPads.Clear();
    native.NativeRoom = null; native.Collision = null; native.Brushes.Clear(); native.NavigationLinks.Clear(); native.Q3Triggers = null;
    QuakeMapInterchange.Export(native, strictSource);
    byte[] originalSource = File.ReadAllBytes(strictSource), originalSidecar = File.ReadAllBytes(RadiantSourceWorkspace.SidecarPath(strictSource));
    QuakeMapInterchange.Export(native, strictSource);
    Check(File.ReadAllBytes(strictSource).SequenceEqual(originalSource)
        && File.ReadAllBytes(RadiantSourceWorkspace.SidecarPath(strictSource)).SequenceEqual(originalSidecar),
        "legacy export remains byte-exact when unchanged");
    File.AppendAllText(strictSource, "// edited\n");
    Refused(() => QuakeMapInterchange.Export(native, strictSource), "strict exporter refuses changed external source");
    Check(File.ReadAllBytes(RadiantSourceWorkspace.SidecarPath(strictSource)).SequenceEqual(originalSidecar),
        "refused strict export leaves identity sidecar unchanged");
    string moved = Path.Combine(root, "moved"); Directory.Move(strict, moved);
    Check(QuakeMapInterchange.Import(Path.Combine(moved, "legacy.map"), Path.Combine(moved, "project-prime.json")).Definition.MapId == native.MapId,
        "moving a source and sidecar together preserves map identity");
    var duplicate = Fixture("DUPLICATE"); duplicate.Geometry.Add(new MapBox { Id = duplicate.Geometry[0].Id });
    Refused(() => RadiantSourceWorkspace.Prepare(duplicate, root), "duplicate source object GUIDs are refused");
    string malformed = Path.Combine(root, "malformed.map"); File.WriteAllText(malformed, "{ broken map }");
    Refused(() => QuakeMapInterchange.Import(malformed), "malformed source is rejected by existing parser");
    string oversized = Path.Combine(root, "oversized.map");
    using (var stream = File.Create(oversized)) stream.SetLength(64L * 1024 * 1024 + 1);
    Refused(() => QuakeMapInterchange.Import(oversized), "oversized source is bounded before parsing");
    string launchFixture = Path.Combine(root, "launch size boundary"); Directory.CreateDirectory(launchFixture);
    string fakeEditor = Path.Combine(launchFixture, "fixture editor");
    File.WriteAllText(fakeEditor, "Regular test fixture only; never executed.");
    foreach (long sourceLength in new[] { 32L * 1024 * 1024 + 1, 64L * 1024 * 1024 })
    {
        string launchSource = Path.Combine(launchFixture, $"source {sourceLength}.map");
        using (var stream = File.Create(launchSource)) stream.SetLength(sourceLength);
        // Validate only ProcessStartInfo construction. The fake tool is never launched.
        var start = NetRadiantBridge.CreateEditorStartInfo(fakeEditor, launchSource);
        Check(start.FileName == fakeEditor && !start.UseShellExecute
            && start.WorkingDirectory == launchFixture && start.ArgumentList.Last() == launchSource
            && new FileInfo(launchSource).Length == sourceLength,
            $"editor launch accepts {sourceLength} source bytes with the intact source path as its final argument");
    }
    Refused(() => NetRadiantBridge.CreateEditorStartInfo(fakeEditor, oversized),
        "editor launch rejects 64 MiB plus one byte before process creation");
    if (!OperatingSystem.IsWindows())
    {
        string linkSource = Path.Combine(root, "source-link.map");
        File.CreateSymbolicLink(linkSource, one);
        Refused(() => RadiantSourceTransaction.Acquire(linkSource).Dispose(), "source file symbolic links are refused");
        string linkDirectory = Path.Combine(root, "directory-link");
        Directory.CreateSymbolicLink(linkDirectory, Path.GetDirectoryName(one)!);
        Refused(() => RadiantSourceTransaction.Acquire(Path.Combine(linkDirectory, "world.map")).Dispose(),
            "symbolic links at the workspace owner are refused");
        string linkLockSource = Path.Combine(root, "lock-link.map");
        File.CreateSymbolicLink(linkLockSource + ".prime-lock", Path.Combine(root, "missing-lock-target"));
        Refused(() => RadiantSourceTransaction.Acquire(linkLockSource).Dispose(), "dangling lock symbolic links are refused");
    }
    string? compiler = Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3MAP2_EXE");
    if (!string.IsNullOrEmpty(compiler))
    {
        string compilerRoot = Path.Combine(root, "real-compiler");
        string maps = Path.Combine(compilerRoot, "baseq3", "maps"); Directory.CreateDirectory(maps);
        string textures = Path.Combine(compilerRoot, "baseq3", "textures", "prime"); Directory.CreateDirectory(textures);
        // An original solid gray 2x2 TGA, with no third-party asset input.
        byte[] tga = new byte[18 + 2 * 2 * 3]; tga[2] = 2; tga[12] = 2; tga[14] = 2; tga[16] = 24; tga[17] = 0x20;
        Array.Fill(tga, (byte)128, 18, 12); File.WriteAllBytes(Path.Combine(textures, "gray.tga"), tga);
        var arena = new MapDefinition { MapId = Guid.NewGuid(), FormatVersion = 2, Name = "REAL_COMPILER" };
        arena.Materials.Add(new() { Id = Guid.NewGuid(), Name = "prime/gray" });
        foreach (var bounds in new (float[] Lo, float[] Hi)[]
        {
            (new[] { -17f, -1f, -17f }, new[] { 17f, 0f, 17f }),
            (new[] { -17f, 16f, -17f }, new[] { 17f, 17f, 17f }),
            (new[] { -17f, 0f, -17f }, new[] { -16f, 16f, 17f }),
            (new[] { 16f, 0f, -17f }, new[] { 17f, 16f, 17f }),
            (new[] { -16f, 0f, -17f }, new[] { 16f, 16f, -16f }),
            (new[] { -16f, 0f, 16f }, new[] { 16f, 16f, 17f })
        })
            arena.Geometry.Add(new MapBox { Transform = new()
            {
                Position = bounds.Lo.Zip(bounds.Hi, (lo, hi) => (lo + hi) / 2).ToArray(),
                Scale = bounds.Lo.Zip(bounds.Hi, (lo, hi) => hi - lo).ToArray()
            } });
        arena.Spawns.Add(new() { Id = Guid.NewGuid(), Position = new[] { 0f, .5f, 0f } });
        string arenaSource = Path.Combine(maps, "real_compiler.map");
        QuakeMapInterchange.Export(arena, arenaSource);
        var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(compiler))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = compilerRoot
        };
        foreach (string argument in new[] { "-game", "quake3", "-fs_basepath", compilerRoot, "-fs_game", "baseq3", "-meta", arenaSource })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        string output = await stdout + await stderr;
        Check(process.ExitCode == 0, "real Q3Map2 accepts exported native geometry: " + (process.ExitCode == 0 ? "exit 0" : output));
        byte[] bsp = File.ReadAllBytes(Path.ChangeExtension(arenaSource, ".bsp"));
        Check(bsp.Length >= 144 && Encoding.ASCII.GetString(bsp, 0, 4) == "IBSP" && BitConverter.ToInt32(bsp, 4) == 46
            && BitConverter.ToInt32(bsp, 8 + 8 * 8 + 4) > 0 && BitConverter.ToInt32(bsp, 8 + 13 * 8 + 4) > 0,
            "real Q3Map2 emits nonempty collision brushes and draw surfaces from exported planes");
        Console.WriteLine("Real compiler SHA-256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(compiler))));
    }
    else Console.WriteLine("SKIP real Q3Map2 regression: configure PROJECT_PRIME_Q3MAP2_EXE to enable it.");
    Console.WriteLine($"radiant-source-check: {checks} checks passed. Real editor/platform acceptance remains pending.");
}
finally { Directory.Delete(root, recursive: true); }
