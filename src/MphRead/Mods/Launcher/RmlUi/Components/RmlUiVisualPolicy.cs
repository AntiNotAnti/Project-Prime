using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Components;

public readonly record struct RmlUiVisualOptions(bool ReduceMotion, bool HighContrast,
    bool LargeText, bool TouchTargets);

/// <summary>Applies one presentation policy to both pages and independently
/// authored modals. Preferences remain owned by launcher settings.</summary>
public static class RmlUiVisualPolicy
{
    public static void Apply(RmlUiHost host, RmlUiDocumentToken document, in RmlUiVisualOptions options)
    {
        if (!host.IsAlive(document)) return;
        host.SetBool(document, "class:@document:reduce-motion", options.ReduceMotion);
        host.SetBool(document, "class:@document:high-contrast", options.HighContrast);
        host.SetBool(document, "class:@document:large-text", options.LargeText);
        host.SetBool(document, "class:@document:touch-targets", options.TouchTargets);
    }
}

/// <summary>Primary navigation reflects the document's route rather than a
/// template default. Utility pages clear the primary selection.</summary>
public static class RmlUiChromeRoutePolicy
{
    private static readonly string[] Navigation = { "nav_play", "nav_hunters", "nav_community", "nav_studio" };
    public static string ActiveNavigation(string? route) => route?.ToLowerInvariant() switch
    {
        "home" or "news" or "play" or "browser" or "lobby" or "offline" or "adventure" => "nav_play",
        "hunter" or "hunters" => "nav_hunters",
        "community" or "forge" => "nav_community",
        "studio" or "studiolaunch" or "theatre" or "theatre-playback" or "playback" => "nav_studio",
        _ => ""
    };
    public static void Apply(RmlUiHost host, RmlUiDocumentToken document, string? route)
    {
        if (!host.IsAlive(document)) return;
        string selected = ActiveNavigation(route);
        foreach (string id in Navigation)
            if (host.TryGetElementBounds(document, id, out _, out _, out _, out _))
                host.SetBool(document, "class:" + id + ":active", id == selected);
    }
}

/// <summary>Shared chrome labels use the game's existing language choice.
/// User names, community content, routes and identity keys are never translated.</summary>
public static class RmlUiChromeLocalization
{
    private static readonly string[] Ids = { "nav_play", "nav_hunters", "nav_community", "nav_studio", "profile",
        "footer_news", "footer_social", "footer_settings", "footer_quit", "client_label" };
    private static readonly string[][] Labels = {
        new[] { "PLAY", "HUNTERS", "COMMUNITY", "STUDIO", "PROFILE", "NEWS", "SOCIAL", "SETTINGS", "QUIT", "PROJECT PRIME CLIENT" },
        new[] { "プレイ", "ハンター", "コミュニティ", "スタジオ", "プロフィール", "ニュース", "ソーシャル", "設定", "終了", "PROJECT PRIME クライアント" },
        new[] { "JOUER", "CHASSEURS", "COMMUNAUTÉ", "STUDIO", "PROFIL", "ACTUALITÉS", "SOCIAL", "PARAMÈTRES", "QUITTER", "CLIENT PROJECT PRIME" },
        new[] { "JUGAR", "CAZADORES", "COMUNIDAD", "ESTUDIO", "PERFIL", "NOTICIAS", "SOCIAL", "AJUSTES", "SALIR", "CLIENTE PROJECT PRIME" },
        new[] { "SPIELEN", "JÄGER", "COMMUNITY", "STUDIO", "PROFIL", "NEUIGKEITEN", "SOZIAL", "OPTIONEN", "BEENDEN", "PROJECT PRIME CLIENT" },
        new[] { "GIOCA", "CACCIATORI", "COMUNITÀ", "STUDIO", "PROFILO", "NOTIZIE", "SOCIAL", "IMPOSTAZIONI", "ESCI", "CLIENT PROJECT PRIME" }
    };
    public static IReadOnlyDictionary<string, string> Bindings(int language)
    {
        int index = language >= 0 && language < Labels.Length ? language : 0;
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < Ids.Length; i++) bindings.Add(Ids[i], Labels[index][i]);
        return bindings;
    }
    public static void Apply(RmlUiHost host, RmlUiDocumentToken document, int language)
    {
        if (!host.IsAlive(document)) return;
        // Modal documents do not contain shared chrome. Their field drafts and
        // per-route strings stay under the presenter that authored them.
        foreach (var binding in Bindings(language))
            if (host.TryGetElementBounds(document, binding.Key, out _, out _, out _, out _)) host.SetText(document, binding.Key, binding.Value);
    }
}
