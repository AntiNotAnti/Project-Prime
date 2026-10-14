using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
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

    private async Task CompileNetRadiantSourceAsync(string source)
    {
        try
        {
            string compiler = Environment.GetEnvironmentVariable(
                "PROJECT_PRIME_Q3MAP2_EXE") ?? "";
            string name = new string(Path.GetFileNameWithoutExtension(source)
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '_'
                    ? char.ToUpperInvariant(ch) : '_')
                .Take(32).ToArray());
            if (string.IsNullOrWhiteSpace(name)) name = "RADIANT_ARENA";
            string destination = Path.Combine(CustomRooms.MapDirectory, name);
            _status.Text = "Compiling Quake .map using configured external q3map2…";
            NetRadiantBridge.CompileResult result = await NetRadiantBridge.CompileImportAsync(
                compiler, source, name, destination, CancellationToken.None);
            if (!result.Succeeded || result.ProjectPath == null)
            {
                _status.Text = "NetRadiant conversion failed: "
                    + string.Join("; ", result.Diagnostics.Take(5));
                return;
            }
            Open(result.ProjectPath);
            _status.Text = "Imported Quake BSP from editable NetRadiant source. "
                + "Validate geometry and gameplay before building .ppmap.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException
            or InvalidDataException or PlatformNotSupportedException)
        { Failure(ex); }
    }
}
