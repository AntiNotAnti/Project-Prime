# Actual replay-window qualification

This is separate from the source-linked and injected-backend OpenGL checks. The
`rmlui-opengl-check` tool has an opt-in `--replay-window` mode that starts the
production Shell and uses its sole RenderWindow, DemoPlayback player, replica,
transport, Current backends and native documents. Run it only under the central
window/GL lease, using a frozen desktop smoke-enabled client and native bridge.

```
rmlui-opengl-check --replay-window OWNED_OR_SYNTHETIC.ppdemo SETUP_paths.txt NEW_OUTPUT_DIRECTORY [WIDTHxHEIGHT]
```

The archive must be a healthy canonical durable-world recording lasting at least
180 frames. The output directory must not exist. The mode copies the recording
and setup into isolated user data, resolves existing game extraction roots as
read-only inputs, and redirects exports/preferences/metadata to that new directory.
It does not copy or edit the supplied recording's camera/annotation sidecars.
Normal source and copy hashes are checked after teardown.

The null-default diagnostic observer only subscribes the fixture to the actual
window's RenderFrame event. All qualification actions run after the production
SwapBuffers and successful scene/timeline picture publication. The fixture never
calls a second render/player/clock or replaces a Current backend. Reflection is
limited to reading state and injecting the existing production controller mapping;
controller hardware is not claimed. Marker press, keyboard, accessibility and
pointer actions traverse native RmlUi and the existing page/scene lease.

The scripted journey creates three saved presented-camera poses via native Add,
holds 120 actual paused swaps, exercises keyboard/controller-mapping/accessibility
and queue-retarget guards, performs pointer group preview/cancel/confirm and binary
sidecar reload, seeks backward through a real replica/transport rebuild and explicit
same-owner refresh, changes actual client size, retires focus, and checks a bounded
same-length/same-stamp camera-sidecar change. Native Back releases the session;
relaunching the same isolated path must create a new player and never reauthorize
retired tools. The isolated sidecar is deliberately restored for that second launch.

`acceptance.json` records actual outcomes, pending stages on failure, process result,
owner/frame/seek/picture facts, callback cleanup, hashes and front-buffer captures.
A setup/runtime failure returns nonzero. Source tests or an injected native page
pass are not substituted for this receipt. Compact control scrolling and unavailable
window focus must fail honestly rather than claiming an unperformed journey.
