using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private bool _poppedOut;
    private static string CommunitySettingsPath => Path.Combine(LauncherPrefs.Directory, "map-community.txt");
    private static string UserMapLibrary => CustomRooms.UserMapDirectory;

    private static async Task<T> WithCommunityAuthentication<T>(string address,
        CancellationToken token, Func<MapCommunityClient, Task<T>> action)
    {
        string credential = await HunterLicenseClient.GetCommunityMapTicketAsync(token);
        using (var client = new MapCommunityClient(address, credential))
        {
            try { return await action(client); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) { }
        }

        // A 401 cannot be repaired by simply pressing the button again if the
        // cached ticket is still alive. Force a mint and retry the operation once.
        credential = await HunterLicenseClient.RefreshCommunityMapTicketAsync(token);
        using var retry = new MapCommunityClient(address, credential);
        try { return await action(retry); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new HttpRequestException(
                "The Community service rejected a freshly minted Hunter License credential. "
                + "Redeploy/restart the map service so it includes Hunter License ticket verification.",
                ex, HttpStatusCode.Unauthorized);
        }
    }

    private async Task PopOut()
    {
#if MPHREAD_SHELL
        if (_poppedOut || _work != null) return;
        if (Shell.StudioWindow) { _status.Text = "This is already a separate editor window. Resize or maximize it using the window controls."; return; }
        string? project = null;
        try
        {
            _poppedOut=true;SetBusy(true);
            _autosave.Dispose();await _autosave.Completion;
            if (_document != null)
            {
                project = _path.Text ?? throw new IOException("Choose a project filename before opening the editor window.");
                _document.Save(project);
                _document.DiscardRecovery(CustomRooms.UserMapDirectory);
            }
            string executable = Environment.ProcessPath ?? throw new IOException("Cannot locate the application executable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute=false, WorkingDirectory=AppContext.BaseDirectory };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ProjectPrime.dll"));
            start.ArgumentList.Add("-mapstudio");
            if (!string.IsNullOrWhiteSpace(project)) { start.ArgumentList.Add("-studioproject"); start.ArgumentList.Add(Path.GetFullPath(project)); }
            using var child = Process.Start(start) ?? throw new IOException("Could not open Map Studio.");
            _poppedOut=true; SetBusy(true);
            _status.Text="Editing in the separate Map Studio window. Close it to resume editing here.";
            await child.WaitForExitAsync();
            if (project != null && File.Exists(project))
            {
                Load(MapProjectSerializer.Load(project), project);
                _status.Text=child.ExitCode==0 ? "Editor window closed. Reloaded the saved project; unsaved edits are available through recovery." : $"Editor process exited with code {child.ExitCode}. Your saved project has been reloaded.";
            }
        }
        catch (Exception ex) { Failure(ex); }
        finally { _poppedOut=false; _autosave=new(); SetBusy(false); }
#else
        _status.Text="Separate editor windows require the desktop build.";
        await Task.CompletedTask;
#endif
    }

    private Task PrepareOnline()
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 540 };
        panel.Children.Add(Text("CUSTOM MAP ONLINE PLAY"));
        panel.Children.Add(Text("Published maps download automatically for joining players. Guests can publish without registering. Your local guest identity owns the map; link an account to recover ownership on another device."));
        var address = new TextBox { Text = MapCommunityClient.DefaultAddress };
        if (File.Exists(CommunitySettingsPath)) address.Text = File.ReadAllText(CommunitySettingsPath).Trim();
        panel.Children.Add(address);
        void Start(bool publish, bool listed)
        {
            string endpoint = address.Text ?? "";
            Dismiss(); _ = PreparePublishedOnline(publish, listed, endpoint);
        }
        AddButton(panel, "Host published version", () => Start(false, true));
        AddButton(panel, "Publish & Host", () => Start(true, true));
        AddButton(panel, "Host Unlisted", () => Start(true, false));
        AddButton(panel, "Cancel", Dismiss); Modal(panel);
        return Task.CompletedTask;
    }

    private Task PreparePublishedOnline(bool publish, bool listed, string address) => Work("Preparing map for online play", async (project, token) =>
    {
        if (!GameFiles.Ready) throw new IOException("Set up game files before hosting a map.");
        GameFiles.ApplyPaths();
        EnsureMapInstallationAllowed();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(project), path, token);
            GuardJob(token);
            using var package = new MapPackageReader(path);
            var manifest = package.Manifest!;
            string hash = MapBuildFingerprint.HashFile(path);
            if (publish)
            {
                await WithCommunityAuthentication(address, token,
                    client => client.UploadAsync(path, token, listed, progress: (sent, total) =>
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            _status.Text = $"Uploading map · {sent / 1048576d:0.0}/{total / 1048576d:0.0} MiB · {(total > 0 ? sent * 100d / total : 0):0}%")));
            }
            else
            {
                using var community = new MapCommunityClient(address);
                var published = await community.GetPackageAsync(hash,token);
                if (published == null || published.MapId != manifest.MapId || published.ContentHash != manifest.ContentHash)
                    throw new IOException("This exact version is not published. Choose Publish & Host or Host Unlisted.");
            }
            GuardJob(token);
            Directory.CreateDirectory(LauncherPrefs.Directory);
            File.WriteAllText(CommunitySettingsPath, address.Trim());
            var identity = MapContentIdentity.FromPackage(path);
            using var prepared = await MapPackageInstaller.PrepareAsync(path, identity, token);
            GuardJob(token);
            var installed = prepared.Commit(UserMapLibrary, cancellation: token);
            Metadata.RegisterDownloadedMap(installed);
            _status.Text="Exact package published and installed. The lobby will advertise this Community service to joining players.";
            HostRequested?.Invoke(this, installed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }

    });

    private static void EnsureMapInstallationAllowed()
    {
        MapRuntimeUsage.RequireInstallationAllowed();
    }

    private void ShowCommunity() => ShowCommunityDashboard();
}
