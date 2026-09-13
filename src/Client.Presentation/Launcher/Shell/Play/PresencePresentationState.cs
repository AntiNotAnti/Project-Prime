using System;
using System.Collections.Immutable;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

public enum PresenceLoadState
{
    Unknown,
    Loading,
    Ready,
    Failed
}

/// <summary>Immutable, presentation-only public presence state.</summary>
public sealed record PresencePresentationState(PresenceLoadState State,
    int TotalOnline, int VisibleOnline,
    ImmutableArray<PublicPresenceEntry> Players, long Revision,
    DateTimeOffset GeneratedAt, string? Error = null)
{
    public static PresencePresentationState Initial { get; } = new(
        PresenceLoadState.Unknown, 0, 0,
        ImmutableArray<PublicPresenceEntry>.Empty, 0, default);

    public string BadgeText => State switch
    {
        PresenceLoadState.Loading when TotalOnline == 0 => "Checking…",
        PresenceLoadState.Ready => TotalOnline == 1 ? "● 1 ONLINE"
            : $"● {TotalOnline} ONLINE",
        PresenceLoadState.Failed when TotalOnline > 0 => TotalOnline == 1
            ? "● 1 ONLINE" : $"● {TotalOnline} ONLINE",
        PresenceLoadState.Failed => "Status unavailable",
        _ => "Offline"
    };
}
