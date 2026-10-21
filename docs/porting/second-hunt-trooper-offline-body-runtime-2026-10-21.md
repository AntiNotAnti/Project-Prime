# Trooper bodies in local offline Weavel matches

This slice adds opt-in Power, Ice, Wave and Plasma Trooper **body cosmetics**
to real local offline Weavel matches. Hunters owns selection, Apply, Discard and
saved preferences. Native is the default. A suit/body selection does not enable
wall-cling, Scout Drone, Pirate Blaster or any other extra gameplay capability.
Imported Trooper characters and the full Second Hunt plan remain incomplete.

## Ownership and presentation

`LocalBodyAppearance` appends stable values3–6 without changing values0–2 or
any Hunter, network or replay identity. The existing finite native intent286
accepts0–6; incompatible buttons are hidden and commands are refused. Samus
owns Echoes1/2, Weavel owns Trooper3–6, and Native0 works for every Hunter.
Random Hunter is resolved once before matching the saved body to that Hunter.

The existing import generation, source-rig loader, animation decoder, material
programs, Scene render graph and per-actor body owner remain the implementation.
Each Trooper retains all35 joints/36 nodes, all1,970 body triangles, three
source programs and seven textures. Its own pose adapter preloads seven strict
source clips at the match load barrier:

| Native state | Exact source clip |
| --- | --- |
| Idle, Turn, lower Shoot/Charge/ChargeShoot | `B_ready_pirate` |
| WalkForward | `B_runloop_pirate` |
| WalkBackward | `B_backward_pirate` |
| WalkLeft / WalkRight | `B_strafleft_pirate` / `B_strafright_pirate` |
| Upper Shoot / ChargeShoot | `A_standshot_pirate`, looped |
| Upper Charge | `A_gunloop_pirate`, looped |

RootPin, Pirate mappings, model-space torso layering and AimTurn are adapted
from Second Hunt `41452f2d053fcfa679e33c00a73d120a53f59e61`, MIT,
Copyright2020 NoneGiven; notices remain in the repository LICENSE. Private
derived channel copies freeze root rotation/translation at source bind and
retain detrended, zero-mean horizontal pelvis sway plus source pelvis height.
They reuse `Anim.Sample`; raw viewer clips and source archives are unchanged.
The upper spine keeps its lower-body position and receives the upper clip's
model orientation; upper descendants retain source local transforms. Six-tick
transitions store unaimed history and apply native-clamped pitch once.

The owner advances only on completed, unpaused60Hz simulation steps. It retains
the exact Scene/player/slot/life/native model/pack/generation and rejects private
practice gameplay variants. Ordinary native physics, health, hurtboxes, gun,
muzzle, effects, projectiles, scoring and clocks remain authoritative. Full body
fallback occurs before source submission for unsupported animation/status,
jump/landing, spawn, morph/ball, damage, team/bright/overlay, crouch/tuck,
mantle/wallrun, incompatible camera and dynamic-shadow states. Trooper guns and
ball remain native. Hunters' ordinary preview remains native; the separate
source-program viewer is unchanged. No online, campaign or replay admission.

## Frozen source and binaries

Initial base was merged PR948 `123df20e3737f672048309310f18c8c7f9428cd9`,
including main PR946/protocol81. Concurrent PR947/949 subsequently merged;
this branch integrates pinned main `a7985b431` at local merge
`caa54da853d3aadb47b6ea906ee9b1fc2e5beb74`, inheriting protocol82 and its
active-grapple native-body fallback. This character slice adds no wire/replay
fields. Character C# freeze remains `353efd1790d7f3310260152759161ad32ca601f6`;
final hint is `5a74d2cf00d706fc438e435247124b13e5a1dbe1`, pose test
`ea9e77b08b7f31911470fe1df62a45242eff891e`, grapple presentation fixture
`670b5c37f`, native UI fixture `b16ea5d64`. Those post-merge changes are test-only.

| Artifact | SHA256 |
| --- | --- |
| Protocol82 desktop Release core | `59ad23e41a4607b38087b1c88f074303405eefbfea4802dedcda2dfdfcd9bde0` |
| Protocol82 dedicated win-x64 Release | `ce5de150386bc3fd1737029c6e74be63c0e1dc88f35516a027c15a99b905c0b6` |
| Protocol82 external-import-check | `0c261d210f2160cbe387b56005a204552f455adb063dd84972af53819b824a66` |
| Native UI contract tool | `9eb80bd7794320d2557d2e58ee4ad7cad84f5811f41d9f1b00ccc3c5ec6a627c` |
| Nettest | `10112baf7f9a22534ae86392c1e38f4e83d5f055c68f5e5e74df548d2cf50431` |
| Rebuilt Windows native bridge | `87f02086cd8400279eb06adec9105aebafd55a472cc7ef3bb9909653bcf76c9e` |

SDK10.0.401, NVIDIA RTX5080/OpenGL4.6 driver617.42. The Windows bridge was
rebuilt and verified with source fingerprint
`aa6bcd55a6b68f95944b170a4bb527fa6c7e602b9fd542ff5403df277f7a3293`.
Other native target manifests were not relabeled or qualified. Windows is the
user's requested test machine; further Android work remains deferred.

## Actual acceptance and limits

Owned Prime USA Rev2 RVZ remains SHA256
`3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844`.
Every actual fixture uses isolated disposable imports/user data, checks source
hashes and deletes its derived tree. Protected payloads and images are not in Git.

