using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// The small amount of state that belongs to the Play surface rather than to
/// the Node.  Authoritative lobby revisions, member seats, and offer data are
/// never copied here; this type only keeps bounded user input and presentation
/// choices alive while the view is rebuilt.
/// </summary>
internal sealed class PlayPresentationState
{
    internal const int ChatHistoryLimit = 32;

    public PlaySubsection Subsection { get; set; } = PlaySubsection.Home;
    public int HostStep { get; set; } = 1;
    public MatchBrowserFilters Filters { get; private set; } = new();
    public MatchBrowserSort Sort { get; private set; } = MatchBrowserSort.Recommended;
    public string ChatDraft { get; private set; } = "";
    public int ChatUnreadCount { get; private set; }
    public double ChatScrollOffset { get; private set; }
    public bool ChatScrollPositionKnown { get; private set; }
    public bool ChatEditing { get; private set; }
    public HostMatchDraft HostDraft { get; } = new();
    public HostMatchDraft? EditDraft { get; private set; }
    public bool EditMatchOpen { get; set; }
    public Guid? LeaveConfirmationLobbyId { get; private set; }
    public bool PresenceExpanded { get; private set; }
    public int PresencePage { get; private set; }

    // This is a view reuse seam, not a second lobby snapshot. The cached view
    // is replaced whenever the authoritative lobby identity changes or the
    // route leaves the lobby, while its Update method consumes the latest
    // snapshot supplied by the shell.
    internal ILobbyPresentationView? LobbyView { get; set; }

    public void ShowAllPresence()
    {
        PresenceExpanded = true;
        PresencePage = 0;
    }

    public void ShowPresencePreview()
    {
        PresenceExpanded = false;
        PresencePage = 0;
    }

    public void SetPresencePage(int page, int pageCount)
        => PresencePage = Math.Clamp(page, 0, Math.Max(0, pageCount - 1));

    // Only the cursor needed to count new entries is retained. Chat messages
    // themselves always come from the current authoritative snapshot.
    private Guid? _chatLobbyId;
    private long _chatLastSequence;
    private long _chatDraftRevision;

    // These are operation guards, not an offer cache.  They prevent a rebuilt
    // card or a double click from sending the same non-idempotent command
    // twice. A failed command releases only the in-flight marker so a fresh
    // authoritative rebuild can retry that same offer identity.
    private Guid? _offerActionId;
    private bool _offerActionInFlight;
    private bool _offerActionCompleted;

    public void SetFilters(MatchBrowserFilters filters)
        => Filters = filters with { MapKey = string.IsNullOrWhiteSpace(filters.MapKey) ? null : filters.MapKey };

    public void SetSort(MatchBrowserSort sort) => Sort = sort;

    public void ClearFilters() => Filters = new MatchBrowserFilters();

    public void SetChatDraft(string? value)
    {
        string bounded = Utf8TextLimit.Truncate(value, 256);
        if (StringComparer.Ordinal.Equals(ChatDraft, bounded)) return;
        ChatDraft = bounded;
        AdvanceChatDraftRevision();
    }

    public void ClearChatDraft()
    {
        if (ChatDraft.Length == 0) return;
        ChatDraft = "";
        AdvanceChatDraftRevision();
    }

    /// <summary>
    /// Capture the submitted text and its generation before an asynchronous
    /// send. A later acknowledgement must not clear newer typing.
    /// </summary>
    public ChatDraftSubmission CaptureChatDraftSubmission(string? submittedText)
        => new(_chatDraftRevision, Utf8TextLimit.Truncate(submittedText, 256));

    public bool TryAcknowledgeChatSubmission(ChatDraftSubmission submission)
    {
        if (String.IsNullOrEmpty(submission.Text)
            || submission.Revision != _chatDraftRevision
            || !StringComparer.Ordinal.Equals(submission.Text, ChatDraft))
            return false;
        ClearChatDraft();
        return true;
    }

    private void AdvanceChatDraftRevision()
        => _chatDraftRevision = _chatDraftRevision == long.MaxValue
            ? 1 : _chatDraftRevision + 1;

    public void SetChatEditing(bool editing)
    {
        ChatEditing = editing;
        if (editing) ChatUnreadCount = 0;
    }

    /// <summary>
    /// Leave the chat editor without changing its draft. The shell can call
    /// this before handling B/Escape as route navigation, giving text editing
    /// the first chance to consume the input.
    /// </summary>
    public bool TryExitChatEditing()
    {
        if (!ChatEditing) return false;
        ChatEditing = false;
        return true;
    }

