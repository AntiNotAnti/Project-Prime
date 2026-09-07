using System;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Demo global state")]
public sealed class ReplayTouchControlsTests
{
    [Fact]
    public void TouchTransportIsFiniteGatedAndBoundToTheRecordingSession()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-touch-{Guid.NewGuid():N}.fpdemo");
        Func<bool>? pause = ClientInputState.ReadPauseOpen;
        try
        {
            using (var writer = new DemoWriter(path, indexed: true))
            { writer.WriteRecord(0, DemoPlaybackTests.Match(1)); writer.WriteRecord(0, DemoPlaybackTests.Snapshot(1, 1, 0)); }
            Assert.True(DemoPlayback.Join(path));
            Assert.False(ReplayControls.PointerDown(float.NaN, 185));
            Assert.False(ReplayControls.QueuePointerDown(20, float.PositiveInfinity));
            Assert.True(ReplayControls.QueuePointerDown(20, 165));
            bool before = DemoPlayback.Transport.Paused;
            Assert.True(ReplayControls.ConsumeQueuedPointer()); Assert.NotEqual(before, DemoPlayback.Transport.Paused);
            Assert.True(ReplayControls.PointerDown(120, 165)); Assert.Equal(2, DemoPlayback.Transport.Rate);
            Assert.True(ReplayControls.PointerDown(80, 165)); Assert.True(DemoPlayback.Transport.Paused);
            Assert.Equal(1, DemoPlayback.Transport.TakeSteps());
            Assert.True(ReplayControls.QueuePointerDown(20, 165));
            ClientInputState.ReadPauseOpen = () => true;
            Assert.False(ReplayControls.ConsumeQueuedPointer()); Assert.False(ReplayControls.QueuePointerDown(20, 165));
            ClientInputState.ReadPauseOpen = () => false;
            Assert.False(ReplayControls.ConsumeQueuedPointer());
            ChatBox.Open(false); Assert.False(ReplayControls.PointerDown(20, 165)); ChatBox.Clear();
            Assert.True(ReplayControls.QueuePointerDown(20, 165));
            Assert.True(DemoPlayback.Join(path));
            Assert.False(ReplayControls.ConsumeQueuedPointer());
            Assert.False(DemoPlayback.Transport.Paused);
        }
        finally { ClientInputState.ReadPauseOpen = pause; ChatBox.Clear(); DemoPlayback.Stop(); File.Delete(path); }
    }
}
