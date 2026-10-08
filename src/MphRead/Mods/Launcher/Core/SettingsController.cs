using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace MphRead.Mods.Launcher.Core;

internal enum SettingsCategory { Display, Graphics, Audio, Controls, Replays, Profile, System, Maintenance, Credits, Hud, Controller, Touch, Online }
internal enum SettingsValueKind { Text, Boolean, Number, Choice, Structured }
internal sealed record SettingsFieldDefinition(string Id, SettingsCategory Category, string Label,
    SettingsValueKind Kind, string Help, IReadOnlyList<string> Choices,
    Func<string, string> Validate, bool Video = false, bool RequiresRestart = false)
{
    private string? _group;
    public string Group => _group ??= SettingsGroups.For(this);
}
internal sealed record SettingsFieldSnapshot(SettingsFieldDefinition Definition, string Value, bool Changed);
internal sealed record SettingsSnapshot(long Revision, SettingsCategory Category, int Page, int PageCount,
    IReadOnlyList<SettingsFieldSnapshot> Fields, bool Dirty, string Status, string Error,
    bool RestartRequired, bool VideoConfirmation, bool UsableVideoFrame, int VideoSecondsRemaining);

internal interface ISettingsBackend
{
    IReadOnlyList<SettingsFieldDefinition> Definitions { get; }
    IReadOnlyDictionary<string, string> Capture();
    bool RestartRequired { get; }
    string ApplyWarning => "";
    // persist=false previews/restores runtime values without writing stores.
    // persist=true must rollback disk and runtime on any failed write.
    void Apply(IReadOnlyDictionary<string, string> values, bool persist);
    void ExpandDraft(string id, string value, IDictionary<string,string> draft) { }
}

// Draft state contains values, never toolkit controls or native document IDs.
// Baselines are detached snapshots and remain immutable until a successful save.
internal sealed class SettingsController
{
    internal const int RowsPerPage = 12;
    private readonly ISettingsBackend _backend;
    private readonly Func<double> _clock;
    private readonly Dictionary<string, SettingsFieldDefinition> _definitions;
    private Dictionary<string, string> _saved, _draft;
    private Dictionary<string, string>? _previewBefore;
    private double _videoDeadline;
    private bool _usableVideoFrame;
    private string _status = "Changes are applied together when you choose Apply.", _error = "";
    private long _revision;
    internal SettingsCategory Category { get; private set; }
    internal int Page { get; private set; }
    internal string Group { get; private set; } = "";
    private readonly Dictionary<SettingsCategory,string[]> _groups = new();
    internal IReadOnlyList<string> Groups => _groups.TryGetValue(Category,out var groups)?groups:_groups[Category]=_backend.Definitions.Where(f => f.Category == Category).Select(f => f.Group).Distinct().OrderBy(g=>g is "Controller / preferences" or "Appearance and scale"?0:1).ToArray();
    internal void SelectGroup(int index) { var groups=Groups; if(index<0||index>=groups.Count)return; Group=groups[index]; Query=""; Page=0; _revision++; }
    internal string Query { get; private set; } = "";
    internal bool Dirty => _draft.Any(value => !_saved.TryGetValue(value.Key, out string? saved) || saved != value.Value);
    internal bool PendingVideoConfirmation => _previewBefore != null;
    internal IReadOnlyDictionary<string, string> Draft => new ReadOnlyDictionary<string, string>(_draft);

    internal SettingsController(ISettingsBackend backend, Func<double>? clock = null)
    {
        _backend = backend;
        _clock = clock ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
        _definitions = backend.Definitions.ToDictionary(field => field.Id, StringComparer.Ordinal);
        _saved = new(backend.Capture(), StringComparer.Ordinal);
        foreach (string id in _definitions.Keys)
            if (!_saved.ContainsKey(id)) throw new InvalidOperationException("Missing authoritative settings value: " + id);
        _draft = new(_saved, StringComparer.Ordinal);
    }

