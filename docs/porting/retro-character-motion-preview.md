# PORT-19: sparse source motion previews for Dark Samus and Troopers

The locally owned Prime or Echoes `-retrocharacterexport` workflow now
emits four private authoring outputs per imported character:

- `<character>.obj`: unanimated local geometry
- `<character>.skeleton.json`: imported CINF bone IDs, names and bind positions
- `<character>.motions.json`: bounded ANCS clip index and sparse ANIM pose samples
- `<character>.weights.json`: exact CSKR bone influence groups and per-vertex group references for reconstructing a skinned rig

Each animation record contains its original source resource IDs and its
status. The importer samples **at most 12 single-primitive clips** at
five points each, preserving bone IDs, rotations and translations. Composites,
unsupported timings, inaccessible resources and malformed motions are
explicitly reported rather than approximated as playable clips.

No original game discs, models, textures, animations or private exports
are committed or transmitted to Community. The output is a local artist
preview, not a runtime animation, Hunter selector option, replacement
rig, network message or replay change.

Asset-free acceptance covers source clip identity, interpolation, timing
guards, missing source clips and malformed quaternions. Actual game-media
retargeting, texture fidelity, first-person cannon alignment, animation
blending and in-game execution are still required before playability.

The weight sidecar uses bounded deduplicated source groups, preserves original bone IDs and ratios, and rejects missing/nonfinite influences. This does not make the OBJ itself skinned or playable.
