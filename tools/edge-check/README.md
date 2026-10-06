# Edge function reproducibility and acceptance

Run from the repository root with Deno **2.9.7** and Node **24 or later**:

```sh
bash tools/edge-check/check-deno.sh
node --test tools/edge-check/*.test.ts
```

Each of the five functions has its own strict `deno.json` and integrity lock.
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

The 2026-10-06 engineering run could not execute that gate: the single daemon
check failed to connect to `~/.docker/run/docker.sock`. Frozen checks passed with
an empty Deno cache, a deliberately incomplete private lock failed, and all ten
Node tests passed. Production credentials and deployments were never used.

Primary references checked for this change: [Supabase dependencies](https://supabase.com/docs/guides/functions/dependencies),
[Supabase npm security](https://supabase.com/docs/guides/security/npm-security),
[Node 20 support removal](https://supabase.com/changelog/45715-deprecation-notice-dropping-support-for-node-js-20),
and [Deno dependency management](https://docs.deno.com/runtime/fundamentals/modules/).
The June 30 Node 20 removal is compatible with the CI job's Node 24 runtime.
