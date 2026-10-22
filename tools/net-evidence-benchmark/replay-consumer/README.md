# Original-file passive scene consumer

This is the existing three-reader diagnostic made portable. `Program.cs` is byte-for-byte the historical `replay-scene-fce73957-final/source/Program.cs`; its printed `ENTRY_SCOPE` describes that code's origin, not the engine being qualified now. The wrapper always selects `--scene`. It builds a distinct thin friend entry named `nettest` against an explicitly supplied, previously qualified physical engine, without rebuilding the game or replacing the full fixture. Generated managed references are private; missing resolver/native payloads are linked rather than duplicating the asset bundle.

The committed clip acceptance tool tests seek and checkpoint restoration. This tool instead advances the actual original file through `PassiveReplayScene.Step`, including production world simulation and bomb draining. In the qualified source, `Step` returns false only when `Session.AtEnd` is true; otherwise it pumps, checks successful decode, and steps the replica. The unchanged helper also has a one-million-step safety limit. The wrapper rejects that limit, zero steps, excess requested frames, exceptions, unsuccessful decode, any final pending bombs, and any live `NetSession` or authority. It does not invent archived grant facts or require nonzero certificate counts for older recordings. Warnings and observed fact counts remain in the receipt.

`--max-frames` is an acceptance bound (default 120,000 successful scene steps). The unchanged helper's execution bound remains 1,000,000 steps; the per-process timeout bounds execution separately (default 1,800 seconds). A 1,200-second recording at 60 Hz fits the default acceptance bound with room for replay warmup. Reaching either bound fails, rather than proving EOF. Adjust the explicit bound for longer recordings without reaching the helper cap.

Run only when the shared build/application execution slot is released:

```sh
python3 tools/net-evidence-benchmark/replay-consumer/run.py \
  --repo /absolute/frozen-engine-source --head FULL_SOURCE_COMMIT \
  --runtime /absolute/qualified-full-runtime --sdk /absolute/dotnet \
  --qualification /absolute/source-native-qualification.json \
  --paths /absolute/sanitized-paths-template.txt \
  --replay /absolute/authority.ppdemo /absolute/client.ppdemo \
  --out /absolute/fresh-output --max-frames 120000 --timeout 1800
```

Supply 1–9 files. Formats 1–6 and PPDM/legacy FPDM headers are recorded without decoding or changing their bytes; the actual engine performs the compatibility adapter and semantic consumption. A run proves only the supplied files, not all formats from one current-format recording.

The passing qualification must bind exact clean frozen source before/after, actual full client and full fixture SHA/MVID, all six physical entry/config/deps files, and closed owned groups. The wrapper rehashes the full fixture and preserves its previously loaded MVID provenance; that fixture is not executed by the thin entry. Actual thin entry and loaded client SHA/MVID are read from each scene process. Thin/full identities must differ. Each run gets a fresh profile containing only sanitized `paths.txt` with absolute asset roots and a new export directory. No settings or credentials are copied. Asset files and entire replay files are streamed into before/after SHA manifests. Source, tool source, managed dependencies, physical runtime, Paths template, and thin output hashes must remain stable.

Fresh output directories preserve failures. `SOURCE_SETUP_FAIL` identifies qualification/build/loader/Paths setup problems. `SEMANTIC_EOF_FAIL` identifies actual reconstruction, step, EOF, frame-bound, decode, or drain failures. `PROVENANCE_FAIL` identifies changed inputs or unclosed owned groups. Logs contain exact commands, UTC, exit, PID/group identity and survivors; timeouts signal only the newly created, still-live owned session. Descendants surviving leader exit are recorded and fail qualification; they are not blindly signaled after possible PID reuse.

The historical bare demoinfo/session-only 2,048 pending-bomb failure remains a separate retained negative. This tool does not alter its decoder or relabel that scope. A PASS means original-file headless scene consumption only: no rendered pixels, GL/audio, live multiplayer authority, once-only payment campaign, timing, or complete-plan acceptance claim.

Source-only tests:

```sh
python3 -m unittest discover -s tools/net-evidence-benchmark/replay-consumer -p test_run.py -v
```

These checks cover provenance mismatches, current/legacy header observation, streaming tail mutation, scene versus bare decoder, cap exhaustion, bomb backlog, live authority, truncated results, duplicate/missing markers, and isolated Paths. They do not replace an actual C# build and semantic run.
