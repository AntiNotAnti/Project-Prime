using System;
using System.Collections.Immutable;
using FruityPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>UI-thread owned selection shared by pointer, keyboard and controller input.</summary>
public sealed class PostMatchSelection
{
    private ImmutableArray<LobbyVoteEntry> _options = ImmutableArray<LobbyVoteEntry>.Empty;
    private uint _revision;
    private bool _pending;
    private bool _closed = true;
    public int SelectedIndex { get; private set; }
    public byte OwnVote { get; private set; }
    public uint BallotRevision => _revision;
    public bool Pending => _pending;
    public bool CanChoose => !Locked;
    public bool Locked => _closed || _pending || OwnVote != 0;
    public void Update(NodeRoundSnapshot? round, DateTimeOffset now)
    {
        if (round == null)
        {
            _options = ImmutableArray<LobbyVoteEntry>.Empty;
            OwnVote = 0;
            _pending = false;
            _closed = true;
            _revision = 0;
            SelectedIndex = 0;
            return;
        }
        if (_revision != round.BallotRevision) { _pending = false; SelectedIndex = 0; }
        _revision = round.BallotRevision;
        _options = round.Options.IsDefault ? ImmutableArray<LobbyVoteEntry>.Empty
            : round.Options.Length > 8 ? round.Options[..8] : round.Options;
        OwnVote = round.OwnVote;
        _closed = _options.IsEmpty || round.ResolvedOption != null || round.VoteDeadline == null
            || now >= round.VoteDeadline || round.TournamentEnded;
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, _options.Length - 1));
    }
    public void Select(int index) { if (index >= 0 && index < _options.Length) SelectedIndex = index; }
    public void Move(int delta)
    {
        if (_options.Length > 0) SelectedIndex = ((SelectedIndex + delta) % _options.Length + _options.Length) % _options.Length;
    }
    public byte? Choose()
    {
        if (Locked) return null;
        _pending = true;
        return _options[SelectedIndex].Id;
    }
    public void RejectPending() => _pending = false;
}
