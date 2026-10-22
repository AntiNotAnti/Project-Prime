#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead;
public partial class Scene
{
    internal bool DrawNativeReplayComparisonViews(Scene compared, Vector4i bounds, Vector2i framebuffer)
    {
        if (ReferenceEquals(this, compared) || !DemoPlayback.Owns(this) || DemoPlayback.Owns(compared)
            || !ReferenceEquals(DemoPlayback.PresentationScene, this) || ReplayController.IsSeeking
            || ReplayVideoExporter.Active || NativeReplayMultiViewActive || compared.NativeReplayMultiViewActive
            || bounds.Z < 4 || bounds.W < 4) return false;

        var bPreview = compared.ReplayPreviewSize;
        var bBounds = compared.ReplayPreviewBounds;
        using (var scope = new ReplayAuxiliaryRenderScope(this, new MultiViewPass { Index = 0 }))
        {
            try
            {
                // Keep original projection and letterbox each image in its half.
                Vector4i FitHalf(int x, int width)
                {
                    double scale = Math.Min(width / (double)Size.X, bounds.W / (double)Size.Y);
                    int fittedWidth = Math.Max(1, (int)Math.Floor(Size.X * scale));
                    int fittedHeight = Math.Max(1, (int)Math.Floor(Size.Y * scale));
                    return new(x + (width - fittedWidth) / 2, bounds.Y + (bounds.W - fittedHeight) / 2,
                        fittedWidth, fittedHeight);
                }
                int left = bounds.Z / 2;
                ReplayPreviewSize = framebuffer;
                ReplayPreviewBounds = FitHalf(bounds.X, left);
                OnDrawFrame();
                if (!OnRenderFrame()) throw new InvalidOperationException("A comparison camera did not render.");
                scope.RestoreCamera();

                compared.ReplayPreviewSize = framebuffer;
                compared.ReplayPreviewBounds = FitHalf(bounds.X + left, bounds.Z - left);
                using (compared.PrepareNativeReplayComparisonPicture(this))
                {
                    MultiViewPasses.Add(compared, new MultiViewPass { Index = 1 });
                    try
                    {
                        if (!compared.OnRenderFrame())
                            throw new InvalidOperationException("B comparison camera did not render.");
                    }
                    finally { MultiViewPasses.Remove(compared); }
                }
                return true;
            }
            finally
            {
                compared.ReplayPreviewSize = bPreview;
                compared.ReplayPreviewBounds = bBounds;
            }
        }
    }
}
#endif
