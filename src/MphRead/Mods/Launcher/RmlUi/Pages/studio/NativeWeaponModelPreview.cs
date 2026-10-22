#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Launcher.RmlUi.Render;
using SkiaSharp;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

// Static CPU picture only; no animation, input, audio or gameplay preview.
internal sealed class NativeWeaponModelPreview : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prime-weapon-preview-" + Guid.NewGuid().ToString("N"));
    private readonly RmlUiMapPreviewCache _cache;
    private readonly object _writeGate = new();
    private Task? _work;
    private byte[]? _pending;
    private byte[]? _requested;
    private bool _disposed;
    private int _pictures;
    internal NativeWeaponModelPreview() => _cache = new(_root, 640);
    internal void Request(byte[] model)
    {
        if (_disposed || _requested != null && model.AsSpan().SequenceEqual(_requested)) return;
        _requested = (byte[])model.Clone(); _pending = _requested;
    }
    internal string Read()
    {
        if (_disposed) return "";
        if (_work is { IsCompleted: true }) { _ = _work.Exception; _work = null; }
        if (_work == null && _pending is { } bytes && _pictures < 32)
        {
            _pending = null; _pictures++;
            _work = Task.Run(() => Render(bytes, Path.Combine(_root, "model.png")));
        }
        return _cache.Read(_work == null && File.Exists(Path.Combine(_root, "model.png")) ? Path.Combine(_root, "model.png") : "");
    }
    private void Render(byte[] bytes, string path)
    {
        var model = CustomRifleModel.Decode(bytes);
        using var bitmap = new SKBitmap(640, 280); using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(9, 26, 39));
        SKPoint Raw(float x, float y, float z) => new(z - x, (x + z) * .25f - y);
        var boxes = model.Lod(0).ToArray();
        var corners = boxes.SelectMany(box => Enumerable.Range(0, 8).Select(v => Raw(
            box.Center.X + ((v & 1) == 0 ? -box.Half.X : box.Half.X),
            box.Center.Y + ((v & 2) == 0 ? -box.Half.Y : box.Half.Y),
            box.Center.Z + ((v & 4) == 0 ? -box.Half.Z : box.Half.Z)))).Append(Raw(model.Muzzle.X, model.Muzzle.Y, model.Muzzle.Z)).ToArray();
        float minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X), minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y);
        float scale = MathF.Min(590 / MathF.Max(.01f, maxX - minX), 230 / MathF.Max(.01f, maxY - minY));
        SKPoint Project(float x, float y, float z)
        { var p = Raw(x, y, z); return new(320 + (p.X - (minX + maxX) / 2) * scale, 140 + (p.Y - (minY + maxY) / 2) * scale); }
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var box in boxes.OrderBy(b => b.Center.X + b.Center.Z))
        {
            var c = box.Center; var h = box.Half; var color = model.Color(box.Material);
            foreach (var face in new[] { new[] { 0, 1, 3, 2 }, new[] { 1, 5, 7, 3 }, new[] { 2, 3, 7, 6 } })
            {
                using var polygon = new SKPath();
                for (int i = 0; i < 4; i++)
                {
                    int v = face[i]; var point = Project(c.X + ((v & 1) == 0 ? -h.X : h.X), c.Y + ((v & 2) == 0 ? -h.Y : h.Y), c.Z + ((v & 4) == 0 ? -h.Z : h.Z));
                    if (i == 0) polygon.MoveTo(point); else polygon.LineTo(point);
                }
                polygon.Close(); paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255)); canvas.DrawPath(polygon, paint);
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1; paint.Color = new SKColor(7, 18, 26); canvas.DrawPath(polygon, paint);
            }
        }
        var muzzle = Project(model.Muzzle.X, model.Muzzle.Y, model.Muzzle.Z);
        paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.Cyan; canvas.DrawCircle(muzzle, 4, paint);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        lock (_writeGate)
        {
            if (_disposed) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); data.SaveTo(output);
        }
    }
    public void Dispose()
    {
        lock (_writeGate)
        {
            if (_disposed) return; _disposed = true; _pending = null;
        }
        _cache.Dispose();
        var work = Task.WhenAll(_work ?? Task.CompletedTask, _cache.RetirementCompletion);
        if (work.IsCompleted) Cleanup();
        else _ = work.ContinueWith(_ => Cleanup(), TaskScheduler.Default);
    }
    private void Cleanup()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root).Take(66))
            {
                string name = Path.GetFileName(file);
                if (name == "model.png" || name.EndsWith(".tga", StringComparison.Ordinal) && name.Length == 68 && name[..64].All(Uri.IsHexDigit)) File.Delete(file);
            }
            if (!Directory.EnumerateFileSystemEntries(_root).Any()) Directory.Delete(_root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
#endif
