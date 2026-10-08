#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.RmlUi.Setup;

internal sealed class EngineSetupBackend : ISetupBackend, IDisposable
{
    private readonly Func<Task<Stream?>>? _pickRom;
    private readonly ConcurrentDictionary<string, byte> _temporary = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<SetupResult> _installerResults = new();
    private IReadOnlyList<UpdateInfo> _releases = Array.Empty<UpdateInfo>();
    private IUpdateInstaller? _preparedInstaller;
    private Action<bool, string>? _previousFinished, _ownedFinished;
    private string? _extracting;
    internal EngineSetupBackend(Func<Task<Stream?>>? pickRom = null)
    { _pickRom = pickRom; UpdateInstall.UseDesktopIfPossible(); }
    public SetupFiles Inspect()
    {
        string? problem = GameFiles.Problem();
        return new(problem == null, problem ?? GameFiles.Describe(), GameFiles.Root, Paths.MphKey,
            OperatingSystem.IsAndroid() ? _pickRom != null : NativeFilePicker.Available,
            problem == null && ThumbnailHost.CanRender, Updater.Disabled);
    }
    public async Task<string?> PickRom(CancellationToken cancel)
    {
        if (!OperatingSystem.IsAndroid()) return await NativeFilePicker.OpenFile("Choose your Metroid Prime Hunters dump", "Nintendo DS ROM", "nds").ConfigureAwait(false);
        if (_pickRom == null) throw new InvalidOperationException("The platform ROM picker is unavailable.");
        using Stream? input = await _pickRom().ConfigureAwait(false);
        if (input == null) return null;
        cancel.ThrowIfCancellationRequested();
        Directory.CreateDirectory(GameFiles.Root);
        string temporary = Path.Combine(GameFiles.Root, "prime-picked-" + Guid.NewGuid().ToString("N") + ".nds");
        try
        {
            using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long total = 0; int read;
            // DS cartridges have a bounded size. Reject an accidental huge document before filling private storage.
            while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > 1024L * 1024 * 1024) throw new InvalidDataException("The selected document is larger than a Nintendo DS ROM.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            }
            _temporary[temporary] = 0; return temporary;
        }
        catch { File.Delete(temporary); throw; }
    }
    public SetupResult Extract(string path, Action<string> report)
    {
        _extracting = path;
        try
        {
            Directory.CreateDirectory(GameFiles.Root);
            bool success = GameFiles.RunSetup(path, report);
            return new(success, success ? GameFiles.Describe() : "Setup did not finish. Read the extraction log and choose a compatible ROM dump.");
        }
        finally
        {
            _extracting = null;
            if (_temporary.TryRemove(path, out _)) { try { File.Delete(path); } catch (IOException) { } }
        }
    }
    public SetupResult ConfigureExtractedPath(string revision, string path)
    {
        string[] allowed = { Ver.AMHE0, Ver.AMHE1, Ver.AMHP0, Ver.AMHP1, Ver.AMHJ0, Ver.AMHJ1, Ver.AMHK0 };
        if (!allowed.Contains(revision, StringComparer.Ordinal)) return new(false, "Revision must be AMHE0, AMHE1, AMHP0, AMHP1, AMHJ0, AMHJ1 or AMHK0.");
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.IndexOfAny(new[] { '\r', '\n', '\0', '=' }) >= 0)
            return new(false, "Enter the full extracted game-data directory.");
        string root = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(root)) return new(false, "That extracted-data directory does not exist.");
        if (GameFiles.CoreRootProblem(root) is string problem) return new(false, problem);
        string pathsFile = Path.Combine(GameFiles.Root, "paths.txt");
        byte[]? previous = File.Exists(pathsFile) ? File.ReadAllBytes(pathsFile) : null;
        string? pending = null;
        try
        {
            Directory.CreateDirectory(GameFiles.Root);
            var lines = previous == null ? new List<string>() : File.ReadAllLines(pathsFile).ToList();
            if (lines.Count == 0) lines.Add(Program.Version.ToString());
            else if (!Version.TryParse(lines[0].Trim(), out Version? version) || version < new Version(0, 19, 0, 0))
                return new(false, "This paths file is from an older extraction. Set up a ROM again before changing its path.");
            string value = revision + "=" + root;
            // Paths loads the last duplicate entry. Replace all entries for this
            // revision so an old duplicate cannot silently override the new path.
            lines.RemoveAll(line => line.Split('=')[0].Trim() == revision); lines.Add(value);
            // Preserve other revisions, export paths and forward-compatible entries; replace atomically.
            pending = pathsFile + ".pending-" + Guid.NewGuid().ToString("N");
            File.WriteAllLines(pending, lines); File.Move(pending, pathsFile, overwrite: true); pending = null;
            GameFiles.ApplyPaths();
            if (GameFiles.Problem() is string invalid) throw new InvalidDataException(invalid);
            return new(true, GameFiles.Describe());
        }
        catch
        {
            if (previous == null) File.Delete(pathsFile); else File.WriteAllBytes(pathsFile, previous);
            GameFiles.ApplyPaths(); throw;
        }
        finally { if (pending != null) File.Delete(pending); }
    }
    public SetupResult VerifyInstallation() => new(true, DesktopUpdate.VerifyInstallation());
    public SetupResult RenderPreviews(Action<string> report)
    {
        if (!GameFiles.Ready || !ThumbnailHost.CanRender) return new(false, "Map previews are unavailable until game files and a preview renderer are ready.");
        ThumbnailHost.RenderMissingAsync(report).GetAwaiter().GetResult();
        int remaining = ThumbnailGenerator.MissingThumbnails().Count;
        return new(remaining == 0, remaining == 0 ? "All map previews are ready." : $"{remaining} map previews remain. Read the rendering log.");
    }
    private static SetupRelease Describe(UpdateInfo info)
    {
        Version? current = BuildVersion.Current is { } raw ? BuildVersion.Normalise(raw) : null;
        bool downgrade = current != null && info.Version < current;
        bool install = UpdateInstall.CanInstall(info) && !(OperatingSystem.IsAndroid() && downgrade);
        return new(info.Tag, info.Version.ToString(3), info.AssetName, info.Notes, info.PageUrl, install, downgrade);
    }
    public SetupResult CheckLatest(CancellationToken cancel)
    {
        UpdateInfo? release = Updater.Check(cancel);
        _releases = release.HasValue ? new[] { release.Value } : Array.Empty<UpdateInfo>();
        return new(true, release.HasValue ? Updater.Describe(release.Value) : UpdateCheck.LastReason ?? "No newer release is available.", _releases.Select(Describe).ToArray());
    }
    internal SetupResult UseAvailableRelease(UpdateInfo release)
    {
        _releases = new[] { release };
        return new(true, Updater.Describe(release), new[] { Describe(release) });
    }
    public SetupResult LoadReleases(CancellationToken cancel)
    {
        _releases = UpdateCheck.Releases(30, cancel);
        return new(true, _releases.Count > 0 ? "Select a published version to review its release notes." : UpdateCheck.LastReason ?? "No published versions are available.", _releases.Select(Describe).ToArray());
    }
    public SetupResult? RequestPreparePermission(int index)
    {
        if (index < 0 || index >= _releases.Count) return new(false, "That release is no longer available.");
        UpdateInfo update = _releases[index];
        if (!Describe(update).CanInstall)
            return new(false, OperatingSystem.IsAndroid() && Describe(update).Downgrade
                ? "Android downgrades require uninstalling the current app or using ADB. Open the release page for that package."
                : OperatingSystem.IsMacOS() ? "Replace the signed application bundle from the release page." : "This package requires a manual download from its release page.");
        IUpdateInstaller installer = UpdateInstall.Current!;
        if (!installer.Allowed)
        {
            bool requested = installer.RequestPermission();
            return new(false, requested ? "Allow installs from Project Prime, then return and prepare the release again." : "The system did not open installation permission settings.");
        }
        if (_preparedInstaller != null && _preparedInstaller.Finished == _ownedFinished) _preparedInstaller.Finished = _previousFinished;
        _preparedInstaller = installer; _previousFinished = installer.Finished;
        _ownedFinished = (ok, message) => _installerResults.Enqueue(new(ok, message));
        installer.Finished = _ownedFinished; return null;
    }
    public SetupResult PrepareRelease(int index, Action<float> progress)
    {
        if (_preparedInstaller == null || index < 0 || index >= _releases.Count) return new(false, "Installation permission must be checked first.");
        bool ok = _preparedInstaller.Prepare(_releases[index], progress, out string error);
        return new(ok, ok ? "The verified package is ready. Press Install and restart to finish." : error, Prepared: ok);
    }
    public SetupResult InstallPrepared(out bool exitAfterInstall)
    {
        exitAfterInstall = false;
        if (_preparedInstaller == null) return new(false, "No verified package is prepared.");
        bool ok = _preparedInstaller.Install(out string error);
        if (ok) exitAfterInstall = _preparedInstaller.ExitAfterInstall;
        return new(ok, ok ? exitAfterInstall ? "Restarting to complete the update…" : "Waiting for the system installer…" : error);
    }
    public SetupResult OpenRelease(int index)
    {
        if (index < 0 || index >= _releases.Count) return new(false, "Select a published release first.");
        UpdateInfo update = _releases[index]; bool opened = Updater.OpenPage(update);
        return new(opened, opened ? "Opened the published release page." : update.PageUrl);
    }
    public SetupResult OpenDataFolder()
    {
        if (OperatingSystem.IsAndroid()) return new(false, "Game data is in the app's private folder: " + GameFiles.Root);
        Directory.CreateDirectory(GameFiles.Root);
        Process.Start(new ProcessStartInfo(GameFiles.Root) { UseShellExecute = true });
        return new(true, "Opened the game-data folder.");
    }
    public bool TryTakeInstallerResult(out SetupResult result) => _installerResults.TryDequeue(out result!);
    public void Dispose()
    {
        if (_preparedInstaller != null && _preparedInstaller.Finished == _ownedFinished) _preparedInstaller.Finished = _previousFinished;
        foreach (string path in _temporary.Keys)
            if (path != _extracting && _temporary.TryRemove(path, out _)) { try { File.Delete(path); } catch (IOException) { } }
    }
}
#endif
