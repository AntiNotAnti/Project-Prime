using System;
using System.Buffers;
using System.IO;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Export
{
    public static class ScreenCapture
    {
        private static readonly CaptureRecordingConsumer _recorder =
            new CaptureRecordingConsumer(new CaptureRecordingQueue(capacity: 8), Write);

        public static void Screenshot(int width, int height, long originatingFrame,
            string? name = null)
        {
            byte[] buffer = new byte[width * height * 3];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
            name ??= DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            var result = new RenderCaptureResult(Guid.NewGuid(), originatingFrame,
                CaptureTargetKind.FinalPresentedFrame, width, height, CapturePixelFormat.Rgb8,
                CaptureRowOrientation.BottomUp, buffer, name, CaptureDeliveryKind.Screenshot);
            Write(result);
        }

        public static void Record(int width, int height, string name, long originatingFrame)
        {
            _recorder.StartRecording();
            int length = checked(width * height * 3);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
                var result = new RenderCaptureResult(Guid.NewGuid(), originatingFrame,
                    CaptureTargetKind.FinalPresentedFrame, width, height, CapturePixelFormat.Rgb8,
                    CaptureRowOrientation.BottomUp, buffer.AsSpan(0, length), name,
                    CaptureDeliveryKind.Recording);
                if (!_recorder.TryEnqueue(result))
                {
                    Console.WriteLine($"[capture] recording queue full; dropping {name} (dropped {_recorder.DroppedCount})");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static void StopRecording()
        {
            _recorder.StopRecording();
        }

        public static void StartRecording()
        {
            _recorder.StartRecording();
        }

        private static void Write(RenderCaptureResult result)
        {
            string directory = Paths.Combine(Paths.Export, "_screenshots");
            Directory.CreateDirectory(directory);
            string name = result.OutputName
                ?? DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            Droid.AndroidPng.Write(result.CopyBytes(), result.Width, result.Height,
                Paths.Combine(directory, $"{name}.png"));
        }

    }
}
