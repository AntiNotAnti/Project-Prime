# N12 movement validation shadow

N12 shadow validation is **verified within the Mac scope below and ready for PR review and merge**. Final source is `166e96fd8ba44e8931afc0776ceb7fb0dba7475e`; immutable attempt-09 engine SHA-256 is `7c0b95b4d991797207d84d089799bf2e8c446e3990bd2d58a620f200575b2f1e`. Protocol 50 is unchanged. Optional server-derived movement authority remains deferred and disabled because independent native-equivalent collision/alt/platform parity is incomplete.

This milestone adds opt-in observations of accepted remote owner positions. It does not introduce server movement authority, packet rejection, position correction, or an independent collision reference. Hard rejection and correction remain disabled.

## Enablement and evidence

`PRIME_MOVEMENT_VALIDATION_SHADOW=1` enables the observer; the default is off. The campaign runner's `--movement-validation-shadow` flag sets the environment explicitly, records it for each process in the manifest, and records the flag in settings. Inherited experimental environment flags are cleared by the runner.

Campaign evidence includes `MovementValidation`: per-hunter/form counters, at most 128 anomaly rows, overflow and invalid-input counts, and `HardRejections=0`. Counters distinguish baselines, evaluated biped samples, exemptions, unsupported samples, would-refuse observations, duplicates/old frames and long gaps. A would-refuse observation is a broad-envelope signal, not a cheat verdict.

## Coverage audit

| Path | Supported observations | Limits |
| --- | --- | --- |
| New network observer, all seven playable hunters | Biped position deltas, broad horizontal/upward envelopes, acceleration over short source-frame intervals, movement-input presence | Heuristic observations; no independently simulated trajectory or collision |
| Stable alt form, all hunters | Delta measurements and unsupported counts | No biped refusal signals; no alt physics or Stinglarva equivalence claim |
| Existing Samus/Spire native reference, stable biped ground | Horizontal impulse/cap/damping | Production vertical, facing and contact values are substituted |
| Existing Samus/Spire native reference, collision-free air | Kinematic integration and gravity | Production facing is substituted; contact transitions excluded |
| Existing native reference, other hunters, alt forms and special contacts | No demonstrated reference equivalence | Collision, climbing, morphs, jump pads, platforms and knockback remain outside the proven kernel |

The existing reference is in `PlayerMovementShadow.cs`, `Mods/Diagnostics/NativeMovementReference.cs` and `MovementShadowRuntime.cs`. It must not be presented as all-hunter native movement parity.

## Accepted-owner boundary and lifecycle

The network observer samples only poses that pass the existing owner-position admission boundary. Replica, local/bot, dead/unready, invalid, stale or unapplied positions do not become observed motion. Frame ordering and elapsed time use owner source frames, including unsigned wrap. Duplicate/old samples leave history unchanged. Long gaps establish a baseline rather than treating delayed travel as an instantaneous teleport.

History is fenced by authority epoch, match, slot generation, life and hunter. Lifecycle retirement and session/match changes clear history. History resets preserve campaign aggregates; the explicit full diagnostic reset clears them. This distinction keeps teardown from erasing the evidence being exported.

## Exemptions and limitations

Teleport, platform/contact, jump pad, impulse/damage, form transition, Spire climbing, lateral collision and Prime state are conservatively exempted. The authority's current native state supplies these exemptions. It is not historically aligned to the source frame of a delayed owner pose: an exemption may have expired before that pose arrives, or may cover a pose predating the effect. Damage invulnerability is broader than a proven knockback interval; standing on a collision object does not necessarily mean the object moved. These limitations require honest evaluated/exempt/unsupported denominators and prohibit enforcement conclusions.

Native 30/60 Hz constants contribute deliberately broad envelopes. They do not prove exact legitimate maximum speed, acceleration or jump height. Frame gaps suppress short-interval acceleration comparisons; missing input while moving is an observation and does not imply invalid movement.

## Validation ledger

