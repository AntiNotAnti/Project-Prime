# Social SQL acceptance

Run from the repository root with Node 24 or later:

```sh
npm ci --ignore-scripts --prefix tools/social-db-check
npm test --prefix tools/social-db-check
```

The exact `@electric-sql/pglite` dependency is pinned to **0.5.10** with npm
integrity in `package-lock.json`. Installation stays in this tool folder and
runs no npm lifecycle scripts. The test starts PostgreSQL in memory with no
database directory, connection string, credentials, socket, or live service.

The runner discovers repository migrations whose names contain `social`,
`native_party_authority`, `native_presence_authority` or
`party_reservation_expiry_fence`, sorts them, and
executes their exact saved SQL. Empty in-progress migration files are reported
and skipped. It requires at least the seven donor Social migrations and includes
later Social/Party migrations automatically. It does
not replace, rewrite, or patch migration statements.

`bootstrap.sql` supplies only three client roles, the local `auth.uid()` subject
setting, and the required parent `prime.players` and `prime.player_profiles`
keys/columns referenced by the actual Hunter License predecessor migrations.
The original EF schema is absent from this repository. None of the seven Social
migrations require predecessor reporter tables, so the fixture does not invent
them. An empty `realtime` schema lets actual migration guards detect missing
Realtime services; broadcast delivery is not simulated.

Tests call actual database functions for friend requests and blocks, privacy
redaction and 45-second presence expiry, party invitation ownership and expiry,
leadership changes, leave/kick/disband and eight-member capacity, verified travel
and revision consent, reservation ownership, assignment, admission and expiry.
Activation and admission are called directly after pending/reserved deadlines
expire, without an earlier validation or snapshot that could mask a missing
handler deadline fence. Checks cover the reserved deadline and its null fallback,
unchanged deadlines/member state after rejection, and attempted resurrection.
The native Party suite checks flat snapshot compatibility, exact account,
revision, party and invitation witnesses, legacy mutation invalidation,
monotonic lifecycle revisions, stable presence-only revisions, privacy redaction,
and denial of both native and renamed legacy entry points to client roles.
Expiry is exercised by aging only synthetic fixture rows. Two additional
100-millisecond checks prove a transaction that began before the deadline cannot
activate/admit after the wall-clock deadline has passed.
They also run as `anon` and `authenticated` to prove direct table reads/mutations
and actor-parameter function calls are denied, and temporarily grant SELECT in a
rolled-back transaction to prove RLS still restricts profile/privacy rows.

This checks real SQL execution and role behavior against a small compatibility
schema. It does not establish production EF schema compatibility, concurrent
transaction behavior, Supabase Auth/JWT/Realtime/relay operation, actual UDP
reservation ownership, or deployment status. Those remain separate acceptance
checks. Every identity, room, server and endpoint in this fixture is synthetic.
