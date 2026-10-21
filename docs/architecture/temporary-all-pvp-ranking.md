# Temporary RP awards for all completed online PvP matches

**Status (2026-10-21): enabled on `project-prime-dev` Supabase.**
Backend migration: `20261021135751_temporary_all_completed_pvp_ranking.sql`.

## Why

The existing server-authoritative career reporter accepts completed matches from
custom, hosted and continuous sessions, but the game server sets
`rating_eligible=false` outside the standard public rotation. The database also
excluded non-official reporter trust classes. Historical career records therefore
had not produced RP transactions.

## Temporary policy

The backend-owned `prime.rating_rollout_controls` row
`key='all_completed_pvp', enabled=true` overrides the old client/server
`rating_eligible` hint and eligible reporter-trust restrictions. **No client
release, ranking formula change, or replay of old results is required.**

Included: new **completed authoritative online PvP** reports from provisioned
reporters with 2–8 started players, authenticated **distinct Hunter License**
identities and at least one opposing player/team. Standard/custom game modes,
modifiers, hosted/persistent and continuous matches can earn RP, including
nonofficial registered reporters.

**Guest and email-unverified Licenses are included.** An install automatically
creates an anonymous Supabase Auth session and an owned Hunter License. Neither
email confirmation nor linking a social sign-in is needed. The same narrow
HMAC career ticket is issued to guests and secured accounts, carrying only the
authenticated Supabase UUID and network ClientId. The trusted Edge Function
verifies that token, and the database credits the corresponding License.
"Verified" on the native UI describes an authoritative *synced snapshot*, not
whether the user has verified an email.

The client requests its anonymous session, license record and career ticket
automatically when joining online, even if the Hunter License UI was never
opened. A locally "UNVERIFIED" display may just need to sync; it does not
automatically disqualify the account once the authenticated ticket arrives.

Unchanged exclusions: bot-assisted matches (never enqueued; rejected again by
Edge and DB), offline/local bot matches, **completely unauthenticated or
unsynced** profiles without valid account attribution, duplicate identities,
unfinished matches, a roster without opposing pairs, unknown/unregistered
reporters. A display name or network ClientId alone cannot identify a License
safely for permanent RP.
This is not retroactive and does not bypass the existing idempotent `MatchId`,
canonical player locks or PairwiseNormalizedV1 RP calculation (0–850).

An enabled rollout does not guarantee RP for a match when the server's
`PROJECT_PRIME_CAREER_SERVER_KEY` is absent or a started participant lacks
a **valid authenticated career ticket**, even when the participant has a local
name/License card. This is **not** an email verification requirement. Results
are posted via the durable asynchronous outbox; Hunter License refresh and
guest session creation may lag briefly.

## Reversal

Execute as a privileged database operator:

```sql
UPDATE prime.rating_rollout_controls
   SET enabled = false, changed_at = now()
 WHERE key = 'all_completed_pvp';
```

The existing official-rules/continuous-session hint and reporter trust-class
checks immediately govern subsequent ingestions. Previously awarded RP is not
reversed. Do **not** delete this table as a rollback.

To reactivate, set `enabled=true`. Do not modify this setting from clients.

## Verification

- Migration was dry-run in a rolled-back transaction, then applied.
- `supabase/tests/temporary_all_completed_pvp_ranking.sql` replays a
  previously unrated authoritative FFA report under new match IDs in a
  transaction, asserts that an **anonymous guest account receives an RP
  transaction without an email-verification requirement**, checks no RP when
  the toggle is disabled, checks missing-identity rejection, then **rolls
  back** all changes.
- `supabase/tests/bot_assisted_career.sql` continues testing bot rejection.
- After a new real completed PvP round, check
  `prime.accepted_matches.RatingStatus='applied'` and corresponding rows in
  `prime.rating_transactions`. Existing historical matches are not backfilled.
