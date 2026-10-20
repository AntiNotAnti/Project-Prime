# CW-05p — acquired M7 gameplay wheel

The gameplay weapon wheel now offers M7 only after acquisition. It cannot grant
ownership or resources. Native-only and unowned holds keep the existing six-slot
wheel. If a player acquires M7 while that native wheel is open, its targets stay
fixed until release; the acquired layout begins on the next hold.

An acquired hold copies the existing six-entry controller permutation, appends a
tagged M7 entry, and adds the held native return weapon when it is absent from
those six. If the held native weapon is exhausted, usable Power Beam is the
return. There are at most eight entries. Native sprite frames, BeamType values,
availability/ammo arrays, six-entry settings persistence and wire fields remain
native. The new labels use existing font drawing and flat boxes; no custom ID
is interpreted as an icon frame.

One layout supplies mouse drag order, controller sectors, pointer rectangles,
highlight and drawing. Drag skips unusable entries and stops at the ends.
Controller neutral and absolute pointers outside a target cancel. Stylus uses
its configured zone; native SEL contact opens the wheel, dragging selects a
shared rectangle and the existing debounced release commits. Native entries
use CanCycleToWeapon; M7 requires magazine or reserve. Exact cardinal controller
boundaries normalize in double precision to avoid float sector underflow.

A hold freezes owner/bootstrap/manifest, entry order and native return. Death,
readiness/identity loss or blocked gameplay cancels it. Release rechecks current
identity and eligibility, including fresh typed custom ammo rather than the
previous HUD tick's enabled bit. Explicit native/custom/cycle bindings retain
priority over a simultaneous wheel release. Native transitions use the existing
TryEquipWeapon path once in the same simulation frame. Custom transitions use
the existing Equip/Unequip sink and prediction/suppression fence, including
returning to the same native fallback. A neutral or stale release does not
silently select that fallback.

Drawing reads only the fixed-step wheel cache. It neither resolves live weapon
state nor submits controls, advances timers or funds an item. Shared pixel bounds
apply the same flat-HUD profile/aspect transform used for drawing; the native
flat drawing implementation is unchanged.

The native GL fixture covers actual public pickup, mouse round trips, synthetic
controller neutral/release with saved permutation and synthetic stylus SEL/drag/
release. A second mode acquires through real walking during a held native wheel.
A declared canonical depletion seam checks ammo becoming empty between highlight
and release. These are desktop synthetic-input checks, not physical-device or
Android touch qualification. Online pickup knowledge/delivery and final platform/
refresh/profile validation remain CW-05 work. Final M7 art remains CW-06.
