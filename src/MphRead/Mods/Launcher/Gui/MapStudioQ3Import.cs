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
        private void OnFilesDropped(IReadOnlyList<string> files)
        {
            if(_work!=null||_detached||_poppedOut)return;
            string? path=files.FirstOrDefault(file=>File.Exists(file)&&
                Path.GetExtension(file).ToLowerInvariant() is ".pk3" or ".bsp" or ".json" or ".ppmap");
            if(path==null){_status.Text="Drop a .pk3, .bsp, .json or .ppmap file into Map Studio.";return;}
            string extension=Path.GetExtension(path).ToLowerInvariant();
            if(extension is ".pk3" or ".bsp")ShowImportWizard(path);
            else Open(path);
        }

        private void Import()=>_ = PickImportSource();
        private async Task PickImportSource()
        {
#if !ANDROID
            if(_services.NativeFileDialogs)
            {
                string? picked=await _services.PickFileAsync("Choose a Quake 3 PK3",false,new[]{".pk3",".bsp"},CancellationToken.None);
                if(picked!=null){ShowImportWizard(picked);return;}
            }
#endif
            Browse("Choose a Quake 3 source",false,ShowImportWizard,".pk3",".bsp");
        }
        private void ShowImportWizard(string source)
        {
            var view=new StackPanel {Spacing=6,MinWidth=680};view.Children.Add(Text("IMPORT QUAKE 3 · "+Path.GetFileName(source)));
            var maps=new ComboBox();
            IReadOnlyList<string> mapNames;
            try{mapNames=Q3Bsp.ListMaps(source);maps.ItemsSource=mapNames;maps.SelectedIndex=0;}
            catch(Exception ex){Failure(ex);return;}
            view.Children.Add(Text("Level in archive"));view.Children.Add(maps);
            var name=new TextBox {Text=mapNames.FirstOrDefault()??Path.GetFileNameWithoutExtension(source)};
            view.Children.Add(Text("Runtime name"));view.Children.Add(name);
            var scaleMode=new ComboBox{ItemsSource=new[]{"Auto","Faithful (35 Q3 units)","Custom"},SelectedIndex=0};
            var customScale=new TextBox{Text="35",IsVisible=false};view.Children.Add(Text("Scale"));view.Children.Add(scaleMode);view.Children.Add(customScale);
            scaleMode.SelectionChanged+=(_,_)=>customScale.IsVisible=scaleMode.SelectedIndex==2;
            var textureSize=new ComboBox{ItemsSource=new[]{"32","64","128"},SelectedItem="64"};
            var patch=new ComboBox{ItemsSource=Enumerable.Range(1,8).ToArray(),SelectedItem=3};
            view.Children.Add(Text("Texture resolution"));view.Children.Add(textureSize);
            view.Children.Add(Text("Bezier patch detail"));view.Children.Add(patch);
            var clip=new CheckBox {Content="Keep player clips",IsChecked=true};
            var items=new CheckBox {Content="Import source pickups",IsChecked=true};
            var sky=new CheckBox {Content="Keep sky surfaces",IsChecked=true};
            var spawns=new CheckBox {Content="Use source spawn points",IsChecked=true};
            var heal=new CheckBox {Content="Auto-heal imported collision",IsChecked=true};
            var healTolerance=new TextBox{Text="0.0625"};
            view.Children.Add(clip);view.Children.Add(items);view.Children.Add(sky);view.Children.Add(spawns);view.Children.Add(heal);
            view.Children.Add(Text("Collision heal tolerance"));view.Children.Add(healTolerance);
            var dependencies=new List<string>();
            var report=Text("Preflight has not run yet.");report.MaxHeight=120;view.Children.Add(report);

            float? SelectedScale()
            {
                if(scaleMode.SelectedIndex==0)return null;
                if(scaleMode.SelectedIndex==1)return 35f;
                return Number(customScale.Text??"");
            }
            async Task AnalyzeWizard()
            {
                try
                {
                    report.Text="Scanning BSP, shaders and sibling PK3s…";
                    string? map=maps.SelectedItem as string;
                    float? scale=SelectedScale();
                    var analysis=await Task.Run(()=>Q3ImportService.Analyze(source,map,dependencies,scale));
                    string missing=analysis.Textures.Missing.Count==0?"all resolved":
                        $"{analysis.Textures.Missing.Count} fallback · "+string.Join(", ",analysis.Textures.Missing.Take(5))
                        +(analysis.Textures.Missing.Count>5?" …":"");
                    report.Text=$"{analysis.MapName}\n{analysis.Surfaces:N0} surfaces · {analysis.Patches:N0} patches · {analysis.Brushes:N0} brushes · {analysis.Spawns} starts · {analysis.Pickups} pickups\n"
                        +$"{analysis.Width:0.#} × {analysis.Height:0.#} × {analysis.Depth:0.#} MPH units · auto scale {analysis.AutoScale:0.#}\n"
                        +$"Textures {analysis.Textures.Resolved}/{analysis.Textures.Total} · {missing}\n"
                        +$"Archives: {string.Join(", ",analysis.Textures.Archives.Select(Path.GetFileName))}"
                        +(analysis.GameplayWarnings.Count==0?"":"\n\nPrime gameplay review:\n"+string.Join("\n",analysis.GameplayWarnings.Select(d=>"• "+d.Message)));
                }
                catch(Exception ex){report.Text="Preflight failed: "+ex.Message;}
            }
#if !ANDROID
            async Task AddDependency()
            {
                string? dep=_services.NativeFileDialogs
                    ? await _services.PickFileAsync("Add texture dependency PK3",false,new[]{".pk3"},CancellationToken.None)
                    : null;
                if(dep!=null&&!dependencies.Contains(dep,StringComparer.OrdinalIgnoreCase))dependencies.Add(dep);
                await AnalyzeWizard();
            }
            AddButton(view,"Add dependency PK3",()=>_=AddDependency());
#endif
            AddButton(view,"Analyze",()=>_=AnalyzeWizard());
            AddButton(view,"Import",()=>
            {
                string room=(name.Text??"").Trim();string? map=maps.SelectedItem as string;
                float? selectedScale;
                int texSize,patchLevel;
                try
                {
                    MapValidator.RequireRuntimeName(room);
                    selectedScale=SelectedScale();
                    texSize=int.Parse(textureSize.SelectedItem?.ToString()??"64",CultureInfo.InvariantCulture);
                    patchLevel=Convert.ToInt32(patch.SelectedItem,CultureInfo.InvariantCulture);
                }
                catch(Exception ex){Failure(ex);return;}
                var options=new Q3ImportService.Options(source,map,room,
                    Path.Combine(_services.MapLibraryDirectory,room.ToLowerInvariant()),
                    selectedScale,clip.IsChecked==true,items.IsChecked==true,sky.IsChecked==true,spawns.IsChecked==true,
                    patchLevel,texSize,dependencies.ToArray(),
                    AutoHealCollision:heal.IsChecked==true,
                    CollisionHealTolerance:Number(healTolerance.Text??""));
                WithUnsaved(()=>_=Job("Importing Quake 3 map",async token=>
                {
                    Dismiss();
                    Action<string> progress=message=>Dispatcher.UIThread.Post(()=>{
                        if(!_detached&&_work!=null)_status.Text=message;
                    });
                    var result=await Task.Run(()=>Q3ImportService.Import(options,token,progress),token);
                    GuardJob(token);
                    _problems.ItemsSource=result.Diagnostics.Select(d=>$"{d.Severity} · {d.Message}").ToArray();
                    if(!result.Succeeded||result.ProjectPath==null)
                    {
                        _status.Text=result.Diagnostics.LastOrDefault(d=>d.Severity==Q3ImportService.Severity.Error)?.Message??"Import failed.";
                        return;
                    }
                    string projectPath=result.ProjectPath;
                    Load(MapProjectMigrator.Upgrade(MapProjectSerializer.Load(projectPath)),projectPath);
                    if(result.Analysis is {} a)
                        _status.Text=$"Imported {a.MapName} · {a.Textures.Resolved}/{a.Textures.Total} textures resolved · {a.GameplayWarnings.Count} gameplay review warnings · Validate and playtest before hosting";
                }));
            });
            AddButton(view,"Cancel",Dismiss);
            var scroll=new ScrollViewer
            {
                Content=view,
                MaxHeight=520,
                VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            Modal(scroll);_=AnalyzeWizard();
        }
        private Task RebakeImportTextures()=>Job("Rebaking Q3 textures",async token=>
        {
            if(_document==null)return;
            var definition=_document.CaptureBuildSnapshot().CreateDefinition();
            var import=definition.Import??throw new InvalidOperationException("This project is not imported.");
            string level=import.Resolve()??throw new IOException("Imported Q3 source could not be resolved.");
            string textureName=String.IsNullOrWhiteSpace(import.Textures)
                ? definition.Name.ToLowerInvariant()+".tex" : import.Textures!;
            string target=Path.Combine(import.BaseDirectory??definition.BaseDirectory??_services.MapLibraryDirectory,textureName);
            string provenanceRoot=definition.BaseDirectory??Path.GetDirectoryName(definition.SourcePath??"")??_services.MapLibraryDirectory;
            Q3ImportManifest? provenance=Q3ImportManifest.Load(provenanceRoot);
            var bsp=await Task.Run(()=>Q3Bsp.Load(level,import.MapName,token),token);
            var archives=MapTextureBake.DiscoverArchives(level,provenance?.DependencyArchives());
            var result=await Task.Run(()=>MapTextureBake.Bake(bsp,archives,target,MapTextureBake.DefaultSize,cancellation:token),token);
            var modern=await Task.Run(()=>MapTextureBake.ExtractModern(bsp,archives,cancellation:token),token);
            GuardJob(token);
            var modernPaths=new Dictionary<int,string>();
            string textureRoot=import.BaseDirectory??definition.BaseDirectory??_services.MapLibraryDirectory;
            foreach(var source in modern)
            {
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source.Bytes)).ToLowerInvariant();
                string relative="textures/q3-"+hash+source.Extension;
                string output=Path.GetFullPath(Path.Combine(textureRoot,relative));
                string prefix=Path.GetFullPath(textureRoot)+Path.DirectorySeparatorChar;
                if(!output.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Rebaked HD texture escapes the map project.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                if(!File.Exists(output))AtomicFile.Write(output,source.Bytes);
                modernPaths[source.SourceIndex]=relative;
            }
            if(provenance!=null){provenance.UpdateTextureBake(result);provenance.Save(provenanceRoot);}
            _document.Edit("Rebake Q3 textures",d=>
            {
                if(String.IsNullOrWhiteSpace(d.Import!.Textures))d.Import.Textures=textureName;
                var stale=d.Import.ModernTextures.Values.Except(modernPaths.Values,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                d.Import.ModernTextures=new Dictionary<int,string>(modernPaths);
                foreach(string relative in modernPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                    if(!d.Assets.Any(a=>a.Path.Equals(relative,StringComparison.OrdinalIgnoreCase)))
                        d.Assets.Add(new MapAsset{Path=relative,Kind="texture",Name="Q3 HD source"});
                d.Assets.RemoveAll(a=>stale.Contains(a.Path)
                    && !d.Materials.Any(m=>new[]{m.Texture,m.Albedo,m.Normal,m.SpecularRoughness,m.Emissive}
                        .Any(p=>String.Equals(p,a.Path,StringComparison.OrdinalIgnoreCase))));
            },MapChangeDomain.Import|MapChangeDomain.Material);
            foreach(string relative in modernPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                _document.RegisterGeneratedAsset(relative,textureRoot);
            _validatedState=null;
            _status.Text=$"Rebaked {result.Baked} Q3 textures · {modern.Count} HD source images · {result.Resolved} resolved · {result.Fallbacks} fallback · {result.Archives.Count} archive(s)";
        });

        private async Task PickReimportSource()
        {
            if(_document?.Project.Definition.Import==null)return;
#if !ANDROID
            if(_services.NativeFileDialogs)
            {
                string? picked=await _services.PickFileAsync("Reimport Quake 3 PK3",false,new[]{".pk3",".bsp"},CancellationToken.None);
                if(picked!=null){RunReimport(picked);return;}
            }
#endif
            Browse("Choose replacement Quake 3 source",false,RunReimport,".pk3",".bsp");
        }

        private void RunReimport(string source)=>_=PreviewReimport(source);

        private async Task PreviewReimport(string source)
        {
            if(_document?.Project.Definition.Import is not {} import)return;
            string projectPath=_document.FilePath??_path.Text??"";
            if(String.IsNullOrWhiteSpace(projectPath)){_status.Text="Save this project before reimporting its source.";return;}
            var existing=_document.Project.ToDefinition();
            string? selectedMap=import.MapName;
            try
            {
                var maps=Q3Bsp.ListMaps(source);
                if(selectedMap==null||!maps.Contains(selectedMap,StringComparer.OrdinalIgnoreCase))selectedMap=maps.FirstOrDefault();
                _status.Text="Comparing Q3 source…";
                var diff=await Task.Run(()=>Q3ImportService.PreviewReimport(existing,source,selectedMap));
                var panel=new StackPanel{Spacing=8,MinWidth=560};
                var summary=Text(diff.Summary());summary.TextWrapping=TextWrapping.Wrap;panel.Children.Add(summary);
                string map=selectedMap??diff.Next.MapName;
                AddButton(panel,"Apply reimport",()=>{Dismiss();StartReimport(source,map,existing,projectPath,import);});
                AddButton(panel,"Cancel",Dismiss);Modal(panel);
            }
            catch(Exception ex){Failure(ex);}
        }

        private void StartReimport(string source,string selectedMap,MapDefinition existing,string projectPath,MapImport import)
        {
            var options=new Q3ImportService.Options(source,selectedMap,existing.Name,
                Path.Combine(_services.StagingDirectory,"ProjectPrime-reimport-"+Guid.NewGuid().ToString("N")),
                import.UnitsPerUnit,import.KeepClip,import.KeepItems,import.KeepSky,import.KeepSpawns,
                import.PatchLevel,MapTextureBake.DefaultSize);
            _=Job("Reimporting Q3 source",async token=>
            {
                var result=await Task.Run(()=>Q3ImportService.Reimport(existing,options,projectPath,token),token);
                GuardJob(token);
                _problems.ItemsSource=result.Diagnostics.Select(d=>$"{d.Severity} · {d.Message}").ToArray();
                if(!result.Succeeded||result.ProjectPath==null)
                {
                    _status.Text=result.Diagnostics.LastOrDefault(d=>d.Severity==Q3ImportService.Severity.Error)?.Message??"Reimport failed.";
                    return;
                }
                Load(MapProjectSerializer.Load(result.ProjectPath),result.ProjectPath);
                _status.Text="Q3 architecture and textures reimported; authored gameplay and hybrid geometry were preserved.";
            });
        }

    }
}
