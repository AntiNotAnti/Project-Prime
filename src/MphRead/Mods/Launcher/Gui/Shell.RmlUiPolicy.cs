#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static readonly Dictionary<RmlUiDocumentToken, (RmlUiVisualOptions Options, int Language, string? Route)> _nativePresentationPolicy = new();
    private static readonly List<RmlUiDocumentToken> _retiredNativePolicies = new(16);

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

        void Apply(RmlUiDocumentToken document, string? route)
        {
            var host = RmlUiPrototype.Runtime;
            if (!host.IsAlive(document) || !host.IsVisible(document)) return;
            var policy = (options, language, route);
            bool known = _nativePresentationPolicy.TryGetValue(document, out var previous);
            if (known && previous == policy) return;
            if (!known)
            {
                // Modal churn must not retain policies for retired native documents.
                _retiredNativePolicies.Clear();
                foreach (var token in _nativePresentationPolicy.Keys)
                    if (!host.IsAlive(token)) _retiredNativePolicies.Add(token);
                foreach (var token in _retiredNativePolicies) _nativePresentationPolicy.Remove(token);
                _retiredNativePolicies.Clear();
            }
            RmlUiVisualPolicy.Apply(host, document, options);
            RmlUiChromeLocalization.Apply(host, document, language);
            RmlUiChromeRoutePolicy.Apply(host, document, route);
            _nativePresentationPolicy[document] = policy;
        }
    }
}
#endif
