using MphRead.Mods.Replay;
using OpenTK.Mathematics;

internal static class CameraPathCacheChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var track = new ReplayCameraTrack();
        var first = new ReplayCameraKeyframe(0, Vector3.Zero, Quaternion.Identity, 1);
        var last = first with { Frame = 60, Position = new Vector3(10, 0, 0) };
        check(track.Put(first) && track.Put(last), "path cache fixture");
        var cache = new ReplayCameraPathSamples();
        var original = cache.Positions(track);
        check(original.Count == 129 && ReferenceEquals(original, cache.Positions(track))
            && cache.Builds == 1, "unchanged authored path retains exact sample storage");

        // Same key count is not a revision identifier. This mirrors a restored
        // sidecar replacing authored keys without changing the timeline shape.
        check(track.Put(last with { Position = new Vector3(20, 0, 0) }), "replace path endpoint");
        var edited = cache.Positions(track);
        check(cache.Builds == 2 && !ReferenceEquals(original, edited)
            && edited[^1].X == 20 && original[^1].X == 10,
            "same-count edit invalidates samples and leaves retained prior samples intact");
        var secondSource = new ReplayCameraTrack();
        foreach (var key in track.Keys) secondSource.Put(key);
        var switched = cache.Positions(secondSource);
        check(cache.Builds == 3 && !ReferenceEquals(edited, switched),
            "distinct replay track owner invalidates cache even with identical keys");
        check(secondSource.Put(first with { Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, .3f), Fov = 1.2f }),
            "edit camera orientation and FOV");
        var rotated = cache.Positions(secondSource);
        check(cache.Builds == 4 && !ReferenceEquals(switched, rotated),
            "non-position authored edits invalidate complete-key cache");
        secondSource.Clear();
        check(cache.Positions(secondSource).Count == 0 && cache.Builds == 5,
            "clearing track removes stale viewport curve");
        check(cache.Positions(secondSource).Count == 0 && cache.Builds == 5,
            "unchanged empty track does not rebuild every draw");
    }
}
