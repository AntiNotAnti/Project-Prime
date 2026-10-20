# CW-05l — source-fenced pickup funding

This dependency slice gives an empty, already-bound player life an engine-only
pickup funding path. It does not place or render world items, activate public
pickup acquisition, remove the remaining public spawn grants, or change packets.
Those integrations remain required by the [pickup correction](05k-pickup-correction.md).

The scene supplies the expected match-start identity, frozen manifest and layout
digest when constructing the journal. A funding call must match that world,
exact owner generation/life, observed source frontier and current authority tick.
The world key deliberately excludes recipient bootstrap/connection identity.
The caller must verify a living current actor's actual contact, globally arbitrate
the item and reserve reliable outcome/state publication before funding it.

First acquisition creates the magazine and initial reserve unequipped, effective
on the source tick after contact. It does not reopen native selection permission,
reset the life clock or consume a custom action sequence. Earlier queued fire and
equip requests remain unowned even when processed after collection. Ordinary
selection and first-pickup auto-selection belong to the subsequent input adapter.

Repeated funding adds reserve, capped by the definition, without resetting the
magazine, reload, optic or fire deadline. Additions remain pending until a verified
contiguous custom action commits their source prefix. Reload completion and
engine interruptions are evaluated in source order around each addition. Read-only
HUD previews project the same order without committing resources. A repeated
pickup can be accepted at the reserve cap; the world adapter owns its consumption
policy and must not invent a second grant from a duplicate message.

Each life lazily allocates a bounded funding store: 128 pending additions and 64
spawn incarnation high-water marks. Duplicate/older incarnations, foreign worlds,
invalid identities and exhausted capacity refuse before resource, revision or
selection mutation. A separate monotonic pickup revision records funding even
when no custom control has settled. Old incarnations remain fenced after their
resource additions commit. Retirement discards pending funding and invalidates
owned states; a new life requires a fresh journal and world-global item state.
