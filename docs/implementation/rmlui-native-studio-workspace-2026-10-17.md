# Native Studio project workspace

The native Studio page now provides a local project library, eight-row pagination,
creation templates, typed/system-picked project paths, recovery browsing,
duplication, and confirmed deletion of saved user JSON projects. Project rows and
templates are native RmlUi buttons, with controller-owned selection. The existing
retained editor now supplies a native object viewport, authoring commands and
project transitions. Advanced geometry tools retain an explicit handoff; this
is not full native geometry-editor parity.

## Ownership

`NativeStudioEntryController` owns navigation, immutable view snapshots, selected
rows/templates, confirmation state, and cancellable catalog/picker generations.
Its backend does not expose a live `MapDocument`. Catalog reads run on a worker;
only the native owner pumps completions and requests an editor handoff. Retiring
the document cancels the current request; late results cannot open an editor.

`CurrentStudioLauncher` uses the existing `MapCatalog`, `MapTemplates`, native OS
file picker, and narrow `Shell`/`StartScreen` forwarding methods. The retained
`MapStudioScreen` remains the sole document owner. New partial methods use its
existing `Load`/writer paths. Native project decisions replace the visual unsaved
prompt while preserving its Save/Recovery/Cancel ordering. Source watching,
build jobs and playtest retain their existing owners. No paired
process or `PopOut` launch path is used.

Recovery rows delegate their original recovery path to
`MapDocument.ReadRecovery` on that retained owner and open as unsaved projects.
Recovery files cannot be duplicate/delete targets in the native library.

Saved-project deletion requires native confirmation and a regular JSON file
inside the user map root, outside recovery/Studio/install metadata directories.
The confirmation stores the exact length, modification stamp and SHA-256; the
backend checks them again and rechecks live editor path ownership before deleting
only that file. Retained hidden documents also veto deletion. Asset folders and
packages are retained.

`TryCaptureCommunityPublishIntent(out intent, out error)` is exposed through the
retained owner and `StartScreen`/`Shell`. An explicit Studio publication
action can capture the actual unsaved project into the existing detached intent.
The render owner opens the native Community publish modal with that frozen
intent. The existing transfer owner performs publication.

## Native action contract

`StudioAction` keeps existing indices 0–4 unchanged. Its library action family
occupies 0–30: refresh, pagination, open selection, create, project-library mode,
duplicate, delete/confirm/cancel, eight project selections, and eight template
selections. Native editor commands append 31–54; project decisions, object
creation/deletion and typed properties append 55–84, for 85 entries total. Only the
managed/native named-action family changes; there is no
network protocol or native packet-layout change. Generic text/IME field handling
is unchanged.

## Validation

