#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

internal sealed class AndroidRmlUiVisualPolicy
{
    private readonly Dictionary<RmlUiDocumentToken, (RmlUiVisualOptions, int)> _applied = new();
    private readonly Dictionary<RmlUiDocumentToken, string> _routes = new();
    internal void ApplyRoute(RmlUiHost host, RmlUiDocumentToken document, string? route)
    {
        if (!host.IsAlive(document) || !host.IsVisible(document)) return;
        string selection = RmlUiChromeRoutePolicy.ActiveNavigation(route);
        if (_routes.TryGetValue(document, out string? previous) && previous == selection) return;
        RmlUiChromeRoutePolicy.Apply(host, document, route);
        _routes[document] = selection;
    }
    internal void Apply(RmlUiHost host, MenuSettings settings, params RmlUiDocumentToken[] documents)
    {
        var options = new RmlUiVisualOptions(LauncherPrefs.ReduceMotion, LauncherPrefs.HighContrast,
            LauncherPrefs.LargeText, true);
        int language = Enum.TryParse(settings.Language, out Language selected) ? (int)selected : 0;
        foreach (var document in documents)
        {
            if (!host.IsAlive(document) || !host.IsVisible(document)) continue;
            if (_applied.TryGetValue(document, out var previous) && previous == (options, language)) continue;
            RmlUiVisualPolicy.Apply(host, document, options);
            RmlUiChromeLocalization.Apply(host, document, language);
            _applied[document] = (options, language);
        }
        if (_applied.Count > 64)
        {
            var retired = new List<RmlUiDocumentToken>();
            foreach (var document in _applied.Keys) if (!host.IsAlive(document)) retired.Add(document);
            foreach (var document in retired) { _applied.Remove(document); _routes.Remove(document); }
        }
    }
}
#endif
