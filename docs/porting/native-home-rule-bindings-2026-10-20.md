# Native Home rule bindings

Frozen source `5b82fe12935f6cb8cc5a546219b665e878f1791b` repairs the
legacy Home data model after Movement17 and M7 18 were added to managed rule
selection. The native model previously allocated/bound only0..16; generic
native action translation stopped at17. One native count19 now sizes the model
and bounds translation, and both missing text/event bindings are present.
Their existing lobby/rules-open/owner guards are preserved. No protocol or
intent ABI change is made.

A real CPU-native regression initially reproduces the missing labels/callbacks
and rejected generic M7 action. After that repair, restoring lobby state
exposed a separate production cache defect: entering lobby mode internally
closes the rules, but a cached requested `rules_open=true` suppressed the next
snapshot's update. Home model boolean mutations now invalidate dependent native
boolean cache entries before applying the setter. Ordinary DOM binding caches
retain their existing behavior. The same fixture now verifies actual model
state and all three callbacks after restoration, rather than relaxing the test.

All13 targeted serial commands pass. All6033 tracked files in both frozen
archives remain unchanged. Three native libraries rebuild and verify against
source fingerprint
`d7bdbe6338ab106252684593fd143383095c7086ecd1e998058ca1c9952d16e6`.
The regression passes143 assertions using invented markup and real pointer
clicks, data-model callbacks16/17/18, repeated label updates, actual guarded
boolean state, generic18 acceptance,19 rejection, current document/generation,
monotonic exactly-once events and temporary asset cleanup.

Current Host source-linked tests build without warnings/errors and pass real
native integration,197 managed canvas owner checks and51 scheduling assertions.
The unchanged PR840 shipping-core/canvas harness additionally passes1085 native
CPU ABI,173 actual managed-reader and776 actual GL checks, including242 pixel
checks at1920x1200 and2560x1440. Core/harness hashes remain unchanged. This
scoped reuse does not claim a fresh whole-game build of concurrent CW-05h.
Android ARM64/x64 native libraries compile and pass architecture/alignment,
export/dependency/license/fingerprint verification; this follow-up does not
claim a new Android managed/AOT/APK or physical-device qualification.

| Native library | SHA256 |
| --- | --- |
| Mac GL2 | `72cafa3ffcc0fb475e598e45ddf835c45c4541ac63f94e6ac1838157e4e93840` |
| Android ARM64 | `25a336e149da38466a20ee635deee34229cb18ad61944eb5e889370768fd8918` |
| Android x64 | `b2688c6a363272feeca629e5dbaa31e5994e99b81ae5ac53754d7ddf2886e534` |

Receipts: `artifacts/sh-integration/home-rules-5b82fe129`. The earlier red
missing-binding run and partial-fix restoration failure/diagnostic are retained
in the adjacent `home-rules-9ff0d19b3` receipts. Source fonts are open runtime
fonts; no game artwork, source text or saves are read. Tests use the authorized
Mac fallback following the recorded Windows authentication refusal. No CI or
external review is claimed.
