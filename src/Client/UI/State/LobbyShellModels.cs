using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.State;

public enum UiLobbyAction
{
    SelectHunter, RequestTeam, SetMap, SetMode, SetRule, SetReady,
    SetBotFillEnabled, SetBotMinimumParticipants, SetBotSkill,
    SendLobbyChat, SendTeamChat, StartMatch, ReturnToLobby, Rematch, LeaveServer
}

public readonly record struct UiLobbyCommand(UiLobbyAction Action, int Value = 0,
    string Text = "", string Rule = "");

public sealed record UiLobbyMember(string Name, string Hunter, int Team, bool Ready,
    bool Loading, bool Observer, bool DisconnectedGrace, bool Bot, bool Host, bool Admin,
    bool RatingEligible, int PingMs, bool Local = false)
{
    public string StatusLabel => string.Join(" · ", Statuses());

    private IEnumerable<string> Statuses()
    {
        if (Observer) yield return "Spectator";
        else if (Loading) yield return "Loading";
        else yield return Ready ? "Ready" : "Not Ready";
        if (DisconnectedGrace) yield return "Disconnected grace";
        if (Bot) yield return "Bot";
        if (Host) yield return "Host";
        if (Admin) yield return "Admin";
        yield return RatingEligible ? "Rating eligible" : "Not rating eligible";
    }
}

public sealed record UiLobbyChatLine(string Name, string Text, bool Team, bool Host, bool Admin,
    ulong SenderIdentity = 0);

public sealed record UiMutedLobbySender(ulong SenderIdentity, string Name);

public sealed record UiLobbySnapshot(uint SessionId, uint Revision, string Phase, string Policy,
    string Map, string Mode, string RulesSummary, ImmutableArray<UiLobbyMember> Members,
    ImmutableArray<UiLobbyChatLine> Chat, IReadOnlyDictionary<UiLobbyAction, string> DisabledReasons,
    bool BotFillEnabled = false, int BotMinimumParticipants = 0, int BotSkill = 0,
    ImmutableArray<UiMutedLobbySender> MutedSenders = default)
{
    public bool IsEnabled(UiLobbyAction action) => !DisabledReasons.ContainsKey(action);
    public string? DisabledReason(UiLobbyAction action)
        => DisabledReasons.TryGetValue(action, out string? reason) ? reason : null;

    public bool UsesTeams => Enum.TryParse(Mode, true, out MatchMode mode) && mode.IsTeamMode();
    public ImmutableArray<UiMutedLobbySender> EffectiveMutedSenders
        => MutedSenders.IsDefault ? ImmutableArray<UiMutedLobbySender>.Empty : MutedSenders;

    public string TeamLabel(UiLobbyMember member)
    {
        if (member.Observer) return "Spectator";
        if (!UsesTeams) return "Solo";
        return member.Team switch { 0 => "Orange", 1 => "Green", _ => $"Team {member.Team + 1}" };
    }
}

public interface ILobbyScreenController
{
    UiLobbySnapshot? Snapshot { get; }
    event Action? Changed;
    void Refresh() { }
    Task<UiActionResult> RequestAsync(UiLobbyCommand command, uint expectedRevision,
        CancellationToken cancellationToken);
    void SetSenderMuted(ulong senderIdentity, bool muted) { }
}

public sealed class LobbyScreenModel
{
    private readonly ILobbyScreenController _controller;
    public LobbyScreenModel(ILobbyScreenController controller)
    {
        _controller = controller;
        Snapshot = controller.Snapshot;
        controller.Changed += Refresh;
        if (Snapshot is null) Status.Loading("Waiting for authoritative lobby state…");
        else Status.Ready();
    }

    public AsyncScreenState Status { get; } = new();
    public UiLobbySnapshot? Snapshot { get; private set; }
    public UiLobbyAction? PendingAction { get; private set; }

    public void SetSenderMuted(ulong senderIdentity, bool muted)
    {
        if (senderIdentity == 0) return;
        _controller.SetSenderMuted(senderIdentity, muted);
        Refresh();
    }

    public async Task<UiActionResult> RequestAsync(UiLobbyCommand command,
        CancellationToken cancellationToken = default)
    {
        UiLobbySnapshot? before = Snapshot;
        if (before is null) return UiActionResult.Failure("Lobby state is still loading.");
        string? disabled = before.DisabledReason(command.Action);
        if (disabled is not null) return UiActionResult.Failure(disabled);
        PendingAction = command.Action;
        Status.Loading($"Waiting for server: {Label(command.Action)}…");
        try
        {
            UiActionResult result = await _controller.RequestAsync(command, before.Revision,
                cancellationToken).ConfigureAwait(false);
            Refresh();
            if (!result.Succeeded)
            {
                Status.Failed(result.Message);
                return result;
            }
            if (command.Action is not (UiLobbyAction.SendLobbyChat or UiLobbyAction.SendTeamChat
                or UiLobbyAction.LeaveServer)
                && (Snapshot is null || Snapshot.Revision == before.Revision))
            {
                Status.Loading("The request was sent; waiting for authoritative lobby state…");
                return UiActionResult.Failure("Authoritative lobby update is still pending.");
            }
            Status.Ready();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UiActionResult.Failure("Lobby request canceled.");
        }
        catch (Exception error)
        {
            string message = AsyncScreenState.FriendlyFailure(error, "The lobby request");
            Status.Failed(message);
            return UiActionResult.Failure(message);
        }
        finally { PendingAction = null; }
    }

