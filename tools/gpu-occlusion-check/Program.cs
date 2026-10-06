using MphRead.Mods.Render;

// This suite tests the production certificate, not GPU execution. A native
// compute/readback fixture separately tests the visibility shader and pyramid.
var depth = new GpuOcclusionDepthPolicy();
object world = new();
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}
bool Current(ulong revision = 1, bool pose = true) =>
    depth.CanReject(world, revision, 7, 1280, 720, pose);

Check(!Current(), "startup has no depth evidence");
depth.Capture(world, 1, 7, 1280, 720);
Check(Current(), "current extracted room depth is usable");
Check(!Current(2), "moving door/new extraction cannot use previous depth");
Check(!depth.CanReject(new object(), 1, 7, 1280, 720, true),
    "another scene/replay with the same revision cannot reuse depth");
Check(!Current(pose: false), "camera cut or any pose difference rejects depth");
Check(!depth.CanReject(world, 1, 8, 1280, 720, true), "replacement depth attachment");
Check(!depth.CanReject(world, 1, 7, 1279, 720, true), "resized width");
Check(!depth.CanReject(world, 1, 7, 1280, 719, true), "resized height");
depth.Invalidate();
Check(!Current(), "start of prepass discards all older evidence");

// A disappearing occluder changes the current depth from .2 to clear depth 1.
// Prior depth must not hide a packet at .7; only a recaptured current pyramid
// can authorize the shader's rejection test.
depth.Capture(world, 1, 7, 1280, 720);
Check(!Current(2), "disappearing occluder invalidates old rejection");
depth.Capture(world, 2, 7, 1280, 720);
Check(Current(2), "current depth is accepted after world change");
Check(!(0.7f > 1.0f + 0.0025f), "clear current depth cannot occlude");
Check(!(0.7f > 0.7f + 0.0025f), "self depth cannot reject a visible packet");
Check(0.7f > 0.2f + 0.0025f, "current solid occluder can reject hidden packet");

foreach (var invalid in new[] {
    (Revision: 0UL, Texture: 7, Width: 1280, Height: 720),
    (Revision: 1UL, Texture: 0, Width: 1280, Height: 720),
    (Revision: 1UL, Texture: 7, Width: 0, Height: 720),
    (Revision: 1UL, Texture: 7, Width: 1280, Height: -1) })
{
    depth.Capture(world, invalid.Revision, invalid.Texture, invalid.Width, invalid.Height);
    Check(!Current(), "invalid capture cannot leave previous evidence usable");
}
depth.Capture(world, 99, 9, 1, 1);
Check(depth.CanReject(world, 99, 9, 1, 1, true), "minimal valid attachment");
depth.Invalidate();
depth.Invalidate();
Check(!depth.CanReject(world, 99, 9, 1, 1, true), "repeated release/lifecycle invalidation");

Console.WriteLine($"GPU occlusion current-depth policy: {checks} checks passed.");
