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

Base is merged PR948 `123df20e3737f672048309310f18c8c7f9428cd9`, including
main PR946/protocol81. Only draft PR947 was open at overlap inspection. This
slice adds no protocol/replay fields. Production C# freeze is
`353efd1790d7f3310260152759161ad32ca601f6`; final hint/world fixture is
`5a74d2cf00d706fc438e435247124b13e5a1dbe1`; final pose test freeze is
`ea9e77b08b7f31911470fe1df62a45242eff891e`.

| Artifact | SHA256 |
| --- | --- |
| Desktop Release core, including final body-policy fixture | `8eccf5dffec7261ff29c2e05a46bac3ee90952bdfa46e533d221249ff6f010c4` |
| Dedicated win-x64 Release | `7e2885870ed0bcc403ce4d955880d7888913eee7830f9d012f82cf6053cf081a` |
| Final external-import-check | `02f244e40db18d5e5a0b615560ade41782b885b7bc5b8bbed60506df7edde224` |
| Native UI contract tool | `6bfc50a722779353bab67bf8cbc292a6b4cd59d9255fcd8fa7a33b3afc4a91d1` |
| Nettest | `7863972a97fc9276ba2e477524b799989bd0d66a13bd9c2dca2d331b46367598` |
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
- **68,103 final actual world assertions** (68,112 in the earlier visual run): real initialized native Weavel baseline
  plus each Trooper, keyboard/mouse movement, turn, shot, charge, jump, morph,
  declared injected lethal damage and native respawn. Each1,011-step gameplay
  trace exactly equals native SHA256
  `00542a94582a3a25b345a4755346c3c8701ebe9873686d8090ff3b399fc88689`.
  Complete source packets replace the native biped without duplication, all
  source components stay exclusively locked through the input/draw script,
  repeated draws preserve gameplay/palettes/clocks, and actual pause, shadow
  fallback, independent owners, native first-person gun and context retirement
  pass. Per Trooper the fixture records1,460 source and556 fallback pictures,
  including its added scheduling phases; these are not simulation-step counts.
- Simulated90/120/240/540Hz schedules exercise fractional and zero-step
  pictures without render-time pose advancement. First three schedules return
 60 completed native steps;540 returns59 with the remaining accumulator interval
  retained. Source time equals the exact returned step inventory. This proves
  scheduling invariants, not achieved display refresh or performance.
- **77 actual launcher/import lifecycle checks**: real RenderWindow,
  OfflineLaunch.Preview, MatchStart.Begin/LoadScene/EndScene; detached four-body
  plans, missing/corrupt refusal before Scene allocation, repair to a new
  generation, Remove retaining the admitted Scene, teardown, reinstall and
  native recovery. Final launch and pose checks use the final external tool
  above; earlier launcher tool SHA was
  `bceb43cf5765320517d2649220b0c99bf2a7e3228207b18ce08a3c88df2a7a26`.
- Real Windows native CPU UI clicks pass all seven Hunters and192 category/
  beam/color/suit publications plus each Trooper's Apply/disk reload/new
  presenter/Discard/duplicate-event fencing. No account writes or physical
  control claim. Final markup explicitly describes native preview/gun/ball
  fallbacks. Managed typed ABI/range/Host/lifecycle/IME/reinitialization checks
  pass independently.
- Desktop/dedicated builds pass; body-policy checks2,708 desktop/2,690 dedicated,
  source-root24/rig910, replay-format3,050, offline-policy971 and network
  architecture pass. The exact same desktop core passes the77,124-assertion
  actual normal/Overcharged cannon regression; that run's earlier tool is
  `db21a35fd121bf237d3fd0ed874589d076fe8b1dcd4373391d26b4bf20f796eb`.

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
