namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Player-facing match transition facts. Empty metadata stays visibly unknown;
/// callers must not manufacture progress or substitute internal diagnostics.
/// </summary>
internal sealed record MatchTransitionState(
    MatchTransitionStage Stage,
    string? Map = null,
    string? Mode = null,
    string? Hunter = null,
    string? Detail = null);
