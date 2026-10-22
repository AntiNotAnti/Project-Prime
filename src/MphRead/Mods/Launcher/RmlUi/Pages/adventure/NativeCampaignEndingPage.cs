#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

/// <summary>Read-only ending receipt and credits. The runtime owns clearing the matching receipt.</summary>
public sealed class NativeCampaignEndingPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCampaignEndingController _controller;
    private readonly CampaignAudioSession _audio;
    private RmlUiDocumentToken _document;
    private long _revision, _controllerRevision;
    private CampaignCreditsPresentation? _presentation;
    private CampaignCreditsPresentation.Phase? _phase;
    private bool _canAcknowledge;
    private CampaignAudioPage? _requestedAudio;
    private readonly bool _original;
    private readonly Func<ICampaignCreditsLoadJob?> _load;
    private readonly Func<bool> _receiptCurrent;
    private readonly Func<double> _now;
    private bool _opened, _disposed;

    public CampaignEndingSummary Receipt { get; }
    public RmlUiDocumentToken Document => _document;
    public bool Closed => _controller.Snapshot().Closed;
    internal CampaignCreditsPresentation.Phase? CreditsPhase => _presentation?.State;
    internal int CreditsPage => _presentation?.SourcePage ?? -1;
    internal bool CreditsCanAcknowledge => _presentation?.CanAcknowledge == true;

    public NativeCampaignEndingPage(RmlUiHost host, RmlUiPageManager pages, CampaignEndingSummary summary)
        : this(host, pages, summary, new CampaignAudioSession(), LauncherPrefs.OriginalCampaignCredits,
            CampaignCreditsLoadJob.TryStart, () => MatchStart.RecentCampaignEnding == summary, MonotonicNow) { }

    internal NativeCampaignEndingPage(RmlUiHost host, RmlUiPageManager pages, CampaignEndingSummary summary,
        ICampaignAudioSink audio, bool original = false, Func<ICampaignCreditsLoadJob?>? load = null,
        Func<bool>? receiptCurrent = null, Func<double>? now = null)
        : this(host, pages, summary, new CampaignAudioSession(audio), original,
            load ?? CampaignCreditsLoadJob.TryStart, receiptCurrent ?? (() => true), now ?? MonotonicNow) { }

    private NativeCampaignEndingPage(RmlUiHost host, RmlUiPageManager pages, CampaignEndingSummary summary,
        CampaignAudioSession audio, bool original, Func<ICampaignCreditsLoadJob?> load,
        Func<bool> receiptCurrent, Func<double> now)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        Receipt = summary;
        _audio = audio; _original = original; _load = load; _receiptCurrent = receiptCurrent; _now = now;
        _controller = new(new NativeCampaignEndingReport(summary.Slot, summary.Kind == CampaignEndingKind.Complete,
            summary.SaveCommitted, summary.CompletionPercent, summary.Octoliths, summary.Deaths,
            summary.HunterKills, summary.EnemyKills) { PlayTicks = summary.PlayTicks, GoreaTicks = summary.GoreaTicks });
    }

    public void Open(bool active = true)
    {
        _host.VerifyOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_opened) return;
        _document = _pages.OpenPage(new("campaign-ending", "pages/adventure/ending.rml", "campaign_ending_continue"));
        _opened = true;
        if (_original && _host.SupportsCanvas)
            _presentation = new(_host, _pages, _document, Receipt, _receiptCurrent, ShowSource, _load);
        Refresh(active);
    }

    internal void Suspend() { _host.VerifyOwnerThread(); _presentation?.Suspend(); }

    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!CanReturn() || intent.Document != _document) return false;
        if (intent.Kind == RmlUiIntentKind.ResultsAction && intent.Argument == 0)
        {
            if (_presentation?.CanAcknowledge != true || !_pages.Accept(intent)) return false;
            bool accepted = _presentation.Acknowledge();
            Refresh(); return accepted;
        }
        if (intent.Kind != RmlUiIntentKind.Navigate || intent.Argument != (int)RmlUiRouteArgument.Adventure
            || !_pages.Accept(intent)) return false;
        return Finish(back: _presentation?.State is CampaignCreditsPresentation.Phase.Loading
            or CampaignCreditsPresentation.Phase.Credits or CampaignCreditsPresentation.Phase.Results);
    }

    public bool Back()
    {
        _host.VerifyOwnerThread();
        return CanReturn() && Finish(back: true);
    }

    private bool CanReturn() => !_disposed && _opened && !Closed && _pages.Top == _document
        && _host.IsAlive(_document) && _host.IsVisible(_document) && _host.CurrentInputDocument == _document;

    private bool Finish(bool back)
    {
        if (!(back ? _controller.Back() : _controller.Continue())) return false;
        _presentation?.Dispose();
        _audio.Dispose();
        Refresh();
        return true;
    }

    public bool TryTakeReturn(out CampaignEndingSummary receipt)
    {
        _host.VerifyOwnerThread();
        receipt = default;
        if (!_controller.TryTakeReturn(out _)) return false;
        receipt = Receipt;
        return true;
    }

    /// <summary>Call on the owner thread while this page is active, including while audio is loading.</summary>
    public void Refresh(bool active = true)
    {
        _host.VerifyOwnerThread();
        if (_disposed || !_opened) return;
        if (!_host.IsAlive(_document)) { _presentation?.Dispose(); _audio.Dispose(); return; }
        if (!Closed)
        {
            _presentation?.Pump(_now(), active);
            if (_presentation?.TryTakeReturn() == true) { Finish(back: false); return; }
            var phase = _presentation?.State;
            if (phase is not CampaignCreditsPresentation.Phase.Loading and not CampaignCreditsPresentation.Phase.Retired)
            {
                var requested = phase == CampaignCreditsPresentation.Phase.Credits ? CampaignAudioPage.Ending : CampaignAudioPage.EndingResults;
                // A failed or stolen music epoch must not retry source IO/cues
                // each UI frame or reclaim a newer scene's playback.
                if (_requestedAudio != requested) { _requestedAudio = requested; _audio.Enter(requested); }
            }
            if (phase == CampaignCreditsPresentation.Phase.Retired) _audio.Dispose();
            else _audio.Tick();
        }
        var state = _controller.Snapshot();
        var currentPhase = _presentation?.State;
        bool acknowledge = !state.Closed && _presentation?.CanAcknowledge == true;
        if (state.Revision == _controllerRevision && currentPhase == _phase && acknowledge == _canAcknowledge) return;
        bool focusOk = acknowledge && !_canAcknowledge;
        bool focusReturn = currentPhase == CampaignCreditsPresentation.Phase.Fallback && _phase != currentPhase;
        var fields = new Dictionary<string, RmlUiBindingValue>();
        void Text(string key, string value) => fields[key] = RmlUiBindingValue.FromText(value);
        Text("campaign_ending_heading", Receipt.Heading);
        Text("campaign_ending_result", Receipt.GoreaResult);
        Text("campaign_ending_detail", state.Report.Complete ? "The final encounter is complete."
            : "The mission has ended, but Gorea's final encounter remains unfinished.");
        Text("campaign_ending_slot", $"SAVE SLOT {Receipt.Slot:00}");
        Text("campaign_ending_save", Receipt.SaveCommitted ? "SAVE COMMITTED" : "SAVE NOT REQUESTED");
        Text("campaign_ending_totals", "COMPLETION  " + (Receipt.CompletionPercent is { } percentage
                ? percentage.ToString(CultureInfo.InvariantCulture) + "%" : "NOT AVAILABLE")
            + "\nTOTAL TIME  " + CampaignTimeRecords.FormatTotal(state.Report.PlayTicks)
            + "\nGOREA TIME  " + (state.Report.Complete ? CampaignTimeRecords.FormatGorea(state.Report.GoreaTicks) : "INCOMPLETE")
            + $"\nOCTOLITHS  {Receipt.Octoliths}/8\nDEATHS  {Receipt.Deaths}\nHUNTER KILLS  {Receipt.HunterKills}\nENEMY KILLS  {Receipt.EnemyKills}");
        Text("campaign_ending_credits", CreditsText());
        fields["disabled:campaign_ending_continue"] = RmlUiBindingValue.FromBoolean(state.Closed);
        bool source = currentPhase is CampaignCreditsPresentation.Phase.Credits or CampaignCreditsPresentation.Phase.Results;
        Text("campaign_ending_continue", currentPhase is CampaignCreditsPresentation.Phase.Loading
            or CampaignCreditsPresentation.Phase.Credits or CampaignCreditsPresentation.Phase.Results
            ? "CANCEL CREDITS // RETURN TO FILE SELECT" : "RETURN TO CAMPAIGN FILE SELECT");
        Text("campaign_ending_source_status", currentPhase == CampaignCreditsPresentation.Phase.Loading
            ? "LOADING ORIGINAL CARTRIDGE CREDITS" : source ? "ORIGINAL CREDITS // STACKED SCREENS" : "");
        fields["visible:campaign_ending_source_ok"] = RmlUiBindingValue.FromBoolean(acknowledge);
        fields["disabled:campaign_ending_source_ok"] = RmlUiBindingValue.FromBoolean(!acknowledge);
        fields["visible:campaign_ending_source"] = RmlUiBindingValue.FromBoolean(source);
        fields["visible:campaign_ending_scroll"] = RmlUiBindingValue.FromBoolean(!source);
        if (_pages.Present(_document, ++_revision, fields))
        {
            _controllerRevision = state.Revision; _phase = currentPhase; _canAcknowledge = acknowledge;
            if (focusOk) _host.FocusDocument(_document, "campaign_ending_source_ok");
            else if (focusReturn) _host.FocusDocument(_document, "campaign_ending_continue");
        }
    }

    private static double MonotonicNow() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private void ShowSource(bool show)
    {
        _host.SetBool(_document, "visible:campaign_ending_source", show);
        _host.SetBool(_document, "visible:campaign_ending_scroll", !show);
    }

    private static string CreditsText()
    {
        var text = new StringBuilder(Credits.Summary).Append("\n\nFruity Prime // ").Append(Credits.Author)
            .Append('\n').Append(Credits.ForkWork);
        foreach (Credits.Entry entry in Credits.Entries)
        {
            text.Append("\n\n").Append(entry.Who).Append('\n').Append(entry.What);
            if (entry.Where.Length > 0) text.Append('\n').Append(entry.Where);
        }
        return text.Append("\n\nMetroid Prime Hunters is Nintendo's. No game data is included with this program: it is unpacked from your own cartridge dump.").ToString();
    }

    public void Dispose()
    {
        _host.VerifyOwnerThread();
        if (_disposed) return;
        _disposed = true;
        _presentation?.Dispose();
        _audio.Dispose();
        _controller.Dispose();
        if (_document != default && _pages.Page == _document) _pages.ClosePage();
        _document = default;
    }
}
#endif