- **27,303 CPU pose checks /68 refusals /21,804 full-rig comparisons**: all
  four profiles, seven clips/126 source keys each,81 supported state pairs at
 65ticks, every enum pair, source-root/pelvis filtering, model-space spine,
  aim, transitions/restarts/reset, detached returned buffers and independent
  owners. All56 imported files are exclusively locked throughout construction
  and sampling; clip reads stay0→0, component bytes/caches/native RNG unchanged.
  CPU pause scope is omitted sampling, not a claim of Scene pause acceptance.
- The independent System.Numerics oracle retains matrix error<0.0004 for raw,
  steady and ordinary transition comparisons (maximum0.0003835068). Eight
  six-tick blend cases hit OpenTK's float dot>=1 endpoint-return branch. Each
  verifies both independent endpoints/history and translation within2e-6,
  exact runtime endpoint-branch reproduction and stable rotation error<=0.0008
  radians. Observed maximum is0.0007414282 radians/0.04248degrees and endpoint
  difference5.96e-8. This is an explicit numerical bound, not relaxed animation
  state, clock or raw-key acceptance. Earlier failures/diagnostics are retained.
- **68,143 protocol82 actual world assertions** (68,103 initial protocol81,
 68,112 in its visual run): real initialized native Weavel baseline
  plus each Trooper, keyboard/mouse movement, turn, shot, charge, jump, morph,
  declared injected lethal damage and native respawn. Each1,011-step gameplay
  trace exactly equals native SHA256
  `00542a94582a3a25b345a4755346c3c8701ebe9873686d8090ff3b399fc88689`.
  Complete source packets replace the native biped without duplication, all
  source components stay exclusively locked through the input/draw script,
  repeated draws preserve gameplay/palettes/clocks, and actual pause, shadow
  fallback, independent owners, native first-person gun and context retirement
  pass. Per Trooper the protocol82 fixture records1,461 source and557 fallback pictures,
  including its added scheduling phases; these are not simulation-step counts.
  Protocol82 additionally injects a declared valid accepted grapple presentation
  state, checks complete native fallback, restores it and verifies the same
  body/palette/clock/gameplay state. This is not controller or anchor acceptance.
- Simulated90/120/240/540Hz schedules exercise fractional and zero-step
  pictures without render-time pose advancement. First three schedules return
 60 completed native steps;540 returns59 with the remaining accumulator interval
  retained. Source time equals the exact returned step inventory. This proves
  scheduling invariants, not achieved display refresh or performance.
- **77 actual launcher/import lifecycle checks**: real RenderWindow,
  OfflineLaunch.Preview, MatchStart.Begin/LoadScene/EndScene; detached four-body
  plans, missing/corrupt refusal before Scene allocation, repair to a new
  generation, Remove retaining the admitted Scene, teardown, reinstall and
  native recovery. Final launch and pose checks use the protocol82 external tool
  above. The earlier protocol81 core/tool remain separately retained under
  `trooper-runtime-protocol81-frozen` with their original receipts.
- Real Windows native CPU UI clicks pass all seven Hunters and192 category/
  beam/color/suit publications plus each Trooper's Apply/disk reload/new
  presenter/Discard/duplicate-event fencing, including Native and all four
  Trooper buttons at640×480 with focus/scroll/click centers inside the viewport.
  No account writes or physical
  control claim. Final markup explicitly describes native preview/gun/ball
  fallbacks. Managed typed ABI/range/Host/lifecycle/IME/reinitialization checks
  pass independently.
- Current protocol82 replay conversion widens old intents to the new canonical
  width. The inherited protocol38 check still expected the old converted width.
  Test-only repair `8862112f930079ef987c5d57896c8bdb47d30732` preserves the
  historical input bytes, verifies shot timing/no invented pose and adds80/81
  touch/identity preservation and zero-grapple-default cases. The repaired
  replay suite passes3,107 checks in a separately compiled CPU-only core
  `3d51b98e7ea460db8dfd565de66ce16c0b1be44e0f92b6275b1d68159abfe38a`.
  Production code is identical to the GPU-qualified merge; that additional
  binary qualifies this fixture repair, not a new GPU run.
- Desktop/dedicated builds pass; body-policy checks2,708 desktop/2,690 dedicated,
  source-root24/rig910, offline-policy971 and network
  architecture pass. The exact same desktop core passes the77,124-assertion
  actual normal/Overcharged cannon regression. Frozen protocol81 receipts are
  retained separately and do not qualify unrelated protocol82 main changes.

Ignored evidence lives under `artifacts/sh-integration/trooper-runtime-*`.
The successful visual run saved eight local idle/fire captures. Six were viewed
and the two Power images matched already viewed captures byte-for-byte. All
were individually deleted with SHA receipts; the earlier two Power captures
were also individually deleted. Colors and body articulation were coherent in
the actual native arena/HUD. This is scoped visual review, not original-hardware
pixel equivalence, complete animation framing or every combat/status case.

The full plan still needs Trooper cannons and broader gun transitions, imported
drone/Blaster/affinity presentation, source balls, explicit gameplay-compatible
owners, independent multiplayer identities/authority/readiness/replay and the
other recorded character/importer/map/matchmaking/campaign work. The next small
source cannon candidate has been measured independently at206 triangles per
Trooper; those research identities do not themselves grant runtime capability.
