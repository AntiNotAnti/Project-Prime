# Paused checkpoint — 2026-10-21

User explicitly paused this task until other concurrent checkout changes finish.
Subagents android and cpu_preparation were interrupted. No final cadence matrix
or 30-minute run was active. Preserve all working-tree changes and all receipts.
Do not resume automatically.

Work is on codex/renderer-next, base 9885b151cd99e598b48871d8c7cd615d7979130b.
Read status.md, reconciliation.md, measurement.md, texture-settings.md,
desktop-compression.md and shadow-receivers.md before resuming.

Recent completed work:
- Shell.AfterDraw startup ownership guard and bounded frozen native input
  callback isolation. Two actual source-linked tests PASS.
- R16 one-binding/settings job per picture, coalescing, desired/applied state,
  sampler freeze, native mip prewarm, teardown cancellation. Native GPU suites
  PASS incl actual normal-only -> disabled cancellation -> rapid reenable
  preserving normal while restoring specular/emissive. Independent Astra P1
  found and repaired; rereview closed.
- R17 default-off BC7 candidate, core4.2+/exactARB_BPTC capability, authored mip
  cap selection, portable fallback and real native format/storage/pixel checks.
  Debug MVID6c6cc5be-32e7-4e60-b2ad-abbe9366e10a PASS: 1392bytes vs5460RGBA per
  flat channel; worst pixel error0. Four channels total5568vs21840. Not pack
  quality/performance acceptance. Initial probe/build failures preserved.
- Native Python suite119 PASS with5 skips; native-FPS13 PASS. Four of the five
  skips use a hardcoded Unix dotnet path plus unavailable historical frozen
  engine defaults; Windows portability repair and explicit current frozen DLL
  rerun were considered but NOT implemented. Fifth skip is Windows symlink
  privilege1314. No validator relaxation.
- R21 detached offscreen actor/platform candidate source and fixtures added by
  CPU agent. Preliminary Debug native181 checks PASS, actual shadow pixels
  covered28 on1024target; HD/form/platform/effect/generation/cache tests were
  still being finalized. New cache source fixes include instance-aware key,
  material-count budget, packet early-out, pure scout guard. Must obtain exact
  source/test status from CPU agent on resume and run final Release validation.
  Astra reviewed current source/fixtures: no new actionable P0/P1/P2 findings.

Unrelated changes appeared concurrently around12:48 in HUD/input/launcher,
shader/supersample, assets and other files. Preserve them. They are NOT all this
renderer task's work. Root observed no CRCRLF in Shaders/EsShaders afterward;
Git emitted LF->CRLF warnings. Do not mass normalize or restore external edits.
Final tests need an immutable snapshot and source manifests. The root asked
whether other builds could stay idle; user responded to pause instead.

Next after explicit resume:
1. Inspect concurrent changes and CPU agent's interrupted state; finish only
   authorized renderer edits without overwriting others. Check EOL/raw shader
   source identity and Android hash sync after external shader changes.
2. Review/fix any remaining R21 source/test issues, then build final Release
   client+native/preparation/material/resource/spatial tools, server, Android
   enrolled ARM64 full AOT and forced optionalUI Compile. Source binding must
   detect concurrent mutations. Prior Debug/intermediate results are separate.
3. Rerun affected native fixtures on final Release; zero new warnings expected
   except baselineCS0649. All executed tests must remain Windows-only.
4. Freeze binaries and source hashes. Keep all builds/tests idle for cadence
   and soak. Windows runner added and parse-checked but NOT EXECUTED:
   tools/native-render-check/qualify_windows_renderer.ps1
   Modes Matrix(6caps24s), Scale(50/100/150/200 xOff/Low/High), Soak(1804s240cap).
   It requires a fresh output dir and records command/flags/results. Use local
   SDK10.0.401 and fixture PROJECT_PRIME_USER_DATA path below.
5. Final matrix, scale/readback/optional feature checks, native repeated-load
   and four-view Studio resource cases, and30min combat remain pending. Preserve
   earlier failures/contaminated diagnostics. Build no scheduler fix without
   attribution. Report all missing device/flow/image/cost gates honestly.
6. Write final manifest/report and update status.md in-progress rows; do not
   call the full program accepted. R22 full color/alpha/linear/mip migration
   remains NOT_IMPLEMENTED/NO_ROLLOUT; partial camera/tangent candidate is off.
   R13 AGX remains UNRESOLVED_P0 and R14 reuse NOT_ELIGIBLE. Other optional
   gated experiments remain NO_ROLLOUT, no preset/release promotion.

Local SDK: C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401\dotnet.exe
Alternate agent SDK: C:\Users\Jarrett\.dotnet\dotnet.exe
Evidence/fixture root: C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-next-20261021
User data: ...\renderer-next-20261021\assets (paths.txt)
Extracted immutable assets: ...\assets\files (no proprietary data tracked here)
Old frozen binary native-final MVID4bad4ebc-6c44-4903-910a-f9dd80651633 predates
startup isolation/R16/BC7/R21 and is diagnostic only. Earlier matrix120 aborted
on UI wrong-thread setup; Unlimited had17 drops; retries passed but six unwanted
thumbnail workers contaminated that campaign. All original receipts retained.

No commits, PRs, publication or release/default promotions have been performed.
