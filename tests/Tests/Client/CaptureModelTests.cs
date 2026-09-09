using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MphRead;
using Xunit;

public sealed class CaptureModelTests
{
    [Fact]
    public void CaptureResultOwnsPixelsAndDescribesOrigin()
    {
        byte[] source = { 1, 2, 3, 4, 5, 6 };
        var result = new RenderCaptureResult(Guid.NewGuid(), 144,
            CaptureTargetKind.SceneTarget, 2, 1, CapturePixelFormat.Rgb8,
            CaptureRowOrientation.BottomUp, source);

        source[0] = 99;
        Assert.Equal(144, result.OriginatingFrame);
        Assert.Equal(CaptureTargetKind.SceneTarget, result.Target);
        Assert.Equal(2, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(CapturePixelFormat.Rgb8, result.PixelFormat);
        Assert.Equal(CaptureRowOrientation.BottomUp, result.RowOrientation);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, result.Bytes.ToArray());
    }

    [Fact]
    public void CapturePixelsNormalizesPitchChannelsAndOrientation()
    {
        // Two RGBA pixels per row with four bytes of driver padding. SDL's
        // source rows are top-down; the desktop PNG contract is RGB bottom-up.
        byte[] mapped =
        {
            255, 0, 0, 17, 0, 255, 0, 18, 99, 99, 99, 99,
            0, 0, 255, 19, 255, 255, 255, 20, 98, 98, 98, 98
        };

        byte[] normalized = RenderCapturePixels.Normalize(mapped, 2, 2,
            CapturePixelFormat.Rgba8, CaptureRowOrientation.TopDown,
            CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp, 12);

        Assert.Equal(new byte[]
        {
            0, 0, 255, 255, 255, 255,
            255, 0, 0, 0, 255, 0
        }, normalized);
    }

    [Fact]
    public void CapturePixelsRejectsShortPaddedRows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderCapturePixels.Normalize(
            new byte[8], 2, 1, CapturePixelFormat.Rgba8,
            CaptureRowOrientation.TopDown, CapturePixelFormat.Rgba8,
            CaptureRowOrientation.TopDown, 4));
    }

    [Fact]
    public void CaptureFailureRetainsRequestMetadata()
    {
        RenderCaptureRequest request = new RenderCaptureRequest(Guid.NewGuid(), 9,
            CaptureTargetKind.FinalPresentedFrame, 4, 3, CapturePixelFormat.Rgb8,
            CaptureRowOrientation.BottomUp, CaptureDeliveryKind.Recording, "frame0009");
        RenderCaptureFailure failure = new RenderCaptureFailure(request, "fence lost");

        Assert.Equal(request.RequestId, failure.RequestId);
        Assert.Equal(request.OriginatingFrame, failure.OriginatingFrame);
        Assert.Equal(request.Delivery, failure.Delivery);
        Assert.Equal("frame0009", failure.OutputName);
        Assert.Equal("fence lost", failure.Error);
    }

    [Fact]
    public void CaptureResultRejectsWrongLayoutAndMissingRequest()
    {
        Assert.Throws<ArgumentException>(() => new RenderCaptureResult(Guid.Empty, 0,
            CaptureTargetKind.FinalPresentedFrame, 1, 1, CapturePixelFormat.Rgba8,
            CaptureRowOrientation.TopDown, new byte[4]));
        Assert.Throws<ArgumentException>(() => new RenderCaptureResult(Guid.NewGuid(), 0,
            CaptureTargetKind.FinalPresentedFrame, 1, 1, CapturePixelFormat.Rgba8,
            CaptureRowOrientation.TopDown, new byte[3]));
    }

    [Fact]
    public void RecordingQueueIsBoundedAndStopDrainsBeforeRestart()
    {
        var queue = new CaptureRecordingQueue(capacity: 1);
        RenderCaptureResult first = Result(1);
        RenderCaptureResult second = Result(2);

        queue.StartRecording();
        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(second));
        Assert.Equal(1, queue.DroppedCount);

        queue.StopRecording();
        Assert.False(queue.IsRecording);
        Assert.True(queue.IsDraining);
        Assert.False(queue.TryEnqueue(second));
        Assert.True(queue.TryDequeue(out RenderCaptureResult? drained));
        Assert.Same(first, drained);
        Assert.False(queue.IsDraining);

        // A new recording may start after the old result drained, and its
        // result remains a separate owned item.
        queue.StartRecording();
        Assert.True(queue.TryEnqueue(second));
        queue.StopRecording();
        Assert.True(queue.TryDequeue(out RenderCaptureResult? restarted));
        Assert.Same(second, restarted);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public async Task RecordingConsumerRestartsWithoutLosingTheNextRecording()
    {
        var written = new List<long>();
        var consumer = new CaptureRecordingConsumer(new CaptureRecordingQueue(),
            result => written.Add(result.OriginatingFrame));

        consumer.StartRecording();
        Assert.True(consumer.TryEnqueue(Result(1)));
        await consumer.StopAndDrainAsync();
        Assert.False(consumer.IsConsumerRunning);

        consumer.StartRecording();
        Assert.True(consumer.TryEnqueue(Result(2)));
        await consumer.StopAndDrainAsync();

        Assert.Equal(new long[] { 1, 2 }, written);
        Assert.Equal(0, consumer.PendingCount);
        Assert.False(consumer.IsConsumerRunning);
    }

    [Fact]
    public async Task RecordingConsumerReportsEncoderFailureAndDrainsRemainingResults()
    {
        var written = new List<long>();
        var failures = new List<Exception>();
        var consumer = new CaptureRecordingConsumer(new CaptureRecordingQueue(),
            result =>
            {
                if (result.OriginatingFrame == 1)
                {
                    throw new InvalidOperationException("encoder failed");
                }
                written.Add(result.OriginatingFrame);
            }, failures.Add);

        consumer.StartRecording();
        Assert.True(consumer.TryEnqueue(Result(1)));
        Assert.True(consumer.TryEnqueue(Result(2)));
        await consumer.StopAndDrainAsync();

        Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(consumer.LastError);
        Assert.Equal(new long[] { 2 }, written);
        Assert.Equal(0, consumer.PendingCount);
        Assert.False(consumer.IsConsumerRunning);
    }

    private static RenderCaptureResult Result(long frame)
        => new RenderCaptureResult(Guid.NewGuid(), frame, CaptureTargetKind.FinalPresentedFrame,
            1, 1, CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp, new byte[3]);
}
