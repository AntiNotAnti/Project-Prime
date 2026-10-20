# CW-09 — isolated corpse physics pilot

Decision: keep physical corpses experimental and outside the shipping game. The
pilot supplies a working backend and an original fifteen-box Samus reference
mannequin, rather than claiming native Hunter mesh integration. CW-10 must retain
the accepted CW-08 procedural/native fallback. Physical-device, full-game frame
budget, seven-Hunter retargeting and context/renderer acceptance remain gates.

The game and server do not reference `MphRead.CorpsePhysics`. Only the standalone
CLI, GL preview and separate Android test app do. No game wire, registry,
inventory, pickup, movement, damage, collision query, replay or RNG changes occur.
The CLI/preview require `--enable-experimental-physics`; the dedicated test app
is explicitly named and isolated from Project Prime's application ID.

## Boundary and ownership

`IRagdollPresentationBackend` accepts immutable copied descriptors and exports
fifteen copied poses. A descriptor holds an opaque accepted event/context key,
finite origin, normalized accepted direction, bounded inherited velocity,
optional nearby impact point, seed and lifetime. It does not authenticate shots
or turn arbitrary descriptors into gameplay authority. A future scene adapter
must authenticate accepted death facts and bind scene, stream, actor, life and
presentation timeline before calling it.

A per-backend world nonce and nonwrapping per-slot generation prevent recycled
handles, including handles from other worlds, from accessing new corpses.
Sixty-four event tombstones bound duplicate tracking. Quality Off/Low/Medium/High
admits zero/two/four/eight corpses. Every corpse has fifteen bodies and fourteen
joints (ball socket, swing limit and twist limit: forty-two constraints).
Bodies do not collide with other dynamic bodies, other corpses, players or
projectiles. Only copied static triangles enter the private world. Reference
landmarks are measured positions; primitive shapes and virtual endpoints are
original. No extracted model, animation, texture or collision asset is committed.

Static input is limited to 128 finite nondegenerate triangles, right-handed CCW
outward faces. The BEPU adapter reverses B/C exactly once for BEPU's clockwise,
one-sided contact convention. The original incorrectly wound pilot floor failed
at tick eleven with no contacts; correcting the boundary passed the unchanged
floor test. Ramp/step and rendered evidence use the corrected conversion.

One caller thread owns the world and its arena. Simulation advances at 1/60 s
with six solver iterations/two substeps and no worker dispatcher. Duplicate
fixed ticks do nothing; discontinuities clear visual state. Sampling interpolates
previous/current poses without advancing simulation. Spawn poses are copied,
accepted direction drives a bounded impulse, and a fixed seed variant breaks
perfect balance. Velocity limits, finite-pose checks, bounded lifetime (at most
240 ticks), sleeping and below-world retirement prevent persistent runaway
visuals. Pool/contact checks are **post-operation fail-stop tripwires**, not a
promise that transient native allocation can never exceed 32 MiB. Input/body
limits bound admitted work separately.

A failed spawn, step or retirement disables the backend and refuses subsequent
samples. Cleanup attempts every slot; disposal always tears down the simulation.
Bodies remove attached constraints. Box shapes are explicitly removed. Static
mesh triangle/tree buffers are explicitly disposed before Simulation.Dispose,
which otherwise does not dispose their shape-owned storage. An arena is leased
to one world; uncertain teardown poisons it. A constructor failure abandons its
arena. Final arena disposal releases reserved pool blocks. Reserved byte counts
are not an outstanding allocation ownership counter; stress evidence and source
ownership review are separate checks.

## Reproduction

Use .NET10; builds here used SDK10.0.401/macOS arm64. Run:

```sh
dotnet run -c Release --project tools/corpse-physics-pilot -- ./pilot-results --enable-experimental-physics
dotnet run -c Release --project tools/corpse-physics-preview -- ./pilot-captures --enable-experimental-physics
```

The first runs contract checks, 45 CPU workloads and 128 world rebuilds. Each
workload compares frozen/procedural/physics at zero/one/two/four/eight corpses and
60/120/240 scheduled interpolation Hz, with fixed 60 Hz stepping. Cohorts retire
and respawn every 180 steps so the measured window does not silently become an
empty scene. It reports step, interpolation, spawn/retire percentiles, allocations
and reserved pool bytes. The measured values include host scheduling/tiered JIT;
they are not achieved display Hz or full-game frame times. The proxy fallback is
a comparison implementation; CW-08 remains the game's established fallback.

The GL tool renders the same isolated primitive scene, including ramp and steps,
at six fixed ticks and three interpolation fractions for each backend. It checks
GL errors and writes 54 PNGs plus a receipt. Pictures demonstrate visual stability;
center-level floor checks do not prove zero penetration of every rotated vertex.
The Android fixture has its own README, separate package ID and explicit test-only
debuggability for result extraction. It is not a production application build.

## Dependencies and rollout

Exact lockfile pins: BepuPhysics and BepuUtilities 2.5.0-beta.33, upstream commit
`29ed466870534c413aba9eeccd813cf15d135d0b`, Apache-2.0. The prerelease is evaluated
only in this experimental leaf. Package TFM is net8.0; two managed DLLs total
1,043,968 bytes, compressed packages 592,320 bytes. Their package archives contain
no native runtime payload. This does not establish Android SIMD quality or device
thermal behavior. Full notices accompany pilot outputs and the test APK.

Sources: [pinned NuGet package](https://www.nuget.org/packages/BepuPhysics/2.5.0-beta.33),
[pinned simple demo](https://github.com/bepu/bepuphysics2/blob/29ed466870534c413aba9eeccd813cf15d135d0b/Demos/Demos/SimpleSelfContainedDemo.cs),
[pinned ragdoll demo](https://github.com/bepu/bepuphysics2/blob/29ed466870534c413aba9eeccd813cf15d135d0b/Demos/Demos/RagdollDemo.cs).

See [evidence](evidence/CW-09.md) for actual results and remaining gates. Rollback
is to leave the standalone pilot unused; no shipping game setting or dependency
was introduced. A rejected pilot does not block subsequent fallback/package work.
