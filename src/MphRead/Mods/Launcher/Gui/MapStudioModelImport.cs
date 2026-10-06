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
        var sources = new StackPanel { Spacing = 8 };
        foreach (var source in _document.Project.Definition.ModelSources)
        {
            sources.Children.Add(Text(Path.GetFileName(source.Source) + $" · {source.Objects.Count} objects"));
            sources.Children.Add(Text(source.Source));
            AddButton(sources, "Reimport / check external changes", () => ModelImportOptions(source.Source, source));
            AddButton(sources, "Locate source…", () => Browse("Locate source model", false, path => ModelImportOptions(path, source), ".obj", ".gltf", ".glb"));
            AddButton(sources, "Select generated objects", () =>
            {
                _document.Selection.Clear();
                foreach (var item in source.Objects) _document.Selection.Add(item.Id);
                _document.SelectionChanged(); _viewport?.FrameSelection(); Dismiss();
            });
            AddButton(sources, "Detach source (keep geometry)", () =>
            {
                _document.Edit("Detach model source", d => d.ModelSources.RemoveAll(s => s.Id == source.Id), MapChangeDomain.Geometry);
                Dismiss();
            });
        }
        if (_document.Project.Definition.ModelSources.Count == 0) sources.Children.Add(Text("Import a 3D model to track its external source."));
        panel.Children.Add(ModelImportScroll(sources, 420));
        AddButton(panel, "Close", Dismiss); Modal(panel);
    }

    private static ScrollViewer ModelImportScroll(Control content, double maxHeight) => new()
    {
        Content = content,
        MaxHeight = maxHeight,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

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
        string previewRoot=Path.Combine(_services.StagingDirectory,"ProjectPrime-model-preview-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewRoot);
        try
        {
            foreach(var asset in result.Assets)
            {
                MapPackageReader.CanonicalName(asset.Key);
                string destination=Path.GetFullPath(Path.Combine(previewRoot,asset.Key));
                string prefix=Path.GetFullPath(previewRoot)+Path.DirectorySeparatorChar;
                if(!destination.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model preview asset escapes its staging folder.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                AtomicFile.Write(destination,asset.Value);
            }
        }
        catch
        {
            try{Directory.Delete(previewRoot,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
        var previewDefinition = new MapDefinition { BaseDirectory = previewRoot };
        previewDefinition.Geometry.AddRange(result.Meshes); previewDefinition.Materials.AddRange(result.Materials);
        MapViewport preview;
        try
        {
            preview = new MapViewport(new MapDocument(new MapProject(previewDefinition)))
                { Height = 280, MinWidth = 480, ReadOnlyPreview = true };
        }
        catch
        {
            try{Directory.Delete(previewRoot,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
        bool previewCleaned=false;
        void CleanupPreview()
        {
            if(previewCleaned)return;previewCleaned=true;preview.DetachDocument();
            try{if(Directory.Exists(previewRoot))Directory.Delete(previewRoot,true);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        preview.DetachedFromVisualTree += (_, _) => CleanupPreview();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text(sourceId == null ? "IMPORT PREVIEW" : "REIMPORT PREVIEW"));
        var oldMeshes = document.Project.Definition.Geometry.OfType<MapMesh>()
            .Where(m => previous?.Objects.Any(o => o.Id == m.Id) == true).ToArray();
        panel.Children.Add(Text($"Objects {oldMeshes.Length} → {result.Meshes.Count} · Vertices {oldMeshes.Sum(m => m.Vertices.Count)} → {result.Meshes.Sum(m => m.Vertices.Count)} · Faces {oldMeshes.Sum(m => m.Faces.Count)} → {result.Meshes.Sum(m => m.Faces.Count)}"));
        panel.Children.Add(Text($"{result.Materials.Count} materials · {result.Assets.Count} baked textures · {result.Meshes.Where(m => m.Solid).Sum(m => m.Faces.Count)} collision triangles"));
        var diff=ModelReimport.Preview(document.Project.Definition,previous,result);
        panel.Children.Add(Text($"Materials {previous?.MaterialMappings.Count??0} → {result.Materials.Count}"));
        var changes = new StackPanel { Spacing = 4 };
        foreach(string name in diff.Added)changes.Children.Add(Text("+ "+name));
        foreach(string name in diff.Changed)changes.Children.Add(Text("~ "+name));
        foreach(string name in diff.Removed)changes.Children.Add(Text("− "+name));
        if (changes.Children.Count > 0) panel.Children.Add(ModelImportScroll(changes, 180));
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
                string? root=document.Project.Definition.BaseDirectory;
                if(String.IsNullOrWhiteSpace(root)&&document.FilePath!=null)
                    root=Path.GetDirectoryName(Path.GetFullPath(document.FilePath));
                if(result.Assets.Count>0&&String.IsNullOrWhiteSpace(root))
                    throw new IOException("Save this map project before applying a textured 3D model.");
                var created=new System.Collections.Generic.List<string>();
                try
                {
                    if(root!=null)
                    {
                        root=Path.GetFullPath(root);
                        string prefix=root+Path.DirectorySeparatorChar;
                        foreach(var asset in result.Assets)
                        {
                            MapPackageReader.CanonicalName(asset.Key);
                            string destination=Path.GetFullPath(Path.Combine(root,asset.Key));
                            if(!destination.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Model asset escapes the map project.");
                            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                            bool existed=File.Exists(destination);
                            AtomicFile.Write(destination,asset.Value);
                            if(!existed)created.Add(destination);
                        }
                    }
                    string? projectRoot=root;
                    document.Edit(sourceId == null ? "Import 3D model" : "Reimport 3D model", definition =>
                    {
                        if(projectRoot!=null)definition.BaseDirectory=projectRoot;
                        ModelReimport.Apply(definition, result, path, hash, settings, sourceId);
                    }, MapChangeDomain.Geometry | MapChangeDomain.Material);
                    if(root!=null)foreach(var asset in result.Assets)document.RegisterGeneratedAsset(asset.Key,root);
                }
                catch
                {
                    foreach(string createdPath in created)
                        try{if(File.Exists(createdPath))File.Delete(createdPath);}catch(IOException){}catch(UnauthorizedAccessException){}
                    throw;
                }
                foreach(string dependency in result.Dependencies)_changedSources.Remove(dependency);
                CleanupPreview();Dismiss(); _viewport?.FrameAll(); _status.Text = "Model applied. Undo restores the previous import.";
            }
            catch (Exception ex) { Failure(ex); }
        });
        AddButton(panel, "Cancel", () => { CleanupPreview(); Dismiss(); }); Modal(panel);
    });
}
