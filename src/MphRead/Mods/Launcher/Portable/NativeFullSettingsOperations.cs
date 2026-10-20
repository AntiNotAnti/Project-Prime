#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using MphRead.Mods.Settings;
namespace MphRead.Mods.Launcher;

internal sealed partial class NativeFullSettingsController
{
    private Task<string>? _operation;
    private CancellationTokenSource? _arenaCancellation;
    private int _arenaStage;
    internal bool ImportingArenas => _arenaCancellation != null && _operation?.IsCompleted != true;
    internal void CancelArenaImport()
    {
        try { _arenaCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        Status = "Cancelling local import; the previous bundle stays active.";
    }
    private sealed class ArenaProgress(Action<Import.ImportPublishStage> report) : IProgress<Import.ImportPublishStage>
    { public void Report(Import.ImportPublishStage stage) => report(stage); }
    private int _confirmationAction;
    internal bool Busy=>_operation!=null||_arenaPicking;
    internal string Confirmation { get; private set; }="";
    internal string ArchivePath { get; set; }=Path.Combine(LauncherPrefs.Directory,"ProjectPrime-settings.zip");
    internal string ProfileName { get; set; }="My controller";
    internal string InstallationSummary=>Maintenance.PerformanceSummary(_settings)+"\n"+Maintenance.StorageSummary();
    internal bool Operation(int action)
    {
        if(Busy||ConfirmingVideo)return false;
        if(action==24){CancelBinding();Confirmation="";return true;}
        if(action==134)return CycleControllerDevice();
        if(action==135)return ResolveBinding("Replace");
        if(action==136)return ResolveBinding("Keep Both");
        if(action==137){CycleCaptureModifier();return true;}
        if(action==23){if(Confirmation.Length==0)return false;if(_confirmationAction==25)return ConfirmBindingSwap();action=_confirmationAction;Confirmation="";}
        else if(action is 7 or 8)
        {
            if(Dirty){Status="Apply or discard changes before replacing saved settings.";return true;}
            _confirmationAction=action;
            Confirmation=action==7?"Import this settings archive? Saved preferences and controls in the archive will be replaced. Restart is required.":"Reset saved preferences, controls, controller profiles and HUD profiles? Your accounts, saves, game files and content are kept. Restart is required.";
            return true;
        }
        if(action is 6 or 7 or 8 or >=16 and <=22 && Dirty){Status="Apply or discard changes before this operation.";return true;}
        try
        {
            switch(action)
            {
                case 5: Status=Update.DesktopUpdate.VerifyInstallation();break;
                case 6: SettingsArchive.ExportFile(LauncherPrefs.Directory,ArchivePath,Program.Version.ToString());Status="Settings exported to "+ArchivePath;break;
                case 7: using(var stream=File.OpenRead(ArchivePath))SettingsPersistence.Replace(()=>SettingsArchive.Import(LauncherPrefs.Directory,stream));Status="Settings imported. Restart now or return later to activate them.";break;
                case 8: SettingsPersistence.Replace(()=>SettingsArchive.Reset(LauncherPrefs.Directory));Status="Settings reset. Restart to activate defaults.";break;
                case 9: Directory.CreateDirectory(LauncherPrefs.Directory);Process.Start(new ProcessStartInfo(LauncherPrefs.Directory){UseShellExecute=true});break;
                case 10: Start(()=>Maintenance.CleanReproducibleData().Summary);break;
                case 11: Start(()=>Maintenance.ClearMapBuildCache().Summary);break;
                case 12: _operation=RebuildThumbnails();Status="Rebuilding map thumbnails…";break;
                case 13:
                    foreach(var row in Rows.Where(r=>r.Category=="Graphics"))
                    {
                        var property=typeof(MenuSettings).GetProperties().FirstOrDefault(p=>System.Text.RegularExpressions.Regex.Replace(p.Name,"(?<=[a-z])([A-Z])"," $1")==row.Label);
                        if(property!=null)row.Value=(string)property.GetValue(new MenuSettings())!;
                    }
                    Status="Performance defaults staged. Apply Changes to save.";break;
                case 14: _operation=PerformanceDiagnostic();Status="Running performance diagnostic…";break;
                case 16: Input.GamepadProfiles.Save(ProfileName);Status=Input.GamepadProfiles.Status;break;
                case 17: Input.GamepadProfiles.Load(ProfileName);Status="Controller profile loaded. Reopen Settings to edit the new active profile.";RebaseControls();break;
                case 18: Input.GamepadProfiles.Export(ProfileName,ArchivePath);Status="Controller profile exported.";break;
                case 19: ProfileName=Input.GamepadProfiles.Import(ArchivePath);Status="Controller profile imported: "+ProfileName;break;
                case 20:
                    if(Input.GamepadManager.ActiveDevice is { } device){Input.GamepadProfiles.Assign(ProfileName,device);Status="Controller profile assigned to this device.";}else Status="Connect a controller first.";break;
                case 21:
                    if(Input.GamepadManager.ActiveDevice is { } active){Input.GamepadProfiles.Unassign(active);Status="Controller device assignment removed.";}else Status="Connect a controller first.";break;
                case 22: SettingsArchivePlatform.Restart();break;
                case 26:case 27:case 28:CalibrationAction(action);break;
                case 31: Status=LogArchive.Create(ArchivePath,out string logError)?"Logs exported to "+ArchivePath:"Could not export logs: "+logError;break;
                default:return false;
            }
        }
        catch(Exception ex){Status="Operation failed: "+ex.Message;}
        return true;
    }
    private void RebaseControls(){_runtime=Input.GamepadRuntimeConfig.Current;RebaseBindings();foreach(var row in Rows.Where(r=>r.Category=="Controls"||r.BindingAction!=null)){string value=row.Read();row.Saved=row.Value=row.Choices.FirstOrDefault(c=>c.Equals(value,StringComparison.OrdinalIgnoreCase))??value;}}
    private void Start(Func<string> action){Status="Working…";_operation=Task.Run(action);}
    private void TickOperation()
    {
        if(_operation?.IsCompleted!=true)
        {
            if (ImportingArenas && !_arenaCancellation!.IsCancellationRequested)
                Status = "Local arena import: " + ((Import.ImportPublishStage)Volatile.Read(ref _arenaStage)).ToString().ToLowerInvariant() + "…";
            return;
        }
        try{Status=_operation.GetAwaiter().GetResult();}
        catch(OperationCanceledException){Status="Local import cancelled. Previous bundle is unchanged.";}
        catch(Exception ex){Status="Operation failed: "+ex.Message;}
        _operation=null;
        _arenaCancellation=null;
    }
    private static async Task<string> RebuildThumbnails()
    {
        if(!ThumbnailHost.CanRender)return "Thumbnail rendering is not available on this platform.";
        await Task.Run(Maintenance.ClearThumbnails).ConfigureAwait(false);
        int written=await ThumbnailHost.RenderMissingAsync(line=>DebugLog.Line("thumbnails",line)).ConfigureAwait(false);
        return "Map thumbnails rebuilt: "+written+" rendered; "+ThumbnailGenerator.MissingThumbnails().Count+" unavailable.";
    }
    private static async Task<string> PerformanceDiagnostic()
    {
        string executable=Environment.ProcessPath??throw new InvalidOperationException("Cannot locate the application.");
        string directory=Path.Combine(LauncherPrefs.Directory,"perf");Directory.CreateDirectory(directory);
        string output=Path.Combine(directory,"perf-manual-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");
        var start=new ProcessStartInfo(executable){WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Diagnostics.RuntimeApplicationImage.AssemblyPath(typeof(NativeFullSettingsController).Assembly));
        foreach(string arg in new[]{"-perfcheck","MP3 PROVING GROUND","-seconds","20","-hz","144","-output",output})start.ArgumentList.Add(arg);
        using var child=Process.Start(start)??throw new IOException("Could not start performance diagnostic.");
        var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().ConfigureAwait(false);await stdout.ConfigureAwait(false);string error=await stderr.ConfigureAwait(false);
        return child.ExitCode==0&&File.Exists(output)?"Performance report saved to "+output:"Performance diagnostic failed: "+error.Trim();
    }
}
#endif
