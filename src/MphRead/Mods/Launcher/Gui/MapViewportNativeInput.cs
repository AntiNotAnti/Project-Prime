using System;
using System.Numerics;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapViewport
{
    internal bool NativePresentationLeased { get; set; }
    internal void NativeCameraMove(double deltaX, double deltaY, bool orbit, bool pan)
    {
        if (!NativePresentationLeased) return;
        var (right, up, _) = Basis();
        float distance = Vector3.Distance(CameraPosition, CameraTarget);
        // Same camera and orbit/pan math as the existing viewport; no independent native camera.
        if (orbit)
        {
            var offset = Vector3.Transform(CameraPosition - CameraTarget, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)-deltaX * .01f));
            var rotated = Vector3.Transform(offset, Quaternion.CreateFromAxisAngle(right, (float)-deltaY * .01f));
            if (Math.Abs(Vector3.Dot(Vector3.Normalize(rotated), Vector3.UnitY)) < .98f) offset = rotated;
            CameraPosition = CameraTarget + offset;
        }
        if (pan)
        {
            var move = (-right * (float)deltaX + up * (float)deltaY) * distance / 500;
            CameraPosition += move; CameraTarget += move;
        }
        InvalidateVisual();
    }
    internal void NativeZoom(double steps)
    {
        if (!NativePresentationLeased) return;
        float distance = Math.Clamp(Vector3.Distance(CameraPosition, CameraTarget) * (float)Math.Pow(.88, Math.Clamp(steps, -100, 100)), .25f, 5000);
        CameraPosition = CameraTarget - Camera.Basis().Forward * distance; InvalidateVisual();
    }
}
