#if MPHREAD_AVALONIA
using System;
using Avalonia.Controls;
namespace MphRead.Mods.Launcher.Gui
{
    // The Library thumbnail ownership smoke check still uses this helper.
    // All cosmetic equip/draft controls now live in Hunters, not License.
    internal sealed partial class LicenseWorkspace
    {
        internal static Image CreateCosmeticThumbnail(string path)
        {
            var image = new Image { Height = 118, Stretch = Avalonia.Media.Stretch.Uniform };
            image.AttachedToVisualTree += (_, _) =>
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(path);
                    image.Source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 144);
                }
                catch (Exception ex) { Mods.DebugLog.Line("ui", $"Cosmetic thumbnail unavailable: {ex.Message}"); }
            };
            image.DetachedFromVisualTree += (_, _) =>
            {
                var bitmap = image.Source as Avalonia.Media.Imaging.Bitmap;
                image.Source = null;
                bitmap?.Dispose();
            };
            return image;
        }
    }
}
#endif
