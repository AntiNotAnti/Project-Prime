# CW-13 — typed Community weapon distribution

Weapon publication uses the existing authenticated v3 Community service, object pool,
creator ownership, compare-and-swap parent revision, storage quotas, request admission
and atomic catalog promotion. Maps retain their existing catalog and transfer paths.
`PROJECT_PRIME_WEAPON_COMMUNITY_PUBLISH=1` is required for new weapon publication or
promotion. Reads, reports and historical retention remain available when it is off.
An explicit owner-only visibility route publishes or withdraws an exact revision
without rewriting its archive. Both public and unlisted publication require the
gate; withdrawing to draft remains available. Re-uploading identical bytes with
different visibility refuses with instructions to use that route.

The `weapon` kind accepts only `application/vnd.projectprime.ppweapon`, at most 8 MiB.
The service parses the complete strict package before registration, including its
mechanics, original rigid model, license text and four bounded PCM cues. Direct
trusted registration also validates the whole package. External Community dependencies
are rejected: this package version is self-contained, and cannot masquerade as an
optional visual dependency. A project retains its original package ID and name;
revisions cannot change ownership or overwrite immutable bytes.

Generic v3 `contentHash` still means SHA-256 of the downloaded raw archive, with
`archiveHash=null`. The additive `weaponPackage` record separately exposes exact
archive, package-content, dependency and mechanics hashes. Downloads validate all
four identities, package ID, name and byte count before becoming verified jobs.
Metadata proofs are bounded and cached only for admitted immutable revisions;
actual object downloads still hash and parse the exact bytes.

Public catalog reads exclude drafts and unlisted-only projects. Direct weapon links
can read unlisted revisions; drafts require their owner. Archived or moderator-hidden
projects are unavailable to other readers. Owner archive/restore never clears a
moderator hide, and publishing another revision preserves that decision. Public
revision edges omit hidden draft parent hashes. Creator tickets are verified at
the HTTP boundary; no submitted owner ID independently grants permission.

Authenticated reports target one exact revision. Reporting is idempotent per creator,
project and revision, bounded to 20 new reports per creator/day and 4,096 retained
reports. Reasons are bounded printable text. Moderator-only routes list reports and
record hide/unhide decisions atomically with the catalog, retaining up to 32 decisions
per report. Report and moderator responses use `Cache-Control: private, no-store`.

The existing durable Community queue now supports typed weapon jobs alongside map
jobs, with the same 32 retained jobs and bounded workers. Uploads freeze exact bytes
and bind admission/retry to the original verified account. Private downloads and
installation retain that account boundary. Credentials never enter receipts. A
completed download requires a separate explicit install action, which revalidates
the full package and uses the existing atomic local store. Neither action grants or
equips inventory. Worker leases prevent cleanup or installation racing active jobs.

## Evidence and limits

Local compilation receipts are under `artifacts/custom-weapons/CW-13/compile-final/`.
They describe the combined worktree, which also contains subsequent Studio/UI drafts;
they are compilation evidence, not runtime or security qualification.

Final Release compilation: desktop exit 0, 0 errors / 2 existing warnings (98.17 s);
dedicated exit 0, 0 errors / 1 existing warning (69.92 s). Log SHA-256:
desktop `b90e327fdfdcd8a50266eec054cadb9791f1bc972263549102f4d5a1a69685a0`;
dedicated `36118bda75b9628eb0de1ac69d7e28e45291f9d8ca2d0381b257faca2d2c52bd`.

**NOT RUN per user instruction:** remaining tests, malicious-input/service restart
checks, authenticated publication/download, account switching, moderation, mixed
queue fairness, multiplayer, UI and rollback acceptance. Android builds/device
checks remain deferred. Existing earlier evidence is historical. Service deployment
and production release qualification are not claimed.
