#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core;

public enum InGameAction
{
    Resume, Fullscreen, Settings, Replay, Vote, Spectate, Rejoin, Recorder,
    ReturnLobby, Leave, Quit, Confirm, Cancel, MapNext, VoteSubmit, VoteYes, VoteNo,
    Bots, BotSelect, BotHunter, BotSuit, BotSkill, BotTeam, BotHandicap, BotRemove, BotAdd, BotApply
}

public enum InGamePage { Pause, MapVote, Confirmation, Bots }
public enum InGameEffect { None, Resume, Settings, Replay }
public readonly record struct InGameBot(byte Slot, ushort Generation, Hunter Hunter, byte Suit,
    byte Skill, sbyte Team, byte Handicap);
public readonly record struct InGameTeam(sbyte Index, int Occupants, int Capacity);
public sealed record InGameFacts(bool Live, bool Replay, bool Spectating, bool CanSpectate,
    bool Recording, bool Owner, bool Persistent, bool InLobby, bool VoteActive,
    bool VoteAnswered, string VotePrompt, string VoteTally, string VoteUnavailable)
{
    public ImmutableArray<InGameBot> Bots { get; init; } = ImmutableArray<InGameBot>.Empty;
    public ImmutableArray<Hunter> BotHunters { get; init; } = ImmutableArray<Hunter>.Empty;
    public ImmutableArray<InGameTeam> Teams { get; init; } = ImmutableArray<InGameTeam>.Empty;
    public bool CanManageBots { get; init; }
    public bool CanAddBot { get; init; }
    public string CommandStatus { get; init; } = "";
}
public sealed record InGameSnapshot(Guid Lifetime, long Revision, InGamePage Page,
    InGameFacts Facts, ImmutableArray<string> Maps, int MapIndex, InGameAction? Confirmation,
    string Error, bool Closed)
{
    public string SelectedMap => Maps.Length == 0 ? "" : Maps[Math.Clamp(MapIndex, 0, Maps.Length - 1)];
    public InGameBot BotDraft { get; init; } = new(byte.MaxValue, 0, Hunter.Random, 0, 1, -1, 0);
    public InGamePage ReturnPage { get; init; } = InGamePage.Pause;
}
public readonly record struct InGameIntent(Guid Lifetime, long Revision, InGameAction Action);
public readonly record struct InGameResult(bool Accepted, InGameEffect Effect = InGameEffect.None, string Error = "");

public interface IInGameBackend
{
    InGameFacts Capture();
    ImmutableArray<string> Maps();
    string Execute(InGameAction action, string map);
    string EditBot(InGameAction action, InGameBot bot) => "Bot administration is unavailable in this match.";
}

