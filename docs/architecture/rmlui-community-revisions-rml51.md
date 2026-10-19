# Native Community revision journeys

The native Community Discover and My Uploads pages open a foreground RmlUi revision-history modal. It pages eight immutable revisions at a time, displays the exact package SHA-256 and parent, and passes a selected published revision to the existing Community installation controller/transfer queue. It does not substitute the project's newest package for a historical selection. Draft, unlisted and deleted revisions cannot enter that anonymous installation path.

Owned histories retain an opaque auth-session lease from the displayed My Uploads result. Account UUID, owner identity, lease and captured history witnesses are internal, explicitly ignored by JSON serialization, and absent from document bindings. The existing auth gate pins one verified account and narrow Community ticket for each transaction; credential callbacks never re-enter the auth gate. An account change or conservative session refresh clears cached private project/history rows and pending confirmations.

The selected-revision actions are Make Current, Publish Retained Revision, Unlist, Make Private Draft, Delete and Restore. Each requires confirmation of the exact revision/hash. Production preflight re-reads the owned project and full history, checking the captured account/session, owner, current/latest hashes, revision number, immutable package metadata and visibility/deletion state. It reuses the established Community map operations and validates fresh readback before publishing any changed state to the UI.

The existing server authority determines pointer behavior:

- Publishing or promoting a retained revision makes its package public and current; package bytes and newer immutable revisions remain.
- Unlisting or drafting the current package selects the highest remaining active public revision, or leaves no public current pointer.
- Revision deletion is a retained soft deletion. The server refuses deletion of the last active revision and recomputes latest/current pointers.
- Restore obeys the service's retention deadline. It restores the retained package without replacing an already valid current public revision.

Readback checks preserve the complete immutable number/hash/parent/package history and reject changes to unrelated revision flags. My Uploads refreshes on return so changes to the current/latest pointer do not leave a stale project action witness.

These endpoints do **not** accept a server-side expected-hash or revision compare-and-swap parameter. Client preflight and readback detect stale or inconsistent state; they do not make separate HTTP calls atomic against another writer. Cancelling a route prevents a late completion from publishing UI state and cancels its wait, but cannot roll back a change already accepted by the service. Cancelling installation similarly retains the existing durable transfer receipt.

If a write outcome cannot be verified, the modal removes action authority and explicitly reports an unverified outcome. Refresh may then reconcile the same map under the same current account lease even if deletion/restoration changed its latest pointer. This is a read-only recovery: fresh ownership/permissions and complete history are still required, the new anchor is adopted only after validation, and the next mutation requires a new confirmation with strict witnesses. Publication and retirement share a short gate; retirement drains queued private catalogs and prevents older workers from enqueueing after cancellation.

`tools/rmlui-community-revisions-check` links the portable production controller/policy and uses synthetic authorities. It covers exact historical selection, account/route cancellation, confirmation, malformed catalogs, JSON lease redaction, lifecycle pointer constraints, failed writes, changed preflight witnesses and rejected readback. No backend writes, credentials, game files or installations are used.

The earlier native history/promotion GL fixture is separately labelled synthetic and runs the real RmlUi pages with fake authorities. New lifecycle layout/input requires its own composed acceptance after the centrally coordinated application/tool build. This batch does not implement new-package publication: that journey must receive an existing frozen `CommunityStudioPublishIntent` from the editor owner and preserve its expected-parent/conflict handling.
