# CW-05o — cycling acquired M7

Next/Previous Weapon now enters an owned M7 through the ordinary fixed input
sample. The resolver returns a tagged native/custom target; it never adds M7 to
the native nine-beam order, native availability/ammo storage or Intent.WeaponSelect.
No match flag or cycle edge grants ownership.

The existing native order stays intact, with M7 after Omega Cannon in the full
cycle. Native eligibility uses the player's existing CanCycleToWeapon checks;
unavailable or empty native weapons are skipped. An unselected M7 with no
magazine or reserve is skipped; a selected empty M7 remains a valid anchor from
which to leave. With Scroll All Weapons off, the six native affinity candidates
retain their order, followed by M7. The held native fallback remains reachable,
including Power Beam or Missile. If that excluded fallback is exhausted and no
affinity candidate is usable, usable Power Beam provides an exit.

A current owner/bootstrap/manifest-matched read-only resource snapshot is required
before intercepting a physical cycle edge. The custom input controller's reversible
selection supplies the anchor. Direct native bindings and wheel release retain
priority; simultaneous Next/Previous follows native Next priority. Selection and
fire continue through the existing ordered input sink and native suppression
fence, so returning to native cannot debit or emit both weapons in one sample.

The scene stages a native target once before native intent capture. The normal
player item/input phase consumes it once in that same frame and rechecks full
owner/bootstrap/manifest before calling the existing native TryEquipWeapon path.
Native-only/unowned scenes retain their original cycle implementation. Missing
online private state retains uncertainty suppression instead of implying ownership.
A stale staged choice cannot apply to a subsequent frame or life.

This slice changes cycle routing, not wheel presentation. Acquired M7 wheel entry,
public online world pickups/ownership knowledge and final physical-device checks
remain CW-05 work. Synthetic keyboard and existing controller-bind regressions
do not qualify a physical controller, touchscreen or online pickup experience.
