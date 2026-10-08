# Adventure controller contracts

Run `dotnet run --project tools/adventure-controller-check -c Release`.

The content-free harness checks immutable save summaries, stale choices, owner-thread
dispatch, exactly-once engine handoff, overwrite cancellation, failed file validation,
failed preference persistence, load-failure feedback, and retirement. Its injected
boundary never reads or writes user saves. Native DOM/input and a real Adventure
scene load remain separate integration gates.

For real headless native layout, action, disabled focus, overwrite modal, safe text,
and draw-list checks, run the same command with `-p:MphReadRmlUi=true -- --native
<native-library> <packaged-rmlui-root>`. It exercises four viewport/density cases;
it does not load game assets or claim physical input, GPU presentation, or campaign parity.
