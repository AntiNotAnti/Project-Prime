using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal Action<System.Diagnostics.ProcessStartInfo>? RadiantLauncher;
    internal Action<string>? NativeEditGeometrySource;
    private string? RadiantSource => _document?.Project.Definition.MapId is { } id && id != Guid.Empty
        ? RadiantSourceWorkspace.SourcePath(Path.Combine(CustomRooms.UserMapDirectory, "netradiant-sources"), id) : null;
    private bool HasRadiantSource => RadiantSource is { } source && File.Exists(source);
    // The shipped editor is the primary geometry workflow. The original JSON project
    // stays open and unmodified; never execute source-provided programs or
    // overwrite an external author's changed .map without a valid sidecar.
    internal void EditCurrentInNetRadiant()
    {
        if (_document == null)
        {
            _status.Text = "Open or create a map before editing it in NetRadiant.";
            return;
        }
        string? executable = CommunityCutoverPolicy.NetRadiantExecutable;
        if (!CommunityCutoverPolicy.NetRadiantDefault || executable == null)
        {
            _status.Text = CommunityCutoverPolicy.NetRadiantTools.EditorError
                ?? "Install Project Prime Radiant beside this application, or configure PROJECT_PRIME_NETRADIANT_EXE. Native geometry fallback remains available. Check PROJECT_PRIME_NETRADIANT_DEFAULT if Radiant was disabled.";
            return;
        }
        try
        {
            var definition = _document.Project.Definition;
            string source = RadiantSourceWorkspace.Prepare(definition,
                Path.Combine(CustomRooms.UserMapDirectory, "netradiant-sources"));
            RadiantTexturePreview.Prepare(definition, source, out var unavailableMaterials);
            LaunchRadiant(executable, source);
            RefreshSourceWatch();
            RefreshStudioChrome();
            _status.Text = "RADIANT · " + RadiantSourceWorkspace.State(definition, source)
                + " · Map source: " + source
                + ". Edit meshes, brushes and gameplay objects in Radiant. "
                + "Save there, then choose Review / Apply changes above."
                + (unavailableMaterials.Count == 0 ? "" : $" Checker previews shown for {unavailableMaterials.Count} unavailable textures.");
        }
        catch (QuakeMapInterchangeException ex)
        {
            _status.Text = "NetRadiant export refused: "
                + string.Join("; ", ex.Diagnostics.Take(3).Select(x => x.Message))
                + ". The project and existing Radiant source were preserved.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException
            or InvalidDataException or MapAuthoringException or PlatformNotSupportedException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Failure(ex); }
    }

    private void LaunchRadiant(string executable, string source)
    {
        var start = NetRadiantBridge.CreateEditorStartInfo(executable, source);
        if (RadiantLauncher is { } launch) launch(start);
        else { using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Radiant did not start."); }
    }

    internal void ShowNativeGeometryTools()
    {
        Dismiss();
        _nativeGeometryRecovery = true;
        RefreshStudioChrome();
        if (_nativeGeometryTools != null) _nativeGeometryTools.IsExpanded = true;
        _status.Text = _nativeStandaloneHost && CommunityCutoverPolicy.NetRadiantDefault
            ? "Native geometry recovery workspace is open. Return to Project Overview to resume the Radiant workflow. Radiant sources remain unchanged until explicitly reviewed and applied."
            : "Native geometry fallback and gameplay placement tools are open. Radiant sources remain unchanged until explicitly reviewed and applied.";
    }

    internal void OpenGeometryInRadiant(string path)
    {
        if (NativeEditGeometrySource is { } open) { open(path); return; }
        WithUnsaved(() =>
        {
            try
            {
#if !ANDROID
                if (NativeCanOpenSource?.Invoke(path) == false) return;
#endif
                Load(MapProjectSerializer.Load(path), path);
                EditCurrentInNetRadiant();
            }
            catch (Exception ex) { Failure(ex); }
        });
    }

    private void LaunchNetRadiant()
        => Browse("Choose editable Quake .map source for external NetRadiant",
            false, path =>
            {
                try
                {
                    string executable = CommunityCutoverPolicy.NetRadiantDefault
                        ? CommunityCutoverPolicy.NetRadiantExecutable! : throw new IOException(
                            CommunityCutoverPolicy.NetRadiantTools.EditorError ?? "Install or enable Project Prime Radiant first.");
                    LaunchRadiant(executable, path);
                    _status.Text = "Opened external NetRadiant source editor. "
                        + "Save the .map there, then choose Compile NetRadiant Source.";
                }
                catch (Exception ex) when (ex is IOException or ArgumentException
                    or InvalidDataException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
                { Failure(ex); }
            }, ".map");

    private void CompileNetRadiantSource()
        => Browse("Choose saved Quake .map source to compile and import",
            false, path => _ = CompileNetRadiantSourceAsync(path), ".map");

    private Task CompileNetRadiantSourceAsync(string source)
        => Job("Compiling source to BSP reference", async cancellation =>
        {
            string compiler = CommunityCutoverPolicy.Q3Map2Executable ?? throw new IOException(
                CommunityCutoverPolicy.NetRadiantTools.CompilerError ?? "Install the packaged q3map2 compiler or configure PROJECT_PRIME_Q3MAP2_EXE.");
            string name = new string(Path.GetFileNameWithoutExtension(source)
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '_'
                    ? char.ToUpperInvariant(ch) : '_').Take(32).ToArray());
            if (string.IsNullOrWhiteSpace(name)) name = "RADIANT_ARENA";
            string destination = Path.Combine(CustomRooms.MapDirectory, name);
            NetRadiantBridge.CompileResult result = await NetRadiantBridge.CompileImportAsync(
                compiler, source, name, destination, cancellation);
            GuardJob(cancellation);
            if (!result.Succeeded || result.ProjectPath == null)
            {
                _status.Text = "BSP reference conversion failed: "
                    + string.Join("; ", result.Diagnostics.Take(5));
                return;
            }
            // A BSP reference is a separate project, never an identity-preserving
            // editable reimport. Respect the open document's unsaved work.
            WithUnsaved(() => Open(result.ProjectPath));
            _status.Text = "Created separate BSP reference project: " + result.ProjectPath
                + ". Source SHA-256 " + result.SourceSha256
                + "; BSP SHA-256 " + result.BspSha256
                + ". Validate collision, materials and gameplay before building .ppmap.";
        });

    internal void ReviewRadiantGeometryChanges()
    {
        if (_document == null) { _status.Text = "Open the native project before reviewing Radiant changes."; return; }
        string source = RadiantSourceWorkspace.SourcePath(
            Path.Combine(CustomRooms.UserMapDirectory, "netradiant-sources"),
            _document.Project.Definition.MapId);
        ReviewRadiantGeometryChanges(source);
    }

    private void ReviewRadiantGeometryChanges(string source)
    {
        if (_document == null) return;
        var document = _document;
        try
        {
            var review = RadiantSourceWorkspace.Preview(document.Project.Definition, source);
            var panel = new Avalonia.Controls.StackPanel { Spacing = 8 };
            panel.Children.Add(Text("RADIANT · NEEDS REVIEW"));
            panel.Children.Add(Text(source));
            panel.Children.Add(Text("Geometry: " + review.BeforeCount + " → " + review.AfterCount
                + ". Project identity, audio, environment and asset references are retained. "
                + "Applying creates one undoable edit. Source SHA-256: " + review.SourceHash));
            var current = document.Project.Definition;
            panel.Children.Add(Text($"Legacy brushes: {current.Brushes.Count} → {review.Proposed.Brushes.Count}; "
                + $"spawns: {current.Spawns.Count} → {review.Proposed.Spawns.Count}; "
                + $"pickups: {current.Items.Count} → {review.Proposed.Items.Count}; "
                + $"jump pads: {current.JumpPads.Count} → {review.Proposed.JumpPads.Count}; "
                + $"navigation links: {current.NavigationLinks.Count} → {review.Proposed.NavigationLinks.Count}; "
                + $"traversal volumes: {current.Q3Triggers?.Count ?? 0} → {review.Proposed.Q3Triggers?.Count ?? 0}."));
            var before = document.Project.Definition.Geometry.ToDictionary(g => g.Id);
            var after = review.Proposed.Geometry.ToDictionary(g => g.Id);
            var added = after.Keys.Except(before.Keys).ToArray();
            var removed = before.Keys.Except(after.Keys).ToArray();
            var changed = after.Keys.Intersect(before.Keys).Where(id =>
                System.Text.Json.JsonSerializer.Serialize<MphRead.Mods.MapGen.MapGeometry>(before[id])
                != System.Text.Json.JsonSerializer.Serialize<MphRead.Mods.MapGen.MapGeometry>(after[id])).ToArray();
            panel.Children.Add(Text($"Added {added.Length}; removed {removed.Length}; changed {changed.Length}."));
            foreach (var diagnostic in review.Diagnostics.Take(12))
                panel.Children.Add(Text((diagnostic.Error ? "Error: " : "Info: ") + diagnostic.Message));
            foreach (string detail in added.Select(id => "Added: " + after[id].Label)
                .Concat(removed.Select(id => "Removed: " + before[id].Label))
                .Concat(changed.Select(id => "Changed: " + after[id].Label)).Take(12))
                panel.Children.Add(Text(detail));
            foreach (var material in review.Proposed.Materials.Skip(document.Project.Definition.Materials.Count).Take(8))
                panel.Children.Add(Text("New shader binding: " + material.Name + ". Configure its native texture before publishing."));
            AddButton(panel, "Apply map changes", () =>
            {
                try
                {
                    if (_document != document) throw new IOException("The open document changed. Review again.");
                    RadiantSourceWorkspace.RequireFresh(document.Project.Definition, review);
                    document.Edit("Apply Radiant map changes", definition =>
                    {
                        // Apply the source's versioned ownership contract in one undo step.
                        RadiantSourceWorkspace.ApplyReviewed(definition, review);
                    }, MphRead.Mods.MapEditor.MapChangeDomain.Geometry | MphRead.Mods.MapEditor.MapChangeDomain.Material
                        | MphRead.Mods.MapEditor.MapChangeDomain.Entity | MphRead.Mods.MapEditor.MapChangeDomain.Navigation);
                    RadiantSourceWorkspace.Accept(document.Project.Definition, review);
                    _changedSources.Remove(source);
                    Dismiss();
                    RefreshStudioChrome();
                    _status.Text = "RADIANT · READY FOR BUILD · Map changes applied. Validate before building; Undo restores the prior map.";
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or MapAuthoringException
                    or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                { Failure(ex); }
            });
            AddButton(panel, "Keep both sources / cancel", Dismiss);
            Modal(panel);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or MapAuthoringException
            or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { _status.Text = "RADIANT · IMPORT CONFLICT · " + ex.Message; }
    }
}
