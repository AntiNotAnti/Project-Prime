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
        private void CloneBuiltIn()
        {
            if(!_services.GameFilesReady){_status.Text="Set up game files before cloning a built-in room.";return;}
            try{_services.ApplyGamePaths();}catch(Exception ex){Failure(ex);return;}
            var panel=new StackPanel{Spacing=8,MinWidth=560};panel.Children.Add(Text("CLONE BUILT-IN MAP"));
            var search=new TextBox{PlaceholderText="Search rooms"};panel.Children.Add(search);
            var rooms=Metadata.RoomMetadata.Values.GroupBy(r=>r.Name,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.First()).OrderByDescending(r=>r.Multiplayer).ThenBy(r=>r.InGameName??r.Name,StringComparer.OrdinalIgnoreCase).ToArray();
            var list=new ListBox{MaxHeight=330};panel.Children.Add(list);
            void Refresh()
            {
                string q=(search.Text??"").Trim();
                list.ItemsSource=rooms.Where(r=>q.Length==0||(r.InGameName??r.Name).Contains(q,StringComparison.OrdinalIgnoreCase)
                    ||r.Name.Contains(q,StringComparison.OrdinalIgnoreCase))
                    .Select(r=>new NativeRoomRow(r)).ToArray();
            }
            search.TextChanged+=(_,_)=>Refresh();Refresh();
            var name=new TextBox{PlaceholderText="New runtime name"};panel.Children.Add(name);
            list.SelectionChanged+=(_,_)=>
            {
                if(list.SelectedItem is NativeRoomRow row&&String.IsNullOrWhiteSpace(name.Text))
                {
                    string candidate=(row.Room.Name+" REMIX").Replace('/',' ').Replace('\\',' ');
                    name.Text=candidate.Length<=40?candidate:candidate[..40].TrimEnd();
                }
            };
            AddButton(panel,"Create remix",()=>WithUnsaved(()=>
            {
                if(list.SelectedItem is not NativeRoomRow row){_status.Text="Choose a built-in room.";return;}
                try{Load(NativeRoomProject.Create(row.Room.Name,(name.Text??"").Trim()));}
                catch(Exception ex){Failure(ex);}
            }));
            AddButton(panel,"Cancel",Dismiss);Modal(new ScrollViewer{Content=panel,MaxHeight=560,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});
        }

        private sealed record NativeRoomRow(RoomMetadata Room)
        {
            public override string ToString()=>$"{Room.InGameName??Room.Name} · {Room.Name} · {(Room.Multiplayer?"Multiplayer":"Adventure")}";
        }

        private void NewMap()
        {
            var view=new Grid{RowDefinitions=new("Auto,Auto,*,Auto"),MinWidth=760,Height=610};
            view.Children.Add(Text("NEW MAP · TEMPLATE GALLERY"));
            var name=new TextBox{Text="My Arena",Margin=new Thickness(0,8,0,8)};
            Grid.SetRow(name,1);view.Children.Add(name);
            var list=new ListBox{SelectionMode=SelectionMode.Single};Grid.SetRow(list,2);view.Children.Add(list);
            list.ItemsSource=MapTemplates.Catalog;
            list.SelectedIndex=0;
            list.ItemTemplate=new FuncDataTemplate<MapTemplateInfo>((info,_)=>
            {
                if(info==null)return new TextBlock();
                var card=new Grid
                {
                    ColumnDefinitions=new("190,*"),
                    Margin=new Thickness(4,6),
                    MinHeight=116
                };
                card.Children.Add(new MapTemplatePreview(info));
                var copy=new StackPanel{Spacing=3,Margin=new Thickness(12,0,0,0)};
                copy.Children.Add(new TextBlock{Text=info.Name,Foreground=GuiTheme.TextBrush,
                    FontWeight=FontWeight.SemiBold,FontSize=16});
                copy.Children.Add(new TextBlock{Text=info.Description,Foreground=GuiTheme.TextDimBrush,
                    TextWrapping=TextWrapping.Wrap,MaxWidth=500});
                copy.Children.Add(new TextBlock
                {
                    Text=$"PLAYERS  {info.RecommendedPlayers}    ·    MODES  {String.Join(", ",info.SupportedModes)}",
                    Foreground=PrimeTheme.PrimaryBrush,FontSize=11
                });
                Grid.SetColumn(copy,1);card.Children.Add(copy);return card;
            });
            var buttons=new WrapPanel();Grid.SetRow(buttons,3);view.Children.Add(buttons);
            AddButton(buttons,"Create / Continue",()=>WithUnsaved(()=>
            {
                if(list.SelectedItem is not MapTemplateInfo info)return;
                try
                {
                    Dismiss();
                    switch(info.Action)
                    {
                        case MapTemplateAction.Create:
                            Load(MapTemplates.Create(name.Text??"",info.Id));break;
                        case MapTemplateAction.ImportQ3:
                            _inspectorPage="Collision repairs";Import();break;
                        case MapTemplateAction.CloneNative:
                            _inspectorPage="Environment";CloneBuiltIn();break;
                    }
                }
                catch(Exception ex){Failure(ex);}
            }));
            AddButton(buttons,"Cancel",Dismiss);
            Modal(view);
        }
        private void ShowLibrary()
        {
            _inspector.Children.Clear();
            Inspect();
            var view=new Grid {RowDefinitions=new("Auto,*,Auto"),MinWidth=650,Height=460};view.Children.Add(Text("MAP LIBRARY"));
            var list=new ListBox();Grid.SetRow(list,1);view.Children.Add(list);
            list.ItemTemplate=new FuncDataTemplate<LibraryRow>((row,_)=>
            {
                var card=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,Margin=new Thickness(4)};
                if(row?.Entry.Definition is {} definition)
                {
                    try
                    {
                        var preview=definition.Assets.FirstOrDefault(a=>a.Kind=="preview");
                        string fallback=ThumbnailGenerator.PathFor(definition.Name);
                        string sourceKey=row!.Entry.Path+"|"+(preview?.Path??fallback);
                        if(preview==null&&File.Exists(fallback))
                        {
                            var info=new FileInfo(fallback);sourceKey+=$"|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                        }
                        if(!_thumbnailCache.TryGetValue(sourceKey,out var bitmap))
                        {
                            using var stream=preview!=null?new MemoryStream(MapAssets.Read(definition,preview.Path)):File.Exists(fallback)?File.OpenRead(fallback):(Stream?)null;
                            if(stream!=null){bitmap=Bitmap.DecodeToWidth(stream,96);_thumbnailCache[sourceKey]=bitmap;}
                        }
                        if(bitmap!=null)card.Children.Add(new Image {Source=bitmap,Width=96,Height=54,Stretch=Stretch.UniformToFill});
                    }
                    catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException){ }
                }
                card.Children.Add(Text(row?.ToString()??""));return card;
            });
            try{list.ItemsSource=_catalog.Refresh(false).Select(e=>new LibraryRow(e)).ToArray();}catch(Exception ex){Failure(ex);}
            var buttons=new WrapPanel();Grid.SetRow(buttons,2);view.Children.Add(buttons);
            AddButton(buttons,"Open",()=>{if(list.SelectedItem is LibraryRow row)Open(row.Entry.Path);});
            AddButton(buttons,"Duplicate",()=>{if(list.SelectedItem is LibraryRow {Entry.Definition:not null} row){var p=MapProjectMigrator.Upgrade(new(row.Entry.Definition));p.Definition.MapId=Guid.NewGuid();p.Definition.Name=NextCopyName(p.Definition.Name);WithUnsaved(()=>Load(p));}});
            AddButton(buttons,"Delete",()=>{if(list.SelectedItem is LibraryRow row)Confirm("Delete "+Path.GetFileName(row.Entry.Path)+"?",()=>{try{File.Delete(row.Entry.Path);ShowLibrary();}catch(Exception ex){Failure(ex);}});});
            AddButton(buttons,"Reveal folder",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_services.MapLibraryDirectory){UseShellExecute=true});}catch(Exception ex){Failure(ex);}});
            AddButton(buttons,"Refresh",ShowLibrary);AddButton(buttons,"New",NewMap);AddButton(buttons,"Close",Dismiss);Modal(view, fitContent: true);
            AddButton(buttons,"Recover unsaved",RecoverUnsaved);
        }
        private void RecoverUnsaved()
        {
            var panel=new StackPanel {Spacing=8};panel.Children.Add(Text("RECOVERY FILES"));
            var list=new ListBox {MaxHeight=350};string directory=Path.Combine(_services.UserMapDirectory,".autosave");
            list.ItemsSource=Directory.Exists(directory)?Directory.EnumerateFiles(directory,"*.json").Where(p=>!p.EndsWith(".context.json",StringComparison.OrdinalIgnoreCase)).Select(p=>new RecoveryRow(p)).ToArray():Array.Empty<RecoveryRow>();panel.Children.Add(list);
            AddButton(panel,"Restore",()=>{if(list.SelectedItem is RecoveryRow row)WithUnsaved(()=>{try{Load(MapDocument.ReadRecovery(row.Path));}catch(Exception ex){Failure(ex);}});});
            AddButton(panel,"Discard",()=>{if(list.SelectedItem is RecoveryRow row)Confirm("Delete this recovery copy?",()=>{try{File.Delete(row.Path);if(File.Exists(row.Path+".context.json"))File.Delete(row.Path+".context.json");RecoverUnsaved();}catch(Exception ex){Failure(ex);}});});
            AddButton(panel,"Back",ShowLibrary);Modal(panel);
        }
        private string NextCopyName(string name)
        {
            string root=name.Trim();
            if(root.Length>34)root=root[..34].TrimEnd();
            var used=_catalog.Refresh(false).Where(e=>e.Definition!=null)
                .Select(e=>e.Definition!.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string candidate=root+" COPY";int suffix=2;
            while(used.Contains(candidate))
            {
                string tail=" COPY "+suffix++;
                string head=root.Length>40-tail.Length?root[..(40-tail.Length)].TrimEnd():root;
                candidate=head+tail;
            }
            return candidate;
        }
        private sealed record RecoveryRow(string Path)
        {public override string ToString(){try{return MapDocument.ReadRecovery(Path).Definition.Name+" · "+File.GetLastWriteTime(Path).ToString("g");}catch{return System.IO.Path.GetFileName(Path)+" · unreadable recovery";}}}
        private sealed record LibraryRow(MapCatalogEntry Entry)
        {
            public override string ToString()
            {
                if(Entry.Definition is not {} d)return Path.GetFileName(Entry.Path)+" · Invalid source";
                string status=Entry.Validation.IsValid?"Ready to validate":"Source problems";

                string source=d.NativeRoom!=null?"Native remix":d.Import!=null?"Q3":"Project Prime";
                return $"{d.InGameName??d.Name} · {d.Author??""} {d.Version??""}\n{source} · {status} · {Entry.Validation.Diagnostics.Count} diagnostics";
            }
        }
        private void Browse(string title,bool save,Action<string> selected,params string[] extensions)
        {
            if (_services.NativeFileDialogs) { _ = BrowseNativeAsync(title,save,selected,extensions); return; }
            var view=new Grid {RowDefinitions=new("Auto,Auto,*,Auto,Auto"),MinWidth=650,Height=480};view.Children.Add(Text(title));
            var location=new TextBox {Text=Directory.Exists(_services.MapLibraryDirectory)?_services.MapLibraryDirectory:AppContext.BaseDirectory};Grid.SetRow(location,1);view.Children.Add(location);
            var files=new ListBox();Grid.SetRow(files,2);view.Children.Add(files);var filename=new TextBox {Text=save?"map.json":""};Grid.SetRow(filename,3);view.Children.Add(filename);
            void Refresh()
            {try{files.ItemsSource=Directory.EnumerateFileSystemEntries(location.Text??"").Where(p=>Directory.Exists(p)||extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p=>!Directory.Exists(p)).ThenBy(p=>p).Select(p=>new BrowserRow(p)).ToArray();}catch(Exception ex){_status.Text=ex.Message;}}
            files.DoubleTapped+=(_,_)=>{if(files.SelectedItem is BrowserRow row){if(Directory.Exists(row.Path)){location.Text=row.Path;Refresh();}else filename.Text=Path.GetFileName(row.Path);}};
            files.SelectionChanged+=(_,_)=>{if(files.SelectedItem is BrowserRow row&&!Directory.Exists(row.Path))filename.Text=Path.GetFileName(row.Path);};
            var buttons=new WrapPanel();Grid.SetRow(buttons,4);view.Children.Add(buttons);
            AddButton(buttons,"Up",()=>{location.Text=Path.GetDirectoryName(location.Text)??location.Text;Refresh();});AddButton(buttons,"Go",Refresh);
            AddButton(buttons,save?"Save":"Open",()=>
            {
                string path=Path.Combine(location.Text??"",filename.Text??"");
                if(!extensions.Contains(Path.GetExtension(path).ToLowerInvariant())){_status.Text="Choose a supported file type: "+string.Join(", ",extensions);return;}
                if(save&&File.Exists(path))Confirm("Replace "+Path.GetFileName(path)+"?",()=>{Dismiss();selected(path);});else{Dismiss();selected(path);}
            });AddButton(buttons,"Cancel",Dismiss);Refresh();Modal(view);
        }
        private sealed record BrowserRow(string Path){public override string ToString()=>(Directory.Exists(Path)?"[folder] ":"")+System.IO.Path.GetFileName(Path);}
        private sealed record PrefabRow(string Path,int Objects,int Materials)
        {
            public override string ToString()=>$"{System.IO.Path.GetFileNameWithoutExtension(Path)} · {Objects} objects · {Materials} materials";
        }
    }
}