/// <summary>Menu decisions only. Existing match, vote, recorder and session services own gameplay.</summary>
public sealed class InGameController : IDisposable
{
    private sealed class Backend : IInGameBackend
    {
        public InGameFacts Capture()
        {
            var roster = NetSession.LobbyRoster();
            bool controls = NetSession.Active && !DemoPlayback.IsActive && NetSession.LocalIsLobbyOwner
                && !NetSession.LobbyCommandPending && (NetSession.IsInLobby || NetSession.IsPlaying);
            TeamLayout layout = NetSession.ActiveMatchDefinition is { } match ? LobbyRules.ResolveTeamLayout(match) : default;
            return new(NetSession.Active && !DemoPlayback.IsActive,
                DemoPlayback.IsActive, SpectatorMode.IsSpectating, SpectatorMode.CanSpectate,
                DemoRecorder.IsRecording, NetSession.LocalIsLobbyOwner, NetSession.PersistentLobby,
                NetSession.IsInLobby, MapVote.Active, MapVote.Answered, MapVote.PromptLine(),
                MapVote.TallyLine(), MapVote.WhyNotProposing())
            {
                Bots = Enumerable.Range(0, roster.Count).Where(roster.IsBot).Select(i => new InGameBot(
                    roster.Slots[i], roster.Generations[i], (Hunter)roster.Hunters[i], roster.Colors[i],
                    roster.BotLevels[i], roster.Teams[i], roster.DamageReductions[i])).ToImmutableArray(),
                BotHunters = HunterRules.Pool(NetSession.ActiveMatchDefinition?.LowTier == true).Append(Hunter.Random).ToImmutableArray(),
                CanManageBots = controls,
                CanAddBot = controls && roster.Count < (NetSession.ServerSession?.MaxPlayers ?? 8),
                Teams = Enumerable.Range(0, layout.TeamCount).Select(team => new InGameTeam((sbyte)team,
                    Enumerable.Range(0, roster.Count).Count(i => !roster.IsSpectator(i) && roster.Teams[i] == team), layout.Capacity(team))).ToImmutableArray(),
                CommandStatus = NetSession.LobbyMessage
            };
        }
        public ImmutableArray<string> Maps() => ThumbnailGenerator.MultiplayerRooms().ToImmutableArray();
        public string Execute(InGameAction action, string map)
        {
            switch (action)
            {
                case InGameAction.Fullscreen: PauseMenu.RequestFullscreenToggle(); break;
                case InGameAction.Spectate: SpectatorMode.Start(); break;
                case InGameAction.Rejoin: SpectatorMode.Rejoin(); break;
                case InGameAction.Recorder:
                    DemoRecorder.ToggleWithFeedback();
                    return DemoRecorder.LastError ?? "";
                case InGameAction.ReturnLobby:
                    return NetSession.SendLobbyCommand(LobbyCommandType.ReturnToLobby)
                        ? "" : "The server could not accept a return to the lobby.";
                case InGameAction.Leave: PauseMenu.RequestLeave(); break;
                case InGameAction.Quit: PauseMenu.RequestQuit(); break;
                case InGameAction.VoteSubmit: MapVote.Propose(map); break;
                case InGameAction.VoteYes: MapVote.Cast(true); break;
                case InGameAction.VoteNo: MapVote.Cast(false); break;
                default: return "This action is unavailable in the current match.";
            }
            return "";
        }
        public string EditBot(InGameAction action, InGameBot bot)
        {
            InGameFacts facts = Capture();
            if (!facts.CanManageBots) return "Only the current owner may manage bots while no command is pending.";
            bool add = action == InGameAction.BotAdd;
            if (add && !facts.CanAddBot) return "The server has no open bot slot.";
            if (!add && !facts.Bots.Any(row => row.Slot == bot.Slot && row.Generation == bot.Generation))
                return "The selected bot left or its slot was reused. Select it again.";
            if (action is InGameAction.BotAdd or InGameAction.BotApply && !facts.BotHunters.Contains(bot.Hunter))
                return "The selected Hunter is unavailable under the current rules.";
            LobbyCommandType command = add ? LobbyCommandType.AddBot
                : action == InGameAction.BotRemove ? LobbyCommandType.RemoveBot
                : action == InGameAction.BotHandicap ? LobbyCommandType.SetHandicap : LobbyCommandType.UpdateBot;
            return NetSession.SendLobbyCommand(command, add ? byte.MaxValue : bot.Slot, bot.Team,
                hunter: (byte)bot.Hunter, color: bot.Suit, botLevel: bot.Skill, damageReduction: bot.Handicap)
                ? "" : "The server could not accept the bot change.";
        }
    }

