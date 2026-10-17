# PORT-26: complete-five local presentation acceptance

The existing PORT-25 runner supports incremental evaluation of any one or
more private visual-test packs. That behavior remains unchanged.

For a **complete local presentation audit**, use:

```bash
python3 tools/retro-character-preview/acceptance_matrix.py \
  --require-all \
  --pack /private/darksamus-visual-test-pack \
  --pack /private/trooper-power-visual-test-pack \
  --pack /private/trooper-ice-visual-test-pack \
  --pack /private/trooper-wave-visual-test-pack \
  --pack /private/trooper-plasma-visual-test-pack \
  --project-prime /path/to/ProjectPrime \
  --report /private/all-five-visual-check.json
```

`--require-all` rejects missing, duplicate, incompatible or tampered
source variants. Report fields `missingVariants`,
`allFiveVariantsProvidedAndValid`, and `strictCompleteSetRequested`
expose why a complete-set audit failed. Native renderer validation
must independently pass for every supplied pack.

No report from this slice marks `fullPortReady` or
`onlineMultiplayerEnabled` true. First-person/morph model,
animations, new Trooper mechanics, real hardware and multiplayer/replay
acceptance still require separate implementation and testing. No game
assets are added to the repository.
