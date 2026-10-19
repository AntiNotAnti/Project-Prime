# RML-40: Native Hunter License public-profile opt-out

Date: October 17, 2026. Continues the local RML-39 career navigation slice.

The opt-in native License can hide the public Hunter profile and explicitly
persist a private preference even when the current visibility is already
private. Showing a profile again, recovery, account linking and other account
actions remain in Full Hunter License. Full Settings stays last.

## Account and request ownership

The existing `HunterLicenseClient` remains the sole authentication owner.
The native adapter retains the loaded profile's account UUID privately and
issues an opaque process-local lease to its controller. Neither the UUID,
the lease, email, tokens nor credentials become document bindings. Only a
verified record with known visibility and a current lease enables opt-out.

The native write is fixed to **private**. Under the existing authentication
gate, it obtains one session, verifies the session's user with the existing
authenticated user endpoint, compares that user to the loaded account,
persists `false` through the existing visibility RPC, and rereads visibility
with that same token. Account recovery cannot interleave those steps. A
changed/invalid account rejects before the preference write. The legacy
account UI's existing general visibility API remains intact.

Admission retires the rendered account/history while the request is pending.
The controller restores the captured record with PRIVATE only after the
same-account write and reread both confirm it. It does not create new career
facts or combine data from a different account. Failed or cancelled saves
remain **NOT CONFIRMED**, disable repeat writes and require a fresh sync;
cancellation never promises that an already-persisted write was rolled back.
Refresh, page disposal and stale epochs discard late results. Service error
bodies are not displayed.

`license:hide-profile` is appended as License action 21. Existing action
numbers, the native ABI structure and gameplay/network/replay protocols do
not change. Native packages must rebuild the bridge from the updated source.

## Verification

The License fixture covers offline/unknown/read-only/missing-lease admission,
explicit private persistence, duplicate suppression, owner-thread/off-thread
boundaries, exact captured-account restoration, error redaction, cancellation,
refresh/account replacement and page disposal. It also exercises the actual
privacy transaction with controlled delegates: changed owner prevents writes;
write/read ordering and both server booleans must match private; cancellation
after persistence cannot report success.

The GL fixture also exercises the production adapter with injected in-memory
service delegates. In particular, a cancelled older load must leave a newer
lease usable without invoking the service; new and out-of-order loads must
retire stale write admission. The cancelled-token check lives inside the lease
lock, before changing its generation. Fixture DTOs and actual document
bindings are checked for omission of account/match UUIDs and email.

The native GL fixture uses deterministic synthetic service data and actual
pointer clicks, verifies disabled pending writes and explicit re-save, checks
wrong-document/modal/sequence rejection, and cancels a save whose simulated
backend subsequently completes. A fresh sync recovers authoritative display.
Its captures and metadata are stored in `artifacts/rmlui-license-rml40/`;
the existing Linux GL job retains that directory as fixture evidence.

Local results: the opt-in desktop build, dedicated-server build, License
privacy/career fixtures, runtime contracts and live-menu contracts passed.
The server build had zero warnings/errors; the desktop build retained the
four existing menu/lobby nullable warnings. All 87 authored assets matched
their packaged runtime files. Actual macOS OpenGL clicks, production-adapter
regressions and synthetic captures passed at framebuffer 1920×1080,
1280×720 and 640×360 on Apple M4 Pro (`2.1 Metal - 91.7`, macOS 27.0.0).
Remote CI for this working-tree continuation has not been run.

## Acceptance limits

This implements and tests the client flow; it does not certify a deployed
backend, public website visibility, Windows/Linux hardware, physical Android
touch/IME/resume, or historical screenshot parity. Live two-account acceptance
remains outstanding. A second device can change visibility after any successful
readback; the display reports that verified observation, not an atomic global
revision guarantee. No database/schema change or live account mutation is
performed by this implementation task. Native remains opt-in, Party's backend
remains staged, CPP-01 stays skipped by user direction, and CPP-02 remains open.