    private readonly IInGameBackend _backend;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private InGameSnapshot _state;
    private bool _dispatching;
    public InGameController() : this(new Backend()) { }
    public static IInGameBackend CreateEngineBackend() => new Backend();
    public InGameController(IInGameBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _state = new(Guid.NewGuid(), 1, InGamePage.Pause, backend.Capture(),
            ImmutableArray<string>.Empty, 0, null, "", false);
    }
    public InGameSnapshot Snapshot() { CheckOwner(); return _state; }
    public InGameIntent Intent(InGameAction action) { CheckOwner(); return new(_state.Lifetime, _state.Revision, action); }
    public void Refresh()
    {
        CheckOwner();
        if (_state.Closed) return;
        InGameFacts facts = _backend.Capture();
        if (!SameFacts(facts, _state.Facts)) Set(_state with { Facts = facts });
    }
    public InGameResult Dispatch(InGameIntent intent)
    {
        CheckOwner();
        if (_state.Closed || _dispatching || intent.Lifetime != _state.Lifetime
            || intent.Revision != _state.Revision || !Enum.IsDefined(intent.Action))
            return new(false, Error: "The menu changed. Try the action again.");
        _dispatching = true;
        try
        {
            InGameFacts facts = _backend.Capture();
            InGameAction action = intent.Action;
            if (_state.Page == InGamePage.Confirmation)
            {
                if (action == InGameAction.Cancel) { Set(_state with { Page = _state.ReturnPage, Confirmation = null, Error = "" }); return new(true); }
                if (action != InGameAction.Confirm || _state.Confirmation == null) return Reject("Answer the confirmation first.");
                action = _state.Confirmation.Value;
            }
            else if (action is InGameAction.Confirm) return Reject("There is no action awaiting confirmation.");
            if (action is InGameAction.ReturnLobby && (!facts.Live || !facts.Owner || !facts.Persistent || facts.InLobby))
                return Reject("Only the lobby owner can return a live persistent match to the lobby.");
            if (action is InGameAction.Spectate && (facts.Replay || facts.Spectating || !facts.CanSpectate))
                return Reject("Spectating is unavailable in this match.");
            if (action is InGameAction.Rejoin && (facts.Replay || !facts.Spectating)) return Reject("You are not a match spectator.");
            if (action is InGameAction.Recorder && !facts.Live) return Reject("Recording requires a live network match.");
            if (action is InGameAction.Replay && !facts.Replay) return Reject("No replay is playing.");
            if (action is InGameAction.Vote or InGameAction.VoteSubmit && (!facts.Live || facts.VoteUnavailable.Length > 0))
                return Reject(facts.VoteUnavailable.Length > 0 ? facts.VoteUnavailable : "Map voting requires a live network match.");
            if (action is InGameAction.VoteYes or InGameAction.VoteNo && (!facts.VoteActive || facts.VoteAnswered))
                return Reject("There is no unanswered map vote.");
            if (action == InGameAction.BotRemove && (!facts.CanManageBots || _state.Page is not (InGamePage.Bots or InGamePage.Confirmation)
                || !facts.Bots.Any(row => row.Slot == _state.BotDraft.Slot && row.Generation == _state.BotDraft.Generation)))
                return Reject("The selected bot is unavailable or you no longer have permission to remove it.");
            if (action is InGameAction.ReturnLobby or InGameAction.Leave or InGameAction.Quit or InGameAction.BotRemove && _state.Page != InGamePage.Confirmation)
            {
                Set(_state with { Facts = facts, ReturnPage = _state.Page, Page = InGamePage.Confirmation, Confirmation = action, Error = "" });
                return new(true);
            }
            if (action == InGameAction.Vote)
            {
                ImmutableArray<string> maps = _backend.Maps();
                if (maps.IsDefaultOrEmpty) return Reject("No locally available maps can be proposed.");
                Set(_state with { Facts = facts, Maps = maps, MapIndex = 0, Page = InGamePage.MapVote, Error = "" }); return new(true);
            }
            if (action == InGameAction.Cancel) { Set(_state with { Page = InGamePage.Pause, Confirmation = null, Error = "" }); return new(true); }
            if (action == InGameAction.MapNext)
            {
                if (_state.Page != InGamePage.MapVote || _state.Maps.IsDefaultOrEmpty) return Reject("Open the map ballot first.");
                Set(_state with { MapIndex = (_state.MapIndex + 1) % _state.Maps.Length }); return new(true);
            }
            if (action == InGameAction.VoteSubmit && (_state.Page != InGamePage.MapVote || _state.SelectedMap.Length == 0))
                return Reject("Choose a locally available map first.");
            if (action == InGameAction.Bots || (int)action >= (int)InGameAction.BotSelect)
            {
                if (!facts.CanManageBots) return Reject("Only the current owner can manage bots while no command is pending.");
                if (action == InGameAction.Bots)
                {
                    InGameBot draft = facts.Bots.Length > 0 ? facts.Bots[0] : _state.BotDraft;
                    Set(_state with { Facts = facts, Page = InGamePage.Bots, BotDraft = draft, Error = "" }); return new(true);
                }
                if (_state.Page is not (InGamePage.Bots or InGamePage.Confirmation)) return Reject("Open bot administration first.");
                InGameBot bot = _state.BotDraft;
                switch (action)
                {
                    case InGameAction.BotSelect:
                        if (facts.Bots.IsDefaultOrEmpty) return Reject("There are no connected bots.");
                        int index = -1;
                        for (int i = 0; i < facts.Bots.Length; i++) if (facts.Bots[i].Slot == bot.Slot && facts.Bots[i].Generation == bot.Generation) index = i;
                        bot = facts.Bots[(index + 1) % facts.Bots.Length]; break;
                    case InGameAction.BotHunter:
                        if (facts.BotHunters.IsDefaultOrEmpty) return Reject("No bot Hunters are available.");
                        bot = bot with { Hunter = facts.BotHunters[(facts.BotHunters.IndexOf(bot.Hunter) + 1) % facts.BotHunters.Length] }; break;
                    case InGameAction.BotSuit: bot = bot with { Suit = (byte)((bot.Suit + 1) % 4) }; break;
                    case InGameAction.BotSkill: bot = bot with { Skill = (byte)((bot.Skill + 1) % 4) }; break;
                    case InGameAction.BotTeam:
                        if (facts.Teams.IsDefaultOrEmpty) return Reject("This match does not use teams.");
                        sbyte originalTeam = facts.Bots.FirstOrDefault(row => row.Slot == bot.Slot && row.Generation == bot.Generation).Team;
                        var availableTeams = facts.Teams.Where(team => team.Occupants < team.Capacity || team.Index == originalTeam)
                            .Select(team => team.Index).Prepend((sbyte)-1).ToImmutableArray();
                        bot = bot with { Team = availableTeams[(availableTeams.IndexOf(bot.Team) + 1) % availableTeams.Length] }; break;
                    case InGameAction.BotHandicap:
                        bot = bot with { Handicap = (byte)(bot.Handicap >= PlayerHandicap.MaxDamageReduction ? 0 : bot.Handicap + PlayerHandicap.Step) };
                        goto case InGameAction.BotApply;
                    case InGameAction.BotApply:
                    case InGameAction.BotAdd:
                    case InGameAction.BotRemove:
                        if (action == InGameAction.BotAdd && !facts.CanAddBot) return Reject("The server has no open bot slot.");
                        if (action != InGameAction.BotAdd && !facts.Bots.Any(row => row.Slot == bot.Slot && row.Generation == bot.Generation))
                            return Reject("The selected bot left or its slot was reused. Select it again.");
                        string rejected = _backend.EditBot(action, bot);
                        if (rejected.Length > 0) return Reject(rejected);
                        break;
                }
                Set(_state with { Facts = _backend.Capture(), BotDraft = bot, Page = InGamePage.Bots, Confirmation = null, Error = "" });
                return new(true);
            }
            if (action is InGameAction.Resume or InGameAction.Settings or InGameAction.Replay)
            {
                Set(_state with { Facts = facts, Error = "" });
                return new(true, action == InGameAction.Resume ? InGameEffect.Resume
                    : action == InGameAction.Settings ? InGameEffect.Settings : InGameEffect.Replay);
            }
            string error = _backend.Execute(action, _state.SelectedMap);
            if (error.Length > 0) return Reject(error);
            Set(_state with { Facts = _backend.Capture(), Page = InGamePage.Pause, Confirmation = null, Error = "" });
            return new(true, InGameEffect.Resume);
        }
        catch (Exception error) { return Reject(error.Message); }
        finally { _dispatching = false; }
    }
    public void ReportError(string error) { CheckOwner(); if (!_state.Closed) Set(_state with { Error = error ?? "" }); }
    public void Dispose() { CheckOwner(); if (!_state.Closed) Set(_state with { Closed = true, Confirmation = null }); }
    private InGameResult Reject(string error) { Set(_state with { Error = error }); return new(false, Error: error); }
    private void Set(InGameSnapshot state) => _state = state with { Revision = _state.Revision + 1 };
    private static bool SameFacts(InGameFacts left, InGameFacts right) =>
        left.Bots.SequenceEqual(right.Bots) && left.BotHunters.SequenceEqual(right.BotHunters) && left.Teams.SequenceEqual(right.Teams)
        && (left with { Bots = ImmutableArray<InGameBot>.Empty, BotHunters = ImmutableArray<Hunter>.Empty, Teams = ImmutableArray<InGameTeam>.Empty })
            == (right with { Bots = ImmutableArray<InGameBot>.Empty, BotHunters = ImmutableArray<Hunter>.Empty, Teams = ImmutableArray<InGameTeam>.Empty });
    private void CheckOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Match menu decisions belong to the engine thread.");
    }
}
#endif
