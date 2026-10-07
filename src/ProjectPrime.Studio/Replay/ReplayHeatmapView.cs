using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MphRead.Mods.StudioReplay;

namespace ProjectPrime.Studio.Replay;

public sealed class ReplayHeatmapView : Control
{
    public IReadOnlyList<StudioReplayHeatSample> Samples { get; set; } = [];
    public ReplayHeatmapView() { Height = 240; MinWidth = 240; ClipToBounds = true; }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#101723")), new Rect(Bounds.Size));
        if (Samples.Count == 0) return;
        float minX = Samples.Min(p => p.Position.X), maxX = Samples.Max(p => p.Position.X), minZ = Samples.Min(p => p.Position.Z), maxZ = Samples.Max(p => p.Position.Z);
        double scale = Math.Min((Bounds.Width - 20) / Math.Max(1, maxX - minX), (Bounds.Height - 20) / Math.Max(1, maxZ - minZ));
        var cells = Samples.GroupBy(p => ((int)((p.Position.X - minX) * scale / 8), (int)((p.Position.Z - minZ) * scale / 8)))
            .Select(g => (g.Key, Weight: g.Sum(p => p.Weight))).ToArray();
        double maximum = cells.Max(c => c.Weight);
        foreach (var cell in cells)
        {
            byte alpha = (byte)Math.Clamp(40 + 215 * cell.Weight / Math.Max(.01, maximum), 40, 255);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(alpha, 250, 80, 35)), new Rect(10 + cell.Key.Item1 * 8, 10 + cell.Key.Item2 * 8, 8, 8));
        }
        foreach (var ray in Samples.Where(p => p.Direction.HasValue).Take(300))
        {
            var direction = ray.Direction!.Value;
            Point start = new(10 + (ray.Position.X - minX) * scale, 10 + (ray.Position.Z - minZ) * scale);
            context.DrawLine(new Pen(Brushes.Cyan, .6), start, new Point(start.X + direction.X * 10 * scale, start.Y + direction.Z * 10 * scale));
        }
    }
}
