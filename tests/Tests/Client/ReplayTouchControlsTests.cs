using System;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Replay global state")]
public sealed class ReplayTouchControlsTests
{
    [Fact]
    public void TouchTransportIsFiniteGatedAndBoundToTheRecordingSession()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-touch-{Guid.NewGuid():N}.fpreplay");
        Func<bool>? pause = ClientInputState.ReadPauseOpen;
        try
        {
            using (var writer = new ReplayWriter(path, indexed: true))
            { writer.WriteRecord(0, ReplayPlaybackTests.Match(1)); writer.WriteRecord(0, ReplayPlaybackTests.Snapshot(1, 1, 0)); }
            Assert.True(ReplayPlayback.Join(path));
            Assert.False(ReplayControls.PointerDown(float.NaN, 185));
            Assert.False(ReplayControls.QueuePointerDown(20, float.PositiveInfinity));
            Assert.True(ReplayControls.QueuePointerDown(20, 165));
            bool before = ReplayPlayback.Transport.Paused;
            Assert.True(ReplayControls.ConsumeQueuedPointer()); Assert.NotEqual(before, ReplayPlayback.Transport.Paused);
            Assert.True(ReplayControls.PointerDown(120, 165)); Assert.Equal(2, ReplayPlayback.Transport.Rate);
            Assert.True(ReplayControls.PointerDown(80, 165)); Assert.True(ReplayPlayback.Transport.Paused);
            Assert.Equal(1, ReplayPlayback.Transport.TakeSteps());
            Assert.True(ReplayControls.QueuePointerDown(20, 165));
            ClientInputState.ReadPauseOpen = () => true;
            Assert.False(ReplayControls.ConsumeQueuedPointer()); Assert.False(ReplayControls.QueuePointerDown(20, 165));
            ClientInputState.ReadPauseOpen = () => false;
            Assert.False(ReplayControls.ConsumeQueuedPointer());
            ChatBox.Open(false); Assert.False(ReplayControls.PointerDown(20, 165)); ChatBox.Clear();
            Assert.True(ReplayControls.QueuePointerDown(20, 165));
            Assert.True(ReplayPlayback.Join(path));
            Assert.False(ReplayControls.ConsumeQueuedPointer());
            Assert.False(ReplayPlayback.Transport.Paused);
        }
        finally { ClientInputState.ReadPauseOpen = pause; ChatBox.Clear(); ReplayPlayback.Stop(); File.Delete(path); }
    }
}
