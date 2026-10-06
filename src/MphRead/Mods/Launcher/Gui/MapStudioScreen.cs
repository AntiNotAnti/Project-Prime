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
    internal sealed partial class MapStudioScreen : UserControl, IDisposable
    {
        internal Task PlaytestAsync() => Play();
        internal void FrameAll() => _viewport?.FrameAll();
        internal void ToggleWireframe() { if (_viewport != null) { _viewport.Wireframe = !_viewport.Wireframe; _viewport.InvalidateVisual(); } }
        public event EventHandler? Closed;
        public event EventHandler<MapDefinition>? PlayRequested;
        public event EventHandler<MapDefinition>? HostRequested;
        private readonly Grid _root = new()
        {
            RowDefinitions = new("Auto,Auto,Auto,*,0,Auto"),
            RowSpacing = 8,
            Margin = new Thickness(18, 14, 18, 16)
        };
        private readonly Panel _viewportHost = new();
        private readonly TextBlock _projectTitle = PrimeChrome.Title("MAP STUDIO");
        private readonly TextBlock _projectState = PrimeChrome.Eyebrow("NO PROJECT");
        private readonly TextBlock _selectionState = PrimeChrome.Text(
            "0 SELECTED", PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true);
        private readonly TextBlock _toolSelectionState = PrimeChrome.Text(
            "OBJECT MODE", PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true);
        private readonly TextBlock _inspectorTitle = PrimeChrome.Title("INSPECTOR");
        private readonly PrimeButton _studioSave;
        private readonly PrimeButton _studioValidate;
        private readonly PrimeButton _studioBuild;
        private readonly PrimeButton _studioPlaytest;
        private readonly List<Control> _editingControls = new();
        private readonly Dictionary<string,Bitmap> _thumbnailCache=new(StringComparer.Ordinal);
        private readonly Dictionary<string,(Bitmap Bitmap,string Details)> _materialPreviewCache=new(StringComparer.Ordinal);
        private readonly StackPanel _inspector = new() { Spacing=6, Margin=new Thickness(10) };
        private readonly ListBox _hierarchy = new() { SelectionMode=SelectionMode.Multiple };
        private readonly ListBox _problems = new() { IsVisible=false };
        private bool? _showProblems;
        private Control? _cancelJob;
        private Panel? _hostToolbar;
        private readonly TextBox _path = new() { PlaceholderText="Project filename (.json)" };
        private readonly TextBlock _status = new() { Foreground=GuiTheme.TextDimBrush, TextWrapping=TextWrapping.Wrap };
        private readonly TextBox _search = new() { PlaceholderText="Search objects" };
        private readonly ComboBox _hierarchyFilter = new(){MinWidth=185,SelectedIndex=0};
        private readonly IMapStudioHostServices _services;
        private IDisposable? _dropSubscription;
        private readonly Border _modal = new() { Background=GuiTheme.ScrimBrush, IsVisible=false };
        private readonly DispatcherTimer _idle = new() { Interval=TimeSpan.FromSeconds(2) };
        private readonly MapCatalog _catalog;
        private MapDocument? _document;
        private MapViewport? _viewport;
        private CancellationTokenSource? _work;
        private bool _refreshing;
        private string _hierarchySignature = "";
        private string _inspectorPage = "Inspector";
        private MapStudioState _studioState = new();
        private MapPickHit? _pickedMaterialHit;
        private long _editorGeneration;
        private bool _detached;
        private MapAutosaveService _autosave = new();
        private MapAutosaveResult? _reportedAutosave;
        private DateTime _autosaveRetryAfter;
        private DocumentStateId? _validatedState;
        private string? _validationSignature;
        private MapDiagnostic[] _importWarnings = Array.Empty<MapDiagnostic>();
        private MapDocument? _jobDocument;
        private DocumentStateId? _jobState;
        private long _jobGeneration;
        private void GuardJob(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_detached || _editorGeneration != _jobGeneration || _document != _jobDocument
                || _document?.CurrentStateId != _jobState) throw new OperationCanceledException();
        }
        private DateTime _autosaved = DateTime.MinValue;
        private bool _checking;
        private MapBuildResult? _lastBuild;
        private TextBlock? _diagnostics;
        private readonly string _previewName="STUDIO "+Guid.NewGuid().ToString("N");

        internal static int Capture(string directory)
        {
            if(!GuiLauncher.EnsureSetup())return 1;
            Directory.CreateDirectory(directory);
            Dispatcher.UIThread.Invoke(()=>
            {
                var screen=new MapStudioScreen();screen.Load(MapTemplates.Create("Studio example",true));
                UiCapture.Capture(screen,Path.Combine(directory,"map-studio.png"),new Size(1440,900));
                var small=new MapStudioScreen();small.Load(MapTemplates.Create("Studio example",false));
                UiCapture.Capture(small,Path.Combine(directory,"map-studio-small.png"),new Size(960,600));
                var quad=new MapStudioScreen();quad.Load(MapTemplates.Create("Studio four views",true));quad.ToggleFourViews();
                UiCapture.Capture(quad,Path.Combine(directory,"map-studio-four-views.png"),new Size(1920,1080));
            });
            return 0;
        }

        internal MapStudioScreen(IMapStudioHostServices services, bool preview = false)
        {
            _services = services;
            ConfigureAssetDropTarget();
            if (services.IsStandalone)
            {
                _root.Margin=new Thickness(6);_root.RowSpacing=4;
                _projectTitle.FontSize=16;_projectState.FontSize=10;
                _projectState.TextWrapping=TextWrapping.NoWrap;
                _status.FontSize=11;
            }
            _catalog = new(services.MapLibraryDirectory);
            Background = PrimeTheme.BackgroundDeepBrush;
            Focusable = true;
            _hierarchy.Background = _problems.Background = PrimeTheme.PanelBrush;
            _hierarchy.Foreground = _problems.Foreground = PrimeTheme.TextBrush;
            _hierarchy.BorderBrush = _problems.BorderBrush = PrimeTheme.BorderBrush;
            _studioSave = new PrimeButton("SAVE", Save, primary: true, compact: true);
            _studioValidate = new PrimeButton("VALIDATE", () => _ = Validate(), compact: true);
            _studioBuild = new PrimeButton("BUILD .PPMAP", () => _ = Build(true), compact: true);
            _studioPlaytest = new PrimeButton("PLAYTEST", PlaytestInspector, compact: true);
            ControllerNav.Identify(_studioSave, "studio.save");
            ControllerNav.Identify(_studioValidate, "studio.validate");
            ControllerNav.Identify(_studioBuild, "studio.build");
            ControllerNav.Identify(_studioPlaytest, "studio.playtest");

            var headerCopy = services.IsStandalone ? new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center,Children={_projectTitle,_projectState}} : PrimeChrome.Stack(
                PrimeChrome.Eyebrow("MAP STUDIO // AUTHORING WORKSPACE"),
                _projectTitle,
                PrimeChrome.Text(
                    "Build geometry, gameplay, materials and packages in one retained editor workspace.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush),
                _projectState);
            var backStudio = new PrimeButton(services.IsStandalone ? "CLOSE TAB" : "BACK", Close, compact: true);
            ControllerNav.Identify(backStudio, "studio.back");
            var headerActions = PrimeChrome.Columns("Auto,Auto,Auto,Auto,Auto",
                backStudio, _studioSave, _studioValidate, _studioBuild, _studioPlaytest);
            if(services.IsStandalone) foreach(var button in new[]{backStudio,_studioSave,_studioValidate,_studioBuild,_studioPlaytest})
            {button.MinHeight=button.Height=button.MaxHeight=32;button.FontSize=11;}
            var header = PrimeChrome.Columns("*,Auto", headerCopy, headerActions);
            _root.Children.Add(header);
            _editingControls.AddRange(new Control[]
            {
                _studioSave, _studioValidate, _studioBuild, _studioPlaytest
            });

            Panel toolbar = services.IsStandalone ? new WrapPanel { Orientation=Orientation.Horizontal } : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            var menus = new Avalonia.Controls.Menu();
            _hostToolbar=toolbar;
            toolbar.Children.Add(menus);
            void MenuGroup(string name, params (string Name,Action Run)[] commands)
            {
                var group=new MenuItem { Header=name };
                foreach(var command in commands) { var item=new MenuItem { Header=command.Name };item.Click+=(_,_)=>command.Run();group.Items.Add(item); }
                menus.Items.Add(group);
            }
            MenuGroup("File",("Source changes…",ShowSourceChanges),("Library",ShowLibrary),("New",NewMap),("Clone built-in",CloneBuiltIn),
                ("Open…",()=>Browse("Open project",false,Open,".json",".ppmap")),("Import Q3…",Import),("Import 3D Model…",ImportModel),("Model Sources / Reimport…",ManageModelSources),
                ("Export project folder…",()=>Browse("Export project folder (choose map.json destination)",true,path=>{try{if(_document!=null){string exported=MapProjectFolder.Export(_document.Project.Definition,Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)));_status.Text="Exported "+exported;}}catch(Exception ex){Failure(ex);}},".json")),("Save",Save),("Save as…",()=>Browse("Save project",true,SaveTo,".json")),("Back to launcher",Close));
            MenuGroup("Edit",("Undo",()=>_document?.History.Undo()),("Redo",()=>_document?.History.Redo()),
                ("Copy",()=>_document?.CopySelection()),("Paste",()=>_document?.PasteClipboard()),
                ("Duplicate",()=>EditSelection("Duplicate",MapObjects.Duplicate)),("Delete",()=>EditSelection("Delete",MapObjects.Delete)),
                ("Hide selection",()=>_document?.HideSelection()),("Show all",()=>_document?.ShowAllGeometry()),("Commands…",ShowCommandPalette));
            MenuGroup("View",("Four views / single view",ToggleFourViews),("Frame all",()=>_viewport?.FrameAll()),("Frame selection",()=>_viewport?.FrameSelection()),
                ("Problems",()=>{_showProblems=!_problems.IsVisible;UpdateProblemsVisibility();}),
                ("Measure",()=>{if(_viewport!=null){_viewport.MeasureMode=!_viewport.MeasureMode;_viewport.InvalidateVisual();}}),
                ("Entity helpers",()=>{if(_viewport!=null){_viewport.EntityVisualization=!_viewport.EntityVisualization;_viewport.InvalidateVisual();}}),
                ("Capture preview",CapturePreview));
            MenuGroup("Build",("Validate",()=>_=Validate()),("Fix selected problem",FixSelectedProblem),("Build runtime",()=>_=Build(false)),
                ("Export .ppmap",()=>_=Build(true)),("Playtest",PlaytestInspector),("Run map audit",()=>_=Audit()));
            MenuGroup("Online",("Community maps…",ShowCommunity),("Host current map…",()=>_=PrepareOnline()));
            AddButton(toolbar, "LIBRARY", ShowLibrary);
            AddButton(toolbar, "IMPORT MODEL", ImportModel);
            AddButton(toolbar, "ASSETS", services.IsStandalone ? OpenAssetBrowser : () => ShowInspectorPage("Assets & music"));
            if (!services.IsStandalone) AddButton(toolbar, "POP OUT", () => _ = PopOut());
            AddButton(toolbar, "CANCEL JOB", () => _work?.Cancel());
            _cancelJob = toolbar.Children[^1];
            _cancelJob.IsVisible = false;
            _editingControls.AddRange(toolbar.Children);

            _path.MinWidth = services.IsStandalone ? 160 : 320;
            var commandBar = new Grid
            {
                ColumnDefinitions = new("Auto,*,Auto"),
                ColumnSpacing = 10
            };
            commandBar.Children.Add(toolbar);
            Grid.SetColumn(_path, 1);
            commandBar.Children.Add(_path);
            var projectOps = PrimeChrome.Text(
                "PROJECT FILE  //  menus retain full import / export / online operations",
                PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true);
            projectOps.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(projectOps, 2);
            commandBar.Children.Add(projectOps);
            if (services.IsStandalone)
            {
                commandBar.ColumnDefinitions = new("Auto,*");
                Grid.SetColumn(_path,1);_path.Margin=new Thickness(4,0,0,0);
                _path.MinHeight=26;_path.Height=26;_path.FontSize=11;
                projectOps.IsVisible=false;
                foreach(var button in toolbar.Children.OfType<PrimeButton>()) {button.MinHeight=button.Height=button.MaxHeight=28;button.FontSize=10;}
            }
            var commandPanel = new PrimePanel(commandBar, raised: true)
            {
                Padding = services.IsStandalone ? new Thickness(6,3) : new Thickness(10,7)
            };
            Grid.SetRow(commandPanel, 1);
            _root.Children.Add(commandPanel);
            var body = new Grid
            {
                ColumnDefinitions = new("260,6,*,6,340")
            };
            _search.MinHeight = services.IsStandalone ? 28 : 34;
            _hierarchyFilter.MinHeight = services.IsStandalone ? 28 : 34;
            var tree = new DockPanel();
            var treeTools = new StackPanel { Spacing = 6 };
            treeTools.Children.Add(_search);
            treeTools.Children.Add(_hierarchyFilter);
            DockPanel.SetDock(treeTools, Dock.Top);
            tree.Children.Add(treeTools);
            tree.Children.Add(_hierarchy);

            var hierarchyHeader = PrimeChrome.Stack(
                PrimeChrome.Eyebrow("SCENE GRAPH"),
                PrimeChrome.Title("SCENE HIERARCHY"),
                PrimeChrome.Text(
                    "Search and select authored geometry, spawns, pickups and navigation objects.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            if(services.IsStandalone)hierarchyHeader.IsVisible=false;
            var treePanel = new MapHierarchyPanel(hierarchyHeader,tree);
            body.Children.Add(treePanel);
            _hierarchy.ItemTemplate=new FuncDataTemplate<HierarchyRow>((row,_)=>
            {
                if(row==null)return new TextBlock();
                return new TextBlock
                {
                    Text=row.ToString(),
                    Foreground=row.Header?PrimeTheme.PrimaryBrush:GuiTheme.TextBrush,
                    FontWeight=row.Header?FontWeight.SemiBold:FontWeight.Normal,
                    Margin=row.Header?new Thickness(2,6,2,2):new Thickness(12,2,2,2)
                };
            });
            var viewportHeader = PrimeChrome.Columns("*,Auto",
                PrimeChrome.Stack(
                    PrimeChrome.Eyebrow("WORLD AUTHORING"),
                    PrimeChrome.Title("VIEWPORT")),
                _selectionState);
            if(services.IsStandalone)
            {
                viewportHeader.Children.Clear();
                viewportHeader.Children.Add(new TextBlock {Text="VIEWPORT",FontSize=11,VerticalAlignment=VerticalAlignment.Center});
                Grid.SetColumn(_selectionState,1);viewportHeader.Children.Add(_selectionState);
            }
            var viewportPanel = new MapViewportPanel(viewportHeader,_viewportHost);
            Grid.SetColumn(viewportPanel, 2);
            body.Children.Add(viewportPanel);

            var tools = new WrapPanel();
            void Choice(string[] choices,Action<string> choose)
            {
                var box=new ComboBox {ItemsSource=choices,SelectedIndex=0,Margin=new Thickness(2),MinWidth=85};
                box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is string text)choose(text);};tools.Children.Add(box);
            }
            Choice(new[]{"Move","Rotate","Scale"},name=>
            {
                if(_viewport!=null)
                {
                    _viewport.Tool=name;
                    RefreshStudioChrome();
                }
            });
            Choice(new[]{"Object","Face","Edge","Vertex"},name=>{if(_viewport!=null){_viewport.ElementMode=name;_viewport.ClearSubSelection();ShowInspectorPage(_inspectorPage,false);}});
            foreach(string action in new[]{"Extrude region","Inset region","Bevel","Snap to surface","Merge center","Delete"})
            {
                if(services.IsStandalone)continue;
                var modelingButton=new Avalonia.Controls.Button{Content=action,Margin=new Thickness(2)};
                modelingButton.Click+=(_,_)=>_viewport?.RunModeling(action);tools.Children.Add(modelingButton);
            }
            Choice(new[]{"Free","X","Y","Z","XY","XZ","YZ"},name=>{if(_viewport!=null)_viewport.Axes=name;});
            Choice(new[]{"Perspective","Top","Front","Side"},name=>_viewport?.SetView(name));
            Choice(new[]{"Place: Cursor","Place: Camera target","Place: Surface"},name=>
            {
                if(_viewport!=null)_viewport.PlacementMode=name switch
                {
                    "Place: Camera target"=>"Camera target",
                    "Place: Surface"=>"Surface",
                    _=>"Cursor"
                };
            });
            Choice(new[]{"Add object","Box","Wedge","Prism","Convex","Mesh","Spawn","Pickup","Jump pad","Navigation link"},name=>{if(name!="Add object")AddObject(name);});
            Choice(ViewportModes,SetViewportMode);
            Choice(new[]{"Inspector","Modeling","Partitioning","Collision repairs","Environment","Materials","UV","Assets & music","Snapping","Arrange","Layers","Map health","Navigation path","Gameplay analysis","Structural diff","Prefabs","Statistics"},name=>ShowInspectorPage(name));
            AddButton(tools,"Four views",ToggleFourViews);

            var inspectorScroll = new ScrollViewer
            {
                Content = _inspector,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            Control InspectorQuick(string label, string page)
            {
                if (!services.IsStandalone) return new PrimeButton(label.ToUpperInvariant(),
                    () => ShowInspectorPage(page), compact: true);
                var button = new Avalonia.Controls.Button
                {
                    Content = new TextBlock { Text=label,FontSize=11,TextWrapping=TextWrapping.NoWrap },
                    MinHeight=28,Height=28,MaxHeight=28,MinWidth=0,Padding=new Thickness(4,2),
                    HorizontalContentAlignment=HorizontalAlignment.Center
                };
                button.Click += (_,_) => ShowInspectorPage(page);
                return button;
            }
            var inspectQuick = InspectorQuick("Inspect", "Inspector");
            var materialsQuick = InspectorQuick("Materials", "Materials");
            var assetsQuick = InspectorQuick("Assets", "Assets & music");
            var healthQuick = InspectorQuick("Health", "Map health");
            ControllerNav.Identify(inspectQuick, "studio.inspector");
            ControllerNav.Identify(materialsQuick, "studio.materials");
            ControllerNav.Identify(assetsQuick, "studio.assets");
            ControllerNav.Identify(healthQuick, "studio.health");
            var inspectorQuick = PrimeChrome.Columns("*,*,*,*",
                inspectQuick, materialsQuick, assetsQuick, healthQuick);
            var inspectorHeader = PrimeChrome.Stack(
                PrimeChrome.Eyebrow("PROPERTIES // CONTEXT"),
                _inspectorTitle,
                inspectorQuick);
            if(services.IsStandalone)
            {
                inspectorHeader.Children.Clear();inspectorHeader.Children.Add(inspectorQuick);
            }
            var inspectorPanel = new MapInspectorPanel(inspectorHeader,inspectorScroll);
            Grid.SetColumn(inspectorPanel, 4);
            body.Children.Add(inspectorPanel);

            foreach(int column in new[]{1,3})
            {
                var splitter=new GridSplitter
                {
                    Width=6,
                    HorizontalAlignment=HorizontalAlignment.Stretch,
                    Background=PrimeTheme.BorderBrush
                };
                Grid.SetColumn(splitter,column);
                body.Children.Add(splitter);
            }

            AddButton(tools,"Maximize view",()=>{
                if (services.IsStandalone) { services.ToggleSidePanels(); return; }
                bool show=treePanel.IsVisible;
                treePanel.IsVisible=inspectorPanel.IsVisible=!show;
                body.ColumnDefinitions[0].Width=show?new GridLength(0):new GridLength(260);
                body.ColumnDefinitions[4].Width=show?new GridLength(0):new GridLength(340);
            });
            Choice(new[]{"Grid: 0.25","Grid: 0.5","Grid: 1","Grid: 2","Grid: 4","Grid: 8","Grid: Off"},name=>{if(_viewport!=null){_viewport.Snap=name=="Grid: Off"?0:float.Parse(name[6..],CultureInfo.InvariantCulture);_viewport.InvalidateVisual();}});

            var toolsScroll = new ScrollViewer
            {
                Content = tools,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            var toolsShell = new Grid
            {
                RowDefinitions = new("Auto,Auto"),
                RowSpacing = 4
            };
            toolsShell.Children.Add(PrimeChrome.Columns("*,Auto",
                PrimeChrome.Eyebrow("AUTHORING TOOLS"), _toolSelectionState));
            Grid.SetRow(toolsScroll, 1);
            toolsShell.Children.Add(toolsScroll);
            if(services.IsStandalone)
            {
                toolsShell.Children.RemoveAt(0);toolsShell.RowDefinitions=new("Auto");Grid.SetRow(toolsScroll,0);
            }
            var toolsPanel = new PrimePanel(toolsShell)
            {
                Padding = services.IsStandalone ? new Thickness(6,3) : new Thickness(8,6)
            };
            Grid.SetRow(toolsPanel,2);
            _root.Children.Add(toolsPanel);
            _editingControls.Add(tools);

            Control workspaceBody=body;
            if (services.IsStandalone)
            {
                body.Children.Remove(treePanel); body.Children.Remove(viewportPanel); body.Children.Remove(inspectorPanel);
                var dock=services.CreateDockLayout(treePanel,viewportPanel,inspectorPanel,_problems);
                if (dock is not null) workspaceBody=dock;
            }
            Grid.SetRow(workspaceBody,3);
            _root.Children.Add(workspaceBody);
            _editingControls.Add(workspaceBody);
            _editingControls.Add(_path);
            if (!services.IsStandalone) { Grid.SetRow(_problems,4); _root.Children.Add(_problems); }

            Control statusBar = services.IsStandalone ? _status : PrimeChrome.Columns("Auto,*",
                new PrimeBadge("EDITOR STATUS"), _status);
            var statusPanel = new PrimePanel(statusBar)
            {
                Padding = services.IsStandalone ? new Thickness(6,3) : new Thickness(8,5)
            };
            Grid.SetRow(statusPanel,5);
            _root.Children.Add(statusPanel);
            var layer=new Panel();layer.Children.Add(_root);layer.Children.Add(_modal);Content=layer;
            _search.TextChanged+=(_,_)=>RefreshHierarchy(true);
            _hierarchyFilter.SelectionChanged+=(_,_)=>{if(!_refreshing)RefreshHierarchy(true);};
            _hierarchy.SelectionChanged+=(_,selection)=>
            {
                if(_refreshing||_document==null)return;
                if(selection.AddedItems.OfType<HierarchyRow>().Any(row=>row.Header))
                {RefreshHierarchy();return;}
                _document.Selection.Clear();
                foreach(var row in _hierarchy.SelectedItems?.OfType<HierarchyRow>()??Enumerable.Empty<HierarchyRow>())
                    if(row.Object is {} item)_document.Selection.Add(item.Id);
                if(selection.AddedItems.OfType<HierarchyRow>().Select(r=>r.Object).OfType<MapObject>().LastOrDefault() is { } active)
                    _document.ActiveObjectId=active.Id;
                _document.SelectionChanged();ShowInspectorPage(_inspectorPage,false);_viewport?.InvalidateVisual();
            };
            _problems.SelectionChanged+=(_,_)=>
            {
                if(_document!=null&&_problems.SelectedItem is ProblemRow {Diagnostic.ObjectId:Guid id})
                {_document.Selection.Clear();_document.Selection.Add(id);_document.SelectionChanged();RefreshHierarchy();Inspect();_viewport?.FrameSelection();}
            };
            _idle.Interval=TimeSpan.FromMilliseconds(250);
            _idle.Tick+=async(_,_)=>
            {
                if (_diagnostics != null && _inspector.Children.Contains(_diagnostics)) RefreshStatistics();
                if (_detached || _poppedOut) return;
                if (_work == null && !_checking && _document is { } document
                    && document.CurrentStateId != _validatedState
                    && DateTime.UtcNow - document.LastEditUtc > TimeSpan.FromMilliseconds(500))
                {
                    var state = document.CurrentStateId; long generation = _editorGeneration;
                    var snapshot = document.CaptureBuildSnapshot(); _checking = true;
                    try
                    {
                        var result = await Task.Run(() => MapValidator.Validate(snapshot.CreateDefinition(), false));
                        if (!_detached && _document == document && document.CurrentStateId == state && generation == _editorGeneration)
                        {
                            result.Diagnostics.AddRange(_importWarnings);
                            _validatedState = state; document.Diagnostics = result; _viewport?.InvalidateVisual();
                            string signature = string.Join("\n", result.Diagnostics.Select(d => d.ToString()));
                            if (signature != _validationSignature)
                            { _validationSignature = signature; _problems.ItemsSource = result.Diagnostics.Select(d => new ProblemRow(d)).ToArray(); UpdateProblemsVisibility(); }
                        }
                    }
                    catch (Exception ex) { if (!_detached && generation == _editorGeneration) Failure(ex); }
                    finally { _checking = false; }
                }
                if (_autosave.Result is { Error: { } error } saved && !ReferenceEquals(saved,_reportedAutosave))
                {
                    _reportedAutosave=saved;
                    _status.Text = "Autosave failed: " + error + " · Retrying shortly";
                    _autosaved=DateTime.MinValue;
                    _autosaveRetryAfter=DateTime.UtcNow.AddSeconds(10);
                }
                if(DateTime.UtcNow<_autosaveRetryAfter)return;
                if(_document==null||!_document.IsDirty||_document.LastEditUtc<=_autosaved||DateTime.UtcNow-_document.LastEditUtc<TimeSpan.FromSeconds(3))return;
                if (_autosave.Queue(_document.CaptureAutosave(_services.UserMapDirectory))) _autosaved = _document.LastEditUtc;
            };
            AttachedToVisualTree+=(_,_)=>{_detached=false;if (!_services.IsStandalone) _autosave=new();_services.SetAuthoringBackdrop();_idle.Start();
_dropSubscription?.Dispose(); _dropSubscription = _services.SubscribeFilesDropped(OnFilesDropped);
            };
            DetachedFromVisualTree+=(_,_)=>{
_dropSubscription?.Dispose(); _dropSubscription = null;
                if (!_services.IsStandalone) { _detached=true;_editorGeneration++;_idle.Stop();_work?.Cancel();_autosave.Dispose();_sourceWatch?.Dispose();_sourceWatch=null;DisposePreviewCaches(); }};
            RefreshStudioChrome();
            if (preview) Load(MapTemplates.Create("Studio example", true)); else ShowLibrary();
        }
        public void Dispose()
        {
            DisposeAssetThumbnails();
            _detached=true; _editorGeneration++; _idle.Stop(); _work?.Cancel(); _dropSubscription?.Dispose(); _dropSubscription=null; _autosave.Dispose(); _sourceWatch?.Dispose(); _sourceWatch=null;
            if (_document != null) _document.Changed -= Changed;
            ReleaseViews();
            DisposePreviewCaches();
        }
        private void DisposePreviewCaches()
        {
            ClearAnimatedMaterialPreviews();
            foreach(var bitmap in _thumbnailCache.Values)bitmap.Dispose();
            foreach(var preview in _materialPreviewCache.Values)preview.Bitmap.Dispose();
            _thumbnailCache.Clear();_materialPreviewCache.Clear();
        }
        private string PreviewCacheKey(MapDefinition definition,MapMaterial material)
        {
            string stamp="";
            if(material.Texture is {} relative&&definition.BundlePath==null)
            {
                try
                {
                    string root=definition.BaseDirectory??_services.MapLibraryDirectory;
                    string file=Path.GetFullPath(Path.Combine(root,relative));
                    if(File.Exists(file)){var info=new FileInfo(file);stamp=$"|{info.Length}|{info.LastWriteTimeUtc.Ticks}";}
                }
                catch(Exception){ }
            }
            string animation = material.Animation is { } motion
                ? $"|uv:{String.Join(",", motion.UvScroll ?? Array.Empty<float>())}"
                    + $"|rot:{motion.UvRotationDegreesPerSecond:R}"
                    + $"|scale:{String.Join(",", motion.UvScale ?? Array.Empty<float>())}"
                    + $"|pulse:{String.Join(",", motion.UvScalePulse ?? Array.Empty<float>())}"
                    + $"|emit:{motion.EmissiveIntensity:R}|emitPulse:{motion.EmissivePulse:R}"
                    + $"|flip:{String.Join(",", motion.FlipbookFrames ?? new System.Collections.Generic.List<string>())}"
                    + $"|hold:{motion.FlipbookHoldFrames}|loop:{motion.LoopFrames}|phase:{motion.PhaseFrames}"
                : "|uv:off";
            return $"{definition.SourcePath}|{definition.BundlePath}|{definition.TextureSource}|{material.Texture}|{material.Albedo}|{material.Normal}|{material.SpecularRoughness}|{material.Emissive}|{material.SourceMaterial}|{material.TexScale:R}|alpha:{material.Alpha}|two:{material.TwoSided}{animation}{stamp}";
        }
        internal void ShowStatus(string message)=>_status.Text=message;
        private static TextBlock Text(string text)=>new(){Text=text,Foreground=GuiTheme.TextBrush,TextWrapping=TextWrapping.Wrap};
        private static void AddButton(Panel panel,string title,Action action)
        {var button=new PrimeButton(title.ToUpperInvariant(), action) {Margin=new Thickness(2),MinHeight=28,Height=28,MinWidth=60};panel.Children.Add(button);}
        private void Modal(Control control, bool fitContent = false)
        {
            if (_services.ShowModal(control, fitContent, Dismiss)) return;
            _modal.Child=new Border {Background=GuiTheme.PanelBrush,Padding=new Thickness(20),MaxWidth=800,MaxHeight=620,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=control};_modal.IsVisible=true;}
        private void Dismiss(){ _services.DismissModal();  _modal.IsVisible=false;_modal.Child=null;}
        private void Confirm(string message,Action yes)
        {var view=new ConfirmScreen(message);view.Answered+=(_,answer)=>{Dismiss();if(answer)yes();};Modal(view);}
        private void WithUnsaved(Action action)
        {if(_document?.IsDirty==true)Confirm("Discard unsaved changes? A recovery copy will remain available.",()=>{_=PreserveThen(action);});else action();}
        internal bool IsDirty => _document?.IsDirty == true;
        internal bool SaveRecovery()
        {
            try { _autosave.Dispose(); _autosave.Completion.GetAwaiter().GetResult(); if (_document?.IsDirty == true) _document.Autosave(_services.UserMapDirectory); return true; }
            catch (Exception ex) { Failure(ex); return false; }
            finally { _autosave=new(); }
        }
        private async Task PreserveThen(Action action)
        {
            var document = _document; long generation = _editorGeneration;
            if (document == null) { action(); return; }
            // Retire the background writer before creating the final recovery copy.
            // Otherwise an older in-flight snapshot can overwrite the newer one.
            _autosave.Dispose();
            await _autosave.Completion;
            if (_detached || generation != _editorGeneration || document != _document) return;
            using var save = new MapAutosaveService();
            save.Queue(document.CaptureAutosave(_services.UserMapDirectory));
            await save.Completion;
            if (_detached || generation != _editorGeneration || document != _document) return;
            _autosave=new();
            if (save.Result?.Error is { } error) { _status.Text = "Recovery failed: " + error; return; }
            action();
        }
        private void Close()
        {
            if (_poppedOut) { Closed?.Invoke(this, EventArgs.Empty); return; }
            if (_services.IsStandalone) Closed?.Invoke(this, EventArgs.Empty);
            else WithUnsaved(() => Closed?.Invoke(this, EventArgs.Empty));
        }
        internal void Load(MapProject project,string? path=null, bool promptRecovery=true)
        {
            _editorGeneration++; _work?.Cancel(); _autosave.Dispose(); _autosave=new(); _validatedState=null; _validationSignature=null; _autosaved=DateTime.MinValue;
            _lastBuild = null; _hierarchySignature = ""; _pickedMaterialHit=null;
            foreach(var preview in _materialPreviewCache.Values)preview.Bitmap.Dispose();_materialPreviewCache.Clear();
            if(_document!=null)_document.Changed-=Changed;
            _sourceWatch?.Dispose();_sourceWatch=null;_sourceWatchSignature="";_changedSources.Clear();
            _document=new(project,path);_document.Changed+=Changed;
            _importWarnings = project.Definition.Import != null && project.Definition.BaseDirectory is {} importRoot
                ? Q3ImportManifest.Load(importRoot)?.GameplayWarnings?.ToArray() ?? Array.Empty<MapDiagnostic>()
                : Array.Empty<MapDiagnostic>();
            _problems.ItemsSource=null;UpdateProblemsVisibility();
            _gameplayReport=null;_gameplayState=null;_gameplayNavigation=null;
            _studioState=MapStudioStateStore.Load(project.Definition,_services.UserMapDirectory);MapStudioStateStore.Prune(project.Definition,_studioState);
            ReleaseViews();
            _viewport=new(_document);
            _viewport.ModelingError += message => _status.Text=message;
            _views.Add(_viewport);
            SetViewportMode(_viewportMode);
            _viewport.SelectionChanged+=()=>{RefreshHierarchy();ShowInspectorPage(_inspectorPage,false);};
            _viewport.MaterialPicked+=hit=>
            {
                _pickedMaterialHit=hit;_inspectorPage="Materials";
                _status.Text=hit.ObjectId==Guid.Empty
                    ?$"Picked source material {(hit.SourceMaterial>=0?hit.SourceMaterial:hit.Material)}."
                    :$"Picked authored material {hit.Material}.";
                MaterialInspector();
            };
            _viewportHost.Children.Clear();_viewportHost.Children.Add(_viewport);_path.Text=path!=null&&!MapBundle.Is(path)?path:Path.Combine(_services.UserMapDirectory,project.Definition.Name.ToLowerInvariant()+".json");
            Dismiss();Changed();_viewport.FrameAll();
            if(promptRecovery && _document.HasRecovery(_services.UserMapDirectory))Recovery();
            if(project.Definition.Import!=null||project.Definition.NativeRoom!=null)
            {
                // Import completion calls Load from inside the active import
                // Job. Queue the visual preview behind that job so the busy
                // lock is released first. The preview deliberately uses patch
                // detail 1; full Validate still checks the authored setting.
                if(_work==null)_=PreviewImport();
                else Dispatcher.UIThread.Post(()=>_=PreviewImport());
            }
        }
        private void Recovery()
        {
            if(_document==null)return;var view=new StackPanel {Spacing=10};view.Children.Add(Text("A newer recovery file exists."));
            AddButton(view,"Restore",()=>{_document.Restore(_services.UserMapDirectory);Dismiss();});
            AddButton(view,"Discard",()=>{_document.DiscardRecovery(_services.UserMapDirectory);Dismiss();});
            AddButton(view,"Inspect",()=>{_status.Text=File.ReadAllText(_document.RecoveryPath(_services.UserMapDirectory));Dismiss();});Modal(view);
        }
        internal void Open(string path)
        {try{WithUnsaved(()=>{try{Load(MapProjectSerializer.Load(path),path);}catch(Exception ex){Failure(ex);}});}catch(Exception ex){Failure(ex);}}
        private void Save()
        {
            if (_document is null) return;
            if (_services.IsStandalone)
            {
                if (_document.FilePath is null || MapBundle.Is(_document.FilePath)) Browse("Save map project",true,path=>_=SaveNativeAsync(path),".json");
                else _=SaveNativeAsync(_path.Text??_document.FilePath);
            }
            else SaveTo(_path.Text??"");
        }
        private async Task SaveNativeAsync(string path)
        {
            try { await SaveDocumentAsync(path,CancellationToken.None); }
            catch (Exception ex) { Failure(ex); }
        }
        private void SaveTo(string path)
        {
            if(_document==null)return;
            try
            {
                // No old autosave may recreate recovery after a successful manual save.
                _autosave.Dispose();_autosave.Completion.GetAwaiter().GetResult();
                _document.Save(path);_document.DiscardRecovery(_services.UserMapDirectory);
                _path.Text=_document.FilePath;_status.Text="Saved "+_document.FilePath;
                RefreshStudioChrome();
            }
            catch(Exception ex){Failure(ex);}
            finally{_autosave=new();}
        }
        private sealed record ProblemRow(MapDiagnostic Diagnostic){public override string ToString()=>$"{Diagnostic.Severity} · {Diagnostic.Code} · {Diagnostic.Message}";}

        private void FixSelectedProblem()
        {
            if(_document==null||_problems.SelectedItem is not ProblemRow row)
            { _status.Text="Select a validation problem first."; return; }
            var diagnostic=row.Diagnostic;
            try
            {
                if(diagnostic.Code=="FP-MAP-003"&&diagnostic.Message.Contains("Collision",StringComparison.OrdinalIgnoreCase)
                    &&_document.Project.Definition.Import!=null)
                {
                    _document.Edit("Auto-fit imported collision",d=>
                    {
                        var import=d.Import!;
                        import.CollisionPatchLevel=import.CollisionPatchLevel==-1?0:-1;
                    },MapChangeDomain.Import);
                    _status.Text=_document.Project.Definition.Import!.CollisionPatchLevel==0
                        ?"Patch collision disabled; BSP brushes and clips remain solid."
                        :"Patch collision returned to automatic budget fitting.";
                    _=Validate();return;
                }
                if(diagnostic.Code=="FP-MAP-004")
                {
                    _document.Edit("Increase model scale",d=>d.ScaleFactor=Math.Min(15,d.ScaleFactor+1),MapChangeDomain.Metadata);
                    _status.Text="Raised scaleFactor by one to increase the model fixed-point range.";_=Validate();return;
                }
                if(diagnostic.Code=="FP-MAP-006"&&_document.Project.Definition.Import!=null)
                { _=RebakeImportTextures(); return; }
                _status.Text="No safe automatic fix is registered for "+diagnostic.Code+".";
            }
            catch(Exception ex){Failure(ex);}
        }
        private void CapturePreview()
        {
            if(_viewport==null||_document==null||_viewport.Bounds.Width<1||_viewport.Bounds.Height<1)return;
            try
            {
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)_viewport.Bounds.Width,(int)_viewport.Bounds.Height),new Avalonia.Vector(96,96));
                bitmap.Render(_viewport);using var stream=new MemoryStream();bitmap.Save(stream);
                byte[] preview = stream.ToArray();
