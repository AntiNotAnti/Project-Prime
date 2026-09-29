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

    private Task PrepareOnline()
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 540 };
        panel.Children.Add(Text("CUSTOM MAP ONLINE PLAY"));
        panel.Children.Add(Text("Joining players need this exact package in their configured Community library."));
        var address = new TextBox { Text = MapCommunityClient.DefaultAddress };
        if (File.Exists(CommunitySettingsPath)) address.Text = File.ReadAllText(CommunitySettingsPath).Trim();
        var uploadToken = new TextBox { PlaceholderText = "Upload token (not saved)", PasswordChar = '●' };
        panel.Children.Add(address); panel.Children.Add(uploadToken);
        void Start(bool publish, bool listed)
        {
            string endpoint = address.Text ?? ""; string? credential = uploadToken.Text;
            Dismiss(); _ = PreparePublishedOnline(publish, listed, endpoint, credential);
        }
        AddButton(panel, "Host published version", () => Start(false, true));
        AddButton(panel, "Publish & Host", () => Start(true, true));
        AddButton(panel, "Host Unlisted", () => Start(true, false));
        AddButton(panel, "Cancel", Dismiss); Modal(panel);
        return Task.CompletedTask;
    }

    private Task PreparePublishedOnline(bool publish, bool listed, string address, string? uploadToken) => Work("Preparing map for online play", async (project, token) =>
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
            using var community = new MapCommunityClient(address, uploadToken);
            if (publish)
                await community.UploadAsync(path, token, listed);
            else
            {
                var published = await community.GetPackageAsync(hash,token);
                if (published == null || published.MapId != manifest.MapId || published.ContentHash != manifest.ContentHash)
                    throw new IOException("This exact version is not published. Choose Publish & Host or Host Unlisted.");
            }
            GuardJob(token);
            Directory.CreateDirectory(LauncherPrefs.Directory);
            File.WriteAllText(CommunitySettingsPath, address.Trim());
            var installed = await Task.Run(() => MapPackageInstaller.Install(path, manifest.MapId,
                manifest.ContentHash, MapBuildFingerprint.HashFile(path), UserMapLibrary), token);
            GuardJob(token);
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

    private void ShowCommunity()
    {
        var panel=new StackPanel { Spacing=8, MinWidth=620 };
        panel.Children.Add(Text("COMMUNITY MAPS"));
        panel.Children.Add(Text("Connect to your community library to share maps. Lobbies prepare the exact required package automatically."));
        var address=new TextBox { Text=MapCommunityClient.DefaultAddress, PlaceholderText="Community address · https://maps.example.com/" };
        try { if(File.Exists(CommunitySettingsPath)) address.Text=File.ReadAllText(CommunitySettingsPath).Trim(); } catch(IOException) { }
        var credential=new TextBox { PlaceholderText="Creator token (My Maps, favorites, reports, publishing; not saved)", PasswordChar='●' };
        panel.Children.Add(address); panel.Children.Add(credential);
        var search=new TextBox { PlaceholderText="Search map, author, or version" }; panel.Children.Add(search);
        var list=new ListBox { Height=230 }; panel.Children.Add(list);
        var shareLink=new TextBox { IsReadOnly=true,PlaceholderText="Select a map for its downloadable package link" };
        panel.Children.Add(shareLink);
        var details = Text("Select a map to view its version and installation status."); panel.Children.Add(details);
        list.SelectionChanged+=(_,_)=>
        {
            if(list.SelectedItem is not CommunityMap chosen || !MapCommunityClient.ValidHash(chosen.Hash)) { shareLink.Text=""; return; }
            shareLink.Text=(address.Text??"").TrimEnd('/')+"/packages/"+chosen.Hash;
            string installed = CustomRooms.Installed.TryGet(chosen.MapId,out var local)
                ? local.Identity.PackageHash.ToString()==chosen.Hash ? "Exact version installed" : "Different version installed · install this version to update" : "Not installed";
            details.Text=$"{chosen.DisplayName ?? chosen.Name} · v{chosen.Version ?? "1"}\n{chosen.Author ?? "Unknown author"} · {chosen.MinPlayers}–{chosen.MaxPlayers} players · {chosen.Bytes/1024:N0} KiB\n"
                + (chosen.SupportedModes.Length==0 ? "" : string.Join(", ",chosen.SupportedModes)+"\n")
                + $"Owner: {chosen.OwnerId} · {chosen.FavoriteCount} favorites · {(chosen.Favorited?"★ Favorited":"☆")} · {(chosen.Draft?"Draft":chosen.Listed?"Published":"Unlisted")}\n"
                + installed + (chosen.MinimumProtocol>Network.NetConfig.ProtocolVersion ? "\nRequires a newer Project Prime version." : "");
        };
        var message=Text("Enter a library address and select Refresh.");panel.Children.Add(message);
        var buttons=new WrapPanel();panel.Children.Add(buttons);
        var scope=new ComboBox{ItemsSource=new[]{"Community","My Maps","My favorites"},SelectedIndex=0};panel.Children.Insert(4,scope);
        var visibility=new ComboBox{ItemsSource=new[]{"All","Published","Unlisted","Draft"},SelectedIndex=0};panel.Children.Insert(5,visibility);
        var sorting=new ComboBox{ItemsSource=new[]{"Name","Favorites","Newest"},SelectedIndex=0};panel.Children.Insert(6,sorting);
        CommunityMap[] entries=Array.Empty<CommunityMap>();
        void Filter() => list.ItemsSource=entries.Where(m=>m.ToString().Contains(search.Text??"",StringComparison.OrdinalIgnoreCase))
            .Where(m=>visibility.SelectedIndex==0||visibility.SelectedIndex==1&&m.Listed&&!m.Draft||visibility.SelectedIndex==2&&!m.Listed&&!m.Draft||visibility.SelectedIndex==3&&m.Draft).ToArray();
        visibility.SelectionChanged+=(_,_)=>Filter();
        System.Threading.Tasks.Task<CommunityMap[]> FetchEntries(MapCommunityClient client,System.Threading.CancellationToken token)=>client.BrowseAsync(token,scope.SelectedIndex==1,scope.SelectedIndex==2,sorting.SelectedIndex==1?"favorites":sorting.SelectedIndex==2?"new":"name");
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
            try { using var client=Client(); entries=await FetchEntries(client,token);GuardJob(token);Filter();message.Text=$"{entries.Length} maps available."; }
            catch(Exception ex) { message.Text=ex.Message; throw; }
            finally { buttons.IsEnabled=true; }
        }));
        AddButton(buttons,"Favorite / unfavorite",()=>_=Job("Updating favorite",async token=>
        {
            if(list.SelectedItem is not CommunityMap map)throw new InvalidOperationException("Select a map first.");
            using var client=Client();await client.SetFavoriteAsync(map.MapId,!map.Favorited,token);entries=await FetchEntries(client,token);GuardJob(token);Filter();message.Text=map.Favorited?"Favorite removed.":"Map favorited.";
        }));
        AddButton(buttons,"Report map",()=>
        {
            if(list.SelectedItem is not CommunityMap map){message.Text="Select a map first.";return;}
            var report=new StackPanel{Spacing=8};report.Children.Add(Text("REPORT · "+(map.DisplayName??map.Name)));
            var reason=new ComboBox{ItemsSource=MapCreatorCatalog.ReportReasons,SelectedIndex=0};var reportDetails=new TextBox{AcceptsReturn=true,Height=100,MaxLength=4000};report.Children.Add(reason);report.Children.Add(reportDetails);
            AddButton(report,"Submit report",()=>_=Job("Submitting report",async token=>{using var client=Client();await client.ReportAsync(map.MapId,new(map.Version,(string)reason.SelectedItem!,reportDetails.Text??""),token);GuardJob(token);Dismiss();_status.Text="Report submitted for moderation.";}));
            AddButton(report,"Cancel",Dismiss);Modal(report);
        });
        foreach(string state in new[]{"Published","Unlisted","Draft"})AddButton(buttons,"Set "+state,()=>_=Job("Updating visibility",async token=>
        {
            if(list.SelectedItem is not CommunityMap map)throw new InvalidOperationException("Select a map first.");using var client=Client();await client.SetVisibilityAsync(map.Hash,state,token);entries=await FetchEntries(client,token);GuardJob(token);Filter();message.Text="Visibility updated.";
        }));
        AddButton(buttons,"Upload current",()=>_=Work("Publishing map",async(project,token)=>
        {
            buttons.IsEnabled=false;message.Text="Building and uploading map…";
            string temporary=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".ppmap");
            try
            {
                using var client=Client();
                await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(project),temporary,token);GuardJob(token);
                var published=await client.UploadAsync(temporary,token,listed:visibility.SelectedIndex!=2&&visibility.SelectedIndex!=3,draft:visibility.SelectedIndex==3);GuardJob(token);
                entries=await FetchEntries(client,token);GuardJob(token);Filter();list.SelectedItem=entries.FirstOrDefault(e=>e.Hash==published.Hash);
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
