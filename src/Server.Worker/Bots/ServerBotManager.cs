using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Identity;
namespace MphRead.Mods.Network;

/// <summary>Simulation-thread ownership; bots never allocate a connection or input stream.</summary>
public sealed class ServerBotManager
{
    private readonly ServerSimulation _simulation;
    private bool _fixedRoster;
    private readonly BotParticipant?[] _slots = new BotParticipant?[8];
    public BotFillPolicy Policy { get; }
    public ReadOnlySpan<BotParticipant?> Participants => _slots;
    public int Count { get { int count = 0; foreach (var bot in _slots) if (bot != null) count++; return count; } }
    public ServerBotManager(ServerSimulation simulation, BotFillPolicy policy)
    { _simulation = simulation; policy.Validate(simulation.Scene.Match.Rules.MaxPlayers); Policy = policy; }
    // Node resolves bot seats before launch. A frozen roster never spawns or
    // retires another participant merely because a human connects or leaves.
    internal void ConfigureFixedRoster(IEnumerable<BotParticipant> participants)
    {
        if (Count != 0) throw new InvalidOperationException("Bot roster is already active.");
        _fixedRoster = true;
        foreach (var bot in participants)
        {
            if (bot.Slot >= _simulation.Scene.Match.Rules.MaxPlayers || _slots[bot.Slot] != null)
                throw new ArgumentException("Invalid or duplicate bot seat.");
            _slots[bot.Slot] = bot;
            Activate(bot);
        }
    }
    public bool Occupied(int slot) => _slots[slot] != null;
    public NetRosterEntry? Roster(int slot) => _slots[slot] is {} bot
        ? new(bot.Slot, bot.Identity, bot.Hunter, bot.TeamIndex, bot.Name, 0, true) : null;
    public void AssignTeam(int slot, byte team)
    {
        if (team >= _simulation.Scene.Match.Rules.TeamCount)
            throw new ArgumentOutOfRangeException(nameof(team));
        if (_slots[slot] is not { } bot || bot.TeamIndex == team) return;
        _slots[slot] = bot with { TeamIndex = team };
        _simulation.Scene.Players[slot].TeamIndex = team;
    }
    public void CancelClaim(int slot)
    {
        if (_slots[slot] is { } bot) bot.RetirementRequested = false;
    }
    public bool Claim(int slot, ServerNetwork network, uint tick)
    {
        if (_slots[slot] is not {} bot) return true;
        if (_fixedRoster) return false;
        bot.RetirementRequested = true;
        if (!Safe(slot)) return false;
        _simulation.Combat.BeginTick(tick);
        Retire(slot, network, tick); return true;
    }
    private bool Safe(int slot) => _simulation.Scene.Match.Phase is MatchPhase.WaitingForPlayers or MatchPhase.Countdown
        || _simulation.Scene.Players[slot].Health == 0;
    public void Update(ServerNetwork network, uint tick)
    {
        if (_fixedRoster || _simulation.Scene.Match.Result != null) return;
        int teamCount = _simulation.Scene.Match.Rules.TeamCount;
        int humans = 0, count = 0;
        Span<int> teams = stackalloc int[MatchRules.MaximumTeamCount];
        teams.Clear();
        foreach (var peer in network.Peers)
            if (peer != null && (peer.Connection.State is NetConnectionState.Ready or NetConnectionState.Playing) && !peer.WaitingForNextMatch) { humans++; if (peer.TeamIndex < teamCount) teams[peer.TeamIndex]++; }
        foreach (var bot in _slots) if (bot != null) { count++; if (bot.TeamIndex < teamCount) teams[bot.TeamIndex]++; }
        int desired = network.Rules.RulesetPreset == RulesetPreset.Duel ? 0 : Policy.DesiredBots(humans, _simulation.Scene.Match.Rules.MaxPlayers);
        int excess = Math.Max(0, count - desired);
        for (int slot = 7; slot >= 0; slot--)
        {
            if (_slots[slot] is not {} bot) continue;
            if (excess > 0) { bot.RetirementRequested = true; excess--; }
            if (bot.RetirementRequested && Safe(slot)) { Retire(slot, network, tick); count--; teams[bot.TeamIndex % teamCount]--; }
        }
        for (int slot = 0; slot < _simulation.Scene.Match.Rules.MaxPlayers && count < desired; slot++)
        {
            if (_slots[slot] != null || network.Peers[slot] != null || network.HasReconnectReservation(slot) || network.HasPendingBotAdmission(slot)) continue;
            byte team = (byte)(_simulation.Scene.Match.Rules.Teams
                ? MinimumTeam(teams, teamCount) : slot);
            ulong identity = NetConnection.NewIdentity();
            var bot = new BotParticipant((byte)slot, identity, (Hunter)(slot % 7), team, $"BOT {slot + 1}");
            _slots[slot] = bot; NetScoreboard.ForgetSlot(_simulation.Scene, slot);
            Activate(bot); count++; if (team < teamCount) teams[team]++;
            network.InvalidateRoster();
        }
    }
    private static int MinimumTeam(ReadOnlySpan<int> teams, int teamCount)
    {
        int selected = 0;
        for (int team = 1; team < teamCount; team++)
            if (teams[team] < teams[selected]) selected = team;
        return selected;
    }
    public void Activate(BotParticipant bot)
    {
        _simulation.Scene.Roster.Nicknames[bot.Slot] = bot.Name;
        _simulation.Scene.Players[bot.Slot].ServerActivate(bot.Identity, bot.Hunter, bot.TeamIndex, Policy.Skill);
    }
    private void Retire(int slot, ServerNetwork network, uint tick)
    {
        _simulation.Reports?.LeaveSlot(_simulation.Scene, slot, tick, ParticipantExitReason.Replaced);
        WorldStateCapture.ReleasePlayer(_simulation.Scene, _simulation.Scene.Players[slot]);
        _simulation.Scene.Players[slot].ServerDeactivate(); NetScoreboard.ForgetSlot(_simulation.Scene, slot);
        _slots[slot] = null; network.InvalidateRoster();
    }
}
