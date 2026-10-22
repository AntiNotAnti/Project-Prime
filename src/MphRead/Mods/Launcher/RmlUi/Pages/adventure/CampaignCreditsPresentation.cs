#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

// One ending-document lifetime. No save/movie ownership, global asset cache,
// second renderer, worker-native cleanup or per-frame source IO.
internal sealed class CampaignCreditsPresentation : IDisposable
{
    internal enum Phase { Loading, Credits, Results, Fallback, Retired }
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly RmlUiDocumentToken _document;
    private readonly CampaignEndingSummary _summary;
    private readonly Func<bool> _receiptCurrent;
    private readonly Action<bool> _show;
    private readonly CampaignCreditsClock _clock = new();
    private readonly Dictionary<int, (int Item, int Action)> _acknowledgements = new();
    private ICampaignCreditsLoadJob? _job;
    private CampaignCreditsTimeline? _timeline;
    private CampaignCreditsFrameOwner? _frames;
    private CampaignCreditsCanvasAdapter? _adapter;
    private RmlUiCanvas? _canvas;
    private bool _returned, _disposed;
    internal Phase State { get; private set; } = Phase.Loading;
    internal bool CanAcknowledge { get; private set; }
    internal int SourcePage => _timeline?.Snapshot().Page ?? -1;

    internal CampaignCreditsPresentation(RmlUiHost host, RmlUiPageManager pages, RmlUiDocumentToken document,
        CampaignEndingSummary summary, Func<bool> receiptCurrent, Action<bool> show,
        Func<ICampaignCreditsLoadJob?> load)
    {
        _host = host; _pages = pages; _document = document; _summary = summary;
        _receiptCurrent = receiptCurrent; _show = show;
        try { _job = load(); if (_job == null) FallBack(); }
        catch (Exception e) when (Recoverable(e)) { FallBack(); }
    }

    internal void Pump(double now, bool active)
    {
        _host.VerifyOwnerThread();
        if (_disposed || State is Phase.Fallback or Phase.Retired) return;
        if (!_host.IsAlive(_document) || !_receiptCurrent() || _job?.Current != true)
        {
            Retire();
            if (_host.IsAlive(_document)) _show(false);
            return;
        }
        bool foreground = _pages.Top == _document && _host.IsVisible(_document)
            && _host.CurrentInputDocument == _document;
        if (State == Phase.Loading)
        {
            _clock.Advance(now, false);
            if (!active || !foreground || !_job.Completion.IsCompleted) return;
            try
            {
                var catalog = _job.Completion.GetAwaiter().GetResult();
                CampaignCreditsCanvasAdapter.Admit(catalog, _summary);
                DiscoverAcknowledgements(catalog);
                _timeline = new(catalog.Document, catalog.Frames, _summary.Kind == CampaignEndingKind.Complete);
                _frames = new(catalog, _timeline, _summary);
                _adapter = new(catalog, _summary, _frames);
                _show(true); _host.Update();
                _canvas = _host.AcquireCanvas(_document, "campaign_ending_source_canvas");
                if (_canvas == null || !_canvas.SetTextures(_adapter.Table) || !_canvas.Submit(_adapter.Build(_frames.Capture())))
                    throw new InvalidDataException("Credits canvas admission failed.");
                State = Phase.Credits; _clock.Reset(); _clock.Advance(now, true);
            }
            catch (Exception e) when (Recoverable(e)) { FallBack(); }
            return;
        }
        try
        {
            if (_canvas == null || !_canvas.TryState(out var native) || (native.Flags & 1) != 0)
            { FallBack(); return; }
            int steps = _clock.Advance(now, active && foreground);
            for (int i = 0; i < steps; i++) _timeline!.Tick();
            if (steps == 0) return;
            var state = _timeline!.Snapshot();
            if (state.Failure != null) { FallBack(); return; }
            if (_timeline.TryTakeReturn()) { _returned = true; Retire(); return; }
            State = state.Page is 49 or 50 ? Phase.Results : Phase.Credits;
            CanAcknowledge = state.PendingPage == null && _acknowledgements.TryGetValue(state.Page, out var ack)
                && state.Items[ack.Item].Started && state.Items[ack.Item].State != 1;
            if (steps > 0 && !_canvas.Submit(_adapter!.Build(_frames!.Capture()))) FallBack();
        }
        catch (Exception e) when (Recoverable(e)) { FallBack(); }
    }
    private void DiscoverAcknowledgements(CampaignCreditsCatalog catalog)
    {
        foreach (var page in catalog.Document.Pages)
        {
            if (page.Index is not (49 or 50)) continue;
            foreach (var item in page.Items)
                for (int i = 0; i < item.Actions.Count; i++)
                {
                    var action = item.Actions[i];
                    if (action.Kind != 1) continue;
                    bool returns = false;
                    foreach (var call in action.Calls) returns |= call.B == 206;
                    if (returns && !_acknowledgements.TryAdd(page.Index, (item.Index, i)))
                        throw new InvalidDataException("Credits results acknowledgement is ambiguous.");
                }
        }
        if (_acknowledgements.Count != 2) throw new InvalidDataException("Credits results acknowledgement is unavailable.");
    }
    internal void Suspend() { _host.VerifyOwnerThread(); _clock.Reset(); }
    internal bool Acknowledge()
    {
        _host.VerifyOwnerThread();
        if (_disposed || !CanAcknowledge || _pages.Top != _document || !_host.IsVisible(_document)
            || _host.CurrentInputDocument != _document
            || !_receiptCurrent() || _job?.Current != true || !_acknowledgements.TryGetValue(SourcePage, out var ack)) return false;
        bool accepted = _timeline!.InvokeItemAction(SourcePage, ack.Item, ack.Action);
        CanAcknowledge = false;
        if (_timeline.TryTakeReturn()) { _returned = true; Retire(); }
        return accepted;
    }
    internal bool TryTakeReturn() { _host.VerifyOwnerThread(); bool result = _returned; _returned = false; return result; }
    private void FallBack()
    {
        Release(); State = Phase.Fallback;
        if (_host.IsAlive(_document)) _show(false);
    }
    private void Retire() { Release(); State = Phase.Retired; }
    private void Release()
    {
        CanAcknowledge = false; _clock.Reset();
        _job?.Dispose(); _job = null;
        _timeline?.Retire(); _canvas?.Dispose(); _canvas = null;
        _frames?.Dispose(); _frames = null; _adapter = null;
        _timeline?.Dispose(); _timeline = null;
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return;
        _disposed = true; _returned = false; Retire();
    }
    private static bool Recoverable(Exception e) => e is IOException or InvalidDataException or ArgumentException
        or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or OperationCanceledException;
}
#endif