The original shadow implementation at `ed4a75b89b904e0cab0af6c71711b98ff2daf7f4`, engine SHA-256 `462eedc383d86ebd5a6c0eb6568a9534b492c759647f3b739ff1ebc3623e5eec`, passed focused shadow lifecycle/source-time/exemption/bounded-observation tests, including the room-reset aggregate regression. The standalone analyzer passed 16 adversarial tests; all 319 Python tests initially passed (existing 303 retained). Final role/quiescence regressions bring the durable final suite to 325 passing tests. The analyzer requires strict base/drain success, exact enabled state, complete peer evidence, zero invalid/overflow/hard rejection counters, nonvacuous authority evaluation and zero client/off measurements. Advisory would-refuse rows must match bounded per-hunter counts; they need not be zero.

The all-seven-hunter asset-backed native tour passed 4,249 actual fixed simulation steps: 2,352 evaluated biped samples (336 per hunter with nonzero measured horizontal movement), 1,110 unsupported alt samples, 780 exempt samples and seven baselines. It reported zero invalid/overflow/would-refuse observations. Enabled and disabled observation preserved before/after native state fingerprints. This is actual local body input through Host/local-slot0 in a ServerSim-owned scene with its headless Roam camera. It is not dedicated server movement-authority equivalence or HUD/camera input parity. Pads, moving platforms, teleport, external knockback, grapple and Spire wall climb were not exercised by that tour.

That original engine separately passed the 278-check native discontinuity endpoint fixture, covering actual teleport/pad/directed damage/form through snapshot, rewind history and observer smoothing. Those endpoints do not substitute for all-hunter collision equivalence. Final test-only tour source is `26d46d62d97011c2d156191aa1775645120a42c2`; test SHA-256 is `a79b4c6e451e83c6c40f6ba3ae7017ad191b28c47557c4602a2b2873481cac0f`, with the production engine unchanged. Attempts 01–04 retain failed fixtures for absent movement, GL binding, post-spawn input state and missing hunter activation; corrected fixtures are recorded independently.

Final Mac three-client native alt-morph campaigns passed all strict base, form and standalone movement audits: LAN shadow-off control and extreme shadow-on, each 20 seconds of action plus a bounded 20-second drain. Every process exited zero, with spectator/drain coverage and no incomplete findings. The off control recorded zero movement measurements on all processes. Extreme authority observed 693 evaluated biped, 329 exempt and 99 unsupported alt samples, four baselines and 1,172 duplicate/old samples, with zero invalid/overflow/would-refuse observations. Actual UDP measured hunter coverage is Samus and Weavel; clients recorded no authority measurements. Independent form anchors had 215 LAN and 148 extreme witnesses. Both reported zero simulation exceptions, two dropped steps and one local stall, so no performance-clean claim is made.

The final engine separately passed the shadow unit checks, 655 clear native-quiescence checks and 1,032 blocked-pose checks. Its exact quiescence helper escaped the recorded blocked pose in 107 native ticks using ordinary Weavel cardinal controls, stopped movement at native eligibility and retried ordinary morph input. This is opt-in workload shutdown, not server movement correction or a forced pose/form change. The older all-seven tour was not repeated on attempt-09: the shadow core is unchanged, while subsequent changes added sample-time form role evidence and scripted native quiescence. Its old source/engine remains separately bound in the ledger.

Retained failures include the original LAN form ambiguity (fixed by explicit sample-time LocalSlot/Spectating/AuthoritySpectating evidence, never a blanket owner-phase exemption), attempt-06/08 live drain failures, attempt-07 compile failure and blocked-pose fixture failures. The drain bound and gameplay audit were not weakened. An isolated passing quiescence fixture was not used to excuse failed live campaigns; final attempt-09 revalidated actual shutdown. Early storage-wrapper setup failures started no processes; the final wrapper preserves private managed binaries and identical native dependency links with recorded hashes.

The [hashed ledger](../tools/nettest/baselines/net2-n12-20261011.json) records exact sources, commands, manifests, denominators and retained failures. No Windows, soak, hard rejection, correction stability, all-map or fully server-derived movement claim is made.

Full server-derived movement is distinct future work. It depends on independently proven fixed-step collision, alt-form, knockback and platform equivalence, followed by legitimate-play and correction-stability evidence. This shadow milestone does not satisfy those prerequisites.
