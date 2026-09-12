# Leaderboard query benchmark

`tools/leaderboard-bench` is the reproducible PostgreSQL plan harness for the
read-only queries in `src/Backend/Matches/CareerQueries.cs`. It creates three
temporary tables on the supplied PostgreSQL connection, seeds deterministic
profiles, licenses, and official career aggregates, runs `ANALYZE`, then
captures `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` for the Ranking Points and
career-kills query shapes. The query text mirrors the provider-generated
semantics that matter for the plan: scores are decimal (`numeric`) casts and
each request takes `limit + 1` rows (26 at the default public limit of 25) so
the application can detect a next cursor before trimming the sentinel row.
The transaction commits only `ON COMMIT DROP`
temporary tables; it does not mutate the application schema or persistent
rows.

Use the existing opt-in connection-file boundary. The file contains a private
connection string and is never printed:

```sh
PRIME_TEST_POSTGRES_FILE=/private/path/prime-postgres-connection.txt \
  dotnet run --project tools/leaderboard-bench/leaderboard-bench.csproj \
  -c Release -- --rows 100000 --iterations 10 --seed 20260911 --metric both
```

`--metric` accepts `rp`, `career`, or `both`. `--rows` is bounded to one
million and `--iterations` to one hundred. The default is 100,000 rows, ten
measured iterations, one warm-up iteration, and seed `20260911`. Output is one
JSON report per run containing the seed, row count, PostgreSQL version,
server-side p50/p95 execution time, top plan node, a plan SHA-256, and the
captured JSON plan. The plan hash includes PostgreSQL's plan details and may
change when the server version, statistics, or schema changes.

The JSON output includes both `Limit` (25) and `QueryTake` (26) so a recorded
run preserves the cursor-sentinel contract. The temporary aggregate indexes reproduce the two currently checked-in
`career_aggregates` indexes (`TrustClass, Dimension, Kills, PlayerId` and the
corresponding `Wins` index). They are measurement fixtures, not a proposal to
add or alter production indexes. The harness deliberately does not benchmark
HTTP serialization, a populated production schema, concurrent writers, or
network latency. Those dimensions require a separate controlled run. A plan
result alone is not sufficient justification for a projection, cache, or index
change; record the workload, PostgreSQL version, plan, and before/after
measurement in the acceptance ledger before making one.

Workload boundary: `--rows` is player/aggregate cardinality (one deterministic
career aggregate per seeded player), not match count. The production queries
read transactional aggregate rows, so match-fact count does not change the
read cardinality represented by these runs. The 1m result is therefore an
aggregate stress result, not the requested 1m-match/10k-player workload.

## Benchmark ledger entry

Copy one row into the project acceptance ledger for each reviewed run:

| Date (UTC) | Seed / rows | Iterations | PostgreSQL | Metric | p50 / p95 ms | Plan SHA-256 | Decision |
| --- | --- | ---: | --- | --- | ---: | --- | --- |
| 2026-09-11 | 20260911 / 10000 | 10 | 17.11 | rp | 2.278 / 2.469 | `cfbe4f...` | Under `<100 ms`; no schema/index change |
| 2026-09-11 | 20260911 / 10000 | 10 | 17.11 | career | 3.066 / 3.365 | `850dd6...` | Under `<100 ms`; no schema/index change |
| 2026-09-11 | 20260911 / 100000 | 10 | 17.11 | rp | 27.815 / 29.010 | `da6ee1...` | Under `<100 ms`; no schema/index change |
| 2026-09-11 | 20260911 / 100000 | 10 | 17.11 | career | 37.595 / 39.383 | `dd26dd...` | Under `<100 ms`; no schema/index change |
| 2026-09-11 | 20260911 / 1000000 | 10 | 17.11 | rp | 392.602 / 414.158 | `7945b9...` | Aggregate stress exceeds target; not the requested 1m-match/10k-player workload; no schema/index change based on the mismatch |
| 2026-09-11 | 20260911 / 1000000 | 10 | 17.11 | career | 520.052 / 647.012 | `2bed75...` | Aggregate stress exceeds target; not the requested 1m-match/10k-player workload; no schema/index change based on the mismatch |

The recorded hashes are shown with the supplied six-character prefixes. The
10k run is the acceptance baseline; the 100k and 1m rows are scale probes.
The 1m aggregate stress result does not justify a production schema or index
change because it does not represent the requested 1m-match/10k-player
workload.

The repository test suite intentionally skips PostgreSQL-only tests unless
`PRIME_TEST_POSTGRES_FILE` is set. CI provisions a temporary connection file
for the Backend PostgreSQL job; local runs should use an isolated disposable
database and the same file boundary.