    internal SettingsSnapshot Snapshot()
    {
        Tick();
        var groups=Groups;
        if (!groups.Contains(Group)) Group=groups.FirstOrDefault() ?? "";
        // Search spans every category; group browsing never hides an editable field.
        var fields = _backend.Definitions.Where(field => Query.Length > 0
            ? field.Label.Contains(Query,StringComparison.OrdinalIgnoreCase) || field.Id.Contains(Query,StringComparison.OrdinalIgnoreCase)
                || field.Group.Contains(Query,StringComparison.OrdinalIgnoreCase)
            : field.Category == Category && field.Group == Group).ToArray();
        int pages = Math.Max(1, (fields.Length + RowsPerPage - 1) / RowsPerPage);
        Page = Math.Clamp(Page, 0, pages - 1);
        var rows = fields.Skip(Page * RowsPerPage).Take(RowsPerPage)
            .Select(field => new SettingsFieldSnapshot(field, _draft[field.Id], _draft[field.Id] != _saved[field.Id])).ToArray();
        return new(_revision, Category, Page, pages, Array.AsReadOnly(rows), Dirty, _status, _error,
            _backend.RestartRequired, PendingVideoConfirmation, _usableVideoFrame,
            PendingVideoConfirmation ? Math.Max(0, (int)Math.Ceiling(_videoDeadline - _clock())) : 0);
    }

    internal bool Set(string id, string value)
    {
        if (_backend.RestartRequired) return Fail("Imported or reset preferences require a restart before editing.");
        if (PendingVideoConfirmation) return Fail("Keep or revert the video preview before editing.");
        if (!_definitions.TryGetValue(id, out SettingsFieldDefinition? field)) return Fail("Unknown setting.");
        try
        {
            string previous = _draft[id];
            _draft[id] = field.Validate(value);
            if (_draft[id] != previous) _backend.ExpandDraft(id, _draft[id], _draft);
            _error = ""; _revision++; return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { return Fail(field.Label + ": " + ex.Message); }
    }

    internal void SelectCategory(SettingsCategory category)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        Category = category; Group=""; Query=""; Page = 0; _revision++;
    }
    internal void MovePage(int delta) { Page = Math.Max(0, Page + delta); _revision++; }
    internal void Search(string query){Query=query.Trim();Page=0;_revision++;}
    internal bool StageSnapshot(IReadOnlyDictionary<string,string> values)
    {
        if (_backend.RestartRequired || PendingVideoConfirmation) return Fail("Finish the current settings operation first.");
        try
        {
            var next = new Dictionary<string,string>(_draft,StringComparer.Ordinal);
            foreach(var value in values)
            {
                if(!next.ContainsKey(value.Key))throw new FormatException("Unknown setting.");
                next[value.Key]=_definitions.TryGetValue(value.Key,out var field) && next[value.Key]!=value.Value
                    ?field.Validate(value.Value):value.Value;
            }
            _draft=next;_error="";_status="Profile changes staged. Apply to activate and save.";_revision++;return true;
        }
        catch(Exception ex) when(ex is FormatException or ArgumentException or OverflowException){return Fail(ex.Message);}
    }
    internal void RevertCategory()
    {
        if (_backend.RestartRequired || PendingVideoConfirmation) return;
        foreach (var field in _backend.Definitions.Where(field => field.Category == Category)) _draft[field.Id] = _saved[field.Id];
        if(Category==SettingsCategory.Hud)
            foreach(var saved in _saved.Where(value=>value.Key.StartsWith("$hud",StringComparison.Ordinal)||value.Key.StartsWith("$legacy",StringComparison.Ordinal)))
                _draft[saved.Key]=saved.Value;
        _error = ""; _status = "Category restored to its last saved values."; _revision++;
    }
    internal void Discard()
    {
        if (_backend.RestartRequired) return;
        if (PendingVideoConfirmation) RevertVideo();
        _draft = new(_saved, StringComparer.Ordinal); _error = ""; _status = "Unsaved changes discarded."; _revision++;
    }

