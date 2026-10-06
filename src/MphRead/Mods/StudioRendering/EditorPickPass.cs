#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Numerics;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.StudioRendering;

public enum StudioPickKind { Object, Face, Edge, Vertex }
public readonly record struct StudioPickElement(Guid ObjectId, int Face, StudioPickKind Kind, int A, int B);
public sealed record StudioPickResult(StudioPickElement? Element, MapPickHit? Surface, bool GpuUsed, bool MatchesCpu);

/// <summary>Integer IDs use a single non-MSAA R32Uint attachment. Zero is always background.</summary>
public static class EditorPickPass
{
    public static StudioPickResult Cpu(MapRenderFrame frame, double x, double y, StudioPickKind kind = StudioPickKind.Face)
    {
        var hit = MapViewportPicking.PickHit(frame, x, y);
        if (hit is not { } surface) return new(null, null, false, true);
        var element = new StudioPickElement(surface.ObjectId, surface.FaceIndex, kind, -1, -1);
        if (kind is StudioPickKind.Edge or StudioPickKind.Vertex)
        {
            foreach (var mesh in frame.Meshes)
            {
                if (mesh.ObjectId != surface.ObjectId || surface.FaceIndex >= mesh.Faces.Count) continue;
                var face = mesh.Faces[surface.FaceIndex];
                var transform = frame.PreviewTransforms.TryGetValue(mesh.ObjectId, out var preview) ? preview : Matrix4x4.Identity;
                double nearest = 9 * 9;
                for (int i = 0; i < face.Points.Length; i++)
                {
                    var a = frame.Camera.Project(frame.Layout, Vector3.Transform(face.Points[i], transform));
                    if (a == null) continue;
                    int next = (i + 1) % face.Points.Length;
                    var b = frame.Camera.Project(frame.Layout, Vector3.Transform(face.Points[next], transform));
                    double distance;
                    if (kind == StudioPickKind.Vertex) distance = Math.Pow(a.Value.X-x, 2) + Math.Pow(a.Value.Y-y, 2);
                    else
                    {
                        if (b == null) continue;
                        double dx = b.Value.X-a.Value.X, dy = b.Value.Y-a.Value.Y;
                        double t = Math.Clamp(((x-a.Value.X)*dx + (y-a.Value.Y)*dy) / Math.Max(1e-10, dx*dx+dy*dy), 0, 1);
                        distance = Math.Pow(a.Value.X+t*dx-x, 2) + Math.Pow(a.Value.Y+t*dy-y, 2);
                    }
                    if (distance < nearest) { nearest = distance; element = element with { A = i, B = kind == StudioPickKind.Edge ? next : -1 }; }
                }
                break;
            }
        }
        return new(element, hit, false, true);
    }
    // Encoded fallback is bit-preserving RGBA8Unorm, never sRGB/premultiplied or multisampled.
    public static uint DecodeRgba(byte r, byte g, byte b, byte a) => (uint)(r | g << 8 | b << 16 | a << 24);
    public static (byte R, byte G, byte B, byte A) EncodeRgba(uint value) => ((byte)value, (byte)(value>>8), (byte)(value>>16), (byte)(value>>24));
}
#endif
