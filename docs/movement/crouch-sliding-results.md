# Crouch and power-slide results — 2026-10-20

This slice completes the advanced movement request with Traversal crouch and earned power sliding. The preceding [Traversal slice](traversal-results.md) supplied ramp jumps, Magmaul jumps, stronger optional air control, tagged training-map surfing and speed/strafe-efficiency feedback. Classic remains the default; Quake III v2 and native abilities/forms retain their existing rules.

Integrated main `6366ce762a692a3b2bcf0bf61e3920c095f15b78`, including the custom native-fire isolation and credits timeline changes. No dependencies or third-party movement/animation code were added.

## Behavior and authority

Left Ctrl is the rebindable keyboard action. Gamepad Crouch is configurable and initially unbound to preserve existing mappings. The feet and radius remain native while the accepted height and eye lower to 65%; ordinary crouch walk uses half desired speed. A fresh grounded crouch above 110% running speed starts 48 fixed ticks of low friction, with no bonus speed, ground acceleration or steering. Holding cannot refresh the window. Release, a jump, airborne movement, a lateral wall or native ability ownership ends sliding. The player remains low under an obstruction until standing fits.

Clearance combines fixed-capacity sphere probes, a continuous sweep and native full-cylinder finite-edge checks, including moving geometry. Native Hunter presentation uses an original procedural two-bone pose with planted animated feet. All seven Hunters and both model LODs are covered by actual animation checks. Collision, camera, pickups, target/head bounds and historical traces consume the same accepted stance. Retained custom weapon facts carry the source-time stance, so delivery-time posture cannot move a shot's origin.

Protocol 69 and Traversal profile v2 use reserved intent/snapshot bits without enlarging native packets. The authority independently derives crouch and blocked release; raw input identity remains immutable. Owners retain newer predicted stance across delayed snapshots; fresh lives receive canonical state. Pre-69 replay packets/checkpoints clear the formerly reserved bits. Protocol-68 Traversal recordings retain v1 identity. Current playback restores canonical stance across world checkpoints and seeks without rerunning movement physics.

## Verification

Windows x64, .NET 10.0.401, normal Release properties with `MphReadReplayPreviewFailureFixture=false`. Mac work was limited to file edits and static inspection. Local raw logs, source manifests, binary hashes, archives and initial failures remain in the owned validation artifacts; [sanitized provenance and source/binary hashes](evidence/crouch-sliding-selection.json) distinguish regression and final artifacts.

Snapshot 04, before the latest-main integration, built with zero warnings/errors and passed all 18 regression routes plus 10,832 custom-weapon checks. It includes 55,605 scene assertions, 267 admitted authority checks, 122 wire/checkpoint/history checks, 193 pure stance/pose-math assertions and 58 focused input/profile checks. Final integrated verification is recorded below.

| Integrated check | Result |
| --- | --- |
| Snapshot 05 normal build / regression routes | Zero warnings/errors; 19/19 passed |
| Classic movement golden | 56 unchanged trajectories at tolerance 0.00001 |
| Authority AI | 42 scenes, seven Hunters at difficulties 0/3 across Classic/Momentum/Traversal |
| Snapshot 06 final build / targeted routes | Zero warnings/errors; 11/11 passed, including 14 Traversal AI scenes |
| Crouch collision / authority / wire / pure / input | 55,605 / 267 / 122 / 193 / 58 checks |
| Final custom-weapon contracts | 14,203 passed on frozen snapshot-06 Core; source binaries unchanged |
| Windows source-manifest contracts | 7 passed in explicit UTF-8 mode |
| Real UDP | Dedicated authority, two independent scripted owners and a native AI bot, 40 seconds, all processes exit 0 |
| Protocol-69 Traversal-v2 passive replay | 2,400 frames; 116,994 checks; 7,200 canonical stance comparisons |
| Windows OpenGL, Samus | Nine PNGs; standing/crouched/released silhouettes 262/177/266 px; exact same-frame repeat RGB |
| Historical protocol 61 / 62 / 68 playback | 80,101 / 80,101 / 87,551 checks; original hashes unchanged |

Both live owners submitted 400 actual Ctrl-held input ticks. They earned 306/400 crouched ticks and 16/20 stance changes; the first owner's native form transitions account for its excluded intervals. Authority snapshots carried 285/374 crouched owner samples, and 401/303 remote crouched collision poses were checked. The new archive contains 405 crouched frames and captures both crouched and subsequently standing world checkpoints, continuation and boundary seeks. Its SHA-256 is `e5c68d47c4aed0d9edc209952b80968642b83f9cadcbf29f2e1f533e3dee97c6`.

The real UDP fixture retains 2/2 displacement diagnostic rows. Its 0.25-unit trigger is not a movement acceptance threshold, and scratch candidates do not prove the resolved native collision response. No zero-anomaly or fault-injected latency qualification is claimed. An initial snapshot-05 live cohort was explicitly interrupted to freeze solver-sample attribution; its partial archive is preserved and excluded from acceptance.

Solver diagnostic samples now retain the stance/slide level at the actual horizontal seam. Later life, teleport or form changes cannot make the test recompute an earlier solver step with a newer stance. This changes diagnostic attribution only; it does not change movement rules or replay schemas.

The Windows native renderer check passed on an NVIDIA RTX 5080/OpenGL 4.6 with the final runtime binary unchanged. Actual Ctrl input produced the visibly lower torso and bent knees; frozen body-hidden controls isolated positive Hunter pixels and all repeat images were exact. CPU fixtures cover all seven Hunters/both LODs; GL captures cover Samus. The diagnostic isolates synthetic input from OS focus callbacks, so it does not qualify physical keyboard or OS-focus behavior. PNGs remain local and are excluded from publication.

## Repairs, CI and limits

Initial compile failures were missing test-only namespace imports. A Windows static-source test first used the host cp1252 default and failed to decode a UTF-8 ledger; the explicit UTF-8 rerun passed all seven contracts without source or assertion changes. The first thin-rail fixture sat inside the low native cylinder and correctly pushed the crouching actor sideways; raising it above the low body made it test standing-only clearance. The continuous clearance now checks the native cylinder's finite edges as well as sphere caps. A delayed-standing-snapshot regression was fixed and is covered by an actual earned-slide trajectory. No golden, collision, authority or timing threshold was weakened.

The GL tool initially had an ambiguous enum reference, fixed by qualifying the existing OpenGL enum. SSH launch had no primary display; the same frozen tool/runtime passed in the existing interactive Windows console session. Its one-shot task was removed after completion.

The full gamepad fixture reaches an unrelated empty startup-screen query after its input checks. The dedicated crouch input route exercises binding/reset/persistence without the shell, and runs in hosted movement CI alongside stance equations and protocol/checkpoint tests.

The previous PR #825 hosted no-assets job exposed a historical source manifest incorrectly requiring live protocol 61. The source-contract test now retains the frozen protocol-61 audit while checking a newer declared live version and the archived compatibility range. Its historical fixtures are unchanged. The other observed hosted failure is a synthetic replay-editor confirmation-focus assertion, outside a movement scene; it remains an unrelated CI limitation. Hosted macOS jobs were queued at observation. Publication and merging do not wait for CI/review, as requested.

Whole-frame performance and competitive balance remain follow-up tuning work. The warmed stance math loop checks zero managed allocation; it does not establish a frame-time budget. Automated synthetic input, CPU assets and rendered diagnostics are reported by their actual scope; no physical-device playtest is claimed.
