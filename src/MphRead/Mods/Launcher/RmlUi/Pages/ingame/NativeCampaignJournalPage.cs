#if (MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER) || PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.InGame;

/// <summary>Read-only journal over an already detached, discovered-only campaign snapshot.</summary>
public sealed class NativeCampaignJournalPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly CampaignJournal _journal;
    private readonly bool _localSessionPaused;
    private RmlUiDocumentToken _document;
    private long _revision;
    private bool _scans;
    private int _category, _offset;
    private const int PageSize = 4;
    public bool IsOpen => _document != default && _host.IsAlive(_document) && _pages.Top == _document;
    public RmlUiDocumentToken Document => _document;

    public NativeCampaignJournalPage(RmlUiHost host, RmlUiPageManager pages, CampaignJournal journal,
        bool localSessionPaused = false)
        => (_host, _pages, _journal, _localSessionPaused) = (host, pages, journal, localSessionPaused);

    public void Open()
    {
        if (IsOpen) return;
        _document = _pages.OpenModal(new("campaign-journal", "pages/ingame/journal.rml", "journal_close"));
        Present();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (!IsOpen || intent.Document != _document || !_pages.Accept(intent)
            || intent.Kind != RmlUiIntentKind.InGameAction) return false;
        switch ((InGameAction)intent.Argument)
        {
            case InGameAction.JournalBack: Close(); return true;
            case InGameAction.JournalMap: _scans = false; _offset = 0; break;
            case InGameAction.JournalScans: _scans = true; _offset = 0; break;
            case InGameAction.JournalCategory: if (!_scans) return false; _category = (_category + 1) % 6; _offset = 0; break;
            case InGameAction.JournalPrevious: _offset = Math.Max(0, _offset - PageSize); break;
            case InGameAction.JournalNext: _offset += PageSize; break;
            default: return false;
        }
        Present();
        return true;
    }

    public bool Close()
    {
        if (!IsOpen) return false;
        if (!_pages.CloseModal()) return false;
        _document = default;
        return true;
    }

    private void Present()
    {
        var rows = new List<(string Title, string Detail)>();
        if (_scans)
        {
            foreach (CampaignJournalScan scan in _journal.Scans)
                if (Matches(scan.Category, _category))
                    rows.Add(($"#{scan.Id:000} // {CategoryName(scan.Category)} // {scan.Title}", scan.Description ?? ""));
        }
        else
        {
            foreach (CampaignPlanetStatus planet in _journal.Planets)
            {
                string progress = !planet.Unlocked ? "SECTOR LOCKED"
                    : $"OCTOLITHS {planet.Octoliths}/{planet.OctolithTotal} // ARTIFACTS {planet.Artifacts}/{planet.ArtifactTotal} // "
                        + (planet.Cleared ? "CLEARED" : "IN PROGRESS");
                string rooms = planet.Unlocked
                    ? String.Join("\n", _journal.Rooms.Where(room => room.Planet == planet.Planet.Id)
                        .Select(room => (room.LandingSite ? "LANDING // " : "") + room.DisplayName)) : "";
                if (planet.Unlocked && rooms.Length == 0) rooms = "NO EXPLORED ROOMS";
                rows.Add((planet.Planet.Name, progress + "\n" + rooms));
            }
        }
        _offset = Math.Clamp(_offset, 0, Math.Max(0, ((rows.Count - 1) / PageSize) * PageSize));
        var fields = new Dictionary<string, RmlUiBindingValue>();
        void Text(string key, string value) => fields[key] = RmlUiBindingValue.FromText(value);
        void Bool(string key, bool value) => fields[key] = RmlUiBindingValue.FromBoolean(value);
        Text("journal_summary", $"OCTOLITHS {_journal.Octoliths}/8 // SCANS {_journal.DiscoveredScanBits} DISCOVERED"
            + (_localSessionPaused ? "" : "\nGAMEPLAY CONTINUES WHILE THIS JOURNAL IS OPEN."));
        Text("journal_section", _scans ? "SCAN LOGBOOK" : "MISSION MAP");
        Text("journal_category", new[] { "ALL CATEGORIES", "LORE", "BIOFORM", "OBJECT", "EQUIPMENT", "OTHER" }[_category]);
        Text("journal_page", rows.Count == 0 ? "0 / 0" : $"{_offset / PageSize + 1} / {(rows.Count + PageSize - 1) / PageSize}");
        Text("journal_empty", _scans && !_journal.HasLogbookTable
            ? "SCAN LOGBOOK TEXT IS UNAVAILABLE FROM THE LOCAL GAME FILES. RECORDED SCAN BITS ARE PRESERVED."
            : _scans ? "NO SCANNED ENTRIES IN THIS CATEGORY." : "NO EXPLORED ROOMS.");
        Bool("visible:journal_empty", rows.Count == 0);
        Bool("disabled:journal_category", !_scans);
        Bool("disabled:journal_previous", _offset == 0);
        Bool("disabled:journal_next", _offset + PageSize >= rows.Count);
        for (int i = 0; i < PageSize; i++)
        {
            bool present = _offset + i < rows.Count;
            Bool("visible:journal_row" + i, present);
            Text("journal_title" + i, present ? rows[_offset + i].Title : "");
            Text("journal_detail" + i, present ? rows[_offset + i].Detail : "");
        }
        _pages.Present(_document, ++_revision, fields);
    }

    private static bool Matches(char category, int index) => index switch
    {
        0 => true, 1 => category == 'L', 2 => category == 'B', 3 => category == 'O',
        4 => category == 'E', _ => category is not ('L' or 'B' or 'O' or 'E')
    };
    private static string CategoryName(char category) => category switch
    { 'L' => "LORE", 'B' => "BIOFORM", 'O' => "OBJECT", 'E' => "EQUIPMENT", _ => "OTHER" };

    public void Dispose() { Close(); _document = default; }
}
#endif
