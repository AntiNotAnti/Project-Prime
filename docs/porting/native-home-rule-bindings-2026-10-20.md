# Native Home rule bindings and state restoration

Frozen source `5351fa1005caf02e71877f21cbf411130d0b441e` repairs the
legacy Home data model after Movement17 and M7 18 were added to managed lobby
rule selection. Native allocated/bound only0..16; generic lobby-rule action
translation stopped at17. One native count19 now sizes the model and bounds
translation, and both missing text/event bindings are present. Their existing
lobby/rules-open/owner guards are preserved. Protocol70 and intent ABI1 remain
unchanged. This slice includes concurrent CW-05h PR839 in its frozen build.

## State and focus ownership

Entering lobby mode internally closes its rules and changes other model
booleans. The native cache previously remembered the old requested values, so
it suppressed the next rules-open update. Native Home model setters now
invalidate cached native boolean requests. The managed Host has its own
cache: after accepted Home mode publication it invalidates exactly the derived
Home fields, allowing the following snapshot fields to restore their values.
Unrelated document/text/DOM binding caches retain their existing behavior.

Clearing the native cache also exposed repeated opening-focus effects. The
rules-open setter now compares the actual model value before applying an
opening transition. An unchanged snapshot retains Movement/M7 keyboard focus;
a genuine false-to-true transition retains the authored map-control focus.
The tests preserve these distinct cases instead of relaxing focus assertions.

## Qualification

All40 serial commands pass. Both archives retain all6035 tracked files
unchanged. Desktop, dedicated and test Release builds have zero warnings/errors.
Default Android Release has25 compiler warnings; the optional GLES build has26,
including its existing nullable ByteBuffer warning. Both APKs have119 AOT
libraries on each arm64-v8a/x86_64 ABI and valid v2/v3 signatures. Both packaged
native libraries match the current source fingerprint, architecture/alignment,
exports, pinned dependencies, manifests and licenses.

The invented actual native regression passes166 checks using pointer clicks,
data-model callbacks16/17/18, repeated label updates, guarded actual model
state, generic lobby18 acceptance/19 rejection, current document/generation,
monotonic exactly-once events, restoration, focus retention and genuine reopen.
A separate12-check actual managed Host/native fixture reproduces and fixes the
managed-cache suppression through real SetBool/focus/Enter/intent ownership.
The red missing-binding, partial-restoration, focus and managed-cache failures
are retained in receipts. No original game artwork, text or saves enter these
fixtures.

The fresh shipping core and focused harness pass197 managed canvas checks,
1085 native CPU ABI checks,173 real reader/transport checks, pure GLES planning,
and776 actual GL checks including242 pixel checks over20 frames at1920x1200
and2560x1440. Current Host/native integration and51 real scheduling assertions
pass without the missing Home-variable warning. All3853 source-free credits
cases, six owned-source probes, historical replay, movie lifetime, ending,
presentation, save and campaign-time regressions pass. Owned probes retain
all33 source inputs, core/tool binaries and five caches unchanged.

| Artifact | SHA256 |
| --- | --- |
| Desktop DLL | `1f992f48014d72f031b4bca17e97ee277c7e6867388a9670db9bbeb625c5a6cc` |
| Dedicated DLL | `313c68cbf20647d733882867008499d536e02c43310a3968c016c49896690acb` |
| Default signed APK | `148f19db3e6dcc4aad5db0aeb90a3091da1913e43dc5da99175a96aefac6bc26` |
| Optional canvas signed APK | `a0435a870ee4a570fd02dfbf6df8b68eda8b8e7c0687c822072ab36edaf4b6c1` |
| osx-arm64 native | `3230ad135b169fef975c1938f7cbd61d639f9107f3dd7d1dae3b34360ba58717` |
| android-arm64 native | `d34c6cb4161825f1778bfecb28a3416f8dc8b46303853eb88a87d8d6d513c31d` |
| android-x64 native | `9c8394eb8ac1d8a738c6be8ea03e79db8f06eebca37426dab0811b50bc07babc` |

All three native builds share source fingerprint
`914d68f9a7678d41ca099231c2a04687d400bdb9eba40166220c546e5b322f40`.
Receipts: `artifacts/sh-integration/home-rules-final-5351fa100`. Preliminary
receipts `home-rules-9ff0d19b3` and `home-rules-5b82fe129` record the earlier
failures and narrower intermediate qualification, superseded by this full run.

Testing uses the authorized Mac fallback after the recorded Windows SSH
authentication refusal. No physical Android or Windows/Linux runtime, including
the GL sampler-object extension branch, is qualified. No CI or external review
is claimed. Original credits loading/activation and the broader integration
plan remain implementation work.
