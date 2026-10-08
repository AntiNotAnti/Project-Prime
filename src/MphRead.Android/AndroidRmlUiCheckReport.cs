#if MPHREAD_RMLUI_ANDROID_CHECK
using System;
using System.IO;
using Android.Content;

namespace MphRead.Droid;

// Check reports contain assertion names/counts only. App-specific external
// storage lets the owned emulator collect a trimmed Release APK's evidence
// without making that APK debuggable or exposing application data.
internal static class AndroidRmlUiCheckReport
{
    internal static void Write(Context context, string name, string report)
    {
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("A check report requires a plain filename.", nameof(name));
        File.WriteAllText(Path.Combine(context.FilesDir!.AbsolutePath, name), report);
        using var directory = context.GetExternalFilesDir(null);
        if (directory == null) throw new IOException("App-specific external check storage is unavailable.");
        File.WriteAllText(Path.Combine(directory.AbsolutePath, name), report);
    }
}
#endif
