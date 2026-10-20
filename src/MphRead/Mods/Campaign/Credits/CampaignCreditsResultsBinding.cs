using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;

namespace MphRead.Mods.Campaign;

/// <summary>Item-scoped result replacements from one captured receipt. Source strings are never mutated.</summary>
internal sealed class CampaignCreditsResultsBinding
{
    private readonly Dictionary<(int Page, int Item), (int StringId, string Value)> _values = new();
    private readonly CampaignCreditsSource.Document _document;
    internal CampaignCreditsResultsBinding(CampaignCreditsSource.Document document, CampaignEndingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        Require(summary.Kind is CampaignEndingKind.Complete or CampaignEndingKind.Incomplete,
            "Credits receipt outcome is unsupported.");
        var good = document.Pages.Single(p => p.Index == 49); var bad = document.Pages.Single(p => p.Index == 50);
        // Positional roles are from the pinned MIT frontend binder. No ROM text, item ordinal or string ID is baked in.
        (int X, int Y)[] anchors = { (38, 136), (38, 82), (128, 25), (218, 136), (218, 82) };
        string[] values = { CampaignTimeRecords.FormatTotal(summary.PlayTicks), CampaignTimeRecords.FormatGorea(summary.GoreaTicks),
            summary.CompletionPercent is >= 0 and <= 100 ? summary.CompletionPercent.Value.ToString(CultureInfo.InvariantCulture) + "%" : "NOT AVAILABLE",
            summary.Deaths.ToString(CultureInfo.InvariantCulture), summary.HunterKills.ToString(CultureInfo.InvariantCulture) };
        var goodIds = new HashSet<int>(); var badIds = new HashSet<int>();
        int goodGorea = -1, badGorea = -1;
        for (int role = 0; role < anchors.Length; role++)
        {
            var a = At(good, anchors[role]); var b = At(bad, anchors[role]);
            int goodId = Id(a), badId = Id(b);
            Require(goodIds.Add(goodId) && badIds.Add(badId), "Credits results roles alias another field.");
            if (role == 1) { goodGorea = goodId; badGorea = badId; }
            else Require(goodId == badId, "Credits results pages do not share the expected field identity.");
            _values.Add((49, a.Index), (goodId, values[role]));
            // The incomplete battle status belongs to its source page, even if the receipt has recorded time.
            if (role != 1) _values.Add((50, b.Index), (badId, values[role]));
        }
        Require(goodGorea != badGorea && !goodIds.Contains(badGorea) && !badIds.Contains(goodGorea),
            "Credits incomplete battle status is not independent of the completed result.");
    }
    internal string Resolve(CampaignCreditsSource.Document document, int page, int item, CampaignCreditsSource.TextStyle style)
    {
        Require(ReferenceEquals(document, _document), "Credits result replacements belong to another document.");
        if (_values.TryGetValue((page, item), out var value))
        {
            Require(value.StringId == style.StringId, "Credits results style changed its bound field identity.");
            return value.Value;
        }
        return document.Strings[style.StringId].ContentLatin1;
    }
    private static CampaignCreditsSource.Item At(CampaignCreditsSource.Page page, (int X, int Y) anchor)
    {
        CampaignCreditsSource.Item? found = null;
        foreach (var item in page.Items)
        {
            if (!item.States.Any(s => s.Text != null)
                || Math.Abs((long)item.XFx - anchor.X * 4096L) >= 6144
                || Math.Abs((long)item.YFx - anchor.Y * 4096L) >= 6144) continue;
            Require(found == null, "Credits results field anchor is ambiguous."); found = item;
        }
        Require(found != null, "Credits results field anchor is missing."); return found!;
    }
    private static int Id(CampaignCreditsSource.Item item)
    {
        Require(item.States.Count == 3 && item.States.All(s => s.Text != null), "Credits results field states are unsupported.");
        int id = item.States[0].Text!.StringId;
        Require(item.States.All(s => s.Text!.StringId == id), "Credits results field states disagree on string identity.");
        return id;
    }
}
