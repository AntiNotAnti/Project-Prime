# Optional native canvas transport

This SH-08B slice adds bounded flat geometry and private RGBA textures to the
existing RmlUi document/render owners. It does not activate original credits
or replace the ending receipt. Source is frozen at
`e7526ef32f022b3131b57e9d48f1fe0143fc9890`, including concurrent CW-05g PR838
and protocol70. The wire protocol and existing intent ABI1 are unchanged.
The independently probed optional canvas ABI is1.

## Lifetime and resource contract

A `prime-canvas` element belongs to an existing foreground document. Acquisition
returns a generation/document/monotonic-epoch lease, with at most eight live
leases. Owner-thread, exact document, visibility and foreground checks fence
publication. Reacquisition retires the prior epoch; stale publication refuses,
and stale release cannot retire its replacement. Document close, structural
replacement and shutdown retire resources before their RmlUi owners. Retirement
also frees retained vector capacity. No new window, Scene, graphics device,
renderer, global model cache or source-data extraction path is introduced.

Each epoch accepts one detached texture table: at most64 images,512x512 each,
4MiB aggregate tightly packed straight RGBA. Each image declares nearest/linear
filtering and independent clamp/repeat S/T. Callback upload happens on the
existing render pass. Canvas texture pixels and vertex colors premultiply
once, using integer floor multiplication. Ordinary RmlUi images retain their
existing sampler policy. Canvas sampling travels in a separate optional export;
no existing ABI field is repurposed.

Each frame copies and validates its entire packet before replacing the prior
accepted frame. Virtual dimensions are finite1..16384; vertex position/UV
magnitudes are finite and at most1048576. Caps are65536vertices,196608indices
and4096nonempty contiguous triangle batches. Indices and texture ordinals must
resolve. Empty frames are explicitly supported. Revisions increase only after
acceptance. Draw geometry copies only batch-referenced vertices, preventing
full-array amplification per batch. Content bounds provide centered aspect-fit
projection and forced clipping; fractional absolute placement is embedded once
to avoid RmlUi translation rounding.

The managed owner requires its creating thread and host/document lease, copies
caller arrays before publication and validates returned state against its last
accepted table/frame. The actual draw-list reader retains immutable resource
payloads per opaque handle. Every returned frame now owns detached read-only
resource maps, so later cache pruning or `Forget` cannot damage older frames
needed for device recreation. Android's existing optional GLES path consumes
the same sampling metadata. Its pass preserves sampler0, pixel-unpack buffer
and unpack row/skip state. Desktop GL2 uploads also preserve active texture,
unit0 binding, unpack/PBO state and texture parameters. Sampler-object handling
is capability gated and restores the prior unit0 sampler on supported contexts.

## Qualification and limits

All39 serial commands pass. Both source archives retain all6023 tracked files
unchanged. Desktop, dedicated and focused tool Release builds have zero
warnings/errors. Default Android Release has25 warnings; the explicit optional
GLES build has26, including its existing nullable ByteBuffer warning. Both
signed APKs have119 AOT libraries per arm64-v8a/x86_64 and valid v2/v3 signatures.
The optional APK contains both verified draw-list libraries, manifests, fonts,
assets and licenses; ELF architecture/alignment/export checks pass.

Invented acceptance includes1085 actual native CPU ABI checks,197 managed
packet/Host checks,173 actual native-to-managed-reader checks, pure GLES
operation planning, and776 actual GL checks containing242 pixel checks over20
frames. The latter uses1920x1200 and2560x1440 framebuffers, verifying texture
filtering/S/T wrap, partial-alpha pixel and vertex conversion, painter order,
clipping, fractional projection, resize, upload/PBO/texture state restoration,
hide/show, lease replacement, document close, shutdown/reinitialization and
stale-generation refusal. The native/managed host schedule adds51 real status,
idle reuse, mutation, timer, composition and resource assertions.

All3853 existing source-free credits cases pass; six owned probes pass,
including both3000-tick/3001-frame branches. They keep all33 source files,
core/tool binaries and five caches unchanged. Existing historical replay,
movie lifetime, ending receipt, presentation, post-match save and campaign
timing regressions pass. No protected source payload is retained in receipts.

| Artifact | SHA256 |
| --- | --- |
| Desktop DLL | `b5f5c9ab9f321db0fa33434e58ffd1de93bb226c7e024b2ce4da5442089d4bb3` |
| Dedicated DLL | `25642090f1fc5decb58f501c961a14aa884654daf3ef28a93af762f0b17c7c2a` |
| Default signed APK | `4a1f3fb1896a3c854cd69f4741ea8db54ebaf915580fcd7eecf4f7dd1667b479` |
| Optional canvas signed APK | `2660c91f09359b6d13204895de33264ae49ff08b51ba2b993fb0e3016d884743` |
| Mac native GL2 | `085e0210bc8288db51d23d191e9150a1bcedcafbd48256810d65d4aea59d3266` |
| Android ARM64 native | `ee382cb078580c5ab44a7c95f9207bb6101aa0a19096c998befd59e277f7dae4` |
| Android x64 native | `587f972fcbe0aaafa8348a9a828b8f1f6f2f2e9691ae9b92419d4e0eb66a6561` |

All three native artifacts have source fingerprint
`e81721a66a588cb4b4995bc1cb91481ef14ea1bf6676e6cee604d178481994ce`.
Receipts: `artifacts/sh-integration/native-canvas-e7526ef32`.

The runtime logs expose an unrelated preexisting Home movement-toggle17
model/callback omission, present unchanged on baseline main. Canvas acceptance
passes despite that bootstrap warning; a separate follow-up fixes that owner.

Exploratory actual GL acceptance exposed a production clipping defect: RmlUi
could not infer oversized custom geometry from layout-box overflow. The canvas
now explicitly pushes its own clipping region and restores render state after
rendering. Both tested framebuffer sizes then pass. Fixture-only corrections
settled the prior accepted dirty state before atomic-refusal comparisons and
cached visible element bounds before hiding the document. The focused canvas
harness uses the ordinary shipping DLL; the full replay GL harness independently
requires its explicit failure-injection build.

All canvas media are invented RGBA and geometry. Original source probes export
only metadata/counts/hashes. Windows SSH authentication failed again; tests use
the authorized Mac fallback. Mac OpenGL2.1 does not exercise the sampler-object
extension branch; Windows/Linux contexts with that feature remain unqualified.
Android compilation, native ELF/package verification and the CPU GLES planner
are not physical Android GPU or input acceptance.

## Remaining work

Original credits still need bounded production source loading, frame-to-canvas
adaptation, actual optional page activation, elapsed-time/30Hz ownership,
cancellation, audio transitions and end-to-end story/device acceptance. Existing
movie, save and return receipt ownership remains in place. The feature is
internal and unregistered until that integration is complete; unavailable
hardware does not substitute for unfinished engineering.
