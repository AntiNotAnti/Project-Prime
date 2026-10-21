using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;

internal static class HunterStagePlanCacheAcceptance
{
    internal static void Run(HubSnapshot hub, RmlUiLobbyView lobby, RmlUiAdminSnapshot roster)
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("STAGE CACHE: " + name);
            checks++;
        }
        var cache = new RmlUiHunterStagePlanCache();
        void Equivalent(HubSnapshot h, RmlUiLobbyView l, RmlUiAdminSnapshot? r)
        {
            var expected = RmlUiHunterStagePlan.Build(h, l, r);
            var actual = cache.Read(h, l, r);
            Check(actual.GameFilesReady == expected.GameFilesReady && actual.Lobby == expected.Lobby
                && actual.Starting == expected.Starting && actual.HomeHunter == expected.HomeHunter
                && actual.HomeSuit == expected.HomeSuit && actual.MatchId == expected.MatchId
                && actual.AuthorityEpoch == expected.AuthorityEpoch
                && actual.Formation.SequenceEqual(expected.Formation), "cached facts equal fresh authoritative projection");
            Check(ReferenceEquals(actual, cache.Read(h, l, r)), "unchanged facts reuse the plan");
        }
        Equivalent(hub, lobby, roster);
        var original = cache.Read(hub, lobby, roster);
        Check(ReferenceEquals(original, cache.Read(hub, lobby, roster with
        { SessionRevision = (ushort)(roster.SessionRevision + 1), Players = roster.Players.ToArray() })),
            "a revision or array replacement alone does not rebuild unchanged visual facts");
        Equivalent(hub, lobby, roster with { MatchId = (ushort)(roster.MatchId + 1) });
        Equivalent(hub, lobby, roster with { AuthorityEpoch = roster.AuthorityEpoch + 1 });
        Equivalent(hub, lobby with { Starting = true }, roster);
        Equivalent(hub, lobby with { Active = false }, roster);
        Equivalent(hub with { GameFilesReady = false }, lobby, roster);
        Equivalent(hub with { DisplayHunter = Hunter.Weavel, Suit = 3 }, lobby, roster);
        Equivalent(hub, lobby, null);
        Equivalent(hub, lobby, roster with { MatchId = 0 });
        Equivalent(hub, lobby, roster with { AuthorityEpoch = 0 });
        foreach (byte teamCount in new byte[] { 0, 1, 2, 3, 4, 5 })
            Equivalent(hub, lobby, roster with { TeamCount = teamCount });
        for (int count = 0; count <= 8; count++)
        for (byte local = 0; local < 9; local++)
        {
            var people = Enumerable.Range(0, count).Select(i => new RmlUiAdminPlayer(
                (byte)i, (ushort)(i + 1), "ACTOR " + i, (Hunter)(i % 7), (byte)(i % 4),
                (sbyte)(i % 2), i % 2 == 0, false, 0, 0, 0)).Reverse().ToArray();
            Equivalent(hub, lobby, roster with { Players = people, LocalSlot = local });
            if (count > 0)
                Equivalent(hub, lobby, roster with { Players = people.Concat(new[] { people[0] }).ToArray(), LocalSlot = local });
        }
        var mutable = roster with { Players = roster.Players.ToArray() };
        var before = cache.Read(hub, lobby, mutable);
        mutable.Players[0] = mutable.Players[0] with
        { Name = "CHANGED WITHOUT REVISION", Hunter = Hunter.Noxus, Suit = 3, Team = 0, Ready = true, Generation = 99 };
        var after = cache.Read(hub, lobby, mutable);
        Check(!ReferenceEquals(before, after), "same-array and same-revision mutation invalidates every changed identity fact");
        Equivalent(hub, lobby, mutable);
        // A real unchanged lobby previously allocated a GroupBy/List/array/plan
        // on every draw. Measure that specific render-thread lookup boundary.
        for (int i = 0; i < 10_000; i++) cache.Read(hub, lobby, mutable);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 24_000; i++) cache.Read(hub, lobby, mutable);
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(bytes == 0, "24,000 unchanged stage lookups allocate zero bytes: " + bytes);
        cache.Clear();
        Check(!ReferenceEquals(after, cache.Read(hub, lobby, mutable)), "hidden-stage clear drops retained roster plan");
        Console.WriteLine($"HUNTER STAGE CACHE PASS {checks} checks; zero-allocation unchanged lookup; no services or GL.");
    }
}
