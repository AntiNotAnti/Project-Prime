#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.InGame;

public sealed class InGamePagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly InGameController _controller;
    private RmlUiDocumentToken _page, _confirm;
    private InGameEffect _effect;
    private long _revision;
    private long _stateRevision;
    private bool _disposed;
    public RmlUiDocumentToken Document => _page;
    public bool Active => !_disposed && _page != default && _host.IsAlive(_page) && _pages.Page == _page;
    public InGamePagePresenter(RmlUiHost host, RmlUiPageManager pages, InGameController controller)
        => (_host, _pages, _controller) = (host, pages, controller);
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("pause", "pages/ingame/pause.rml", "ingame_resume"));
        _revision = 0;
        _stateRevision = 0;
        Refresh();
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!Active || intent.Kind != RmlUiIntentKind.InGameAction || !_pages.Accept(intent)) return false;
        InGameAction action = (InGameAction)intent.Argument;
        bool confirmation = (action is InGameAction.Confirm or InGameAction.Cancel) && _confirm != default;
        if (intent.Document != (confirmation ? _confirm : _page)) return false;
        InGameResult result = _controller.Dispatch(_controller.Intent(action));
        if (result.Accepted) _effect = result.Effect;
        Refresh();
        return true;
    }
    public bool Back()
    {
        if (!Active) return false;
        InGameSnapshot state = _controller.Snapshot();
        InGameResult result = _controller.Dispatch(_controller.Intent(state.Page == InGamePage.Pause
            ? InGameAction.Resume : InGameAction.Cancel));
        if (result.Accepted) _effect = result.Effect;
        Refresh();
        return true;
    }
    public bool TryTakeEffect(out InGameEffect effect)
    {
        effect = _effect; _effect = InGameEffect.None;
        return effect != InGameEffect.None;
    }
    public void Refresh()
    {
        if (!Active) return;
        if (_confirm != default && !_host.IsAlive(_confirm))
        {
            _confirm = default;
            _controller.Dispatch(_controller.Intent(InGameAction.Cancel));
        }
        _controller.Refresh();
        InGameSnapshot state = _controller.Snapshot();
        if (state.Closed) { Dispose(); return; }
        if (_stateRevision == state.Revision) return;
        _stateRevision = state.Revision;
        var fields = new Dictionary<string, RmlUiBindingValue>();
        void Text(string name, string value) => fields[name] = RmlUiBindingValue.FromText(value);
        void Bool(string name, bool value) => fields[name] = RmlUiBindingValue.FromBoolean(value);
        void Enabled(string name, bool value) => Bool("disabled:" + name, !value);
        Text("ingame_title", state.Facts.Replay ? "REPLAY PAUSED" : state.Facts.Live ? "LIVE MATCH" : "MATCH PAUSED");
        Text("ingame_session", state.Facts.Live ? "NETWORK SESSION // GAMEPLAY CONTINUES" : "LOCAL SESSION // PAUSED");
        Text("ingame_error", state.Error);
        Text("ingame_record", state.Facts.Recording ? "STOP REPLAY RECORDING" : "RECORD REPLAY");
        Text("ingame_vote_prompt", state.Facts.VotePrompt + "\n" + state.Facts.VoteTally);
        Text("ingame_vote_reason", state.Facts.VoteUnavailable);
        Text("ingame_map", state.SelectedMap);
        Bool("visible:ingame_error", state.Error.Length > 0);
        Bool("visible:ingame_vote", state.Page == InGamePage.MapVote);
        InGamePage shown = state.Page == InGamePage.Confirmation ? state.ReturnPage : state.Page;
        Bool("visible:ingame_pause", shown == InGamePage.Pause);
        Bool("visible:ingame_bots_panel", shown == InGamePage.Bots);
        Enabled("ingame_replay", state.Facts.Replay);
        Enabled("ingame_spectate", !state.Facts.Replay && state.Facts.CanSpectate && !state.Facts.Spectating);
        Enabled("ingame_rejoin", !state.Facts.Replay && state.Facts.Spectating);
        Enabled("ingame_record", state.Facts.Live);
        Enabled("ingame_propose", state.Facts.Live && state.Facts.VoteUnavailable.Length == 0);
        Enabled("ingame_vote_yes", state.Facts.VoteActive && !state.Facts.VoteAnswered);
        Enabled("ingame_vote_no", state.Facts.VoteActive && !state.Facts.VoteAnswered);
        Enabled("ingame_return", state.Facts.Live && state.Facts.Persistent && state.Facts.Owner && !state.Facts.InLobby);
        Enabled("ingame_bots", state.Facts.CanManageBots);
        InGameBot bot = state.BotDraft;
        bool selected = state.Facts.Bots.Any(row => row.Slot == bot.Slot && row.Generation == bot.Generation);
        bool edit = state.Facts.CanManageBots;
        Text("ingame_bot_summary", state.Facts.Bots.Length + " CONNECTED BOTS // OWNER ADMINISTRATION");
        Text("ingame_bot_selected", selected ? "BOT " + (bot.Slot + 1) + " // " + bot.Hunter : "NEW BOT DRAFT");
        Text("ingame_bot_hunter", "HUNTER // " + bot.Hunter);
        Text("ingame_bot_suit", "SUIT // " + (bot.Suit + 1));
        Text("ingame_bot_skill", "SKILL // " + new[] { "EASY", "NORMAL", "HARD", "EXPERT" }[Math.Clamp(bot.Skill, (byte)0, (byte)3)]);
        Text("ingame_bot_team", bot.Team < 0 ? "TEAM // AUTO" : "TEAM // " + (char)('A' + bot.Team));
        Text("ingame_bot_handicap", "DAMAGE REDUCTION // " + bot.Handicap + "% (APPLY NEXT STEP)");
        Text("ingame_bot_teams", state.Facts.Teams.Length == 0 ? "FREE FOR ALL // NO TEAMS"
            : String.Join("   ", state.Facts.Teams.Select(team => $"TEAM {(char)('A' + team.Index)} {team.Occupants}/{team.Capacity}")));
        Text("ingame_bot_status", state.Facts.CommandStatus);
        Enabled("ingame_bot_select", edit && state.Facts.Bots.Length > 0);
        Enabled("ingame_bot_hunter", edit && state.Facts.BotHunters.Length > 0);
        Enabled("ingame_bot_suit", edit); Enabled("ingame_bot_skill", edit);
        Enabled("ingame_bot_team", edit && state.Facts.Teams.Length > 0);
        Enabled("ingame_bot_handicap", edit && selected);
        Enabled("ingame_bot_apply", edit && selected);
        Enabled("ingame_bot_add", edit && state.Facts.CanAddBot);
        Enabled("ingame_bot_remove", edit && selected);
        _pages.Present(_page, ++_revision, fields);
        if (state.Page == InGamePage.Confirmation)
        {
            if (_confirm == default) _confirm = _pages.OpenModal(new("match-confirm", "pages/ingame/confirm.rml", "ingame_cancel"));
            string question = state.Confirmation switch
            {
                InGameAction.ReturnLobby => "RETURN EVERY CONNECTED PLAYER TO THE LOBBY?",
                InGameAction.Quit => "QUIT PROJECT PRIME?",
                InGameAction.BotRemove => "REMOVE BOT " + (state.BotDraft.Slot + 1) + " FROM THIS SESSION?",
                _ => "LEAVE THIS MATCH?"
            };
            _pages.Present(_confirm, _revision, new Dictionary<string, RmlUiBindingValue>
            {
                ["ingame_question"] = RmlUiBindingValue.FromText(question),
                ["ingame_confirmation_error"] = RmlUiBindingValue.FromText(state.Error)
            });
        }
        else if (_confirm != default) { _pages.CloseModal(); _confirm = default; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_pages.Page == _page && !_pages.ClosePage()) throw new InvalidOperationException("The match menu could not be closed.");
        _page = _confirm = default; _disposed = true;
    }
}
#endif
