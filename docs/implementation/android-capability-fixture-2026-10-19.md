# Android capability fixture and authored hidden actions

The shared Studio chooser adds `nav_studio_home` with an inline `display:none`
style and the `studio:open` action. Android correctly denies that action. The
capability fixture previously recognized only visibility overrides applied by the
platform policy, so it incorrectly reported the permanently hidden button as a
visible unsupported action.

The DOM audit now also recognizes an ancestor's authored `display:none`, matching
case-insensitive property/value spelling and surrounding whitespace. The last
inline display declaration wins. It does not infer hidden state from unrelated
properties or from `visibility:hidden`. Existing action parsing, capability
admission and real visibility-override DOM coverage assertions remain intact.
Focused checks cover hidden ancestors, normal visible controls, overriding
`display:block`, and refusal of an unsupported visible Studio action.

This changes the portable fixture only. It does not enable Studio on Android or
claim Android device, APK, GLES, touch or accessibility acceptance. PR-08 must also
keep Host action bounds independent of Core editor classes so source-only Android
harnesses retain their existing compilation boundary.

The completed DOM audit also found visible desktop-only actions newly added to
shared markup: `nav_hunters` (`route:hunter`), `nav_theatre` (`route:theatre`),
`build_version` (`settings:action:250`), and `home_party_slot2` /
`home_party_slot3` (`route:social`). Configured-local Android has no backend for
these workspace/settings/social routes. Its existing handler intercepts
`HunterOpen` for the local inline picker, while its navigation service supports
Home only. The local capability projection now hides those five actual IDs;
unrestricted desktop projection is unchanged. A positive assertion preserves
inline `HunterOpen` admission. The strict document-surface harness still rejects
nonexistent visibility targets and checks exact per-surface selector counts.

Verification: `dotnet run --project tools/rmlui-android-capabilities-check -c
Release -p:BuildProjectReferences=false` passes 728 checks. These use actual
source contracts and authored XML with fake native/engine boundaries. The current
markup contributes 14 more action parse/admission assertions, and this repair adds
ten focused visibility/inline-picker checks. No original acceptance assertion
was removed. The final log is
`artifacts/ci-presence-publish/android-capabilities-final-02.txt` in the UI worktree.
