using MphRead.Mods.Launcher.Core; using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public sealed record RmlUiPageSpec(string Id, string Path, string Focus);
    public sealed class RmlUiHost
    {
        internal readonly HashSet<RmlUiDocumentToken> Alive = new();
        internal readonly Dictionary<(RmlUiDocumentToken, string), string> Text = new();
        internal readonly Dictionary<(RmlUiDocumentToken, string), bool> Flags = new();
        public bool IsAlive(RmlUiDocumentToken token) => Alive.Contains(token);
        public void SetField(RmlUiDocumentToken token,string id,string value){}
        public string ReadField(RmlUiDocumentToken token,string id)=>"100";
        public void SetText(RmlUiDocumentToken token, string id, string value) => Text[(token, id)] = value;
        public void SetBool(RmlUiDocumentToken token, string id, bool value) => Flags[(token, id)] = value;
    }
    public sealed class RmlUiPageManager
    {
        private readonly RmlUiHost _host;
        private readonly Stack<RmlUiDocumentToken> _stack = new();
        private readonly Dictionary<RmlUiDocumentToken, ulong> _sequence = new();
        private ulong _next = 1;
        public RmlUiPageManager(RmlUiHost host) { _host = host; var root = new RmlUiDocumentToken(1, 1); _stack.Push(root); host.Alive.Add(root); }
        public RmlUiDocumentToken Top => _stack.Peek();
        public RmlUiDocumentToken OpenModal(RmlUiPageSpec spec)
        { var token = new RmlUiDocumentToken(1, ++_next); _stack.Push(token); _host.Alive.Add(token); return token; }
        public bool CloseModal()
        { if (_stack.Count < 2) return false; _host.Alive.Remove(_stack.Pop()); return true; }
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
        // This modal-only fixture deliberately excludes the separately tested workspace/timeline UI.
        private bool TimelineOpen=>false;
        private string _timelinePublicationNotice { get; set; }="";
        private object? _editorBackend=>null;
        private bool WorkspaceMode=>false;
        private bool _workspaceClosing=false;
        private void SelectWorkspaceTab(string tab){}
        private void EnsureWorkspaceOwners(){}
        private bool WorkspaceTrackVisible(string name)=>true;
        private uint WorkspaceTimelineFrame(uint reviewed)=>reviewed;

        private readonly RmlUiHost _host;
        private readonly RmlUiPageManager _pages;
        private readonly ITheatreCameraBackend _cameraBackend;
        private readonly RmlUiDocumentToken _document = new(1, 1);
        private ulong _next;
        private RmlUiIntent _last;
        private bool _disposed;
        private RmlUiDocumentToken _cameraModal=default;
        // This legacy controller fixture does not emulate graphical curve layout/input.
        private readonly (float Min,float Max)[] _curveRanges=new(float,float)[5];
        private static float CurveValue(TheatreCameraPose pose,int channel)=>channel switch{0=>pose.X,1=>pose.Y,2=>pose.Z,3=>pose.Fov,_=>pose.Roll};
        private void ReleaseCameraCurveInput(){}
        private void ActivateCameraCurveKey(){}
        private void PresentCameraCurveKeys(TheatreCameraToolsSnapshot state){}
        private static string CameraPose(TheatreCameraPose pose)=>$"{pose.X},{pose.Y},{pose.Z} // {pose.Fov}";
        private StubViewport Viewport { get; } = new();
        public RmlUiDocumentToken Document => _document;
        public bool IsOpen => !_disposed;
        public TheatrePlaybackPagePresenter(RmlUiHost host, RmlUiPageManager pages, ITheatreCameraBackend backend)
        { _host = host; _pages = pages; _cameraBackend = backend; }
        public bool Send(TheatrePlaybackAction action) => SendFrom(action, _pages.Top);
        public bool SendFrom(TheatrePlaybackAction action, RmlUiDocumentToken document)
        { _last = new(RmlUiIntentKind.ReplayAction, (int)action, document, ++_next); return HandleCameraToolsIntent(_last); }
        public bool Repeat() => HandleCameraToolsIntent(_last);
        public void RefreshFixture() => PresentCameraTools();
        public void Dispose() { CloseCameraTools(); _disposed = true; }
        private sealed class StubViewport { public void Release() { } }
    }
}
