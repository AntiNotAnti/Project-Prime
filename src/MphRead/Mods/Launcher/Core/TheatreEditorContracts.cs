#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.Core;

public enum TheatreEditorTab { Events, AutomaticHighlights, NamedHighlights, Reel, Analytics }
public enum TheatreEditorFilter { All, Combat, KillsDeaths, Damage, Shots, SpawnsDeaths, Objectives, Annotations }
public enum TheatreEditorEditKind { AddSelection, AddHighlight, AddTopHighlights, MoveUp, MoveDown, Trim, CycleCamera, Remove, ClearReel, RemoveNamedHighlight }
public enum TheatreEditorHud { Analytics, Network, Combat, ShotRays }
public sealed record TheatreEditorEntry(string Id, uint Frame, uint EndFrame, string Name, string Detail,
    int Actor = -1, int EventType = -1, int Camera = 0, int Target = -1, uint? FocusFrame = null);
public sealed record TheatreEditorPlayer(int Slot, string Name);
public sealed record TheatreEditorContext(long SceneGeneration, object Owner, object Transport, string LogicalPath,
    string PlaybackPath, string SourceRevision, string AnnotationRevision, string ReelRevision, uint Frame,
    uint Duration, long SeekGeneration, uint? MarkIn, uint? MarkOut, bool SettledPaused,
    ImmutableArray<TheatreEditorPlayer> Players, ImmutableArray<TheatreEditorEntry> Events,
    ImmutableArray<TheatreEditorEntry> Highlights, ImmutableArray<TheatreEditorEntry> NamedHighlights,
    ImmutableArray<TheatreEditorEntry> Reel, ImmutableArray<TheatreEditorEntry> Analytics, object Persisted)
{
    public bool PublicationVerified { get; init; } = true;
    public string PublicationNotice { get; init; } = "";
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;
}
public sealed record TheatreEditorEdit(TheatreEditorEditKind Kind, TheatreEditorContext Context,
    TheatreEditorEntry? Selected, ImmutableArray<TheatreEditorEntry> Before, ImmutableArray<TheatreEditorEntry> After);
public sealed record TheatreEditorLive(bool Current, bool ExportRendering, string Diagnostic, bool AnalyticsHud,
    bool NetworkHud, bool CombatHud, bool ShotRays);
public sealed record TheatreEditorSnapshot(TheatreEditorContext Context, TheatreEditorTab Tab,
    TheatreEditorFilter Filter, int Player, ImmutableArray<TheatreEditorEntry> Entries, int SelectedIndex,
    TheatreEditorEntry? Selected, TheatreEditorEdit? Pending, TheatreEditorLive Live, string Name, string Status, string Error)
{
    public string EventSearch { get; init; } = "";
    public string Tags { get; init; } = "";
    public bool TagsDirty { get; init; }
}
public sealed class TheatreEditorStaleException(string message) : InvalidOperationException(message) { }

/// <summary>Editor commands borrow the existing playback transport and persisted annotation owners.</summary>
public interface ITheatreEditorBackend
{
    TheatreEditorContext Capture();
    TheatreEditorLive ReadLive(TheatreEditorContext context);
    void Seek(TheatreEditorContext context, TheatreEditorEntry exact, bool selectRange);
    void InspectShot(TheatreEditorContext context, TheatreEditorEntry? previous, int direction, int player);
    void ToggleHud(TheatreEditorContext context, TheatreEditorHud hud);
    TheatreEditorContext Commit(TheatreEditorEdit reviewed);
    void Retire();
}

/// <summary>Optional metadata publication through the editor's exact reviewed annotation owner.</summary>
public interface ITheatreEditorSuggestionBackend
{
    void ValidateSuggestions(TheatreEditorContext context);
    System.Threading.Tasks.Task<ImmutableArray<TheatreEditorEntry>> GenerateSuggestions(
        TheatreEditorContext context, System.Threading.CancellationToken cancellation);
}

public interface ITheatreEditorMetadataBackend
{
    TheatreEditorContext SaveTags(TheatreEditorContext context, ImmutableArray<string> tags);
}

/// <summary>Explicit workspace review of a seek-created transport under the same recording/player.</summary>
public interface ITheatreEditorSeekReviewBackend
{
    void ValidateSeekReview(TheatreEditorContext previous, TheatreEditorContext next);
}

public static class TheatreReplayTags
{
    public const int MaximumTextLength = 1024;
    public const int MaximumLabels = 24;
    public const int MaximumLabelLength = 40;

    public static ImmutableArray<string> Parse(string text)
    {
        if (text.Length > MaximumTextLength) throw new ArgumentException("Use at most 1024 characters for replay tags.");
        return Normalize(text.Split(','));
    }

    // Match the retained ReplayAnnotations organization semantics.
    public static ImmutableArray<string> Normalize(IEnumerable<string> labels)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (string? raw in labels)
        {
            string label = raw?.Trim() ?? "";
            if (label.Length == 0) continue;
            if (label.Length > MaximumLabelLength) label = label[..MaximumLabelLength];
            if (!result.Contains(label, StringComparer.OrdinalIgnoreCase)) result.Add(label);
            if (result.Count == MaximumLabels) break;
        }
        return result.ToImmutable();
    }
}
#endif
