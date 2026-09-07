# G1–G5 integration evidence

This records the final working-tree integration of the supplied plan. It does not
replace the frozen G1 baseline or establish rendered, physical-device, deployed
Backend, or external WAN acceptance. Protocol 8 remains unreleased.

## Environment and reproduction

- .NET SDK 10.0.400; Release configuration.
- Extracted user-owned AMHE1 multiplayer content; no ROM assets are committed.
- Build with `dotnet build Game.sln -c Release --artifacts-path <artifacts>`.
- Main tests: `GAME_DATA_DIRECTORY=<AMHE1> dotnet test tests/Tests/Tests.csproj -c Release`.
- Backend tests: `PRIME_TEST_POSTGRES_FILE=<private connection file> dotnet test tests/Backend.Tests/Backend.Tests.csproj -c Release`.
- Imaging: `dotnet test tests/Imaging/Imaging.Tests.csproj -c Release`.
- Python: `DOTNET=<SDK executable> python3 -m unittest discover -s tools/tests`.
- Boundaries: `python3 tools/check-project-boundaries.py`.
- Android managed Release: explicit `RuntimeIdentifier=android-arm64`, Android SDK 36,
  JDK 21, and `RunAOTCompilation=false`. This is not an APK/AOT or device test.

Backend integration used an isolated PostgreSQL 18.6 cluster bound to loopback,
with private temporary credentials. It exercised actual migrations, HTTP ticket
issuance and UDP admission, raw ledger concurrency/rollback/rebuild, and the actual
server outbox → HTTP → committed PostgreSQL receipt → spool removal path.

## Regression results

The final main suite passed **954/954**, Backend **43/43**, Imaging **18/18**, and
Python **58/58**, with no skipped tests. Long-run results follow separately.
The solution, dedicated server publish and Android managed Release built successfully.
The project boundary guard found zero violations. A redacted Gitleaks scan of the
changed source/doc/test files found no leaks; LICENSE and maps were excluded.

All twelve multiplayer world modes pass real-content capture/replica checks.
Lifecycle, match phases, overtime, simulation ordering, projectile catch-up/homing,
weapon policy, bomb pooling, shared locks, and history boundaries pass.
The history fixture now reconnects with retained same-endpoint connection proof;
a fresh guest cannot claim the reserved identity. The weapon fixture journals a
processed desired-weapon request while UpDown blocks it, then explicitly completes
the headless animation and verifies the actual switch and previous-weapon state.

## Production spectator / replay soak

A 300-second run used eight human UDP clients plus sixteen separate observers,
three-second spectator delay, two rotating AMHE1 rooms, automatic replay and
telemetry. It passed through 18 match epochs with no transport queue drops or send
errors. Maximum observed observer age was 192 ticks / 3.2 seconds. The small extra
age is the bounded snapshot cadence; no live fallback was used.

All 18 format-3 replay files were seekable, had indexes, contained readable records,
and required no recovered tail: 33,324 records and 89 index entries in total.
Telemetry produced 18 gzip files and 1,008 events. All four analysis commands also
processed a completed production export into SVG/JSON/CSV. These neutral-input
matches prove lifecycle/export behavior, not combat effectiveness or balance.

Observed queue depth peaked at three packets for players and six for observers.
At one memory sample per second (304 samples), server working set was 108,625,920
bytes initially, 160,874,496 at the end, and 170,983,424 at peak. This is a bounded
five-minute observation, not a proof of lifetime memory stability. Private memory
was unavailable on this host, and this harness exposes no tick p95 statistic.

Raw report: `/private/tmp/codex-re-prime-g5/observer-reliable-300.json`.
This repeat uses the fixed reliable-event history. Exact assembly hashes are in
`/private/tmp/codex-re-prime-g5/reliable-binaries.json`.
Reproduce with `nettest --observer-soak <AMHE1> 300 <report.json>` after building;
run without concurrent builds. Dynamic bot handoff and mixed-team behavior have
separate actual UDP/AMHE1 tests; the full human roster leaves no bot slots here.

