using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private void ImportEditableQuakeMap()
        => Browse("Import editable Quake .map brushes, curves and gameplay objects",
            false, source =>
            {
                try
                {
                    var sidecar = Path.Combine(Path.GetDirectoryName(source)!,
                        "project-prime.json");
                    var result = QuakeMapInterchange.Import(source,
                        File.Exists(sidecar) ? sidecar : null);
                    // Never save the Project Prime JSON back over the external
                    // Quake .map source. Authors select a normal .json save path.
                    WithUnsaved(() => Load(new MapProject(result.Definition)));
                    string warnings = string.Join("; ", result.Diagnostics
                        .Where(d => !d.Error).Take(2)
                        .Select(d => d.Code + ": " + d.Message));
                    int patchCount = result.Definition.Geometry.OfType<MapBezierPatch>().Count();
                    int brushCount = result.Definition.Geometry.Count - patchCount;
                    _status.Text = "Imported " + brushCount
                        + " editable box/convex brush(es), "
                        + patchCount + " Bezier patch(es), "
                        + result.Definition.Spawns.Count + " spawn(s), and "
                        + result.Definition.Items.Count + " pickup(s). "
                        + "Scale factor " + result.Definition.ScaleFactor
                        + ". Save to a new .json project before building."
                        + (warnings.Length > 0 ? " " + warnings : "");
                }
                catch (QuakeMapInterchangeException ex)
                {
                    _status.Text = "Quake source import refused: "
                        + string.Join("; ", ex.Diagnostics.Take(4)
                            .Select(d => d.Code + " line " + d.Line + " " + d.Message));
                }
                catch (Exception ex) when (ex is IOException or ArgumentException
                    or System.Text.Json.JsonException or MapAuthoringException
                    or UnauthorizedAccessException)
                { Failure(ex); }
            }, ".map");

    private void ExportEditableQuakeMap()
        => Browse("Export supported brushes, Bezier patches and gameplay as Quake .map",
            true, output =>
            {
                try
                {
                    if (_document == null)
                        throw new InvalidOperationException("Open a map before exporting.");
                    QuakeMapInterchange.Export(_document.Project.Definition, output);
                    _status.Text = "Exported editable .map and project-prime.json "
                        + "identity sidecar. Unsupported geometry is never silently flattened.";
                }
                catch (QuakeMapInterchangeException ex)
                {
                    _status.Text = "Quake export refused: "
                        + string.Join("; ", ex.Diagnostics.Take(4)
                            .Select(d => d.Code + " " + d.Message));
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException
                    or ArgumentException or MapAuthoringException
                    or UnauthorizedAccessException)
                { Failure(ex); }
            }, ".map");
}
