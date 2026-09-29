using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private void ImportModel() => Browse("Import OBJ / glTF / GLB model", false, path => ModelImportOptions(path), ".obj", ".gltf", ".glb");

    private void ModelImportOptions(string path, MapModelSource? source = null)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text("3D MODEL · " + Path.GetFileName(path)));
        panel.Children.Add(Text("OBJ, glTF and GLB dependencies are resolved inside the model folder. Static geometry retains materials and UV0."));
        var settings = source?.Settings ?? new();
        var scale = new TextBox { Text = settings.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        panel.Children.Add(Text("Scale")); panel.Children.Add(scale);
        var reverse = new CheckBox { Content = "Reverse face winding", IsChecked = settings.FlipWinding }; panel.Children.Add(reverse);
        var zUp = new CheckBox { Content = "Z-up source (Blender / 3ds Max)", IsChecked = settings.ZUp }; panel.Children.Add(zUp);
        var flipUv = new CheckBox { Content = "Flip UV vertically", IsChecked = settings.FlipUvVertical }; panel.Children.Add(flipUv);
        var collision = new ComboBox { ItemsSource = Enum.GetValues<ModelCollisionMode>(),
            SelectedItem = settings.VisualCollision ? ModelCollisionMode.Visual : settings.Collision };
        panel.Children.Add(Text("Collision: None, Visual, Simplified, BoundingBoxes, or Companion")); panel.Children.Add(collision);
        string? companion = ModelCollisionImport.FindCompanion(path);
        if (companion != null) panel.Children.Add(Text("Companion detected: " + Path.GetFileName(companion)));
        panel.Children.Add(Text("Collision proxies remain editable in the Collision layer and are invisible in play. Bounding boxes can block openings; inspect before applying."));
        AddButton(panel, "Preview changes", () =>
        {
            try
            {
                var options = new ModelImportSettings(Number(scale.Text ?? "1"), reverse.IsChecked == true, false, zUp.IsChecked == true, flipUv.IsChecked == true, (ModelCollisionMode)(collision.SelectedItem ?? ModelCollisionMode.None));
                Dismiss(); AnalyzeModel(path, options, source?.Id);
            }
            catch (Exception ex) { Failure(ex); }
        });
        AddButton(panel, "Cancel", Dismiss); Modal(panel);
    }

    private void ManageModelSources()
    {
        if (_document == null) return;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text("IMPORTED MODEL SOURCES"));
        foreach (var source in _document.Project.Definition.ModelSources)
        {
            panel.Children.Add(Text(Path.GetFileName(source.Source) + $" · {source.Objects.Count} objects"));
            panel.Children.Add(Text(source.Source));
            AddButton(panel, "Reimport / check external changes", () => ModelImportOptions(source.Source, source));
            AddButton(panel, "Locate source…", () => Browse("Locate source model", false, path => ModelImportOptions(path, source), ".obj", ".gltf", ".glb"));
            AddButton(panel, "Select generated objects", () =>
            {
                _document.Selection.Clear();
                foreach (var item in source.Objects) _document.Selection.Add(item.Id);
                _document.SelectionChanged(); _viewport?.FrameSelection(); Dismiss();
            });
            AddButton(panel, "Detach source (keep geometry)", () =>
            {
                _document.Edit("Detach model source", d => d.ModelSources.RemoveAll(s => s.Id == source.Id), MapChangeDomain.Geometry);
                Dismiss();
            });
        }
        if (_document.Project.Definition.ModelSources.Count == 0) panel.Children.Add(Text("Import a 3D model to track its external source."));
        AddButton(panel, "Close", Dismiss); Modal(panel);
    }

    private void AnalyzeModel(string path, ModelImportSettings settings, Guid? sourceId) => _ = Job("Analyzing model", async token =>
    {
        if (_document == null) throw new IOException("Create or open a map first.");
        var document = _document;
        var state = document.CurrentStateId;
        if (document.Project.Definition.BundlePath != null) throw new IOException("Save this package as an editable project before importing a model.");
        var result = await Task.Run(() => ModelImportService.Import(path, settings, token), token);
        var hash = await Task.Run(() => MapSourceFingerprint.Hash(result.Dependencies), token);
        GuardJob(token);
        var previous = document.Project.Definition.ModelSources.FirstOrDefault(s => s.Id == sourceId);
        if (previous != null && previous.NormalizedHash == ModelReimport.NormalizedHash(result)
            && previous.Settings == settings && previous.Source == path && previous.SourceHash == hash)
        { _status.Text = "Model and referenced materials are unchanged."; return; }
        string root = document.Project.Definition.BaseDirectory ?? Path.GetDirectoryName(Path.GetFullPath(_path.Text!))!;
        foreach (var asset in result.Assets)
        {
            string destination = Path.Combine(root, asset.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            AtomicFile.Write(destination, asset.Value);
            document.RegisterGeneratedAsset(asset.Key, root);
        }
        var previewDefinition = new MapDefinition { BaseDirectory = root };
        previewDefinition.Geometry.AddRange(result.Meshes); previewDefinition.Materials.AddRange(result.Materials);
        var preview = new MapViewport(new MapDocument(new MapProject(previewDefinition))) { Height = 280, MinWidth = 480, ReadOnlyPreview = true };
        preview.DetachedFromVisualTree += (_, _) => preview.DetachDocument();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text(sourceId == null ? "IMPORT PREVIEW" : "REIMPORT PREVIEW"));
        var oldMeshes = document.Project.Definition.Geometry.OfType<MapMesh>()
            .Where(m => previous?.Objects.Any(o => o.Id == m.Id) == true).ToArray();
        panel.Children.Add(Text($"Objects {oldMeshes.Length} → {result.Meshes.Count} · Vertices {oldMeshes.Sum(m => m.Vertices.Count)} → {result.Meshes.Sum(m => m.Vertices.Count)} · Faces {oldMeshes.Sum(m => m.Faces.Count)} → {result.Meshes.Sum(m => m.Faces.Count)}"));
        panel.Children.Add(Text($"{result.Materials.Count} materials · {result.Assets.Count} baked textures · {result.Meshes.Where(m => m.Solid).Sum(m => m.Faces.Count)} collision triangles"));
        var diff=ModelReimport.Preview(document.Project.Definition,previous,result);
        panel.Children.Add(Text($"Materials {previous?.MaterialMappings.Count??0} → {result.Materials.Count}"));
        foreach(string name in diff.Added)panel.Children.Add(Text("+ "+name));
        foreach(string name in diff.Changed)panel.Children.Add(Text("~ "+name));
        foreach(string name in diff.Removed)panel.Children.Add(Text("− "+name));
        panel.Children.Add(Text($"Preserved edits: {diff.Transforms} transforms · {diff.MaterialOverrides} material overrides · {diff.PaintedFaces} painted faces · {diff.UvOverrides} UV overrides"));
        var collisionPreview = new CheckBox {Content="Show collision preview"};
        collisionPreview.IsCheckedChanged += (_,_) => {preview.Collision=collisionPreview.IsChecked==true;preview.InvalidateVisual();};
        panel.Children.Add(collisionPreview);
        panel.Children.Add(preview); preview.FrameAll();
        panel.Children.Add(Text("Orbit and zoom to inspect. Compatible transforms, face paint and UV edits are preserved on reimport."));
        foreach (string warning in result.Warnings.Take(20)) panel.Children.Add(Text(warning));
        AddButton(panel, sourceId == null ? "Import" : "Apply reimport", () =>
        {
            try
            {
                if (_document != document || document.CurrentStateId != state) throw new IOException("The project changed. Preview the model again before applying.");
                document.Edit(sourceId == null ? "Import 3D model" : "Reimport 3D model", definition =>
                {
                    definition.BaseDirectory = root;
                    ModelReimport.Apply(definition, result, path, hash, settings, sourceId);
                }, MapChangeDomain.Geometry | MapChangeDomain.Material);
                foreach(string dependency in result.Dependencies)_changedSources.Remove(dependency);
                Dismiss(); _viewport?.FrameAll(); _status.Text = "Model applied. Undo restores the previous import.";
            }
            catch (Exception ex) { Failure(ex); }
        });
        AddButton(panel, "Cancel", Dismiss); Modal(panel);
    });
}
