# Optional campaign time records

This SH-08 slice persists optional fixed-step campaign observations and captures
their scalar values in the existing ending receipt. It does not replace the
movie chain, save serializer, committed-save owner or ending return path.

## Implemented

`StorySave.SaveStats.CampaignTime` is a nullable versioned extension. Neither
the StorySave constructor nor the extension's properties initialize missing
history. A default/legacy/deserialized save remains unknown; absent extensions
remain omitted when serialized. An unsupported version stays untouched and is
not displayed as a known duration. Partial fields remain partial.

Only the explicit `StorySave.CreateNewCampaign` factory initializes version 1
with all three counters at zero. `StartNewSave` calls it for a deliberate new
game; `ReadSave` calls it only if no save file exists. Existing JSON containing
literal `null` retains the ordinary unknown fallback. Reading, copying or
resuming an existing save does not opt its missing history into tracking.

The existing `RunSimulationFrame` elapsed-time branch advances these counters
once per eligible 60 Hz step, never during drawing or the 30 Hz credits timeline.
Its existing ProcessFrame/InProgress/no-dialog-pause/no-menu-pause policy is
retained. Additional gates require single player, loaded original story room
27–92, and no headless/replica/demo/network session. Camera sequences, movies,
fades and death animation follow the existing elapsed-time policy when otherwise
eligible. This is an explicit Project Prime observation policy; no original ROM
timer parity is claimed.

Total play ticks advance in every admitted room; current loaded room 91/92
advances only its matching Gorea phase counter. Pending transition destinations
do not select the phase. Unknown counters remain unknown, negative counters are
never exposed, and overflow becomes unknown rather than wrapping or saturating
to a fabricated elapsed time. The combined Gorea value requires both phases
known and an overflow-safe sum before conversion to whole seconds.

The accepted checkpoint owner already excludes Stats from progression CopyTo.
Death/checkpoint retry therefore preserves the live cumulative counters while
restoring progression; room transitions reuse the same StorySave. An explicit
Continue reloads the last committed record from disk. This slice adds no second
checkpoint or save authority.

`CampaignEndingSummary` captures nullable tick scalars before world teardown.
Its existing positional constructor/deconstruction stays intact; new body init
properties extend immutable receipt equality. The existing post-match context
captures exact save bytes and the receipt once; failed-save retry still writes
the original payload/destination and publishes only after successful commit.
`SaveWhen.Never` remains a valid uncommitted receipt without a new write.

Native and shared result views show total time as hours/minutes and good-ending
Gorea time as minutes/seconds. The original Gorea format intentionally drops
whole hours. Unknown values say `NOT AVAILABLE`; bad-ending Gorea says
`INCOMPLETE`. Formatting is invariant and truncates fractional seconds. Existing
deaths and defeated-Hunter facts retain their current owners; EnemyKills remains
a separate Project Prime fact.

## Qualification

Pending frozen qualification of counter/save contracts, captured receipt retry,
native synthetic result layout and cross-target builds. Renderer/movie/story
runtime evidence must be stated separately from pure counter checks.

## Remaining and rollback

Original credits/results rendering, source glyphs/backgrounds, source result
placeholder binding and physical-device/story acceptance remain unfinished.
No setting or UI route activates an incomplete original frontend. Legacy saves
cannot recover missing past timing; they intentionally remain unknown.

Rollback removes the counter hook and nullable extension/display. Existing
progress, movies, kill/death counters and save/receipt ownership remain intact;
new timing JSON is additive data that older serializers may ignore.
