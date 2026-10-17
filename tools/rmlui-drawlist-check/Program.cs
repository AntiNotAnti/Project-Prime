using System;
using MphRead.Mods.Launcher.RmlUi.Render;

static void Check(bool yes, string label)
{
    if (!yes) throw new InvalidOperationException(label);
    Console.WriteLine("RML DRAW LIST PASS " + label);
}
static bool Reject(Action act)
{
    try { act(); return false; }
    catch (InvalidOperationException) { return true; }
}
var vertices = new[]
{
    new RmlUiVertex { X=0, Y=0, U=0, V=0, Color=0xffffffff },
    new RmlUiVertex { X=1, Y=0, U=1, V=0, Color=0xffffffff },
    new RmlUiVertex { X=0, Y=1, U=0, V=1, Color=0xffffffff }
};
var geom = new RmlUiDrawGeometry(vertices, new[] { 0, 1, 2 });
Check(geom.Vertices.Length == 3 && geom.Indices.Length == 3,
    "valid indexed triangles accepted");
Check(Reject(() => new RmlUiDrawGeometry(vertices, new[] { 0, 1 })),
    "non-triangle index count rejected");
Check(Reject(() => new RmlUiDrawGeometry(vertices, new[] { 0, 1, 9 })),
    "out-of-bounds vertex indices rejected");
var bad = (RmlUiVertex[])vertices.Clone();
bad[1].X = float.NaN;
Check(Reject(() => new RmlUiDrawGeometry(bad, new[] { 0, 1, 2 })),
    "nonfinite coordinates rejected");
var rgba = new byte[4 * 32 * 16];
var tex = new RmlUiDrawTexture(32, 16, rgba);
Check(tex.Width == 32 && tex.Height == 16 && tex.Pixels.Length == rgba.Length,
    "tightly packed premultiplied RGBA accepted");
Check(Reject(() => new RmlUiDrawTexture(32, 16, new byte[100])),
    "wrong RGBA byte count rejected");
Check(Reject(() => new RmlUiDrawTexture(0, 16, rgba)),
    "zero-size atlas rejected");
var command = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.Geometry);
Check(command.Size == 120 && (uint)command.Kind == 1,
    "draw command matches pinned ABI size and geometry discriminator");
var frame = new RmlUiDrawListFrame(1, new[] {command},
    new System.Collections.Generic.Dictionary<ulong, RmlUiDrawGeometry> { [1] = geom },
    new System.Collections.Generic.Dictionary<ulong, RmlUiDrawTexture> { [1] = tex });
Check(frame.Generation == 1 && frame.Geometry.Count == 1 && frame.Textures.Count == 1,
    "native generation frames can retain geometry and texture data");
// RML-19: top-left pixels map to bottom-left GL scissors with safe bounds.
var clipped = RmlUiGlesPlan.Scissor(-10, 15, 30, 30, 100, 90);
Check(clipped == new RmlUiGlesRect(0, 45, 20, 30),
    "scissor clamps coordinates and flips the GL Y origin");
var overflow = RmlUiGlesPlan.Scissor(int.MaxValue, int.MinValue,
    int.MaxValue, int.MaxValue, 100, 100);
Check(!overflow.Visible, "extreme out-of-bounds scissor is empty");
Check(!RmlUiGlesPlan.Scissor(100, 10, 1, 20, 100, 100).Visible,
    "right-edge clipped rectangle cannot overdraw");

var draw = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.Geometry);
draw.Geometry = 1;
draw.Texture = 1;
var scissorOn = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.EnableScissor);
scissorOn.Enabled = 1;
var scissorBox = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.Scissor);
scissorBox.X = 10; scissorBox.Y = 20; scissorBox.Width = 50; scissorBox.Height = 60;
var set = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.ClipMask);
set.Geometry = 1;
set.Operation = RmlUiClipOperation.Set;
var intersect = set;
intersect.Operation = RmlUiClipOperation.Intersect;
var inverse = set;
inverse.Operation = RmlUiClipOperation.SetInverse;
var clipOn = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.EnableClipMask);
clipOn.Enabled = 1;
RmlUiDrawListFrame ClipFrame(RmlUiDrawCommand[] commands) => new(1, commands,
    new System.Collections.Generic.Dictionary<ulong, RmlUiDrawGeometry> { [1] = geom },
    new System.Collections.Generic.Dictionary<ulong, RmlUiDrawTexture> { [1] = tex });
var ops = RmlUiGlesPlan.Build(ClipFrame(new[]
{
    scissorOn, scissorBox, set, clipOn, draw, intersect, draw, inverse, draw
}), 100, 100);
Check(ops.Length == 6 && ops[0].Kind == RmlUiGlesOperationKind.MaskSet
    && ops[1].ClipEnabled && ops[1].StencilReference == 1
    && ops[2].Kind == RmlUiGlesOperationKind.MaskIntersect && ops[2].StencilReference == 1
    && ops[3].StencilReference == 2
    && ops[4].Kind == RmlUiGlesOperationKind.MaskSetInverse && ops[4].StencilReference == 0
    && ops[5].StencilReference == 1,
    "set, intersect and inverse masks use correct eight-bit stencil references");
Check(ops[0].Scissor == new RmlUiGlesRect(10, 20, 50, 60),
    "masked draw retains clamped GL viewport scissor");
Check(Reject(() => RmlUiGlesPlan.Build(ClipFrame(new[]
{
    scissorOn, scissorBox, clipOn, new RmlUiDrawCommand { Size = 120, Kind = RmlUiDrawCommandKind.Geometry, Geometry = 123 }
}), 100, 100)), "missing native geometry is never submitted");
var staleTexture = draw;
staleTexture.Texture = 123;
Check(Reject(() => RmlUiGlesPlan.Build(ClipFrame(new[] { staleTexture }), 100, 100)),
    "released native texture cannot be bound");
var nonfiniteTranslation = draw;
nonfiniteTranslation.TranslationX = float.PositiveInfinity;
Check(Reject(() => RmlUiGlesPlan.Build(ClipFrame(new[] { nonfiniteTranslation }), 100, 100)),
    "nonfinite local translation cannot reach GL uniforms");
var overflowMasks = new RmlUiDrawCommand[256];
overflowMasks[0] = set;
for (int i = 1; i < overflowMasks.Length; i++) overflowMasks[i] = intersect;
Check(!Reject(() => RmlUiGlesPlan.Build(ClipFrame(overflowMasks[..255]), 100, 100)),
    "254 nested clip intersections fit within eight-bit stencil");
Check(RejectNotSupported(() => RmlUiGlesPlan.Build(ClipFrame(overflowMasks), 100, 100)),
    "255th nested clip intersection fails closed");
Console.WriteLine("RmlUi draw-list and GLES operation planning contracts passed.");

static bool RejectNotSupported(Action act)
{
    try { act(); return false; }
    catch (NotSupportedException) { return true; }
}
