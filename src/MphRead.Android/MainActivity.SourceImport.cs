using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using MphRead.Mods.Import;

namespace MphRead.Droid;

public partial class MainActivity
{
    private const string SourcePickerHighWater = "prime.source-picker-high-water";
    private SourceDocumentPickerSession? _sourcePicker;
    private SourceDocumentPlatform.Registration? _sourceDocuments;

    private void InstallSourceDocuments(Bundle? savedState)
    {
        SourceDocumentPickerSession.RestoreHighWater(savedState?.GetInt(SourcePickerHighWater,
            SourceDocumentPickerSession.FirstCode - 1) ?? SourceDocumentPickerSession.FirstCode - 1);
        _sourcePicker = new();
        using var files = new Java.IO.File(FilesDir!.AbsolutePath);
        string root = files.CanonicalPath;
        var owner = new WeakReference<MainActivity>(this);
        _sourceDocuments = SourceDocumentPlatform.Install(token => owner.TryGetTarget(out var activity)
            ? activity.PickSourceDocument(token) : Task.FromException<OwnedSourceDocument?>(new InvalidOperationException("Source picker unavailable.")),
            Path.Combine(root, "source-import-scratch"));
    }
    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutInt(SourcePickerHighWater, SourceDocumentPickerSession.HighWater);
        base.OnSaveInstanceState(outState);
    }
    private Task<OwnedSourceDocument?> PickSourceDocument(CancellationToken token)
    {
        var session = _sourcePicker ?? throw new InvalidOperationException("Source picker unavailable.");
        var request = session.Begin(token);
        RunOnUiThread(() => session.TryLaunch(request.Code, () =>
        {
            if (_settingsDocument != null) throw new InvalidOperationException("Close the settings document chooser first.");
            using var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivityForResult(intent, request.Code);
        }));
        return request.Task;
    }
    private bool CompleteSourceDocument(int code, Android.App.Result result, Intent? data)
    {
        if (code < SourceDocumentPickerSession.FirstCode || code > SourceDocumentPickerSession.LastCode) return false;
        _sourcePicker?.TryComplete(code, () =>
        {
            if (result != Android.App.Result.Ok || data?.Data == null) return null;
            // Capture application scope and an opaque URI only. Opening and
            // copying happen on a worker after the view/activity token check.
            string uri = data.Data.ToString()!;
            var resolver = ApplicationContext!.ContentResolver!;
            return new OwnedSourceDocument(token => OpenSourceDocument(resolver, uri, token));
        });
        return true; // Stale restored/retired chooser results are drained too.
    }
    private static async Task<Stream> OpenSourceDocument(ContentResolver resolver, string uri, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var signal = new CancellationSignal();
        object gate = new(); Task? abort = null; bool retired = false;
        var registration = token.Register(() =>
        {
            // Android may synchronously call a remote provider from Cancel.
            // Keep that work off the view's cancelling thread too, and await
            // its retirement before disposing the Java signal.
            lock (gate) if (!retired) abort ??= Task.Run(() => { try { signal.Cancel(); } catch (Exception) { } });
        });
        try
        {
            var descriptor = resolver.OpenAssetFileDescriptor(Android.Net.Uri.Parse(uri)!, "r", signal)
                ?? throw new IOException("The selected source could not be opened.");
            try
            {
                // The auto-close stream takes descriptor ownership. JNI peer
                // disposal alone does not close a descriptor on a failed open.
                Stream stream = descriptor.CreateInputStream()
                    ?? throw new IOException("The selected source is not readable.");
                descriptor.Dispose();
                return stream;
            }
            catch { try { descriptor.Close(); } finally { descriptor.Dispose(); } throw; }
        }
        finally
        {
            registration.Dispose();
            Task? closing;
            lock (gate) { retired = true; closing = abort; }
            if (closing != null) await closing.ConfigureAwait(false);
        }
    }
    private void DisposeSourceDocuments()
    {
        _sourceDocuments?.Dispose(); _sourceDocuments = null;
        _sourcePicker?.Dispose(); _sourcePicker = null;
    }
}
