using MphRead.Mods.Launcher.RmlUi.Core;

internal static class TheatreSmartViewAcceptance
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Theatre Smart View: " + message);
            checks++;
        }
        long now = DateTime.Now.Ticks;
        var alpha = new RmlUiReplayEntry("/_demos/alpha.ppdemo", "Alpha", "tagged, bookmarks, 20 minutes",
            "Arena A", now, 100, 1000, true, false, false, true);
        var beta = alpha with { Path = "/_demos/beta.ppdemo", Title = "Beta", Room = "arena a",
            RecordedTicks = now - 1, Favorite = false, Clip = true, Tags = "practice" };
        var gamma = alpha with { Path = "/_demos/gamma.ppdemo", Title = "Gamma", Room = "Arena AB",
            RecordedTicks = now - 2, Favorite = false, Collections = "Tournament" };
        var whitespace = alpha with { Path = "/_demos/blank.ppdemo", Title = "Blank", Room = "Arena B",
            RecordedTicks = now - 3, Favorite = false, Tags = "  ", Collections = "\t" };
        var rows = new[] { alpha, beta, gamma, whitespace };

        var order = new RmlUiTheatreIndex();
        order.Refresh(rows);
        foreach (string label in new[] { "ALL REPLAYS", "FULL MATCHES", "CLIPS", "FAVORITES",
            "RECENT", "RECOVERABLE", "SAME MAP", "TAGGED / COLLECTED", "SAME PLAYERS",
            "ANNOTATED", "HAS HIGHLIGHTS", "HAS BOOKMARKS", "LONG SESSIONS // 10 MIN+",
            "SHORT CLIPS // UNDER 60 SEC" })
        {
            Check(order.FilterLabel == label, "all fourteen views retain their established cycle order: " + label);
            order.NextFilter();
        }
        Check(order.FilterLabel == "ALL REPLAYS", "the expanded cycle returns to All Replays");

        var map = new RmlUiTheatreIndex();
        map.Refresh(rows);
        for (int i = 0; i < 5; i++) map.NextFilter();
        Check(map.FilterLabel == "RECOVERABLE" && map.Count == 0 && map.Selected == null,
            "an empty recovery view clears actionable selection");
        map.NextFilter();
        Check(map.FilterLabel == "SAME MAP" && map.Count == 2 && map.Selected == alpha
            && map.Entry(1) == beta,
            "nonactionable map context survives an empty intervening view and matches exact case-insensitive room keys");
        Check(map.SearchFor("Gamma") && map.Count == 0 && map.Selected == null
            && !map.SelectedUnchanged(alpha),
            "search intersects the pinned map without retaining a hidden replay for actions");
        Check(map.SearchFor("") && map.Count == 2 && map.Selected == alpha,
            "clearing an empty search preserves the map context");
        Check(!map.SearchFor(new string('X', 513)) && map.Count == 2 && map.Selected == alpha,
            "invalid search cannot alter map context or selection");
        map.NextFilter();
        Check(map.FilterLabel == "TAGGED / COLLECTED" && map.Count == 2
            && map.Entry(0) == beta && map.Entry(1) == gamma,
            "saved tags or collections qualify; whitespace and descriptive prose do not invent organization metadata");
        Check(map.SearchFor("Tournament") && map.Count == 1 && map.Selected == gamma,
            "organization search intersects the tagged view and repairs selection");

        var removed = new RmlUiTheatreIndex();
        removed.Refresh(rows);
        for (int i = 0; i < 6; i++) removed.NextFilter();
        removed.Refresh(new[] { beta, gamma });
        Check(removed.Count == 2 && removed.Selected == beta,
            "removing the anchor retires map context rather than silently rebinding it to a same-map replay");
        removed.Refresh(rows);
        Check(removed.Count == 4,
            "a removed anchor reappearing later does not resurrect retired filter context");
        Check(removed.Select(2) && removed.Count == 1 && removed.Selected == gamma,
            "explicitly selecting a visible replay supplies new map context when none remains");
        var changedGamma = gamma with { Bytes = gamma.Bytes + 1, LastWriteTicks = gamma.LastWriteTicks + 1 };
        removed.Refresh(new[] { alpha, beta, changedGamma });
        Check(removed.Count == 3 && removed.Selected == changedGamma && !removed.SelectedUnchanged(gamma),
            "replaced file facts retire map context and reject the old action witness");

        var virtualAnchor = alpha with { VirtualWitness = new("descriptor-a", alpha.Path, 100, 1000) };
        var virtualMap = new RmlUiTheatreIndex();
        virtualMap.Refresh(new[] { virtualAnchor, gamma });
        for (int i = 0; i < 6; i++) virtualMap.NextFilter();
        var changedVirtual = virtualAnchor with { VirtualWitness = new("descriptor-b", alpha.Path, 100, 1000) };
        virtualMap.Refresh(new[] { changedVirtual, gamma });
        Check(virtualMap.Count == 2 && !virtualMap.SelectedUnchanged(virtualAnchor),
            "virtual descriptor replacement also retires the map anchor");

        var unknown = new RmlUiTheatreIndex();
        unknown.Refresh(new[] { alpha with { Room = "" } });
        for (int i = 0; i < 6; i++) unknown.NextFilter();
        Check(unknown.Count == 1 && unknown.Selected.HasValue,
            "a library without map metadata remains browseable rather than inventing a map predicate");

        // A selected favorite clip survives the intervening views and supplies
        // the context for both relational filters. Distinct facts make every
        // filter's membership observable, including exact duration boundaries.
        var marked = alpha with { Clip = true, Recoverable = true, Tags = "practice",
            PlayerNamesText = "  Al   Li\tNK  ",
            DurationFrames = 3599, BookmarkCount = 1, HighlightCount = 1,
            AnnotationNames = "Opening pick Final escape" };
        var minute = beta with { Tags = "", DurationFrames = 3600,
            PlayerNamesText = "FALCON", BookmarkCount = 2, HighlightCount = 0,
            AnnotationNames = "Secret route" };
        var belowLong = gamma with { DurationFrames = 35999, PlayerNamesText = "li\tNK guest",
            BookmarkCount = 0, HighlightCount = 1, AnnotationNames = "Comeback" };
        var longMatch = whitespace with { Title = "Delta", DurationFrames = 36000,
            BookmarkCount = 0, HighlightCount = 0 };
        var unknownClip = alpha with { Path = "/_demos/epsilon.ppdemo", Title = "Epsilon",
            RecordedTicks = now - 4, Room = "Arena C", Favorite = false, Clip = true,
            Detail = "bookmarks and highlights mentioned in prose" };
        var zeroClip = unknownClip with { Path = "/_demos/zeta.ppdemo", Title = "Zeta",
            RecordedTicks = now - 5, DurationFrames = 0 };
        var oldLong = unknownClip with { Path = "/_demos/eta.ppdemo", Title = "Eta", Clip = false,
            RecordedTicks = DateTime.Now.AddDays(-8).Ticks, DurationFrames = 36001 };
        var richRows = new[] { marked, minute, belowLong, longMatch, unknownClip, zeroClip, oldLong };
        string[][] expected = {
            new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta" },
            new[] { "Gamma", "Delta", "Eta" }, new[] { "Alpha", "Beta", "Epsilon", "Zeta" },
            new[] { "Alpha" }, new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta" },
            new[] { "Alpha" }, new[] { "Alpha", "Beta" }, new[] { "Alpha", "Gamma" },
            new[] { "Alpha", "Beta", "Gamma" }, new[] { "Alpha", "Beta", "Gamma" },
            new[] { "Alpha", "Gamma" }, new[] { "Alpha", "Beta" }, new[] { "Delta", "Eta" },
            new[] { "Alpha" }
        };
        var filters = new RmlUiTheatreIndex();
        filters.Refresh(richRows);
        for (int filter = 0; filter < expected.Length; filter++)
        {
            Check(Titles(filters).SequenceEqual(expected[filter]),
                "view membership respects saved facts and boundaries: " + filters.FilterLabel);
            Check(filters.Selected is { } selected && expected[filter].Contains(selected.Title),
                "every nonempty view repairs selection to a visible replay: " + filters.FilterLabel);
            filters.NextFilter();
        }
        Check(filters.FilterLabel == "ALL REPLAYS" && filters.Count == richRows.Length,
            "fourteen functional filters cycle back to the whole library");

        var people = new RmlUiTheatreIndex();
        people.Refresh(richRows);
        for (int i = 0; i < 8; i++) people.NextFilter();
        Check(people.SearchFor("FALCON") && people.Selected?.Path == minute.Path && people.Count == 1,
            "same players matches ANY anchor token as a case-insensitive substring, without requiring the full roster");
        Check(people.SearchFor("guest") && people.Selected?.Path == belowLong.Path && people.Count == 1,
            "a different matching token still uses the original pinned roster after search selected another replay");
        var spaceOnly = unknownClip with { Path = "/_demos/space.ppdemo", Title = "Space",
            PlayerNamesText = "Li NK guest", RecordedTicks = now - 6 };
        people.Refresh(richRows.Append(spaceOnly));
        Check(people.Count == 1 && people.Selected?.Path == belowLong.Path,
            "anchor tokens split on ASCII spaces; embedded tabs cannot become independent matching names");
        Check(people.SearchFor("no matching roster") && people.Selected == null
            && !people.SelectedUnchanged(marked) && !people.SelectedUnchanged(belowLong),
            "an empty same-players search retires actionable selection");
        Check(people.SearchFor("") && people.Count == 3 && people.Selected?.Path == marked.Path,
            "empty player searches preserve browsing context without retaining hidden action targets");
        var changedRoster = marked with { PlayerNamesText = "New roster", PlayerNameTokens = new[] { "New", "roster" } };
        people.Refresh(richRows.Select(row => row.Path == marked.Path ? changedRoster : row));
        Check(people.Count == richRows.Length,
            "changed player metadata retires the previous relational context");
        people.Refresh(richRows);
        Check(people.Count == richRows.Length,
            "restoring a roster cannot resurrect a retired player context");
        Check(people.Select(1) && people.Count == 1 && people.Selected?.Path == minute.Path,
            "explicit selection supplies a fresh player context after retirement");
        people.Refresh(richRows.Where(row => row.Path != minute.Path));
        Check(people.Count == richRows.Length - 1 && people.Selected?.Path == marked.Path,
            "removing the selected player anchor retires context and picks a visible replacement");

        var searches = new RmlUiTheatreIndex();
        searches.Refresh(richRows);
        Check(searches.SearchFor("Final escape") && searches.Count == 1 && searches.Selected?.Path == marked.Path,
            "named highlights are searchable without relying on display prose");
        Check(searches.SearchFor("Secret route") && searches.Selected?.Path == minute.Path,
            "named bookmarks are searchable");
        var displayNameSearch = new RmlUiTheatreIndex();
        var retailMapReplay = alpha with { Room = "MP1 SANCTORUS" };
        displayNameSearch.Refresh(new[] { retailMapReplay });
        Check(displayNameSearch.SearchFor("data shrine")
            && displayNameSearch.Selected?.Path == retailMapReplay.Path
            && displayNameSearch.Selected?.Room == "MP1 SANCTORUS",
            "Theatre search accepts the player-facing map title while retaining the room key for identity");

        var sorts = new RmlUiTheatreIndex();
        sorts.Refresh(new[] {
            marked with { Title = "zulu", Bytes = 200 },
            minute with { Title = "ALPHA", Bytes = 300 },
            belowLong with { Title = "bravo", Bytes = 100 },
            unknownClip with { Title = "charlie", Bytes = 400 }
        });
        string[][] sortedTitles = {
            new[] { "zulu", "ALPHA", "bravo", "charlie" },
            new[] { "charlie", "bravo", "ALPHA", "zulu" },
            new[] { "ALPHA", "bravo", "charlie", "zulu" },
            new[] { "charlie", "ALPHA", "zulu", "bravo" },
            new[] { "bravo", "ALPHA", "zulu", "charlie" }
        };
        string[] sortLabels = { "NEWEST FIRST", "OLDEST FIRST", "NAME A–Z", "LARGEST FIRST", "LONGEST FIRST" };
        var sortSelection = sorts.Selected;
        for (int sort = 0; sort < sortLabels.Length; sort++)
        {
            Check(sorts.SortLabel == sortLabels[sort] && Titles(sorts).SequenceEqual(sortedTitles[sort]),
                "sort order uses actual metadata: " + sortLabels[sort]);
            Check(sorts.Selected == sortSelection, "sorting preserves selected replay identity");
            sorts.NextSort();
        }
        Check(sorts.SortLabel == "NEWEST FIRST", "all five sorts cycle back to newest");
        var unknownDurations = new RmlUiTheatreIndex();
        unknownDurations.Refresh(new[] { unknownClip, zeroClip, longMatch });
        for (int i = 0; i < 4; i++) unknownDurations.NextSort();
        Check(Titles(unknownDurations).SequenceEqual(new[] { "Delta", "Epsilon", "Zeta" })
            && unknownDurations.Entry(1)?.DurationFrames == null
            && unknownDurations.Entry(2)?.DurationFrames == null,
            "zero duration becomes unknown and both unknown rows sort last in stable newest order");
        Console.WriteLine($"Theatre Smart View acceptance passed: {checks} assertions.");
    }

    private static string[] Titles(RmlUiTheatreIndex index)
        => Enumerable.Range(0, index.Count).Select(slot => index.Entry(slot)!.Value.Title).ToArray();
}
