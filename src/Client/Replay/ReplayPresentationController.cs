using System;
using MphRead.Entities;
using MphRead.Hud;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead;

internal enum ReplayReelTransitionPhase
{
    None,
    FadeOut,
    Seeking,
    TitleCard,
    FadeIn
}

internal enum ReplayReelTransitionAction
{
    None,
    BeginNextRange
}

internal readonly record struct ReplayReelTitleCard(
    string Label,
    CombatActor Focus,
    uint ReplayFrame,
    string? FocusName = null);

/// <summary>
/// Pure presentation clock for the gap between two noncontiguous replay
/// ranges. It never advances simulation; the playback session remains the
/// sole owner of seek and silent historical warmup.
/// </summary>
internal sealed class ReplayReelTransition
{
    internal const int FadeOutFrames = 12;
    internal const int TitleCardFrames = 24;
    internal const int FadeInFrames = 12;
    internal const int IntendedFrames = FadeOutFrames + TitleCardFrames
        + FadeInFrames;

    private int _remainingFrames;

    internal ReplayReelTransitionPhase Phase { get; private set; }
    internal ReplayReelTitleCard? TitleCard { get; private set; }
    internal bool IsActive => Phase != ReplayReelTransitionPhase.None;
    internal float Coverage => Phase switch
    {
        ReplayReelTransitionPhase.FadeOut => Math.Clamp(
            (FadeOutFrames - _remainingFrames + 1f) / FadeOutFrames, 0, 1),
        ReplayReelTransitionPhase.Seeking
            or ReplayReelTransitionPhase.TitleCard => 1,
        ReplayReelTransitionPhase.FadeIn => Math.Clamp(
            _remainingFrames / (float)FadeInFrames, 0, 1),
        _ => 0
    };

    internal void Begin(in ReplayHighlight next)
    {
        if (IsActive) throw new InvalidOperationException(
            "A replay reel transition is already active.");
        TitleCard = new ReplayReelTitleCard(next.Label, next.Focus,
            next.FocusFrame);
        Phase = ReplayReelTransitionPhase.FadeOut;
        _remainingFrames = FadeOutFrames;
    }

    internal ReplayReelTransitionAction Advance(bool seekComplete)
    {
        switch (Phase)
        {
        case ReplayReelTransitionPhase.None:
            return ReplayReelTransitionAction.None;
        case ReplayReelTransitionPhase.FadeOut:
            if (--_remainingFrames > 0) return ReplayReelTransitionAction.None;
            Phase = ReplayReelTransitionPhase.Seeking;
            _remainingFrames = 0;
            return ReplayReelTransitionAction.BeginNextRange;
        case ReplayReelTransitionPhase.Seeking:
            if (!seekComplete) return ReplayReelTransitionAction.None;
            Phase = ReplayReelTransitionPhase.TitleCard;
            _remainingFrames = TitleCardFrames;
            return ReplayReelTransitionAction.None;
        case ReplayReelTransitionPhase.TitleCard:
            if (--_remainingFrames > 0) return ReplayReelTransitionAction.None;
            Phase = ReplayReelTransitionPhase.FadeIn;
            _remainingFrames = FadeInFrames;
            return ReplayReelTransitionAction.None;
        case ReplayReelTransitionPhase.FadeIn:
            if (--_remainingFrames > 0) return ReplayReelTransitionAction.None;
            Reset();
            return ReplayReelTransitionAction.None;
        default:
            throw new InvalidOperationException("Unknown replay reel transition phase.");
        }
    }

    internal void SetFocusName(string name)
    {
        if (TitleCard is not ReplayReelTitleCard card
            || String.IsNullOrWhiteSpace(name)) return;
        TitleCard = card with { FocusName = name };
    }

    internal void Reset()
    {
        Phase = ReplayReelTransitionPhase.None;
        TitleCard = null;
        _remainingFrames = 0;
    }
}

/// <summary>
/// Keeps replay presentation concerns at the render boundary.  Playback may
/// be owned by an isolated session or by the default Theatre facade; the
/// presentation-facing behavior is identical in either case.
/// </summary>
internal sealed class ReplayPresentationController : IDisposable
{
    private readonly ScenePresentation _presentation;
    private readonly ReplayPlaybackSession? _session;
    private readonly Action<ScenePresentation>? _priorOverlay;
    private readonly Action<ScenePresentation> _overlay;
    private readonly ReplayReelTransition _transition = new();
    private object? _replayIdentity;
    private object? _focusedIdentity;
    private int _focusedHighlightIndex = -1;
    private ReplayHighlight? _focusedHighlight;
    private object? _focusedRangeIdentity;
    private CombatActor? _focusedRangeActor;
    private bool _terminalEndObserved;
    private bool _ownsTransitionPause;
    private bool _transportWasPaused;

