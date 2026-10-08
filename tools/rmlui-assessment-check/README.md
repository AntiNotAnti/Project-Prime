# Review slice proposal

Run from any directory:

```sh
python3 tools/rmlui-assessment-check/propose-slices.py
```

The tool reads current diff versus HEAD (staged and unstaged) names and nonignored untracked paths. It
writes `docs/architecture/rmlui-review-slice-proposal.json`; it never stages,
commits, resets, rebases or changes Git. Already committed foundation/Social work
is outside this current-diff-only proposal and remains in its existing stack.

Each file has a proposed RML slice, rationale and shared/late dependencies.
Parent-owned all-route `Shell.RmlUi*`/`Shell.cs` integration is separately excluded
and assigned to RML-18. Shared native/managed ABI files may contain all future
bounded action IDs in RML-01, but optional exports need matching headers. Shared
project/CI/prototype/engine files need chronological hunk review: their final
contents often combine foundation, route integration and RML-19/20 shipping
policy/removal. A file-name proposal does not prove that an independently
compiling or functional slice commit has been created.

Regenerate after source changes and inspect `MANUAL-REVIEW` entries before using
it for the one-PR-per-slice stack. The workflow assessment and immutable source
baseline remain separate from this proposed review partition.

`refresh-workflows.py` regenerates the current 61-workflow/27-slice assessment
from the frozen baseline and curated source/evidence mapping. It refreshes source
hashes and the observed HEAD; it does not rerun tests or accept release gates.
Update named evidence counts only after inspecting new actual results. The user
owns external physical/live testing, and automated cutover evidence remains
explicitly separate from those unperformed coverage limits.

The final user delivery instruction is one aggregate PR with further checks
skipped. The slice proposal is now an ownership audit, not a request to create
separate PRs. The current workflow JSON records the reviewed frozen aggregate
plus separately hashed applied development candidate; its final validation
state must not be overwritten by regeneration that lacks this user decision.
Use `--frozen-aggregate <full-sha>` to read exact Git-object source hashes and
`--candidate-manifest <manifest> --candidate-applied` to record the unmerged
working candidate without accepting its release gates.
