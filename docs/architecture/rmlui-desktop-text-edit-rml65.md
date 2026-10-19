# RmlUi desktop text editing, RML65

RmlUi's current `TextInputContext` remains the only widget/text writer. The
optional range API carries exact numeric ownership and UTF16 ranges; it does not
publish a field value, retain a managed editable-value cache or introduce a second
text model. All operations run on the existing engine owner thread.

## Wire and ownership contract

Protocol1, typed intent40, text-input state64 and old composition stages0–3 are
unchanged. Independently discovered exports are
`pp_rmlui_text_edit_state_v1` (state88) and `pp_rmlui_text_edit_v1` (request80).
Older native bridges retain their existing supported behavior; unsupported
explicit replacement ranges are consumed and reported rather than dropped into
range-less GLFW insertion.

The state exposes generation/document/focus epoch/revision, bounded lengths,
selection/marked ranges, protected flag and actual caret or field bounds. It
contains no text. Requests require the exact current foreground editable owner,
matching revision and increasing nonzero operation sequence. Invalid UTF8, NUL,
oversized fields, overflow, surrogate-interior endpoints and unknown flags fail
closed. Native mutation consumes its sequence before an effect. Direct replayed
C ABI requests remain rejected.

A same-Host monotonic allocator owns Cocoa-generated sequence numbers across
adapter detach/reattach, focus changes and host reinitialization. Exhaustion fails
closed. This is an operation identifier, not text/time-based deduplication. A new
Cocoa adapter does not reset the still-focused native owner's replay fence.

## Actual owner coverage

| Actual path | Invalidation or ownership rule |
| --- | --- |
| Document focus capture + `OnActivate` | Capture the actual focus event target and serial. Pinned Context dispatches focus before assigning its new focus pointer, so querying that pointer inside OnActivate would pair the old field with the new input owner. Activation consumes only the fresh captured target and checks it again after cancellation/reentry. |
| `OnDeactivate`, destroy, detach, reload and document retirement | Advance owner lifetime/revision, discard private restore authority and invalidate geometry. Listener lifetimes follow the document. |
| Physical key down and committed scalar | Conservatively invalidate before existing widget key/text processing, including selection, deletion and clipboard paths. Ordinary no-range text remains GLFW-owned. |
| Pointer button / drag selection | Invalidate before actual native selection/focus mutation. Stationary unpressed hover does not invalidate composition. |
| Captured DOM `input` / `change` | Observe the actual event target before action filtering. An unknown intervening edit retires private restore authority rather than restoring over new bytes. |
| `SetField`, deferred home model value write, accessibility SetValue | Notify the same text owner before actual SetValue. No callback is attached to an unrelated page cache. |
| Old and new composition operations | Internal mutation scope pins input/field/focus. Known reentrant external mutation advances revision/interference; readback cannot silently adopt it. |
| Hide/disable/background/computed CSS availability | Retire exact active owner. Real Update validates computed availability and foreground; a later reactivation gets a new focus lifetime. |
| Pending bindings before state/edit | Existing EnsureUpdated applies deferred values/visibility before taking or checking the witness. A changed value invalidates a queued request. |

These are the registered writers in this bridge. A future C++ scripting/plugin
writer must join the same hook; this contract does not claim to fence arbitrary
unregistered direct widget calls. Exported optional operations and owner callbacks
contain C++ exceptions; uncertain failure clears private authority without writing
into a replacement owner. Existing unrelated exports are not made universally
exception-safe by this slice.

## AppKit semantics

Real per-view Objective-C callbacks forward actual selected/replacement ranges.
Owned composition and explicit replacement suppress only that callback's original
GLFW insertion. Ordinary repeated no-range text still passes through GLFW. A
rejected/stale marked sequence keeps discard authority until a real new-key
boundary, including when Unmark arrives before any geometry refresh. Synchronous
`discardMarkedText` → Unmark reentry cannot clear this fence.

[Apple's Unmark contract](https://developer.apple.com/documentation/appkit/nstextinputclient/unmarktext%28%29?language=objc)
accepts the marked text and does nothing when none is marked. Commit flag1 with
empty payload implements that operation against the exact current owner. Normal
Unmark clears the marker and private cancel data while preserving current bytes
and selection. If preedit exceeds the authored maxlength, the native owner reads
only its exact marked substring, truncates at a scalar boundary through its
existing commit path and remaps selection. It never round-trips the whole field
through managed code. Other flag/kind/payload combinations are rejected.

Candidate geometry reuses the actual ActivateKeyboard position and line height;
width0 is valid. It is exact only for the same collapsed requested insertion
range and current sampled owner/layout. Reflow/scroll invalidates the sample.
Otherwise the callback returns honest field bounds and their actual whole-field
range. No character metrics or arbitrary-range caret location are guessed.

## Evidence and remaining validation

The preserved private candidate passed native65, managed range29, scheduling28,
existing desktop IME31, actual CPU Host/Cocoa lifecycle20, and actual macOS
programmatic Objective-C49 checks. The predecessor negative failed at same-focus
second attachment; numeric diagnosis proved native Duplicate result-3 for its
reused sequence1. Both the failure and fixed result are retained in
[the evidence directory](../evidence/rmlui-text-edit-rml65/).

That input-owner candidate used Replay124/Studio190. Its production patch was
narrowly applied to current Replay135/Studio200, preserving Timeline queued-action
source/focus guards. Matching production source hashes and the exact promotion
patch are recorded separately. Central production rebuilds and later validation
must be reported separately from the earlier private passes.

Durable fixtures and commands are in
[tools/rmlui-text-edit-check](../../tools/rmlui-text-edit-check/README.md).
The real AppKit fixture uses a NoAPI window and programmatic messages. Physical
keyboard/CJK IME, input-method server and candidate-panel behavior remain
unverified. No GL acceptance, Android runtime or unrelated UI parity is implied.
