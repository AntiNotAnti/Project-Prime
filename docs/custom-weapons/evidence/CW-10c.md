# CW-10c evidence — copied static surfaces

Base: main `f59389ece3f93d0afb95536327848f02c801cc4e` (PR909).
Scope: leaf bounded static mesh builder, relative falling guard and non-shipping
diagnostics. No game/server source, package/wire or native gameplay table changed.
[Design](../10c-copied-static-surfaces.md).

SDK10.0.401, runtime10.0.12, macOS arm64; processor count2, single MSBuild worker,
shared compilation disabled. Raw receipts remain under
`artifacts/custom-weapons/CW-10c/`. Core DLL SHA256:
`292cf85686b0c8666f3ae9b70de9b344bc47af39f7cd2978762c154b8d8ed855`.

| Check | Actual result |
|---|---|
| Leaf and standalone Release builds | PASS, zero warnings/errors |
| New static contracts | PASS789 after the projected-area correction; both windings, translated floors Y=0/7/-30, actual solver contact, malformed/concave/star/warped faces, atomic invalid refusal,128/8192 bounds, owner thread and defensive copy |
| Full standalone pilot | PASS2,294 contracts,45 workloads,128 worlds/1,024 corpses |
| Actual filtered native room scan | PASS88 checks,27 rooms,81 local candidates;12 accepted,69 refused, zero source mutations |
| Actual copied native support probe | PASS Alinos Perch extent1;15 primitive bodies/42 joints/120 fixed steps, zero faults, minimum box-corner Y=-.03120375 against floorY=0 |
| Existing custom CPU regression on final core | PASS35,726 checks,6.581s; isolated user data and unchanged assembly hashes |
| Separate Android Release arm64 Mono AOT | PASS16.390s build, zero warnings/errors,24 AOT modules and executable-mapped core/BEPU images |
| Actual Android static/affine/map contracts | PASS942=789+108+45 on owned API36 arm64 emulator5582 |
| Actual Android proxy/lifecycle matrix | PASS45 workloads/128 worlds/1,024 corpses; zero warmed step/sample bytes, arena plateau/final0B; Activity23.536s includes contracts and benchmarks |
| Relative unsupported fall | Birth Y=0 and -30 both retire at tick129; last minimum Y=-11.903998 and -41.903996 respectively, without backend fault |

The static tests also cover large crossing polygons whose vertices lie outside
the capture region, finite input/plane limits, sealed builders and allocation-free
successful face ingestion. Default birthY=0 behavior is retained. Joint stability
measurements remain.080523096 maximum BallSocket error in the existing varied
eight-corpse cohort; no new visual-quality claim follows from this value.

The native diagnostic uses owned AMHE1 content, an isolated user-data directory
and the unchanged CW-10a engine build
`0f03fa9fc780355d29797425294ff129d3a22aed4f2203d1f6df44765f8ac0c2`.
This leaf slice does not claim a new full game build or acceptance of subsequently
merged unrelated UI changes. Native tool SHA256:
`23b7c39ffc6e1077c1e5bce8a868da7ae6c03d9795de289c1bca1ed40d0193ac`.
Its final scan took2.020s with the projected-area correction. The tool builds
completed with zero warnings/errors.

All27 source rooms used MphCollisionInfo. Accepted local half extents were:
Alinos Perch1, Magma Vents1, Gorea Prison1/2, Sanctorus1, Overload1, Proving
Ground1/2, Highground1, Fuel Sluice1/2 and Arcterra Base1.68 refused candidates
had stored-plane mismatches (.00203345–.134306 world units); one Alinos Landfall
extent4 face failed the other strict geometric policies. No tolerance was widened
and no nearby bad face was skipped. The fixture requires both actual acceptance
and at least one physical support probe, preventing a vacuous all-fallback pass.
That synthetic primitive probe is not a genuine death, dynamic-barrier, gameplay
collision, renderer or whole-map qualification.

All45 desktop warmed step/sample rows report0B; the128-world arena plateau is
1,073,152B and final reserved memory0B. Eight-corpse-cohort/240-scheduled-Hz physics
step p50/p95/p99=.233625/.254791/.275000ms, maximum.309958ms. These are standalone
CPU workloads, not achieved display refresh or a controlled game-frame delta.

Read-only audit found a real fan triangulation bug before merge. A polygon whose
middle vertex rose.001 above otherwise collinear projected points met the plane
tolerance yet emitted a vertical fan. A one-ULP inward variant could face the
wrong way. The pinned pre-fix core (`da225cb3…`) failed exactly four new atomic
refusal assertions; the other785 static checks passed. The final builder requires
each fan's signed projected area along the stored normal to be at least.001.
All789 static checks and the2,294-check full pilot then passed. The original RED,
pre-fix benchmarks and pre-fix Android run remain separately labeled artifacts;
they are not final-source acceptance.

Final Android APK SHA256:
`a32368ace480cbe6259ae2bcf888f88c0528cdb6a18a42779215f5b0fd6da35e`.
The separate test application remains permission-free and includes the notices.
Its explicit test-only debuggability permits private result retrieval; no native
Hunter/map asset is packaged. The8-corpse/60-scheduled-Hz physics step measured
p50/p95/p99=.924459/1.058375/1.106084ms, maximum1.185250ms. These emulator results
do not qualify physical-phone performance, heat, graphics or full-game frames.

Low host space initially prevented the private emulator boot. Completed generated
historical server caches from this worktree were removed with a manifest; source,
latest server binary, APKs and evidence remained. A hashed stale private5582 data
overlay was then removed, leaving the recreated1GiB private data image. Failed
launches, pre-fix and final runs remain distinct. Other emulator5580 was untouched.

Rollback removes the new unused leaf builder/diagnostics and restores the previous
fall guard if desired. There is no shipping feature flag or migration to undo in
this slice. No CI/review result is claimed or awaited; the user authorizes immediate
merge after local validation.

CW-10 remains ongoing. Collider fitting, accepted-life render ownership, actual
multi-actor GPU handoff, scene/connector/dynamic-barrier fences and integrated
death/respawn stress remain subsequent work. Physics is still unavailable in
the shipping game. Physical-device and full-frame budget gates remain open.
