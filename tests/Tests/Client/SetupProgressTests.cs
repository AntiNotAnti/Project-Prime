using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class SetupProgressTests
{
    [Fact]
    public void CartridgeReadIsReportedBeforeFileExtraction()
    {
        var progress = new SetupProgress();

        Assert.True(progress.Observe("Reading cartridge dump..."));

        Assert.Equal("Reading cartridge dump", progress.Stage);
        Assert.InRange(progress.Fraction, 0.001, 0.03);
    }

    [Fact]
    public void UpdateQueueCoalescesOutputBurstAndPresentsLatestState()
    {
        var scheduled = new List<Action>();
        string status = "";
        double fraction = 0;
        string stage = "";
        var updates = new SetupProgressUpdateQueue("", scheduled.Add,
            (nextStatus, nextFraction, nextStage) =>
            {
                status = nextStatus;
                fraction = nextFraction;
                stage = nextStage;
            });

        updates.Report("Writing files/AMHE1...");
        updates.Report("Writing files/AMHE1/data...");
        updates.Report("Reading models...");

        Assert.Single(scheduled);
        scheduled[0]();
        Assert.Equal("Writing files/AMHE1...\nWriting files/AMHE1/data...\nReading models...",
            status);
        Assert.Equal("Unpacking archives", stage);
        Assert.True(fraction >= 0.40);
    }

    [Fact]
    public void FinishUpdatesAlreadyQueuedPresentationAndRejectsLateOutput()
    {
        var scheduled = new List<Action>();
        string status = "";
        double fraction = 0;
        string stage = "";
        var updates = new SetupProgressUpdateQueue("No game files yet", scheduled.Add,
            (nextStatus, nextFraction, nextStage) =>
            {
                status = nextStatus;
                fraction = nextFraction;
                stage = nextStage;
            });

        updates.Report("Writing files/AMHE1...");
        updates.Finish(ok: true, "Ready to play.");
        updates.Report("late output");

        Assert.Single(scheduled);
        scheduled[0]();
        Assert.Equal(1, fraction);
        Assert.Equal("Ready to play", stage);
        Assert.EndsWith("Ready to play.", status, StringComparison.Ordinal);
        Assert.DoesNotContain("late output", status, StringComparison.Ordinal);
    }
}
