using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.StudioReplay;
using System.Numerics;

namespace ProjectPrime.Studio.Replay;

public enum ReplayCameraGraphChannel { PositionX, PositionY, PositionZ, Fov, Roll, Speed }

/// <summary>Samples the authoritative camera track. Tangent handles edit its
/// Bezier controls; box selection is independent of timeline packet tracks.</summary>
public sealed class ReplayCameraGraph : Control
{
    private readonly StudioReplayPlayer _player;
    private Point? _dragStart;
    private StudioReplayCameraKey? _tangentKey;
    private bool _incoming;
    private readonly HashSet<uint> _selected = new();
    public ReplayCameraGraphChannel Channel { get; set; } = ReplayCameraGraphChannel.Fov;
    public bool ConstantSpeed { get; set; }
    public IReadOnlyCollection<uint> SelectedFrames => _selected;
    public event Action? SelectionChanged;
    public event Action<Exception>? Error;
    private double _minimum, _maximum = 1;
    private uint _start, _end = 1;
    public ReplayCameraGraph(StudioReplayPlayer player)
    {
        _player = player; Height = 180; MinWidth = 220; ClipToBounds = true;
        PointerPressed += (_, e) =>
        {
            if (Channel == ReplayCameraGraphChannel.Speed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Point point = e.GetPosition(this); _dragStart = point; e.Pointer.Capture(this);
            var nearest = _player.CameraKeys.OrderBy(k => Math.Abs(X(k.Frame) - point.X)).FirstOrDefault();
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) && nearest != null && Math.Abs(X(nearest.Frame) - point.X) < 30)
            { _tangentKey = nearest; _incoming = point.X < X(nearest.Frame) || e.KeyModifiers.HasFlag(KeyModifiers.Alt); _selected.Clear(); _selected.Add(nearest.Frame); SelectionChanged?.Invoke(); }
        };
        PointerReleased += (_, e) =>
        {
            if (Channel == ReplayCameraGraphChannel.Speed)
            {
                _dragStart = null; _tangentKey = null; e.Pointer.Capture(null);
                return;
            }
            if (_dragStart is { } start)
            {
                Point end = e.GetPosition(this);
                if (_tangentKey is { } key)
                {
                    float delta = (float)((start.Y - end.Y) * (_maximum - _minimum) / Math.Max(1, Bounds.Height - 20));
                    var changed = key with { Interpolation = StudioReplayCameraInterpolation.Bezier };
                    if (Channel == ReplayCameraGraphChannel.Fov) changed = _incoming ? changed with { FovIncomingTangent = key.FovIncomingTangent + delta } : changed with { FovOutgoingTangent = key.FovOutgoingTangent + delta };
                    else if (Channel == ReplayCameraGraphChannel.Roll) changed = _incoming ? changed with { RollIncomingTangent = key.RollIncomingTangent + delta } : changed with { RollOutgoingTangent = key.RollOutgoingTangent + delta };
                    else if (Channel != ReplayCameraGraphChannel.Speed)
                    {
                        Vector3 tangent = (_incoming ? key.IncomingTangent : key.OutgoingTangent) ?? Vector3.Zero;
                        tangent = Channel switch { ReplayCameraGraphChannel.PositionX => tangent with { X = tangent.X + delta }, ReplayCameraGraphChannel.PositionY => tangent with { Y = tangent.Y + delta }, _ => tangent with { Z = tangent.Z + delta } };
                        changed = _incoming ? changed with { IncomingTangent = tangent } : changed with { OutgoingTangent = tangent };
                    }
                    try { _player.PutCameraKey(changed); } catch (Exception exception) { Error?.Invoke(exception); }
                }
                else
                {
                    _selected.Clear();
                    foreach (var candidate in _player.CameraKeys)
                        if (X(candidate.Frame) >= Math.Min(start.X, end.X) && X(candidate.Frame) <= Math.Max(start.X, end.X)
                            && Y(Value(candidate)) >= Math.Min(start.Y,end.Y) && Y(Value(candidate)) <= Math.Max(start.Y,end.Y)) _selected.Add(candidate.Frame);
                    SelectionChanged?.Invoke();
                }
            }
            _dragStart = null; _tangentKey = null; e.Pointer.Capture(null); InvalidateVisual();
        };
    }
    private double Value(StudioReplayCameraKey key) => Channel switch
    { ReplayCameraGraphChannel.PositionX => key.Position.X, ReplayCameraGraphChannel.PositionY => key.Position.Y, ReplayCameraGraphChannel.PositionZ => key.Position.Z, ReplayCameraGraphChannel.Fov => key.Fov, ReplayCameraGraphChannel.Roll => key.Roll, _ => 0 };
    private double X(uint frame) => 10 + (frame - _start) * Math.Max(1, Bounds.Width - 20) / Math.Max(1, _end - _start);
    private double Y(double value) => Bounds.Height - 10 - (value - _minimum) / Math.Max(.0001, _maximum - _minimum) * (Bounds.Height - 20);
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#101723")), new Rect(Bounds.Size));
        var keys = _player.CameraKeys; if (keys.Count == 0) return;
        _start = keys[0].Frame; _end = Math.Max(_start + 1, keys[^1].Frame);
        var samples = new List<(double Frame, double Value)>(); Vector3? previous = null;
        for (int i = 0; i <= 128; i++)
        {
            double frame = _start + (_end - _start) * i / 128d;
            if (_player.SampleCamera(frame, ConstantSpeed) is not { } sample) continue;
            double value = Channel == ReplayCameraGraphChannel.Speed
                ? previous is { } position ? Vector3.Distance(position, sample.Position) * 60 * 128 / Math.Max(1u, _end - _start) : 0
                : Value(sample);
            previous = sample.Position; samples.Add((frame, value));
        }
        _minimum = samples.Min(s => s.Value); _maximum = samples.Max(s => s.Value);
        if (_maximum - _minimum < .001) { _minimum -= 1; _maximum += 1; }
        double margin = (_maximum - _minimum) * .15; _minimum -= margin; _maximum += margin;
        for (int i = 1; i < samples.Count; i++)
            context.DrawLine(new Pen(Brushes.MediumPurple, 2), new Point(X((uint)samples[i - 1].Frame), Y(samples[i - 1].Value)), new Point(X((uint)samples[i].Frame), Y(samples[i].Value)));
        if (Channel != ReplayCameraGraphChannel.Speed) foreach (var key in keys)
        {
            Point point = new(X(key.Frame), Y(Value(key))); context.DrawEllipse(_selected.Contains(key.Frame) ? Brushes.Orange : Brushes.White, null, point, 4, 4);
            double tangent = Channel switch { ReplayCameraGraphChannel.Fov => key.FovOutgoingTangent, ReplayCameraGraphChannel.Roll => key.RollOutgoingTangent,
                ReplayCameraGraphChannel.PositionX => key.OutgoingTangent?.X ?? 0, ReplayCameraGraphChannel.PositionY => key.OutgoingTangent?.Y ?? 0, _ => key.OutgoingTangent?.Z ?? 0 };
            if (key.Interpolation == StudioReplayCameraInterpolation.Bezier)
            {
                Point outgoing = new(point.X + 15, Y(Value(key) + tangent));
                context.DrawLine(new Pen(Brushes.Gold, 1), point, outgoing); context.DrawEllipse(Brushes.Gold, null, outgoing, 3, 3);
                double input = Channel switch { ReplayCameraGraphChannel.Fov => key.FovIncomingTangent, ReplayCameraGraphChannel.Roll => key.RollIncomingTangent,
                    ReplayCameraGraphChannel.PositionX => key.IncomingTangent?.X ?? 0, ReplayCameraGraphChannel.PositionY => key.IncomingTangent?.Y ?? 0, _ => key.IncomingTangent?.Z ?? 0 };
                Point incoming = new(point.X - 15, Y(Value(key) + input));
                context.DrawLine(new Pen(Brushes.LightBlue, 1), point, incoming); context.DrawEllipse(Brushes.LightBlue, null, incoming, 3, 3);
            }
        }
    }
}
