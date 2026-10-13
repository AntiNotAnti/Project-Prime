# Turret source-life fixture correction

The live turret fixture expected a deliberately zeroed source life to enter authority playback. That expectation predates the authority outcome contract now on main. The corrected fixture expects early rejection and verifies no pending source, retired-source tombstone, queued outcome, published outcome, or flight is created, including after native retirement. Valid-life pending/expired/retired claims, native death/respawn, genuinely committed old-flight collision/physical payment, and idempotent ACK controls remain.

No production code, authority policy, protocol layout, replay producer, or gameplay behavior changes. Current source still declares wire53. This does not prove how every malformed hit claim is settled: the obsolete zero-life live-admission/retirement premise is replaced by explicit admission rejection, while valid-life claim controls remain. Historical replay interpretation is unchanged.

## Source evidence

- Base: `91b1c0c6c44e8c90effd250729709f791a84765e` (main); fixture commit: `38a12ac943439e270caf3887b74ae17439f8b2a1`.
- Earlier test: `8c7c6515f958c86e751692e50a4a37baed13e06f` (2026-10-11 07:16), added `Offer(zeroSourceLife:true)` and expected current-life retirement.
- Later contract: `a0a120b5afa344d2ed6ef79c6be8a01edb56fd65` (2026-10-11 20:36), added explicit TurretFire zero SourceLife rejection before authority observations/queueing in NetFireEvents.Accept. TurretFireOutcome.Valid requires nonzero Key.LifeId and exact Fire.SourceLife equality. Native authoring supplies the actual lifecycle life.
- The unchanged suite also failed at Offer line326, invoked by zero-life case401, on frozen combined source `edcf0f1bc6be7cfb0734c42ebaa41ce8b8724ef2` and on the independent nullable-capture candidate. This fixture correction resolves that stale test dependency for #517; those production captures do not cause the failure.

## Paired validation and metrics

SDK10.0.401/runtime10.0.12 on macOS. Unchanged and corrected nettest suites were compiled separately against the same immutable engineering-baseline engine SHA256 `22c7eb0fb0e59c7e40fa656c4bda1c20a58c42b5628e3fdd5f47fb79fb6ff0b2` and dependency set, eliminating runtime delta from the fixture comparison. Baseline fixture passed81 assertions then failed exactly326→401 (exit1, preserved); candidate passed127 with evidence off and on (exit0). Fixed-engine nettest hashes: baseline `d58cb390a60b1f9bbf8ace9ba0de56c128dd9d529e64cac3309b929fd1346458`, candidate `5ee5ef8cfea33ea46ba86f1cdd4314dcab767a347ac9cce1b080ebcbee9a7dc3`.

The existing asset-free `--turret-fire-outcome-codec` suite passed245 baseline and247 candidate assertions, including bounded reliable delivery and outcome replay producer/checkpoint/dedup behavior. New negative cases reject zero Fire.SourceLife and matching zero key/source life. The case already runs in build.yml's portable nettest job; no new retail-asset CI case or workflow is introduced. Additional fixed-engine passes: actual native-outcome scene25, form protocol31, replay form43, replay format3034, protocol18 lane budget/roundtrip/decode allocation/1172B worst-case datagram.

Normal repository `dotnet build tools/nettest/nettest.csproj -c Release` with isolated ArtifactsPath also passed,31 existing warnings/0 errors. This recompiles the main-based production source unchanged; engine SHA256 `0e07ab3ede846931b4700020fd87c4528b2fdf6291aa756115a8a91173ac34b6`, nettest `a43a34c1d6623e844ba5f472acce44e936af098aad033cc3986860bcaa0e172f`. Repeated against these normal project outputs: turret127 both evidence states, codec247, native outcome25, replay format3034; all exits0. The old main-based warnings remain for their independent fixes; none is suppressed here.

## Reproduction and evidence

Use normal Release outputs: `dotnet <nettest.dll> --turret-events <fresh isolated data directory> "MP1 SANCTORUS"`; repeat in a new process with PRIME_NET2_EVIDENCE pointing to a fresh output path. The data directory contains only paths.txt for legally extracted AMHE1 assets. Run `--turret-fire-outcome-codec`, `--turret-outcome-scene <data> "MP1 SANCTORUS"`, `--player-form-protocol`, `--replay-form-history`, and client `-replayformatcheck` as recorded.

Local artifacts: `artifacts/final-completion-pass-20261013/turret-source-life/`. Full commands/environment/binary/log hashes and output counts are in checks-receipt.json (SHA256 `57880bb307af39cf9c4de85fecb50e4b01a0f0bb6ebc12a0652ba596da2ea1f1`) and main-checks-receipt.json (`3427ae422b934c014acce1024a8cc021772bb92180be6150b090780360b4a446`); normal build log `7f093e1083313a0d9c8e8d7e7aa4411d175679c0b84d08b97ad7ae8c639b5ae1`. identity.json preserves source/binary identities; runners and custom fixed-engine build definition are saved. No retail assets/preferences are committed or distributed.

## Scope and rollback

Asset-backed tests are headless with synthetic local worlds. No native windows, rendered projectile agreement, physical audio, Android, live dedicated-server peer campaign, or WAN impairment acceptance is claimed. This is a fixture contract correction, not full consolidation performance acceptance. Reverting the two test-file changes restores the stale failure; runtime/wire/replay semantics remain identical. No dependency on nullable #517 is needed to run this correction; #517's previous turret failure is resolved when the corrected fixture is included. No merge performed.
