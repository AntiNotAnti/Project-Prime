using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Export
{
    public static class ScreenCapture
    {
        private static Task? _task = null;
        private static bool _recording = false;
        private static readonly ConcurrentQueue<(byte[], string, int, int)> _queue = new ConcurrentQueue<(byte[], string, int, int)>();

        public static void Screenshot(int width, int height, string? name = null)
        {
            byte[] buffer = new byte[width * height * 3];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
            string path = Paths.Combine(Paths.Export, "_screenshots");
            Directory.CreateDirectory(path);
            name ??= DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            Droid.AndroidPng.Write(buffer, width, height, Paths.Combine(path, $"{name}.png"));
        }

        public static void Record(int width, int height, string name)
        {
            _recording = true;
            if (_task == null)
            {
                _task = Task.Run(async () => await ProcessQueue());
            }
            byte[] buffer = ArrayPool<byte>.Shared.Rent(width * height * 3);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
            _queue.Enqueue((buffer, name, width, height));
        }

        public static void StopRecording()
        {
            _recording = false;
        }

        private static async Task ProcessQueue()
        {
            while (_recording || _queue.Count > 0)
            {
                while (_queue.TryDequeue(out (byte[] Buffer, string Name, int Width, int Height) result))
                {
                    string path = Paths.Combine(Paths.Export, "_screenshots");
                    Directory.CreateDirectory(path);
                    Droid.AndroidPng.Write(result.Buffer, result.Width, result.Height, Paths.Combine(path, $"{result.Name}.png"));
                    ArrayPool<byte>.Shared.Return(result.Buffer);
                }
                await Task.Delay(1);
            }
        }

    }
}
