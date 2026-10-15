using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    // Default external authoring is available when a separately installed
    // NetRadiant executable has been configured. The original JSON project
    // stays open and unmodified; never execute source-provided programs or
    // overwrite an external author's changed .map without a valid sidecar.
    private void EditCurrentInNetRadiant()
    {
        if (_document == null)
        {
            _status.Text = "Open or create a map before editing it in NetRadiant.";
            return;
        }
        string? executable = CommunityCutoverPolicy.NetRadiantExecutable;
        if (!CommunityCutoverPolicy.NetRadiantDefault || executable == null)
        {
            _status.Text = "NetRadiant needs an installed desktop executable at "
                + "PROJECT_PRIME_NETRADIANT_EXE. Continue editing in native Studio "
                + "or use File > Import editable .map.";
            return;
        }
        try
        {
            var definition = _document.Project.Definition;
            string source = RadiantSourceWorkspace.Prepare(definition,
                Path.Combine(CustomRooms.UserMapDirectory, "netradiant-sources"));
            using var opened = NetRadiantBridge.LaunchEditor(executable, source);
            RefreshSourceWatch();
            _status.Text = "RADIANT · " + RadiantSourceWorkspace.State(definition, source)
                + " · Geometry source: " + source
                + ". Native gameplay, materials, assets and environment remain in this project. "
                + "Save there, then File > Review Radiant geometry changes.";
        }
        catch (QuakeMapInterchangeException ex)
        {
            _status.Text = "NetRadiant export refused: "
                + string.Join("; ", ex.Diagnostics.Take(3).Select(x => x.Message))
                + ". Keep editing unsupported geometry in native Studio.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException
            or InvalidDataException or MapAuthoringException or PlatformNotSupportedException
            or UnauthorizedAccessException)
        { Failure(ex); }
    }

    private void LaunchNetRadiant()
        => Browse("Choose editable Quake .map source for external NetRadiant",
            false, path =>
            {
                try
                {
                    string executable = Environment.GetEnvironmentVariable(
                        "PROJECT_PRIME_NETRADIANT_EXE") ?? "";
                    using var process = NetRadiantBridge.LaunchEditor(executable, path);
                    _status.Text = "Opened external NetRadiant source editor. "
                        + "Save the .map there, then choose Compile NetRadiant Source.";
                }
                catch (Exception ex) when (ex is IOException or ArgumentException
                    or InvalidDataException or PlatformNotSupportedException)
                { Failure(ex); }
            }, ".map");

    private void CompileNetRadiantSource()
        => Browse("Choose saved Quake .map source to compile and import",
            false, path => _ = CompileNetRadiantSourceAsync(path), ".map");

    private Task CompileNetRadiantSourceAsync(string source)
        => Job("Compiling source to BSP reference", async cancellation =>
        {
            string compiler = Environment.GetEnvironmentVariable(
                "PROJECT_PRIME_Q3MAP2_EXE") ?? "";
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

    private void ReviewRadiantGeometryChanges()
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
                + ". Native gameplay, identity, audio, environment and asset references are retained. "
                + "Applying creates one undoable edit. Source SHA-256: " + review.SourceHash));
            var before = document.Project.Definition.Geometry.ToDictionary(g => g.Id);
            var after = review.Proposed.Geometry.ToDictionary(g => g.Id);
            var added = after.Keys.Except(before.Keys).ToArray();
            var removed = before.Keys.Except(after.Keys).ToArray();
            var changed = after.Keys.Intersect(before.Keys).Where(id =>
                System.Text.Json.JsonSerializer.Serialize<MphRead.Mods.MapGen.MapGeometry>(before[id])
                != System.Text.Json.JsonSerializer.Serialize<MphRead.Mods.MapGen.MapGeometry>(after[id])).ToArray();
            panel.Children.Add(Text($"Added {added.Length}; removed {removed.Length}; changed {changed.Length}."));
            foreach (string detail in added.Select(id => "Added: " + after[id].Label)
                .Concat(removed.Select(id => "Removed: " + before[id].Label))
                .Concat(changed.Select(id => "Changed: " + after[id].Label)).Take(12))
                panel.Children.Add(Text(detail));
            foreach (var material in review.Proposed.Materials.Skip(document.Project.Definition.Materials.Count).Take(8))
                panel.Children.Add(Text("New shader binding: " + material.Name + ". Configure its native texture before publishing."));
            AddButton(panel, "Apply geometry changes", () =>
            {
                try
                {
                    if (_document != document) throw new IOException("The open document changed. Review again.");
                    RadiantSourceWorkspace.RequireFresh(document.Project.Definition, review);
                    document.Edit("Apply Radiant geometry", definition =>
                    {
                        // Only replace the reviewed domains; concurrent native metadata edits survive.
                        definition.Geometry = review.Proposed.Geometry;
                        definition.Materials = review.Proposed.Materials;
                    }, MphRead.Mods.MapEditor.MapChangeDomain.Geometry | MphRead.Mods.MapEditor.MapChangeDomain.Material);
                    RadiantSourceWorkspace.Accept(document.Project.Definition, review);
                    _changedSources.Remove(source);
                    Dismiss();
                    _status.Text = "RADIANT · READY FOR BUILD · Geometry applied. Validate before building; Undo restores the prior geometry.";
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
