#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.InGame;

public sealed class AimResultsPagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly AimResultsController _controller;
    private AimResultsAction? _action;
    private bool _disposed;
    public RmlUiDocumentToken Document { get; private set; }
    public bool Active => !_disposed && Document != default && _host.IsAlive(Document) && _pages.Page == Document;
    public AimResultsPagePresenter(RmlUiHost host, RmlUiPageManager pages, AimResultsController controller)
        => (_host, _pages, _controller) = (host, pages, controller);
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Document = _pages.OpenPage(new("training-results", "pages/ingame/aim-results.rml", "aim_results_retry"));
        var report = _controller.Report;
        var fields = new Dictionary<string, RmlUiBindingValue> { ["aim_results_summary"] = RmlUiBindingValue.FromText(report.Summary),
            ["aim_results_status"] = RmlUiBindingValue.FromText(report.Status), ["visible:aim_results_status"] = RmlUiBindingValue.FromBoolean(report.Status.Length > 0) };
        for (int index = 0; index < 20; index++)
        {
            bool present = index < report.Metrics.Length;
            fields["visible:aim_result" + index] = RmlUiBindingValue.FromBoolean(present);
            if (present) fields["aim_result" + index] = RmlUiBindingValue.FromText(report.Metrics[index].Label + " // " + report.Metrics[index].Value);
        }
        _pages.Present(Document, 1, fields);
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!Active || intent.Document != Document || intent.Kind != RmlUiIntentKind.AimResultsAction || !_pages.Accept(intent)) return false;
        var action = (AimResultsAction)intent.Argument;
        if (_controller.Dispatch(_controller.Lifetime, action)) _action = action;
        return true;
    }
    public bool Back()
    {
        if (!Active) return false;
        if (_controller.Dispatch(_controller.Lifetime, AimResultsAction.Exit)) _action = AimResultsAction.Exit;
        return true;
    }
    public bool TryTakeAction(out AimResultsAction action) { action = _action.GetValueOrDefault(); bool available = _action.HasValue; _action = null; return available; }
    public void Dispose()
    {
        if (_disposed) return;
        if (_pages.Page == Document && !_pages.ClosePage()) throw new InvalidOperationException("The training result page could not be closed.");
        Document = default; _disposed = true;
    }
}
#endif
