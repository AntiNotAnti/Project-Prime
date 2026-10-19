# Reservation registry compatibility — 2026-10-19

PR 733's `no game assets` job reached the reservation harness, then failed its pre-build `check_registry.py` assertion for protocol57. Production intentionally uses protocol59. Local execution reproduced that exact assertion; the preceding packet-registry checks passed.

The registry fixture now pins the current protocol59 and its success message/README reflect that expectation. Every frozen packet ID, the exact permitted additions 80–82, alias uniqueness and optional server-status flag2 checks remain unchanged. No production schema, protocol, admission or runtime code changed.

Validation: the source-linked group reservation harness passed 1,267 codec/FIFO/worker/queue/ticket checks. The actual server-control-owner harness passed 75 checks. The group harness retains an existing unused facade-field compiler warning. A search found no other stale protocol57 fixture pins; the remaining literal protocol57 test deliberately verifies old-admission refusal. Full hosted CI must rerun after publication.
