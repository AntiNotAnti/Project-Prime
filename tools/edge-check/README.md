# Edge function reproducibility and acceptance

Run from the repository root with Deno **2.9.7** and Node **24 or later**:

```sh
bash tools/edge-check/check-deno.sh
node --test tools/edge-check/*.test.ts
```

Run the disposable SQL acceptance check with Node/npm on macOS or Linux:

```sh
node tools/edge-check/check-career-sql.mjs
```

This installs **@electric-sql/pglite 0.5.8**, verifies its exact npm integrity,
and keeps the dependency, npm cache and configuration in a private directory
under `/tmp`.
It deletes that directory afterward and creates no repository package or global
service. The runtime is PostgreSQL **18.3** with PL/pgSQL, in memory, with no
database connection string or production connection.

The runner executes all actual repository migrations in order and the unchanged
`career-cumulative.sql` and `supabase/tests/bot_assisted_career.sql` gates. It also
checks a version 1 report across the migration boundary, duplicate/hash/reporter
conflicts, bot rejection without state changes, account recovery, preserved ACLs
and actual client-role denial. The cumulative gate covers 128 admissions, eight
rating starters, rejection at 129 and an oversized starting-roster downgrade.

`sql-fixture/fixture.sql` is a minimal compatible schema derived from columns and
conflict keys referenced by these migration functions. The original EF models
and DDL are not in this repository. This checks actual SQL execution against that
fixture; original EF schema compatibility, production PostgreSQL versions,
concurrent transactions and Supabase relay/JWT behavior remain deployment checks.
The simplified `auth.uid()` fixture tests bridge behavior with a local subject,
not token verification. The local relay gate below remains required.

Primary runtime references: [PGlite setup and batch SQL](https://pglite.dev/docs/),
[PGlite PL/pgSQL examples](https://pglite.dev/examples), and
[PGlite source](https://github.com/electric-sql/pglite).

Each of the eight functions has its own strict `deno.json` and integrity lock.
CI checks those graphs with `--frozen-lockfile`; dependency drift fails instead
of updating locks. Supabase client imports use exact version **2.117.2**, published
2026-09-25. Runtime declarations come from the same exact `functions-js` npm
package's shipped `src/edge-runtime.d.ts`, through a type-only reference. Postgres
remains pinned to **3.4.7**. No npm lifecycle scripts are enabled.

To update dependencies, review current upstream releases, change the exact
imports, and run the per-function `deno check` with `--frozen-lockfile=false`
once to regenerate its lock. Review the complete lock diff, rerun the frozen
checks, and include all affected locks in the change.

The Node adapter executes the real `career-report` handler with a local tagged
SQL fixture. It checks opaque reporter authentication before reading a body,
handler status codes, and bounded stream consumption. The shared reader tests
include one-byte UTF-8 fragments, 20,000 empty chunks, exact/oversized ceilings,
cancellation, invalid JSON and interrupted streams. These tests do not exercise
the Supabase relay or a real Postgres connection.

Before releasing the reporter gateway change, run this required local acceptance
gate on a machine with a working Docker daemon and Supabase CLI:

1. Discover the installed CLI's commands and flags with `supabase --help`,
   `supabase start --help` and `supabase functions serve --help`. Start an isolated
   local stack and apply this repository's migrations to its disposable database.
2. Register a test-only reporter in that database's
   `public.project_prime_career_reporters`: use a 32–256 character opaque key,
   store its lowercase SHA-256 digest in `key_hash`, and set `enabled=true`.
   Configure the function's database and service key from that local stack.
3. Serve the checked-in `career-report` through the local relay using this
   repository's `config.toml`. **Do not pass a global JWT-verification bypass**:
   acceptance must prove the function-specific `verify_jwt=false` is applied.
4. Send GET probes through the relay with `Authorization: Bearer <opaque key>`.
   The registered non-JWT key must return 200 with its reporter identity;
   an unknown key and the same reporter after local disabling must return 401.
5. Re-enable the fixture and send a streamed POST larger than 512 KiB; expect
   413. Repeat with the unknown key; expect 401 before body consumption. Preserve
   the relay/function/database logs and destroy the disposable stack afterward.

The initial 2026-10-06 engineering run could not execute that gate: the single daemon
check failed to connect to `~/.docker/run/docker.sock`. Frozen checks passed with
an empty Deno cache, a deliberately incomplete private lock failed, and all ten
original Node tests passed. The cumulative participation implementation expands
the passing Node suite to 18 tests and adds the disposable PostgreSQL acceptance
run described above. Production credentials and deployments were never used.

Primary references checked for this change: [Supabase dependencies](https://supabase.com/docs/guides/functions/dependencies),
[Supabase npm security](https://supabase.com/docs/guides/security/npm-security),
[Node 20 support removal](https://supabase.com/changelog/45715-deprecation-notice-dropping-support-for-node-js-20),
and [Deno dependency management](https://docs.deno.com/runtime/fundamentals/modules/).
The June 30 Node 20 removal is compatible with the CI job's Node 24 runtime.
