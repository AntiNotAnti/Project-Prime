using MphRead.Mods.MapGen;
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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
