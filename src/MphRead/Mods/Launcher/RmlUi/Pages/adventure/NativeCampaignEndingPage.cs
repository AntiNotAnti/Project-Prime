#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
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
    private long _revision;
    private bool _opened, _disposed;

    public CampaignEndingSummary Receipt { get; }
    public RmlUiDocumentToken Document => _document;
    public bool Closed => _controller.Snapshot().Closed;

    public NativeCampaignEndingPage(RmlUiHost host, RmlUiPageManager pages, CampaignEndingSummary summary)
        : this(host, pages, summary, new CampaignAudioSession()) { }

    internal NativeCampaignEndingPage(RmlUiHost host, RmlUiPageManager pages, CampaignEndingSummary summary,
        ICampaignAudioSink audio) : this(host, pages, summary, new CampaignAudioSession(audio)) { }

    private NativeCampaignEndingPage(RmlUiHost host, RmlUiPageManager pages, CampaignEndingSummary summary,
        CampaignAudioSession audio)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        Receipt = summary;
        _audio = audio;
        _controller = new(new NativeCampaignEndingReport(summary.Slot, summary.Kind == CampaignEndingKind.Complete,
            summary.SaveCommitted, summary.CompletionPercent, summary.Octoliths, summary.Deaths,
            summary.HunterKills, summary.EnemyKills) { PlayTicks = summary.PlayTicks, GoreaTicks = summary.GoreaTicks });
    }

    public void Open()
    {
        _host.VerifyOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_opened) return;
        _document = _pages.OpenPage(new("campaign-ending", "pages/adventure/ending.rml", "campaign_ending_continue"));
        _opened = true;
        _audio.Enter(CampaignAudioPage.EndingResults);
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!CanReturn() || intent.Document != _document || intent.Kind != RmlUiIntentKind.Navigate
            || intent.Argument != (int)RmlUiRouteArgument.Adventure || !_pages.Accept(intent)) return false;
        return Finish(back: false);
    }

    public bool Back()
    {
        _host.VerifyOwnerThread();
        return CanReturn() && Finish(back: true);
    }

    private bool CanReturn() => !_disposed && _opened && !Closed && _pages.Top == _document
        && _host.IsAlive(_document) && _host.IsVisible(_document);

    private bool Finish(bool back)
    {
        if (!(back ? _controller.Back() : _controller.Continue())) return false;
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
    public void Refresh()
    {
        _host.VerifyOwnerThread();
        if (_disposed || !_opened) return;
        if (!_host.IsAlive(_document)) { _audio.Dispose(); return; }
        if (!Closed) _audio.Tick();
        var state = _controller.Snapshot();
        if (state.Revision <= _revision) return;
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
        if (_pages.Present(_document, state.Revision, fields)) _revision = state.Revision;
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
        _audio.Dispose();
        _controller.Dispose();
        if (_document != default && _pages.Page == _document) _pages.ClosePage();
        _document = default;
    }
}
#endif