#if MPHREAD_SHELL
                preview = _viewport.CaptureGpuPreview(preview);
#endif
                StoreAsset("preview",".png",preview);_status.Text="Preview captured.";
            }
            catch(Exception ex){Failure(ex);}
        }
        private void UpdateProblemsVisibility()
        {
            bool visible=_showProblems??(_document?.Diagnostics.Diagnostics.Count>0);
            _problems.IsVisible=visible;
            if (_services.IsStandalone) _services.ShowProblems(visible);
            _root.RowDefinitions[4].Height=new GridLength(!_services.IsStandalone && visible?100:0);
        }
        private void Problems(MapValidationResult result)
        {_importWarnings=result.Diagnostics.Where(d=>d.Code=="FP-MAP-023").ToArray();if(_document!=null)_document.Diagnostics=result;_viewport?.InvalidateVisual();_problems.ItemsSource=result.Diagnostics.Select(d=>new ProblemRow(d)).ToArray();UpdateProblemsVisibility();_status.Text=(result.IsValid?"Validation passed. ":"Build blocked. ")+string.Join(" · ",result.Budgets.Select(b=>$"{b.Name}: {b.Used:N0}"+(b.Limit!=null?$" / {b.Limit:N0}":"")));}
        private void Failure(Exception ex)
        {_status.Text=ex.Message;if(ex is not(IOException or InvalidDataException or ProgramException or FormatException or ArgumentException))DebugLog.Exception("mapeditor",ex);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(_poppedOut){if(e.Key==Key.Escape)Close();e.Handled=true;return;}
            if(_work!=null){if(e.Key==Key.Escape)_work.Cancel();e.Handled=true;return;}
            if(e.Key==Key.Escape){if(_modal.IsVisible)Dismiss();else Close();e.Handled=true;}
            else if((e.KeyModifiers & (KeyModifiers.Control|KeyModifiers.Meta))!=0&&e.KeyModifiers.HasFlag(KeyModifiers.Shift)&&e.Key==Key.P)
            {ShowCommandPalette();e.Handled=true;}
            else if((e.KeyModifiers & (KeyModifiers.Control|KeyModifiers.Meta))!=0&&e.Key==Key.S){Save();e.Handled=true;}
            else if((e.KeyModifiers & (KeyModifiers.Control|KeyModifiers.Meta))!=0&&e.Key==Key.Enter){_=Play();e.Handled=true;}
            else base.OnKeyDown(e);
        }
    }
}
