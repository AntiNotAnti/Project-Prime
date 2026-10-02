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
        panel.Children.Add(address);
        panel.Children.Add(Text("Guests can publish, favorite, report, and manage My Maps without registering. Account linking is optional and enables recovery on another device."));
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
        var scope=new ComboBox{ItemsSource=new[]{"Community","My Maps","My favorites"},SelectedIndex=0};panel.Children.Insert(5,scope);
        var visibility=new ComboBox{ItemsSource=new[]{"All","Published","Unlisted","Draft"},SelectedIndex=0};panel.Children.Insert(6,visibility);
        var sorting=new ComboBox{ItemsSource=new[]{"Name","Favorites","Newest"},SelectedIndex=0};panel.Children.Insert(7,sorting);
        CommunityMap[] entries=Array.Empty<CommunityMap>();
        void Filter() => list.ItemsSource=entries.Where(m=>m.ToString().Contains(search.Text??"",StringComparison.OrdinalIgnoreCase))
            .Where(m=>visibility.SelectedIndex==0||visibility.SelectedIndex==1&&m.Listed&&!m.Draft||visibility.SelectedIndex==2&&!m.Listed&&!m.Draft||visibility.SelectedIndex==3&&m.Draft).ToArray();
        visibility.SelectionChanged+=(_,_)=>Filter();
        System.Threading.Tasks.Task<CommunityMap[]> FetchEntries(MapCommunityClient client,System.Threading.CancellationToken token)=>client.BrowseAsync(token,scope.SelectedIndex==1,scope.SelectedIndex==2,sorting.SelectedIndex==1?"favorites":sorting.SelectedIndex==2?"new":"name");
        search.TextChanged+=(_,_)=>Filter();
        async System.Threading.Tasks.Task<MapCommunityClient> Client(bool authenticated,System.Threading.CancellationToken token)
        {
            string endpoint=(address.Text??"").Trim();
            string? ticket=authenticated?await HunterLicenseClient.GetCommunityMapTicketAsync(token):null;
            Directory.CreateDirectory(LauncherPrefs.Directory);
            File.WriteAllText(CommunitySettingsPath,endpoint);
            return new MapCommunityClient(endpoint,ticket);
        }
        AddButton(buttons,"Refresh",()=>_=Job("Loading community maps",async token=>
        {
            buttons.IsEnabled=false;message.Text="Loading maps…";
            try { using var client=await Client(scope.SelectedIndex!=0,token); entries=await FetchEntries(client,token);GuardJob(token);Filter();message.Text=$"{entries.Length} maps available."; }
            catch(Exception ex) { message.Text=ex.Message; throw; }
            finally { buttons.IsEnabled=true; }
        }));
        AddButton(buttons,"Favorite / unfavorite",()=>_=Job("Updating favorite",async token=>
        {
            if(list.SelectedItem is not CommunityMap map)throw new InvalidOperationException("Select a map first.");
            using var client=await Client(true,token);await client.SetFavoriteAsync(map.MapId,!map.Favorited,token);entries=await FetchEntries(client,token);GuardJob(token);Filter();message.Text=map.Favorited?"Favorite removed.":"Map favorited.";
        }));
        AddButton(buttons,"Report map",()=>
        {
            if(list.SelectedItem is not CommunityMap map){message.Text="Select a map first.";return;}
            var report=new StackPanel{Spacing=8};report.Children.Add(Text("REPORT · "+(map.DisplayName??map.Name)));
            var reason=new ComboBox{ItemsSource=MapCreatorCatalog.ReportReasons,SelectedIndex=0};var reportDetails=new TextBox{AcceptsReturn=true,Height=100,MaxLength=4000};report.Children.Add(reason);report.Children.Add(reportDetails);
            AddButton(report,"Submit report",()=>_=Job("Submitting report",async token=>{using var client=await Client(true,token);await client.ReportAsync(map.MapId,new(map.Version,(string)reason.SelectedItem!,reportDetails.Text??""),token);GuardJob(token);Dismiss();_status.Text="Report submitted for moderation.";}));
            AddButton(report,"Cancel",Dismiss);Modal(report);
        });
        foreach(string state in new[]{"Published","Unlisted","Draft"})AddButton(buttons,"Set "+state,()=>_=Job("Updating visibility",async token=>
        {
            if(list.SelectedItem is not CommunityMap map)throw new InvalidOperationException("Select a map first.");using var client=await Client(true,token);await client.SetVisibilityAsync(map.Hash,state,token);entries=await FetchEntries(client,token);GuardJob(token);Filter();message.Text="Visibility updated.";
        }));
        AddButton(buttons,"Upload current",()=>_=Work("Publishing map",async(project,token)=>
        {
            buttons.IsEnabled=false;message.Text="Building and uploading map…";
            string temporary=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".ppmap");
            try
            {
                string endpoint=(address.Text??"").Trim();
                Directory.CreateDirectory(LauncherPrefs.Directory);File.WriteAllText(CommunitySettingsPath,endpoint);
                await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(project),temporary,token);GuardJob(token);
                var result=await WithCommunityAuthentication(endpoint,token,async client=>
                {
                    var published=await client.UploadAsync(temporary,token,
                        listed:visibility.SelectedIndex!=2&&visibility.SelectedIndex!=3,
                        draft:visibility.SelectedIndex==3,
                        progress:(sent,total)=>Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            message.Text=$"Uploading map… {sent/1048576d:0.0}/{total/1048576d:0.0} MiB · {(total>0?sent*100d/total:0):0}%"));
                    var refreshed=await FetchEntries(client,token);
                    return (Published:published,Entries:refreshed);
                });
                GuardJob(token);entries=result.Entries;Filter();list.SelectedItem=entries.FirstOrDefault(e=>e.Hash==result.Published.Hash);
                message.Text="Published under your Hunter License. Lobbies can download this exact version automatically.";
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
                    EnsureMapInstallationAllowed();GameFiles.ApplyPaths();using var client=await Client(map.Draft,token);
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
