# Guarded actual Mac candidate capture

`golden-capture.py` runs an existing full native client serially at sixteen Home
viewport/density cases and four News/Settings representative cases. It verifies
the exact requested assembly SHA and diagnostic authentication guards, freezes
the asset tree digest, creates fresh per-case user data containing only the
extraction `paths.txt`, and checks the real PNG/framebuffer/Metal evidence.
It never builds or copies account/preferences files.

```sh
python3 tools/rmlui/golden-capture.py \
  --native <full-native-client>/ProjectPrime.dll \
  --expected-sha256 <verified-assembly-sha256> \
  --paths <extraction-fixture>/paths.txt \
  --out <new-artifact-directory> --dotnet <dotnet10>
```

Use `--list` for a no-window preflight or `--case <case-name>` for one capture.
The exact client must first pass `tools/rmlui-account-diagnostic-check`. Captures
must hold the shared desktop lease so they do not affect other measurements.
The script enables existing central performance diagnostic guards for account,
Social startup and updater requests; it does not infer a timing result.

The local M4 Pro candidate matrix produced 20 actual saved PNGs. Its two 4K
requests were clamped by macOS to 3024×1646; those cases are not actual 4K evidence.
CLI density 1/2 is forced native density, not physical 1x/2x monitor coverage.
The separately hashed two-image Settings repair retains the original defect
and verifies containment after the authored scroll repair. See
`docs/architecture/rmlui-evidence/mac-golden-candidate-matrix.json` and
`mac-golden-settings-repair.json` for dimensions, hashes and inspection notes.
These observations do not certify legacy golden parity or production timing.
