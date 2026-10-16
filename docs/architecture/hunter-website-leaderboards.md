# Project Prime website Hunter License and career leaderboard

This integration uses the existing Project Prime Supabase Auth, Hunter License
snapshot function, trusted career aggregates and immutable reporter processing.
No second account database, browser-submitted statistics, or rating rewrite.

## Public visibility and private identity

- Everyone starts **private**. The default public board shows score/rank
  rows under "Private Hunter" without a name, Hunter/License UUID, email,
  cosmetic loadout, match history, or stable public identifier.
- Players can explicitly opt in from their authenticated website License page.
  The visibility preference is stored in `prime.hunter_public_visibility`.
- Opted-in players may display their existing game nickname and a limited
  aggregate public Hunter card linked by a pseudonymous 32-character ID.
  Their Supabase/Auth UUID is never returned by the public Edge endpoint.
- The name search and public directory only match opted-in players.
- Opting out removes the public name and public profile URL as soon as
  caches expire (60-second TTL). Neither the opt-in nor the leaderboard
  modifies authoritative game statistics, ratings, or historical matches.
- Private personal career and recent matches remain on the authenticated
  `hunter-license` Edge Function. No match log is exposed anonymously.

## Career ranking source and scope

Only `prime.career_aggregates` with `TrustClass=-1` and a valid whole
career or a whitelisted numeric game mode enter the board.
The ingestion pipeline produces this scope only from accepted eligible
server reports. Local games, bot-assisted games, and untrusted uploads
cannot author rows here.

Sorts: wins, kills, games, K/D, win rate, headshots.
Filters: lifetime or a specific GameMode numeric wire value.
K/D uses max(deaths, 1); win rate is wins / matches * 100.
The board is a **lifetime career** board, not a ranked matchmaking ladder
or a seasonal ranking. The actual rating_transactions table currently has
no completed results, so ranked rating is never fabricated.

## Database and API

Migration: `20261016184500_hunter_website_visibility.sql`.
It creates a private RLS-enabled table and exactly two narrowly scoped
authenticated owner RPCs:

- `public.project_prime_hunter_visibility()`
- `public.project_prime_set_hunter_visibility(p_public boolean)`

Both derive the authenticated account from `auth.uid()`. Neither accepts
an arbitrary player UUID, touches the accepted career record, or exposes
the underlying private Prime/EF-managed tables.

The `hunter-leaderboard` Supabase Edge Function is GET-only and uses a
server-side database connection. It caps pages to 25 (100 pages max),
validates whitelisted sort/mode values, bounds name searches and uses
parameterized Postgres queries. Response metadata:
`version`, `source`, `metric`, `mode`, `page`, `page_size`,
`total`, `ranked_available`, `entries`.
The `?hunter=<opaque-id>` route returns only opted-in careers.

API errors do not echo database details or include PII.
The Edge function sends no-store responses. Public website catalog fetches may cache for at most 60 seconds; direct public profile lookups are uncached so a visibility opt-out prevents new profile reads immediately. No public write API
exists, and the website does not receive database service credentials.

## Deployment sequence

1. Merge and apply the SQL migration using normal Supabase migration tooling.
2. Run `supabase/tests/hunter-website-visibility.sql` and security advisors.
3. Deploy `supabase/functions/hunter-leaderboard` with `verify_jwt=false`.
4. Verify query/permission tests, backend anonymous/private responses,
   and caching behavior.
5. Deploy the website using the same Supabase project as Project Prime game.
6. Sign in as an existing Hunter and compare website private career
   statistics and recent matches with the in-game Hunter License.
7. Verify both opt-in and opt-out, directory name search,
   public profile 404, and per-mode sorting.
8. Keep rating ranking disabled until a truly rated match is accepted.

The live service is not changed by simply merging source PRs. Rollback
is safe by hiding website entry points and/or disabling the Edge
Function; retain visibility preferences for future restores.
