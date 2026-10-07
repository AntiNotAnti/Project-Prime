using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Replay;

internal static partial class Program
{
    private static async Task CheckReplayGraphPointerAsync(StudioWindow window, ReplayStudioDocument document, string output)
    {
        var player = document.Session!.Player;
        byte[] original = player.ExportCameraSidecarState(); uint originalFrame = player.Status.Frame;
        var inspector = document.Host.GetVisualDescendants().OfType<TabControl>().Single(control =>
            control.Items.OfType<TabItem>().Any(tab => tab.Header?.ToString() == "Camera"));
        inspector.SelectedItem = inspector.Items.OfType<TabItem>().Single(tab => tab.Header?.ToString() == "Camera");
        var graph = document.Host.GetVisualDescendants().OfType<ReplayCameraGraph>().Single();
        var graphChannel = document.Host.GetVisualDescendants().OfType<ComboBox>().Single(control =>
            control.Items.OfType<ReplayCameraGraphChannel>().Any());
        var scroll = graph.GetVisualAncestors().OfType<ScrollViewer>().First(); Vector originalOffset = scroll.Offset;
        var keys = (ListBox)document.Host.GetType().GetField("_keys", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(document.Host)!;
        MethodInfo refresh = document.Host.GetType().GetMethod("RefreshKeys", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            player.RemoveCameraKeys(player.CameraKeys.Select(key => key.Frame).ToArray());
            foreach (uint frame in new uint[] { 0, 10, 20, 30 })
                player.PutCameraKey(new(frame, new(0, 5, 10), System.Numerics.Quaternion.Identity,
                    Fov: frame % 20 == 0 ? 40 : 120,
                    Interpolation: StudioReplayCameraInterpolation.Linear, Ease: StudioReplayCameraEase.None));
            refresh.Invoke(document.Host, null); graph.Channel = ReplayCameraGraphChannel.Fov;
            graph.InvalidateVisual(); PumpLayout(window);
            scroll.Offset = new Vector(0, graph.Bounds.Y); await Task.Delay(5); PumpLayout(window);
            using (var rendered = window.CaptureRenderedFrame())
                Check(rendered is not null, "camera graph pointer fixture renders the real production control before input");
            CheckControlBounds(window, graph, 220, 150, "camera graph pointer fixture allocates a visible two-dimensional key graph");
            Point? inScroll = graph.TranslatePoint(new Point(), scroll);
            Check(inScroll is { } local && local.Y >= 0 && local.Y + graph.Bounds.Height <= scroll.Bounds.Height,
                "camera graph pointer fixture places the entire key graph inside the inspector's actual scrolling viewport; origin="+inScroll+", viewport="+scroll.Bounds);
            string before = Convert.ToBase64String(player.ExportCameraSidecarState());
            DragBox(graph.Bounds.Height / 2, graph.Bounds.Height - 5);
            scroll.Offset = new Vector(0, graph.Bounds.Y); PumpLayout(window);
            using(var probe=window.CaptureRenderedFrame())probe?.Save(Path.Combine(output,"replay-studio-camera-box-selection-lower.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Check(graph.SelectedFrames.Order().SequenceEqual(new uint[] { 0, 20 })
                && SelectedKeyFrames().SequenceEqual(new uint[] { 0, 20 }),
                "Shift-drag lower graph box excludes high-FOV keys at the same frame span and synchronizes actual canonical key selection: "
                +System.Text.Json.JsonSerializer.Serialize(new{Graph=graph.SelectedFrames,List=SelectedKeyFrames(),Bounds=graph.Bounds.ToString(),Offset=scroll.Offset.ToString(),
                    Origin=graph.TranslatePoint(new Point(),window)?.ToString(),Keys=player.CameraKeys.Select(key=>new{key.Frame,key.Fov})}));
            DragBox(5, graph.Bounds.Height / 2);
            scroll.Offset = new Vector(0, graph.Bounds.Y); PumpLayout(window);
            Check(graph.SelectedFrames.Order().SequenceEqual(new uint[] { 10, 30 })
                && SelectedKeyFrames().SequenceEqual(new uint[] { 10, 30 }),
                "Shift-drag upper graph box selects only high-FOV keys and replaces the synchronized key selection");
            Check(Convert.ToBase64String(player.ExportCameraSidecarState()) == before,
                "real graph box selection leaves every authored camera key and tangent unchanged");
            using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Selected camera graph did not render.");
            image.Save(Path.Combine(output, "replay-studio-camera-box-selection.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            await player.FlushCameraEditsAsync();
            byte[] durableBefore = File.ReadAllBytes(player.LogicalPath + ".camera");
            uint[] selectedBefore = graph.SelectedFrames.Order().ToArray();
            graphChannel.SelectedItem = ReplayCameraGraphChannel.Speed; graph.InvalidateVisual(); PumpLayout(window);
            DragGraph(false, switchToSpeed: false);
            await AssertDerivedGestureAsync("ordinary Speed drag cannot author invisible camera tangents or change selection");
            DragGraph(true, switchToSpeed: false);
            await AssertDerivedGestureAsync("Shift-box on the derived Speed curve cannot select invisible authored keys");
            graphChannel.SelectedItem = ReplayCameraGraphChannel.Fov; graph.InvalidateVisual(); PumpLayout(window);
            DragGraph(false, switchToSpeed: true);
            await AssertDerivedGestureAsync("switching from FOV to Speed during a captured drag cancels authoring without changing durable camera bytes");
            scroll.Offset = new Vector(0, graph.Bounds.Y); PumpLayout(window);
            using (var speedImage = window.CaptureRenderedFrame())
                speedImage?.Save(Path.Combine(output, "replay-studio-camera-speed-readonly.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            void DragGraph(bool shift, bool switchToSpeed)
            {
                scroll.Offset = new Vector(0, graph.Bounds.Y); PumpLayout(window);
                double keyX = 10 + (graph.Bounds.Width - 20) / 3;
                Point start = graph.TranslatePoint(new Point(shift ? 5 : keyX, 20), window)!.Value;
                Point end = graph.TranslatePoint(new Point(shift ? graph.Bounds.Width - 5 : keyX + 15, graph.Bounds.Height - 20), window)!.Value;
                var modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
                window.MouseDown(start, MouseButton.Left, modifiers);
                window.MouseMove(end, modifiers | RawInputModifiers.LeftMouseButton);
                if (switchToSpeed)
                {
                    // The initial FOV press legitimately selects its visible key;
                    // switching channel must retain that selection on release.
                    selectedBefore = graph.SelectedFrames.Order().ToArray();
                    graphChannel.SelectedItem = ReplayCameraGraphChannel.Speed; graph.InvalidateVisual();
                }
                window.MouseUp(end, MouseButton.Left, modifiers); PumpLayout(window);
            }
            async Task AssertDerivedGestureAsync(string label)
            {
                await player.FlushCameraEditsAsync();
                Check(Convert.ToBase64String(player.ExportCameraSidecarState()) == before
                    && File.ReadAllBytes(player.LogicalPath + ".camera").SequenceEqual(durableBefore)
                    && graph.SelectedFrames.Order().SequenceEqual(selectedBefore)
                    && SelectedKeyFrames().SequenceEqual(selectedBefore), label);
            }

            void DragBox(double top, double bottom)
            {
                scroll.Offset = new Vector(0, graph.Bounds.Y); PumpLayout(window);
                Point start = graph.TranslatePoint(new Point(5, top), window)!.Value;
                Point end = graph.TranslatePoint(new Point(graph.Bounds.Width - 5, bottom), window)!.Value;
                window.MouseDown(start, MouseButton.Left, RawInputModifiers.Shift);
                window.MouseMove(end, RawInputModifiers.Shift | RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left, RawInputModifiers.Shift); PumpLayout(window);
            }
            uint[] SelectedKeyFrames() => keys.SelectedItems!.Cast<object>().Select(item =>
                ((StudioReplayCameraKey)item.GetType().GetProperty("Key")!.GetValue(item)!).Frame).Order().ToArray();
        }
        finally
        {
            player.ImportCameraSidecarState(original); await player.FlushCameraEditsAsync();
            refresh.Invoke(document.Host, null); player.Seek(originalFrame, resume: false);
            var wait = System.Diagnostics.Stopwatch.StartNew();
            do { player.Advance(TimeSpan.Zero); await Task.Delay(1); }
            while (!player.Status.Ready && wait.Elapsed < TimeSpan.FromSeconds(10));
            Check(player.Status.Ready && player.Status.Frame == originalFrame,
                "camera graph fixture restores its original authoring state and canonical transport frame");
            if(document.Session!.GetType().GetProperty("HasPendingTransportJobs") is { } transportJobs)
            {
                wait.Restart();
                while((bool)transportJobs.GetValue(document.Session)!&&wait.Elapsed<TimeSpan.FromSeconds(10))
                {player.Advance(TimeSpan.Zero);await Task.Delay(1);}
                Check(!(bool)transportJobs.GetValue(document.Session)!,
                    "camera graph fixture drains all superseded selection-seek observers before later document work");
            }
            scroll.Offset = originalOffset; graph.InvalidateVisual(); PumpLayout(window);
        }
    }
}
