using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;

// Invented pixels and geometry only. Uses the caller's actual host/context and
// shared RmlUi pass; creates no window, Scene, source-art file or renderer.
internal static class CanvasGlAcceptance
{
    private readonly record struct Bounds(float X, float Y, float Width, float Height);
    private readonly record struct Rgb(int R, int G, int B);

    internal static void Run(RmlUiHost host, int width, int height, string assetRoot)
    {
        if (width < 320 || height < 240) throw new ArgumentException("Canvas GL acceptance needs at least320x240 pixels.");
        string name = "synthetic-canvas-" + Guid.NewGuid().ToString("N") + ".rml";
        string path = Path.Combine(Path.GetFullPath(assetRoot), name);
        int checks = 0, pixelChecks = 0, frames = 0, renderWidth = width, renderHeight = height;
        bool homeVisible = host.IsVisible(host.HomeDocument);
        bool created = false;
        RmlUiDocumentToken document = default;
        RmlUiCanvas? canvas = null;
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                created = true;
                file.Write(Encoding.UTF8.GetBytes("""
                    <rml><head><title>Invented canvas GL acceptance</title><style>
                    body { margin: 0px; padding: 0px; width: 100%; height: 100%; background-color: #101020; }
                    #clip { position: absolute; left: 10%; top: 10%; width: 80%; height: 80%; overflow: hidden; background-color: #101020; }
                    prime-canvas { display: block; position: absolute; left: 0px; top: 0px; width: 100%; height: 100%; }
                    #cover { position: absolute; left: 80%; top: 80%; width: 20%; height: 20%; background-color: #ff8000; }
                    </style></head><body><div id="clip"><prime-canvas id="canvas"></prime-canvas><div id="cover"></div></div></body></rml>
                    """));
            }
            Check(host.ShowDocument(host.HomeDocument, false), "hide compatibility Home behind own opaque fixture");
            Open();
            var source = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255 };
            var bw = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 };
            var table = new RmlUiCanvasTextureTable(new RmlUiCanvasImage[]
            {
                new(2, 2, source, 0, 0, 0),
                new(1, 1, new byte[] { 200, 100, 50, 128 }, 0, 0, 0),
                new(2, 2, source, 0, 1, 0),
                new(2, 2, source, 0, 0, 1),
                new(2, 1, bw, 1, 0, 0),
                new(2, 1, bw, 0, 0, 0),
                new(2, 1, bw, 1, 1, 0),
                new(1, 2, bw, 1, 0, 1),
            });
            Array.Clear(source); Array.Clear(bw); // Public table must own detached pixel bytes.
            var packet = Artwork();
            Check(canvas!.SetTextures(table) && canvas.Submit(packet), "publish detached texture table and immutable synthetic frame");
            Clear(); host.Update();
            using (var sentinels = new UploadSentinels(Check))
            {
                RmlUiOpenGlFrame.Draw(host, renderWidth, renderHeight); frames++;
                sentinels.AssertRestored();
                Check(canvas.TryState(out var state) && state.Revision == 1 && state.TextureCount == 8
                    && state.BatchCount == packet.BatchCount && (state.Flags & 1) == 0 && (state.Flags & 2) != 0,
                    "lazy actual GL uploads retain the exact current packet and successful canvas state");
            }
            VerifyArtwork();
            Draw(); VerifyArtwork(); // Reuse cannot premultiply a second time.
            Letterbox();
            Bounds cover = Element("cover");
            Pixel(cover.X + cover.Width / 2, cover.Y + cover.Height / 2, new(255, 128, 0), 2,
                "later DOM sibling paints above native canvas geometry");

            int narrow = Math.Max(256, width * 2 / 3);
            host.Resize(narrow, height, 1); renderWidth = narrow;
            Draw(); VerifyArtwork(); Letterbox();
            Check(Element("canvas").Width < width * .8f, "host resize recomputes real element content dimensions");
            host.Resize(width, height, 1); renderWidth = width;
            Draw(); VerifyArtwork();

            Check(canvas.Submit(Oversized()), "publish deliberately oversized synthetic geometry for ancestor clipping");
            Draw();
            Bounds clip = Element("clip");
            Pixel(clip.X - 3, clip.Y + clip.Height / 2, new(16, 16, 32), 2,
                "overflow-hidden ancestor clips canvas geometry beyond left content edge");
            Pixel(clip.X + clip.Width / 2, clip.Y - 3, new(16, 16, 32), 2,
                "overflow-hidden ancestor clips geometry beyond top content edge");
            Pixel(clip.X + clip.Width / 2, clip.Y + clip.Height / 2, new(0, 255, 255), 2,
                "the same oversized geometry paints its unclipped interior");

            var retired = canvas;
            canvas = host.AcquireCanvas(document, "canvas");
            Check(canvas != null && !retired.TryState(out _) && !retired.Submit(packet),
                "same-document reacquire replaces the exact epoch and refuses old publication");
            retired.Dispose();
            Check(canvas!.TryState(out var replacement) && replacement.Revision == 0,
                "retired owner disposal cannot release a replacement canvas");
            Check(canvas.SetTextures(table) && canvas.Submit(packet), "replacement uploads its own immutable table and frame");
            Draw(); VerifyArtwork();
            Bounds visibleBounds = Element("canvas");
            float visibleScale = MathF.Min(visibleBounds.Width / 320, visibleBounds.Height / 240);
            float hiddenWitnessX = visibleBounds.X + (visibleBounds.Width - 320 * visibleScale) / 2 + 28 * visibleScale;
            float hiddenWitnessY = visibleBounds.Y + (visibleBounds.Height - 240 * visibleScale) / 2 + 28 * visibleScale;
            host.SetBool(document, "visible:canvas", false); Draw();
            Check(!canvas.Submit(packet), "hidden canvas rejects current-frame publication");
            Pixel(hiddenWitnessX, hiddenWitnessY, new(16, 16, 32), 2, "hidden canvas contributes no original synthetic texture pixels");
            host.SetBool(document, "visible:canvas", true); Draw(); VerifyArtwork();

            Check(host.CloseDocument(document) && !canvas.TryState(out _) && !canvas.Submit(packet),
                "document close retires the canvas before later publication or draw");
            canvas.Dispose(); canvas = null; document = default;
            Open();
            Check(canvas!.SetTextures(table) && canvas.Submit(packet), "fresh document owns a fresh texture epoch");
            Draw(); VerifyArtwork();
            var beforeReinitialize = canvas; var oldDocument = document;
            Check(host.Reinitialize(), "shared existing host reinitializes in its current GL context");
            Check(!host.IsAlive(oldDocument) && !beforeReinitialize.TryState(out _) && !beforeReinitialize.Submit(packet),
                "generation retirement fences old canvas leases after actual reinitialization");
            beforeReinitialize.Dispose(); canvas = null; document = default;
            Check(host.ShowDocument(host.HomeDocument, false), "hide fresh compatibility Home after reinitialize");
            Open();
            Check(canvas!.SetTextures(table) && canvas.Submit(packet), "fresh runtime can upload and draw new synthetic canvas resources");
            Draw(); VerifyArtwork();
            Check(GL.GetError() == ErrorCode.NoError, "synthetic native canvas acceptance leaves no GL error");
            Console.WriteLine(JsonSerializer.Serialize(new { Result = "PASS", Checks = checks, PixelChecks = pixelChecks,
                Frames = frames, Width = width, Height = height, Textures = table.Count, SourceArtRead = false,
                SharedHostPass = true, NewWindowOrScene = false, OriginalCreditsActivated = false,
                SyntheticOnly = true, TemporaryRmlRemovedInFinally = true }));
        }
        finally
        {
            try { canvas?.Dispose(); }
            finally
            {
                try
                {
                    if (host.IsAlive(document)) host.CloseDocument(document);
                    if (host.Active) { host.Resize(width, height, 1); host.ShowDocument(host.HomeDocument, homeVisible); }
                }
                finally { if (created && File.Exists(path)) File.Delete(path); }
            }
        }

        void Check(bool value, string label)
        {
            checks++;
            if (!value) throw new InvalidOperationException("Canvas GL: " + label);
            Console.WriteLine("RML CANVAS GL PASS " + label);
        }
        void Open()
        {
            document = host.OpenDocument(name, RmlUiDocumentLayer.Page);
            Check(host.ShowDocument(document, true), "show own ordinary document through existing host");
            host.Update();
            canvas = host.AcquireCanvas(document, "canvas");
            Check(canvas != null, "acquire actual prime-canvas element with foreground document ownership");
        }
        Bounds Element(string id)
        {
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h),
                "native bounds resolve synthetic element " + id);
            return new(x, y, w, h);
        }
        void Clear()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, renderWidth, renderHeight);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(16 / 255f, 16 / 255f, 32 / 255f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        }
        void Draw() { Clear(); host.Update(); RmlUiOpenGlFrame.Draw(host, renderWidth, renderHeight); frames++; }
        void Pixel(float x, float y, Rgb expected, int tolerance, string label)
        {
            int px = (int)MathF.Floor(x), py = (int)MathF.Floor(y);
            Check(px >= 0 && py >= 0 && px < renderWidth && py < renderHeight, "pixel witness lies within current viewport");
            Rgb actual = ReadPixel(px, renderHeight - py - 1); pixelChecks++;
            Check(Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance
                && Math.Abs(actual.B - expected.B) <= tolerance,
                label + $" expected={expected.R},{expected.G},{expected.B} actual={actual.R},{actual.G},{actual.B}");
        }
        void At(float x, float y, Rgb expected, int tolerance, string label)
        {
            Bounds bounds = Element("canvas"); float scale = MathF.Min(bounds.Width / 320, bounds.Height / 240);
            Pixel(bounds.X + (bounds.Width - 320 * scale) / 2 + x * scale,
                bounds.Y + (bounds.Height - 240 * scale) / 2 + y * scale, expected, tolerance, label);
        }
        void VerifyArtwork()
        {
            At(28, 28, new(255, 0, 0), 2, "top-first synthetic texture orientation: top-left red");
            At(68, 28, new(0, 255, 0), 2, "top-first synthetic texture orientation: top-right green");
            At(28, 68, new(0, 0, 255), 2, "top-first synthetic texture orientation: bottom-left blue");
            At(68, 68, new(255, 255, 0), 2, "top-first synthetic texture orientation: bottom-right yellow");
            At(128, 32, new(25, 25, 13), 3, "straight texture and vertex alpha each premultiply once");
            At(184, 32, new(100, 50, 25), 2, "partial texture alpha composites once over opaque black");
            At(240, 32, new(128, 64, 0), 2, "ordered translucent untextured layers use premultiplied blending");
            At(292, 32, new(17, 99, 201), 2, "untextured batch preserves flat straight RGBA color");
            At(36, 132, new(0, 0, 255), 2, "nearest texture repeats S and clamps T independently");
            At(100, 132, new(0, 255, 0), 2, "nearest texture clamps S and repeats T independently");
            At(164, 132, new(128, 128, 128), 3, "linear sampling blends the center of black-white texels");
            At(228, 132, new(255, 255, 255), 2, "nearest sampling avoids linear interpolation");
            At(284, 116, new(128, 128, 128), 3, "linear repeat-S blends across the wrap boundary");
            At(284, 152, new(128, 128, 128), 3, "linear repeat-T blends across the wrap boundary");
        }
        void Letterbox()
        {
            Bounds b = Element("canvas"); float scale = MathF.Min(b.Width / 320, b.Height / 240);
            float mx = (b.Width - 320 * scale) / 2, my = (b.Height - 240 * scale) / 2;
            if (mx > 4) Pixel(b.X + mx / 2, b.Y + b.Height / 2, new(16, 16, 32), 2, "horizontal letterbox stays unpainted");
            else if (my > 4) Pixel(b.X + b.Width / 2, b.Y + my / 2, new(16, 16, 32), 2, "vertical letterbox stays unpainted");
            At(160, 184, new(0, 0, 0), 2, "fitted virtual image interior remains opaque black");
        }
    }

    private static RmlUiCanvasFrame Artwork()
    {
        var builder = new Packet();
        builder.Quad(0, 0, 320, 240, uint.MaxValue, 0, 0, 0, 255);
        builder.Quad(8, 8, 88, 88, 0);
        builder.Quad(104, 8, 152, 56, 1, 128, 255, 255, 128);
        builder.Quad(160, 8, 208, 56, 1);
        builder.Quad(216, 8, 264, 56, uint.MaxValue, 0, 255, 0, 128);
        builder.Quad(216, 8, 264, 56, uint.MaxValue, 255, 0, 0, 128);
        builder.Quad(272, 8, 312, 56, uint.MaxValue, 17, 99, 201, 255);
        builder.Quad(8, 104, 64, 160, 2, u: 1.25f, v: 1.25f, constantUv: true);
        builder.Quad(72, 104, 128, 160, 3, u: 1.25f, v: 1.25f, constantUv: true);
        builder.Quad(136, 104, 192, 160, 4, u: .5f, v: .5f, constantUv: true);
        builder.Quad(200, 104, 256, 160, 5, u: .625f, v: .5f, constantUv: true);
        builder.Quad(264, 104, 312, 128, 6, u: 1, v: .5f, constantUv: true);
        builder.Quad(264, 136, 312, 168, 7, u: .5f, v: 1, constantUv: true);
        return builder.Freeze();
    }
    private static RmlUiCanvasFrame Oversized()
    {
        var builder = new Packet(); builder.Quad(-80, -80, 400, 320, uint.MaxValue, 0, 255, 255, 255); return builder.Freeze();
    }
    private sealed class Packet
    {
        private readonly List<RmlUiCanvasVertex> _vertices = new();
        private readonly List<int> _indices = new();
        private readonly List<RmlUiCanvasBatch> _batches = new();
        internal void Quad(float x0, float y0, float x1, float y1, uint texture, byte r = 255, byte g = 255,
            byte b = 255, byte a = 255, float u = 0, float v = 0, bool constantUv = false)
        {
            int first = _vertices.Count; uint start = (uint)_indices.Count;
            _vertices.Add(new(x0, y0, r, g, b, a, u, v));
            _vertices.Add(new(x1, y0, r, g, b, a, constantUv ? u : 1, v));
            _vertices.Add(new(x1, y1, r, g, b, a, constantUv ? u : 1, constantUv ? v : 1));
            _vertices.Add(new(x0, y1, r, g, b, a, u, constantUv ? v : 1));
            _indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            _batches.Add(new(texture, start, 6));
        }
        internal RmlUiCanvasFrame Freeze()
        {
            var vertices = _vertices.ToArray(); var indices = _indices.ToArray(); var batches = _batches.ToArray();
            var result = new RmlUiCanvasFrame(320, 240, vertices, indices, batches);
            Array.Clear(vertices); Array.Clear(indices); Array.Clear(batches);
            return result;
        }
    }
    private static Rgb ReadPixel(int x, int y)
    {
        int read = GL.GetInteger(GetPName.ReadFramebufferBinding), buffer = GL.GetInteger(GetPName.ReadBuffer);
        int alignment = GL.GetInteger(GetPName.PackAlignment), pbo = GL.GetInteger(GetPName.PixelPackBufferBinding);
        int row = GL.GetInteger(GetPName.PackRowLength), rows = GL.GetInteger(GetPName.PackSkipRows), skip = GL.GetInteger(GetPName.PackSkipPixels);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0); GL.ReadBuffer(ReadBufferMode.Back);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0); GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.PackRowLength, 0); GL.PixelStore(PixelStoreParameter.PackSkipRows, 0);
            GL.PixelStore(PixelStoreParameter.PackSkipPixels, 0);
            byte[] pixel = new byte[3]; GL.ReadPixels(x, y, 1, 1, PixelFormat.Rgb, PixelType.UnsignedByte, pixel);
            return new(pixel[0], pixel[1], pixel[2]);
        }
        finally
        {
            GL.PixelStore(PixelStoreParameter.PackAlignment, alignment); GL.BindBuffer(BufferTarget.PixelPackBuffer, pbo);
            GL.PixelStore(PixelStoreParameter.PackRowLength, row); GL.PixelStore(PixelStoreParameter.PackSkipRows, rows);
            GL.PixelStore(PixelStoreParameter.PackSkipPixels, skip);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read); GL.ReadBuffer((ReadBufferMode)buffer);
        }
    }
    private sealed class UploadSentinels : IDisposable
    {
        private readonly Action<bool, string> _check;
        private readonly int _active, _client, _texture0, _texture1, _pbo, _alignment, _row, _rows, _pixels;
        private readonly int _sentinel0, _sentinel1, _buffer;
        internal UploadSentinels(Action<bool, string> check)
        {
            _check = check; _active = GL.GetInteger(GetPName.ActiveTexture); _client = GL.GetInteger(GetPName.ClientActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture0); _texture0 = GL.GetInteger(GetPName.TextureBinding2D);
            GL.ActiveTexture(TextureUnit.Texture1); _texture1 = GL.GetInteger(GetPName.TextureBinding2D);
            _pbo = GL.GetInteger(GetPName.PixelUnpackBufferBinding); _alignment = GL.GetInteger(GetPName.UnpackAlignment);
            _row = GL.GetInteger(GetPName.UnpackRowLength); _rows = GL.GetInteger(GetPName.UnpackSkipRows);
            _pixels = GL.GetInteger(GetPName.UnpackSkipPixels);
            _sentinel0 = GL.GenTexture(); _sentinel1 = GL.GenTexture(); _buffer = GL.GenBuffer();
            GL.ActiveTexture(TextureUnit.Texture0); Configure(_sentinel0);
            GL.ActiveTexture(TextureUnit.Texture1); Configure(_sentinel1);
            GL.ClientActiveTexture(TextureUnit.Texture1);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _buffer);
            GL.BufferData(BufferTarget.PixelUnpackBuffer, 4096, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8); GL.PixelStore(PixelStoreParameter.UnpackRowLength, 7);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 1); GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 2);
        }
        private static void Configure(int texture)
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapNearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.MirroredRepeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }
        internal void AssertRestored()
        {
            _check(GL.GetInteger(GetPName.ActiveTexture) == (int)TextureUnit.Texture1
                && GL.GetInteger(GetPName.ClientActiveTexture) == (int)TextureUnit.Texture1
                && GL.GetInteger(GetPName.TextureBinding2D) == _sentinel1,
                "lazy canvas upload restores caller active/client units and unit1 texture binding");
            _check(GL.GetInteger(GetPName.PixelUnpackBufferBinding) == _buffer
                && GL.GetInteger(GetPName.UnpackAlignment) == 8 && GL.GetInteger(GetPName.UnpackRowLength) == 7
                && GL.GetInteger(GetPName.UnpackSkipRows) == 1 && GL.GetInteger(GetPName.UnpackSkipPixels) == 2,
                "lazy canvas upload restores caller PBO and all nondefault unpack strides/skips");
            GL.ActiveTexture(TextureUnit.Texture0);
            _check(GL.GetInteger(GetPName.TextureBinding2D) == _sentinel0, "lazy upload restores caller unit0 texture binding");
            GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureMinFilter, out int min);
            GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureMagFilter, out int mag);
            GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureWrapS, out int s);
            GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureWrapT, out int t);
            _check(min == (int)TextureMinFilter.NearestMipmapNearest && mag == (int)TextureMagFilter.Linear
                && s == (int)TextureWrapMode.MirroredRepeat && t == (int)TextureWrapMode.Repeat,
                "lazy upload never changes caller texture0 filter or per-axis wrapping objects");
            GL.ActiveTexture(TextureUnit.Texture1);
        }
        public void Dispose()
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, _alignment); GL.PixelStore(PixelStoreParameter.UnpackRowLength, _row);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, _rows); GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, _pixels);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pbo);
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, _texture0);
            GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, _texture1);
            GL.ActiveTexture((TextureUnit)_active); GL.ClientActiveTexture((TextureUnit)_client);
            GL.DeleteBuffer(_buffer); GL.DeleteTexture(_sentinel0); GL.DeleteTexture(_sentinel1);
        }
    }
}
