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
                _document.SelectionChanged(); RefreshHierarchy(); _viewport?.FrameSelection(); Dismiss();
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

    private void AnalyzeModel(string path,ModelImportSettings settings,Guid? sourceId)
    {
        try
        {
            path=Path.GetFullPath(path);var context=CaptureAssetContext();var snapshot=context.Document.CaptureBuildSnapshot();
            _=Job("Analyzing model",async token=>
            {
                var captured=await Task.Run(()=>ImportFrozenModel(path,settings,token),token);
                GuardAssetContext(context,token);var result=captured.Model;
                var previous=context.Document.Project.Definition.ModelSources.FirstOrDefault(source=>source.Id==sourceId);
                var comparison=await Task.Run(()=>
                {
                    var definition=snapshot.CreateDefinition();var old=definition.ModelSources.FirstOrDefault(source=>source.Id==sourceId);
                    bool unchanged=old!=null && old.NormalizedHash==ModelReimport.NormalizedHash(result) && old.Settings==settings && old.Source==path && old.SourceHash==captured.Hash;
                    return(Unchanged:unchanged,Diff:ModelReimport.Preview(definition,old,result));
                },token);
                GuardAssetContext(context,token);
                if(comparison.Unchanged){_status.Text="Model and referenced materials are unchanged.";return;}
                string previewRoot=Path.Combine(_services.StagingDirectory,"model-preview-"+Guid.NewGuid().ToString("N"));
                try
                {
                    await Task.Run(()=>
                    {
                        foreach(var asset in result.Assets)
                        {
                            token.ThrowIfCancellationRequested();MapPackageReader.CanonicalName(asset.Key);
                            AtomicFile.Write(Path.Combine(previewRoot,asset.Key),asset.Value);
                        }
                    },token);
                    GuardAssetContext(context,token);
                    var previewDefinition=new MapDefinition {BaseDirectory=previewRoot};
                    previewDefinition.Geometry.AddRange(result.Meshes);previewDefinition.Materials.AddRange(result.Materials);
                    var preview=new MapViewport(new MapDocument(new MapProject(previewDefinition))) {Height=280,MinWidth=480,ReadOnlyPreview=true};
                    bool cleaned=false,applying=false,accepted=false;
                    void Cleanup()
                    {
                        if(cleaned)return;cleaned=true;if(applying&&!accepted)_work?.Cancel();preview.DetachDocument();
                        try{if(Directory.Exists(previewRoot))Directory.Delete(previewRoot,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                    preview.DetachedFromVisualTree+=(_,_)=>Cleanup();
                    var panel=new StackPanel {Spacing=8};panel.Children.Add(Text(sourceId==null?"IMPORT PREVIEW":"REIMPORT PREVIEW"));
                    var oldMeshes=context.Document.Project.Definition.Geometry.OfType<MapMesh>().Where(mesh=>previous?.Objects.Any(item=>item.Id==mesh.Id)==true).ToArray();
                    panel.Children.Add(Text($"Objects {oldMeshes.Length} → {result.Meshes.Count} · Vertices {oldMeshes.Sum(mesh=>mesh.Vertices.Count)} → {result.Meshes.Sum(mesh=>mesh.Vertices.Count)} · Faces {oldMeshes.Sum(mesh=>mesh.Faces.Count)} → {result.Meshes.Sum(mesh=>mesh.Faces.Count)}"));
                    panel.Children.Add(Text($"{result.Materials.Count} materials · {result.Assets.Count} textures · {result.Meshes.Where(mesh=>mesh.Solid).Sum(mesh=>mesh.Faces.Count)} collision faces"));
                    var changes=new StackPanel {Spacing=4};var diff=comparison.Diff;
                    foreach(string name in diff.Added)changes.Children.Add(Text("+ "+name));
                    foreach(string name in diff.Changed)changes.Children.Add(Text("~ "+name));
                    foreach(string name in diff.Removed)changes.Children.Add(Text("− "+name));
                    if(changes.Children.Count>0)panel.Children.Add(ModelImportScroll(changes,180));
                    panel.Children.Add(Text($"Preserved edits: {diff.Transforms} transforms · {diff.MaterialOverrides} material overrides · {diff.PaintedFaces} painted faces · {diff.UvOverrides} UV overrides"));
                    var collisionPreview=new CheckBox {Content="Show collision preview"};
                    collisionPreview.IsCheckedChanged+=(_,_)=>{preview.Collision=collisionPreview.IsChecked==true;preview.InvalidateVisual();};
                    panel.Children.Add(collisionPreview);panel.Children.Add(preview);preview.FrameAll();
                    panel.Children.Add(Text("Orbit and zoom to inspect. Compatible transforms, face paint and UV edits are preserved on reimport."));
                    foreach(string warning in result.Warnings.Take(20))panel.Children.Add(Text(warning));
                    AddButton(panel,sourceId==null?"Import":"Apply reimport",()=>
                    {
                        if(applying)return;applying=true;
                        _=RunAssetUiAsync(()=>Job("Applying model",async applyToken=>
                        {
                            try
                            {
                                GuardAssetContext(context,applyToken);
                                await ApplyCapturedModelAsync(context,snapshot,captured,path,settings,sourceId,applyToken);
                                accepted=true;Cleanup();Dismiss();_viewport?.FrameAll();
                            }
                            finally{applying=false;}
                        },propagateErrors:true));
                    });
                    AddButton(panel,"Cancel",()=>{Cleanup();Dismiss();});Modal(panel);
                }
                catch
                {
                    try{if(Directory.Exists(previewRoot))Directory.Delete(previewRoot,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                    throw;
                }
            });
        }
        catch(Exception error){Failure(error);}
    }
}
