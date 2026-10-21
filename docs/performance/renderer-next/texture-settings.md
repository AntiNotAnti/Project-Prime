# R16 settings-change scheduling

2026-10-21. The scene render owner now records desired texture settings and
processes at most one tracked source binding or missing authored slot per
picture. A newer desired generation replaces pending jobs. Stable handles keep
their prior resident image until their own job runs; progressive HD replacement
continues to use the correct native placeholder. A settings job reserves the
existing GPU promotion slot, while decode workers can continue within their
existing byte/worker reservations.

Pending native and modern sampling recipes remain frozen to their resident
generation on both direct and retained draws. Native mip creation is prewarmed
inside that binding's job, preventing a mip toggle from generating every
pending texture's chain on the following draw. An unpaid native lazy-mip recipe
is frozen without mips. Existing format-specific modern mip availability stays
authoritative. Advanced-material companion changes are processed per source;
rapid toggles reuse valid companion ownership rather than leak handles.

Teardown clears jobs and sampler snapshots. Individual released bindings retire
without issuing GL work. Existing material-revision, quality, sampling, ownership
and stream-version fences still reject stale HD results. Applied settings and
their generation advance only after queued settings jobs drain. Separately
queued HD promotions can remain pending, which their existing counters expose.

Observable receipt properties include desired/applied generations, pending jobs,
completed binding jobs, estimated reupload bytes and worst individual job time.
The native-render-check receipt includes both requested options and these
applied settings. A job is a source binding plus its synchronous companions
(up to three); an individual GL image upload remains indivisible. Byte counts
estimate images reuploaded by jobs and exclude GPU-generated native mip bytes.

The native Windows material-pack fixture uses six invented textures and actual
GPU dimensions/mip residency to check one job per picture, applied-state lag,
rapid supersession, stable live names, released-name retirement, direct/retained
sampler freezing, bounded native mip prewarm and teardown cancellation. The
existing source-bank fixture now advances the actual picture scheduler while
checking independent captured identities and replacement/quality toggles.
The partial-companion negative control reaches an actual normal-only
promotion, cancels its outstanding specular/emissive requests while disabled,
then re-enables before the target's off job executes. It requires the existing
normal handle to survive and both missing channels to be requeued/promoted.

Validation results are recorded after execution below. Large HD pack/map-change
stress, first-use hitch rates and p99/p99.9 performance acceptance remain
UNVERIFIED. Visible/near-future priority, incremental region/lower-mip uploads
and an explicit off-thread color/alpha mip contract remain NOT_IMPLEMENTED;
this narrow change retains FIFO source jobs and established GL mip generation.
Release defaults are unchanged.

Native Windows validation using pinned SDK 10.0.401:

- `dotnet build tools/material-pack/material-pack.csproj -c Release -v minimal`
  PASS, zero errors / two unrelated warnings, 32.04 seconds. Receipt:
  [build log](texture-settings-build.log).
- `dotnet tools/material-pack/bin/Release/net10.0/material-pack.dll --gpu`
  PASS: 56 material-pack assertions plus all native GPU suites, including the
  settings and actual partial-companion assertions above. Receipt:
  [default GPU log](texture-settings-gpu-default.log).
- The same command with `PROJECT_PRIME_GL_BC7_TEXTURES=1` FAILED in the R17
  capability prerequisite before reaching the settings fixture: no observed
  BC7-capable context. Receipt: [enabled BC7 log](texture-settings-gpu-bc7.log).
  This failure is retained; it does not establish enabled BC7 correctness.
- After correcting R17 capability qualification to the actual core/extension
  contract, the fresh Debug client and no-reference tool build PASS. The same
  enabled native GPU command PASS: all 56 assertions and settings regressions;
  BC7 storage/pixels also passed for the four invented flat channels. Receipt:
  [qualified enabled GPU log](bc7-qualified-gpu.log). The earlier failed probes
  remain available. A tool-only Release compile against the older Release
  client was rejected for its missing new capability helper; the successful
  rerun used the current Debug client, as recorded in
  [qualified Debug build log](bc7-qualified-debug-build.log).

These are invented-image functional tests. No non-Windows tests were run.
