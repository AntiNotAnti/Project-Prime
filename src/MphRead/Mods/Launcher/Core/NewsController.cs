using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.Launcher.Core;

public enum NewsFilter { All, News, PatchNotes, Announcements }
public enum NewsDialog { None, Transmission, DiscordFallback }
public interface INewsLinkLauncher { bool Open(string uri); }
public sealed record NewsRow(int SourceIndex, NewsDispatch Article);
public sealed record NewsSnapshot(Guid Lifetime, long Revision, NewsFilter Filter,
    ImmutableArray<NewsRow> Rows, int SelectedIndex, NewsDispatch? Selected, NewsDialog Dialog, string Error);

/// <summary>The same bundled dispatch authority serves both presentations.
/// The controller owns only filtering, selection and the current local dialog.</summary>
public sealed class NewsController : IDisposable
{
    public const string DiscordUrl = "https://discord.gg/qKp2M8kHd6";
    private readonly ImmutableArray<NewsDispatch> _articles;
    private readonly INewsLinkLauncher _links;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private NewsSnapshot _snapshot;
    private bool _disposed;
    public NewsController(INewsProvider provider, INewsLinkLauncher links)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        var articles = (provider ?? throw new ArgumentNullException(nameof(provider))).Read();
        if (articles == null || articles.Count > 4 || articles.Any(a => a == null))
            throw new ArgumentException("The bundled launcher feed supports up to four dispatches.", nameof(provider));
        _articles = articles.Select(a => a with { }).ToImmutableArray();
        _snapshot = new(Guid.NewGuid(), 0, NewsFilter.All, ImmutableArray<NewsRow>.Empty, -1, null, NewsDialog.None, "");
        Filter(NewsFilter.All);
    }
    public NewsSnapshot Snapshot() { Verify(); return _snapshot; }
    public bool Filter(NewsFilter filter)
    {
        Verify(); if (!Enum.IsDefined(filter)) return false;
        string category = filter switch { NewsFilter.News => "NEWS", NewsFilter.PatchNotes => "PATCH NOTES", NewsFilter.Announcements => "ANNOUNCEMENTS", _ => "" };
        var rows = _articles.Select((a,i)=>new NewsRow(i,a)).Where(r=>category.Length==0||r.Article.Category==category).ToImmutableArray();
        Set(_snapshot with { Filter=filter,Rows=rows,SelectedIndex=rows.IsEmpty?-1:0,Selected=rows.IsEmpty?null:rows[0].Article,Dialog=NewsDialog.None,Error="" }); return true;
    }
    public bool Select(int index)
    {
        Verify(); if (index < 0 || index >= _snapshot.Rows.Length || _snapshot.Dialog != NewsDialog.None) return false;
        Set(_snapshot with { SelectedIndex=index,Selected=_snapshot.Rows[index].Article,Error="" });return true;
    }
    public bool OpenTransmission()
    {
        Verify(); if (_snapshot.Selected == null || _snapshot.Dialog != NewsDialog.None) return false;
        Set(_snapshot with { Dialog=NewsDialog.Transmission,Error="" }); return true;
    }
    public bool OpenDiscord()
    {
        Verify(); if (_snapshot.Dialog != NewsDialog.None) return false;
        // The immutable project invite is the only URI this adapter can open.
        if (!Uri.TryCreate(DiscordUrl,UriKind.Absolute,out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "discord.gg")
            throw new InvalidOperationException("The bundled community invite is invalid.");
        bool opened=false;
        try { opened=_links.Open(DiscordUrl); } catch { /* Display the same recoverable fallback as the legacy shell. */ }
        Set(_snapshot with { Dialog=opened?NewsDialog.None:NewsDialog.DiscordFallback,
            Error=opened?"":"The browser could not open. Use the Discord invite below." });return true;
    }
    public bool CloseDialog()
    {
        Verify(); if (_snapshot.Dialog == NewsDialog.None) return false;
        Set(_snapshot with { Dialog=NewsDialog.None,Error="" }); return true;
    }
    private void Set(NewsSnapshot snapshot) => _snapshot = snapshot with { Revision=_snapshot.Revision+1 };
    private void Verify() { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("News belongs to its owner thread."); ObjectDisposedException.ThrowIf(_disposed,this); }
    public void Dispose() { if (_disposed) return; Verify(); _disposed=true; }
}
