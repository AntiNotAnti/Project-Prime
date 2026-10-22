using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MphRead.Mods.Launcher.RmlUi.Host;

public enum RmlUiHeaderTab { None, Play, Hunters, Community, Theatre, Studio }

/// <summary>Application-role presentation only. Routing permissions remain with the application owner.</summary>
public static class RmlUiApplicationHeader
{
    private static readonly IReadOnlyDictionary<string,RmlUiBindingValue> Game = Create(false);
    private static readonly IReadOnlyDictionary<string,RmlUiBindingValue> Studio = Create(true);
    public static IReadOnlyDictionary<string,RmlUiBindingValue> Bindings(bool isStudio) => isStudio ? Studio : Game;

    /// <summary>Safe for every visible document; headerless modal and playback documents are skipped.</summary>
    public static bool Apply(RmlUiHost host, RmlUiDocumentToken page, bool isStudio,
        RmlUiHeaderTab active = RmlUiHeaderTab.None)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.IsAlive(page) || !host.TryGetElementBounds(page,"topbar",out _,out _,out _,out _)) return false;
        // RmlUiHost caches bindings per document generation and clears them on retirement.
        // Applying each frame therefore restores role state without repeated DOM writes or stale role stamps.
        foreach (var pair in Bindings(isStudio))
            if (pair.Value.Kind == RmlUiBindingKind.Text) host.SetText(page,pair.Key,pair.Value.Text);
            else host.SetBool(page,pair.Key,pair.Value.Boolean);
        host.SetBool(page, "class:nav_play:active", active == RmlUiHeaderTab.Play && !isStudio);
        host.SetBool(page, "class:nav_hunters:active", active == RmlUiHeaderTab.Hunters && !isStudio);
        host.SetBool(page, "class:nav_community:active", active == RmlUiHeaderTab.Community && !isStudio);
        host.SetBool(page, "class:nav_theatre:active", active == RmlUiHeaderTab.Theatre);
        host.SetBool(page, "class:nav_studio:active", active == RmlUiHeaderTab.Studio && !isStudio);
        host.SetBool(page, "class:nav_studio_home:active", active == RmlUiHeaderTab.Studio && isStudio);
        return true;
    }
    private static IReadOnlyDictionary<string,RmlUiBindingValue> Create(bool studio)
    {
        var values = new Dictionary<string,RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["brand_title"] = RmlUiBindingValue.FromText(studio ? "PROJECT PRIME STUDIO" : "PROJECT PRIME"),
            ["brand_subtitle"] = RmlUiBindingValue.FromText(studio ? "REPLAY // CREATION" : "HUNTERS ONLINE"),
            ["client_label"] = RmlUiBindingValue.FromText(studio ? "PROJECT PRIME STUDIO" : "PROJECT PRIME CLIENT"),
            ["nav_theatre"] = RmlUiBindingValue.FromText(studio ? "REPLAY STUDIO" : "THEATRE"),
            ["class:screen:studio-application"] = RmlUiBindingValue.FromBoolean(studio),
            ["visible:nav_studio_home"] = RmlUiBindingValue.FromBoolean(studio),
            ["visible:nav_play"] = RmlUiBindingValue.FromBoolean(!studio),
            ["visible:nav_hunters"] = RmlUiBindingValue.FromBoolean(!studio),
            ["visible:nav_community"] = RmlUiBindingValue.FromBoolean(!studio),
            ["visible:nav_studio"] = RmlUiBindingValue.FromBoolean(!studio),
            ["visible:nav_theatre"] = RmlUiBindingValue.FromBoolean(true),
            ["visible:profile"] = RmlUiBindingValue.FromBoolean(!studio),
            ["visible:header_settings"] = RmlUiBindingValue.FromBoolean(true)
        };
        return new ReadOnlyDictionary<string,RmlUiBindingValue>(values);
    }
}
