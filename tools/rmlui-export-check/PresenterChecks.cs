using System.Text.RegularExpressions;
using System.Xml.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

internal static class PresenterChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Actual export presenter: " + message); checks++; }
        var backend = new MemoryExports(); var host = new RmlUiHost(); var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, backend);
        Check(page.Send(TheatrePlaybackAction.ExportOpen) && pages.Top != page.Document, "actual presenter opens foreground export modal with injected backend");
        var panel = pages.Top;
        Check(page.Send(TheatrePlaybackAction.ExportGameHud) && page.Send(TheatrePlaybackAction.ExportQueue)
            && backend.Accepted.Single().Options.GameHud, "actual presenter delegates reviewed native options and enqueue");
        Check(!page.Repeat(), "actual presenter rejects duplicate accepted intent sequence");
        Check(page.Send(TheatrePlaybackAction.ExportClearPending) && pages.Top != panel, "actual presenter opens queue confirmation before clearing");
        var confirmation = pages.Top; backend.AddPending(); page.RefreshFixture();
        Check(host.Flags[(confirmation, "disabled:replay_export_confirm_apply")], "actual presenter disables changed pending-set authority");
        Check(page.Send(TheatrePlaybackAction.ExportConfirm) && backend.Pending.Length == 2 && pages.Top == confirmation,
            "stale confirmation retains review UI and cannot clear new work");
        Check(!page.SendFrom(TheatrePlaybackAction.ExportConfirm, panel), "background document cannot confirm queue operation");
        Check(page.Send(TheatrePlaybackAction.ExportCancelConfirm) && pages.Top == panel,
            "actual presenter dismisses confirmation before modal");
        Check(page.Send(TheatrePlaybackAction.ExportClearPending) && page.Send(TheatrePlaybackAction.ExportConfirm)
            && backend.Pending.IsEmpty && pages.Top == panel, "actual presenter completes fresh exact clear once");
        backend.AddPending(); int accepted = backend.Pending.Length;
        Check(page.Send(TheatrePlaybackAction.ExportClose) && pages.Top == page.Document && backend.Pending.Length == accepted,
            "actual presenter close retires editor without clearing accepted jobs");
        Check(!page.SendFrom(TheatrePlaybackAction.ExportQueue, panel), "retired modal cannot enqueue another export");
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "src/MphRead/Mods/Launcher/RmlUi/Pages/theatre/export.rml")))
            root = Directory.GetParent(root)?.FullName ?? throw new IOException("Repository source unavailable.");
        string folder = Path.Combine(root, "src/MphRead/Mods/Launcher/RmlUi/Pages/theatre");
        var assets = new[] { XDocument.Load(Path.Combine(folder, "export.rml")), XDocument.Load(Path.Combine(folder, "export-confirm.rml")) };
        var ids = assets.SelectMany(asset => asset.Descendants()).Attributes("id").Select(value => value.Value).ToHashSet();
        string presenter = File.ReadAllText(Path.Combine(folder, "TheatrePlaybackPagePresenter.Exports.cs"));
        foreach (Match match in Regex.Matches(presenter, "\"(replay_export_[a-z_]+)\""))
            Check(ids.Contains(match.Groups[1].Value), "native export binding exists in authored RML: " + match.Groups[1].Value);
        foreach (string action in assets.SelectMany(asset => asset.Descendants()).Attributes("data-action").Select(value => value.Value))
            Check(RmlUiIntentRegistry.TryParseLegacy(action, panel, 1, out var intent)
                && intent.Kind == RmlUiIntentKind.ReplayAction && intent.Argument is >= 48 and <= 67,
                "actual authored export action maps to reviewed appended Replay family: " + action);
        Check((int)TheatrePlaybackAction.CameraTrack == 47 && (int)TheatrePlaybackAction.ExportOpen == 48
            && (int)TheatrePlaybackAction.ExportOpenOutput == 67, "old camera actions remain unchanged while export appends48 through67");
        Console.WriteLine($"PASS {checks} actual source-linked export presenter/asset/intent checks; native host seam only");
    }
}

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public sealed record RmlUiPageSpec(string Id, string Path, string Focus);
    public sealed class RmlUiHost
    {
        internal readonly HashSet<RmlUiDocumentToken> Alive = new();
        internal readonly Dictionary<(RmlUiDocumentToken, string), string> Text = new();
        internal readonly Dictionary<(RmlUiDocumentToken, string), bool> Flags = new();
        public bool IsAlive(RmlUiDocumentToken token) => Alive.Contains(token);
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
        public TheatrePlaybackPagePresenter(RmlUiHost host, RmlUiPageManager pages, ITheatreExportBackend backend)
        { _host = host; _pages = pages; _exportBackend = backend; }
        public bool Send(TheatrePlaybackAction action) => SendFrom(action, _pages.Top);
        public bool SendFrom(TheatrePlaybackAction action, RmlUiDocumentToken document)
        { _last = new(RmlUiIntentKind.ReplayAction, (int)action, document, ++_next); return HandleExportIntent(_last); }
        public bool Repeat() => HandleExportIntent(_last);
        public void RefreshFixture() => PresentExports();
        public void Dispose() { CloseExports(); _disposed = true; }
        private sealed class StubViewport { public void Release() { } }
    }
}
