# Hunter License public profiles and authoritative career leaderboard

Project Prime uses its existing Supabase Auth identity and Prime/EF career
database. This policy introduces **public-by-default Hunter Licenses with an
explicit opt-out**, without adding a second player database or changing
authoritative match ingestion.

## Public visibility policy

- New and previously created Hunter Licenses **with a Supabase Auth owner**
  and without a preference row are public after the migration and Edge update.
  This affects existing Hunters too, not only new registrations. Legacy orphan
  Licenses with no Auth identity remain hidden because nobody can opt them out.
- An existing `is_public=false` remains private. The new migration never
  backfills, deletes, resets or silently changes a saved preference.
- Signing into a Hunter License gives the owner a visibility control at
  `/license` on the website **and in the game's Account screen**. Guest
  Hunters can opt out directly in-game without linking a recovery email.
- Opting out hides the nickname and removes public profile URLs. Players still
  contribute anonymous rank and score totals from accepted career matches.
  Future searches only match publicly visible names.
- Public fields are limited to the in-game nickname, favorite Hunter and
  aggregated server-accepted career statistics. Public URLs use an opaque
  derived ID. **Never** expose Supabase Auth UUID, email, refresh token,
  raw match evidence, cosmetic inventory, or private recent match history.
- Even new Hunters with **zero** accepted matches have a discoverable public
  license with zeroed statistics. The competitive leaderboard **excludes**
  accounts without accepted eligible matches.

## Database and permissions

`20261016214500_hunter_public_default_opt_out.sql` changes the private
`prime.hunter_public_visibility` table column default and the
`public.project_prime_hunter_visibility()` owner-read RPC so missing rows
mean public. Explicit `false` is still the opt-out.

The existing `project_prime_set_hunter_visibility(p_public boolean)` function
already derives the caller from `auth.uid()`, requires a real License, and
writes only their own preference. It is not replaced or loosened.
The visibility table still has RLS, no grants to browser roles and no
`anon` RPC permission. In the game, RPCs use the same signed-in Supabase
session (anonymous/guest sign-ins are still an authenticated role).

## Read-only public career projection

The `hunter-leaderboard` Edge Function uses the existing service-only DB
connection, and accepts GETs for a ranked career list, public directory or
an individual public Hunter card. The SQL starts at the licenses/profiles
tables and LEFT JOINs the server-accepted aggregate:
- A public Hunter card resolves without an eligible career match.
- Directory `only_public=1` lists public Hunters even when matches = 0.
- Normal leaderboards remain restricted to `TrustClass=-1` career records
  with `Matches > 0`, preserving bot exclusions and trusted ingestion.
- The query masks `public_id` and `display_name` for opted-out players.
- Rankings support wins, kills, games, K/D, win rate and headshots, with
  existing bounded search, mode keys and 25-row pages.
- No ranked rating is invented from lifetime career totals.
- Direct public profile responses are no-store; the website should also
  avoid caching public lists so an opt-out takes effect for fresh requests.

## Rollout sequence and acceptance

1. Merge and release a client build that exposes the in-game guest opt-out,
   so players who have never linked a website sign-in can control privacy.
2. Merge/update the website visibility copy and uncached directory/leaderboard.
3. Merge/apply the SQL migration; retain existing `false` overrides.
4. Deploy the updated `hunter-leaderboard` Edge Function with verify_jwt=false
   and server-only DB credentials; do not redeploy unrelated auth functions.
5. Run the owner-grant and default SQL tests, database security advisors,
   Edge Deno typecheck and website CI.
6. Exercise one opted-out player and one newly public player in real accounts;
   verify the opted-out public profile returns 404 and no name on leaderboards.
7. Verify empty-career public cards, guest opt-out in-game, website opt-back-in,
   true match rankings, bot exclusions and per-mode pagination.
8. Confirm that the email, account UUID and recent private matches never
   appear in anonymous API responses or cached website HTML.

**Deployment warning:** changing the live database + Edge Function exposes
existing unset-preference Hunters publicly. Do not deploy this migration
ahead of the in-game opt-out release and user-facing privacy notice.
