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
        public string ReadField(RmlUiDocumentToken token,string id)=>"REVIEWED NAME";
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
        private bool WorkspaceMode=>false;
        private string _workspaceTab="markers";
        private bool _workspaceClosing=false;
        private void SelectWorkspaceTab(string tab){}
        private void EnsureWorkspaceOwners(){}
        private bool WorkspaceTrackVisible(string name)=>true;
        private uint WorkspaceTimelineFrame(uint reviewed)=>reviewed;

        private readonly RmlUiHost _host;
        private readonly RmlUiPageManager _pages;
        private readonly RmlUiDocumentToken _document = new(1, 1);
        private ulong _next;
        private RmlUiIntent _last;
        private bool _disposed;
        private StubViewport Viewport { get; } = new();
        public RmlUiDocumentToken Document => _document;
        public bool IsOpen => !_disposed;
        public TheatrePlaybackPagePresenter(RmlUiHost host, RmlUiPageManager pages, ITheatreEditorBackend backend)
        { _host = host; _pages = pages; _editorBackend = backend; }
        public bool Send(TheatrePlaybackAction action) => SendFrom(action, _pages.Top);
        public bool SendFrom(TheatrePlaybackAction action, RmlUiDocumentToken document)
        { _last = new(RmlUiIntentKind.ReplayAction, (int)action, document, ++_next); return HandleEditorIntent(_last); }
        public bool Repeat() => HandleEditorIntent(_last);
        public void RefreshFixture() => PresentEditor();
        public void Dispose() { CloseEditor(); _disposed = true; }
        private sealed class StubViewport { public void Release() { } }
    }
}
