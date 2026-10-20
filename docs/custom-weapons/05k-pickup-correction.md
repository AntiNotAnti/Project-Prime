# CW-05k — pickup acquisition correction

The user's October 20, 2026 clarification takes precedence over the attached CW-05
loadout/menu proposal: the M7 is a world pickup, acquired and selected through
normal gameplay. Custom equip/return pause buttons are removed. The abandoned
responsive-menu work is preserved locally and is not part of this change.

This removal slice retires the UI-only desired-selection mailbox, its cached
publication and obsolete tests. Existing physical custom input, native return
suppression, authority action validation, optics and HUD remain intact. Native
menu action IDs 0–33 retain their meanings. Rules availability remains an opt-in
match configuration; it will mean enabling M7 pickup placements, not granting a
weapon to each spawning player.

Pickup integration follows immediately as separate reviewed slices. The old
public spawn-grant behavior is not the target gameplay and CW-05 remains
incomplete until it is replaced. Explicit developer-only grants may remain for
combat fixtures. Do not describe this removal as a delivered world pickup.

## Required pickup contract

- Keep existing native pickups, ItemType/BeamType IDs, arrays and packets intact.
- Author explicit additional M7 placements with stable spawn identity, position,
  delay and respawn policy; do not silently replace Imperialist pickups.
- Start each public life without M7 ownership. Actual valid contact grants once;
  death/respawn starts empty. First acquisition follows normal auto-selection
  conditions; subsequent selection uses ordinary weapon-cycle/wheel input.
- Custom identity stays tagged at the native/custom boundary; runtime ID 10 must
  never enter a native weapon array or native Intent.WeaponSelect.
- Online authority owns item availability, contention, grant/refill and respawn.
  Reserve bounded outcome/state delivery before changing item or inventory.
  Replicas cannot grant from local overlap.
- Fence pickup incarnation, match/epoch/map, owner generation/life and manifest.
  Reordered/duplicate pickup messages cannot refill. Retained input sourced before
  acquisition remains unowned; pickup cannot reset source/selection history.
- Preserve reload/cooldown on repeated pickup. Publish empty ownership on respawn
  and correct world-item state to late join/reconnect. Existing flights retain
  their original payment identity after shooter death.

Native timed-powerup claim packets describe three timed effects with fixed expiry;
they cannot safely encode this weapon. Reuse their bounded delivery and contact
patterns, not their enum or payload. Physical/device and full CW-16 acceptance
remain open. The Android candidate discovery also found its offline launch path
missing custom runtime activation; repair belongs with pickup activation rather
than restoring automatic spawn grants.
