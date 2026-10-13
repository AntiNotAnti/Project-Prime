using System;
using System.Collections;
using System.Collections.Generic;
using MphRead.Formats.Collision;

namespace MphRead.Formats
{
    /// <summary>
    /// A synchronous collision-query lease. Candidates and enumerators are valid until Dispose;
    /// callers must not retain them afterwards. Nested borrows have separate scratch storage.
    /// Each scene still owns its collision data; this lease does not make scene mutation concurrent-safe.
    /// </summary>
    public sealed class CollisionQueryWorkspace : IReadOnlyList<CollisionCandidate>, IDisposable
    {
        private const int MaxRetainedWorkspaces = 8;
        // The supported 4,225-cell room fixture needs the next geometric capacity,
        // 8,192. Retain that high-water scratch instead of allocating it each query.
        // Eight cached workspaces still bound each lane to 65,536 candidate objects.
        private const int MaxRetainedCandidates = 8192;
        private const int MaxRetainedFaces = 16384;
        [ThreadStatic] private static Stack<CollisionQueryWorkspace>? _available;
        private bool _borrowed;
        private int _ownerThread;
        internal List<CollisionCandidate> Items { get; } = new(64);
        // Preserve reverse room traversal, followed by reverse entity traversal.
        // Each population pass drains this stack before the next pass begins.
        internal Stack<CollisionCandidate> PendingItems { get; } = new(64);
        private readonly Queue<CollisionCandidate> _inactiveItems = new(64);
        internal HashSet<CollisionFace> SeenFaces { get; } = new(64);

        private CollisionQueryWorkspace() { }

        internal static CollisionQueryWorkspace Borrow()
        {
            _available ??= new Stack<CollisionQueryWorkspace>(MaxRetainedWorkspaces);
            var workspace = _available.Count > 0 ? _available.Pop() : new CollisionQueryWorkspace();
            workspace._borrowed = true;
            workspace._ownerThread = Environment.CurrentManagedThreadId;
            return workspace;
        }

        internal CollisionCandidate RentCandidate()
            => _inactiveItems.Count > 0 ? _inactiveItems.Dequeue() : new CollisionCandidate(null!, default);

        public int Count { get { ValidateBorrow(); return Items.Count; } }
        public CollisionCandidate this[int index] { get { ValidateBorrow(); return Items[index]; } }
        public IEnumerator<CollisionCandidate> GetEnumerator()
        {
            ValidateBorrow();
            return Items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void ValidateBorrow()
        {
            if (!_borrowed) throw new ObjectDisposedException(nameof(CollisionQueryWorkspace));
            if (_ownerThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Collision query leases must stay on the borrowing thread.");
        }

        private void ReturnCandidate(CollisionCandidate item, bool retain)
        {
            item.EntityCollision = null;
            item.Collision = null!;
            item.Entry = default;
            if (retain) _inactiveItems.Enqueue(item);
        }

        public void Dispose()
        {
            if (!_borrowed) return;
            ValidateBorrow();
            // This also handles a population failure before pending items are drained.
            bool retain = Items.Capacity <= MaxRetainedCandidates
                && PendingItems.EnsureCapacity(0) <= MaxRetainedCandidates
                && SeenFaces.EnsureCapacity(0) <= MaxRetainedFaces;
            while (PendingItems.Count > 0) ReturnCandidate(PendingItems.Pop(), retain);
            for (int i = Items.Count - 1; i >= 0; i--) ReturnCandidate(Items[i], retain);
            Items.Clear();
            SeenFaces.Clear();
            _borrowed = false;
            // Bound both retained object counts and high-water collection capacity.
            if (retain && _inactiveItems.Count <= MaxRetainedCandidates
                && _inactiveItems.EnsureCapacity(0) <= MaxRetainedCandidates && _available!.Count < MaxRetainedWorkspaces)
                _available.Push(this);
        }
    }
}
