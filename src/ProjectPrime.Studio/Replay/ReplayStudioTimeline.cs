using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.StudioReplay;

namespace ProjectPrime.Studio.Replay;

/// <summary>Frame-snapped, ripple-free timeline; it only issues transport commands
/// and sidecar selection ranges. Packet bytes and simulation remain untouched.</summary>
public sealed class ReplayStudioTimeline : Control
{
    private static readonly string[] Tracks = ["Camera", "Cuts", "Events", "Kills", "Damage", "Shots", "Spawns", "Objectives", "Bookmarks", "Annotations", "Audio", "Export range"];
    private readonly StudioReplayPlayer _player;
    private readonly Action<uint> _seek;
    private readonly HashSet<string> _hidden = new();
    private double _zoom = 1, _start;
    private uint? _rangeStart;
    private uint _contextFrame;
    private StudioReplayMarker? _contextMarker;
    public ReplayStudioTimeline(StudioReplayPlayer player, Action<uint>? seek = null)
    {
        _player = player; _seek = seek ?? (frame => player.Seek(frame)); Height = 244; Focusable = true; ClipToBounds = true;
        var tracks = new MenuItem { Header = "Visible tracks", ItemsSource = Tracks.Select(track =>
        { var item = new MenuItem { Header = track, ToggleType = MenuItemToggleType.CheckBox, IsChecked = true }; item.Click += (_, _) => { if (item.IsChecked) _hidden.Remove(track); else _hidden.Add(track); InvalidateVisual(); }; return item; }).ToArray() };
        MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }
        ContextMenu = new ContextMenu { ItemsSource = new object[] { tracks,
            Item("Seek here", () => _seek(_contextFrame)),
            Item("Mark In here", () => _player.SetRange(_contextFrame, Math.Max(_contextFrame, _player.Status.ClipOut ?? _player.Status.DurationFrames))),
            Item("Mark Out here", () => _player.SetRange(Math.Min(_contextFrame, _player.Status.ClipIn ?? 0), _contextFrame)),
            Item("Add bookmark here", () => _player.AddBookmark(_contextFrame, "Bookmark " + _contextFrame)),
            Item("Select marker range", () => { if (_contextMarker is { } marker) _player.SetRange(marker.StartFrame, marker.EndFrame); }),
            Item("Delete marker", () => { if (_contextMarker is { } marker) _player.RemoveMarker(marker); }) } };
        PointerPressed += (_, e) =>
        {
            Focus(); Point point = e.GetPosition(this); if (point.X < 105) return;
            uint frame = Frame(point.X);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _contextFrame = frame; _contextMarker = _player.Markers.OrderBy(marker => Math.Abs(X(marker.StartFrame) - point.X))
                    .FirstOrDefault(marker => Math.Abs(X(marker.StartFrame) - point.X) <= 6 || marker.StartFrame <= frame && marker.EndFrame >= frame);
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _rangeStart = frame;
            else _seek(frame);
            e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !_rangeStart.HasValue) _seek(Frame(e.GetPosition(this).X)); };
        PointerReleased += (_, e) =>
        {
            if (_rangeStart is { } start) { uint end = Frame(e.GetPosition(this).X); _player.SetRange(Math.Min(start, end), Math.Max(start, end)); _rangeStart = null; }
            e.Pointer.Capture(null);
        };
        PointerWheelChanged += (_, e) =>
        {
            double duration = Math.Max(1, _player.Status.DurationFrames);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _start = Math.Clamp(_start - e.Delta.Y * duration / _zoom / 8, 0, duration - duration / _zoom);
            else { _zoom = Math.Clamp(_zoom * Math.Pow(1.25, e.Delta.Y), 1, 128); _start = Math.Clamp(_start, 0, duration - duration / _zoom); }
            InvalidateVisual(); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            var status = _player.Status; uint step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 60u : 1u;
            if (e.Key == Key.Left) _seek(status.Frame > step ? status.Frame - step : 0);
            else if (e.Key == Key.Right) _seek(Math.Min(status.DurationFrames, status.Frame + step));
            else if (e.Key == Key.Home) _seek(0); else if (e.Key == Key.End) _seek(status.DurationFrames);
            else if (e.Key == Key.Space) _player.TogglePause(); else return; e.Handled = true;
        };
    }
    private uint Frame(double x)
    {
        double duration = Math.Max(1, _player.Status.DurationFrames);
        return (uint)Math.Clamp(Math.Round(_start + (x - 105) / Math.Max(1, Bounds.Width - 105) * duration / _zoom), 0, duration);
    }
    private double X(uint frame) => 105 + (frame - _start) * Math.Max(1, Bounds.Width - 105) * _zoom / Math.Max(1, _player.Status.DurationFrames);
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#151B24")), new Rect(Bounds.Size));
        string[] visible = Tracks.Where(t => !_hidden.Contains(t)).ToArray(); double row = Math.Min(20, Bounds.Height / Math.Max(1, visible.Length));
        for (int i = 0; i < visible.Length; i++)
        {
            double y = i * row; context.DrawLine(new Pen(Brushes.DimGray, .5), new Point(0, y), new Point(Bounds.Width, y));
            context.DrawText(new FormattedText(visible[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 11, Brushes.LightGray), new Point(6, y + 2));
            if (visible[i] == "Camera") foreach (var key in _player.CameraKeys) Mark(context, X(key.Frame), y, row, Brushes.MediumPurple);
            foreach (var marker in _player.Markers.Where(m => m.Track == visible[i] || visible[i] == "Cuts" && m.Track == "Reel"))
            { double left = X(marker.StartFrame), right = X(marker.EndFrame); context.FillRectangle(Brushes.Goldenrod, new Rect(left, y + 4, Math.Max(3, right - left), row - 7)); }
            foreach (var marker in _player.Events.Where(e => EventTrack(e.Type) == visible[i] || visible[i] == "Events" || visible[i] == "Audio" && (e.Type is "WeaponFired" or "PlayerSpawn" or "PlayerDeath" or "Damage"))) Mark(context, X(marker.Frame), y, row, Brushes.CornflowerBlue);
            if (visible[i] == "Export range" && _player.Status.ClipIn is { } start && _player.Status.ClipOut is { } end)
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(150, 75, 150, 95)), new Rect(X(start), y + 2, Math.Max(1, X(end) - X(start)), row - 4));
        }
        double cursor = X(_player.Status.Frame); context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(cursor, 0), new Point(cursor, Bounds.Height));
    }
    private static void Mark(DrawingContext context, double x, double y, double row, IBrush brush) => context.FillRectangle(brush, new Rect(x, y + 5, 3, Math.Max(2, row - 10)));
    private static string EventTrack(string type) => type switch { "Kill" or "PlayerDeath" => "Kills", "Damage" or "Headshot" => "Damage", "WeaponFired" => "Shots", "PlayerSpawn" => "Spawns", "Objective" or "FlagCapture" or "NodeCapture" or "HardpointCaptured" or "PrimeChanged" or "RelicPickup" => "Objectives", _ => "Events" };
}
