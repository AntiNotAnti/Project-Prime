# CW-12g — ordinary phase playback, ranges and detached export

The user waived remaining tests on October21. This implementation is not new
runtime, visual, security, export or performance acceptance. Historical replay
checks cannot qualify newly admitted paths.

Ordinary exact-package preparation, request sessions and private Scene/player
opening admit both custom formats7 and8. Every seek retains the existing bounded
single-worker preparation and replaces the Scene on its owner thread. Exact
descriptor, file baseline, format, source policy and package leases remain fenced.
The existing explicit playback feature gate and approved local store still apply.

Custom archives can now carry a hidden lead-in using the existing header field.
The world, source clocks and original records stay anchored to their original
recording origin. Visible time subtracts the hidden interval; absolute seek targets
add it. EOF and pose availability use the complete raw duration. Warm-up remains
bounded and cannot complete seek0 before the visible origin or overshoot a paused
target after a batch finishes seeking.

Range extraction preserves the custom source format and firing policy, including
unindexed full matches. It retains every required warm-up fact and durable
checkpoint, preserves post-world envelopes at the baseline and selected terminal
frame, removes old quiet markers and emits one final quiet marker. Nested ranges
add their hidden interval without retagging facts or rebuilding a network seed.
Theatre virtual/materialized ranges, reviewed editing and export controls are
admitted for custom playback.

Detached export admission retains the exact historical weapon package alongside
the recording. Preparation copies validated archive bytes into immutable job
inputs and hashes them. The child verifies the complete package manifest row and
installs into its own private work-directory store, enabling exact-resource
playback there. It never installs into the user's gameplay store, grants inventory
or substitutes current mechanics. Retry uses its retained private package copy.
The input-count bound includes this additional package. Cancellation and failure
retain the existing process/job ownership rules.

Custom presentation cameras support authored track sampling, bookmarks and
keyframe restoration through the private Scene. Native gameplay simulation and
weapon IDs remain unchanged. Full custom sound-cue fidelity, portable bundles,
phase-preserving rolling clips/killcams and custom bots remain follow-up CW-12
implementation. Android and remaining tests stay unrun under the user's waiver.

The integrated desktop build exits0 with zero errors (MSBuild79.79s), against
`bf4fdd2c2239991003070b287ddd16e4c65fe68c` after the upstream renderer/HUD/settings
merge. SHA-256: `3d2843788023e0091624a3969a637a23701552f80dc64652ea3960c8ae555db1`.
The two warnings are existing CS0649 and upstream CS0618 `TextBox.Watermark`.
Receipts: `artifacts/custom-weapons/CW-12g/compile-integrated/`. The earlier
pre-integration compile is preserved separately. No tests or Android commands ran.
