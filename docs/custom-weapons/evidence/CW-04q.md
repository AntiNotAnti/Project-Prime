# CW-04q evidence — authority acceptance closeout

Base SHA: `f7a901fd36470cd447d1d7e79bb1745b81617207` (latest main, including PR818).
Head SHA: the CW-04q slice commit; documentation accompanies the validated source.
Scope: acceptance fixtures and probe/source-frame helpers, CLI dispatch and the
[acceptance contract](../04q-authority-acceptance.md). No production combat,
native resource, packet shape, texture or replay behavior changes.

Environment: macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`;
actual-asset checks use the private extracted-data fixture under CW-03/content-data.
Main command: `/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release -p:UseSharedCompilation=false`.
Result: PASS, 82.11s, zero warnings/errors (`CW-04q/main-final-build.log`).
Final game DLL SHA256:
`588259862b83deb8229f6764747a08339388a8c814ed5480c7ad9f3f9ca0704f`.
Fresh tool copies must match that hash; preflight DLL7d86864d… evidence is retained
separately and is not substituted for final runs.

Final commands, environments, elapsed times, exits, game/copied DLL hashes before
and after runs, and complete logs live under `artifacts/custom-weapons/CW-04q/`.
Structured result files are listed below with final suite results.

| Final check | Result | Structured metadata |
| --- | --- | --- |
| Fresh nettest / custom tool builds | PASS3.434s /2.009s, zero warnings/errors | q-final-udp-results.json /q-final-custom-results.json |
| Custom pure contracts | PASS10586 | q-final-custom-results.json |
| Actual client health/death projection | PASS145 | same |
| Seven existing custom engine scenes | PASS173/127/249/318/985/46/156 | same |
| New authority acceptance / spectator reconnect UDP | PASS1603/371 | q-final-udp-results.json |
| Prior P/M/L/K/I/J and actual client runtime UDP | PASS650/713/1040/1546/134/92/67 | same |
| Sixteen native/protocol modes and static registry | All PASS, protocol67 and unchanged old IDs | q-final-registry-results.json |
| Native tables and twelve combat modes | All PASS | q-final-native-results.json |
| Native accepted / Imperialist scenes | PASS343/949 | same |
| Fidelity | 46PASS, known single divergence, exit1 | same; q-final-native-fidelity.log |
| Native lobby / waitlist | PASS7750/309,99.778s/55.805s | q-final-udp-results.json |
| Texture-import synthetic pure suite, fresh tool | PASS, including new137 texture-bank/128 material checks | q-final-external-results.json |

The final owner-symmetry impairment case dropped313 actual client datagrams;
each slot paid one round and dealt65HP, with no native history alias. These
fixture totals measure the configured run, not WAN or latency fairness percentiles.
FireSource10 established FireReady85; ReloadSource16 established deadline154;
the next accepted post-reload Fire used genuine source162. The acceptance fixture
does not move that deadline with server wall time.

Baseline comparison: native wire version remains67; no native ID, damage table,
serialization fixture or tolerance was changed. Native fidelity's existing
missile-splash tick177 position4096.z divergence (52814/52815) remains a release
limitation, not a passing full fidelity suite. Physical Android, rendered views,
audio, WAN and latency percentiles are NOT_RUN.

The actual UDP suite covers terminal aim/muzzle denial and corrected-nonce refusal,
source-driven cooldown/reload, low/impaired owner/non-owner symmetry, genuine
spectator reconnect with old ACK replay over the replacement authenticated socket,
and independent outstanding owner payment retention. Client projection is an
asset-backed snapshot decoder/live-bridge fixture with an explicit role handoff;
it is not another authenticated client socket. See the contract for exact seams.

Regressions found: no production regression identified by the completed local
checks. Fixture source allocation now advances above both consumed source and
authority world after regular-input heartbeat helpers. Health assertions remain
strict; heartbeat inputs neither mutate HP nor advance clocks directly.

CI job/status: not awaited or claimed passing, as requested. Review not awaited.
Rollback: revert this acceptance-only slice; custom readiness/combat remain off by
default. Next dependency gate: CW-05 canonical request production and player controls.
Custom bot admission, public package readiness and custom replay remain CW-12.
