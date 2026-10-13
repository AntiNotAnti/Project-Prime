using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

// A scene-local, bounded interner. Equality is always the full descriptor, never
// a hash alone. Items retain identities but identities do not retain the scene.
internal sealed class RetainedMaterialIdentity
{
    internal readonly RetainedBatchState State;
    internal readonly object Owner;
    internal RetainedMaterialIdentity(RetainedBatchState state, object owner)
    { State = state; Owner = owner; }

    internal sealed class Cache
    {
        private object _owner = new();
        private readonly Dictionary<RetainedBatchState, RetainedMaterialIdentity> _states = new();
        internal RetainedMaterialIdentity Get(RenderItem item, RetainedBatchState state)
        {
            if (item.CachedMaterialIdentity is { } cached && ReferenceEquals(cached.Owner, _owner)
                && cached.State == state)
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.RoomIdentityReuses);
                return cached;
            }
            if (!_states.TryGetValue(state, out var identity))
            {
                if (_states.Count >= 2048) Clear();
                identity = new(state, _owner);
                _states.Add(state, identity);
            }
            return item.CachedMaterialIdentity = identity;
        }
        internal void Clear() { _states.Clear(); _owner = new(); }
    }

    internal static int Check()
    {
        int failures = 0;
        void Test(bool pass, string name) { Console.WriteLine($"[roomidentitycheck] {(pass ? "PASS" : "FAIL")}: {name}"); if (!pass) failures++; }
        var cache = new Cache(); var a = new RenderItem(); var b = new RenderItem();
        var state = new RetainedBatchState(a.CullingMode, a.Wireframe, a.RenderMode, RetainedMaterialDescriptor.From(a));
        var token = cache.Get(a, state);
        Test(ReferenceEquals(token, cache.Get(b, state)), "equal materials across meshes share exact identity");
        Test(ReferenceEquals(token, cache.Get(a, state)), "unchanged item reuses identity");
        var changed = state with { Material = state.Material with { EmissiveIntensity = 2 } };
        Test(!ReferenceEquals(token, cache.Get(a, changed)), "material change invalidates cached identity");
        cache.Clear(); Test(!ReferenceEquals(token, cache.Get(b, state)), "scene reset invalidates old ownership");
        return failures;
    }
}
