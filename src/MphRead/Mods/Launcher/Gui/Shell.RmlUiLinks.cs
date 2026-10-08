#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using System.Collections.Concurrent;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static readonly ConcurrentQueue<LauncherRoute> _nativeRoutes = new();
    private static bool _nativeCommandLineRouteApplied;

    private static bool QueueNativeDroppedDocument(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.ToLowerInvariant() is not (".json" or ".ppmap" or ".ppdemo" or ".ppclip")) return false;
        var route = new LauncherRoute(LauncherPage.StudioLaunch, path);
        if (!LauncherRouteCatalog.IsValid(route) || _nativeRoutes.Count >= 32) return false;
        _nativeRoutes.Enqueue(route);
        return true;
    }

    private static void PumpNativeRoutes()
    {
        if (!RmlUiPrototype.Active || _window?.HasScene == true) return;
        if (!_nativeCommandLineRouteApplied)
        {
            _nativeCommandLineRouteApplied = true;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 1; index < arguments.Length; index++)
            {
                string? value = arguments[index].StartsWith("prime://launcher/", StringComparison.OrdinalIgnoreCase)
                    ? arguments[index] : arguments[index].Equals("-ui-route", StringComparison.OrdinalIgnoreCase)
                        && index + 1 < arguments.Length ? arguments[++index] : null;
                if (value == null) continue;
                if (LauncherRouteCatalog.TryParse(value, out LauncherRoute route)) _nativeRoutes.Enqueue(route);
                else RmlUiPrototype.SetMenuText("system_status", "The requested launcher link is invalid.");
            }
        }
        if (!NativeSetupBlocksNavigation && _nativeRoutes.TryPeek(out var pending) && RmlUiPrototype.Pages?.Manager.ModalCount == 0)
        {
            _nativeRoutes.TryDequeue(out _);
            if (pending.Page == LauncherPage.Hunters) OpenNativeHunters();
            else if (pending.Page == LauncherPage.Home || pending.Page == LauncherPage.Play)
            {
                RmlUiIntentKind kind = RmlUiIntentKind.Navigate;
                HandleNativePageIntent(new(kind, pending.Page == LauncherPage.Play ? 1 : 0,
                    RmlUiPrototype.Pages?.Manager.Top ?? default, 0));
            }
            else if (!OpenNativePage(pending.Page, item: pending.Item))
                RmlUiPrototype.SetMenuText("system_status", "That launcher destination is unavailable.");
        }
    }
}
#endif
