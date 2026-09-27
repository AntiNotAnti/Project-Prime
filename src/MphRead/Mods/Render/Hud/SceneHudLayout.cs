using System;
using MphRead.Mods.Render.Hud;
namespace MphRead;

public partial class Scene
{
    private System.Numerics.Vector3 _profileHudColor = System.Numerics.Vector3.One;
    private float _profileHudScale = 1, _profileHudAlpha = 1, _profileHudX, _profileHudY;
    internal HudLayoutScope PushHudElement(int index, float legacyX, float legacyY, bool visible = true)
    {
        var saved = new HudLayoutScope(this, _profileHudScale, _profileHudAlpha, _profileHudX, _profileHudY, _profileHudColor);
        var runtime = HudProfiles.Runtime;
        if (index < 0 || runtime.Mode != HudMode.Custom || Size.X <= 0 || Size.Y <= 0) return saved;
        var element = runtime[index];
        var transform = new HudTransform(Size.X, Size.Y, runtime.SafeArea);
        var position = transform.Resolve(element.Anchor, element.Offset);
        _profileHudScale = element.Scale * transform.UnitScale / (Size.Y / 1080f);
        _profileHudAlpha = element.Enabled && visible ? element.Opacity : 0;
        _profileHudColor = element.Color;
        _profileHudX = position.X * 256 / Size.X - legacyX * _profileHudScale;
        _profileHudY = position.Y * 192 / Size.Y - legacyY * _profileHudScale;
        return saved;
    }
    internal ColorRgba? HudTextColor(ColorRgba? color)
    {
        if (_profileHudColor == System.Numerics.Vector3.One) return color;
        var c = color ?? new ColorRgba(255, 255, 255, 255);
        return new ColorRgba((byte)(c.Red * _profileHudColor.X), (byte)(c.Green * _profileHudColor.Y), (byte)(c.Blue * _profileHudColor.Z), c.Alpha);
    }
    private static float HudPresentationAlpha(float alpha)
        => HudProfiles.Runtime.Mode == HudMode.Custom && HudProfiles.Runtime.ReduceTransparency && alpha > 0 ? Math.Max(alpha,.85f) : alpha;
    private void HudTint(ref OpenTK.Mathematics.Vector4 color)
    {
        color.X *= _profileHudColor.X; color.Y *= _profileHudColor.Y; color.Z *= _profileHudColor.Z;
    }
    internal readonly struct HudLayoutScope : IDisposable
    {
        private readonly Scene _scene;
        private readonly float _scale, _alpha, _x, _y;
        private readonly System.Numerics.Vector3 _color;
        internal HudLayoutScope(Scene scene, float scale, float alpha, float x, float y, System.Numerics.Vector3 color)
        { _color = color; _scene = scene; _scale = scale; _alpha = alpha; _x = x; _y = y; }
        public void Dispose()
        { _scene._profileHudScale = _scale; _scene._profileHudAlpha = _alpha; _scene._profileHudX = _x; _scene._profileHudY = _y; _scene._profileHudColor = _color; }
    }
}
