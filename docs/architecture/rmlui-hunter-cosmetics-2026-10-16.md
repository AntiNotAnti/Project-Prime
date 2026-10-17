# RML-24: Native cosmetic loadout selection (full Settings remains last)

## Restored functionality

The opt-in desktop Hunter picker now exposes the three cosmetic categories
from the current catalog: Hunter-specific skins, armor effects, and death
presentations. The original RML-era presenter depended on a retired
HunterSelectionController, so the port uses an independent, owner-thread
LauncherCosmeticDraftController rather than reinstating legacy services.

- Cycle canonical CosmeticCatalog keys, enforcing Hunter-specific skin filters.
- Keep edits private until an explicit EQUIP COSMETICS action. The independent
  Hunter/Suit draft remains intact; changing Hunters is blocked while an
  unsaved cosmetic draft exists.
- Reset discards and reloads live CosmeticPersistence state, so a second
  settings/license surface's changes cannot be overwritten.
- Before Equip, recheck saved loadout; stale drafts fail with a conflict status.
- Equip delegates to HunterLicenseClient.UpdateCosmeticAsync. This preserves
  the current local-first durable cosmetic persistence and authenticated
  account synchronization, including honest LOCAL / NOT SYNCED status.
- The HTTP operation never blocks the OpenGL/UI render owner. Completion
  is polled on that thread and retired/cancelled with the native page.
- No new entitlement, cosmetic wire ID, duplicated account API, or server
  session is created.

## Coverage and limits

The asset-free Hunter fixture verifies each catalog category, local-only edits,
explicit single Equip, external persistence conflicts, cancellation, pending
sync state, Hunter filtering and owner thread enforcement.

No false 3D preview is displayed. Rich cinematic preview and custom Hunter
models still require a separate live renderer ownership/integration slice,
including context-loss and GPU acceptance. Cosmetics can already be equipped
and synced through the existing Hunter License path.

Default client behavior, gameplay and Android packaging remain unchanged.
Full Settings is reserved for the **final** migration phase, after the
remaining non-Settings workflows and platform acceptance are complete.