The existing `tools/rmlui-studio-check` now source-links the production controller,
file guard and typed-intent registry without building the main application.

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-studio-check -c Release
```

The initial library suite passed 83 checks. The earlier expanded editor/library
suite passed 138 checks, with zero build warnings/errors. Checks cover
launch refusal/retirement, owner-thread execution, complete catalog pagination,
opaque selection keys, template validation, cancelled and stale picker/catalog
results, immutable snapshot reuse, confirmation/single-use behavior, newly
acquired editor ownership, same-size/same-timestamp replacement, real temporary
file SHA checks, symlink/root boundaries, recovery protection, and all 31 typed
original action round-trips plus the appended editor actions (55 total).
Deletion checks use temporary synthetic files; no existing
user project, application, editor process, account, network or GL context is used.

Opening Delete moves focus into the native confirmation after its visibility
binding applies. Unrelated picker, path and template commands clear the pending
confirmation; cancellation returns focus to the current row.

`StudioGlAcceptance` is prepared in the existing GL tool with fake memory-only
catalog/editor/picker/delete owners. It exercises actual native rows, templates,
pagination, typed text, picker cancellation/retirement, delete confirmation and
Back/focus. Its pre-publication checkpoint passed 86 checks at each actual
640×360, 1280×720 and 1920×1080. It never creates an actual editor process or
reads map assets/files.

The explicit `PUBLISH CURRENT EDITOR PROJECT` button reuses the validated
`community:detail:5` intent. Only the current foreground Studio document may
request a publish capture; duplicate sequences, busy reads and retired documents
are rejected. `TryTakePublishRequest()` consumes the request once. The compositor
owns the synchronous retained-editor capture and Community modal handoff, using
`native_studio_publish` for return focus. Selecting a library row does not load a
project for publishing. The prepared GL fixture additionally checks single-use,
duplicate/wrong-document rejection, covered-page retirement and busy disabling;
these added checks await the next coordinated build/run.

The existing `tools/map-editor-check/Program.cs` autosave/recovery cases remain
the regression oracle for the real writer: detached payload, exact recovery
round-trip, latest snapshot wins, and closed writer rejection. Main application
build, native GL layout/input acceptance and real retained-editor handoff tests
remain central verification work.

## Native object editor slice

`EDIT CURRENT PROJECT HERE` requests an exclusive presentation lease on the
existing retained `MapStudioScreen`. The native editor page provides object
hierarchy pagination, click selection, finite numeric Move/Y-Rotate/Scale,
Undo/Redo, a typed JSON save path, frame/view commands and orbit/pan/zoom.
An absent retained project produces a native status message. Native project
creation/open/recovery can install the next document directly into that same
cached owner and return its native editor lease.

`MapStudioNativeSession` preserves the exact `MapDocument`, history, autosave
owner, validation timer and source watcher while legacy controls are hidden.
The visual attachment and native lease jointly control the existing owner's
lifetime. Acquiring a lease retires and awaits an older detached writer before
resuming; close uses the existing final recovery ordering and vetoes navigation
if preservation fails. Retiring the page releases input and the lease. Legacy
handback keeps the retained document/history; no second document, application
loop, network pump, game simulation or window is created.

`MapViewport.NativePresentationLeased` transfers presentation of the existing
active viewport away from `UiSurface`. Native drawing calls its existing
`PrepareRenderer`/`Scene.DrawEditorFrame` resource owner, frame/cache/material
pipeline and geometry picker on the same RenderWindow GL thread. The native
viewport rectangle stays transparent so the geometry is drawn before native
chrome. Drawing and picking share its contained framebuffer rectangle. Client
coordinates scale once; out-of-rectangle, modal, focus, resize and retirement
paths release camera capture. Renderer failure disables picking and reports a
native status while retaining the document.

Hierarchy slots resolve frozen object IDs. Inspector drafts retain their lease,
document state, revision and selection ticket across background refreshes.
An external change requires explicit reselection before Apply. Each Apply calls
the existing `TransformSelection` history command once. Parsing is invariant,
bounded and rejects nonfinite values; the retained owner independently enforces
the bounds and ticket. Existing Studio values 0–30 remain unchanged; bounded
editor actions append 31–54 without changing the wire kind/struct/protocol.

The 138 source-linked checks additionally cover stale inspector drafts after
refresh, exact hierarchy identities/paging, numeric locale/size/range rejection,
lease replacement, owner command delegation, recovery-close veto and thread
ownership. `StudioEditorOwnerAcceptance.Run()` is prepared for central
`--studio-owner` verification: it uses the actual retained owner, synthetic maps
and private temporary files to test transforms/history/save, exact newest
recovery after an older writer, reacquisition and real recovery I/O failure.
It creates no renderer/Scene, game window or application/network loop. This
basic production owner fixture subsequently passed 32 assertions, and actual
native editor GL passed 228 assertions at each of 640×360, 1280×720 and
1920×1080 in the central RML57 checkpoint. Those counts precede slice A.

The existing GL tool now has a separate prepared `--studio-editor` fixture
(`StudioEditorGlAcceptance.Run(host,width,height)`). It requires isolated
userdata at process launch and uses a synthetic map with the real retained
owner and viewport renderer. It checks actual native editing/save controls,
geometry picking/cache rebuilds, renderer identity and compositor GL state,
camera input containment, density-only changes, resize-before-draw rejection,
post-update focus/modal release, and Back. PNGs and its scoped manifest stay
under that isolated userdata. `--studio-owner` is the separate headless
production writer/lifecycle fixture; neither command is a performance or device
qualification.

Full geometry-editor parity remains open: native gizmos, sub-object/UV tools,
material/model/import browsers, NetRadiant workflows, quad views and build/
playtest controls still use the explicit advanced-editor handoff to the same
retained owner.

## Slice A: native transitions and object authoring

`NativeStudioProjectChanges` stages template or file data on a worker while the
current document remains installed. The owner pins document identity, history
state and generation. Source JSON/package and optional recovery context each
carry canonical path, length, modification time and SHA-256; commit rechecks
them. CPU authoring preflight rejects malformed candidate shapes before the
existing `Load` operation. A failed installation restores the previous exact
document/history, viewport host tree and pending source notices.

`NativeStudioProjectChangePage` presents Save and Continue, Keep Recovery and
Continue, and Cancel in native RmlUi. An existing alternative save target needs
an explicit reviewed overwrite; content replacement invalidates that decision
even with identical length/time. Manual save cannot target the reserved recovery
directory, including its context sidecars. Rejected path preflight preserves
the same autosave writer. Actual Save and Preserve retire/await the existing
writer before continuing. Old-document retirement occurs only after successful
preservation; cancellation retains it. A consumed committed lease is removed
from the pending owner before modal/entry retirement.

The editor adds nine known object creation controls, duplication, reviewed
object deletion, and eight visible typed property slots with paging. Shared
`MapStudioObjectCommands` and `MapObjectPropertySchema` issue the existing
`MapDocument` history operations. The latter uses explicit known field schemas,
immutable choices, invariant finite parsing and one atomic patch. Property
drafts retain their document/selection/schema ticket across refresh/page changes;
an external owner edit invalidates Apply. No-op patches add no history command.
The same retained viewport/render owner presents every change.

Slice A source-linked controller/file-version checks: **203 PASS**, log
`/tmp/prime-native-studio-slice-a-pure.log`; asset source checker: **110 markup/
styles PASS**. The existing headless `--studio-owner` fixture now invokes real
object/property and cold-owner transition regressions; `--studio-editor` adds
actual native create/property controls and Save/Preserve/Cancel/overwrite modal
acceptance. Actual central RML58 GL subsequently passed **672 assertions at each
640×360, 1280×720 and 1920×1080**, including these property/transition helpers.
The initial headless Recovery check exposed a fixture setup error: the source
template had a nominal path but no loaded-file base directory. Diagnostics
showed exact recovered bytes, original null base context, correct source path
and an unsaved document. The fixture now writes/loads a real private original
JSON before capturing recovery, retaining the exact assertions. Its isolated
owner-only project references the unchanged RML58 client SHA-256
`c73eb2864075178ea3be2f05a2a0400c890498bcb79449bfbcd800600e66adc6` and passes
**259 actual owner assertions**. Initial failure diagnostics and corrected pass
are preserved under `artifacts/rmlui-studio-a-owner-diagnostic-20261017/`.
They use private synthetic JSON/recovery only and make no account, network,
application-loop, device or performance claim.

## Slice B: materials and declared asset references

The same editor page now has Objects, Materials and Assets views beside its
existing viewport. Eight-row material/asset lists and eight-field material
property pages use a dedicated portable controller. The retained session
implements `INativeStudioEditorResources`; it remains the only document,
history, autosave and source-watch authority. No routing, window, renderer or
native packet-layout owner is added.

Material controls add/duplicate, edit known surface/animation fields, assign
selected geometry, select usages, replace usages and confirm unused removal.
`MapMaterialPropertySchema` parses the complete patch before one `EditMaterial`
operation, preserves existing texture channels/flipbook frames and identities,
and reuses `MapValidator` for material/animation rules. Existing unrelated
diagnostics remain editable; newly introduced material or shared animation
budget errors are rejected. No-op patches create no history state.

Asset controls operate on declared references: logical rename, usage selection,
known native/HD channel binding/clearing and confirmed unused-reference removal.
`MapStudioResourceCommands` reuses the existing `MapMaterialEditing` remapping
and document command writers. Removing a reference retains physical files for
Undo and recovery. Native fallback binding requires a declared `.tex`; HD
channels require a declared PNG/JPEG/TGA/KTX2. Refresh uses the existing viewport
material cache. This slice does not import, replace or export asset bytes and
does not claim that every declared file has been verified.

Resource tickets pin the current lease, document state/revision/selection,
material index/ID/fingerprint, selected asset path/metadata fingerprint, and the
existing source-watch notification set and monotonic delivery generation (so a
second change to an already-pending path also invalidates it). Background refresh retains the old
draft ticket; a changed owner or source notice rejects Apply/confirmation.
Native controls use fixed semantic actions, with separate material/asset
confirmation and view checks. Studio actions 85–138 are coordinated centrally
as 139 total entries, preserving all earlier values and the wire layout.

The initial staged direct-client suite passed **77 checks** with zero compiler
warnings/errors: 65 real document material/asset/history/validator assertions
and 12 portable controller delegation/ticket assertions. The durable
source-linked Studio suite passes **278 checks** after adding that controller
coverage. Logs: `/tmp/prime-studio-b-staged-check.log` and
`/tmp/prime-studio-b-last-painted-source-check.log`. The last-painted row
regressions replace/reorder owner data between paint and click; selection keeps
the displayed identity and old ticket, so the backend rejects the stale action
instead of refreshing the slot into a different resource. Actual retained-session and
native GL resource helpers are integrated into `--studio-owner` and
`--studio-editor` for the next central build/run; their new assertions remain
unmeasured at this source freeze. Private synthetic texture generation in the
GL fixture exercises the existing renderer, rather than claiming product file
import acceptance.
