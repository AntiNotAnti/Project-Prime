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
    private string CommunitySettingsPath => Path.Combine(_services.CommunitySettingsDirectory, "map-community.txt");
    private string UserMapLibrary => _services.UserMapDirectory;

    private async Task<T> WithCommunityAuthentication<T>(string address,
        CancellationToken token, Func<MapCommunityClient, Task<T>> action)
    {
        string credential = await _services.GetCommunityTicketAsync(false,token);
        using (var client = new MapCommunityClient(address, credential))
        {
            try { return await action(client); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) { }
        }

        // A 401 cannot be repaired by simply pressing the button again if the
        // cached ticket is still alive. Force a mint and retry the operation once.
        credential = await _services.GetCommunityTicketAsync(true,token);
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
        if (_services.IsStandalone) { _status.Text = "This editor is already hosted in the desktop Studio application."; return; }
        if (_work != null || _poppedOut) return;
        try
        {
            _poppedOut = true; SetBusy(true);
            _autosave.Dispose(); await _autosave.Completion;
            if (_document != null) await SaveDocumentAsync(_path.Text ?? "", CancellationToken.None);
            string? path=_document?.FilePath;
            await _services.OpenDetachedEditorAsync(path, CancellationToken.None);
            if (path != null && File.Exists(path)) Load(MapProjectSerializer.Load(path),path);
            _status.Text="Editor window closed. The saved project was reloaded.";
        }
        catch (Exception ex) { Failure(ex); }
        finally { _poppedOut = false; _autosave=new(); SetBusy(false); }
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
        if (!_services.IsStandalone) { if (!_services.GameFilesReady) throw new IOException("Set up game files before hosting a map."); _services.ApplyGamePaths(); }
        string path = Path.Combine(_services.StagingDirectory, Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            await _services.BuildScheduler.PackageAsync(MapBuildSnapshot.Capture(project), path, token);
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
            Directory.CreateDirectory(_services.CommunitySettingsDirectory);
            File.WriteAllText(CommunitySettingsPath, address.Trim());
            var identity = MapContentIdentity.FromPackage(path);
            GuardJob(token);
            var installed = await _services.CommitPackageAsync(path,identity,token);
            _status.Text="Exact package published and installed. The lobby will advertise this Community service to joining players.";
            if (_services.IsStandalone) await _services.RequestHostAsync(path,identity,address,token); else HostRequested?.Invoke(this, installed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }

    });

    private void ShowCommunity() => ShowCommunityDashboard();
}
