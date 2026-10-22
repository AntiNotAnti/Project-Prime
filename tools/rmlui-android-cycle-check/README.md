# Android local cycle ownership checks

Run from the repository root:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-android-cycle-check/rmlui-android-cycle-check.csproj -c Release
```

The project source-links the production `RmlUiAndroidLocalCycle` and
`RmlUiAndroidLocalRetirement` pipeline without a main-project reference. It tests
128 complete cycles, explicit Home/Loading/Match/return/ack phases, one-shot
admission, every historical match/return witness against later cycles, exact UI
ack identity, foreign lifetime rejection, owner-thread decisions, Stop before
and during retirement, and thrown/false failures at each retirement stage.
Failure aggregation proves later scene/visual/reset cleanup is attempted while
failed capture never invents a commit and failed cleanup/commit cannot release
foreground or authorize Home.

The production cleanup-context algorithm is also linked. Its fake EGL boundary
verifies already-current and surfaceless cleanup, the bounded pbuffer fallback,
foreign/wrong-context rejection, non-GL retirement after bind failure, and
guaranteed temporary unbind/destruction attempts after creation, bind, retirement
or release errors. This does not prove an Android config or driver supports them.

Scene, native, UI, held input, result pixels, View/context identities and
input/visual state are explicit fixture boundaries. Their fake reuse
and ordering tests exercise the real lifecycle/pipeline, not Android GameView,
Activity, actual Scene/GL/commit adapters, native loading, hardware or a second
runtime. Existing input/launcher/Pause fixtures separately exercise their real
source owners. Passing this suite does not establish actual EGL reuse,
foreground weak-reference eligibility, save I/O, device rendering/navigation,
resource/performance behavior or physical acceptance.
