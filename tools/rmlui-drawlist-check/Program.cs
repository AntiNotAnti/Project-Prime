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
Console.WriteLine("RmlUi draw-list geometry/texture source contracts passed.");
