#if MPHREAD_RMLUI_ANDROID
using System;
using System.IO;
using Android.Content;

namespace MphRead.Droid;

internal static class AndroidRmlUiAssets
{
    // Native RmlUi uses std::filesystem; APK assets are not filesystem paths.
    // Refresh from this installed package before the owner starts, using atomic
    // file replacements so a killed extraction cannot leave a partial font.
    internal static string Extract(Context context)
    {
        string root = Path.Combine(context.FilesDir!.AbsolutePath, "rmlui");
        Copy(context, "rmlui", root);
        if (!File.Exists(Path.Combine(root, "prime_home.rml")))
            throw new InvalidOperationException("The Android RmlUi documents are missing from the installed package.");
        return root;
    }
    private static void Copy(Context context, string asset, string output)
    {
        string[] children = context.Assets!.List(asset) ?? Array.Empty<string>();
        if (children.Length > 0)
        {
            Directory.CreateDirectory(output);
            foreach (string child in children) Copy(context, asset + "/" + child, Path.Combine(output, child));
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".new";
        using (Stream source = context.Assets.Open(asset))
        using (var destination = File.Create(temporary)) source.CopyTo(destination);
        File.Move(temporary, output, overwrite: true);
    }
}
#endif
