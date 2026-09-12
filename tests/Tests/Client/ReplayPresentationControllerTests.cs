using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ReplayPresentationControllerTests
{
    [Fact]
    public void NoncontiguousReelTransitionWaitsForSeekAndUsesFortyEightFrames()
    {
        ReplayHighlight next = Highlight(900, 960, 1020);
        var transition = new ReplayReelTransition();

        transition.Begin(next);

        Assert.Equal(ReplayReelTransitionPhase.FadeOut, transition.Phase);
        Assert.Equal(next.Label, transition.TitleCard?.Label);
        Assert.Equal(next.Focus, transition.TitleCard?.Focus);
        Assert.Null(transition.TitleCard?.FocusName);
        for (int i = 1; i < ReplayReelTransition.FadeOutFrames; i++)
        {
            Assert.Equal(ReplayReelTransitionAction.None,
                transition.Advance(seekComplete: false));
        }
        Assert.Equal(ReplayReelTransitionAction.BeginNextRange,
            transition.Advance(seekComplete: false));
        Assert.Equal(ReplayReelTransitionPhase.Seeking, transition.Phase);
        Assert.Equal(1f, transition.Coverage);

        for (int i = 0; i < 120; i++)
        {
            Assert.Equal(ReplayReelTransitionAction.None,
                transition.Advance(seekComplete: false));
            Assert.Equal(ReplayReelTransitionPhase.Seeking, transition.Phase);
        }

        transition.SetFocusName("SYLUX");
        Assert.Equal("SYLUX", transition.TitleCard?.FocusName);
        transition.Advance(seekComplete: true);
        Assert.Equal(ReplayReelTransitionPhase.TitleCard, transition.Phase);
        for (int i = 0; i < ReplayReelTransition.TitleCardFrames; i++)
            transition.Advance(seekComplete: true);
        Assert.Equal(ReplayReelTransitionPhase.FadeIn, transition.Phase);
        for (int i = 0; i < ReplayReelTransition.FadeInFrames; i++)
            transition.Advance(seekComplete: true);

        Assert.Equal(48, ReplayReelTransition.IntendedFrames);
        Assert.Equal(ReplayReelTransitionPhase.None, transition.Phase);
        Assert.False(transition.IsActive);
        Assert.Equal(0f, transition.Coverage);
        Assert.Null(transition.TitleCard);
    }

    [Fact]
    public void TransitionCoverageFadesOutAndBackInDeterministically()
    {
        var transition = new ReplayReelTransition();
        transition.Begin(Highlight(900, 960, 1020));
        float previous = transition.Coverage;
        for (int i = 1; i < ReplayReelTransition.FadeOutFrames; i++)
        {
            transition.Advance(seekComplete: false);
            Assert.True(transition.Coverage > previous);
            previous = transition.Coverage;
        }
        transition.Advance(seekComplete: false);
        transition.Advance(seekComplete: true);
        for (int i = 0; i < ReplayReelTransition.TitleCardFrames; i++)
            transition.Advance(seekComplete: true);

        previous = transition.Coverage;
        for (int i = 1; i < ReplayReelTransition.FadeInFrames; i++)
        {
            transition.Advance(seekComplete: true);
            Assert.True(transition.Coverage < previous);
            previous = transition.Coverage;
        }
    }

    [Fact]
    public void ResetDropsPendingTitleAndPresentationState()
    {
        var transition = new ReplayReelTransition();
        transition.Begin(Highlight(900, 960, 1020));
        transition.SetFocusName("NOXUS");

        transition.Reset();

        Assert.Equal(ReplayReelTransitionPhase.None, transition.Phase);
        Assert.Null(transition.TitleCard);
        Assert.Equal(0f, transition.Coverage);
    }

    private static ReplayHighlight Highlight(uint start, uint focus, uint end)
    {
        const ReplayMarker markers = ReplayMarker.MultiKill;
        return new ReplayHighlight(start, focus, end, authoritativeTick: focus,
            new CombatActor(2, 202, 3), HighlightKind.DoubleKill,
            HighlightScoringPolicy.BaseScore(HighlightKind.DoubleKill), markers,
            HighlightScoringPolicy.Label(HighlightKind.DoubleKill, markers));
    }
}
