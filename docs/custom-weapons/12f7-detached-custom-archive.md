# CW-12f7 — detached custom archive construction and reopen

This source contract covers the explicit format7 path in `ReplayFormatV3.cs`,
`ReplayFormatV3.CustomWeapons.cs`, `ReplayMetadata.cs`, `DemoFile.cs` and
`ReplayTimelineArchive.cs`. Local qualification is recorded in
[CW-12f7 evidence](evidence/CW-12f7.md). It extends
[recorder clock continuity](12f6-recorder-clock.md) for detached inspection;
it does not open Scene playback or ordinary custom recording gates.

Format7 freezes protocol83 and declares a CWRD2 inspection descriptor. Its
bootstrap contains exactly one construction packet (tag253) wrapping a complete
explicit replica15 checkpoint. Metadata room/mode and origin recording frame
must match that construction; descriptor bytes must match exactly. The native
world-checkpoint field is empty. Timed-shot capability must agree with the
constructed owner. Older formats cannot silently carry this custom declaration.

The custom capability block carries its version, bounded flags, reserved zero
byte and bounded descriptor bytes. Descriptor inspection validates mechanics
and layout consistency, not package provenance or resource availability.
`ValidateCustomArchiveMetadata` restores an independent explicit replica owner
and checks the cross-field contract before returning it. It creates no Scene,
connection, package installation, grant, collision or gameplay authority.

Actual accepted custom suffix records retain their semantic payload and
authority tick, with recording frame rebased against the exact construction
origin. Custom records at relative frame zero are retained: they may follow
the captured prefix on the same recording frame and must not be discarded as
native bootstrap duplication. Each custom record requires the declared context
and bounded valid fact bytes; publication sequence and state semantics remain
the detached owner's responsibility. Reader structural validation alone does
not establish complete sequence or state continuity; reconstruction must pass.

The indexed writer creates a new partial recording, refuses overwrite and
finalizes its container on disposal. The test writes an actual complete prefix
plus every subsequent captured custom fact to `actual-custom.ppdemo`, closes
the writer, then reopens through `DemoReader`. Absolute replay recording frames
are restored with checked addition of metadata origin. End markers are not
fed as gameplay packets. Canonical final replica15/CWRS equality is the intended
detached close/reopen invariant; observed results are recorded in the linked receipt.

The separate cold-process tool additionally resolves the archive's exact
historical package manifest with `PrepareHistorical`, retains that binding for
the operation, and checks its definition against the archive context. Missing
historical bytes refuse rather than selecting a current stock definition or
installing/downloading a package. This historical binding is separate from pure
metadata inspection and does not yet establish a Scene resource owner.

This slice's file can establish accepted custom-lane continuity with its actual
native Match context. It does not prove Roster/snapshot coverage, whole Scene
reconstruction, world12 integration, archive seeks, accepted two-client contact
capture or bots. Default PlaybackSession/Join gates remain closed. No release,
Android, WAN, audio or performance qualification is claimed here.
