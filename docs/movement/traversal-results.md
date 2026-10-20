# Traversal first-slice results — 2026-10-20

This slice adds the optional Traversal profile, earned ramp jumps, Magmaul self-blast jumps, tagged training-map surfing and the speed/strafe display. Classic remains the default and Quake III v2 is unchanged. Crouch/power sliding is the next slice.

Integrated latest main `da443dc9e86c7bb51402ce0024797379497f1d10`, including source-geometry PR #824. Compilation and fan triangulation retain both source UV metadata and surf tags. No runtime dependency or third-party implementation was added.

## Verification

Windows x64, .NET 10.0.401, normal Release build with `MphReadReplayPreviewFailureFixture=false`: zero warnings/errors. The Mac performed file/static checks only. [Sanitized evidence and source hashes](evidence/traversal-selection.json) distinguish regression snapshot 03 from final snapshot 04; the latter adds only two corrected error messages and server documentation.

| Check | Result |
| --- | --- |
| Regression routes | 18/18 passed |
| Traversal actual input/collision, ramps, surf and HUD | 82,331 assertions |
| Authority AI, all seven Hunters at difficulties 0/3 | 273,310 assertions, 14 scenes |
| Classic movement golden | 56 trajectories, unchanged tolerance 0.00001 |
| Quake III ordinary / advanced scenes | 24,169 / 171,054 assertions |
| Native / Quake III / Traversal self-splash arbitration | 665 / 668 / 671 assertions |
| Map surface authoring / integrated source geometry | 16 / 2,126 checks |
| Offline and live menu CPU fixtures | Passed, four-choice cycle and saved drafts |
| Replay compatibility, metadata, lifecycle checkpoint, architecture | Passed |

The Magmaul checks use actual charged-fire hold/release admission, native floor explosions, authority-derived damage/impulse and immutable repeat receipts. They include rising/falling bodies, forged claims and unchanged Classic/Quake III Magmaul behavior. These are admitted ServerSim/packet-seam checks, not live grenade-combat qualification.

Final snapshot 04 also passed a 40-second real UDP run with two independent scripted human-owner processes, dedicated authority and a native AI bot. Owners earned 13/20 held hops and 17/30 legal jumps. The resulting protocol-68 Traversal-v1 archive contains 2,400 frames; passive replay passed 87,551 assertions with three players and nine seeks. Its SHA-256 is `0c81189d5793974af3a736fed70c2f2a5ff620fa08653ede9a905d2f00bfc968`. Genuine protocol-61 Quake III-v1 and protocol-62 Quake III-v2 archives each passed 80,101 checks without changing archive bytes.

The online fixture retained 2/5 native displacement diagnostic rows. Its 0.25-unit trigger is deliberately not a movement acceptance threshold; the recorded scratch collision candidates are not proof of the resolved response. Those rows remain available for later native-collision investigation and are not presented as zero-anomaly evidence.

The warmed 100,000-step math benchmark allocated zero managed bytes (snapshot 03: 12.993 ms). This is a narrow timing observation, not a gameplay frame-time budget pass. The accepted performance tradeoff and later optimization remain unchanged.

## Repairs, CI and limits

Initial failures are retained in local evidence: the charged Magmaul fixture lacked verified hold/release history; open slope polygons made spawn validation see unbounded solids; a menu fixture assumed three choices; an optional geometry build exhausted Windows disk. Each was corrected and rerun. No authority, collision, golden or timing gate was weakened. Disk cleanup removed only reproducible outputs from owned validation directories.

Asset publication guard and whitespace checks pass. Hosted CI is observed after publication; this PR is merged without waiting for CI or review, as requested. No physical-device, rendered UI, competitive-balance, macOS execution or whole-frame performance acceptance is claimed. Remaining work in this request is crouch/power sliding with consistent collision, visible pose, source-time origins, network state and replay restoration.
