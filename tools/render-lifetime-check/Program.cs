using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class Program
{
    private static int failures;
    private static int cases;

    private static int Main()
    {
        GL.Reset();
        var shared = new Model(11, 11, 22);
        var first = new TestScene(shared);
        var second = new TestScene(shared);
        first.Release(true);
        Check(GL.Deleted.Count == 0 && shared.Meshes.All(m => m.ListId != 0)
            && SharedModelResources.RetainedModelCount == 1,
            "one scene cannot delete another scene's live shared geometry");
        first.Release(false);
        second.Release(true);
        Check(GL.Deleted.Order().SequenceEqual(new[] { 11, 22 })
            && shared.Meshes.All(m => m.ListId == 0) && SharedModelResources.RetainedModelCount == 0,
            "final owner deletes each distinct native list and clears all handles");
        second.Release(true);
        Check(first.ManagedReleases == 1 && second.ManagedReleases == 1 && GL.Deleted.Count == 2,
            "repeated teardown does not decrement leases or delete twice");

        GL.Reset();
        shared = new Model(31, 32);
        first = new TestScene(shared);
        second = new TestScene(shared);
        first.Release(false);
        Check(shared.Meshes.All(m => m.ListId != 0) && GL.Deleted.Count == 0,
            "lost-context release preserves another scene's lease");
        second.Release(false);
        Check(shared.Meshes.All(m => m.ListId == 0) && GL.Deleted.Count == 0
            && SharedModelResources.RetainedModelCount == 0,
            "final lost-context release clears IDs and CPU roots without driver calls");

        GL.Reset();
        var failed = new Model(41, 41, 42);
        shared = new Model(51);
        first = new TestScene(failed, shared);
        second = new TestScene(shared);
        GL.FailDelete = 41;
        bool threw = false;
        try { first.Release(true); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && failed.Meshes.All(m => m.ListId == 0) && first.Leases.Count == 0
            && first.ManagedReleases == 1 && SharedModelResources.RetainedModelCount == 1
            && shared.Meshes.Single().ListId == 51,
            "failed native deletion still drops all scene owners without over-releasing shared leases");
        first.Release(true);
        second.Release(false);
        Check(SharedModelResources.RetainedModelCount == 0 && shared.Meshes.Single().ListId == 0,
            "faulted teardown cannot leave a model rooted or delete the surviving owner twice");

        GL.Reset();
        first = new TestScene(new Model(61));
        Parallel.Invoke(() => first.Release(false), () => first.Release(false), () => first.Release(false));
        Check(first.ManagedReleases == 1 && SharedModelResources.RetainedModelCount == 0 && GL.Deleted.Count == 0,
            "duplicate terminal notifications perform managed release exactly once");

        int released = 0, managed = 0, native = 0;
        Action? reenter = null;
        reenter = () => RenderResourceLifetime.Release(ref released, true,
            () => { native++; reenter!(); }, () => managed++);
        reenter();
        Check(native == 1 && managed == 1, "reentrant cleanup remains idempotent");

        released = 0; managed = native = 0;
        RenderResourceLifetime.WithNativeReleaseEligibility(false, () =>
            RenderResourceLifetime.WithNativeReleaseEligibility(true, () =>
                RenderResourceLifetime.Release(ref released, true, () => native++, () => managed++)));
        Check(native == 0 && managed == 1 && RenderResourceLifetime.CanReleaseNativeInCurrentScope,
            "nested replay teardown inherits unavailable context and restores outer eligibility");
        bool cleanupThrew = false;
        try { RenderResourceLifetime.WithNativeReleaseEligibility(false, () => throw new InvalidOperationException()); }
        catch (InvalidOperationException) { cleanupThrew = true; }
        Check(cleanupThrew && RenderResourceLifetime.CanReleaseNativeInCurrentScope,
            "cleanup failure cannot poison native eligibility for the next renderer session");

        GL.Reset();
        bool bounded = true;
        for (int cycle = 0; cycle < 128; cycle++)
        {
            var scene = new TestScene(Enumerable.Range(0, 12).Select(i => new Model(100 + i, 100 + i)).ToArray());
            scene.Release((cycle & 1) == 0);
            scene.Release(true);
            bounded &= scene.Leases.Count == 0 && SharedModelResources.RetainedModelCount == 0;
        }
        Check(bounded && GL.Deleted.Count == 64 * 12,
            "128 healthy/lost startup-shutdown cycles leave no owners and bounded native deletion");

        GL.Reset();
        (var holder, var weak) = CreateWeakOwner();
        Collect();
        Check(weak.IsAlive, "active ownership keeps decoded model data alive");
        holder.Release(false);
        Collect();
        Check(!weak.IsAlive && SharedModelResources.RetainedModelCount == 0,
            "released decoded model data is collectable even while the scene object survives");

        Console.WriteLine($"RENDERLIFETIME cases={cases} failures={failures}");
        return failures == 0 ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TestScene, WeakReference) CreateWeakOwner()
    {
        var model = new Model(77);
        return (new TestScene(model), new WeakReference(model));
    }

    private static void Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    private static void Check(bool condition, string name)
    {
        cases++;
        if (!condition) failures++;
        Console.WriteLine($"RENDERLIFETIME {(condition ? "PASS" : "FAIL")} {name}");
    }

    private sealed class TestScene
    {
        private int released;
        internal readonly HashSet<Model> Leases;
        internal int ManagedReleases;
        internal TestScene(params Model[] models)
        {
            Leases = new HashSet<Model>(models);
            foreach (var model in Leases) SharedModelResources.Retain(model);
        }
        internal void Release(bool canReleaseNativeResources)
            => RenderResourceLifetime.Release(ref released, canReleaseNativeResources,
                () => SharedModelResources.ReleaseAll(Leases, true),
                () => { ManagedReleases++; SharedModelResources.ReleaseAll(Leases, false); });
    }
}

// Stand-ins for the data/driver boundary only. The owner registry, lease release,
// idempotence and native-fault handling above are linked production source.
namespace MphRead
{
    internal sealed class Model(params int[] lists)
    {
        internal IReadOnlyList<Mesh> Meshes { get; } = lists.Select(id => new Mesh { ListId = id }).ToArray();
    }
    internal sealed class Mesh { internal int ListId; }
}
namespace OpenTK.Graphics.OpenGL
{
    internal static class GL
    {
        internal static readonly List<int> Deleted = new();
        internal static int FailDelete;
        internal static void DeleteLists(int list, int count)
        {
            Deleted.Add(list);
            if (list == FailDelete) throw new InvalidOperationException("Injected native deletion failure");
        }
        internal static void Reset() { Deleted.Clear(); FailDelete = 0; }
    }
}
