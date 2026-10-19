# Original capture admission: proposed format 2

This is a review proposal, not an implemented schema or an accepted comparison.
Format 1 remains strict and unchanged. All thirteen cases remain NOT_VERIFIED.

The current original capture has two identity facts that format 1 cannot express
honestly: its supplied workflow snapshot has no `.git`, and the native display
rate is `33513982/560190` Hz. Assigning a remote Git commit to the snapshot or
relabeling that rate as exactly 60 Hz would fabricate provenance.

## Source identity

Use a closed tagged union for workflow identity. A `git` identity requires an
actually verified full commit plus source-state hash. A `sourceBundle` identity
requires a versioned digest of the exact ordered relative paths, byte lengths and
SHA-256 content hashes. Include the driver, every executed route, relevant memory
layouts and any native owner proof. Bound the file count, paths and total source
bytes, reject duplicate paths, and compare every recorded hash before and after
capture. A source bundle carries no inferred Git revision.

Keep emulator/core, ROM revision, core options, adapter, initial state, input and
trace identities separate. A matching source bundle does not prove a RAM field's
meaning or a route's initial state. Keep reference captures private.

## Rate and endpoint identity

Represent actual display rate as a reduced positive rational and retain every raw
post-`retro_run` source-frame index. Preserve the native callback count and the
original route edges: the strafe route still holds Right on indices 20–59 and Up
on 120–159. Do not interpolate, resample or fit an offset to numeric values.

The already observed even-only changes in player values do not identify a native
logic clock. Admission must separately require an AMHE1 source-backed native
update-counter owner: exact ROM identity, address or validated pointer chain,
width, update code identity, wrap behavior and pause semantics. Capture that
counter with both intervening source frames and the effective input witnesses.
Unknown clock ownership must refuse accuracy comparison.

If native source proves one update every two display frames, its physical rate
is one half of the exact display rational, not exactly 30 Hz. Project retains its
actual fixed 60-step clock. A comparison may describe a source-proven mapping of
logical updates to two Project steps while retaining both physical rate
identities; this does not establish equal elapsed wall time. State endpoint
selection and event sum-pairs must follow that explicit owner/consumption proof.
Keep original and Project raw input indices unchanged. Do not choose the first
Project endpoint until the input-latch and half-step relationship is proven.

## Room and input ownership

The supplied memory source defines `GameState.RoomId` at offset 1, but supplies
global addresses only for AMHP1 and A76E. Its AMHE1 player-array address does not
justify applying the same region delta to room or frame globals. Room admission
requires the actual AMHE1 base/pointer and its native initialization/use owner.

`CPlayer.Input` is source-backed at player-relative `0x464`, with 26 typed fields
covering `0x48` bytes. Their names are still `Field0`, `Field2`, and so on. The
private bounded sampler labels them unresolved raw diagnostics. Its 70 synthetic
source/address checks do not establish key, pressed/released or latch semantics.
Do not promote correlation with a host input or a motion result into owner proof.

The [buttons/jump plan](../../artifacts/cpp07-local-ds-source-audit-20261018/original-buttons330-capture-plan.json)
preserves the complete source 800-frame route, including A on 330–337, after a
fresh unchanged 7799-frame bootstrap. It proposes two fresh guarded raw captures,
with no state load, RAM writes or fitted warmup. New-route runtime still requires
root review; repeatability alone would not qualify the jump comparison.

## Implementation gate

Implement format 2 only after reviewing the source-bundle/rational contract and
obtaining the missing native room, update and input-owner proofs. Add synthetic
boundary tests for both identity variants, rational validation, counter wrap and
pause rules, immutable indices, missing owner proof, and event sum-pairs. Those
tests must remain explicitly separate from real original-data availability.
Preserve format 1 behavior, the existing 47 Project baselines, BalancedMode,
network/replay identities, tolerances and all recorded failures.
