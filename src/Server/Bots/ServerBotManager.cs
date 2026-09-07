using System;
using MphRead.Entities;
using MphRead.Identity;
namespace MphRead.Mods.Network;

/// <summary>Simulation-thread ownership; bots never allocate a connection or input stream.</summary>
public sealed class ServerBotManager
{
    private readonly ServerSimulation _simulation;
    private readonly BotParticipant?[] _slots = new BotParticipant?[8];
    public BotFillPolicy Policy { get; private set; }
    public ReadOnlySpan<BotParticipant?> Participants => _slots;
    public int Count { get { int count = 0; foreach (var bot in _slots) if (bot != null) count++; return count; } }
    public ServerBotManager(ServerSimulation simulation, BotFillPolicy policy)
    { _simulation = simulation; policy.Validate(simulation.Scene.Match.Rules.MaxPlayers); Policy = policy; }
    public bool ApplyLobbyPolicy(BotFillPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate(_simulation.Scene.Match.Rules.MaxPlayers);
        if (Policy == policy) return false;
        Policy = policy;
        return true;
    }
    public bool Occupied(int slot) => _slots[slot] != null;
    public NetRosterEntry? Roster(int slot) => _slots[slot] is {} bot
        ? new(bot.Slot, bot.Identity, bot.Hunter, bot.TeamIndex, bot.Name, 0, true) : null;
    public void AssignTeam(int slot, byte team)
    {
        if (team > 1) throw new ArgumentOutOfRangeException(nameof(team));
        if (_slots[slot] is not { } bot || bot.TeamIndex == team) return;
        _slots[slot] = bot with { TeamIndex = team };
        PlayerEntity.Players[slot].TeamIndex = team;
    }
    public void CancelClaim(int slot)
    {
        if (_slots[slot] is { } bot) bot.RetirementRequested = false;
    }
    public bool Claim(int slot, ServerNetwork network, uint tick)
    {
        if (_slots[slot] is not {} bot) return true;
        bot.RetirementRequested = true;
        if (!Safe(slot)) return false;
        using var combatScope = _simulation.Combat.Enter(tick);
        Retire(slot, network, tick); return true;
    }
    private bool Safe(int slot) => _simulation.Scene.Match.Phase is MatchPhase.WaitingForPlayers or MatchPhase.Countdown
        || PlayerEntity.Players[slot].Health == 0;
    public void Update(ServerNetwork network, uint tick)
    {
        if (_simulation.Scene.Match.Result != null) return;
        int humans = 0, count = 0; Span<int> teams = stackalloc int[2];
        teams.Clear();
        foreach (var peer in network.Peers)
            if (peer != null && (peer.Connection.State is NetConnectionState.Ready or NetConnectionState.Playing) && !peer.WaitingForNextMatch) { humans++; if (peer.TeamIndex < 2) teams[peer.TeamIndex]++; }
        foreach (var bot in _slots) if (bot != null) { count++; if (bot.TeamIndex < 2) teams[bot.TeamIndex]++; }
        int desired = network.Rules.RulesetPreset == RulesetPreset.Duel ? 0 : Policy.DesiredBots(humans, _simulation.Scene.Match.Rules.MaxPlayers);
        int excess = Math.Max(0, count - desired);
        for (int slot = 7; slot >= 0; slot--)
        {
            if (_slots[slot] is not {} bot) continue;
            if (excess > 0) { bot.RetirementRequested = true; excess--; }
            if (bot.RetirementRequested && Safe(slot)) { Retire(slot, network, tick); count--; teams[bot.TeamIndex % 2]--; }
        }
        for (int slot = 0; slot < _simulation.Scene.Match.Rules.MaxPlayers && count < desired; slot++)
        {
            if (_slots[slot] != null || network.Peers[slot] != null || network.HasReconnectReservation(slot) || network.HasPendingBotAdmission(slot)) continue;
            byte team = (byte)(_simulation.Scene.Match.Rules.Teams ? teams[0] <= teams[1] ? 0 : 1 : slot);
            ulong identity = NetConnection.NewIdentity();
            var bot = new BotParticipant((byte)slot, identity, (Hunter)(slot % 7), team, $"BOT {slot + 1}");
            _slots[slot] = bot; NetScoreboard.ForgetSlot(_simulation.Scene, slot);
            Activate(bot); count++; if (team < 2) teams[team]++;
            network.InvalidateRoster();
        }
    }

    /// <summary>
    /// Reuses the authoritative bot roster while the gameplay scene is fenced by
    /// the lobby. Bodies are activated later by the existing countdown reset.
    /// </summary>
    public void UpdateLobby(ServerNetwork network)
    {
        int humans = 0, count = 0;
        Span<int> teams = stackalloc int[2];
        teams.Clear();
        foreach (ServerPeer? peer in network.Peers)
        {
            if (peer == null || peer.IsObserver) continue;
            humans++;
            if (peer.TeamIndex < 2) teams[peer.TeamIndex]++;
        }
        foreach (BotParticipant? bot in _slots)
            if (bot != null) { count++; if (bot.TeamIndex < 2) teams[bot.TeamIndex]++; }
        int desired = network.Rules.RankingEligibility == RankingEligibility.VerifiedServerOnly
            || network.Rules.RulesetPreset == RulesetPreset.Duel
            ? 0 : Policy.DesiredBots(humans, network.Rules.MaxPlayers);
        for (int slot = _slots.Length - 1; slot >= 0 && count > desired; slot--)
        {
            if (_slots[slot] is not { } bot) continue;
            _slots[slot] = null;
            count--;
            if (bot.TeamIndex < 2) teams[bot.TeamIndex]--;
            network.InvalidateRoster();
        }
        for (int slot = 0; slot < network.Rules.MaxPlayers && count < desired; slot++)
        {
            if (_slots[slot] != null || network.Peers[slot] != null
                || network.HasReconnectReservation(slot) || network.HasPendingBotAdmission(slot)) continue;
            byte team = (byte)(network.Rules.Teams ? teams[0] <= teams[1] ? 0 : 1 : slot);
            var bot = new BotParticipant((byte)slot, NetConnection.NewIdentity(),
                (Hunter)(slot % 7), team, $"BOT {slot + 1}");
            _slots[slot] = bot;
            count++;
            if (team < 2) teams[team]++;
            network.InvalidateRoster();
        }
    }
    public void Activate(BotParticipant bot)
    {
        GameState.Nicknames[bot.Slot] = bot.Name;
        PlayerEntity.Players[bot.Slot].ServerActivate(bot.Identity, bot.Hunter, bot.TeamIndex, Policy.Skill);
    }
    private void Retire(int slot, ServerNetwork network, uint tick)
    {
        _simulation.Reports?.LeaveSlot(_simulation.Scene, slot, tick, ParticipantExitReason.Replaced);
        WorldStateCapture.ReleasePlayer(_simulation.Scene, PlayerEntity.Players[slot]);
        PlayerEntity.Players[slot].ServerDeactivate(); NetScoreboard.ForgetSlot(_simulation.Scene, slot);
        _slots[slot] = null; network.InvalidateRoster();
    }
}
