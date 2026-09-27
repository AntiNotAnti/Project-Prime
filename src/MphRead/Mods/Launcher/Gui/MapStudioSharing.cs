using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private bool _poppedOut;
    private static string CommunitySettingsPath => Path.Combine(LauncherPrefs.Directory, "map-community.txt");
    private static string UserMapLibrary => CustomRooms.UserMapDirectory;

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

    private Task PrepareOnline() => Work("Preparing map for online play", async (project, token) =>
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
            var installed = await Task.Run(() => MapPackageInstaller.Install(path, manifest.MapId,
                manifest.ContentHash, MapBuildFingerprint.HashFile(path), UserMapLibrary), token);
            GuardJob(token);
            Metadata.RegisterDownloadedMap(installed);
            _status.Text="Map installed. Choose this computer to host; players can install the same package from Community.";
            HostRequested?.Invoke(this, installed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }

    });

    private static void EnsureMapInstallationAllowed()
    {
        if (Network.NetSession.Active) throw new IOException("Leave the current online session before installing or replacing maps.");
    }

    private void ShowCommunity()
    {
        var panel=new StackPanel { Spacing=8, MinWidth=620 };
        panel.Children.Add(Text("COMMUNITY MAPS"));
        panel.Children.Add(Text("Connect to your community library to share maps. Install the same version before joining an online lobby."));
        var address=new TextBox { Text=MapCommunityClient.DefaultAddress, PlaceholderText="Community address · https://maps.example.com/" };
        try { if(File.Exists(CommunitySettingsPath)) address.Text=File.ReadAllText(CommunitySettingsPath).Trim(); } catch(IOException) { }
        var credential=new TextBox { PlaceholderText="Upload token (only needed to publish; not saved)", PasswordChar='●' };
        panel.Children.Add(address); panel.Children.Add(credential);
        var search=new TextBox { PlaceholderText="Search map, author, or version" }; panel.Children.Add(search);
        var list=new ListBox { Height=230 }; panel.Children.Add(list);
        var shareLink=new TextBox { IsReadOnly=true,PlaceholderText="Select a map for its downloadable package link" };
        panel.Children.Add(shareLink);
        list.SelectionChanged+=(_,_)=>shareLink.Text=list.SelectedItem is CommunityMap chosen && MapCommunityClient.ValidHash(chosen.Hash)
            ? (address.Text??"").TrimEnd('/')+"/maps/"+chosen.Hash : "";
        var message=Text("Enter a library address and select Refresh.");panel.Children.Add(message);
        var buttons=new WrapPanel();panel.Children.Add(buttons);
        CommunityMap[] entries=Array.Empty<CommunityMap>();
        void Filter() => list.ItemsSource=entries.Where(m=>m.ToString().Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).ToArray();
        search.TextChanged+=(_,_)=>Filter();
        MapCommunityClient Client()
        {
            var client=new MapCommunityClient(address.Text??"",credential.Text);
            Directory.CreateDirectory(LauncherPrefs.Directory);
            File.WriteAllText(CommunitySettingsPath,address.Text!.Trim());return client;
        }
        AddButton(buttons,"Refresh",()=>_=Job("Loading community maps",async token=>
        {
            buttons.IsEnabled=false;message.Text="Loading maps…";
            try { using var client=Client(); entries=await client.BrowseAsync(token);GuardJob(token);Filter();message.Text=$"{entries.Length} maps available."; }
            catch(Exception ex) { message.Text=ex.Message; throw; }
            finally { buttons.IsEnabled=true; }
        }));
        AddButton(buttons,"Upload current",()=>_=Work("Publishing map",async(project,token)=>
        {
            buttons.IsEnabled=false;message.Text="Building and uploading map…";
            string temporary=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".ppmap");
            try
            {
                using var client=Client();
                await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(project),temporary,token);GuardJob(token);
                var published=await client.UploadAsync(temporary,token);GuardJob(token);
                entries=await client.BrowseAsync(token);GuardJob(token);Filter();list.SelectedItem=entries.FirstOrDefault(e=>e.Hash==published.Hash);
                message.Text="Published. Share this library address and select the same map version on each computer.";
            }
            catch(Exception ex) { message.Text=ex.Message;throw; }
            finally { buttons.IsEnabled=true;if(File.Exists(temporary))File.Delete(temporary); }
        }));
        void Install(bool host)
        {
            if(list.SelectedItem is not CommunityMap map) { message.Text="Select a map first.";return; }
            _=Job("Installing community map",async token=>
            {
                buttons.IsEnabled=false;message.Text="Downloading and validating map…";
                try
                {
                    if(!GameFiles.Ready)throw new IOException("Set up game files before installing playable maps.");
                    EnsureMapInstallationAllowed();GameFiles.ApplyPaths();using var client=Client();
                    var installed=await client.InstallAsync(map,UserMapLibrary,token);GuardJob(token);
                    Metadata.RegisterDownloadedMap(installed);message.Text="Installed. This map is available in the map picker.";
                    if(host){Dismiss();HostRequested?.Invoke(this,installed);}
                }
                catch(Exception ex) { message.Text=ex.Message;throw; }
                finally { buttons.IsEnabled=true; }
            });
        }
        AddButton(buttons,"Install",()=>Install(false));AddButton(buttons,"Install & host",()=>Install(true));
        AddButton(buttons,"Import .ppmap",()=>Browse("Install map package",false,path=>_=Job("Installing map package",async token=>
        {
            if(!GameFiles.Ready)throw new IOException("Set up game files before installing playable maps.");
            EnsureMapInstallationAllowed();GameFiles.ApplyPaths();
            using var package=new MapPackageReader(path);
            var manifest=package.Manifest??throw new IOException("Rebuild this legacy package in Map Studio before sharing.");
            var installed=await Task.Run(()=>MapPackageInstaller.Install(path,manifest.MapId,manifest.ContentHash,
                MapBuildFingerprint.HashFile(path),UserMapLibrary),token);GuardJob(token);
            Metadata.RegisterDownloadedMap(installed);message.Text="Installed "+installed.Name;
        }),".ppmap"));
        AddButton(buttons,"Close",Dismiss);Modal(panel);
    }
}
