using System.Collections.Generic;
using MphRead.Formats.Collision;

namespace MphRead.Formats;

/// <summary>
/// One query owns this scratch state and its returned candidates for their entire lifetime.
/// A reusable workspace is leased for exactly one query so accidental re-entry
/// fails before it can corrupt the active candidate or duplicate-suppression state.
/// </summary>
internal sealed class CollisionWorkspace
{
    internal sealed class CollisionDataComparer : IEqualityComparer<CollisionData>
    {
        internal static readonly CollisionDataComparer Instance = new();
        public bool Equals(CollisionData x, CollisionData y)
            => x.Counter == y.Counter && x.PlaneIndex == y.PlaneIndex && x.Flags == y.Flags
                && x.LayerMask == y.LayerMask && x.PaddingA == y.PaddingA
                && x.PointIndexCount == y.PointIndexCount && x.PointStartIndex == y.PointStartIndex;
        public int GetHashCode(CollisionData value)
            => System.HashCode.Combine(value.Counter, value.PlaneIndex, value.Flags, value.LayerMask,
                value.PaddingA, value.PointIndexCount, value.PointStartIndex);
    }

    private readonly List<CollisionCandidate> _pool;
    private readonly int _hardCandidateCapacity;
    private int _rented;
    private bool _leased;

    internal List<CollisionCandidate> Candidates { get; }
    // Preserve the original reverse room order followed by reverse entity order.
    internal Stack<CollisionCandidate> Pending { get; }
    internal HashSet<CollisionData> SeenData { get; }

    internal CollisionWorkspace(int candidateCapacity = 64, int seenDataCapacity = 64,
        bool preallocate = false, bool bounded = false)
    {
        if (candidateCapacity < 0) throw new System.ArgumentOutOfRangeException(nameof(candidateCapacity));
        if (seenDataCapacity < 0) throw new System.ArgumentOutOfRangeException(nameof(seenDataCapacity));
        Candidates = new List<CollisionCandidate>(candidateCapacity);
        Pending = new Stack<CollisionCandidate>(candidateCapacity);
        SeenData = new HashSet<CollisionData>(seenDataCapacity, CollisionDataComparer.Instance);
        _pool = new List<CollisionCandidate>(candidateCapacity);
        _hardCandidateCapacity = bounded ? candidateCapacity : int.MaxValue;
        if (preallocate)
        {
            for (int i = 0; i < candidateCapacity; i++)
                _pool.Add(new CollisionCandidate(null!, default));
        }
    }

    internal void BeginQuery()
    {
        if (_leased) throw new System.InvalidOperationException("Collision workspace query cannot recurse.");
        _leased = true;
        ResetCore();
    }

    internal void EndQuery()
    {
        if (!_leased) throw new System.InvalidOperationException("Collision workspace has no active query.");
        ResetCore();
        _leased = false;
    }

    internal List<CollisionCandidate> DetachCandidates()
    {
        if (!_leased) throw new System.InvalidOperationException("Collision workspace has no active query.");
        // The one-shot public APIs return this list, so clearing it would
        // invalidate their result. The workspace has no references back from
        // the list and can be collected after transferring that ownership.
        _leased = false;
        return Candidates;
    }

    internal CollisionCandidate RentCandidate()
    {
        if (!_leased) throw new System.InvalidOperationException("Collision workspace must be leased before use.");
        if (_rented == _hardCandidateCapacity)
            throw new System.InvalidOperationException("Collision workspace exceeded its fixed candidate capacity.");
        if (_rented == _pool.Count)
            _pool.Add(new CollisionCandidate(null!, default));
        return _pool[_rented++];
    }

    private void ResetCore()
    {
        Candidates.Clear();
        Pending.Clear();
        SeenData.Clear();
        _rented = 0;
    }
}
