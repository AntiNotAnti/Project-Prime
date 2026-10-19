# CW-04e evidence — bounded custom launch/payment state machine

Base: `669e08732da378061543d305ed3019ffe20ee995` (latest main after CW-04d).
Tested implementation source: `438042763`; following commit adds evidence/ledger only.
Environment: macOS arm64, .NET SDK 10.0.401; `DN=/Users/jarrett/.dotnet/dotnet`.

Scope: [custom payment contract](../04e-custom-payments.md). Fixed, life-owned storage
pins the full manifest/registry and custom action identity; opaque entry/contact
leases protect reserved and terminal single-component contacts. Actual canonical
health observations determine removal and lethality. The module does not mutate
health, allocate damage event IDs, admit source poses, establish trade, send outcomes
or enable live custom Spawn/damage. CW-04 remains incomplete.

| Check / command | Actual result | Log in `artifacts/custom-weapons/CW-04e/` |
| --- | --- | --- |
| `DOTNET_PROCESSOR_COUNT=2 $DN build src/MphRead -c Release -p:UseSharedCompilation=false` | PASS, zero warnings/errors; final build 112.50 s | `build-final.log` |
| `$DN build tools/custom-weapons-check -c Release -p:BuildProjectReferences=false -p:UseSharedCompilation=false` | PASS, zero warnings/errors; 1.97 s | `tool-final-build.log` |
| Fresh custom check binary without arguments | PASS, 3,291 contract checks, including 1,186 payment checks | `contracts-final.log` |
| Same binary `--scene <isolated CW-03 content-data>` | PASS, 46 real offline rifle checks | `scene-final.log` |
| Same binary `--rewind-scene <isolated CW-03 content-data>` | PASS, 155 scope/isolation checks | `rewind-boundary-final.log` |
| Native table snapshot against unchanged CW-00 | PASS | `native-tables.log` |
| Fresh nettest build and architecture, protocol58-replay, shot-source-journal, weapon-policy, shot-inputs, shot-lifecycle, timed-shot-damage, instagib-ion-fracture | PASS each | Named logs and `native-results.json` |
| Nettest `--timed-shot-accepted <content-data>` | PASS, 343 assertions | `native-accepted-scene-final.log` |
| Nettest `--timed-imperialist-impact-scene <content-data>` | PASS, 949 assertions | `native-imperialist-scene-final.log` |
| Fidelity oracle, all, unchanged baselines, content allowed, 60 Hz | 46 PASS, one unchanged baseline divergence; exit 1 | `native-fidelity-final.log` |
| `git diff --check` | PASS | No whitespace errors |

The new checks supply consistent certificates, real resource-state one-round
transitions and declared health observations. They exercise every owner/action/
victim/component/channel join; source object identity; protected-zero consumption;
reentry; contact during emission; no-spawn/refund boundaries; body/turret/split/lethal
results; invalid completion; capacity saturation; permanent replay fences; exact
retention boundaries; uint wrap; stale/default/foreign leases; independent stores,
old-life admitted flight and retirement. These are pure store checks, not engine
collision, actual multiplayer HP or authenticated transport evidence.

Review caught two pending-emitter cases: a terminal catch-up outcome must still time
out/quarantine an uncertain emitter; and a 600-tick projectile can outlive 256-tick
receipt retention. Eviction now quarantines any still-pending emission before freeing
the entry. Paid and protected-zero regressions cover both cases. The long-lifetime
check was run against the actual preceding game build and failed at the precise
new assertion (`before-eviction-fix.log`, exit 134); the final rebuild/check passes.
No fixture or tolerance was relaxed. Earlier isolated draft checks used copied game
DLLs and were provisional, not substituted for the final game build.

Measured default 128-entry store construction: **33,776 allocated bytes** with frozen
registry/manifest prebuilt. Four separate warmed 100,000-iteration batches—duplicate
contacts, exact lookup, mismatched-victim lookup and full-capacity preflight—each
allocate **zero bytes**, with the retained terminal in the final slot. Accepted custom
source construction remains 336 bytes; rejected full-capacity registration does not
construct a source. There is no native-only store allocation because there is no live
caller. Whole-scene store retention/lifecycle budgeting still needs the authority
container and is not represented by this per-store measurement.

Native fidelity retains only the existing missile splash divergence at tick 177,
`victim.position4096.z`, expected 52814, actual 52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Native tables, protocol, packet and replay layouts are unchanged. CI/review are not
awaited or reported PASS under the user's instruction.

NOT_RUN / remaining CW-04: real custom authority source/history recovery, mandatory
native/custom death/trade ordering, canonical HP/payment integration and damage-event
allocation, whole-scene lifecycle/capacity ownership, reliable typed outcomes,
authenticated custom UDP/readiness, snapshot/reconnect/spectator state and custom
latency/loss/reorder acceptance. Custom replay and physical/rendered platform testing
remain later release gates. Rollback: module has no live caller and CW-04d's live
custom damage refusal remains in place. Next: connect the authority path and validate
these gates before CW-05.
