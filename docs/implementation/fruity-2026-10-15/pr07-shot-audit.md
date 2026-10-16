# PR-07: shot validation audit and conservative normalization guard

Comparison: upstream Fruity #145/#151, Project Prime main dfa987b1c55f57ea86253cd5010b5ab0879828d9 (protocol 56).

**Implemented here:** `NetFireEvents.Validate` already rejects zero-length/nonfinite rays, nonfinite/out-of-range origins, malformed targets/reticles and illegal event identifiers. It did not bound the **upper** length of a finite `Direction`, `Aim` or `View`. New events authored by `NetFireEvents` are normalized to length 1 before transmission. Reject vectors longer than length 2, retaining a 2x margin to avoid unnecessary compatibility risk. Packet size, layout, replay and authority checks are unchanged. Headless `nettest --shot-pose-bounds` verifies valid, invalid and legacy no-pose events. CI runs it.

**Not implemented / separate gate:** no hard historical shooter/muzzle offset rejection. `NetUnlagged.PositionAt` provides a generation/life-fenced snapshot but is at the **ACK** history frame, not necessarily the shooter's physical fire frame. A simple distance to that snapshot could reject legitimate high-speed movement at high RTT and favor the host. Further shadow-telemetry measurements must establish a defensible threshold and actual fire-time position proof before enforcement.

Also unchanged: `NetShotInputProof`, `NetShotSourceJournal`, life/epoch fencing, `NetTargetIdentity`, native homing and Sylux muzzle policies, and `NetShotDiagnostics`. Do not reimplement them.

## Further acceptance

- Verify all seven Hunters, missiles, homing and Shock Coil through native 2–8 player tests.
- Include 45–750 ms RTT/loss/jitter and old demo playback to ensure no false refusals.
- Verify no NoSpawn ledger leakage in timed shot/turret cancellation tests before adding new cleanup.
- No protocol bump without a dedicated migration proposal.
