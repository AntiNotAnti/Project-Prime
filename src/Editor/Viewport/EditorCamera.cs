using MphRead;
using OpenTK.Mathematics;

namespace ProjectPrime.Editor.Viewport;

public enum EditorViewMode { Perspective, Top, Front, Side }

public sealed class EditorCamera
{
    private Vector3 _focus = Vector3.Zero;
    public Vector3 Position { get; private set; } = new(12, 10, 12);
    public float Yaw { get; private set; } = -135;
    public float Pitch { get; private set; } = -25;
    public float FieldOfView { get; set; } = 65;
    public float OrthographicScale { get; private set; } = 24;
    public EditorViewMode Mode { get; private set; }
    public Vector3 EyePosition => Mode switch
    {
        EditorViewMode.Top => _focus + Vector3.UnitY * 100,
        EditorViewMode.Front => _focus + Vector3.UnitZ * 100,
        EditorViewMode.Side => _focus + Vector3.UnitX * 100,
        _ => Position
    };

    public Vector3 Forward
    {
        get
        {
            float yaw = MathHelper.DegreesToRadians(Yaw);
            float pitch = MathHelper.DegreesToRadians(Pitch);
            return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw)).Normalized();
        }
    }

    public Matrix4 View => Mode switch
    {
        EditorViewMode.Top => Matrix4.LookAt(EyePosition, _focus, -Vector3.UnitZ),
        EditorViewMode.Front => Matrix4.LookAt(EyePosition, _focus, Vector3.UnitY),
        EditorViewMode.Side => Matrix4.LookAt(EyePosition, _focus, Vector3.UnitY),
        _ => Matrix4.LookAt(Position, Position + Forward, Vector3.UnitY)
    };

    public Matrix4 Projection(float aspect, float farClip)
        => Mode == EditorViewMode.Perspective
            ? Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(FieldOfView),
                aspect, 0.05f, farClip)
            : Matrix4.CreateOrthographic(OrthographicScale * aspect, OrthographicScale,
                0.05f, Math.Max(200, farClip));

    public void Update(RenderSurfaceInput input, float elapsedSeconds, bool viewportHovered)
    {
        if (!viewportHovered) return;
        if (Mode != EditorViewMode.Perspective)
        {
            Vector3 horizontal = Mode == EditorViewMode.Side ? Vector3.UnitZ : Vector3.UnitX;
            Vector3 vertical = Mode == EditorViewMode.Top ? Vector3.UnitZ : Vector3.UnitY;
            float pan = OrthographicScale * 0.6f * elapsedSeconds;
            if (input.Down(RenderSurfaceKey.A)) _focus -= horizontal * pan;
            if (input.Down(RenderSurfaceKey.D)) _focus += horizontal * pan;
            if (input.Down(RenderSurfaceKey.W)) _focus += vertical * pan;
            if (input.Down(RenderSurfaceKey.S)) _focus -= vertical * pan;
            if (input.RightMouse)
            {
                _focus -= horizontal * input.MouseDelta.X * OrthographicScale / 700;
                _focus += vertical * input.MouseDelta.Y * OrthographicScale / 700;
            }
            if (input.MouseWheel.Y != 0)
                OrthographicScale = Math.Clamp(OrthographicScale
                    * MathF.Pow(0.85f, input.MouseWheel.Y), 1, 1000);
            return;
        }
        float speed = (input.Down(RenderSurfaceKey.LeftShift) || input.Down(RenderSurfaceKey.RightShift))
            ? 18 : 7;
        Vector3 forward = new(Forward.X, 0, Forward.Z);
        if (forward.LengthSquared > 0) forward.Normalize();
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY).Normalized();
        if (input.Down(RenderSurfaceKey.W)) Position += forward * speed * elapsedSeconds;
        if (input.Down(RenderSurfaceKey.S)) Position -= forward * speed * elapsedSeconds;
        if (input.Down(RenderSurfaceKey.D)) Position += right * speed * elapsedSeconds;
        if (input.Down(RenderSurfaceKey.A)) Position -= right * speed * elapsedSeconds;
        if (input.Down(RenderSurfaceKey.E)) Position += Vector3.UnitY * speed * elapsedSeconds;
        if (input.Down(RenderSurfaceKey.Q)) Position -= Vector3.UnitY * speed * elapsedSeconds;
        if (input.RightMouse)
        {
            Yaw += input.MouseDelta.X * 0.16f;
            Pitch = Math.Clamp(Pitch - input.MouseDelta.Y * 0.16f, -89, 89);
        }
        if (input.MouseWheel.Y != 0) Position += Forward * input.MouseWheel.Y * 1.5f;
    }

    public void Frame(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) / 2;
        float radius = Math.Max(2, (max - min).Length / 2);
        _focus = center;
        OrthographicScale = Math.Max(4, radius * 2.25f);
        Position = center - Forward * radius * 2.2f;
    }

    public void SetMode(EditorViewMode mode)
    {
        Mode = mode;
        if (mode == EditorViewMode.Perspective) Position = _focus - Forward * OrthographicScale;
    }
}
