# Accepted shot and homing validation audit

Project Prime already records source-frame owner poses in NetShotProof.ObservePose, with life/generation matching, source-clock integrity, normalized ray and aim checks, a 3-unit muzzle origin envelope, and authoritative dynamic-geometry obstruction tracing. Client homing target decisions are generation/life fenced and re-run against the server's eligible-target selector; no client-only target slot decides damage.

The missing lifecycle resolution was the accepted shot path returning NoSpawn after NetTimedShotProof.AcceptedAttempt without calling Complete or Abort. This change explicitly Abort()s a retained attempt when the native spawn returns NoSpawn. This invalidates uncertain source expenditure for subsequent timed certificates and never writes a fictitious successful shot or ammo debit.

No network packet changes, protocol bump or origin-distance threshold change. Do not reimplement source validation based on the wrong ACK frame or add host-favoring target redirection. Recovered packets, high RTT and authority rewind require asset-enabled replay tests.

Still required for full production proof: 2–8 player 45–750 ms jitter/loss, real projectiles with blocked muzzles, no-projectile-slot, homing mismatches, kill/disconnect/new life, historical replay and snapshot restart. Device-free existing tests and CI cannot attest those end-to-end scenarios.