    public void Refresh()
    {
        _controller.Refresh();
        UiLobbySnapshot? current = _controller.Snapshot;
        if (current is null) return;
        if (Snapshot is null || current.SessionId != Snapshot.SessionId || current.Revision > Snapshot.Revision
            || current.Revision == Snapshot.Revision && !ReferenceEquals(current, Snapshot))
        {
            Snapshot = current;
            Status.Ready();
        }
    }

    public static int SectionColumns(UiLayoutMode mode) => mode switch
    {
        UiLayoutMode.Compact => 1,
        UiLayoutMode.Medium => 2,
        _ => 3
    };
    private static string Label(UiLobbyAction action) => action.ToString().Replace("Set", "");
}

public enum PostMatchAction { Continue, Rematch, NextMap, ReturnToLobby, LeaveServer, HunterLicense }
public enum RatingUpdateState { NotEligible, Pending, Updated, Unavailable }
public sealed record UiPostMatchRow(int Placement, string Name, string Hunter, int Team, bool Bot,
    int Points, int Kills, int Deaths, int Assists, int Damage, int Headshots,
    int ObjectivePrimary, int ObjectiveSecondary, int ObjectiveTertiary);
public sealed record UiPostMatchSummary(uint SessionId, uint LobbyRevision, uint MatchId,
    TimeSpan Duration, string Map, string Mode, string EndReason,
    ImmutableArray<UiPostMatchRow> Rows, RatingUpdateState RatingState,
    int? RatingDelta = null, int? RatingPoints = null);

public interface IPostMatchScreenController
{
    UiPostMatchSummary? Summary { get; }
    event Action? Changed;
    Task<UiPostMatchSummary> AwaitRatingAsync(uint matchId, CancellationToken cancellationToken);
    Task<UiActionResult> InvokeAsync(PostMatchAction action, CancellationToken cancellationToken);
}

public sealed class PostMatchScreenModel
{
    public const int MaximumVisibleRows = 8;
    private readonly IPostMatchScreenController _controller;
    public PostMatchScreenModel(IPostMatchScreenController controller)
    {
        _controller = controller;
        Summary = Bound(controller.Summary);
        controller.Changed += Refresh;
        if (Summary is null) Status.Loading("Waiting for match summary…");
        else Status.Ready();
    }
    public AsyncScreenState Status { get; } = new();
    public UiPostMatchSummary? Summary { get; private set; }

    public async Task RefreshRatingAsync(CancellationToken cancellationToken = default)
    {
        UiPostMatchSummary? current = Summary;
        if (current is null || current.RatingState != RatingUpdateState.Pending) return;
        try
        {
            UiPostMatchSummary updated = await _controller.AwaitRatingAsync(current.MatchId,
                cancellationToken).ConfigureAwait(false);
            if (updated.MatchId == current.MatchId) Summary = Bound(updated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Summary = current with { RatingState = RatingUpdateState.Unavailable }; }
    }

    public Task<UiActionResult> InvokeAsync(PostMatchAction action,
        CancellationToken cancellationToken = default)
        => _controller.InvokeAsync(action, cancellationToken);

    public void Refresh()
    {
        UiPostMatchSummary? current = Bound(_controller.Summary);
        if (current is null) return;
        if (Summary is null || current.MatchId != Summary.MatchId
            || current.LobbyRevision >= Summary.LobbyRevision)
        {
            Summary = current;
            Status.Ready();
        }
    }

    public static int ResultColumns(UiLayoutMode mode) => mode == UiLayoutMode.Compact ? 1 : 2;

    private static UiPostMatchSummary? Bound(UiPostMatchSummary? summary)
        => summary is null || summary.Rows.Length <= MaximumVisibleRows
            ? summary
            : summary with { Rows = summary.Rows.Take(MaximumVisibleRows).ToImmutableArray() };
}

public enum UiAccountPhase { Guest, Restoring, SigningIn, SignedIn, Offline, Failed }
public sealed record UiAccountSnapshot(UiAccountPhase Phase, string DisplayName,
    string PlayerId, bool EmailConfirmed, bool OfficialPlayEligible, string Message = "");

public interface IAccountScreenController
{
    UiAccountSnapshot Snapshot { get; }
    Task<UiActionResult> RestoreAsync(CancellationToken cancellationToken);
    Task<UiActionResult> SignInAsync(string email, string password, CancellationToken cancellationToken);
    Task<UiActionResult> SignOutAsync(CancellationToken cancellationToken);
}