    /// <summary>
    /// Observe bounded chat history without retaining or replaying messages.
    /// A new lobby establishes a new sequence baseline; only later entries in
    /// the same lobby contribute to the unread indicator.
    /// </summary>
    public void ObserveChat(Guid lobbyId, IReadOnlyList<LobbyChatEntry> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (lobbyId == Guid.Empty)
        {
            ResetChatPresentation();
            return;
        }

        long latest = 0;
        foreach (LobbyChatEntry entry in history)
            latest = Math.Max(latest, entry.Sequence);

        if (_chatLobbyId != lobbyId)
        {
            _chatLobbyId = lobbyId;
            _chatLastSequence = latest;
            ChatUnreadCount = 0;
            return;
        }

        int newEntries = 0;
        foreach (LobbyChatEntry entry in history)
        {
            if (entry.Sequence > _chatLastSequence) newEntries++;
        }
        if (ChatEditing)
            ChatUnreadCount = 0;
        else
            ChatUnreadCount = Math.Min(ChatHistoryLimit,
                ChatUnreadCount + Math.Min(ChatHistoryLimit, newEntries));
        _chatLastSequence = Math.Max(_chatLastSequence, latest);
    }

    public void MarkChatRead() => ChatUnreadCount = 0;

    public void SetChatScrollOffset(double offset)
    {
        ChatScrollOffset = double.IsFinite(offset) ? Math.Clamp(offset, 0, 100_000) : 0;
        ChatScrollPositionKnown = true;
    }

    /// <summary>Clear transient chat viewport state while preserving the draft.</summary>
    public void ResetChatPresentation()
    {
        _chatLobbyId = null;
        _chatLastSequence = 0;
        ChatUnreadCount = 0;
        ChatScrollOffset = 0;
        ChatScrollPositionKnown = false;
        ChatEditing = false;
    }

    public void BeginEdit(LobbySnapshot lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        if (EditDraft?.SourceLobbyId == lobby.LobbyId) return;
        EditDraft = HostMatchDraft.FromLobby(lobby);
    }

    public void ClearEdit()
    {
        EditDraft = null;
        EditMatchOpen = false;
    }

    /// <summary>
    /// Keep a destructive leave confirmation across the expected authoritative
    /// view rebuild, but scope it to the lobby identity the player saw.
    /// </summary>
    public void ObserveLobby(Guid lobbyId)
    {
        if (lobbyId == Guid.Empty
            || (LeaveConfirmationLobbyId is { } active && active != lobbyId))
            LeaveConfirmationLobbyId = null;
    }

    public bool IsLeaveConfirmationOpen(Guid lobbyId)
        => lobbyId != Guid.Empty && LeaveConfirmationLobbyId == lobbyId;

    public void RequestLeaveConfirmation(Guid lobbyId)
    {
        if (lobbyId != Guid.Empty) LeaveConfirmationLobbyId = lobbyId;
    }

    public void CancelLeaveConfirmation(Guid lobbyId)
    {
        if (LeaveConfirmationLobbyId == lobbyId) LeaveConfirmationLobbyId = null;
    }

    /// <summary>Consume one explicit confirmation; duplicate clicks are ignored.</summary>
    public bool TryConfirmLeave(Guid lobbyId)
    {
        if (!IsLeaveConfirmationOpen(lobbyId)) return false;
        LeaveConfirmationLobbyId = null;
        return true;
    }

    public bool TryBeginOfferAction(Guid offerId)
    {
        if (offerId == Guid.Empty) return false;
        if (_offerActionId != offerId)
        {
            _offerActionId = offerId;
            _offerActionInFlight = false;
            _offerActionCompleted = false;
        }
        if (_offerActionInFlight || _offerActionCompleted) return false;
        _offerActionInFlight = true;
        return true;
    }

    /// <summary>Mark a non-idempotent offer command as successfully sent.</summary>
    public void CompleteOfferAction(Guid offerId)
    {
        if (_offerActionId != offerId || !_offerActionInFlight) return;
        _offerActionInFlight = false;
        _offerActionCompleted = true;
    }

    /// <summary>
    /// Release an in-flight offer command after a failed request. The identity
    /// remains associated with the current authoritative offer, but it may be
    /// attempted again after the next authoritative rebuild.
    /// </summary>
    public void ReleaseOfferAction(Guid offerId)
    {
        if (_offerActionId != offerId || !_offerActionInFlight) return;
        _offerActionInFlight = false;
    }

    /// <summary>Forget only a superseded server offer identity.</summary>
    public void ObserveOffer(Guid? offerId)
    {
        if (_offerActionId.HasValue && _offerActionId != offerId)
        {
            _offerActionId = null;
            _offerActionInFlight = false;
            _offerActionCompleted = false;
        }
    }
}

