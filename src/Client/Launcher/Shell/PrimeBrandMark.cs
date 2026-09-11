using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Embedded Project Prime mark for shell chrome and branded entry surfaces.
/// The default bitmap is process-owned because the mark remains visible for
/// the application lifetime. Route detach never disposes or invalidates it.
/// </summary>
internal sealed class PrimeBrandMark : Grid, IDisposable
{
    internal const double MinimumSize = 16;
    internal const double MaximumSize = 256;
    internal static readonly Uri AssetUri = new(
        "avares://ProjectPrime/Assets/project-prime-mark.png");

    private static readonly Lazy<Bitmap?> SharedBitmap = new(LoadSharedBitmap,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _assetFailureLogged;

    private readonly Image _image;
    private readonly PrimeBrandMarkFallback _fallback;
    private readonly Bitmap? _ownedBitmap;
    private bool _disposed;

    public PrimeBrandMark()
        : this(32, null)
    {
    }

    internal PrimeBrandMark(double size)
        : this(size, null)
    {
    }

    internal PrimeBrandMark(Func<Stream> openAsset)
        : this(32, openAsset)
    {
    }

    internal PrimeBrandMark(double size, Func<Stream>? openAsset)
    {
        if (!Double.IsFinite(size) || size is < MinimumSize or > MaximumSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"Brand mark size must be from {MinimumSize} to {MaximumSize} DIPs.");

        Width = size;
        Height = size;
        MaxWidth = MaximumSize;
        MaxHeight = MaximumSize;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
        Classes.Add("prime-brand-mark");
        PrimeAccessibility.SetName(this, "Project Prime");

        Bitmap? bitmap;
        if (openAsset == null)
        {
            bitmap = SharedBitmap.Value;
            UsesSharedBitmap = bitmap != null;
        }
        else
        {
            bitmap = TryLoadBitmap(openAsset, logFailure: false);
            _ownedBitmap = bitmap;
        }

        _image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsVisible = bitmap != null
        };
        AutomationProperties.SetName(_image, "Project Prime mark");

        _fallback = new PrimeBrandMarkFallback
        {
            IsVisible = bitmap == null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        PrimeAccessibility.SetName(_fallback, "Project Prime mark unavailable");
        Children.Add(_fallback);
        Children.Add(_image);
    }

    internal bool HasImage => _image.Source != null;
    internal bool IsFallbackVisible => _fallback.IsVisible;
    internal bool UsesSharedBitmap { get; }
    internal IImage? ImageSource => _image.Source;
    internal Stretch ImageStretch => _image.Stretch;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _image.Source = null;
        _ownedBitmap?.Dispose();
    }

    private static Bitmap? LoadSharedBitmap()
        => TryLoadBitmap(() => AssetLoader.Open(AssetUri), logFailure: true);

    private static Bitmap? TryLoadBitmap(Func<Stream> openAsset, bool logFailure)
    {
        try
        {
            using Stream stream = openAsset();
            return new Bitmap(stream);
        }
        catch (Exception error)
        {
            if (logFailure && Interlocked.Exchange(ref _assetFailureLogged, 1) == 0)
                Debug.WriteLine($"Project Prime mark could not be loaded: {error.Message}");
            return null;
        }
    }
}

/// <summary>Non-text fallback used only when the embedded mark cannot decode.</summary>
internal sealed class PrimeBrandMarkFallback : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 4 || Bounds.Height < 4) return;

        double inset = Math.Max(2, Math.Min(Bounds.Width, Bounds.Height) * 0.12);
        Rect area = Bounds.Deflate(inset);
        var pen = new Pen(GuiTheme.BrandBrush, Math.Max(1.5, area.Width * 0.045));
        Point top = new(area.Center.X, area.Top);
        Point right = new(area.Right, area.Center.Y);
        Point bottom = new(area.Center.X, area.Bottom);
        Point left = new(area.Left, area.Center.Y);
        context.DrawLine(pen, top, right);
        context.DrawLine(pen, right, bottom);
        context.DrawLine(pen, bottom, left);
        context.DrawLine(pen, left, top);

        double arm = area.Width * 0.22;
        context.DrawLine(pen,
            new Point(area.Center.X - arm, area.Center.Y),
            new Point(area.Center.X + arm, area.Center.Y));
        context.DrawLine(pen,
            new Point(area.Center.X, area.Center.Y - arm),
            new Point(area.Center.X, area.Center.Y + arm));
    }
}
