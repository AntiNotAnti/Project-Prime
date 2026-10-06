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
            AtomicFile.Write(Path.Combine(root,relative),bytes);
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
            void RefreshRows() { _assetSearch=search.Text??""; _assetUnused=unused.IsChecked==true; rows.Children.Clear(); AppendAssetRows(rows,_assetSearch,_assetUnused); }
            search.TextChanged+=(_,_)=>RefreshRows(); unused.IsCheckedChanged+=(_,_)=>RefreshRows(); RefreshRows();
            AddButton(target,"Clean generated orphans",()=>{try{_status.Text=$"Removed {_document.CleanupGeneratedAssets()} generated files. Undo and recovery assets retained.";}catch(Exception ex){Failure(ex);}});
            AddButton(target,"Import texture",()=>Browse("Choose a texture image",false,path=>_=Job("Baking texture",async token=>
            {
                try
                {
                    byte[] source=await Task.Run(()=>File.ReadAllBytes(path),token);
                    if(source.LongLength>MapPackageReader.MaxEntryBytes)throw new IOException("Texture image exceeds the 256 MiB asset limit.");
                    _=ModernTextureAsset.ProbeDimensions(source);
                    string extension=ModernTextureAsset.PortableEncodedExtension(source)
                        ?? throw new InvalidDataException("HD map textures must be PNG, JPEG, TGA or KTX2.");
                    byte[] baked=await Task.Run(()=>MapTextureBake.BakeImage(source, token),token);
                    GuardJob(token);
                    string fallback=StoreAsset("textures",".tex",baked);
                    string albedo=StoreAsset("textures",extension,source);
                    _document.Edit("Add custom HD material",d=>d.Materials.Add(new(){Id=Guid.NewGuid(),Name=Path.GetFileNameWithoutExtension(path),Texture=fallback,Albedo=albedo,TexScale=16}));
                    MaterialInspector();
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){GuardJob(token);Failure(ex);}
            }),".png",".jpg",".jpeg",".tga",".ktx2"));
            AddButton(target,"Choose custom music",()=>Browse("Choose map music",false,path=>
            {
                try
                {
                    if(new FileInfo(path).Length>32*1024*1024)throw new IOException("Music exceeds 32 MiB.");
                    string asset=StoreAsset("audio",Path.GetExtension(path).ToLowerInvariant(),File.ReadAllBytes(path));
                    _document.Edit("Map music",d=>d.Audio=new(){Music=asset});AssetInspector(target);
                }
                catch(Exception ex){Failure(ex);}
            },".wav",".ogg",".mp3"));
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
        private void AppendAssetRows(StackPanel rows,string query,bool unused)
        {
            if(_document==null)return;
            foreach(var asset in _document.Project.Definition.Assets.Where(asset=>MapAssetCatalog.Matches(asset,query)))
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
                if(entry.Kind=="texture")
                {
                    try
                    {
                        var material = new MapMaterial {Texture=entry.Path}; string key = PreviewCacheKey(_document.Project.Definition,material);
                        if(!_materialPreviewCache.TryGetValue(key,out var preview))
                        { preview=MapMaterialPreview.Create(_document.Project.Definition,material); _materialPreviewCache[key]=preview; }
                        rows.Children.Add(new Image {Source=preview.Bitmap,Width=72,Height=72,HorizontalAlignment=HorizontalAlignment.Left});
                        rows.Children.Add(Text(preview.Details));
                    }
                    catch(Exception ex) when(ex is IOException or InvalidDataException or ProgramException or ArgumentException) { rows.Children.Add(Text("Preview unavailable: "+ex.Message)); }
                }
                AddButton(rows,"Export asset…",()=>Browse("Export asset",true,path=>
                {
                    try {AtomicFile.Write(path,MapAssets.Read(_document.Project.Definition,entry.Path));_status.Text="Asset exported.";}catch(Exception ex){Failure(ex);}
                },Path.GetExtension(entry.Path)));
                AddButton(rows, "Find usages", () =>
                {
                    _status.Text = usages.Count==0 ? "No references use this asset." : string.Join(" · ",usages.Select(usage=>usage.Kind+": "+usage.Name));
                });
                var tags=new TextBox {Text=string.Join(", ",entry.Tags),PlaceholderText="Tags separated by commas"};
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
            if(!unused) AppendAssetSources(rows,query);
        }
        private void AppendAssetSources(StackPanel rows,string query)
        {
            if(_document==null)return;
            bool Match(string value)=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(term=>value.Contains(term,StringComparison.OrdinalIgnoreCase));
            var definition=_document.Project.Definition;
            foreach(var material in definition.Materials.Where(material=>Match("material "+material.Name)))
            {
                int uses=definition.Geometry.Count(geometry=>geometry.Material==definition.Materials.IndexOf(material));
                rows.Children.Add(Text("Material · "+material.Name+" · "+uses+" objects"));
                AddButton(rows,"Open material editor",()=>ShowInspectorPage("Materials"));
            }
            foreach(var source in definition.ModelSources.Where(source=>Match("model "+source.Source)))
            {
                rows.Children.Add(Text("Model · "+Path.GetFileName(source.Source)+" · "+source.Objects.Count+" objects"));
                AddButton(rows,"Reimport model",()=>ModelImportOptions(source.Source,source));
                AddButton(rows,"Locate model source",()=>Browse("Locate source model",false,path=>ModelImportOptions(path,source),".obj",".gltf",".glb"));
                AddButton(rows,"Select model objects",()=> { _document.Selection.Clear(); foreach(var model in source.Objects)_document.Selection.Add(model.Id); _document.SelectionChanged(); _viewport?.FrameSelection(); });
            }
            if(definition.Import is {} import && Match("source import "+import.Source))
            { rows.Children.Add(Text("Source import · "+import.Source)); AddButton(rows,"Reimport source",()=>_=PickReimportSource()); }
            if(Match("prefab library")) AddButton(rows,"Browse prefab library",InsertPrefab);
        }
        private Task ReplaceAsset(string previous, string source) => Job("Replacing asset", async token =>
        {
            if (_document == null) return;
            var document = _document;
            var original = document.Project.Definition.Assets.Find(asset => asset.Path == previous);
            if (original == null) return;
            string root = document.Project.Definition.BaseDirectory ?? _services.MapLibraryDirectory;
            string kind = original.Kind;
            var replacement = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (new FileInfo(source).Length > 32 * 1024 * 1024) throw new IOException("Assets must be no larger than 32 MiB.");
                byte[] bytes = File.ReadAllBytes(source);
                string extension = Path.GetExtension(source).ToLowerInvariant();
                if (kind == "texture")
                {
                    if(Path.GetExtension(previous).Equals(".tex",StringComparison.OrdinalIgnoreCase))
                    { bytes=extension==".tex" ? bytes : MapTextureBake.BakeImage(bytes,token); extension=".tex"; var pack=MapTexturePack.Load(bytes,source); if(pack.Entries.Count!=1) throw new IOException("Choose a single baked texture."); }
                    else { _=ModernTextureAsset.ProbeDimensions(bytes); extension=ModernTextureAsset.PortableEncodedExtension(bytes) ?? throw new IOException("Choose a portable PNG, JPEG, TGA or KTX2 image."); }
                }
                else if (kind == "preview" && extension != ".png") throw new IOException("Choose a PNG preview.");
                else if (kind == "audio" && extension is not (".wav" or ".ogg" or ".mp3")) throw new IOException("Choose WAV, OGG or MP3 audio.");
                string relative = kind + "/" + Guid.NewGuid().ToString("N") + extension;
                token.ThrowIfCancellationRequested();
                AtomicFile.Write(Path.Combine(root, relative), bytes);
                return relative;
            }, token);
            // Record ownership even if cancellation arrives just after publication, so
            // later explicit cleanup can reclaim the generated orphan safely.
            document.RegisterGeneratedAsset(replacement, root);
            GuardJob(token);
            document.Edit("Replace asset", d =>
            {
                var asset = d.Assets.Find(a => a.Path == previous); if (asset == null) return;
                asset.Path = replacement; asset.SourcePath = source; d.BaseDirectory = root;
                MapAssetCatalog.ReplaceReferences(d,previous,replacement);
            });
            AssetInspector(); _status.Text = "Asset replaced. Previous version remains available to Undo.";
        });
    }
}
