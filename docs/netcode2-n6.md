# Netcode 2.0 — N6 continuous weapons

N6 builds on merged N5/main `734b30ef9f7764bff0d9f11fe49086cd41fbaf2f`.
Branch: `codex/net2-06-continuous-weapons`. Live protocol is **46**; mixed
protocol-45 peers must update together. Canonical player state remains 145 bytes;
individual replay and semantic record layouts are unchanged.

## Behavior

Shock Coil payments are owned by the full shooter stream, generation, life and
authored tick, with an exact victim generation/life and body/turret component.
The authority reserves a tick before effects and commits only actual removed
health and actual capped healing. Repeated packets, late native collisions,
rescued claims, changed emitted shot IDs and redirected targets cannot repay it.
The ledger retains 1,024 entries per owner for 1,024 server frames; exhaustion
refuses a new payment. Victim replacement cannot free somebody else's paid tick.

An immutable source journal checks rescued continuous claims against their
authored phase, target, historical pose/form, team, cone, range and wall geometry.
Native playback and rescue share one captured fractional historical world.
Continuous hit-point validation waits through the existing grace window so a
claim arriving before its native firing event cannot receive a contradictory
terminal refusal. Unpaid rescue still has to pass the radius, cone and LOS checks.
The native collision path also requires the selected target's exact life and
component: a pulse selecting no target cannot damage an incidental player. A
continuous pulse keeps one historical ACK timestamp from emission through its
claim, separately from its owner-clock damage phase. Damage strength uses the
authored Prime/double-damage/ramp context. Current health eligibility still
prevents healing a dead, allied, self or current Prime owner, and preserves
immunity, health caps, Balanced Sylux rules and native Weavel health splitting.

An exact reliable WorldReady confirmation now gates local shot authoring.
Applying a bootstrap locally cannot start firing before the authority admits
that client. Countdown and recipient-generation checks still apply; remote
playback continues while the local client waits for confirmation.

All retained continuous events are consumed independently of newer carrier
input, cooldown or target state. An unpaid event closed by owner death/life
retirement cannot later be rescued. An already-paid event keeps its original
outcome through native cleanup. Expiry remains explicit evidence of failure;
ordinary missing shots are not relabelled as successful playback.

Predicted local drain is immediate and keyed to its own tick and claim. Each
owner's fast snapshot carries a 128-bit paid-phase window captured with that
snapshot's health. Only included ticks retire their corresponding local credit;
an accepted ACK alone does not remove healing before authority health arrives.
Rejection and lifecycle changes clear the appropriate credit. Zero-gain tick
tombstones prevent a full-health pulse from healing again after intervening damage.

The personalized fast snapshot remains **1,172 bytes** including its network
header for eight players. Bootstrap omits the optional receipt tail and stays
within 1,200 bytes including its reliable envelope. A separate slow-lane sidecar
carries confirmed owner life, phase, target identity and endpoint for drawing.
It never selects a target or applies damage/healing. Endpoint consumption requires
matching current identities and a recent phase; unrepresentable coordinates use
the existing local visual fallback.

Continuous firing can produce more small semantic facts than the previous
16-packet reliable admission window can carry at high latency. Retained semantic,
award and replay facts now share same-type batches bounded to **1,200 bytes**
including reliable and network headers. Every individual identity and record is
preserved. Backpressure advances no cursor; receivers validate the entire batch
before decoding its individual records. Reliable window, lifetime and retry
limits are unchanged.

The opt-in test shutdown pauses periodic roster/session refreshes only after
every expected live peer reports quiescence in the same match. Simulation,
retransmissions and retained facts continue; the barrier still requires empty
reliable queues, zero claims/projectiles and complete shot/fact frontiers.

## Validation

Execution used this Mac arm64 with .NET 10.0.401/10.0.12. No Windows-specific
change required a Windows run. No Linux/Android testing, GitHub Actions or long
soak was used. Multiplayer controls use real UDP, actual damage/healing and
native deaths/respawns. The opt-in mixed rig equips Sylux clients with Shock Coil
or Imperialist by their assigned slot and replenishes ammunition. Combat Hall
avoids the Data Shrine navigation problem without relocating players or altering
their health.

The final production candidate is `06548969`
(attempt 18; gameplay matches attempt 17). Release build: zero errors, 42
warnings. The focused asset suite passes 163 assertions; snapshot/presentation/
batch checks pass 337. The health/shot suite passes 3,338,739 assertions across
7,776 fault profiles. Python evidence tests pass 119 cases. Lifecycle/lobby
validation passes 7,739 assertions, and bot compatibility passes 197 checks. The tracked ledger records final compatibility checks, campaign totals,
exact commands, binary hashes and the immutable attempts used for each result.

Bootstrap tests also verify missing, stale and exact confirmations across
countdown and re-admission. The timing regression fails against the previous N6
candidate at its intended assertion. Tests cover native/rescue ordering, explicit no-target and incidental
collision refusal, owner death before settlement, rejected/accepted ACK ordering,
receipt bitmap holes, lifecycle changes, caps, immunity, allies, Balanced healing,
split health, historical LOS and repeated retained events. Packet tests cover
backpressure, mixed fact types, malformed batch members, truncation, bounded work,
history overrun, bootstrap and unchanged canonical assembly.

Final real-UDP mixed controls all passed the unchanged campaign adjudicator and
the exact continuous transaction audit:

| Players | Fault profile | Active combat | Paid ticks | Damage / healing | Both audits |
| --- | --- | --- | --- | --- | --- |
| 2 | Lan | 35 s | 127 | 130 / 130 | PASS |
| 4 | Moderate | 40 s | 215 | 219 / 219 | PASS |
| 8 | Extreme | 50 s | 383 | 390 / 390 | PASS |

The moderate profile uses 100 ms RTT, 20 ms jitter and 1% loss. Extreme uses
400 ms RTT, 80 ms jitter, 5% loss, 3% reordering and 1% duplication. Each run
also has an 18-second maximum drain budget. Every run exercised both weapons
and actual deaths/respawns, with no missing shots/facts, duplicate payments,
refused-ACK/payment contradictions, health/score mismatches, remaining prediction
credit, unexpected disconnects or drain failures.

## Evidence and limits

The machine-readable record is
[`net2-n6-20261010.json`](../tools/nettest/baselines/net2-n6-20261010.json).
Raw binaries, logs and all development attempts remain under `artifacts/net2-n6`
and in the local `artifacts/net2-n6-evidence.tar.gz` archive; they are ignored by
Git. The ledger includes their hashes and archive verification.

Earlier attempts exposed real phase-skipped/stranded events, inconsistent claim
clocks, death-rejected ticks subsequently rescued, incidental native targets,
reliable semantic backlog, fractional geometry disagreements and firing before
authority readiness. They are retained as failures. The first two-player
candidate's original aggregate audit passed, but the later exact ACK-to-payment
join correctly fails it on 12 refused-but-paid shots. Both results are retained;
the original PASS is not used as final acceptance. Other failed attempts concern
fixture setup, a tiered-JIT allocation measurement, an invalid test command and
stock-map navigation; the ledger distinguishes these from production failures.

N4 admission remains in shadow mode. This does not certify strict resource proof,
honest-player rejection rates, specialized self-hit/bomb behavior or N7–N9.
Short campaigns establish bounded authority payments, health/score agreement,
complete claim outcomes and retired local credit. They do not measure every
rendered health-bar frame, exhaustive per-tick display agreement across clients,
long-session behavior or GPU endpoint agreement. Exact credit ordering is covered
by deterministic tests. No performance improvement is claimed.
