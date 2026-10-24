# Thumbnail and calibration readback — 2026-10-22

Read-only review found two callers outside the shared readback contract:
`ScreenCapture.QueueCosmeticThumbnail` set pack alignment without restoring it,
read RGB directly and inherited pixel-pack buffer/row/skip state.
`CalibrateInk` read a managed RGBA patch with the same inherited-state risk.
This is a concrete source defect; no native crash or visual reproduction is
invented from that finding.

`CaptureReadback` now supports checked RGB and RGBA subregions. It validates the
rectangle against its source dimensions and the existing 64 MiB working budget
before GL work. Both forms use portable RGBA8 reads, neutralize the pack buffer,
alignment, row length and skips, and restore the caller's read framebuffer,
read-buffer selection and pack state in `finally`. RGB strips alpha without
flipping or changing channels. Full-target captures retain the same path/budget.

The preview supplies its **draw** framebuffer and actual target dimensions in a
`CaptureSource`; inherited read/draw bindings may differ. Its worker receives
the same bottom-up RGB crop and dimensions, uses the same encoder/writer and
retains the single thumbnail slot. Failed readback releases that slot. A crop
outside the target is skipped before taking a slot, preserving a clipped
preview's draw rather than failing it for an optional thumbnail.
Calibration reads a centered RGBA region from `_frameBuffer` after the probe
draw. Its side limit, red-channel selection, empty-frame retry, median, depth
quantum and fixed-threshold failure behavior are unchanged.

Executed on Windows with the pinned SDK, without a shared application build:

```powershell
python -m unittest discover -s tools/native-render-check -p test_capture_regions.py -v
```

**PASS**, latest retained in [focused bounds log](windows-capture-regions-bounds.log).
The isolated temporary C# project compiles the actual helper and extracts the
current thumbnail/calibration methods. A stateful GL substitute checks exact
crop coordinates, RGB/RGBA bytes/alpha/bottom-up orientation, dirty PBO/pack
state, success and failure restoration, bounds/overflow/budget rejection before
GL work, worker delivery/slot retirement, and calibration median/empty/failure/
small-target behavior. The actual preview call site is guarded to select the
draw target. This is source-linked execution, not GPU/device evidence.

`tools/final-composite-check/CaptureRegionChecks.cs` adds a native Windows
fixture for different read/draw framebuffers, hostile pack state, exact cropped
pixels, actual thumbnail worker delivery and incomplete-FBO restoration.
It is integrated into the existing composite check, but its shared-client build
and native execution remain **NOT_RUN at this handoff** pending the parent's
coordinated rebuild. Actual cel shader/probe GPU behavior and Android device
readback remain separate qualification gates. No timing, final matrix or soak
claim is made by these tests.

Final native region/thumbnail validation PASS in `windows-composite-v4.log`: distinct draw/read targets, hostile PBO/pack state, exact RGB/RGBA crop, worker delivery/release and incomplete-source restoration. Final client identity is recorded in status.md; v4/v5 bundles match completely. All12 scale/shadow captures also pass PNG/depth dimensions, nonblank coverage and exact repeated RGB/depth checks. This remains narrower than full UI/replay/export/cel lifecycle acceptance.
