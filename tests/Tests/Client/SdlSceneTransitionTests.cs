using System.Collections.Generic;
using System;
using MphRead;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class SdlSceneTransitionTests
{
    [Fact]
    public void EverySubmittedFrameAcknowledgesPresentationButCallbackIsFirstFrameOnly()
    {
        var events = new List<string>();
        var notification = new SceneFirstFrameNotification(73, generation =>
        {
            events.Add($"callback:{generation}");
        });

        Assert.True(notification.Notify(() => events.Add("presentation")));
        Assert.False(notification.Notify(() => events.Add("presentation")));
        Assert.Equal(new[] { "presentation", "callback:73", "presentation" }, events);
    }

    [Fact]
    public void SceneExitDefaultsToHidingStandaloneHost()
    {
        Assert.Equal(SceneExitPresentation.HideWindow, default(SceneExitPresentation));
        Assert.NotEqual(SceneExitPresentation.HideWindow, SceneExitPresentation.KeepWindowVisible);
    }

    [Fact]
    public void FirstFrameNotificationRequiresGeneration()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneFirstFrameNotification(0, _ => { }));
}
