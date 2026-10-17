using System;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;

static void Check(bool pass, string label)
{
    if (!pass) throw new InvalidOperationException(label);
    Console.WriteLine("RML NEWS PASS " + label);
}
var links = new StubLinks();
using var controller = new NewsController(new BundledNewsProvider(), links);
var initial = controller.Snapshot();
Check(initial.Filter == NewsFilter.All && initial.Rows.Length == 4
    && initial.Selected?.Category == "NEWS" && initial.Dialog == NewsDialog.None,
    "RmlUi and Avalonia share four immutable bundled articles");
Check(controller.Filter(NewsFilter.PatchNotes)
    && controller.Snapshot().Rows.Length == 1
    && controller.Snapshot().Selected?.Category == "PATCH NOTES",
    "category filter uses the shared source without duplicate news");
Check(!controller.Select(4) && controller.Select(0),
    "visible article bounds are checked");
long oldRevision = controller.Snapshot().Revision;
Check(controller.OpenTransmission()
    && controller.Snapshot().Dialog == NewsDialog.Transmission
    && controller.Snapshot().Revision > oldRevision
    && !controller.Select(0),
    "opening a native detail dialog fences underlying selection");
Check(controller.CloseDialog() && controller.Snapshot().Dialog == NewsDialog.None,
    "native detail dialog closes and restores the source document");
Check(controller.Filter(NewsFilter.Announcements)
    && controller.Snapshot().Selected?.Title.Contains("DISCORD", StringComparison.Ordinal) == true,
    "announcements share current launcher news");
Check(controller.OpenDiscord() && links.LastUri == NewsController.DiscordUrl
    && links.Calls == 1 && controller.Snapshot().Dialog == NewsDialog.DiscordFallback
    && controller.Snapshot().Error.Length > 0,
    "only the canonical HTTPS invite is opened and failure becomes a safe modal");
Check(controller.CloseDialog()
    && controller.Filter(NewsFilter.All)
    && controller.Snapshot().Rows.Length == 4,
    "fallback closes without losing the complete feed");
Exception? crossThread = null;
Task.Run(() =>
{
    try { controller.Filter(NewsFilter.News); }
    catch (Exception ex) { crossThread = ex; }
}).GetAwaiter().GetResult();
Check(crossThread is InvalidOperationException,
    "news actions are restricted to native owner thread");
Console.WriteLine("RML NEWS complete.");
sealed class StubLinks : INewsLinkLauncher
{
    public string LastUri = "";
    public int Calls;
    public bool Open(string uri) { LastUri = uri; Calls++; return false; }
}
