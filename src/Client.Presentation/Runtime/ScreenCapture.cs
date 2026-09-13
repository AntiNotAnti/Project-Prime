using System;
using System.IO;
#if ANDROID
using OpenTK.Graphics.OpenGL;
#endif
using MphRead.Mods.Render;

namespace MphRead.Mods
{
    /// <summary>
    /// Saves what the scene just drew to a PNG.
    ///
    /// Reads the scene's own offscreen target rather than the window's back
    /// buffer. A hidden window has no usable back buffer under Mesa -- which
    /// is why headless captures came out black on Linux while the identical
    /// code produced correct thumbnails on Windows -- and even a visible one
    /// leaves the back buffer undefined once SwapBuffers has run. The
    /// offscreen target is a texture the scene owns, so it is valid either
    /// way.
    /// </summary>
    public static class ScreenCapture
    {
        /// <summary>
        /// How a PNG gets written: the pixels bottom-up in RGB, the width, the
        /// height and the path.
        ///
        /// STB everywhere but Android, where the package ships natives for
        /// Linux and Windows and none for a phone -- the room rendered and the
        /// frame read back fine, and then the encoder's type initializer threw.
        /// The head there sets this to Android's own encoder.
        /// </summary>
        public static Action<byte[], int, int, string>? PngWriter { get; set; }

        /// <summary>
        /// Save what the *window* holds, which is the only capture that
        /// includes the HUD. See <c>Scene.ReadWindowBuffer</c>: only valid on
        /// a visible window, and only between the draw and the buffer swap.
        /// </summary>
#if ANDROID
        public static bool SaveWindow(Scene scene, string path)
        {
            return Save(scene, path, ScenePresentation.Get(scene).ReadWindowBuffer,
                CaptureTargetKind.FinalPresentedFrame);
        }

        public static bool Save(Scene scene, string path)
        {
            return Save(scene, path, ScenePresentation.Get(scene).ReadSceneTarget,
                CaptureTargetKind.SceneTarget);
        }

        private delegate byte[]? ReadPixels(out int width, out int height);

        private static bool Save(Scene scene, string path, ReadPixels read, CaptureTargetKind target)
        {
            try
            {
                byte[]? pixels = read(out int width, out int height);
                if (pixels == null || width <= 0 || height <= 0)
                {
                    return false;
                }
                long frame = ScenePresentation.Get(scene).Timing.TotalFrames;
                var result = new RenderCaptureResult(Guid.NewGuid(), frame, target,
                    width, height, CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp, pixels);
                // An all-black frame is a failure that writes a file. It
                // looks like a success in every log, the caller counts it,
                // and the player ends up with a full set of black pictures
                // and nothing saying why -- which is exactly how it was
                // reported. Refusing to save it turns a silent wrong answer
                // into a loud missing one.
                if (LitFraction(result.Bytes.Span) < MinLitFraction)
                {
                    string why = $"{Path.GetFileName(path)} came out black "
                        + $"({LitFraction(result.Bytes.Span) * 100:0.00}% lit, {width}x{height}); not saving it. "
                        + $"The scene rendered nothing -- {DescribeContext()}";
                    Console.WriteLine($"[capture] {why}");
                    ThumbnailLog.Write(why);
                    return false;
                }
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                Action<byte[], int, int, string>? writer = PngWriter;
                if (writer != null)
                {
                    writer(result.CopyBytes(), result.Width, result.Height, path);
                    return true;
                }
                throw new InvalidOperationException("This platform has not installed a PNG encoder.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[capture] could not save {path}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// How much of a picture has to be lit before it counts as a picture.
        ///
        /// "Any lit pixel at all" is not enough, and was tried: a dead context
        /// still produces the odd non-zero pixel, so a retry eventually passed
        /// and the run reported a capture it had refused to save. A room seen
        /// from its own intro camera lights up far more than a hundredth of
        /// the frame.
        /// </summary>
        private const double MinLitFraction = 0.01;

        private static double LitFraction(ReadOnlySpan<byte> pixels)
        {
            int lit = 0;
            int total = 0;
            for (int i = 0; i + 2 < pixels.Length; i += 3)
            {
                total++;
                if (pixels[i] > 8 || pixels[i + 1] > 8 || pixels[i + 2] > 8)
                {
                    lit++;
                }
            }
            return total == 0 ? 0 : lit / (double)total;
        }

        /// <summary>
        /// What the driver actually handed over, which is the one thing a
        /// black render never says on its own.
        ///
        /// Android uses the GLES presentation path, so a context mismatch or
        /// rejected call can otherwise present as a black frame with little
        /// useful diagnostic output.
        /// </summary>
        // Kept alive deliberately: the driver holds this pointer for the
        // lifetime of the context, and a delegate that is only a local goes
        // to the collector and takes the process with it on the next message.
        private static DebugProc? _debugCallback;
        private static int _messagesLogged;

        /// <summary>
        /// Ask the driver to say which call it is refusing.
        ///
        /// GL_INVALID_OPERATION on its own names nothing: it is the error for
        /// several dozen different mistakes, and a frame that raises one every
        /// time can be refusing anything. KHR_debug is the extension that
        /// turns it into a sentence, and it is the difference between another
        /// guess and an answer. Off in normal play -- it costs a callback per
        /// message and there is nobody to read them.
        /// </summary>
        public static void EnableDebugOutput(Action<string> report)
        {
            try
            {
                _debugCallback = (source, type, id, severity, length, message, param) =>
                {
                    if (severity == DebugSeverity.DebugSeverityNotification || _messagesLogged >= 12)
                    {
                        return;
                    }
                    _messagesLogged++;
                    string text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(message, length);
                    report($"GL says: [{severity}] {type} from {source}: {text}");
                };
                GL.Enable(EnableCap.DebugOutput);
                GL.Enable((EnableCap)All.DebugOutputSynchronous);
                GL.DebugMessageCallback(_debugCallback, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                report($"could not turn on GL debug output ({ex.GetType().Name}); "
                    + "this driver may not have KHR_debug");
            }
        }

        public static string DescribeContext()
        {
            try
            {
                string vendor = GL.GetString(StringName.Vendor) ?? "?";
                string renderer = GL.GetString(StringName.Renderer) ?? "?";
                string version = GL.GetString(StringName.Version) ?? "?";
                return $"OpenGL ES {version}, {vendor} / {renderer}";
            }
            catch (Exception ex)
            {
                return $"could not query the GL context: {ex.Message}";
            }
        }

        /// <summary>
        /// How much of the image is not the clear colour, as a fraction.
        ///
        /// A capture that is entirely black is the failure this whole path
        /// has to be able to detect: it looks like a successful render in
        /// every log, and only reading the pixels back says otherwise.
        /// </summary>
        public static double NonBlackFraction(Scene scene)
        {
            byte[]? pixels = ScenePresentation.Get(scene).ReadSceneTarget(out int width, out int height);
            if (pixels == null || width <= 0)
            {
                return 0;
            }
            int lit = 0;
            for (int i = 0; i < pixels.Length; i += 3)
            {
                if (pixels[i] > 8 || pixels[i + 1] > 8 || pixels[i + 2] > 8)
                {
                    lit++;
                }
            }
            return lit / (double)(width * height);
        }
#endif
    }
}
