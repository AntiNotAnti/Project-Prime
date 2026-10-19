# Native text-edit ownership checks

The production text widget remains RmlUi's pinned `TextInputContext`. These
fixtures use synthetic text only and create temporary authored documents only in
an isolated assets clone. They do not touch user settings, saves, accounts or the
network. The test projects link current Host source without the main game graph.

`native/main.cpp` exercises the actual native owner through C ABI using the
DrawList backend. It checks the unchanged protocol1/intent40/state64 and optional
request80/state88 packets, UTF16/supplementary boundaries, exact revisions,
selection, preedit, maxlength unmark, real field/key/text/pointer mutation,
foreground/hidden/retired documents, owner-thread refusal and duplicate sequences.
No window or GL context is created.

`managed-cpu` loads the actual native bridge and actual Host/Cocoa adapter, with a
synthetic Cocoa callback-registration boundary. It checks actual bytes/marked
ranges, adapter detach/reattach on the *same focused native owner*, strict native
replay rejection, FIRST stale Unmark before refresh, canceled late insertion and
owner-thread refusal. It creates no AppKit/GLFW window.

`appkit` creates a real macOS GLFW NoAPI window and sends programmatic
NSTextInputClient Objective-C messages through the real per-view subclass. It
checks attributed text and replacement ranges, selection/candidate queries,
payload-free Unmark, supplementary maxlength truncation, FIRST stale Unmark,
late-insert fences, ordinary repeated GLFW text, protected cancellation and class
restoration. **This mode requires the exclusive desktop window lease.** It does
not qualify physical keyboard/CJK IME input, the candidate server/panel or GL.

`tools/rmlui-runtime-check/RangeImeAcceptance.cs` supplements this with faultable
optional packet and older-bridge compatibility checks. Its boundary is synthetic.

## Run without a main/native rebuild

Pass the existing coordinated native bridge, packaged RmlUi assets and exact
pinned RmlUi checkout. Preparation only copies inputs and prints commands:

```sh
python3 tools/rmlui-text-edit-check/prepare.py \
  --native /absolute/libProjectPrime.RmlUi.Native.dylib \
  --assets /absolute/packaged/rmlui \
  --rmlui-source /absolute/pinned/RmlUi-6.3 \
  --opentk-dir /absolute/existing/opengl-tool/bin/Release/net10.0
```

Run its printed native configure/build/check and managed CPU commands in order.
Set the printed `PRIME_RMLUI_TEXT_EDIT_NATIVE` value for managed commands so the
standalone fixture resolves only its private copied bridge. The C++ fixture
links that same existing library and does not build or download RmlUi, FreeType,
the bridge or the game. The optional AppKit project references the existing
OpenTK4.9.4 assemblies; no new packages are installed. Build it and copy the
existing GLFW library with the printed commands, then acquire the window lease
before running the explicitly labeled AppKit command.

Prepared asset/runtime input hashes and commands remain in the temporary folder.
The managed projects link current Host source, so rerun preparation after source
changes and retain matching input/output manifests when recording results.

## Qualified evidence and limits

The private frozen RML62 input-owner candidate passed actual native65, managed
range29, actual native/Host/Cocoa lifecycle20, and actual macOS programmatic
AppKit49 checks. Its predecessor failed on second adapter attachment with the
same focused native owner: adapter sequence1 was correctly rejected Duplicate
by the native owner after earlier operations1/2. Host lifetime-monotonic sequence
allocation fixed that defect without changing native replay rejection. FIRST
stale Unmark also retains cancellation authority before a geometry refresh.

The frozen evidence is in `docs/evidence/rmlui-text-edit-rml65`; it records the
private Replay124/Studio190 candidate. Production promotion applied narrow hunks
to Replay135/Studio200 plus the newer Timeline queued-action guard. The promoted
source requires the central production build/qualification; the earlier private
passes alone do not establish acceptance of unrelated production changes.