## Mixed-combat follow-up

The first attempted 300-second ON run at 100 ms RTT, ±20 ms jitter and 2% loss
aborted early. The server recorded `reason=IdSpan`, nine pending events, a pending
high-water mark of twelve and an oldest span of 33. This reproduced a real
reliable-delivery limit, rather than pending-message capacity exhaustion. The
client timed out; the old Python wrapper then timed out waiting for the server,
so that attempt has no completed measurement report and OFF was not started.
Raw logs remain under `/private/tmp/codex-re-prime-g5/mixed-final/on/`.

The fix separates a fixed 16,384-bit reliable-event deduplication ring (2 KiB per
connection) from the unchanged 32-bit packet ACK history. Pending payload capacity
remains 32. The event history covers 30 seconds at the nominal eight-send-per-tick
budget, and a matching sender span bound still prevents unbounded selective loss.
All 57 focused reliability checks and the full 954-test suite pass, including lost
events, lost ACKs, exactly-once delivery, wrap and the inclusive capacity edge.
Oldest-event type/ID/attempt count/age diagnostics contain no payload or credentials.
The runner now retains incomplete failures and continues requested modes; four
mocked runner regressions pass within the full 58-test Python suite.

Both corrected 300-second runs **passed** under the same 100 ms RTT, ±20 ms jitter
and 2% loss configuration, without concurrent builds or other test suites:

| Compensation | Tick p95 | Tick p99 | Maximum tick | Minimum peers | Server/client dropped ticks | Reliable overflow / transport drops |
|---|---:|---:|---:|---:|---:|---:|
| ON | 1.8004 ms | 2.4557 ms | 11.0644 ms | 8 | 0 / 0 | 0 / 0 |
| OFF | 1.6220 ms | 2.4021 ms | 11.1738 ms | 8 | 0 / 0 | 0 / 0 |

These are real UDP/headless measurements with fixed loadouts and infinite test
ammunition, not rendered gameplay or an external WAN test. The runs have different
combat outcomes and shot counts, so their cost difference is not a controlled
causal estimate of compensation overhead. Raw summaries/log paths and exit codes
are preserved in `/private/tmp/codex-re-prime-g5/mixed-reliable-final/summary.json`.
Reproduce using `tools/run-mixed-combat-soak.py --dotnet <SDK> --nettest <nettest.dll>
--data <AMHE1> --output <new-directory> --seconds 300 --rtt 100 --jitter 20 --loss 2
--modes on off`.

## Limits and retained failures

The earlier full-suite runs caught stale protocol/capacity fixtures and an unfinished
weapon fixture. They are retained in the work logs; they are not reported as passes.
The original long mixed-combat failures remain recorded in G1_NETWORK_BASELINE.md.
New controlled runs must be interpreted independently of those frozen observations.

- Ranking Points, original star progression and the proposed rating/forfeit policy
  await approval of G4_RANKING_SPEC.md; the Backend reports policy pending.
- Real 60/120/144/165/240 Hz render traces, visual/audio quality, and physical Android
  touch/HUD/radar/spectator/replay acceptance remain open. The macOS compatibility
  OpenGL probe failed before gameplay; managed tests cannot replace these checks.
- Public TLS/SMTP deployment, external WAN traffic and production operations are not
  established by loopback UDP, an impairment proxy or disposable PostgreSQL.
- The long spectator/recording run and impaired-combat runs are separate workloads.
  A combined release soak with dynamic bots and a Backend outage alongside all
  observer/recording features remains open; focused tests cover the individual
  outbox recovery, admission, bot handoff and gate behavior.
- Tmds.DBus.Protocol 0.21.2 still produces the existing NU1903 dependency advisory.
- No weapon, Hunter health/speed, charge or item-timing balance tuning was introduced.
