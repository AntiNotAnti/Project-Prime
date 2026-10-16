# Replay shot event payload validation

Production source: `ReplayTimeline/ReplayRecorder.cs`, `NetFireEvents.WithoutTurretOffers`, `CompactIntentCodec`, `IntentPacket.TouchCanonicalSize`.

Project Prime does not share the native C++ demo truncated-wire problem. Its canonical recorder uses the complete decoded `IntentPacket.TouchCanonicalSize` and retains accepted FireEvents, origin, aim, reticle and lifecycle identity. This PR adds test-only proof covering eight weapons across local and remote source slots, idle carriers, historical interleaved carriers via `CompactIntentReplayTests`, and the current bomb protocol fixture.

The recorder intentionally strips owner-authored turret offers because authoritative turret outcomes have a separate canonical fact channel; that is not a missing packet bug.

**Limits:** Source/codec parity is device-free; no asset-loaded 2–8 player Studio export, killcam, replay seek or historical archived demo device test is performed by this fixture. Existing replay and export CI remains mandatory. No replay format or gameplay code changes are justified by current evidence.
