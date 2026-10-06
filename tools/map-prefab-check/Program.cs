using System.Numerics;
using System.Text;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

string root = Path.Combine(Path.GetTempPath(), "prime-map-prefab-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
try
{
    Headless.Enter();
    string sourceRoot = Path.Combine(root, "source"), targetRoot = Path.Combine(root, "target");
    Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(targetRoot);
    using (var writer = new BinaryWriter(File.Create(Path.Combine(sourceRoot, "tile.tex"))))
    {
        writer.Write(Encoding.ASCII.GetBytes("FPTX")); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((ushort)0); writer.Write((ushort)8); writer.Write((ushort)8); writer.Write((ushort)1);
        writer.Write((ushort)0); writer.Write((ushort)32767); writer.Write(new byte[64]);
    }
    var source = new MapDefinition { Name = "PREFAB_SOURCE", FormatVersion = 2, MapId = Guid.NewGuid(), BaseDirectory = sourceRoot };
    source.Materials.Add(new() { Texture = "tile.tex", Name = "Floor" }); source.Assets.Add(new() { Path = "tile.tex" });
    var box = new MapBox { Label = "Source floor", Transform = new() { Position = [0f, -1, 0], Scale = [8f, 1, 8] } };
    var spawn = new MapSpawn { Id = Guid.NewGuid(), Position = [0f, 2, 0] };
    var item = new MapItem { Id = Guid.NewGuid(), Position = [1f, 2, 0] };
    var navigation = new MapNavigationLink { From = [0f, 2, 0], To = [1f, 2, 0] };
    source.Geometry.Add(box); source.Spawns.Add(spawn); source.Items.Add(item); source.NavigationLinks.Add(navigation);
    string prefab = Path.Combine(root, "library", "arena.json");
    var selected = new HashSet<Guid> { box.Id, spawn.Id, item.Id, navigation.Id };
    MapPrefabService.Save(source, selected, prefab);
    var library = MapDefinition.Load(prefab);
    Guid sourceId = library.PrefabSource!.Id; string revision = library.PrefabSource.Revision;
    MapPrefabService.Save(source, selected, prefab);
    library = MapDefinition.Load(prefab);
    Check(library.PrefabSource!.Id == sourceId && library.PrefabSource.Revision == revision, "unchanged save preserves stable prefab identity and content revision");
    var target = new MapDefinition { Name = "PREFAB_TARGET", FormatVersion = 2, MapId = Guid.NewGuid(), BaseDirectory = targetRoot };
    File.Copy(Path.Combine(sourceRoot, "tile.tex"), Path.Combine(targetRoot, "unused.tex"));
    target.Materials.Add(new() { Texture = "unused.tex", Name = "Unrelated material" }); target.Assets.Add(new() { Path = "unused.tex" });
    // A large unrelated object proves retained history does not snapshot the full document.
    var unrelated = new MapMesh { Label = "Unrelated mesh" }; unrelated.Vertices.AddRange(Enumerable.Range(0, 12000).Select(i => new[] { (float)i, 0, 0 }));
    target.Geometry.Add(unrelated);
    var document = new MapDocument(new(target));
    object originalUnrelated = document.Project.Definition.Geometry[0];
    var inserted = document.InsertPrefab(prefab, targetRoot, new() { Position = [10f, 0, 0], Scale = [2f, 2, 2] });
    Check(inserted.ObjectIds.Count == 4 && inserted.InstanceId != Guid.Empty && document.History.CommandCount == 1, "linked insertion uses canonical document and one history command");
    Check(document.History.ApproximateBytes < 35000 && ReferenceEquals(originalUnrelated, document.Project.Definition.Geometry.First(g => g.Id == unrelated.Id)), "prefab history retains changed objects instead of unrelated large mesh");
    var instance = document.Project.Definition.PrefabInstances.Single();
    Guid boxId = instance.Members.Single(m => m.SourceObjectId == box.Id).ObjectId;
    Guid spawnId = instance.Members.Single(m => m.SourceObjectId == spawn.Id).ObjectId;
    Guid itemId = instance.Members.Single(m => m.SourceObjectId == item.Id).ObjectId;
    Check(document.Project.Definition.Geometry.Single(g => g.Id == boxId).Transform.Position.SequenceEqual(new[] { 10f, -2, 0 }), "instance position and scale flatten into ordinary geometry transform");
    Check(document.Project.Definition.NavigationLinks.Single().To.SequenceEqual(new[] { 12f, 4, 0 }), "instance transform includes navigation endpoints");
    Check(document.Project.Definition.Items.Single().Position.SequenceEqual(new[] { 12f, 4, 0 }), "instance transform includes pickups");
    string afterInsert = document.Project.Definition.Serialize();
    document.History.Undo(); Check(document.Project.Definition.PrefabInstances.Count == 0 && document.Project.Definition.Geometry.Count == 1, "insertion undo removes linked objects and provenance");
    document.History.Redo(); Check(document.Project.Definition.Serialize() == afterInsert, "insertion redo restores exact instance identity and asset references");
    var preparedDocument = new MapDocument(new(target));
    using (var firstPrepared = preparedDocument.PreparePrefabInsertAsync(prefab, targetRoot).GetAwaiter().GetResult())
    using (var secondPrepared = preparedDocument.PreparePrefabInsertAsync(prefab, targetRoot).GetAwaiter().GetResult())
    {
        Check(preparedDocument.History.CommandCount == 0 && preparedDocument.Project.Definition.PrefabInstances.Count == 0,
            "worker preparation never adopts objects or increments document history");
        bool pendingRejected = false;
        try { preparedDocument.PreparePrefabInsertAsync(prefab, targetRoot).GetAwaiter().GetResult(); } catch (InvalidOperationException) { pendingRejected = true; }
        Check(pendingRejected, "pending prefab preparation count is bounded");
        preparedDocument.EditObjects("Concurrent edit", new[] { unrelated.Id }, d => d.Geometry[0].Label = "Changed while preparing");
        string edited = preparedDocument.Project.Definition.Serialize();
        bool staleRejected = false;
        try { preparedDocument.ApplyPreparedPrefabEdit(firstPrepared); } catch (InvalidOperationException) { staleRejected = true; }
        Check(staleRejected && preparedDocument.Project.Definition.Serialize() == edited, "stale worker result cannot replace newer owner document state");
    }
    Check(!Directory.GetDirectories(targetRoot, ".prefab-preparation-*").Any(), "abandoned preparation disposal removes every private staging directory");
    using (var prepared = preparedDocument.PreparePrefabInsertAsync(prefab, targetRoot).GetAwaiter().GetResult())
    {
        bool threadRejected = Task.Run(() => { try { preparedDocument.ApplyPreparedPrefabEdit(prepared); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        Check(threadRejected, "worker cannot execute document adoption or history callbacks");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool cancellationRejected = false;
        try { preparedDocument.ApplyPreparedPrefabEdit(prepared, canceled.Token); } catch (OperationCanceledException) { cancellationRejected = true; }
        Check(cancellationRejected && preparedDocument.Project.Definition.PrefabInstances.Count == 0, "cancellation before adoption preserves authoring graph");
        preparedDocument.ApplyPreparedPrefabEdit(prepared);
        Check(preparedDocument.Project.Definition.PrefabInstances.Count == 1 && preparedDocument.History.CommandCount == 2,
            "owner adopts prepared objects and assets through one additional delta command");
    }
    var saveAsDocument = new MapDocument(new(target));
    using (var prepared = saveAsDocument.PreparePrefabInsertAsync(prefab, targetRoot).GetAwaiter().GetResult())
    {
        var sameState = saveAsDocument.CurrentStateId;
        saveAsDocument.Save(Path.Combine(root, "save-as", "map.json"));
        bool contextRejected = false;
        try { saveAsDocument.ApplyPreparedPrefabEdit(prepared); } catch (InvalidOperationException) { contextRejected = true; }
        Check(contextRejected && saveAsDocument.CurrentStateId == sameState && saveAsDocument.Project.Definition.PrefabInstances.Count == 0,
            "Save As during preparation rejects prior asset context even when document state ID is unchanged");
    }
    var frozen = document.CaptureBuildSnapshot();
    document.EditObjects("Local label", new[] { boxId }, d => d.Geometry[0].Label = "Local floor");
    document.EditObjects("Delete spawn", new[] { spawnId }, d => d.Spawns.Clear());
    box.Shade = .7f; item.Type = "HealthSmall";
    MapPrefabService.Save(source, selected, prefab);
    string beforeUpdate = document.Project.Definition.Serialize();
    var updated = document.UpdatePrefab(inserted.InstanceId, targetRoot);
    instance = document.Project.Definition.PrefabInstances.Single();
    Check(instance.SourceRevision != revision && instance.SourceId == sourceId && updated.PreservedOverrides >= 1, "update advances source revision and preserves stable linked identity");
    Check(document.Project.Definition.Geometry.Single(g => g.Id == boxId) is { Label: "Local floor", Shade: .7f }, "source field updates preserve independent local field override");
    Check(document.Project.Definition.Spawns.Count == 0 && instance.Members.Single(m => m.ObjectId == spawnId).Deleted, "locally deleted member remains deleted across source update");
    Check(document.Project.Definition.Items.Single(i => i.Id == itemId).Type == "HealthSmall", "source pickup change updates in place with stable instance object ID");
    Check(frozen.CreateDefinition().Geometry.Single(g => g.Id == boxId).Label == "Source floor" && frozen.CreateDefinition().Spawns.Count == 1, "build snapshot detaches prefab members, baseline and transformed objects");
    string afterUpdate = document.Project.Definition.Serialize();
    document.History.Undo(); Check(document.Project.Definition.Serialize() == beforeUpdate, "update undo restores exact local override and prior source revision");
    document.History.Redo(); Check(document.Project.Definition.Serialize() == afterUpdate, "update redo restores exact stable IDs and preserved overrides");
    byte[] validLibrary = File.ReadAllBytes(prefab);
    var wrongSource = MapDefinition.Load(prefab); wrongSource.PrefabSource!.Id = Guid.NewGuid(); wrongSource.Save(prefab);
    bool identityRejected = false;
    try { document.UpdatePrefab(inserted.InstanceId, targetRoot); } catch (InvalidDataException) { identityRejected = true; }
    Check(identityRejected && document.Project.Definition.Serialize() == afterUpdate, "same path with substituted source identity cannot change instance or history");
    File.WriteAllBytes(prefab, validLibrary);
    document.EditObjects("Convert linked primitive", new[] { boxId }, d =>
    { var converted = MapMeshEditing.Convert(d.Geometry[0], 16); converted.Id = boxId; d.Geometry[0] = converted; });
    string convertedState = document.Project.Definition.Serialize();
    bool convertedRejected = false;
    try { document.UpdatePrefab(inserted.InstanceId, targetRoot); } catch (InvalidDataException) { convertedRejected = true; }
    Check(convertedRejected && document.Project.Definition.Serialize() == convertedState,
        "local object type conversion rejects incompatible source update without discarding modeled geometry");
    document.History.Undo();
    document.EditObjects("Local pickup", new[] { itemId }, d => d.Items[0].SpawnInterval = 777);
    selected.Remove(item.Id); selected.Remove(navigation.Id);
    var added = new MapItem { Id = Guid.NewGuid(), Position = [2f, 2, 0] }; source.Items.Add(added); selected.Add(added.Id);
    MapPrefabService.Save(source, selected, prefab);
    document.UpdatePrefab(inserted.InstanceId, targetRoot);
    instance = document.Project.Definition.PrefabInstances.Single();
    Check(document.Project.Definition.Items.Any(i => i.Id == itemId && i.SpawnInterval == 777) && !instance.Members.Any(m => m.ObjectId == itemId), "locally modified member removed upstream becomes independent without losing edits");
    Check(document.Project.Definition.NavigationLinks.Count == 0, "unchanged member removed upstream disappears from resolved runtime graph");
    Check(instance.Members.Any(m => m.SourceObjectId == added.Id), "new source member receives a distinct stable instance identity");
    // Source files can disappear; resolved maps, snapshots, transform and detach remain usable.
    File.Delete(prefab);
    var state = document.CurrentStateId; string unchanged = document.Project.Definition.Serialize(); long historyBytes = document.History.ApproximateBytes;
    bool rejected = false; try { document.UpdatePrefab(inserted.InstanceId, targetRoot); } catch (IOException) { rejected = true; }
    Check(rejected && document.CurrentStateId == state && document.Project.Definition.Serialize() == unchanged && document.History.ApproximateBytes == historyBytes, "missing source update is transactional and creates no history state");
    document.SetPrefabTransform(inserted.InstanceId, new() { Position = [20f, 0, 0], Scale = [2f, 2, 2] });
    Check(document.Project.Definition.Geometry.Single(g => g.Id == boxId).Transform.Position[0] == 20 && document.Project.Definition.Geometry.Single(g => g.Id == boxId).Label == "Local floor", "transform changes resolved objects while source is absent and preserves edits");
    state = document.CurrentStateId; unchanged = document.Project.Definition.Serialize();
    rejected = false; try { document.SetPrefabTransform(inserted.InstanceId, new() { Scale = [0f, 1, 1] }); } catch (InvalidDataException) { rejected = true; }
    Check(rejected && document.CurrentStateId == state && document.Project.Definition.Serialize() == unchanged, "invalid transform leaves document and history byte-identical");
    string projectPath = Path.Combine(targetRoot, "map.json"); document.Save(projectPath);
    var reloaded = MapDefinition.Load(projectPath);
    Check(reloaded.PrefabInstances.Count == 1 && reloaded.PrefabInstances[0].Members.Any(m => m.Overrides != null), "authoring project roundtrips linked revision and local overrides");
    var beforeDiff = MapPrefabService.Flatten(reloaded); var afterDiff = MapBuildSnapshot.Capture(beforeDiff).CreateDefinition();
    afterDiff.Geometry.First().Shade = .4f; afterDiff.Materials.First().Name = "Changed material";
    afterDiff.Spawns.Add(new() { Id = Guid.NewGuid() }); afterDiff.Items.First().Type = "HealthBig";
    afterDiff.Capabilities = new() { SupportedModes = ["Battle"] }; afterDiff.Assets.Add(new() { Path = "extra.tex" });
    afterDiff.NavigationLinks.Add(new() { From = [0f, 0, 0], To = [1f, 0, 0] });
    var diff = MapStructuralDiff.Compare(beforeDiff, afterDiff);
    foreach (var category in new[] { MapDiffCategory.Object, MapDiffCategory.Material, MapDiffCategory.Spawn, MapDiffCategory.Pickup, MapDiffCategory.Capability, MapDiffCategory.Asset, MapDiffCategory.Navigation })
        Check(diff.ForCategory(category).Count != 0, "structural diff reports " + category + " changes");
    var reordered = MapBuildSnapshot.Capture(reloaded).CreateDefinition();
    reordered.Materials.Reverse(); int Remap(int slot) => reordered.Materials.Count - 1 - slot;
    foreach (var geometry in reordered.Geometry) { geometry.Material = Remap(geometry.Material); if (geometry is MapMesh mesh) mesh.FaceMaterials = mesh.FaceMaterials.Select(Remap).ToList(); }
    Check(MapStructuralDiff.Compare(reloaded, reordered).IsEmpty, "material slot reordering with stable IDs creates no semantic structural changes");
    string beforeDetach = document.Project.Definition.Serialize(); string fingerprint = MapBuildFingerprint.Create(document.Project.Definition).ContentKey;
    document.DetachPrefab(inserted.InstanceId);
    Check(document.Project.Definition.PrefabInstances.Count == 0 && document.Project.Definition.Geometry.Any(g => g.Id == boxId), "detach retains resolved objects and material assets");
    Check(MapBuildFingerprint.Create(document.Project.Definition).ContentKey == fingerprint, "authoring provenance does not invalidate canonical runtime build fingerprint");
    document.History.Undo(); Check(document.Project.Definition.Serialize() == beforeDetach, "detach undo restores exact linked instance metadata");
    var runtime = MapPrefabService.Flatten(document.Project.Definition); runtime.Geometry.RemoveAll(g => g.Id == unrelated.Id);
    runtime.Spawns.Add(new() { Id = Guid.NewGuid(), Position = [20f, 3, 0] });
    var compilation = MapCompiler.Compile(runtime);
    Check(compilation.Map != null && compilation.Validation.IsValid, "flattened resolved prefab compiles through canonical map compiler without library file: " + string.Join("; ", compilation.Validation.Diagnostics.Select(d => d.Code + " " + d.Message)));
    string package = MapPackageBuilder.Build(runtime, Path.Combine(root, "resolved.ppmap"));
    var packaged = MapDefinition.Load(package);
    Check(packaged.PrefabInstances.Count == 0 && packaged.PrefabSource == null && packaged.Geometry.Count > 0, "runtime package contains resolved objects and excludes external authoring source metadata");
    Check(MapCompiler.Compile(packaged).Validation.IsValid, "resolved package compiles after original prefab file disappears");
    Console.WriteLine($"Map prefab/diff checks passed: {checks}.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(root, true); }

void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
