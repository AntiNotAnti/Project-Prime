#if (ANDROID && PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT) || MPHREAD_RMLUI_DRAWLIST_TEST
using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.RmlUi.Render;

// RML-19: pure, owner-thread-independent command interpretation. Android GLES
// consumes these immutable operations; no native pointers or GL calls escape here.
internal enum RmlUiGlesOperationKind { Geometry, MaskSet, MaskSetInverse, MaskIntersect }

internal readonly record struct RmlUiGlesRect(int X, int Y, int Width, int Height)
{
    internal bool Visible => Width > 0 && Height > 0;
}

internal sealed class RmlUiGlesOperation
{
    internal RmlUiGlesOperationKind Kind { get; }
    internal ulong Geometry { get; }
    internal ulong Texture { get; }
    internal RmlUiGlesRect Scissor { get; }
    internal bool ClipEnabled { get; }
    internal byte StencilReference { get; }
    internal float TranslationX { get; }
    internal float TranslationY { get; }
    internal float[] Transform { get; }

    internal RmlUiGlesOperation(RmlUiGlesOperationKind kind, ulong geometry, ulong texture,
        RmlUiGlesRect scissor, bool clipEnabled, byte stencilReference,
        float translationX, float translationY, float[] transform)
    {
        Kind = kind;
        Geometry = geometry;
        Texture = texture;
        Scissor = scissor;
        ClipEnabled = clipEnabled;
        StencilReference = stencilReference;
        TranslationX = translationX;
        TranslationY = translationY;
        Transform = transform;
    }
}

internal static unsafe class RmlUiGlesPlan
{
    // RmlUi uses top-left framebuffer coordinates. GL scissor uses bottom-left.
    // Compute in long to avoid overflow when clipping untrusted native rectangles.
    internal static RmlUiGlesRect Scissor(int x, int y, int w, int h, int targetW, int targetH)
    {
        if (targetW <= 0 || targetH <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetW));
        long left = Math.Clamp((long)x, 0, targetW);
        long right = Math.Clamp((long)x + Math.Max(0L, w), 0, targetW);
        long top = Math.Clamp((long)y, 0, targetH);
        long bottom = Math.Clamp((long)y + Math.Max(0L, h), 0, targetH);
        return new((int)left, checked(targetH - (int)bottom),
            (int)(right - left), (int)(bottom - top));
    }

    internal static RmlUiGlesOperation[] Build(RmlUiDrawListFrame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (frame.Generation == 0)
            throw new InvalidOperationException("RmlUi draw generation must be nonzero.");

        bool scissorEnabled = false, clipEnabled = false;
        int scissorX = 0, scissorY = 0, scissorW = width, scissorH = height;
        byte clipReference = 1;
        float[] transform = Identity();
        var operations = new List<RmlUiGlesOperation>(frame.Commands.Length);

        foreach (RmlUiDrawCommand command in frame.Commands)
        {
            if (command.Size != sizeof(RmlUiDrawCommand)
                || !float.IsFinite(command.TranslationX) || !float.IsFinite(command.TranslationY))
                throw new InvalidOperationException("RmlUi command size or translation is invalid.");

            switch (command.Kind)
            {
                case RmlUiDrawCommandKind.EnableScissor:
                    if (command.Enabled > 1) throw new InvalidOperationException("Invalid scissor switch.");
                    scissorEnabled = command.Enabled != 0;
                    continue;
                case RmlUiDrawCommandKind.Scissor:
                    scissorX = command.X; scissorY = command.Y;
                    scissorW = command.Width; scissorH = command.Height;
                    continue;
                case RmlUiDrawCommandKind.EnableClipMask:
                    if (command.Enabled > 1) throw new InvalidOperationException("Invalid clip switch.");
                    clipEnabled = command.Enabled != 0;
                    continue;
                case RmlUiDrawCommandKind.Transform:
                    if (command.Enabled > 1) throw new InvalidOperationException("Invalid transform switch.");
                    transform = command.Enabled != 0 ? CopyMatrix(command) : Identity();
                    continue;
                case RmlUiDrawCommandKind.Geometry:
                case RmlUiDrawCommandKind.ClipMask:
                    break;
                default:
                    throw new InvalidOperationException("Unsupported RmlUi draw operation.");
            }

            if (command.Geometry == 0 || !frame.Geometry.ContainsKey(command.Geometry))
                throw new InvalidOperationException("RmlUi draw references missing geometry.");
            if (command.Kind == RmlUiDrawCommandKind.Geometry
                && command.Texture != 0 && !frame.Textures.ContainsKey(command.Texture))
                throw new InvalidOperationException("RmlUi draw references missing texture.");

            RmlUiGlesOperationKind kind;
            if (command.Kind == RmlUiDrawCommandKind.ClipMask)
            {
                kind = command.Operation switch
                {
                    RmlUiClipOperation.Set => RmlUiGlesOperationKind.MaskSet,
                    RmlUiClipOperation.SetInverse => RmlUiGlesOperationKind.MaskSetInverse,
                    RmlUiClipOperation.Intersect => RmlUiGlesOperationKind.MaskIntersect,
                    _ => throw new InvalidOperationException("Unknown RmlUi stencil operation.")
                };
                if (kind == RmlUiGlesOperationKind.MaskIntersect)
                {
                    if (clipReference == byte.MaxValue)
                        throw new NotSupportedException("RmlUi clip mask nesting exceeds 8-bit stencil.");
                    // The intersect draw reads the previous reference. Later
                    // geometry must compare against the incremented value.
                    byte previous = clipReference;
                    clipReference++;
                    var maskRect = Scissor(scissorEnabled ? scissorX : 0,
                        scissorEnabled ? scissorY : 0, scissorEnabled ? scissorW : width,
                        scissorEnabled ? scissorH : height, width, height);
                    if (maskRect.Visible)
                        operations.Add(new(kind, command.Geometry, 0, maskRect, false, previous,
                            command.TranslationX, command.TranslationY, (float[])transform.Clone()));
                    continue;
                }
                clipReference = 1;
            }
            else kind = RmlUiGlesOperationKind.Geometry;

            RmlUiGlesRect rect = Scissor(scissorEnabled ? scissorX : 0,
                scissorEnabled ? scissorY : 0, scissorEnabled ? scissorW : width,
                scissorEnabled ? scissorH : height, width, height);
            if (!rect.Visible) continue;
            operations.Add(new(kind, command.Geometry,
                kind == RmlUiGlesOperationKind.Geometry ? command.Texture : 0,
                rect, kind == RmlUiGlesOperationKind.Geometry && clipEnabled,
                kind == RmlUiGlesOperationKind.MaskSetInverse ? (byte)0 : clipReference,
                command.TranslationX, command.TranslationY, (float[])transform.Clone()));
        }
        return operations.ToArray();
    }

    private static float[] Identity() =>
        new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    private static float[] CopyMatrix(RmlUiDrawCommand command)
    {
        var transform = new float[16];
        for (int i = 0; i < transform.Length; i++)
        {
            float value = command.Transform[i];
            if (!float.IsFinite(value))
                throw new InvalidOperationException("RmlUi matrix contains a nonfinite element.");
            transform[i] = value;
        }
        return transform;
    }
}
#endif
