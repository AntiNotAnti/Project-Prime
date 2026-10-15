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
    MapId = Guid.NewGuid(), FormatVersion = 2, Name = name,
    Author = "Native owner", Description = "Unchanged metadata", Version = "19",
    Materials = new() { new() { Id = Guid.NewGuid(), Name = "textures/prime/metal", Texture = "assets/metal.png",
        Albedo = "assets/metal-hd.png", Alpha = 24, TwoSided = true,
        Animation = new() { UvScroll = new[] { .1f, .2f } } } },
    Geometry = new() { new MapBox { Id = Guid.NewGuid(), Label = "Author label", Layer = "Floor", Shade = .7f, Transform = new() { Scale = new[] { 8f, 2f, 8f } } } },
    Spawns = new() { new() { Id = Guid.NewGuid(), Team = 0, Position = new[] { 0f, 4f, 0f } } },
    Items = new() { new() { Id = Guid.NewGuid(), SpawnInterval = 721, HasBase = false } },
    JumpPads = new() { new() { Id = Guid.NewGuid(), Speed = 22 } },
    Audio = new() { Volume = .6f }, KillHeight = -91,
    NativeRoom = new() { Room = "MP3 PROVING GROUND", PreserveEntities = true },
    Collision = new() { Source = "collision.obj", ZUp = true },
    Brushes = new() { new() { Id = Guid.NewGuid(), Label = "Native-only brush", Damaging = true } },
    NavigationLinks = new() { new() { Kind = MapNavigationLinkKind.Teleporter, To = new[] { 1f, 2f, 3f } } },
    Q3Triggers = new() { new() { Kind = "teleport", Destination = new[] { 4f, 5f, 6f } } }
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
        bool moved = current.Geometry.OfType<MapBox>().Any(before =>
            review.Proposed.Geometry.Single(g => g.Id == before.Id) is MapBox after
            && before.Transform.Position.Concat(before.Transform.Scale)
                .Zip(after.Transform.Position.Concat(after.Transform.Scale), (a, b) => Math.Abs(a - b) > .0001f).Any(x => x));
        Check(moved, "actual GUI save contains a box position or size edit, beyond serialization changes");
        var expected = MapProjectSerializer.Clone(current);
        expected.Geometry = review.Proposed.Geometry; expected.Materials = review.Proposed.Materials;
        Check(expected.Serialize() == review.Proposed.Serialize(), "GUI review retains every native gameplay and project domain");
        RadiantSourceWorkspace.RequireFresh(current, review);
        var document = new MapDocument(new(current));
        string before = document.Project.Definition.Serialize();
        document.Edit("Apply actual Radiant GUI geometry", d =>
        { d.Geometry = review.Proposed.Geometry; d.Materials = review.Proposed.Materials; });
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
try
{
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
    document.Edit("Apply Radiant geometry", d => { d.Geometry = review.Proposed.Geometry; d.Materials = review.Proposed.Materials; });
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
    geometryEdit.Geometry[0].Locked = true; geometryEdit.Geometry[0].Terrain = "Stone";
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
        && geometryReview.Proposed.Geometry[0].Locked && geometryReview.Proposed.Geometry[0].Terrain == "Stone"
        && geometryReview.Proposed.Geometry[0].Shade == geometryEdit.Geometry[0].Shade,
        "stock-Radiant-style save keeps geometry IDs, labels, layers and shade after comments disappear");
    var expectedHybrid = MapProjectSerializer.Clone(geometryEdit);
    expectedHybrid.Geometry = geometryReview.Proposed.Geometry;
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
    var patch = new MapBezierPatch { Label = "Native patch", Solid = false, Tessellation = 2 };
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
            candidate += "\n" + original[original.IndexOf("{\n\"classname\" \"func_group\"", StringComparison.Ordinal)..];
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
        Check(manifest.RootElement.GetProperty("Version").GetInt32() == 2, "legacy upgrade advances the native adjunct format");
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
    Refused(() => RadiantSourceWorkspace.Prepare(unsupported, root), "unsupported meshes refuse export instead of flattening");
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
    using (var stream = File.Create(oversized)) stream.SetLength(32L * 1024 * 1024 + 1);
    Refused(() => QuakeMapInterchange.Import(oversized), "oversized source is bounded before parsing");
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
