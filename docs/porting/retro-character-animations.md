# PORT-18: Retro ANCS/ANIM source decoding contracts

Ports the MIT-licensed Second Hunt ANCS animation-set parser and source
ANIM frame/pose decoder at pinned source commit `c16a907b3658c2fd4079e394a73711596a85caee`.

The bounded `RetroAnimationIndex.Parse` lists clip names and primitive
source animation IDs for subsequent character rig retargeting. Compressed
and uncompressed animation sampling is available through the adapted
source codec, but is **not** connected to Project Prime native player
animation, render pose, hitboxes, networking, or replay state.

Synthetic acceptance parses a minimal original-format ANCS animation set,
retains a blend's leaf IDs, samples quaternion interpolation, and rejects
unsupported compression and oversized sources without commercial assets.

Next work: match source CINF hierarchy to the existing Samus/Weavel
native model joints, validate foot placement and muzzle animations,
connect presentation-only renderer and implement controlled separate
character identity. Real-media and cross-device acceptance remain
mandatory before online play.
