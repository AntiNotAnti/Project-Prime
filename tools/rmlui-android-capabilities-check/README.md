# Configured local launcher capability checks

Run from the repository root:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-android-capabilities-check/rmlui-android-capabilities-check.csproj
```

This isolated fixture compiles the production Host sources, pointer mapping, and
Android launcher admission gate, local Hunter presenter, and portable Hunter
choice controller. It verifies the configured local allowlist,
desktop defaults, visibility of real authored DOM actions, publisher refreshes,
foreground document and sequence admission, arena modal lifetime/Back/focus,
and copied UI/EGL launch admission. Local Hunter checks cover selected bindings,
draft Apply/Discard/Back behavior, persistence failures and rollback, stale or
foreign documents, duplicate submission, concurrent profile changes, and the
actual authored local-picker actions. The original 454-check capability/admission
baseline remains part of the suite.

Preview barrier checks execute its production static gate: exact ownership,
duplicate and foreign acquisition, foreign release, exact release, and stale
old-owner release after replacement acquisition. The original 630-check
capability/admission/Hunter baseline remains part of the suite.

Owner-retirement admission tests retain the 639-check baseline and exercise the
production callback overload: absent plans skip callbacks, successful retirement
precedes copied-plan exposure, and failed or throwing retirement permanently
consumes admission without exposing the acknowledged plan or allowing retries.

The section/footer extension retains those original checks and adds six checks:
three actual unsupported Home section headings are hidden, the authored DOM
inventory contains all eighteen hidden IDs, configured local publishes its
actual no-preview footer text binding, and unrestricted desktop leaves the
authored footer unchanged. Intermediate baseline counts include those six
additional assertions.

The native bridge is an instrumented fake copied from the Android input fixture.
Hunter enum/rules and launcher preferences are explicit engine-boundary stubs;
the presenter and choice controller are actual production sources. No real
profile is read or written. Android, EGL/GLES, and main projects are
neither built nor exercised. Passing these checks does not establish device
rendering, touch usability, asset loading, or game launch acceptance.
