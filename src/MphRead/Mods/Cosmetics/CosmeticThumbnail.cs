using System.IO;
namespace MphRead.Mods.Cosmetics
{
    public static class CosmeticThumbnail
    {
        public static string PathFor(Hunter hunter, string key) => Path.Combine(Launcher.LauncherPrefs.Directory,
            "cosmetic-thumbnails", "v3", hunter.ToString(), key + ".png");
    }
}
