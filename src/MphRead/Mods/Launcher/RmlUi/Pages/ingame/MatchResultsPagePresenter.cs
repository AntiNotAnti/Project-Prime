#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.InGame;

public sealed class MatchResultsPagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly MatchResultsController _controller;
    private long _revision;
    private MatchResultsEffect _effect;
    private bool _disposed;
    public RmlUiDocumentToken Document { get; private set; }
    public bool Active => !_disposed && Document != default && _host.IsAlive(Document) && _pages.Page == Document;
    public MatchResultsPagePresenter(RmlUiHost host, RmlUiPageManager pages, MatchResultsController controller)
        => (_host, _pages, _controller) = (host, pages, controller);
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Document = _pages.OpenPage(new("results", "pages/ingame/results.rml", "results_close"));
        _revision = 0;
        Refresh();
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!Active || intent.Document != Document || intent.Kind is not (RmlUiIntentKind.ResultsAction or RmlUiIntentKind.ResultsMap) || !_pages.Accept(intent)) return false;
        MatchResultsAction action = intent.Kind == RmlUiIntentKind.ResultsMap ? MatchResultsAction.SelectMap : (MatchResultsAction)intent.Argument;
        string query = action == MatchResultsAction.Search ? _host.ReadField(Document, "results_search") : "";
        MatchResultsResult result = _controller.Dispatch(_controller.Intent(action, intent.Kind == RmlUiIntentKind.ResultsMap ? intent.Argument : -1), query);
        if (result.Accepted) _effect = result.Effect;
        if (action == MatchResultsAction.ClearSearch && result.Accepted) _host.SetField(Document, "results_search", "");
        Refresh();
        return true;
    }
    public bool Back()
    {
        if (!Active) return false;
        MatchResultsResult result = _controller.Dispatch(_controller.Intent(MatchResultsAction.Close));
        if (result.Accepted) _effect = result.Effect;
        Refresh(); return true;
    }
    public bool TryTakeEffect(out MatchResultsEffect effect) { effect = _effect; _effect = MatchResultsEffect.None; return effect != MatchResultsEffect.None; }
    public void Refresh()
    {
        if (!Active) return;
        _controller.Refresh();
        var state = _controller.Snapshot();
        if (state.Closed) { Dispose(); return; }
        if (_revision == state.Revision) return;
        _revision = state.Revision;
        var fields = new Dictionary<string, RmlUiBindingValue>();
        void Text(string id, string value) => fields[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => fields[id] = RmlUiBindingValue.FromBoolean(value);
        void Enable(string id, bool value) => Bool("disabled:" + id, !value);
        var facts = state.Facts;
        Text("results_title", facts.Persistent ? "RETURNING TO LOBBY" : "NEXT DEPLOYMENT");
        Text("results_phase", facts.Persistent ? "MATCH COMPLETE // SESSION CONTINUES" : "MATCH COMPLETE // NEXT ARENA");
        Text("results_next", facts.Persistent ? "NEXT // LOBBY" : facts.Picked.Length > 0 ? "SELECTED // " + (facts.Maps.FirstOrDefault(map => map.Key == facts.Picked).Name ?? facts.Picked)
            : facts.Online ? "NEXT // " + facts.NextRoom : "CURRENT ARENA // REMATCH");
        Text("results_countdown", facts.Countdown < 0 ? "RESULTS COMPLETE" : (facts.Persistent ? "RETURNING IN " : "DEPLOYING IN ") + facts.Countdown + " SEC");
        Text("results_message", facts.Persistent ? "The session stays connected. Choose the next arena and Hunter from the lobby."
            : !facts.BallotOpen ? "Waiting for the server to open the arena ballot."
            : state.Rows.Length == 0 ? "No arenas match the search." : "Choose again to remove your selection.");
        Text("results_tally", facts.Online && !facts.Persistent ? "ELIGIBLE VOTERS // " + facts.Eligible : "LOCAL MATCH");
        Text("results_paging", $"{state.Page + 1} / {state.PageCount}");
        Text("results_error", state.Error);
        Bool("visible:results_error", state.Error.Length > 0);
        Bool("visible:results_ballot", !facts.Persistent);
        Bool("visible:results_rematch", !facts.Online && !facts.Persistent);
        Enable("results_rematch", facts.Available && !facts.Online);
        Enable("results_previous", state.Page > 0); Enable("results_next_page", state.Page + 1 < state.PageCount);
        for (int index = 0; index < 8; index++)
        {
            string id = "results_map" + index;
            bool present = index < state.Rows.Length;
            Bool("visible:" + id, present); Enable(id, present && facts.Available && facts.BallotOpen && !facts.Persistent);
            Bool("class:" + id + ":selected", present && state.Rows[index].Key == facts.Picked);
            Bool("class:" + id + ":leader", present && state.Rows[index].Votes > 0 && state.Rows[index].Key == facts.Leader);
            if (present)
            {
                Text(id, state.Rows[index].Name + (facts.Online ? " // " + state.Rows[index].Votes + " VOTES" : ""));
                if (state.Rows[index].CatalogIndex is >= 0 and < 4096) Text("action:" + id, "results:map:" + state.Rows[index].CatalogIndex);
                else Enable(id, false);
            }
        }
        _pages.Present(Document, _revision, fields);
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_pages.Page == Document && !_pages.ClosePage()) throw new InvalidOperationException("The results page could not be closed.");
        Document = default; _disposed = true;
    }
}
#endif