    internal ReplayPresentationController(ScenePresentation presentation,
        ReplayPlaybackSession? session = null)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _session = session;
        _priorOverlay = presentation.AdditionalOverlay;
        _overlay = DrawTransitionOverlay;
        presentation.AdditionalOverlay = _overlay;
    }

    internal bool IsReplayActive => _session?.IsActive ?? ReplayPlayback.IsActive;
    internal bool ShouldClose { get; private set; }
    internal object? ReplayIdentity => _session != null
        ? _session.SessionIdentity : ReplayPlayback.SessionIdentity;
    internal uint CurrentFrame => _session?.CurrentFrame ?? ReplayPlayback.CurrentFrame;
    internal int CurrentHighlightIndex => _session?.CurrentHighlightIndex
        ?? ReplayPlayback.CurrentHighlightIndex;
    internal ReplayHighlight? CurrentHighlight => _session != null
        ? _session.CurrentHighlight : ReplayPlayback.CurrentHighlight;
    internal uint? CurrentHighlightFocusFrame => CurrentHighlight?.FocusFrame;
    internal CombatActor? CurrentHighlightFocus => CurrentHighlight?.Focus;
    internal CombatActor? CurrentPlaybackFocus => _session?.PlaybackFocus
        ?? ReplayPlayback.PlaybackFocus;
    internal ReplayReelTransitionPhase TransitionPhase => _transition.Phase;
    internal float TransitionCoverage => _transition.Coverage;
    internal ReplayReelTitleCard? TransitionTitleCard => _transition.TitleCard;

    internal void Advance()
    {
        ShouldClose = false;
        if (!IsReplayActive)
        {
            Reset();
            return;
        }

        object? identity = ReplayIdentity;
        if (!ReferenceEquals(identity, _replayIdentity))
        {
            Reset();
            _replayIdentity = identity;
        }

        if (_transition.IsActive)
        {
            ReplayReelTransitionAction action = _transition.Advance(!IsSeeking);
            if (action == ReplayReelTransitionAction.BeginNextRange)
            {
                if (!AdvanceHighlightRange())
                {
                    _transition.Reset();
                    RestoreTransitionPause();
                }
                _terminalEndObserved = false;
            }
            UpdateTransitionFocusName();
            if (!_transition.IsActive) RestoreTransitionPause();
        }
        else if (AtEnd && NextHighlight is ReplayHighlight next)
        {
            if (CurrentHighlight is ReplayHighlight priorHighlight
                && IsNoncontiguous(priorHighlight, next))
            {
                BeginTransitionPause();
                _transition.Begin(next);
            }
            else if (AdvanceHighlightRange())
            {
                _terminalEndObserved = false;
            }
        }

        ReplayHighlight? highlight = CurrentHighlight;
        if (highlight is ReplayHighlight current)
            FocusHighlight(identity, CurrentHighlightIndex, current);
        else if (CurrentPlaybackFocus is CombatActor rangeFocus)
            FocusPlaybackRange(identity, rangeFocus);

        if (!_transition.IsActive && AtEnd && ShouldExitAtEnd)
        {
            if (_terminalEndObserved) ShouldClose = true;
            else _terminalEndObserved = true;
        }
        else if (!AtEnd)
        {
            _terminalEndObserved = false;
        }
    }

    internal void Reset()
    {
        ShouldClose = false;
        _replayIdentity = null;
        _focusedIdentity = null;
        _focusedHighlightIndex = -1;
        _focusedHighlight = null;
        _focusedRangeIdentity = null;
        _focusedRangeActor = null;
        _terminalEndObserved = false;
        _transition.Reset();
        RestoreTransitionPause();
    }

    public void Dispose()
    {
        Reset();
        if (_presentation.AdditionalOverlay == _overlay)
            _presentation.AdditionalOverlay = _priorOverlay;
    }

    private bool AtEnd => _session?.AtEnd ?? ReplayPlayback.AtEnd;
    private bool ShouldExitAtEnd => _session?.ShouldExitAtEnd
        ?? ReplayPlayback.ShouldExitAtEnd;
    private bool IsSeeking => _session?.IsSeeking ?? ReplayPlayback.IsSeeking;
    private ReplayHighlight? NextHighlight => _session != null
        ? _session.NextHighlight : ReplayPlayback.NextHighlight;
    private bool TransportPaused
    {
        get => _session?.Paused ?? ReplayPlayback.Transport.Paused;
        set
        {
            if (_session != null) _session.Paused = value;
            else ReplayPlayback.Transport.Paused = value;
        }
    }

    private bool AdvanceHighlightRange() => _session != null
        ? _session.AdvanceHighlightRange() : ReplayPlayback.AdvanceHighlightRange();

    private void FocusHighlight(object? identity, int index,
        in ReplayHighlight highlight)
    {
        if (!Mods.SpectatorMode.IsSpectating || !IsNewHighlight(identity, index, highlight))
            return;

        if (!_presentation.SpectatorCamera.FocusHighlight(_presentation, highlight))
            return;
        _focusedIdentity = identity;
        _focusedHighlightIndex = index;
        _focusedHighlight = highlight;
    }

    private bool IsNewHighlight(object? identity, int index,
        in ReplayHighlight highlight)
        => !ReferenceEquals(identity, _focusedIdentity)
            || index != _focusedHighlightIndex
            || _focusedHighlight is not ReplayHighlight focused
            || !focused.Equals(highlight);

    private void FocusPlaybackRange(object? identity, CombatActor actor)
    {
        if (!Mods.SpectatorMode.IsSpectating
            || ReferenceEquals(identity, _focusedRangeIdentity)
                && _focusedRangeActor == actor)
            return;
        if (!_presentation.SpectatorCamera.FocusReplayActor(_presentation, actor))
            return;
        _focusedRangeIdentity = identity;
        _focusedRangeActor = actor;
    }

    private bool TryGetActorName(CombatActor actor, out string? name)
        => _session != null
            ? _session.TryGetActorName(actor, out name)
            : ReplayPlayback.TryGetActorName(actor, out name);

    private void UpdateTransitionFocusName()
    {
        if (IsSeeking || _transition.TitleCard is not ReplayReelTitleCard card
            || card.FocusName != null || !card.Focus.IsValid) return;
        if (TryGetActorName(card.Focus, out string? name) && name != null)
            _transition.SetFocusName(name);
    }

    private void BeginTransitionPause()
    {
        if (_ownsTransitionPause) return;
        _transportWasPaused = TransportPaused;
        TransportPaused = true;
        _ownsTransitionPause = true;
    }

    private void RestoreTransitionPause()
    {
        if (!_ownsTransitionPause) return;
        TransportPaused = _transportWasPaused;
        _ownsTransitionPause = false;
    }

    private void DrawTransitionOverlay(ScenePresentation presentation)
    {
        _priorOverlay?.Invoke(presentation);
        float coverage = _transition.Coverage;
        if (coverage <= 0) return;
        presentation.DrawHudFlatBox(0, 0, 256, 192,
            new Vector4(0, 0, 0, coverage));
        if (_transition.Phase != ReplayReelTransitionPhase.TitleCard
            || _transition.TitleCard is not ReplayReelTitleCard card
            || presentation.World.Players.Count == 0
            || presentation.World.LocalPlayer is not { } localPlayer) return;

        PlayerPresentation hud = localPlayer.GetPresentation();
        hud.DrawText2D(128, 80, Align.Center, 0, card.Label,
            maxLength: HighlightScoringPolicy.MaximumLabelLength, scale: .72f);
        int line = 94;
        if (card.FocusName is { } focusName)
        {
            hud.DrawText2D(128, line, Align.Center, 0, focusName,
                maxLength: 28, scale: .55f);
            line += 12;
        }
        uint seconds = card.ReplayFrame / 60;
        hud.DrawText2D(128, line, Align.Center, 0,
            $"{seconds / 60:00}:{seconds % 60:00}", scale: .5f);
    }

    private static bool IsNoncontiguous(in ReplayHighlight current,
        in ReplayHighlight next)
        => next.StartFrame > current.EndFrame
            && next.StartFrame - current.EndFrame > 1;
}
