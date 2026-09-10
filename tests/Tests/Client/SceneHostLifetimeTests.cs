using System;
using System.Collections.Generic;
using Xunit;

namespace MphRead.Tests;

public sealed class SceneHostLifetimeTests
{
    [Fact]
    public void SavedWindowModeChangesApplyButUnchangedPreferencePreservesManualToggle()
    {
        var preferences = new SceneWindowModePreference();
        var applied = new List<MphRead.Mods.WindowStartMode>();
        var current = MphRead.Mods.WindowStartMode.Windowed;
        void Apply(MphRead.Mods.WindowStartMode mode) { current = mode; applied.Add(mode); }
        preferences.ApplyIfChanged(MphRead.Mods.WindowStartMode.Windowed, Apply);
        current = MphRead.Mods.WindowStartMode.BorderlessFullscreen; // Manual F11 toggle.
        preferences.ApplyIfChanged(MphRead.Mods.WindowStartMode.Windowed, Apply);
        Assert.Equal(MphRead.Mods.WindowStartMode.BorderlessFullscreen, current);
        Assert.Single(applied);
        preferences.ApplyIfChanged(MphRead.Mods.WindowStartMode.BorderlessFullscreen, Apply);
        preferences.ApplyIfChanged(MphRead.Mods.WindowStartMode.Windowed, Apply);
        Assert.Equal(MphRead.Mods.WindowStartMode.Windowed, current);
        Assert.Equal(3, applied.Count);
    }

    [Fact]
    public void OneHostConsumesSequentialScenesAndKeepsResultsBeforeCleanup()
    {
        using var host = new SceneHostLifetime();
        var events = new List<string>();
        var first = new object();
        void Run(object scene, string name) => host.Run(scene,
            () => { Assert.False(host.SceneStopRequested); events.Add(name + ":run"); host.StopScene(); },
            () => events.Add(name + ":results"), () => events.Add(name + ":cleanup"));
        Run(first, "a");
        Assert.False(host.CloseRequested);
        Run(new object(), "b");
        Assert.Equal(new[] { "a:run", "a:results", "a:cleanup", "b:run", "b:results", "b:cleanup" }, events);
        Assert.Throws<InvalidOperationException>(() => Run(first, "reused"));
        Assert.False(host.CloseRequested);
        host.Dispose();
        Assert.True(host.CloseRequested);
        Assert.Throws<ObjectDisposedException>(() => Run(new object(), "c"));
    }

    [Fact]
    public void NativeCloseIsPermanentAndCleanupStillRuns()
    {
        using var host = new SceneHostLifetime();
        int cleanup = 0;
        host.Run(new object(), host.Close, () => Assert.True(host.CloseRequested), () => cleanup++);
        Assert.Equal(1, cleanup);
        Assert.Throws<InvalidOperationException>(() => host.Run(new object(), () => { }, () => { }, () => cleanup++));
        Assert.Equal(1, cleanup);
    }

    [Fact]
    public void FailedResultsStillCleanOnceAndDoNotPermitConcurrentScenes()
    {
        using var host = new SceneHostLifetime();
        int cleanup = 0;
        Assert.Throws<ApplicationException>(() => host.Run(new object(),
            () => Assert.Throws<InvalidOperationException>(() => host.Run(new object(), () => { }, () => { }, () => { })),
            () => throw new ApplicationException(), () => cleanup++));
        host.Run(new object(), () => { }, () => { }, () => cleanup++);
        Assert.Equal(2, cleanup);
    }
}
