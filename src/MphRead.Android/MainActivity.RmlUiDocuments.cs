#if MPHREAD_RMLUI_ANDROID
using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;

namespace MphRead.Droid;

public partial class MainActivity
{
    private const int NativeRomRequest = 0x5073;
    private TaskCompletionSource<Stream?>? _nativeRomDocument;
    private Task<Stream?> PickNativeRomDocument()
    {
        var completion = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() =>
        {
            if (_destroyed) { completion.TrySetCanceled(); return; }
            if (_nativeRomDocument != null || _settingsDocument != null)
            { completion.TrySetException(new InvalidOperationException("A document picker is already open.")); return; }
            _nativeRomDocument = completion;
            try
            {
                using var intent = new Intent(Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable); intent.SetType("*/*");
                StartActivityForResult(intent, NativeRomRequest);
            }
            catch (Exception ex) { _nativeRomDocument = null; completion.TrySetException(ex); }
        });
        return completion.Task;
    }
    private bool HandleNativeDocumentResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != NativeRomRequest) return false;
        var completion = _nativeRomDocument; _nativeRomDocument = null;
        if (completion == null) return true;
        try
        {
            if (resultCode != Result.Ok || data?.Data == null) { completion.TrySetResult(null); return true; }
            var stream = ContentResolver?.OpenInputStream(data.Data) ?? throw new IOException("The selected ROM document could not be opened.");
            if (!completion.TrySetResult(stream)) stream.Dispose();
        }
        catch (Exception ex) { completion.TrySetException(ex); }
        return true;
    }
    private void DisposeNativeDocumentServices() { _nativeRomDocument?.TrySetResult(null); _nativeRomDocument = null; }
}
#else
namespace MphRead.Droid;
public partial class MainActivity
{
    private bool HandleNativeDocumentResult(int requestCode, Android.App.Result resultCode, Android.Content.Intent? data) => false;
    private void DisposeNativeDocumentServices() { }
}
#endif
