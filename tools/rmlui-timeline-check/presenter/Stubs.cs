using MphRead.Mods.Launcher.Core; using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public sealed record RmlUiPageSpec(string Id, string Path, string Focus);
    public sealed class RmlUiHost
    {
        internal readonly HashSet<RmlUiDocumentToken> Alive = new();
        internal readonly Dictionary<(RmlUiDocumentToken, string), string> Text = new();
        internal readonly Dictionary<(RmlUiDocumentToken, string), bool> Flags = new();
        internal RmlUiDocumentToken Current;internal string Focus="";
        internal float ScrollY;internal readonly HashSet<string> Hidden=new();
        public RmlUiDocumentToken CurrentInputDocument=>Current;
        public string FocusedElement()=>Focus;
        public bool FocusDocument(RmlUiDocumentToken token,string id){if(!IsAlive(token)||Flags.GetValueOrDefault((token,"disabled:"+id)))return false;Focus=id;return true;}
        public bool TryGetElementBounds(RmlUiDocumentToken token,string id,out float x,out float y,out float w,out float h)
        {
            (x,y,w,h)=(0,0,0,0);if(!IsAlive(token)||Hidden.Contains(id)||Flags.TryGetValue((token,id),out bool visible)&&!visible)return false;
            if(id=="replay_camera_tools_panel"){(x,y,w,h)=(0,0,1280,720);return true;}
            if(id.StartsWith("replay_camera_curve_lane_")&&int.TryParse(id["replay_camera_curve_lane_".Length..],out int curveLane)){(x,y,w,h)=(20,100+curveLane*80-ScrollY,600,64);return true;}
            if(id=="replay_timeline_panel"){(x,y,w,h)=(0,0,1280,720);return true;}
            if(id=="replay_timeline_seek"){(x,y,w,h)=(20,400-ScrollY,120,24);return true;}
            string[] lanes={"camera","events","bookmarks","ranges","reel"};
            for(int kind=0;kind<lanes.Length;kind++)if(id=="replay_timeline_"+lanes[kind]){(x,y,w,h)=(20,100+kind*40-ScrollY,600,24);return true;}
            if(!Text.TryGetValue((token,"rect:"+id),out string? rect))return false;
            var parts=rect.Split(',').Select(s=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if(id.StartsWith("replay_camera_curve_"))
            {var names=id.Split('_');int channel=int.Parse(names[4]);(x,y,w,h)=(20+parts[0],100+channel*80+parts[1]-ScrollY,parts[2],parts[3]);return true;}
            int lane=id.StartsWith("replay_timeline_key_")||id.StartsWith("replay_timeline_ghost_")||id=="replay_timeline_playhead"?0:
                id.StartsWith("replay_timeline_events_")?1:id.StartsWith("replay_timeline_bookmarks_")?2:id.StartsWith("replay_timeline_ranges_")?3:4;
            (x,y,w,h)=(20+parts[0],100+lane*40+parts[1]-ScrollY,parts[2],parts[3]);return true;
        }
        public bool IsAlive(RmlUiDocumentToken token) => Alive.Contains(token);
        public void SetField(RmlUiDocumentToken token,string id,string value)=>Text[(token,id)]=value;
        public string ReadField(RmlUiDocumentToken token,string id)=>Text.GetValueOrDefault((token,id),"100");
        public void SetText(RmlUiDocumentToken token, string id, string value) => Text[(token, id)] = value;
        public void SetBool(RmlUiDocumentToken token, string id, bool value) => Flags[(token, id)] = value;
    }
    public sealed class RmlUiPageManager
    {
        private readonly RmlUiHost _host;
        private readonly Stack<RmlUiDocumentToken> _stack = new();
        private readonly Dictionary<RmlUiDocumentToken, ulong> _sequence = new();
        private ulong _next = 1;
        public RmlUiPageManager(RmlUiHost host) { _host = host; var root = new RmlUiDocumentToken(1, 1); _stack.Push(root); host.Alive.Add(root);host.Current=root; }
        public RmlUiDocumentToken Top => _stack.Peek();
        public RmlUiDocumentToken OpenModal(RmlUiPageSpec spec)
        { var token = new RmlUiDocumentToken(1, ++_next); _stack.Push(token); _host.Alive.Add(token);_host.Current=token;_host.Focus=spec.Focus; return token; }
        public bool CloseModal()
        { if (_stack.Count < 2) return false; _host.Alive.Remove(_stack.Pop());_host.Current=Top; return true; }
        public bool Accept(in RmlUiIntent intent)
        {
            if (intent.Document != Top || !_host.IsAlive(intent.Document)
                || _sequence.GetValueOrDefault(intent.Document) >= intent.Sequence) return false;
            _sequence[intent.Document] = intent.Sequence; return true;
        }
    }
}
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre
{
    public sealed partial class TheatrePlaybackPagePresenter : IDisposable
    {
        private bool WorkspaceMode=>false;
        private string _workspaceTab="camera";
        private bool _workspaceClosing=false;
        private void SelectWorkspaceTab(string tab){}
        private void EnsureWorkspaceOwners(){}
        private bool WorkspaceTrackVisible(string name)=>true;
        private uint WorkspaceTimelineFrame(uint reviewed)=>reviewed;

        private readonly RmlUiHost _host;
        private readonly RmlUiPageManager _pages;
        private readonly ITheatreCameraBackend _cameraBackend;
        private readonly ITheatreEditorBackend _editorBackend;
        private readonly RmlUiDocumentToken _document = new(1, 1);
        private ulong _next;
        private RmlUiIntent _last;
        private bool _disposed;
        private RmlUiDocumentToken _cameraModal=default;
        private static string CameraPose(TheatreCameraPose pose)=>$"{pose.X},{pose.Y},{pose.Z} // {pose.Fov}";
        private StubViewport Viewport { get; } = new();
        public RmlUiDocumentToken Document => _document;
        public bool IsOpen => !_disposed;
        public TheatrePlaybackPagePresenter(RmlUiHost host, RmlUiPageManager pages, ITheatreCameraBackend backend)
        { _host = host; _pages = pages; _cameraBackend = backend;_editorBackend=(ITheatreEditorBackend)backend; }
        public bool Send(TheatrePlaybackAction action) => SendFrom(action, _pages.Top);
        public bool SendFrom(TheatrePlaybackAction action, RmlUiDocumentToken document)
        { _last = new(RmlUiIntentKind.ReplayAction, (int)action, document, ++_next); return action is TheatrePlaybackAction.CameraToolsCurveKey or TheatrePlaybackAction.CameraToolsHandles or TheatrePlaybackAction.CameraToolsTransform?HandleCameraToolsIntent(_last):(int)action>=(int)TheatrePlaybackAction.TimelineOpen||_pages.Top==_timelineModal?HandleTimelineIntent(_last):HandleCameraToolsIntent(_last); }
        internal TheatreCameraToolsSnapshot CameraState=>_cameraTools!.Snapshot();
        public bool Repeat() => HandleCameraToolsIntent(_last);
        public void RefreshFixture(){PresentCameraTools();PresentTimeline();}
        internal bool Paint(TimelineMetrics metrics){RefreshFixture();var ticket=PrepareTimeline(metrics,true);return ticket!=null&&TimelinePresented(ticket,metrics,true);}
        internal bool BackFixture()=>BackTimeline();
        internal void SetFocus(string id)=>_host.FocusDocument(_pages.Top,id);
        public void Dispose() { CloseTimeline();CloseCameraTools(); _disposed = true; }
        private sealed class StubViewport { public void Release() { } }
    }
}
