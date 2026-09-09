using System;
using System.Buffers;
using System.IO;
#if ANDROID
using OpenTK.Graphics.OpenGL;
#endif
using ReFuel.Stb;

namespace MphRead.Export
{
    public static class ScreenCapture
    {
        private static readonly CaptureRecordingConsumer _recorder =
            new CaptureRecordingConsumer(new CaptureRecordingQueue(capacity: 8), Write);

#if ANDROID
        public static void Screenshot(int width, int height, string? name = null)
        {
            byte[] buffer = new byte[width * height * 3];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
            name ??= DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            RenderCaptureResult result = new RenderCaptureResult(Guid.NewGuid(),
                MphRead.Mods.Render.FrameTiming.TotalFrames, CaptureTargetKind.FinalPresentedFrame,
                width, height, CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp, buffer, name,
                CaptureDeliveryKind.Screenshot);
            Write(result);
        }

        public static void Record(int width, int height, string name)
        {
            StartRecording();
            int length = checked(width * height * 3);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
                var result = new RenderCaptureResult(Guid.NewGuid(),
                    MphRead.Mods.Render.FrameTiming.TotalFrames, CaptureTargetKind.FinalPresentedFrame,
                    width, height, CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp,
                    buffer.AsSpan(0, length), name, CaptureDeliveryKind.Recording);
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
#endif

        public static void StopRecording()
        {
            _recorder.StopRecording();
        }

        public static void StartRecording()
        {
            _recorder.StartRecording();
        }

        /// <summary>
        /// Delivers an owned backend-neutral result without touching the
        /// OpenGL context. SDL GPU readbacks arrive here after their fence has
        /// completed; recording results enter the same bounded consumer used
        /// by the legacy path.
        /// </summary>
        public static void Deliver(RenderCaptureResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            try
            {
                if (result.Delivery == CaptureDeliveryKind.Recording)
                {
                    if (!_recorder.TryEnqueue(result))
                    {
                        Console.Error.WriteLine($"[capture] recording queue full; dropping {result.OutputName ?? result.RequestId.ToString()} (dropped {_recorder.DroppedCount})");
                    }
                }
                else
                {
                    Write(result);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[capture] could not deliver {result.OutputName ?? result.RequestId.ToString()}: {ex.Message}");
            }
        }

        private static void Write(RenderCaptureResult result)
        {
            string directory = Paths.Combine(Paths.Export, "_screenshots");
            Directory.CreateDirectory(directory);
            string name = result.OutputName
                ?? DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            byte[] bytes;
            StbiImageFormat format;
            if (result.PixelFormat == CapturePixelFormat.Bgra8)
            {
                bytes = RenderCapturePixels.Normalize(result.Bytes.Span, result.Width, result.Height,
                    CapturePixelFormat.Bgra8, result.RowOrientation, CapturePixelFormat.Rgb8,
                    result.RowOrientation, checked(result.Width * 4));
                format = StbiImageFormat.Rgb;
            }
            else
            {
                bytes = result.CopyBytes();
                format = result.PixelFormat == CapturePixelFormat.Rgba8
                    ? StbiImageFormat.Rgba : StbiImageFormat.Rgb;
            }
            using FileStream fileStream = File.Create(Paths.Combine(directory, $"{name}.png"));
            StbImage.FlipVerticallyOnSave = result.RowOrientation == CaptureRowOrientation.BottomUp;
            StbImage.WritePng<byte>(bytes, result.Width, result.Height, format, fileStream);
        }

    }
}
