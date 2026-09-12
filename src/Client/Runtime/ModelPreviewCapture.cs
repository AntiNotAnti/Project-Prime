using System;
using System.IO;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods;

/// <summary>Desktop renderer-worker lifecycle for one actual local game model.</summary>
internal sealed class ModelPreviewCapture : IRenderToolClient
{
    private readonly IRenderToolHost _host;
    private readonly ModelPreviewSpec _spec;
    private readonly string _outputPath;
    private int _frames = 2;
    private int _attempts;
    private bool _saved;
    private readonly Vector3 _cameraPosition;
    private readonly Vector3 _cameraTarget;

    private ModelPreviewCapture(ModelPreviewSpec spec, string outputPath, IRenderToolHost host)
    {
        _spec = spec;
        _outputPath = outputPath;
        _host = host;
        Scene = new Scene(features: ClientMatchFeatures.Capture());
        Presentation = host.CreatePresentation(Scene);
        ModelInstance source = Read.GetModelInstance(spec.ModelName);
        EntityBase model = Presentation.AddModel(spec.ModelName);
        Vector3 scale = source.Model.Scale * spec.Scale;
        model.Scale = new Vector3(spec.Scale);
        model.Rotation = new Vector3(0, MathF.PI, 0);
        Vector3 min = new(Single.MaxValue);
        Vector3 max = new(Single.MinValue);
        foreach (Node node in source.Model.Nodes)
        {
            if (node.Bounds.Length < 6) continue;
            min = Vector3.ComponentMin(min,
                new Vector3(node.Bounds[0], node.Bounds[1], node.Bounds[2]) * scale);
            max = Vector3.ComponentMax(max,
                new Vector3(node.Bounds[3], node.Bounds[4], node.Bounds[5]) * scale);
        }
        Vector3 center = (min + max) / 2;
        _cameraTarget = new Vector3(-center.X, center.Y, -center.Z);
        float halfExtent = Math.Max((max.X - min.X) / 2, (max.Y - min.Y) / 2);
        halfExtent = Math.Max(halfExtent, (max.Z - min.Z) / 2);
        if (!Single.IsFinite(halfExtent) || halfExtent < 0.01f) halfExtent = 1;
        float distance = halfExtent * spec.FrameMargin
            / MathF.Tan(MathHelper.DegreesToRadians(27.5f));
        _cameraPosition = _cameraTarget + new Vector3(0, 0, Math.Max(0.1f, distance));
        Console.WriteLine($"[previews] {spec.WorkerKey} bounds {min}..{max}, "
            + $"camera {_cameraPosition} -> {_cameraTarget}");
    }

    public Scene Scene { get; }
    public ScenePresentation Presentation { get; }
    public bool Succeeded => _saved;

    public void OnLoad()
    {
        Presentation.Size = _host.Size;
        Presentation.OnLoad();
        Presentation.OnResize();
    }

    public void OnFrame()
    {
        Presentation.OnUpdateFrame();
        Presentation.SetPreviewCamera(_cameraPosition, _cameraTarget);
        if (_frames-- > 0) return;
        var request = new RenderToolCapture(CaptureTargetKind.SceneTarget, _outputPath);
        RenderToolFrameResult frame = _host.Render(Presentation, request);
        for (int i = 0; i < frame.Captures.Count; i++) OnCapture(frame.Captures[i]);
        if (frame.Submitted) _attempts++;
        if (!frame.Submitted || _saved || _attempts >= 3) _host.Close();
        else _frames = 1;
    }

    public void OnCapture(RenderCaptureResult capture)
    {
        if (_saved || capture.Target != CaptureTargetKind.SceneTarget) return;
        _saved = SaveCandidate(capture);
        if (_saved) _host.Close();
    }

    private bool SaveCandidate(RenderCaptureResult capture)
    {
        double lit = RenderToolCaptureSupport.NonBlackFraction(capture);
        Console.WriteLine($"[previews] {_spec.WorkerKey} candidate {lit * 100:0.00}% lit");
        if (lit < 0.0025) return false;
        Action<byte[], int, int, string>? writer = ScreenCapture.PngWriter;
        if (writer == null) return false;
        string? directory = Path.GetDirectoryName(_outputPath);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        writer(capture.CopyBytes(), capture.Width, capture.Height, _outputPath);
        return true;
    }

    public void OnClosing() => Presentation.DoCleanup();

    public static bool Capture(ModelPreviewSpec spec, int width, int height)
    {
        string path;
        try { path = ModelPreviewCatalog.CurrentPath(spec); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] identity failed: {error.Message}");
            return false;
        }
        string temporary = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            ThumbnailMode.Enter();
            RenderOptions.Lighting = false;
            RenderOptions.Fog = false;
            using IRenderToolHost host = RenderToolHostFactory.Create(
                new Vector2i(width, height), $"{Branding.Name} model preview",
                visible: false, presentable: false);
            var capture = new ModelPreviewCapture(spec, temporary, host);
            host.Run(capture);
            if (!capture.Succeeded) return false;
            string? directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] {spec.WorkerKey}: {error}");
            return false;
        }
        finally
        {
            ThumbnailMode.Exit();
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}
