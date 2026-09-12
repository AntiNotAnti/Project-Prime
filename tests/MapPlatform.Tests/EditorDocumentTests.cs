using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using ProjectPrime.Editor.App;
using ProjectPrime.Editor.Commands;
using ProjectPrime.Editor.Documents;

namespace ProjectPrime.MapPlatform.Tests;

public sealed class EditorDocumentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "project-prime-editor-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewProjectStartsWithEditableArenaAndStableIdentity()
    {
        MapDocument document = MapDocument.New("community.test-arena", "Test Arena");

        Assert.Equal("community.test-arena", document.Project.StableId);
        Assert.Single(document.Project.Authoring!.Brushes);
        Assert.Equal(4, document.Project.Authoring.Entities.Count);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void PlacementMovesNewObjectsAwayFromOccupiedLocations()
    {
        Vector3 desired = new(-3, 0, -3);

        Vector3 first = EditorApplication.FindOpenPlacement(desired, [], 4);
        Vector3 second = EditorApplication.FindOpenPlacement(desired, [first], 4);

        Assert.Equal(desired, first);
        Assert.NotEqual(first, second);
        Assert.Equal(desired.Y, second.Y);
        Assert.True((new Vector2(second.X, second.Z)
            - new Vector2(first.X, first.Z)).Length >= 3.2f);
    }

    [Fact]
    public void CommandsRoundTripThroughUndoAndRedo()
    {
        MapDocument document = MapDocument.New("community.commands", "Commands");
        int originalCount = document.Project.Authoring!.Brushes.Count;
        ConvexBrush brush = ConvexBrushFactory.Box("brush.second", "material.default", new(2, 2, 2));

        document.Execute(new CreateBrushCommand(document, brush));
        Assert.Equal(originalCount + 1, document.Project.Authoring.Brushes.Count);
        Assert.True(document.IsDirty);

        document.Undo();
        Assert.Equal(originalCount, document.Project.Authoring.Brushes.Count);
        Assert.False(document.IsDirty);

        document.Redo();
        Assert.Equal(originalCount + 1, document.Project.Authoring.Brushes.Count);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void SaveThenEditIsDirty()
    {
        Directory.CreateDirectory(_directory);
        MapDocument document = MapDocument.New("community.save-edit", "Save Edit");
        document.Save(Path.Combine(_directory, "save-edit.json"));

        document.Execute(Rename(document, "Edited"));

        Assert.True(document.IsDirty);
        Assert.NotEqual(document.SavedStateId, document.CurrentStateId);
    }

    [Fact]
    public void SaveEditUndoIsClean()
    {
        Directory.CreateDirectory(_directory);
        MapDocument document = MapDocument.New("community.save-undo", "Save Undo");
        document.Save(Path.Combine(_directory, "save-undo.json"));
        document.Execute(Rename(document, "Edited"));

        document.Undo();

        Assert.False(document.IsDirty);
        Assert.Equal(document.SavedStateId, document.CurrentStateId);
    }

    [Fact]
    public void BranchAfterUndoDoesNotReuseSavedStateIdentity()
    {
        Directory.CreateDirectory(_directory);
        MapDocument document = MapDocument.New("community.branch", "Branch");
        document.Execute(Rename(document, "Edit A"));
        document.Save(Path.Combine(_directory, "branch.json"));
        DocumentStateId saved = document.SavedStateId;
        document.Execute(Rename(document, "Edit B"));
        document.Undo();

        document.Execute(Rename(document, "Edit C"));

        Assert.True(document.IsDirty);
        Assert.NotEqual(saved, document.CurrentStateId);
        Assert.Equal("Edit C", document.Project.Metadata.Name);
    }

    [Fact]
    public void UndoPastSaveIsDirtyAndRedoBackToSaveIsClean()
    {
        Directory.CreateDirectory(_directory);
        MapDocument document = MapDocument.New("community.saved-node", "Saved Node");
        document.Execute(Rename(document, "A"));
        document.Execute(Rename(document, "Saved"));
        document.Save(Path.Combine(_directory, "saved-node.json"));

        document.Undo();
        Assert.True(document.IsDirty);

        document.Redo();
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void SaveResetsDirtyIdentity()
    {
        Directory.CreateDirectory(_directory);
        MapDocument document = MapDocument.New("community.save-reset", "Save Reset");
        document.Execute(Rename(document, "Changed"));
        Assert.True(document.IsDirty);

        document.Save(Path.Combine(_directory, "save-reset.json"));

        Assert.False(document.IsDirty);
        Assert.Equal(document.CurrentStateId, document.SavedStateId);
    }

    [Fact]
    public void AutosaveRecoveryNeverOverwritesCreatorProject()
    {
        Directory.CreateDirectory(_directory);
        string projectPath = Path.Combine(_directory, "map.json");
        string autosaves = Path.Combine(_directory, "autosave");
        MapDocument document = MapDocument.New("community.recovery", "Recovery");
        document.Save(projectPath);
        byte[] saved = File.ReadAllBytes(projectPath);
        document.Execute(new ModifyPropertyCommand(document, "Rename",
            project => project.Metadata.Name = "Recovered Name"));

        var service = new AutosaveService(autosaves, generations: 2);
        service.Tick(document, TimeSpan.Zero);
        string? recovery = service.Latest(document.Project.StableId);
        Assert.NotNull(recovery);

        MapDocument reopened = MapDocument.Open(projectPath);
        reopened.Recover(recovery!);
        Assert.Equal("Recovered Name", reopened.Project.Metadata.Name);
        Assert.True(reopened.IsDirty);
        Assert.Equal(saved, File.ReadAllBytes(projectPath));

        service.Discard(recovery!);
        Assert.False(File.Exists(recovery));
    }

    [Fact]
    public void PlaytestSnapshotDoesNotChangeDocumentPathOrDirtyState()
    {
        Directory.CreateDirectory(_directory);
        string projectPath = Path.Combine(_directory, "map.json");
        string snapshotPath = Path.Combine(_directory, ".playtest.json");
        MapDocument document = MapDocument.New("community.snapshot", "Snapshot");
        document.Save(projectPath);
        document.Execute(new ModifyPropertyCommand(document, "Rename",
            project => project.Metadata.Name = "Unsaved Playtest"));

        document.SaveSnapshot(snapshotPath);

        Assert.Equal(Path.GetFullPath(projectPath), document.Path);
        Assert.True(document.IsDirty);
        Assert.Equal("Snapshot", MapProjectIO.Load(projectPath).Metadata.Name);
        Assert.Equal("Unsaved Playtest", MapProjectIO.Load(snapshotPath).Metadata.Name);
    }

    [Fact]
    public void UndoRedoRestoresImportedSourceRuntimeContext()
    {
        Directory.CreateDirectory(_directory);
        string source = Path.Combine(_directory, "arena.bsp");
        File.WriteAllBytes(source, [1, 2, 3]);
        string projectPath = Path.Combine(_directory, "map.json");
        var project = new MapProject
        {
            StableId = "community.import-context",
            Metadata = new MapProjectMetadata { Name = "Import Context" },
            Map = new MapDefinition
            {
                Name = "IMPORT CONTEXT",
                Import = new MapImport { Source = "arena.bsp", MapName = "arena" }
            }
        };
        MapProjectIO.Save(project, projectPath);
        MapDocument document = MapDocument.Open(projectPath);

        document.Execute(Rename(document, "Changed"));
        document.Undo();
        document.Redo();

        Assert.Equal(Path.GetFullPath(projectPath), document.Project.SourcePath);
        Assert.Equal(_directory, document.Project.Map.BaseDirectory);
        Assert.Equal(_directory, document.Project.Map.Import!.BaseDirectory);
        Assert.Equal(source, document.Project.Map.Import.Resolve());
    }

    [Fact]
    public void AuthoringEnvironmentBecomesTheCompiledRuntimeEnvironment()
    {
        MapDocument document = MapDocument.New("community.environment", "Environment");
        document.Project.Environment!.KillHeight = -123;
        document.Project.Environment.FarClip = 725;
        document.Project.Environment.FogEnabled = false;

        MapBuildScene scene = new NativeMapProjectImporter().Import(document.Project, verbose: false);

        Assert.Equal(-123, scene.Definition.KillHeight);
        Assert.Equal(725, scene.Definition.FarClip);
        Assert.False(scene.Definition.FogEnabled);
    }

    [Fact]
    public void MaterialCommandTargetsOneFaceOrTheWholeBrushAndUndoRestoresIt()
    {
        MapDocument document = MapDocument.New("community.materials", "Materials");
        MapAuthoringScene scene = document.Project.Authoring!;
        scene.Materials.Add(new MapAuthoringMaterial
            { Id = "material.second", Name = "Second", SourceMaterial = 2 });
        ConvexBrush brush = scene.Brushes[0];

        document.Execute(new ChangeMaterialCommand(document, brush.Id, "material.second", faceIndex: 0));
        Assert.Equal("material.second", document.Project.Authoring!.Brushes[0].Faces[0].MaterialId);
        Assert.All(document.Project.Authoring.Brushes[0].Faces.Skip(1),
            face => Assert.Equal("material.default", face.MaterialId));

        document.Undo();
        Assert.All(document.Project.Authoring.Brushes[0].Faces,
            face => Assert.Equal("material.default", face.MaterialId));
        document.Execute(new ChangeMaterialCommand(document, brush.Id, "material.second"));
        Assert.All(document.Project.Authoring.Brushes[0].Faces,
            face => Assert.Equal("material.second", face.MaterialId));
    }

    [Fact]
    public void TransformTransactionsCoalesceAndCommonCommandsUseDeltas()
    {
        MapDocument document = MapDocument.New("community.coalesced", "Coalesced");
        MapTransform original = document.Project.Authoring!.Brushes[0].Transform;
        var first = new MapTransform
        {
            Position = [1, 0, 0], Rotation = [0, 0, 0], Scale = [1, 1, 1]
        };
        document.ExecuteCoalesced(
            new TransformObjectCommand(document, "brush.floor", first), "drag-1");
        var final = new MapTransform
        {
            Position = [3, 2, 1], Rotation = [0, 45, 0], Scale = [2, 1, 0.5f]
        };
        document.ExecuteCoalesced(
            new TransformObjectCommand(document, "brush.floor", final), "drag-1");

        Assert.Equal(1, document.UndoCount);
        Assert.True(document.HistoryMemoryBytes <= 256);
        Assert.Equal(final.Position, document.Project.Authoring.Brushes[0].Transform.Position);

        document.Undo();
        Assert.Equal(original.Position, document.Project.Authoring.Brushes[0].Transform.Position);
        document.Redo();
        Assert.Equal(final.Position, document.Project.Authoring.Brushes[0].Transform.Position);
    }

    [Fact]
    public void HistoryDropsOldestCommandsAtConfiguredLimitWithoutBreakingDirtyState()
    {
        MapDocument document = MapDocument.New("community.bounded", "Bounded",
            new EditorHistoryLimits(3, 1024 * 1024));
        for (int index = 0; index < 6; index++)
            document.Execute(Rename(document, $"Edit {index}"));

        Assert.Equal(3, document.UndoCount);
        Assert.True(document.IsDirty);
        document.Undo();
        document.Undo();
        document.Undo();
        Assert.False(document.CanUndo);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void Q3FactoryCreatesAReadOnlyImportedProjectWithPortableRelativeSource()
    {
        Directory.CreateDirectory(_directory);
        string projectPath = Path.Combine(_directory, "project", "map.project.json");
        string source = Path.Combine(_directory, "project", "assets", "fixture.bsp");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, [1, 2, 3]);

        MapProject project = Q3MapProjectFactory.Create(source, projectPath,
            "community.q3-fixture", "Q3 Fixture", mapName: "fixture",
            unitsPerUnit: 32, keepClip: false, keepSky: true, patchLevel: 4);

        Assert.Null(project.Authoring);
        Assert.Equal("fixture", project.Map.Import!.MapName);
        Assert.Equal(32, project.Map.Import.UnitsPerUnit);
        Assert.False(project.Map.Import.KeepClip);
        Assert.True(project.Map.Import.KeepSky);
        Assert.Equal(4, project.Map.Import.PatchLevel);
        Assert.Equal("assets/fixture.bsp", project.Map.Import.Source);
    }

    [Fact]
    public void Q3FactoryCopiesExternalSourcesIntoProjectByDefault()
    {
        Directory.CreateDirectory(_directory);
        string source = Path.Combine(_directory, "incoming", "fixture.pk3");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        string texture = Path.Combine(_directory, "incoming", "fixture.tex");
        File.WriteAllBytes(texture, [5, 6, 7]);
        string projectPath = Path.Combine(_directory, "project", "map.project.json");

        MapProject project = Q3MapProjectFactory.Create(source, projectPath,
            "community.portable-q3", "Portable Q3", textures: texture);
        MapProjectIO.Save(project, projectPath);
        File.Delete(source);
        File.Delete(texture);

        Assert.Equal("source/fixture.pk3", project.Map.Import!.Source);
        Assert.Equal("textures/fixture.tex", project.Map.Import.Textures);
        Assert.False(Path.IsPathRooted(project.Map.Import.Source));
        Assert.True(File.Exists(project.Map.Import.Resolve()));
        Assert.True(File.Exists(project.Map.Import.ResolveTextures()));
    }

    [Fact]
    public void Q3FactoryCanExplicitlyRetainAnExternalDevelopmentReference()
    {
        Directory.CreateDirectory(_directory);
        string source = Path.Combine(_directory, "incoming", "external.bsp");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, [1, 2, 3]);
        string projectPath = Path.Combine(_directory, "project", "map.project.json");

        MapProject project = Q3MapProjectFactory.Create(source, projectPath,
            "community.external-q3", "External Q3",
            sourceReferenceMode: Q3SourceReferenceMode.ReferenceExternally);

        Assert.Equal(Path.GetFullPath(source), project.Map.Import!.Source);
        Assert.False(Directory.Exists(Path.Combine(_directory, "project", "source")));
    }

    [Fact]
    public async Task EditorRequiresBaseContentOnlyForMapsThatBorrowIt()
    {
        Directory.CreateDirectory(_directory);
        using var builds = new ProjectPrime.Editor.App.EditorBuildService(null, "AMHE1",
            Path.Combine(_directory, "cache"));
        MapProject borrowed = MapDocument.New("community.borrowed", "Borrowed").Project;

        MapBuildResult result = await builds.BuildAsync(borrowed, false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, value => value.Code == "MAP-DEP-010");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static ModifyPropertyCommand Rename(MapDocument document, string name)
        => new(document, "Rename", project => project.Metadata.Name = name);
}
