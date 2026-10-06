using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class MapStudioScreen
    {
        private string StoreAsset(string kind,string extension,byte[] bytes)
        {
            if(_document==null)throw new InvalidOperationException("Open a project first.");
            if(bytes.LongLength>MapPackageReader.MaxEntryBytes)throw new IOException("Asset exceeds the 256 MiB package entry limit.");
            string root=_document.Project.Definition.BaseDirectory??_services.MapLibraryDirectory;
            string relative=kind+"/"+Guid.NewGuid().ToString("N")+extension;
            string canonicalRoot=MapPublicationLease.ResolveRuntimeDirectoryAliases(root);
            string destination=MapAssetDestination.Resolve(root,relative,canonicalRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            AtomicFile.Write(MapAssetDestination.Resolve(root,relative,canonicalRoot),bytes);
            _document.RegisterGeneratedAsset(relative,root);
            _document.Edit(kind=="preview"?"Replace preview":"Add "+kind,d=>{d.BaseDirectory=root;if(kind=="preview")d.Assets.RemoveAll(a=>a.Kind=="preview");d.Assets.Add(new(){Path=relative,Kind=kind=="audio"?"audio":kind=="preview"?"preview":"texture"});});
            return relative;
        }
        private void AssetInspector(StackPanel? target = null)
        {
            target ??= _inspector;
            target.Children.Clear();if(_document==null)return;target.Children.Add(Text("ASSETS & MUSIC"));
            target.Children.Add(Text("Project root · "+(_document.Project.Definition.BaseDirectory ?? _services.MapLibraryDirectory)));
            var search=new TextBox {Text=_assetSearch,PlaceholderText="Search names, paths and tags"};
            var unused=new CheckBox {Content="Only unused assets",IsChecked=_assetUnused};
            var rows=new StackPanel {Spacing=6};
            target.Children.Add(search); target.Children.Add(unused); target.Children.Add(rows);
            void RefreshRows() { _assetSearch=search.Text??""; _assetUnused=unused.IsChecked==true; _assetPage=0; rows.Children.Clear(); AppendAssetRows(rows,_assetSearch,_assetUnused); }
            search.TextChanged+=(_,_)=>RefreshRows(); unused.IsCheckedChanged+=(_,_)=>RefreshRows(); RefreshRows();
            AddButton(target,"Clean generated orphans",()=>{try{_status.Text=$"Removed {_document.CleanupGeneratedAssets()} generated files. Undo and recovery assets retained.";}catch(Exception ex){Failure(ex);}});
            AddButton(target,"Import texture",()=>Browse("Choose a texture image",false,path=>_=RunAssetUiAsync(()=>ImportAssetAsync(path)),".png",".jpg",".jpeg",".tga",".ktx2",".tex"));
            AddButton(target,"Import model",ImportModel);
            AddButton(target,"Choose custom music",()=>Browse("Choose map music",false,path=>_=RunAssetUiAsync(()=>ImportAssetAsync(path)),".wav",".ogg",".mp3"));
            var gameMusic=new ComboBox {ItemsSource=Enum.GetNames<MusicId>(),SelectedItem=_document.Project.Definition.Audio?.GameMusic};target.Children.Add(Text("Existing game music"));target.Children.Add(gameMusic);
            AddButton(target,"Use game music",()=>{if(gameMusic.SelectedItem is string music)_document.Edit("Game music",d=>d.Audio=new(){GameMusic=music});});
            var volume=new TextBox {Text=(_document.Project.Definition.Audio?.Volume??.8f).ToString(CultureInfo.InvariantCulture)};
            var loop=new CheckBox {Content="Loop music",IsChecked=_document.Project.Definition.Audio?.Loop??true};target.Children.Add(Text("Music volume (0–1)"));target.Children.Add(volume);target.Children.Add(loop);
            AddButton(target,"Apply audio",()=>{try{_document.Edit("Audio settings",d=>{d.Audio??=new();d.Audio.Volume=Number(volume.Text??"");d.Audio.Loop=loop.IsChecked==true;});}catch(Exception ex){Failure(ex);}});
            AddButton(target,"Use default audio",()=>_document.Edit("Default audio",d=>d.Audio=null));
        }
        internal Control CreateAssetBrowser()
        {
            var rows=new StackPanel {Spacing=6,Margin=new Thickness(10)};
            var browser=new MapAssetBrowserPanel(new ScrollViewer {Content=rows});
            void RefreshBrowser() { if(!browser.IsDisposed) AssetInspector(rows); }
            DocumentChanged+=RefreshBrowser;
            browser.Released+=()=>DocumentChanged-=RefreshBrowser;
            RefreshBrowser(); return browser;
        }
        internal void OpenAssetBrowser()
        {
            Control browser=CreateAssetBrowser();
            if(!_services.OpenAssetBrowser(browser)) { ((IDisposable)browser).Dispose(); ShowInspectorPage("Assets & music"); }
        }
        private string _assetSearch="";
        private bool _assetUnused;
        private int _assetPage;
        private void AppendAssetRows(StackPanel rows,string query,bool unused)
        {
            if(_document==null)return;
            var matches=_document.Project.Definition.Assets.Where(asset=>MapAssetCatalog.Matches(asset,query))
                .Where(asset=>!unused || MapAssetCatalog.Usages(_document.Project.Definition,asset.Path).Count==0).ToArray();
            const int pageSize=32;_assetPage=Math.Clamp(_assetPage,0,Math.Max(0,(matches.Length-1)/pageSize));
            rows.Children.Add(Text($"{matches.Length} matching file assets · page {_assetPage+1}/{Math.Max(1,(matches.Length+pageSize-1)/pageSize)}"));
            void Page(int page){_assetPage=page;rows.Children.Clear();AppendAssetRows(rows,query,unused);}
            if(_assetPage>0)AddButton(rows,"Previous assets",()=>Page(_assetPage-1));
            if((_assetPage+1)*pageSize<matches.Length)AddButton(rows,"Next assets",()=>Page(_assetPage+1));
            foreach(var asset in matches.Skip(_assetPage*pageSize).Take(pageSize))
            {
                var entry = asset;
                string root = _document.Project.Definition.BaseDirectory ?? _services.MapLibraryDirectory;
                string file = Path.GetFullPath(Path.Combine(root, entry.Path));
                var usages = MapAssetCatalog.Usages(_document.Project.Definition,entry.Path);
                int uses=usages.Count;
                if(unused && uses!=0) continue;
                string size = File.Exists(file) ? $"{new FileInfo(file).Length / 1024d:0.0} KiB" : "MISSING";
                rows.Children.Add(Text($"{entry.Kind} · {entry.Name ?? Path.GetFileName(entry.Path)} · {size} · {uses} uses"));
                if(entry.SourcePath is { } sourcePath)
                {
                    rows.Children.Add(Text("Source: "+sourcePath));
                    if(Path.GetExtension(sourcePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".tga" or ".ktx2")
                        AddButton(rows,"Reload source texture",()=>_=ReplaceAsset(entry.Path,sourcePath));
                }
                if(entry.Kind is "texture" or "preview")AddTextureThumbnail(rows,entry);
                if(entry.Kind=="audio")AddAudioThumbnail(rows,entry);
                AddAssetDrag(rows,entry.Name??Path.GetFileName(entry.Path),new(_document.Project.Definition.MapId,
                    entry.Kind=="audio"?MapAssetDragKind.Audio:MapAssetDragKind.Texture,entry.Path));
                AddButton(rows,"Export asset…",()=>Browse("Export asset",true,path=>
                {
                    try {AtomicFile.Write(path,MapAssets.Read(_document.Project.Definition,entry.Path));_status.Text="Asset exported.";}catch(Exception ex){Failure(ex);}
                },Path.GetExtension(entry.Path)));
                AddButton(rows, "Find usages", () =>
                {
                    _status.Text = usages.Count==0 ? "No references use this asset." : string.Join(" · ",usages.Select(usage=>usage.Kind+": "+usage.Name));
                });
                var tags=new TextBox {Text=string.Join(", ",entry.Tags ?? new()),PlaceholderText="Tags separated by commas"};
                rows.Children.Add(tags);
                AddButton(rows,"Apply tags",()=> { try { var values=MapAssetCatalog.ParseTags(tags.Text??""); _document.Edit("Asset tags",d=>d.Assets.Find(a=>a.Path==entry.Path)!.Tags=values,MapChangeDomain.Metadata); } catch(Exception ex) {Failure(ex);} });
                var logicalName = new TextBox { Text = entry.Name ?? Path.GetFileNameWithoutExtension(entry.Path) };
                rows.Children.Add(logicalName);
                AddButton(rows, "Rename asset", () => { _document.Edit("Rename asset", d =>
                    { var asset = d.Assets.Find(a => a.Path == entry.Path); if (asset != null) asset.Name = logicalName.Text?.Trim(); }); AssetInspector(); });
#if !ANDROID
                AddButton(rows, "Reveal folder", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Path.GetDirectoryName(_document.Project.Definition.BundlePath ?? file)!) { UseShellExecute = true }); } catch (Exception ex) { Failure(ex); } });
#endif
                AddButton(rows, "Replace asset", () => Browse("Replace " + entry.Kind, false,
                    path => _ = ReplaceAsset(entry.Path, path)));
                if (uses == 0) AddButton(rows, "Remove unused reference", () => { _document.Edit("Remove unused asset", d => d.Assets.RemoveAll(a => a.Path == entry.Path)); AssetInspector(); });
            }
            AppendAssetSources(rows,query,unused);
        }
        private void AppendAssetSources(StackPanel rows,string query,bool unused)
        {
            if(_document==null)return;
            bool Match(string value)=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(term=>value.Contains(term,StringComparison.OrdinalIgnoreCase));
            var definition=_document.Project.Definition;MapBuildSnapshot? geometrySnapshot=null;
            string MaterialTerms(MapMaterial material)
            {
                string?[] references={material.Texture,material.Albedo,material.Normal,material.SpecularRoughness,material.Emissive};
                return string.Join(" ",definition.Assets.Where(asset=>references.Contains(asset.Path)).SelectMany(asset=>asset.Tags??new()));
            }
            foreach(var material in definition.Materials.Where(material=>Match("material "+material.Name+" "+MaterialTerms(material))).Take(32))
            {
                int index=definition.Materials.IndexOf(material);
                int uses=definition.Geometry.Count(geometry=>geometry.Material==index || geometry is MapMesh mesh&&mesh.FaceMaterials.Contains(index))+definition.Brushes.Count(brush=>brush.Material==index);
                if(unused&&uses!=0)continue;
                rows.Children.Add(Text("Material · "+material.Name+" · "+uses+" objects"));
                string? preview=material.Albedo??material.Texture;
                if(preview is not null && definition.Assets.FirstOrDefault(asset=>asset.Path==preview) is {} asset)AddTextureThumbnail(rows,asset);
                AddAssetDrag(rows,material.Name,new(definition.MapId,MapAssetDragKind.Material,material.Id.ToString()));
                AddButton(rows,"Open material editor",()=>ShowInspectorPage("Materials"));
            }
            foreach(var source in definition.ModelSources.Where(source=>Match("model "+source.Source)).Take(32))
            {
                var ids=source.Objects.Select(item=>item.Id).ToHashSet();int uses=definition.Geometry.Count(geometry=>ids.Contains(geometry.Id));
                if(unused&&uses!=0)continue;
                rows.Children.Add(Text("Model · "+Path.GetFileName(source.Source)+" · "+uses+" objects"));
                geometrySnapshot??=_document.CaptureBuildSnapshot();AddGeometryThumbnail(rows,geometrySnapshot,ids);
                AddAssetDrag(rows,Path.GetFileName(source.Source),new(definition.MapId,MapAssetDragKind.Model,source.Source));
                AddButton(rows,"Reimport model",()=>ModelImportOptions(source.Source,source));
                AddButton(rows,"Locate model source",()=>Browse("Locate source model",false,path=>ModelImportOptions(path,source),".obj",".gltf",".glb"));
                AddButton(rows,"Select model objects",()=> { _document.Selection.Clear(); foreach(var model in source.Objects)_document.Selection.Add(model.Id); _document.SelectionChanged();RefreshHierarchy();_viewport?.FrameSelection(); });
#if !ANDROID
                AddButton(rows,"Reveal model source",()=>RevealAssetFolder(source.Source));
#endif
            }
            if(!unused && definition.Import is {} import && Match("source import "+import.Source))
            { rows.Children.Add(Text("Source import · "+import.Source)); AddButton(rows,"Reimport source",()=>_=PickReimportSource()); }
            string prefabRoot=Path.Combine(_services.MapLibraryDirectory,".prefabs");
            if(Directory.Exists(prefabRoot))foreach(string path in Directory.EnumerateFiles(prefabRoot,"*.json").OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).Take(32))
            {
                if(!Match("prefab "+Path.GetFileName(path)))continue;
                int uses=definition.PrefabInstances.Count(instance=>string.Equals(instance.SourcePath,path,StringComparison.OrdinalIgnoreCase));
                if(unused&&uses!=0)continue;
                rows.Children.Add(Text("Prefab · "+Path.GetFileNameWithoutExtension(path)+" · "+uses+" instances"));AddPrefabThumbnail(rows,path);
                AddAssetDrag(rows,Path.GetFileNameWithoutExtension(path),new(definition.MapId,MapAssetDragKind.Prefab,path));
                AddButton(rows,"Insert prefab",()=>_=InsertPrefabAsync(path));
#if !ANDROID
                AddButton(rows,"Reveal prefab",()=>RevealAssetFolder(path));
#endif
            }
            if(Match("prefab library"))AddButton(rows,"Browse prefab library",InsertPrefab);
        }
#if !ANDROID
        private void RevealAssetFolder(string path)
        {try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(Path.GetFullPath(path))!) {UseShellExecute=true});}catch(Exception error){Failure(error);}}
#endif
        private Task ReplaceAsset(string previous,string source)=>RunAssetUiAsync(()=>ReplaceAssetAsync(previous,source));
        private async Task RunAssetUiAsync(Func<Task> action)
        {
            try { await action(); }
            catch(OperationCanceledException) { }
            catch(Exception error) { Failure(error); }
        }
    }
}
