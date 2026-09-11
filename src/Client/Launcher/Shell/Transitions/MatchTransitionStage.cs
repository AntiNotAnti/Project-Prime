using System;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Truthful milestones reported while entering or leaving a match.</summary>
public enum MatchTransitionStage
{
    Preparing,
    Connecting,
    PreparingContent,
    LoadingArena,
    PreparingPlayers,
    Finalizing,
    EnteringMatch,
    LoadingNextRound,
    ReturningToLobby,
    Reconnecting,
    Failed
}

internal static class MatchTransitionStagePresentation
{
    internal static string Label(this MatchTransitionStage stage)
        => stage switch
        {
            MatchTransitionStage.Preparing => "Preparing Hunt",
            MatchTransitionStage.Connecting => "Connecting to Server",
            MatchTransitionStage.PreparingContent => "Preparing Content",
            MatchTransitionStage.LoadingArena => "Loading Arena",
            MatchTransitionStage.PreparingPlayers => "Preparing Players",
            MatchTransitionStage.Finalizing => "Finalizing",
            MatchTransitionStage.EnteringMatch => "Entering Match",
            MatchTransitionStage.LoadingNextRound => "Preparing Next Hunt",
            MatchTransitionStage.ReturningToLobby => "Returning to Lobby",
            MatchTransitionStage.Reconnecting => "Reconnecting",
            MatchTransitionStage.Failed => "Transition Failed",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

    internal static bool StartsWorkflow(this MatchTransitionStage stage)
        => stage is MatchTransitionStage.Preparing
            or MatchTransitionStage.LoadingNextRound
            or MatchTransitionStage.ReturningToLobby
            or MatchTransitionStage.Reconnecting;
}