    internal bool Apply()
    {
        if (_backend.RestartRequired) return Fail("Preferences require a restart before another save.");
        if (PendingVideoConfirmation) return Fail("Keep or revert the current video preview.");
        if (!Dirty) { _status = "All settings are saved."; _revision++; return true; }
        try
        {
            foreach (var field in _backend.Definitions)
                if (_draft[field.Id] != _saved[field.Id]) _draft[field.Id] = field.Validate(_draft[field.Id]);
            bool video = _backend.Definitions.Any(field => field.Video && _draft[field.Id] != _saved[field.Id]);
            if (video)
            {
                _previewBefore = new(_backend.Capture(), StringComparer.Ordinal);
                try { _backend.Apply(_draft, persist: false); }
                catch
                {
                    // A failed preview may already have changed the surface.
                    // Restore before discarding the rollback snapshot.
                    RevertPreviewRuntime();
                    throw;
                }
                _videoDeadline = _clock() + 15; _usableVideoFrame = false;
                _status = "Confirm the new video settings after a usable frame, or they revert in 15 seconds.";
                _error = ""; _revision++; return true;
            }
            return Persist();
        }
        catch (Exception ex) { return Fail("Could not apply settings: " + ex.Message); }
    }

    // Called by the engine only after final composition and a successful present.
    internal void ObservePresentedFrame(bool usable)
    {
        if (!PendingVideoConfirmation) return;
        if (!usable) { RevertVideo(); return; }
        _usableVideoFrame = true; _revision++;
    }
    internal bool KeepVideo()
    {
        Tick();
        if (!PendingVideoConfirmation) return false;
        if (!_usableVideoFrame) return Fail("Wait for the renderer to present a usable frame before keeping video settings.");
        if (!Persist()) { RevertPreviewRuntime(); return false; }
        _previewBefore = null; _usableVideoFrame = false; _revision++; return true;
    }
    internal void RevertVideo()
    {
        if (!PendingVideoConfirmation) return;
        RevertPreviewRuntime();
        _draft = new(_saved, StringComparer.Ordinal);
        _status = "Video settings reverted to the last usable configuration."; _revision++;
    }
    private void RevertPreviewRuntime()
    {
        var before = _previewBefore; _previewBefore = null; _usableVideoFrame = false;
        if (before == null) return;
        try { _backend.Apply(before, persist: false); }
        catch (Exception ex) { Fail("Could not restore the video configuration: " + ex.Message); }
    }
    internal void Tick() { if (PendingVideoConfirmation && _clock() >= _videoDeadline) RevertVideo(); }
    private bool Persist()
    {
        try
        {
            _backend.Apply(_draft, persist: true);
            bool restart = _backend.Definitions.Any(field => field.RequiresRestart && _draft[field.Id] != _saved[field.Id]);
            _saved = new(_backend.Capture(), StringComparer.Ordinal); _draft = new(_saved, StringComparer.Ordinal);
            _error = ""; _status = restart ? "Settings saved. Restart Project Prime to change renderer." : "Settings saved and applied.";
            if (_backend.ApplyWarning.Length > 0) _status += " " + _backend.ApplyWarning;
            _revision++; return true;
        }
        catch (Exception ex) { return Fail("Could not save settings: " + ex.Message); }
    }
    private bool Fail(string error) { _error = error; _revision++; return false; }
}

internal static class SettingsValueValidation
{
    internal static string Number(string value, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            || !double.IsFinite(result) || result < min || result > max)
            throw new FormatException($"Enter a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");
        return result.ToString("0.####", CultureInfo.InvariantCulture);
    }
    internal static string Integer(string value, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result < min || result > max)
            throw new FormatException($"Enter a whole number from {min} to {max}.");
        return result.ToString(CultureInfo.InvariantCulture);
    }
    internal static string Boolean(string value)
    {
        if (!bool.TryParse(value, out bool result)) throw new FormatException("Choose true or false.");
        return result ? "true" : "false";
    }
    internal static string Choice(string value, IReadOnlyList<string> choices)
    {
        string? match = choices.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new FormatException("Choose " + string.Join(", ", choices) + ".");
    }
    internal static string Text(string value)
    {
        if (value.Length > 4096 || value.Any(character => character is '\r' or '\n' or '\0'))
            throw new FormatException("Use one line of text, up to 4096 characters.");
        return value;
    }
}
