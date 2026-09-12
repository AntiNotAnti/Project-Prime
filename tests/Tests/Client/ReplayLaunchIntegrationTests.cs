using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Replay global state")]
public sealed class ReplayLaunchIntegrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        $"project-prime-replay-launch-{Guid.NewGuid():N}.fpreplay");

    public ReplayLaunchIntegrationTests()
    {
        NetSession.Stop();
        WriteReplay(_path, duration: 120);
    }

    [Fact]
    public void FullReplayConsumesPreparedFileWithoutCreatingLiveSession()
    {
        Assert.True(ReplayPlayback.Prepare(_path), ReplayPlayback.LastError);

        Assert.True(ReplayLaunchCoordinator.TryStart(Plan(), out string? error), error);

        Assert.True(ReplayPlayback.IsActive);
        Assert.Equal(-1, ReplayPlayback.CurrentHighlightIndex);
        Assert.Null(ReplayPlayback.CurrentHighlight);
        Assert.False(ReplayPlayback.ShouldExitAtEnd);
        Assert.False(ReplayPlayback.ConsumePrepared(_path));
        AssertNoLiveNetworkSession();
    }

    [Fact]
    public void SingleHighlightStartsAtRequestedRangeAndTerminatesThere()
    {
        ReplayHighlight highlight = Highlight(12, 15, 22);
        StartPrepared([highlight]);

        AssertRangeStarts(highlight, expectedIndex: 0);
        PlayCurrentRangeToEnd();

        Assert.True(ReplayPlayback.AtEnd);
        Assert.True(ReplayPlayback.ShouldExitAtEnd);
        Assert.False(ReplayPlayback.AdvanceHighlightRange());
        Assert.Equal(highlight.EndFrame, ReplayPlayback.CurrentFrame);
        AssertNoLiveNetworkSession();
    }

    [Fact]
    public void ManualClipUsesABoundedRangeWithoutBecomingAHighlight()
    {
        CombatActor focus = new(7, 99, 1);
        Assert.True(ReplayPlayback.Prepare(_path), ReplayPlayback.LastError);
        LaunchPlan plan = Plan() with
        {
            ReplayStartFrame = 12,
            ReplayEndFrame = 22,
            ReplayFocus = focus
        };

        Assert.True(ReplayLaunchCoordinator.TryStart(plan, out string? error), error);
        ProcessPendingSeek();
        Assert.True(ReplayPlayback.BoundedPlayback);
        Assert.Null(ReplayPlayback.CurrentHighlight);
        Assert.Equal(12u, ReplayPlayback.CurrentFrame);
        Assert.Equal(focus.Slot, ReplayPlayback.PerspectiveSlot);

        PlayCurrentRangeToEnd();

        Assert.Equal(22u, ReplayPlayback.CurrentFrame);
        Assert.True(ReplayPlayback.ShouldExitAtEnd);
        AssertNoLiveNetworkSession();
    }

    [Fact]
    public void TwoHighlightReelAdvancesEveryRequestedRangeThenTerminates()
    {
        ReplayHighlight[] highlights =
        [
            Highlight(8, 10, 16),
            Highlight(30, 33, 40)
        ];
        StartPrepared(highlights);

        AssertReelProgression(highlights);
        AssertNoLiveNetworkSession();
    }

    [Fact]
    public void EightHighlightReelAdvancesEveryRequestedRangeThenTerminates()
    {
        var highlights = new ReplayHighlight[HighlightAnalyzer.MaximumHighlights];
        for (int i = 0; i < highlights.Length; i++)
        {
            uint start = (uint)(4 + i * 13);
            highlights[i] = Highlight(start, start + 2, start + 7);
        }
        StartPrepared(highlights);

        AssertReelProgression(highlights);
        AssertNoLiveNetworkSession();
    }

    public void Dispose()
    {
        ReplayPlayback.Stop();
        NetSession.Stop();
        File.Delete(_path);
    }

    private void StartPrepared(IReadOnlyList<ReplayHighlight> highlights)
    {
        Assert.True(ReplayPlayback.Prepare(_path), ReplayPlayback.LastError);
        Assert.True(ReplayLaunchCoordinator.TryStart(Plan(highlights),
            out string? error), error);
        AssertNoLiveNetworkSession();
    }

    private static void AssertReelProgression(
        IReadOnlyList<ReplayHighlight> highlights)
    {
        for (int i = 0; i < highlights.Count; i++)
        {
            ReplayHighlight expected = highlights[i];
            AssertRangeStarts(expected, i);
            PlayCurrentRangeToEnd();
            Assert.True(ReplayPlayback.AtEnd);

            if (i + 1 < highlights.Count)
            {
                Assert.False(ReplayPlayback.ShouldExitAtEnd);
                Assert.True(ReplayPlayback.AdvanceHighlightRange());
            }
            else
            {
                Assert.True(ReplayPlayback.ShouldExitAtEnd);
                Assert.False(ReplayPlayback.AdvanceHighlightRange());
            }
        }
    }

    private static void AssertRangeStarts(in ReplayHighlight expected,
        int expectedIndex)
    {
        ProcessPendingSeek();
        Assert.Equal(expectedIndex, ReplayPlayback.CurrentHighlightIndex);
        Assert.Equal(expected, ReplayPlayback.CurrentHighlight);
        Assert.Equal(expected.StartFrame, ReplayPlayback.CurrentFrame);
        Assert.Equal(expected.Focus.Slot, ReplayPlayback.PerspectiveSlot);
    }

    private static void PlayCurrentRangeToEnd()
    {
        int guard = 1000;
        while (!ReplayPlayback.AtEnd && guard-- > 0)
        {
            ReplayPlayback.PumpFrame();
        }
        Assert.True(guard > 0, "Replay range did not reach its configured end.");
    }

    private static void ProcessPendingSeek()
    {
        int guard = 100;
        do
        {
            Assert.True(ReplayPlayback.ProcessSeek(ReplayPlayback.PumpFrame));
        }
        while (ReplayPlayback.IsSeeking && guard-- > 0);
        Assert.True(guard > 0, "Replay seek did not complete.");
    }

    private static void AssertNoLiveNetworkSession()
    {
        Assert.Null(AuthoritativePlay.Current);
        Assert.Null(NetSession.TrafficMetrics);
        Assert.Equal(-1, NetSession.LocalSlot);
    }

    private LaunchPlan Plan(IReadOnlyList<ReplayHighlight>? highlights = null)
        => new()
        {
            Kind = LaunchKind.Replay,
            ReplayPath = _path,
            ReplayHighlights = highlights,
            Hunter = Hunter.Samus,
            PlayerName = "Replay integration"
        };

    private static ReplayHighlight Highlight(uint start, uint focus, uint end)
    {
        const ReplayMarker markers = ReplayMarker.Kill;
        return new ReplayHighlight(start, focus, end, authoritativeTick: focus,
            new CombatActor(7, 99, 1), HighlightKind.Kill,
            HighlightScoringPolicy.BaseScore(HighlightKind.Kill), markers,
            HighlightScoringPolicy.Label(HighlightKind.Kill, markers));
    }

    private static void WriteReplay(string path, uint duration)
    {
        using var writer = new ReplayWriter(path, NetHeader.Version, indexed: true);
        writer.WriteRecord(0, ReplayPlaybackTests.Match(5));
        writer.WriteRecord(0, ReplayPlaybackTests.Snapshot(5, 1, 0));
        foreach (byte[] world in ReplayPlaybackTests.LiveWorld(5))
        {
            writer.WriteRecord(0, world);
        }
        writer.WriteRecord(duration, ReplayPlaybackTests.Snapshot(5, 2, 1));
    }
}