internal interface ILobbyPresentationView
{
    Guid LobbyId { get; }
    Control View { get; }
    LobbyChatPanel ChatPanel { get; }
    void Update(PlayPresentationContext context, LobbySnapshot lobby);
}

/// <summary>
/// The presentation state of the authoritative public match directory.  This
/// is deliberately separate from <see cref="PlayPhase"/>: a connected Node
/// can have no directory yet, an empty directory, or a directory that failed
/// to load, and those states need different player-facing copy.
/// </summary>
internal enum MatchDirectoryPresentationState
{
    NotLoaded,
    Loading,
    Empty,
    Loaded,
    Failed
}

/// <summary>Projects the current Play state without retaining directory data.</summary>
internal static class MatchDirectoryPresentation
{
    public static MatchDirectoryPresentationState From(PlayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase == PlayPhase.Error) return MatchDirectoryPresentationState.Failed;
        if (state.Loading || state.Phase == PlayPhase.LoadingNodes)
            return MatchDirectoryPresentationState.Loading;

        LobbyListSnapshot? directory = state.Lobbies;
        if (directory is null) return MatchDirectoryPresentationState.NotLoaded;
        return directory.Lobbies.IsDefaultOrEmpty
            ? MatchDirectoryPresentationState.Empty
            : MatchDirectoryPresentationState.Loaded;
    }
}

internal readonly record struct ChatDraftSubmission(long Revision, string Text);

internal enum PlaySubsection
{
    Home,
    Browser,
    HostMatch
}

internal enum MatchBrowserSort
{
    Recommended,
    MostPlayers,
    MostOpenSlots,
    Name
}

internal sealed record MatchBrowserFilters(
    MatchMode? Mode = null,
    string? MapKey = null,
    bool OpenPlayerSlotsOnly = false,
    bool SpectatableOnly = false,
    bool HideFull = false);

/// <summary>Pure, bounded projections used by the match browser.</summary>
internal static class MatchBrowserFiltering
{
    public static int OpenPlayerSlots(LobbyListEntry entry)
        => Math.Max(0, entry.PlayerLimit - entry.Players - entry.BotCount);

    public static int OpenObserverSlots(LobbyListEntry entry)
        => Math.Max(0, entry.ObserverLimit - entry.Observers);

    public static bool IsPlayerJoinable(LobbyListEntry entry)
        => entry.Phase == LobbyPhase.Open && OpenPlayerSlots(entry) > 0;

    public static bool IsSpectatable(LobbyListEntry entry)
        => entry.Phase == LobbyPhase.Open && OpenObserverSlots(entry) > 0;

    public static bool IsWaitlistJoinable(LobbyListEntry entry)
        => entry.Phase == LobbyPhase.Open && OpenPlayerSlots(entry) == 0;

    public static ImmutableArray<LobbyListEntry> Apply(
        System.Collections.Generic.IEnumerable<LobbyListEntry> entries,
        MatchBrowserFilters filters, MatchBrowserSort sort)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filters);

        IEnumerable<LobbyListEntry> query = entries;
        if (filters.Mode is { } mode)
            query = query.Where(entry => entry.Mode == mode);
        if (!string.IsNullOrWhiteSpace(filters.MapKey))
            query = query.Where(entry => StringComparer.Ordinal.Equals(entry.MapKey, filters.MapKey));
        if (filters.OpenPlayerSlotsOnly || filters.HideFull)
            query = query.Where(entry => OpenPlayerSlots(entry) > 0);
        if (filters.SpectatableOnly)
            query = query.Where(IsSpectatable);

        query = sort switch
        {
            MatchBrowserSort.MostPlayers => query
                .OrderByDescending(entry => entry.Players)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.LobbyId),
            MatchBrowserSort.MostOpenSlots => query
                .OrderByDescending(OpenPlayerSlots)
                .ThenByDescending(entry => entry.Players)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.LobbyId),
            MatchBrowserSort.Name => query
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.LobbyId),
            _ => query
                .OrderByDescending(IsPlayerJoinable)
                .ThenByDescending(OpenPlayerSlots)
                .ThenByDescending(entry => entry.Players)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.LobbyId)
        };
        return query.ToImmutableArray();
    }
}

internal static class Utf8TextLimit
{
    public static string Truncate(string? value, int maxBytes)
    {
        if (maxBytes <= 0 || string.IsNullOrEmpty(value)) return "";
        string text = value;
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;

        var builder = new StringBuilder(text.Length);
        int bytes = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int runeBytes = Encoding.UTF8.GetByteCount(rune.ToString());
            if (bytes + runeBytes > maxBytes) break;
            builder.Append(rune.ToString());
            bytes += runeBytes;
        }
        return builder.ToString();
    }
}
