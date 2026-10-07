#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static readonly Dictionary<RmlUiDocumentToken, (RmlUiVisualOptions Options, int Language, string Route)> _nativePresentationPolicy = new();

    private static void ApplyNativePresentationPolicy()
    {
        if (!RmlUiPrototype.Active || !RmlUiPrototype.Visible) return;
        var options = new RmlUiVisualOptions(LauncherPrefs.ReduceMotion, LauncherPrefs.HighContrast,
            LauncherPrefs.LargeText, LauncherPrefs.TouchTargets);
        int language = Enum.TryParse(_settings.Language, out Language selected) ? (int)selected : 0;
        Apply(RmlUiPrototype.Runtime.HomeDocument, "home");
        if (RmlUiPrototype.Pages is { } pages)
        { Apply(pages.Manager.Page, pages.Manager.PageKey); Apply(pages.Manager.Top, pages.Manager.PageKey); }
        if (_nativeHunters?.Active == true) Apply(_nativeHunters.Document, "hunters");
        if (_nativeAdmin?.Active == true) Apply(_nativeAdmin.Document, "lobby");

        void Apply(RmlUiDocumentToken document, string route)
        {
            var host = RmlUiPrototype.Runtime;
            if (!host.IsAlive(document) || !host.IsVisible(document)) return;
            var policy = (options, language, route);
            if (_nativePresentationPolicy.TryGetValue(document, out var previous) && previous == policy) return;
            RmlUiVisualPolicy.Apply(host, document, options);
            RmlUiChromeLocalization.Apply(host, document, language);
            RmlUiChromeRoutePolicy.Apply(host, document, route);
            _nativePresentationPolicy[document] = policy;
        }
    }
}
#endif
