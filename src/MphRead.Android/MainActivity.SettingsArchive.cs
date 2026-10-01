using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using MphRead.Mods.Settings;

namespace MphRead.Droid;

public partial class MainActivity
{
    private const int SettingsDocumentRequest = 0x5071;
    private TaskCompletionSource<Stream?>? _settingsDocument;
    private bool _settingsDocumentExport;

    private void InstallSettingsArchiveServices()
    {
        SettingsArchivePlatform.PickDocument = PickSettingsDocument;
        SettingsArchivePlatform.RestartApplication = RestartForSettings;
    }

    private Task<Stream?> PickSettingsDocument(bool export)
    {
        if (_settingsDocument != null) throw new InvalidOperationException("A settings document picker is already open.");
        var completion = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _settingsDocument = completion;
        _settingsDocumentExport = export;
        RunOnUiThread(() =>
        {
            try
            {
                using var intent = new Intent(export ? Intent.ActionCreateDocument : Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("application/zip");
                if (export) intent.PutExtra(Intent.ExtraTitle, "ProjectPrime-settings.zip");
                StartActivityForResult(intent, SettingsDocumentRequest);
            }
            catch (Exception ex) { _settingsDocument = null; completion.TrySetException(ex); }
        });
        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SettingsDocumentRequest) return;
        var completion = _settingsDocument;
        _settingsDocument = null;
        if (completion == null) return;
        try
        {
            if (resultCode != Result.Ok || data?.Data == null) { completion.TrySetResult(null); return; }
            Stream? stream = _settingsDocumentExport
                ? ContentResolver?.OpenOutputStream(data.Data, "wt")
                : ContentResolver?.OpenInputStream(data.Data);
            if (stream == null) throw new IOException("The selected document could not be opened.");
            if (!completion.TrySetResult(stream)) stream.Dispose();
        }
        catch (Exception ex) { completion.TrySetException(ex); }
    }

    private void DisposeSettingsArchiveServices()
    {
        _settingsDocument?.TrySetResult(null);
        _settingsDocument = null;
        if (Instance == this)
        {
            SettingsArchivePlatform.PickDocument = null;
            SettingsArchivePlatform.RestartApplication = null;
        }
    }

    private void RestartForSettings()
    {
        // A recreated Activity would retain stale static preference state. Ask the
        // OS to launch a fresh process; a non-exact alarm needs no special permission.
        using var intent = PackageManager?.GetLaunchIntentForPackage(PackageName!)
            ?? throw new InvalidOperationException("Cannot locate the launcher activity.");
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        using var pending = PendingIntent.GetActivity(this, SettingsDocumentRequest + 1, intent,
            PendingIntentFlags.CancelCurrent | PendingIntentFlags.Immutable)
            ?? throw new InvalidOperationException("Cannot schedule application restart.");
        using var alarm = GetSystemService(AlarmService) as AlarmManager
            ?? throw new InvalidOperationException("Android restart service is unavailable.");
        alarm.Set(AlarmType.ElapsedRealtime, SystemClock.ElapsedRealtime() + 500, pending);
        FinishAffinity();
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
    }
}
