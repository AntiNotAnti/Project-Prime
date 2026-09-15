# Supported cartridge identities

Project Prime accepts a cartridge dump for extraction only when its complete
image matches an immutable catalog entry. The header game code and revision
select a candidate; the whole-image byte length and SHA-256 digest are the
acceptance key. A valid header never authorizes extraction by itself.

## Current catalog

| Variant | Family | Expected image length | Whole-image SHA-256 | Evidence |
| --- | --- | ---: | --- | --- |
| AMHE0 | Hunters, USA Rev 0 | 67,108,864 | `7d0a98ff98e1b7c985d1f3d89b01730af1b2115061a4dfea847612d217a8b855` | Public independently verified whole-ROM reference |
| AMHE1 | Hunters, USA Rev 1 | 67,108,864 | `bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f` | Project Prime local/repository whole-ROM evidence |

The digests above are for the complete `.nds` images, not ARM9 payloads or
extracted files. The AMHE1 whole-ROM assertion is also recorded in the
[AMHE1 ranking evidence](G4_RANKING_SPEC.md); its ARM9 digest is intentionally
not used for cartridge acceptance.

## Evidence boundary

The repository knows historical header families including AMHP, AMHJ, AMHK,
A76E, AMFE, and AMFP, but it does not currently have independently verified
whole-ROM identities for those variants. They therefore fail closed as
unsupported, even when their header game code and revision look familiar.
Adding one requires an independently verified complete-image length and
SHA-256 pair; an ARM9 hash, a header-only match, or a guessed digest is not
enough.

Validation happens while the setup lock is held and before `Paths` is updated,
the ROM filesystem is traversed, or any extracted output is written. A
header-valid hash mismatch is reported separately from an unsupported header
and from an image read failure. There is no development override for the
catalog.
