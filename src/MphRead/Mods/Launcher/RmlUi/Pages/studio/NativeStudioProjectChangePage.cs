#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

internal sealed class NativeStudioProjectChangePage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly INativeStudioProjectChanges _changes;
    private RmlUiDocumentToken _document;
    private Guid _ticket;
    private CancellationTokenRegistration _retirement;
    private bool _open, _disposed, _taken;
    private long _revision;
    private string? _pendingFocus;
    private NativeStudioChangeSnapshot? _shown;
    public bool IsOpen => _open && !_disposed;
    internal RmlUiDocumentToken Document => _document;
    public NativeStudioProjectChangePage(RmlUiHost host, RmlUiPageManager pages, INativeStudioProjectChanges changes)
    { _host = host; _pages = pages; _changes = changes; }
    public bool Open()
    {
        var state = _changes.CaptureChange(); if (state == null) return false;
        _ticket = state.Ticket;
        _document = _pages.OpenModal(new("studio-project-change", "pages/studio/project-change.rml", "native_change_cancel"));
        _open = true;
        _retirement = _pages.Lifetime(_document).Register(() => { _disposed = true; _open = false; if (!_taken) _changes.CancelChange(_ticket); });
        _host.SetField(_document, "native_change_path", state.SavePath); Refresh(); return true;
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!Foreground || intent.Kind != RmlUiIntentKind.StudioAction || intent.Document != _document || !_pages.Accept(intent)) return false;
        NativeStudioChangeDecision decision;
        switch(intent.Argument)
        {
            case 55: decision = NativeStudioChangeDecision.Save; break;
            case 56: decision = NativeStudioChangeDecision.Preserve; break;
            case 57: return Back();
            case 58: decision = NativeStudioChangeDecision.Overwrite; break;
            case 59: decision = NativeStudioChangeDecision.KeepFile; break;
            default: return false;
        }
        _changes.ResolveChange(_ticket, decision, _host.ReadField(_document, "native_change_path", 32767)); Refresh(); return true;
    }
    private bool Foreground => IsOpen && _pages.Top == _document && _host.CurrentInputDocument == _document;
    public void Refresh()
    {
        if (!Foreground) return;
        if (_pendingFocus != null && _host.FocusDocument(_document, _pendingFocus)) _pendingFocus = null;
        var state = _changes.CaptureChange();
        if (state == null || state.Ticket != _ticket) { _open = false; return; }
        if (state == _shown) return;
        if (_shown?.Stage != state.Stage)
            _pendingFocus = state.Stage switch { NativeStudioChangeStage.NeedsOverwrite => "native_change_overwrite", NativeStudioChangeStage.NeedsDecision => "native_change_path", _ => null };
        _shown = state;
        _pages.Present(_document, ++_revision, new Dictionary<string,RmlUiBindingValue> {
            ["native_change_current"] = RmlUiBindingValue.FromText("CURRENT: " + state.CurrentName),
            ["native_change_target"] = RmlUiBindingValue.FromText("OPEN: " + state.TargetName),
            ["native_change_status"] = RmlUiBindingValue.FromText(state.StatusText),
            ["native_change_review_name"] = RmlUiBindingValue.FromText(state.ReviewedFileName),
            ["native_change_review_path"] = RmlUiBindingValue.FromText(state.SavePath),
            ["visible:native_change_decision"] = RmlUiBindingValue.FromBoolean(state.Stage == NativeStudioChangeStage.NeedsDecision),
            ["visible:native_change_overwrite_panel"] = RmlUiBindingValue.FromBoolean(state.Stage == NativeStudioChangeStage.NeedsOverwrite),
            ["disabled:native_change_cancel"] = RmlUiBindingValue.FromBoolean(state.Stage is NativeStudioChangeStage.Preserving or NativeStudioChangeStage.Ready)
        });
    }
    public bool TryTakeCommittedEditor(out INativeStudioEditorSession? session)
    {
        session = null;
        if (!Foreground || !_changes.TryTakeCommittedEditor(_ticket, out session)) return false;
        _taken = true; _open = false; return true;
    }
    public bool Back()
    {
        if (!Foreground) return false;
        var state = _changes.CaptureChange();
        if (state?.Stage is NativeStudioChangeStage.Ready or NativeStudioChangeStage.Preserving) return false;
        _changes.CancelChange(_ticket); _open = false; return true;
    }
    public void Dispose()
    {
        _retirement.Dispose(); if (_disposed) return;
        _disposed = true; _open = false; if (!_taken) _changes.CancelChange(_ticket);
        if (_pages.Top == _document) _pages.CloseModal();
    }
}
#endif
