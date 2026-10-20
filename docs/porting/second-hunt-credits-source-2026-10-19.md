# Original credits/results source admission

This SH-08 slice adds a bounded, detached reader for the original frontend data
needed by credits and results. It does not activate presentation, load native
models or animations, execute callbacks, change audio, write saves or create a
Scene. The existing ending trigger, committed-save receipt, native controller
and fallback results page remain the owners of the current flow.

Layout evidence is the existing `Formats/Frontend.cs` and the MIT-licensed
[MenuFile.cs reference at the frozen source pin](https://github.com/BountyHunterKanden/SecondHunt/blob/41452f2d053fcfa679e33c00a73d120a53f59e61/src/MphRecomp.Core/Frontend/MenuFile.cs).
The original MIT notice remains in LICENSE. The bounded reader does not call
the existing debug frontend parser or import the upstream menu engine.

## Implemented

`CampaignCreditsSource.Read(marm, localizedText)` admits two read-only spans.
It retains only pages 6–12, 49 and 50, their referenced widgets and strings, and
source identities. Original page/widget/string indices remain unchanged.
Getter-only document collections and read-only nested collections own detached
values. Signed 20.12 positions, delays and durations retain their original
integers. State codes, action targets, call pairs and opaque flag fields remain
data; no executable callback or source pointer survives as an active owner.

Pointer-table scans are bounded independently from selected-record parsing.
Global widget tables may exceed 4096, as the owned source has 4699 pointers;
only referenced definitions are traversed. Every range uses widened arithmetic,
every pointer-list terminator is bounded, and a shared visit budget also counts
repeated aliases. This bounds work even when many legal lists share records.

| Bound | Limit |
| --- | ---: |
| Each MARM/text input | 4 MiB |
| Global pages / widgets / strings | 128 / 8,192 / 2,048 |
| Items/page; states/item; actions/list | 64; 16; 16 |
| Timers/page; call pairs/action | 8; 16 |
| Referenced widgets | 128 |
| Text per string / aggregate retained text | 4,096 bytes / 1 MiB |
| Path bytes / pointer-and-record visits | 512 / 32,768 |

The selected profile supports widget/text states, known state codes and action
kinds 0/1/3. Callback pairs `(0,11)`, `(0,12)` and `(0,206)` are retained as data.
An action's nonzero target code addresses an item on the **current** page;
source execution mutates that item before callbacks and a later page change.
Code 0 means no item-state mutation, item 65535 means no target and page 255 means
no page change. The reader validates these independently. Links, extra item
blocks, page extensions and unsupported state/action families are refused.

Text entries must have matching duplicate offsets and lengths. Declared spans
may be empty, contain a NUL, or have no in-span NUL. Latin1 preserves exact
source bytes and glyph escape pairs. The full declared span is retained;
content ends at its first NUL when present and otherwise uses its exact length.
External alignment bytes are not reinterpreted as text.

Filename spans require an in-span NUL. Logical paths normalize source
backslashes once while retaining case and spaces. Only the observed relative
roots `logo_screen`, `main menu`, `tostart` and `gameoptions`, with `.bin`
extensions, are admitted. Rooted, drive, URI, traversal, empty-segment and
unsupported-character forms are refused. Every segment also refuses trailing
spaces/dots, spaces before its first dot and Windows device-name stems (including
case variants and extensions), keeping the logical path safe across platforms.
Interior spaces and original case remain intact. Filename padding after NUL is ignored
as unexecuted padding, while declared lengths and opaque numeric fields remain.
The reader never resolves or opens these paths.

Nonzero state flags and item +48 fields are preserved rather than mistaken for
zero-required padding or a new pointer graph. The misleading existing
`MenuStruct1A3` size comment is corrected from 4 to 8 bytes; no raw format changes.

## Qualification

Frozen source `9de1f683c75cdd1e3664d5655c907cc70d85d62f` includes main's
concurrent Traversal merge; current protocol is 68. This reader changes no wire,
replay, asset or save format. Two independent 5,935-file git archives remained
byte-identical through 30 serial qualification commands. Windows SSH failed
with authentication refusal, so the user-authorized Mac fallback was used.

| Evidence | Result |
| --- | --- |
| Desktop / dedicated Release | Pass, zero warnings/errors |
| Android Release/AOT | Pass, 119 assemblies, 25 existing compiler/documentation warnings |
| Signed APK | v2/v3 signatures pass; arm64-v8a and x86_64 include ProjectPrime AOT |
| Credits generated fixtures | 239 pass; bounded malformed-pointer refusal allocates 912 bytes |
| External import suites | Pass, including 2,126 source-companion, 952 material, 191 texture, 137 texture-bank and 128 material-bank checks |
| Character contracts / owned body-ball probe | Pass; source RVZ hash unchanged |
| Map editor / custom weapon contracts | 502 / 10,833 checks pass |
| Historical replay suites and campaign map detail | Pass |
| Movies, lifetime, endings, presentation, audio, journey, committed-save suites | Pass |

The initial owned profile is AMHE1 English only; other revisions/languages
remain unqualified. The opt-in metadata-only probe admits 9 pages, 74 items,
213 states, 39 widgets, 48 strings, 25 existing paths and 18 actions including
7 timers. Both input hashes and document identities match before/after:

- MARM, 512,232 bytes: `1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059`
- English text, 30,604 bytes: `742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114`

The probe reads no model/animation payloads and writes no source content. Fresh
isolated contexts were removed. Local ignored receipts are in
`artifacts/sh-integration/credits-9de1f683c/{results,source-manifest,source-integrity-after-tests}.json`.

| Frozen output | SHA256 |
| --- | --- |
| Desktop DLL | `19771899ee6d571134955c787c879d4b715470559c769ef32090f13f0ed6c0ad` |
| Dedicated DLL | `9cee7d5ee326ad7249424ee8538493df1be77bfe1f264a4d9e3611acafe0eb8a` |
| Signed APK | `bf96458f0f04e50a0227ba522b6cb489bfd1d51f07e8404aeeb8f7723db299b2` |

The action-index regression and Windows-normalized path cases reproduced against
predecessor DLLs before their fixes. The completed predecessor `8b9e31e19`
qualification was superseded; it is not the final proof. No physical Android,
controller, Windows runtime or credits playback acceptance is claimed.

## Disabled, remaining and rollback

Original credits playback remains unimplemented. A following pure timeline
needs admitted animation lengths, settlement-relative 30 Hz timers, transition
state handling, non-skippable ending semantics and retirement fencing. Rendering
then needs the existing window/context/native UI bridge, original model/text
assets and the campaign audio epoch owner. Original timing/statistics records
need separate save-history and pre-teardown capture work; absent old values must
remain unknown rather than fabricated as zero.

No new presentation setting, route or endpoint is registered. Removing this
reader leaves the current ending/save/audio flow unchanged. Source data is local
and is neither packaged nor sent to Community, replay or network owners.
