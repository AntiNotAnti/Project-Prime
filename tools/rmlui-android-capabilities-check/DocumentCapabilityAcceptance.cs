using System.Xml.Linq;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class DocumentCapabilityAcceptance
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string assets = Path.Combine(root, "src/MphRead/Mods/Launcher/RmlUi");
        HashSet<string> Authored(string relative)
        {
            string local = relative.StartsWith("pages/", StringComparison.Ordinal) ? "Pages/" + relative[6..] : relative;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Read(string file)
            {
                if (!visited.Add(file)) return;
                var xml = XDocument.Load(file);
                foreach (var element in xml.Descendants())
                {
                    if (element.Attribute("id") is { } id) ids.Add(id.Value);
                    if (element.Name.LocalName == "link" && (string?)element.Attribute("type") == "text/template"
                        && element.Attribute("href") is { } template)
                        Read(Path.Combine(assets, "Components", Path.GetFileName(template.Value)));
                }
            }
            Read(Path.Combine(assets, local)); return ids;
        }
        var native = new FakeBridge { AuthoredElements = Authored };
        using var host = new RmlUiHost(native);
        check(host.Initialize(640, 360, 1, root, RmlUiRenderBackend.DrawList), "strict authored-binding host initializes");
        var capability = RmlUiLauncherCapabilities.ConfiguredLocalAdventureSaves;
        foreach (var (path, surface, count) in new[] {
            ("pages/home/home.rml", RmlUiLocalDocumentSurface.Home, 12),
            ("pages/offline/setup.rml", RmlUiLocalDocumentSurface.Offline, 6),
            ("pages/adventure/saves.rml", RmlUiLocalDocumentSurface.Adventure, 4),
            ("pages/hunters/local-picker.rml", RmlUiLocalDocumentSurface.Shell, 4),
            ("pages/offline/arena.rml", RmlUiLocalDocumentSurface.OfflineArena, 2) })
        {
            var document = host.OpenDocument(path, surface == RmlUiLocalDocumentSurface.OfflineArena ? RmlUiDocumentLayer.Modal : RmlUiDocumentLayer.Page);
            capability.Apply(host, document, surface);
            int applied = native.Bools.Count(pair => pair.Key.Document == document.DocumentId);
            check(applied == count, "capability binds only real surface-owned selectors: " + surface);
            check(native.Strings.ContainsKey((document.DocumentId, "home_drawer_footer")) == (surface == RmlUiLocalDocumentSurface.Home),
                "Home-only footer never targets another document: " + surface);
            host.CloseDocument(document);
        }
        var adventure = host.OpenDocument("pages/adventure/saves.rml", RmlUiDocumentLayer.Page);
        bool rejected = false;
        try { capability.Apply(host, adventure, RmlUiLocalDocumentSurface.Home); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("visible:drawer_browser", StringComparison.Ordinal); }
        check(rejected, "wrong document surface still fails on missing required native selector");
        rejected = false;
        try { host.SetText(adventure, "home_drawer_footer", "UNOWNED"); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "strict authored fake also rejects missing Home footer rather than ignoring it");
        host.CloseDocument(adventure);
        using var pages = new RmlUiLauncherPages(host, capabilities: capability);
        pages.SetBool("adventure_mode", true); pages.SetBool("home_mode", false);
        pages.Flush(); host.Update(); pages.AfterUpdate();
        check(pages.Page == RmlUiMenuPage.Adventure, "actual first Adventure Flush passes strict native selector membership");
        pages.ShowBaseline(RmlUiMenuPage.Offline);
        pages.SetBool("offline_arena_open", true);
        pages.Flush(); host.Update(); pages.AfterUpdate();
        var arena = pages.Manager.Top;
        check(arena != pages.Document && native.Bools.GetValueOrDefault((arena.DocumentId, "visible:offline_arena_search"), 1) == 0
            && !native.Bools.ContainsKey((arena.DocumentId, "visible:nav_community")),
            "composed Offline arena receives only its dialog-owned restrictions");
        pages.SetBool("offline_arena_open", false); pages.Flush();
        var hunter = pages.Manager.OpenPage(new("local-hunter", "pages/hunters/local-picker.rml", "local_hunter0"));
        pages.PresentChrome(hunter); host.Update();
        check(native.Bools.GetValueOrDefault((hunter.DocumentId, "visible:nav_studio"), 1) == 0
            && !native.Strings.ContainsKey((hunter.DocumentId, "home_drawer_footer")), "custom Hunter chrome applies only common shell capability selectors");
    }
}
