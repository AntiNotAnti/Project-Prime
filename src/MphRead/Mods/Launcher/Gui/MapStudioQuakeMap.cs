using System;
using System.IO;
using MphRead.Mods.MapGen;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private void ImportEditableQuakeMap()
        => Browse("Import supported editable Quake .map brush source",
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
                    _status.Text = "Imported " + result.Definition.Geometry.Count
                        + " editable axial brush(es) and "
                        + result.Definition.Spawns.Count
                        + " spawn(s). Save to a new .json project before building.";
                }
                catch (QuakeMapInterchangeException ex)
                {
                    _status.Text = "Quake source import refused: "
                        + string.Join("; ", ex.Diagnostics.Take(4)
                            .Select(d => d.Code + " line " + d.Line + " " + d.Message));
                }
                catch (Exception ex) when (ex is IOException or ArgumentException
                    or InvalidDataException)
                { Failure(ex); }
            }, ".map");

    private void ExportEditableQuakeMap()
        => Browse("Export supported boxes/spawns as editable Quake .map",
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
                    or ArgumentException or InvalidDataException)
                { Failure(ex); }
            }, ".map");
}
