# Waitlist source fixture compatibility — 2026-10-19

The PR 735 retired-hunter compatibility job failed compiling the independent `lobby-waitlist-check` harness: linked `LobbyWaitlist.cs` now references its production partial's private `GroupHold` type, while the harness omitted `LobbyWaitlistGroups.cs`.

The project now links the real production group partial and its real reservation wire helper/schema source. The isolated test supplies only the three existing packet identifier constants required by that schema; it does not invent a replacement group policy or modify production admission. The separate group-reservation registry gate remains responsible for proving the production packet registry values. Existing synthetic queue IDs and all policy/ownership/malformed-packet/loopback tests remain unchanged.

Validation: `dotnet run --project tools/lobby-waitlist-check -c Release` passed all 203 assertions, including genuine loopback datagrams with generated synthetic queue schemas. This harness does not claim live NetTransport admission coverage. Its existing nullable-value compiler warning remains. No Core build was required. `git diff --check` passed.

The preceding broader local no-assets audit was stopped when prioritizing this hosted failure. The exact hosted main no-assets job 118333645697 completed successfully on 361e42cb3. Local early timing-sensitive ticket/reservation tests were recorded separately and no authentication or reservation production behavior was changed.
