# CPP-08/09: supplied local discs verified on macOS

The user supplied `Documents/MPH/Repos/SecondHunt` and `Documents/MPH/Roms`, then
added the original Echoes image. This supersedes the earlier bounded inventory's
absence of configured external sources. The SecondHunt folder is a source
snapshot without Git metadata; actual reader/recipe/license hashes identify it.

Two private source-linked probes use the existing donor DiscImage reader and
read the original RVZ files without extraction. Both exit zero, retain exact
whole-disc SHA256 before/after, and match every selected expected PAK SHA1.
Metadata-only receipts, source/tool identities and command logs are under
`artifacts/cpp08-cpp09-local-disc-proofs-20261018`. No payload or keys are copied.

| Source | Actual identity | Whole-file SHA256 |
| --- | --- | --- |
| Metroid Prime Trilogy (USA).rvz | Wii, R3ME01, revision 0 | `9e12e697a1b6613b1307d4e7801d005d60d91def6997a0e622f119907b997c77` |
| Metroid Prime 2 - Echoes (USA).rvz | GameCube, G2ME01, revision 0 | `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529` |

Trilogy's six selected PAK proofs cover the admitted four Trooper bodies,
Corruption Dark Samus body and separate HD appearance recipes. The original
Echoes image supplies the exact native MP2 Dark Samus and six-arena archives:

| Archive | Bytes | Expected and measured SHA1 |
| --- | ---: | --- |
| Metroid1.pak | 183,991,840 | `6b9a47578f41b2c64a12f1a0bf4e5cd783692124` |
| Metroid5.pak | 134,245,760 | `b3375e83c5290d35d6c83f35b04b7f60f6fb8ea7` |
| Metroid6.pak | 21,994,208 | `b8b92af37dc772ecd8234fb1ce664be0493188f6` |

Echoes AudioGrp is measured separately, with no reviewed expected pin; the Prime
AudioGrp pin is not reused. Trilogy's edition allowlist still excludes native
MP2 Dark Samus and the six multiplayer arenas. Their source here is the actual
original Echoes image.

Project's current production GameCubeDiscReader rejects compressed RVZ. A private
bounded GC-only logical-stream adapter is being qualified through that existing
reader and existing source/PAK receipt owners. It does not imply Wii/Trilogy/MP3
support. Private donor body-only imports are also being prepared with explicit
output roots and unchanged edition/source validation; they are separate from
Project's own model format and runtime activation.

These proofs establish real source availability and archive identity. They do
not establish converted characters, source clip playback, cannons, alternate-form
mechanics, converted arena geometry/materials/navigation, online/replay behavior
or release/package acceptance. No source content is added to a build or network.
