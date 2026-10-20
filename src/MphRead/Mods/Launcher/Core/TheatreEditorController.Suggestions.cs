#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

public sealed partial class TheatreEditorController
{
    private Task<ImmutableArray<TheatreEditorEntry>>? _suggestions;
    private CancellationTokenSource? _suggestionCancellation;
    private TheatreEditorContext? _suggestionReview;
    public bool GenerateSuggestions() => Run(() =>
    {
        Free(); Current();
        if (_backend is not ITheatreEditorSuggestionBackend backend)
            throw new InvalidOperationException("This replay owner does not offer background suggestions.");
        CancelSuggestions();
        _suggestionCancellation = new(); _suggestionReview = _context;
        _suggestions = backend.GenerateSuggestions(_context, _suggestionCancellation.Token);
        _tab = TheatreEditorTab.AutomaticHighlights; _selected = 0;
        _status = "Generating annotation suggestions; acceptance alone saves reel changes.";
    });
    public bool RejectSuggestion() => Run(() =>
    {
        Free(); Current();
        if (_tab != TheatreEditorTab.AutomaticHighlights) throw new InvalidOperationException("Select an automatic suggestion.");
        var selected = Selected();
        _context = _context with { Highlights = _context.Highlights.Remove(selected) };
        _status = "Suggestion rejected; source and saved reel unchanged.";
    });
    private System.Collections.Generic.IEnumerable<TheatreEditorEntry> VariedSuggestions(ImmutableArray<TheatreEditorEntry> before)
    {
        var counts = new int[8]; var accepted = new System.Collections.Generic.List<TheatreEditorEntry>(8);
        foreach (var row in _context.Highlights)
        {
            if (row.Actor is >= 0 and < 8 && counts[row.Actor] >= 2) continue;
            bool overlap = false;
            foreach (var prior in before) if (row.Frame < prior.EndFrame && row.EndFrame > prior.Frame) { overlap = true; break; }
            foreach (var prior in accepted) if (row.Frame < prior.EndFrame && row.EndFrame > prior.Frame) { overlap = true; break; }
            if (overlap) continue;
            accepted.Add(row); if (row.Actor is >= 0 and < 8) counts[row.Actor]++;
            yield return row;
            if (accepted.Count == 8) yield break;
        }
    }
    private void CancelSuggestions()
    {
        _suggestionCancellation?.Cancel(); _suggestionCancellation?.Dispose();
        _suggestionCancellation = null; _suggestions = null; _suggestionReview = null;
    }
    private void PollSuggestions()
    {
        if (_suggestions?.IsCompleted != true || _pending != null) return;
        try
        {
            var review = _suggestionReview!;
            if (_authority && ReferenceEquals(review.Owner,_context.Owner)
                && ReferenceEquals(review.Transport,_context.Transport)
                && review.SourceRevision == _context.SourceRevision
                && review.AnnotationRevision == _context.AnnotationRevision
                && review.ReelRevision == _context.ReelRevision)
            {
                ((ITheatreEditorSuggestionBackend)_backend).ValidateSuggestions(review);
                var result = _suggestions.GetAwaiter().GetResult();
                _context = _context with { Highlights = result };
                _status = $"{result.Length} ranked annotation suggestions. Preview, accept or reject.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _error = "Suggestion analysis failed: " + error.Message; }
        finally { CancelSuggestions(); }
    }
}
#endif
