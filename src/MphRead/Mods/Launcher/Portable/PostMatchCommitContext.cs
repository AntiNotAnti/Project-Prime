using System;

namespace MphRead.Mods.Launcher;

internal enum PostMatchCommitState { Active, Ending, CommitPending, Committed, Failed, Released }

// The save lease owns immutable bytes/path only. SceneGameState.Owner would
// retain an entire world, so the state is borrowed only during Capture.
internal sealed class PostMatchCommitContext
{
    private readonly object _gate = new();
    private StorySaveCommit? _payload;
    internal PostMatchCommitState State { get; private set; } = PostMatchCommitState.Active;
    internal long RequestVersion { get; }
    internal long SceneGeneration { get; }
    internal byte Slot { get; }
    internal int WriteAttempts { get; private set; }
    internal bool HasPayload => _payload.HasValue;

    private PostMatchCommitContext(SceneGameState state, byte slot, SaveWhen requested, long requestVersion)
    {
        RequestVersion = requestVersion;
        SceneGeneration = state.Owner?.LifetimeGeneration ?? 0;
        Slot = slot;
        State = PostMatchCommitState.Ending;
        if (requested != SaveWhen.Never && slot != 0) _payload = state.CaptureSaveCommit(slot);
        State = PostMatchCommitState.CommitPending;
    }
    internal static PostMatchCommitContext Capture(SceneGameState state, byte slot, SaveWhen requested, long requestVersion)
        => new(state, slot, requested, requestVersion);

    internal void Commit()
    {
        lock (_gate)
        {
            if (State is PostMatchCommitState.Committed or PostMatchCommitState.Released) return;
            if (State is not (PostMatchCommitState.CommitPending or PostMatchCommitState.Failed))
                throw new InvalidOperationException("Post-match save is not ready to commit.");
            try
            {
                if (_payload is { } payload) { WriteAttempts++; payload.Write(); }
                State = PostMatchCommitState.Committed;
            }
            catch { State = PostMatchCommitState.Failed; throw; }
        }
    }
    internal void Release()
    {
        lock (_gate)
        {
            if (State == PostMatchCommitState.Released) return;
            if (State != PostMatchCommitState.Committed)
                throw new InvalidOperationException("A pending or failed save must remain owned until it commits.");
            _payload = null;
            State = PostMatchCommitState.Released;
        }
    }
}
