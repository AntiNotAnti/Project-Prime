# Android Adventure admission checks

This source-links the production Android immutable local request and original
launcher admission owner, using the compiled engine's actual launch/save/ship
DTOs. It does not instantiate GameView, AndroidMatch, a Scene, native RmlUi/EGL,
audio, assets, StorySave serialization, network or real user-data files.

After the centrally coordinated opt-in client build:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-android-adventure-check -c Release -p:BuildProjectReferences=false --no-restore
```

An immutable compatible client assembly can instead be supplied with
`-p:MphReadTestAssembly=/absolute/path/to/ProjectPrime.dll`. This avoids reading
the mutable main output while its central server/client build is running. The
initial 49 checks passed using the INITIAL58 immutable client DTO assembly.

The checks cover exact source metadata/fingerprint/empty-slot witnesses,
malformed or unreadable slots, foreign plan/hunter/destination/surface-exit
rejection, dock changes during readback, distinct Offline/Adventure/Ship
families, one-shot admission, atomic plan/request extraction and safe discard
when the original native owner fails to retire. The existing 35,350-cycle and
capability source-only suite (661 baseline, 682 with Adventure) remain separate evidence. These results do
not establish real Android scene construction, save commits, rendering, EGL
reuse, navigation, audio, hardware, performance or device acceptance.
