using System;
using System.Collections.Immutable;

namespace MphRead.Mods.Network;

public readonly record struct VoteResolution(IntermissionChoice Kind, int RotationIndex);

/// <summary>One server-owned ballot. Slot identities fence reconnects; count updates never replace option-set revision.</summary>
public sealed class ServerVoteSession
{
    private readonly IntermissionOption[] _options = new IntermissionOption[8];
    private readonly int[] _rotationIndices = new int[8];
    private readonly ulong[] _identities = new ulong[8];
    private readonly byte[] _votes = new byte[8];
    private int _count;
    private uint _random;
    private VoteResolution? _result;
    public uint MatchId { get; private set; }
    public uint PhaseRevision { get; private set; }
    public uint Revision { get; private set; }
    public uint UpdateRevision { get; private set; }
    public uint DeadlineTick { get; private set; }
    public bool HasDeadline { get; private set; }
    public MatchPhase Phase { get; private set; }
    public bool Active => _count > 0 && _result == null;
    public uint RandomState => _random;
    public bool Dirty { get; private set; }
    public ServerVoteSession(uint seed) { _random = seed; }

    public void Begin(uint matchId, uint phaseRevision, MatchPhase phase, uint deadlineTick,
        VotePolicy policy, MapRotation? rotation, MatchRules current)
    {
        if (matchId == 0 || phaseRevision == 0 || phase is not (MatchPhase.Intermission or MatchPhase.WaitingForPlayers))
            throw new ArgumentException("Voting requires intermission or an explicit held lobby.");
        MatchId = matchId; PhaseRevision = phaseRevision; Phase = phase;
        Revision = unchecked(Revision + 1); if (Revision == 0) Revision = 1;
        UpdateRevision = 1;
        DeadlineTick = deadlineTick; HasDeadline = phase == MatchPhase.Intermission;
        _count = 0; _result = null; Array.Clear(_votes); Array.Clear(_identities);
        Add(IntermissionChoice.Rematch, "Rematch", -1);
        Add(IntermissionChoice.NextMap, "Next map", -1);
        if (policy == VotePolicy.PrivateRematch && phase == MatchPhase.Intermission) Add(IntermissionChoice.Lobby, "Return to lobby", -1);
        else if (policy == VotePolicy.PublicRotation && rotation != null)
        {
            for (int offset = 1; offset <= rotation.Entries.Count && _count < 8; offset++)
            {
                int index = (rotation.Index + offset) % rotation.Entries.Count;
                RotationEntry entry = rotation.Entries[index];
                if (entry.RoomKey == current.RoomKey && entry.Mode.ToMatchMode() == current.Mode) continue;
                Add(IntermissionChoice.Map, entry.RoomKey, index);
            }
        }
        Dirty = true;
    }
    private void Add(IntermissionChoice kind, string label, int rotationIndex)
    {
        _rotationIndices[_count] = rotationIndex;
        _options[_count] = new((byte)(_count + 1), kind, 0, label); _count++;
    }
    public void SetEligible(ReadOnlySpan<ulong> connectedHumanIdentities)
    {
        if (_result != null) return;
        if (connectedHumanIdentities.Length != 8) throw new ArgumentException("Expected eight player slots.");
        for (int slot = 0; slot < 8; slot++)
            for (int prior = 0; prior < slot; prior++)
                if (connectedHumanIdentities[slot] != 0 && connectedHumanIdentities[slot] == connectedHumanIdentities[prior])
                    throw new ArgumentException("A human identity cannot occupy two vote slots.");
        for (int slot = 0; slot < 8; slot++)
            if (_identities[slot] != connectedHumanIdentities[slot])
            {
                _identities[slot] = connectedHumanIdentities[slot]; _votes[slot] = 0; Changed();
            }
    }
    public bool Cast(byte slot, ulong identity, in IntermissionVoteRequest request, uint tick)
    {
        if (!Active || slot >= 8 || identity == 0 || _identities[slot] != identity
            || request.MatchId != MatchId || request.PhaseRevision != PhaseRevision || request.BallotRevision != Revision
            || request.OptionId == 0 || request.OptionId > _count || HasDeadline && (tick == DeadlineTick || Sequence32.IsNewer(tick, DeadlineTick))) return false;
        if (_votes[slot] != 0) return _votes[slot] == request.OptionId;
        _votes[slot] = request.OptionId; Changed();
        if (Phase == MatchPhase.WaitingForPlayers && !HasDeadline)
        { DeadlineTick = unchecked(tick + MatchLifecycle.IntermissionTicks); HasDeadline = true; }
        return true;
    }
    public bool DeadlineReached(uint tick) => HasDeadline && (tick == DeadlineTick || Sequence32.IsNewer(tick, DeadlineTick));
    public VoteResolution Resolve()
    {
        if (_result is VoteResolution result) return result;
        if (_count == 0) throw new InvalidOperationException("No ballot exists.");
        Span<int> counts = stackalloc int[8]; counts.Clear();
        foreach (byte vote in _votes) if (vote != 0) counts[vote - 1]++;
        int maximum = 0;
        for (int i = 0; i < _count; i++) maximum = Math.Max(maximum, counts[i]);
        int winner = 1; // No votes preserve ordinary next-map rotation, without consuming RNG.
        if (maximum > 0)
        {
            Span<int> tied = stackalloc int[8]; int ties = 0;
            for (int i = 0; i < _count; i++) if (counts[i] == maximum) tied[ties++] = i;
            winner = tied[ties == 1 ? 0 : (int)Rng.CallRng(ref _random, (uint)ties)];
        }
        _result = new VoteResolution(_options[winner].Kind, _rotationIndices[winner]);
        return _result.Value;
    }
    public IntermissionBallot Snapshot(byte viewerSlot, ulong? viewerIdentity = null)
    {
        Span<byte> counts = stackalloc byte[8]; counts.Clear(); byte eligible = 0;
        for (int slot = 0; slot < 8; slot++)
        {
            if (_identities[slot] != 0) eligible++;
            if (_votes[slot] != 0) counts[_votes[slot] - 1]++;
        }
        var options = ImmutableArray.CreateBuilder<IntermissionOption>(_count);
        for (int i = 0; i < _count; i++) options.Add(_options[i] with { Votes = counts[i] });
        return new(MatchId, PhaseRevision, Revision, DeadlineTick, Phase, HasDeadline,
            viewerSlot < 8 && (!viewerIdentity.HasValue || _identities[viewerSlot] == viewerIdentity.Value) ? _votes[viewerSlot] : (byte)0, eligible, options.MoveToImmutable(), UpdateRevision);
    }
    private void Changed()
    {
        UpdateRevision = unchecked(UpdateRevision + 1); if (UpdateRevision == 0) UpdateRevision = 1;
        Dirty = true;
    }
    public void MarkPublished() => Dirty = false;
}
